using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // Section builders for CsEvidence.Build, kept separate so each file stays under the line cap.
    public static partial class CsEvidence
    {
        private static Dictionary<string, object> InquiryObject(string tool, string requestJson,
            string resultJson, int? resultChars, bool? ok, string errorCode)
        {
            return new Dictionary<string, object>
            {
                { "tool", tool },
                { "request_json", requestJson },
                { "result_json", resultJson },
                { "result_chars", resultChars },
                { "ok", ok },
                { "error_code", errorCode },
            };
        }

        private static Dictionary<string, object> NoteObject(bool? saved, string raw, List<string> unknown)
        {
            int? chars = null;
            string note = ReadString(raw, "note");
            if (note != null) chars = note.Length;
            else unknown.Add("action.note.chars");
            return new Dictionary<string, object> { { "saved", saved }, { "chars", chars } };
        }

        private static Dictionary<string, object> ReplyObject(CsDispatchContext context,
            CsDispatchCapture cap, CsRunLine run, bool linked, List<string> unknown)
        {
            // A response exists only when the inner transport returned one. Refused, abandoned,
            // cancelled and sealed requests keep every response field null (never zero/empty).
            bool responded = cap.State == "completed" || cap.State == "failed";
            string finish = responded
                ? (linked ? CsEvidenceRecorder.Text(run.Root, "finish_reason") : cap.FinishReason) : null;
            int? input = responded
                ? (linked ? CsEvidenceRecorder.Int(run.Root, "input_tokens") : cap.InputTokens) : null;
            int? output = responded
                ? (linked ? CsEvidenceRecorder.Int(run.Root, "output_tokens") : cap.OutputTokens) : null;
            int? reasoning = responded
                ? (linked ? CsEvidenceRecorder.Int(run.Root, "reasoning_tokens") : cap.ReasoningTokens) : null;
            int? total = responded
                ? (linked ? CsEvidenceRecorder.Int(run.Root, "total_tokens") : cap.TotalTokens) : null;
            int? http = responded
                ? (linked ? CsEvidenceRecorder.Int(run.Root, "http_status") : cap.HttpStatus) : null;
            string requestId = responded
                ? (linked ? CsEvidenceRecorder.Text(run.Root, "provider_request_id") : cap.ProviderRequestId)
                : null;
            int? latency = responded ? cap.LatencyMs : (int?)null;
            long? elapsed = responded ? cap.ElapsedMs : (long?)null;
            // The audit reads unknown names from inside reply; the flat list stays for run summaries.
            // after_state is a state field, named by StateObject only when it is genuinely absent.
            var replyUnknown = new List<string>();
            if (!responded) { unknown.Add("reply.response_text"); replyUnknown.Add("response_text"); }
            if (finish == null) { unknown.Add("reply.finish_reason"); replyUnknown.Add("finish_reason"); }
            if (input == null) { unknown.Add("reply.input_tokens"); replyUnknown.Add("input_tokens"); }
            if (output == null) { unknown.Add("reply.output_tokens"); replyUnknown.Add("output_tokens"); }
            if (reasoning == null) { unknown.Add("reply.reasoning_tokens"); replyUnknown.Add("reasoning_tokens"); }
            if (total == null) { unknown.Add("reply.total_tokens"); replyUnknown.Add("total_tokens"); }
            if (http == null) { unknown.Add("reply.http_status"); replyUnknown.Add("http_status"); }
            if (requestId == null) { unknown.Add("reply.provider_request_id"); replyUnknown.Add("provider_request_id"); }
            if (latency == null) { unknown.Add("reply.latency_ms"); replyUnknown.Add("latency_ms"); }
            if (elapsed == null) { unknown.Add("reply.elapsed_ms"); replyUnknown.Add("elapsed_ms"); }
            string source = context.ProviderLive ? "provider"
                : string.Equals(context.ProviderKind, "fake", StringComparison.Ordinal)
                    ? "fake_scripted" : "fixture_" + (context.ProviderKind ?? "unknown");
            return new Dictionary<string, object>
            {
                { "raw_text", responded ? Redact(cap.ReplyText) : null },
                { "source", source },
                { "finish_reason", finish },
                { "input_tokens", input },
                { "output_tokens", output },
                { "reasoning_tokens", reasoning },
                { "total_tokens", total },
                { "http_status", http },
                { "provider_request_id", requestId },
                { "latency_ms", latency },
                { "elapsed_ms", elapsed },
                { "unknown_fields", replyUnknown },
            };
        }

        private static Dictionary<string, object> ExplanationObject(string raw)
        {
            string text = ReadString(raw, "summary") ?? ReadString(raw, "reason");
            // The audit requires non-null strings; an empty text with a named source stays honest.
            return new Dictionary<string, object>
            {
                { "text", text ?? "" },
                { "source", text == null ? "not_provided" : "model_written_summary" },
                { "reasoning_text_captured", false },
            };
        }

        private static Dictionary<string, object> SettingsObject(CsDispatchContext context)
        {
            return new Dictionary<string, object>
            {
                { "settings_hash", context.SettingsHash },
                { "config_redacted", context.ConfigRedacted },
                { "flags", new Dictionary<string, object>
                    {
                        { "attack", context.Flags?.Attack ?? false },
                        { "research", context.Flags?.Research ?? false },
                        { "manual_lookup", context.Flags?.ManualLookup ?? false },
                        { "deliberation", context.Flags?.Deliberation ?? false },
                    } },
                { "ceilings", new Dictionary<string, object>
                    {
                        { "max_requests", context.ScenarioMaxRequests },
                        { "max_wall_seconds", context.ScenarioWallSeconds },
                        { "request_timeout_ms", context.RequestTimeoutMs },
                        { "max_run_requests", context.RunCeilings?.MaxRunRequests },
                        { "max_run_wall_seconds", context.RunCeilings?.MaxRunWallSeconds },
                    } },
            };
        }

        private static Dictionary<string, object> PromptObject(CsDispatchContext context,
            CsDispatchCapture cap, CsRunLine run, bool linked)
        {
            CsWorldSnapshot before = cap.Before;
            var unknown = new List<string>();
            if (before == null)
            {
                unknown.Add("prompt.visible_options");
                unknown.Add("truncation.movement_truncated");
                unknown.Add("truncation.attack_truncated");
            }
            else unknown.AddRange(before.UnknownFields);
            var sections = new List<string>();
            string all = (cap.System ?? "") + "\n" + (cap.User ?? "");
            if (all.Contains("CANDIDATE OPTIONS")) sections.Add("CANDIDATE OPTIONS");
            if (all.Contains("MOVE OPTIONS")) sections.Add("MOVE OPTIONS");
            if (all.Contains("ATTACK")) sections.Add("ATTACK");
            if (all.Contains("INQUIRY")) sections.Add("INQUIRY");
            if (all.Contains("DELIBERATION")) sections.Add("DELIBERATION");
            if (all.Contains(PromptBuilder.StrategicNoteBeginMarker)) sections.Add("STRATEGIC NOTE");
            int chars = (cap.System?.Length ?? 0) + (cap.User?.Length ?? 0);
            return new Dictionary<string, object>
            {
                { "system", Redact(cap.System) },
                { "user", Redact(cap.User) },
                { "system_sha256", CsWorld.Sha256(cap.System) },
                { "user_sha256", CsWorld.Sha256(cap.User) },
                { "prompt_version", linked ? CsEvidenceRecorder.Text(run.Root, "prompt_version") : null },
                { "schema_hash", linked ? CsEvidenceRecorder.Text(run.Root, "schema_hash") : null },
                { "visible_options", new Dictionary<string, object>
                    {
                        { "catalogue", before?.Catalogue },
                        { "catalogue_text", before?.CatalogueText },
                        { "movement_json", before?.MovementJson },
                        { "attacks_json", before?.AttackJson },
                    } },
                { "truncation", new Dictionary<string, object>
                    {
                        { "prompt_chars", chars },
                        { "prompt_limit", context.PromptLimit },
                        { "movement_truncated", before?.MovementTruncated },
                        { "attack_truncated", before?.AttackTruncated },
                        { "spatial", null },
                        { "sections_present", sections },
                        { "unknown_fields", unknown },
                    } },
            };
        }

        private static Dictionary<string, object> StateObject(CsDispatchCapture cap, CsRunLine run,
            List<string> unknown)
        {
            // The run line is written after the runner handled the reply (including submitting an
            // accepted action), so it is the authoritative after-state. The wrapper's post-response
            // snapshot is the fallback when no run line exists.
            CsWorldSnapshot after = run?.After ?? cap.After;
            if (cap.Before == null) unknown.Add("state.before");
            if (after == null) { unknown.Add("state.after"); unknown.Add("after_state"); }
            return new Dictionary<string, object>
            {
                { "before", Snapshot(cap.Before) },
                { "after", Snapshot(after) },
            };
        }

        private static Dictionary<string, object> Snapshot(CsWorldSnapshot snap)
        {
            if (snap == null)
                return new Dictionary<string, object>
                {
                    { "observation_sha256", null }, { "observation_json", null },
                    { "canonical_world_digest", null },
                };
            return new Dictionary<string, object>
            {
                { "observation_sha256", snap.ObservationSha256 },
                { "observation_json", Redact(snap.ObservationJson) },
                { "canonical_world_digest", snap.WorldDigest },
            };
        }

        private static Dictionary<string, object> LinksObject(CsDispatchContext context, int index,
            CsRunLine run, bool linked, List<string> unknown)
        {
            string runLink = linked ? context.RunFileName + ":" + run.Line : null;
            string requestLink = null, resultLink = null;
            if (linked)
            {
                requestLink = FindExchange(context.Exchanges, "request", run) is CsExchangeLine rq
                    ? context.ExchangeFileName + ":" + rq.Line : null;
                resultLink = FindExchange(context.Exchanges, "result", run) is CsExchangeLine rs
                    ? context.ExchangeFileName + ":" + rs.Line : null;
            }
            if (runLink == null) unknown.Add("links.run_record");
            if (requestLink == null) unknown.Add("links.exchange_request");
            if (resultLink == null) unknown.Add("links.exchange_result");
            return new Dictionary<string, object>
            {
                { "run_record", runLink },
                { "exchange_request", requestLink },
                { "exchange_result", resultLink },
                { "prompt_capture", "prompts.ndjson:" + (index + 1).ToString(CultureInfo.InvariantCulture) },
            };
        }

        private static CsExchangeLine FindExchange(List<CsExchangeLine> exchanges, string eventName,
            CsRunLine run)
        {
            foreach (CsExchangeLine ex in exchanges)
            {
                if (ex.Event != eventName) continue;
                if (ex.TurnToken != run.TurnToken || ex.RequestIndex != run.RequestIndex) continue;
                if (ex.SeatId != null && run.SeatId != null && ex.SeatId != run.SeatId) continue;
                return ex;
            }
            return null;
        }

        private static int SeatRequestIndex(List<CsDispatchCapture> captures, int index, string seat)
        {
            int n = 0;
            for (int i = 0; i <= index; i++)
                if (captures[i].Seat == seat) n++;
            return n;
        }

        private static string NextInquiryResult(List<CsDispatchCapture> captures, int index, string seat)
        {
            for (int j = index + 1; j < captures.Count; j++)
            {
                if (captures[j].Seat != seat) continue;
                string user = captures[j].User ?? "";
                int begin = user.IndexOf(PromptBuilder.InquiryResultBeginMarker, StringComparison.Ordinal);
                if (begin < 0) return null;
                begin += PromptBuilder.InquiryResultBeginMarker.Length;
                int end = user.IndexOf(PromptBuilder.InquiryResultEndMarker, begin, StringComparison.Ordinal);
                if (end < 0) return null;
                return user.Substring(begin, end - begin).Trim();
            }
            return null;
        }

        private static void ParseInquiry(string json, out bool? ok, out string errorCode)
        {
            ok = null;
            errorCode = null;
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return;
                    if (root.TryGetProperty("ok", out JsonElement okElement))
                    {
                        if (okElement.ValueKind == JsonValueKind.True) ok = true;
                        else if (okElement.ValueKind == JsonValueKind.False) ok = false;
                    }
                    if (root.TryGetProperty("error_code", out JsonElement error)
                        && error.ValueKind == JsonValueKind.String) errorCode = error.GetString();
                }
            }
            catch (JsonException) { ok = null; }
        }

        private static bool HasProperty(string raw, string name)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(raw))
                    return doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty(name, out _);
            }
            catch (JsonException) { return false; }
        }

        private static string ReadString(string raw, string name)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(raw))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                    return doc.RootElement.TryGetProperty(name, out JsonElement value)
                        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                }
            }
            catch (JsonException) { return null; }
        }

        private static string Redact(string value)
            => string.IsNullOrEmpty(value) ? value : Secret.Replace(value, "<redacted>");
    }
}
