using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LuanShi.Engine
{
    /// <summary>
    /// Deny-by-default translation of engine rejection text into a small, typed allowlist of safe
    /// categories. Model-visible feedback must never distinguish a hidden-but-existing id from a
    /// nonexistent one (or otherwise reveal canonical state outside the seat's sight), so anything
    /// unmatched collapses to <see cref="Generic"/> instead of passing raw engine text through.
    /// </summary>
    public static class FeedbackPolicy
    {
        public const string NotEnoughGold = "not enough gold";
        public const string NotEnoughFood = "not enough food";
        public const string NotEnoughPopulation = "not enough population";
        public const string NoOrdersLeft = "no orders left";
        public const string CapReached = "cap reached";
        public const string AlreadyMarched = "already marched this season";
        public const string DestinationNotReachable = "destination not reachable this season";
        public const string DestinationNotVisible = "destination not visible";
        public const string DestinationBlocked = "destination blocked";
        public const string InvalidTarget = "invalid target";
        public const string UnsupportedAction = "unsupported action";
        public const string GameOver = "game over";
        public const string Generic = "action rejected";

        private static readonly string[] AllCategories =
        {
            NotEnoughGold, NotEnoughFood, NotEnoughPopulation, NoOrdersLeft, CapReached,
            AlreadyMarched, DestinationNotReachable, DestinationNotVisible, DestinationBlocked,
            InvalidTarget, UnsupportedAction, GameOver, Generic,
        };

        private static readonly ReadOnlyCollection<string> AllCategoriesView =
            Array.AsReadOnly(AllCategories);

        /// <summary>Every string <see cref="ForModel"/> may return. Safe to show to a model.</summary>
        public static IReadOnlyCollection<string> Categories => AllCategoriesView;

        public static bool IsSafeCategory(string value)
        {
            if (value == null) return false;
            for (int i = 0; i < AllCategories.Length; i++)
                if (string.Equals(AllCategories[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Maps one engine rejection string to a safe category. The order of the rules matters:
        /// hidden-target existence checks come first, then the specific safe resource/movement
        /// categories, then the generic fallback.
        /// </summary>
        public static string ForModel(string engineError)
        {
            if (string.IsNullOrEmpty(engineError)) return Generic;

            // Target existence/ownership: a hidden-but-existing id must read exactly like a
            // nonexistent one, so all of these become one fixed answer.
            if (Starts(engineError, "unknown city") || Starts(engineError, "unknown army")
                || Starts(engineError, "unknown seat")
                || Contains(engineError, "not your city") || Contains(engineError, "not your army")
                || Contains(engineError, "army is not yours"))
                return InvalidTarget;

            if (Equals(engineError, "game over") || Starts(engineError, "game over"))
                return GameOver;

            if (Starts(engineError, "unknown action type") || Starts(engineError, "unsupported action")
                || Starts(engineError, "unsupported argument")
                || Starts(engineError, "unsupported unit type")
                || Starts(engineError, "unknown unit_type")
                || Starts(engineError, "invalid field") || Starts(engineError, "unknown field")
                || Starts(engineError, "forbidden field"))
                return UnsupportedAction;

            if (Contains(engineError, "battle is beyond this demo")) return UnsupportedAction;

            if (Contains(engineError, "no command points left")) return NoOrdersLeft;
            if (Contains(engineError, "not enough gold")) return NotEnoughGold;
            if (Contains(engineError, "not enough food")) return NotEnoughFood;
            if (Contains(engineError, "not enough population")) return NotEnoughPopulation;

            // Caps and per-season limits that contain no hidden facts.
            if (Contains(engineError, "already reclaimed this season")
                || Contains(engineError, "land fully reclaimed")
                || Contains(engineError, "reclaim cap reached")
                || Contains(engineError, "training already at max")
                || Contains(engineError, "defense already at max")
                || Contains(engineError, "already at max")
                || Contains(engineError, "cap reached"))
                return CapReached;

            if (Contains(engineError, "already marched")) return AlreadyMarched;

            // March destination facts. Every advertised destination is in sight, so these describe
            // only what the seat can already see (or its own army's position/terrain).
            if (Contains(engineError, "beyond your sight")
                || Contains(engineError, "destination not visible"))
                return DestinationNotVisible;
            if (Contains(engineError, "out of reach")
                || Contains(engineError, "not reachable this season")
                || Contains(engineError, "destination not reachable"))
                return DestinationNotReachable;
            if (Contains(engineError, "destination not walkable")
                || Contains(engineError, "destination occupied")
                || Contains(engineError, "destination blocked")
                || Contains(engineError, "blocked destination"))
                return DestinationBlocked;

            return Generic;
        }

        private static bool Starts(string value, string prefix)
            => value.StartsWith(prefix, StringComparison.Ordinal);
        private static bool Contains(string value, string fragment)
            => value.IndexOf(fragment, StringComparison.Ordinal) >= 0;
        private static bool Equals(string value, string expected)
            => string.Equals(value, expected, StringComparison.Ordinal);
    }
}
