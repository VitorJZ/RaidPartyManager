using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PrototypeSceneMenu : MonoBehaviour
    {
        [SerializeField] private bool showDestinations;

        private static AsyncOperation loadingScene;
        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;

        internal static Button ReturnButton() =>
            PrototypeStyle.Button("返回主菜单", "return-main-menu", () => OpenScene("MainMenu"));

        private static void OpenScene(string destination)
        {
            if (loadingScene == null || loadingScene.isDone)
                loadingScene = SceneManager.LoadSceneAsync(destination);
        }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);

            if (showDestinations)
                ShowMainMenu();
            else
                ShowReturnButton();
        }

        private void OnDisable()
        {
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void ShowMainMenu()
        {
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;

            var panel = PrototypeStyle.Panel();
            panel.name = "main-menu";
            panel.style.width = 400;
            panel.style.maxWidth = Length.Percent(90);
            panel.style.paddingTop = panel.style.paddingBottom = 28;
            panel.style.paddingLeft = panel.style.paddingRight = 30;

            var title = PrototypeStyle.Text("讨伐队经理", 34, PrototypeStyle.Accent);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 22;
            PrototypeStyle.Middle(title);
            panel.Add(title);

            var battleButton = PrototypeStyle.Button("战斗", "open-battle",
                () => OpenScene("BattlePrototype"), true);
            battleButton.style.minHeight = 46;
            panel.Add(battleButton);

            var templateButton = PrototypeStyle.Button("模板", "open-template", () => OpenScene("SampleScene"));
            templateButton.style.minHeight = 46;
            templateButton.style.marginTop = 10;
            panel.Add(templateButton);

            root.Add(panel);
        }

        private void ShowReturnButton()
        {
            root.pickingMode = PickingMode.Ignore;

            var button = ReturnButton();
            button.style.position = Position.Absolute;
            button.style.top = 16;
            button.style.right = 22;
            button.style.width = 120;
            button.style.minHeight = 36;
            root.Add(button);
        }
    }
}
