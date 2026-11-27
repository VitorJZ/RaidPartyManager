using RaidPartyManager.Content;
using RaidPartyManager.Domain;
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
        private readonly HangingSign[] serviceSigns = new HangingSign[4];
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
            stage = new TownStage(transform, root, stageArt, run.InGraveyard, run.InCapital);
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
            for (int i = 0; i < serviceSigns.Length; i++)
                if (serviceSigns[i]?.panel != null) stage.PlaceService(serviceSigns[i], i);
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
            var title = PrototypeStyle.Text($"{run.RegionName} · 城镇", 26, PrototypeStyle.Accent);
            title.style.marginLeft = 16; title.style.flexGrow = 1; header.Add(title);
            town.Add(header);
            AdventureFlow.AddServiceNavigation(town, topBar);
            var services = new VisualElement { name = "town-services", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(services);
            town.Insert(0,services);
            Service(services, "人才市场", TownService.Recruit, 0,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("TalentMarket", true)));
            Service(services, "商店", TownService.Shop, 1,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("DockShop", true)));
            Service(services, "营地", TownService.Camp, 2,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("Camp", true)));
            Service(services, "委托", TownService.Quest, 3, OpenQuest);
            FirstRunGuide.ShowFacility(town, run, GuideStep.Town,
                (serviceSigns[0], run.Party.ContractsEnabled ? "签约 · 卖人" : "招募 · 卖人"), (serviceSigns[1], "买卖装备"),
                (serviceSigns[2], "每次进城用一次"), (serviceSigns[3], "接活 · 回城领赏"));
            if (string.IsNullOrEmpty(run.Current.Message)) return;
            var notice = PrototypeStyle.Text(run.Current.Message, 17, PrototypeStyle.Accent);
            notice.pickingMode = PickingMode.Ignore;
            notice.name = "town-message"; notice.style.position = Position.Absolute;
            notice.style.left = notice.style.right = 28; notice.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            PrototypeStyle.Middle(notice);
            notice.style.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 0.88f);
            notice.style.paddingLeft = notice.style.paddingRight = 12; notice.style.paddingTop = notice.style.paddingBottom = 9;
            town.Add(notice);
        }

        private void OpenQuest()
        {
            town.Clear();
            void ReturnToTown() => topBar.WhenRewardsArrive(() => {
                Render();
                serviceSigns[(int)TownService.Quest].Focus();
            });
            // The board's own close ring returns to town, so no exit sign hangs above it.
            var board = new QuestBoard($"{run.RegionName} · 委托", ReturnToTown);
            board.style.paddingTop = 18;
            board.style.paddingBottom = 10;
            board.style.top = RunTopBar.Height;
            if (run.QuestActive)
            {
                board.Accepted(run.Commission, run.QuestProgress, run.TownQuestGold, () => {
                    if (topBar.CollectReward(board.Q("quest-payout"), run.ClaimCommission)) OpenQuest();
                });
            }
            else
            {
                int order = 0;
                foreach (var job in run.Current.Commissions)
                    board.Offer(job, run.CommissionReward(job), order++, () => {
                        if (run.AcceptCommission(job.Id)) OpenQuest();
                    });
            }
            town.Add(board);
        }

        private void Service(VisualElement parent, string title, TownService service, int index, System.Action click)
        {
            // The same hanging sign as everywhere else; its board hangs in the scenery and mirrors it.
            var button = new HangingSign("town-service-" + service, title, click);
            serviceSigns[index] = button;
            stage.MountService(button, index);
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.RegisterCallback<GeometryChangedEvent>(evt =>
                button.style.fontSize = Mathf.Clamp(evt.newRect.height * .52f, 9, 26));
            stage.PlaceService(button, index);
            button.SetEnabled(run.CanUseTown(service));
            parent.Add(button);
        }
    }
}
