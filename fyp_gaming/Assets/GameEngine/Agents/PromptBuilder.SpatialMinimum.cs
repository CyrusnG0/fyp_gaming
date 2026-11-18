namespace LuanShi.Engine
{
    public static partial class PromptBuilder
    {
        /// <summary>
        /// Smallest movement section that still lists one host-offered legal destination: no spatial
        /// summaries, one option, and only the armies up to the first one that has an option. Returns
        /// null when the view offers no destination. Nothing is added to what the host already offers.
        /// </summary>
        internal static string RenderMinimalDestinationSection(MovementView view,
            SpatialContextOptions opts, out SpatialTruncationReport report)
        {
            report = null;
            if (view == null || view.Armies == null) return null;
            int armyLimit = 0;
            for (int i = 0; i < view.Armies.Count; i++)
            {
                ArmyMovePlan plan = view.Armies[i];
                if (plan != null && plan.Options != null && plan.Options.Count > 0) { armyLimit = i + 1; break; }
            }
            if (armyLimit == 0) return null;
            opts = opts ?? new SpatialContextOptions { Level = SpatialDetailLevel.Standard };
            return AssembleSpatialSection(opts, view, SpatialTruncationSteps[3], false, 0, 1, armyLimit,
                "section_char_limit", out report);
        }
    }
}
