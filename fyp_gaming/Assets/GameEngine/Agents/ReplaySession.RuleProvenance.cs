using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public sealed partial class ReplaySession
    {
        private readonly struct VisibleMarchRuleResolution
        {
            public readonly bool SharedEngineRule;
            public readonly bool LlmHostVisibleRoutes;
            public readonly bool? EstablishedSharedRule;
            public readonly VisibleMarchRuleProvenance Provenance;
            public readonly bool RuleEstablished;
            public readonly string Note;

            public VisibleMarchRuleResolution(bool sharedEngineRule, bool llmHostVisibleRoutes,
                bool? establishedSharedRule, VisibleMarchRuleProvenance provenance,
                bool ruleEstablished, string note)
            {
                SharedEngineRule = sharedEngineRule;
                LlmHostVisibleRoutes = llmHostVisibleRoutes;
                EstablishedSharedRule = establishedSharedRule;
                Provenance = provenance;
                RuleEstablished = ruleEstablished;
                Note = note;
            }
        }

        /// <summary>
        /// Returns true only when recorded bootstrap evidence explicitly establishes that the shared
        /// engine rule (<see cref="GameState.RequireVisibleMarchRoutes"/>) was enabled for all controllers.
        /// Historical spatial runs without <c>initial_world.require_visible_march_routes</c> return false
        /// here and instead enforce visible routes at the replay LLM host only.
        /// </summary>
        public static bool ResolveVisibleMarchRoutesRule(ReplayBundle bundle)
            => ResolveVisibleMarchRuleInfo(bundle).SharedEngineRule;

        private static VisibleMarchRuleResolution ResolveVisibleMarchRuleInfo(ReplayBundle bundle)
        {
            if (bundle == null)
                return new VisibleMarchRuleResolution(false, false, false,
                    VisibleMarchRuleProvenance.LegacyUnenforced, true, null);

            bool? stateFlag = bundle.BootstrapStateRequireVisibleMarchRoutes;
            bool? bootFlag = bundle.BootstrapVisibleMarchRoutes;
            bool? sumFlag = bundle.Summary != null ? bundle.Summary.VisibleMarchRoutesEnforced : null;

            bool? explicitRule = null;
            void CheckSource(bool? next, string name)
            {
                if (!next.HasValue) return;
                if (explicitRule.HasValue && explicitRule.Value != next.Value)
                    throw new InvalidOperationException(
                        $"Recorded visible_march_routes rule diverges at {name}: expected {explicitRule.Value}, got {next.Value}.");
                explicitRule = next.Value;
            }
            CheckSource(stateFlag, "bootstrap.initial_world");
            CheckSource(bootFlag, "bootstrap.features");
            CheckSource(sumFlag, "summary.visible_march_routes_enforced");

            bool spatialActive = false;
            if (bundle.Summary != null)
            {
                string lvl = bundle.Summary.SpatialContextLevel;
                if (string.Equals(lvl, "standard", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lvl, "rich", StringComparison.OrdinalIgnoreCase))
                    spatialActive = true;
            }
            bool hasNonLlmAction = false;
            if (bundle.Requests != null)
            {
                for (int i = 0; i < bundle.Requests.Count; i++)
                {
                    ReplayRequestRecord req = bundle.Requests[i];
                    if (req == null) continue;
                    if (!spatialActive && PromptBuilder.IsSpatialPromptVersion(req.PromptVersion))
                        spatialActive = true;
                    if (IsNonLlmRecord(req))
                        hasNonLlmAction = true;
                }
            }

            if (explicitRule.HasValue && !explicitRule.Value && spatialActive)
                throw new InvalidOperationException(
                    "Recorded visible_march_routes=false diverges from recorded spatial context.");

            if (stateFlag.HasValue)
                return new VisibleMarchRuleResolution(
                    stateFlag.Value, stateFlag.Value, stateFlag.Value,
                    VisibleMarchRuleProvenance.SharedEngineEstablished, true, null);

            if (spatialActive || bootFlag == true || sumFlag == true)
            {
                if (hasNonLlmAction)
                    return new VisibleMarchRuleResolution(
                        false, true, null,
                        VisibleMarchRuleProvenance.Unestablished, false,
                        "Recorded evidence shows LLM spatial/visible-route flags without initial_world.require_visible_march_routes; the shared-engine rule governing recorded non-LLM (human) actions cannot be established.");

                return new VisibleMarchRuleResolution(
                    false, true, null,
                    VisibleMarchRuleProvenance.LlmHostOnlyHistorical, true,
                    "Historical spatial run: LLM-host visible_march_routes=true established for LLM actions, but bootstrap lacks initial_world.require_visible_march_routes so shared-engine rule for human actions is unestablished.");
            }

            if (hasNonLlmAction)
                return new VisibleMarchRuleResolution(
                    false, false, null,
                    VisibleMarchRuleProvenance.Unestablished, false,
                    "Recorded evidence lacks initial_world.require_visible_march_routes; the shared-engine movement rule governing recorded non-LLM (human) actions cannot be established.");

            return new VisibleMarchRuleResolution(
                false, false, false,
                VisibleMarchRuleProvenance.LegacyUnenforced, true, null);
        }

        private static bool IsNonLlmRecord(ReplayRequestRecord req)
            => req != null
               && !string.IsNullOrEmpty(req.ControllerType)
               && !string.Equals(req.ControllerType, ControllerType.Llm, StringComparison.Ordinal);

        private static string EffectiveControllerType(ReplayRequestRecord req)
            => req != null && !string.IsNullOrEmpty(req.ControllerType)
                ? req.ControllerType
                : ControllerType.Llm;

        private static void ParseReplayRecordAction(ReplayRequestRecord req, bool diplomacy, bool isLlmController,
            out ActionCommand cmd, out bool isEndTurn, out bool replayedParseOk, out string parseError,
            out string actType, out string actSummary)
        {
            cmd = null;
            isEndTurn = false;
            replayedParseOk = false;
            parseError = null;

            if (!isLlmController && !string.IsNullOrEmpty(req.CommandJson))
            {
                if (!req.ParseOk)
                {
                    parseError = req.ResultError ?? "recorded invalid response";
                }
                else if (TryParseCommandJson(req.CommandJson, out ActionCommand parsedCmd, out string cmdErr))
                {
                    replayedParseOk = true;
                    isEndTurn = string.Equals(parsedCmd.Type, "end_turn", StringComparison.Ordinal);
                    cmd = parsedCmd;
                }
                else
                {
                    parseError = cmdErr;
                }

                actType = isEndTurn ? "end_turn" : (replayedParseOk && cmd != null ? cmd.Type : "unknown");
                actSummary = isEndTurn
                    ? "Voluntary end of turn"
                    : (replayedParseOk && cmd != null
                        ? $"{cmd.Type} {cmd.TargetId ?? ""}".Trim()
                        : (!string.IsNullOrEmpty(req.CommandJson) ? req.CommandJson : "unknown"));
                return;
            }

            // Never execute a reply the original runner rejected as invalid, even if a later
            // parser accepts it. The recorded outcome, not today's parser, defines replay.
            ParseResult parsed = !req.ParseOk
                ? ParseResult.Failure(req.ResultError ?? "recorded invalid response")
                : LlmActionParser.Parse(req.RawModelText, false, false, diplomacy,
                    PromptBuilder.IsGarrisonConversionPromptVersion(req.PromptVersion));
            isEndTurn = parsed.Ok && parsed.Action != null && parsed.Action.EndTurn;
            replayedParseOk = parsed.Ok && parsed.Action != null;
            parseError = replayedParseOk ? null : (parsed.Error ?? "parse_failure");
            if (replayedParseOk && !isEndTurn)
                cmd = LlmActionParser.ToCommand(parsed.Action);

            actType = isEndTurn ? "end_turn" : (replayedParseOk ? parsed.Action.Type : "unknown");
            actSummary = isEndTurn
                ? "Voluntary end of turn"
                : (replayedParseOk
                    ? $"{parsed.Action.Type} {parsed.Action.TargetId ?? ""}".Trim()
                    : (!string.IsNullOrEmpty(req.RawModelText) ? req.RawModelText : "unknown"));
        }

        private static bool TryParseCommandJson(string json, out ActionCommand cmd, out string error)
        {
            cmd = null;
            error = null;
            if (!StrictJson.TryParseLenientNumbers(json, out JsonNode root, out string err) || !(root is JsonObject obj))
            {
                error = err ?? "invalid command JSON";
                return false;
            }
            string type = obj.TryGet("type", out JsonNode tNode) && tNode is JsonString ts ? ts.Value : null;
            if (string.IsNullOrEmpty(type))
            {
                error = "command JSON missing type";
                return false;
            }
            Dictionary<string, string> args = null;
            if (obj.TryGet("args", out JsonNode aNode) && aNode is JsonObject aObj)
            {
                args = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in aObj.Items)
                    if (kv.Value is JsonString vs) args[kv.Key] = vs.Value;
            }
            cmd = new ActionCommand
            {
                Type = type,
                TargetId = obj.TryGet("target_id", out JsonNode tgNode) && tgNode is JsonString tgs ? tgs.Value : null,
                ParamA = obj.TryGet("param_a", out JsonNode paNode) && paNode is JsonInt pai ? (int)pai.Value : 0,
                ParamB = obj.TryGet("param_b", out JsonNode pbNode) && pbNode is JsonInt pbi ? (int)pbi.Value : 0,
                Args = args,
                Text = obj.TryGet("text", out JsonNode txNode) && txNode is JsonString txs ? txs.Value : null,
                Reason = obj.TryGet("reason", out JsonNode rsNode) && rsNode is JsonString rss ? rss.Value : null,
            };
            return true;
        }
    }
}
