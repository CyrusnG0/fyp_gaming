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

        private static int MovementFloor(RichContextInputs input)
            => HasLegalDestinations(input.Movement) ? MovementReserveChars : 1;

        /// <summary>True when the host would still accept another inquiry after this reply.</summary>
        private static bool InquiryRemains(PromptBuilder.RichAllowances a)
        {
            if (a == null) return false;
            // A follow-up inquiry needs one more request than this reply uses.
            return (a.InquiriesLeftTurn < 0 || a.InquiriesLeftTurn > 0)
                && (a.InquiriesLeftSession < 0 || a.InquiriesLeftSession > 0)
                && (a.RequestsLeftTurn < 0 || a.RequestsLeftTurn >= 2)
                && (a.RequestsLeftSession < 0 || a.RequestsLeftSession >= 2);
        }

        /// <summary>Wording that stays true whether or not inquiries remain.</summary>
        private static string InquiryAdvice(bool inquiryRemains)
            => inquiryRemains
                ? "you may repeat a narrower inquiry for the rest"
                : "no further inquiry is available this turn, so treat this result as partial";

        private static string InquiryMarker(int original, int kept, bool inquiryRemains)
            => "[INCOMPLETE: inquiry result cut from "
               + original.ToString(CultureInfo.InvariantCulture) + " to "
               + kept.ToString(CultureInfo.InvariantCulture)
               + " chars to fit the prompt limit; " + InquiryAdvice(inquiryRemains) + "]";
    }
}
