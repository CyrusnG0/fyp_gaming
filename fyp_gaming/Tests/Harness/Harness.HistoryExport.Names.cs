using System;
using System.IO;
using LuanShi.Engine;

// [LS-014] Filename encoding, collision resistance and length capping for HistoryExport.
//
// The writer must derive a safe, injective filename component from any .NET string, including
// malformed strings with lone surrogates, without letting the result approach filesystem limits.
// This partial holds the pure filename tests; Harness.HistoryExport.cs holds serializer/writer tests.

internal static partial class Harness
{
    private const string HistoryPrefix = "luanshi_history_";
    private const string HistorySuffix = ".jsonl";

    private static bool SameName(string left, string right)
        => string.Equals(HistoryExport.FileName(left), HistoryExport.FileName(right),
                         StringComparison.OrdinalIgnoreCase);

    private static string HistoryBody(string seatId)
    {
        string name = HistoryExport.FileName(seatId);
        return name.Substring(HistoryPrefix.Length,
                             name.Length - HistoryPrefix.Length - HistorySuffix.Length);
    }

    private static void FileNameEncoding()
    {
        Check(HistoryExport.FileName("seat-a") == "luanshi_history_seat-a.jsonl",
              "[LS-014] [a-z0-9-] survive raw in seat filenames");
        Check(HistoryExport.FileName("Seat-A") == "luanshi_history_%0053eat-%0041.jsonl",
              "[LS-014] uppercase letters become four uppercase hex digits");
        Check(HistoryExport.FileName("seat_a") == "luanshi_history_seat%005Fa.jsonl",
              "[LS-014] '_' is escaped as %005F");
        Check(HistoryExport.FileName("員外") == "luanshi_history_%54E1%5916.jsonl",
              "[LS-014] non-ASCII is encoded from its UTF-16 code units");
        Check(HistoryExport.FileName("a/b") == "luanshi_history_a%002Fb.jsonl"
           && HistoryExport.FileName("a:b") == "luanshi_history_a%003Ab.jsonl",
              "[LS-014] separators and ':' are escaped from seat filenames");
        Check(HistoryExport.FileName(null) == "luanshi_history__n.jsonl"
           && HistoryExport.FileName("") == "luanshi_history__e.jsonl"
           && HistoryExport.FileName(null) != HistoryExport.FileName(""),
              "[LS-014] null and empty ids map to distinct, non-encoding tokens");
    }

    private static void LoneSurrogateIdsAreDistinct()
    {
        string[] ids = { "\uD800", "\uD801", new string((char)0xFFFD, 1) };
        string[] names =
        {
            HistoryExport.FileName(ids[0]),
            HistoryExport.FileName(ids[1]),
            HistoryExport.FileName(ids[2]),
        };

        Check(names[0] == "luanshi_history_%D800.jsonl"
           && names[1] == "luanshi_history_%D801.jsonl"
           && names[2] == "luanshi_history_%FFFD.jsonl",
              "[LS-014] lone surrogates and U+FFFD encode their exact UTF-16 code units");
        Check(!SameName(ids[0], ids[1]) && !SameName(ids[0], ids[2]) && !SameName(ids[1], ids[2]),
              "[LS-014] lone-surrogate ids all map to distinct filenames");

        string dir = NewHistoryDir("surrogates");
        try
        {
            var paths = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                paths[i] = HistoryExport.Write(dir, new Observation { SeatId = ids[i] });

            Check(paths[0] != null && paths[1] != null && paths[2] != null
               && paths[0] != paths[1] && paths[0] != paths[2] && paths[1] != paths[2]
               && File.Exists(paths[0]) && File.Exists(paths[1]) && File.Exists(paths[2]),
                  "[LS-014] lone-surrogate ids can be written as three distinct files");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static void LongIdsAreHashedAndUnique()
    {
        string dir = NewHistoryDir("long-ids");
        try
        {
            string longAscii = new string('a', 300);
            string longNonAscii = new string('員', 200);
            string sharedOne = new string('b', 120) + "-one";
            string sharedTwo = new string('b', 120) + "-two";
            string[] ids = { longAscii, longNonAscii, sharedOne, sharedTwo };

            bool namesShortEnough = true, writesSucceed = true;
            var paths = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                string name = HistoryExport.FileName(ids[i]);
                namesShortEnough &= name.Length <= 142;
                paths[i] = HistoryExport.Write(dir, new Observation { SeatId = ids[i] });
                writesSucceed &= paths[i] != null && File.Exists(paths[i])
                              && Path.GetFileName(paths[i]) == name;
            }

            Check(namesShortEnough,
                  "[LS-014] 300-char and 200-char non-ASCII ids cap the filename at <= 142 chars");
            Check(writesSucceed,
                  "[LS-014] long ids still write successfully in the temp export directory");
            Check(HistoryExport.FileName(longAscii).Contains("~")
               && HistoryExport.FileName(longNonAscii).Contains("~"),
                  "[LS-014] over-long encoded bodies use the '~' hash form");
            Check(HistoryExport.FileName(sharedOne).Contains("~")
               && HistoryExport.FileName(sharedTwo).Contains("~")
               && HistoryExport.FileName(sharedOne).Length == HistoryExport.FileName(sharedTwo).Length
               && HistoryExport.FileName(sharedOne) != HistoryExport.FileName(sharedTwo)
               && HistoryBody(sharedOne).Substring(0, 80) == HistoryBody(sharedTwo).Substring(0, 80)
               && paths[2] != paths[3] && File.Exists(paths[2]) && File.Exists(paths[3]),
                  "[LS-014] long ids sharing a 120-char prefix stay distinct in the capped hash form");
            Check(Directory.GetFiles(dir).Length == ids.Length
               && Directory.GetFiles(dir, "*.tmp").Length == 0,
                  "[LS-014] long-id writes leave exactly one file each and no .tmp leftovers");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static void FileNameLengthBoundaries()
    {
        string exactly120 = new string('b', 120);
        string exactly121 = new string('b', 121);
        string name120 = HistoryExport.FileName(exactly120);
        string name121 = HistoryExport.FileName(exactly121);

        Check(!name120.Contains("~") && name120.Length == 142,
              "[LS-014] a 120-char encoded body stays uncapped (142-char full name)");
        Check(name121.Contains("~") && name121.Length == 135,
              "[LS-014] a 121-char encoded body caps to the 135-char '~' form");

        string loneD800 = new string('a', 116) + "\uD800";
        string loneD801 = new string('a', 116) + "\uD801";
        string replacement = new string('a', 116) + new string((char)0xFFFD, 1);
        string nameD800 = HistoryExport.FileName(loneD800);
        string nameD801 = HistoryExport.FileName(loneD801);
        string nameFFFD = HistoryExport.FileName(replacement);

        Check(nameD800.Contains("~") && nameD801.Contains("~") && nameFFFD.Contains("~")
           && nameD800 != nameD801 && nameD800 != nameFFFD && nameD801 != nameFFFD,
              "[LS-014] long lone-surrogate ids hash exact code units instead of colliding on U+FFFD");

        string huge = new string('z', 5000);
        Check(HistoryExport.FileName(huge).Length <= 142,
              "[LS-014] FileName stays at or below 142 chars even for a 5000-char id");
    }

    private static void TrailingDotAndSpaceAreSafe()
    {
        string dir = NewHistoryDir("dot-space");
        try
        {
            string dotted = "trailing.";
            string spaced = "trailing ";
            string dottedName = HistoryExport.FileName(dotted);
            string spacedName = HistoryExport.FileName(spaced);

            Check(dottedName == "luanshi_history_trailing%002E.jsonl"
               && spacedName == "luanshi_history_trailing%0020.jsonl",
                  "[LS-014] trailing dot and space are escaped instead of relying on Windows trimming");
            string pathDot = HistoryExport.Write(dir, new Observation { SeatId = dotted });
            string pathSpace = HistoryExport.Write(dir, new Observation { SeatId = spaced });
            Check(pathDot != null && pathSpace != null && pathDot != pathSpace
               && File.Exists(pathDot) && File.Exists(pathSpace),
                  "[LS-014] trailing dot/space ids write as two distinct files");
            Check(SameName(dotted, "trailing") == false && SameName(spaced, "trailing") == false,
                  "[LS-014] trailing dot/space cannot collapse onto the untrimmed id");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static void FileNameIsSafe()
    {
        string dir = NewHistoryDir("names");
        try
        {
            string[] ids =
            {
                "../evil", "..\\evil", "a/b", "a:b", "seat with space", "seat_under",
                "員外", "..", ".", "CON", "NUL", "", null,
            };
            bool prefixed = true, noSeparators = true, noTraversal = true, inside = true;

            foreach (string id in ids)
            {
                string name = HistoryExport.FileName(id);
                prefixed &= name.StartsWith("luanshi_history_") && name.EndsWith(".jsonl");
                noSeparators &= !name.Contains("/") && !name.Contains("\\") && !name.Contains(":");
                noTraversal &= !name.Contains("..") && name != ".." && name != ".";
                string path = HistoryExport.Write(dir, new Observation { SeatId = id });
                inside &= path != null
                       && string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)),
                                        Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
            }

            Check(prefixed, "[LS-014] every seat filename keeps the fixed prefix and .jsonl suffix");
            Check(noSeparators, "[LS-014] separators and ':' are escaped out of seat filenames");
            Check(noTraversal, "[LS-014] '..' and '.' seat ids cannot traverse directories");
            Check(inside, "[LS-014] every encoded seat file stays inside the export directory");
        }
        finally
        {
            DeleteHistoryDir(dir);
        }
    }

    private static void FileNameCollisions()
    {
        Check(!SameName("Seat-A", "seat-a"),
              "[LS-014] ids differing only by case map to different names (case-insensitive comparison)");
        Check(!SameName("seat_a", "seat-a"),
              "[LS-014] '_' and '-' in ids map to different names");
        Check(!SameName("A", "%41"),
              "[LS-014] escape-looking text cannot collide with an encoded id");
        Check(!SameName("a/b", "a%2Fb"),
              "[LS-014] a separator id cannot collide with literal escape text");
        Check(!SameName("", "empty"),
              "[LS-014] the empty-id token cannot collide with a real seat id");
        Check(!SameName(null, ""),
              "[LS-014] null and empty ids never share a filename");
    }
}
