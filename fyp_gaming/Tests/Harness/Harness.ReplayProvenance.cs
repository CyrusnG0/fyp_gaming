using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LuanShi.Engine;

// [CATALOG] (continued) Who controlled the seats. The request log never holds a person's actions, so an
// absent controller field proves nothing: a recording is LLM-only only when it says so or, for an older
// one, when every seat-turn and every command can be accounted for.

internal static partial class Harness
{
    private static ReplayBundle CatProvenanceBundle(string hostMode, string[] factionControllers, int seasons,
        int seasonsPlayed, Func<string, int, bool> seatPlayed, bool commandsAttributed = true)
    {
        // factionControllers[i] is the declared controller of seat-i, or null for a legacy record without one.
        var factions = new List<string>();
        for (int i = 0; i < factionControllers.Length; i++)
            factions.Add("{\"state\":{\"seat_id\":\"seat-" + i + "\",\"name\":\"F" + i + "\""
                + (factionControllers[i] == null ? "" : ",\"controller_type\":\"" + factionControllers[i] + "\"") + "}}");
        string world = "{\"factions\":[" + string.Join(",", factions) + "]}";
        StrictJson.TryParseLenientNumbers(world, out JsonNode node, out _);
        var requests = new List<ReplayRequestRecord>();
        for (int season = 1; season <= seasons; season++)
            for (int i = 0; i < factionControllers.Length; i++)
            {
                string seat = "seat-" + i;
                if (!seatPlayed(seat, season)) continue;
                requests.Add(new ReplayRequestRecord
                {
                    SeatId = seat, Season = season, RequestIndex = requests.Count + 1,
                    CommandJson = "{\"type\":\"levy_tax\"}",
                    ControllerType = commandsAttributed ? ControllerType.Llm : null,
                });
            }
        return new ReplayBundle
        {
            Requests = requests,
            Summary = new ReplaySummaryData { SeasonsPlanned = seasons, SeasonsPlayed = seasonsPlayed },
            Bootstrap = new ReplayBootstrapData { HostMode = hostMode, RawWorld = node as JsonObject },
        };
    }

    private static void CatalogProvenance(string root)
    {
        string[] allLlm = { "llm", "llm" }, undeclared = { null, null };
        Func<string, int, bool> everyone = (s, n) => true;
        Func<string, int, bool> seat1MissesSeason2 = (s, n) => !(s == "seat-1" && n == 2);

        ReplayProvenance declared = ReplayProvenanceCheck.Assess(CatProvenanceBundle("spectator", allLlm, 3, 3, everyone));
        CatCheck(declared.Level == ReplayProvenanceLevel.DeclaredLlmOnly && declared.Accepted,
            "declared LLM-only: spectator host with every seat declared llm is accepted (" + declared.Reason + ")");

        ReplayProvenance legacy = ReplayProvenanceCheck.Assess(CatProvenanceBundle(null, undeclared, 3, 3, everyone));
        CatCheck(legacy.Level == ReplayProvenanceLevel.InferredLlmOnly && legacy.Accepted && legacy.Reason.StartsWith("inferred, not declared"),
            "legacy: no host mode and no seat controllers, but every seat has requests in every season and every command is LLM: accepted as inferred, and labelled so");

        ReplayProvenance legacySpectator = ReplayProvenanceCheck.Assess(CatProvenanceBundle("spectator", undeclared, 3, 3, everyone));
        CatCheck(legacySpectator.Level == ReplayProvenanceLevel.InferredLlmOnly,
            "a spectator host whose seats do not declare controllers is still only inferred, not declared");

        ReplayProvenance gap = ReplayProvenanceCheck.Assess(CatProvenanceBundle(null, undeclared, 3, 3, seat1MissesSeason2));
        CatCheck(gap.Level == ReplayProvenanceLevel.Unknown && !gap.Accepted && gap.Reason.Contains("seat-1") && gap.Reason.Contains("season 2"),
            "legacy with a seat missing from a completed season: provenance unknown, refused (a person may have played it)");
        ReplayProvenance gapDeclaredHost = ReplayProvenanceCheck.Assess(CatProvenanceBundle("spectator", undeclared, 3, 3, seat1MissesSeason2));
        CatCheck(gapDeclaredHost.Level == ReplayProvenanceLevel.Unknown,
            "the same gap is not excused by a host mode when the seats declare nothing");

        // An incomplete run is judged only on the seasons that finished.
        ReplayProvenance partialOk = ReplayProvenanceCheck.Assess(
            CatProvenanceBundle(null, undeclared, 3, 2, (s, n) => !(s == "seat-1" && n == 3)));
        CatCheck(partialOk.Level == ReplayProvenanceLevel.InferredLlmOnly,
            "a run stopped during season 3 is not penalised for the seat that had not yet played it");
        ReplayProvenance partialGap = ReplayProvenanceCheck.Assess(
            CatProvenanceBundle(null, undeclared, 3, 3, (s, n) => !(s == "seat-1" && n == 3)));
        CatCheck(partialGap.Level == ReplayProvenanceLevel.Unknown,
            "the same gap in a season the summary says finished is refused");
        ReplayProvenance noneFinished = ReplayProvenanceCheck.Assess(CatProvenanceBundle(null, undeclared, 1, 0, everyone));
        CatCheck(noneFinished.Level == ReplayProvenanceLevel.Unknown,
            "with no completed season there is no coverage to infer anything from");

        ReplayProvenance unattributed = ReplayProvenanceCheck.Assess(CatProvenanceBundle(null, undeclared, 3, 3, everyone, false));
        CatCheck(unattributed.Level == ReplayProvenanceLevel.Unknown && unattributed.Reason.Contains("carry no controller"),
            "executed commands with no controller field cannot be attributed to the LLM: unknown");
        ReplayProvenance unattributedDeclared = ReplayProvenanceCheck.Assess(CatProvenanceBundle("spectator", allLlm, 3, 3, everyone, false));
        CatCheck(unattributedDeclared.Level == ReplayProvenanceLevel.Unknown,
            "even a declared recording is refused when its executed commands are unattributed");

        ReplayProvenance single = ReplayProvenanceCheck.Assess(CatProvenanceBundle("single_llm", allLlm, 3, 3, everyone));
        CatCheck(single.Level == ReplayProvenanceLevel.NotLlmOnly && !single.Accepted && single.Reason.Contains("human seat"),
            "a single_llm host has a human seat whose actions are not logged: refused even if its seats say llm");
        ReplayProvenance human = ReplayProvenanceCheck.Assess(CatProvenanceBundle(null, new[] { "human", "llm" }, 3, 3, everyone));
        CatCheck(human.Level == ReplayProvenanceLevel.NotLlmOnly && human.Reason.Contains("seat-0") && human.Reason.Contains("human"),
            "a seat declared human is refused, with the seat named");
        ReplayBundle mixed = CatProvenanceBundle("spectator", allLlm, 2, 2, everyone);
        mixed.Requests[0].ControllerType = ControllerType.Human;
        CatCheck(ReplayProvenanceCheck.Assess(mixed).Level == ReplayProvenanceLevel.NotLlmOnly,
            "a request recorded under a human controller is refused");
        ReplayBundle noSeats = CatProvenanceBundle("spectator", new string[0], 2, 2, everyone);
        CatCheck(ReplayProvenanceCheck.Assess(noSeats).Level == ReplayProvenanceLevel.Unknown
                 && ReplayProvenanceCheck.Assess(new ReplayBundle { Requests = new List<ReplayRequestRecord>() }).Level == ReplayProvenanceLevel.Unknown
                 && ReplayProvenanceCheck.Assess(null).Level == ReplayProvenanceLevel.Unknown,
            "no listed seats, no bootstrap or no bundle: unknown, never LLM-only");
        ReplayProvenance odd = ReplayProvenanceCheck.Assess(CatProvenanceBundle("hotseat", allLlm, 2, 2, everyone));
        CatCheck(odd.Level == ReplayProvenanceLevel.Unknown, "an unrecognised host mode is unknown, not assumed LLM-only");
    }

    /// <summary>The real recorded run, re-opened with its bootstrap's host mode edited, through the catalog.</summary>
    private static void CatalogProvenanceOnDisk(string root, string runId)
    {
        string path = Path.Combine(root, runId + "-bootstrap.json");
        if (!File.Exists(path)) { CatCheck(false, "the recorded bootstrap exists for the provenance checks"); return; }
        string original = File.ReadAllText(path);
        string marker = "\"host_mode\":\"spectator\"";
        CatCheck(original.Contains(marker), "the recorded bootstrap declares its host mode");
        ReplayOpenResult declared = ReplayCatalog.Open(runId, new[] { root });
        CatCheck(declared.Ok && declared.Provenance != null && declared.Provenance.Level == ReplayProvenanceLevel.DeclaredLlmOnly
                 && declared.Notes.Any(n => n.StartsWith("Controllers declared")),
            "the recorded spectator run opens as declared LLM-only and the open result says so");
        try
        {
            File.WriteAllText(path, original.Replace(marker, "\"host_mode\":\"single_llm\""));
            ReplayOpenResult single = ReplayCatalog.Open(runId, new[] { root });
            CatCheck(single.Status == ReplayOpenStatus.Unsupported && single.Session == null && single.Message.Contains("not LLM-only")
                     && single.Message.Contains("Mixed human/LLM replay is not supported"),
                "the same run recorded under a single_llm host is Unsupported and says mixed replay is not supported");
            File.WriteAllText(path, original.Replace(marker, "\"host_mode\":\"hotseat\""));
            ReplayOpenResult odd = ReplayCatalog.Open(runId, new[] { root });
            CatCheck(odd.Status == ReplayOpenStatus.Unsupported && odd.Session == null && odd.Message.Contains("unknown")
                     && odd.Provenance.Level == ReplayProvenanceLevel.Unknown,
                "an unrecognised host mode is Unsupported with the controllers reported as unknown");
            File.WriteAllText(path, original.Replace(marker + ",", "").Replace(marker, ""));
            ReplayOpenResult legacy = ReplayCatalog.Open(runId, new[] { root });
            CatCheck(legacy.Status == ReplayOpenStatus.Ready
                     ? legacy.Provenance.Level == ReplayProvenanceLevel.InferredLlmOnly && legacy.Notes.Any(n => n.Contains("inferred, not declared"))
                     : legacy.Status == ReplayOpenStatus.Unsupported && legacy.Provenance.Level == ReplayProvenanceLevel.Unknown,
                "with no host mode the recording opens only as inferred (and says so) or is refused as unknown (" + legacy.Status + ")");
        }
        finally { File.WriteAllText(path, original); }
    }
}
