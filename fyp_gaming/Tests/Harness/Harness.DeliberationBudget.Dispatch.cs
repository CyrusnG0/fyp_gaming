using System;
using LuanShi.Engine;

// [LS-019] Dispatch is a one-time permission gate, and AppliedToTurn uses the right identity.
//
// MarkDispatched must return Ok exactly once, only for the currently bound (seat, turn) bucket, only
// while the ledger is not halted, and only while the admission is still inside its wall-time window.
// A denial is a pure no-op: counters and holds stay untouched so the host can Cancel the reservation.
// Already-dispatched late work still settles once against its original bucket.

internal static partial class Harness
{
    private static void DeliberationDispatchGate()
    {
        // ---- duplicate and repeated dispatch ---------------------------------
        DeliberationBudgetLedger ledger =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 4 });
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission first = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        Check(ledger.MarkDispatched(first.ReservationId).Ok,
            "[LS-019] the first dispatch of an admitted reservation is permitted");

        string afterFirstDispatch = ledger.Snapshot().ToJson();
        DeliberationBindingResult duplicate = ledger.MarkDispatched(first.ReservationId);
        DeliberationBindingResult repeated = ledger.MarkDispatched(first.ReservationId);
        Check(!duplicate.Ok && duplicate.Reason == DeliberationDenyReason.AlreadyDispatched
              && duplicate.Code == "already_dispatched",
            "[LS-019] a duplicate dispatch is denied with its own typed reason");
        Check(!repeated.Ok && repeated.Reason == DeliberationDenyReason.AlreadyDispatched
              && ledger.Snapshot().ToJson() == afterFirstDispatch,
            "[LS-019] repeated dispatch is denied and leaves every counter and hold unchanged");
        Check(ledger.Settle(first.ReservationId, DeliberationSettlement.Known(90, 150)).Applied
              && ledger.Settle(first.ReservationId, DeliberationSettlement.Known(90, 150)).Outcome
                 == DeliberationSettlementOutcome.AlreadySettled,
            "[LS-019] the gated reservation still settles exactly once");

        // ---- turn switch: pending is stale, dispatched late work still settles ----
        DeliberationBudgetLedger switched =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 4 });
        switched.BindTurn("seat-a", 1);
        DeliberationAdmission pending = switched.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationAdmission lateWork = switched.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        Check(switched.MarkDispatched(lateWork.ReservationId).Ok,
            "[LS-019] late work is dispatched under its original binding");

        switched.BindTurn("seat-b", 1);
        string beforeStale = switched.Snapshot().ToJson();
        DeliberationBindingResult stale = switched.MarkDispatched(pending.ReservationId);
        Check(!stale.Ok && stale.Reason == DeliberationDenyReason.StaleTurn
              && stale.Code == "stale_turn" && switched.Snapshot().ToJson() == beforeStale,
            "[LS-019] pending work from a replaced binding cannot dispatch and changes nothing");

        DeliberationCancelResult releasedStale =
            switched.Cancel(pending.ReservationId, DeliberationCancelReason.Cancelled);
        Check(releasedStale.Outcome == DeliberationCancelOutcome.Released
              && releasedStale.ReleasedInputTokens == 100
              && releasedStale.ReleasedOutputTokens == 200,
            "[LS-019] a denied undispatched reservation can still be cancelled and its holds released");

        string beforeLate = switched.Snapshot().ToJson();
        DeliberationSettlementResult late =
            switched.Settle(lateWork.ReservationId, DeliberationSettlement.Known(90, 150));
        Check(late.Applied && !late.AppliedToTurn,
            "[LS-019] already-dispatched late work settles once against its original bucket");
        DeliberationBudgetSnapshot afterLate = switched.Snapshot();
        Check(afterLate.Session.InputTokens == 90 && afterLate.Session.OutputTokens == 150
              && afterLate.Turn.InputTokens == 0 && afterLate.Turn.OutputTokens == 0
              && afterLate.Turn.Requests == 0,
            "[LS-019] late settlement never mutates the newly bound turn's counters");
        Check(switched.Settle(lateWork.ReservationId, DeliberationSettlement.Known(1, 1)).Outcome
              == DeliberationSettlementOutcome.AlreadySettled
              && switched.Snapshot().ToJson() == afterLate.ToJson(),
            "[LS-019] late settlement cannot double-count");
        DeliberationAdmission secondSeat = switched.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(switched.MarkDispatched(secondSeat.ReservationId).Ok
              && switched.Settle(secondSeat.ReservationId, DeliberationSettlement.Known(5, 6)).Applied,
            "[LS-019] the bound seat can still dispatch and settle legitimate work");

        // ---- halt after admission --------------------------------------------
        DeliberationBudgetLedger halted = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerTurn = 500,
        }, DeliberationBudgetMode.TokenStrict);
        halted.BindTurn("seat-a", 1);
        DeliberationAdmission overrun = halted.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationAdmission heldWhileHalted = halted.TryAdmitRequest(DeliberationTokenBounds.Of(100, 100));
        halted.MarkDispatched(overrun.ReservationId);
        DeliberationSettlementResult over = halted.Settle(overrun.ReservationId,
            DeliberationSettlement.Known(150, 600));
        Check(over.Applied && over.Overrun && halted.Blocked,
            "[LS-019] an overrun halts the ledger after another admission is already pending");

        string beforeHalt = halted.Snapshot().ToJson();
        DeliberationBindingResult blocked = halted.MarkDispatched(heldWhileHalted.ReservationId);
        Check(!blocked.Ok && blocked.Reason == DeliberationDenyReason.TokenOverrun
              && blocked.Code == "token_overrun" && halted.Snapshot().ToJson() == beforeHalt,
            "[LS-019] a halted ledger refuses pending dispatch without changing counters or holds");
        DeliberationCancelResult cancelledHalted =
            halted.Cancel(heldWhileHalted.ReservationId, DeliberationCancelReason.Cancelled);
        Check(cancelledHalted.Outcome == DeliberationCancelOutcome.Released
              && cancelledHalted.ReleasedInputTokens == 100
              && cancelledHalted.ReleasedOutputTokens == 100
              && halted.Snapshot().Turn.ReservedInputTokens == 0,
            "[LS-019] the host can release the pending reservation after a halt denial");
    }

    private static void DeliberationDispatchExpiry()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxWallMillisPerTurn = 1000,
        }, clock: clock);
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission held = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(10, 20));

        clock.Advance(1000);
        string beforeExpired = ledger.Snapshot().ToJson();
        DeliberationBindingResult expired = ledger.MarkDispatched(held.ReservationId);
        Check(!expired.Ok && expired.Reason == DeliberationDenyReason.TurnWallTimeExhausted
              && expired.Code == "turn_wall_time_exhausted"
              && ledger.Snapshot().ToJson() == beforeExpired,
            "[LS-019] admission held past the wall-time window cannot dispatch and changes nothing");

        DeliberationCancelResult released =
            ledger.Cancel(held.ReservationId, DeliberationCancelReason.TimedOut);
        DeliberationBudgetSnapshot afterRelease = ledger.Snapshot();
        Check(released.Outcome == DeliberationCancelOutcome.Released
              && released.ReleasedInputTokens == 10 && released.ReleasedOutputTokens == 20
              && afterRelease.Turn.InputTokens == 0 && afterRelease.Turn.OutputTokens == 0
              && afterRelease.Turn.ReservedInputTokens == 0
              && afterRelease.Turn.ReservedOutputTokens == 0,
            "[LS-019] an expired denied reservation is cancelled without charging tokens");

        Check(ledger.BindTurn("seat-a", 2).Ok,
            "[LS-019] the host can open the next turn after an expired admission");
        DeliberationAdmission fresh = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(10, 20));
        Check(ledger.MarkDispatched(fresh.ReservationId).Ok,
            "[LS-019] a fresh admission inside the new wall-time window may dispatch");
        Check(ledger.Settle(fresh.ReservationId, DeliberationSettlement.Known(8, 9)).Applied,
            "[LS-019] subsequent legitimate settlement applies after the expiry denial");
    }

    private static void DeliberationAppliedToTurnIdentity()
    {
        DeliberationBudgetLedger ledger =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 6 });
        ledger.BindTurn("seat-a", 1);

        DeliberationAdmission current = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        ledger.MarkDispatched(current.ReservationId);
        DeliberationSettlementResult currentSettle =
            ledger.Settle(current.ReservationId, DeliberationSettlement.Known(10, 20));
        Check(currentSettle.Applied && currentSettle.AppliedToTurn,
            "[LS-019] AppliedToTurn is true for work against the currently bound bucket");

        DeliberationAdmission otherSeat = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        ledger.BindTurn("seat-b", 1);
        DeliberationSettlementResult otherSeatSettle =
            ledger.Settle(otherSeat.ReservationId, DeliberationSettlement.Known(5, 6));
        Check(otherSeatSettle.Applied && !otherSeatSettle.AppliedToTurn
              && ledger.Snapshot().Turn.Requests == 0
              && ledger.Snapshot().Turn.InputTokens == 0,
            "[LS-019] AppliedToTurn is false after a different seat is bound");

        Check(ledger.BindTurn("seat-a", 1).Ok,
            "[LS-019] the host can restore the still-open seat-a turn-one bucket");
        DeliberationAdmission restored = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        DeliberationSettlementResult restoredSettle =
            ledger.Settle(restored.ReservationId, DeliberationSettlement.Known(3, 4));
        Check(restoredSettle.Applied && restoredSettle.AppliedToTurn,
            "[LS-019] AppliedToTurn is true again for the same still-open restored bucket");

        DeliberationAdmission differentTurn = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        ledger.BindTurn("seat-a", 2);
        DeliberationSettlementResult differentTurnSettle =
            ledger.Settle(differentTurn.ReservationId, DeliberationSettlement.Known(1, 2));
        Check(differentTurnSettle.Applied && !differentTurnSettle.AppliedToTurn,
            "[LS-019] AppliedToTurn is false after a different turn of the same seat is bound");
    }
}
