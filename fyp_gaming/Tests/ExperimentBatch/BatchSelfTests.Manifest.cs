using System;
using System.Collections.Generic;
using System.Linq;

namespace LuanShi.ExperimentBatch
{
    internal static partial class BatchSelfTests
    {
        private static void ManifestExampleIsValid(TestLog log)
        {
            string json = ExampleJson();
            BatchManifest m = BatchManifest.Parse(json, out List<string> parseErrors);
            log.Check(m != null && parseErrors.Count == 0, "the example manifest parses strictly");
            List<string> errors = BatchPlan.Validate(m);
            log.Check(errors.Count == 0, "the example manifest validates", string.Join("; ", errors));
            log.Check(System.Text.RegularExpressions.Regex.IsMatch(m.PinnedCommit ?? "", "^[0-9a-f]{7,40}$") && m.Scenario == "compact-contact-v1" && m.BalanceProfile == "food-easy"
                      && m.FirstMover == "seat-0" && m.Provider.Kind == "fake" && m.Features.Research && m.Features.RichProfile
                      && m.Ceilings.MaxTotalRequests == 80 && m.BatchCeilings.MaxTotalInputTokens == 5000000
                      && m.Seeds.SequenceEqual(new[] { 4242 }),
                "the manifest pins commit, scenario, profile, first mover, provider and explicit ceilings");
            BatchManifest again = BatchManifest.Parse(m.ToJson(), out _);
            log.Check(again != null && again.ToJson() == m.ToJson(), "the manifest round-trips without change");
        }

        private static void ManifestRefusesSecretsAndTypos(TestLog log)
        {
            string withKey = ExampleJson().Replace("\"provider\": {", "\"api_key\": \"not-real-but-present\", \"provider\": {");
            BatchManifest m = BatchManifest.Parse(withKey, out List<string> errors);
            log.Check(m == null && Has(errors, "credential-like key") && !Has(errors, "not-real-but-present"),
                "a credential key is refused and its value is never echoed");

            string secretValue = "sk-ABCDEFGHIJKLMNOPQRSTUV";
            m = BatchManifest.Parse(ExampleJson().Replace("fake-model", secretValue), out errors);
            log.Check(m == null && Has(errors, "credential-like value") && !Has(errors, secretValue),
                "a key-shaped value is refused and not echoed");
            m = BatchManifest.Parse(ExampleJson().Replace("fake-model", "Bearer abcdefghijklmnop1234"), out errors);
            log.Check(m == null && Has(errors, "credential-like value"), "a bearer-token-shaped value is refused");

            m = BatchManifest.Parse(ExampleJson().Replace("\"spatial_context\"", "\"spatial_contxt\""), out errors);
            log.Check(m == null && errors.Count > 0, "an unknown field (typo) is rejected instead of silently ignored");
            m = BatchManifest.Parse("{ not json", out errors);
            log.Check(m == null && Has(errors, "not valid JSON"), "malformed JSON is rejected");
        }

        private static void ManifestRefusesBadSettings(TestLog log)
        {
            Action<string, Action<BatchManifest>> expect = (fragment, mutate) =>
            {
                List<string> errors = BatchPlan.Validate(Example(mutate));
                log.Check(Has(errors, fragment), "refused: " + fragment, string.Join("; ", errors));
            };
            expect("ceilings.max_total_requests must be set", m => m.Ceilings.MaxTotalRequests = null);
            expect("ceilings.max_session_input_tokens must be set", m => m.Ceilings.MaxSessionInputTokens = 0);
            expect("batch_ceilings.max_batch_wall_seconds must be set", m => m.BatchCeilings.MaxBatchWallSeconds = null);
            expect("batch_ceilings.max_total_output_tokens must be set", m => m.BatchCeilings.MaxTotalOutputTokens = -1);
            expect("max_matches is below the planned", m => { m.Seeds = new List<int> { 1, 2, 3 }; });
            expect("provider.kind must be 'fake'", m => m.Provider.Kind = "deepseek");
            expect("unknown scenario", m => m.Scenario = "nowhere-v9");
            expect("unknown or missing balance_profile", m => m.BalanceProfile = "typo");
            expect("first_mover", m => m.FirstMover = "seat-1");
            expect("faction assignment for seat-1", m => m.Factions["seat-1"] = "Wu");
            expect("pinned_commit", m => m.PinnedCommit = "HEAD");
            expect("experiment.variable", m => m.Experiment.Variable = "not_a_flag");
            expect("must differ", m => m.Experiment.Treatment = m.Experiment.Control);
            expect("rich_profile requires", m => m.Features.Deliberation = false);
            expect("unknown_usage_policy must be", m => m.BatchCeilings.UnknownUsagePolicy = null);
            expect("the production runner would refuse this configuration: max_requests_per_turn_exceeds_session", m => m.Ceilings.MaxTotalRequests = 10);
            expect("the production runner would refuse this configuration: max_turn_seconds_exceeds_session", m => m.Ceilings.MaxSessionSeconds = 30);
            expect("seeds must be distinct", m => m.Seeds = new List<int> { 5, 5 });
            expect("unknown fault kind", m => m.Provider.Faults.Add(new FaultSpec { Seat = "seat-0", SeatRequest = 1, Kind = "melt" }));
            expect("fault names a seat", m => m.Provider.Faults.Add(new FaultSpec { Seat = "seat-9", SeatRequest = 1, Kind = "provider_error" }));
            expect("personas.by_seat set while personas are disabled", m => m.Personas.BySeat["seat-0"] = "wei-mild");
        }

        private static void PairsDifferOnlyInTheVariable(TestLog log)
        {
            BatchManifest m = Example(x => x.Seeds = new List<int> { 4242, 4243 });
            List<MatchSpec> specs = BatchPlan.Build(m);
            log.Check(specs.Count == 4 && specs[0].MatchId == "seed4242-control" && specs[1].MatchId == "seed4242-treatment"
                      && specs[2].PairId == "seed4243",
                "two arms per seed, control first");
            PairCheck check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(check.Ok && check.Differences.Count == 1 && check.Differences[0].StartsWith("features.spatial_context: false -> true"),
                "the only difference between the arms is features.spatial_context", string.Join("; ", check.Differences));
            log.Check(specs[0].Flat["seasons"] == specs[1].Flat["seasons"]
                      && specs[0].Resolved.Seeds.Single() == 4242 && specs[1].Resolved.Seeds.Single() == 4242,
                "both arms of a pair carry the same single seed");
            List<MatchSpec> again = BatchPlan.Build(Example(x => x.Seeds = new List<int> { 4242, 4243 }));
            bool same = true;
            for (int i = 0; i < specs.Count; i++) same &= specs[i].SettingsHash == again[i].SettingsHash;
            log.Check(same && specs[0].SettingsHash != specs[1].SettingsHash && specs[0].SettingsHash != specs[2].SettingsHash,
                "settings hashes are reproducible and tell arms and seeds apart");
            string[] mustAppear = { "ceilings.max_prompt_chars", "features.attack", "personas.by_seat", "provider.faults", "setup.research_points", "first_mover", "factions.seat-0", "pinned_commit" };
            log.Check(mustAppear.All(k => specs[0].Flat.ContainsKey(k)),
                "the flattened settings cover every manifest section the pair check compares",
                string.Join(",", mustAppear.Where(k => !specs[0].Flat.ContainsKey(k))));
        }

        private static void PairCheckCatchesExtraDifference(TestLog log)
        {
            BatchManifest m = Example();
            List<MatchSpec> specs = BatchPlan.Build(m);
            specs[1].Resolved.Seasons = 3;
            specs[1].Flat = BatchPlan.FlatSettings(specs[1].Resolved);
            PairCheck check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(!check.Ok && check.Differences.Count == 2, "an extra difference (seasons) fails the pair check", string.Join("; ", check.Differences));

            specs = BatchPlan.Build(m);
            specs[1].Resolved.Ceilings.MaxTotalRequests = 79;
            specs[1].Resolved.Provider.Faults.Add(new FaultSpec { Seat = "seat-1", SeatRequest = 2, Kind = "empty_reply" });
            specs[1].Flat = BatchPlan.FlatSettings(specs[1].Resolved);
            check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(!check.Ok && check.Differences.Count >= 4, "a changed ceiling and a one-sided fault are both caught", string.Join("; ", check.Differences));

            specs = BatchPlan.Build(m);
            specs[1].Resolved.Features.Research = !specs[1].Resolved.Features.Research;
            specs[1].Flat = BatchPlan.FlatSettings(specs[1].Resolved);
            check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(!check.Ok, "flipping a different feature flag is not accepted as the declared variable");

            specs = BatchPlan.Build(m);
            specs[1].Seed = 9;
            check = BatchPlan.CheckPair(specs[0], specs[1], m.Experiment);
            log.Check(!check.Ok && check.Problems.Any(p => p.Contains("different pairs or seeds")), "arms from different seeds are not a pair");
        }
    }
}
