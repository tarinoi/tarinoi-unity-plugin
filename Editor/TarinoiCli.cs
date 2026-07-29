using System;
using Tarinoi.Data;
using Tarinoi.Editor.Codegen;
using Tarinoi.Sync;
using UnityEditor;

namespace Tarinoi.Editor
{
    /// <summary>
    /// Entry points for driving Tarinoi from the command line.
    /// </summary>
    /// <remarks>
    /// Invoke with <c>-batchmode -executeMethod Tarinoi.Editor.TarinoiCli.&lt;Method&gt;</c>.
    /// Each method blocks until it finishes and sets a non-zero exit code on failure, so a
    /// build script can rely on it. That is why they wait on the task rather than using
    /// <c>async void</c> like the menu items, which would let the editor quit mid-sync.
    /// </remarks>
    public static class TarinoiCli
    {
        /// <summary>Syncs content and regenerates bindings.</summary>
        public static void SyncAndGenerate()
        {
            Run(regenerate: true, sync: true);
        }

        /// <summary>Regenerates bindings from already-synced content, without a network call.</summary>
        public static void Generate()
        {
            Run(regenerate: true, sync: false);
        }

        /// <summary>
        /// Exports the bundled snapshot a build plays from. Required before shipping:
        /// a player has its own storage location and cannot see content synced in the
        /// editor.
        /// </summary>
        public static void ExportSnapshot()
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();
            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                Fail("no API path configured — set it in Project Settings > Tarinoi.");
                return;
            }

            if (!SnapshotExporter.Export(settings.ProjectId))
            {
                Fail("could not export a snapshot.");
                return;
            }

            AssetDatabase.Refresh();
        }

        static void Run(bool regenerate, bool sync)
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();

            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                Fail("no API path configured — set it in Project Settings > Tarinoi.");
                return;
            }

            try
            {
                using (var db = new TarinoiDb { CommittedOnly = settings.committedOnly })
                {
                    if (!db.Open(settings.ProjectId))
                    {
                        Fail($"could not open the local database for '{settings.ProjectId}'.");
                        return;
                    }

                    if (sync)
                    {
                        var apiKey = Credentials.Read(Credentials.ApiKeyName);
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            Fail("no API token saved.");
                            return;
                        }

                        var result = new ApiImporter()
                            .SyncAsync(settings.apiPath, apiKey, db, null, settings.skipTlsVerify)
                            .GetAwaiter().GetResult();

                        if (!result.Success)
                        {
                            Fail(result.Error);
                            return;
                        }

                        TarinoiLog.Info($"Tarinoi: sync complete — {result.Stats}");
                    }

                    if (regenerate)
                    {
                        var model = BindingCodegen.Load(db);
                        if (!BindingCodegen.Write(model, settings.codegenOutputPath, settings.ProjectId))
                        {
                            Fail("could not write the generated bindings.");
                            return;
                        }

                        TarinoiLog.Info($"Tarinoi: wrote bindings to {settings.codegenOutputPath}.");
                    }
                }

                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        static void Fail(string message)
        {
            TarinoiLog.Error("Tarinoi: " + message);
            EditorApplication.Exit(1);
        }
    }
}
