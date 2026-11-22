using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>One FAKE reply the director handed out, kept as evidence of which seat received what.</summary>
    public sealed class FakeRehearsalDispatch
    {
        public string SeatId;
        public int Season;
        public int Index;
        public string Reply;
        public string OrderType;
    }

    /// <summary>
    /// Hands out the scripted FAKE replies of a spectator run. The transport queue is consumed strictly in
    /// order and a turn can end before its script does (action, request or invalid cap, a rejected order, an
    /// early end_turn), so a reply must never be written for a seat in advance: it would be read by whichever
    /// seat acts next. Instead every queued slot is a pool entry. When the slot is dispatched the director
    /// asks who is acting, builds that seat's script on its first request of the turn, and returns the next
    /// step of it. A leftover slot is therefore consumed by the next seat as that seat's own step, or never
    /// consumed, and never carries another seat's order.
    /// </summary>
    public sealed class FakeRehearsalDirector
    {
        /// <summary>Upper bound on the orders one turn scripts (garrison 3, treaty 1, research 1).</summary>
        public const int MaxScriptedOrders = 5;

        private readonly GameEngine _engine;
        private readonly Func<string> _actingSeat;
        private readonly FakeRehearsalOptions _options;
        private readonly Func<string, MovementView> _movement;
        private readonly int _openingSeason;
        private readonly List<FakeRehearsalDispatch> _dispatched = new List<FakeRehearsalDispatch>();
        private string _turnKey;
        private Queue<Func<string>> _steps;
        private int _turnIndex;

        /// <param name="actingSeat">Returns the seat whose turn is open (for the spectator, its ActiveSeatId).</param>
        /// <param name="movement">Builds the seat's march options when a turn begins; may be null.</param>
        /// <param name="openingSeason">Season in which the treaty exchange is scripted; 0 for never.</param>
        public FakeRehearsalDirector(GameEngine engine, Func<string> actingSeat, FakeRehearsalOptions options,
            Func<string, MovementView> movement, int openingSeason)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _actingSeat = actingSeat ?? throw new ArgumentNullException(nameof(actingSeat));
            _options = options ?? new FakeRehearsalOptions();
            _movement = movement;
            _openingSeason = openingSeason;
        }

        public IReadOnlyList<FakeRehearsalDispatch> Dispatched => _dispatched;

        /// <summary>Slots one seat turn needs: its orders plus the closing end_turn.</summary>
        public int SlotsPerTurn => _options.CanPlay ? Math.Min(_options.OrderCap, MaxScriptedOrders) + 1 : 0;

        /// <summary>Queues one seat turn's worth of slots. They are interchangeable between seats.</summary>
        public void EnqueueSeatTurn(FakeLlmTransport fake, int delayMs = 0)
        {
            if (fake == null) throw new ArgumentNullException(nameof(fake));
            for (int i = 0; i < SlotsPerTurn; i++)
                fake.EnqueueDynamic(_ => Next(), delayMs, 120, 20);
        }

        /// <summary>The next reply for the seat that is acting right now.</summary>
        public string Next()
        {
            string seat = _actingSeat();
            if (string.IsNullOrEmpty(seat)) return Record(null, 0, EndTurn());
            int season = _engine.State.Season;
            string key = seat + "@" + season;
            if (key != _turnKey)
            {
                _turnKey = key;
                _steps = BeginTurn(seat, season);
                _turnIndex = 0;
            }
            string reply = _steps.Count > 0 ? _steps.Dequeue()() : EndTurn();
            return Record(seat, season, reply);
        }

        private string Record(string seat, int season, string reply)
        {
            string type = TypeOf(reply);
            _dispatched.Add(new FakeRehearsalDispatch
            {
                SeatId = seat, Season = season, Index = _turnIndex++, Reply = reply, OrderType = type,
            });
            return reply;
        }

        private Queue<Func<string>> BeginTurn(string seat, int season)
        {
            var steps = new Queue<Func<string>>();
            int cap = _options.OrderCap;
            Observation observation = _engine.Observe(seat);
            IReadOnlyList<string> order = SeatRotation.PlayOrder(_engine.State);
            bool opening = _openingSeason > 0 && season == _openingSeason;
            int used = 0;

            if (_options.Garrison)
            {
                var garrison = new FakeTurnPlan.SpectatorGarrisonSteps(_engine, seat,
                    () => _movement == null ? null : _movement(seat));
                for (; used < Math.Min(FakeTurnPlan.GarrisonStepCount, cap); used++) steps.Enqueue(garrison.NextReply);
                int pointsLeft = (observation == null ? 0 : observation.CommandPoints)
                    - FakeTurnPlan.GarrisonStepsCommandPoints;
                foreach (string line in FakeRehearsalScript.Extras(seat, order, opening, _options, pointsLeft,
                             cap - used))
                {
                    string fixedLine = line;
                    steps.Enqueue(() => fixedLine);
                    used++;
                }
            }
            else
            {
                IReadOnlyList<string> plan = FakeRehearsalScript.Build(_movement == null ? null : _movement(seat),
                    observation, seat, order, opening, _options);
                for (int i = 0; i < plan.Count - 1 && used < cap; i++, used++)   // the last line is end_turn
                {
                    string fixedLine = plan[i];
                    steps.Enqueue(() => fixedLine);
                }
            }

            steps.Enqueue(EndTurn);
            return steps;
        }

        private static string EndTurn()
            => "{\"type\":\"end_turn\",\"summary\":\"" + MiniJson.Escape(FakeTurnPlan.Label + ": end turn") + "\"}";

        private static string TypeOf(string reply)
        {
            const string marker = "\"type\":\"";
            int at = reply == null ? -1 : reply.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return null;
            at += marker.Length;
            int end = reply.IndexOf('"', at);
            return end < 0 ? null : reply.Substring(at, end - at);
        }
    }
}
