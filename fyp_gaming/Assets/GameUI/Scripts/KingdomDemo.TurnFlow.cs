// KingdomDemo (partial) — command and turn flow: SubmitHuman, seat rotation, season rollover.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------------ orders

        bool SubmitHuman(ActionCommand cmd)
        {
            cmd.ActorSeatId = currentSeat;
            cmd.ControllerType = ControllerType.Human;
            var r = engine.Submit(cmd);
            PushJson(cmd, r);
            if (r.Ok && cmd.Type == ActionType.March)
            {
                ClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
                selArmy = null;
            }
            if (r.Ok && cmd.Type == ActionType.ConvertGarrison)
            {
                var convertedArmy = engine.State.ArmyAt(engine.State.FindCity(cmd.TargetId).X,
                                                        engine.State.FindCity(cmd.TargetId).Z);
                if (convertedArmy != null) EnsureArmyVisual(convertedArmy);
                garrisonConversionOpen = false;
                garrisonConversionCityId = null;
            }
            if (r.Ok && cmd.Type == ActionType.GarrisonArmy)
            {
                garrisonArmyId = null;
                garrisonArmyConversionOpen = false;
                var remainingArmy = engine.State.FindArmy(cmd.TargetId);
                if (remainingArmy == null)
                {
                    if (armyObjs.TryGetValue(cmd.TargetId, out var armyObject) && armyObject != null)
                        armyObject.SetActive(false);
                    selArmy = null;
                    ClearArmyMoveTargets();
                }
            }
            if (r.Ok && IsBattle(cmd.Type))
            {
                ClearArmyMoveTargets();
                SyncBattleVisuals();
            }
            RefreshObservation();            // the world just moved: the fog radius moves with it

            if (!r.Ok)
                statusMsg = r.Error == "no command points left"
                    ? "Not enough command points: no command points remain this season"
                    : "Order rejected: " + r.Error;
            else if (IsBattle(cmd.Type))
                statusMsg = LastBattleSummary() ?? "Order executed";
            else
                statusMsg = "Order executed";

            return r.Ok;
        }

        static bool IsBattle(string type)
            => type == ActionType.AttackArmy || type == ActionType.AttackCity;

        // ------------------------------------------------------------- hotseat seat flow

        /// <summary>Ends the current seat's turn: scripted seats play out through the same pipeline,
        /// then either the next human takes the chair or the season resolves (ADR-000).</summary>
        void EndTurn()
        {
            selCity = null; selArmy = null;
            ClearArmyMoveTargets();

            string next = SeatRotation.NextAfter(engine.State, currentSeat);
            while (next != null && !SeatRotation.IsHuman(engine.State, next))
            {
                RunScriptedSeat(next);
                next = SeatRotation.NextAfter(engine.State, next);
            }

            if (next != null)
            {
                currentSeat = next;
                StartSeatTurn();
                return;
            }

            ResolveSeason();
        }

        void RunScriptedSeat(string seatId)
        {
            foreach (var cmd in ScriptedController.PlanTurn(engine.State, seatId))
            {
                var r = engine.Submit(cmd);
                PushJson(cmd, r);
                if (r.Ok && cmd.Type == ActionType.March) SyncArmyVisual(cmd.TargetId);
                if (r.Ok && IsBattle(cmd.Type)) SyncBattleVisuals();
            }
        }

        /// <summary>
        /// Hands the chair over. With more than one human seat a full-screen blocker covers the map and
        /// every panel — the previous player's screen is not the next player's briefing (hotseat rule).
        /// With a single human seat the flow stays exactly as before: no blocker, play on.
        /// </summary>
        void StartSeatTurn()
        {
            selCity = null; selArmy = null;
            locationChoiceOpen = false;           // the previous seat's popup is not the next one's
            envoyText = "";                       // nor is a half-written envoy message
            ClearArmyMoveTargets();
            jsonFeed.Clear();                     // one player's command feed is not the next one's
            if (string.IsNullOrEmpty(currentSeat)) currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();

            handoverShowing = SeatRotation.HumanSeats(engine.State).Count > 1;
            if (handoverShowing) return;

            if (pendingReport) { pendingReport = false; reportShowing = true; }
            else statusMsg = SeatTurnPrompt();
        }

        string SeatTurnPrompt()
        {
            var fac = engine.State.FindFaction(currentSeat);
            return $"{fac?.Name}'s turn (season {engine.State.Season}) — select a city or army to issue orders";
        }

        /// <summary>The only world the UI draws: the current seat's filtered observation.</summary>
        void RefreshObservation()
        {
            obs = engine.Observe(currentSeat);
            ApplyFog();
            RefreshFogOverlays();
        }

        void PushJson(ActionCommand cmd, ValidationResult r)
        {
            jsonFeed.Add((r.Ok ? "✓ " : "✗ ") + cmd.ToJson());
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        /// <summary>Season rollover: resolve the world, then open the next seat's court report (design §2 ①).</summary>
        void ResolveSeason()
        {
            var report = engine.EndSeason();
            engine.BeginSeason();
            currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();

            BuildReport(report);
            pendingReport = true;
            handoverShowing = false;
            StartSeatTurn();
        }
    }
}
