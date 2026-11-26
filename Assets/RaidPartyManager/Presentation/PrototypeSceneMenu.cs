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
        private Font noticeFont;
        private FontAsset noticeFontAsset;

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
            {
                string serif = System.Array.Find(Font.GetOSInstalledFontNames(), name => name == "Songti SC" ||
                    name == "STSong" || name == "SimSun" || name == "Noto Serif CJK SC" || name == "Source Han Serif SC");
                noticeFont = serif == null ? null : Font.CreateDynamicFontFromOSFont(serif, 32);
                noticeFontAsset = noticeFont == null ? null : FontAsset.CreateFontAsset(noticeFont);
                ShowMainMenu();
            }
            else
                ShowReturnButton();
        }

        private void OnDisable()
        {
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
            if (noticeFontAsset != null) Destroy(noticeFontAsset);
            if (noticeFont != null) Destroy(noticeFont);
            noticeFont = null;
            noticeFontAsset = null;
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
                float fade = width / height > 1.25f ? width * .62f : width;
                mesh.SetAllVertices(new[]
                {
                    ShadeVertex(0, 0, new Color32(9, 14, 23, 170)), ShadeVertex(0, height, new Color32(9, 14, 23, 120)),
                    ShadeVertex(fade * .45f, 0, new Color32(9, 14, 23, 110)),
                    ShadeVertex(fade * .45f, height, new Color32(9, 14, 23, 30)),
                    ShadeVertex(fade, 0, new Color32(9, 14, 23, 0)),
                    ShadeVertex(fade, height, new Color32(9, 14, 23, 0))
                });
                mesh.SetAllIndices(new ushort[] { 0, 2, 1, 2, 3, 1, 2, 4, 3, 4, 5, 3 });
            };
            root.Add(shade);
            root.Add(MenuVignette());

            var title = new MainMenuLogo();
            root.Add(title);

            string detail = null;
            if (company.Day > 0)
            {
                string region = ((company.Floor - 1) % 3) switch
                {
                    1 => "低语墓园", 2 => "机巧遗都", _ => "弥雾码头"
                };
                detail = $"{region} · 第 {company.Day} 天";
            }

            var panel = new MainMenuBoard();
            if (noticeFontAsset != null)
                panel.style.unityFontDefinition = FontDefinition.FromSDFFont(noticeFontAsset);
            bool resuming = AdventureFlow.HasSave || company.Day > 0;
            panel.AddCommission(resuming ? "继续讨伐" : "开始讨伐", "open-route", AdventureFlow.OpenRoute, detail,
                resuming ? "MenuWordContinue" : "MenuWordStart");
            panel.AddNotice("战斗", "open-battle", () => OpenStandalone("BattlePrototype"), folded: false, "MenuWordBattle");
            panel.AddNotice("事件选择", "open-events", AdventureFlow.OpenHub, folded: true, "MenuWordEvents");
            var settingsButton = panel.AddTag("设置", "open-settings", null, "MenuWordSettings");
            settingsButton.clicked += OpenSettings;
            root.Add(panel);
            root.RegisterCallback<GeometryChangedEvent>(FitMenu);
            panel.RegisterCallback<DetachFromPanelEvent>(_ => root.UnregisterCallback<GeometryChangedEvent>(FitMenu));

            void FitMenu(GeometryChangedEvent evt)
            {
                float width = evt.newRect.width, height = evt.newRect.height;
                bool wide = width / height >= 1.25f;
                panel.Fit(width, height, wide);
                float logoWidth = wide ? Mathf.Min(width * .46f, height * 1.1f) : width * .8f;
                title.Fit(logoWidth);
                title.style.left = wide ? width * .515f : (width - logoWidth) / 2;
                title.style.top = height * (wide ? .075f : .04f);
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

        private static VisualElement MenuVignette()
        {
            var vignette = new VisualElement { name = "main-menu-vignette", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(vignette);
            vignette.generateVisualContent += context =>
            {
                float width = vignette.layout.width, height = vignette.layout.height;
                var clear = new Color32(6, 9, 15, 0);
                var mesh = context.Allocate(8, 12);
                mesh.SetAllVertices(new[]
                {
                    ShadeVertex(0, 0, new Color32(6, 9, 15, 150)), ShadeVertex(width, 0, new Color32(6, 9, 15, 150)),
                    ShadeVertex(0, height * .2f, clear), ShadeVertex(width, height * .2f, clear),
                    ShadeVertex(0, height * .86f, clear), ShadeVertex(width, height * .86f, clear),
                    ShadeVertex(0, height, new Color32(6, 9, 15, 175)), ShadeVertex(width, height, new Color32(6, 9, 15, 175))
                });
                mesh.SetAllIndices(new ushort[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 5, 7, 6 });
            };
            return vignette;
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
