using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        /// <summary>CS-04: an empty reply and a non-JSON reply both become invalid-response evidence
        /// and a bounded stop; nothing crashes and the scenario is not reported as a clean pass.</summary>
        private static void MalformedAndEmptyResponse(TestLog log)
        {
            CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4242);
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step("", fault: "empty_reply"),
                CsScenarioFactory.Step("this is not json {oops"),
            };
            spec.MaxRequests = 10;
            spec.MaxWallSeconds = 20;
            spec.RequestTimeoutMs = 5000;
            CsScenarioResult result = RunSpec(spec, "cs04", out string root);
            try
            {
                List<JsonElement> runs = ReadJsonLines(RunFile(result.Directory));
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                JsonElement empty = Find(runs, r => CsEvidenceRecorder.Text(r, "seat_id") == "seat-0"
                    && CsEvidenceRecorder.Text(r, "raw_model_text") == "");
                JsonElement garbage = Find(runs, r => CsEvidenceRecorder.Text(r, "seat_id") == "seat-0"
                    && (CsEvidenceRecorder.Text(r, "raw_model_text") ?? "").Contains("not json"));
                log.Check(empty.ValueKind != JsonValueKind.Undefined
                        && CsEvidenceRecorder.Bool(empty, "parse_ok") == false,
                    "[CS-04] the empty reply is retained as an invalid response");
                log.Check(garbage.ValueKind != JsonValueKind.Undefined
                        && CsEvidenceRecorder.Bool(garbage, "parse_ok") == false,
                    "[CS-04] the non-JSON reply is retained as an invalid response");
                int invalid = 0;
                foreach (JsonElement dispatch in dispatches)
                    if (CsEvidenceRecorder.Text(dispatch, "status") == "invalid_response"
                        && CsEvidenceRecorder.Bool(Obj(dispatch, "action"), "accepted") == false) invalid++;
                log.Check(invalid >= 2,
                    "[CS-04] both malformed replies have invalid-response dispatch records");
                string summary = SummaryText(result.Directory);
                log.Check(result.Status != CsStatus.Completed
                        && (summary.IndexOf("InvalidResponseLimit", StringComparison.Ordinal) >= 0
                            || summary.IndexOf("invalid-response", StringComparison.Ordinal) >= 0),
                    "[CS-04] the invalid replies end the turn with a bounded stop, not a crash",
                    result.Status + " " + result.Detail);
            }
            finally { Cleanup(root); }
        }

        /// <summary>CS-06: provider-missing usage stays null in the run line, dispatch and summary;
        /// unknown_fields names it and no total is reported as zero.</summary>
        private static void MissingUsageIsUnknown(TestLog log)
        {
            CsScenarioSpec spec = Builtin("cs-visible-contact-v1", 4242);
            spec.Scripts["seat-0"] = new List<CsScriptStep>
            {
                CsScenarioFactory.Step(CsScenarioFactory.EndTurn("no usage"), fault: "missing_usage"),
            };
            spec.MaxRequests = 10;
            spec.MaxWallSeconds = 20;
            spec.RequestTimeoutMs = 5000;
            CsScenarioResult result = RunSpec(spec, "cs06", out string root);
            try
            {
                JsonElement runLine = Find(ReadJsonLines(RunFile(result.Directory)),
                    r => CsEvidenceRecorder.Text(r, "seat_id") == "seat-0");
                log.Check(runLine.ValueKind != JsonValueKind.Undefined
                        && IsNull(runLine, "input_tokens") && IsNull(runLine, "output_tokens"),
                    "[CS-06] the production run line keeps missing token counts as null");

                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                JsonElement dispatch = Find(dispatches,
                    d => CsEvidenceRecorder.Text(d, "seat_id") == "seat-0");
                JsonElement reply = Obj(dispatch, "reply");
                log.Check(IsNull(reply, "input_tokens") && IsNull(reply, "output_tokens")
                        && IsNull(reply, "total_tokens"),
                    "[CS-06] the dispatch keeps missing token counts null");
                log.Check(Names(reply, "unknown_fields", "input_tokens")
                        && Names(reply, "unknown_fields", "output_tokens")
                        && Names(dispatch, "unknown_fields", "reply.input_tokens"),
                    "[CS-06] unknown_fields names the missing token fields");
                log.Check(result.InputTokens == null && result.OutputTokens == null
                        && result.UnknownUsageRequests >= 1,
                    "[CS-06] the scenario totals stay unknown and count the request",
                    "input=" + result.InputTokens + " unknown=" + result.UnknownUsageRequests);

                string summaryPath = SummaryFile(result.Directory);
                JsonElement summary = summaryPath == null ? default : ReadJsonFile(summaryPath);
                log.Check(IsNull(summary, "input_tokens") && IsNull(summary, "output_tokens")
                        && IsNull(Obj(summary, "totals"), "input_tokens"),
                    "[CS-06] the production summary reports unknown as null, never zero");
            }
            finally { Cleanup(root); }
        }

        /// <summary>CS-07: every dispatch joins to exactly one run, exchange request/result and
        /// prompt line, the hashes match, and only a wall-stopped run may leave an in-flight record.</summary>
        private static void EvidenceLinksJoin(TestLog log)
        {
            string outRoot = NewRunDir("cs07");
            try
            {
                CsRunResult run = CsRunner.Run(
                    Manifest(new[] { "cs-visible-contact-v1" }, 4242), outRoot, CsPaths.RepoRoot());
                CsScenarioResult scenario = run.Scenarios[0];
                bool ok = VerifyLinks(scenario, 0, out string detail);
                log.Check(ok, "[CS-07] every dispatch joins exactly to its run, exchange and prompt lines",
                    detail);
                log.Check(ok && detail.IndexOf("in-flight", StringComparison.Ordinal) < 0,
                    "[CS-07] a completed scenario has no unlinked in-flight dispatch");

                CsScenarioSpec wall = Builtin("cs-visible-contact-v1", 4243);
                wall.Scripts["seat-0"] = new List<CsScriptStep>
                {
                    CsScenarioFactory.Step(CsScenarioFactory.EndTurn("slow fixture"), delayMs: 3000),
                };
                wall.MaxRequests = 10;
                wall.MaxWallSeconds = 1;
                wall.RequestTimeoutMs = 60000;
                CsScenarioResult stopped = RunSpec(wall, "cs07-wall", out string wallRoot);
                try
                {
                    bool wallOk = VerifyLinks(stopped, 1, out string wallDetail);
                    log.Check(stopped.Status == CsStatus.Stopped && wallOk,
                        "[CS-07] a wall stop allows at most one in-flight record, labelled as such",
                        wallDetail);
                }
                finally { Cleanup(wallRoot); }
            }
            finally { Cleanup(outRoot); }
        }

        private static bool VerifyLinks(CsScenarioResult scenario, int allowedUnlinked, out string detail)
        {
            detail = "";
            List<JsonElement> dispatches = ReadJsonLines(
                Path.Combine(scenario.Directory, "dispatches.ndjson"));
            List<JsonElement> prompts = ReadJsonLines(
                Path.Combine(scenario.Directory, "prompts.ndjson"));
            string runPath = RunFile(scenario.Directory);
            string exchangePath = ExchangeFile(scenario.Directory);
            List<JsonElement> runs = ReadJsonLines(runPath);
            List<JsonElement> exchanges = ReadJsonLines(exchangePath);
            string runName = runPath == null ? null : Path.GetFileName(runPath);
            string exchangeName = exchangePath == null ? null : Path.GetFileName(exchangePath);
            bool ok = runName != null && exchangeName != null && dispatches.Count > 0;
            int unlinked = 0;
            foreach (JsonElement dispatch in dispatches)
            {
                JsonElement links = Obj(dispatch, "links");
                string runLink = CsEvidenceRecorder.Text(links, "run_record");
                if (runLink == null)
                {
                    unlinked++;
                    if (CsEvidenceRecorder.Text(dispatch, "status") != "in_flight_stopped")
                    { ok = false; detail += "unlinked dispatch is not an in-flight stop; "; }
                }
                else
                {
                    string error;
                    ok &= Join(runLink, runName, runs, dispatch, null, out error);
                    if (error != null) detail += error + "; ";
                    ok &= Join(CsEvidenceRecorder.Text(links, "exchange_request"), exchangeName,
                        exchanges, dispatch, "request", out error);
                    if (error != null) detail += error + "; ";
                    ok &= Join(CsEvidenceRecorder.Text(links, "exchange_result"), exchangeName,
                        exchanges, dispatch, "result", out error);
                    if (error != null) detail += error + "; ";
                }

                string promptLink = CsEvidenceRecorder.Text(links, "prompt_capture");
                if (!LinkIndex(promptLink, "prompts.ndjson", out int promptLine)
                    || promptLine < 1 || promptLine > prompts.Count)
                { ok = false; detail += "prompt link does not resolve; "; continue; }
                JsonElement promptRecord = prompts[promptLine - 1];
                JsonElement prompt = Prompt(dispatch);
                if (CsEvidenceRecorder.Text(promptRecord, "seat") != CsEvidenceRecorder.Text(dispatch, "seat_id")
                    || CsEvidenceRecorder.Text(promptRecord, "system_sha256") != CsEvidenceRecorder.Text(prompt, "system_sha256")
                    || CsEvidenceRecorder.Text(promptRecord, "user_sha256") != CsEvidenceRecorder.Text(prompt, "user_sha256"))
                { ok = false; detail += "prompt capture does not match the dispatch; "; }
                if (CsWorld.Sha256(CsEvidenceRecorder.Text(prompt, "system"))
                        != CsEvidenceRecorder.Text(prompt, "system_sha256")
                    || CsWorld.Sha256(CsEvidenceRecorder.Text(prompt, "user"))
                        != CsEvidenceRecorder.Text(prompt, "user_sha256"))
                { ok = false; detail += "prompt hash mismatch; "; }
                JsonElement before = Obj(Obj(dispatch, "state"), "before");
                string observation = CsEvidenceRecorder.Text(before, "observation_json");
                if (observation != null && CsWorld.Sha256(observation)
                        != CsEvidenceRecorder.Text(before, "observation_sha256"))
                { ok = false; detail += "observation hash mismatch; "; }
            }
            if (unlinked > allowedUnlinked)
            { ok = false; detail += unlinked + " unlinked dispatch(es) exceeds " + allowedUnlinked + "; "; }
            return ok;
        }

        private static bool Join(string link, string expectedFile, List<JsonElement> lines,
            JsonElement dispatch, string exchangeEvent, out string error)
        {
            error = null;
            if (!LinkIndex(link, expectedFile, out int number) || number < 1 || number > lines.Count)
            {
                error = "link " + (link ?? "null") + " does not resolve to " + expectedFile;
                return false;
            }
            JsonElement line = lines[number - 1];
            bool ok = CsEvidenceRecorder.Text(line, "seat_id") == CsEvidenceRecorder.Text(dispatch, "seat_id")
                && CsEvidenceRecorder.Int(line, "turn_token") == CsEvidenceRecorder.Int(dispatch, "turn_token")
                && CsEvidenceRecorder.Int(line, "request_index") == CsEvidenceRecorder.Int(dispatch, "request_index");
            if (exchangeEvent != null)
                ok &= CsEvidenceRecorder.Text(line, "exchange_event") == exchangeEvent;
            if (!ok) error = "linked line " + number + " of " + expectedFile + " does not match the dispatch";
            return ok;
        }

        private static bool LinkIndex(string link, string expectedFile, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(link)) return false;
            int at = link.LastIndexOf(':');
            if (at <= 0) return false;
            if (Path.GetFileName(link.Substring(0, at)) != expectedFile) return false;
            return int.TryParse(link.Substring(at + 1), out number);
        }

        /// <summary>CS-08: the staged reject is retained as result_ok=false and the legal move as
        /// result_ok=true, each with a before/after state (world unchanged on rejection).</summary>
        private static void AcceptedAndRejectedAction(TestLog log)
        {
            CsScenarioResult result = RunSpec(Builtin("cs-visible-contact-v1", 4242), "cs08", out string root);
            try
            {
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                JsonElement accepted = Find(dispatches,
                    d => CsEvidenceRecorder.Bool(Obj(d, "action"), "accepted") == true);
                JsonElement rejected = Find(dispatches,
                    d => CsEvidenceRecorder.Bool(Obj(d, "action"), "accepted") == false
                      && CsEvidenceRecorder.Text(Obj(d, "action"), "result_error") != null);
                log.Check(accepted.ValueKind != JsonValueKind.Undefined,
                    "[CS-08] a legal order is recorded as accepted (result_ok=true)");
                log.Check(rejected.ValueKind != JsonValueKind.Undefined,
                    "[CS-08] the staged illegal order is recorded as rejected with its result error");

                log.Check(accepted.ValueKind != JsonValueKind.Undefined
                        && CsEvidenceRecorder.Text(Obj(accepted, "action"), "command_json") != null
                        && Digest(accepted, "before") != null && Digest(accepted, "after") != null
                        && Digest(accepted, "before") != Digest(accepted, "after"),
                    "[CS-08] the accepted order keeps its command and a changed before/after world");
                log.Check(rejected.ValueKind != JsonValueKind.Undefined
                        && Digest(rejected, "before") != null && Digest(rejected, "after") != null
                        && Digest(rejected, "before") == Digest(rejected, "after"),
                    "[CS-08] the rejected order keeps before/after state and leaves the world unchanged");
            }
            finally { Cleanup(root); }
        }

        private static string Digest(JsonElement dispatch, string side)
        {
            JsonElement state = Obj(Obj(dispatch, "state"), side);
            return CsEvidenceRecorder.Text(state, "canonical_world_digest");
        }

        /// <summary>CS-09: the manual_lookup request and its bounded result are linked, and the
        /// result bytes are the ones between the next prompt's inquiry markers.</summary>
        private static void InquiryResultLinked(TestLog log)
        {
            CsScenarioResult result = RunSpec(Builtin("cs-inquiry-manual-v1", 4246), "cs09", out string root);
            try
            {
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                int index = IndexOf(dispatches, d => CsEvidenceRecorder.Text(Obj(d, "action"), "kind") == "inquiry"
                    && CsEvidenceRecorder.Text(Obj(Obj(d, "action"), "inquiry"), "tool") == "manual_lookup");
                log.Check(index >= 0, "[CS-09] the manual_lookup request is retained as an inquiry dispatch");
                if (index < 0) return;
                JsonElement inquiry = Obj(Obj(dispatches[index], "action"), "inquiry");
                string requestJson = CsEvidenceRecorder.Text(inquiry, "request_json");
                string resultJson = CsEvidenceRecorder.Text(inquiry, "result_json");
                int? chars = CsEvidenceRecorder.Int(inquiry, "result_chars");
                log.Check(requestJson != null && requestJson.Contains("fog_of_war")
                        && resultJson != null && chars == resultJson.Length
                        && CsEvidenceRecorder.Bool(inquiry, "ok") == true,
                    "[CS-09] the inquiry request, bounded result and character count are linked",
                    "chars=" + chars + " result=" + (resultJson == null ? "null" : "present"));

                string seat = CsEvidenceRecorder.Text(dispatches[index], "seat_id");
                string extracted = null;
                for (int j = index + 1; j < dispatches.Count; j++)
                {
                    if (CsEvidenceRecorder.Text(dispatches[j], "seat_id") != seat) continue;
                    extracted = BetweenMarkers(UserText(dispatches[j]),
                        PromptBuilder.InquiryResultBeginMarker, PromptBuilder.InquiryResultEndMarker);
                    break;
                }
                log.Check(extracted != null && extracted == resultJson,
                    "[CS-09] the result is exactly the block between the next prompt's inquiry markers");

                bool runLine = Find(ReadJsonLines(RunFile(result.Directory)),
                    r => CsEvidenceRecorder.Text(r, "inquiry_tool") == "manual_lookup").ValueKind
                    != JsonValueKind.Undefined;
                log.Check(runLine, "[CS-09] the production run line names inquiry_tool=manual_lookup");

                string sample = resultJson != null && resultJson.Length > 80
                    ? resultJson.Substring(0, 80) : resultJson;
                bool absent = true;
                foreach (JsonElement dispatch in dispatches)
                    if (CsEvidenceRecorder.Text(dispatch, "seat_id") == "seat-1" && sample != null)
                        absent &= !UserText(dispatch).Contains(sample);
                log.Check(absent, "[CS-09] the inquiry result does not reach the other seat's prompt");
            }
            finally { Cleanup(root); }
        }

        /// <summary>CS-10: the provider-written note reaches the same seat's season-2 prompt, the
        /// recall_memory result is linked, and neither crosses to the other seat.</summary>
        private static void NoteAndMemoryFollowUp(TestLog log)
        {
            CsScenarioResult result = RunSpec(Builtin("cs-memory-note-followup-v1", 4247), "cs10", out string root);
            try
            {
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(result.Directory, "dispatches.ndjson"));
                int noteIndex = IndexOf(dispatches,
                    d => CsEvidenceRecorder.Text(Obj(d, "action"), "kind") == "note"
                      && CsEvidenceRecorder.Bool(Obj(Obj(d, "action"), "note"), "saved") == true);
                log.Check(noteIndex >= 0, "[CS-10] the provider-written note is retained as saved");
                if (noteIndex < 0) return;
                string note = JsonStringField(CsEvidenceRecorder.Text(Obj(dispatches[noteIndex], "reply"), "raw_text"), "note");

                bool noteInSeason2 = false, noteIsolated = true;
                foreach (JsonElement dispatch in dispatches)
                {
                    string prompt = PromptText(dispatch);
                    if (CsEvidenceRecorder.Text(dispatch, "seat_id") == "seat-0"
                        && CsEvidenceRecorder.Int(dispatch, "season") == 2
                        && note != null && prompt.Contains(note)
                        && prompt.Contains(PromptBuilder.StrategicNoteBeginMarker)) noteInSeason2 = true;
                    if (CsEvidenceRecorder.Text(dispatch, "seat_id") == "seat-1"
                        && note != null && prompt.Contains(note)) noteIsolated = false;
                }
                log.Check(noteInSeason2,
                    "[CS-10] the note appears in the same seat's season-2 prompt inside the note markers");
                log.Check(noteIsolated, "[CS-10] the note never reaches the other seat's prompt");

                int recallIndex = IndexOf(dispatches,
                    d => CsEvidenceRecorder.Text(d, "seat_id") == "seat-0"
                      && CsEvidenceRecorder.Text(Obj(d, "action"), "kind") == "inquiry"
                      && CsEvidenceRecorder.Text(Obj(Obj(d, "action"), "inquiry"), "tool") == "recall_memory");
                JsonElement recall = recallIndex < 0 ? default : Obj(Obj(dispatches[recallIndex], "action"), "inquiry");
                string recallResult = CsEvidenceRecorder.Text(recall, "result_json");
                string extracted = null;
                for (int j = recallIndex + 1; recallIndex >= 0 && j < dispatches.Count; j++)
                {
                    if (CsEvidenceRecorder.Text(dispatches[j], "seat_id") != "seat-0") continue;
                    extracted = BetweenMarkers(UserText(dispatches[j]),
                        PromptBuilder.InquiryResultBeginMarker, PromptBuilder.InquiryResultEndMarker);
                    break;
                }
                log.Check(recallIndex >= 0 && recallResult != null && extracted == recallResult
                        && CsEvidenceRecorder.Int(dispatches[recallIndex], "season") == 2,
                    "[CS-10] the season-2 recall_memory result is linked through the next prompt's markers",
                    "recall_index=" + recallIndex + " result=" + (recallResult == null ? "null" : "present"));

                bool bothSeasons = false;
                foreach (JsonElement dispatch in dispatches)
                    bothSeasons |= CsEvidenceRecorder.Int(dispatch, "season") == 2;
                log.Check(result.SeasonsPlayed == 2 && bothSeasons,
                    "[CS-10] the planned follow-up season is actually played and retained");
                log.Check(result.PrivacyLeaks == 0,
                    "[CS-10] the private note and memory stay seat-isolated (privacy leaks=0)");
                log.Check(Directory.Exists(Path.Combine(result.Directory, "memory"))
                        && Directory.GetFiles(Path.Combine(result.Directory, "memory"), "*.jsonl").Length >= 2
                        && new FileInfo(Path.Combine(result.Directory, "notes.jsonl")).Length > 0,
                    "[CS-10] the per-seat memory store and the note store are written under the scenario");
            }
            finally { Cleanup(root); }
        }
    }
}
