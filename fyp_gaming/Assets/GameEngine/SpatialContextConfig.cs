using System;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Configurable map-context detail presets for experiments and production prompting.</summary>
    public enum SpatialDetailLevel
    {
        Off = 0,
        Standard = 1,
        Rich = 2,
    }

    /// <summary>
    /// Configurable map information bounds and truncation limits for spatial prompting.
    /// Zero or non-positive limits fall back to the preset defaults (Standard: bounded 12 moves
    /// per army / 24 own / 16 visible / 8 stale / 12 bearings / 16 army frontiers; Rich: all
    /// visible map context and all legal visible move options that fit within ProviderCharLimit).
    /// </summary>
    public sealed class SpatialContextOptions
    {
        public const int StandardOwnCap = 24;
        public const int StandardVisibleCap = 16;
        public const int StandardStaleCap = 8;
        public const int StandardObjectivesCap = 12;
        public const int StandardArmyFrontiersCap = 16;
        public const int StandardVisibleRangesCap = 12;

        public static readonly SpatialContextOptions OffDefault = new SpatialContextOptions();

        public SpatialDetailLevel Level = SpatialDetailLevel.Off;
        public int MaxMoveOptionsPerArmy;
        public int MaxOwnEntities;
        public int MaxVisibleOthers;
        public int MaxStaleIntel;
        public int MaxObjectives;
        public int MaxArmyFrontiers;
        public int MaxVisibleCellRanges;
        public bool IncludeVisibleCellRanges;
        public int ProviderCharLimit;
        public int MaxSectionChars;

        public bool IsEnabled => Level != SpatialDetailLevel.Off;

        public string LevelKey()
        {
            switch (Level)
            {
                case SpatialDetailLevel.Off: return "off";
                case SpatialDetailLevel.Rich: return "rich";
                default: return "standard";
            }
        }

        public int EffectiveProviderCharLimit()
            => ProviderCharLimit > 0 ? ProviderCharLimit : PromptBuilder.MaxPromptChars;

        public int EffectiveMoveCap(int callerDefault = MovementPlanner.DefaultMaxOptionsPerArmy)
        {
            if (Level == SpatialDetailLevel.Off) return callerDefault > 0 ? callerDefault : MovementPlanner.DefaultMaxOptionsPerArmy;
            if (MaxMoveOptionsPerArmy > 0) return MaxMoveOptionsPerArmy;
            if (Level == SpatialDetailLevel.Rich) return int.MaxValue;
            return callerDefault > 0 ? callerDefault : MovementPlanner.DefaultMaxOptionsPerArmy;
        }

        public int EffectiveOwnCap()
            => MaxOwnEntities > 0 ? MaxOwnEntities : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardOwnCap);

        public int EffectiveVisibleCap()
            => MaxVisibleOthers > 0 ? MaxVisibleOthers : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardVisibleCap);

        public int EffectiveStaleCap()
            => MaxStaleIntel > 0 ? MaxStaleIntel : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardStaleCap);

        public int EffectiveObjectivesCap()
            => MaxObjectives > 0 ? MaxObjectives : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardObjectivesCap);

        public int EffectiveArmyFrontiersCap()
            => MaxArmyFrontiers > 0 ? MaxArmyFrontiers : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardArmyFrontiersCap);

        public int EffectiveVisibleRangesCap()
            => MaxVisibleCellRanges > 0 ? MaxVisibleCellRanges
                : (Level == SpatialDetailLevel.Rich ? int.MaxValue : StandardVisibleRangesCap);

        public bool EffectiveIncludeVisibleRanges()
            => Level != SpatialDetailLevel.Off && (Level == SpatialDetailLevel.Rich || IncludeVisibleCellRanges);

        public SpatialContextOptions Clone()
            => new SpatialContextOptions
            {
                Level = Level,
                MaxMoveOptionsPerArmy = MaxMoveOptionsPerArmy,
                MaxOwnEntities = MaxOwnEntities,
                MaxVisibleOthers = MaxVisibleOthers,
                MaxStaleIntel = MaxStaleIntel,
                MaxObjectives = MaxObjectives,
                MaxArmyFrontiers = MaxArmyFrontiers,
                MaxVisibleCellRanges = MaxVisibleCellRanges,
                IncludeVisibleCellRanges = IncludeVisibleCellRanges,
                ProviderCharLimit = ProviderCharLimit,
                MaxSectionChars = MaxSectionChars,
            };

        public string ToSummaryLine()
        {
            var sb = new StringBuilder(128);
            sb.Append("context_config: level=").Append(LevelKey())
              .Append(" provider_char_limit=").Append(EffectiveProviderCharLimit().ToString(CultureInfo.InvariantCulture));
            if (MaxSectionChars > 0)
                sb.Append(" section_char_limit=").Append(MaxSectionChars.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"level\":").Append(MiniJson.Str(LevelKey()));
            sb.Append(",\"provider_char_limit\":").Append(MiniJson.Num(EffectiveProviderCharLimit()));
            sb.Append(",\"max_section_chars\":").Append(MiniJson.Num(MaxSectionChars));
            sb.Append(",\"max_move_options_per_army\":").Append(MiniJson.Num(MaxMoveOptionsPerArmy));
            sb.Append(",\"include_visible_cell_ranges\":").Append(EffectiveIncludeVisibleRanges() ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Explicit accounting of any spatial or movement items dropped to fit limits.</summary>
    public sealed class SpatialTruncationReport
    {
        public string Level = "standard";
        public int ProviderCharLimit;
        public int SectionCharLimit;
        public int SystemChars;
        public int UserChars;
        public int TotalChars;
        public bool Truncated;
        public int OwnEntitiesDropped;
        public int VisibleOthersDropped;
        public int StaleIntelDropped;
        public int ObjectivesDropped;
        public int ArmyFrontiersDropped;
        public int VisibleCellRangesDropped;
        public int MoveOptionsDropped;
        public int ArmiesDropped;
        public string Reason = "none";

        public bool AnyDropped => OwnEntitiesDropped > 0 || VisibleOthersDropped > 0
            || StaleIntelDropped > 0 || ObjectivesDropped > 0 || ArmyFrontiersDropped > 0
            || VisibleCellRangesDropped > 0 || MoveOptionsDropped > 0 || ArmiesDropped > 0;

        public string SummaryLine()
        {
            string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);
            return "spatial_truncation: truncated=" + (Truncated ? "true" : "false")
                + " reason=" + (string.IsNullOrEmpty(Reason) ? "none" : Reason)
                + " provider_char_limit=" + Inv(ProviderCharLimit)
                + " section_char_limit=" + Inv(SectionCharLimit)
                + " own_dropped=" + Inv(OwnEntitiesDropped)
                + " visible_dropped=" + Inv(VisibleOthersDropped)
                + " stale_dropped=" + Inv(StaleIntelDropped)
                + " objectives_dropped=" + Inv(ObjectivesDropped)
                + " frontiers_dropped=" + Inv(ArmyFrontiersDropped)
                + " visible_rows_dropped=" + Inv(VisibleCellRangesDropped)
                + " move_options_dropped=" + Inv(MoveOptionsDropped)
                + " armies_dropped=" + Inv(ArmiesDropped);
        }

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"level\":").Append(MiniJson.Str(Level));
            sb.Append(",\"truncated\":").Append(Truncated ? "true" : "false");
            sb.Append(",\"reason\":").Append(MiniJson.Str(Reason));
            sb.Append(",\"provider_char_limit\":").Append(MiniJson.Num(ProviderCharLimit));
            sb.Append(",\"section_char_limit\":").Append(MiniJson.Num(SectionCharLimit));
            sb.Append(",\"system_chars\":").Append(MiniJson.Num(SystemChars));
            sb.Append(",\"user_chars\":").Append(MiniJson.Num(UserChars));
            sb.Append(",\"total_chars\":").Append(MiniJson.Num(TotalChars));
            sb.Append(",\"own_dropped\":").Append(MiniJson.Num(OwnEntitiesDropped));
            sb.Append(",\"visible_dropped\":").Append(MiniJson.Num(VisibleOthersDropped));
            sb.Append(",\"stale_dropped\":").Append(MiniJson.Num(StaleIntelDropped));
            sb.Append(",\"objectives_dropped\":").Append(MiniJson.Num(ObjectivesDropped));
            sb.Append(",\"frontiers_dropped\":").Append(MiniJson.Num(ArmyFrontiersDropped));
            sb.Append(",\"visible_rows_dropped\":").Append(MiniJson.Num(VisibleCellRangesDropped));
            sb.Append(",\"move_options_dropped\":").Append(MiniJson.Num(MoveOptionsDropped));
            sb.Append(",\"armies_dropped\":").Append(MiniJson.Num(ArmiesDropped));
            sb.Append('}');
            return sb.ToString();
        }
    }

    internal static class SpatialTruncationBuilder
    {
        public static SpatialTruncationReport Build(SpatialContextOptions opts,
            SpatialContextView spatial, MovementView movement, int[] caps, bool includeRows,
            int maxRows, int moveLimit, int armyLimit, int sysChars, int userChars,
            string limitReason)
        {
            opts = opts ?? new SpatialContextOptions();
            int ownTot = spatial?.OwnEntities?.Count ?? 0;
            int visTot = spatial?.VisibleOthers?.Count ?? 0;
            int staleTot = spatial?.StaleIntel?.Count ?? 0;
            int objTot = spatial?.ObjectiveBearings?.Count ?? 0;
            int frTot = spatial?.ArmyFrontiers?.Count ?? 0;
            int rowTot = opts.EffectiveIncludeVisibleRanges() && spatial?.Frontier?.VisibleRowRanges != null
                ? spatial.Frontier.VisibleRowRanges.Count : 0;

            int ownDrop = Math.Max(0, ownTot - Math.Min(ownTot, Math.Max(0, caps[0])));
            int visDrop = Math.Max(0, visTot - Math.Min(visTot, Math.Max(0, caps[1])));
            int staleDrop = Math.Max(0, staleTot - Math.Min(staleTot, Math.Max(0, caps[2])));
            int objDrop = Math.Max(0, objTot - Math.Min(objTot, Math.Max(0, caps[3])));
            int frDrop = Math.Max(0, frTot - Math.Min(frTot, Math.Max(0, caps[4])));
            int rowShown = includeRows ? Math.Min(rowTot, Math.Max(0, maxRows)) : 0;
            int rowDrop = Math.Max(0, rowTot - rowShown);

            int moveDrop = 0, armiesDrop = 0;
            if (movement != null && movement.Armies != null)
            {
                int totalArmies = movement.Armies.Count;
                int shownArmies = armyLimit < 0 ? 0 : Math.Min(totalArmies, armyLimit);
                int lim = moveLimit < 0 ? 0 : moveLimit;
                for (int i = 0; i < totalArmies; i++)
                {
                    ArmyMovePlan plan = movement.Armies[i];
                    if (plan == null) continue;
                    int avail = plan.Options == null ? 0 : plan.Options.Count;
                    int reachable = Math.Max(plan.TotalReachable, avail);
                    if (i >= shownArmies)
                    {
                        armiesDrop++;
                        moveDrop += reachable;
                    }
                    else
                    {
                        moveDrop += Math.Max(0, reachable - Math.Min(avail, lim));
                    }
                }
            }

            bool anyEntityDrop = ownDrop > 0 || visDrop > 0 || staleDrop > 0
                || objDrop > 0 || frDrop > 0 || rowDrop > 0;
            bool anyMoveDrop = moveDrop > 0 || armiesDrop > 0;
            bool truncated = anyEntityDrop || anyMoveDrop || !string.IsNullOrEmpty(limitReason);
            string reason = !string.IsNullOrEmpty(limitReason) && (anyEntityDrop || anyMoveDrop)
                ? limitReason
                : (anyEntityDrop ? "entity_cap" : (anyMoveDrop ? "move_option_cap" : "none"));

            return new SpatialTruncationReport
            {
                Level = opts.LevelKey(),
                ProviderCharLimit = opts.EffectiveProviderCharLimit(),
                SectionCharLimit = opts.MaxSectionChars,
                SystemChars = sysChars,
                UserChars = userChars,
                TotalChars = sysChars + userChars,
                Truncated = truncated,
                OwnEntitiesDropped = ownDrop,
                VisibleOthersDropped = visDrop,
                StaleIntelDropped = staleDrop,
                ObjectivesDropped = objDrop,
                ArmyFrontiersDropped = frDrop,
                VisibleCellRangesDropped = rowDrop,
                MoveOptionsDropped = moveDrop,
                ArmiesDropped = armiesDrop,
                Reason = reason,
            };
        }
    }
}
