namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Library-level gate for a live run. A live manifest (or a live provider override) must be
    /// paired with <see cref="AllowLive"/>; the CLI flag is <c>--allow-live</c>.
    /// <see cref="ProviderOverride"/> is a TEST-ONLY seam so selection and gating can be exercised
    /// without reading the environment or touching the network.
    /// </summary>
    public sealed class CsRunOptions
    {
        public bool AllowLive { get; set; }

        internal IScenarioProvider ProviderOverride { get; set; }
    }
}
