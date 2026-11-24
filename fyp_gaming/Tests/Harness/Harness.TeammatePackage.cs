using System;
using System.IO;
using System.Text.RegularExpressions;
using LuanShi.Engine;

// [TEAMMATE] The tracked teammate package: GETTING_STARTED.md and the example local configs. Offline checks
// only: the example loads, enables nothing, names no secret, and the guide states the facts a clean clone
// depends on. Skipped loudly when the harness runs outside a checkout that contains them.

internal static partial class Harness
{
    private static void TmCheck(bool ok, string name) => Check(ok, "[TEAMMATE] " + name);

    private static string TmRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "GETTING_STARTED.md"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void RunTeammatePackage()
    {
        Section("[TEAMMATE] getting-started guide and example local configs");
        string root = TmRepoRoot();
        if (root == null)
        {
            Console.WriteLine("  [TEAMMATE] skipped: GETTING_STARTED.md not found above the working directory");
            return;
        }
        string guide = File.ReadAllText(Path.Combine(root, "GETTING_STARTED.md"));
        string llmExample = Path.Combine(root, "fyp_gaming", "llm.example.json");
        string presetExample = Path.Combine(root, "fyp_gaming", "replay-presets.example.json");
        TmCheck(File.Exists(llmExample) && File.Exists(presetExample), "both example config files exist next to the Unity project");

        string llmText = File.ReadAllText(llmExample);
        LlmConfig cfg = LlmConfig.LoadFromFile(llmExample);
        TmCheck(!cfg.EnableAttack && !cfg.EnableResearch && !cfg.EnableDiplomacy && !cfg.EnableGarrisonConversion
                && !cfg.EnableManualLookup && !cfg.EnableActionReference && !cfg.EnableUnitTypes
                && string.IsNullOrEmpty(cfg.LoadWarning),
            "the example config loads cleanly with every optional feature off");
        TmCheck(llmText.Contains("\"api_key_env\"") && !Regex.IsMatch(llmText, "\"api_key\"[ \t]*:")
                && !Regex.IsMatch(llmText, "sk-[A-Za-z0-9]{8,}") && !llmText.Contains("Bearer"),
            "the example config names the key variable and carries no key");
        TmCheck(cfg.BaseUrl.StartsWith("https://", StringComparison.Ordinal) && cfg.MaxTotalRequests > 0 && cfg.MaxTotalRequests <= 20,
            "the example is https-only and bounds total requests");

        ReplayPresetSet presets = ReplayCatalog.LoadPresets(presetExample);
        TmCheck(presets.FromFile && presets.Presets.Count >= 1 && presets.Presets[0].Name == "Treaty & Attack"
                && presets.Presets[0].RunId == ReplayCatalog.TreatyAttackRunId,
            "the example preset file loads and names the treaty run");

        TmCheck(guide.Contains("6000.6.2f1") && guide.Contains("llm.local.json") && guide.Contains("LUANSHI_LLM_API_KEY")
                && guide.Contains("LUANSHI_RUNS_DIR") && guide.Contains("--live") && guide.Contains("LLM-only")
                && guide.Contains("Mixed human/LLM replay is not implemented"),
            "the guide states the Unity version, local config, key variable, replay folder, probe and replay limits");
        TmCheck(!Regex.IsMatch(guide, "sk-[A-Za-z0-9]{8,}") && !guide.Contains("C:\\Users") && !guide.Contains("micmi"),
            "the guide carries no key-shaped text and no machine-specific path");
        string ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        TmCheck(ignore.Contains("*.local.json") && ignore.Contains("/runs/") && ignore.Contains("/docs/"),
            ".gitignore keeps local configs, run files and local-only docs out of a commit");
    }
}
