using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Bounds and defaults for the LS-020 strict investigation request contract.
    ///
    /// The parser deliberately reuses <see cref="InvestigationOptions"/> for field, limit and
    /// pagination bounds so a request can never be admitted with an argument the LS-018 service
    /// would reject. Only the raw-response cap is new here; limits remain deliberately small and
    /// are not a paid-model or investigation-call budget. Budget admission stays with the future
    /// host and is never performed by the parser or dispatcher.
    /// </summary>
    public sealed class InvestigationRequestOptions
    {
        /// <summary>Wire-contract version carried by parsed requests and the handoff note.</summary>
        public const string ContractVersion = "ls020-v1";

        /// <summary>
        /// Hard cap on raw response characters before JSON parsing. One request is expected to be
        /// a few dozen characters; this only leaves room for whitespace, escapes and a long query.
        /// </summary>
        public const int DefaultMaxInputChars = 4096;

        /// <summary>A cap smaller than this cannot carry a bounded request-error message.</summary>
        public const int MinimumMaxInputChars = 256;

        public int MaxInputChars = DefaultMaxInputChars;

        /// <summary>
        /// Bounds mirrored from the investigation service (topic/query/entity lengths, result
        /// limits and pagination). <see cref="InvestigationOptions.Defaults"/> is the default.
        /// </summary>
        public InvestigationOptions ServiceOptions = InvestigationOptions.Defaults();

        /// <summary>A fresh configuration with the conservative, documented defaults.</summary>
        public static InvestigationRequestOptions Defaults() => new InvestigationRequestOptions();

        /// <summary>An independent snapshot; later changes to the source never affect the copy.</summary>
        public InvestigationRequestOptions Copy()
        {
            return new InvestigationRequestOptions
            {
                MaxInputChars = MaxInputChars,
                ServiceOptions = ServiceOptions == null ? null : ServiceOptions.Copy(),
            };
        }

        /// <summary>
        /// Rejects an impossible configuration explicitly instead of quietly enlarging it. A
        /// caller that asks for an unusable input cap gets an exception, not a fragile parser.
        /// </summary>
        public void Validate()
        {
            if (MaxInputChars < MinimumMaxInputChars)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxInputChars), MaxInputChars,
                    "MaxInputChars must be at least " + MinimumMaxInputChars
                    + " so a bounded request error can fit.");
            }
            if (ServiceOptions == null)
                throw new ArgumentNullException(nameof(ServiceOptions));
            ServiceOptions.Validate();
        }
    }
}
