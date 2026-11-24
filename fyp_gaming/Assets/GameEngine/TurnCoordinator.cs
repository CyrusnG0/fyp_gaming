using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Contract A: host-driven per-seat turn gate.
    ///
    /// The gate is OFF until the host first calls <see cref="BeginSeatTurn"/>, so legacy callers that
    /// simply call <see cref="Submit"/> keep working unchanged. Once enabled, only the active seat may
    /// submit; <see cref="EndSeatTurn"/> closes the seat but leaves the gate ON, so no seat may act until
    /// the host begins the next turn. Seasons do not reset it: the host calls BeginSeatTurn for every
    /// seat turn (including the first turn of a new season).
    ///
    /// The controller type is never bound here. <see cref="Submit"/> already replaces it with the
    /// faction's recorded controller, and SubmitAs deliberately routes through Submit for the same
    /// validation/execution/logging path.
    /// </summary>
    public sealed partial class GameEngine
    {
        private bool turnGateEnabled;
        private string activeSeatId;
        private int turnToken;
        private string turnController;   // the seat's controller when the active turn began

        /// <summary>The seat whose turn it is, or null when the gate is off or no seat is active.</summary>
        public string ActiveSeatId => activeSeatId;

        /// <summary>Incremented by every BeginSeatTurn; 0 until the first one.</summary>
        public int TurnToken => turnToken;

        /// <summary>
        /// Host-only: starts a seat's turn. Unknown seats are rejected before any gate state changes.
        /// </summary>
        public int BeginSeatTurn(string seatId)
        {
            if (State.FindFaction(seatId) == null)
                throw new ArgumentException($"unknown seat '{seatId}'", nameof(seatId));

            turnGateEnabled = true;
            activeSeatId = seatId;
            turnController = State.FindFaction(seatId).Controller;
            return ++turnToken;
        }

        /// <summary>
        /// Host-only: ends the active seat's turn. A stale token is ignored, so an old response cannot
        /// close a newer turn. The token is not invalidated here; with no active seat the gate rejects
        /// everything until the next BeginSeatTurn.
        /// </summary>
        public void EndSeatTurn(int token)
        {
            if (!turnGateEnabled || token != turnToken) return;
            string closed = activeSeatId;
            activeSeatId = null;
            turnController = null;
            // The only same-turn handover hook: a request waits for the seat's own turn to close.
            if (closed != null) ApplyPendingHandover(closed, "after_turn");
        }

        /// <summary>
        /// Host-bound submission for controller output. The host supplies the seat and captured token;
        /// the command's actor is overwritten on the accepted path, so an LLM cannot choose who acts.
        /// </summary>
        public ValidationResult SubmitAs(string seatId, int token, ActionCommand cmd)
        {
            if (!turnGateEnabled || token != turnToken)
                return Reject(cmd, "stale turn token");
            if (activeSeatId == null || !string.Equals(seatId, activeSeatId, StringComparison.Ordinal))
                return Reject(cmd, "not this seat's turn");
            if (cmd == null) return Submit(null);

            cmd.ActorSeatId = seatId;
            return Submit(cmd);
        }

        /// <summary>
        /// Controller-bound host submission. <paramref name="controllerType"/> is the kind of controller the
        /// host is running (llm for the runner). It must equal both the controller the active turn began with
        /// and the seat's current controller, so a reply that outlived a handover can never be stamped as, or
        /// executed for, the new controller. The command's own controller claim is overwritten.
        /// </summary>
        public ValidationResult SubmitAs(string seatId, int token, string controllerType, ActionCommand cmd)
        {
            if (cmd != null)
            {
                // Host-bound identity: a discarded reply is recorded against the seat, never with a controller claim.
                cmd.ControllerType = null;
                cmd.ActorSeatId = seatId;
            }
            if (!turnGateEnabled || token != turnToken)
                return Reject(cmd, "stale turn token");
            if (activeSeatId == null || !string.Equals(seatId, activeSeatId, StringComparison.Ordinal))
                return Reject(cmd, "not this seat's turn");
            if (cmd == null) return Submit(null);
            var fac = State.FindFaction(seatId);
            if (fac == null || controllerType != fac.Controller || controllerType != turnController)
            {
                cmd.ActorSeatId = seatId;
                return Reject(cmd, "controller mismatch");
            }
            cmd.ActorSeatId = seatId;
            cmd.ControllerType = controllerType;
            return Submit(cmd);
        }

        /// <summary>
        /// Called by <see cref="Submit"/> before any command validation. When the gate has never been
        /// enabled this returns true unconditionally (legacy behaviour); once enabled it requires a
        /// begun, not-yet-ended seat that matches the command actor.
        /// </summary>
        private bool TurnGateAccepts(ActionCommand cmd)
            => !turnGateEnabled
               || (activeSeatId != null
                   && string.Equals(cmd.ActorSeatId, activeSeatId, StringComparison.Ordinal));
    }
}
