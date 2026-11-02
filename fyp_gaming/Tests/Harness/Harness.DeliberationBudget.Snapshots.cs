using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [LS-019] Wall-time boundaries, policy/limit validation, zero-limit semantics and detached snapshots.
//
// The clock is injected so every boundary is exact: no sleeps, no provider, no Unity editor.

internal static partial class Harness
{
    private static void DeliberationWallTime()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 10,
            MaxWallMillisPerTurn = 1000,
        }, clock: clock);
        ledger.BindTurn("seat-a", 1);

        clock.Advance(999);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] admission just inside the turn wall-time budget is allowed");
        clock.Advance(1);
        DeliberationAdmission expired = ledger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!expired.Allowed && expired.Reason == DeliberationDenyReason.TurnWallTimeExhausted
              && expired.Code == "turn_wall_time_exhausted",
            "[LS-019] the turn wall-time boundary denies at exactly the limit");
        Check(ledger.Snapshot().Turn.WallClockMillis == 1000,
            "[LS-019] the snapshot records the observed elapsed turn time");

        clock.Advance(500);
        Check(ledger.BindTurn("seat-a", 2).Ok, "[LS-019] the host can bind the next turn");
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] a new turn restarts the per-turn wall-time window");

        var sessionClock = new FakeDeliberationClock();
        DeliberationBudgetLedger sessionLedger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 10,
            MaxWallMillisPerSession = 2000,
        }, clock: sessionClock);
        sessionLedger.BindTurn("seat-a", 1);
        sessionClock.Advance(1999);
        Check(sessionLedger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] admission just inside the session wall-time budget is allowed");
        sessionClock.Advance(1);
        DeliberationAdmission sessionExpired =
            sessionLedger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!sessionExpired.Allowed
              && sessionExpired.Reason == DeliberationDenyReason.SessionWallTimeExhausted,
            "[LS-019] the session wall-time boundary denies at exactly the limit");
        sessionLedger.BindTurn("seat-b", 1);
        Check(!sessionLedger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] a new player does not reset the session wall-time budget");

        var clockLedger = DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 10 },
            clock: new FakeDeliberationClock());
        Check(clockLedger.Snapshot().Session.Requests == 0,
            "[LS-019] a ledger starts with no session requests");
        var backwards = new FakeDeliberationClock();
        DeliberationBudgetLedger backwardsLedger =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 10 }, clock: backwards);
        backwardsLedger.BindTurn("seat-a", 1);
        backwards.Now = 100;
        Check(backwardsLedger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] a monotonic advance keeps admission open");
        backwards.Now = 50;
        DeliberationAdmission backwardsDenied =
            backwardsLedger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!backwardsDenied.Allowed && backwardsDenied.Reason == DeliberationDenyReason.ClockInvalid
              && backwardsDenied.Blocked,
            "[LS-019] a backwards clock halts admission instead of granting time");
        backwards.Now = 200;
        Check(!backwardsLedger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed
              && backwardsLedger.Snapshot().StopCode == "clock_invalid",
            "[LS-019] the clock fault is sticky and reported as an explicit stop");

        var throwingClock = new FakeDeliberationClock { ThrowOnRead = true };
        Check(Throws<ArgumentException>(
                () => DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 1 },
                    clock: throwingClock)),
            "[LS-019] an unavailable clock fails ledger construction closed");
        Check(Throws<ArgumentException>(
                () => DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 1 },
                    clock: new FakeDeliberationClock { Now = -1 })),
            "[LS-019] a negative clock value is rejected, not treated as elapsed time");

        var flaky = new FakeDeliberationClock();
        DeliberationBudgetLedger flakyLedger =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 4 }, clock: flaky);
        flakyLedger.BindTurn("seat-a", 1);
        flaky.ThrowOnRead = true;
        DeliberationAdmission flakyDenied = flakyLedger.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!flakyDenied.Allowed && flakyDenied.Reason == DeliberationDenyReason.ClockInvalid,
            "[LS-019] a clock exception during admission is denied instead of thrown");
    }

    private static void DeliberationValidation()
    {
        var requestLimits = new DeliberationBudgetLimits { MaxRequestsPerTurn = 2 };
        Check(Throws<ArgumentOutOfRangeException>(() => new DeliberationBudgetPolicy("p", "1",
                DeliberationBudgetMode.RequestLimited,
                new DeliberationBudgetLimits { MaxRequestsPerTurn = -1 })),
            "[LS-019] a negative limit is rejected");
        Check(Throws<ArgumentException>(() => new DeliberationBudgetPolicy("p", "1",
                DeliberationBudgetMode.RequestLimited, new DeliberationBudgetLimits
                {
                    MaxRequestsPerTurn = 2,
                    MaxOutputTokensPerTurn = 100,
                })),
            "[LS-019] request-limited mode refuses a token cap it would not enforce");
        Check(Throws<ArgumentException>(() => new DeliberationBudgetPolicy("p", "1",
                DeliberationBudgetMode.TokenStrict, new DeliberationBudgetLimits
                {
                    MaxRequestsPerTurn = 2,
                    MaxInputTokensPerTurn = 1000,
                })),
            "[LS-019] token-strict mode requires an output-token limit");
        Check(Throws<ArgumentException>(() => new DeliberationBudgetPolicy("p", "1",
                DeliberationBudgetMode.RequestLimited, new DeliberationBudgetLimits())),
            "[LS-019] a request-limited policy with no request cap is rejected");
        Check(Throws<ArgumentException>(() => new DeliberationBudgetPolicy(" ", "1",
                DeliberationBudgetMode.RequestLimited, requestLimits)),
            "[LS-019] a profile id is required for the experiment record");
        Check(Throws<ArgumentException>(() => new DeliberationBudgetPolicy("p", "",
                DeliberationBudgetMode.RequestLimited, requestLimits)),
            "[LS-019] a profile version is required for the experiment record");
        Check(Throws<ArgumentOutOfRangeException>(() => new DeliberationBudgetPolicy("p", "1",
                DeliberationBudgetMode.RequestLimited, new DeliberationBudgetLimits
                {
                    MaxRequestsPerTurn = 2,
                    MaxTokenBoundPerRequest = 0,
                })),
            "[LS-019] the per-request token-bound ceiling must be positive");
        Check(Throws<ArgumentOutOfRangeException>(() => DeliberationBudgetPolicies.FromDemoConfig(
                new LlmConfig { MaxTurnSeconds = int.MaxValue })),
            "[LS-019] a turn window that overflows milliseconds is rejected");
        Check(Throws<ArgumentException>(
                () => new DeliberationBudgetLedger("", DeliberationBudgetPolicies.DemoDefaults())),
            "[LS-019] a ledger requires a match identity");
        Check(Throws<ArgumentNullException>(() => new DeliberationBudgetLedger("m", null)),
            "[LS-019] a ledger requires a policy");

        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 5,
            MaxTokenBoundPerRequest = 1000,
        });
        ledger.BindTurn("seat-a", 1);
        Check(!ledger.TryAdmitRequest(DeliberationTokenBounds.Of(1001, 10)).Allowed
              && ledger.TryAdmitRequest(DeliberationTokenBounds.Of(1001, 10)).Reason
                 == DeliberationDenyReason.InvalidTokenBounds,
            "[LS-019] a token bound above the policy ceiling is rejected");
        Check(!ledger.TryAdmitRequest(DeliberationTokenBounds.Of(int.MaxValue, int.MaxValue)).Allowed,
            "[LS-019] absurd token bounds are rejected without overflowing");
        Check(ledger.Snapshot().Turn.Requests == 0,
            "[LS-019] rejected token bounds consume no request allowance");

        Check(!ledger.BindTurn("", 1).Ok && ledger.BindTurn("", 1).Reason
              == DeliberationDenyReason.InvalidBinding,
            "[LS-019] an empty seat id is not a valid binding");
        Check(!ledger.BindTurn("seat-a", -1).Ok,
            "[LS-019] a negative turn token is not a valid binding");
        Check(!ledger.BindTurn(new string('s', DeliberationBudgetLedger.MaxIdLength + 1), 1).Ok,
            "[LS-019] an oversized seat id is rejected, not truncated");

        ledger.BindTurn("seat-a", 3);
        Check(ledger.TryAdmitRequest(DeliberationTokenBounds.None).Allowed,
            "[LS-019] the bound turn admits work");
        int requestsBeforeRebind = (int)ledger.Snapshot().Turn.Requests;
        Check(ledger.BindTurn("seat-a", 3).Ok
              && ledger.Snapshot().Turn.Requests == requestsBeforeRebind,
            "[LS-019] re-binding the same turn is idempotent and refreshes no allowance");
        Check(!ledger.BindTurn("seat-a", 2).Ok
              && ledger.BindTurn("seat-a", 2).Reason == DeliberationDenyReason.StaleTurn,
            "[LS-019] an older turn token cannot reopen an allowance");

        DeliberationAdmission unknownKind =
            ledger.TryAdmit((DeliberationWorkKind)99, DeliberationTokenBounds.None);
        Check(!unknownKind.Allowed && unknownKind.Reason == DeliberationDenyReason.InvalidRequest,
            "[LS-019] an unknown work kind is rejected with a typed reason");

        DeliberationBudgetLedger zero = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 0,
            MaxInquiryPerTurn = 0,
            MaxActionsPerTurn = 0,
        });
        zero.BindTurn("seat-a", 1);
        Check(!zero.TryAdmitRequest(DeliberationTokenBounds.None).Allowed
              && zero.TryAdmitRequest(DeliberationTokenBounds.None).Reason
                 == DeliberationDenyReason.TurnRequestsExhausted,
            "[LS-019] a zero request limit is a zero allowance, not an unset value");
        Check(!zero.TryAdmitInquiry().Allowed
              && zero.TryAdmitInquiry().Reason == DeliberationDenyReason.TurnInquiryExhausted,
            "[LS-019] a zero inquiry limit is a zero allowance");
        Check(!zero.TryAdmitGameAction().Allowed
              && zero.TryAdmitGameAction().Reason == DeliberationDenyReason.TurnActionsExhausted,
            "[LS-019] a zero action limit is a zero allowance");
        DeliberationBudgetSnapshot zeroSnapshot = zero.Snapshot();
        Check(zeroSnapshot.Turn.Requests == 0 && zeroSnapshot.Turn.Inquiries == 0
              && zeroSnapshot.Turn.ActionAdmissions == 0,
            "[LS-019] zero allowances leave every counter at zero");

        DeliberationBudgetLedger zeroTokens = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 5,
            MaxInputTokensPerTurn = 0,
            MaxOutputTokensPerTurn = 100,
        }, DeliberationBudgetMode.TokenStrict);
        zeroTokens.BindTurn("seat-a", 1);
        Check(!zeroTokens.TryAdmitRequest(DeliberationTokenBounds.Of(10, 10)).Allowed
              && zeroTokens.TryAdmitRequest(DeliberationTokenBounds.Of(10, 10)).Reason
                 == DeliberationDenyReason.TurnInputTokensExhausted,
            "[LS-019] a zero token allowance denies the reservation up front");

        DeliberationBudgetLedger unboundedInquiry = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 2,
            MaxRequestsPerSession = DeliberationBudgetLimits.Unenforced,
        });
        unboundedInquiry.BindTurn("seat-a", 1);
        Check(unboundedInquiry.Snapshot().UnenforcedDimensions.Contains("requests.session"),
            "[LS-019] an unenforced dimension is reported as such in the snapshot");
    }

    private static void DeliberationSnapshotDetachment()
    {
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 4,
            MaxInquiryPerTurn = 2,
            MaxActionsPerTurn = 2,
        });
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission request = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(10, 20));
        ledger.Settle(request.ReservationId, DeliberationSettlement.Known(9, 12));

        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        string json = snapshot.ToJson();
        Check(DeliberationParses(json) && json.Contains("\"schema_version\":\"ls019-v1\"")
              && json.Contains("\"match_id\":\"match-ls019\""),
            "[LS-019] a snapshot is versioned, valid JSON with the match identity");

        DeliberationAdmission action = ledger.TryAdmitGameAction();
        ledger.Settle(action.ReservationId, new DeliberationSettlement { Accepted = true });
        Check(snapshot.Turn.ActionAdmissions == 0 && snapshot.Turn.AcceptedActions == 0
              && snapshot.Session.Requests == 1 && snapshot.Session.InputTokens == 9,
            "[LS-019] a snapshot keeps its own values after later activity");
        Check(snapshot.ToJson() == json,
            "[LS-019] a snapshot serializes identically after later activity");

        Check(Throws<NotSupportedException>(() => ((IList<DeliberationHalt>)snapshot.Halts).Add(null))
              && Throws<NotSupportedException>(
                  () => ((IList<string>)snapshot.OutstandingReservationIds).Clear())
              && Throws<NotSupportedException>(
                  () => ((IList<DeliberationSeatTotals>)snapshot.Seats).Clear()),
            "[LS-019] snapshot collections are read-only");

        DeliberationBudgetLedger second = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 0,
        });
        second.BindTurn("seat-a", 1);
        string beforeDenial = second.Snapshot().ToJson();
        DeliberationAdmission denied = second.TryAdmitRequest(DeliberationTokenBounds.None);
        Check(!denied.Allowed && second.Snapshot().ToJson() == beforeDenial,
            "[LS-019] a denied admission leaves the whole ledger byte-identical");

        Check(DeliberationFlowJson() == DeliberationFlowJson(),
            "[LS-019] identical state transitions produce identical snapshots");
    }

    private static void DeliberationAccountingOutcomes()
    {
        DeliberationBudgetLedger ledger =
            DeliberationLedger(new DeliberationBudgetLimits { MaxRequestsPerTurn = 1, MaxActionsPerTurn = 2 });
        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission accepted = ledger.TryAdmitGameAction();
        DeliberationAdmission rejected = ledger.TryAdmitGameAction();
        ledger.Settle(accepted.ReservationId, new DeliberationSettlement { Accepted = true });
        ledger.Settle(rejected.ReservationId, new DeliberationSettlement { Failed = true });
        DeliberationBudgetSnapshot snapshot = ledger.Snapshot();
        Check(snapshot.Turn.ActionAdmissions == 2 && snapshot.Turn.AcceptedActions == 1
              && snapshot.Turn.Failures == 1,
            "[LS-019] accepted game actions are distinguished from action admissions");
        DeliberationAdmission retry = ledger.TryAdmitGameAction();
        Check(retry.Allowed,
            "[LS-019] a rejected action releases the accepted-action slot it was holding");
        ledger.Settle(retry.ReservationId, new DeliberationSettlement { Accepted = true });
        Check(!ledger.TryAdmitGameAction().Allowed
              && ledger.TryAdmitGameAction().Reason == DeliberationDenyReason.TurnActionsExhausted,
            "[LS-019] the accepted-action allowance stops further accepted actions");

        DeliberationBudgetLedger inquiries = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 1,
            MaxInquiryPerTurn = 2,
        });
        inquiries.BindTurn("seat-a", 1);
        DeliberationAdmission failOne = inquiries.TryAdmitInquiry();
        DeliberationAdmission failTwo = inquiries.TryAdmitInquiry();
        inquiries.Settle(failOne.ReservationId, new DeliberationSettlement { Failed = true });
        inquiries.Settle(failTwo.ReservationId, new DeliberationSettlement { Failed = true });
        DeliberationBudgetSnapshot inquirySnapshot = inquiries.Snapshot();
        Check(inquirySnapshot.Turn.Inquiries == 2 && inquirySnapshot.Turn.Failures == 2
              && inquirySnapshot.Turn.ActionAdmissions == 0,
            "[LS-019] failed inquiry attempts count against the inquiry policy");
        Check(!inquiries.TryAdmitInquiry().Allowed
              && inquiries.TryAdmitInquiry().Reason == DeliberationDenyReason.TurnInquiryExhausted,
            "[LS-019] retrying a failed inquiry cannot create unlimited free attempts");

        DeliberationBudgetLedger probe = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 1,
            MaxInquiryPerTurn = 1,
        });
        probe.BindTurn("seat-a", 1);
        DeliberationAdmission inquiry = probe.TryAdmitInquiry();
        DeliberationSettlementResult tokenish =
            probe.Settle(inquiry.ReservationId, DeliberationSettlement.Known(5000, 5000));
        Check(tokenish.Applied && tokenish.InputTokens == 0 && tokenish.OutputTokens == 0
              && !tokenish.Overrun && !tokenish.UsageUnknown,
            "[LS-019] token figures on an inquiry settlement are ignored, not charged as an overrun");
    }

    /// <summary>One fixed transition sequence; equal output proves deterministic accounting and ids.</summary>
    private static string DeliberationFlowJson()
    {
        var clock = new FakeDeliberationClock();
        DeliberationBudgetLedger ledger = DeliberationLedger(new DeliberationBudgetLimits
        {
            MaxRequestsPerTurn = 3,
            MaxInquiryPerTurn = 1,
            MaxActionsPerTurn = 1,
            MaxInputTokensPerTurn = 200,
            MaxOutputTokensPerSession = 400,
        }, DeliberationBudgetMode.TokenStrict, clock);

        ledger.BindTurn("seat-a", 1);
        DeliberationAdmission request = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(100, 200));
        clock.Advance(250);
        DeliberationAdmission inquiry = ledger.TryAdmitInquiry();
        ledger.Settle(inquiry.ReservationId, new DeliberationSettlement { Failed = true });
        ledger.MarkDispatched(request.ReservationId);
        ledger.Settle(request.ReservationId, DeliberationSettlement.Known(90, 150, 300));
        ledger.BindTurn("seat-b", 1);
        DeliberationAdmission action = ledger.TryAdmitGameAction();
        ledger.Settle(action.ReservationId, new DeliberationSettlement { Accepted = true });
        DeliberationAdmission cancelled = ledger.TryAdmitRequest(DeliberationTokenBounds.Of(10, 20));
        ledger.Cancel(cancelled.ReservationId, DeliberationCancelReason.Aborted);
        return ledger.Snapshot().ToJson();
    }
}
