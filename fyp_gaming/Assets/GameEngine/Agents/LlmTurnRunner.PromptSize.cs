namespace LuanShi.Engine
{
    /// <summary>
    /// Counts-only prompt-size diagnostics for the rich assembler and the final system+user check.
    /// Nothing here reads prompt text back out; the recorded JSON is bounded and secret-free.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        private PromptSizeReport _lastRichSizeReport;

        /// <summary>
        /// JSON of the failure-stage prompt-size report (counts and section completeness only), or
        /// null while no prompt-size failure has occurred in this turn. Reset at the start of a turn.
        /// </summary>
        public string LastPromptSizeJson { get; private set; }

        private void ResetPromptSizeState()
        {
            LastPromptSizeJson = null;
            _lastRichSizeReport = null;
        }

        private void RecordPromptSizeFinalCheck(int systemChars, int userChars, int limitChars,
            string diplomacySection)
        {
            int diplomacyChars = diplomacySection == null ? 0 : diplomacySection.Length;
            string reason = diplomacyChars > 0 ? "diplomacy_reservation" : "final_check_overflow";
            PromptSizeReport size = _lastRichSizeReport != null
                ? _lastRichSizeReport.FinalCheck(systemChars, userChars, limitChars,
                    diplomacyChars, reason)
                : PromptSizeReport.FinalOnly(systemChars, userChars, limitChars, diplomacyChars, reason);
            LastPromptSizeJson = size.ToJson();
        }
    }
}
