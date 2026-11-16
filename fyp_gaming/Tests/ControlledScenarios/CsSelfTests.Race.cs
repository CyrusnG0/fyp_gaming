using System.Collections.Generic;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        /// <summary>
        /// [CS-35] The runner loop and the recording transport share a wall deadline. When the
        /// transport abandons the in-flight request first, the runner records a failed turn; the
        /// seal-time capture scan must still attribute that cut to the named wall ceiling. A
        /// genuine provider failure without such evidence must stay a failure.
        /// </summary>
        private static void WallCutAttribution(TestLog log)
        {
            string limit = null;
            bool cut = CsRunner.WallCutByTransport(
                new List<CsDispatchCapture> { new CsDispatchCapture { State = "deadline_abandoned" } },
                true, ref limit);
            log.Check(cut && limit == "max_run_wall_seconds",
                "[CS-35] deadline abandonment attributes run wall",
                "cut=" + cut + " limit=" + (limit ?? "null"));

            limit = null;
            cut = CsRunner.WallCutByTransport(
                new List<CsDispatchCapture> { new CsDispatchCapture { State = "deadline_abandoned" } },
                false, ref limit);
            log.Check(cut && limit == "max_scenario_wall_seconds",
                "[CS-35] deadline abandonment attributes scenario wall",
                "cut=" + cut + " limit=" + (limit ?? "null"));

            limit = null;
            cut = CsRunner.WallCutByTransport(
                new List<CsDispatchCapture>
                {
                    new CsDispatchCapture { State = "refused_before_dispatch", RefusalReason = "max_run_wall_seconds" },
                },
                false, ref limit);
            log.Check(cut && limit == "max_run_wall_seconds",
                "[CS-35] wall refusal reason attributes cut",
                "cut=" + cut + " limit=" + (limit ?? "null"));

            limit = null;
            cut = CsRunner.WallCutByTransport(
                new List<CsDispatchCapture>
                {
                    new CsDispatchCapture { State = "failed", ErrorKind = "provider_error" },
                },
                true, ref limit);
            log.Check(!cut && limit == null,
                "[CS-35] provider error without wall capture stays a failure",
                "cut=" + cut + " limit=" + (limit ?? "null"));

            limit = "max_run_wall_seconds";
            cut = CsRunner.WallCutByTransport(
                new List<CsDispatchCapture>
                {
                    new CsDispatchCapture { State = "failed", ErrorKind = "provider_error" },
                },
                true, ref limit);
            log.Check(!cut && limit == "max_run_wall_seconds",
                "[CS-35] loop-set wall limit is preserved without wall capture",
                "cut=" + cut + " limit=" + (limit ?? "null"));
        }
    }
}
