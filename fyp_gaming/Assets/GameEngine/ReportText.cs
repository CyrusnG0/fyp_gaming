namespace LuanShi.Engine
{
    /// <summary>
    /// Player-facing report text built only from a seat's filtered observation (LS-003).
    ///
    /// The court report used to resolve a captured city through canonical state, which leaked the name of
    /// a city the seat could not see and named the city's current owner instead of the event's captor.
    /// A city is now named only when the observation carries it (in sight) or a live 偵察 report covers it;
    /// otherwise the public city id is all the seat may print. Nothing here consults the canonical state.
    /// </summary>
    public static class ReportText
    {
        /// <summary>
        /// The name this seat may use for a city: an in-sight city first, then a live 偵察 report's name,
        /// then the public city id. Null-safe on the observation; a missing city simply keeps its id.
        /// </summary>
        public static string CityLabel(Observation obs, string cityId)
        {
            if (obs == null || cityId == null) return cityId;
            var city = obs.FindCity(cityId);
            if (city != null) return city.Name;
            var scouted = obs.FindScoutedCity(cityId);
            return scouted != null ? scouted.Name : cityId;
        }

        /// <summary>
        /// The court-report sentence for a <c>city_captured</c> event. The captor is the event's actor —
        /// the seat that took the city then — never the city's later owner, and the city label comes from
        /// the seat's own observation. The projected event carries only the public <c>city_id</c> payload.
        /// </summary>
        public static string CaptureLine(Observation obs, EventView rec)
        {
            string cityId = null;
            if (rec != null && rec.Payload != null) rec.Payload.TryGetValue("city_id", out cityId);
            string captorSeatId = rec != null ? rec.ActorSeatId : null;
            var captor = obs != null && captorSeatId != null ? obs.Faction(captorSeatId) : null;
            string captorName = captor != null ? captor.Name : captorSeatId;
            return $"* {captorName} army occupied {CityLabel(obs, cityId)}, expanding its territory!";
        }
    }
}
