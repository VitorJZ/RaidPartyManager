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
            AdventureFlow.AddServiceNavigation(town, stageArt.graveyardStone, topBar);
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
            var board = ContractPanel.Sheet($"{run.RegionName}委托", out var paper);
            var back = HangingSign.Exit("close-town-quest", () => topBar.WhenRewardsArrive(() => {
                Render();
                serviceSigns[(int)TownService.Quest].Focus();
            }), belowTopBar: true);
            back.tooltip = "下拉返回城镇";
            back.style.right = StyleKeyword.Auto;
            back.style.left = Length.Percent(9);
            board.Add(new StageSignMask(back));
            board.name = "town-quest-board";
            board.style.top = RunTopBar.Height;
            paper.Add(ContractPanel.Text("战斗胜利时记录进度。完成后可在任意城镇交付。", 14));
            if (run.QuestActive)
            {
                var job = run.Commission;
                paper.Add(ContractPanel.Text($"{job.Rank} · {job.Title}", 19));
                paper.Add(ContractPanel.Text($"{job.Objective}　{run.QuestProgress}/{job.Target}", 16));
                var claim = PrototypeStyle.Button($"领取报酬 · {run.TownQuestGold} 金币", "town-quest-act", () => {
                    if (topBar.CollectReward(board.Q<Button>("town-quest-act"), run.ClaimCommission)) OpenQuest();
                }, true);
                claim.SetEnabled(run.QuestProgress >= job.Target);
                paper.Add(claim);
            }
            else
            {
                foreach (var job in run.Current.Commissions)
                {
                    var accept = PrototypeStyle.Button("", "town-quest-offer-" + job.Difficulty, () => {
                        if (run.AcceptCommission(job.Id)) OpenQuest();
                    });
                    accept.style.alignItems = Align.FlexStart;
                    accept.Add(PrototypeStyle.Text($"{job.Rank} · {job.Title}　{run.CommissionReward(job)} 金币", 16, PrototypeStyle.Accent));
                    accept.Add(PrototypeStyle.Text(job.Objective, 14));
                    paper.Add(accept);
                }
            }
            town.Add(board);
        }

        private void Service(VisualElement parent, string title, TownService service, int index, System.Action click)
        {
            var button = PrototypeStyle.Button(title, "town-service-" + service, click);
            serviceSigns[index] = button;
            button.style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(button, Color.clear, 0);
            button.style.marginTop = button.style.marginBottom = button.style.marginLeft = button.style.marginRight = 0;
            button.style.paddingTop = button.style.paddingBottom = button.style.paddingLeft = button.style.paddingRight = 0;
            button.style.minHeight = 0;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            Color lettering = new Color32(255, 229, 171, 255);
            button.style.color = lettering;
            button.RegisterCallback<GeometryChangedEvent>(evt =>
                button.style.fontSize = Mathf.Clamp(evt.newRect.height * .52f, 9, 26));
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.color = Color.white);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.color = lettering);
            stage.PlaceService(button, index);
            button.SetEnabled(run.CanUseTown(service));
            parent.Add(button);
        }
    }
}
