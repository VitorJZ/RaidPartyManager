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
            PrototypeStyle.Button("返回主菜单", "return-main-menu", () => OpenStandalone("MainMenu"));

        internal static void OpenScene(string destination)
        {
            if (loadingScene == null || loadingScene.isDone)
                loadingScene = SceneManager.LoadSceneAsync(destination);
        }

        internal static void OpenStandalone(string destination)
        {
            AdventureFlow.ClearSceneContext();
            OpenScene(destination);
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
                () => OpenStandalone("BattlePrototype"), true);
            battleButton.style.minHeight = 46;
            panel.Add(battleButton);

            var templateButton = PrototypeStyle.Button("模板", "open-template", () => OpenStandalone("SampleScene"));
            templateButton.style.minHeight = 46;
            templateButton.style.marginTop = 10;
            panel.Add(templateButton);

            var shopButton = PrototypeStyle.Button("商店", "open-shop", () => OpenStandalone("DockShop"));
            shopButton.style.minHeight = 46;
            shopButton.style.marginTop = 10;
            panel.Add(shopButton);

            var campButton = PrototypeStyle.Button("营地", "open-camp", () => OpenStandalone("Camp"));
            campButton.style.minHeight = 46;
            campButton.style.marginTop = 10;
            panel.Add(campButton);

            var eventsButton = PrototypeStyle.Button("事件选择", "open-events", AdventureFlow.OpenHub);
            eventsButton.style.minHeight = 46;
            eventsButton.style.marginTop = 10;
            panel.Add(eventsButton);

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
