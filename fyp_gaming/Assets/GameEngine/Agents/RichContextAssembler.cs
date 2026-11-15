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
            sb.Append("]}");
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
    }

    internal sealed class RichContextResult
    {
        public string User;
        public string ObservationJson;
        public RichContextReport Report;
    }

    /// <summary>
    /// Assembles the rich, seat-filtered user prompt. If it exceeds the configured prompt ceiling it
    /// steps down a fixed ladder (prior inquiry results, then history/memory back to the historical
    /// sizes, then events) and the completeness report states the figures actually included.
    /// </summary>
    internal static class RichContextAssembler
    {
        public static RichContextResult Assemble(RichContextInputs input, LlmConfig config)
        {
            RichContextResult last = null;
            for (int level = 0; level <= 3; level++)
            {
                last = Build(input, config, level);
                if (last.Report.Fits) return last;
            }
            return last;
        }

        private static RichContextResult Build(RichContextInputs input, LlmConfig config, int level)
        {
            int historyCap = config.EffectiveHistoryItems;
            int memoryChars = config.EffectiveMemoryChars;
            int events = config.EffectiveEventItems;
            if (level >= 2)
            {
                historyCap = Math.Min(historyCap, LlmTurnRunner.MaxRecentHistory);
                memoryChars = Math.Min(memoryChars, LlmMemoryRetriever.DefaultMaxChars);
            }
            if (level >= 3) events = Math.Min(events, PromptBuilder.MaxPromptObservationEvents);

            Observation obs = input.Observation;
            var report = new RichContextReport { TrimLevel = level, MaxPromptChars = config.EffectiveMaxPromptChars };

            int totalEvents = obs.Events.Count;
            report.Add("observation events (newest kept)", Math.Min(totalEvents, events), totalEvents,
                totalEvents > events ? "older events: use search_history" : null);

            int hFirst = Math.Max(0, input.History.Count - historyCap);
            var history = new List<string>();
            for (int i = hFirst; i < input.History.Count; i++) history.Add(input.History[i]);
            int historyTotal = input.History.Count + input.HistoryDropped;
            report.Add("own action feedback lines (newest kept)", history.Count, historyTotal,
                "feedback lines carry the order and its accept/reject result only, not resulting numbers");

            IReadOnlyList<LlmMemory> memories = LlmMemoryRetriever.Select(input.Memories, obs,
                input.Options, memoryChars);
            report.Add("long-term memories (ranked, character-limited)", memories.Count, input.Memories.Count,
                input.Memories.Count > memories.Count ? "the rest: use recall_memory" : null);

            int shown = SelectPrior(input, config, level, report);
            report.Add("prior inquiry results (newest kept)", shown, input.PriorResults.Count + input.PriorDropped,
                shown < input.PriorResults.Count + input.PriorDropped
                    ? "omitted results are gone from the prompt; repeat the inquiry to read them again" : null);

            report.Add("strategic note", string.IsNullOrEmpty(input.Note) ? 0 : 1,
                string.IsNullOrEmpty(input.Note) ? 0 : 1,
                string.IsNullOrEmpty(input.Note) ? "none saved yet" : "one note (" + input.Note.Length
                    .ToString(CultureInfo.InvariantCulture) + " chars), the latest only");
            AddMovement(input.Movement, report);
            report.Add("candidate orders", input.Options.Count, input.Options.Count, null);
            report.Add("other factions' private information", 0, -1,
                "only what your own seat-filtered observation shows is supplied");

            string movementText = input.Movement == null ? null : PromptBuilder.MovementOptionsText(input.Movement);
            string attackNotice = input.AttackEnabled ? ActionCatalogue.AttackNotice(input.Attacks) : null;
            string section = PromptBuilder.RichSectionText(input.Allowances, report, input.Note);
            string body = PromptBuilder.BuildRichBase(obs, input.Options, history, input.RequestsLeft,
                input.ActionsLeft, input.Version, movementText, memories, input.LatestInquiryResult,
                input.Persona, attackNotice, events);
            string user = PromptBuilder.InsertBeforeCandidates(body, section);
            report.PromptChars = input.SystemChars + user.Length;
            report.Fits = report.PromptChars <= report.MaxPromptChars;
            return new RichContextResult
            {
                User = user,
                ObservationJson = PromptBuilder.BoundedObservationJson(obs, events),
                Report = report,
            };
        }

        private static int SelectPrior(RichContextInputs input, LlmConfig config, int level,
            RichContextReport report)
        {
            if (level >= 1 || config.ContextInquiryChars <= 0) return 0;
            int cap = Math.Max(0, config.ContextCrossTurnInquiryResults);
            int budget = config.ContextInquiryChars;
            var picked = new List<string>();
            for (int i = input.PriorResults.Count - 1; i >= 0 && picked.Count < cap; i--)
            {
                string line = input.PriorResults[i].Line();
                if (line.Length > budget) break;
                budget -= line.Length;
                picked.Insert(0, line);
            }
            report.PriorResults.AddRange(picked);
            return picked.Count;
        }

        private static void AddMovement(MovementView movement, RichContextReport report)
        {
            if (movement == null || movement.Armies == null || movement.Armies.Count == 0) return;
            int shown = 0, reachable = 0;
            foreach (ArmyMovePlan plan in movement.Armies)
            {
                if (plan == null) continue;
                shown += plan.Options == null ? 0 : plan.Options.Count;
                reachable += Math.Max(plan.Options == null ? 0 : plan.Options.Count, plan.TotalReachable);
            }
            report.Add("march destinations across your armies", shown, reachable,
                shown < reachable
                    ? "the planner lists only these; omitted destinations are not listed by any tool, but the host checks a submitted destination on its own, so a legal visible one you name from the map may be accepted"
                    : null);
        }
    }
}
