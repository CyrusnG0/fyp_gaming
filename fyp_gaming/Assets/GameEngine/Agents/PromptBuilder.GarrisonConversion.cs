using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    public static partial class PromptBuilder
    {
        public const string GarrisonConversionVersionSuffix = "+garrison-conversion-2";
        public const string LegacyGarrisonConversionVersionSuffix = "+garrison-conversion-1";
        public static bool IsGarrisonConversionPromptVersion(string version)
            => version != null && (version.EndsWith(GarrisonConversionVersionSuffix, global::System.StringComparison.Ordinal)
                || version.EndsWith(LegacyGarrisonConversionVersionSuffix, global::System.StringComparison.Ordinal));
        public const string GarrisonConversionAddendum = "\nGARRISON CONVERSION:\n"
            + "- convert_garrison transfers troops from an owned city's garrison into a field army on that city. "
            + "It creates an army if none is there, or reinforces a compatible own army already there.\n"
            + "- Send type=convert_garrison, target_id=<owned city id>, param_a=<positive troop count>. "
            + "The amount cannot exceed the city's garrison. Omit param_b or set it to zero; args must be empty or omitted. "
            + "The engine validates the order and movement restrictions.\n";

        public static string GarrisonConversionHash(string system)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(system));
            var result = new StringBuilder(16);
            for (int i = 0; i < 8; i++) result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}
