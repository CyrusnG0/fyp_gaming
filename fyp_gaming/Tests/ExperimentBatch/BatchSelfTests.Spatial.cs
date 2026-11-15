using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// The matched spatial pair through the PRODUCTION prompt path. The fake provider only returns scripted replies
    /// that ignore the spatial setting, so these checks say nothing about model behaviour: they show what each arm was
    /// SENT, that the arms differ in exactly the production spatial context, and that nothing hidden reached a prompt.
    /// </summary>
    internal static partial class BatchSelfTests
    {
        private static void SpatialConfigurationIsTheProductionSwitch(TestLog log)
        {
            BatchManifest m = Example();
            var specs = BatchPlan.Build(m);
            LlmConfig control = BatchWorld.Config(specs[0].Resolved), treatment = BatchWorld.Config(specs[1].Resolved);
            log.Check(control.SpatialContext.Level == SpatialDetailLevel.Off && treatment.SpatialContext.Level == SpatialDetailLevel.Standard,
                "control sets LlmConfig.SpatialContext off and treatment sets it to the production standard preset");
            log.Check(control.RequireVisibleMarchRoutes && treatment.RequireVisibleMarchRoutes && control.EffectiveVisibleMarchRoutes && treatment.EffectiveVisibleMarchRoutes,
                "require_visible_march_routes is true in both arms");
            PairCheck check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(check.Ok && check.Differences.Count == 1 && check.Differences[0].StartsWith("features.spatial_context"),
                "seed, scenario, factions, personas, research, deliberation and every ceiling are identical: spatial_context is the only difference",
                string.Join("; ", check.Differences));
            specs[1].Resolved.Spatial.Level = "rich";
            log.Check(BatchWorld.Config(specs[1].Resolved).SpatialContext.Level == SpatialDetailLevel.Rich, "spatial.level selects the production rich preset");

            List<string> errors = BatchPlan.Validate(Example(x => x.Features.VisibleMarchRoutes = false));
            log.Check(Has(errors, "visible_march_routes=true in both arms"), "a spatial pair without visible march routes in both arms is refused", string.Join("; ", errors));
            errors = BatchPlan.Validate(Example(x => x.Spatial.Level = "huge"));
            log.Check(Has(errors, "spatial.level"), "an unknown spatial level is refused", string.Join("; ", errors));
        }

        private static void SpatialPairSendsProductionPrompts(TestLog log)
        {
            BatchManifest m = Example(x =>
            {
                x.Personas.Enabled = true;
                x.Personas.BySeat["seat-0"] = "wei-mild";
            });
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome c = Match(r, "seed4242-control"), t = Match(r, "seed4242-treatment");
                PairComparison pair = r.Pairs[0];
                log.Check(c.Status == "completed" && t.Status == "completed" && r.AllAuditsOk && pair.SpatialProblems != null && pair.SpatialProblems.Count == 0,
                    "both arms complete and the spatial pair check finds no problem", string.Join("; ", pair.SpatialProblems ?? new List<string>()) + " " + string.Join("; ", t.Audit.Mismatches));

                // What the production runner would build for this arm, rebuilt here from the config alone.
                List<MatchSpec> specs = BatchPlan.Build(m);
                GameEngine world = BatchWorld.Create(m, "probe", 4242);
                bool Production(MatchOutcome o, ExchangeCapture cap, bool spatial)
                {
                    LlmConfig config = BatchWorld.Config(specs.Find(s => s.MatchId == o.MatchId).Resolved);
                    LlmPersona persona = LlmPersona.ResolveForTurn(config, null, world.Observe(cap.Seat), cap.Seat);
                    return cap.System == PromptBuilder.CombinedSystemPromptFor(true, false, persona, false, true, true, true, spatial);
                }
                log.Check(c.Captures.All(x => Production(c, x, false)) && t.Captures.All(x => Production(t, x, true)),
                    "the fake provider received exactly the production system prompt for each arm and seat (personas on)");
                log.Check(t.Captures.All(x => x.System.EndsWith(PromptBuilder.SpatialContextAddendum())) && c.Captures.All(x => !x.System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES")),
                    "every treatment system prompt ends with the production spatial addendum and no control prompt contains it");
                log.Check(t.Captures.All(x => x.User.Contains(PromptBuilder.SpatialHeader)) && c.Captures.All(x => !x.User.Contains("SPATIAL CONTEXT (seat-filtered")),
                    "every treatment user prompt carries the production spatial section and no control prompt does");
                log.Check(c.Captures.Count == 14 && t.Captures.Count == 14 && c.Captures.Select(x => x.Seat + "#" + x.SeatRequest).SequenceEqual(t.Captures.Select(x => x.Seat + "#" + x.SeatRequest)),
                    "both arms make the same 14 requests in the same order");

                JsonElement cs = Summary(root, m, c), ts = Summary(root, m, t);
                string cv = MatchAuditor.Str(cs, "prompt_version"), tv = MatchAuditor.Str(ts, "prompt_version");
                log.Check(MatchAuditor.Str(cs, "spatial_context_level") == "off" && MatchAuditor.Str(ts, "spatial_context_level") == "standard"
                          && cs.GetProperty("visible_march_routes_enforced").GetBoolean() && ts.GetProperty("visible_march_routes_enforced").GetBoolean(),
                    "each summary records its spatial level and that visible march routes were enforced in both arms");
                log.Check(cv != tv && tv.Contains("spatial") && !cv.Contains("spatial") && c.Spatial.PromptVersions.SequenceEqual(new[] { cv }) && t.Spatial.PromptVersions.SequenceEqual(new[] { tv }),
                    "the prompt version differs and names spatial only in the treatment, in the summary and in every request", cv + " | " + tv);
                log.Check(c.Spatial.SchemaHashes.Count == 2 && t.Spatial.SchemaHashes.Count == 2 && !c.Spatial.SchemaHashes.Intersect(t.Spatial.SchemaHashes).Any(),
                    "one schema hash per persona in each arm (two seats, two personas) and no hash is shared between the arms");

                List<JsonElement> cr = Records(root, m, c), tr = Records(root, m, t);
                log.Check(cr.All(x => !x.TryGetProperty("spatial_truncation", out _)) && tr.All(x => x.TryGetProperty("spatial_truncation", out _)),
                    "the run records carry a spatial_truncation report for every treatment request and none for control");
                log.Check(t.Spatial.TruncationReports == 14 && c.Spatial.TruncationReports == 0,
                    "the evidence counts the production truncation reports: 14 treatment, 0 control");
                bool cut = t.Spatial.TruncatedRequests > 0;
                log.Check(cut == (t.Spatial.TruncationReasons.Count > 0) && (!cut || t.Spatial.Dropped.Values.Sum() > 0) && t.Spatial.MaxTotalChars > 0,
                    "actual spatial truncation is recorded: " + t.Spatial.TruncatedRequests + " requests truncated ("
                    + string.Join(",", t.Spatial.TruncationReasons) + "), items dropped " + t.Spatial.Dropped.Values.Sum() + ", largest prompt " + t.Spatial.MaxTotalChars + " chars");
                string report = BatchReport.Text(r);
                log.Check(report.Contains("spatial pair check: ok") && report.Contains("truncated " + t.Spatial.TruncatedRequests),
                    "the batch report states the pair check and the truncation count");
            }
            finally { Cleanup(root); }
        }

        private static void SpatialPairKeepsTheHiddenWorldAndPrivacy(TestLog log)
        {
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome c = Match(r, "seed4242-control"), t = Match(r, "seed4242-treatment");
                log.Check(c.Spatial.WorldStartDigest == t.Spatial.WorldStartDigest && !string.IsNullOrEmpty(c.Spatial.WorldStartDigest),
                    "both arms start from the same canonical world, hidden parts included");
                log.Check(r.Pairs[0].FinalWorldIdentical == true && c.Spatial.WorldFinalDigest == t.Spatial.WorldFinalDigest,
                    "with identical scripted orders both arms end in the same canonical world: the spatial prompt changes only what the model is shown");
                log.Check(c.Spatial.ObservationHashes.Count == 14 && c.Spatial.ObservationHashes.All(kv => t.Spatial.ObservationHashes[kv.Key] == kv.Value),
                    "the seat-filtered observation hash is identical in every aligned request (the arm name never reaches the prompt)");
                log.Check(!c.Captures.Any(x => x.User.Contains("treatment") || x.User.Contains("-control")) && !t.Captures.Any(x => x.User.Contains("-treatment") || x.User.Contains("-control")),
                    "no prompt names its arm");

                GameEngine probe = BatchWorld.Create(m, "probe", 4242);
                HashSet<string> hidden0 = BatchSpatial.HiddenFrom(probe, "seat-0"), hidden1 = BatchSpatial.HiddenFrom(probe, "seat-1");
                log.Check(hidden0.Contains("city-shu") && hidden0.Contains("Chengdu") && hidden1.Contains("city-wei") && hidden1.Contains("Luoyang"),
                    "the opening fog hides each seat's rival capital (ids and names), derived from the engine's own observation");
                log.Check(t.Spatial.HiddenIdsChecked > 0 && t.Spatial.HiddenIdLeaks.Count == 0 && c.Spatial.HiddenIdLeaks.Count == 0,
                    "no hidden rival id or city name appears in any dispatched prompt of either arm (" + t.Spatial.HiddenIdsChecked + " ids checked)");
                bool direct = true;
                foreach (ExchangeCapture x in t.Captures)
                    foreach (string id in x.Seat == "seat-0" ? hidden0 : hidden1)
                        direct &= !x.User.Contains(id) && !x.System.Contains(id);
                log.Check(direct, "a direct scan of every treatment prompt finds no id the seat's fog hides");

                var planted = new List<ExchangeCapture> { new ExchangeCapture { Seat = "seat-0", SeatRequest = 1, System = "s", User = "the enemy holds Chengdu" } };
                SpatialEvidence probeEvidence = BatchSpatial.Collect("standard", planted, null, null,
                    new Dictionary<string, HashSet<string>> { { "seat-0", hidden0 } }, "d", "d");
                log.Check(probeEvidence.HiddenIdLeaks.Count == 1 && probeEvidence.HiddenIdLeaks[0].Contains("Chengdu"),
                    "the leak scan is not vacuous: a prompt that names a hidden city is reported");
                log.Check(!SeatPrompts(t, "seat-1").Contains("NOTE-seat-0") && !SeatPrompts(t, "seat-0").Contains("NOTE-seat-1"),
                    "private strategic notes still never cross seats in the treatment arm");
            }
            finally { Cleanup(root); }
        }

        private static void SpatialPairCheckCatchesABrokenPair(TestLog log)
        {
            BatchManifest m = Example();
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome c = Match(r, "seed4242-control"), t = Match(r, "seed4242-treatment");
                log.Check(BatchSpatial.CheckPair(c, t).Count == 0, "the genuine pair passes");
                List<string> same = BatchSpatial.CheckPair(c, c);
                log.Check(same.Any(x => x.Contains("treatment spatial level is off")) && same.Any(x => x.Contains("lack spatial material")),
                    "two control arms are not accepted as a spatial pair", string.Join("; ", same));

                string saved = t.Captures[0].System;
                t.Captures[0].System = saved + "\nextra rule";
                log.Check(BatchSpatial.CheckPair(c, t).Any(x => x.Contains("not 'control system prompt + production spatial addendum'")),
                    "an extra system-prompt difference beyond the spatial addendum is caught");
                t.Captures[0].System = saved;

                string hash = t.Spatial.ObservationHashes["seat-0#1"];
                t.Spatial.ObservationHashes["seat-0#1"] = "different";
                log.Check(BatchSpatial.CheckPair(c, t).Any(x => x.Contains("observation differs")), "a differing seat observation is caught");
                t.Spatial.ObservationHashes["seat-0#1"] = hash;

                string digest = t.Spatial.WorldStartDigest;
                t.Spatial.WorldStartDigest = "other";
                log.Check(BatchSpatial.CheckPair(c, t).Any(x => x.Contains("same canonical world")), "arms that start from different worlds are caught");
                t.Spatial.WorldStartDigest = digest;

                t.Spatial.HiddenIdLeaks.Add("seat-0 request 1 shows hidden 'city-shu'");
                log.Check(BatchSpatial.CheckPair(c, t).Any(x => x.StartsWith("treatment leak")), "a hidden-id leak makes the pair fail");
                t.Spatial.HiddenIdLeaks.Clear();
                log.Check(BatchSpatial.CheckPair(c, t).Count == 0, "the restored pair passes again");
            }
            finally { Cleanup(root); }
        }

        private static void SpatialRichLevelRunsThroughProduction(TestLog log)
        {
            BatchManifest m = Example(x => x.Spatial.Level = "rich");
            BatchResult r = RunBatch(m, out string root);
            try
            {
                MatchOutcome t = Match(r, "seed4242-treatment");
                log.Check(t.SpatialContext == "rich" && t.Spatial.SystemWithAddendum == t.Requests && r.Pairs[0].SpatialProblems.Count == 0 && r.AllAuditsOk,
                    "the rich preset also reaches every treatment prompt and passes the pair check", string.Join("; ", r.Pairs[0].SpatialProblems));
                log.Check(t.Spatial.TruncationReports == t.Requests, "the rich preset records a truncation report per request too");
            }
            finally { Cleanup(root); }
        }

        private static JsonElement Summary(string root, BatchManifest m, MatchOutcome o)
            => JsonDocument.Parse(File.ReadAllText(Path.Combine(MatchDir(root, m, o), o.Files["summary"].Path))).RootElement.Clone();
    }
}
