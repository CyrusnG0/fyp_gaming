using System;
using System.IO;
using System.Text;
using LuanShi.Engine;

// [LS-014] Seat-filtered history export (replaces the canonical log dump).
//
// The player-facing export must be exactly the viewing seat's filtered `Observation.Events`: no canonial
// payload, no other seat's private facts. These tests exercise the serializer, the real writer/path
// selection, seat switching, filename safety and the untouched legacy `luanshi_log.jsonl`. Every
// assertion name carries the [LS-014] tag.

internal static partial class Harness
{
    private static void RunHistoryExport()
    {
        Section("[new] LS-014 seat-filtered history export");

        ExportMatchesObservation();
        DeterministicForSameSeed();
        PrivatePayloadsStayWithTheirSeat();
        HiddenWorldsExportIdentically();
        RetainedSnapshotStable();
        WriterSwitchesSeats();
        PrivateSeatFilesStayIsolated();
        FileNameEncoding();
        FileNameIsSafe();
        FileNameCollisions();
        LoneSurrogateIdsAreDistinct();
        LongIdsAreHashedAndUnique();
        FileNameLengthBoundaries();
        TrailingDotAndSpaceAreSafe();
        LegacyCanonicalFileUntouched();
        NullObservationWritesNothing();
    }

    // ---- shared test plumbing ----------------------------------------------

    private static string NewHistoryDir(string label)
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "luanshi_history_" + label + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteHistoryDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    // ---- serializer --------------------------------------------------------

    private static void ExportMatchesObservation()
    {
        var e = PrivacyWorld(901, hiddenArmyTroops: 800);
        e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
            reason: "export reason"));
        var obs = e.Observe("seat-a");

        var expected = new StringBuilder();
        foreach (var rec in obs.Events) expected.Append(rec.ToJson()).Append('\n');
        string json = HistoryExport.ToJsonLines(obs);

        Check(obs.Events.Count > 0 && json == expected.ToString(),
              "[LS-014] export is the observation's EventView.ToJson lines, in order");
        Check(json.EndsWith("\n") && !json.Contains("\n\n"),
              "[LS-014] each record is one newline-terminated JSON line");
        Check(HistoryExport.ToJsonLines(null) == "" && HistoryExport.ToJsonLines(new Observation()) == "",
              "[LS-014] null or empty history exports as the empty string");
    }

    private static string ReplayFilteredExport(int seed)
    {
        var e = PrivacyWorld(seed, hiddenArmyTroops: 800);
        e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
            reason: "replay export"));
        e.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-b", "city-b",
            reason: "replay second"));
        return HistoryExport.ToJsonLines(e.Observe("seat-a"));
    }

    private static void DeterministicForSameSeed()
    {
        string one = ReplayFilteredExport(902);
        string two = ReplayFilteredExport(902);

        Check(one.Length > 0 && one == two,
              "[LS-014] two identical engines export byte-identical history");
    }

    // ---- private payloads --------------------------------------------------

    private static void PrivatePayloadsStayWithTheirSeat()
    {
        // The proven covert fixture from Harness.EventPrivacy: seat-b is the spy, seat-a a bystander.
        int seed = FindSpySeed(ActionType.Rumor, success: true, espionageLevel: 2);
        var e = PrivacyWorld(seed, cityBGold: 5000);
        SetLevel(e, "seat-b", ResearchLine.Espionage, 2);
        var r = e.Submit(PrivacyOrder(ActionType.Rumor, "seat-b", "city-c",
            reason: "deniable export"));

        string spy = HistoryExport.ToJsonLines(e.Observe("seat-b"));
        string bystander = HistoryExport.ToJsonLines(e.Observe("seat-a"));

        Check(seed > 0 && r.Ok, $"[LS-014] covert fixture executes ({r.Error})");
        Check(spy.Contains("deniable export") && spy.Contains("\"action\":\"rumor\""),
              "[LS-014] the acting seat's export carries its private mission payload");
        Check(!bystander.Contains("deniable export") && !bystander.Contains("rumor"),
              "[LS-014] a bystander's export carries no covert key or value");
        Check(spy != bystander,
              "[LS-014] seats with private events get different export contents");

        // A hidden march destination and its private reason stay with the actor too.
        var march = PrivacyWorld(908, hiddenArmyTroops: 800);
        march.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
            reason: "hidden destination"));
        string actor = HistoryExport.ToJsonLines(march.Observe("seat-b"));
        string observer = HistoryExport.ToJsonLines(march.Observe("seat-a"));

        Check(actor.Contains("hidden destination") && actor.Contains("\"to\":\"[12,0]\""),
              "[LS-014] the actor's export keeps the hidden march target and reason");
        Check(!observer.Contains("hidden destination") && !observer.Contains("\"to\""),
              "[LS-014] the observer's export has neither the march target nor the reason");
    }

    // ---- hidden-world equivalence ------------------------------------------

    private static void HiddenWorldsExportIdentically()
    {
        var w1 = PrivacyWorld(903, cityBGarrison: 1000, hiddenArmyTroops: 500);
        var w2 = PrivacyWorld(903, cityBGarrison: 9000, hiddenArmyTroops: 7777);
        var r1 = w1.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 11, 0,
            reason: "reason one"));
        var r2 = w2.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
            reason: "reason two"));

        string one = HistoryExport.ToJsonLines(w1.Observe("seat-a"));
        string two = HistoryExport.ToJsonLines(w2.Observe("seat-a"));

        Check(r1.Ok && r2.Ok,
              $"[LS-014] both hidden-world fixtures execute ({r1.Error}; {r2.Error})");
        Check(one == two,
              "[LS-014] hidden-world differences do not change the observer's export");
        Check(one.Length > 0
           && !one.Contains("reason one") && !one.Contains("reason two")
           && !one.Contains("9000") && !one.Contains("7777") && !one.Contains("\"to\""),
              "[LS-014] the identical export leaks no hidden target, reason or strength");
        Check(w1.State.FindCity("city-b").Garrison == 1000
           && w2.State.FindCity("city-b").Garrison == 9000,
              "[LS-014] fixture: the two canonical worlds really differ");

        string dir1 = NewHistoryDir("hidden-one");
        string dir2 = NewHistoryDir("hidden-two");
        try
        {
            string path1 = HistoryExport.Write(dir1, w1.Observe("seat-a"));
            string path2 = HistoryExport.Write(dir2, w2.Observe("seat-a"));
            Check(path1 != null && path2 != null && File.Exists(path1) && File.Exists(path2),
                  "[LS-014] both hidden-world exports were actually written");
            if (path1 == null || path2 == null) return;
            Check(SameBytes(File.ReadAllBytes(path1), File.ReadAllBytes(path2)),
                  "[LS-014] hidden-world variants produce byte-identical written files");
        }
        finally
        {
            DeleteHistoryDir(dir1);
            DeleteHistoryDir(dir2);
        }
    }

    // ---- retained snapshot -------------------------------------------------

    private static void RetainedSnapshotStable()
    {
        var e = PrivacyWorld(904, hiddenArmyTroops: 800);
        e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
            reason: "before snapshot"));

        var retained = e.Observe("seat-a");
        string before = HistoryExport.ToJsonLines(retained);
        int beforeCount = retained.Events.Count;

        e.Submit(PrivacyOrder(ActionType.LevyTax, "seat-b", "city-b"));
        e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 13, 0,
            reason: "after snapshot"));
        string after = HistoryExport.ToJsonLines(retained);

        Check(before.Length > 0 && before == after,
              "[LS-014] a retained observation exports unchanged after later actions");
        Check(beforeCount > 0 && e.Observe("seat-a").Events.Count > beforeCount,
              "[LS-014] fixture: those later actions really added events to a fresh observation");
    }

    // ---- the real writer and path selection --------------------------------

    private static void WriterSwitchesSeats()
    {
        string dir = NewHistoryDir("writer");
        try
        {
            var e = PrivacyWorld(905, hiddenArmyTroops: 800);
            var obsA1 = e.Observe("seat-a");
            var obsB = e.Observe("seat-b");
            string a1 = HistoryExport.ToJsonLines(obsA1);
            string b = HistoryExport.ToJsonLines(obsB);
            Check(a1 == b,
                  "[LS-014] fixture: two seats can have identical public history");

            string pathA1 = HistoryExport.Write(dir, obsA1);
            string pathB = HistoryExport.Write(dir, obsB);

            Check(pathA1 != null && pathB != null && pathA1 != pathB,
                  "[LS-014] identical public history still gets two distinct seat paths");
            if (pathA1 == null || pathB == null) return;
            Check(SameBytes(ExportBytes(obsA1), File.ReadAllBytes(pathA1))
               && SameBytes(ExportBytes(obsB), File.ReadAllBytes(pathB)),
                  "[LS-014] each written file's bytes match its own observation");
            byte[] bBytes = File.ReadAllBytes(pathB);

            // Advance only seat B's action news, then report for seat A again.
            e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
                reason: "writer secret"));
            var obsA2 = e.Observe("seat-a");
            string pathA2 = HistoryExport.Write(dir, obsA2);

            Check(pathA2 == pathA1,
                  "[LS-014] a second report by seat A reuses its own filename");
            if (pathA2 == null) return;
            Check(SameBytes(ExportBytes(obsA2), File.ReadAllBytes(pathA2))
               && HistoryExport.ToJsonLines(obsA2) != a1,
                  "[LS-014] A's second write replaces only A's file with the new filtered history");
            Check(SameBytes(bBytes, File.ReadAllBytes(pathB)),
                  "[LS-014] writing A again leaves B's file byte-for-byte unchanged");
            Check(Directory.GetFiles(dir, "*.jsonl").Length == 2,
                  "[LS-014] three writes across two seats leave exactly two files");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static void PrivateSeatFilesStayIsolated()
    {
        string dir = NewHistoryDir("private-isolation");
        string legacy = Path.Combine(dir, "luanshi_log.jsonl");
        byte[] sentinel = Encoding.UTF8.GetBytes("LEGACY-CANONICAL");
        try
        {
            File.WriteAllBytes(legacy, sentinel);

            var e = PrivacyWorld(907, hiddenArmyTroops: 800);
            // Each seat performs a different action with its own private reason. The other seat sees
            // only the public action type, never the reason.
            var rA = e.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-a", "city-a",
                reason: "PRIVATE-ONLY-A"));
            var rB = e.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-b", "city-b",
                reason: "PRIVATE-ONLY-B"));
            Check(rA.Ok && rB.Ok,
                  $"[LS-014] both seats' private fixtures execute ({rA.Error}; {rB.Error})");

            string pathA1 = HistoryExport.Write(dir, e.Observe("seat-a"));
            string pathB = HistoryExport.Write(dir, e.Observe("seat-b"));
            Check(pathA1 != null && pathB != null && pathA1 != pathB,
                  "[LS-014] private per-seat events still get distinct seat files");
            if (pathA1 == null || pathB == null) return;

            string a1 = File.ReadAllText(pathA1);
            string b1 = File.ReadAllText(pathB);
            Check(a1.Contains("PRIVATE-ONLY-A") && !a1.Contains("PRIVATE-ONLY-B"),
                  "[LS-014] seat A's file contains only A's private marker");
            Check(b1.Contains("PRIVATE-ONLY-B") && !b1.Contains("PRIVATE-ONLY-A"),
                  "[LS-014] seat B's file contains only B's private marker");

            byte[] bBytes = File.ReadAllBytes(pathB);
            string pathA2 = HistoryExport.Write(dir, e.Observe("seat-a"));
            Check(pathA2 == pathA1,
                  "[LS-014] A's second write reuses A's path and leaves B's path alone");
            Check(SameBytes(bBytes, File.ReadAllBytes(pathB)),
                  "[LS-014] B's file is byte-for-byte unchanged by A's second write");
            string a2 = File.ReadAllText(pathA1);
            Check(a2.Contains("PRIVATE-ONLY-A") && !a2.Contains("PRIVATE-ONLY-B"),
                  "[LS-014] A's rewritten file still holds only A's private marker");

            bool sentinelLeak = false;
            foreach (string file in Directory.GetFiles(dir))
                if (Path.GetFileName(file) != "luanshi_log.jsonl"
                    && File.ReadAllText(file).Contains("LEGACY-CANONICAL"))
                    sentinelLeak = true;
            Check(!sentinelLeak,
                  "[LS-014] the seeded legacy sentinel never appears in a per-seat file");
            Check(SameBytes(sentinel, File.ReadAllBytes(legacy)),
                  "[LS-014] the seeded legacy file is still byte-for-byte untouched");

            string[] files = Directory.GetFiles(dir);
            bool onlyExpected = true, noTemps = true;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                onlyExpected &= name == "luanshi_log.jsonl"
                             || (name.StartsWith("luanshi_history_") && name.EndsWith(".jsonl"));
                noTemps &= !name.EndsWith(".tmp") && !name.StartsWith(".luanshi-");
            }
            Check(onlyExpected && noTemps && files.Length == 3,
                  "[LS-014] the directory holds only the two seat files and the seeded legacy file, with no .tmp leftovers");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static byte[] ExportBytes(Observation obs)
        => new UTF8Encoding(false).GetBytes(HistoryExport.ToJsonLines(obs));

    // ---- legacy canonical file ---------------------------------------------

    private static void LegacyCanonicalFileUntouched()
    {
        string dir = NewHistoryDir("legacy");
        string fresh = NewHistoryDir("fresh");
        try
        {
            string legacy = Path.Combine(dir, "luanshi_log.jsonl");
            byte[] sentinel = Encoding.UTF8.GetBytes("LEGACY-CANONICAL");
            File.WriteAllBytes(legacy, sentinel);

            var e = PrivacyWorld(906, hiddenArmyTroops: 800);
            e.Submit(PrivacyOrder(ActionType.March, "seat-b", "army-seat-b-1", 12, 0,
                reason: "legacy secret"));
            // Seat C's private action keeps both A's and B's filtered exports different from the canonical log.
            e.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-c", "city-c",
                reason: "legacy private"));
            var obsA = e.Observe("seat-a");
            var obsB = e.Observe("seat-b");
            string pathA = HistoryExport.Write(dir, obsA);
            string pathB = HistoryExport.Write(dir, obsB);

            Check(SameBytes(sentinel, File.ReadAllBytes(legacy)),
                  "[LS-014] a seeded legacy canonical log is left byte-for-byte unchanged");
            Check(pathA != null && pathB != null
               && Path.GetFileName(pathA) != "luanshi_log.jsonl"
               && Path.GetFileName(pathB) != "luanshi_log.jsonl",
                  "[LS-014] neither export reuses the legacy canonical filename");

            bool canonicalContent = false;
            foreach (string file in Directory.GetFiles(dir))
                if (Path.GetFileName(file) != "luanshi_log.jsonl"
                    && File.ReadAllText(file) == e.Log.ToJsonLines())
                    canonicalContent = true;
            Check(!canonicalContent,
                  "[LS-014] no new file contains a canonical-log dump");
            Check(Directory.GetFiles(dir).Length == 3,
                  "[LS-014] writes add only per-seat files beside the untouched legacy log");

            string freshPath = HistoryExport.Write(fresh, obsA);
            Check(freshPath != null && File.Exists(freshPath)
               && !File.Exists(Path.Combine(fresh, "luanshi_log.jsonl")),
                  "[LS-014] a fresh export directory never receives luanshi_log.jsonl");
        }
        finally
        {
            DeleteHistoryDir(dir);
            DeleteHistoryDir(fresh);
        }
    }

    // ---- null observation --------------------------------------------------

    private static void NullObservationWritesNothing()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "luanshi_history_null_" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(!Directory.Exists(dir),
                  "[LS-014] fixture: the null-observation directory does not exist yet");
            string path = HistoryExport.Write(dir, null);
            Check(path == null,
                  "[LS-014] Write(null observation) returns null");
            Check(!Directory.Exists(dir) && !File.Exists(dir),
                  "[LS-014] a null observation creates no directory or file");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }
}
