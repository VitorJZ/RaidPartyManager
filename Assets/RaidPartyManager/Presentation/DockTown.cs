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
        [SerializeField] private ServiceStageArt stageArt;
        private TownStage stage;
        private readonly Button[] serviceSigns = new Button[4];
        private AdventureRun run;
        private VisualElement root;
        private VisualElement town;
        private RunTopBar topBar;
        private Font localFont;
        private FontAsset localFontAsset;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            run = AdventureFlow.ForScene("Town") ?? AdventureFlow.Select(assets, true);
            if (run.Current?.Kind != NodeKind.Town) run.Inspect(NodeKind.Town);
            stage = new TownStage(transform, root, stageArt);
            root.style.backgroundColor = Color.clear;
            town = new VisualElement { name = "town-content" };
            PrototypeStyle.Fill(town);
            root.Add(town);
            topBar = new RunTopBar(root, run.Party, assets.Encounter, () => topBar.EditParty(Render), "open-town-editor");
            RunTopBar.PlaceAtTop(root, topBar);
            Render();
        }

        private void OnDisable()
        {
            stage?.Dispose();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Update()
        {
            stage.FrameCamera();
            stage.Animate(Time.unscaledTime);
            for (int i=0;i<serviceSigns.Length;i++)
                stage.PlaceService(serviceSigns[i], i);
        }

        private void Render()
        {
            stage.ShowCompany(run.Party, assets.Encounter);
            town.Clear();
            topBar.Refresh();
            var header = PrototypeStyle.Row();
            header.style.position = Position.Absolute; header.style.top = RunTopBar.Height + 7; header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            header.pickingMode = PickingMode.Ignore;
            var title = PrototypeStyle.Text("弥雾码头 · 城镇", 26, PrototypeStyle.Accent);
            title.style.marginLeft = 16; title.style.flexGrow = 1; header.Add(title);
            var status = PrototypeStyle.Text($"已使用 {run.Current.UsedServices.Count}／2 项服务", 17);
            status.name = "town-services-count"; status.style.backgroundColor = PrototypeStyle.PanelColor;
            status.style.marginRight = Length.Percent(15); status.style.paddingLeft = status.style.paddingRight = 12; header.Add(status); town.Add(header);
            AdventureFlow.AddServiceNavigation(town, stageArt.timber, topBar);
            var services = new VisualElement { name = "town-services", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(services);
            town.Insert(0,services);
            Service(services, "招募", "邀请一名初始形态伙伴", TownService.Recruit, 0, Recruit);
            Service(services, "商店", "进入商店，拖拽购买物品", TownService.Shop, 1,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("DockShop", true)));
            Service(services, "营地", "招募、升阶或休息，三选一", TownService.Camp, 2,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("Camp", true)));
            Service(services, "委托", run.QuestActive ? $"战斗胜利 {run.QuestWins}／2 · 完成可领取 15 金币" : "接下两场战斗的委托", TownService.Quest, 3,
                () =>
                {
                    if (topBar.CollectReward(town.Q("town-service-Quest"), run.TownQuest))
                        Render();
                }, !run.QuestActive || run.QuestWins >= 2);
            var notice = PrototypeStyle.Text(string.IsNullOrEmpty(run.Current.Message) ? "可任选两处设施。" : run.Current.Message,
                17, PrototypeStyle.Accent);
            notice.name = "town-message"; notice.style.position = Position.Absolute;
            notice.style.left = notice.style.right = 28; notice.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            PrototypeStyle.Middle(notice);
            notice.style.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 0.88f);
            notice.style.paddingLeft = notice.style.paddingRight = 12; notice.style.paddingTop = notice.style.paddingBottom = 9;
            town.Add(notice);
        }

        private void Service(VisualElement parent, string title, string description, TownService service, int index,
            System.Action click, bool available = true)
        {
            bool used = run.Current.UsedServices.Contains(service);
            var button = PrototypeStyle.Button("", "town-service-" + service, click);
            serviceSigns[index] = button;
            stage.PlaceService(button, index);
            button.style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(button,Color.clear,0);
            button.style.justifyContent = Justify.FlexEnd;
            var sign = new VisualElement { pickingMode = PickingMode.Ignore };
            sign.style.backgroundColor = new Color(.085f,.075f,.065f,.92f);
            sign.style.borderTopWidth = 2;
            sign.style.borderTopColor = PrototypeStyle.Accent;
            sign.style.paddingTop = sign.style.paddingBottom = 4;
            var heading = PrototypeStyle.Text(title + (used ? " · 已使用" : ""), 20, PrototypeStyle.Accent);
            PrototypeStyle.Middle(heading); sign.Add(heading);
            var words = PrototypeStyle.Text(description, 12, PrototypeStyle.Ink); PrototypeStyle.Middle(words); sign.Add(words);
            button.Add(sign);
            button.RegisterCallback<PointerEnterEvent>(_ => sign.style.backgroundColor = new Color(.25f,.19f,.11f,.96f));
            button.RegisterCallback<PointerLeaveEvent>(_ => sign.style.backgroundColor = new Color(.085f,.075f,.065f,.92f));
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
            town.Add(overlay);
        }
    }
}
