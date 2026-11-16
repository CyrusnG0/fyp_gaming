using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    public sealed class FileRef
    {
        [JsonPropertyName("path")] public string Path { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
    }

    /// <summary>Everything retained about one match, including a failed or skipped one.</summary>
    public sealed class MatchOutcome
    {
        [JsonPropertyName("match_id")] public string MatchId { get; set; }
        [JsonPropertyName("pair_id")] public string PairId { get; set; }
        [JsonPropertyName("arm")] public string Arm { get; set; }
        [JsonPropertyName("seed")] public int Seed { get; set; }
        [JsonPropertyName("settings_hash")] public string SettingsHash { get; set; }
        /// <summary>completed | stopped | failed | error | skipped</summary>
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("detail")] public string Detail { get; set; }
        [JsonPropertyName("session_state")] public string SessionState { get; set; }
        [JsonPropertyName("stop_reason")] public string StopReason { get; set; }
        [JsonPropertyName("run_id")] public string RunId { get; set; }
        [JsonPropertyName("seasons_played")] public int SeasonsPlayed { get; set; }
        [JsonPropertyName("turn_endings")] public List<string> TurnEndings { get; set; } = new List<string>();
        /// <summary>The production LlmConfig.SpatialContext level this arm dispatched with: off | standard | rich.</summary>
        [JsonPropertyName("spatial_context")] public string SpatialContext { get; set; }
        [JsonPropertyName("spatial_evidence")] public SpatialEvidence Spatial { get; set; }
        /// <summary>Which wall-clock limit ended the match, or null.</summary>
        [JsonPropertyName("wall_limit")] public string WallLimit { get; set; }
        [JsonPropertyName("elapsed_ms")] public long ElapsedMs { get; set; }
        [JsonPropertyName("final_state")] public List<string> FinalState { get; set; } = new List<string>();
        [JsonPropertyName("files")] public Dictionary<string, FileRef> Files { get; set; } = new Dictionary<string, FileRef>();
        [JsonPropertyName("audit")] public MatchAudit Audit { get; set; }

        [JsonIgnore] public List<ExchangeCapture> Captures = new List<ExchangeCapture>();
        [JsonIgnore] public long? InputTokens;
        [JsonIgnore] public long? OutputTokens;
        [JsonIgnore] public int Requests;
        [JsonIgnore] public long KnownInputTokens;
        [JsonIgnore] public long KnownOutputTokens;
    }

    /// <summary>Runs one arm through the production runner and session, then writes and audits its evidence.</summary>
    public static class BatchRunner
    {
        /// <param name="batchWallRemainingSeconds">Batch wall time left; the match stops at the earlier of this and its own limit.</param>
        public static MatchOutcome RunMatch(MatchSpec spec, string matchDir, string batchId, double batchWallRemainingSeconds = double.MaxValue)
        {
            var outcome = new MatchOutcome
            {
                MatchId = spec.MatchId, PairId = spec.PairId, Arm = spec.Arm, Seed = spec.Seed, SettingsHash = spec.SettingsHash,
                Status = "error",
                SpatialContext = BatchWorld.Config(spec.Resolved).SpatialContext.LevelKey(),
            };
            Directory.CreateDirectory(matchDir);
            var clock = Stopwatch.StartNew();
            try
            {
                WriteText(outcome, matchDir, "settings", "settings.json", JsonSerializer.Serialize(
                    new { match_id = spec.MatchId, settings_hash = spec.SettingsHash, settings = spec.Flat }, BatchManifest.WriteOptions));
                Play(spec, matchDir, batchId, outcome, batchWallRemainingSeconds);
            }
            catch (Exception e)
            {
                // A crash is an outcome, not a reason to lose the batch: keep type and message, no stack.
                outcome.Status = "error";
                outcome.Detail = e.GetType().Name + ": " + e.Message;
            }
            outcome.ElapsedMs = clock.ElapsedMilliseconds;
            Retain(outcome, matchDir);
            return outcome;
        }

        /// <summary>A match that never started is still an outcome: it is written with the reason it was skipped.</summary>
        public static MatchOutcome Skipped(MatchSpec spec, string matchDir, string reason)
        {
            Directory.CreateDirectory(matchDir);
            var outcome = new MatchOutcome
            {
                MatchId = spec.MatchId, PairId = spec.PairId, Arm = spec.Arm, Seed = spec.Seed, SettingsHash = spec.SettingsHash,
                Status = "skipped", Detail = reason,
            };
            // The planned settings are kept for a skipped arm too, so the pair's intended difference stays checkable.
            WriteText(outcome, matchDir, "settings", "settings.json", JsonSerializer.Serialize(
                new { match_id = spec.MatchId, settings_hash = spec.SettingsHash, settings = spec.Flat }, BatchManifest.WriteOptions));
            Retain(outcome, matchDir);
            return outcome;
        }

        private static void Retain(MatchOutcome outcome, string matchDir)
            => WriteText(outcome, matchDir, "match", "match.json", JsonSerializer.Serialize(outcome, BatchManifest.WriteOptions));

        private static void Play(MatchSpec spec, string matchDir, string batchId, MatchOutcome outcome, double batchRemaining)
        {
            BatchManifest m = spec.Resolved;
            GameEngine engine = BatchWorld.Create(m, batchId + "-" + spec.PairId, spec.Seed);   // the arm name must not reach the prompt (the observation carries game_id)
            Dictionary<string, List<string>> scripts = BatchScripts.Build(engine, spec);
            string startDigest = BatchWorld.WorldDigest(engine);
            SpectatorSession session = null;
            var transport = new ScriptedSeatTransport(m.Provider.Model, () => session == null ? null : session.ActiveSeatId,
                scripts, m.Provider.Faults) { WitnessHook = seat => BatchPrivacy.Witness(engine, seat) };
            var recorder = new LlmRunRecorder(matchDir);
            LlmConfig config = BatchWorld.Config(m);
            var runner = new LlmTurnRunner(transport, config, recorder, null, new MemoryLlmStrategicNoteStore());
            session = new SpectatorSession(engine, runner, m.Seasons, spec.Seed, recorder);

            session.Start();
            var wall = Stopwatch.StartNew();
            double matchLimit = m.BatchCeilings.MaxMatchWallSeconds ?? 60;
            // The earlier of the two limits ends the match; which one it was is recorded. Checked between ticks only.
            bool batchLimitFirst = batchRemaining < matchLimit;
            long limitMs = (long)((batchLimitFirst ? batchRemaining : matchLimit) * 1000.0);
            bool wallHit = false;
            while (session.State == SpectatorState.Running)
            {
                if (wall.ElapsedMilliseconds >= limitMs) { wallHit = true; outcome.WallLimit = batchLimitFirst ? "max_batch_wall_seconds" : "max_match_wall_seconds"; break; }
                session.Tick();
                Thread.Yield();
            }

            outcome.SessionState = session.State.ToString();
            outcome.StopReason = session.StopReason;
            outcome.RunId = runner.RunId;
            outcome.SeasonsPlayed = session.SeasonsPlayed;
            outcome.Captures = transport.Captures();
            outcome.Requests = runner.TotalRequests;
            foreach (ExchangeCapture c in outcome.Captures)
                if (c.InputTokens.HasValue && c.OutputTokens.HasValue)
                { outcome.KnownInputTokens += c.InputTokens.Value; outcome.KnownOutputTokens += c.OutputTokens.Value; }
            outcome.InputTokens = runner.TotalInputTokens;
            outcome.OutputTokens = runner.TotalOutputTokens;
            foreach (SpectatorTurnRecord t in session.TurnRecords)
                outcome.TurnEndings.Add("s" + t.Season + " " + t.SeatId + ": " + t.EndReason + " " + t.EndReasonText);
            outcome.FinalState = BatchWorld.FinalState(engine);
            bool clean = true;
            foreach (SpectatorTurnRecord t in session.TurnRecords)
                clean &= t.EndReason == EndReason.ModelEndedTurn || t.EndReason == EndReason.GameOver;
            // "completed" means every turn ended by the model's own end_turn (or game over); a session that
            // finished its seasons while some turns were cut by a ceiling or a provider error is not clean.
            outcome.Status = wallHit ? "stopped" : session.State != SpectatorState.Completed ? "failed"
                : clean ? "completed" : "completed_with_stops";
            outcome.Detail = wallHit ? outcome.WallLimit + " reached during the match (checked between ticks)" : session.PauseReason;

            WriteText(outcome, matchDir, "prompts", "prompts.ndjson", PromptLines(outcome.Captures));
            string summaryPath = SpectatorSummary.Write(matchDir, session, config, transport, runner);
            Register(outcome, matchDir, "run_jsonl", recorder.FilePath);
            Register(outcome, matchDir, "exchanges", recorder.ExchangeFilePathFor(runner.RunId));
            Register(outcome, matchDir, "summary", summaryPath);
            outcome.Spatial = BatchSpatial.Collect(outcome.SpatialContext, outcome.Captures, recorder.ExchangeFilePathFor(runner.RunId), recorder.FilePath,
                startDigest, BatchWorld.WorldDigest(engine));
            outcome.Audit = MatchAuditor.Audit(matchDir, outcome, m);
        }

        private static string PromptLines(List<ExchangeCapture> captures)
        {
            var sb = new StringBuilder();
            foreach (ExchangeCapture c in captures)
                sb.Append(JsonSerializer.Serialize(new
                {
                    index = c.Index, seat = c.Seat, seat_request = c.SeatRequest, fault = c.Fault, script = c.ScriptNote,
                    system_sha256 = BatchPlan.Sha256(c.System), user_sha256 = BatchPlan.Sha256(c.User),
                    system = c.System, user = c.User, ok = c.Ok, reply = c.Reply,
                    input_tokens = c.InputTokens, output_tokens = c.OutputTokens, finish_reason = c.FinishReason, error = c.Error,
                })).Append('\n');
            return sb.ToString();
        }

        private static void WriteText(MatchOutcome outcome, string dir, string key, string name, string text)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Register(outcome, dir, key, path);
        }

        private static void Register(MatchOutcome outcome, string dir, string key, string path)
        {
            if (!File.Exists(path)) return;
            byte[] bytes = File.ReadAllBytes(path);
            outcome.Files[key] = new FileRef
            {
                Path = Path.GetFileName(path), Bytes = bytes.LongLength, Sha256 = BatchPlan.Sha256(bytes),
            };
        }
    }
}
