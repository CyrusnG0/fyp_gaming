using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>One earlier inquiry result a seat may revisit, with when it was obtained.</summary>
    internal sealed class RichInquiryEntry
    {
        public int Season;
        public string Tool;
        public string Json;

        public string Line()
            => "[season " + Season.ToString(CultureInfo.InvariantCulture) + ", " + Tool
               + ", observed then and possibly out of date] " + Json;
    }

    /// <summary>
    /// What one rich prompt actually contains. Counts are measured on the assembled prompt, never
    /// copied from a limit, so a section that was trimmed to fit reports the trimmed figure.
    /// </summary>
    internal sealed class RichContextReport
    {
        internal sealed class Section
        {
            public string Name;
            public int Included;
            public int Available;      // -1 = the total is not known to the host
            public string Note;
        }

        public readonly List<Section> Sections = new List<Section>();
        public readonly List<string> PriorResults = new List<string>();
        public int TrimLevel;
        public bool Fits;
        public int PromptChars;
        public int MaxPromptChars;
        public SpatialTruncationReport SpatialTruncation;

        // Counts-only failure classification (never serialized into the model-facing prompt or
        // ToJson, so prompts that fit at levels 0-4 stay byte-identical).
        public string UnfitReason;
        public int InquiryOriginalChars = -1;
        public int InquiryKeptChars = -1;

        public void Add(string name, int included, int available, string note)
            => Sections.Add(new Section { Name = name, Included = included, Available = available, Note = note });

        public string ToPromptText()
        {
            var sb = new StringBuilder(900);
            sb.Append("CONTEXT COMPLETENESS (measured on this prompt; anything not listed was not supplied):\n");
            foreach (Section s in Sections)
            {
                sb.Append("- ").Append(s.Name).Append(": ").Append(s.Included.ToString(CultureInfo.InvariantCulture));
                if (s.Available >= 0)
                {
                    sb.Append(" of ").Append(s.Available.ToString(CultureInfo.InvariantCulture));
                    if (s.Included < s.Available) sb.Append(" (INCOMPLETE)");
                }
                else sb.Append(" (total not known to the host)");
                if (!string.IsNullOrEmpty(s.Note)) sb.Append(". ").Append(s.Note);
                sb.Append('\n');
            }
            if (TrimLevel > 0)
                sb.Append("- Prompt-size trim level ").Append(TrimLevel.ToString(CultureInfo.InvariantCulture))
                  .Append(": sections above were reduced to stay under the prompt ceiling.\n");
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"trim_level\":").Append(TrimLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"fits\":").Append(Fits ? "true" : "false");
            sb.Append(",\"prompt_chars\":").Append(PromptChars.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"max_prompt_chars\":").Append(MaxPromptChars.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"sections\":[");
            for (int i = 0; i < Sections.Count; i++)
            {
                Section s = Sections[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(MiniJson.Str(s.Name));
                sb.Append(",\"included\":").Append(s.Included.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"available\":").Append(s.Available < 0 ? "null"
                    : s.Available.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"truncated\":").Append(s.Available >= 0 && s.Included < s.Available ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            if (SpatialTruncation != null)
                sb.Append(",\"spatial_truncation\":").Append(SpatialTruncation.ToJson());
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Everything one rich prompt is assembled from. All of it is already seat-filtered.</summary>
    internal sealed class RichContextInputs
    {
        public Observation Observation;
        public IReadOnlyList<ActionOption> Options;
        public MovementView Movement;
        public IReadOnlyList<string> History;
        public int HistoryDropped;
        public IReadOnlyList<LlmMemory> Memories;
        public IReadOnlyList<RichInquiryEntry> PriorResults;   // oldest first, excluding the latest result
        public int PriorDropped;
        public string LatestInquiryResult;
        public string Note;
        public LlmPersona Persona;
        public bool AttackEnabled;
        public AttackView Attacks;
        public string Version;
        public int SystemChars;
        public int RequestsLeft;
        public int ActionsLeft;
        public PromptBuilder.RichAllowances Allowances;
        public SpatialContextOptions SpatialOptions;
        /// <summary>Chars reserved for a section appended after assembly (0 when there is none).</summary>
        public int ReservedChars;
    }

    internal sealed class RichContextResult
    {
        public string User;
        public string ObservationJson;
        public RichContextReport Report;
        public PromptSizeReport SizeReport;
        public SpatialTruncationReport SpatialTruncation => Report?.SpatialTruncation;
    }

}
