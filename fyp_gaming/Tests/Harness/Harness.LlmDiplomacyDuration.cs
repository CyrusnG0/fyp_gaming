using System.Linq;
using LuanShi.Engine;

// [DIP-3] A bare integer args.duration through the real runner, fake provider and real GameEngine.
// Live evidence: run-20261122140759713-c8b0fcfc requests 8-10 sent "duration":8 / 8 / 12 and were
// refused as "invalid field type". Only duration is widened; bounds and refusals stay strict.

internal static partial class Harness
{
    private static void DipRunnerIntegerDuration()
    {
        string Raw(string literal) => DipProposeRaw(literal, "seat-a");

        // Integer and quoted forms reach the engine as the same command and the same recorded offer.
        var engineInt = DiplomacyScenario();
        var sink = new MemoryRecorderSink();
        DipTurnResult asInt = DipTurn(engineInt, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), sink, 44,
            Raw("4"), DipEndTurn());
        var engineStr = DiplomacyScenario();
        DipTurnResult asStr = DipTurn(engineStr, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), null, 44,
            DipPropose("seat-a", TreatyType.Nap, "4", "peace"), DipEndTurn());
        TreatyProposal pInt = engineInt.State.Diplomacy.FindProposal("seat-b", "seat-a");
        TreatyProposal pStr = engineStr.State.Diplomacy.FindProposal("seat-b", "seat-a");
        DipRunnerCheck(asInt.Host.SubmitCalls == 1 && pInt != null && pInt.DurationSeasons == 4
           && pStr != null && pStr.DurationSeasons == pInt.DurationSeasons
           && asInt.Host.Commands[0].Args["duration"] == asStr.Host.Commands[0].Args["duration"],
            "an integer duration becomes the same engine offer as the quoted string");
        DipRunnerCheck(!asInt.Runner.RecentHistory.Contains("invalid model response"),
            "the integer reply costs no invalid-response budget");
        DipRunnerCheck(sink.ExchangeLines.Any(l => l.Contains("\"exchange_event\":\"result\"")
                && l.Contains("\\\"duration\\\":\\\"4\\\"") && l.Contains("\"parse_ok\":true")
                && l.Contains("\"result_ok\":true")),
            "the captured command shows the normalised string the engine received, with the result");

        // The engine still clamps the range, exactly as for a quoted value.
        var engineBig = DiplomacyScenario();
        DipTurn(engineBig, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), null, 45, Raw("99"), DipEndTurn());
        TreatyProposal pBig = engineBig.State.Diplomacy.FindProposal("seat-b", "seat-a");
        DipRunnerCheck(pBig != null && pBig.DurationSeasons == BalanceConfig.TreatyDurationMaxSeasons,
            "an in-shape large integer is clamped by the engine to the configured maximum");

        // Out-of-shape integers never reach the engine and the feedback says why.
        foreach (string literal in new[] { "0", "-3", "1000" })
        {
            var engine = DiplomacyScenario();
            DipTurnResult bad = DipTurn(engine, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), null, 46,
                Raw(literal), DipEndTurn());
            DipRunnerCheck(bad.Host.SubmitCalls == 0
               && bad.Runner.RecentHistory.Contains("invalid model response -> duration must be a number of seasons")
               && engine.State.Diplomacy.FindProposal("seat-b", "seat-a") == null,
                "integer duration " + literal + " is refused before the engine");
        }

        // A wrong type other than an integer is named, and no other key accepts an integer.
        var engineBool = DiplomacyScenario();
        DipTurnResult wrong = DipTurn(engineBool, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), null, 47,
            Raw("true"), DipEndTurn());
        DipRunnerCheck(wrong.Host.SubmitCalls == 0 && wrong.Runner.RecentHistory.Contains(
                "invalid model response -> invalid field type: args.duration must be a whole number of seasons such as 8 or \"8\""),
            "a boolean duration is refused with feedback that names the field and the accepted form");
        var engineKey = DiplomacyScenario();
        DipTurnResult key = DipTurn(engineKey, "seat-b", new FakeLlmTransport(), DipRunnerConfig(), null, 48,
            "{\"type\":\"propose_treaty\",\"target_id\":\"seat-a\",\"args\":{\"treaty_type\":7}}", DipEndTurn());
        DipRunnerCheck(key.Host.SubmitCalls == 0 && key.Runner.RecentHistory.Contains(
                "invalid model response -> invalid field type: args.treaty_type must be a quoted string"),
            "an integer treaty_type is still refused and the feedback names that field");

        // Feature off: the diplomacy vocabulary stays closed, so the integer shape is never read.
        var engineOff = DiplomacyScenario();
        DipTurnResult off = DipTurn(engineOff, "seat-b", new FakeLlmTransport(), DipRunnerConfig(diplomacy: false),
            null, 49, Raw("4"), DipEndTurn());
        DipRunnerCheck(off.Host.SubmitCalls == 0
           && engineOff.State.Diplomacy.FindProposal("seat-b", "seat-a") == null,
            "with diplomacy off an integer-duration proposal is still refused");
    }
}
