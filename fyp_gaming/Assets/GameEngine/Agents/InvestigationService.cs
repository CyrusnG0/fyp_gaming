using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Read-only investigation foundation (LS-018): vocabulary lookup, current-observation entity
    /// inspection, own filtered history search and own-seat memory recall.
    ///
    /// The service is constructed with an explicit host-supplied match/player context and a detached
    /// <see cref="Observation"/>; it never reads canonical <see cref="GameState"/>, the canonical
    /// <see cref="EventLog"/>, the filesystem, the network or the shell. Every operation returns a
    /// typed result, never mutates the observation or the injected memories, and fails closed when the
    /// observation does not match the expected context. Model-supplied ids are never used as authority.
    /// </summary>
    public sealed partial class InvestigationService
    {
        private static readonly IReadOnlyList<LlmMemory> NoMemories =
            new List<LlmMemory>().AsReadOnly();

        private readonly string _expectedGameId;
        private readonly string _expectedSeatId;
        private readonly Observation _observation;
        private readonly IReadOnlyList<LlmMemory> _memories;
        private readonly InvestigationOptions _options;
        private readonly string _contextError;

        public string ExpectedGameId => _expectedGameId;
        public string ExpectedSeatId => _expectedSeatId;
        /// <summary>A detached copy of the bounded configuration this service uses.</summary>
        public InvestigationOptions Options => _options.Copy();
        public bool ContextValid => _contextError == null;
        public string ContextErrorMessage => _contextError;

        public InvestigationService(string expectedGameId, string expectedSeatId,
            Observation observation, IReadOnlyList<LlmMemory> memories,
            InvestigationOptions options = null)
        {
            _expectedGameId = expectedGameId;
            _expectedSeatId = expectedSeatId;
            _observation = observation;
            _memories = memories ?? NoMemories;
            _options = options == null ? InvestigationOptions.Defaults() : options.Copy();
            // Reject an impossible output cap explicitly; never emit a response above the caller's cap.
            _options.Validate();
            _contextError = ValidateContext(expectedGameId, expectedSeatId, observation);
        }

        /// <summary>
        /// Discovery metadata for the four operations. It needs no match context and reveals no
        /// game state; callers can use it without constructing a service.
        /// </summary>
        public static InvestigationToolCatalogueResult Catalogue(InvestigationOptions options = null)
        {
            options = (options ?? InvestigationOptions.Defaults()).Copy();
            options.Validate();
            var result = new InvestigationToolCatalogueResult();
            var tools = new List<InvestigationToolDescriptor>();
            foreach (InvestigationToolDescriptor tool in InvestigationCatalogue.Tools)
            {
                if (tools.Count >= options.MaxToolCatalogueEntries) break;
                tools.Add(new InvestigationToolDescriptor
                {
                    Name = tool.Name,
                    Title = tool.Title,
                    Summary = tool.Summary,
                    ReadOnly = tool.ReadOnly,
                    MutatesGameState = tool.MutatesGameState,
                });
            }
            result.Tools = tools.AsReadOnly();
            result.Succeed(tools.Count);
            result.ApplyResponseBound(options);
            return result;
        }

        /// <summary>Look up an English implemented rule. Null/empty/"index" returns the bounded index.</summary>
        public ManualLookupResult ManualLookup(string topic)
        {
            var result = new ManualLookupResult();
            try
            {
                if (_contextError != null)
                {
                    result.Fail("context_mismatch", ContextMismatchMessage);
                    return result;
                }
                result = InvestigationManual.Lookup(topic, _options, _observation.Balance);
                return result;
            }
            finally
            {
                // Every path, including errors and the index, is bounded after JSON escaping.
                result.ApplyResponseBound(_options);
            }
        }

        /// <summary>
        /// Inspect a current-observation city or army. A hidden id and a nonexistent id return the
        /// identical generic failure (the requested id is never echoed).
        /// </summary>
        public EntityInspectionResult InspectEntity(string kind, string id)
        {
            var result = new EntityInspectionResult();
            try
            {
                if (_contextError != null)
                {
                    result.Fail("context_mismatch", ContextMismatchMessage);
                    return result;
                }

                string normalizedKind = InvestigationText.Normalize(kind);
                if (normalizedKind != "city" && normalizedKind != "army")
                {
                    result.Fail("unsupported_kind", "Supported inspection kinds are 'city' and 'army'.");
                    return result;
                }
                if (string.IsNullOrEmpty(id))
                {
                    result.Fail("invalid_request", "An entity id is required.");
                    return result;
                }
                if (id.Length > _options.MaxEntityIdLength)
                {
                    result.Fail("invalid_request", "The entity id exceeds the configured length bound.");
                    return result;
                }

                result.Kind = normalizedKind;
                result.Season = _observation.Season;

                if (normalizedKind == "city")
                {
                    CityView city = _observation.FindCity(id);
                    if (city == null) return EntityNotFound(result);
                    result.City = CopyCity(city, _options);
                }
                else
                {
                    ArmyView army = _observation.FindArmy(id);
                    if (army == null) return EntityNotFound(result);
                    result.Army = CopyArmy(army, _options);
                }

                result.Found = true;
                result.Knowledge = "current_observation";
                result.Succeed(1);
                return result;
            }
            finally
            {
                result.ApplyResponseBound(_options);
            }
        }

        private static EntityInspectionResult EntityNotFound(EntityInspectionResult result)
        {
            // Same code, message, count, season and payload for hidden and nonexistent ids. The
            // caller's id is deliberately not echoed, so the two probes are indistinguishable.
            result.Found = false;
            result.Knowledge = null;
            result.Fail("entity_not_found", "Entity is not available in the current observation.");
            return result;
        }

        private const string ContextMismatchMessage =
            "Observation does not match the expected match/player context.";

        private static string ValidateContext(string expectedGameId, string expectedSeatId,
            Observation observation)
        {
            if (string.IsNullOrEmpty(expectedGameId) || string.IsNullOrEmpty(expectedSeatId))
                return "An explicit expected match and player context is required.";
            if (observation == null) return "A detached observation is required.";
            if (!string.Equals(observation.GameId, expectedGameId, StringComparison.Ordinal)
                || !string.Equals(observation.SeatId, expectedSeatId, StringComparison.Ordinal))
                return ContextMismatchMessage;
            return null;
        }

        private static CityView CopyCity(CityView city, InvestigationOptions options)
        {
            return new CityView
            {
                Id = InvestigationText.Clip(city.Id, options.EffectiveTextChars),
                Name = InvestigationText.Clip(city.Name, options.EffectiveTextChars),
                X = city.X,
                Z = city.Z,
                OwnerSeatId = InvestigationText.Clip(city.OwnerSeatId, options.EffectiveTextChars),
                Population = city.Population,
                Food = city.Food,
                Gold = city.Gold,
                Garrison = city.Garrison,
                GarrisonType = InvestigationText.Clip(city.GarrisonType, options.EffectiveTextChars),
                ReclaimedThisSeason = city.ReclaimedThisSeason,
                Morale = city.Morale,
                Defense = city.Defense,
                Training = city.Training,
                ReclaimPct = city.ReclaimPct,
            };
        }

        private static ArmyView CopyArmy(ArmyView army, InvestigationOptions options)
        {
            return new ArmyView
            {
                Id = InvestigationText.Clip(army.Id, options.EffectiveTextChars),
                Name = InvestigationText.Clip(army.Name, options.EffectiveTextChars),
                OwnerSeatId = InvestigationText.Clip(army.OwnerSeatId, options.EffectiveTextChars),
                Troops = army.Troops,
                UnitType = InvestigationText.Clip(army.UnitType, options.EffectiveTextChars),
                X = army.X,
                Z = army.Z,
                MarchedThisSeason = army.MarchedThisSeason,
                MarchCountThisSeason = army.MarchCountThisSeason,
                Morale = army.Morale,
            };
        }

        private static IReadOnlyDictionary<string, string> CopyPayload(
            IReadOnlyDictionary<string, string> source, InvestigationOptions options)
        {
            if (source == null) return null;
            var keys = new List<string>(source.Count);
            foreach (KeyValuePair<string, string> pair in source)
                if (pair.Key != null) keys.Add(pair.Key);
            keys.Sort(StringComparer.Ordinal);

            var copied = new Dictionary<string, string>(StringComparer.Ordinal);
            int limit = options.EffectivePayloadEntries;
            foreach (string key in keys)
            {
                if (copied.Count >= limit) break;
                copied[InvestigationText.Clip(key, options.MaxPayloadKeyChars)] =
                    InvestigationText.Clip(source[key], options.MaxPayloadValueChars);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(copied);
        }
    }
}
