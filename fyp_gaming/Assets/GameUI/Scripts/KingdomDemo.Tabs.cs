// KingdomDemo (partial) — the Domestic / Diplomacy / Intel / Research / History tab panels.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------ concept tabs
        // Domestic / Diplomacy / Intelligence / Research are all live now;
        // History renders the real in-memory event log.

        /// <summary>Opens a concept panel by tab key. Same entry point the tab bar uses,
        /// so scripted demos and screenshot passes can drive the UI without a mouse.</summary>
        public void ShowTab(string key)
        {
            activeTab = key;
        }

        void DrawTabPanel(GUIStyle body, GUIStyle btn, GUIStyle title, GUIStyle mono)
        {
            float height = TabPanelHeight();
            var p = new Rect(400, 190, 800, height);
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(p.x + 20, p.y + 12, 760, 30), TabTitle(), title);

            switch (activeTab)
            {
                case "domestic":  DrawDomesticPanel(p, body); break;
                case "diplomacy": DrawDiplomacyPanel(p, body); break;
                case "intel":     DrawIntelPanel(p, body, btn); break;
                case "research":  DrawResearchPanel(p, body, btn); break;
                default:          DrawHistorianPanel(p, body, mono); break;
            }

            // pinned to the panel bottom, so a content-sized panel keeps its button inside
            if (GUI.Button(new Rect(p.x + 320, p.y + height - 56, 160, 40), "Close", btn)) activeTab = "map";
        }

        /// <summary>
        /// Panel height per tab: the design previews keep the full shell; the historian chronicle hugs its
        /// contents (capped) so a two-record log does not open an 80%-empty box.
        /// </summary>
        float TabPanelHeight()
            => activeTab == "history" ? 178f + HistorianViewHeight() : 480f;

        /// <summary>Height of the historian scroll view for the current seat: content-sized, capped.</summary>
        float HistorianViewHeight()
            => Mathf.Min(HistorianViewMaxHeight,
                         (obs != null ? obs.Events.Count : 1) * HistorianLineHeight + 8f);

        string TabTitle()
        {
            switch (activeTab)
            {
                case "domestic":  return "Domestic · Orders";
                case "diplomacy": return "Diplomacy · Orders";
                case "intel":     return "Intel · Orders";
                case "research":  return "Research · Orders";
                default:           return "History · Chronicle";
            }
        }

        void DrawDomesticPanel(Rect p, GUIStyle body)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22), "Domestic actions (design document chapter 11 · 10 total)", body);
            y += 30;

            // Live actions mirror ACTIONS.md §3 and read their numbers from BalanceConfig,
            // so this panel cannot drift from what the engine actually charges.
            string[] live =
            {
                $"Tax      costs {BalanceConfig.CommandPointsPerOrder} order  Gold +{BalanceConfig.TaxLevyAmount}, morale -{BalanceConfig.TaxLevyMoraleCost}",
                $"Lighten labor  costs {BalanceConfig.CommandPointsPerOrder} order  Gold -{BalanceConfig.LightenLaborGoldCost}, morale +{BalanceConfig.LightenLaborMoraleGain}",
                $"Reclaim  costs {BalanceConfig.CommandPointsPerOrder} order  City food yield +{BalanceConfig.ReclaimPctPerUse}% (cap +{BalanceConfig.ReclaimPctCap}%)",
                $"Recruit  costs {BalanceConfig.CommandPointsPerOrder} order  Gold -{BalanceConfig.RecruitGoldCost}, population -{BalanceConfig.RecruitPopCost}, garrison +{BalanceConfig.RecruitTroopGain}",
                $"Train    costs {BalanceConfig.CommandPointsPerOrder} order  Training +1 (cap {MyTrainingCap()})",
                $"Fortify  costs {BalanceConfig.CommandPointsPerOrder} order  Defense +1 (cap {BalanceConfig.DefenseMax})",
                $"Relief   costs {BalanceConfig.CommandPointsPerOrder} order  Food -{BalanceConfig.ReliefFoodCost}, morale +{BalanceConfig.ReliefMoraleGain}",
            };
            foreach (var row in live)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 22), "Available  " + row, body);
                y += 24;
            }

            y += 10;
            string[] planned =
            {
                "Farm fields  costs 1 order  Requires agriculture 3: army supply +30%",
                "Build structure  costs 1 order  Requires gold and an empty slot (market / farm / barracks...)",
                "Move capital  costs 1 order  Requires another city: prestige -5, change capital",
            };
            foreach (var row in planned)
            {
                PlannedLabel(new Rect(p.x + 36, y, 744, 22), "[Planned] " + row, body);
                y += 24;
            }

            y += 10;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                $"Select one of our cities, then issue orders from the city panel on the left; unavailable buttons are grey.\n" +
                $"Select one of our armies to start a field battle against an adjacent enemy (1 order), or assault an enemy city ({BalanceConfig.CommandPointsPerAttackCity} orders, requiring a {BalanceConfig.AssaultTroopRatio}:1 troop ratio).", body);
        }

        void DrawDiplomacyPanel(Rect p, GUIStyle body)
        {
            var me = engine.State.FindFaction(currentSeat);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            const int shownFactions = 3;      // placeholder layout: the rest are summarised

            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Diplomacy ({me.Name})  Orders {me.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}  Prestige {me.Prestige}" +
                $"  Treaties {(obs != null ? obs.Treaties.Count : 0)}" +
                $"  Pending proposals {(obs != null ? obs.Proposals.Count : 0)}", body);
            y += 26;

            // Envoy message: type here, then press Send message on a faction row to submit it.
            GUI.Label(new Rect(p.x + 36, y, 120, 22), "Envoy message", body);
            envoyText = GUI.TextField(new Rect(p.x + 160, y, 604, 24), envoyText, 120);
            if (envoyConfirmationUntil > Time.unscaledTime)
                GUI.Box(new Rect(p.x + 270, y + 34, 300, 52), "Message sent successfully");
            y += 34;

            // Incoming offers first: they are the only diplomacy that demands an answer.
            var incoming = new List<TreatyProposal>();
            if (obs != null)
                foreach (var proposal in obs.Proposals)
                    if (proposal.ToSeatId == currentSeat) incoming.Add(proposal);

            GUI.Label(new Rect(p.x + 20, y, 760, 22), $"Pending proposals ({incoming.Count})", body);
            y += 24;

            if (incoming.Count == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), "No incoming proposals.", body);
                y += 24;
            }
            foreach (var proposal in incoming)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20),
                    $"{SeatShort(proposal.FromSeatId)} proposes {TreatyLabel(proposal.Type)} for {proposal.DurationSeasons} seasons" +
                    (string.IsNullOrEmpty(proposal.Text) ? "" : $"  Note: {proposal.Text}"), body);
                y += 22;
                OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Accept",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "accept" } });
                OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Reject",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "reject" } });
                y += 36;
            }

            // Relations and the actions available against each other seat.
            int drawn = 0;
            foreach (var other in engine.State.Factions)
            {
                if (other.SeatId == currentSeat) continue;
                if (drawn == shownFactions)
                {
                    GUI.Label(new Rect(p.x + 36, y, 744, 20), "... (remaining factions omitted)", body);
                    break;
                }

                var relation = obs != null ? obs.Relation(other.SeatId) : null;
                int myTrust = relation != null ? relation.MyTrust : 0;
                int theirTrust = relation != null ? relation.TheirTrust : 0;
                bool atWar = relation != null && relation.AtWar;

                GUI.Label(new Rect(p.x + 20, y, 760, 20),
                    $"{other.Name}  Trust me→them {myTrust} / them→me {theirTrust}  {StatusLine(other.SeatId)}", body);
                y += 22;

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.DeclareWar,
                    other.SeatId, "Declare war", atWar ? "Already at war" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.Gift,
                    other.SeatId, $"Gift {BalanceConfig.GiftGoldPerTrustUnit}",
                    Treasury() < BalanceConfig.GiftGoldPerTrustUnit ? "Not enough gold" : null,
                    BalanceConfig.GiftGoldPerTrustUnit, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 400, y, 176, 30), ActionType.SendMessage,
                    other.SeatId, "Send message", string.IsNullOrEmpty(envoyText) ? "Enter a message first" : null,
                    0, 0, btn, null, envoyText, () =>
                    {
                        envoyText = "";
                        envoyConfirmationUntil = Time.unscaledTime + 2f;
                    }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 582, y, 182, 30), ActionType.BreakTreaty,
                    other.SeatId, "Break treaty", BreakBlocked(other.SeatId), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", BreakableType(other.SeatId) } }));
                y += 34;

                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Non-aggression pact", ProposalBlocked(other.SeatId, TreatyType.Nap), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 282, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Alliance", ProposalBlocked(other.SeatId, TreatyType.Alliance), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Alliance } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 528, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Propose truce", ProposalBlocked(other.SeatId, TreatyType.Truce), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Truce } }));
                y += 34;

                if (hint != null)
                {
                    GUI.Label(new Rect(p.x + 20, y, 760, 20), hint, body);
                    y += 22;
                }
                y += 4;
                drawn++;
            }
        }

        /// <summary>Peace / war plus every treaty in force (with seasons left) and any pending offer,
        /// read from this seat's observation only.</summary>
        string StatusLine(string seatId)
        {
            var relation = obs != null ? obs.Relation(seatId) : null;
            var sb = new StringBuilder(relation != null && relation.AtWar ? "At war" : "Peace");
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
            {
                var treaty = MyTreaty(type, seatId);
                if (treaty == null) continue;
                int seasonsLeft = treaty.ExpirySeason - engine.State.Season + 1;
                if (seasonsLeft < 1) seasonsLeft = 1;
                sb.Append($"  [{TreatyLabel(type)} {seasonsLeft} seasons left]");
            }
            var proposal = MyProposal(seatId);
            if (proposal != null)
                                sb.Append(" [").Append(proposal.FromSeatId == currentSeat ? "Our proposal pending" : "Your response needed")
                                    .Append(": ").Append(TreatyLabel(proposal.Type)).Append(']');
            return sb.ToString();
        }

        /// <summary>This seat's own treaty of that type with another seat, from the observation.</summary>
        TreatyRecord MyTreaty(string type, string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var treaty in obs.Treaties)
                if (treaty.Type == type && treaty.Covers(currentSeat, otherSeatId)) return treaty;
            return null;
        }

        /// <summary>The pending offer between this seat and another, in either direction.</summary>
        TreatyProposal MyProposal(string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var proposal in obs.Proposals)
                if ((proposal.FromSeatId == currentSeat && proposal.ToSeatId == otherSeatId)
                    || (proposal.ToSeatId == currentSeat && proposal.FromSeatId == otherSeatId))
                    return proposal;
            return null;
        }

        /// <summary>Reason a treaty offer is obviously illegal, or null when it can be sent.</summary>
        string ProposalBlocked(string seatId, string type)
        {
            if (MyTreaty(type, seatId) != null) return "Already active";
            if (MyProposal(seatId) != null) return "Proposal already pending";
            return null;
        }

        string BreakBlocked(string seatId)
            => BreakableType(seatId) == null ? "No treaty to break" : null;

        /// <summary>First treaty in force with that seat (nap → alliance → truce), or null.</summary>
        string BreakableType(string seatId)
        {
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
                if (MyTreaty(type, seatId) != null) return type;
            return null;
        }

        int Treasury()
        {
            int gold = 0;
            if (obs == null) return 0;
            foreach (var city in obs.CitiesOf(currentSeat)) gold += city.Gold;
            return gold;
        }

        /// <summary>
        /// 諜報 (design §8.2, P8/ADR-015): three missions, each an instant action gated by 諜報 research and
        /// paid from the treasury. Rows submit real commands through the shared OrderButton pipeline, so the
        /// engine stays the only authority; the targets are the foreign cities this seat can actually name
        /// (in sight, or already scouted) and the seat pairs for 挑撥離間.
        /// </summary>
        void DrawIntelPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            int espionage = obs != null ? obs.ResearchLevel(ResearchLine.Espionage) : 0;
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Intelligence ({engine.State.FindFaction(currentSeat)?.Name})  Espionage level {espionage}" +
                $"  ·  success {BalanceConfig.SpySuccessChance(espionage) * 100f:F0}% per mission" +
                $"  ·  gold {Treasury()}  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}", body);
            y += 24;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                "Fog of war: 3 hexes around our cities, 2 around our armies. A failed mission exposes the agent publicly (prestige −8).", body);
            y += 26;

            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Missions", body);
            y += 20;
            string[] missions =
            {
                $"Scout {BalanceConfig.ScoutGoldCost} gold · Espionage {BalanceConfig.ScoutResearchLevel} — target city's garrison and food (banded below Espionage {BalanceConfig.ScoutExactAccuracyLevel})",
                $"Spread rumours {BalanceConfig.RumorGoldCost} gold · Espionage {BalanceConfig.RumorResearchLevel} — target city loses {BalanceConfig.RumorMoraleLoss} morale, deniably",
                $"Sow distrust {BalanceConfig.SowDistrustGoldCost} gold · Espionage {BalanceConfig.SowDistrustResearchLevel} — two other seats lose {BalanceConfig.SowDistrustTrustPenalty} trust in each other",
            };
            foreach (string mission in missions)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), mission, body);
                y += 20;
            }
            y += 8;

            // targets: the foreign cities this seat can see right now
            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Targets (foreign cities in sight)", body);
            y += 22;
            var targets = new List<CityState>();
            if (obs != null)
                foreach (var city in obs.Cities)
                    if (city.OwnerSeatId != currentSeat) targets.Add(city);
            if (targets.Count == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20),
                    "No foreign city is in sight — march an army closer, then send an agent.", body);
                y += 22;
            }
            int shown = 0;
            foreach (var city in targets)
            {
                if (shown == 2) { GUI.Label(new Rect(p.x + 36, y, 744, 20), "... (remaining cities omitted)", body); y += 22; break; }
                shown++;
                var report = obs.FindScoutedCity(city.Id);
                GUI.Label(new Rect(p.x + 36, y, 384, 20),
                    $"{city.Name} ({OwnerName(city)})  garrison {city.Garrison}, morale {city.Morale}" +
                    (report != null ? $", report until season {report.UntilSeason}" : ""), body);
                OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, city.Id, "Scout",
                    SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn);
                OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, city.Id, "Rumours",
                    city.IsNeutral ? "no faction to undermine"
                                   : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                    0, 0, btn);
                y += 26;
            }

            // live reports on cities nothing of ours can see — and the way to extend them, since a report
            // is the only handle a seat has on a city outside its fog (ADR-015)
            if (obs != null && obs.ScoutedCities.Count > 0)
            {
                GUI.Label(new Rect(p.x + 20, y, 760, 20), "Live reports", body);
                y += 22;
                foreach (var report in obs.ScoutedCities)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"exact · garrison {report.Garrison}, food {report.Food}, morale {report.Morale}"
                        : $"band · troops {report.GarrisonBand}, food {report.FoodBand}";
                    GUI.Label(new Rect(p.x + 36, y, 384, 20),
                        $"{report.Name} ({SeatShort(report.OwnerSeatId)})  {detail}  ·  to season {report.UntilSeason}", body);
                    OrderButton(new Rect(p.x + 424, y, 158, 24), ActionType.Scout, report.CityId, "Scout",
                        SpyBlocked(BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost), 0, 0, btn);
                    OrderButton(new Rect(p.x + 590, y, 148, 24), ActionType.Rumor, report.CityId, "Rumours",
                        string.IsNullOrEmpty(report.OwnerSeatId) ? "no faction to undermine"
                            : SpyBlocked(BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost),
                        0, 0, btn);
                    y += 26;
                }
                y += 4;
            }

            // 挑撥離間 needs two other seats: offer the first pairs and say so when there are more
            GUI.Label(new Rect(p.x + 20, y, 760, 20), "Sow distrust between", body);
            y += 22;
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
                    OrderButton(new Rect(p.x + 36, y, 300, 24), ActionType.SowDistrust, others[i],
                        $"{SeatShort(others[i])} ↔ {SeatShort(others[j])}",
                        SpyBlocked(BalanceConfig.SowDistrustResearchLevel, BalanceConfig.SowDistrustGoldCost),
                        0, 0, btn, new Dictionary<string, string> { { "target_b", others[j] } });
                    y += 28;
                }
                if (pairs == 2) break;
            }
            if (pairs == 0)
            {
                GUI.Label(new Rect(p.x + 36, y, 744, 20), "No other seat is known yet.", body);
                y += 20;
            }
            else if (others.Count > 2)
            {
                GUI.Label(new Rect(p.x + 348, y - 26, 420, 20), "... (remaining pairs omitted)", body);
            }
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
        void DrawResearchPanel(Rect p, GUIStyle body, GUIStyle btn)
        {
            float y = p.y + 52;
            GUI.Label(new Rect(p.x + 20, y, 760, 22),
                $"Research points {obs.ResearchPoints}  (+{BalanceConfig.ResearchIncome(obs.ResearchLevel(ResearchLine.Governance))}/season)" +
                $"  Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $"  ·  level costs {string.Join(" / ", System.Array.ConvertAll(BalanceConfig.ResearchLevelCosts, v => v.ToString()))}", body);
            y += 30;

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

                GUI.Label(new Rect(p.x + 20, y, 560, 22),
                    $"{ResearchLine.Label(line)}  {ResearchPips(level)}  {level}/{BalanceConfig.ResearchMaxLevel}", body);
                OrderButton(new Rect(p.x + 596, y, 184, 30), ActionType.Research, null, "Research",
                    blocked, 0, 0, btn, new Dictionary<string, string> { { "line", line } });

                GUI.Label(new Rect(p.x + 36, y + 26, 744, 20),
                    "Now:  " + (level > 0 ? ResearchEffect(line, level) : "—"), body);
                GUI.Label(new Rect(p.x + 36, y + 44, 744, 20),
                    maxed ? "All five levels researched."
                          : $"Next: level {level + 1} for {cost} points — {ResearchEffect(line, level + 1)}", body);
                y += 70;
            }

            y += 6;
            GUI.Label(new Rect(p.x + 20, y, 760, 44),
                "Points arrive at every season rollover. Harvest, tax, combat and training-cap effects apply as soon as they resolve;\n" +
                "律法 (level 3) and the income bonus settle at the next rollover. 學堂 the building (+3/season) and Intelligence 3-5 effects are deferred.", body);
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
                        case 2: return "Water works — drought damage halved (deferred: no 天時 events)";
                        case 3: return "Military farms — garrison self-supply 30% (deferred: 屯田 D9)";
                        case 4: return "State granaries — food cap +50% (deferred: no food cap)";
                        case 5: return $"Shennong — all harvests +{(int)(BalanceConfig.ResearchAgriL5FoodBonus * 100)}%";
                    }
                    break;
                case ResearchLine.Military:
                    switch (level)
                    {
                        case 1: return $"Drill — training cap +{BalanceConfig.ResearchMilTrainingCapBonus} (now {BalanceConfig.TrainingCap(1)})";
                        case 2: return "Cavalry unlock (deferred: no unit types)";
                        case 3: return "Siege engines unlock (deferred: no 器械)";
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

        void DrawHistorianPanel(Rect p, GUIStyle body, GUIStyle mono)
        {
            // the chronicle is the seat's filtered view of the log (CONTROLLER_PROTOCOL §5)
            var records = obs != null ? obs.Events : new List<EventRecord>();
            GUI.Label(new Rect(p.x + 20, p.y + 52, 760, 22),
                $"Campaign chronicle (visible to {engine.State.FindFaction(currentSeat)?.Name}) — {records.Count} entries" +
                $" (season {engine.State.Season} · {PhaseLabel(engine.State.Phase)})", body);

            // hug the chronicle: the scroll view is as tall as its content (capped), and the
            // scrollbar only appears when the cap actually bites
            float viewHeight = HistorianViewHeight();
            float contentHeight = records.Count * HistorianLineHeight + 8f;
            var view = new Rect(p.x + 20, p.y + 80, 760, viewHeight);
            historianScroll = GUI.BeginScrollView(view, historianScroll,
                                                  new Rect(0, 0, 744, contentHeight), false, false);
            for (int i = 0; i < records.Count; i++)
                GUI.Label(new Rect(4, 4 + i * HistorianLineHeight, 736, HistorianLineHeight),
                          HistorianLine(records[i]), mono);
            GUI.EndScrollView();

            GUI.Label(new Rect(p.x + 20, p.y + 80 + viewHeight + 12, 760, 22),
                $"Historian: {CountSeasonEvents(engine.State.Season)} events recorded this season.", body);
        }

        string HistorianLine(EventRecord rec)
        {
            var sb = new StringBuilder();
            sb.Append(rec.Seq.ToString().PadLeft(2));
            sb.Append(" Season ").Append(rec.Season).Append(" ");
            sb.Append(rec.EventType);
            sb.Append("　").Append(SeatShort(rec.ActorSeatId));
            string payload = PayloadText(rec.Payload);
            if (payload.Length > 0) sb.Append("　").Append(payload);
            return sb.ToString();
        }
    }
}
