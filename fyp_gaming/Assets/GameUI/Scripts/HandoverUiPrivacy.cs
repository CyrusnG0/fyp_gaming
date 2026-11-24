// HandoverUiPrivacy.cs — pure-C# UI privacy and control-boundary policy for live LLM panels,
// session chat, generic command feed, and cancel controls across human, handover, hotseat, and
// spectator modes.

using System;
using LuanShi.Engine;

namespace LuanShi
{
    public static class HandoverUiPrivacy
    {
        public static bool CanExposeLiveAgentDetails(
            bool spectatorMode,
            bool showAgentDetails,
            string agentSeatId,
            string currentSeatId,
            GameEngine engine,
            HandoverCoordinatorState handoverState)
        {
            if (spectatorMode) return true;
            if (string.IsNullOrEmpty(agentSeatId)) return false;

            int humanCount = engine != null ? SeatRotation.HumanSeats(engine.State).Count : 0;
            int eligibleCount = handoverState != null ? handoverState.EligibleSeats.Count : 0;
            if (humanCount > 1 || eligibleCount > 1) return false;

            if (humanCount > 0 || eligibleCount > 0)
            {
                string ownedSeat = eligibleCount == 1
                    ? handoverState.EligibleSeats[0]
                    : SeatRotation.HumanSeats(engine.State)[0];
                return string.Equals(agentSeatId, ownedSeat, StringComparison.Ordinal);
            }

            return showAgentDetails && string.Equals(agentSeatId, currentSeatId, StringComparison.Ordinal);
        }

        public static bool ShouldMaskAgentDetails(
            bool spectatorMode,
            bool showAgentDetails,
            string agentSeatId,
            string currentSeatId,
            GameEngine engine,
            HandoverCoordinatorState handoverState)
            => !spectatorMode && !CanExposeLiveAgentDetails(
                spectatorMode, showAgentDetails, agentSeatId, currentSeatId, engine, handoverState);

        public static bool CanCancelAgentTurn(
            bool spectatorMode,
            bool isAgentRunning,
            bool showAgentDetails,
            string agentSeatId,
            string currentSeatId,
            GameEngine engine,
            HandoverCoordinatorState handoverState)
            => !spectatorMode && isAgentRunning
               && CanExposeLiveAgentDetails(
                   spectatorMode, showAgentDetails, agentSeatId, currentSeatId, engine, handoverState);

        /// <summary>
        /// Formats a status-bar feed entry for an LLM command when live details for that seat may
        /// be shown to the viewer. Returns null when hidden (opponent or hotseat live LLM turn).
        /// </summary>
        public static string FormatLiveAgentFeedLine(
            bool exposeDetails,
            ActionCommand cmd,
            ValidationResult result)
        {
            if (!exposeDetails) return null;
            return (result.Ok ? "✓ " : "✗ ") + "[LLM] " + (cmd != null ? cmd.ToJson() : "null");
        }
    }
}
