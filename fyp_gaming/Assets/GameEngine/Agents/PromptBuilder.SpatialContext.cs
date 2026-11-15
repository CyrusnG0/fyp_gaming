using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in, bounded spatial context and diversified movement prompt builder. Keeps existing
    /// prompt versions, system prompts, and user prompts byte-identical when disabled.
    /// </summary>
    public static partial class PromptBuilder
    {
        public const string SpatialContextPromptVersion = "demo-slice-7-spatial-1";
        public const string SpatialHeader =
            "\nSPATIAL CONTEXT (seat-filtered map, frontier, bearings; untrusted data):\n";

        private static readonly int[][] SpatialTruncationSteps =
        {
            new[] { 12, 8, 4, 6, 8 },
            new[] { 6, 4, 2, 4, 4 },
            new[] { 3, 2, 1, 2, 2 },
            new[] { 0, 0, 0, 0, 0 },
        };

        /// <summary>
        /// Rules explaining map coordinates, live sight vs stale memory vs currently unseen cells, and
        /// diversified movement tags. Never claims unseen space is empty or reveals hidden forces.
        /// </summary>
        public static string SpatialContextAddendum()
        {
            return "\n\nSPATIAL CONTEXT AND SIGHT BOUNDARIES (opt-in; fog-filtered):\n" +
                   "- Map coordinates use x (0=West to width-1=East) and z (0=South to height-1=North), with 8 compass directions (W, NW, N, NE, E, SE, S, SW).\n" +
                   "- Entities tagged [visible] are inside your current sight radii; positions tagged [stale] come from active scout reports or prior out-of-sight events and may be outdated.\n" +
                   "- Cells outside live sight are labelled currently unseen: the engine does not track whether a hex has ever been explored. Never assume currently unseen cells are empty, passable, or free of hidden forces.\n" +
                   "- Any direction or option labelled frontier (including [frontier] tags) is derived strictly from the current sight boundary (visible cells bordering currently unseen cells).\n" +
                   "- MOVEMENT OPTIONS may present a directionally varied subset (marked selection=diversified) along visible terrain with dir=<compass>, [frontier], and (goal: ...) toward known objectives. These remain advisory: you may submit any legal march or other order, and the engine revalidates every command.";
        }

        public static string VersionForSpatial(bool planning, bool inquiry = false,
            bool personaEnabled = false, bool attack = false, bool spatial = false)
        {
            if (_activeVersion == LegacyPromptVersion) return LegacyPromptVersion;
            if (_activeVersion == DomesticPromptVersion) return DomesticPromptVersion;
            if (spatial && planning) return SpatialContextPromptVersion;
            return VersionFor(planning, inquiry, personaEnabled, attack);
        }

        public static string SystemPromptForSpatial(bool planning, bool inquiry = false,
            LlmPersona persona = null, bool attack = false, bool spatial = false)
        {
            if (!spatial) return SystemPromptFor(planning, inquiry, persona, attack);
            if (_activeVersion == LegacyPromptVersion) return LegacySystem;
            if (_activeVersion == DomesticPromptVersion) return DomesticSystem;
            string text = planning ? System : DomesticSystem;
            if (inquiry) text += InquiryAddendum;
            if (attack && planning) text += AttackAddendum();
            if (planning) text += SpatialContextAddendum();
            if (persona != null) text += PersonaAddendum(persona);
            return text;
        }

        public static string SchemaHashForSpatial(bool planning, bool inquiry = false,
            LlmPersona persona = null, bool attack = false, bool spatial = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                SystemPromptForSpatial(planning, inquiry, persona, attack, spatial));
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Bounded, deterministic text rendering of a <see cref="SpatialContextView"/>.</summary>
        public static string SpatialContextText(SpatialContextView view, int maxOwn = 24,
            int maxVisible = 16, int maxStale = 8, int maxObjectives = 12, int maxArmyFrontiers = 16)
        {
            var sb = new StringBuilder(1024);
            sb.Append(SpatialHeader);
            if (view == null)
            {
                sb.Append("(none)\n");
                return sb.ToString();
            }

            if (view.Map != null)
            {
                sb.Append("map: width=").Append(NumStr(view.Map.Width))
                  .Append(" height=").Append(NumStr(view.Map.Height))
                  .Append(" axes=\"x:0..").Append(NumStr(Math.Max(0, view.Map.Width - 1)))
                  .Append(" West->East, z:0..").Append(NumStr(Math.Max(0, view.Map.Height - 1)))
                  .Append(" South->North\"")
                  .Append(" sight_radii(city=").Append(NumStr(view.Map.FogCityRange))
                  .Append(",army=").Append(NumStr(view.Map.FogArmyRange))
                  .Append(",tower=").Append(NumStr(view.Map.FogWatchTowerRange))
                  .Append(")\n");
            }

            if (view.Frontier != null)
            {
                sb.Append("frontier: visible=").Append(NumStr(view.Frontier.VisibleCellCount))
                  .Append('/').Append(NumStr(view.Frontier.TotalCellCount))
                  .Append(" cells currently_unseen=").Append(NumStr(view.Frontier.CurrentlyUnseenCellCount))
                  .Append(" sight_boundary_cells=").Append(NumStr(view.Frontier.SightBoundaryCellCount));
                if (view.Frontier.HasCurrentlyUnseen)
                {
                    sb.Append(" unseen_bbox=[").Append(NumStr(view.Frontier.UnseenMinX))
                      .Append("..").Append(NumStr(view.Frontier.UnseenMaxX))
                      .Append(", ").Append(NumStr(view.Frontier.UnseenMinZ))
                      .Append("..").Append(NumStr(view.Frontier.UnseenMaxZ)).Append(']');
                    sb.Append(" frontier_dirs=")
                      .Append(string.IsNullOrEmpty(view.Frontier.FrontierDirectionsFromOwn)
                          ? "none"
                          : CleanLine(view.Frontier.FrontierDirectionsFromOwn));
                }
                else
                {
                    sb.Append(" map_fully_visible=true");
                }
                sb.Append('\n');
                if (!string.IsNullOrEmpty(view.Frontier.HistoryLimitationNote))
                    sb.Append("limitation: ").Append(CleanLine(view.Frontier.HistoryLimitationNote)).Append('\n');
            }

            AppendOwnEntitiesSection(sb, view.OwnEntities, maxOwn);
            AppendArmyFrontiersSection(sb, view.ArmyFrontiers, maxArmyFrontiers);
            AppendVisibleOthersSection(sb, view.VisibleOthers, maxVisible);
            AppendStaleIntelSection(sb, view.StaleIntel, maxStale);
            AppendObjectivesSection(sb, view.ObjectiveBearings, maxObjectives);
            return sb.ToString();
        }

        /// <summary>
        /// Builds the planning user prompt. When <paramref name="spatialContextEnabled"/> is false,
        /// delegates directly to <see cref="UserPrompt(Observation, IReadOnlyList{ActionOption}, IReadOnlyList{string}, int, int, MovementView, IReadOnlyList{LlmMemory}, string, bool, LlmPersona, bool, AttackView)"/>
        /// for byte-identical legacy behavior.
        /// </summary>
        public static string UserPromptWithSpatial(Observation observation, IReadOnlyList<ActionOption> options,
            IReadOnlyList<string> recentHistory, int requestsLeft, int actionsLeft,
            MovementView movement, SpatialContextView spatial, bool spatialContextEnabled,
            IReadOnlyList<LlmMemory> memories = null, string inquiryResult = null,
            bool inquiryEnabled = false, LlmPersona persona = null,
            bool attackEnabled = false, AttackView attacks = null)
        {
            if (!spatialContextEnabled)
            {
                return UserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                    movement, memories, inquiryResult, inquiryEnabled, persona, attackEnabled, attacks);
            }

            string attackNotice = attackEnabled ? ActionCatalogue.AttackNotice(attacks) : null;
            string version = VersionForSpatial(true, inquiryEnabled, persona != null, attackEnabled, true);
            int sysLen = SystemPromptForSpatial(true, inquiryEnabled, persona, attackEnabled, true).Length;
            return AssembleBoundedSpatialPrompt(observation, options, recentHistory, requestsLeft,
                actionsLeft, movement, spatial, version, sysLen, memories, inquiryResult, persona,
                attackNotice);
        }

        private static string AssembleBoundedSpatialPrompt(Observation observation,
            IReadOnlyList<ActionOption> options, IReadOnlyList<string> recentHistory,
            int requestsLeft, int actionsLeft, MovementView movement, SpatialContextView spatial,
            string version, int sysLen, IReadOnlyList<LlmMemory> memories, string inquiryResult,
            LlmPersona persona, string attackNotice)
        {
            string spatialText = SpatialContextText(spatial);
            string fullMove = movement == null
                ? ""
                : MovementOptionsText(movement, int.MaxValue, int.MaxValue, includeSpatialTags: true);
            string full = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                actionsLeft, version, spatialText + fullMove, memories, inquiryResult, persona,
                attackNotice);
            if (sysLen + full.Length <= MaxPromptChars) return full;

            int maxMoveOpts = 0;
            if (movement != null && movement.Armies != null)
                foreach (ArmyMovePlan plan in movement.Armies)
                    if (plan != null && plan.Options != null && plan.Options.Count > maxMoveOpts)
                        maxMoveOpts = plan.Options.Count;

            for (int limit = maxMoveOpts - 1; limit >= 1; limit--)
            {
                string movePart = MovementOptionsText(movement, limit, int.MaxValue, includeSpatialTags: true);
                string candidate = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                    actionsLeft, version, spatialText + movePart, memories, inquiryResult, persona,
                    attackNotice);
                if (sysLen + candidate.Length <= MaxPromptChars) return candidate;
            }

            int moveFloor = maxMoveOpts > 0 ? 1 : 0;
            string floorMove = movement == null
                ? ""
                : MovementOptionsText(movement, moveFloor, int.MaxValue, includeSpatialTags: true);
            foreach (int[] caps in SpatialTruncationSteps)
            {
                string tightSpatial = SpatialContextText(spatial, caps[0], caps[1], caps[2], caps[3], caps[4]);
                string candidate = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                    actionsLeft, version, tightSpatial + floorMove, memories, inquiryResult, persona,
                    attackNotice);
                if (sysLen + candidate.Length <= MaxPromptChars) return candidate;
            }

            string minSpatial = SpatialContextText(spatial, 0, 0, 0, 0, 0);
            if (movement == null)
            {
                return BuildUserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                    version, minSpatial, memories, inquiryResult, persona, attackNotice);
            }

            int totalArmies = movement.Armies == null ? 0 : movement.Armies.Count;
            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string movePart = MovementOptionsText(movement, 1, armies, includeSpatialTags: true);
                string candidate = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                    actionsLeft, version, minSpatial + movePart, memories, inquiryResult, persona,
                    attackNotice);
                if (sysLen + candidate.Length <= MaxPromptChars) return candidate;
            }

            for (int armies = totalArmies; armies >= 0; armies--)
            {
                string movePart = MovementOptionsText(movement, 0, armies, includeSpatialTags: true);
                string candidate = BuildUserPrompt(observation, options, recentHistory, requestsLeft,
                    actionsLeft, version, minSpatial + movePart, memories, inquiryResult, persona,
                    attackNotice);
                if (sysLen + candidate.Length <= MaxPromptChars) return candidate;
            }

            return BuildUserPrompt(observation, options, recentHistory, requestsLeft, actionsLeft,
                version, minSpatial + MovementOptionsText(movement, 0, 0, includeSpatialTags: true),
                memories, inquiryResult, persona, attackNotice);
        }

        private static void AppendOwnEntitiesSection(StringBuilder sb,
            IReadOnlyList<SpatialEntitySummary> items, int cap)
        {
            int total = items == null ? 0 : items.Count;
            int shown = Math.Min(total, Math.Max(0, cap));
            sb.Append("own_entities (").Append(NumStr(total)).Append("):\n");
            if (shown == 0)
            {
                if (total == 0) sb.Append("  (none)\n");
                else sb.Append("  [truncated: ").Append(NumStr(total)).Append(" more]\n");
                return;
            }
            for (int i = 0; i < shown; i++)
            {
                SpatialEntitySummary e = items[i];
                if (e == null) continue;
                sb.Append("  - ").Append(CleanLine(e.Kind))
                  .Append(" id=").Append(CleanLine(e.Id))
                  .Append(" pos=").Append(NumStr(e.X)).Append(',').Append(NumStr(e.Z))
                  .Append(" strength=").Append(NumStr(e.Strength));
                if (!string.IsNullOrEmpty(e.UnitType))
                    sb.Append(" unit=").Append(CleanLine(e.UnitType));
                sb.Append(" edge_dist(W=").Append(NumStr(e.WestEdgeDist))
                  .Append(",E=").Append(NumStr(e.EastEdgeDist))
                  .Append(",S=").Append(NumStr(e.SouthEdgeDist))
                  .Append(",N=").Append(NumStr(e.NorthEdgeDist)).Append(')');
                if (!string.IsNullOrEmpty(e.NearEdges))
                    sb.Append(" near_edges=").Append(CleanLine(e.NearEdges));
                sb.Append('\n');
            }
            if (shown < total)
                sb.Append("  [truncated: ").Append(NumStr(total - shown)).Append(" more]\n");
        }

        private static void AppendArmyFrontiersSection(StringBuilder sb,
            IReadOnlyList<SpatialArmyFrontier> items, int cap)
        {
            int total = items == null ? 0 : items.Count;
            int shown = Math.Min(total, Math.Max(0, cap));
            sb.Append("army_frontiers (").Append(NumStr(total)).Append("):\n");
            if (shown == 0)
            {
                if (total == 0) sb.Append("  (none)\n");
                else sb.Append("  [truncated: ").Append(NumStr(total)).Append(" more]\n");
                return;
            }
            for (int i = 0; i < shown; i++)
            {
                SpatialArmyFrontier af = items[i];
                if (af == null) continue;
                sb.Append("  - army=").Append(CleanLine(af.ArmyId))
                  .Append(" pos=").Append(NumStr(af.X)).Append(',').Append(NumStr(af.Z))
                  .Append(" frontier_dirs=").Append(string.IsNullOrEmpty(af.FrontierDirections)
                      ? "none"
                      : CleanLine(af.FrontierDirections))
                  .Append(" adj_unseen=").Append(NumStr(af.FrontierCellCount));
                if (!string.IsNullOrEmpty(af.BorderDirections))
                    sb.Append(" borders=").Append(CleanLine(af.BorderDirections));
                sb.Append('\n');
            }
            if (shown < total)
                sb.Append("  [truncated: ").Append(NumStr(total - shown)).Append(" more]\n");
        }

        private static void AppendVisibleOthersSection(StringBuilder sb,
            IReadOnlyList<SpatialEntitySummary> items, int cap)
        {
            int total = items == null ? 0 : items.Count;
            int shown = Math.Min(total, Math.Max(0, cap));
            sb.Append("visible_others (").Append(NumStr(total)).Append("):\n");
            if (shown == 0)
            {
                if (total == 0) sb.Append("  (none)\n");
                else sb.Append("  [truncated: ").Append(NumStr(total)).Append(" more]\n");
                return;
            }
            for (int i = 0; i < shown; i++)
            {
                SpatialEntitySummary e = items[i];
                if (e == null) continue;
                sb.Append("  - [").Append(string.IsNullOrEmpty(e.Knowledge) ? "visible" : CleanLine(e.Knowledge))
                  .Append("] ").Append(CleanLine(e.Kind))
                  .Append(" id=").Append(CleanLine(e.Id))
                  .Append(" owner=").Append(string.IsNullOrEmpty(e.OwnerSeatId) ? "neutral" : CleanLine(e.OwnerSeatId))
                  .Append(" allegiance=").Append(CleanLine(e.Allegiance))
                  .Append(" pos=").Append(NumStr(e.X)).Append(',').Append(NumStr(e.Z))
                  .Append(" strength=").Append(NumStr(e.Strength));
                if (!string.IsNullOrEmpty(e.UnitType))
                    sb.Append(" unit=").Append(CleanLine(e.UnitType));
                sb.Append('\n');
            }
            if (shown < total)
                sb.Append("  [truncated: ").Append(NumStr(total - shown)).Append(" more]\n");
        }

        private static void AppendStaleIntelSection(StringBuilder sb,
            IReadOnlyList<SpatialStaleRecord> items, int cap)
        {
            int total = items == null ? 0 : items.Count;
            int shown = Math.Min(total, Math.Max(0, cap));
            sb.Append("stale_intel (").Append(NumStr(total)).Append("):\n");
            if (shown == 0)
            {
                if (total == 0) sb.Append("  (none)\n");
                else sb.Append("  [truncated: ").Append(NumStr(total)).Append(" more]\n");
                return;
            }
            for (int i = 0; i < shown; i++)
            {
                SpatialStaleRecord s = items[i];
                if (s == null) continue;
                sb.Append("  - [").Append(string.IsNullOrEmpty(s.Knowledge) ? "stale" : CleanLine(s.Knowledge))
                  .Append("] ").Append(CleanLine(s.Kind))
                  .Append(" id=").Append(CleanLine(s.Id))
                  .Append(" owner=").Append(string.IsNullOrEmpty(s.OwnerSeatId) ? "unknown" : CleanLine(s.OwnerSeatId))
                  .Append(" last_seen=").Append(NumStr(s.X)).Append(',').Append(NumStr(s.Z))
                  .Append(" season=").Append(NumStr(s.RecordedSeason));
                if (s.ValidUntilSeason >= 0)
                    sb.Append(" valid_until=").Append(NumStr(s.ValidUntilSeason));
                if (!string.IsNullOrEmpty(s.Summary))
                    sb.Append(" note=\"").Append(CleanLine(s.Summary)).Append('"');
                sb.Append('\n');
            }
            if (shown < total)
                sb.Append("  [truncated: ").Append(NumStr(total - shown)).Append(" more]\n");
        }

        private static void AppendObjectivesSection(StringBuilder sb,
            IReadOnlyList<SpatialObjectiveBearing> items, int cap)
        {
            int total = items == null ? 0 : items.Count;
            int shown = Math.Min(total, Math.Max(0, cap));
            sb.Append("objective_bearings (").Append(NumStr(total)).Append("):\n");
            if (shown == 0)
            {
                if (total == 0) sb.Append("  (none)\n");
                else sb.Append("  [truncated: ").Append(NumStr(total)).Append(" more]\n");
                return;
            }
            for (int i = 0; i < shown; i++)
            {
                SpatialObjectiveBearing b = items[i];
                if (b == null) continue;
                sb.Append("  - army=").Append(CleanLine(b.ArmyId))
                  .Append(" -> [").Append(CleanLine(b.Knowledge)).Append("] ")
                  .Append(CleanLine(b.TargetKind)).Append(' ').Append(CleanLine(b.TargetId))
                  .Append(" at ").Append(NumStr(b.TargetX)).Append(',').Append(NumStr(b.TargetZ))
                  .Append(" dist=").Append(NumStr(b.GridDistance))
                  .Append(" dir=").Append(CleanLine(b.Direction))
                  .Append(" (dx=").Append(NumStr(b.DeltaX))
                  .Append(",dz=").Append(NumStr(b.DeltaZ)).Append(")\n");
            }
            if (shown < total)
                sb.Append("  [truncated: ").Append(NumStr(total - shown)).Append(" more]\n");
        }

        private static string NumStr(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
