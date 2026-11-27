using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class CampPrototype : MonoBehaviour
    {
        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private ServiceStageArt stageArt;
        [SerializeField] private Texture2D fireArtwork;

        private VisualElement root;
        private CampStage stage;
        private EncounterData content;
        private PartyRun run;
        private Font localFont;
        private FontAsset localFontAsset;
        private AdventureRun adventure;
        private bool standalone;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            content = encounter.ToData();
            adventure = AdventureFlow.ForScene("Camp");
            standalone = adventure == null;
            run = adventure?.Party ?? BattlePrototype.GetPlaySession(content);
            if (standalone)
            {
                adventure = new AdventureRun(run, content.CharacterCatalog, content.StartingEquipment, true);
                adventure.Inspect(NodeKind.Camp);
            }
            stage = new CampStage(transform, root, stageArt, fireArtwork, adventure.InGraveyard, adventure.InCapital);
            Render();
        }

        private void OnDisable()
        {
            stage?.Dispose();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            root.Clear();
            root.style.backgroundColor = Color.clear;
            root.style.overflow = Overflow.Hidden;
            stage.SeatParty(run, content);

            var header = PrototypeStyle.Row("camp-header");
            header.style.position = Position.Absolute;
            header.style.top = RunTopBar.Height + 7;
            header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            header.pickingMode = PickingMode.Ignore;
            var title = PrototypeStyle.Text("营地", 24, PrototypeStyle.Accent);
            title.style.marginLeft = 18;
            title.style.flexGrow = 1;
            header.Add(title);
            root.Add(header);
            var bar = new RunTopBar(root, run, content, OpenEditor, "open-camp-editor");
            if (standalone) root.Add(new StageSignMask(HangingSign.Exit("leave-camp", () => PrototypeSceneMenu.OpenStandalone("MainMenu"))));
            else AdventureFlow.AddServiceNavigation(root, bar);
            RunTopBar.PlaceAtTop(root, bar);
            var actions = new VisualElement { name = "camp-actions" };
            actions.style.position = Position.Absolute;
            actions.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            actions.style.left = Length.Percent(20);
            actions.style.right = Length.Percent(20);
            actions.pickingMode = PickingMode.Ignore;
            var plaques = PrototypeStyle.Row();
            plaques.pickingMode = PickingMode.Ignore;
            actions.Add(plaques);
            int ascending = adventure.EvolutionCandidates.Count, training = adventure.TrainingCandidates.Count;
            AddChoice("升阶", "camp-evolve", "EmblemAscend", () => AdventureWidgets.EvolutionChoices(OpenChoice("升阶"), adventure, content,
                adventure.EvolveAtCamp, CompleteChoice), ascending > 0, ascending > 0 ? $"{ascending} 名队员可以升阶" : "暂无可升阶的队员");
            AddChoice("休息", "camp-rest", "EmblemRest", () => { if (adventure.Rest()) { GameAudio.Play(SoundCue.CampRest); Render(); } },
                true, "全队恢复 30% 最大生命");
            AddChoice("训练", "camp-train", "EmblemTrain", OpenTraining, training > 0,
                training > 0 ? "一名队员随机属性永久提升" : "队员的训练次数已用完");
            actions.Add(new CampNote("camp-message", string.IsNullOrEmpty(adventure.Current.Message)
                ? "篝火旁只能做一件事：休息、训练或升阶，任选其一。" : adventure.Current.Message));
            root.Add(actions);
            FirstRunGuide.ShowFacility(root, adventure, GuideStep.Camp);

            void AddChoice(string title, string id, string emblem, System.Action action, bool available, string note)
            {
                var plaque = new CampPlaque(id, title, Resources.Load<Texture2D>("Camp/" + emblem), () => {
                    root.Q("camp-guide")?.RemoveFromHierarchy();
                    action();
                });
                plaque.style.height = 70;
                plaque.style.flexGrow = 1;
                plaque.style.flexBasis = 0;
                plaque.Offer(adventure.CanUseCamp && available, note);
                plaques.Add(plaque);
            }
        }

        private void OpenTraining()
        {
            var list = OpenChoice("选择训练队员");
            list.Add(PrototypeStyle.Text("本次随机提升一项属性（攻击、物防、特防、最大生命或能量上限）。每人最多训练两次。", 14, PrototypeStyle.Muted));
            foreach (var member in adventure.TrainingCandidates)
            {
                var button = AdventureWidgets.Character(list, member.Character, content, $"train-{member.Id}", () =>
                {
                    if (adventure.TrainAtCamp(member.Id)) CompleteChoice();
                }, member: member);
                button.text += $"  ·  剩余 {CampTraining.Limit - member.Training.Sessions} 次";
            }
        }

        private ScrollView OpenChoice(string title)
        {
            var overlay = new VisualElement { name = "camp-choice" };
            PrototypeStyle.Fill(overlay);
            overlay.style.top = RunTopBar.Height;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.72f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            var panel = PrototypeStyle.Panel();
            panel.style.width = Length.Percent(80);
            panel.style.maxWidth = 530;
            panel.style.maxHeight = Length.Percent(86);
            panel.Add(PrototypeStyle.Text(title, 23, PrototypeStyle.Accent));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexShrink = 1;
            panel.Add(scroll);
            var actions = root.Q("camp-actions");
            actions.SetEnabled(false);
            var close = AdventureWidgets.Action(panel, "返回篝火", "close-camp-choice", () =>
            {
                overlay.RemoveFromHierarchy();
                actions.SetEnabled(true);
            });
            overlay.Add(panel);
            root.Add(overlay);
            close.Focus();
            return scroll;
        }

        private void CompleteChoice()
        {
            if (standalone)
                BattlePrototype.MarkPartyEdited();
            Render();
        }

        private void OpenEditor()
        {
            root.Q<RunTopBar>().EditParty(Render, standalone ? BattlePrototype.MarkPartyEdited : null);
        }

        private void Update()
        {
            stage.FrameCamera();
            stage.Animate(Time.unscaledTime);
        }
    }
}
