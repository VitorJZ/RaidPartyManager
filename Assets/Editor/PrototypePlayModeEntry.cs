using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace RaidPartyManager.Editor
{
    internal static class PrototypePlayModeEntry
    {
        [InitializeOnLoadMethod]
        private static void ConfigureOnLoad() => EditorApplication.playModeStateChanged += SetStartScene;

        private static void SetStartScene(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;

            // Unity Test Framework saves its temporary runner scene as Assets/InitTestScene<guid>.unity.
            var path = SceneManager.GetActiveScene().path;
            bool isTestBootstrap = path.StartsWith("Assets/InitTestScene", StringComparison.Ordinal);
            EditorSceneManager.playModeStartScene = isTestBootstrap ? null :
                AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        }
    }
}
