using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tarinoi.Data;
using Tarinoi.Editor.Codegen;
using Tarinoi.Sync;
using UnityEditor;
using UnityEngine;

namespace Tarinoi.Editor
{
    /// <summary>
    /// The <b>Tools → Tarinoi</b> menu: sync, generate bindings, export a snapshot, and
    /// clear local content.
    /// </summary>
    public static class TarinoiMenu
    {
        const string Root = "Tools/Tarinoi/";

        // -------------------------------------------------------------------------
        // Sync
        // -------------------------------------------------------------------------

        [MenuItem(Root + "Sync", priority = 0)]
        public static async void Sync()
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();

            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                Complain("Set your project's API path in Project Settings → Tarinoi first.");
                return;
            }

            if (!Credentials.Has(Credentials.ApiKeyName))
            {
                Complain("Save your API token first: Tools → Tarinoi → Set API token…");
                return;
            }

            try
            {
                using (var db = new TarinoiDb { CommittedOnly = settings.committedOnly })
                {
                    if (!db.Open(settings.ProjectId))
                    {
                        Complain($"Could not open the local database for '{settings.ProjectId}'.");
                        return;
                    }

                    var progress = new Progress<SyncProgress>(p =>
                        EditorUtility.DisplayProgressBar("Tarinoi", p.Message, p.Fraction));

                    var result = await new ApiImporter().SyncAsync(
                        settings.apiPath,
                        Credentials.Read(Credentials.ApiKeyName),
                        db,
                        progress,
                        settings.skipTlsVerify);

                    EditorUtility.ClearProgressBar();

                    if (!result.Success)
                    {
                        Complain(result.Error);
                        return;
                    }

                    TarinoiLog.Info($"Tarinoi: sync complete — {result.Stats}");

                    if (settings.codegenOnSync)
                    {
                        GenerateFrom(db, settings);
                    }
                }
            }
            catch (Exception e)
            {
                Complain($"Sync failed: {e.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // -------------------------------------------------------------------------
        // Codegen
        // -------------------------------------------------------------------------

        [MenuItem(Root + "Regenerate Bindings", priority = 20)]
        public static void RegenerateBindings()
        {
            WithDatabase((db, settings) => GenerateFrom(db, settings));
        }

        [MenuItem(Root + "Check Bindings", priority = 21)]
        public static void CheckBindings()
        {
            WithDatabase((db, settings) =>
            {
                var model = BindingCodegen.Load(db);
                var issues = BindingValidator.Validate(model);

                if (issues.Count == 0)
                {
                    TarinoiLog.Info("Tarinoi: bindings match the synced content.");
                    return;
                }

                foreach (var issue in issues.Where(i => i.IsBreaking))
                {
                    TarinoiLog.Error("Tarinoi: " + issue.Message);
                }

                foreach (var issue in issues.Where(i => !i.IsBreaking))
                {
                    TarinoiLog.Warn("Tarinoi: " + issue.Message);
                }

                var breaking = issues.Count(i => i.IsBreaking);
                TarinoiLog.Info($"Tarinoi: {issues.Count} difference(s) from the synced content, "
                                + $"{breaking} of which would break existing code. "
                                + "Run Regenerate Bindings to apply them.");
            });
        }

        static void GenerateFrom(TarinoiDb db, TarinoiSettings settings)
        {
            var model = BindingCodegen.Load(db);
            var output = settings.codegenOutputPath;

            if (string.IsNullOrWhiteSpace(output))
            {
                Complain("No output folder set for generated bindings (Project Settings → Tarinoi).");
                return;
            }

            if (!BindingCodegen.Write(model, output, settings.ProjectId))
            {
                return;
            }

            AssetDatabase.Refresh();

            var counts = $"{model.Functions.Values.Sum(v => v.Count)} function(s), "
                         + $"{model.Variables.Values.Sum(v => v.Count)} variable(s), "
                         + $"{model.Lists.Values.Sum(v => v.Count)} list(s), "
                         + $"{model.Entities.Values.Sum(v => v.Count)} entity/entities";

            TarinoiLog.Info($"Tarinoi: wrote bindings to {output} — {counts}.");

            if (model.IsEmpty)
            {
                TarinoiLog.Warn("Tarinoi: nothing was generated. Sync first, and check that your "
                                + "project declares functions or variables.");
            }
        }

        // -------------------------------------------------------------------------
        // Credentials
        // -------------------------------------------------------------------------

        [MenuItem(Root + "Set API token…", priority = 40)]
        public static void SetApiToken() => CredentialWindow.Show();

        // -------------------------------------------------------------------------
        // Snapshot and cleanup
        // -------------------------------------------------------------------------

        [MenuItem(Root + "Snapshot for Export", priority = 60)]
        public static void SnapshotForExport()
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();
            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                Complain("Set your project's API path in Project Settings → Tarinoi first.");
                return;
            }

            if (SnapshotExporter.Export(settings.ProjectId))
            {
                AssetDatabase.Refresh();
            }
        }

        [MenuItem(Root + "Clear Local Content", priority = 61)]
        public static void ClearLocalContent()
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();
            var projectId = settings.ProjectId;

            if (string.IsNullOrEmpty(projectId))
            {
                Complain("Set your project's API path in Project Settings → Tarinoi first.");
                return;
            }

            var path = TarinoiDb.PathForProject(projectId);
            if (!File.Exists(path))
            {
                TarinoiLog.Info("Tarinoi: there is no local content to clear.");
                return;
            }

            // Destructive and easy to hit by accident from a menu, so confirm — but note
            // that the content is a cache: the next sync restores it.
            if (!EditorUtility.DisplayDialog(
                    "Clear local Tarinoi content?",
                    $"This deletes the downloaded content for '{projectId}'.\n\n"
                    + "Your authored content in Tarinoi is untouched — the next sync "
                    + "downloads it again.",
                    "Clear", "Cancel"))
            {
                return;
            }

            try
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    if (File.Exists(path + suffix))
                    {
                        File.Delete(path + suffix);
                    }
                }

                TarinoiLog.Info($"Tarinoi: cleared local content for '{projectId}'.");
            }
            catch (Exception e)
            {
                Complain($"Could not clear local content: {e.Message}");
            }
        }

        // -------------------------------------------------------------------------

        static void WithDatabase(Action<TarinoiDb, TarinoiSettings> action)
        {
            var settings = TarinoiSettingsProvider.LoadOrCreate();
            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                Complain("Set your project's API path in Project Settings → Tarinoi first.");
                return;
            }

            using (var db = new TarinoiDb { CommittedOnly = settings.committedOnly })
            {
                if (!db.Open(settings.ProjectId))
                {
                    Complain($"Could not open the local database for '{settings.ProjectId}'.");
                    return;
                }

                action(db, settings);
            }
        }

        static void Complain(string message)
        {
            TarinoiLog.Error("Tarinoi: " + message);
            EditorUtility.DisplayDialog("Tarinoi", message, "OK");
        }
    }
}
