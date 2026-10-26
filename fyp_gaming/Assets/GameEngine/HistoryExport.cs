using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Player-facing history export (LS-014): one JSONL file per seat, containing exactly that seat's
    /// filtered <see cref="Observation.Events"/>. The canonical <see cref="EventLog"/> is never written
    /// here; it stays available to experiments through the explicit engine/harness API only.
    ///
    /// Trusted-host boundary: per-seat filenames keep hotseat players from overwriting one another's
    /// exports, but they are not access control and the files are not sealed. Anyone sharing the OS
    /// account can open every file in the output directory. This class guarantees that each artifact
    /// contains its named seat's permitted history; it does not isolate a hostile local user.
    /// </summary>
    public static class HistoryExport
    {
        private const string Prefix = "luanshi_history_";
        private const string Suffix = ".jsonl";

        // Encode never emits '_' or '~', so these fixed tokens cannot collide with any encoded id.
        private const string NullSeatToken = "_n";
        private const string EmptySeatToken = "_e";

        // If the encoded body is longer than this, keep a readable prefix and append a hash. The
        // resulting filename component is 80 + '~' + 32 = 113 chars, so even with the fixed prefix
        // and suffix the whole name stays well below common 255-byte filename limits.
        private const int MaxEncodedBodyLength = 120;
        private const int HashPrefixLength = 80;
        private const int HashHexLength = 32;

        /// <summary>
        /// JSONL for the seat's own projected history: one <see cref="EventView.ToJson"/> record per line
        /// in observation order, with a trailing newline like <see cref="EventLog.ToJsonLines"/>. A null
        /// observation or an empty history yields "" (not a lone newline).
        /// </summary>
        public static string ToJsonLines(Observation obs)
        {
            if (obs == null || obs.Events.Count == 0) return "";
            var sb = new StringBuilder(obs.Events.Count * 256);
            foreach (var rec in obs.Events) sb.Append(rec.ToJson()).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// A safe filename component for any .NET string, including lone surrogates.
        /// Lowercase ASCII letters, digits and '-' are kept verbatim; every other UTF-16 code unit —
        /// including uppercase letters, '_', '.', path separators, ':' and non-ASCII — becomes
        /// '%' followed by four uppercase hex digits. Encoding code units (rather than UTF-8 bytes)
        /// makes the mapping defined and injective for every <see cref="string"/>, even malformed ones,
        /// while escaping uppercase letters keeps distinct ids distinct on case-insensitive filesystems.
        ///
        /// Null maps to "_n" and the empty string to "_e"; '_' is never emitted by the encoder, so the
        /// two tokens are distinct from each other and from every encoded id. If the encoded body exceeds
        /// 120 chars, the name uses its first 80 chars, '~', and the first 32 lowercase hex digits of the
        /// SHA-256 of the full id's exact UTF-16LE code units ('~' is never emitted by the encoder). That
        /// capped form is fixed-length, distinct from every uncapped form, and injective subject to
        /// SHA-256 collision resistance over those code units rather than mathematically injective.
        /// Uncapped names are injective for every <see cref="string"/>.
        /// The fixed "luanshi_history_" prefix also keeps reserved Windows device names (CON, NUL, ...)
        /// out of the result: the full filename is never exactly a device name.
        /// </summary>
        public static string FileName(string seatId)
        {
            string body;
            if (seatId == null)
                body = NullSeatToken;
            else if (seatId.Length == 0)
                body = EmptySeatToken;
            else
            {
                body = Encode(seatId);
                if (body.Length > MaxEncodedBodyLength)
                    body = body.Substring(0, HashPrefixLength) + "~" + HashSuffix(seatId);
            }
            return Prefix + body + Suffix;
        }

        /// <summary>
        /// Writes obs to <paramref name="directory"/> using the seat encoded from
        /// <see cref="Observation.SeatId"/> — never from a model/user-supplied filename. The directory is
        /// created if missing. A short temp file named ".luanshi-&lt;guid&gt;.tmp" (independent of the
        /// final name, so the temp path cannot approach filename limits when the seat id is long) is
        /// written first and then replaced/moved into place, so an interrupted write does not leave a
        /// partial export. Returns the full path, or null when obs (or a usable directory) is missing.
        /// I/O failures propagate to the caller; the court report catches and logs them so a disk error
        /// cannot abort the report or season rollover. This method never reads or writes the legacy
        /// canonical "luanshi_log.jsonl".
        /// </summary>
        public static string Write(string directory, Observation obs)
        {
            if (obs == null || string.IsNullOrEmpty(directory)) return null;

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName(obs.SeatId));
            string temp = Path.Combine(directory, ".luanshi-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                // Fixed UTF-8 (no BOM) keeps the bytes identical for the same filtered history.
                File.WriteAllText(temp, ToJsonLines(obs), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return path;
        }

        /// <summary>
        /// Percent-encodes the UTF-16 code units of <paramref name="text"/>. Each code unit is emitted
        /// either raw when it is [a-z0-9-], or as '%' plus four uppercase hex digits. Because '%' itself
        /// is escaped, the encoding is injective (including for lone surrogates and other malformed
        /// strings) and safe for Windows/POSIX filenames.
        /// </summary>
        private static string Encode(string text)
        {
            var sb = new StringBuilder(text.Length * 5);
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%');
                    sb.Append(((int)c).ToString("X4"));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// First 32 hex digits (128 bits) of SHA-256 over the id's exact UTF-16LE code units.
        /// The bytes are written manually because <see cref="Encoding.Unicode"/> uses a replacement
        /// fallback that maps every lone surrogate to U+FFFD, which would collide distinct ids.
        /// </summary>
        private static string HashSuffix(string text)
        {
            var bytes = new byte[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bytes[2 * i] = (byte)(c & 0xFF);
                bytes[2 * i + 1] = (byte)(c >> 8);
            }
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(HashHexLength);
            for (int i = 0; i < hash.Length && sb.Length < HashHexLength; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
