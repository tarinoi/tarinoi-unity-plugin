using System.Collections.Generic;

namespace Tarinoi.Data
{
    /// <summary>
    /// Checks each synced document's <c>data_version</c> against the data format this
    /// package was built for.
    /// </summary>
    /// <remarks>
    /// The Tarinoi data format promises a semver contract:
    /// <list type="bullet">
    /// <item>MAJOR — breaking; this package can no longer read the data. Fatal.</item>
    /// <item>MINOR — additive and backward-compatible. Logged as a warning.</item>
    /// <item>PATCH — cosmetic, no shape change. Logged for debugging only.</item>
    /// </list>
    /// A null or empty <c>data_version</c> means a pre-versioning legacy document and
    /// is not checked.
    /// <para>
    /// One instance is expected to live for the duration of a single sync, so repeated
    /// occurrences of the same version are only logged once. Note that memoization must
    /// keep returning the fatal message for a MAJOR mismatch rather than swallowing it
    /// on the second occurrence.
    /// </para>
    /// </remarks>
    public sealed class DataVersion
    {
        public const string SupportedVersion = "1.0.0";

        /// <summary>Version string → "" (compatible) or the fatal error message.</summary>
        readonly Dictionary<string, string> _logged = new Dictionary<string, string>();

        /// <summary>
        /// Returns an empty string when the version is compatible, unversioned, or
        /// unparseable. Returns a non-empty error message on a MAJOR mismatch, which
        /// callers must treat as fatal and abort the sync.
        /// </summary>
        public string Check(string dataVersion)
        {
            if (string.IsNullOrEmpty(dataVersion))
            {
                return "";
            }

            if (_logged.TryGetValue(dataVersion, out var cached))
            {
                return cached;
            }

            if (!TryParse(dataVersion, out var major, out var minor, out var patch))
            {
                TarinoiLog.Warn($"DataVersion: unparseable data_version '{dataVersion}' — skipping check");
                _logged[dataVersion] = "";
                return "";
            }

            TryParse(SupportedVersion, out var supMajor, out var supMinor, out var supPatch);

            var result = "";
            if (major != supMajor)
            {
                result = $"DataVersion: MAJOR data format mismatch — package supports {SupportedVersion}, "
                         + $"data is {dataVersion}. Update the package.";
                TarinoiLog.Error(result);
            }
            else if (minor != supMinor)
            {
                TarinoiLog.Warn($"DataVersion: minor data format mismatch — package supports {SupportedVersion}, "
                                + $"data is {dataVersion}");
            }
            else if (patch != supPatch)
            {
                TarinoiLog.Debug($"DataVersion: patch data format mismatch — package supports {SupportedVersion}, "
                                 + $"data is {dataVersion}");
            }

            _logged[dataVersion] = result;
            return result;
        }

        /// <summary>Parses "X.Y.Z" into its three integer components.</summary>
        internal static bool TryParse(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(version))
            {
                return false;
            }

            var parts = version.Split('.');
            return parts.Length == 3
                   && TryParseComponent(parts[0], out major)
                   && TryParseComponent(parts[1], out minor)
                   && TryParseComponent(parts[2], out patch);
        }

        /// <summary>
        /// Parses a single unsigned version component. Deliberately stricter than
        /// <c>int.TryParse</c>, which would accept leading signs and surrounding
        /// whitespace that the Godot implementation rejects.
        /// </summary>
        static bool TryParseComponent(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return int.TryParse(text, out value);
        }
    }
}
