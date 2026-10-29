using System;
using System.IO;
using System.Security.Cryptography;

namespace WrathLifecycleFix
{
    internal static class GameCompatibility
    {
        // The exact Assembly-CSharp used for static analysis and the recorded 2.7.0x runs.
        internal const string SupportedSha256 = "2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953";
        internal static bool Matches(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
                return string.Equals(BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", ""), SupportedSha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
