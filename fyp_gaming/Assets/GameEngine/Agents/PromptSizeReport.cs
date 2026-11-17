using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Counts-only prompt-size evidence for one assembly or final size check. It never contains
    /// prompt text, model output, secrets or canonical state; the JSON is bounded under 2 KB.
    /// </summary>
    internal sealed class PromptSizeReport
    {
        private const int MaxJsonChars = 2000;

        public string Stage = "rich_assembler";   // "rich_assembler" | "final_check"
        public int TotalChars;
        public int LimitChars;
        public int SystemChars;
        public int UserChars;
        public int DiplomacySectionChars;
        public int ReservedChars;
        public int TrimLevel = -1;
        public int ObservationChars = -1;
        public int HistoryChars = -1;
        public int MemoryChars = -1;
        public int MovementChars = -1;
        public int CandidateChars = -1;
        public int RichSectionChars = -1;
        public int InquiryChars = -1;
        public string UnfitReason;                       // null when Fits, or when unknown
        public int InquiryResultCharsOriginal = -1;
        public int InquiryResultCharsKept = -1;
        public List<Section> Sections = new List<Section>();

        internal sealed class Section
        {
            public string Name;
            public int Included;
            public int Available;
        }

        /// <summary>Breakdown taken from one rich assembler result (counts only).</summary>
        internal static PromptSizeReport RichAssembler(RichContextReport report, RichContextInputs input,
            int observationChars, int historyChars, int memoryChars, int movementChars,
            int candidateChars, int richSectionChars, int inquiryChars)
        {
            var size = new PromptSizeReport
            {
                Stage = "rich_assembler",
                TotalChars = report.PromptChars,
                LimitChars = report.MaxPromptChars,
                SystemChars = input.SystemChars,
                UserChars = report.PromptChars - input.SystemChars,
                DiplomacySectionChars = Math.Max(0, input.ReservedChars),
                ReservedChars = Math.Max(0, input.ReservedChars),
                TrimLevel = report.TrimLevel,
                ObservationChars = observationChars,
                HistoryChars = historyChars,
                MemoryChars = memoryChars,
                MovementChars = movementChars,
                CandidateChars = candidateChars,
                RichSectionChars = richSectionChars,
                InquiryChars = inquiryChars,
                UnfitReason = report.UnfitReason,
                InquiryResultCharsOriginal = report.InquiryOriginalChars,
                InquiryResultCharsKept = report.InquiryKeptChars,
            };
            if (report.Sections != null)
            {
                foreach (RichContextReport.Section section in report.Sections)
                    if (section != null)
                        size.Sections.Add(new Section
                        {
                            Name = section.Name,
                            Included = section.Included,
                            Available = section.Available,
                        });
            }
            return size;
        }

        /// <summary>
        /// The same breakdown measured after the last prompt mutation (for example the inserted
        /// diplomacy section), with the final system+user check's own totals.
        /// </summary>
        internal PromptSizeReport FinalCheck(int systemChars, int userChars, int limitChars,
            int diplomacySectionChars, string unfitReason)
        {
            return new PromptSizeReport
            {
                Stage = "final_check",
                TotalChars = systemChars + userChars,
                LimitChars = limitChars,
                SystemChars = systemChars,
                UserChars = userChars,
                DiplomacySectionChars = diplomacySectionChars,
                ReservedChars = 0,
                TrimLevel = TrimLevel,
                ObservationChars = ObservationChars,
                HistoryChars = HistoryChars,
                MemoryChars = MemoryChars,
                MovementChars = MovementChars,
                CandidateChars = CandidateChars,
                RichSectionChars = RichSectionChars,
                InquiryChars = InquiryChars,
                UnfitReason = unfitReason,
                InquiryResultCharsOriginal = InquiryResultCharsOriginal,
                InquiryResultCharsKept = InquiryResultCharsKept,
                Sections = Sections,
            };
        }

        internal static PromptSizeReport FinalOnly(int systemChars, int userChars, int limitChars,
            int diplomacySectionChars, string unfitReason)
            => new PromptSizeReport
            {
                Stage = "final_check",
                TotalChars = systemChars + userChars,
                LimitChars = limitChars,
                SystemChars = systemChars,
                UserChars = userChars,
                DiplomacySectionChars = diplomacySectionChars,
                ReservedChars = 0,
                TrimLevel = -1,
                UnfitReason = unfitReason,
            };

        public string ToJson()
        {
            var sb = new StringBuilder(896);
            sb.Append("{\"stage\":").Append(MiniJson.Str(Stage));
            sb.Append(",\"total_chars\":").Append(Num(TotalChars));
            sb.Append(",\"limit_chars\":").Append(Num(LimitChars));
            sb.Append(",\"system_chars\":").Append(Num(SystemChars));
            sb.Append(",\"user_chars\":").Append(Num(UserChars));
            sb.Append(",\"diplomacy_section_chars\":").Append(Num(DiplomacySectionChars));
            sb.Append(",\"reserved_chars\":").Append(Num(ReservedChars));
            sb.Append(",\"trim_level\":").Append(Num(TrimLevel));
            sb.Append(",\"unfit_reason\":")
              .Append(UnfitReason == null ? "null" : MiniJson.Str(UnfitReason));
            sb.Append(",\"inquiry_result_chars_original\":").Append(Num(InquiryResultCharsOriginal));
            sb.Append(",\"inquiry_result_chars_kept\":").Append(Num(InquiryResultCharsKept));
            sb.Append(",\"section_chars\":{");
            sb.Append("\"observation\":").Append(Num(ObservationChars));
            sb.Append(",\"history\":").Append(Num(HistoryChars));
            sb.Append(",\"memories\":").Append(Num(MemoryChars));
            sb.Append(",\"movement\":").Append(Num(MovementChars));
            sb.Append(",\"candidates\":").Append(Num(CandidateChars));
            sb.Append(",\"rich_section\":").Append(Num(RichSectionChars));
            sb.Append(",\"inquiry\":").Append(Num(InquiryChars));
            sb.Append('}');
            sb.Append(",\"sections\":[");
            for (int i = 0; i < Sections.Count; i++)
            {
                Section section = Sections[i];
                if (section == null) continue;
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(MiniJson.Str(section.Name));
                sb.Append(",\"included\":").Append(Num(section.Included));
                sb.Append(",\"available\":").Append(Num(section.Available));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.Length <= MaxJsonChars ? sb.ToString() : MinimalJson();
        }

        /// <summary>Valid, bounded fallback if an unexpectedly long section list is supplied.</summary>
        private string MinimalJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"stage\":").Append(MiniJson.Str(Stage));
            sb.Append(",\"total_chars\":").Append(Num(TotalChars));
            sb.Append(",\"limit_chars\":").Append(Num(LimitChars));
            sb.Append(",\"system_chars\":").Append(Num(SystemChars));
            sb.Append(",\"user_chars\":").Append(Num(UserChars));
            sb.Append(",\"diplomacy_section_chars\":").Append(Num(DiplomacySectionChars));
            sb.Append(",\"reserved_chars\":").Append(Num(ReservedChars));
            sb.Append(",\"trim_level\":").Append(Num(TrimLevel));
            sb.Append(",\"unfit_reason\":")
              .Append(UnfitReason == null ? "null" : MiniJson.Str(UnfitReason));
            sb.Append(",\"inquiry_result_chars_original\":").Append(Num(InquiryResultCharsOriginal));
            sb.Append(",\"inquiry_result_chars_kept\":").Append(Num(InquiryResultCharsKept));
            sb.Append('}');
            return sb.ToString();
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
