using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    internal static class RichContextAssembler
    {
        public static RichContextResult Assemble(RichContextInputs input, LlmConfig config)
        {
            RichContextResult last = null;
            for (int level = 0; level <= 5; level++)
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
            if (level >= 4)
            {
                // Last-resort trim: lowest-priority context only. LatestInquiryResult, candidate
                // orders and the allowance/budget text are deliberately never reduced here.
                events = Math.Min(events, 10);
                historyCap = Math.Min(historyCap, 6);
                memoryChars = Math.Max(0, memoryChars / 2);
            }

            Observation obs = input.Observation;
            int effectiveMaxChars = input.SpatialOptions != null && input.SpatialOptions.IsEnabled
                && input.SpatialOptions.ProviderCharLimit > 0
                ? Math.Min(config.EffectiveMaxPromptChars, input.SpatialOptions.ProviderCharLimit)
                : config.EffectiveMaxPromptChars;
            int reservedChars = Math.Max(0, input.ReservedChars);
            int fitLimit = Math.Max(0, effectiveMaxChars - reservedChars);
            var report = new RichContextReport { TrimLevel = level, MaxPromptChars = fitLimit };
            report.InquiryOriginalChars = input.LatestInquiryResult == null
                ? 0 : input.LatestInquiryResult.Length;
            report.InquiryKeptChars = report.InquiryOriginalChars;

            int totalEvents = obs.Events.Count;
            string eventNote = totalEvents > events ? "older events: use search_history" : null;
            if (level >= 4 && eventNote != null)
                eventNote += "; further reduced for the prompt-size ceiling";
            report.Add("observation events (newest kept)", Math.Min(totalEvents, events), totalEvents,
                eventNote);

            int hFirst = Math.Max(0, input.History.Count - historyCap);
            var history = new List<string>();
            for (int i = hFirst; i < input.History.Count; i++) history.Add(input.History[i]);
            int historyTotal = input.History.Count + input.HistoryDropped;
            string historyNote =
                "feedback lines carry the order and its accept/reject result only, not resulting numbers";
            if (level >= 4 && history.Count < historyTotal)
                historyNote += "; oldest feedback lines omitted to keep this prompt under the ceiling";
            report.Add("own action feedback lines (newest kept)", history.Count, historyTotal, historyNote);

            IReadOnlyList<LlmMemory> memories = LlmMemoryRetriever.Select(input.Memories, obs,
                input.Options, memoryChars);
            string memoryNote = input.Memories.Count > memories.Count ? "the rest: use recall_memory" : null;
            if (level >= 4 && memoryNote != null)
                memoryNote += "; memory budget halved for the prompt-size ceiling";
            report.Add("long-term memories (ranked, character-limited)", memories.Count, input.Memories.Count,
                memoryNote);

            int shown = SelectPrior(input, config, level, report);
            report.Add("prior inquiry results (newest kept)", shown, input.PriorResults.Count + input.PriorDropped,
                shown < input.PriorResults.Count + input.PriorDropped
                    ? "omitted results are gone from the prompt; repeat the inquiry to read them again" : null);

            report.Add("strategic note", string.IsNullOrEmpty(input.Note) ? 0 : 1,
                string.IsNullOrEmpty(input.Note) ? 0 : 1,
                string.IsNullOrEmpty(input.Note) ? "none saved yet" : "one note (" + input.Note.Length
                    .ToString(CultureInfo.InvariantCulture) + " chars), the latest only");

            int inquiryOriginalChars = input.LatestInquiryResult == null
                ? 0 : input.LatestInquiryResult.Length;
            int inquiryKeptChars = inquiryOriginalChars;
            string boundedInquiry = input.LatestInquiryResult;
            int inquirySectionIndex = -1;
            if (level >= 5 && inquiryOriginalChars > 0)
            {
                inquirySectionIndex = report.Sections.Count;
                report.Add("latest inquiry result", inquiryKeptChars, inquiryOriginalChars, null);
            }

            bool spatialOn = input.Movement != null && input.Movement.SpatialContext != null
                && input.SpatialOptions != null && input.SpatialOptions.IsEnabled;
            SpatialTruncationReport spatialReport = null;
            string movementText = input.Movement == null ? null
                : (spatialOn
                    ? PromptBuilder.MovementOptionsText(input.Movement, int.MaxValue, int.MaxValue,
                        input.SpatialOptions, out spatialReport)
                    : PromptBuilder.MovementOptionsText(input.Movement));

            int moveSectionIndex = report.Sections.Count;
            AddMovement(input.Movement, report, spatialReport);
            report.Add("candidate orders", input.Options.Count, input.Options.Count, null);
            report.Add("other factions' private information", 0, -1,
                "only what your own seat-filtered observation shows is supplied");

            string attackNotice = input.AttackEnabled ? ActionCatalogue.AttackNotice(input.Attacks) : null;
            string section = null;
            string body = null;
            string user = null;
            void Rebuild(string inquiryText)
            {
                section = PromptBuilder.RichSectionText(input.Allowances, report, input.Note);
                body = PromptBuilder.BuildRichBase(obs, input.Options, history, input.RequestsLeft,
                    input.ActionsLeft, input.Version, movementText, memories, inquiryText,
                    input.Persona, attackNotice, events);
                user = PromptBuilder.InsertBeforeCandidates(body, section);
            }
            Rebuild(boundedInquiry);
            if (spatialOn && level >= 3 && input.SystemChars + user.Length > fitLimit)
            {
                for (int pass = 0; pass < 2 && input.SystemChars + user.Length > fitLimit; pass++)
                {
                    int excess = input.SystemChars + user.Length - fitLimit;
                    // Budget 0 means "no budget" to the renderer (it would return the FULL section);
                    // reserve one char so the smallest section is selected instead.
                    int sectionBudget = Math.Max(1,
                        (movementText == null ? 0 : movementText.Length) - excess);
                    movementText = PromptBuilder.RenderSpatialMovementSectionForBudget(input.Movement,
                        input.SpatialOptions, sectionBudget, out spatialReport);
                    if (moveSectionIndex < report.Sections.Count)
                    {
                        report.Sections.RemoveAt(moveSectionIndex);
                        AddMovement(input.Movement, report, spatialReport, insertAt: moveSectionIndex);
                    }
                    Rebuild(boundedInquiry);
                }
            }
            if (level >= 5)
            {
                // The movement section is at its floor before the inquiry budget is measured, so
                // the inquiry gets every character not spent on mandatory text.
                if (spatialOn)
                {
                    movementText = PromptBuilder.RenderSpatialMovementSectionForBudget(input.Movement,
                        input.SpatialOptions, 1, out spatialReport);
                    if (moveSectionIndex < report.Sections.Count)
                    {
                        report.Sections.RemoveAt(moveSectionIndex);
                        AddMovement(input.Movement, report, spatialReport, insertAt: moveSectionIndex);
                    }
                    Rebuild(boundedInquiry);
                }
                if (inquiryOriginalChars > 0)
                {
                    int minKept = Math.Min(inquiryOriginalChars, 200);
                    int floorRequest = Math.Min(inquiryOriginalChars, minKept + 8);
                    // Try the full result first: the level-4 reductions and the movement floor
                    // may already be enough.
                    boundedInquiry = input.LatestInquiryResult;
                    inquiryKeptChars = inquiryOriginalChars;
                    UpdateInquirySection(report, inquirySectionIndex, inquiryKeptChars,
                        inquiryOriginalChars, false);
                    Rebuild(boundedInquiry);
                    for (int pass = 0; pass < 16 && input.SystemChars + user.Length > fitLimit; pass++)
                    {
                        int over = input.SystemChars + user.Length - fitLimit;
                        int next = inquiryKeptChars - Math.Max(1, over);
                        if (next < floorRequest) next = floorRequest;
                        if (next >= inquiryKeptChars) break;
                        inquiryKeptChars = next;
                        string bound = BoundInquiry(input.LatestInquiryResult, inquiryKeptChars,
                            inquiryOriginalChars, out int actualKept);
                        boundedInquiry = bound;
                        inquiryKeptChars = actualKept;
                        UpdateInquirySection(report, inquirySectionIndex, inquiryKeptChars,
                            inquiryOriginalChars, inquiryKeptChars < inquiryOriginalChars);
                        Rebuild(boundedInquiry);
                    }
                }
                report.InquiryOriginalChars = inquiryOriginalChars;
                report.InquiryKeptChars = inquiryKeptChars;
                if (input.SystemChars + user.Length > fitLimit)
                {
                    int mandatoryTotal = input.SystemChars + user.Length;
                    if (inquirySectionIndex >= 0)
                    {
                        RichContextReport.Section saved = report.Sections[inquirySectionIndex];
                        report.Sections.RemoveAt(inquirySectionIndex);
                        Rebuild(null);
                        mandatoryTotal = input.SystemChars + user.Length;
                        report.Sections.Insert(inquirySectionIndex, saved);
                        Rebuild(boundedInquiry);
                    }
                    if (reservedChars > 0 && mandatoryTotal <= effectiveMaxChars)
                        report.UnfitReason = "diplomacy_reservation";
                    else if (mandatoryTotal > fitLimit)
                        report.UnfitReason = "mandatory_context_exceeds_limit";
                    else
                        report.UnfitReason = "inquiry_result_floor_unfit";
                }
            }
            if (spatialReport != null)
            {
                spatialReport.ProviderCharLimit = effectiveMaxChars;
                spatialReport.SystemChars = input.SystemChars;
                spatialReport.UserChars = user.Length;
                spatialReport.TotalChars = input.SystemChars + user.Length;
                report.SpatialTruncation = spatialReport;
            }
            report.PromptChars = input.SystemChars + user.Length;
            report.Fits = report.PromptChars <= report.MaxPromptChars;
            string observationJson = PromptBuilder.BoundedObservationJson(obs, events);
            int historyChars = 0;
            foreach (string line in history) historyChars += (line == null ? 0 : line.Length) + 2;
            int memorySectionChars = 0;
            foreach (LlmMemory memory in memories)
                if (memory != null)
                    memorySectionChars += 6 + (memory.Type == null ? 0 : memory.Type.Length)
                        + (memory.Content == null ? 0 : memory.Content.Length);
            return new RichContextResult
            {
                User = user,
                ObservationJson = observationJson,
                Report = report,
                SizeReport = PromptSizeReport.RichAssembler(report, input, observationJson.Length,
                    historyChars, memorySectionChars, movementText == null ? 0 : movementText.Length,
                    ActionCatalogue.ToPromptText(input.Options).Length,
                    section == null ? 0 : section.Length,
                    input.LatestInquiryResult == null ? 0 : input.LatestInquiryResult.Length),
            };
        }

        private static void UpdateInquirySection(RichContextReport report, int index, int kept,
            int original, bool truncated)
        {
            if (index < 0 || index >= report.Sections.Count) return;
            RichContextReport.Section section = report.Sections[index];
            section.Included = kept;
            section.Available = original;
            section.Note = truncated
                ? "truncated to fit the prompt limit; repeat a narrower inquiry for the rest"
                : null;
        }

        /// <summary>
        /// Bounds an inquiry result to <paramref name="requestedKept"/> content characters and
        /// prefixes a clear marker. The marker, the cut content and the report line are all part of
        /// the prompt the caller measures. Cuts are safe for surrogate pairs and (for JSON-ish text)
        /// never end inside a backslash or \uXXXX escape.
        /// </summary>
        private static string BoundInquiry(string text, int requestedKept, int original, out int kept)
        {
            kept = SafeCut(text, requestedKept);
            if (kept >= text.Length)
            {
                kept = text.Length;
                return text;
            }
            string marker = "[INCOMPLETE: inquiry result cut from "
                + original.ToString(CultureInfo.InvariantCulture) + " to "
                + kept.ToString(CultureInfo.InvariantCulture)
                + " chars to fit the prompt limit; repeat a narrower inquiry for the rest]";
            return marker + "\n" + text.Substring(0, kept);
        }

        private static int SafeCut(string text, int requested)
        {
            int cut = Math.Min(Math.Max(0, requested), text.Length);
            if (cut > 0 && cut < text.Length && char.IsHighSurrogate(text[cut - 1])
                && char.IsLowSurrogate(text[cut]))
                cut--;
            if (!LooksJsonish(text)) return cut;
            while (cut > 0)
            {
                // An odd run of backslashes means the cut lands after an escaped character.
                int backslashes = 0;
                for (int i = cut - 1; i >= 0 && text[i] == '\\'; i--) backslashes++;
                if ((backslashes & 1) == 1)
                {
                    cut--;
                    continue;
                }
                // An incomplete \uXXXX sequence at the end is dropped entirely.
                int slash = -1;
                for (int i = cut - 1; i >= 0 && i >= cut - 5; i--)
                    if (text[i] == '\\') { slash = i; break; }
                if (slash >= 0 && slash + 1 < text.Length && text[slash + 1] == 'u')
                {
                    int hex = 0;
                    for (int i = slash + 2; i < cut; i++)
                        if (IsHex(text[i])) hex++; else break;
                    if (hex < 4)
                    {
                        cut = slash;
                        continue;
                    }
                }
                break;
            }
            if (cut > 0 && cut < text.Length && char.IsHighSurrogate(text[cut - 1])
                && char.IsLowSurrogate(text[cut]))
                cut--;
            return Math.Max(0, cut);
        }

        private static bool LooksJsonish(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                return c == '{' || c == '[' || c == '"';
            }
            return false;
        }

        private static bool IsHex(char c)
            => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

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

        private static void AddMovement(MovementView movement, RichContextReport report,
            SpatialTruncationReport spatialReport = null, int insertAt = -1)
        {
            if (movement == null || movement.Armies == null || movement.Armies.Count == 0) return;
            int shown = 0, reachable = 0;
            foreach (ArmyMovePlan plan in movement.Armies)
            {
                if (plan == null) continue;
                shown += plan.Options == null ? 0 : plan.Options.Count;
                reachable += Math.Max(plan.Options == null ? 0 : plan.Options.Count, plan.TotalReachable);
            }
            if (spatialReport != null)
                shown = Math.Max(0, reachable - spatialReport.MoveOptionsDropped);
            var entry = new RichContextReport.Section
            {
                Name = "march destinations across your armies",
                Included = shown,
                Available = reachable,
                Note = shown < reachable
                    ? "the planner lists only these; omitted destinations are not listed by any tool, but the host checks a submitted destination on its own, so a legal visible one you name from the map may be accepted"
                    : null,
            };
            if (insertAt >= 0 && insertAt <= report.Sections.Count) report.Sections.Insert(insertAt, entry);
            else report.Sections.Add(entry);
        }
    }
}
