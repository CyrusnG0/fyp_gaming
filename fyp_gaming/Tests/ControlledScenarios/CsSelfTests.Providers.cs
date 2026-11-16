using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        /// <summary>
        /// CS-12: the provider status must be honest. Fake is available; the live provider is
        /// prepared and offline-tested but has not been exercised against a network, and no
        /// command or refusal prints a credential even when one is present in the environment.
        /// </summary>
        private static void ProviderStatusHonesty(TestLog log)
        {
            string previous = Environment.GetEnvironmentVariable("LUANSHI_LLM_API_KEY");
            const string canary = "sk-CANARYCANARYCANARYCANARY00";
            Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", canary);
            try
            {
                string status = CsProviderFactory.StatusText;
                log.Check(status.IndexOf("fake: AVAILABLE", StringComparison.Ordinal) >= 0,
                    "[CS-12] provider status states that the fake provider is available");
                log.Check(status.IndexOf("NOT exercised or validated", StringComparison.Ordinal) >= 0
                        && status.IndexOf("does not claim live", StringComparison.Ordinal) >= 0,
                    "[CS-12] provider status does not claim a validated real-provider connection");
                log.Check(status.IndexOf("makes no live call", StringComparison.Ordinal) >= 0
                        && status.IndexOf("no paid call", StringComparison.Ordinal) >= 0,
                    "[CS-12] provider status states that no live or paid call is made");
                log.Check(!status.Contains(canary) && !LooksLikeKey(status),
                    "[CS-12] provider status never prints a credential from the environment");

                IScenarioProvider fake = CsProviderFactory.Create("fake", FakeModel);
                log.Check(fake.Kind == "fake" && !fake.IsLive && fake.Model == FakeModel,
                    "[CS-12] selecting 'fake' yields an offline provider");

                var context = new CsProviderContext
                {
                    RunDirectory = null,
                    ScenarioId = "cs-visible-contact-v1",
                    Seed = 4242,
                    Config = new LlmConfig(),
                    ActiveSeatId = () => "seat-0",
                    Script = new List<CsScriptStep>(),
                };
                ILlmTransport transport = fake.CreateTransport(context);
                log.Check(transport is CsSeatTransport && transport.Provider == "fake",
                    "[CS-12] the fake provider boundary returns the per-seat fake transport");

                string refusal = null;
                try { CsProviderFactory.Create("openai-compatible", FakeModel); }
                catch (CsLiveRefusedException e) { refusal = e.Message; }
                log.Check(refusal != null && refusal.IndexOf("refused", StringComparison.Ordinal) >= 0
                        && refusal.IndexOf("no live call", StringComparison.Ordinal) >= 0,
                    "[CS-12] selecting a live provider is refused, not silently downgraded");
                log.Check(refusal != null && !refusal.Contains(canary) && !LooksLikeKey(refusal),
                    "[CS-12] the live-provider refusal never prints a credential");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUANSHI_LLM_API_KEY", previous);
            }
        }

        private static bool LooksLikeKey(string text)
            => !string.IsNullOrEmpty(text)
               && Regex.IsMatch(text, "sk-[A-Za-z0-9_\\-]{12,}|Bearer\\s+[A-Za-z0-9._\\-]{12,}");
    }
}
