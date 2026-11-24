using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public enum ReplayProvenanceLevel
    {
        /// <summary>The recording declares an LLM-only host and every seat as an LLM controller.</summary>
        DeclaredLlmOnly,
        /// <summary>The files do not say enough to tell whether a person played a seat. Not opened.</summary>
        Unknown,
        /// <summary>The recording declares a non-LLM controller or a host that has one. Not opened.</summary>
        NotLlmOnly,
    }

    public sealed class ReplayProvenance
    {
        public ReplayProvenanceLevel Level;
        /// <summary>Why the level was chosen, in plain language (also the label shown for accepted runs).</summary>
        public string Reason;
        public bool Accepted => Level == ReplayProvenanceLevel.DeclaredLlmOnly;
    }

    /// <summary>
    /// Decides whether a recording may be presented as LLM-only. The request log holds only model requests,
    /// so an action taken by a person never appears in it: an absent controller field is not evidence that
    /// the controller was an LLM, and a seat that has model requests in every season may still have had a
    /// person act in it. A recording is accepted only when it says so itself (a spectator host, every seat
    /// declared an LLM, every executed command attributed to the LLM). An older recording that does not
    /// declare this is reported as unknown and refused: request coverage is never treated as proof, and
    /// there is no inspection path that shows it as LLM-only or as a complete controller timeline.
    /// </summary>
    /// <summary>The provenance wording the chooser and the replay panel show (kept here so tests read the same text).</summary>
    public static class ReplayProvenanceLabels
    {
        public static string ChooserHeader =>
            ReplayCatalog.LlmOnlyLabel + " Only a recording that declares a spectator host with every seat an LLM opens; "
            + "an older recording that does not declare its controllers is refused, because a person's actions are not in its log.";

        public static string PanelLine(ReplayProvenance provenance)
            => provenance != null && provenance.Accepted
                ? "LLM-only recording (declared). Mixed human/LLM replay is not yet supported."
                : "Controllers not established: this recording is not shown as LLM-only.";
    }

    public static class ReplayProvenanceCheck
    {
        public const string SpectatorHost = "spectator";
        public const string SingleLlmHost = "single_llm";

        public static ReplayProvenance Assess(ReplayBundle bundle)
        {
            if (bundle == null || bundle.Requests == null)
                return Make(ReplayProvenanceLevel.Unknown, "no recording was loaded");

            int nonLlm = ReplayCatalog.CountNonLlmRequests(bundle);
            if (nonLlm > 0)
                return Make(ReplayProvenanceLevel.NotLlmOnly, nonLlm + " request(s) were made by a non-LLM controller");

            ReplayBootstrapData boot = bundle.Bootstrap;
            string host = boot == null ? null : boot.HostMode;
            if (string.Equals(host, SingleLlmHost, StringComparison.Ordinal))
                return Make(ReplayProvenanceLevel.NotLlmOnly,
                    "it was recorded with host mode '" + SingleLlmHost + "', which has a human seat whose actions are not in the log");

            var seats = new List<string>();
            int undeclared = 0;
            if (boot != null && boot.RawWorld != null
                && boot.RawWorld.TryGet("factions", out JsonNode list) && list is JsonArray array)
            {
                foreach (JsonNode item in array.Items)
                {
                    var wrap = item as JsonObject;
                    if (wrap == null) continue;
                    JsonObject state = wrap.TryGet("state", out JsonNode sn) && sn is JsonObject so ? so : wrap;
                    string seat = state.TryGet("seat_id", out JsonNode idn) && idn is JsonString ids ? ids.Value : null;
                    if (string.IsNullOrEmpty(seat)) continue;
                    seats.Add(seat);
                    string controller = state.TryGet("controller_type", out JsonNode cn) && cn is JsonString cs ? cs.Value : null;
                    if (string.IsNullOrEmpty(controller)) undeclared++;
                    else if (!string.Equals(controller, ControllerType.Llm, StringComparison.Ordinal))
                        return Make(ReplayProvenanceLevel.NotLlmOnly,
                            "seat " + seat + " is declared as '" + controller + "', not an LLM");
                }
            }
            if (seats.Count == 0)
                return Make(ReplayProvenanceLevel.Unknown, "the recording does not list its seats, so nothing says who controlled them");

            int unattributed = 0;
            foreach (ReplayRequestRecord request in bundle.Requests)
                if (string.IsNullOrEmpty(request.ControllerType) && IsExecutedCommand(request.CommandJson)) unattributed++;
            if (unattributed > 0)
                return Make(ReplayProvenanceLevel.Unknown,
                    unattributed + " executed command(s) carry no controller, so they cannot be attributed to the LLM");

            if (!string.IsNullOrEmpty(host) && !string.Equals(host, SpectatorHost, StringComparison.Ordinal))
                return Make(ReplayProvenanceLevel.Unknown, "its host mode '" + host + "' is not one this replay recognises");

            if (string.Equals(host, SpectatorHost, StringComparison.Ordinal) && undeclared == 0)
                return Make(ReplayProvenanceLevel.DeclaredLlmOnly,
                    "declared: host mode '" + SpectatorHost + "' and all " + seats.Count + " seats are LLM controllers");

            // Older recording: it does not declare a spectator host with every seat an LLM. The request log holds
            // only model requests, so even full per-seat coverage cannot show that nobody else acted in a seat.
            string gap = FirstCoverageGap(bundle, seats);
            return Make(ReplayProvenanceLevel.Unknown,
                "it does not declare its controllers (" + (string.IsNullOrEmpty(host) ? "no host mode" : "host mode '" + host + "'")
                + (undeclared > 0 ? ", " + undeclared + " of " + seats.Count + " seats without a controller" : "") + "); "
                + (gap == null
                    ? "every seat has model requests in every completed season, but that cannot show that no person also acted in a seat"
                    : gap)
                + ". Any human actions are missing from the log");
        }

        // A request with no command (a failed or unparsable reply) and a voluntary end_turn, which the runner
        // records as a bare command and never submits to the engine, change nothing and carry no controller.
        // Every command that reached the engine is stamped with its controller.
        private static bool IsExecutedCommand(string commandJson)
        {
            if (string.IsNullOrWhiteSpace(commandJson)) return false;
            if (!StrictJson.TryParseLenientNumbers(commandJson, out JsonNode node, out _) || !(node is JsonObject obj)) return false;
            if (!obj.TryGet("type", out JsonNode type) || !(type is JsonString text) || string.IsNullOrEmpty(text.Value)) return false;
            return !string.Equals(text.Value, "end_turn", StringComparison.Ordinal);
        }

        private static string FirstCoverageGap(ReplayBundle bundle, List<string> seats)
        {
            int lastLogged = 0;
            foreach (ReplayRequestRecord request in bundle.Requests)
                if (request.Season > lastLogged) lastLogged = request.Season;
            if (lastLogged == 0) return "its log has no season numbers";
            int played = bundle.Summary == null ? 0 : bundle.Summary.SeasonsPlayed;
            int required = played >= lastLogged ? lastLogged : lastLogged - 1;
            if (required < 1) return "too little of it was played to show who controlled each seat";
            for (int season = 1; season <= required; season++)
                foreach (string seat in seats)
                {
                    bool found = false;
                    foreach (ReplayRequestRecord request in bundle.Requests)
                        if (request.Season == season && string.Equals(request.SeatId, seat, StringComparison.Ordinal)) { found = true; break; }
                    if (!found) return "seat " + seat + " has no requests in season " + season;
                }
            return null;
        }

        private static ReplayProvenance Make(ReplayProvenanceLevel level, string reason)
            => new ReplayProvenance { Level = level, Reason = reason };
    }
}
