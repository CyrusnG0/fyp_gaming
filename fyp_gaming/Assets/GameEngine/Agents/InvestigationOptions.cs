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
        public int MaxResponseChars = 16000;      // serialized result envelope, before any trimming
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

        // Effective values protect the service from a nonsensical caller configuration such as a
        // negative budget. They never change the defaults on disk and are not paid-model budgets.
        internal int EffectiveResponseChars => MaxResponseChars < 256 ? 256 : MaxResponseChars;
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
