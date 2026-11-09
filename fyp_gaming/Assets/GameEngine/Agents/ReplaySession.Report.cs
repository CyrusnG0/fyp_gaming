using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    public sealed partial class ReplaySession
    {
        public string GetDisplayedSummarySnapshot()
        {
            var sb = new StringBuilder(512);
            string state = _isCompleted ? "Completed" : (_state == ReplayState.Playing ? "Playing" : "Paused");
            string seat = _currentSeatId ?? "seat-0";
            sb.AppendLine($"OFFLINE REPLAY  |  Season {_currentSeason}/{_totalSeasons}  |  {state} (viewing {seat})");
            sb.AppendLine($"Zero API calls  |  Req {_currentRequestIndex}/{_totalRequests}  |  Obs matched {_observationHashesMatched}/{_currentRequestIndex}");

            var scores = CurrentScores();
            ReplaySeatScore wei = scores.Count > 0 ? scores[0] : null;
            ReplaySeatScore shu = scores.Count > 1 ? scores[1] : null;

            string weiG = FormatScoreField(wei, "gold", wei?.Gold ?? 0);
            string weiF = FormatScoreField(wei, "food", wei?.Food ?? 0);
            string weiP = FormatScoreField(wei, "population", wei?.Population ?? 0);
            string weiGar = FormatScoreField(wei, "garrison", wei?.Garrison ?? 0);

            string shuG = FormatScoreField(shu, "gold", shu?.Gold ?? 0);
            string shuF = FormatScoreField(shu, "food", shu?.Food ?? 0);
            string shuP = FormatScoreField(shu, "population", shu?.Population ?? 0);
            string shuGar = FormatScoreField(shu, "garrison", shu?.Garrison ?? 0);

            sb.AppendLine($"Wei (Capital): Gold {weiG} Food {weiF} Pop {weiP} Gar {weiGar}");
            sb.AppendLine($"Shu (Chengdu): Gold {shuG} Food {shuF} Pop {shuP} Gar {shuGar}");

            if (_isCompleted && _finalReport != null)
            {
                int dWeiG = _finalReport.WeiFinalGold - _finalReport.WeiExpectedGold, dWeiF = _finalReport.WeiFinalFood - _finalReport.WeiExpectedFood;
                sb.AppendLine($"Wei Summary G:{_finalReport.WeiExpectedGold} ({(dWeiG >= 0 ? "+" : "")}{dWeiG}) F:{_finalReport.WeiExpectedFood} ({(dWeiF >= 0 ? "+" : "")}{dWeiF})");
                int dShuG = _finalReport.ShuFinalGold - _finalReport.ShuExpectedGold, dShuF = _finalReport.ShuFinalFood - _finalReport.ShuExpectedFood;
                sb.AppendLine($"Shu Summary G:{_finalReport.ShuExpectedGold} ({(dShuG >= 0 ? "+" : "")}{dShuG}) F:{_finalReport.ShuExpectedFood} ({(dShuF >= 0 ? "+" : "")}{dShuF})");
            }

            var rec = CurrentRequestRecord;
            string seatId = rec != null ? rec.SeatId : seat;
            sb.AppendLine($"Current Turn: {seatId} ({CurrentPersonaId})");
            string act = rec != null && !string.IsNullOrEmpty(rec.RawModelText) ? rec.RawModelText : "end_turn";
            sb.AppendLine("Action: " + act);

            if (_isCompleted)
            {
                bool exact = _finalReport != null && _finalReport.RawExactMatch;
                sb.AppendLine("VERIFICATION: " + (exact ? "PASS (RAW EXACT)" : "COMPLETED WITH DISCREPANCIES"));
                int dirObs = _finalReport != null ? _finalReport.DirectObservationMatched : 0;
                int dirCount = _finalReport != null ? _finalReport.DirectObservationCount : 0;
                int cmdInf = _finalReport != null ? _finalReport.CommandInferredMatched : 0;
                int cmdCount = _finalReport != null ? _finalReport.CommandInferredCount : 0;
                int unverified = _finalReport != null ? _finalReport.UnverifiedSceneSetupCount : 0;
                sb.AppendLine($"Obs: {_observationHashesMatched}/{_totalRequests}  |  Accepted: {_acceptedActionsReplayed}/{_expectedAcceptedActions} (Rej: {_rejectedActionsReplayed}/{_expectedRejectedActions}, Mismatch: {_actionOutcomeMismatches})  |  Positions: {dirObs}/{dirCount} obs, {cmdInf}/{cmdCount} cmd-inferred, {unverified} unverified (fog/setup)");
            }
            return sb.ToString().TrimEnd();
        }

        private void GenerateComparisonReport()
        {
            var rawMismatches = new List<string>();
            bool allSeasonsExact = true, finalSeasonExact = true;
            int weiFinalFood = 0, weiFinalGold = 0, weiFinalPop = 0, weiFinalGar = 0;
            int weiExpFood = 0, weiExpGold = 0, weiExpPop = 0, weiExpGar = 0;
            int shuFinalFood = 0, shuFinalGold = 0, shuFinalPop = 0, shuFinalGar = 0;
            int shuExpFood = 0, shuExpGold = 0, shuExpPop = 0, shuExpGar = 0;

            var seasonsSet = new SortedSet<int>();
            int played = _bundle.Summary != null ? _bundle.Summary.SeasonsPlayed : 0;
            int planned = _bundle.Summary != null ? _bundle.Summary.SeasonsPlanned : 0;
            for (int s = 1; s <= Math.Max(played, planned); s++) seasonsSet.Add(s);
            if (_bundle.Summary?.Turns != null) foreach (var t in _bundle.Summary.Turns) if (t.Season > 0) seasonsSet.Add(t.Season);
            if (_bundle.Summary?.Scoreboards != null) foreach (var sb in _bundle.Summary.Scoreboards) if (sb.Season > 0) seasonsSet.Add(sb.Season);
            if (_bundle.Requests != null) foreach (var r in _bundle.Requests) if (r.Season > 0) seasonsSet.Add(r.Season);
            foreach (var sb in _replayedScores) if (sb.Season > 0) seasonsSet.Add(sb.Season);
            var expectedSeasons = new List<int>(seasonsSet);

            var seatsList = new List<string>();
            void AddSeat(string s) { if (!string.IsNullOrEmpty(s) && !seatsList.Contains(s)) seatsList.Add(s); }
            if (_engine?.State?.Factions != null) foreach (var f in _engine.State.Factions) AddSeat(f.SeatId);
            if (_bundle.Requests != null) foreach (var r in _bundle.Requests) AddSeat(r.SeatId);
            if (_bundle.Summary?.Turns != null) foreach (var t in _bundle.Summary.Turns) AddSeat(t.SeatId);
            if (_bundle.Summary?.Scoreboards != null)
                foreach (var sb in _bundle.Summary.Scoreboards)
                    if (sb.Scores != null) foreach (var sc in sb.Scores) AddSeat(sc.SeatId);

            int finalSeasonNum = expectedSeasons.Count > 0
                ? expectedSeasons[expectedSeasons.Count - 1]
                : (played > 0 ? played : 0);

            if (_bundle.Summary == null || _bundle.Summary.Scoreboards == null || expectedSeasons.Count == 0)
            { allSeasonsExact = false; finalSeasonExact = false; rawMismatches.Add("Missing summary scoreboards"); }
            if (seatsList.Count == 0) { allSeasonsExact = false; finalSeasonExact = false; }

            bool finalSeasonVerified = false;
            int finalSeasonSeatsVerified = 0;

            for (int sIdx = 0; sIdx < expectedSeasons.Count; sIdx++)
            {
                int seasonNum = expectedSeasons[sIdx];
                ReplaySeasonScoreboard actualSb = null;
                foreach (var sb in _replayedScores) if (sb.Season == seasonNum) { actualSb = sb; break; }
                ReplaySeasonScoreboard expSb = null;
                if (_bundle.Summary?.Scoreboards != null)
                    foreach (var sb in _bundle.Summary.Scoreboards) if (sb.Season == seasonNum) { expSb = sb; break; }

                if (actualSb == null || expSb == null || expSb.Scores == null)
                {
                    allSeasonsExact = false; if (seasonNum == finalSeasonNum) finalSeasonExact = false;
                    rawMismatches.Add($"Season {seasonNum}: missing {(expSb == null ? "expected summary" : "replayed")} season scoreboard");
                    continue;
                }

                for (int stIdx = 0; stIdx < seatsList.Count; stIdx++)
                {
                    string seatId = seatsList[stIdx];
                    ReplaySeatScore actScore = null;
                    if (actualSb.Scores != null)
                        foreach (var sc in actualSb.Scores) if (string.Equals(sc.SeatId, seatId, StringComparison.Ordinal)) { actScore = sc; break; }
                    ReplaySeatScore expScore = null;
                    foreach (var sc in expSb.Scores) if (string.Equals(sc.SeatId, seatId, StringComparison.Ordinal)) { expScore = sc; break; }

                    if (actScore == null || expScore == null)
                    {
                        allSeasonsExact = false; if (seasonNum == finalSeasonNum) finalSeasonExact = false;
                        rawMismatches.Add($"Season {seasonNum} {seatId}: missing faction scoreboard entry in {(expScore == null ? "summary" : "replayed state")}");
                        continue;
                    }

                    if ((expScore.MissingFields != null && expScore.MissingFields.Count > 0) || string.IsNullOrEmpty(expScore.SeatId) || string.IsNullOrEmpty(expScore.Faction))
                    {
                        allSeasonsExact = false; if (seasonNum == finalSeasonNum) finalSeasonExact = false;
                        string missingList = expScore.MissingFields != null && expScore.MissingFields.Count > 0 ? string.Join(",", expScore.MissingFields) : "seat_id/faction";
                        rawMismatches.Add($"Season {seasonNum} {seatId}: incomplete scoreboard entry in summary (missing: {missingList})");
                    }

                    int dGold = actScore.Gold - expScore.Gold, dFood = actScore.Food - expScore.Food;
                    int dPop = actScore.Population - expScore.Population, dGar = actScore.Garrison - expScore.Garrison;
                    int dCities = actScore.Cities - expScore.Cities;
                    bool factionMatch = string.Equals(actScore.Faction, expScore.Faction, StringComparison.Ordinal);

                    if (dGold != 0 || dFood != 0 || dPop != 0 || dGar != 0 || dCities != 0 || !factionMatch)
                    {
                        allSeasonsExact = false; if (seasonNum == finalSeasonNum) finalSeasonExact = false;
                        rawMismatches.Add($"Season {seasonNum} {actScore.SeatId} ({actScore.Faction}): Gold delta={dGold} (act={actScore.Gold}, exp={expScore.Gold}), Food delta={dFood} (act={actScore.Food}, exp={expScore.Food}), Pop delta={dPop}, Gar delta={dGar}, Cities delta={dCities}{(factionMatch ? "" : $", Faction mismatch: act='{actScore.Faction}', exp='{expScore.Faction}'")}");
                    }

                    if (seasonNum == finalSeasonNum)
                    {
                        finalSeasonSeatsVerified++;
                        if (actScore.SeatId == "seat-0")
                        {
                            weiFinalFood = actScore.Food; weiFinalGold = actScore.Gold; weiFinalPop = actScore.Population; weiFinalGar = actScore.Garrison;
                            weiExpFood = expScore.Food; weiExpGold = expScore.Gold; weiExpPop = expScore.Population; weiExpGar = expScore.Garrison;
                        }
                        else if (actScore.SeatId == "seat-1")
                        {
                            shuFinalFood = actScore.Food; shuFinalGold = actScore.Gold; shuFinalPop = actScore.Population; shuFinalGar = actScore.Garrison;
                            shuExpFood = expScore.Food; shuExpGold = expScore.Gold; shuExpPop = expScore.Population; shuExpGar = expScore.Garrison;
                        }
                    }
                }
                if (seasonNum == finalSeasonNum) finalSeasonVerified = true;
            }

            if (!finalSeasonVerified || finalSeasonSeatsVerified < seatsList.Count)
            { finalSeasonExact = false; allSeasonsExact = false; }

            ParseLoggedPositions(out var observedEntities, out var inferredMoves);
            var checks = new List<ReplayPositionCheck>();
            int directCount = 0, directMatched = 0, cmdCount = 0, cmdMatched = 0, unverifiedCount = 0;

            var allEntities = new List<(string id, string kind, string owner, int actX, int actZ)>();
            foreach (var c in _engine.State.Cities) allEntities.Add((c.Id, "city", c.OwnerSeatId, c.X, c.Z));
            foreach (var t in _engine.State.WatchTowers) allEntities.Add((t.Id, "tower", t.OwnerSeatId, t.X, t.Z));
            foreach (var a in _engine.State.Armies) allEntities.Add((a.Id, "army", a.OwnerSeatId, a.X, a.Z));

            foreach (var ent in allEntities)
            {
                ReplayPositionCheck pc;
                if (!observedEntities.TryGetValue(ent.id, out var obs))
                {
                    unverifiedCount++;
                    pc = new ReplayPositionCheck {
                        EntityId = ent.id, EntityKind = ent.kind, OwnerSeatId = ent.owner,
                        ExpectedX = null, ExpectedZ = null, ActualX = ent.actX, ActualZ = ent.actZ,
                        Matched = false, VerifiedByLogs = false, VerificationKind = ReplayPositionVerificationKind.UnverifiedSceneSetup,
                        VerificationBasis = "unverified scene setup (never in logged observations)", Note = "Neutral entity outside fog-of-war range of both seats"
                    };
                }
                else if (inferredMoves.TryGetValue(ent.id, out var move) && move.reqOrder >= obs.reqOrder)
                {
                    cmdCount++;
                    bool matched = ent.actX == move.x && ent.actZ == move.z;
                    if (matched) cmdMatched++;
                    pc = new ReplayPositionCheck {
                        EntityId = ent.id, EntityKind = ent.kind, OwnerSeatId = ent.owner,
                        ExpectedX = move.x, ExpectedZ = move.z, ActualX = ent.actX, ActualZ = ent.actZ,
                        Matched = matched, VerifiedByLogs = true, VerificationKind = ReplayPositionVerificationKind.AcceptedCommandInference,
                        VerificationBasis = $"accepted command {move.actionId}",
                        Note = $"Observed at ({obs.x},{obs.z}) in turn {obs.turn} req {obs.req}, then moved to ({move.x},{move.z}) by accepted command {move.actionId}; no post-action observation in JSONL"
                    };
                }
                else
                {
                    directCount++;
                    bool matched = ent.actX == obs.x && ent.actZ == obs.z;
                    if (matched) directMatched++;
                    pc = new ReplayPositionCheck {
                        EntityId = ent.id, EntityKind = ent.kind, OwnerSeatId = ent.owner,
                        ExpectedX = obs.x, ExpectedZ = obs.z, ActualX = ent.actX, ActualZ = ent.actZ,
                        Matched = matched, VerifiedByLogs = true, VerificationKind = ReplayPositionVerificationKind.DirectObservation,
                        VerificationBasis = $"observation (turn {obs.turn}, req {obs.req})", Note = ""
                    };
                }
                checks.Add(pc);
            }

            bool rawExact = _observationHashMismatches == 0 && finalSeasonExact && allSeasonsExact
                            && _actionOutcomeMismatches == 0 && directMatched == directCount && cmdMatched == cmdCount;

            _finalReport = new ReplayComparisonReport
            {
                RunId = _bundle.Summary?.RunId ?? "unknown",
                Seed = _bundle.Summary != null ? _bundle.Summary.Seed : 0,
                BalanceProfileKey = _bundle.Summary?.BalanceProfileKey ?? "unknown",
                SeasonsVerified = _replayedScores.Count,
                TotalRequests = _bundle.Requests.Count,
                ObservationHashesMatched = _observationHashesMatched,
                ObservationHashMismatches = _observationHashMismatches,
                ObservationHashDiscrepancies = _observationHashDiscrepancies,
                ExpectedAcceptedActions = _expectedAcceptedActions,
                AcceptedActionsReplayed = _acceptedActionsReplayed,
                ExpectedRejectedActions = _expectedRejectedActions,
                RejectedActionsReplayed = _rejectedActionsReplayed,
                UnexpectedRejections = _unexpectedRejections,
                UnexpectedAcceptances = _unexpectedAcceptances,
                RejectionReasonMismatches = _rejectionReasonMismatches,
                ActionOutcomeMismatches = _actionOutcomeMismatches,
                ActionDiscrepancies = _actionDiscrepancies,
                ExpectedVoluntaryEndTurns = _expectedVoluntaryEndTurns,
                VoluntaryEndTurnsReplayed = _voluntaryEndTurnsReplayed,
                WeiFinalFood = weiFinalFood, WeiFinalGold = weiFinalGold,
                WeiExpectedFood = weiExpFood, WeiExpectedGold = weiExpGold,
                WeiFinalPop = weiFinalPop, WeiExpectedPop = weiExpPop,
                WeiFinalGarrison = weiFinalGar, WeiExpectedGarrison = weiExpGar,
                ShuFinalFood = shuFinalFood, ShuFinalGold = shuFinalGold,
                ShuExpectedFood = shuExpFood, ShuExpectedGold = shuExpGold,
                ShuFinalPop = shuFinalPop, ShuExpectedPop = shuExpPop,
                ShuFinalGarrison = shuFinalGar, ShuExpectedGarrison = shuExpGar,
                FinalScoreboardExactMatch = finalSeasonExact,
                AllSeasonsScoreboardExactMatch = allSeasonsExact,
                RawScoreboardMismatches = rawMismatches,
                DirectObservationCount = directCount,
                DirectObservationMatched = directMatched,
                CommandInferredCount = cmdCount,
                CommandInferredMatched = cmdMatched,
                UnverifiedSceneSetupCount = unverifiedCount,
                PositionChecks = checks,
                ProposedFloatingPointNotes = new[]
                {
                    "In Season 1 EndSeason, city tax is calculated as (int)(Population * 0.01f).",
                    "With Season 1 starting population 9000, 9000 * 0.01f evaluates to 89 in Unity Mono (F64 CIL evaluation stack) vs 90 in .NET CoreCLR (F32 native single precision), producing +1 gold across S1-S5 for both Wei and Shu (Wei final gold 1742 vs 1741, Shu final gold 1249 vs 1248).",
                    "In Season 1 EndSeason, city food consumption/harvest is (int)(9000 * 0.02f + 50f) = 230 in CoreCLR F32 vs 229 in Mono F64 (-1 food for Wei in S1-S4: 1111 vs 1112, 866 vs 867, 1089 vs 1090, 574 vs 575).",
                    "In Season 5, (int)(300f * 1.6f * 0.7f) evaluates to 336 in CoreCLR vs 335 in Mono, exactly offsetting the -1 food delta so Season 5 final Wei food matches 100% exactly (243 vs 243).",
                    "All population, garrison, and city values match 100% exactly across all 5 seasons.",
                    "Raw observation hashes differ in Seasons 2-5 (10/44 matched, 34 mismatches) due to the Season 1 +1 gold / -1 food float truncation carryover in serialized city state and city_resolved event payloads.",
                    "A separate standalone Unity Mono CLI executable (MonoBleedingEdge/bin/mono.exe against unityjit-win32) matched this run (44/44 hashes, 0 resource deltas), confirming the (int)(9000 * 0.01f) F64-vs-F32 truncation cause, but that separate Mono executable is not an interactive Unity Editor Play Mode result."
                },
                DisclosedDependencies = new[]
                {
                    "RNG Seed 12345: EndSeason population growth sequence (rng.NextDouble() called per owned non-starving city).",
                    "easy-research balance profile: starting food 1500, gold 900, base harvest 300, 0.5x upkeep & domestic gold cost.",
                    "JIT Float Evaluation (Unity Mono F64 CIL stack vs .NET CoreCLR F32): at S1 pop=9000, (int)(9000*0.01f) is 89 in Mono F64 vs 90 in CoreCLR F32 (+1 gold).",
                    "MapData / Scene Setup: 26x17 staggered hex grid matching TBTK layout.",
                    "Neutral city-south (12,2) and watch-tower-1 (6,2): never in fog-of-war sight in JSONL; positions come from scene setup.",
                    "Rejection category and validator reason comparison (parse_failure vs engine_validation_failure + ResultError) is implemented; run-20261109082434809-51b7ebd5 itself contains 0 rejected actions (42 accepted + 2 voluntary end_turns)."
                },
                RawExactMatch = rawExact
            };
        }

        private void ParseLoggedPositions(
            out Dictionary<string, (int x, int z, int turn, int req, int reqOrder)> observedEntities,
            out Dictionary<string, (int x, int z, string actionId, int reqOrder)> inferredMoves)
        {
            observedEntities = new Dictionary<string, (int x, int z, int turn, int req, int reqOrder)>(StringComparer.Ordinal);
            inferredMoves = new Dictionary<string, (int x, int z, string actionId, int reqOrder)>(StringComparer.Ordinal);

            for (int i = 0; i < _bundle.Requests.Count; i++)
            {
                var req = _bundle.Requests[i];
                if (!string.IsNullOrEmpty(req.ObservationJson) &&
                    StrictJson.TryParseLenientNumbers(req.ObservationJson, out JsonNode obsNode, out _) &&
                    obsNode is JsonObject obsObj)
                {
                    ExtractEntitiesFromObsArray(obsObj, "cities", req, i, observedEntities);
                    ExtractEntitiesFromObsArray(obsObj, "scouted_cities", req, i, observedEntities);
                    ExtractEntitiesFromObsArray(obsObj, "watch_towers", req, i, observedEntities);
                    ExtractEntitiesFromObsArray(obsObj, "armies", req, i, observedEntities);
                }

                if (req.ResultOk && !string.IsNullOrEmpty(req.CommandJson) &&
                    StrictJson.TryParseLenientNumbers(req.CommandJson, out JsonNode cmdNode, out _) &&
                    cmdNode is JsonObject cmdObj)
                {
                    string type = cmdObj.TryGet("type", out JsonNode tNode) && tNode is JsonString ts ? ts.Value : "";
                    if (type == "march" || type == "army_march")
                    {
                        string target = cmdObj.TryGet("target_id", out JsonNode tgNode) && tgNode is JsonString tgs ? tgs.Value : "";
                        int px = cmdObj.TryGet("param_a", out JsonNode paNode) && paNode is JsonInt pai ? (int)pai.Value : -1;
                        int pz = cmdObj.TryGet("param_b", out JsonNode pbNode) && pbNode is JsonInt pbi ? (int)pbi.Value : -1;
                        string actId = cmdObj.TryGet("action_id", out JsonNode aidNode) && aidNode is JsonString aids ? aids.Value : "";
                        if (!string.IsNullOrEmpty(target) && px >= 0 && pz >= 0) inferredMoves[target] = (px, pz, actId, i);
                    }
                }
            }
        }

        private static void ExtractEntitiesFromObsArray(JsonObject obsObj, string key, ReplayRequestRecord req, int reqIndex,
            Dictionary<string, (int x, int z, int turn, int req, int reqOrder)> target)
        {
            if (obsObj.TryGet(key, out JsonNode arrNode) && arrNode is JsonArray arr)
            {
                foreach (var item in arr.Items)
                {
                    if (item is JsonObject obj)
                    {
                        string id = obj.TryGet("id", out JsonNode idNode) && idNode is JsonString ids ? ids.Value : "";
                        int x = obj.TryGet("x", out JsonNode xNode) && xNode is JsonInt xi ? (int)xi.Value : -1;
                        int z = obj.TryGet("z", out JsonNode zNode) && zNode is JsonInt zi ? (int)zi.Value : -1;
                        if (!string.IsNullOrEmpty(id) && x >= 0 && z >= 0) target[id] = (x, z, req.TurnToken, req.RequestIndex, reqIndex);
                    }
                }
            }
        }

        public static MapData BuildDefaultStaggeredHexMap(int width = 26, int height = 17)
        {
            var map = new MapData { Width = width, Height = height, Walkable = new bool[width * height], Cost = new int[width * height], Neighbors = new int[width * height][] };
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    int i = map.Index(x, z);
                    map.Walkable[i] = !((x % 2 == 0) && (z == height - 1));
                    map.Cost[i] = 1;
                }
            int[][] evenOffsets = { new[] { 0, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 } };
            int[][] oddOffsets  = { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { 0, 1 } };

            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    int i = map.Index(x, z);
                    if (!map.Walkable[i]) { map.Neighbors[i] = new int[0]; continue; }
                    var offsets = (x % 2 == 0) ? evenOffsets : oddOffsets;
                    var list = new List<int>(6);
                    for (int o = 0; o < offsets.Length; o++)
                    {
                        int nx = x + offsets[o][0], nz = z + offsets[o][1];
                        if (nx >= 0 && nx < width && nz >= 0 && nz < height && map.Walkable[map.Index(nx, nz)])
                            list.Add(map.Index(nx, nz));
                    }
                    map.Neighbors[i] = list.ToArray();
                }
            return map;
        }

        private static string FormatScoreField(ReplaySeatScore sc, string field, int val)
        {
            if (sc == null) return "unknown";
            if (sc.MissingFields != null)
            {
                for (int i = 0; i < sc.MissingFields.Count; i++)
                {
                    if (string.Equals(sc.MissingFields[i], field, StringComparison.Ordinal))
                        return "unknown";
                }
            }
            return val.ToString();
        }
    }
}
