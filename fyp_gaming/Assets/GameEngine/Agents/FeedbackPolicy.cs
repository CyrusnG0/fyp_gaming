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
        public const string AlreadyAtDestination = "already at that destination";
        public const string DestinationNotReachable = "destination not reachable this season";
        public const string DestinationNotVisible = "destination not visible";
        public const string DestinationBlocked = "destination blocked";
        public const string InvalidTarget = "invalid target";
        public const string UnsupportedAction = "unsupported action";
        public const string GameOver = "game over";
        public const string ArmyMoraleBroken = "army morale broken";
        public const string AssaultRatioTooLow = "assault ratio too low";
        public const string Generic = "action rejected";

        private static readonly string[] AllCategories =
        {
            NotEnoughGold, NotEnoughFood, NotEnoughPopulation, NoOrdersLeft, CapReached,
            AlreadyMarched, AlreadyAtDestination, DestinationNotReachable, DestinationNotVisible,
            DestinationBlocked, InvalidTarget, UnsupportedAction, GameOver,
            ArmyMoraleBroken, AssaultRatioTooLow, Generic,
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
            // The army is already standing on the ordered cell. This describes only its own position,
            // which the seat can see, so it is safe to name specifically.
            if (Equals(engineError, "already there")) return AlreadyAtDestination;

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

        /// <summary>
        /// Attack-aware form of <see cref="ForModel(string)"/>. Everything the plain mapping answers is
        /// returned unchanged; only two attack rejections are upgraded from generic, and only when the
        /// seat's own observation establishes that the reason is something it can already see:
        /// <list type="bullet">
        /// <item><see cref="ArmyMoraleBroken"/>: the engine checks morale before it reads the target, so
        /// the answer depends on the seat's own army alone, which the observation must list as its own.</item>
        /// <item><see cref="AssaultRatioTooLow"/>: the engine only reaches the ratio test for an adjacent,
        /// enemy-owned city, so the observation must already show an enemy city on the ordered hex
        /// (cities in sight carry their garrison). No numbers are repeated.</item>
        /// </list>
        /// Treaties are deliberately absent: the engine never rejects an attack on a treaty partner, it
        /// prices it afterwards, so there is no rejection to explain. A missing or mismatched observation
        /// keeps the generic answer.
        /// </summary>
        public static string ForModel(string engineError, ActionCommand command, Observation observation)
        {
            string category = ForModel(engineError);
            if (category != Generic || string.IsNullOrEmpty(engineError)
                || command == null || observation == null) return category;
            bool field = command.Type == ActionType.AttackArmy, city = command.Type == ActionType.AttackCity;
            if (!field && !city) return category;

            if (Starts(engineError, "army morale is broken"))
                return OwnArmyListed(observation, command.TargetId) ? ArmyMoraleBroken : category;
            if (city && Starts(engineError, "assault needs"))
                return EnemyCityListed(observation, command.ParamA, command.ParamB)
                    ? AssaultRatioTooLow : category;
            return category;
        }

        private static bool OwnArmyListed(Observation observation, string armyId)
        {
            if (armyId == null) return false;
            foreach (var army in observation.Armies)
                if (army != null && string.Equals(army.Id, armyId, StringComparison.Ordinal)
                    && string.Equals(army.OwnerSeatId, observation.SeatId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool EnemyCityListed(Observation observation, int x, int z)
        {
            foreach (var city in observation.Cities)
                if (city != null && city.X == x && city.Z == z && !city.IsNeutral
                    && !string.Equals(city.OwnerSeatId, observation.SeatId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool Starts(string value, string prefix)
            => value.StartsWith(prefix, StringComparison.Ordinal);
        private static bool Contains(string value, string fragment)
            => value.IndexOf(fragment, StringComparison.Ordinal) >= 0;
        private static bool Equals(string value, string expected)
            => string.Equals(value, expected, StringComparison.Ordinal);
    }
}
