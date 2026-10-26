using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// A detached, read-only snapshot of one event as a seat's observation carries it (LS-003).
    ///
    /// The canonical <see cref="EventRecord"/> keeps the full payload, its audience and the frozen
    /// projection rules; a seat receives this copy instead. Payload and args are copied dictionaries
    /// exposed only through <see cref="IReadOnlyDictionary{TKey,TValue}"/>, so mutating a retained
    /// observation cannot reach the canonical log, another seat's snapshot or a previously emitted view.
    /// </summary>
    public sealed class EventView
    {
        public readonly int Seq;
        public readonly string GameId;
        public readonly int Season;
        public readonly string Phase;
        public readonly string ActorSeatId;
        public readonly string ControllerType;
        public readonly string EventType;
        public readonly string Visibility;
        public readonly IReadOnlyDictionary<string, string> Payload;
        public readonly string SourceActionId;
        public readonly IReadOnlyDictionary<string, string> Args;   // null when the action carried none
        public readonly string Text;                                // null when the action carried none

        internal EventView(EventRecord rec, Dictionary<string, string> payload,
            Dictionary<string, string> args, string text)
        {
            Seq = rec.Seq;
            GameId = rec.GameId;
            Season = rec.Season;
            Phase = rec.Phase;
            ActorSeatId = rec.ActorSeatId;
            ControllerType = rec.ControllerType;
            EventType = rec.EventType;
            Visibility = rec.Visibility;
            Payload = new ReadOnlyDictionary<string, string>(
                payload ?? new Dictionary<string, string>());
            SourceActionId = rec.SourceActionId;
            Args = args == null ? null : new ReadOnlyDictionary<string, string>(args);
            Text = text;
        }

        /// <summary>Canonical record shape — byte-identical to <see cref="EventRecord.ToJson"/>.</summary>
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
}
