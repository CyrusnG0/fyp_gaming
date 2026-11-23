using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LuanShi.Engine
{
    public static partial class ReplayLoader
    {
        private static void LoadBootstrapOrRuleFlags(
            string bootstrapPath,
            ReplaySummaryData summary,
            bool isLegacyDemo,
            out ReplayBootstrapData bootstrap,
            out bool? bootstrapVisible,
            out bool? bootstrapStateVisible,
            out bool? bootstrapAssaultRatio,
            out bool? bootstrapStateAssaultRatio,
            out string setupSource,
            out string mapSource)
        {
            bootstrap = null;
            bootstrapVisible = null;
            bootstrapStateVisible = null;
            bootstrapAssaultRatio = null;
            bootstrapStateAssaultRatio = null;
            setupSource = "legacy_demo";
            mapSource = "legacy_default_map";

            if (string.IsNullOrEmpty(bootstrapPath) || !File.Exists(bootstrapPath))
            {
                if (!isLegacyDemo)
                    throw new FileNotFoundException(
                        $"Missing replay bootstrap for run '{summary?.RunId ?? "unknown"}': {bootstrapPath ?? "(not found)"}");
                return;
            }

            if (!isLegacyDemo || BootstrapHasFullSnapshot(bootstrapPath))
            {
                bootstrap = ReplayBootstrapLoader.LoadFromFile(bootstrapPath, summary.RunId, summary.Seed);
                bootstrapVisible = bootstrap.HasVisibleMarchRoutesFeature;
                bootstrapStateVisible = bootstrap.StateRequireVisibleMarchRoutes;
                bootstrapAssaultRatio = bootstrap.HasAssaultTroopRatioFeature;
                bootstrapStateAssaultRatio = bootstrap.StateRequireAssaultTroopRatio;
                setupSource = "bootstrap";
                mapSource = "bootstrap";
                ValidateSummaryAgainstBootstrap(summary, bootstrapStateVisible ?? bootstrapVisible,
                    bootstrapStateAssaultRatio ?? bootstrapAssaultRatio);
                return;
            }

            LoadBootstrapRuleFlags(bootstrapPath, summary, out bootstrapVisible, out bootstrapStateVisible,
                out bootstrapAssaultRatio, out bootstrapStateAssaultRatio);
        }

        private static bool BootstrapHasFullSnapshot(string path)
        {
            string text = File.ReadAllText(path);
            return StrictJson.TryParseLenientNumbers(text, out JsonNode root, out _)
                && root is JsonObject obj
                && obj.TryGet("initial_map", out _);
        }

        private static void LoadBootstrapRuleFlags(string path, ReplaySummaryData summary,
            out bool? featuresVisible, out bool? stateVisible,
            out bool? featuresAssaultRatio, out bool? stateAssaultRatio)
        {
            featuresVisible = null;
            stateVisible = null;
            featuresAssaultRatio = null;
            stateAssaultRatio = null;
            string text = File.ReadAllText(path);
            if (!StrictJson.TryParseLenientNumbers(text, out JsonNode root, out string err) || !(root is JsonObject obj))
                throw new InvalidDataException("Invalid bootstrap JSON: " + err);
            if (obj.TryGet("features", out JsonNode fNode) && fNode is JsonObject fObj)
            {
                featuresVisible = GetNullableBool(fObj, "visible_march_routes");
                featuresAssaultRatio = GetNullableBool(fObj, "assault_troop_ratio");
            }
            if (obj.TryGet("initial_world", out JsonNode wNode) && wNode is JsonObject wObj)
            {
                stateVisible = GetNullableBool(wObj, "require_visible_march_routes");
                stateAssaultRatio = GetNullableBool(wObj, "require_assault_troop_ratio");
            }

            if (featuresVisible.HasValue && stateVisible.HasValue && featuresVisible.Value != stateVisible.Value)
                throw new InvalidDataException(
                    $"Bootstrap rule divergence: features.visible_march_routes={featuresVisible.Value} != initial_world.require_visible_march_routes={stateVisible.Value}.");
            if (featuresAssaultRatio.HasValue && stateAssaultRatio.HasValue
                && featuresAssaultRatio.Value != stateAssaultRatio.Value)
            {
                throw new InvalidDataException(
                    $"Bootstrap rule divergence: features.assault_troop_ratio={featuresAssaultRatio.Value} != initial_world.require_assault_troop_ratio={stateAssaultRatio.Value}.");
            }
            ValidateSummaryAgainstBootstrap(summary, stateVisible ?? featuresVisible,
                stateAssaultRatio ?? featuresAssaultRatio);
        }

        private static void ValidateSummaryAgainstBootstrap(ReplaySummaryData summary,
            bool? bootVisibleRule, bool? bootAssaultRule)
        {
            if (summary != null && summary.VisibleMarchRoutesEnforced.HasValue && bootVisibleRule.HasValue
                && bootVisibleRule.Value != summary.VisibleMarchRoutesEnforced.Value)
            {
                throw new InvalidDataException(
                    $"Summary/bootstrap rule divergence: summary.visible_march_routes_enforced={summary.VisibleMarchRoutesEnforced.Value} != bootstrap={bootVisibleRule.Value}.");
            }
            if (summary != null && summary.AssaultTroopRatioEnforced.HasValue && bootAssaultRule.HasValue
                && bootAssaultRule.Value != summary.AssaultTroopRatioEnforced.Value)
            {
                throw new InvalidDataException(
                    $"Summary/bootstrap rule divergence: summary.assault_troop_ratio_enforced={summary.AssaultTroopRatioEnforced.Value} != bootstrap={bootAssaultRule.Value}.");
            }
        }

        private static void ExtractRecordedEventCapFromContextReport(JsonObject obj, out int? included, out int? available)
        {
            included = null;
            available = null;
            if (obj == null || !obj.TryGet("context_report", out JsonNode crNode) || !(crNode is JsonObject crObj)) return;
            if (!crObj.TryGet("sections", out JsonNode secNode) || !(secNode is JsonArray secArr)) return;
            foreach (JsonNode item in secArr.Items)
            {
                if (!(item is JsonObject secObj)) continue;
                if (!string.Equals(GetString(secObj, "name"), "observation events (newest kept)", StringComparison.Ordinal))
                    continue;
                int? inc = GetNullableInt(secObj, "included");
                int? avail = GetNullableInt(secObj, "available");
                if (inc.HasValue && inc.Value >= 0) included = inc.Value;
                if (avail.HasValue && avail.Value >= 0) available = avail.Value;
                return;
            }
        }

        private static void ExtractRecordedEventCapFromPrompt(string userPrompt, out int? included, out int? available)
        {
            included = null;
            available = null;
            if (string.IsNullOrEmpty(userPrompt)) return;
            Match m = Regex.Match(userPrompt, @"-\s*observation events \(newest kept\):\s*(\d+)\s+of\s+(\d+)", RegexOptions.CultureInvariant);
            if (!m.Success) return;
            if (int.TryParse(m.Groups[1].Value, out int inc) && inc >= 0) included = inc;
            if (int.TryParse(m.Groups[2].Value, out int avail) && avail >= 0) available = avail;
        }
    }
}
