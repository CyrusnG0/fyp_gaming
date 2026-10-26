using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunDemoS4()
    {
        RunDemoS4PointerMapping();
        RunDemoS4ChatAttributionAndSnapshot();
        RunDemoS4SpectatorBeginFailure();
        RunDemoS4SpectatorPanelLayout();
    }

    // ---- 1: Input.mousePosition (bottom-left) -> 1600x900 design space -------------

    private static void RunDemoS4PointerMapping()
    {
        Section("[DEMO-S4] UI pointer mapping (bottom-left mouse -> design space)");

        UiPointerMapping.ToDesignSpace(0f, 0f, 1920, 1080, out float x, out float y);
        Check(Near(x, 0f) && Near(y, 900f),
            "[DEMO-S4] 16:9 bottom-left maps to design bottom-left (Y is flipped)");

        UiPointerMapping.ToDesignSpace(0f, 1080f, 1920, 1080, out x, out y);
        Check(Near(x, 0f) && Near(y, 0f),
            "[DEMO-S4] 16:9 top-left maps to design origin");

        UiPointerMapping.ToDesignSpace(1920f, 0f, 1920, 1080, out x, out y);
        UiPointerMapping.ToDesignSpace(1920f, 1080f, 1920, 1080, out float trx, out float try_);
        UiPointerMapping.ToDesignSpace(960f, 540f, 1920, 1080, out float cx, out float cy);
        Check(Near(x, 1600f) && Near(y, 900f) && Near(trx, 1600f) && Near(try_, 0f)
              && Near(cx, 800f) && Near(cy, 450f),
            "[DEMO-S4] 16:9 corners and centre scale by 1600/Screen.width");

        // 4:3: the same uniform scale is anchored top-left; the unused strip below design y=900
        // maps to design y > 900 (OnGUI draws nothing there), it is not stretched to the window.
        UiPointerMapping.ToDesignSpace(0f, 0f, 1280, 960, out x, out y);
        UiPointerMapping.ToDesignSpace(0f, 960f, 1280, 960, out float tx, out float ty);
        UiPointerMapping.ToDesignSpace(1280f, 0f, 1280, 960, out float bx, out float by);
        UiPointerMapping.ToDesignSpace(1280f, 960f, 1280, 960, out trx, out try_);
        Check(Near(x, 0f) && Near(y, 1200f)
              && Near(tx, 0f) && Near(ty, 0f)
              && Near(bx, 1600f) && Near(by, 1200f)
              && Near(trx, 1600f) && Near(try_, 0f),
            "[DEMO-S4] 4:3 corners use the uniform 1600-wide, top-anchored transform");

        UiPointerMapping.ToDesignSpaceFromTopLeft(85.6f, 85.6f, 1280, out x, out y);
        UiPointerMapping.ToDesignSpace(85.6f, 960f - 85.6f, 1280, 960, out cx, out cy);
        Check(Near(x, 107f) && Near(y, 107f) && Near(cx, x) && Near(cy, y),
            "[DEMO-S4] e.mousePosition and flipped Input.mousePosition agree at 4:3");

        var rects = new[]
        {
            new DesignRect(960f, 68f, 420f, 412f),    // agent/spectator dock
            new DesignRect(0f, 846f, 1600f, 46f),     // status bar at the bottom
        };
        // 16:9 screen point over the status bar; its bottom-left Y is small, so only the flip
        // (not the raw Input.mousePosition) can match the bottom row.
        UiPointerMapping.ToDesignSpace(1200f, 1080f - 1032f, 1920, 1080, out cx, out cy);
        Check(UiPointerMapping.Contains(cx, cy, new DesignRect(0f, 846f, 1600f, 46f))
              && UiPointerMapping.Contains(1000f, 100f, rects)
              && !UiPointerMapping.Contains(1000f, 500f, rects),
            "[DEMO-S4] contains works for bottom and dock rectangles in 16:9 design space");
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.05f;

    // ---- 5: spectator panel bottom block has its own rows -------------------------

    private static void RunDemoS4SpectatorPanelLayout()
    {
        Section("[DEMO-S4] spectator panel bottom block (totals / buttons / run record)");

        Check(SpectatorPanelLayout.CostRowY
                  == SpectatorPanelLayout.RequestsRowY + SpectatorPanelLayout.TotalsRowHeight
              && SpectatorPanelLayout.TotalsClearOfControls
              && SpectatorPanelLayout.ControlsClearOfSummary
              && SpectatorPanelLayout.SummaryInsidePanel
              && SpectatorPanelLayout.PanelHeight
                  - (SpectatorPanelLayout.SummaryRowY + SpectatorPanelLayout.SummaryRowHeight) >= 4f,
            "[DEMO-S4] totals, controls and run record have distinct non-overlapping rows "
            + "inside the panel");
    }

    // ---- 2/4: observation-only chat attribution and detached snapshots -------------

    private static void RunDemoS4ChatAttributionAndSnapshot()
    {
        Section("[DEMO-S4] chat attribution uses observations; Activity snapshots are detached");

        var engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var runner = SpectatorRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 20 });
        var host = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(host, host.Token, "seat-a", 4242);
        LlmDrain(runner);

        LlmActivity turn = runner.Activity.First(e => e.Kind == "turn");
        LlmActivity request = runner.Activity.First(e => e.Kind == "request");
        Check(turn.FactionName == null && turn.DisplayFactionName == "LLM seat seat-a",
            "[DEMO-S4] pre-observation entry falls back to a neutral seat label");
        Check(request.FactionName == "Wei" && request.DisplayFactionName == "Wei",
            "[DEMO-S4] activity carries the faction name from the seat-filtered observation");

        IReadOnlyList<LlmActivity> snapshot = runner.Activity;
        int count = snapshot.Count;
        fake.EnqueueDeferred();
        var next = new LlmHarnessHost(engine, "seat-a");
        runner.Begin(next, next.Token, "seat-a", 4242);
        Check(snapshot.Count == count && runner.Activity.Count > count,
            "[DEMO-S4] a previously returned Activity snapshot is not changed by later entries");
        runner.Cancel("test cleanup");
        Check(snapshot.Count == count,
            "[DEMO-S4] cancellation cannot mutate an already returned snapshot");
    }

    // ---- 3: a transport that throws inside Begin must not wedge the session --------

    /// <summary>Throws on the first request, then delegates to a scripted fake transport.</summary>
    private sealed class ThrowingOnceTransport : ILlmTransport
    {
        public readonly FakeLlmTransport Inner = new FakeLlmTransport();
        public int Calls;

        public string Provider => "fake";
        public string Model => "fake-model";

        public void EnqueueSuccess(string text) => Inner.EnqueueSuccess(text);

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            Calls++;
            if (Calls == 1) throw new InvalidOperationException("transport exploded inside Begin");
            return Inner.SendAsync(req, ct);
        }
    }

    /// <summary>A host whose observation throws synchronously inside runner.Begin.</summary>
    private sealed class ThrowingObserveHost : ITurnHost
    {
        public Observation Observe() => throw new InvalidOperationException("observe exploded");
        public ValidationResult Submit(ActionCommand cmd) => ValidationResult.Fail("unexpected");
        public void EndTurn() { }
    }

    private static void RunDemoS4SpectatorBeginFailure()
    {
        Section("[DEMO-S4] spectator survives a transport/host throwing inside Begin");

        var hostThrowRunner = new LlmTurnRunner(new FakeLlmTransport(), new LlmConfig(), null);
        bool escaped = false;
        try { hostThrowRunner.Begin(new ThrowingObserveHost(), 1, "seat-a", 7); }
        catch (Exception) { escaped = true; }
        Check(!escaped && hostThrowRunner.State == RunnerState.Failed
              && hostThrowRunner.EndReason == EndReason.HostError,
            "[DEMO-S4] a host throwing in Observe becomes a failed turn, not an escaping exception");

        var engine = SpectatorEngine();
        var transport = new ThrowingOnceTransport();
        // seat-a's first Begin call throws synchronously; the next seat must still run normally.
        SpectatorQueueTurn(transport.Inner, "seat-b", 1);
        var runner = new LlmTurnRunner(transport,
            new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 2, MaxTotalRequests = 30 }, null);
        var session = SpectatorNew(engine, runner, 1);

        session.Start();
        Check(session.State == SpectatorState.Running && session.ActiveSeatId == "seat-a"
              && runner.State == RunnerState.Failed
              && runner.EndReason == EndReason.TransportError,
            "[DEMO-S4] a throw inside Begin ends the turn as failed, not as an exception");

        SpectatorDrain(session);
        Check(session.State == SpectatorState.Completed
              && session.TurnRecords.Count == 2
              && session.TurnRecords[0].SeatId == "seat-a"
              && session.TurnRecords[0].EndedBy.StartsWith("failed", StringComparison.Ordinal)
              && session.TurnRecords[1].SeatId == "seat-b"
              && session.TurnRecords[1].EndReason == EndReason.ModelEndedTurn
              && transport.Calls == 3,
            "[DEMO-S4] the next seat and season still run after Begin threw");
    }
}
