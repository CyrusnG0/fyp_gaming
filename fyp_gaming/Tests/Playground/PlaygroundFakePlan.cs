using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Deterministic scripted fake replies for the default session. Replies are generated from the
    /// seat's real observation and movement candidates, not invented coordinates: the same seed and
    /// scenario always produce the same sequence. The fake provider is labelled as such in results.
    /// </summary>
    internal static class PlaygroundFakePlan
    {
        public const string Label = "FAKE playground";

        public static void Enqueue(FakeLlmTransport transport, GameEngine engine,
            PlaygroundConfig config)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            IReadOnlyList<string> seats = PlaygroundScenario.SeatOrder(engine);
            bool checkOnly = config.ActionMode == PlaygroundActionMode.CheckOnly;
            for (int season = 1; season <= Math.Max(1, config.Seasons); season++)
            {
                foreach (string seat in seats)
                {
                    if (season > 1)
                    {
                        Enqueue(transport, EndTurn("season " + season + " hold position"));
                        continue;
                    }
                    if (seat == PlaygroundScenario.SeatA) EnqueueSeatA(transport, engine, checkOnly);
                    else EnqueueSeatB(transport, engine, checkOnly);
                }
            }
        }

        private static void EnqueueSeatA(FakeLlmTransport transport, GameEngine engine,
            bool checkOnly)
        {
            Observation observation = engine.Observe(PlaygroundScenario.SeatA);
            string city = "city-a";
            if (observation != null)
                foreach (CityView view in observation.Cities)
                    if (view != null && view.Id == "city-neutral") { city = view.Id; break; }

            Enqueue(transport, "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":"
                + PlaygroundJson.Str(city) + "}");
            Enqueue(transport, "{\"tool\":\"recall_memory\",\"query\":\"fixture\",\"limit\":3}");
            Enqueue(transport, "{\"note\":\"" + MiniJson.Escape(
                Label + ": I will take the Crossroads at (3,0) and then hold it.") + "\"}");
            Enqueue(transport, MarchOrDomestic(engine, PlaygroundScenario.SeatA, "city-a"));
            // Check-only stops at the candidate order, so it must not queue a trailing end_turn:
            // a leftover reply would be consumed by the next seat and desynchronise the plan.
            if (!checkOnly) Enqueue(transport, EndTurn("Crossroads secured"));
        }

        private static void EnqueueSeatB(FakeLlmTransport transport, GameEngine engine,
            bool checkOnly)
        {
            Enqueue(transport, "{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
            Enqueue(transport, "{\"tool\":\"search_history\",\"limit\":3}");
            Enqueue(transport, "{\"note\":\"" + MiniJson.Escape(
                Label + ": I will bank gold and avoid the neutral city this season.") + "\"}");
            Enqueue(transport, Domestic("levy_tax", "city-b", "raise taxes before winter"));
            if (!checkOnly) Enqueue(transport, EndTurn("bank gold"));
        }

        private static string MarchOrDomestic(GameEngine engine, string seat, string fallbackCity)
        {
            MovementView movement = MovementPlanner.Build(engine, seat);
            ArmyMovePlan army = null;
            MoveOption choice = null;
            if (movement != null && movement.Armies != null)
            {
                foreach (ArmyMovePlan plan in movement.Armies)
                {
                    if (plan == null || !plan.CanMarch || plan.Options == null) continue;
                    foreach (MoveOption option in plan.Options)
                    {
                        if (option == null) continue;
                        if (option.Kind == "capture_neutral_city")
                        {
                            army = plan;
                            choice = option;
                            break;
                        }
                    }
                    if (choice != null) break;
                }
            }
            if (army == null || choice == null)
                return Domestic("levy_tax", fallbackCity, "expand income instead of marching");
            return "{\"type\":\"march\",\"target_id\":" + MiniJson.Str(army.ArmyId)
                + ",\"param_a\":" + MiniJson.Num(choice.X)
                + ",\"param_b\":" + MiniJson.Num(choice.Z)
                + ",\"summary\":\"" + MiniJson.Escape(
                    Label + ": march " + army.ArmyId + " to capture the Crossroads") + "\"}";
        }

        private static string Domestic(string type, string cityId, string why)
            => "{\"type\":" + MiniJson.Str(type) + ",\"target_id\":" + MiniJson.Str(cityId)
               + ",\"summary\":\"" + MiniJson.Escape(Label + ": " + why) + "\"}";

        private static string EndTurn(string why)
            => "{\"type\":\"end_turn\",\"summary\":\""
               + MiniJson.Escape(Label + ": " + why) + "\"}";

        private static void Enqueue(FakeLlmTransport transport, string text)
        {
            // Deterministic, plausible usage inside the configured bounds. The provider is fake; these
            // counts exercise budget settlement and are labelled as fake evidence in the trace.
            transport.EnqueueSuccess(text, 0, 1200, 60);
        }
    }
}
