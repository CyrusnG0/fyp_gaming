using System;
using System.Collections.Generic;
using System.Globalization;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// The scripted "model" for one match: a reply list per seat, computed from the opening state with the
    /// production movement planner. It is plumbing, not behaviour: the same script always plays the same
    /// way, and it NEVER reads the spatial setting, so both arms of a spatial pair send the same orders and
    /// any difference between their requests comes from the production prompt alone.
    /// </summary>
    public static class BatchScripts
    {
        public const string NotePrefix = "NOTE-";

        public static string NoteFor(string seat) => NotePrefix + seat + "-keep-the-line";

        public static Dictionary<string, List<string>> Build(GameEngine engine, MatchSpec spec)
        {
            FeatureFlags f = spec.Resolved.Features;
            var scripts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (FactionState faction in engine.State.Factions)
            {
                string seat = faction.SeatId;
                var replies = new List<string>();
                for (int season = 1; season <= spec.Resolved.Seasons; season++)
                {
                    if (season == 1) FirstSeason(engine, spec, seat, replies);
                    else
                    {
                        if (f.Deliberation) replies.Add("{\"tool\":\"search_history\",\"limit\":5}");
                        replies.Add("{\"type\":\"end_turn\",\"summary\":\"hold\"}");
                    }
                }
                scripts[seat] = replies;
            }
            return scripts;
        }

        private static void FirstSeason(GameEngine engine, MatchSpec spec, string seat, List<string> replies)
        {
            FeatureFlags f = spec.Resolved.Features;
            if (f.Deliberation)
            {
                replies.Add("{\"tool\":\"manual_lookup\",\"topic\":\"" + (f.Research ? "research" : "recruit") + "\"}");
                replies.Add("{\"note\":\"" + NoteFor(seat) + "\"}");
            }
            if (f.Research) replies.Add("{\"type\":\"research\",\"args\":{\"line\":\"agri\"}}");
            string march = MarchReply(engine, spec, seat);
            if (march != null) replies.Add(march);
            replies.Add("{\"type\":\"end_turn\",\"summary\":\"season one done\"}");
        }

        /// <summary>
        /// One legal march for the seat's first army: the reachable cell nearest the neutral city. The same
        /// choice in every arm, whatever the spatial setting.
        /// </summary>
        private static string MarchReply(GameEngine engine, MatchSpec spec, string seat)
        {
            MovementView view = spec.Resolved.Features.VisibleMarchRoutes
                ? MovementPlanner.BuildDiversified(engine, seat)
                : MovementPlanner.Build(engine, seat);
            CityState prize = null;
            foreach (CityState city in engine.State.Cities) if (string.IsNullOrEmpty(city.OwnerSeatId)) { prize = city; break; }
            foreach (ArmyMovePlan plan in view.Armies)
            {
                if (plan.Options == null) continue;
                MoveOption best = null;
                int bestDistance = 0;
                foreach (MoveOption option in plan.Options)
                {
                    if (option.Kind != "move") continue;
                    int distance = prize == null ? 0 : Math.Abs(option.X - prize.X) + Math.Abs(option.Z - prize.Z);
                    bool better = best == null || distance < bestDistance;
                    if (better) { best = option; bestDistance = distance; }
                }
                if (best == null) continue;
                return "{\"type\":\"march\",\"target_id\":\"" + plan.ArmyId + "\",\"param_a\":"
                       + best.X.ToString(CultureInfo.InvariantCulture) + ",\"param_b\":"
                       + best.Z.ToString(CultureInfo.InvariantCulture) + "}";
            }
            return null;
        }
    }
}
