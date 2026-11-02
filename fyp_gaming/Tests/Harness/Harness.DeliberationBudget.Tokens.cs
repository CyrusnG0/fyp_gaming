using System;
using LuanShi.Engine;

// [LS-019] Token reservations, single settlement, dispatch/cancel/timeout and late completion.
//
// The ledger must count a reservation before dispatch, charge measured usage once, record (not clamp)
// usage above the reservation, and let an obsolete turn's completion settle without authorizing more
// work. No provider, network or clock sleeps are involved: the clock is a fake.

internal static partial class Harness
{
    private static void DeliberationTokenReservations()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 6,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerTurn = 500,
        }, DeliberationBudgetMode.TokenStrict, clock);
        ledger.BindTurn("seat-a", 1);

        DeliberationAdmission first = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 300));
        Check(first.Allowed && first.ReservedInputTokens == 100 && first.ReservedOutputTokens == 300,
            "[LS-019] an admitted request holds its full input/output token reservation");
        DeliberationAdmission second = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 250));
        Check(!second.Allowed && second.Reason == DeliberationDenyReason.TurnOutputTokensExhausted
              && second.Code == "turn_output_tokens_exhausted",
            "[LS-019] an insufficient output-token allowance denies the reservation");
        DeliberationBudgetSnapshot held = ledger.Snapshot();
        Check(held.Turn.ReservedOutputTokens == 300 && held.Turn.OutputTokens == 0,
            "[LS-019] a reservation is reported as a bound, not as measured usage");

        DeliberationAdmission missingBound = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!missingBound.Allowed && missingBound.Reason == DeliberationDenyReason.TokenBoundRequired,
            "[LS-019] token-strict admission fails closed without host-supplied bounds");
        DeliberationAdmission negative = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(-1, 10));
        Check(!negative.Allowed && negative.Reason == DeliberationDenyReason.InvalidTokenBounds,
            "[LS-019] a negative token bound is rejected, never repaired");

        DeliberationSettlementResult settled =
            ledger.Settle(first.ReservationId, DeliberationSettlement.Known(100, 120, 1500));
        Check(settled.Applied && settled.InputTokens == 100 && settled.OutputTokens == 120
              && settled.InputUsageKnown && settled.OutputUsageKnown && !settled.UsageUnknown,
            "[LS-019] settlement charges measured usage and releases the rest of the reservation");
        DeliberationBudgetSnapshot afterSettle = ledger.Snapshot();
        Check(afterSettle.Turn.OutputTokens == 120 && afterSettle.Turn.ReservedOutputTokens == 0
              && afterSettle.Turn.MeasuredElapsedMillis == 1500,
            "[LS-019] the settled measurement replaces the held upper bound");

        DeliberationAdmission third = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 300));
        Check(third.Allowed, "[LS-019] released output allowance can be reserved again");
        DeliberationAdmission tooMuchInput = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(950, 10));
        Check(!tooMuchInput.Allowed && tooMuchInput.Reason == DeliberationDenyReason.TurnInputTokensExhausted,
            "[LS-019] an input reservation is checked against the per-turn allowance");

        DeliberationBudgetLedger sessionLedger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 10,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerSession = 100,
        }, DeliberationBudgetMode.TokenStrict);
        sessionLedger.BindTurn("seat-a", 1);
        Check(sessionLedger.TryAdmitRequest(DeliberationTokenBounds.Of(1, 60)).Allowed,
            "[LS-019] the first session token reservation fits");
        DeliberationAdmission sessionDenied =
            sessionLedger.TryAdmitRequest(DeliberationTokenBounds.Of(1, 60));
        Check(!sessionDenied.Allowed
              && sessionDenied.Reason == DeliberationDenyReason.SessionOutputTokensExhausted,
            "[LS-019] an outstanding reservation is charged against the session token cap");
        sessionLedger.BindTurn("seat-b", 1);
        DeliberationAdmission otherSeat =
            sessionLedger.TryAdmitRequest(DeliberationTokenBounds.Of(1, 60));
        Check(!otherSeat.Allowed && otherSeat.Reason == DeliberationDenyReason.SessionOutputTokensExhausted,
            "[LS-019] session token holds are not refreshed by another player");
    }

    private static void DeliberationSettlementLifecycle()
    {
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxInputTokensPerTurn = 2000,
            MaxOutputTokensPerTurn = 1000,
        }, DeliberationBudgetMode.TokenStrict);
        ledger.BindTurn("seat-a", 1);

        DeliberationAdmission first = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationSettlementResult once =
            ledger.Settle(first.ReservationId, DeliberationSettlement.Known(80, 150));
        string afterOnce = ledger.Snapshot().ToJson();
        DeliberationSettlementResult twice =
            ledger.Settle(first.ReservationId, DeliberationSettlement.Known(80, 150));
        Check(once.Applied && twice.Outcome == DeliberationSettlementOutcome.AlreadySettled
              && twice.Code == "already_settled",
            "[LS-019] a duplicate settlement is reported and not applied");
        Check(ledger.Snapshot().ToJson() == afterOnce,
            "[LS-019] a duplicate settlement cannot double-count usage");

        DeliberationAdmission second = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        string beforeInvalid = ledger.Snapshot().ToJson();
        DeliberationSettlementResult invalid =
            ledger.Settle(second.ReservationId, new DeliberationSettlement { InputTokens = -5 });
        Check(invalid.Outcome == DeliberationSettlementOutcome.Rejected
              && invalid.Reason == DeliberationDenyReason.InvalidUsage,
            "[LS-019] a malformed settlement is rejected with a typed reason");
        Check(ledger.Snapshot().ToJson() == beforeInvalid,
            "[LS-019] a rejected settlement leaves the reservation open and every counter unchanged");
        Check(ledger.Settle(second.ReservationId, DeliberationSettlement.Known(0, 0)).Applied,
            "[LS-019] the host can retry the same reservation with valid usage");

        DeliberationSettlementResult unknownReservation =
            ledger.Settle("dr-missing", DeliberationSettlement.Known(1, 1));
        Check(unknownReservation.Outcome == DeliberationSettlementOutcome.UnknownReservation
              && unknownReservation.Code == "unknown_reservation",
            "[LS-019] settling an unknown identity is a typed no-op");
        Check(!ledger.MarkDispatched("dr-missing").Ok
              && ledger.Cancel("dr-missing", DeliberationCancelReason.Cancelled).Outcome
                 == DeliberationCancelOutcome.UnknownReservation,
            "[LS-019] dispatch and cancel of an unknown identity are typed no-ops");
        Check(ledger.Snapshot().OutstandingReservationIds.Count == 0,
            "[LS-019] no reservation stays outstanding after settlement");
    }

    private static void DeliberationDispatchAndCancel()
    {
        DeliberationBudgetLedger limited =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 2 });
        limited.BindTurn("seat-a", 1);
        DeliberationAdmission released = limited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationCancelResult cancel =
            limited.Cancel(released.ReservationId, DeliberationCancelReason.Cancelled);
        Check(cancel.Outcome == DeliberationCancelOutcome.Released
              && cancel.ReleasedInputTokens == 100 && cancel.ReleasedOutputTokens == 200,
            "[LS-019] cancelling before dispatch releases only that reservation's hold");
        DeliberationBudgetSnapshot afterCancel = limited.Snapshot();
        Check(afterCancel.Turn.Requests == 1 && afterCancel.Turn.InputTokens == 0
              && afterCancel.Turn.ReservedInputTokens == 0,
            "[LS-019] a pre-dispatch cancel refunds no request and charges no tokens");
        Check(limited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200)).Allowed,
            "[LS-019] releasing the reservation frees its token hold");
        Check(!limited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200)).Allowed,
            "[LS-019] the cancelled attempt still consumed its request slot");

        DeliberationBudgetLedger strict = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 3,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerTurn = 500,
        }, DeliberationBudgetMode.TokenStrict);
        strict.BindTurn("seat-a", 1);
        DeliberationAdmission dispatched = strict.TryAdmitRequest(DeliberationTokenBounds.Of(100, 300));
        Check(strict.MarkDispatched(dispatched.ReservationId).Ok,
            "[LS-019] the host marks work dispatched before handing it to a provider");
        DeliberationCancelResult timeout =
            strict.Cancel(dispatched.ReservationId, DeliberationCancelReason.TimedOut);
        Check(timeout.Outcome == DeliberationCancelOutcome.Charged
              && timeout.ChargedInputTokens == 100 && timeout.ChargedOutputTokens == 300
              && timeout.UsageUnknown,
            "[LS-019] a dispatched call that times out charges its reserved bound");
        DeliberationBudgetSnapshot afterTimeout = strict.Snapshot();
        Check(afterTimeout.Turn.InputTokens == 100 && afterTimeout.Turn.OutputTokens == 300
              && afterTimeout.Turn.ReservedOutputTokens == 0,
            "[LS-019] the timeout charge becomes measured usage, not a leftover hold");
        DeliberationAdmission blocked = strict.TryAdmitRequest(DeliberationTokenBounds.Of(10, 10));
        Check(!blocked.Allowed && blocked.Reason == DeliberationDenyReason.UsageUnknown
              && blocked.Code == "usage_unknown" && blocked.Blocked,
            "[LS-019] token-strict accounting halts after unknown provider usage");
        Check(strict.Snapshot().StopCode == "usage_unknown" && !strict.Snapshot().TokenAccountingSafe,
            "[LS-019] the stop and the unsafe accounting are explicit in the snapshot");
        Check(strict.Cancel(dispatched.ReservationId, DeliberationCancelReason.TimedOut).Outcome
              == DeliberationCancelOutcome.AlreadySettled
              && strict.Settle(dispatched.ReservationId, DeliberationSettlement.Known(5, 5)).Outcome
                 == DeliberationSettlementOutcome.AlreadySettled,
            "[LS-019] a timeout followed by a late completion cannot double-charge");

        DeliberationBudgetLedger requestLimited =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 3 });
        requestLimited.BindTurn("seat-a", 1);
        DeliberationAdmission gap = requestLimited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 300));
        requestLimited.MarkDispatched(gap.ReservationId);
        DeliberationCancelResult limitedTimeout =
            requestLimited.Cancel(gap.ReservationId, DeliberationCancelReason.TimedOut);
        Check(limitedTimeout.Outcome == DeliberationCancelOutcome.Charged
              && limitedTimeout.UsageUnknown && !limitedTimeout.Blocked,
            "[LS-019] request-limited mode records unknown usage without halting admission");
        DeliberationBudgetSnapshot limitedSnapshot = requestLimited.Snapshot();
        Check(!limitedSnapshot.TokenBudgetEnforced && !limitedSnapshot.TokenAccountingSafe,
            "[LS-019] a request-limited record says tokens are unenforced and the gap is known");
        Check(requestLimited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 100)).Allowed,
            "[LS-019] request-limited mode keeps admitting after unknown usage");
    }

    private static void DeliberationTurnBucketsAndUnboundedTokens()
    {
        // Returning to an open (seat, turn) bucket restores its counters; a shared session total
        // still sees both seats, and the restored per-turn allowance stays bounded.
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 2,
            MaxRequestsPerSession = 10,
        });
        ledger.BindTurn("seat-a", 1);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] seat-a opens its turn bucket");
        ledger.BindTurn("seat-b", 1);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] seat-b has its own per-turn bucket in the same turn token");
        Check(ledger.BindTurn("seat-a", 1).Ok
              && ledger.Snapshot().Turn.Requests == 1,
            "[LS-019] returning to an open seat/turn restores its counters instead of resetting them");
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] the restored bucket keeps admitting until its own cap");
        Check(!ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed
              && ledger.Snapshot().Turn.Requests == 2,
            "[LS-019] the restored per-turn allowance is still bounded");
        Check(ledger.Snapshot().Session.Requests == 3,
            "[LS-019] the session total counts every seat's spend");

        // Request-limited mode may dispatch without a token bound. Measured usage is then recorded
        // as evidence and cannot be an overrun, because nothing was reserved to exceed.
        DeliberationBudgetLedger unbounded =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 2 });
        unbounded.BindTurn("seat-a", 1);
        DeliberationAdmission request = unbounded.TryAdmitRequest(DeliberationTokenBounds.None);
        DeliberationSettlementResult measured =
            unbounded.Settle(request.ReservationId, DeliberationSettlement.Known(1234, 567));
        Check(measured.Applied && measured.InputTokens == 1234 && measured.OutputTokens == 567
              && !measured.Overrun && !measured.Blocked,
            "[LS-019] unbound request-limited usage is measured, not treated as an overrun");
        Check(unbounded.Snapshot().Turn.InputTokens == 1234
              && unbounded.Snapshot().Turn.OutputTokens == 567
              && !unbounded.Snapshot().TokenBudgetEnforced,
            "[LS-019] the measured usage is recorded while token enforcement is reported as off");
        Check(unbounded.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] request-limited mode continues after measured usage");

        // An admitted action holds a reservation until the engine answer. Cancelling before the
        // engine dispatch releases only that hold; the action admission itself is still recorded.
        DeliberationBudgetLedger actions = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 1,
            MaxActionsPerTurn = 1,
        });
        actions.BindTurn("seat-a", 1);
        DeliberationAdmission abandoned = actions.TryAdmitGameAction();
        actions.Cancel(abandoned.ReservationId, DeliberationCancelReason.Aborted);
        Check(actions.TryAdmitGameAction().Allowed,
            "[LS-019] cancelling an action before engine dispatch releases its accepted-action hold");
        Check(actions.Snapshot().Turn.ActionAdmissions == 2,
            "[LS-019] the cancelled action admission is still recorded as evidence");
    }

    private static void DeliberationLateCompletion()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 5,
            MaxInputTokensPerTurn = 5000,
            MaxOutputTokensPerTurn = 2000,
        }, DeliberationBudgetMode.TokenStrict, clock);
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission firstTurn = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));

        clock.Advance(1000);
        ledger.BindTurn("seat-a", 2);
        DeliberationAdmission secondTurn = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(50, 60));
        DeliberationSettlementResult late =
            ledger.Settle(firstTurn.ReservationId, DeliberationSettlement.Known(90, 140, 900));
        Check(late.Applied && !late.AppliedToTurn,
            "[LS-019] a late completion settles once against its own, no-longer-bound turn");
        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(snapshot.Session.InputTokens == 90 && snapshot.Session.OutputTokens == 140
              && snapshot.Turn.OutputTokens == 0 && snapshot.Turn.ReservedOutputTokens == 60,
            "[LS-019] late usage reaches the session total, never the current turn's usage");
        Check(snapshot.Session.MeasuredElapsedMillis == 900
              && snapshot.Turn.MeasuredElapsedMillis == 0,
            "[LS-019] late elapsed time is recorded against the session, not the new turn");
        Check(snapshot.Turn.Requests == 1 && snapshot.Session.Requests == 2,
            "[LS-019] a late completion grants no extra request allowance");
        Check(snapshot.OutstandingReservationIds.Count == 1
              && snapshot.OutstandingReservationIds[0] == secondTurn.ReservationId,
            "[LS-019] the closed turn's hold is released and only the open reservation remains");
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.Of(50, 60)).Allowed,
            "[LS-019] the late completion did not block the current turn");
        Check(ledger.BindTurn("seat-a", 1).Reason == DeliberationDenyReason.StaleTurn,
            "[LS-019] a completed turn cannot be rebound to reclaim its allowance");
    }

    private static void DeliberationUnknownUsage()
    {
        DeliberationBudgetLedger strict = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerTurn = 500,
        }, DeliberationBudgetMode.TokenStrict);
        strict.BindTurn("seat-a", 1);
        DeliberationAdmission request = strict.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationSettlementResult unknown =
            strict.Settle(request.ReservationId, DeliberationSettlement.Unknown(800));
        Check(unknown.Applied && unknown.UsageUnknown && !unknown.InputUsageKnown
              && unknown.InputTokens == 100 && unknown.OutputTokens == 200,
            "[LS-019] missing provider usage charges the reserved bound and is marked unknown");
        DeliberationBudgetSnapshot strictSnapshot = strict.Snapshot();
        Check(strictSnapshot.Turn.UnknownUsageEvents == 1 && strictSnapshot.Turn.InputTokens == 100
              && strictSnapshot.Blocked && !strictSnapshot.TokenAccountingSafe,
            "[LS-019] token-strict mode fails closed after a missing-usage settlement");
        Check(!strict.TryAdmitRequest(DeliberationTokenBounds.Of(10, 10)).Allowed,
            "[LS-019] no further work is admitted once token accounting is unsafe");

        DeliberationBudgetLedger limited =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 4 });
        limited.BindTurn("seat-a", 1);
        DeliberationAdmission limitedRequest =
            limited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationSettlementResult limitedUnknown =
            limited.Settle(limitedRequest.ReservationId, DeliberationSettlement.Unknown());
        Check(limitedUnknown.Applied && limitedUnknown.UsageUnknown && !limitedUnknown.Blocked,
            "[LS-019] request-limited mode records missing usage without claiming enforcement");
        DeliberationBudgetSnapshot limitedSnapshot = limited.Snapshot();
        Check(limitedSnapshot.Turn.UnknownUsageEvents == 1
              && limitedSnapshot.Halts.Count == 1
              && limitedSnapshot.Halts[0].Code == "usage_unknown"
              && !limitedSnapshot.TokenBudgetEnforced,
            "[LS-019] the gap is visible as an explicit halt record in the snapshot");
        DeliberationAdmission afterGap = limited.TryAdmitRequest(DeliberationTokenBounds.Of(100, 100));
        Check(afterGap.Allowed,
            "[LS-019] request-limited admission continues when tokens are not the enforced bound");
    }

    private static void DeliberationOverrun()
    {
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxInputTokensPerTurn = 1000,
            MaxOutputTokensPerTurn = 500,
        }, DeliberationBudgetMode.TokenStrict);
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission request = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        DeliberationSettlementResult overrun =
            ledger.Settle(request.ReservationId, DeliberationSettlement.Known(120, 500));
        Check(overrun.Applied && overrun.Overrun && overrun.OutputTokens == 500,
            "[LS-019] reported usage above the reservation is recorded, not clamped");
        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(snapshot.Turn.OutputTokens == 500 && snapshot.Turn.OverrunEvents == 1
              && snapshot.Session.OutputTokens == 500 && snapshot.Blocked
              && snapshot.StopCode == "token_overrun",
            "[LS-019] an overrun is recorded in full and halts further admission");
        DeliberationAdmission next = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(10, 10));
        Check(!next.Allowed && next.Reason == DeliberationDenyReason.TokenOverrun
              && next.Blocked,
            "[LS-019] the overrun stop reason is returned to the host explicitly");
        Check(snapshot.Session.OutputTokens > 200,
            "[LS-019] the overrun is visible in the session total, not hidden by the reservation");
    }
}
