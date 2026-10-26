namespace LuanShi.Engine
{
    /// <summary>
    /// Pure bottom-block geometry for the researcher spectator panel. The Unity partial owns the
    /// drawing; keeping the row offsets here lets the headless harness prove that the two-line
    /// totals block, the Stop/Resume/+3 seasons row and the run-record line never overlap.
    /// All values are design-space pixels (1600x900).
    /// </summary>
    public static class SpectatorPanelLayout
    {
        public const float PanelHeight = 412f;

        /// <summary>Requests/token totals, then cost: two full-height lines of their own.</summary>
        public const float RequestsRowY = 316f;
        public const float TotalsRowHeight = 18f;
        public const float CostRowY = RequestsRowY + TotalsRowHeight;

        /// <summary>Button row starts below the totals block, with a small breathing gap.</summary>
        public const float ControlsRowY = CostRowY + TotalsRowHeight + 6f;
        public const float ButtonHeight = 28f;

        /// <summary>Run-record line sits below the button row and inside the panel rectangle.</summary>
        public const float SummaryRowY = ControlsRowY + ButtonHeight + 4f;
        public const float SummaryRowHeight = 14f;

        public static bool TotalsClearOfControls => CostRowY + TotalsRowHeight <= ControlsRowY;
        public static bool ControlsClearOfSummary => ControlsRowY + ButtonHeight <= SummaryRowY;
        public static bool SummaryInsidePanel => SummaryRowY + SummaryRowHeight <= PanelHeight;
    }
}
