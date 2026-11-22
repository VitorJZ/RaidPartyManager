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
        [SerializeField] private Texture2D menuBackdrop;
        private IVisualElementScheduledItem sceneryPlayback;

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
            sceneryPlayback?.Pause();
            sceneryPlayback = null;
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void ShowMainMenu()
        {
            root.style.backgroundColor = PrototypeStyle.Background;
            if (menuBackdrop != null)
            {
                sceneryPlayback = SceneryFlipbook.Animate(root, menuBackdrop);
                root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                root.style.unityBackgroundImageTintColor = new Color(.47f, .47f, .50f);
            }
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

            var routeButton = PrototypeStyle.Button(AdventureFlow.HasSave ? "继续讨伐" : "开始讨伐",
                "open-route", AdventureFlow.OpenRoute, true);
            routeButton.style.minHeight = 46;
            panel.Add(routeButton);

            var battleButton = PrototypeStyle.Button("战斗", "open-battle", () => OpenStandalone("BattlePrototype"));
            battleButton.style.minHeight = 46;
            battleButton.style.marginTop = 10;
            panel.Add(battleButton);

            var eventsButton = PrototypeStyle.Button("事件选择", "open-events", AdventureFlow.OpenHub);
            eventsButton.style.minHeight = 46;
            eventsButton.style.marginTop = 10;
            panel.Add(eventsButton);

            var settingsButton = PrototypeStyle.Button("设置", "open-settings", null);
            settingsButton.clicked += OpenSettings;
            settingsButton.style.minHeight = 36;
            settingsButton.style.marginTop = 10;
            panel.Add(settingsButton);
            root.Add(panel);

            void OpenSettings()
            {
                panel.SetEnabled(false);
                new SettingsPanel(() =>
                {
                    panel.SetEnabled(true);
                    settingsButton.Focus();
                }).Show(root);
            }
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
