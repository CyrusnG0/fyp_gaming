using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LuanShi.Engine;

// [UNITS] (continued) the complete dispatched prompt never contradicts itself, and its unit-counter
// statement matches what the engine computes for both combatants.

internal static partial class Harness
{
    private static readonly string[] UtAllTypes = { "infantry", "archer", "cavalry", "siege" };

    private static LlmConfig UtCombo(bool unitTypes, bool attack, bool conversion, bool reference, bool research,
        bool diplomacy, bool lookup)
    {
        LlmConfig c = UtConfig(unitTypes, attack, conversion);
        c.EnableActionReference = reference;
        c.EnableResearch = research;
        c.EnableDiplomacy = diplomacy;
        c.EnableManualLookup = lookup;
        return c;
    }

    // Statements that say recruitment is infantry-only. None may appear in an enabled prompt.
    private static readonly string[] UtInfantryOnly =
    {
        "args.unit_type must be \"infantry\"", "with unit_type = \"infantry\"", "args.unit_type \"infantry\"):",
        "infantry-only", "infantry-only rule", "1,000 infantry eat",
    };

    private const string UtOrderOld = "recruit (args.unit_type must be \"infantry\")";
    private const string UtOrderNew = "recruit (args.unit_type: infantry, archer, cavalry or siege; see UNIT TYPES)";
    private const string UtArgsOld = "only recruit accepts it, with unit_type = \"infantry\".";
    private const string UtArgsNew = "only recruit accepts it, with unit_type as listed under UNIT TYPES.";

    private static void UnitParityPromptConsistency()
    {
        var combos = new (bool a, bool c, bool r, bool res, bool d, bool l, bool h)[]
        {
            (false, false, false, false, false, false, false), (true, true, false, false, false, false, false),
            (true, true, true, false, false, false, false), (true, false, true, false, false, false, false),
            (true, true, true, true, true, true, false), (true, true, true, true, true, true, true),
            (false, true, true, false, false, false, true), (true, true, false, false, false, false, true),
        };
        foreach (var k in combos)
        {
            string tag = $"[attack={k.a} conv={k.c} ref={k.r} research={k.res} dip={k.d} lookup={k.l} 3:1={k.h}] ";
            GameEngine engineOn = UtEngine(BalanceProfile.Standard, 1, 200, "infantry", 5000);
            GameEngine engineOff = UtEngine(BalanceProfile.Standard, 1, 200, "infantry", 5000);
            engineOn.State.RequireAssaultTroopRatio = k.h;
            engineOff.State.RequireAssaultTroopRatio = k.h;
            LlmRequest on = UtRun(engineOn, UtCombo(true, k.a, k.c, k.r, k.res, k.d, k.l)).Fake.Requests.FirstOrDefault();
            LlmRequest off = UtRun(engineOff, UtCombo(false, k.a, k.c, k.r, k.res, k.d, k.l)).Fake.Requests.FirstOrDefault();
            if (on == null || off == null) { UtCheck(false, tag + "both prompts were dispatched"); continue; }
            string whole = on.System + "\n" + on.User;
            string hit = UtInfantryOnly.FirstOrDefault(s => whole.Contains(s));
            UtCheck(hit == null, tag + "the enabled prompt contains no infantry-only recruit statement" + (hit == null ? "" : " (found: " + hit + ")"));
            UtCheck(on.System.Contains(UtOrderNew) && on.System.Contains(UtArgsNew),
                tag + "the base rules and output contract are replaced, not overridden later");
            UtCheck(off.System.Contains(UtOrderOld) && off.System.Contains(UtArgsOld) && !off.System.Contains("UNIT TYPES")
                    && (!k.r || off.System.Contains("args.unit_type \"infantry\"): costs"))
                    && !off.System.Contains("archer, cavalry or siege"),
                tag + "disabled: the infantry-only wording and the action reference line are as before");
            if (k.r)
                UtCheck(on.System.Contains("depend on the type and are listed under UNIT TYPES below")
                        && on.System.IndexOf("RECRUITING AND ARMIES", StringComparison.Ordinal)
                           < on.System.IndexOf("UNIT TYPES (opt-in", StringComparison.Ordinal),
                    tag + "the action reference defers recruit costs to the per-type block");
            else
            {
                // With no action reference the enabled system prompt is the disabled one plus the two
                // replaced sentences and the unit-type block; nothing else moved.
                string tail = k.c ? PromptBuilder.GarrisonConversionAddendum : "";
                string offBody = off.System.EndsWith(tail, StringComparison.Ordinal) ? off.System.Substring(0, off.System.Length - tail.Length) : off.System;
                string expectedStart = offBody.Replace(UtOrderOld, UtOrderNew).Replace(UtArgsOld, UtArgsNew);
                UtCheck(on.System.StartsWith(expectedStart, StringComparison.Ordinal)
                        && on.System.Substring(expectedStart.Length).StartsWith("\n\nUNIT TYPES", StringComparison.Ordinal)
                        && on.System.EndsWith(tail, StringComparison.Ordinal),
                    tag + "apart from the two replaced sentences and the unit-type block, the system prompt is the disabled one");
            }
        }

        string sysSpec = PromptBuilder.ApplyUnitTypes(PromptBuilder.SystemPromptFor(true), BalanceProfile.Standard, true, true, false);
        UtCheck(!sysSpec.Contains("must be \"infantry\"") && sysSpec.Contains("UNIT TYPES"),
            "ApplyUnitTypes alone removes the base infantry-only sentences from any planning system prompt");
    }

    // ---- both combatants' multipliers, as the engine computes them ---------------------------------------
    private static float UtParseF(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The factor the prompt states for <paramref name="self"/> fighting <paramref name="other"/>.</summary>
    private static float? UtPromptFactor(string line, string self, string other)
    {
        Match m;
        if (self == "siege")
        { m = Regex.Match(line, @"Siege is x([0-9.]+) for itself"); return m.Success ? UtParseF(m.Groups[1].Value) : (float?)null; }
        if (other == "siege")
        { m = Regex.Match(line, @"fighting siege is x([0-9.]+)"); return m.Success ? UtParseF(m.Groups[1].Value) : (float?)null; }
        if (self == other)
        { m = Regex.Match(line, @"same type x([0-9.]+) each"); return m.Success ? UtParseF(m.Groups[1].Value) : (float?)null; }
        m = Regex.Match(line, self + " vs " + other + ": " + self + @" x([0-9.]+), " + other + @" x([0-9.]+)");
        if (m.Success) return UtParseF(m.Groups[1].Value);
        m = Regex.Match(line, other + " vs " + self + ": " + other + @" x([0-9.]+), " + self + @" x([0-9.]+)");
        return m.Success ? UtParseF(m.Groups[2].Value) : (float?)null;
    }

    private static string UtF(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void UnitParityCombatFactsMatchEngine()
    {
        GameEngine probe = UtEngine(BalanceProfile.Standard);
        LlmRequest req = UtRun(probe, UtConfig(true, true, true)).Fake.Requests.FirstOrDefault();
        string system = req == null ? "" : req.System;
        string line = system.Split('\n').FirstOrDefault(l => l.StartsWith("- Field battle (attack_army)")) ?? "";
        string cityLine = system.Split('\n').FirstOrDefault(l => l.StartsWith("- City assault (attack_city)")) ?? "";
        string other = system.Split('\n').FirstOrDefault(l => l.StartsWith("- Other type effects:")) ?? "";
        UtCheck(line.Length > 0 && cityLine.Length > 0 && other.Length > 0,
            "the enabled prompt states field-battle, city-assault and other type effects");

        int checkedPairs = 0, wrong = 0;
        string firstWrong = null;
        foreach (string attacker in UtAllTypes)
            foreach (string defender in UtAllTypes)
            {
                GameEngine e = TwoSeatCorridor(900 + checkedPairs);
                string attempt = null;
                e.CombatDebugLogger = msg => { if (attempt == null && msg.StartsWith("[Combat][Field][Attempt]")) attempt = msg; };
                ArmyState a = AddArmy(e, "seat-a", 0, 1000);
                a.UnitType = attacker;
                AddEnemyArmy(e, "seat-b", 1, 1000, defender);
                ValidationResult r = e.Submit(AttackOrder("seat-a", a.Id, 1, 0));
                checkedPairs++;
                MatchCollection mults = Regex.Matches(attempt ?? "", @"type_mult\(([0-9.]+)\)");
                bool charge = (attempt ?? "").Contains("plains_charge=True");
                float? statedAtk = UtPromptFactor(line, attacker, defender), statedDef = UtPromptFactor(line, defender, attacker);
                float engineAtk = mults.Count == 2 ? UtParseF(mults[0].Groups[1].Value) / (charge ? BalanceConfig.CavalryPlainsChargeBonus : 1f) : -1f;
                float engineDef = mults.Count == 2 ? UtParseF(mults[1].Groups[1].Value) : -1f;
                bool ok = r.Ok && statedAtk.HasValue && statedDef.HasValue
                    && Math.Abs(statedAtk.Value - engineAtk) < 0.001f && Math.Abs(statedDef.Value - engineDef) < 0.001f;
                if (!ok) { wrong++; if (firstWrong == null) firstWrong = $"{attacker} vs {defender}: stated {statedAtk}/{statedDef}, engine {engineAtk}/{engineDef} ({r.Error})"; }
            }
        UtCheck(checkedPairs == 16 && wrong == 0,
            "all 16 attacker/defender type pairs: the prompt's factor for each combatant equals the engine's" + (firstWrong == null ? "" : " (" + firstWrong + ")"));

        // Cavalry charge: attacker only, when the defender stands on plains.
        GameEngine charge1 = TwoSeatCorridor(950);
        string chargeLog = null;
        charge1.CombatDebugLogger = msg => { if (chargeLog == null && msg.StartsWith("[Combat][Field][Attempt]")) chargeLog = msg; };
        ArmyState cav = AddArmy(charge1, "seat-a", 0, 1000); cav.UnitType = "cavalry";
        AddEnemyArmy(charge1, "seat-b", 1, 1000, "archer");
        charge1.Submit(AttackOrder("seat-a", cav.Id, 1, 0));
        GameEngine charge2 = TwoSeatCorridor(951);
        string reverseLog = null;
        charge2.CombatDebugLogger = msg => { if (reverseLog == null && msg.StartsWith("[Combat][Field][Attempt]")) reverseLog = msg; };
        ArmyState archer = AddArmy(charge2, "seat-a", 0, 1000); archer.UnitType = "archer";
        AddEnemyArmy(charge2, "seat-b", 1, 1000, "cavalry");
        charge2.Submit(AttackOrder("seat-a", archer.Id, 1, 0));
        UtCheck(chargeLog != null && chargeLog.Contains("plains_charge=True") && reverseLog != null && reverseLog.Contains("plains_charge=False")
                && other.Contains("an attacking cavalry army also gets x" + UtF(BalanceConfig.CavalryPlainsChargeBonus))
                && other.Contains("(the defender gets no charge bonus)"),
            "the cavalry charge is attacker-only in the engine and the prompt says so");

        UtCheck(cityLine.Contains("siege attacker is x" + UtF(UnitTypes.SiegeVsCityMultiplier("siege")) + " against the city's defense")
                && cityLine.Contains("garrison of archers defends at x" + UtF(UnitTypes.SiegeDefenseMultiplier("archer")))
                && UnitTypes.SiegeVsCityMultiplier("cavalry") == 1f && UnitTypes.SiegeVsCityMultiplier("infantry") == 1f
                && UnitTypes.SiegeVsCityMultiplier("archer") == 1f
                && UnitTypes.SiegeDefenseMultiplier("cavalry") == 1f && UnitTypes.SiegeDefenseMultiplier("infantry") == 1f
                && UnitTypes.SiegeDefenseMultiplier("siege") == 1f,
            "city-assault type effects equal the engine's: only a siege attacker and an archer garrison have one");
        UtCheck(other.Contains("infantry, attacking or defending, takes x" + UtF(UnitTypes.LossTakenMultiplier("infantry")))
                && UnitTypes.LossTakenMultiplier("archer") == 1f && UnitTypes.LossTakenMultiplier("cavalry") == 1f
                && UnitTypes.LossTakenMultiplier("siege") == 1f
                && other.Contains("up to " + UnitTypes.AttackRange("archer") + " hexes away (other types " + UnitTypes.AttackRange("infantry") + ")")
                && other.Contains("cavalry may march " + UnitTypes.MarchesPerSeason("cavalry") + " times per season"),
            "infantry loss factor, archer range and cavalry marches equal the engine's");
    }
}
