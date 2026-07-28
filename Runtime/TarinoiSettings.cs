using UnityEngine;

namespace Tarinoi
{
    /// <summary>
    /// Project-wide Tarinoi configuration. Edited under <b>Project Settings → Tarinoi</b>
    /// and loaded at runtime from <c>Resources</c>.
    /// </summary>
    /// <remarks>
    /// This is the Unity equivalent of the Godot plugin's <c>tarinoi/*</c> ProjectSettings
    /// entries. Secrets are deliberately absent: the API token lives outside the project
    /// directory (see the credentials store) so it can never be committed or shipped in
    /// a build.
    /// </remarks>
    public sealed class TarinoiSettings : ScriptableObject
    {
        /// <summary>Resource name, and therefore the required asset filename.</summary>
        public const string ResourceName = "TarinoiSettings";

        [Header("API")]
        [Tooltip("Full documents endpoint for your project, ending in /documents.")]
        public string apiPath = "";

        [Tooltip("Skip TLS certificate validation. For local development against a self-signed host only.")]
        public bool skipTlsVerify;

        [Tooltip("Re-sync periodically while playing in the editor, so authored changes appear without a restart.")]
        public bool pollEnabled;

        [Tooltip("Seconds between polls when polling is enabled.")]
        [Min(1)]
        public int pollInterval = 10;

        [Header("Codegen")]
        [Tooltip("Where generated binding classes are written.")]
        public string codegenOutputPath = "Assets/Tarinoi/Generated";

        [Tooltip("Regenerate bindings automatically after every successful sync.")]
        public bool codegenOnSync;

        [Header("Behaviour")]
        [Tooltip("Show only committed content, hiding uncommitted edits — what a player would see.")]
        public bool committedOnly;

        [Tooltip("How much Tarinoi writes to the console.")]
        public TarinoiLogLevel logLevel = TarinoiLogLevel.Info;

        [Tooltip("Play from the bundled snapshot in StreamingAssets and never contact the API.")]
        public bool offlineMode;

        [Tooltip("Optional assembly-qualified type name of a custom IDocumentStore implementation.")]
        public string dataProvider = "";

        static TarinoiSettings _instance;

        /// <summary>
        /// The project's settings asset, or a transient default when none exists yet.
        /// Never returns null, so callers don't need null checks before the user has
        /// created the asset.
        /// </summary>
        public static TarinoiSettings Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = Resources.Load<TarinoiSettings>(ResourceName);
                if (_instance == null)
                {
                    _instance = CreateInstance<TarinoiSettings>();
                    _instance.name = ResourceName + " (defaults)";
                }

                return _instance;
            }
        }

        /// <summary>
        /// Drops the cached instance so the next access reloads from Resources. Called
        /// by the editor after the asset is created or reimported.
        /// </summary>
        public static void ClearCache()
        {
            _instance = null;
        }

        /// <summary>
        /// True when the project has a real settings asset rather than the transient
        /// defaults. The editor uses this to prompt for first-time setup.
        /// </summary>
        public static bool AssetExists => Resources.Load<TarinoiSettings>(ResourceName) != null;

        /// <summary>
        /// Derives the project id from <see cref="apiPath"/>: the last path segment
        /// before a trailing <c>/documents</c>. Returns "" when the path isn't set or
        /// doesn't have that shape.
        /// </summary>
        public string ProjectId => ProjectIdFromApiPath(apiPath);

        /// <summary>
        /// Extracts the project id from a documents endpoint URL. Tolerates a trailing
        /// slash and a missing <c>/documents</c> suffix, mirroring the Godot plugin.
        /// </summary>
        public static string ProjectIdFromApiPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            var trimmed = path.Trim().TrimEnd('/');

            const string documentsSuffix = "/documents";
            if (trimmed.EndsWith(documentsSuffix, System.StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - documentsSuffix.Length);
            }

            trimmed = trimmed.TrimEnd('/');

            // Drop the scheme before splitting, so the "//" in "https://" isn't mistaken
            // for path structure — otherwise a bare host reads as a project id.
            var schemeEnd = trimmed.IndexOf("://", System.StringComparison.Ordinal);
            if (schemeEnd >= 0)
            {
                trimmed = trimmed.Substring(schemeEnd + 3);
            }

            var segments = trimmed.Split('/');

            // A host alone is not a project path; there must be at least one segment
            // beneath it.
            return segments.Length >= 2 ? segments[segments.Length - 1] : "";
        }
    }
}
