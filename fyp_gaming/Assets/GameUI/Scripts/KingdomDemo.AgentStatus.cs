// KingdomDemo (partial) — read-only agent status helpers and play-mode lifecycle.
//
// These methods feed the docked agent panel and the debug hooks. They do not add any information path:
// every value comes from the runner/config the UI already owns.

using System;
using System.Globalization;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // The docked panel itself lives in KingdomDemo.AgentPanel.cs; the helpers below feed it.
        bool IsAgentRunning()
            => agentTurnActive && agentRunner != null
               && (agentRunner.State == RunnerState.Requesting
                   || agentRunner.State == RunnerState.Executing);

        string AgentStateName()
        {
            if (!agentHasSeat) return "unavailable";
            if (!agentFakeMode && !string.IsNullOrEmpty(agentUnavailableReason)) return "unavailable";
            if (agentRunner == null) return "idle";
            switch (agentRunner.State)
            {
                case RunnerState.Requesting: return "requesting";
                case RunnerState.Executing: return "executing";
                case RunnerState.Finished: return "finished";
                case RunnerState.Failed: return "failed";
                case RunnerState.Cancelled: return "cancelled";
                default: return "idle";
            }
        }

        string AgentResultLabel()
        {
            LlmTurnRunner runner = agentRunner;
            if (runner == null || string.IsNullOrEmpty(runner.LastResult)) return "-";
            if (runner.LastResult == "ok" || runner.LastResult == "end_turn") return runner.LastResult;
            if (runner.State == RunnerState.Failed)
                return "failed: " + (string.IsNullOrEmpty(runner.FailureReason)
                    ? runner.LastResult : runner.FailureReason);
            if (runner.LastResult.StartsWith("invalid response", StringComparison.Ordinal))
                return runner.LastResult;
            return "rejected: " + runner.LastResult;
        }

        /// <summary>The fair-play panel only reveals the public outcome, never the validator's detail.</summary>
        string AgentPublicResultLabel()
        {
            LlmTurnRunner runner = agentRunner;
            if (runner == null || string.IsNullOrEmpty(runner.LastResult)) return "-";
            if (runner.LastResult == "ok" || runner.LastResult == "end_turn") return "ok";
            if (runner.State == RunnerState.Failed) return "failed";
            return "rejected";
        }

        string AgentFailureReason()
        {
            if (!agentFakeMode && !string.IsNullOrEmpty(agentUnavailableReason))
                return agentUnavailableReason;
            LlmTurnRunner runner = agentRunner;
            if (runner == null) return null;
            return runner.State == RunnerState.Failed || runner.State == RunnerState.Cancelled
                ? runner.FailureReason : null;
        }

        /// <summary>Primary UI never shows a raw file path; the path is only a tooltip (and the log).</summary>
        string AgentRecorderLabel()
            => string.IsNullOrEmpty(agentRecorderShort) ? "Run record: not saved" : "Run record saved";

        string AgentElapsedLabel()
        {
            float seconds = agentTurnStartedAt > 0f ? Time.unscaledTime - agentTurnStartedAt : agentLastElapsed;
            return seconds <= 0f ? "" : Mathf.RoundToInt(seconds).ToString(CultureInfo.InvariantCulture) + "s";
        }

        static string AgentShort(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            string one = value.Replace('\r', ' ').Replace('\n', ' ');
            if (one.Length <= max) return one;
            return max <= 3 ? one.Substring(0, max) : one.Substring(0, max - 3) + "...";
        }

        static string JsonText(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ------------------------------------------------------------------ debug hooks
        /// <summary>One-line rehearsal status: state|requests|actions|lastResult|failure.</summary>
        public string DebugAgentStatus()
        {
            if (!agentHasSeat) return "unavailable|0|0|-|no LLM seat";
            int req = agentRunner != null ? agentRunner.RequestsThisTurn : 0;
            int act = agentRunner != null ? agentRunner.ActionsThisTurn : 0;
            string last = agentRunner != null ? agentRunner.LastResult : null;
            return AgentField(AgentStateName()) + "|" + req + "|" + act + "|"
                 + AgentField(last) + "|" + AgentField(AgentFailureReason());
        }

        public void DebugCancelAgent() => CancelAgentTurn();

        static string AgentField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // ------------------------------------------------------------------ play-mode exit
        /// <summary>
        /// Re-enabling the component (or a scripted enable/disable cycle) must resume the coroutine;
        /// Start() normally owns the first start, so this is a no-op until the game is ready.
        /// </summary>
        void OnEnable()
        {
            if (ready) StartAgentTicker();
        }

        void OnDisable()
        {
            if (spectatorMode) StopSpectatorSession();
            if (agentTickLoop != null) { StopCoroutine(agentTickLoop); agentTickLoop = null; }
            if (agentRunner != null && IsAgentRunning()) agentRunner.Cancel("play mode ended");
            int token = agentSeatToken; agentSeatToken = 0;
            if (token != 0 && engine != null) engine.EndSeatTurn(token);
            agentTurnActive = false; agentResumePending = false; ReleaseControlLock();
        }
    }
}
