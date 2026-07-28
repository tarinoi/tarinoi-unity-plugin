using System.Collections.Generic;
using System.IO;
using Tarinoi.Sync;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tarinoi.Editor
{
    /// <summary>
    /// The <b>Project Settings → Tarinoi</b> page.
    /// </summary>
    /// <remarks>
    /// Creates the settings asset on demand, so a new project does not have to know that
    /// a <c>Resources</c> asset is what backs this page.
    /// </remarks>
    static class TarinoiSettingsProvider
    {
        const string AssetPath = "Assets/Resources/" + TarinoiSettings.ResourceName + ".asset";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Tarinoi", SettingsScope.Project)
            {
                label = "Tarinoi",
                guiHandler = _ => DrawSettings(),
                keywords = new HashSet<string>
                {
                    "tarinoi", "dialogue", "narrative", "api", "sync", "bindings", "codegen",
                },
            };
        }

        static void DrawSettings()
        {
            var settings = LoadOrCreate();
            var serialized = new SerializedObject(settings);
            serialized.Update();

            EditorGUILayout.Space();

            DrawApiSection(serialized, settings);
            EditorGUILayout.Space();
            DrawCodegenSection(serialized);
            EditorGUILayout.Space();
            DrawBehaviourSection(serialized);

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(settings);
                TarinoiSettings.ClearCache();
            }
        }

        static void DrawApiSection(SerializedObject serialized, TarinoiSettings settings)
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("apiPath"),
                new GUIContent("API path", "Your project's documents endpoint, from Tarinoi."));

            var projectId = settings.ProjectId;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("Project",
                    "Derived from the API path. Names the local content database."),
                    string.IsNullOrEmpty(projectId) ? "—" : projectId);
            }

            if (!string.IsNullOrEmpty(settings.apiPath) && string.IsNullOrEmpty(projectId))
            {
                EditorGUILayout.HelpBox(
                    "That does not look like a documents endpoint. It should end in /documents.",
                    MessageType.Warning);
            }

            // The token itself is never shown or stored here — only whether one exists.
            var hasToken = Credentials.Has(Credentials.ApiKeyName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("API token",
                    hasToken ? "Saved" : "Not set", EditorStyles.miniLabel);

                if (GUILayout.Button(hasToken ? "Change…" : "Set…", GUILayout.Width(80)))
                {
                    CredentialWindow.Show();
                }

                using (new EditorGUI.DisabledScope(!hasToken))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    {
                        Credentials.Clear(Credentials.ApiKeyName);
                    }
                }
            }

            EditorGUILayout.LabelField(" ",
                "Stored outside the project, so it is never committed or shipped.",
                EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(serialized.FindProperty("pollEnabled"),
                new GUIContent("Re-sync while playing",
                    "Pick up authored changes without leaving play mode."));

            if (serialized.FindProperty("pollEnabled").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serialized.FindProperty("pollInterval"),
                    new GUIContent("Every (seconds)"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(serialized.FindProperty("skipTlsVerify"),
                new GUIContent("Skip TLS verification",
                    "Development only. Disables certificate checking."));

            if (serialized.FindProperty("skipTlsVerify").boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Certificate verification is off. Only use this against a local "
                    + "development server, and never ship a build with it enabled.",
                    MessageType.Warning);
            }
        }

        static void DrawCodegenSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Generated bindings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("codegenOutputPath"),
                new GUIContent("Output folder"));
            EditorGUILayout.PropertyField(serialized.FindProperty("codegenOnSync"),
                new GUIContent("Regenerate after sync"));
        }

        static void DrawBehaviourSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("committedOnly"),
                new GUIContent("Committed content only",
                    "Hide uncommitted edits — preview what a player would see."));
            EditorGUILayout.PropertyField(serialized.FindProperty("offlineMode"),
                new GUIContent("Offline mode",
                    "Play the snapshot bundled in StreamingAssets and never contact the API."));
            EditorGUILayout.PropertyField(serialized.FindProperty("logLevel"),
                new GUIContent("Log level"));
            EditorGUILayout.PropertyField(serialized.FindProperty("dataProvider"),
                new GUIContent("Custom document store",
                    "Optional. Assembly-qualified type name of an IDocumentStore implementation."));
        }

        /// <summary>Loads the settings asset, creating it the first time it is needed.</summary>
        internal static TarinoiSettings LoadOrCreate()
        {
            var existing = Resources.Load<TarinoiSettings>(TarinoiSettings.ResourceName);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory("Assets/Resources");
            var created = ScriptableObject.CreateInstance<TarinoiSettings>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            TarinoiSettings.ClearCache();

            TarinoiLog.Info($"Tarinoi: created settings at {AssetPath}.");
            return created;
        }
    }
}
