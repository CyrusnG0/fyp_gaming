using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Rich-deliberation experiment checks through the ACTUAL production <see cref="LlmTurnRunner"/>
    /// and <see cref="SpectatorSession"/>. Fake provider only: no network, no paid call. Scripted
    /// replies are plumbing evidence, never model behaviour.
    /// </summary>
    internal static partial class PlaygroundSelfTests
    {
        private const string ResearchResultMarker = "level_costs";
        private const string DiplomacyResultMarker = "treaty_types";

        private static LlmConfig RichConfig()
        {
            LlmConfig c = LlmConfig.RichDeliberation();
            c.RequestTimeoutMs = 5000;   // tests never wait on a real provider
            return c;
        }

        private static void EnqueueTelemetry(FakeLlmTransport fake, string text, int input, int output,
            int? reasoning, string finish)
        {
            fake.EnqueueDeferred().SetResult(LlmResponse.Success(text, 12, input, output, "req-fake",
                finish, reasoning, input + output, null, null, 200));
        }

        private static string UserOfCall(FakeLlmTransport fake, int index) => fake.Requests[index].User;

        private static void ExperimentConfiguration(TestLog log)
        {
            var off = new LlmConfig();
            log.Check(!off.ExperimentActive && off.ValidateExperiment().Count == 0
                      && !off.Redacted().Contains("experiment"),
                "experiment config: default is off, valid, and leaves the diagnostic text unchanged");
            LlmConfig rich = LlmConfig.RichDeliberation();
            log.Check(rich.ExperimentActive && rich.ValidateExperiment().Count == 0,
                "experiment config: the shipped large-budget profile is active and valid");

            var broken = new List<string>();
            Action<string, Action<LlmConfig>> expect = (code, mutate) =>
            {
                LlmConfig c = LlmConfig.RichDeliberation();
                mutate(c);
                if (!new List<string>(c.ValidateExperiment()).Contains(code)) broken.Add(code);
            };
            expect("max_requests_per_turn_not_set", c => c.MaxRequestsPerTurn = 0);
            expect("max_total_requests_above_hard_max", c => c.MaxTotalRequests = 999999);
            expect("max_actions_per_turn_not_set", c => c.MaxActionsPerTurn = 0);
            expect("max_accepted_actions_per_session_not_set", c => c.MaxAcceptedActionsPerSession = 0);
            expect("max_inquiries_per_turn_not_set", c => c.MaxInquiriesPerTurn = 0);
            expect("max_inquiries_per_session_not_set", c => c.MaxInquiriesPerSession = 0);
            expect("max_turn_seconds_not_set", c => c.MaxTurnSeconds = 0);
            expect("max_session_seconds_not_set", c => c.MaxSessionSeconds = 0);
            expect("request_timeout_ms_above_hard_max", c => c.RequestTimeoutMs = 99999999);
            expect("max_output_tokens_not_set", c => c.MaxOutputTokens = 0);
            expect("max_session_input_tokens_not_set", c => c.MaxSessionInputTokens = 0);
            expect("max_session_output_tokens_not_set", c => c.MaxSessionOutputTokens = 0);
            expect("max_prompt_chars_not_set", c => c.MaxPromptChars = 0);
            expect("experiment_requires_deliberation", c => c.EnableDeliberation = false);
            expect("experiment_profile_unknown", c => c.ExperimentProfile = "other");
            expect("max_turn_seconds_exceeds_session", c => c.MaxSessionSeconds = 100);
            expect("max_requests_per_turn_exceeds_session", c => c.MaxTotalRequests = 10);
            log.Check(broken.Count == 0,
                "experiment config: an unset, absurd or inconsistent ceiling is refused with a stable code",
                string.Join(",", broken));

            string path = Path.Combine(Path.GetTempPath(), "luanshi-exp-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{\"api_key\":\"sk-TESTSECRET0123456789\",\"enable_deliberation\":true,"
                    + "\"experiment_profile\":\"rich-deliberation-v1\",\"max_session_seconds\":900,"
                    + "\"max_session_input_tokens\":5000000,\"max_session_output_tokens\":900000,"
                    + "\"max_prompt_chars\":150000,\"context_history_items\":50,\"context_event_items\":80,"
                    + "\"context_memory_chars\":9000,\"context_cross_turn_inquiry_results\":7,"
                    + "\"context_inquiry_chars\":30000}");
                LlmConfig loaded = LlmConfig.LoadFromFile(path);
                log.Check(loaded.ExperimentActive && loaded.MaxSessionSeconds == 900
                          && loaded.MaxSessionInputTokens == 5000000 && loaded.MaxPromptChars == 150000
                          && loaded.ContextHistoryItems == 50 && loaded.ContextEventItems == 80
                          && loaded.ContextMemoryChars == 9000 && loaded.ContextCrossTurnInquiryResults == 7
                          && loaded.ContextInquiryChars == 30000,
                    "experiment config: every experiment key loads from the local config file");
                log.Check(!loaded.Redacted().Contains("TESTSECRET")
                          && !loaded.ExperimentSettingsJson().Contains("TESTSECRET")
                          && loaded.Redacted().Contains("experiment=rich-deliberation-v1"),
                    "experiment config: diagnostics and the settings record never contain the key");
            }
            finally { try { File.Delete(path); } catch (Exception) { } }
        }

        private static string FirstPromptWith(LlmConfig config, GameEngine engine, string reply)
        {
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(reply, 0, 100, 10);
            var runner = new LlmTurnRunner(fake, config, null);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            return fake.Requests[0].System + "\n=====\n" + fake.Requests[0].User;
        }

        private static void ExperimentFeatureOffIsUnchanged(TestLog log)
        {
            // Context knobs are inert unless the profile is set: prompts stay byte-identical.
            LlmConfig plain = ProductionConfig();
            LlmConfig knobs = ProductionConfig();
            knobs.MaxPromptChars = 300000;
            knobs.ContextHistoryItems = 99;
            knobs.ContextEventItems = 99;
            knobs.ContextMemoryChars = 50000;
            knobs.ContextCrossTurnInquiryResults = 9;
            knobs.ContextInquiryChars = 90000;
            knobs.MaxSessionSeconds = 0;
            string a = FirstPromptWith(plain, ProductionEngine(), "{\"type\":\"end_turn\"}");
            string b = FirstPromptWith(knobs, ProductionEngine(), "{\"type\":\"end_turn\"}");
            log.Check(a == b && !a.Contains("INFORMATION ACCESS") && !a.Contains("CONTEXT COMPLETENESS")
                      && a.Contains(PromptBuilder.DeliberationPromptVersion)
                      && !a.Contains(PromptBuilder.RichDeliberationPromptVersion),
                "feature off: plain deliberation prompt is unchanged by inert experiment knobs");

            var offPlain = new LlmConfig { MaxRequestsPerTurn = 6, MaxTotalRequests = 60 };
            var offKnobs = new LlmConfig { MaxRequestsPerTurn = 6, MaxTotalRequests = 60,
                MaxPromptChars = 300000, ContextHistoryItems = 99, ContextEventItems = 99 };
            log.Check(FirstPromptWith(offPlain, ProductionEngine(), "{\"type\":\"end_turn\"}")
                      == FirstPromptWith(offKnobs, ProductionEngine(), "{\"type\":\"end_turn\"}"),
                "feature off: the legacy prompt is unchanged by inert experiment knobs");

            // A plain-deliberation turn writes no experiment lines and no deliberation_event.
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-off-" + Guid.NewGuid().ToString("N"));
            try
            {
                var recorder = new LlmRunRecorder(dir);
                var fake = new FakeLlmTransport();
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}", 0, 100, 10);
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                var runner = new LlmTurnRunner(fake, ProductionConfig(), recorder);
                var host = new ProductionHost(ProductionEngine(), "seat-a");
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                string runText = File.ReadAllText(recorder.FilePath);
                string exchText = File.ReadAllText(recorder.ExchangeFilePathFor(runner.RunId));
                log.Check(!runText.Contains("deliberation_event") && !exchText.Contains("experiment_config")
                          && !exchText.Contains("turn_end") && runner.StopCeiling == null
                          && !runner.EndReasonText.Contains("[ceiling"),
                    "feature off: no experiment record line, event field or ceiling text appears");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        private static void ExperimentRichPromptAndTruncationReport(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            LlmConfig config = RichConfig();
            config.ContextHistoryItems = 3;
            config.ContextEventItems = 2;
            config.ContextMemoryChars = 120;
            var memories = new InMemoryLlmMemoryStore();
            for (int i = 0; i < 6; i++)
                memories.Append(LlmMemoryExtractor.FromSummary(ProdGame, "seat-a", 1,
                    "fixture memory number " + i + " with enough words to occupy the character budget"));
            // Five accepted orders so the feedback list (cap 3) must drop two.
            for (int i = 0; i < 5; i++)
                fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-a\"}", 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            var runner = new LlmTurnRunner(fake, config, null, memories);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);

            string system = fake.Requests[0].System;
            string first = UserOfCall(fake, 0);
            string last = UserOfCall(fake, fake.CallCount - 1);
            log.Check(system.Contains("RICH DELIBERATION") && system.Contains("unification")
                      && first.Contains(PromptBuilder.RichDeliberationPromptVersion)
                      && first.Contains("INFORMATION ACCESS AND LIMITS")
                      && first.Contains("CONTEXT COMPLETENESS"),
                "rich prompt: rules/goal, access section and completeness report are present");
            log.Check(first.Contains("{\"tool\":\"manual_lookup\"") && first.Contains("{\"tool\":\"inspect_entity\"")
                      && first.Contains("{\"tool\":\"search_history\"") && first.Contains("{\"tool\":\"recall_memory\"")
                      && first.Contains("{\"note\":") && first.Contains("seasonal_economy")
                      && first.Contains("requests this turn 40") && first.Contains("inquiries this turn 30"),
                "rich prompt: states how to request more (all four tools, note, topic ids) and the live allowances");
            log.Check(first.Contains("other factions' private information: 0 (total not known to the host)"),
                "rich prompt: states that nothing beyond the seat-filtered observation is supplied");

            // After five accepted orders: feedback 3 of 5, memories truncated, events capped at 2.
            log.Check(last.Contains("own action feedback lines (newest kept): 3 of 5 (INCOMPLETE)"),
                "rich prompt: reports the feedback cut honestly (3 of 5)", Slice(last, "own action feedback"));
            Match mem = Regex.Match(last, @"long-term memories \(ranked, character-limited\): (\d+) of (\d+)");
            log.Check(mem.Success && int.Parse(mem.Groups[1].Value) < int.Parse(mem.Groups[2].Value)
                      && last.Contains("recall_memory"),
                "rich prompt: reports memories selected of stored when the character budget cuts some",
                mem.Value);
            Match ev = Regex.Match(last, @"observation events \(newest kept\): (\d+) of (\d+)");
            int promptEvents = Regex.Matches(Between(last, "<<<BEGIN_UNTRUSTED_OBSERVATION_JSON>>>",
                "<<<END_UNTRUSTED_OBSERVATION_JSON>>>"), "\"seq\":").Count;
            log.Check(ev.Success && ev.Groups[1].Value == "2" && int.Parse(ev.Groups[2].Value) > 2
                      && promptEvents == 2,
                "rich prompt: the event cap is applied and the same cap is reported", ev.Value + " json=" + promptEvents);
            log.Check(last.Contains("long-term memories") && !last.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN"),
                "rich prompt: the rich note section replaces the plain-deliberation note header");
            log.Check(runner.LastContextReportJson != null && runner.LastContextReportJson.Contains("\"truncated\":true"),
                "rich prompt: the runner exposes the machine-readable completeness report");

            // Under the ceiling the prompt steps down the ladder and says so.
            GameEngine engine2 = ProductionEngine();
            var host2 = new ProductionHost(engine2, "seat-a");
            var fake2 = new FakeLlmTransport();
            LlmConfig tight = RichConfig();
            fake2.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            new LlmTurnRunner(fake2, tight, null).Begin(host2, host2.Token, "seat-a", ProdSeed);
            int full = fake2.Requests[0].System.Length + fake2.Requests[0].User.Length;

            var fake3 = new FakeLlmTransport();
            LlmConfig small = RichConfig();
            small.MaxPromptChars = fake2.Requests[0].System.Length + 5000;   // cannot hold even the minimal prompt
            var host3 = new ProductionHost(ProductionEngine(), "seat-a");
            fake3.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            var runner3 = new LlmTurnRunner(fake3, small, null);
            runner3.Begin(host3, host3.Token, "seat-a", ProdSeed);
            DrainProduction(runner3);
            log.Check(full > small.MaxPromptChars && fake3.CallCount == 0
                      && runner3.EndReason == EndReason.PromptTooLarge && runner3.StopCeiling == "prompt_chars"
                      && host3.EndTurnCalls == 1,
                "rich prompt: a prompt that cannot fit the ceiling is refused before any provider call",
                runner3.EndReasonText);
        }

        private static string Between(string text, string begin, string end)
        {
            int a = text.IndexOf(begin, StringComparison.Ordinal);
            int b = text.IndexOf(end, StringComparison.Ordinal);
            return a < 0 || b < a ? "" : text.Substring(a, b - a);
        }

        private static string Slice(string text, string marker)
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            return at < 0 ? "(missing " + marker + ")" : text.Substring(at, Math.Min(400, text.Length - at));
        }
    }
}
