using System;
using System.IO;
using Tarinoi.Data;
using Tarinoi.Sync;
using UnityEngine;

namespace Tarinoi.Editor
{
    /// <summary>
    /// Copies the synced content into <c>StreamingAssets</c> so a build can play it
    /// without contacting the API.
    /// </summary>
    public static class SnapshotExporter
    {
        /// <summary>
        /// Writes a snapshot for a project. Returns false and logs on failure.
        /// </summary>
        /// <remarks>
        /// The API path and sync cursor are stripped from the copy. They are development
        /// configuration, and a shipped build has no business carrying the endpoint it was
        /// authored against — the token is elsewhere and never travels, but the URL would
        /// otherwise be a needless disclosure.
        /// </remarks>
        public static bool Export(string projectId)
        {
            var source = TarinoiDb.PathForProject(projectId);
            if (!File.Exists(source))
            {
                TarinoiLog.Error($"Tarinoi: there is no local content for '{projectId}' to export. "
                                 + "Sync first.");
                return false;
            }

            var targetDirectory = Path.Combine(Application.streamingAssetsPath,
                SnapshotSeeder.SnapshotFolder);
            var target = Path.Combine(targetDirectory, projectId + ".db");

            try
            {
                Directory.CreateDirectory(targetDirectory);

                // Checkpoint through a normal open/close so the copy contains everything:
                // in WAL mode recent writes can still be sitting in the -wal file.
                using (var db = new TarinoiDb())
                {
                    if (!db.Open(projectId))
                    {
                        return false;
                    }
                }

                File.Copy(source, target, true);

                using (var snapshot = new TarinoiDb())
                {
                    if (!snapshot.OpenAtPath(target))
                    {
                        TarinoiLog.Error("Tarinoi: exported the snapshot but could not clean it up.");
                        return false;
                    }

                    snapshot.DeleteMeta(TarinoiDb.ApiPathKey, TarinoiDb.ApiSyncCursorKey);
                }

                var sizeKb = new FileInfo(target).Length / 1024;
                TarinoiLog.Info($"Tarinoi: exported a {sizeKb} KB snapshot to {target}. "
                                + "Enable offline mode in Project Settings → Tarinoi to play it.");
                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Tarinoi: could not export a snapshot: {e.Message}");
                return false;
            }
        }
    }
}
