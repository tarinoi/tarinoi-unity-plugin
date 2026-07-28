using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tarinoi.Sync
{
    /// <summary>
    /// Stores the Tarinoi API token outside the Unity project directory.
    /// </summary>
    /// <remarks>
    /// The file lives under <see cref="Application.persistentDataPath"/> rather than in
    /// <c>Assets/</c> or a settings asset, so a token cannot be committed to version
    /// control or shipped inside a player build. That placement is the whole point of
    /// this class — the format is deliberately boring.
    /// <para>
    /// The format is one <c>key=value</c> per line. Unknown keys are preserved on
    /// write, so a hand-added entry survives.
    /// </para>
    /// </remarks>
    public static class Credentials
    {
        public const string ApiKeyName = "api_key";

        public static string FilePath =>
            Path.Combine(Application.persistentDataPath, "tarinoi", ".credentials");

        /// <summary>Reads a credential, returning "" when the file or key is absent.</summary>
        public static string Read(string key)
        {
            if (string.IsNullOrEmpty(key) || !File.Exists(FilePath))
            {
                return "";
            }

            var prefix = key.ToLowerInvariant() + "=";
            try
            {
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return line.Substring(prefix.Length).Trim();
                    }
                }
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Credentials: could not read '{FilePath}': {e.Message}");
            }

            return "";
        }

        /// <summary>Writes a credential, leaving any other stored keys untouched.</summary>
        public static bool Write(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            try
            {
                var lines = new List<string>();
                var prefix = key.ToLowerInvariant() + "=";
                var replaced = false;

                if (File.Exists(FilePath))
                {
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0)
                        {
                            continue;
                        }

                        if (line.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
                        {
                            lines.Add(prefix + value);
                            replaced = true;
                        }
                        else
                        {
                            lines.Add(line);
                        }
                    }
                }

                if (!replaced)
                {
                    lines.Add(prefix + value);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                File.WriteAllLines(FilePath, lines);
                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Credentials: could not write '{FilePath}': {e.Message}");
                return false;
            }
        }

        /// <summary>Removes a stored credential. Returns true if one was present.</summary>
        public static bool Clear(string key)
        {
            if (string.IsNullOrEmpty(key) || !File.Exists(FilePath))
            {
                return false;
            }

            try
            {
                var prefix = key.ToLowerInvariant() + "=";
                var kept = new List<string>();
                var removed = false;

                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
                    {
                        removed = true;
                    }
                    else
                    {
                        kept.Add(line);
                    }
                }

                File.WriteAllLines(FilePath, kept);
                return removed;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Credentials: could not update '{FilePath}': {e.Message}");
                return false;
            }
        }

        /// <summary>Whether a non-empty value is stored for a key.</summary>
        public static bool Has(string key) => !string.IsNullOrEmpty(Read(key));
    }
}
