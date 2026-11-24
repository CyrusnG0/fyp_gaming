// HandoverUiTypes.cs — pure-C# data contracts and engine-seam interface for between-turn
// Human <-> LLM handover UI. Zero engine mutations; zero canonical-state reads.

using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi
{
    public enum HandoverPendingKind
    {
        None = 0,
        HandoverToAi = 1,
        ReclaimToHuman = 2,
    }

    public sealed class HandoverPendingRequest
    {
        public string RequestId;
        public string SeatId;
        public string FactionName;
        public string TargetController;
        public HandoverPendingKind Kind;
        public int RequestedSeason;
    }

    public readonly struct HandoverTransferOutcome
    {
        public readonly bool Applied;
        public readonly bool Unsupported;
        public readonly bool NoPending;
        public readonly string Message;

        private HandoverTransferOutcome(bool applied, bool unsupported, bool noPending, string message)
        {
            Applied = applied;
            Unsupported = unsupported;
            NoPending = noPending;
            Message = message;
        }

        public static HandoverTransferOutcome Succeeded(string message = null)
            => new HandoverTransferOutcome(true, false, false, message);

        public static HandoverTransferOutcome Failed(string message)
            => new HandoverTransferOutcome(false, false, false, message);

        public static HandoverTransferOutcome DeferredUnsupported(string message)
            => new HandoverTransferOutcome(false, true, false, message);

        public static HandoverTransferOutcome NonePending()
            => new HandoverTransferOutcome(false, false, true, null);
    }

    /// <summary>
    /// Authoritative engine handover seam. Pending requests, cancellation, eligibility and boundary
    /// transfers live strictly inside <see cref="GameEngine"/>; UI code never maintains a second
    /// pending queue or mutates <see cref="FactionState.Controller"/> directly.
    /// </summary>
    public interface IHandoverEngineAdapter
    {
        bool IsAvailable { get; }
        string UnavailableReason { get; }
        bool IsEligibleSeat(string seatId);
        HandoverStatus GetStatus(GameEngine engine, string seatId);
        HandoverResult Request(GameEngine engine, string seatId, string toController);
        HandoverResult Cancel(GameEngine engine, string seatId);
        bool PrepareSeatTurn(GameEngine engine, string seatId);
    }

    /// <summary>
    /// Host-authorized engine adapter backed by <see cref="HandoverOperator"/> and
    /// <see cref="GameEngine.PrepareSeatTurn"/> / <see cref="GameEngine.GetHandoverStatus"/>.
    /// </summary>
    public sealed class EngineHandoverAdapter : IHandoverEngineAdapter
    {
        private readonly GameEngine _engine;
        private readonly HandoverOperator _operator;

        public EngineHandoverAdapter(GameEngine engine, IEnumerable<string> eligibleSeatIds)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _operator = _engine.CreateHandoverOperator(eligibleSeatIds ?? Array.Empty<string>());
        }

        public bool IsAvailable => _engine != null && _operator != null;
        public string UnavailableReason => IsAvailable ? null : "handover operator unavailable";

        public bool IsEligibleSeat(string seatId) => _operator.IsEligible(seatId);

        public HandoverStatus GetStatus(GameEngine engine, string seatId)
            => (engine ?? _engine)?.GetHandoverStatus(seatId);

        public HandoverResult Request(GameEngine engine, string seatId, string toController)
        {
            if (engine != null && !ReferenceEquals(engine, _engine))
                return new HandoverResult(false, "handover engine mismatch", null);
            return _operator.Request(seatId, toController);
        }

        public HandoverResult Cancel(GameEngine engine, string seatId)
        {
            if (engine != null && !ReferenceEquals(engine, _engine))
                return new HandoverResult(false, "handover engine mismatch", null);
            return _operator.Cancel(seatId);
        }

        public bool PrepareSeatTurn(GameEngine engine, string seatId)
        {
            if (engine != null && !ReferenceEquals(engine, _engine)) return false;
            return _engine.PrepareSeatTurn(seatId);
        }
    }

    /// <summary>Fail-closed adapter used when handover is disabled (replay, spectator, or no eligible seats).</summary>
    public sealed class DisabledHandoverAdapter : IHandoverEngineAdapter
    {
        public DisabledHandoverAdapter(string reason = "Handover is disabled in this mode.")
        {
            UnavailableReason = reason;
        }

        public bool IsAvailable => false;
        public string UnavailableReason { get; }
        public bool IsEligibleSeat(string seatId) => false;
        public HandoverStatus GetStatus(GameEngine engine, string seatId) => engine?.GetHandoverStatus(seatId);

        public HandoverResult Request(GameEngine engine, string seatId, string toController)
            => new HandoverResult(false, UnavailableReason, null);

        public HandoverResult Cancel(GameEngine engine, string seatId)
            => new HandoverResult(false, UnavailableReason, null);

        public bool PrepareSeatTurn(GameEngine engine, string seatId) => false;
    }

    /// <summary>
    /// Bounded snapshot of one LLM turn for one specific seat, built exclusively from seat-filtered
    /// observation data and runner telemetry for that seat.
    /// </summary>
    public sealed class HandoverSeatTurnCapture
    {
        public string SeatId;
        public int Season;
        public string EndReasonText;
        public string FailureReason;
        public int RequestsThisTurn;
        public int ActionsThisTurn;
        public string ContextReportJson;
        public SpatialTruncationReport SpatialTruncation;
        public int PromptEventCap;
        public int ObservationEventsAtTurnEnd;
        public bool ActivityGapDetected;
        public readonly List<LlmActivity> Activities = new List<LlmActivity>();
    }

    /// <summary>
    /// Per-seat record of one delegation period (Human -> LLM -> Human), storing pre-handover
    /// own-faction baseline counts and all LLM turn captures for that seat only.
    /// </summary>
    public sealed class HandoverDelegationEpisode
    {
        public const int MaxCapturedActivities = 120;

        public string SeatId;
        public string FactionName;
        public int StartedSeason;
        public int BaselineEventSeq;
        public bool HasPreHandoverBaseline;
        public int PreCitiesCount;
        public int PreTotalFood;
        public int PreTotalGold;
        public int PreTotalPopulation;
        public int PrePrestige;
        public bool ActivityRetentionDropped;
        public bool PendingBriefing;
        public readonly List<HandoverSeatTurnCapture> Turns = new List<HandoverSeatTurnCapture>();

        public int TotalCapturedActivities
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Turns.Count; i++) count += Turns[i].Activities.Count;
                return count;
            }
        }
    }
}
