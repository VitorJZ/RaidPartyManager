using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace RaidPartyManager.Editor
{
    internal static class PrototypePlayModeEntry
    {
        [InitializeOnLoadMethod]
        private static void ConfigureOnLoad()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, scene) => SetStartScene(scene);
            EditorApplication.delayCall += () => SetStartScene(SceneManager.GetActiveScene());
        }

        private static void SetStartScene(Scene scene)
        {
            // Unity's Play Mode tests start in an unsaved bootstrap scene.
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(scene.path) ? null :
                AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        }
    }
}
