using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class SpectatorDispatchClock : IDeliberationClock
    {
        private int _reads;
        private long _now;
        public int AdvanceOnRead = 4;
        public long AdvanceMillis;
        public long NowMillis
        {
            get { if (++_reads == AdvanceOnRead) _now += AdvanceMillis; return _now; }
        }
    }

    private static void RunSpectatorDeliberationSessionCap()
    {
        Section("Spectator: deliberation session cap pauses without empty turns");
        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"note\":\"Watch the border.\"}", 0, 5, 1);
        var runner = SpectatorRunner(fake, new LlmConfig
        {
            EnableDeliberation = true,
            MaxRequestsPerTurn = 6,
            MaxActionsPerTurn = 5,
            MaxTotalRequests = 1,
        });
        var session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused && runner.EndReason == EndReason.SessionRequestLimit,
            "session-wide request denial pauses the spectator on the typed reason");
        Check(fake.CallCount == 1 && runner.TotalRequests == 1,
            "the fake provider was called exactly once");
        Check(session.TurnRecords.Count == 1 && session.TurnRecords[0].Requests == 1,
            "no zero-request turns are recorded after the session cap");
        Check(session.SeasonsPlayed == 0 && engine.State.Season == 1,
            "no season is advanced or reported as completed after the cap");
        session.Resume();
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused && fake.CallCount == 1
              && session.TurnRecords.Count == 1 && session.SeasonsPlayed == 0,
            "resume and start cannot advance a session whose shared request budget is exhausted");

        engine = SpectatorEngine();
        fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}", 0, 5, 1);
        fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-a\"}", 0, 5, 1);
        runner = SpectatorRunner(fake, new LlmConfig
        {
            EnableDeliberation = true,
            MaxRequestsPerTurn = 6,
            MaxActionsPerTurn = 5,
            MaxAcceptedActionsPerSession = 1,
            MaxTotalRequests = 20,
        });
        session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused
              && runner.StopCeiling == "session_actions_exhausted"
              && session.TurnRecords.Count == 1 && session.SeasonsPlayed == 0,
            "whole-session action exhaustion pauses instead of advancing empty turns");

        // Expire a turn reservation between admission and dispatch: no provider work is sent.
        engine = SpectatorEngine();
        fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var clock = new SpectatorDispatchClock { AdvanceMillis = 5 };
        runner = new LlmTurnRunner(fake, new LlmConfig
        {
            EnableDeliberation = true, MaxRequestsPerTurn = 6, MaxActionsPerTurn = 5,
            MaxTotalRequests = 20, MaxDeliberationTurnMillis = 5,
        }, null, null, null, null, clock);
        session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Paused && runner.EndReason == EndReason.DispatchDenied
              && fake.CallCount == 0 && session.TurnRecords.Count == 1 && session.SeasonsPlayed == 0,
            "a turn-level dispatch denial pauses without a provider call or advancing a seat");
        session.Resume();
        Check(session.State == SpectatorState.Running,
            "a turn-level dispatch denial remains explicitly resumable");

        // Expire the whole-session reservation at dispatch: even an explicit resume cannot
        // advance empty turns because the match-wide allowance cannot be refilled in place.
        engine = SpectatorEngine();
        fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        clock = new SpectatorDispatchClock { AdvanceOnRead = 5, AdvanceMillis = 1000 };
        runner = new LlmTurnRunner(fake, new LlmConfig
        {
            EnableDeliberation = true, MaxRequestsPerTurn = 6, MaxActionsPerTurn = 5,
            MaxTotalRequests = 20, MaxSessionSeconds = 1,
        }, null, null, null, null, clock);
        session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);
        bool sessionDeadlinePaused = session.State == SpectatorState.Paused
            && runner.EndReason == EndReason.DispatchDenied
            && runner.StopCeiling == "session_wall_time_exhausted"
            && fake.CallCount == 0 && session.TurnRecords.Count == 1;
        session.Resume();
        Check(sessionDeadlinePaused && session.State == SpectatorState.Paused
              && session.TurnRecords.Count == 1 && session.SeasonsPlayed == 0,
            "a match-wide deadline at dispatch pauses and cannot resume into empty turns"
            + " state=" + session.State + " reason=" + runner.EndReason + " code=" + runner.StopCeiling
            + " calls=" + fake.CallCount + " turns=" + session.TurnRecords.Count);
    }
}
