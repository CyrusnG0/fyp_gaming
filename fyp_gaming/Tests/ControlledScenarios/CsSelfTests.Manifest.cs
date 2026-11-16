using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static bool Has(IEnumerable<string> errors, string fragment)
        {
            foreach (string error in errors)
                if (error.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// CS-11: credentials, unset ceilings, a live manifest without an output-token ceiling,
        /// unknown ids and an output directory outside runs/controlled-scenarios/ are all refused
        /// before anything runs.
        /// </summary>
        private static void ManifestRefusesBadInput(TestLog log)
        {
            string json = DefaultManifestJson();
            CsManifest parsed = CsManifest.Parse(json, out List<string> errors);
            log.Check(parsed != null && errors.Count == 0,
                "[CS-11] the tracked default manifest parses and validates", string.Join("; ", errors));

            const string keyName = "not-a-real-key-value";
            JsonNode node = JsonNode.Parse(json);
            node["api_key"] = keyName;
            CsManifest manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "credential-like key") && !Has(errors, keyName),
                "[CS-11] a credential-like key is refused and its value is never echoed",
                string.Join("; ", errors));

            string canary = "sk-" + new string('A', 24);
            node = JsonNode.Parse(json);
            node["provider"]["model"] = canary;
            manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "credential-like value") && !Has(errors, canary),
                "[CS-11] a key-shaped value is refused and is not echoed", string.Join("; ", errors));

            node = JsonNode.Parse(json);
            node.AsObject().Remove("run_ceilings");
            manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "run_ceilings are required"),
                "[CS-11] a manifest without ceilings is refused", string.Join("; ", errors));

            node = JsonNode.Parse(json);
            node["run_ceilings"]["max_scenarios"] = null;
            node["run_ceilings"]["request_timeout_ms"] = 0;
            manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "max_scenarios must be a positive integer")
                    && Has(errors, "request_timeout_ms must be a positive integer"),
                "[CS-11] a missing or non-positive ceiling is refused, not treated as unbounded",
                string.Join("; ", errors));

            node = JsonNode.Parse(json);
            node["provider"]["kind"] = "openai-compatible";
            manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "max_output_tokens")
                    && Has(errors, "live provider"),
                "[CS-11] a live provider manifest without an output-token ceiling is refused",
                string.Join("; ", errors));

            node = JsonNode.Parse(json);
            node["scenarios"][0]["scenario_id"] = "cs-nowhere-v9";
            manifest = CsManifest.Parse(node.ToJsonString(), out errors);
            log.Check(manifest == null && Has(errors, "unknown scenario"),
                "[CS-11] an unknown scenario id is refused", string.Join("; ", errors));

            string repoRoot = CsPaths.RepoRoot();
            string allowed = CsPaths.AllowedRoot(repoRoot);
            bool outsideRefused = false;
            try { CsPaths.ResolveOut(Path.GetTempPath(), repoRoot); }
            catch (InvalidOperationException) { outsideRefused = true; }
            log.Check(outsideRefused,
                "[CS-11] --out outside runs/controlled-scenarios is refused");

            bool insideAccepted = false;
            try
            {
                string resolved = CsPaths.ResolveOut(Path.Combine(allowed, "selftest-out"), repoRoot);
                insideAccepted = CsPaths.IsUnder(resolved, allowed);
            }
            catch (InvalidOperationException) { insideAccepted = false; }
            log.Check(insideAccepted,
                "[CS-11] an output directory inside runs/controlled-scenarios is accepted");
        }
    }
}
