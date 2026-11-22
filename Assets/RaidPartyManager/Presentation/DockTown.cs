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
            stage = new TownStage(transform, root, stageArt, run.InGraveyard);
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
            var title = PrototypeStyle.Text($"{run.RegionName} · 城镇", 26, PrototypeStyle.Accent);
            title.style.marginLeft = 16; title.style.flexGrow = 1; header.Add(title);
            town.Add(header);
            AdventureFlow.AddServiceNavigation(town, stageArt.timber, topBar);
            var services = new VisualElement { name = "town-services", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(services);
            town.Insert(0,services);
            Service(services, "人才市场", "查看挂牌队员，洽谈合约", TownService.Recruit, 0,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("TalentMarket", true)));
            Service(services, "商店", DockShop.ServiceDescription, TownService.Shop, 1,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("DockShop", true)));
            Service(services, "营地", "本镇可升阶或休息一次", TownService.Camp, 2,
                () => topBar.WhenRewardsArrive(() => AdventureFlow.OpenNodeScene("Camp", true)));
            Service(services, "委托", run.QuestActive ? $"战斗胜利 {Mathf.Min(run.QuestWins, AdventureRun.QuestWinTarget)}／{AdventureRun.QuestWinTarget}" : $"查看{run.RegionName}委托", TownService.Quest, 3, OpenQuest);
            if (string.IsNullOrEmpty(run.Current.Message)) return;
            var notice = PrototypeStyle.Text(run.Current.Message, 17, PrototypeStyle.Accent);
            notice.name = "town-message"; notice.style.position = Position.Absolute;
            notice.style.left = notice.style.right = 28; notice.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            PrototypeStyle.Middle(notice);
            notice.style.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 0.88f);
            notice.style.paddingLeft = notice.style.paddingRight = 12; notice.style.paddingTop = notice.style.paddingBottom = 9;
            town.Add(notice);
        }

        private void OpenQuest()
        {
            var board = new VisualElement { name = "town-quest-board" };
            PrototypeStyle.Fill(board);
            board.style.backgroundColor = new Color(.05f, .05f, .06f, .78f);
            board.style.justifyContent = Justify.Center;
            board.style.alignItems = Align.Center;
            var paper = PrototypeStyle.Panel();
            paper.style.width = Length.Percent(85);
            paper.style.maxWidth = 420;
            paper.style.paddingLeft = paper.style.paddingRight = 20;
            paper.style.paddingTop = paper.style.paddingBottom = 16;
            paper.Add(PrototypeStyle.Text($"{run.RegionName}委托", 22, PrototypeStyle.Accent));
            paper.Add(PrototypeStyle.Text($"赢得 {AdventureRun.QuestWinTarget} 场战斗后，回到任意城镇领取报酬。", 16));
            if (run.QuestActive)
                paper.Add(PrototypeStyle.Text($"已胜 {Mathf.Min(run.QuestWins, AdventureRun.QuestWinTarget)}／{AdventureRun.QuestWinTarget} 场", 16));
            paper.Add(PrototypeStyle.Text($"当前报酬　{run.TownQuestGold} 金币", 16, PrototypeStyle.Accent));
            var action = PrototypeStyle.Button(run.QuestActive ? "领取报酬" : "接受委托", "town-quest-act", () => {
                if (topBar.CollectReward(board.Q<Button>("town-quest-act"), run.TownQuest)) Render();
            }, true);
            action.SetEnabled(!run.QuestActive || run.QuestWins >= AdventureRun.QuestWinTarget);
            paper.Add(action);
            paper.Add(PrototypeStyle.Button("返回城镇", "close-town-quest", board.RemoveFromHierarchy));
            board.Add(paper);
            town.Add(board);
        }

        private void Service(VisualElement parent, string title, string description, TownService service, int index,
            System.Action click)
        {
            bool available = run.CanUseTown(service);
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
            var heading = PrototypeStyle.Text(title + (service == TownService.Camp && !available ? " · 已使用" : ""), 20, PrototypeStyle.Accent);
            PrototypeStyle.Middle(heading); sign.Add(heading);
            var words = PrototypeStyle.Text(description, 12, PrototypeStyle.Ink); PrototypeStyle.Middle(words); sign.Add(words);
            button.Add(sign);
            button.RegisterCallback<PointerEnterEvent>(_ => sign.style.backgroundColor = new Color(.25f,.19f,.11f,.96f));
            button.RegisterCallback<PointerLeaveEvent>(_ => sign.style.backgroundColor = new Color(.085f,.075f,.065f,.92f));
            button.SetEnabled(available); parent.Add(button);
        }
    }
}
