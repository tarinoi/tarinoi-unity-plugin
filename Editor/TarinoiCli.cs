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
        /// <summary>
        /// Points the project at a Tarinoi project, creating the settings asset if needed.
        /// </summary>
        /// <remarks>
        /// Every other entry point here needs an API path, and until this existed the only
        /// way to set one was the Project Settings window — so a CI job or a scripted
        /// first-run could not get started at all.
        /// <code>
        /// Unity -batchmode -quit -projectPath . \
        ///   -executeMethod Tarinoi.Editor.TarinoiCli.Configure \
        ///   -tarinoiApiPath https://…/documents
        /// </code>
        /// The API token is deliberately not settable this way: it lives outside the
        /// project so it cannot be committed, and a token on a command line ends up in
        /// shell history and CI logs.
        /// </remarks>
        public static void Configure()
        {
            var apiPath = Argument("-tarinoiApiPath");
            if (string.IsNullOrEmpty(apiPath))
            {
                Fail("Configure needs -tarinoiApiPath <documents endpoint>.");
                return;
            }

            if (string.IsNullOrEmpty(TarinoiSettings.ProjectIdFromApiPath(apiPath)))
            {
                Fail($"'{apiPath}' is not a documents endpoint — it should end in /documents.");
                return;
            }

            var settings = TarinoiSettingsProvider.LoadOrCreate();
            settings.apiPath = apiPath;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            TarinoiSettings.ClearCache();

            TarinoiLog.Info($"Tarinoi: project set to '{settings.ProjectId}'.");
        }

        /// <summary>Reads a value that follows the given flag on the command line.</summary>
        static string Argument(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

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
                        if (!BindingCodegen.Write(model, settings.codegenOutputPath, settings.ProjectId,
                                settings.codegenAsmdef))
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
