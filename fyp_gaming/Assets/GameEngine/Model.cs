using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Controller kinds from CONTROLLER_PROTOCOL.md §1.</summary>
    public static class ControllerType
    {
        public const string Human = "human";
        public const string Llm = "llm";
        public const string ScriptedAi = "scripted_ai";
    }

    public sealed class FactionState
    {
        public string SeatId;          // "seat-0", "seat-1", ...
        public string Name;            // 魏 / 蜀 / ...
        public string Controller;      // ControllerType.*
        public int CommandPoints;
        public int Prestige = BalanceConfig.PrestigeStart;    // 威望, range PrestigeMin..PrestigeMax

        public bool IsHuman => Controller == ControllerType.Human;

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(Controller));
            sb.Append(",\"command_points\":").Append(MiniJson.Num(CommandPoints));
            sb.Append(",\"prestige\":").Append(MiniJson.Num(Prestige));
            sb.Append('}');
            return sb.ToString();
        }
    }

    public sealed class CityState
    {
        public string Id;              // "city-wei", ...
        public string Name;            // 魏都 ...
        public int X, Z;               // hex coords
        public string OwnerSeatId;     // null/"" = neutral
        public int Population;
        public int Food;
        public int Gold;
        public int Garrison;
        public bool ReclaimedThisSeason;
        public int Morale = BalanceConfig.MoraleStart;          // 民心, range MoraleMin..MoraleMax
        public int Defense = BalanceConfig.DefenseStart;        // 城防, range 0..DefenseMax
        public int Training = BalanceConfig.TrainingStart;      // 練兵, range 0..TrainingMax
        public int ReclaimPct = BalanceConfig.ReclaimPctStart;  // 開墾, range 0..ReclaimPctCap, scales food yield

        public bool IsNeutral => string.IsNullOrEmpty(OwnerSeatId);

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"population\":").Append(MiniJson.Num(Population));
            sb.Append(",\"food\":").Append(MiniJson.Num(Food));
            sb.Append(",\"gold\":").Append(MiniJson.Num(Gold));
            sb.Append(",\"garrison\":").Append(MiniJson.Num(Garrison));
            sb.Append(",\"morale\":").Append(MiniJson.Num(Morale));
            sb.Append(",\"defense\":").Append(MiniJson.Num(Defense));
            sb.Append(",\"training\":").Append(MiniJson.Num(Training));
            sb.Append(",\"reclaim_pct\":").Append(MiniJson.Num(ReclaimPct));
            sb.Append('}');
            return sb.ToString();
        }
    }

    public sealed class ArmyState
    {
        public string Id;              // "army-wei-1", ...
        public string Name;
        public string OwnerSeatId;
        public int Troops;
        public int X, Z;
        public bool MarchedThisSeason;
        public int Morale = BalanceConfig.ArmyMoraleStart;   // 士氣, range MoraleMin..MoraleMax; 0 = 潰散

        public string ToJson()
        {
            var sb = new StringBuilder(160);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"troops\":").Append(MiniJson.Num(Troops));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"morale\":").Append(MiniJson.Num(Morale));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Engine-owned copy of the strategic map. The Unity adapter builds this once from
    /// the TBTK grid; the engine never touches Unity types. Neighbors are hex-adjacency
    /// (precomputed by the adapter via cube-coordinate distance).
    /// </summary>
    public sealed class MapData
    {
        public int Width, Height;
        public bool[] Walkable;
        public int[] Cost;
        public int[][] Neighbors;

        public int Index(int x, int z) => z * Width + x;
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;
        public bool IsWalkable(int x, int z) => InBounds(x, z) && Walkable[Index(x, z)];

        /// <summary>Same hex or hex-adjacent: the range-1 check for 野戰 / 攻城 (ACTIONS.md §4).</summary>
        public bool AreAdjacent(int x1, int z1, int x2, int z2)
        {
            if (x1 == x2 && z1 == z2) return true;
            if (!InBounds(x1, z1) || !InBounds(x2, z2)) return false;
            var neighbors = Neighbors[Index(x1, z1)];
            if (neighbors == null) return false;
            int target = Index(x2, z2);
            foreach (int n in neighbors) if (n == target) return true;
            return false;
        }

        /// <summary>
        /// Hex steps between two nodes over the adjacency graph, or -1 when farther than maxSteps
        /// (used by fog of war). Sight is a radius, not line-of-sight, so terrain never blocks it —
        /// the search follows the adjacency graph without consulting Walkable.
        /// </summary>
        public int HexDistance(int fromX, int fromZ, int toX, int toZ, int maxSteps)
        {
            if (!InBounds(fromX, fromZ) || !InBounds(toX, toZ)) return -1;
            int start = Index(fromX, fromZ), goal = Index(toX, toZ);
            if (start == goal) return 0;

            var visited = new HashSet<int> { start };
            var frontier = new List<int> { start };
            for (int step = 1; step <= maxSteps && frontier.Count > 0; step++)
            {
                var next = new List<int>();
                foreach (int node in frontier)
                {
                    var neighbors = Neighbors[node];
                    if (neighbors == null) continue;
                    foreach (int n in neighbors)
                    {
                        if (!visited.Add(n)) continue;
                        if (n == goal) return step;
                        next.Add(n);
                    }
                }
                frontier = next;
            }
            return -1;
        }

        /// <summary>Dijkstra over hex costs. Returns total cost, or -1 if unreachable within maxCost.</summary>
        public int PathCost(int fromX, int fromZ, int toX, int toZ, int maxCost)
        {
            var path = FindPath(fromX, fromZ, toX, toZ, maxCost);
            if (path == null || path.Count == 0) return -1;
            int total = 0;
            for (int i = 1; i < path.Count; i++) total += Cost[path[i]];
            return total;
        }

        /// <summary>Dijkstra; returns node indices from start to destination inclusive, or null.</summary>
        public List<int> FindPath(int fromX, int fromZ, int toX, int toZ, int maxCost)
        {
            if (!InBounds(fromX, fromZ) || !InBounds(toX, toZ) || !IsWalkable(toX, toZ)) return null;
            int start = Index(fromX, fromZ), goal = Index(toX, toZ);
            if (start == goal) return new List<int> { start };

            int n = Width * Height;
            var dist = new int[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
            dist[start] = 0;

            for (;;)
            {
                int cur = -1, best = int.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; cur = i; }
                if (cur < 0) return null;               // unreachable
                if (cur == goal) break;
                if (best > maxCost) return null;        // beyond this season's reach
                done[cur] = true;

                foreach (int nb in Neighbors[cur])
                {
                    if (done[nb] || !Walkable[nb]) continue;
                    int nd = dist[cur] + Cost[nb];
                    if (nd < dist[nb]) { dist[nb] = nd; prev[nb] = cur; }
                }
            }

            if (dist[goal] > maxCost) return null;
            var path = new List<int>();
            for (int at = goal; at >= 0; at = prev[at]) path.Add(at);
            path.Reverse();
            return path;
        }
    }

    /// <summary>Treaty kinds for the MVP diplomatic set (ACTIONS.md §5 P2).</summary>
    public static class TreatyType
    {
        public const string Nap = "nap";               // 互不侵犯
        public const string Alliance = "alliance";     // 同盟
        public const string Truce = "truce";           // 停戰

        public static bool IsValid(string type)
            => type == Nap || type == Alliance || type == Truce;
    }

    /// <summary>
    /// A treaty offer awaiting an answer (ACTIONS.md §5 P2/P3). One may be pending per pair, in either
    /// direction. Unanswered offers lapse at season rollover; accepted ones are ratified there, because
    /// treaties take effect next season (ADR-010 #5).
    /// </summary>
    public sealed class TreatyProposal
    {
        public string ProposalId;      // "p-1", ...
        public string FromSeatId;
        public string ToSeatId;
        public string Type;            // TreatyType.*
        public int DurationSeasons;    // seasons in force once ratified
        public int ProposedSeason;
        public bool Accepted;          // answered "accept", waiting for ratification at rollover
        public string Text;            // 附言, optional

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"proposal_id\":").Append(MiniJson.Str(ProposalId));
            sb.Append(",\"from_seat_id\":").Append(MiniJson.Str(FromSeatId));
            sb.Append(",\"to_seat_id\":").Append(MiniJson.Str(ToSeatId));
            sb.Append(",\"treaty_type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"duration_seasons\":").Append(MiniJson.Num(DurationSeasons));
            sb.Append(",\"proposed_season\":").Append(MiniJson.Num(ProposedSeason));
            sb.Append(",\"accepted\":").Append(Accepted ? "true" : "false");
            sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One treaty record (ACTIONS.md §5). Type ∈ TreatyType.*</summary>
    public sealed class TreatyRecord
    {
        public string Type;            // TreatyType.*
        public string FactionA;        // seat id
        public string FactionB;        // seat id
        public int ExpirySeason;       // last season the treaty is in force

        public bool Covers(string seatA, string seatB)
            => (FactionA == seatA && FactionB == seatB) || (FactionA == seatB && FactionB == seatA);

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"faction_a\":").Append(MiniJson.Str(FactionA));
            sb.Append(",\"faction_b\":").Append(MiniJson.Str(FactionB));
            sb.Append(",\"expiry_season\":").Append(MiniJson.Num(ExpirySeason));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 外交 state: per ordered-pair 信任, active treaties, and wars (ACTIONS.md §5, §9).
    /// Everything is keyed by seat id, so nothing is sized by player count — seats enter
    /// and leave through faction config, never through a hardcoded 3 or 8.
    /// </summary>
    public sealed class DiplomacyState
    {
        public readonly Dictionary<string, int> Trust = new Dictionary<string, int>();  // key: PairKey(from, to)
        public readonly List<TreatyRecord> Treaties = new List<TreatyRecord>();
        public readonly List<string> WarWith = new List<string>();                      // key: WarKey(a, b), unordered
        public readonly List<TreatyProposal> Proposals = new List<TreatyProposal>();     // pending offers (§5 P2/P3)
        private int nextProposalId = 1;

        /// <summary>Key of an ordered pair — 信任 is directional (A's view of B ≠ B's view of A).</summary>
        public static string PairKey(string fromSeatId, string toSeatId) => fromSeatId + "|" + toSeatId;

        /// <summary>Order-independent key, so a war between A and B is one fact rather than two.</summary>
        public static string WarKey(string seatA, string seatB)
            => string.CompareOrdinal(seatA, seatB) <= 0 ? seatA + "|" + seatB : seatB + "|" + seatA;

        public int GetTrust(string fromSeatId, string toSeatId)
            => Trust.TryGetValue(PairKey(fromSeatId, toSeatId), out int value) ? value : BalanceConfig.TrustStart;

        public void SetTrust(string fromSeatId, string toSeatId, int value)
            => Trust[PairKey(fromSeatId, toSeatId)] = Clamp(value, BalanceConfig.TrustMin, BalanceConfig.TrustMax);

        public void AddTrust(string fromSeatId, string toSeatId, int delta)
            => SetTrust(fromSeatId, toSeatId, GetTrust(fromSeatId, toSeatId) + delta);

        public bool IsAtWar(string seatA, string seatB) => WarWith.Contains(WarKey(seatA, seatB));

        public void DeclareWar(string seatA, string seatB)
        {
            string key = WarKey(seatA, seatB);
            if (!WarWith.Contains(key)) WarWith.Add(key);
        }

        public void MakePeace(string seatA, string seatB) => WarWith.Remove(WarKey(seatA, seatB));

        /// <summary>The treaty of this type between the two seats, or null (order-independent).</summary>
        public TreatyRecord FindTreaty(string type, string seatA, string seatB)
            => Treaties.Find(t => t.Type == type && t.Covers(seatA, seatB));

        public bool HasTreaty(string type, string seatA, string seatB)
            => FindTreaty(type, seatA, seatB) != null;

        /// <summary>Removes a treaty — used when it is broken by force (ADR-010 #7) or expires.</summary>
        public void BreakTreaty(TreatyRecord treaty)
        {
            if (treaty != null) Treaties.Remove(treaty);
        }

        /// <summary>The pending offer between these two seats in either direction, or null.</summary>
        public TreatyProposal FindProposal(string seatA, string seatB)
            => Proposals.Find(p => (p.FromSeatId == seatA && p.ToSeatId == seatB)
                                || (p.FromSeatId == seatB && p.ToSeatId == seatA));

        /// <summary>One offer per ordered pair, so an offer id never has to be guessed.</summary>
        public TreatyProposal AddProposal(string fromSeatId, string toSeatId, string type, int durationSeasons,
            int season, string text)
        {
            var proposal = new TreatyProposal
            {
                ProposalId = "p-" + nextProposalId++,
                FromSeatId = fromSeatId,
                ToSeatId = toSeatId,
                Type = type,
                DurationSeasons = durationSeasons,
                ProposedSeason = season,
                Text = text,
            };
            Proposals.Add(proposal);
            return proposal;
        }

        public string ToJson()
        {
            var treaties = new List<string>(Treaties.Count);
            foreach (var t in Treaties) treaties.Add(t.ToJson());
            var proposals = new List<string>(Proposals.Count);
            foreach (var p in Proposals) proposals.Add(p.ToJson());

            var sb = new StringBuilder(320);
            sb.Append("{\"trust\":").Append(MiniJson.IntMap(Trust));
            sb.Append(",\"treaties\":").Append(MiniJson.Array(treaties));
            sb.Append(",\"war_with\":").Append(MiniJson.StrArray(WarWith));
            sb.Append(",\"proposals\":").Append(MiniJson.Array(proposals));
            sb.Append('}');
            return sb.ToString();
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;
    }

    public sealed class GameState
    {
        public string GameId = "g-001";
        public int Season;                       // 1-based once the first season begins
        public string Phase = "setup";           // setup | player_orders | resolution
        public readonly List<FactionState> Factions = new List<FactionState>();
        public readonly DiplomacyState Diplomacy = new DiplomacyState();
        public readonly List<CityState> Cities = new List<CityState>();
        public readonly List<ArmyState> Armies = new List<ArmyState>();
        public MapData Map;

        public FactionState FindFaction(string seatId) => Factions.Find(f => f.SeatId == seatId);
        public CityState FindCity(string id) => Cities.Find(c => c.Id == id);
        public ArmyState FindArmy(string id) => Armies.Find(a => a.Id == id);
        public CityState CityAt(int x, int z) => Cities.Find(c => c.X == x && c.Z == z);
        public ArmyState ArmyAt(int x, int z) => Armies.Find(a => a.X == x && a.Z == z);

        public List<CityState> CitiesOf(string seatId) => Cities.FindAll(c => c.OwnerSeatId == seatId);
        public List<ArmyState> ArmiesOf(string seatId) => Armies.FindAll(a => a.OwnerSeatId == seatId);
    }

    /// <summary>
    /// What one seat knows about another: name, controller and 威望 are public (design §3/§7.3); the
    /// seat's own side of 信任 is its business. Both directions are carried because the design's
    /// diplomacy screen is built on watching the other party's trust move.
    /// </summary>
    public sealed class FactionView
    {
        public string SeatId;
        public string Name;
        public string Controller;      // ControllerType.*
        public int Prestige;

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(Controller));
            sb.Append(",\"prestige\":").Append(MiniJson.Num(Prestige));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>A seat's view of one relationship: 信任 in both directions plus whether war is on.</summary>
    public sealed class RelationView
    {
        public string SeatId;
        public int MyTrust;            // this seat's 信任 in them
        public int TheirTrust;         // their 信任 in this seat
        public bool AtWar;

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"my_trust\":").Append(MiniJson.Num(MyTrust));
            sb.Append(",\"their_trust\":").Append(MiniJson.Num(TheirTrust));
            sb.Append(",\"at_war\":").Append(AtWar ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// One seat's filtered world — the only thing a controller may decide on (AGENTS.md invariant #3).
    /// Cities and armies outside the fog ranges are simply absent, and the event log is pre-filtered.
    /// </summary>
    public sealed class Observation
    {
        public string SeatId;
        public int Season;
        public string Phase;
        public int CommandPoints;
        public int Prestige;

        public readonly List<CityState> Cities = new List<CityState>();
        public readonly List<ArmyState> Armies = new List<ArmyState>();
        public readonly List<FactionView> Factions = new List<FactionView>();
        public readonly List<RelationView> Relations = new List<RelationView>();
        public readonly List<TreatyRecord> Treaties = new List<TreatyRecord>();
        public readonly List<TreatyProposal> Proposals = new List<TreatyProposal>();
        public readonly List<EventRecord> Events = new List<EventRecord>();

        public CityState FindCity(string id) => Cities.Find(c => c.Id == id);
        public ArmyState FindArmy(string id) => Armies.Find(a => a.Id == id);
        public List<CityState> CitiesOf(string seatId) => Cities.FindAll(c => c.OwnerSeatId == seatId);
        public List<ArmyState> ArmiesOf(string seatId) => Armies.FindAll(a => a.OwnerSeatId == seatId);
        public FactionView Faction(string seatId) => Factions.Find(f => f.SeatId == seatId);
        public RelationView Relation(string seatId) => Relations.Find(r => r.SeatId == seatId);

        public string ToJson()
        {
            var cities = new List<string>(Cities.Count);
            foreach (var c in Cities) cities.Add(c.ToJson());
            var armies = new List<string>(Armies.Count);
            foreach (var a in Armies) armies.Add(a.ToJson());
            var factions = new List<string>(Factions.Count);
            foreach (var f in Factions) factions.Add(f.ToJson());
            var relations = new List<string>(Relations.Count);
            foreach (var r in Relations) relations.Add(r.ToJson());
            var treaties = new List<string>(Treaties.Count);
            foreach (var t in Treaties) treaties.Add(t.ToJson());
            var proposals = new List<string>(Proposals.Count);
            foreach (var p in Proposals) proposals.Add(p.ToJson());
            var events = new List<string>(Events.Count);
            foreach (var e in Events) events.Add(e.ToJson());

            var sb = new StringBuilder(640);
            sb.Append("{\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"phase\":").Append(MiniJson.Str(Phase));
            sb.Append(",\"command_points\":").Append(MiniJson.Num(CommandPoints));
            sb.Append(",\"prestige\":").Append(MiniJson.Num(Prestige));
            sb.Append(",\"cities\":").Append(MiniJson.Array(cities));
            sb.Append(",\"armies\":").Append(MiniJson.Array(armies));
            sb.Append(",\"factions\":").Append(MiniJson.Array(factions));
            sb.Append(",\"relations\":").Append(MiniJson.Array(relations));
            sb.Append(",\"treaties\":").Append(MiniJson.Array(treaties));
            sb.Append(",\"proposals\":").Append(MiniJson.Array(proposals));
            sb.Append(",\"events\":").Append(MiniJson.Array(events));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Seat play order for a season (ADR-000: each seat takes one ordered turn). Pure C# so the hotseat
    /// flow — who plays when and where the UI must stop for a human — is testable without Unity.
    /// </summary>
    public static class SeatRotation
    {
        /// <summary>Every seat, in the order it acts this season.</summary>
        public static List<string> PlayOrder(GameState state)
        {
            var order = new List<string>();
            foreach (var fac in state.Factions) order.Add(fac.SeatId);
            return order;
        }

        /// <summary>Human-controlled seats, in play order — the ones a hotseat UI stops for.</summary>
        public static List<string> HumanSeats(GameState state)
        {
            var humans = new List<string>();
            foreach (var fac in state.Factions)
                if (fac.Controller == ControllerType.Human) humans.Add(fac.SeatId);
            return humans;
        }

        public static bool IsHuman(GameState state, string seatId)
        {
            var fac = state.FindFaction(seatId);
            return fac != null && fac.Controller == ControllerType.Human;
        }

        /// <summary>The seat that acts after this one, or null when the season's rotation is done.</summary>
        public static string NextAfter(GameState state, string seatId)
        {
            var order = PlayOrder(state);
            int index = order.IndexOf(seatId);
            if (index < 0 || index + 1 >= order.Count) return null;
            return order[index + 1];
        }

        /// <summary>First seat to act in a season: the first human, else the first seat at all.</summary>
        public static string FirstSeat(GameState state)
        {
            var humans = HumanSeats(state);
            if (humans.Count > 0) return humans[0];
            return state.Factions.Count > 0 ? state.Factions[0].SeatId : null;
        }
    }
}
