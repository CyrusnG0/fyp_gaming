using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Bounded, deterministic rendering of the seat-safe <see cref="MovementView"/>. Only values the
    /// seat can already observe are printed: its own army identity/position, its marches and budget,
    /// and in-sight destination cells. No canonical state is consulted here.
    /// </summary>
    public static partial class PromptBuilder
    {
        public const string MovementHeader =
            "\nMOVEMENT OPTIONS (candidates, revalidated by the engine):\n";

        /// <summary>
        /// Hard cap on a rendered option note. Engine notes are already short and safe (terrain /
        /// visibility words only); this keeps the bounded prompt bounded even for a hand-built view.
        /// </summary>
        public const int MaxOptionNoteChars = 64;

        /// <summary>Full section (every advertised option, including any engine-side cap).</summary>
        public static string MovementOptionsText(MovementView view)
            => MovementOptionsText(view, int.MaxValue);

        /// <summary>
        /// Renders at most <paramref name="maxOptionsPerArmy"/> options per army, keeping each
        /// army's stable prefix (captures first). A counted note follows when options were dropped.
        /// </summary>
        public static string MovementOptionsText(MovementView view, int maxOptionsPerArmy)
            => MovementOptionsText(view, maxOptionsPerArmy, int.MaxValue);

        /// <summary>
        /// Bounded form used by the prompt-size fallback: also keeps only the first
        /// <paramref name="maxArmies"/> armies (ordered by army id) and notes the remainder.
        /// Deterministic; captures stay first because they are first within each army's prefix.
        /// </summary>
        public static string MovementOptionsText(MovementView view, int maxOptionsPerArmy, int maxArmies)
        {
            var sb = new StringBuilder(512);
            sb.Append(MovementHeader);
            if (view == null || view.Armies == null || view.Armies.Count == 0)
            {
                sb.Append("(none)\n");
                return sb.ToString();
            }

            int limit = maxOptionsPerArmy < 0 ? 0 : maxOptionsPerArmy;
            int totalArmies = view.Armies.Count;
            int takeArmies = Math.Min(totalArmies, maxArmies < 0 ? 0 : maxArmies);
            for (int armyIndex = 0; armyIndex < takeArmies; armyIndex++)
            {
                ArmyMovePlan plan = view.Armies[armyIndex];
                if (plan == null) continue;
                sb.Append("- army_id=").Append(Safe(plan.ArmyId))
                  .Append(" unit_type=").Append(Safe(plan.UnitType))
                  .Append(" position=").Append(Int(plan.X)).Append(',').Append(Int(plan.Z))
                  .Append(" marches_left=").Append(Int(plan.MarchesLeft))
                  .Append(" budget=").Append(Int(plan.Budget));
                if (!plan.CanMarch && !string.IsNullOrEmpty(plan.BlockedReason))
                    sb.Append(" blocked=\"").Append(Safe(plan.BlockedReason)).Append('"');
                sb.Append('\n');

                int available = plan.Options == null ? 0 : plan.Options.Count;
                int shown = Math.Min(available, limit);
                sb.Append("  options:");
                if (shown == 0)
                {
                    sb.Append(" (none)\n");
                }
                else
                {
                    sb.Append(' ');
                    for (int i = 0; i < shown; i++)
                    {
                        MoveOption option = plan.Options[i];
                        if (option == null) continue;
                        if (i > 0) sb.Append("; ");
                        sb.Append(Int(option.X)).Append(',').Append(Int(option.Z))
                          .Append(" cost=").Append(Int(option.PathCost))
                          .Append(' ').Append(Safe(option.Kind));
                        string note = NoteText(option.Note);
                        if (note.Length > 0) sb.Append(" - ").Append(note);
                    }
                    sb.Append('\n');
                }

                int hidden = Math.Max(0, plan.TotalReachable - shown);
                if (hidden > 0)
                    sb.Append("  [truncated: ").Append(Int(hidden)).Append(" more]\n");
            }
            if (takeArmies < totalArmies)
                sb.Append("[truncated: ").Append(Int(totalArmies - takeArmies))
                  .Append(" more armies]\n");
            return sb.ToString();
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(char.IsControl(c) || c == '"' ? '_' : c);
            return sb.ToString();
        }

        /// <summary>
        /// Compact, bounded one-line note. The engine's notes are short and contain no ids or hidden
        /// facts; ';' is folded to ',' so it cannot be mistaken for an option separator.
        /// </summary>
        private static string NoteText(string value)
        {
            string safe = Safe(value).Replace(';', ',');
            if (safe.Length > MaxOptionNoteChars)
                safe = safe.Substring(0, MaxOptionNoteChars);
            return safe.Trim();
        }
    }
}
