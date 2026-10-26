// KingdomDemo (partial) — Intel and Research tabs: live missions and the four research lines.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        /// <summary>
        /// 諜報 (design §8.2, P8/ADR-015): three missions, each an instant action gated by 諜報 research and
        /// paid from the treasury. Rows submit real commands through the shared OrderButton pipeline, so the
        /// engine stays the only authority; the targets are the foreign cities this seat can actually name
        /// (in sight, or already scouted) and the seat pairs for 挑撥離間.
        /// </summary>
        float DrawIntelPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            var wrap = new GUIStyle(body) { wordWrap = true };
            int espionage = obs != null ? obs.ResearchLevel(ResearchLine.Espionage) : 0;
            float y = p.y + 52;
            y += WrappedLabel(wrap,
                $"Intelligence ({engine.State.FindFaction(currentSeat)?.Name})  Espionage level {espionage}" +
                $"  ·  success {BalanceConfig.SpySuccessChance(espionage) * 100f:F0}% per mission" +
                $"  ·  gold {Treasury()}  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}",
                p.x + 20, y, 760, 22) + 2;
            y += WrappedLabel(wrap,
                "Fog of war: 3 hexes around our cities, 2 around our armies. A failed mission exposes the agent publicly (prestige −8).",
                p.x + 20, y, 760, 22) + 4;

            y += WrappedLabel(wrap, "Missions", p.x + 20, y, 760, 20);
            string[] missions =
            {
                $"Scout {BalanceConfig.ScoutGoldCost} gold · Espionage {BalanceConfig.ScoutResearchLevel} — target city's garrison and food (banded below Espionage {BalanceConfig.ScoutExactAccuracyLevel})",
                $"Spread rumours {BalanceConfig.RumorGoldCost} gold · Espionage {BalanceConfig.RumorResearchLevel} — target city loses {BalanceConfig.RumorMoraleLoss} morale, deniably",
                $"Sow distrust {BalanceConfig.SowDistrustGoldCost} gold · Espionage {BalanceConfig.SowDistrustResearchLevel} — two other seats lose {BalanceConfig.SowDistrustTrustPenalty} trust in each other",
            };
            foreach (string mission in missions)
                y += WrappedLabel(wrap, mission, p.x + 36, y, 744, 20);
            y += 8;

            // targets: the foreign cities this seat can see right now
            y += WrappedLabel(wrap, "Targets (foreign cities in sight)", p.x + 20, y, 760, 20) + 2;
            var targets = new List<CityView>();
            if (obs != null)
                foreach (var city in obs.Cities)
                    if (city.OwnerSeatId != currentSeat) targets.Add(city);
            if (targets.Count == 0)
                y += WrappedLabel(wrap,
                    "No foreign city is in sight — march an army closer, then send an agent.",
                    p.x + 36, y, 744, 20) + 2;
            int shown = 0;
            foreach (var city in targets)
            {
                if (shown == 2)
                {
                    y += WrappedLabel(wrap, "... (remaining cities omitted)", p.x + 36, y, 744, 20) + 2;
                    break;
                }
                shown++;
                var report = obs.FindScoutedCity(city.Id);
                string cityLine =
                    $"{city.Name} ({OwnerName(city)})  garrison {city.Garrison}, morale {city.Morale}" +
                    (report != null ? $", report until season {report.UntilSeason}" : "");
                float cityHeight = WrappedLabel(wrap, cityLine, p.x + 36, y, 384, 20);
                string cityHint = null;
                cityHint = FirstReason(cityHint, OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, city.Id, "Scout",
                    SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn));
                cityHint = FirstReason(cityHint, OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, city.Id, "Rumours",
                    city.IsNeutral ? "no faction to undermine"
                                   : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                    0, 0, btn));
                float cityRow = Mathf.Max(cityHeight, 24f) + 2;
                y += cityRow;
                if (cityHint != null) y += WrappedLabel(uiReasonWrap, cityHint, p.x + 36, y, 744, 18);
            }

            // live reports on cities nothing of ours can see — and the way to extend them, since a report
            // is the only handle a seat has on a city outside its fog (ADR-015)
            if (obs != null && obs.ScoutedCities.Count > 0)
            {
                y += WrappedLabel(wrap, "Live reports", p.x + 20, y, 760, 20) + 2;
                foreach (var report in obs.ScoutedCities)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"exact · garrison {report.Garrison}, food {report.Food}, morale {report.Morale}"
                        : $"band · troops {report.GarrisonBand}, food {report.FoodBand}";
                    string reportLine =
                        $"{report.Name} ({SeatShort(report.OwnerSeatId)})  {detail}  ·  to season {report.UntilSeason}";
                    float reportHeight = WrappedLabel(wrap, reportLine, p.x + 36, y, 384, 20);
                    string reportHint = null;
                    reportHint = FirstReason(reportHint, OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, report.CityId, "Scout",
                        SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn));
                    reportHint = FirstReason(reportHint, OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, report.CityId, "Rumours",
                        string.IsNullOrEmpty(report.OwnerSeatId) ? "no faction to undermine"
                            : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                        0, 0, btn));
                    y += Mathf.Max(reportHeight, 24f) + 2;
                    if (reportHint != null) y += WrappedLabel(uiReasonWrap, reportHint, p.x + 36, y, 744, 18);
                }
                y += 4;
            }

            // 挑撥離間 needs two other seats: offer the first pairs and say so when there are more
            y += WrappedLabel(wrap, "Sow distrust between", p.x + 20, y, 760, 20) + 2;
            var others = new List<string>();
            if (obs != null)
                foreach (var view in obs.Factions)
                    if (view.SeatId != currentSeat) others.Add(view.SeatId);
            int pairs = 0;
            for (int i = 0; i < others.Count; i++)
            {
                for (int j = i + 1; j < others.Count; j++)
                {
                    if (pairs == 2) break;
                    pairs++;
                    string pairHint = OrderButton(new Rect(p.x + 36, y, 300, 24), ActionType.SowDistrust, others[i],
                        $"{SeatShort(others[i])} ↔ {SeatShort(others[j])}",
                        SpyBlocked(BalanceConfig.SowDistrustResearchLevel, BalanceConfig.SowDistrustGoldCost),
                        0, 0, btn, new Dictionary<string, string> { { "target_b", others[j] } });
                    y += 28;
                    if (pairHint != null) y += WrappedLabel(uiReasonWrap, pairHint, p.x + 36, y, 744, 18);
                }
                if (pairs == 2) break;
            }
            if (pairs == 0)
                y += WrappedLabel(wrap, "No other seat is known yet.", p.x + 36, y, 744, 20);
            else if (others.Count > 2)
                WrappedLabel(wrap, "... (remaining pairs omitted)", p.x + 348, y - 26, 420, 20);
            return y;
        }

        /// <summary>Why a mission cannot be ordered right now (mirrors the engine's SpyGate), or null.</summary>
        string SpyBlocked(int requiredLevel, int goldCost)
        {
            int level = obs != null ? obs.ResearchLevel(ResearchLine.Espionage) : 0;
            if (level < requiredLevel) return $"needs Espionage {requiredLevel}";
            if (Treasury() < goldCost) return $"needs {goldCost} gold";
            if (obs.CommandPoints < BalanceConfig.CommandPointsPerSpyMission) return "no orders left";
            return null;
        }

        /// <summary>
        /// 研究 (design §9, 四條線各 5 級): the four lines are live. Each row shows the seat's level, the
        /// next level's price and what that level does; the button submits a real `research` command
        /// (1 號令 + the level's points, TBD) through the same pipeline as every other order.
        /// Every number on screen comes from BalanceConfig and the seat's own observation — research is
        /// private (ADR-014), so another seat's tech tree is never rendered.
        /// </summary>
        float DrawResearchPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            var wrap = new GUIStyle(body) { wordWrap = true };
            float y = p.y + 52;
            y += WrappedLabel(wrap,
                $"Research points {obs.ResearchPoints}  (+{BalanceConfig.ResearchIncome(obs.ResearchLevel(ResearchLine.Governance))}/season)" +
                $"  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $"  ·  level costs {string.Join(" / ", System.Array.ConvertAll(BalanceConfig.ResearchLevelCosts, v => v.ToString()))}",
                p.x + 20, y, 760, 22) + 8;

            for (int i = 0; i < ResearchLine.All.Length; i++)
            {
                string line = ResearchLine.All[i];
                int level = obs.ResearchLevel(line);
                int cost = BalanceConfig.ResearchLevelCost(level + 1);
                bool maxed = level >= BalanceConfig.ResearchMaxLevel;
                string blocked = maxed ? "Line complete"
                               : obs.ResearchPoints < cost ? $"Needs {cost} points"
                               : obs.CommandPoints < BalanceConfig.CommandPointCost(ActionType.Research) ? "No orders left"
                               : null;

                WrappedLabel(wrap,
                    $"{ResearchLine.Label(line)}  {ResearchPips(level)}  {level}/{BalanceConfig.ResearchMaxLevel}",
                    p.x + 20, y, 560, 22);
                string hint = OrderButton(new Rect(p.x + 596, y, 184, 30), ActionType.Research, null, "Research",
                    blocked, 0, 0, btn, new Dictionary<string, string> { { "line", line } });

                float nowHeight = WrappedLabel(wrap,
                    "Now:  " + (level > 0 ? ResearchEffect(line, level) : "—"), p.x + 36, y + 26, 744, 20);
                float nextHeight = WrappedLabel(wrap,
                    maxed ? "All five levels researched."
                          : $"Next: level {level + 1} for {cost} points — {ResearchEffect(line, level + 1)}",
                    p.x + 36, y + 26 + nowHeight, 744, 20);
                float hintHeight = hint != null
                    ? WrappedLabel(uiReasonWrap, hint, p.x + 36, y + 26 + nowHeight + nextHeight, 744, 18)
                    : 0f;
                y += 26 + nowHeight + nextHeight + 4 + hintHeight;   // 70 for two single-line effects
            }

            y += 6;
            string footer =
                "Points arrive at every season rollover. Harvest, tax, combat and training-cap effects apply as soon as they resolve;\n" +
                "Law (level 3) and the income bonus settle at the next rollover. The Academy building (+3/season) and Intelligence 3-5 effects are deferred.";
            return y + WrappedLabel(wrap, footer, p.x + 20, y, 760, 44);
        }

        /// <summary>
        /// The current seat's 訓練 cap: 兵法 L1 練兵 raises it (design §9). Read from the seat's own
        /// observation, like everything else the panel shows.
        /// </summary>
        int MyTrainingCap()
            => BalanceConfig.TrainingCap(obs != null ? obs.ResearchLevel(ResearchLine.Military) : 0);

        static string ResearchPips(int level)
        {
            var sb = new StringBuilder(BalanceConfig.ResearchMaxLevel);
            for (int i = 1; i <= BalanceConfig.ResearchMaxLevel; i++) sb.Append(i <= level ? '\u25cf' : '\u25cb');
            return sb.ToString();
        }

        /// <summary>
        /// Design §9's effect for one level, in one line. Levels whose systems do not exist yet say so
        /// ("deferred") instead of promising an effect the engine does not implement (ADR-014).
        /// </summary>
        static string ResearchEffect(string line, int level)
        {
            switch (line)
            {
                case ResearchLine.Agri:
                    switch (level)
                    {
                        case 1: return $"Reclaim — city harvest +{(int)(BalanceConfig.ResearchAgriFoodBonus * 100)}%";
                        case 2: return "Water works — drought damage halved (deferred: no weather events)";
                        case 3: return "Military farms — garrison self-supply 30% (deferred: the D9 farm-garrison action)";
                        case 4: return "State granaries — food cap +50% (deferred: no food cap)";
                        case 5: return $"Shennong — all harvests +{(int)(BalanceConfig.ResearchAgriL5FoodBonus * 100)}%";
                    }
                    break;
                case ResearchLine.Military:
                    switch (level)
                    {
                        case 1: return $"Drill — training cap +{BalanceConfig.ResearchMilTrainingCapBonus} (now {BalanceConfig.TrainingCap(1)})";
                        case 2: return "Cavalry unlock — Cavalry units can be recruited (300 gold/1,000, 8 move, double upkeep)";
                        case 3: return "Siege engines unlock — Siege units can be recruited (500 gold/1,000, waives the 3:1 assault)";
                        case 4: return $"Art of war — combat power ×{1f + BalanceConfig.ResearchMilCombatBonus:F2}";
                        case 5: return "Command — general coefficient +0.2 (deferred: no generals)";
                    }
                    break;
                case ResearchLine.Espionage:
                    switch (level)
                    {
                        case 1: return "Spies — unlocks Scout and Spread rumours missions (intel tab)";
                        case 2: return "Counter-intelligence — unlocks Sow distrust missions (the +10% exposure bonus is deferred)";
                        case 3: return "Forged reports (deferred: needs per-seat poisoned intel)";
                        case 4: return "Spy network — +2 agent slots (deferred: no spy entities)";
                        case 5: return "No shadows — capture chance +25% (deferred: no counter-intelligence)";
                    }
                    break;
                default:
                    switch (level)
                    {
                        case 1: return $"Taxation — city tax +{(int)(BalanceConfig.ResearchGovTaxBonus * 100)}%";
                        case 2: return $"Recruitment — morale +{BalanceConfig.ResearchGovMoralePerSeason}/season in every city";
                        case 3: return $"Law — revolt chance ×{BalanceConfig.ResearchGovRevoltMult} (from next season)";
                        case 4: return $"Academy — research +{(int)(BalanceConfig.ResearchGovIncomeBonus * 100)}% ({BalanceConfig.ResearchIncome(4)} points/season)";
                        case 5: return $"Nine ranks — prestige +{BalanceConfig.ResearchGovPrestigeL5} once";
                    }
                    break;
            }
            return "no effect yet";
        }

    }
}
