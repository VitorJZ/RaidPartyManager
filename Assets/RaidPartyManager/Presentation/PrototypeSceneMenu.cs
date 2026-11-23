using System.Linq;
using RaidPartyManager.Content;
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
        [SerializeField] private EncounterAsset menuEncounter;

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

        internal static void AfterSceneLoad(System.Action callback)
        {
            if (loadingScene != null && !loadingScene.isDone)
                loadingScene.completed += _ => callback();
            else
                callback();
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
            var content = menuEncounter != null ? menuEncounter.ToData() : null;
            var company = AdventureFlow.MenuCompany();
            var cast = company.Members?.Length > 0 ? company.Members :
                content?.Party.OrderBy(unit => unit.Position).Take(2).Select(unit => unit.Character.Id).ToArray();
            root.Add(new MainMenuMarch(menuBackdrop, content, cast ?? System.Array.Empty<string>()));

            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(shade);
            shade.generateVisualContent += context =>
            {
                var mesh = context.Allocate(6, 12);
                float width = shade.layout.width, height = shade.layout.height;
                float fade = width / height > 1.25f ? width * .70f : width;
                var black = new Color32(9, 14, 23, 248);
                mesh.SetAllVertices(new[]
                {
                    ShadeVertex(0, 0, black), ShadeVertex(0, height, new Color32(9, 14, 23, 190)),
                    ShadeVertex(fade * .42f, 0, new Color32(9, 14, 23, 222)),
                    ShadeVertex(fade * .42f, height, new Color32(9, 14, 23, 42)),
                    ShadeVertex(fade, 0, new Color32(9, 14, 23, 0)),
                    ShadeVertex(fade, height, new Color32(9, 14, 23, 0))
                });
                mesh.SetAllIndices(new ushort[] { 0, 2, 1, 2, 3, 1, 2, 4, 3, 4, 5, 3 });
            };
            root.Add(shade);

            var panel = new VisualElement { name = "main-menu", pickingMode = PickingMode.Ignore };
            panel.style.position = Position.Absolute;
            panel.style.left = Length.Percent(6);
            panel.style.top = Length.Percent(13);
            panel.style.width = Length.Percent(88);
            panel.style.maxWidth = 510;

            var title = PrototypeStyle.Text("讨伐队经理", 64);
            title.name = "main-menu-title";
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            panel.Add(title);

            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.style.width = 62;
            rule.style.height = 2;
            rule.style.marginTop = 11;
            rule.style.marginBottom = 23;
            rule.style.backgroundColor = PrototypeStyle.Accent;
            panel.Add(rule);

            if (company.Day > 0)
            {
                string region = ((company.Floor - 1) % 3) switch
                {
                    1 => "低语墓园", 2 => "机巧遗都", _ => "弥雾码头"
                };
                var progress = PrototypeStyle.Text($"{region}  ·  第 {company.Day} 天", 15, PrototypeStyle.Accent);
                progress.name = "menu-run-progress";
                progress.style.marginBottom = 9;
                panel.Add(progress);
            }

            var controls = new VisualElement { name = "main-menu-actions", pickingMode = PickingMode.Ignore };
            controls.style.width = Length.Percent(100);
            controls.style.maxWidth = 256;
            panel.Add(controls);
            var routeButton = AddAction(AdventureFlow.HasSave || company.Day > 0 ? "继续讨伐" : "开始讨伐",
                "open-route", AdventureFlow.OpenRoute, primary: true);
            var battleButton = AddAction("战斗", "open-battle", () => OpenStandalone("BattlePrototype"));
            var eventsButton = AddAction("事件选择", "open-events", AdventureFlow.OpenHub);
            var settingsButton = AddAction("设置", "open-settings", null);
            settingsButton.clicked += OpenSettings;
            root.Add(panel);
            root.RegisterCallback<GeometryChangedEvent>(FitMenu);
            panel.RegisterCallback<DetachFromPanelEvent>(_ => root.UnregisterCallback<GeometryChangedEvent>(FitMenu));

            void FitMenu(GeometryChangedEvent evt)
            {
                float width = evt.newRect.width, height = evt.newRect.height;
                bool compact = height < 470;
                panel.style.top = Length.Percent(compact ? 6 : 13);
                title.style.fontSize = Mathf.Clamp(Mathf.Min(width * .107f, height * .124f), 27, 72);
                rule.style.marginBottom = compact ? 11 : 23;
                routeButton.style.height = compact ? 40 : 51;
                battleButton.style.height = eventsButton.style.height = settingsButton.style.height = compact ? 32 : 42;
            }

            Button AddAction(string label, string id, System.Action action, bool primary = false)
            {
                var button = PrototypeStyle.Button(label, id, action, primary);
                Color rest = primary ? new Color32(225, 181, 104, 255) : new Color32(14, 24, 36, 222);
                Color hover = primary ? new Color32(248, 211, 144, 255) : new Color32(52, 49, 48, 250);
                button.style.backgroundColor = rest;
                button.style.color = primary ? (Color)new Color32(26, 19, 14, 255) : PrototypeStyle.Ink;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = 16;
                button.style.fontSize = primary ? 18 : 15;
                button.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
                button.style.height = primary ? 51 : 42;
                button.style.marginBottom = primary ? 11 : 4;
                PrototypeStyle.Outline(button, primary ? PrototypeStyle.Accent : new Color32(105, 101, 91, 220), 3);
                var arrow = PrototypeStyle.Text("→", 18, primary ? PrototypeStyle.Background : PrototypeStyle.Accent);
                arrow.pickingMode = PickingMode.Ignore;
                arrow.style.position = Position.Absolute;
                arrow.style.right = 13;
                arrow.style.top = arrow.style.bottom = 0;
                PrototypeStyle.Middle(arrow);
                button.Add(arrow);
                button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = hover);
                button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = rest);
                button.RegisterCallback<FocusInEvent>(_ => button.style.borderLeftColor = PrototypeStyle.Accent);
                button.RegisterCallback<FocusOutEvent>(_ => button.style.borderLeftColor = primary
                    ? PrototypeStyle.Accent : (Color)new Color32(105, 101, 91, 220));
                controls.Add(button);
                return button;
            }

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

        private static Vertex ShadeVertex(float x, float y, Color32 tint) =>
            new() { position = new Vector3(x, y, Vertex.nearZ), tint = tint };

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
