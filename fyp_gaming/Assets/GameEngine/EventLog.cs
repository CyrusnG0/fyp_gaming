using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>One append-only event record (CONTROLLER_PROTOCOL.md §5).</summary>
    public sealed class EventRecord
    {
        public int Seq;
        public string GameId;
        public int Season;
        public string Phase;
        public string ActorSeatId;
        public string ControllerType;
        public string EventType;
        public string Visibility;                 // "public" | "owner_and_observers" | "owner_only"
        public Dictionary<string, string> Payload = new Dictionary<string, string>();
        public string SourceActionId;

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
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Append-only event log — the canonical memory of the game. Powers replay,
    /// the 史官 chronicle, and LLM handover catch-up. Records are never mutated
    /// or removed once emitted.
    /// </summary>
    public sealed class EventLog
    {
        private readonly List<EventRecord> records = new List<EventRecord>();
        private int nextSeq = 1;

        public IReadOnlyList<EventRecord> Records => records;

        public EventRecord Emit(string gameId, int season, string phase,
            string actorSeatId, string controllerType, string eventType,
            string visibility, Dictionary<string, string> payload, string sourceActionId)
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
                Payload = payload ?? new Dictionary<string, string>(),
                SourceActionId = sourceActionId,
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
