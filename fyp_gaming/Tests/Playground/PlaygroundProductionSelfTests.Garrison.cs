using System.Linq;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal static partial class PlaygroundSelfTests
    {
        private static void ProductionGarrisonConversion(TestLog log)
        {
            const string convert = "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1000}";
            log.Check(!LlmActionParser.Parse(convert, false, false, false).Ok
                      && LlmActionParser.Parse(convert, false, false, false, true).Ok,
                "garrison: opt-in parser accepts the conversion and legacy parser rejects it");
            foreach (string bad in new[] {
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\"}",
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":0}",
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1,\"param_b\":1}",
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1,\"actor_seat_id\":\"seat-b\"}" })
                log.Check(!LlmActionParser.Parse(bad, false, false, false, true).Ok,
                    "garrison: invalid conversion shape or actor rejected");
            log.Check(LlmActionParser.Parse("{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1,\"param_b\":0,\"args\":{}}", false, false, false, true).Ok,
                "garrison: harmless zero param_b and empty args are accepted");
            string longReason = new string('r', 203);
            string longReply = "{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1000,\"param_b\":0,\"args\":{},\"reason\":\"" + longReason + "\"}";
            ParseResult bounded = LlmActionParser.Parse(longReply, false, false, false, true);
            log.Check(bounded.Ok && bounded.Action.Reason.Length == 200 && bounded.Action.Reason.EndsWith("…")
                      && !LlmActionParser.Parse(longReply, false, false, false, false).Ok,
                "garrison: a 203-character reason is bounded while feature-off parsing stays strict");
            log.Check(!LlmActionParser.Parse(longReply.Replace(longReason, new string('r', 200) + "\\n"), false, false, false, true).Ok
                      && !LlmActionParser.Parse(longReply.Replace("city-a", new string('x', 201)), false, false, false, true).Ok
                      && !LlmActionParser.Parse("{\"type\":\"send_message\",\"target_id\":\"seat-b\",\"text\":\"" + new string('x', 201) + "\"}", false, false, true, true).Ok,
                "garrison: control characters, oversized target IDs and diplomacy text stay invalid");

            var engine = ProductionEngine();
            engine.State.Armies.RemoveAll(a => a.OwnerSeatId == "seat-a");
            var fake = new FakeLlmTransport();
            var config = ProductionConfig();
            config.EnableGarrisonConversion = true;
            config.MaxActionsPerTurn = 5;
            fake.EnqueueSuccess("{\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"infantry\"}}");
            fake.EnqueueSuccess(longReply);
            fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-seat-a-1\",\"param_a\":1,\"param_b\":0}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var host = new ProductionHost(engine, "seat-a");
            var runner = new LlmTurnRunner(fake, config, null);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            var army = engine.State.FindArmy("army-seat-a-1");
            log.Check(runner.AcceptedActionsThisTurn == 3 && runner.EndReason == EndReason.ModelEndedTurn
                      && army != null && army.Troops == 1000 && army.X == 1
                      && engine.State.FindCity("city-a").Garrison == 500,
                "garrison: production runner recruits, forms and marches an army",
                runner.EndReasonText);
            log.Check(fake.Requests.Count == 4 && fake.Requests[0].System.Contains("GARRISON CONVERSION")
                      && fake.Requests[1].User.Contains("type=convert_garrison")
                      && fake.Requests[2].User.Contains("army-seat-a-1")
                      && !fake.Requests[1].User.Contains("type=convert_garrison target_id=city-b"),
                "garrison: prompt offers owned city and later shows the created army");
            log.Check(PromptBuilder.IsDiplomacyPromptVersion("demo-slice-6+diplomacy" + PromptBuilder.GarrisonConversionVersionSuffix)
                      && PromptBuilder.IsGarrisonConversionPromptVersion("demo-slice-6+diplomacy" + PromptBuilder.GarrisonConversionVersionSuffix)
                      && !PromptBuilder.IsGarrisonConversionPromptVersion("demo-slice-6+diplomacy"),
                "garrison: recorded prompt identity keeps diplomacy and conversion distinct");

            var reinforcedEngine = ProductionEngine();
            reinforcedEngine.State.FindArmy("army-a-1").X = 0;
            var reinforcedFake = new FakeLlmTransport();
            reinforcedFake.EnqueueSuccess("{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":500}");
            reinforcedFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var reinforcedHost = new ProductionHost(reinforcedEngine, "seat-a");
            var reinforcedRunner = new LlmTurnRunner(reinforcedFake, config, null);
            reinforcedRunner.Begin(reinforcedHost, reinforcedHost.Token, "seat-a", ProdSeed);
            DrainProduction(reinforcedRunner);
            log.Check(reinforcedRunner.AcceptedActionsThisTurn == 1
                      && reinforcedEngine.State.FindArmy("army-a-1").Troops == 2500
                      && reinforcedEngine.State.FindCity("city-a").Garrison == 0,
                "garrison: conversion reinforces an existing compatible army on the city");

            var foreignEngine = ProductionEngine();
            var foreignHost = new ProductionHost(foreignEngine, "seat-a");
            var result = foreignHost.Submit(new ActionCommand { Type = ActionType.ConvertGarrison,
                TargetId = "city-b", ParamA = 1 });
            log.Check(!result.Ok && foreignEngine.State.FindCity("city-b").Garrison == 500,
                "garrison: the engine refuses conversion of a foreign city's troops");
        }
    }
}
