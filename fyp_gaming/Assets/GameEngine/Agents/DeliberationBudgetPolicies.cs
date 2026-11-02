using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Host-facing policy factories. Nothing here changes the live game: the demo factory only mirrors
    /// the limits the existing <see cref="LlmConfig"/> already uses, and the research factory accepts
    /// limits the host states explicitly. The module never invents a larger paid budget.
    /// </summary>
    public static class DeliberationBudgetPolicies
    {
        public const string DemoProfileId = "demo-request-limited";
        public const string DemoProfileVersion = "1";
        public const string ResearchProfileVersion = "1";

        /// <summary>
        /// The demo-equivalent policy: the same six requests per turn, five accepted action admissions
        /// per turn, sixty-second turn window and sixty-request session cap that <see cref="LlmConfig"/>
        /// already defaults to. Inquiry is a zero allowance because the live demo exposes no inquiry
        /// tool; an integration that enables LS-018 must supply its own policy. Token limits are left
        /// unenforced, so <see cref="DeliberationBudgetPolicy.TokenBudgetEnforced"/> is false.
        /// </summary>
        public static DeliberationBudgetPolicy DemoDefaults() => FromDemoConfig(new LlmConfig());

        /// <summary>
        /// Mirror an existing live configuration instead of copying its numbers. The integration slice
        /// passes the runner's actual <see cref="LlmConfig"/> so a configured demo limit and the budget
        /// record cannot drift apart. A negative live limit becomes a zero allowance (the runner treats
        /// it as "stop"), never an unlimited budget.
        /// </summary>
        public static DeliberationBudgetPolicy FromDemoConfig(LlmConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var limits = new DeliberationBudgetLimits
            {
                MaxRequestsPerTurn = NonNegative(config.MaxRequestsPerTurn),
                MaxRequestsPerSession = NonNegative(config.MaxTotalRequests),
                MaxActionsPerTurn = NonNegative(config.MaxActionsPerTurn),
                MaxActionsPerSession = DeliberationBudgetLimits.Unenforced,
                MaxInquiryPerTurn = 0,
                MaxInquiryPerSession = 0,
                MaxWallMillisPerTurn = Milliseconds(config.MaxTurnSeconds, nameof(config.MaxTurnSeconds)),
                MaxWallMillisPerSession = DeliberationBudgetLimits.Unenforced,
                MaxInputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxInputTokensPerSession = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerTurn = DeliberationBudgetLimits.Unenforced,
                MaxOutputTokensPerSession = DeliberationBudgetLimits.Unenforced,
            };
            return new DeliberationBudgetPolicy(DemoProfileId, DemoProfileVersion,
                DeliberationBudgetMode.RequestLimited, limits);
        }

        /// <summary>
        /// An explicit research policy. The caller states every limit, the profile id recorded with the
        /// experiment and whether token budgets are enforced. No default research budget is selected here.
        /// </summary>
        public static DeliberationBudgetPolicy Research(string profileId,
            DeliberationBudgetMode mode, DeliberationBudgetLimits limits)
            => new DeliberationBudgetPolicy(profileId, ResearchProfileVersion, mode, limits);

        private static int NonNegative(int value) => value < 0 ? 0 : value;

        private static int Milliseconds(int seconds, string name)
        {
            if (seconds <= 0) return 0;
            long millis = (long)seconds * 1000L;
            if (millis > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(name, seconds,
                    "a turn window this large cannot be represented in milliseconds");
            }
            return (int)millis;
        }
    }
}
