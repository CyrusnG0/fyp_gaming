using System;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunVisibleMarchRunner()
    {
        foreach (bool deliberation in new[] { false, true })
        {
            string next = null;
            foreach (bool shortcutOpen in new[] { true, false })
            {
                var engine = BuildUnseenShortcutWorld(707, shortcutOpen);
                var fake = new FakeLlmTransport();
                fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId
                    + "\",\"param_a\":3,\"param_b\":5}");
                fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId
                    + "\",\"param_a\":4,\"param_b\":5}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var config = new LlmConfig
                {
                    RequireVisibleMarchRoutes = true,
                    EnableDeliberation = deliberation,
                };
                var runner = new LlmTurnRunner(fake, config, null);
                var session = new SpectatorSession(engine, runner, 1, 707);
                session.Start();
                session.Tick();
                var army = engine.State.FindArmy(ShortcutArmyId);
                Check(fake.CallCount == 2 && army.X == 2 && army.Z == 2
                    && runner.LastResult == FeedbackPolicy.DestinationNotReachable,
                    "visible march policy: an unlisted shortcut attempt is rejected before movement");
                if (next != null)
                    Check(string.Equals(next, fake.Requests[1].User, StringComparison.Ordinal),
                        "visible march policy: next prompt matches across hidden shortcut worlds");
                next = fake.Requests[1].User;
                for (int i = 0; i < 20 && session.State == SpectatorState.Running; i++) session.Tick();
                Check(session.State == SpectatorState.Completed && session.TotalAccepted == 1
                      && session.TotalRejected == 1 && army.X == 4 && army.Z == 5,
                    "visible march policy: an order with a visible route reaches the engine");
            }
        }
    }
}
