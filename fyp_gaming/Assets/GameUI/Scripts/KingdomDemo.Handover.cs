// KingdomDemo.Handover.cs — UI flow, between-turn seam calls, Faction Control dock, and
// seat-specific Reclaim Briefing modal for Human <-> LLM handover.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private readonly HandoverCoordinatorState handoverState = new HandoverCoordinatorState();
        private IHandoverEngineAdapter handoverAdapter = new BaselineDeferredHandoverAdapter();
        private HandoverReclaimBriefing activeReclaimBriefing;
        private bool reclaimBriefingShowing;
        private Vector2 reclaimBriefingScroll;
        private string handoverStatusNote;

        private const float ControlDockGap = 8f;
        private const float ControlDockPad = 10f;
        private const float ControlDockRowGap = 4f;

        void InitializeHandoverState()
        {
            activeReclaimBriefing = null;
            reclaimBriefingShowing = false;
            reclaimBriefingScroll = Vector2.zero;
            handoverStatusNote = null;

            if (engine == null || replayMode || spectatorMode)
            {
                handoverState.Initialize(null, null);
                return;
            }
            handoverState.Initialize(engine.State.Factions, humanSeats);
        }

        /// <summary>
        /// Allows test or integration wiring to supply the engine-backed adapter once Claude's
        /// handover-engine API is integrated. UI code never mutates FactionState.Controller directly.
        /// </summary>
        public void SetHandoverEngineAdapter(IHandoverEngineAdapter adapter)
        {
            handoverAdapter = adapter ?? new BaselineDeferredHandoverAdapter();
        }

        bool IsHandoverUiVisible()
            => ready && engine != null && !replayMode && !spectatorMode && !reportShowing
               && !battleResultShowing && !handoverShowing && engine.State.WinnerSeatId == null
               && handoverState.HasEligibleSeats;

        bool EnsureAgentRunnerReady(out string unavailableReason)
        {
            unavailableReason = null;
            if (replayMode || spectatorMode)
            {
                unavailableReason = "not available in replay or spectator mode";
                return false;
            }
            if (agentLoadedConfig == null) LoadAgentConfig();
            if (agentRunner == null
                || FakeTransportRequested != agentFakeMode
                || fakeGarrisonRehearsal != agentGarrisonRehearsalActive)
                RebuildAgentRunner();
            if (agentRunner == null || (!FakeTransportRequested && !string.IsNullOrEmpty(agentUnavailableReason)))
            {
                unavailableReason = string.IsNullOrEmpty(agentUnavailableReason)
                    ? AgentNoKey : agentUnavailableReason;
                return false;
            }
            return true;
        }

        bool RequestHandoverToAiForSeat(string seatId)
        {
            if (engine == null || replayMode || spectatorMode) return false;
            if (!string.Equals(currentSeat, seatId, StringComparison.Ordinal) || uiInputLocked)
            {
                handoverStatusNote = "Hand over to AI can only be requested during your active human turn.";
                return false;
            }

            if (!EnsureAgentRunnerReady(out string unavailableReason))
            {
                handoverStatusNote = "Cannot hand over to AI: " + unavailableReason;
                statusMsg = handoverStatusNote;
                return false;
            }

            FactionState fac = engine.State.FindFaction(seatId);
            if (!handoverState.RequestHandoverToAi(
                    seatId, fac?.Name, fac?.Controller, activeTurnToken != 0, engine.State.Season, out string error))
            {
                handoverStatusNote = error;
                statusMsg = error;
                return false;
            }

            handoverStatusNote = null;
            statusMsg = $"{fac?.Name}: handover to AI pending at turn end";
            return true;
        }

        bool RequestReclaimToHumanForSeat(string seatId)
        {
            if (engine == null || replayMode || spectatorMode) return false;
            FactionState fac = engine.State.FindFaction(seatId);
            bool active = agentTurnActive && string.Equals(agentSeatId, seatId, StringComparison.Ordinal);

            if (!handoverState.RequestReclaimToHuman(
                    seatId, fac?.Name, fac?.Controller, active, engine.State.Season, out string error))
            {
                handoverStatusNote = error;
                statusMsg = error;
                return false;
            }

            handoverStatusNote = null;
            statusMsg = active
                ? $"{fac?.Name}: take control pending (waiting for turn boundary)"
                : $"{fac?.Name}: take control pending at next turn";
            return true;
        }

        bool CancelPendingHandoverForSeat(string seatId)
        {
            if (!handoverState.CancelPending(seatId, out string error))
            {
                handoverStatusNote = error;
                return false;
            }

            handoverStatusNote = null;
            string name = engine?.State.FindFaction(seatId)?.Name ?? seatId;
            statusMsg = $"{name}: pending controller transfer cancelled";
            return true;
        }

        bool ApplyPendingHandoverAtBoundary(string boundarySeatId)
        {
            if (engine == null || replayMode || spectatorMode) return false;
            string openEngineSeat = activeTurnToken != 0 ? currentSeat : (agentSeatToken != 0 ? agentSeatId : null);
            HandoverTransferOutcome outcome = handoverState.ApplyPendingAtSafeBoundary(
                engine, boundarySeatId, openEngineSeat, handoverAdapter, FilteredObservation(boundarySeatId));

            if (outcome.NoPending) return false;

            if (!outcome.Applied)
            {
                handoverStatusNote = outcome.Message ?? "Controller transfer did not apply.";
                statusMsg = handoverStatusNote;
                return false;
            }

            handoverStatusNote = null;
            if (!string.IsNullOrEmpty(currentSeat)) RefreshObservation();
            string name = engine.State.FindFaction(boundarySeatId)?.Name ?? boundarySeatId;
            string ctrl = ControllerLabel(engine.State.FindFaction(boundarySeatId)?.Controller);
            statusMsg = $"{name}: control transferred to {ctrl}";
            return true;
        }

        void RecordCompletedAgentTurnForHandover(string seatId)
        {
            if (string.IsNullOrEmpty(seatId) || agentRunner == null) return;
            Observation seatObs = FilteredObservation(seatId);
            handoverState.RecordLlmTurnForSeat(
                seatId,
                seatObs != null ? seatObs.Season : (engine != null ? engine.State.Season : 1),
                seatObs,
                agentRunner.ActivityForSeat(seatId),
                agentRunner.EndReason != EndReason.None ? agentRunner.EndReasonText : agentRunner.Status,
                agentRunner.FailureReason,
                agentRunner.RequestsThisTurn,
                agentRunner.ActionsThisTurn,
                agentRunner.LastContextReportJson,
                agentRunner.LastSpatialTruncation,
                PromptBuilder.MaxPromptObservationEvents);
        }

        void TryOpenPendingReclaimBriefing(string seatId)
        {
            if (string.IsNullOrEmpty(seatId) || !handoverState.HasPendingBriefing(seatId)) return;
            if (reportShowing || handoverShowing || battleResultShowing) return;

            activeReclaimBriefing = HandoverBriefingBuilder.Build(
                seatId, obs, handoverState.GetEpisode(seatId), PromptBuilder.MaxPromptObservationEvents);
            handoverState.ConsumePendingBriefing(seatId);
            reclaimBriefingScroll = Vector2.zero;
            reclaimBriefingShowing = true;
        }

        void OpenCurrentSeatEpisodeBriefing()
        {
            if (string.IsNullOrEmpty(currentSeat)) return;
            HandoverDelegationEpisode ep = handoverState.GetEpisode(currentSeat);
            if (ep == null) return;
            activeReclaimBriefing = HandoverBriefingBuilder.Build(
                currentSeat, obs, ep, PromptBuilder.MaxPromptObservationEvents);
            reclaimBriefingScroll = Vector2.zero;
            reclaimBriefingShowing = true;
        }

        string TopBarPendingSuffix(string seatId)
        {
            if (!handoverState.TryGetPending(seatId, out var req)) return "";
            return req.Kind == HandoverPendingKind.HandoverToAi
                ? " (AI pending)"
                : " (Human pending)";
        }

        void DrawFactionControlPanel()
        {
            if (!IsHandoverUiVisible()) return;

            var eligible = handoverState.EligibleSeats;
            float panelX = TabColumnX;
            float panelY = TabTop + Tabs.GetLength(0) * TabStep + ControlDockGap;
            float panelW = TabColumnWidth;
            float maxBottom = DesignHeight - UiLayout.BottomMargin;
            if (panelY + 80f > maxBottom) return;

            // Compute compact height per eligible seat (1-2 lines + 1 button)
            float contentY = ControlDockPad + 22f;
            float perSeatH = 56f;
            float noteHeight = string.IsNullOrEmpty(handoverStatusNote)
                ? 0f
                : Mathf.Max(28f, uiReasonWrap.CalcHeight(new GUIContent(handoverStatusNote), panelW - ControlDockPad * 2f) + 6f);
            bool showBriefingButton = !uiInputLocked && handoverState.GetEpisode(currentSeat) != null;
            float briefingBtnH = showBriefingButton ? 30f : 0f;

            float panelH = Mathf.Min(
                maxBottom - panelY,
                contentY + eligible.Count * perSeatH + briefingBtnH + noteHeight + ControlDockPad);
            var area = new Rect(panelX, panelY, panelW, panelH);
            uiRects.Add(area);
            DrawBackdrop(area, CurrentSeatAccent());

            float x = area.x + ControlDockPad;
            float w = area.width - ControlDockPad * 2f;
            float y = area.y + ControlDockPad;

            GUI.Label(new Rect(x, y, w, 20f), "Faction Control", uiPanelTitle);
            y += 22f;

            var prevEnabled = GUI.enabled;
            var smallBtn = new GUIStyle(uiButton) { fontSize = 13 };
            for (int i = 0; i < eligible.Count; i++)
            {
                string seatId = eligible[i];
                FactionState fac = engine.State.FindFaction(seatId);
                if (fac == null) continue;

                bool hasPending = handoverState.TryGetPending(seatId, out var pending);
                bool isHuman = fac.Controller == ControllerType.Human;
                bool isLlm = fac.Controller == ControllerType.Llm;
                bool isActiveHumanTurn = isHuman && !uiInputLocked && string.Equals(currentSeat, seatId, StringComparison.Ordinal);

                string stateLine = eligible.Count > 1
                    ? $"{fac.Name}: {ControllerLabel(fac.Controller)}"
                    : $"Controller: {ControllerLabel(fac.Controller)}";
                if (hasPending)
                    stateLine += pending.Kind == HandoverPendingKind.HandoverToAi ? " -> AI" : " -> Human";

                GUI.Label(new Rect(x, y, w, 18f), EllipsizeToWidth(uiSmall, stateLine, w), uiSmall);
                y += 18f;

                var btnRect = new Rect(x, y, w, 28f);
                if (hasPending)
                {
                    GUI.enabled = true; // cancel stays available even while locked
                    string cancelLabel = pending.Kind == HandoverPendingKind.HandoverToAi
                        ? "Cancel handover"
                        : "Cancel take control";
                    if (GUI.Button(btnRect, cancelLabel, smallBtn))
                        CancelPendingHandoverForSeat(seatId);
                }
                else if (isHuman)
                {
                    GUI.enabled = isActiveHumanTurn;
                    string label = eligible.Count > 1 ? $"Hand over {fac.Name}" : "Hand over to AI";
                    if (GUI.Button(btnRect, label, isActiveHumanTurn ? smallBtn : uiButtonDisabledSmall))
                        RequestHandoverToAiForSeat(seatId);
                }
                else if (isLlm)
                {
                    GUI.enabled = true; // Take control remains interactive even when uiInputLocked == true!
                    string label = eligible.Count > 1 ? $"Take control ({fac.Name})" : "Take control";
                    if (GUI.Button(btnRect, label, smallBtn))
                        RequestReclaimToHumanForSeat(seatId);
                }
                y += 32f + ControlDockRowGap;
            }

            if (showBriefingButton && y + 26f <= area.yMax - ControlDockPad)
            {
                GUI.enabled = true;
                if (GUI.Button(new Rect(x, y, w, 26f), "Reclaim briefing", smallBtn))
                    OpenCurrentSeatEpisodeBriefing();
                y += 30f;
            }

            GUI.enabled = prevEnabled;

            if (!string.IsNullOrEmpty(handoverStatusNote) && y + 16f <= area.yMax)
            {
                GUI.Label(new Rect(x, y, w, area.yMax - y - 4f), handoverStatusNote, uiReasonWrap);
            }
        }

        void DrawReclaimBriefingModal()
        {
            if (!reclaimBriefingShowing || activeReclaimBriefing == null) return;

            string bodyText = activeReclaimBriefing.FormatPlainText();
            float contentHeight = uiBodyWrap.CalcHeight(new GUIContent(bodyText), ReportBodyWidth) + 8f;
            float textHeight = Mathf.Min(ReportTextMaxHeight, Mathf.Max(60f, contentHeight));

            var p = new Rect(ReportPanelX, ReportPanelY, ReportPanelWidth, ReportPanelBaseHeight + textHeight);
            uiRects.Add(p);
            DrawBackdrop(p, CurrentSeatAccent());

            GUI.Label(new Rect(p.x + 20f, p.y + 12f, ReportTitleWidth, 30f),
                EllipsizeToWidth(uiTitle, activeReclaimBriefing.Title, ReportTitleWidth), uiTitle);

            var view = new Rect(p.x + 20f, p.y + 50f, ReportTitleWidth, textHeight);
            if (contentHeight > textHeight + 0.5f)
            {
                TryWheelScroll(p, view, contentHeight, ref reclaimBriefingScroll);
                reclaimBriefingScroll = GUI.BeginScrollView(
                    view, reclaimBriefingScroll, new Rect(0f, 0f, ReportBodyWidth, contentHeight), false, false);
                GUI.Label(new Rect(0f, 0f, ReportBodyWidth, contentHeight), bodyText, uiBodyWrap);
                GUI.EndScrollView();
            }
            else
            {
                GUI.Label(new Rect(view.x, view.y, ReportTitleWidth, contentHeight + 8f), bodyText, uiBodyWrap);
            }

            float closeX = p.x + (ReportPanelWidth - ReportContinueWidth) * 0.5f;
            if (GUI.Button(new Rect(closeX, p.y + 50f + textHeight + 14f, ReportContinueWidth, ReportContinueHeight),
                "Continue", uiButton))
            {
                reclaimBriefingShowing = false;
                reclaimBriefingScroll = Vector2.zero;
                statusMsg = SeatTurnPrompt();
            }
        }
    }
}
