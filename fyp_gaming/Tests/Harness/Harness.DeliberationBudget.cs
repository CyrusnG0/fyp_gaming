using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [LS-019] Configurable deliberation budget: policy contract, request/inquiry/action separation,
// session-wide accounting across seats and turns, and the demo/research policy factories.
//
// These tests are pure state transitions: a fake monotonic clock, no provider, no network, no Unity
// editor, no sleeps. See Harness.DeliberationBudget.Tokens.cs for reservation/settlement behaviour and
// Harness.DeliberationBudget.Snapshots.cs for wall time, validation and snapshot detachment.

internal static partial class Harness
{
    private static void RunDeliberationBudget()
    {
        Section("[new] LS-019 configurable deliberation budget");

        DeliberationReasonCodes();
        DeliberationPolicyContract();
        DeliberationAdmissionSeparation();
        DeliberationSessionWidth();
        DeliberationPolicyFactories();
        DeliberationInquirySpendsNoGameResources();
        DeliberationTokenReservations();
        DeliberationSettlementLifecycle();
        DeliberationDispatchAndCancel();
        DeliberationDispatchGate();
        DeliberationDispatchExpiry();
        DeliberationAppliedToTurnIdentity();
        DeliberationTurnBucketsAndUnboundedTokens();
        DeliberationLateCompletion();
        DeliberationUnknownUsage();
        DeliberationOverrun();
        DeliberationWallTime();
        DeliberationValidation();
        DeliberationSnapshotDetachment();
        DeliberationAccountingOutcomes();
    }

    // ---- fixtures ----------------------------------------------------------

    private sealed class FakeDeliberationClock : IDeliberationClock
    {
        public long Now;
        public bool ThrowOnRead;

        public long NowMillis
        {
            get
            {
                if (ThrowOnRead) throw new InvalidOperationException("clock unavailable");
                return Now;
            }
        }

        public void Advance(long millis) => Now += millis;
    }

    private static DeliberationBudgetLedger DeliberationLedger(DeliberationBudgetLimits limits,
        DeliberationBudgetMode mode = DeliberationBudgetMode.RequestLimited,
        FakeDeliberationClock clock = null, string matchId = "match-ls019")
    {
        var policy = new DeliberationBudgetPolicy("test-profile", "1", mode, limits);
        return new DeliberationBudgetLedger(matchId, policy, clock ?? new FakeDeliberationClock());
    }

    private static DeliberationSeatTotals DeliberationSeatTotalsFor(DeliberationBudgetSnapshot snapshot,
        string seatId)
    {
        foreach (DeliberationSeatTotals seat in snapshot.Seats)
            if (string.Equals(seat.SeatId, seatId, StringComparison.Ordinal)) return seat;
        return null;
    }

    private static bool DeliberationParses(string json) => StrictJson.TryParse(json, out _, out _);

    // ---- vocabulary --------------------------------------------------------

    private static void DeliberationReasonCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        bool allCodes = true;
        bool unique = true;
        foreach (DeliberationDenyReason reason in Enum.GetValues(typeof(DeliberationDenyReason)))
        {
            string code = DeliberationBudgetReasons.Code(reason);
            allCodes = allCodes && !string.IsNullOrEmpty(code);
            unique = unique && codes.Add(code);
            if (DeliberationBudgetReasons.Message(reason) == null) allCodes = false;
        }
        Check(allCodes, "[LS-019] every deny reason has a stable code and message");
        Check(unique, "[LS-019] deny reason codes are unique");

        Check(DeliberationBudgetReasons.IsExhaustion(DeliberationDenyReason.TurnRequestsExhausted)
              && DeliberationBudgetReasons.IsExhaustion(DeliberationDenyReason.SessionInquiryExhausted)
              && !DeliberationBudgetReasons.IsExhaustion(DeliberationDenyReason.InvalidTokenBounds)
              && !DeliberationBudgetReasons.IsExhaustion(DeliberationDenyReason.AlreadySettled),
            "[LS-019] exhaustion reasons are distinguishable from malformed calls");
        Check(DeliberationBudgetReasons.Dimension(DeliberationDenyReason.TurnOutputTokensExhausted)
              == DeliberationBudgetDimension.OutputTokens
              && DeliberationBudgetReasons.Dimension(DeliberationDenyReason.SessionActionsExhausted)
              == DeliberationBudgetDimension.Actions,
            "[LS-019] reasons carry a reporting dimension");
    }

    // ---- immutable validated policy ---------------------------------------

    private static void DeliberationPolicyContract()
    {
        var supplied = new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 6,
            MaxActionsPerTurn = 5,
            MaxWallMillisPerTurn = 60000,
        };
        var policy = new DeliberationBudgetPolicy("probe", "2", DeliberationBudgetMode.RequestLimited, supplied);
        supplied.MaxRequestsPerTurn = 99;
        DeliberationBudgetLimits copy = policy.Limits;
        copy.MaxRequestsPerTurn = 1;

        Check(policy.Limits.MaxRequestsPerTurn == 6,
            "[LS-019] a policy copies its limits and protects them from later mutation");
        Check(policy.ProfileId == "probe" && policy.ProfileVersion == "2"
              && policy.Mode == DeliberationBudgetMode.RequestLimited,
            "[LS-019] a policy records the experiment profile identity");
        Check(!policy.TokenBudgetEnforced, "[LS-019] request-limited mode reports token budgets as unenforced");
        Check(policy.Limits.MaxInquiryPerTurn == DeliberationBudgetLimits.Unenforced,
            "[LS-019] an unset dimension is explicitly unenforced, not zero");
        Check(policy.EnforcedDimensions().Contains("requests.turn")
              && policy.EnforcedDimensions().Contains("wall_time.turn")
              && !policy.EnforcedDimensions().Contains("requests.session")
              && policy.UnenforcedDimensions().Contains("input_tokens.turn"),
            "[LS-019] enforced and unenforced dimensions are reported explicitly");

        string json = policy.ToJson();
        Check(DeliberationParses(json) && json.Contains("\"token_budget_enforced\":false")
              && !json.Contains("\"api_key\""),
            "[LS-019] a policy record is valid JSON and carries no credentials");
        Check(json.Contains("\"requests_session\":null"),
            "[LS-019] an unenforced limit is recorded as null, not as a sentinel number");
    }

    // ---- request / inquiry / action separation ----------------------------

    private static void DeliberationAdmissionSeparation()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 2,
            MaxInquiryPerTurn = 0,
            MaxActionsPerTurn = 3,
        }, clock: clock);

        DeliberationAdmission unbound = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 50));
        Check(!unbound.Allowed && unbound.Reason == DeliberationDenyReason.NotBound
              && unbound.ReservationId == null,
            "[LS-019] admission before the host binds a turn is denied without a reservation");

        Check(ledger.BindTurn("seat-a", 1).Ok, "[LS-019] the host binds a seat and turn token");

        DeliberationAdmission first = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 50));
        DeliberationAdmission second = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 50));
        DeliberationAdmission third = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 50));
        Check(first.Allowed && second.Allowed && first.ReservationId != second.ReservationId,
            "[LS-019] each admitted request receives its own reservation identity");
        Check(first.SeatId == "seat-a" && first.TurnToken == 1 && first.MatchId == "match-ls019",
            "[LS-019] an admission carries the host-bound identity it was issued under");
        Check(!first.Exhausted && second.Exhausted,
            "[LS-019] the admission that uses the last slot reports an explicit exhaustion signal");
        Check(!third.Allowed && third.Reason == DeliberationDenyReason.TurnRequestsExhausted
              && third.Code == "turn_requests_exhausted",
            "[LS-019] the per-turn request allowance stops further requests");

        DeliberationAdmission inquiry = ledger.TryAdmitInquiry();
        Check(!inquiry.Allowed && inquiry.Reason == DeliberationDenyReason.TurnInquiryExhausted,
            "[LS-019] a zero inquiry allowance denies inquiry instead of borrowing request slots");
        DeliberationAdmission inquiryWithTokens = ledger.TryAdmit(DeliberationWorkKind.Inquiry,
            DeliberationTokenBounds.Of(10, 10));
        Check(!inquiryWithTokens.Allowed && inquiryWithTokens.Reason == DeliberationDenyReason.InvalidTokenBounds,
            "[LS-019] only a model request may reserve tokens");

        DeliberationAdmission action1 = ledger.TryAdmitGameAction();
        DeliberationAdmission action2 = ledger.TryAdmitGameAction();
        DeliberationAdmission action3 = ledger.TryAdmitGameAction();
        DeliberationAdmission action4 = ledger.TryAdmitGameAction();
        Check(action1.Allowed && action2.Allowed && action3.Allowed && !action4.Allowed
              && action4.Reason == DeliberationDenyReason.TurnActionsExhausted,
            "[LS-019] game actions have their own per-turn allowance");

        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(snapshot.Turn.Requests == 2 && snapshot.Turn.Inquiries == 0
              && snapshot.Turn.ActionAdmissions == 3,
            "[LS-019] requests, inquiries and actions are counted separately");
        Check(snapshot.Turn.ReservedOutputTokens == 100 && snapshot.Turn.Requests == 2,
            "[LS-019] only the admitted requests hold token reservations");
        Check(!third.Allowed && ledger.Snapshot().Turn.Requests == 2
              && ledger.Snapshot().Turn.ActionAdmissions == 3,
            "[LS-019] denied admissions leave every counter unchanged");

        DeliberationAdmission boundInquiry = ledger.TryAdmit(DeliberationWorkKind.Inquiry,
            DeliberationTokenBounds.None);
        Check(!boundInquiry.Allowed && boundInquiry.Reason == DeliberationDenyReason.TurnInquiryExhausted,
            "[LS-019] a zero allowance is a real cap, not an unset value");
    }

    // ---- session-wide caps across players and turns -----------------------

    private static void DeliberationSessionWidth()
    {
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 10,
            MaxRequestsPerSession = 3,
        });

        ledger.BindTurn("seat-a", 1);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] seat-a uses the first session request");
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] seat-a uses the second session request");

        ledger.BindTurn("seat-b", 1);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] seat-b may use the remaining session request");
        DeliberationAdmission denied = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!denied.Allowed && denied.Reason == DeliberationDenyReason.SessionRequestsExhausted,
            "[LS-019] a second player does not receive a fresh global session cap");

        ledger.BindTurn("seat-a", 2);
        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(!ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] a new turn of an earlier seat also inherits the spent session budget");
        Check(snapshot.Session.Requests == 3 && snapshot.Turn.Requests == 0,
            "[LS-019] per-turn counters reset at a new turn while session totals survive");
        Check(snapshot.Turn.WallClockMillis == 0,
            "[LS-019] a fresh turn starts with a zero wall-clock observation");

        DeliberationSeatTotals seatA = DeliberationSeatTotalsFor(snapshot, "seat-a");
        DeliberationSeatTotals seatB = DeliberationSeatTotalsFor(snapshot, "seat-b");
        Check(seatA != null && seatA.Requests == 2 && seatB != null && seatB.Requests == 1,
            "[LS-019] per-seat totals are recorded as evidence across that seat's turns");
    }

    // ---- demo / research factories ---------------------------------------

    private static void DeliberationPolicyFactories()
    {
        var live = new LlmConfig();
        DeliberationBudgetPolicy demo = DeliberationBudgetPolicies.DemoDefaults();
        Check(demo.ProfileId == DeliberationBudgetPolicies.DemoProfileId
              && demo.Mode == DeliberationBudgetMode.RequestLimited
              && !demo.TokenBudgetEnforced,
            "[LS-019] the demo policy is request-limited and names its profile");
        Check(demo.Limits.MaxRequestsPerTurn == 6 && live.MaxRequestsPerTurn == 6
              && demo.Limits.MaxRequestsPerTurn == live.MaxRequestsPerTurn
              && demo.Limits.MaxActionsPerTurn == live.MaxActionsPerTurn
              && demo.Limits.MaxRequestsPerSession == live.MaxTotalRequests,
            "[LS-019] the demo policy mirrors the existing live limits instead of raising them");
        Check(demo.Limits.MaxWallMillisPerTurn == live.MaxTurnSeconds * 1000,
            "[LS-019] the demo turn window matches the live turn-time limit");
        Check(demo.Limits.MaxInquiryPerTurn == 0 && demo.Limits.MaxInquiryPerSession == 0,
            "[LS-019] the demo has no inquiry allowance until the integration enables it");
        Check(demo.Limits.MaxOutputTokensPerTurn == DeliberationBudgetLimits.Unenforced
              && demo.UnenforcedDimensions().Contains("output_tokens.turn"),
            "[LS-019] the demo does not claim an enforced token budget");

        var custom = new LlmConfig
        {
            MaxRequestsPerTurn = 2,
            MaxActionsPerTurn = 1,
            MaxTotalRequests = 5,
            MaxTurnSeconds = 7,
        };
        DeliberationBudgetPolicy mirrored = DeliberationBudgetPolicies.FromDemoConfig(custom);
        Check(mirrored.Limits.MaxRequestsPerTurn == 2 && mirrored.Limits.MaxActionsPerTurn == 1
              && mirrored.Limits.MaxRequestsPerSession == 5
              && mirrored.Limits.MaxWallMillisPerTurn == 7000,
            "[LS-019] the demo factory can mirror an existing live configuration");

        var negative = new LlmConfig { MaxRequestsPerTurn = -1, MaxTotalRequests = 3 };
        Check(DeliberationBudgetPolicies.FromDemoConfig(negative).Limits.MaxRequestsPerTurn == 0,
            "[LS-019] a negative live limit becomes a zero allowance, never an unlimited budget");

        DeliberationBudgetPolicy research = DeliberationBudgetPolicies.Research("exp-inquiry-on",
            DeliberationBudgetMode.TokenStrict, new DeliberationBudgetLimits
            {
                MaxRequestsPerTurn = 8,
                MaxInquiryPerTurn = 4,
                MaxActionsPerTurn = 3,
                MaxInputTokensPerTurn = 20000,
                MaxOutputTokensPerTurn = 1000,
            });
        Check(research.TokenBudgetEnforced && research.ProfileId == "exp-inquiry-on"
              && research.ProfileVersion == DeliberationBudgetPolicies.ResearchProfileVersion,
            "[LS-019] a research policy is selected explicitly with its recorded profile id");

        var experiment = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 8,
            MaxInputTokensPerTurn = 20000,
            MaxOutputTokensPerTurn = 1000,
        }, DeliberationBudgetMode.TokenStrict);
        Check(DeliberationParses(experiment.Snapshot().ToJson()),
            "[LS-019] a fresh token-strict snapshot is valid JSON");
        Check(experiment.Snapshot().TokenBudgetEnforced
              && experiment.Snapshot().EnforcedDimensions.Contains("output_tokens.turn"),
            "[LS-019] a token-strict snapshot reports the enforced token dimensions");
    }

    // ---- inquiry spends no game resources ---------------------------------

    private static void DeliberationInquirySpendsNoGameResources()
    {
        GameEngine engine = InvestigationHistoryEngine(out Observation observation);
        InvestigationService service = InvestigationServiceFor(engine, observation);
        string canonicalBefore = CanonicalFingerprint(engine);
        string observationBefore = observation.ToJson();
        int commandPointsBefore = engine.State.FindFaction("seat-a").CommandPoints;

        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 3,
            MaxInquiryPerTurn = 2,
            MaxActionsPerTurn = 0,
        }, clock: clock, matchId: engine.State.GameId);
        ledger.BindTurn("seat-a", 1);

        DeliberationAdmission request = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(1200, 200));
        Check(request.Allowed && ledger.MarkDispatched(request.ReservationId).Ok,
            "[LS-019] a request is admitted and marked dispatched before provider work");

        DeliberationAdmission inquiry = ledger.TryAdmitInquiry();
        Check(inquiry.Allowed, "[LS-019] reading rules needs an inquiry admission, not a game action");

        Check(service.ManualLookup("turn").Ok && service.SearchHistory("", 5).Ok,
            "[LS-019] a budgeted inquiry reaches the read-only LS-018 tools");
        Check(ledger.Settle(request.ReservationId, DeliberationSettlement.Known(1100, 90)).Applied,
            "[LS-019] the request settles its measured usage once");
        Check(ledger.Settle(inquiry.ReservationId, new DeliberationSettlement { Failed = true }).Applied,
            "[LS-019] a failed inquiry still settles as one attempt");

        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(snapshot.Turn.Requests == 1 && snapshot.Turn.Inquiries == 1
              && snapshot.Turn.ActionAdmissions == 0 && snapshot.Turn.AcceptedActions == 0,
            "[LS-019] inquiry spends inquiry budget, never an action admission");
        Check(engine.State.FindFaction("seat-a").CommandPoints == commandPointsBefore,
            "[LS-019] inquiry spends no game command points");
        Check(CanonicalFingerprint(engine) == canonicalBefore
              && observation.ToJson() == observationBefore,
            "[LS-019] inquiry and budget accounting leave canonical state and the observation untouched");
        Check(snapshot.Turn.Failures == 1 && snapshot.Turn.Inquiries == 1,
            "[LS-019] a failed inquiry is recorded but still counts against the inquiry allowance");
    }
}
