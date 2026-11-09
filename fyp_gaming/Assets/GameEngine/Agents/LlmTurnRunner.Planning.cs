using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Planning-host capabilities for the movement slice: seat-safe candidate query, the authoritative
    /// terminal stop and the observation-derived capture wording. A plain <see cref="ITurnHost"/> has
    /// none of these, so its domestic-only demo-slice-3 behaviour is unchanged.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        /// <summary>The host's planning capabilities; null for a plain domestic-only host.</summary>
        private IPlanningHost PlanningHost => _host as IPlanningHost;
        private bool HasPlanningHost => _host is IPlanningHost;

        /// <summary>
        /// Attack and assault orders are advertised, parsed and submitted only when the config opts in
        /// AND the host can plan them. Either missing means the march-only behaviour, exactly as before.
        /// </summary>
        private bool AttackActive => _config.EnableAttack && _host is IAttackPlanningHost;

        /// <summary>
        /// Calls the host's read-only movement query and rejects a view built for another seat.
        /// A null view is allowed (the host may have no map); it means no advertised options.
        /// </summary>
        private bool TryPlanMovement(out MovementView movement, out string failure)
        {
            movement = null;
            failure = null;
            try { movement = PlanningHost.PlanMovement(); }
            catch (Exception) { failure = "host movement query failed"; return false; }
            if (movement != null && !string.IsNullOrEmpty(movement.SeatId)
                && !string.Equals(movement.SeatId, _seatId, StringComparison.Ordinal))
            { failure = "movement view seat mismatch"; return false; }
            return true;
        }

        /// <summary>
        /// Calls the host's read-only attack query and rejects a view built for another seat.
        /// A null view is allowed and advertises nothing.
        /// </summary>
        private bool TryPlanAttacks(out AttackView attacks, out string failure)
        {
            attacks = null;
            failure = null;
            try { attacks = ((IAttackPlanningHost)_host).PlanAttacks(); }
            catch (Exception) { failure = "host attack query failed"; return false; }
            if (attacks != null && !string.IsNullOrEmpty(attacks.SeatId)
                && !string.Equals(attacks.SeatId, _seatId, StringComparison.Ordinal))
            { failure = "attack view seat mismatch"; return false; }
            return true;
        }

        /// <summary>
        /// Authoritative terminal stop. The turn finishes with a distinct GameOver reason and the
        /// usual single EndTurn notification; no further request is ever dispatched.
        /// </summary>
        private void FinishGameOver()
        {
            FailureReason = null;
            EndReason = EndReason.GameOver;
            Status = "finished: game over";
            FinishTerminal(RunnerState.Finished, "game over");
        }

        /// <summary>
        /// The city captured by an accepted march, read from a FRESH seat-filtered observation and
        /// only when the destination was previously not ours. Null when nothing observable changed.
        /// Never consults canonical state.
        /// </summary>
        private string CapturedCityName(InFlight flight, LlmAction action)
        {
            try
            {
                Observation after = _host.Observe();
                if (after == null || after.Cities == null) return null;
                CityView city = after.Cities.Find(c => c != null
                    && c.X == action.ParamA && c.Z == action.ParamB);
                if (city == null
                    || !string.Equals(city.OwnerSeatId, _seatId, StringComparison.Ordinal))
                    return null;

                Observation before = flight.Observation;
                CityView prior = before == null || before.Cities == null ? null
                    : before.Cities.Find(c => c != null
                        && c.X == action.ParamA && c.Z == action.ParamB);
                if (prior != null
                    && string.Equals(prior.OwnerSeatId, _seatId, StringComparison.Ordinal))
                    return null;   // it was already ours: a plain move, not a capture

                return string.IsNullOrEmpty(city.Name) ? city.Id : city.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
