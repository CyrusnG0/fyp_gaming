using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>A detached copy of one observation entity. Only current-observation fields are exposed.</summary>
    public sealed class EntityInspectionResult : InvestigationResult
    {
        public string Kind;
        public bool Found;
        public string Knowledge;      // "current_observation" when Found
        public int Season;
        public CityView City;         // detached copy; null unless a city was found
        public ArmyView Army;         // detached copy; null unless an army was found

        public EntityInspectionResult() : base("inspect_entity") { }

        protected override string BuildPayloadJson()
        {
            string entity = City != null ? City.ToJson() : Army != null ? Army.ToJson() : "null";
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"kind\":").Append(MiniJson.Str(Kind));
            sb.Append(",\"found\":").Append(Found ? "true" : "false");
            sb.Append(",\"knowledge\":").Append(MiniJson.Str(Knowledge));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"entity\":").Append(entity);
            sb.Append('}');
            return sb.ToString();
        }

        // Entity rows have a fixed field skeleton. When a cap is too tight for even a trimmed row,
        // whole trailing string values are shortened; if the skeleton itself cannot fit the result
        // becomes the shared bounded error instead of an oversized response.
        protected override bool TrimForResponse(InvestigationOptions options)
        {
            bool changed = City != null ? ShrinkCity(City) : Army != null && ShrinkArmy(Army);
            if (!changed) return false;
            Truncated = true;
            return true;
        }

        private static bool ShrinkCity(CityView city)
        {
            bool changed = false;
            changed |= Shrink(ref city.Id);
            changed |= Shrink(ref city.Name);
            changed |= Shrink(ref city.OwnerSeatId);
            changed |= Shrink(ref city.GarrisonType);
            return changed;
        }

        private static bool ShrinkArmy(ArmyView army)
        {
            bool changed = false;
            changed |= Shrink(ref army.Id);
            changed |= Shrink(ref army.Name);
            changed |= Shrink(ref army.OwnerSeatId);
            changed |= Shrink(ref army.UnitType);
            return changed;
        }

        internal static bool Shrink(ref string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            value = InvestigationText.Clip(value, value.Length / 2) ?? "";
            return true;
        }
    }

    /// <summary>One detached history hit. Payload/args are copied and bounded.</summary>
    public sealed class HistoryHit
    {
        public int Seq;
        public int Season;
        public string Phase;
        public string ActorSeatId;
        public string ControllerType;
        public string EventType;
        public string Visibility;
        public string SourceActionId;
        public string Text;
        public IReadOnlyDictionary<string, string> Payload = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>());
        public IReadOnlyDictionary<string, string> Args;

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"seq\":").Append(MiniJson.Num(Seq));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"phase\":").Append(MiniJson.Str(Phase));
            sb.Append(",\"actor_seat_id\":").Append(MiniJson.Str(ActorSeatId));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(ControllerType));
            sb.Append(",\"event_type\":").Append(MiniJson.Str(EventType));
            sb.Append(",\"visibility\":").Append(MiniJson.Str(Visibility));
            sb.Append(",\"source_action_id\":").Append(MiniJson.Str(SourceActionId));
            sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append(",\"payload\":").Append(MiniJson.Dict(Payload));
            sb.Append(",\"args\":").Append(Args == null ? "null" : MiniJson.Dict(Args));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Shortens one copied string field; returns false when no text remains to shrink.</summary>
        internal bool ShrinkText()
        {
            bool changed = false;
            changed |= EntityInspectionResult.Shrink(ref Phase);
            changed |= EntityInspectionResult.Shrink(ref ActorSeatId);
            changed |= EntityInspectionResult.Shrink(ref ControllerType);
            changed |= EntityInspectionResult.Shrink(ref EventType);
            changed |= EntityInspectionResult.Shrink(ref Visibility);
            changed |= EntityInspectionResult.Shrink(ref SourceActionId);
            changed |= EntityInspectionResult.Shrink(ref Text);
            changed |= ShrinkPayload(Payload, false);
            changed |= ShrinkPayload(Args, true);
            return changed;
        }

        private bool ShrinkPayload(IReadOnlyDictionary<string, string> source, bool isArgs)
        {
            if (source == null || source.Count == 0) return false;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            bool shrunk = false;
            foreach (KeyValuePair<string, string> pair in source)
            {
                string value = pair.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    string clipped = InvestigationText.Clip(value, value.Length / 2) ?? "";
                    if (clipped != value) shrunk = true;
                    value = clipped;
                }
                copy[pair.Key] = value;
            }
            if (!shrunk) return false;
            var readOnly = new ReadOnlyDictionary<string, string>(copy);
            if (isArgs) Args = readOnly;
            else Payload = readOnly;
            return true;
        }
    }

    /// <summary>A bounded, deterministic search over the seat's already filtered history.</summary>
    public sealed class HistorySearchResult : InvestigationResult
    {
        private List<HistoryHit> _hits = new List<HistoryHit>();

        public string Query;
        public int Offset;
        public int NextOffset = -1;   // -1 = no further page
        internal int TotalMatches;

        public IReadOnlyList<HistoryHit> Hits
        {
            get => _hits.AsReadOnly();
            set => _hits = CopyList(value);
        }

        public HistorySearchResult() : base("search_history") { }

        protected override string BuildPayloadJson()
        {
            var hits = new List<string>(_hits.Count);
            foreach (HistoryHit hit in _hits) hits.Add(hit.ToJson());
            var sb = new StringBuilder(384);
            sb.Append('{');
            sb.Append("\"query\":").Append(MiniJson.Str(Query));
            sb.Append(",\"offset\":").Append(MiniJson.Num(Offset));
            sb.Append(",\"next_offset\":").Append(MiniJson.Num(NextOffset));
            sb.Append(",\"hits\":").Append(ObjectArray(hits));
            sb.Append('}');
            return sb.ToString();
        }

        internal void UpdatePagination()
        {
            int consumed = Offset + _hits.Count;
            NextOffset = consumed < TotalMatches ? consumed : -1;
            ReturnedCount = _hits.Count;
            if (NextOffset >= 0) Truncated = true;
        }

        // Drop whole trailing hits first (the frontier hit keeps the page progressing), then clip
        // that single hit explicitly. Dropping a middle hit or returning an empty page with a
        // non-advancing offset would silently skip history, so neither is allowed.
        protected override bool TrimForResponse(InvestigationOptions options)
        {
            if (_hits.Count > 1)
            {
                _hits.RemoveAt(_hits.Count - 1);
                Truncated = true;
                UpdatePagination();
                return true;
            }
            if (_hits.Count == 1 && _hits[0].ShrinkText())
            {
                Truncated = true;
                UpdatePagination();
                return true;
            }
            return false;
        }
    }

    /// <summary>One detached memory hit. Beliefs are explicitly marked as untrusted model memory.</summary>
    public sealed class MemoryHit
    {
        public string MemoryId;
        public string Type;
        public string Source;
        public int Season;
        public string Content;
        public int Importance;
        public int Confidence;
        public string EpistemicStatus = "untrusted_model_memory";

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"memory_id\":").Append(MiniJson.Str(MemoryId));
            sb.Append(",\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"source\":").Append(MiniJson.Str(Source));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"content\":").Append(MiniJson.Str(Content));
            sb.Append(",\"importance\":").Append(MiniJson.Num(Importance));
            sb.Append(",\"confidence\":").Append(MiniJson.Num(Confidence));
            sb.Append(",\"epistemic_status\":").Append(MiniJson.Str(EpistemicStatus));
            sb.Append('}');
            return sb.ToString();
        }

        internal bool ShrinkText()
        {
            bool changed = false;
            changed |= EntityInspectionResult.Shrink(ref MemoryId);
            changed |= EntityInspectionResult.Shrink(ref Type);
            changed |= EntityInspectionResult.Shrink(ref Source);
            changed |= EntityInspectionResult.Shrink(ref Content);
            return changed;
        }
    }

    /// <summary>A bounded, deterministic search over injected own-seat memory records.</summary>
    public sealed class MemoryRecallResult : InvestigationResult
    {
        private List<MemoryHit> _memories = new List<MemoryHit>();

        public string Query;

        public IReadOnlyList<MemoryHit> Memories
        {
            get => _memories.AsReadOnly();
            set => _memories = CopyList(value);
        }

        public MemoryRecallResult() : base("recall_memory") { }

        protected override string BuildPayloadJson()
        {
            var memories = new List<string>(_memories.Count);
            foreach (MemoryHit memory in _memories) memories.Add(memory.ToJson());
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"query\":").Append(MiniJson.Str(Query));
            sb.Append(",\"memories\":").Append(ObjectArray(memories));
            sb.Append('}');
            return sb.ToString();
        }

        protected override bool TrimForResponse(InvestigationOptions options)
        {
            if (_memories.Count > 1)
            {
                _memories.RemoveAt(_memories.Count - 1);
                Truncated = true;
                ReturnedCount = _memories.Count;
                return true;
            }
            if (_memories.Count == 1 && _memories[0].ShrinkText())
            {
                Truncated = true;
                return true;
            }
            return false;
        }
    }
}
