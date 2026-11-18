using System.Globalization;

namespace LuanShi.Engine
{
    internal static partial class RichContextAssembler
    {
        /// <summary>
        /// Smallest budget (chars) the spatial movement section may be squeezed to while the seat
        /// has at least one legal march destination. A large inquiry result is cut earlier rather
        /// than taking this space. The reserve only sizes the host's own seat-filtered section; it
        /// never adds destinations. If the prompt cannot fit with the reserve held, the assembler
        /// reports it unfit (prompt too large) instead of dropping the reserve.
        /// </summary>
        internal const int MovementReserveChars = 2000;

        private static bool HasLegalDestinations(MovementView movement)
        {
            if (movement == null || movement.Armies == null) return false;
            foreach (ArmyMovePlan plan in movement.Armies)
                if (plan != null && plan.Options != null && plan.Options.Count > 0) return true;
            return false;
        }

        /// <summary>Destinations listed in a rendered movement section ("options: X,Z cost=..; ...").</summary>
        internal static int CountListedDestinations(string movementText)
        {
            if (string.IsNullOrEmpty(movementText)) return 0;
            int count = 0;
            foreach (string raw in movementText.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("  options:", System.StringComparison.Ordinal)) continue;
                foreach (string part in line.Substring(10).Split(';'))
                {
                    string[] xz = part.Trim().Split(' ')[0].Split(',');
                    if (xz.Length == 2 && int.TryParse(xz[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                        && int.TryParse(xz[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        count++;
                }
            }
            return count;
        }

        private static int MovementFloor(RichContextInputs input)
            => HasLegalDestinations(input.Movement) ? MovementReserveChars : 1;

        /// <summary>
        /// Neutral on purpose: it states what the prompt lacks without promising that any tool can
        /// fetch the rest (a manual_lookup topic, for one, has no paging) or that inquiries remain.
        /// </summary>
        private const string InquiryAdvice =
            "the rest is not shown, so treat this result as partial";

        private static string InquiryMarker(int original, int kept)
            => "[INCOMPLETE: inquiry result cut from "
               + original.ToString(CultureInfo.InvariantCulture) + " to "
               + kept.ToString(CultureInfo.InvariantCulture)
               + " chars to fit the prompt limit; " + InquiryAdvice + "]";
    }
}
