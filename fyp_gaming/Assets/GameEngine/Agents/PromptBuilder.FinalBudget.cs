using System.Collections.Generic;

namespace LuanShi.Engine
{
    public static partial class PromptBuilder
    {
        /// <summary>
        /// Builds the conversion-enabled prompt against the final system and version identity.
        /// User sections inserted by the caller are reserved before any optional content is trimmed.
        /// Returns the smallest candidate if mandatory content still cannot fit; the runner rejects
        /// that candidate before dispatch. Legacy builders remain unchanged when the feature is off.
        /// </summary>
        public static string UserPromptWithFinalBudget(Observation observation,
            IReadOnlyList<ActionOption> options, IReadOnlyList<string> history,
            int requestsLeft, int actionsLeft, MovementView movement, SpatialContextView spatial,
            SpatialContextOptions spatialOptions, int limit, string version, int systemChars,
            int reservedUserChars, IReadOnlyList<LlmMemory> memories, string inquiryResult,
            LlmPersona persona, bool attackEnabled, AttackView attacks, string note,
            IReadOnlyList<string> retained, out SpatialTruncationReport spatialReport)
        {
            string attackNotice = attackEnabled ? ActionCatalogue.AttackNotice(attacks) : null;
            if (spatialOptions != null && spatialOptions.IsEnabled)
            {
                // The spatial assembler accounts for the not-yet-inserted section as reserved
                // overhead. The runner records actual system/user counts after insertion.
                return AssembleBoundedSpatialPrompt(observation, options, history, requestsLeft,
                    actionsLeft, movement, spatial, spatialOptions, limit, version,
                    systemChars + reservedUserChars, memories, inquiryResult, persona, attackNotice,
                    retained, note, out spatialReport);
            }

            spatialReport = null;
            string Render(string moves, IReadOnlyList<string> earlier, string strategicNote)
                => BuildUserPrompt(observation, options, history, requestsLeft, actionsLeft,
                    version, moves, memories, inquiryResult, persona, attackNotice, earlier, strategicNote);
            bool Fits(string text) => systemChars + reservedUserChars + text.Length <= limit;
            string movementText = movement == null ? null : MovementOptionsText(movement);
            string candidate = Render(movementText, retained, note);
            if (Fits(candidate)) return candidate;
            candidate = Render(movementText, null, note);
            if (Fits(candidate)) return candidate;
            candidate = Render(movementText, null, null);
            if (Fits(candidate) || movement == null) return candidate;

            int maxOptions = 0;
            if (movement.Armies != null)
                foreach (ArmyMovePlan army in movement.Armies)
                    if (army?.Options != null && army.Options.Count > maxOptions)
                        maxOptions = army.Options.Count;
            for (int count = maxOptions - 1; count >= 0; count--)
            {
                candidate = Render(MovementOptionsText(movement, count), null, null);
                if (Fits(candidate)) return candidate;
            }
            int armies = movement.Armies == null ? 0 : movement.Armies.Count;
            for (int count = armies - 1; count >= 0; count--)
            {
                candidate = Render(MovementOptionsText(movement, 1, count), null, null);
                if (Fits(candidate)) return candidate;
            }
            return Render(MovementOptionsText(movement, 0, 0), null, null);
        }
    }
}
