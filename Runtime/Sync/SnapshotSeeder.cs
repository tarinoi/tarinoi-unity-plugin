using System;
using System.IO;
using System.Threading.Tasks;
using Tarinoi.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace Tarinoi.Sync
{
    /// <summary>
    /// Seeds the working database from a snapshot shipped inside the build.
    /// </summary>
    /// <remarks>
    /// Offline mode plays content bundled at build time and never contacts the API.
    /// The snapshot lives in <c>StreamingAssets/tarinoi/</c>, which is read-only and on
    /// some platforms isn't even a real file path — SQLite needs a writable file, so the
    /// snapshot is copied to <see cref="Application.persistentDataPath"/> before opening.
    /// <para>
    /// The copy is unconditional by default, matching the Godot plugin: the snapshot is
    /// the source of truth in offline mode, and the local database is only ever a cache
    /// of it. Nothing of value is lost by overwriting.
    /// </para>
    /// </remarks>
    public static class SnapshotSeeder
    {
        public const string SnapshotFolder = "tarinoi";

        /// <summary>Where a project's bundled snapshot lives inside the build.</summary>
        public static string SourcePath(string projectId) =>
            Path.Combine(Application.streamingAssetsPath, SnapshotFolder, projectId + ".db");

        /// <summary>
        /// True when the platform exposes StreamingAssets as a plain file path. Android
        /// (inside the APK) and WebGL (over HTTP) do not, and need a web request instead.
        /// </summary>
        public static bool StreamingAssetsIsFilePath =>
            !Application.streamingAssetsPath.Contains("://");

        /// <summary>
        /// Copies the bundled snapshot into place. Returns false when no snapshot is
        /// bundled for the project, which callers should treat as a configuration error
        /// in offline mode.
        /// </summary>
        public static async Task<bool> SeedAsync(string projectId, bool overwriteExisting = true)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                TarinoiLog.Error("SnapshotSeeder: cannot seed without a project id");
                return false;
            }

            var target = TarinoiDb.PathForProject(projectId);
            if (!overwriteExisting && File.Exists(target))
            {
                return true;
            }

            var source = SourcePath(projectId);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");

                byte[] bytes;
                if (StreamingAssetsIsFilePath)
                {
                    if (!File.Exists(source))
                    {
                        TarinoiLog.Error(
                            $"SnapshotSeeder: offline mode is on but no snapshot exists at '{source}'. "
                            + "Run Tools > Tarinoi > Snapshot for Export before building.");
                        return false;
                    }

                    bytes = File.ReadAllBytes(source);
                }
                else
                {
                    bytes = await ReadViaWebRequestAsync(source);
                    if (bytes == null)
                    {
                        return false;
                    }
                }

                // Stale journal files would be interpreted against the new database.
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(target + suffix))
                    {
                        File.Delete(target + suffix);
                    }
                }

                File.WriteAllBytes(target, bytes);
                TarinoiLog.Info($"SnapshotSeeder: seeded '{projectId}' from the bundled snapshot "
                                + $"({bytes.Length / 1024} KB)");
                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"SnapshotSeeder: could not seed '{projectId}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads a StreamingAssets file on platforms where it isn't a filesystem path.
        /// </summary>
        static Task<byte[]> ReadViaWebRequestAsync(string url)
        {
            var completion = new TaskCompletionSource<byte[]>();
            var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();

            operation.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        TarinoiLog.Error(
                            $"SnapshotSeeder: could not read the bundled snapshot at '{url}': {request.error}");
                        completion.SetResult(null);
                        return;
                    }

                    completion.SetResult(request.downloadHandler.data);
                }
                finally
                {
                    request.Dispose();
                }
            };

            return completion.Task;
        }
    }
}
