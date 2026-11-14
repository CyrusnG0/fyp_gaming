using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// The reusable headless loop. It uses the production prompt builder, action catalogue, strict
    /// parsers, investigation service, memory store and deliberation ledger, while owning the
    /// season/seat lifecycle itself. The production <see cref="LlmTurnRunner"/> only wires
    /// manual_lookup; the playground adds the other three read-only tools and the strategic note so
    /// those interfaces can be exercised before integration.
    /// </summary>
    internal sealed partial class PlaygroundSession
    {
        private const int MaxHistory = 12;

        private readonly PlaygroundConfig _config;
        private readonly GameEngine _engine;
        private readonly ILlmTransport _transport;
        private readonly FakeLlmTransport _fake;
        private readonly PlaygroundClock _clock;
        private readonly ILlmMemoryStore _memoryStore;
        private readonly IPlaygroundNoteStore _noteStore;
        private readonly DeliberationBudgetLedger _ledger;
        private readonly PlaygroundJournal _journal = new PlaygroundJournal();
        private readonly PlaygroundRunRecord _record = new PlaygroundRunRecord();
        private readonly Dictionary<string, List<string>> _histories =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly List<LlmMemory> _memories = new List<LlmMemory>();
        private readonly List<string> _retainedInquiryResults = new List<string>();
        private string _pendingInquiryResult;
        private int _requestIndex;

        public PlaygroundSession(PlaygroundConfig config, GameEngine engine = null,
            ILlmTransport transport = null, PlaygroundClock clock = null,
            ILlmMemoryStore memoryStore = null, IPlaygroundNoteStore noteStore = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();
            _engine = engine ?? PlaygroundScenario.Create(config);
            _transport = transport ?? config.CreateTransport();
            _fake = _transport as FakeLlmTransport;
            _clock = clock ?? new PlaygroundClock();
            _memoryStore = memoryStore ?? new InMemoryLlmMemoryStore();
            _noteStore = noteStore ?? new InMemoryPlaygroundNoteStore();
            _ledger = new DeliberationBudgetLedger(config.GameId, BuildPolicy(config), _clock);
        }

        public GameEngine Engine => _engine;
        public DeliberationBudgetLedger Ledger => _ledger;
        public PlaygroundRunRecord Record => _record;

        public PlaygroundRunRecord Run()
        {
            _record.Kind = "fake-session";
            _record.RunId = _config.RunId;
            _record.Provider = _transport.Provider;
            _record.Fake = _config.IsFakeProvider;
            _record.Seed = _config.Seed;
            _record.GameId = _config.GameId;
            _record.ScenarioId = PlaygroundConfig.ScenarioId;
            _record.PromptBaseVersion = PromptBuilder.VersionFor(true, false, (LlmPersona)null,
                _config.EnableAttack);
            _record.PromptAddendumVersion = PlaygroundConfig.PromptAddendumVersion;
            _record.ConfigJson = _config.ToJson();
            _record.StartStateHash = PlaygroundStateHash.Authoritative(_engine);

            if (_config.ResetStores)
            {
                _noteStore.Reset();
                if (_memoryStore is InMemoryLlmMemoryStore inMemory) inMemory.Reset();
            }
            SeedFixtureMemories();

            IReadOnlyList<string> seats = PlaygroundScenario.SeatOrder(_engine);
            _engine.BeginSeason();
            _journal.BeginSeason(_engine.State.Season);
            if (_fake != null && _config.UseBuiltInFakePlan)
                PlaygroundFakePlan.Enqueue(_fake, _engine, _config);

            for (int index = 0; index < _config.Seasons; index++)
            {
                if (_engine.State.WinnerSeatId != null) break;
                foreach (string seat in seats)
                {
                    if (_engine.State.WinnerSeatId != null) break;
                    RunSeatTurn(seat);
                }
                if (_engine.State.WinnerSeatId != null) break;

                _engine.EndSeason();
                _journal.EndSeason(_engine.State.Season);
                if (index + 1 < _config.Seasons)
                {
                    _engine.BeginSeason();
                    _journal.BeginSeason(_engine.State.Season);
                }
            }

            _record.EndStateHash = PlaygroundStateHash.Authoritative(_engine);
            _record.BudgetJson = _ledger.Snapshot().ToJson();
            AddAutomaticAssertions();
            return _record;
        }

        private void RunSeatTurn(string seatId)
        {
            int token = _engine.BeginSeatTurn(seatId);
            var turn = new PlaygroundTurnRecord
            {
                SeatId = seatId,
                Season = _engine.State.Season,
                TurnToken = token,
                StateHashBefore = PlaygroundStateHash.Authoritative(_engine),
                RequestStartIndex = _requestIndex + 1,
            };
            FactionState faction = _engine.State.FindFaction(seatId);
            turn.CpBefore = faction == null ? 0 : faction.CommandPoints;

            DeliberationBindingResult binding = _ledger.BindTurn(seatId, token);
            if (!binding.Ok)
            {
                turn.EndReason = "budget_binding_denied:" + binding.Code;
                turn.StateHashAfter = PlaygroundStateHash.Authoritative(_engine);
                turn.CpAfter = turn.CpBefore;
                _record.Turns.Add(turn);
                _engine.EndSeatTurn(token);
                return;
            }

            LoadSeatMemories(seatId, _engine.State.Season);
            string noteShown = _noteStore.Load(_config.GameId, seatId);
            turn.NoteShown = noteShown;
            if (!string.IsNullOrEmpty(noteShown))
                _record.Notes.Add(new PlaygroundNoteRecordView
                {
                    RequestIndex = _requestIndex,
                    SeatId = seatId,
                    Season = _engine.State.Season,
                    Event = "loaded",
                    Text = noteShown,
                    Chars = noteShown.Length,
                });

            _pendingInquiryResult = null;
            _retainedInquiryResults.Clear();
            int requests = 0, actions = 0, inquiries = 0, invalid = 0;
            string noteSaved = null;
            string endReason = "limit";

            while (true)
            {
                if (_engine.State.WinnerSeatId != null) { endReason = "game_over"; break; }
                if (_ledger.Blocked)
                {
                    DeliberationBudgetSnapshot snapshot = _ledger.Snapshot();
                    endReason = "budget_blocked:" + (snapshot.StopCode ?? "unknown");
                    break;
                }
                if (requests >= _config.MaxRequestsPerTurn) { endReason = "request_limit"; break; }
                if (actions >= _config.MaxActionsPerTurn) { endReason = "action_limit"; break; }
                if (invalid >= _config.MaxInvalidPerTurn) { endReason = "invalid_limit"; break; }

                Observation observation = _engine.Observe(seatId);
                if (observation == null || !string.Equals(observation.SeatId, seatId, StringComparison.Ordinal))
                {
                    endReason = "host_error";
                    break;
                }

                MovementView movement = MovementPlanner.Build(_engine, seatId);
                AttackView attacks = _config.EnableAttack ? AttackPlanner.Build(_engine, seatId) : null;
                List<ActionOption> options = ActionCatalogue.Build(observation, attacks);
                IReadOnlyList<LlmMemory> relevant = LlmMemoryRetriever.Select(_memories, observation, options);
                int requestsLeft = Math.Max(0, _config.MaxRequestsPerTurn - requests);
                int actionsLeft = Math.Max(0, _config.MaxActionsPerTurn - actions);
                int inquiriesLeft = Math.Max(0, _config.MaxInquiryPerTurn - inquiries);

                string latest = TakePendingInquiryResult();
                var earlier = new List<string>(_retainedInquiryResults);
                if (latest != null && earlier.Count > 0
                    && string.Equals(earlier[earlier.Count - 1], latest, StringComparison.Ordinal))
                    earlier.RemoveAt(earlier.Count - 1);

                PlaygroundContext context = PlaygroundPrompt.Build(_config, observation,
                    movement, attacks, options, relevant, earlier, latest, noteShown,
                    GetHistory(seatId), requestsLeft, actionsLeft, inquiriesLeft);

                var request = new PlaygroundRequestRecord
                {
                    Index = ++_requestIndex,
                    SeatId = seatId,
                    Season = observation.Season,
                    TurnToken = token,
                    PromptVersion = context.PromptVersion,
                    PromptBaseVersion = context.BaseVersion,
                    PromptAddendumVersion = context.AddendumVersion,
                    SchemaHash = context.SchemaHash,
                    Seed = _config.Seed,
                    System = context.System,
                    User = context.User,
                    ObservationJson = context.ObservationJson,
                    OfferedActions = context.OfferedActions,
                    OptionCount = context.OptionCount,
                    MemoryLines = context.MemoryLines,
                    PriorInquiryResults = context.PriorInquiryResults,
                    NoteShown = context.NoteShown,
                    RemainingJson = PlaygroundJson.Obj(
                        "requests", PlaygroundJson.Num(requestsLeft),
                        "actions", PlaygroundJson.Num(actionsLeft),
                        "inquiries", PlaygroundJson.Num(inquiriesLeft)),
                    ConfigJson = _config.ToJson(),
                };

                if (context.TooLarge)
                {
                    request.Admission = "not_attempted";
                    request.ResponseKind = "not_sent";
                    request.Outcome = "prompt_too_large";
                    request.Detail = "prompt is " + context.TotalChars + " chars; bound is "
                        + PromptBuilder.MaxPromptChars;
                    _record.Requests.Add(request);
                    endReason = "prompt_too_large";
                    break;
                }

                DeliberationAdmission admission = _ledger.TryAdmitRequest(
                    DeliberationTokenBounds.Of(_config.InputTokenBound, _config.MaxOutputTokens));
                if (!admission.Allowed)
                {
                    request.Admission = "denied:" + admission.Code;
                    request.ResponseKind = "not_sent";
                    request.Outcome = "request_denied:" + admission.Code;
                    request.Detail = admission.Message;
                    _record.Requests.Add(request);
                    endReason = "request_budget:" + admission.Code;
                    break;
                }

                request.Admission = "allowed";
                LlmResponse response = Send(context, out long elapsedMs);
                requests++;
                request.ElapsedMs = elapsedMs;
                SettleRequest(admission.ReservationId, response, elapsedMs);
                FillResponse(request, response, _config.MaxTraceResponseChars);
                _record.Requests.Add(request);

                TurnStep step = HandleResponse(request, response, seatId, observation, token,
                    ref actions, ref inquiries, ref invalid, ref noteSaved);
                if (step.Stop) { endReason = step.EndReason; break; }
            }

            turn.NoteSaved = noteSaved;
            turn.EndReason = endReason;
            turn.RequestCount = _requestIndex + 1 - turn.RequestStartIndex;
            turn.InquiryCount = inquiries;
            turn.ActionCount = actions;
            FactionState after = _engine.State.FindFaction(seatId);
            turn.CpAfter = after == null ? turn.CpBefore : after.CommandPoints;
            _engine.EndSeatTurn(token);
            turn.StateHashAfter = PlaygroundStateHash.Authoritative(_engine);
            _record.Turns.Add(turn);
        }

        private LlmResponse Send(PlaygroundContext context, out long elapsedMs)
        {
            var request = new LlmRequest(context.System, context.User,
                _config.MaxOutputTokens, _config.RequestTimeoutMs);
            long started = _clock.NowMillis;
            LlmResponse response;
            using (var cts = new CancellationTokenSource(_config.RequestTimeoutMs))
            {
                try
                {
                    var task = _transport.SendAsync(request, cts.Token);
                    response = task == null
                        ? LlmResponse.Failure(LlmErrorKind.Network, "transport returned no task")
                        : task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    response = LlmResponse.Failure(LlmErrorKind.Timeout, "request timed out");
                }
                catch (Exception)
                {
                    response = LlmResponse.Failure(LlmErrorKind.Network, "transport failure");
                }
            }
            _clock.Advance(_config.SimulatedRequestMs);
            elapsedMs = _clock.NowMillis - started;
            return response ?? LlmResponse.Failure(LlmErrorKind.Network, "transport returned no response");
        }

        private void SettleRequest(string reservationId, LlmResponse response, long elapsedMs)
        {
            var settlement = new DeliberationSettlement
            {
                InputTokens = response.Ok ? response.InputTokens : null,
                OutputTokens = response.Ok ? response.OutputTokens : null,
                ElapsedMillis = elapsedMs,
                Failed = !response.Ok,
            };
            _ledger.Settle(reservationId, settlement);
        }

        private static void FillResponse(PlaygroundRequestRecord request, LlmResponse response, int maxChars)
        {
            request.ResponseKind = response.Ok ? "success" : "error:" + response.Error.ToString().ToLowerInvariant();
            request.ResponseText = Bound(response.Text, maxChars);
            request.InputTokens = response.InputTokens;
            request.OutputTokens = response.OutputTokens;
            request.LatencyMs = response.LatencyMs;
        }

        private string TakePendingInquiryResult()
        {
            string value = _pendingInquiryResult;
            _pendingInquiryResult = null;
            return value;
        }

        private List<string> GetHistory(string seatId)
        {
            if (!_histories.TryGetValue(seatId, out List<string> history))
            {
                history = new List<string>();
                _histories[seatId] = history;
            }
            return history;
        }

        private void AddHistory(string seatId, string line)
        {
            List<string> history = GetHistory(seatId);
            history.Add(line);
            while (history.Count > MaxHistory) history.RemoveAt(0);
        }

    }

    internal sealed class TurnStep
    {
        public bool Stop;
        public string EndReason;
        public static TurnStep Continue() => new TurnStep();
        public static TurnStep StopWith(string reason) => new TurnStep { Stop = true, EndReason = reason };
    }
}
