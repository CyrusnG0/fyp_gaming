// HandoverUiTypes.cs — pure-C# data contracts and engine-seam interface for between-turn
// Human <-> LLM handover UI. Zero engine mutations; zero canonical-state reads.

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
        public string SeatId;
        public string FactionName;
        public string TargetController;
        public HandoverPendingKind Kind;
        public string Reason;
        public int RequestedSeason;
        public bool RequestedWhileActiveTurn;
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
    /// Integration seam to the authoritative engine transfer operation owned by the engine branch.
    /// UI code never writes <see cref="FactionState.Controller"/> directly.
    /// </summary>
    public interface IHandoverEngineAdapter
    {
        bool IsTransferSupported { get; }
        string UnavailableReason { get; }
        HandoverTransferOutcome TryTransferBetweenTurns(
            GameEngine engine, string seatId, string targetController, string reason);
    }

    /// <summary>
    /// Baseline 9eeb633 placeholder adapter: keeps the UI compiling cleanly against the baseline
    /// engine without creating a parallel transfer mechanism or mutating FactionState.Controller.
    /// </summary>
    public sealed class BaselineDeferredHandoverAdapter : IHandoverEngineAdapter
    {
        public bool IsTransferSupported => false;

        public string UnavailableReason
            => "Engine handover API pending integration from handover-engine (baseline 9eeb633 stays unchanged).";

        public HandoverTransferOutcome TryTransferBetweenTurns(
            GameEngine engine, string seatId, string targetController, string reason)
            => HandoverTransferOutcome.DeferredUnsupported(UnavailableReason);
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
