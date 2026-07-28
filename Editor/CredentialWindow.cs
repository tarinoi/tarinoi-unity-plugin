using Tarinoi.Sync;
using UnityEditor;
using UnityEngine;

namespace Tarinoi.Editor
{
    /// <summary>
    /// Prompts for the Tarinoi API token.
    /// </summary>
    /// <remarks>
    /// The token goes straight to the credentials file outside the project. It is never
    /// held in a settings asset, never written to the scene, and the field is masked —
    /// tokens leak most often by being committed, so the only copy lives outside version
    /// control.
    /// </remarks>
    sealed class CredentialWindow : EditorWindow
    {
        string _token = "";

        internal static void Show()
        {
            var window = CreateInstance<CredentialWindow>();
            window.titleContent = new GUIContent("Tarinoi API token");
            window.minSize = new Vector2(420, 150);
            window.maxSize = new Vector2(560, 150);
            window.ShowUtility();
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Paste the API token from your Tarinoi project's "
                                       + "Integrations page.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            GUI.SetNextControlName("token");
            _token = EditorGUILayout.PasswordField("Token", _token);

            EditorGUILayout.LabelField(" ",
                $"Saved to {Credentials.FilePath}", EditorStyles.miniLabel);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(90)))
                {
                    Close();
                }

                using (new EditorGUI.DisabledScope(_token.Trim().Length == 0))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(90)))
                    {
                        if (Credentials.Write(Credentials.ApiKeyName, _token.Trim()))
                        {
                            TarinoiLog.Info("Tarinoi: API token saved.");
                        }

                        // Don't leave the token sitting in memory once it's stored.
                        _token = "";
                        Close();
                    }
                }
            }
        }
    }
}
