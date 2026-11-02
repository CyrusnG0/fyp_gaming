using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Bounds, versions and conservative defaults for the read-only investigation tools (LS-018).
    ///
    /// Every finite limit used by <see cref="InvestigationService"/> lives here so callers can lower
    /// (or, deliberately, raise) one budget without editing tool code. The defaults are intentionally
    /// small: this foundation is not wired to live model runs and must never imply a paid budget.
    /// Instances are plain mutable DTOs; <see cref="Copy"/> returns an independent snapshot.
    /// </summary>
    public sealed class InvestigationOptions
    {
        /// <summary>Wire-schema version carried by every result envelope.</summary>
        public const string ResponseVersion = "ls018-v1";

        /// <summary>Version label for the English rules/topic material served by manual_lookup.</summary>
        public const string RulesVersion = "luan-shi-manual-2026-11-02";

        // ---- request bounds -------------------------------------------------
        public int MaxQueryLength = 200;          // history/memory query characters
        public int MaxTopicLength = 64;           // manual_lookup topic characters
        public int MaxEntityIdLength = 64;        // inspect_entity id characters

        // ---- result bounds --------------------------------------------------
        public int DefaultHistoryLimit = 10;
        public int MaxHistoryLimit = 50;
        public int DefaultMemoryLimit = 10;
        public int MaxMemoryLimit = 50;
        public int MaxPaginationOffset = 1000;

        // ---- output bounds --------------------------------------------------
        // The serialized envelope, measured AFTER JSON escaping. It is a hard cap: results are
        // structurally trimmed or replaced by a bounded explicit error, never allowed to exceed it.
        public int MaxResponseChars = 16000;
        public int MaxTextChars = 600;            // one event/memory/name text field
        public int MaxPayloadEntries = 16;        // event payload/args entries returned per hit
        public int MaxPayloadKeyChars = 64;
        public int MaxPayloadValueChars = 400;
        public int MaxTopicIndexEntries = 32;     // the catalogue is far smaller than this
        public int MaxToolCatalogueEntries = 8;   // the four operations plus headroom

        public InvestigationOptions Copy()
        {
            return new InvestigationOptions
            {
                MaxQueryLength = MaxQueryLength,
                MaxTopicLength = MaxTopicLength,
                MaxEntityIdLength = MaxEntityIdLength,
                DefaultHistoryLimit = DefaultHistoryLimit,
                MaxHistoryLimit = MaxHistoryLimit,
                DefaultMemoryLimit = DefaultMemoryLimit,
                MaxMemoryLimit = MaxMemoryLimit,
                MaxPaginationOffset = MaxPaginationOffset,
                MaxResponseChars = MaxResponseChars,
                MaxTextChars = MaxTextChars,
                MaxPayloadEntries = MaxPayloadEntries,
                MaxPayloadKeyChars = MaxPayloadKeyChars,
                MaxPayloadValueChars = MaxPayloadValueChars,
                MaxTopicIndexEntries = MaxTopicIndexEntries,
                MaxToolCatalogueEntries = MaxToolCatalogueEntries,
            };
        }

        /// <summary>A fresh instance with the documented conservative defaults.</summary>
        public static InvestigationOptions Defaults() => new InvestigationOptions();

        /// <summary>
        /// The smallest response cap that can carry the bounded error envelope used when a result
        /// cannot be trimmed further. A smaller cap is rejected rather than silently expanded.
        /// </summary>
        public const int MinimumResponseChars = 256;

        /// <summary>
        /// Rejects a configuration that cannot honour the serialized response bound. Callers see an
        /// explicit <see cref="ArgumentOutOfRangeException"/> instead of a quietly enlarged cap.
        /// </summary>
        public void Validate()
        {
            if (MaxResponseChars < MinimumResponseChars)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxResponseChars), MaxResponseChars,
                    "MaxResponseChars must be at least " + MinimumResponseChars
                    + " so a bounded error envelope can fit.");
            }
        }

        // Effective values protect the service from a nonsensical caller configuration such as a
        // negative budget. They never change the defaults on disk and are not paid-model budgets.
        // The response cap is deliberately NOT raised here: Validate() rejects impossible caps.
        internal int EffectiveResponseChars => MaxResponseChars;
        internal int EffectiveTextChars
        {
            get
            {
                int cap = MaxTextChars < 0 ? 0 : MaxTextChars;
                return cap > EffectiveResponseChars ? EffectiveResponseChars : cap;
            }
        }
        internal int EffectiveHistoryLimit => MaxHistoryLimit < 1 ? 1 : MaxHistoryLimit;
        internal int EffectiveMemoryLimit => MaxMemoryLimit < 1 ? 1 : MaxMemoryLimit;
        internal int EffectivePayloadEntries => MaxPayloadEntries < 0 ? 0 : MaxPayloadEntries;
    }
}
