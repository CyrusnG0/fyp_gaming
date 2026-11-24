using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

// [CATALOG] Replay chooser logic: presets, run discovery, validation and the LLM-only gate, plus the
// recorded statement / reasoning-token fields. Everything runs offline against files this test writes.
// External recordings are exercised only when their files are found, and skipped loudly otherwise.

internal static partial class Harness
{
    private static void CatCheck(bool ok, string name) => Check(ok, "[CATALOG] " + name);

    /// <summary>Replies with a recorded raw body that carries a provider reasoning stream for actions.</summary>
    private sealed class CatReasoningTransport : ILlmTransport
    {
        private readonly Queue<string> _replies = new Queue<string>();
        private int _calls;
        public string Provider => "fake";
        public string Model => "fake-reasoning";
        public CatReasoningTransport(IEnumerable<string> replies) { foreach (string r in replies) _replies.Enqueue(r); }
        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            _calls++;
            string reply = _replies.Count > 0 ? _replies.Dequeue() : "{\"type\":\"end_turn\",\"summary\":\"done\"}";
            int inTok = ((req.System?.Length ?? 0) + (req.User?.Length ?? 0)) / 4 + 10;
            if (reply.Contains("end_turn"))
                return Task.FromResult(LlmResponse.Success(reply, 5, inTok, 5, "fake-" + _calls, "stop", null, inTok + 5, null, null, 200));
            string reasoning = "RZ-" + _calls + " weighing the recorded options";
            string raw = "{\"id\":\"fake-" + _calls + "\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":"
                + MiniJson.Str(reply) + ",\"reasoning_content\":" + MiniJson.Str(reasoning) + "}}]}";
            return Task.FromResult(LlmResponse.Success(reply, 5, inTok, 40, "fake-" + _calls, "stop", 111,
                inTok + 40, reasoning.Length, raw, 200));
        }
    }

    private static void RunReplayCatalog()
    {
        Section("[CATALOG] replay chooser: presets, discovery, validation, LLM-only gate, statements");
        string root = Path.Combine(Path.GetTempPath(), "luanshi-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CatalogPresets(root);
            CatalogDiscovery(root);
            CatalogReasoningExtraction();
            CatalogRecordedRoundTrip(root);
            CatalogExternalRuns();
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
    }

    private static void CatalogPresets(string root)
    {
        ReplayPresetSet missing = ReplayCatalog.LoadPresets(Path.Combine(root, "nope.json"));
        CatCheck(!missing.FromFile && missing.Message != null && missing.Message.Contains("not found")
                 && missing.Presets.Count == ReplayCatalog.BuiltInPresets.Count && missing.Presets[0].Name == "Treaty & Attack"
                 && missing.Presets[0].RunId == ReplayCatalog.TreatyAttackRunId,
            "a missing preset file is stated and the built-in 'Treaty & Attack' preset remains");

        string bad = Path.Combine(root, "bad.json");
        File.WriteAllText(bad, "{ not json");
        ReplayPresetSet malformed = ReplayCatalog.LoadPresets(bad);
        CatCheck(!malformed.FromFile && malformed.Message.Contains("not valid JSON") && malformed.Presets.Count == 1,
            "a malformed preset file is reported and replaced by the built-ins");

        string empty = Path.Combine(root, "empty.json");
        File.WriteAllText(empty, "{\"presets\":[{\"name\":\"x\"},{\"run_id\":\"run-1\"}]}");
        ReplayPresetSet none = ReplayCatalog.LoadPresets(empty);
        CatCheck(!none.FromFile && none.Message.Contains("no usable presets") && none.Presets.Count == 1,
            "entries without both a name and a run_id are skipped; an empty list falls back and says so");

        string good = Path.Combine(root, "good.json");
        File.WriteAllText(good, "{\"search_roots\":[\"" + root.Replace("\\", "\\\\") + "\"],\"presets\":["
            + "{\"name\":\"Treaty & Attack\",\"run_id\":\"run-20261123090426264-1890d898\",\"note\":\"custom\"},"
            + "{\"name\":\"Capture\",\"run_id\":\"run-capture-1\"},{\"name\":\"no id\"}]}");
        ReplayPresetSet ok = ReplayCatalog.LoadPresets(good);
        CatCheck(ok.FromFile && ok.Message == null && ok.Presets.Count == 2 && ok.Presets[0].Note == "custom"
                 && ok.Presets[1].Name == "Capture" && ok.SearchRoots[0] == root,
            "a valid preset file replaces the built-ins; named presets and search roots are read");

        ReplayPresetSet fallback = ReplayCatalog.LoadPresets();
        CatCheck(fallback.Presets.Count >= 1 && (fallback.FromFile || fallback.Message != null),
            "the default lookup never throws and always yields a preset list with a reason when no file is used");
    }

    private static void CatalogDiscovery(string root)
    {
        ReplayOpenResult empty = ReplayCatalog.Open("  ");
        CatCheck(empty.Status == ReplayOpenStatus.NotFound && empty.Message == "Enter a run ID." && empty.Session == null,
            "an empty run id asks for one");
        foreach (string unsafeId in new[] { "../x", "run 1", "a/b", "run:1" })
            CatCheck(ReplayCatalog.Open(unsafeId, new[] { root }).Status == ReplayOpenStatus.Invalid,
                "run id '" + unsafeId + "' is refused as unsafe");
        ReplayOpenResult unknown = ReplayCatalog.Open("run-does-not-exist-0001", new[] { root });
        CatCheck(unknown.Status == ReplayOpenStatus.NotFound && unknown.Message.Contains("local-only")
                 && unknown.Message.Contains(ReplayCatalog.RunsEnvVar),
            "an unknown run is NotFound and says run files do not come with a clone");

        string partialId = "run-20990101000000000-aaaaaaaa";
        File.WriteAllText(Path.Combine(root, "pilot-" + partialId + "-summary.json"), "{}");
        ReplayOpenResult partial = ReplayCatalog.Open(partialId, new[] { root });
        CatCheck(partial.Status == ReplayOpenStatus.Incomplete && partial.Message.Contains("missing jsonl")
                 && partial.Message.Contains("missing exchanges") && partial.Message.Contains("missing bootstrap"),
            "a summary without its request log, exchanges and bootstrap is Incomplete and lists what is missing");
        CatCheck(ReplayCatalog.Probe(partialId, new[] { root }, out _) == ReplayOpenStatus.Incomplete,
            "Probe reports the same incomplete state without loading");

        string junkId = "run-20990101000000001-bbbbbbbb";
        File.WriteAllText(Path.Combine(root, "pilot-" + junkId + "-summary.json"), "{\"run_id\":\"" + junkId + "\"}");
        File.WriteAllText(Path.Combine(root, junkId + ".jsonl"), "not json\n");
        File.WriteAllText(Path.Combine(root, junkId + "-exchanges.ndjson"), "\n");
        File.WriteAllText(Path.Combine(root, junkId + "-bootstrap.json"), "{}");
        ReplayOpenResult junk = ReplayCatalog.Open(junkId, new[] { root });
        CatCheck(junk.Status == ReplayOpenStatus.Invalid && junk.Message.Contains("failed validation") && junk.Session == null,
            "files that are present but invalid are reported as Invalid, never opened");

        // Mixed controllers are outside replay support.
        ReplayBundle mixed = DipReplayBundle(PromptBuilder.PromptVersion);
        CatCheck(ReplayCatalog.CountNonLlmRequests(mixed) == 0, "an LLM-only bundle has no non-LLM requests");
        mixed.Requests[0].ControllerType = ControllerType.Human;
        CatCheck(ReplayCatalog.CountNonLlmRequests(mixed) == 1 && ReplayCatalog.LlmOnlyLabel.Contains("mixed human/LLM replay is not yet supported"),
            "a human-controlled request is counted, and the LLM-only label states mixed replay is unsupported");
    }

    private static void CatalogReasoningExtraction()
    {
        string body = "{\"choices\":[{\"message\":{\"content\":\"x\",\"reasoning_content\":\"because food\"}}]}";
        CatCheck(ReplayLoader.ExtractProviderReasoning(body) == "because food", "provider reasoning is read from choices[0].message");
        CatCheck(ReplayLoader.ExtractProviderReasoning(null) == null && ReplayLoader.ExtractProviderReasoning("") == null
                 && ReplayLoader.ExtractProviderReasoning("{\"choices\":[{\"message\":{\"content\":\"x\"}}]}") == null
                 && ReplayLoader.ExtractProviderReasoning("{\"choices\":[]}") == null
                 && ReplayLoader.ExtractProviderReasoning("[1,2]") == null
                 && ReplayLoader.ExtractProviderReasoning("{\"choices\":[{\"message\":{\"reasoning_content\":\"cut off") == null,
            "absent, empty, truncated or non-matching bodies yield no reasoning and never throw");
        string longText = new string('r', ReplayLoader.MaxProviderReasoningChars + 500);
        string capped = ReplayLoader.ExtractProviderReasoning(
            "{\"choices\":[{\"message\":{\"reasoning_content\":" + MiniJson.Str(longText) + "}}]}");
        CatCheck(capped != null && capped.Length < longText.Length && capped.EndsWith("[truncated]"),
            "very long reasoning is capped and marked");
        var evt = new ReplayFeedEvent { Explanation = "raise food", ReasoningTokens = 595 };
        var none = new ReplayFeedEvent { Explanation = "unknown" };
        CatCheck(evt.StatementText() == "[reasoning tokens: 595] model statement: \"raise food\""
                 && none.StatementText() == "[reasoning tokens: not reported] model statement: none recorded",
            "the displayed statement is labelled as a model statement with the provider's token count or 'not reported'");
        CatCheck(!evt.StatementText().ToLowerInvariant().Contains("thought") && !evt.StatementText().ToLowerInvariant().Contains("verified reasoning"),
            "the statement line makes no claim to be the model's reasoning");
    }

    private static void CatalogRecordedRoundTrip(string root)
    {
        // A recorded match through the production runner, recorder, bootstrap and summary writer, with unit
        // types on: an accepted archer recruit, a research-gated cavalry recruit, then end turns.
        LlmConfig cfg = new LlmConfig
        {
            EnableUnitTypes = true, EnableGarrisonConversion = true,
            MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4, MaxTotalRequests = 30,
        };
        var recorder = new LlmRunRecorder(root);
        GameEngine eng = BuildPaidShapeEngine(12345, "catalog-game");
        CityState home = eng.State.FindCity("city-wei");
        home.Garrison = 0;
        var transport = new CatReasoningTransport(new[]
        {
            "{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":\"archer\"},\"summary\":\"raise archers\"}",
            "{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":\"cavalry\"},\"summary\":\"try cavalry\"}",
            "{\"type\":\"end_turn\",\"summary\":\"wei done\"}",
            "{\"type\":\"end_turn\",\"summary\":\"shu done\"}",
        });
        var runner = new LlmTurnRunner(transport, cfg, recorder, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(eng, runner, 1, 12345, recorder);
        recorder.WriteReplayBootstrap(eng, 12345, cfg, "test", "spectator", 1);
        string runId = runner.RunId;
        session.Start();
        while (session.State == SpectatorState.Running) { session.Tick(); Thread.Sleep(1); }
        string summaryPath = SpectatorSummary.Write(root, session, cfg, transport, runner);
        CatCheck(File.ReadAllText(summaryPath).Contains("\"unit_types_enabled\":true")
                 && File.ReadAllText(summaryPath).Contains(PromptBuilder.UnitTypesVersionSuffix),
            "the run summary records unit types and the +unit-types-1 prompt identity");

        ReplayOpenResult open = ReplayCatalog.Open(runId, new[] { root });
        CatCheck(open.Ok && open.Status == ReplayOpenStatus.Ready && open.Session != null,
            "the recorded run opens through the catalog: " + open.Message);
        if (!open.Ok) return;
        Console.WriteLine("  [CATALOG] notes: " + string.Join(" || ", open.Notes));
        CatCheck(open.Notes.Count >= 2 && open.Notes[0] == ReplayCatalog.LlmOnlyLabel
                 && open.Notes.Any(n => n.StartsWith("Recorded material only")),
            "the open result carries the LLM-only label and the recorded-material label");
        ReplayBundle bundle = open.Bundle;
        CatCheck(bundle.Requests.Count == 4 && bundle.Requests.All(r => PromptBuilder.IsUnitTypesPromptVersion(r.PromptVersion)),
            "every recorded request carries the unit-types identity");

        // Reasoning fields: reported for actions, not reported for end_turn, text only for what was recorded.
        ReplayRequestRecord first = bundle.Requests[0], second = bundle.Requests[1], end = bundle.Requests[2];
        CatCheck(first.ReasoningTokensReported == 111 && first.ReasoningContentChars.HasValue
                 && first.ProviderReasoningText == "RZ-1 weighing the recorded options",
            "an action request carries the provider's reasoning-token count and, separately, its recorded reasoning text");
        CatCheck(second.ReasoningTokensReported == 111 && second.ProviderReasoningText == "RZ-2 weighing the recorded options",
            "each request carries its own reasoning, not another request's");
        CatCheck(end.ReasoningTokensReported == null && end.ProviderReasoningText == null && end.RecordedExplanation == "wei done",
            "a request with no reported reasoning says so and carries no reasoning text; its statement is still shown");
        CatCheck(first.RecordedExplanation == "raise archers" && !first.RecordedExplanation.Contains("RZ-"),
            "the model's statement and the provider reasoning stream are kept apart");

        open.Session.PauseOnDivergence = false;
        open.Session.RunToCompletion();
        ReplaySession replay = open.Session;
        CatCheck(replay.IsCompleted && replay.AcceptedActionsReplayed == 1 && replay.EngineRejectionsReplayed == 1
                 && replay.VoluntaryEndTurnsReplayed == 2 && replay.ActionOutcomeMismatches == 0
                 && replay.UnexpectedAcceptances == 0 && replay.UnexpectedRejections == 0,
            "the replay reproduces the recorded outcomes (archer accepted, cavalry gated, two end turns, no mismatches)");
        EventRecord recruit = replay.Engine.Log.Records.FirstOrDefault(r => r.EventType == "recruit");
        CatCheck(recruit != null && recruit.Payload["unit_type"] == "archer"
                 && replay.Engine.State.FindCity("city-wei").GarrisonType == "archer",
            "the replayed engine recruited archers through the same validator");
        CatCheck(replay.EventFeed.Count == 4 && replay.EventFeed[0].ReasoningTokens == 111
                 && replay.EventFeed[2].ReasoningTokens == null
                 && replay.EventFeed[0].StatementText() == "[reasoning tokens: 111] model statement: \"raise archers\""
                 && replay.EventFeed[2].StatementText().StartsWith("[reasoning tokens: not reported]"),
            "feed events carry the statement and token count shown in the main view");
        CatCheck(replay.EventFeed.All(e => !e.StatementText().Contains("RZ-")),
            "the main-view statement never includes the provider reasoning text");

        // The same files with the unit-types identity stripped do not silently replay under the new vocabulary.
        string oldId = runId + "-old";
        foreach (string suffix in new[] { ".jsonl", "-exchanges.ndjson", "-bootstrap.json" })
        {
            string src = Path.Combine(root, runId + suffix);
            if (File.Exists(src)) File.WriteAllText(Path.Combine(root, oldId + suffix),
                File.ReadAllText(src).Replace(runId, oldId));
        }
        string sum = File.ReadAllText(summaryPath).Replace(runId, oldId);
        File.WriteAllText(Path.Combine(root, "pilot-" + oldId + "-summary.json"), sum);
        ReplayOpenResult relabelled = ReplayCatalog.Open(oldId, new[] { root });
        CatCheck(relabelled.Status != ReplayOpenStatus.Ready || relabelled.Message != null,
            "edited copies are validated like any other run (never assumed valid)");
    }

    private static void CatalogExternalRuns()
    {
        // External evidence: found through LUANSHI_RUNS_DIR or the loader's own locations; skipped loudly.
        ReplayPresetSet presets = ReplayCatalog.LoadPresets(Path.Combine(Path.GetTempPath(), "luanshi-no-such-preset.json"));
        ReplayOpenResult treaty = ReplayCatalog.Open(ReplayCatalog.TreatyAttackRunId, presets.SearchRoots);
        if (treaty.Status == ReplayOpenStatus.NotFound)
        {
            Console.WriteLine("  [CATALOG] skipped external " + ReplayCatalog.TreatyAttackRunId
                + " (" + ReplayCatalog.RunsEnvVar + " not set or files absent)");
            return;
        }
        CatCheck(treaty.Ok && treaty.Bundle.Requests.Count == 86 && treaty.Notes[0] == ReplayCatalog.LlmOnlyLabel,
            "treaty run opens as an LLM-only recording of 86 requests (" + treaty.Status + ": " + treaty.Message + ")");
        if (!treaty.Ok) return;
        int reported = treaty.Bundle.Requests.Count(r => r.ReasoningTokensReported.HasValue);
        int withText = treaty.Bundle.Requests.Count(r => !string.IsNullOrEmpty(r.ProviderReasoningText));
        Console.WriteLine("  [CATALOG] treaty run: reasoning tokens reported on " + reported + "/86 requests, recorded reasoning text on "
            + withText + "/86; assault rule: " + treaty.Session.AssaultTroopRatioProvenance);
        CatCheck(reported > 0, "the treaty recording's reasoning-token counts are read from its records");
        CatCheck(treaty.Notes.Any(n => n.StartsWith("City-assault rule:")) && treaty.Notes.Any(n => n.StartsWith("Recorded with")),
            "the treaty recording's open result states its provider and its historical assault-rule provenance");
    }
}
