using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class DockTown : MonoBehaviour
    {
        [SerializeField] private AdventureContentAsset assets;
        private AdventureRun run;
        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            run = AdventureFlow.ForScene("Town") ?? AdventureFlow.Select(assets, true);
            if (run.Current?.Kind != NodeKind.Town) run.Inspect(NodeKind.Town);
            Render();
        }

        private void OnDisable() => PrototypeStyle.ClearDocument(root, localFont, localFontAsset);

        private void Render()
        {
            root.Clear();
            root.style.backgroundImage = new StyleBackground(assets.TownBackdrop);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.backgroundColor = PrototypeStyle.Background;
            var header = PrototypeStyle.Row();
            header.style.position = Position.Absolute; header.style.top = RunTopBar.Height + 7; header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            header.Add(AdventureFlow.ReturnButton());
            var title = PrototypeStyle.Text("弥雾码头 · 城镇", 26, PrototypeStyle.Accent);
            title.style.marginLeft = 16; title.style.flexGrow = 1; header.Add(title);
            var status = PrototypeStyle.Text($"已使用 {run.Current.UsedServices.Count}／2 项服务", 17);
            status.name = "town-services-count"; status.style.backgroundColor = PrototypeStyle.PanelColor;
            status.style.paddingLeft = status.style.paddingRight = 12; header.Add(status); root.Add(header);
            var services = PrototypeStyle.Row("town-services");
            services.style.position = Position.Absolute; services.style.left = services.style.right = 22; services.style.bottom = 34;
            root.Add(services);
            Service(services, "招募", "邀请一名初始形态伙伴", TownService.Recruit, NodeKind.Event, Recruit);
            Service(services, "商店", "进入商店，拖拽购买物品", TownService.Shop, NodeKind.Shop,
                () => AdventureFlow.OpenNodeScene("DockShop", true));
            Service(services, "休息", "恢复每名在场队员最大生命的 30%", TownService.Camp, NodeKind.Camp,
                () => AdventureFlow.OpenNodeScene("Camp", true));
            Service(services, "委托", run.QuestActive ? $"战斗胜利 {run.QuestWins}／2 · 完成可领取 15 金币" : "接下两场战斗的委托", TownService.Quest, NodeKind.Event,
                () => { run.TownQuest(); Render(); }, !run.QuestActive || run.QuestWins >= 2);
            var notice = PrototypeStyle.Text(string.IsNullOrEmpty(run.Current.Message) ? "城镇里可以选择两项不同服务。完成后返回事件选择继续前进。" : run.Current.Message,
                17, PrototypeStyle.Accent);
            notice.name = "town-message"; notice.style.position = Position.Absolute;
            notice.style.left = notice.style.right = 28; notice.style.bottom = 214;
            notice.style.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 0.88f);
            notice.style.paddingLeft = notice.style.paddingRight = 12; notice.style.paddingTop = notice.style.paddingBottom = 9;
            root.Add(notice);
            var content = assets.Encounter;
            var topBar = new RunTopBar(root, run.Party, content,
                () => RunTopBar.EditParty(root, run.Party, content, () => root.Q<RunTopBar>().Refresh()), "open-town-editor");
            RunTopBar.PlaceAtTop(root, topBar);
        }

        private void Service(VisualElement parent, string title, string description, TownService service, NodeKind icon,
            System.Action click, bool available = true)
        {
            bool used = run.Current.UsedServices.Contains(service);
            var button = PrototypeStyle.Button("", "town-service-" + service, click);
            button.style.width = Length.Percent(24); button.style.flexGrow = 1; button.style.marginRight = 10;
            button.style.height = 164; button.style.paddingTop = 9;
            button.style.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 0.95f);
            var image = new Image { image = assets.NodeIcon(icon), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.height = 66; button.Add(image);
            var heading = PrototypeStyle.Text(title + (used ? " · 已使用" : ""), 23, PrototypeStyle.Accent);
            PrototypeStyle.Middle(heading); button.Add(heading);
            var words = PrototypeStyle.Text(description, 13, PrototypeStyle.Muted); PrototypeStyle.Middle(words); button.Add(words);
            button.SetEnabled(available && run.CanUseTown(service)); parent.Add(button);
        }

        private void Recruit()
        {
            var overlay = new VisualElement { name = "town-recruit-window" };
            PrototypeStyle.Fill(overlay); overlay.style.top = RunTopBar.Height;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.8f);
            overlay.style.alignItems = Align.Center; overlay.style.justifyContent = Justify.Center;
            var panel = PrototypeStyle.Panel(); panel.style.width = Length.Percent(70); panel.style.maxWidth = 760;
            panel.style.height = Length.Percent(88); overlay.Add(panel);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; panel.Add(scroll);
            scroll.Add(PrototypeStyle.Text("伙伴招募", 25, PrototypeStyle.Accent));
            void Refresh()
            {
                if (run.Current.PendingRecruit == null) Render();
                else { overlay.RemoveFromHierarchy(); Recruit(); }
            }
            var content = assets.Encounter;
            if (run.Current.PendingRecruit != null)
                AdventureWidgets.RecruitReplacement(scroll, run, content, Refresh);
            else
            {
                var image = new Image { image = assets.EventArt(AdventureEventKind.LostTraveler), scaleMode = ScaleMode.ScaleToFit };
                image.style.height = 220; scroll.Add(image);
                foreach (var recruit in run.Current.Recruits)
                    AdventureWidgets.Character(scroll, recruit, content, "town-recruit-" + recruit.Id, () =>
                    { run.ChooseTownRecruit(recruit.Id); Refresh(); });
            }
            AdventureWidgets.Action(scroll, "返回城镇", "close-town-recruit", () => overlay.RemoveFromHierarchy());
            root.Add(overlay);
        }
    }
}
