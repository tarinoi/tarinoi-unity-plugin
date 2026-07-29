using System.IO;
using Tarinoi.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tarinoi.Editor
{
    /// <summary>
    /// Creates a scene that plays Tarinoi content, for people who would rather click a
    /// menu item than assemble one.
    /// </summary>
    public static class QuickstartScene
    {
        const string ScenePath = "Assets/Tarinoi Quickstart.unity";

        [MenuItem("Tools/Tarinoi/Create Quickstart Scene", priority = 80)]
        public static void Create()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog(
                    "Replace the quickstart scene?",
                    $"{ScenePath} already exists. Replacing it discards any changes you made.",
                    "Replace", "Cancel"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single);

            var host = new GameObject("Tarinoi");
            host.AddComponent<TarinoiQuickstart>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // Put it in the build settings so pressing Play in a fresh project does
            // something useful.
            AddToBuildSettings(ScenePath);

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(ScenePath));
            TarinoiLog.Info($"Tarinoi: created {ScenePath}. Press Play to try your content.");
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var scene in scenes)
            {
                if (scene.path == path)
                {
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(path, true);
            EditorBuildSettings.scenes = updated;
        }

        /// <summary>Command-line entry point, for scripted setup.</summary>
        public static void CreateFromCli()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single);

            new GameObject("Tarinoi").AddComponent<TarinoiQuickstart>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.Refresh();

            TarinoiLog.Info($"Tarinoi: created {ScenePath}.");
        }
    }
}
