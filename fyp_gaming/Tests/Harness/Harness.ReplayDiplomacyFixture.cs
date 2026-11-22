using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [DIP-3] A constructed-fixture replay regression: the offline ReplaySession is driven with a
// hand-built bundle for the verified run id and the session's own fixed two-seat scenario, once with a
// diplomacy prompt identity and once with the base identity.
//
// This is NOT replay of a real recorded diplomacy match: ReplayLoader/ReplaySession still accept only
// ReplayLoader.VerifiedDemoRunId and their fixed scenario, and no such run exists. It proves only that
// the replay paths classify, re-parse and re-submit a diplomacy reply consistently, and fail closed
// under a non-diplomacy identity.

internal static partial class Harness
{
    private static void RunReplayDiplomacyFixture()
    {
        Section("[DIP-3] constructed-fixture replay: diplomacy identity vs base identity");

        ReplayBundle diplomacy = DipReplayBundle(PromptBuilder.DiplomacyPromptVersion);
        var session = new ReplaySession(diplomacy);
        session.RunToCompletion();

        EventRecord message = session.Engine.Log.Records.FirstOrDefault(r => r.EventType == "message_sent");
        DipReplayCheck(session.IsCompleted
           && session.AcceptedActionsReplayed == 1
           && session.VoluntaryEndTurnsReplayed == 1
           && session.UnexpectedRejections == 0
           && session.UnexpectedAcceptances == 0
           && session.ActionOutcomeMismatches == 0
           && session.RejectionReasonMismatches == 0,
            "a diplomacy-identity request is re-parsed and accepted, and the end_turn is replayed");
        DipReplayCheck(message != null && message.ActorSeatId == "seat-0"
           && message.Payload["to_seat_id"] == "seat-1"
           && message.Payload["text"] == DipReplayMessageText,
            "the authoritative engine logged the replayed message with its text");
        DipReplayCheck(PromptBuilder.IsDiplomacyPromptVersion(diplomacy.Requests[0].PromptVersion)
           && diplomacy.Requests[0].PromptVersion == PromptBuilder.DiplomacyPromptVersion,
            "the fixture records the diplomacy prompt identity the replay derives its flags from");
        // The fixture carries no recorded observation hash, so both requests must show up as
        // mismatches: the replay never claims an exact match it was not given.
        DipReplayCheck(session.ObservationHashesMatched == 0 && session.ObservationHashMismatches == 2,
            "no recorded observation hash means no claimed observation match");

        ReplayBundle baseline = DipReplayBundle(PromptBuilder.PromptVersion);
        var baseSession = new ReplaySession(baseline);
        baseSession.RunToCompletion();
        DipReplayCheck(baseSession.AcceptedActionsReplayed == 0
           && baseSession.UnexpectedRejections == 1
           && baseSession.ActionOutcomeMismatches >= 1
           && baseSession.Engine.Log.Records.All(r => r.EventType != "message_sent"),
            "under the base identity the same reply fails closed and nothing is logged");

        ReplayBundle conversion = DipReplayBundle(PromptBuilder.DiplomacyPromptVersion
            + PromptBuilder.GarrisonConversionVersionSuffix);
        conversion.Requests[0].RawModelText = "{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":\"infantry\"}}";
        var conversionRequests = new List<ReplayRequestRecord>(conversion.Requests);
        conversionRequests.Insert(1, new ReplayRequestRecord
        {
            RunId = ReplayLoader.VerifiedDemoRunId, Seed = 12345, SeatId = "seat-0",
            Season = 1, RequestIndex = 2, TotalRequests = 2, TurnToken = 1,
            PromptVersion = conversion.Requests[0].PromptVersion, ParseOk = true, ResultOk = true,
            ResultRecorded = true,
            RawModelText = "{\"type\":\"convert_garrison\",\"target_id\":\"city-wei\",\"param_a\":1000,\"param_b\":0,\"reason\":\"" + new string('r', 203) + "\"}",
        });
        conversion.Requests = conversionRequests;
        var conversionSession = new ReplaySession(conversion);
        conversionSession.RunToCompletion();
        DipReplayCheck(conversionSession.AcceptedActionsReplayed == 2
           && conversionSession.Engine.Log.Records.Any(r => r.EventType == "garrison_converted"),
            "a conversion-enabled identity replays recruitment and a bounded-reason conversion");

        conversionRequests[1].ParseOk = false;
        conversionRequests[1].ResultOk = false;
        conversionRequests[1].ResultError = "string too long";
        conversionRequests[1].PromptVersion = PromptBuilder.DiplomacyPromptVersion
            + PromptBuilder.LegacyGarrisonConversionVersionSuffix;
        var invalidSession = new ReplaySession(conversion);
        invalidSession.RunToCompletion();
        DipReplayCheck(invalidSession.IsCompleted
           && invalidSession.AcceptedActionsReplayed == 1
           && invalidSession.RejectedActionsReplayed == 1
           && invalidSession.VoluntaryEndTurnsReplayed == 1
           && invalidSession.UnexpectedRejections == 0
           && invalidSession.UnexpectedAcceptances == 0
           && invalidSession.RejectionReasonMismatches == 0
           && invalidSession.ActionOutcomeMismatches == 0
           && invalidSession.Engine.Log.Records.All(r => r.EventType != "garrison_converted"),
            "a historically invalid conversion is not executed by a more permissive parser");
    }

    private static void DipReplayCheck(bool ok, string name) => Check(ok, "[DIP-3] " + name);

    private const string DipReplayMessageText = "a replayed peace offer";

    /// <summary>
    /// A two-request bundle for the verified run id: one message to the other seat, then end_turn.
    /// The session's own fixed scenario supplies seat-0/seat-1; nothing here touches the loader.
    /// </summary>
    private static ReplayBundle DipReplayBundle(string promptVersion)
    {
        const int seed = 12345;
        var requests = new List<ReplayRequestRecord>
        {
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = seed, SeatId = "seat-0",
                Season = 1, RequestIndex = 1, TotalRequests = 2, TurnToken = 1,
                PromptVersion = promptVersion, ParseOk = true, ResultOk = true, ResultRecorded = true,
                RawModelText = "{\"type\":\"send_message\",\"target_id\":\"seat-1\",\"text\":\""
                    + DipReplayMessageText + "\"}",
            },
            new ReplayRequestRecord
            {
                RunId = ReplayLoader.VerifiedDemoRunId, Seed = seed, SeatId = "seat-0",
                Season = 1, RequestIndex = 2, TotalRequests = 2, TurnToken = 1,
                PromptVersion = promptVersion, ParseOk = true, ResultOk = true, ResultRecorded = true,
                RawModelText = "{\"type\":\"end_turn\"}",
            },
        };
        return new ReplayBundle
        {
            Summary = new ReplaySummaryData
            {
                RunId = ReplayLoader.VerifiedDemoRunId,
                Seed = seed,
                BalanceProfileKey = "easy-research",
                SeasonsPlanned = 1,
                SeasonsPlayed = 1,
            },
            Requests = requests,
        };
    }
}
