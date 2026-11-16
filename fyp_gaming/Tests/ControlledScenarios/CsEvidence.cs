using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>Everything retained about one scenario run, including a stopped or failed one.</summary>
    public sealed class CsScenarioResult
    {
        public string ScenarioId { get; set; }
        public int Seed { get; set; }
        public string StudySeat { get; set; }
        public string Directory { get; set; }
        public string Status { get; set; }              // completed | completed_with_stops | stopped | failed
        public string Detail { get; set; }
        public string Limit { get; set; }               // named ceiling, or null
        public int SeasonsPlanned { get; set; }
        public int SeasonsPlayed { get; set; }
        public int Requests { get; set; }
        public int Dispatches { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public int UnknownUsageRequests { get; set; }
        public long ElapsedMs { get; set; }
        public string SettingsHash { get; set; }
        public string ProviderKind { get; set; }
        public string ProviderModel { get; set; }
        public bool ProviderLive { get; set; }
        public int EffectiveMaxRequests { get; set; }
        public int EffectiveWallSeconds { get; set; }
        public int RequestTimeoutMs { get; set; }
        public int MaxOutputTokens { get; set; }
        public string SetupHash { get; set; }
        public string ScriptSha256 { get; set; }
        public string StartWorldDigest { get; set; }
        public string FinalWorldDigest { get; set; }
        public string RunId { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public int Inquiries { get; set; }
        public int Notes { get; set; }
        public int PrivacyLeaks { get; set; }
        public int LateResponsesDiscarded { get; set; }
        public List<string> Files { get; } = new List<string>();
    }

    /// <summary>Inputs shared by every dispatch record of one scenario.</summary>
    public sealed class CsDispatchContext
    {
        public string RunId, ScenarioId, Commit, BaseCommit, StudySeat;
        public string ProviderKind, ProviderModel;
        public bool ProviderLive;
        public string UsageSource;
        public string ProviderBaseUrlDisplay;
        public List<string> ProviderEnvVarNames = new List<string>();
        public int ProviderMaxOutputTokens;
        public bool Dirty;
        public int Seed, ScenarioMaxRequests, ScenarioWallSeconds, RequestTimeoutMs, PromptLimit;
        public int LateResponsesDiscarded;
        public string SettingsHash, ConfigRedacted;
        public CsFeatureBlock Flags;
        public CsRunCeilings RunCeilings;
        public List<CsDispatchCapture> Captures = new List<CsDispatchCapture>();
        public List<CsRunLine> RunLines = new List<CsRunLine>();
        public List<CsExchangeLine> Exchanges = new List<CsExchangeLine>();
        public string RunFileName, ExchangeFileName;
    }

    /// <summary>
    /// Builds the linked dispatch records (design section 5). Every value comes from the exact
    /// request/reply captured by the transport, the production run line written by the runner, or
    /// the state snapshot. Unknown values are null and named in unknown_fields; they are never 0.
    /// An explanation is a model-written summary/reason field, never private reasoning.
    /// </summary>
    public static partial class CsEvidence
    {
        private static readonly Regex Secret = new Regex(
            @"(sk-[A-Za-z0-9_\-]{12,}|Bearer\s+[A-Za-z0-9._\-]{12,})", RegexOptions.CultureInvariant);

        public static void WritePrompts(string path, CsDispatchContext context)
        {
            var sb = new StringBuilder();
            foreach (CsDispatchCapture cap in context.Captures)
            {
                var line = new Dictionary<string, object>
                {
                    { "index", cap.Index }, { "seat", cap.Seat },
                    { "scenario_id", context.ScenarioId },
                    { "system_sha256", CsWorld.Sha256(cap.System) },
                    { "user_sha256", CsWorld.Sha256(cap.User) },
                    { "system", Redact(cap.System) }, { "user", Redact(cap.User) },
                    { "script", cap.ScriptNote }, { "reply", Redact(cap.ReplyText) },
                };
                sb.Append(JsonSerializer.Serialize(line, CsJson.Compact)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static void WriteDispatches(string path, CsDispatchContext context)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < context.Captures.Count; i++)
            {
                Dictionary<string, object> record = Build(context, i);
                sb.Append(JsonSerializer.Serialize(record, CsJson.Compact)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static Dictionary<string, object> Build(CsDispatchContext context, int index)
        {
            CsDispatchCapture cap = context.Captures[index];
            CsRunLine run = index < context.RunLines.Count ? context.RunLines[index] : null;
            bool linked = run != null && string.Equals(run.SeatId, cap.Seat, StringComparison.Ordinal);
            if (run != null && !linked) run = null;

            var unknown = new List<string>();
            string seat = linked ? run.SeatId : cap.Seat;
            int? turnToken = linked ? run.TurnToken : null;
            int? season = linked ? run.Season : null;
            int? requestIndex = linked ? run.RequestIndex : null;
            if (turnToken == null) unknown.Add("turn_token");
            if (season == null) unknown.Add("season");
            if (requestIndex == null) unknown.Add("request_index");

            string commandJson = linked ? CsEvidenceRecorder.Text(run.Root, "command_json") : null;
            bool? resultOk = linked ? CsEvidenceRecorder.Bool(run.Root, "result_ok") : null;
            string resultError = linked ? CsEvidenceRecorder.Text(run.Root, "result_error") : null;
            string inquiryTool = linked ? CsEvidenceRecorder.Text(run.Root, "inquiry_tool") : null;
            bool parseOk = linked && CsEvidenceRecorder.Bool(run.Root, "parse_ok") == true;
            string raw = Redact(cap.ReplyText);

            string kind = KindOf(cap, linked, parseOk, commandJson, inquiryTool, raw);
            string inquiryResult = kind == "inquiry" ? NextInquiryResult(context.Captures, index, cap.Seat) : null;
            bool? inquiryOk = null, noteSaved = null;
            string inquiryError = null;
            int? inquiryResultChars = linked
                ? CsEvidenceRecorder.Int(run.Root, "inquiry_result_chars") : null;
            if (kind == "inquiry")
            {
                if (inquiryResult != null)
                {
                    inquiryResultChars = inquiryResult.Length;
                    ParseInquiry(inquiryResult, out inquiryOk, out inquiryError);
                }
                else unknown.Add("action.inquiry.result_json");
            }
            if (kind == "note")
            {
                noteSaved = linked && string.IsNullOrEmpty(resultError) && parseOk;
                if (!noteSaved.HasValue) unknown.Add("action.note.saved");
            }

            // The saved schema only names action/inquiry/note; provider_error and stop detail live in status.
            string actionKind = kind == "inquiry" || kind == "note" ? kind : "action";
            var action = new Dictionary<string, object>
            {
                { "kind", actionKind },
                { "command_json", commandJson },
                { "accepted", AcceptedOf(kind, resultOk, inquiryOk, noteSaved) },
                { "result_error", resultError },
                { "inquiry", kind == "inquiry" ? InquiryObject(inquiryTool, raw, inquiryResult,
                    inquiryResultChars, inquiryOk, inquiryError) : null },
                { "note", kind == "note" ? NoteObject(noteSaved, raw, unknown) : null },
            };

            var reply = ReplyObject(context, cap, run, linked, unknown);
            var record = new Dictionary<string, object>
            {
                { "dispatch_state", cap.State },
                { "refusal_reason", cap.RefusalReason },
                { "schema", "luanshi-controlled-dispatch/1" },
                { "run_id", context.RunId },
                { "scenario_id", context.ScenarioId },
                { "dispatch_id", context.RunId + ":" + cap.Index.ToString(CultureInfo.InvariantCulture) },
                { "commit", context.Commit },
                { "base_commit", context.BaseCommit },
                { "dirty", context.Dirty },
                { "seed", context.Seed },
                { "seat_id", seat },
                { "turn_token", turnToken },
                { "season", season },
                { "request_index", requestIndex },
                { "seat_request_index", SeatRequestIndex(context.Captures, index, cap.Seat) },
                { "provider", new Dictionary<string, object>
                    {
                        { "kind", context.ProviderKind }, { "model", context.ProviderModel },
                        { "live", context.ProviderLive }, { "usage_source", context.UsageSource },
                        { "base_url_display", context.ProviderBaseUrlDisplay },
                        { "env_var_names", context.ProviderEnvVarNames },
                        { "max_output_tokens", context.ProviderMaxOutputTokens },
                        { "request_timeout_ms", context.RequestTimeoutMs },
                        { "max_requests", context.ScenarioMaxRequests },
                        { "max_wall_seconds", context.ScenarioWallSeconds },
                        { "effective_max_requests", context.ScenarioMaxRequests },
                        { "effective_max_wall_seconds", context.ScenarioWallSeconds },
                        { "commit", context.Commit }, { "dirty", context.Dirty },
                        { "seed", context.Seed }, { "scenario_id", context.ScenarioId },
                        { "settings_hash", context.SettingsHash },
                        { "limits_note", CsLimits.LimitsNote },
                    } },
                { "settings", SettingsObject(context) },
                { "prompt", PromptObject(context, cap, run, linked) },
                { "reply", reply },
                { "explanation", ExplanationObject(raw) },
                { "action", action },
                { "state", StateObject(cap, run, unknown) },
                { "links", LinksObject(context, index, run, linked, unknown) },
                { "status", StatusOf(cap, linked, kind) },
                { "unknown_fields", unknown },
            };
            return record;
        }

        private static string KindOf(CsDispatchCapture cap, bool linked, bool parseOk,
            string commandJson, string inquiryTool, string raw)
        {
            if (cap.State == "refused_before_dispatch") return "refused";
            // An unlinked dispatch never produced a run line: it timed out, was cancelled, was
            // abandoned, or was still in flight at seal. The exact reason lives in dispatch_state.
            if (!linked) return "in_flight_stopped";
            if (cap.State == "deadline_abandoned" || cap.State == "timed_out"
                || cap.State == "cancelled" || cap.State == "transport_exception") return "provider_error";
            if (cap.Ok != true) return "provider_error";
            if (!string.IsNullOrEmpty(inquiryTool)) return "inquiry";
            if (HasProperty(raw, "note")) return "note";
            if (!parseOk) return "invalid";
            if (!string.IsNullOrEmpty(commandJson)) return "action";
            if (HasProperty(raw, "type") && ReadString(raw, "type") == "end_turn") return "end_turn";
            return "unknown";
        }

        private static bool? AcceptedOf(string kind, bool? resultOk, bool? inquiryOk, bool? noteSaved)
        {
            switch (kind)
            {
                case "inquiry": return inquiryOk;
                case "note": return noteSaved;
                case "refused":
                case "in_flight_stopped": return null;
                case "provider_error":
                case "invalid": return false;
                default: return resultOk;
            }
        }

        private static string StatusOf(CsDispatchCapture cap, bool linked, string kind)
        {
            if (cap.State == "refused_before_dispatch") return "refused";
            if (!linked) return "in_flight_stopped";
            if (cap.Ok != true) return "provider_error";
            if (kind == "invalid") return "invalid_response";
            if (kind == "unknown") return "unclassified";
            return "complete";
        }
    }
}
