using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

namespace LuanShi.ExperimentBatch
{
    /// <summary>
    /// The world digest and the per-dispatch privacy scan, each with the limit it has. Offline: no provider, no prompts
    /// beyond synthetic ones, and nothing here describes model behaviour.
    /// </summary>
    internal static partial class BatchSelfTests
    {
        private static void WorldDigestCoversTheCanonicalState(TestLog log)
        {
            BatchManifest m = Example();
            GameEngine a = BatchWorld.Create(m, "digest", 4242), b = BatchWorld.Create(m, "digest", 4242);
            string baseline = BatchWorld.WorldDigest(a);
            log.Check(baseline == BatchWorld.WorldDigest(a) && baseline == BatchWorld.WorldDigest(b),
                "the digest is deterministic: the same call and an identically built engine give the same value");

            // Each of these is a field the earlier, partial digest did not hash (or a map table it never touched).
            var changes = new List<KeyValuePair<string, Action<GameEngine>>>
            {
                Change("a map cost cell", e => e.State.Map.Cost[0] += 1),
                Change("a map walkable cell", e => e.State.Map.Walkable[0] = !e.State.Map.Walkable[0]),
                Change("a map neighbour table", e => { int[] n = e.State.Map.Neighbors.First(x => x != null && x.Length > 0); n[0] = n[0] == 0 ? 1 : 0; }),
                Change("a city's training", e => e.State.Cities[0].Training += 1),
                Change("a city's name", e => e.State.Cities[0].Name += "x"),
                Change("a city's garrison type", e => e.State.Cities[0].GarrisonType = "cavalry-x"),
                Change("an army's unit type", e => e.State.Armies[0].UnitType = "archer-x"),
                Change("an army's march flag", e => e.State.Armies[0].MarchedThisSeason = !e.State.Armies[0].MarchedThisSeason),
                Change("diplomacy: trust", e => e.State.Diplomacy.Trust["seat-0|seat-1"] = 77),
                Change("diplomacy: a war", e => e.State.Diplomacy.WarWith.Add("seat-0|seat-1")),
                Change("a faction's research points", e => e.State.Factions[0].ResearchPoints += 1),
                Change("the phase", e => e.State.Phase = "resolution"),
                Change("a watch tower", e => e.State.WatchTowers.Add(new WatchTowerState { Id = "tower-x", Name = "x", X = 1, Z = 1 })),
            };
            foreach (KeyValuePair<string, Action<GameEngine>> change in changes)
            {
                GameEngine e = BatchWorld.Create(m, "digest", 4242);
                change.Value(e);
                log.Check(BatchWorld.WorldDigest(e) != baseline, "the digest changes when " + change.Key + " changes");
            }

            GameEngine other = BatchWorld.Create(m, "digest", 1);
            log.Check(BatchWorld.WorldDigest(other) == baseline,
                "documented scope: the private RNG stream is NOT part of the digest (a different seed with the same state gives the same value)");
            log.Check(BatchWorldDigest.Scope.Contains("map topology and costs") && BatchWorldDigest.Scope.Contains("excludes private fields"),
                "the scope text says what is and is not covered");
            BatchManifest manifest = Example();
            BatchResult r = RunBatch(manifest, out string root);
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(Match(r, "seed4242-control").Spatial, BatchManifest.WriteOptions);
                log.Check(json.Contains("world_digest_scope") && json.Contains("excludes private fields"), "every result that carries a digest also carries its scope");
            }
            finally { Cleanup(root); }
        }

        private static KeyValuePair<string, Action<GameEngine>> Change(string name, Action<GameEngine> action)
            => new KeyValuePair<string, Action<GameEngine>>(name, action);

        private static ExchangeCapture Dispatch(string seat, int n, string user, params string[] statuses)
        {
            // statuses are "token=visible|scouted|hidden"
            var w = new DispatchWitness();
            foreach (string s in statuses) { string[] p = s.Split('='); w.Tokens[p[0]] = p[1]; }
            return new ExchangeCapture { Seat = seat, SeatRequest = n, Index = n, System = "system", User = user, Witness = w };
        }

        private static void PrivacyIsJudgedPerDispatch(TestLog log)
        {
            // X is hidden, then visible, then hidden again; Y is hidden throughout.
            var captures = new List<ExchangeCapture>
            {
                Dispatch("seat-0", 1, "nothing about the rival", "X=hidden", "Y=hidden"),
                Dispatch("seat-0", 2, "the rival X is in view", "X=visible", "Y=hidden"),
                Dispatch("seat-0", 3, "last seen at X", "X=hidden", "Y=hidden"),
            };
            PrivacyEvidence e = BatchPrivacy.Scan(captures);
            log.Check(e.Leaks.Count == 0 && e.RetainedKnownMentions == 1 && e.HiddenTokenChecks == 5,
                "an entity shown earlier and hidden again may be mentioned: it is retained knowledge, counted and not called a leak",
                string.Join("; ", e.Leaks) + " retained=" + e.RetainedKnownMentions + " checks=" + e.HiddenTokenChecks);

            captures[2].User = "last seen at X, and Y is about to attack";
            e = BatchPrivacy.Scan(captures);
            log.Check(e.Leaks.Count == 1 && e.Leaks[0].Contains("'Y'") && e.Leaks[0].Contains("request 3"),
                "an entity never shown that appears while hidden is a leak, naming the request");

            captures[0].User = "the rival X";
            e = BatchPrivacy.Scan(captures);
            log.Check(e.Leaks.Any(x => x.Contains("'X'") && x.Contains("request 1")),
                "mentioning an entity BEFORE it was ever shown is a leak even though it is shown and hidden later");
            captures[0].User = "nothing about the rival";

            var otherSeat = new List<ExchangeCapture>
            {
                Dispatch("seat-0", 1, "the rival X is in view", "X=visible"),
                Dispatch("seat-1", 1, "the rival X", "X=hidden"),
            };
            e = BatchPrivacy.Scan(otherSeat);
            log.Check(e.Leaks.Count == 1 && e.Leaks[0].StartsWith("seat-1"), "what one seat was shown never excuses the same text in another seat's prompt");

            var scouted = new List<ExchangeCapture> { Dispatch("seat-0", 1, "scouting says X", "X=scouted"), Dispatch("seat-0", 2, "report on X", "X=hidden") };
            e = BatchPrivacy.Scan(scouted);
            log.Check(e.Leaks.Count == 0 && e.RetainedKnownMentions == 1, "a scouted entity counts as shown");

            captures.Add(new ExchangeCapture { Seat = "seat-0", SeatRequest = 4, System = "s", User = "u" });
            e = BatchPrivacy.Scan(captures);
            log.Check(e.RequestsWithoutWitness == 1, "a request without a witness is counted, never silently passed");
            log.Check(BatchPrivacy.Scope.Contains("exact strings") && BatchPrivacy.Scope.Contains("retained knowledge"), "the scope text states the limits of what the scan proves");
        }

        private static void PrivacyWitnessFollowsTheRealFog(TestLog log)
        {
            BatchManifest m = Example();
            GameEngine engine = BatchWorld.Create(m, "fog", 4242);
            string Status(string token) => BatchPrivacy.Witness(engine, "seat-0").Tokens[token];
            ArmyState rival = engine.State.Armies.First(a => a.OwnerSeatId == "seat-1");
            CityState home = engine.State.Cities.First(c => c.OwnerSeatId == "seat-0");
            int hx = rival.X, hz = rival.Z;
            log.Check(Status(rival.Id) == DispatchWitness.Hidden, "at the start the rival army is hidden from seat-0");
            rival.X = home.X; rival.Z = home.Z;
            log.Check(Status(rival.Id) == DispatchWitness.Visible, "placed on seat-0's own city it becomes visible in the witness");
            rival.X = hx; rival.Z = hz;
            log.Check(Status(rival.Id) == DispatchWitness.Hidden, "moved back it is hidden again: the witness is recomputed at each dispatch");

            var captures = new List<ExchangeCapture>();
            captures.Add(new ExchangeCapture { Seat = "seat-0", SeatRequest = 1, System = "s", User = "no mention", Witness = BatchPrivacy.Witness(engine, "seat-0") });
            rival.X = home.X; rival.Z = home.Z;
            captures.Add(new ExchangeCapture { Seat = "seat-0", SeatRequest = 2, System = "s", User = "enemy " + rival.Id + " at the gate", Witness = BatchPrivacy.Witness(engine, "seat-0") });
            rival.X = hx; rival.Z = hz;
            captures.Add(new ExchangeCapture { Seat = "seat-0", SeatRequest = 3, System = "s", User = "earlier " + rival.Id + " was at the gate", Witness = BatchPrivacy.Witness(engine, "seat-0") });
            PrivacyEvidence e = BatchPrivacy.Scan(captures);
            log.Check(e.Leaks.Count == 0 && e.RetainedKnownMentions >= 1,
                "with the real engine: seen while visible, mentioned later while hidden, is retained knowledge and not a leak");
        }
    }
}
