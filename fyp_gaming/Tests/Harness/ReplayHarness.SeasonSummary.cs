using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

// Season-summary evidence rules: opening values, lookups and action outcomes come from the
// recorded run, and anything the records do not establish reads "unknown".
internal static partial class Harness
{
    private const string EndTurnRaw = "{\"type\":\"end_turn\"}";
    private const string ReclaimRaw = "{\"type\":\"reclaim\",\"target_id\":\"city-wei\",\"reason\":\"test\"}";
    private const string LookupRaw = "{\"tool\":\"manual_lookup\",\"topic\":\"morale\"}";

    private static void RunSeasonSummaryEvidenceTests(ReplayBundle bundle)
    {
        RunOpeningValueTests();
        RunSeasonCountTests();
        RunLookupEvidenceTests();
        RunActionOutcomeTests();
        RunLoaderEvidenceTests(bundle);
    }

    private static ReplayRequestRecord SumReq(string seat, int season, string raw, bool parseOk = true,
        bool? resultOk = null, string error = null, string inquiryTool = null, string obs = null,
        int? inquiryResultChars = null)
        => new ReplayRequestRecord {
            RunId = ReplayLoader.VerifiedDemoRunId, SeatId = seat, Season = season, RequestIndex = 1,
            RawModelText = raw, ParseOk = parseOk, ResultOk = resultOk ?? false, ResultRecorded = resultOk.HasValue,
            ResultError = error, InquiryTool = inquiryTool, ObservationJson = obs,
            InquiryResultChars = inquiryResultChars
        };

    private static List<ReplaySeasonSummaryItem> SumBuild(int seasonsPlayed, params ReplayRequestRecord[] requests)
        => ReplaySeasonSummaryBuilder.Build(new ReplayBundle {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, SeasonsPlayed = seasonsPlayed },
            Requests = new List<ReplayRequestRecord>(requests)
        });

    private static string OpenObs(string seat, string cities)
        => "{\"seat_id\":\"" + seat + "\",\"season\":1,\"cities\":[" + cities + "]}";

    private static string CityJson(string owner, string food, string gold, string garrison)
        => "{\"id\":\"c\",\"owner_seat_id\":\"" + owner + "\"" + (food != null ? ",\"food\":" + food : "")
           + (gold != null ? ",\"gold\":" + gold : "") + (garrison != null ? ",\"garrison\":" + garrison : "") + "}";

    private static void RunOpeningValueTests()
    {
        // Different opening values per seat, summed over own cities only (the enemy city is ignored).
        string wei = OpenObs("seat-0", CityJson("seat-0", "700", "300", "50") + "," + CityJson("seat-0", "100", "20", "5")
            + "," + CityJson("seat-1", "9999", "9999", "9999"));
        string shu = OpenObs("seat-1", CityJson("seat-1", "1234", "567", "8"));
        var diff = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, resultOk: null, obs: wei), SumReq("seat-1", 1, EndTurnRaw, obs: shu))[0];
        Check(diff.Wei.BeforeFood == "800" && diff.Wei.BeforeGold == "320" && diff.Wei.BeforeGarrison == "55",
            $"Wei opening is its own recorded 800/320/55 (got {diff.Wei.BeforeFood}/{diff.Wei.BeforeGold}/{diff.Wei.BeforeGarrison})");
        Check(diff.Shu.BeforeFood == "1234" && diff.Shu.BeforeGold == "567" && diff.Shu.BeforeGarrison == "8",
            $"Shu opening differs from Wei and is its own 1234/567/8 (got {diff.Shu.BeforeFood}/{diff.Shu.BeforeGold}/{diff.Shu.BeforeGarrison})");
        Check(diff.Wei.BeforeFood != "1500" && diff.Wei.BeforeGold != "900", "no hard-coded 1500/900 opening is shown");

        // A missing field on an own city is unknown for that field only.
        string noGold = OpenObs("seat-0", CityJson("seat-0", "700", null, "50"));
        var partial = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, obs: noGold))[0];
        Check(partial.Wei.BeforeFood == "700" && partial.Wei.BeforeGold == "unknown" && partial.Wei.BeforeGarrison == "50",
            $"missing gold on an own city reads unknown only for gold (got {partial.Wei.BeforeFood}/{partial.Wei.BeforeGold}/{partial.Wei.BeforeGarrison})");

        // No opening observation, wrong seat, unreadable text, no own city, and a seat with no record at all.
        var none = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, obs: null), SumReq("seat-1", 1, EndTurnRaw, obs: "not json"))[0];
        Check(none.Wei.BeforeFood == "unknown" && none.Wei.BeforeGold == "unknown" && none.Wei.BeforeGarrison == "unknown",
            "missing opening observation reads unknown");
        Check(none.Shu.BeforeFood == "unknown", "unreadable opening observation reads unknown");
        var wrongSeat = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, obs: shu))[0];
        Check(wrongSeat.Wei.BeforeFood == "unknown", "an observation belonging to another seat is not used");
        var noOwn = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, obs: OpenObs("seat-0", CityJson("seat-1", "1", "2", "3"))))[0];
        Check(noOwn.Wei.BeforeGarrison == "unknown", "no own city in the opening observation reads unknown");
        var noShuRecord = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw, obs: wei))[0];
        Check(noShuRecord.Shu.BeforeFood == "unknown" && noShuRecord.Shu.Lookups == null
              && noShuRecord.Shu.ActionCountsSummary == "unknown", "a seat with no recorded request reads unknown, not zero");

        // An opening recorded for a later season is not presented as the season-1 opening.
        var late = SumBuild(2, SumReq("seat-0", 2, EndTurnRaw, obs: wei));
        Check(late[0].Wei.BeforeFood == "unknown" && late[1].Wei.BeforeFood == "800",
            "opening observation is shown only for the season it was recorded in");
    }

    private static void RunSeasonCountTests()
    {
        Check(SumBuild(0).Count == 0, "no seasons played and no records yields no summaries (no five-season fallback)");
        Check(SumBuild(0, SumReq("seat-0", 2, EndTurnRaw)).Count == 2, "season count falls back to the latest recorded season");
        var planned = ReplaySeasonSummaryBuilder.Build(new ReplayBundle {
            Summary = new ReplaySummaryData { RunId = ReplayLoader.VerifiedDemoRunId, SeasonsPlanned = 5, SeasonsPlayed = 2 },
            Requests = new List<ReplayRequestRecord> { SumReq("seat-0", 1, EndTurnRaw) }
        });
        Check(planned.Count == 2, $"summaries follow seasons played, not seasons planned (got {planned.Count})");
    }

    private static void RunLookupEvidenceTests()
    {
        var answered = SumBuild(1, SumReq("seat-0", 1, LookupRaw, inquiryTool: "manual_lookup", inquiryResultChars: 120), SumReq("seat-0", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(answered.Lookups == 1 && answered.LookupsDisplay == "1", "a recorded answered manual_lookup is counted");
        Check(answered.AcceptedActions == 1 && answered.InvalidRequests == 0, "a lookup is neither an accepted action nor an invalid request");

        var fromText = SumBuild(1, SumReq("seat-0", 1, LookupRaw, inquiryResultChars: 120))[0].Wei;
        Check(fromText.Lookups == 1, "a lookup is also recognised from the recorded reply text when inquiry_tool is absent");

        var refused = SumBuild(1, SumReq("seat-0", 1, "{\"tool\":\"inspect_entity\"}", resultOk: false, error: "tool_not_enabled", inquiryTool: "inspect_entity"))[0].Wei;
        Check(refused.Lookups == 0 && refused.InvalidRequests == 1, "a refused inquiry is not a lookup and counts as an invalid request");

        var failed = SumBuild(1, SumReq("seat-0", 1, LookupRaw, resultOk: false, error: "inquiry_failed", inquiryTool: "manual_lookup"))[0].Wei;
        Check(failed.Lookups == 0 && failed.InvalidRequests == 1, "an unparseable inquiry is not counted as a lookup");

        var unrecorded = SumBuild(1, SumReq("seat-0", 1, LookupRaw, inquiryTool: "manual_lookup"))[0].Wei;
        Check(unrecorded.Lookups == null && unrecorded.LookupsDisplay == "unknown",
            "a manual_lookup without a recorded host result is unknown, not assumed answered or failed");

        var malformedReply = SumBuild(1, SumReq("seat-0", 1, "", parseOk: false, error: "empty"), SumReq("seat-0", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(malformedReply.Lookups == null && malformedReply.LookupsDisplay == "unknown",
            "an unparseable response makes the lookup count unknown");

        var malformedTool = SumBuild(1, SumReq("seat-0", 1, "{\"tool\":17}", parseOk: false))[0].Wei;
        Check(malformedTool.Lookups == null && malformedTool.InvalidRequests == 1,
            "a malformed tool field makes the lookup count unknown");

        var unknownTool = SumBuild(1, SumReq("seat-0", 1, "{\"tool\":\"future_tool\"}", resultOk: false, error: "unsupported", inquiryTool: "future_tool"))[0].Wei;
        Check(unknownTool.Lookups == null && unknownTool.InvalidRequests == 1,
            "an unrecognized tool name makes the lookup count unknown");

        var missing = SumBuild(1, SumReq("seat-0", 1, ""), SumReq("seat-0", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(missing.Lookups == null && missing.LookupsDisplay == "unknown", "missing inquiry evidence reads unknown, not zero");
        var noRecords = SumBuild(1, SumReq("seat-1", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(noRecords.Lookups == null && noRecords.LookupsDisplay == "unknown", "no records for the seat/season reads unknown, not zero");
        var established = SumBuild(1, SumReq("seat-0", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(established.Lookups == 0, "a recorded reply that is not an inquiry establishes zero lookups");
    }

    private static void RunActionOutcomeTests()
    {
        var accepted = SumBuild(1, SumReq("seat-0", 1, ReclaimRaw, resultOk: true))[0].Wei;
        Check(accepted.AcceptedActions == 1 && accepted.RejectedActions == 0
              && accepted.ActionCountsSummary == "accepted 1 (reclaim: 1) | rejected 0 | invalid 0 | end_turn 0",
            $"recorded accepted action is counted as accepted (got '{accepted.ActionCountsSummary}')");

        var rejected = SumBuild(1, SumReq("seat-0", 1, ReclaimRaw, resultOk: false, error: "not enough command points"))[0].Wei;
        Check(rejected.AcceptedActions == 0 && rejected.RejectedActions == 1
              && rejected.ActionCountsSummary == "accepted 0 | rejected 1 | invalid 0 | end_turn 0",
            $"recorded rejected action is counted as rejected, not accepted (got '{rejected.ActionCountsSummary}')");

        var invalid = SumBuild(1, SumReq("seat-0", 1, "not an action at all", parseOk: false), SumReq("seat-0", 1, "{\"nonsense\":1}"))[0].Wei;
        Check(invalid.InvalidRequests == 2 && invalid.AcceptedActions == 0 && invalid.RejectedActions == 0,
            "unparseable replies are invalid requests, separate from rejected actions");

        // end_turn carries a derived (not recorded) ResultOk and must not be counted as an accepted action.
        var end = SumBuild(1, SumReq("seat-0", 1, EndTurnRaw))[0].Wei;
        var endDerived = new ReplayRequestRecord { SeatId = "seat-0", Season = 1, RawModelText = EndTurnRaw, ParseOk = true, ResultOk = true, ResultRecorded = false };
        var endWithOk = SumBuild(1, endDerived)[0].Wei;
        Check(end.EndTurns == 1 && end.AcceptedActions == 0 && endWithOk.EndTurns == 1 && endWithOk.AcceptedActions == 0
              && endWithOk.ActionCountsSummary == "accepted 0 | rejected 0 | invalid 0 | end_turn 1",
            $"end_turn is reported separately from game actions (got '{endWithOk.ActionCountsSummary}')");

        var unrecorded = SumBuild(1, SumReq("seat-0", 1, ReclaimRaw, resultOk: null))[0].Wei;
        Check(unrecorded.AcceptedActions == 0 && unrecorded.RejectedActions == 0 && unrecorded.UnrecordedOutcomes == 1
              && unrecorded.ActionCountsSummary.Contains("outcome unrecorded 1"),
            "a parsed action with no recorded result is not claimed accepted");
    }

    private static void RunLoaderEvidenceTests(ReplayBundle bundle)
    {
        // The real run: actions carry result_ok, end_turn rows do not (their ResultOk is derived).
        int recorded = 0, derivedEnd = 0;
        foreach (var req in bundle.Requests)
        {
            if (req.ResultRecorded) recorded++;
            else if (req.RawModelText != null && req.RawModelText.Contains("end_turn") && req.ResultOk) derivedEnd++;
        }
        Check(recorded == 42 && derivedEnd == 2, $"loader marks 42 recorded results and 2 derived end_turn results (got {recorded}/{derivedEnd})");

        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi_sum_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string run = ReplayLoader.VerifiedDemoRunId;
            string sFile = Path.Combine(tempDir, "sum.json"), jFile = Path.Combine(tempDir, "req.jsonl"), eFile = Path.Combine(tempDir, "ex.ndjson");
            File.WriteAllText(sFile, "{\"run_id\":\"" + run + "\",\"seed\":12345,\"requests\":3,\"seasons_played\":1}");
            string Row(int idx, string rawEscaped, string extra)
                => "{\"run_id\":\"" + run + "\",\"seed\":12345,\"seat_id\":\"seat-0\",\"turn_token\":1,\"season\":1,\"request_index\":" + idx
                   + ",\"total_requests\":3,\"raw_model_text\":\"" + rawEscaped + "\",\"parse_ok\":true,\"observation_hash\":\"h\"" + extra + "}\n";
            File.WriteAllText(jFile,
                Row(1, "{\\\"tool\\\":\\\"manual_lookup\\\",\\\"topic\\\":\\\"morale\\\"}", ",\"result_ok\":null,\"inquiry_tool\":\"manual_lookup\",\"inquiry_result_chars\":120")
                + Row(2, "{\\\"type\\\":\\\"reclaim\\\",\\\"target_id\\\":\\\"city-wei\\\"}", "")
                + Row(3, "{\\\"tool\\\":\\\"manual_lookup\\\",\\\"topic\\\":\\\"unknown\\\"}", ",\"result_ok\":false,\"result_error\":\"inquiry_failed\",\"inquiry_tool\":\"manual_lookup\""));
            string Ex(int idx, string ev, string body)
                => "{\"run_id\":\"" + run + "\",\"seat_id\":\"seat-0\",\"season\":1,\"request_index\":" + idx + ",\"exchange_event\":\"" + ev + "\"," + body + "}\n";
            string exchanges = "";
            for (int i = 1; i <= 3; i++)
                exchanges += Ex(i, "request", "\"request\":{\"user\":\"u\"}") + Ex(i, "result", "\"response\":{\"content_text\":\"{\\\"summary\\\":\\\"s\\\"}\"}");
            File.WriteAllText(eFile, exchanges);

            var loaded = ReplayLoader.Load(sFile, jFile, eFile);
            Check(loaded.Requests[0].InquiryTool == "manual_lookup" && !loaded.Requests[0].ResultRecorded
                  && loaded.Requests[0].InquiryResultChars == 120,
                "loader reads real inquiry evidence: result_ok null with inquiry_result_chars present");
            Check(!loaded.Requests[1].ResultRecorded && !loaded.Requests[1].ResultOk,
                "an action row without result_ok is not treated as accepted");
            var side = ReplaySeasonSummaryBuilder.Build(loaded)[0].Wei;
            Check(side.Lookups == 1 && side.InvalidRequests == 1 && side.AcceptedActions == 0 && side.UnrecordedOutcomes == 1,
                "loaded runner records count answered lookup from chars, exclude failed lookup and leave action outcome unknown");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
    }
}
