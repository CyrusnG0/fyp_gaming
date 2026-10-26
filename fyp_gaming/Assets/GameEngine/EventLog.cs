using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// One append-only event record (CONTROLLER_PROTOCOL.md §5).
    ///
    /// The canonical record is engine-owned (LS-003): its fields and its payload/args dictionaries are
    /// writable only inside this assembly. Readers — including the UI adapter, which lives in a separate
    /// assembly — get the values but no way to alter history. Per-seat observations carry a detached
    /// <see cref="EventView"/> instead.
    /// </summary>
    public sealed class EventRecord
    {
        public int Seq { get; internal set; }
        public string GameId { get; internal set; }
        public int Season { get; internal set; }
        public string Phase { get; internal set; }
        public string ActorSeatId { get; internal set; }
        public string ControllerType { get; internal set; }
        public string EventType { get; internal set; }
        public string Visibility { get; internal set; }   // "public" | "owner_and_observers" | "owner_only"
        public IReadOnlyDictionary<string, string> Payload { get; internal set; }
        public string SourceActionId { get; internal set; }
        public IReadOnlyDictionary<string, string> Args { get; internal set; }   // schema v2 extras; null when absent
        public string Text { get; internal set; }                                // schema v2 free text; null when absent

        // ---- event-time privacy (LS-002, ADR-025) -------------------------------------------------
        // Not serialized. The canonical record always keeps the full payload; these fields decide who
        // may receive it and what a non-party recipient sees.
        internal List<string> Recipients;                  // seats entitled at emit time; null = not frozen (unreadable)
        internal List<string> FullSeatIds;                 // actor + parties + entitled witnesses: canonical
        internal Dictionary<string, string> LimitedPayload; // safe projection for everyone else; null = canonical

        internal bool HasFullPayload(string seatId)
            => FullSeatIds != null && FullSeatIds.Contains(seatId);

        /// <summary>
        /// The detached copy of this record a seat's observation carries. The actor, named parties and
        /// entitled subject witnesses get the canonical payload; every other recipient gets the frozen
        /// limited projection, with command extras and free text removed. Payload and args are copied, so
        /// the returned view can never reach this canonical record.
        /// </summary>
        internal EventView ForSeat(string seatId)
        {
            bool limited = LimitedPayload != null && !HasFullPayload(seatId);
            var payload = new Dictionary<string, string>(limited ? LimitedPayload : Payload);
            var args = limited || Args == null
                ? null
                : new Dictionary<string, string>(Args);
            return new EventView(this, payload, args, limited ? null : Text);
        }

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"seq\":").Append(MiniJson.Num(Seq));
            sb.Append(",\"game_id\":").Append(MiniJson.Str(GameId));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"phase\":").Append(MiniJson.Str(Phase));
            sb.Append(",\"actor_seat_id\":").Append(MiniJson.Str(ActorSeatId));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(ControllerType));
            sb.Append(",\"event_type\":").Append(MiniJson.Str(EventType));
            sb.Append(",\"visibility\":").Append(MiniJson.Str(Visibility));
            sb.Append(",\"payload\":").Append(MiniJson.Dict(Payload));
            sb.Append(",\"source_action_id\":").Append(MiniJson.Str(SourceActionId));
            if (Args != null) sb.Append(",\"args\":").Append(MiniJson.Dict(Args));
            if (Text != null) sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Append-only event log — the canonical memory of the game. Powers replay,
    /// the historian chronicle, and LLM handover catch-up. Records are never mutated
    /// or removed once emitted, and <see cref="Records"/> exposes them read-only: callers can read
    /// history but cannot modify it, its payloads or its args.
    /// </summary>
    public sealed class EventLog
    {
        private readonly List<EventRecord> records = new List<EventRecord>();
        private readonly ReadOnlyCollection<EventRecord> recordsView;
        private int nextSeq = 1;

        public EventLog()
        {
            recordsView = records.AsReadOnly();
        }

        /// <summary>
        /// History as a read-only collection: callers can read every record but can neither add/remove
        /// entries nor write a record's fields or payload outside this assembly.
        /// </summary>
        public IReadOnlyList<EventRecord> Records => recordsView;

        /// <summary>
        /// Appends a record, copying the caller's payload/args so later changes to the caller's own
        /// dictionaries cannot alter history or its JSONL. <see cref="GameEngine"/> freezes the record's
        /// audience immediately after; a record emitted directly without <see cref="EventPrivacy.Freeze"/>
        /// has no recipients and is unreadable by every seat (fail closed).
        /// </summary>
        internal EventRecord Emit(string gameId, int season, string phase,
            string actorSeatId, string controllerType, string eventType,
            string visibility, Dictionary<string, string> payload, string sourceActionId,
            Dictionary<string, string> args = null, string text = null)
        {
            var r = new EventRecord
            {
                Seq = nextSeq++,
                GameId = gameId,
                Season = season,
                Phase = phase,
                ActorSeatId = actorSeatId,
                ControllerType = controllerType,
                EventType = eventType,
                Visibility = visibility,
                Payload = new ReadOnlyDictionary<string, string>(payload == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(payload)),
                SourceActionId = sourceActionId,
                Args = args == null
                    ? null
                    : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(args)),
                Text = text,
            };
            records.Add(r);
            return r;
        }

        public string ToJsonLines()
        {
            var sb = new StringBuilder(records.Count * 256);
            foreach (var r in records) sb.Append(r.ToJson()).Append('\n');
            return sb.ToString();
        }
    }
}
