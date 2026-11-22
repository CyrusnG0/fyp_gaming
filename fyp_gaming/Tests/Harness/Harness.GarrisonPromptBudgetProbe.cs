using System;
using System.Globalization;
using System.Text.RegularExpressions;
using LuanShi.Engine;

internal static partial class Harness
{
    private static int RunGarrisonPromptBudgetProbe()
    {
        foreach (int limit in new[] { 40000, 80000 })
        {
            GameEngine engine = BuildCrowdedRichOverflowWorld();
            CityState garrisonCity = engine.State.FindCity("city-shu");
            garrisonCity.Garrison = 1000;
            garrisonCity.GarrisonType = UnitTypes.Infantry;

            var spatial = new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                ProviderCharLimit = limit,
            };
            MovementView offered = MovementPlanner.BuildDiversified(engine, "seat-1",
                MovementPlanner.DefaultMaxOptionsPerArmy, null, spatial);
            int offeredDestinations = 0;
            if (offered?.Armies != null)
                foreach (ArmyMovePlan plan in offered.Armies)
                    if (plan?.Options != null) offeredDestinations += plan.Options.Count;

            LlmConfig config = LlmConfig.RichDeliberation();
            config.MaxPromptChars = limit;
            config.EnableGarrisonConversion = true;
            config.RequireVisibleMarchRoutes = true;
            config.SpatialContext = spatial;

            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
            var runner = new LlmTurnRunner(fake, config, null, null,
                new MemoryLlmStrategicNoteStore());
            var session = new SpectatorSession(engine, runner, 1, 909);
            session.Start();
            SpectatorDrain(session);

            if (fake.Requests.Count < 2)
            {
                Console.WriteLine("budget=" + limit.ToString(CultureInfo.InvariantCulture)
                    + " state=" + session.State + " reason=" + runner.EndReasonText
                    + " requests=" + fake.Requests.Count);
                return 1;
            }

            LlmRequest request = fake.Requests[1];
            string user = request.User ?? string.Empty;
            int movementStart = user.IndexOf(PromptBuilder.MovementHeader, StringComparison.Ordinal);
            int shownDestinations = movementStart < 0 ? 0 : Regex.Matches(user.Substring(movementStart), @"\bcost=\d+").Count;
            int total = request.System.Length + user.Length;
            SpatialTruncationReport truncation = runner.LastSpatialTruncation;
            Console.WriteLine("budget=" + limit.ToString(CultureInfo.InvariantCulture)
                + " state=" + session.State
                + " reason=" + runner.EndReasonText
                + " dispatches=" + fake.CallCount
                + " prompt_chars=" + total
                + " offered_destinations=" + offeredDestinations
                + " shown_destinations=" + shownDestinations
                + " move_options_dropped=" + (truncation?.MoveOptionsDropped ?? -1)
                + " visible_ranges_dropped=" + (truncation?.VisibleCellRangesDropped ?? -1)
                + " conversion_option_shown=" + user.Contains("type=convert_garrison target_id=city-shu")
                + " size_report=" + (runner.LastPromptSizeJson ?? "none"));
        }
        return 0;
    }
}
