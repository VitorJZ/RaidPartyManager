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
            stage = new CampStage(transform, root, stageArt, fireArtwork);
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
            else AdventureFlow.AddServiceNavigation(root, stageArt.timber, bar);
            RunTopBar.PlaceAtTop(root, bar);
            var actions = new VisualElement { name = "camp-actions" };
            actions.style.position = Position.Absolute;
            actions.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            actions.style.left = Length.Percent(21);
            actions.style.right = Length.Percent(21);
            var buttons = PrototypeStyle.Row();
            actions.Add(buttons);
            AddChoice("招募", "camp-recruit", ShowRecruits, adventure.CampRecruits.Count > 0);
            AddChoice("升阶", "camp-evolve", () => AdventureWidgets.EvolutionChoices(OpenChoice("升阶"), adventure, content,
                adventure.EvolveAtCamp, CompleteChoice), adventure.EvolutionCandidates.Count > 0);
            AddChoice("休息", "camp-rest", () => { if (adventure.Rest()) Render(); });
            var message = PrototypeStyle.Text(string.IsNullOrEmpty(adventure.Current.Message)
                ? "任选一项。休息为在场的存活队员恢复 30% 最大生命。" : adventure.Current.Message, 14, PrototypeStyle.Ink);
            message.style.backgroundColor = new Color(0, 0, 0, 0.65f);
            PrototypeStyle.Middle(message);
            actions.Add(message);
            root.Add(actions);

            void AddChoice(string title, string id, System.Action action, bool available = true)
            {
                var button = PrototypeStyle.Button(title, id, action);
                button.style.minHeight = 42;
                button.style.flexGrow = 1;
                button.style.flexBasis = 0;
                button.style.marginLeft = button.style.marginRight = 4;
                button.SetEnabled(adventure.CanUseCamp && available);
                buttons.Add(button);
            }
        }

        private void ShowRecruits()
        {
            var choices = OpenChoice("招募一位同伴");
            foreach (var candidate in adventure.CampRecruits)
                AdventureWidgets.Character(choices, candidate, content, "camp-recruit-" + candidate.Id, () =>
                {
                    if (adventure.RecruitAtCamp(candidate.Id))
                    {
                        CompleteChoice();
                        return;
                    }

                    choices.Clear();
                    AdventureWidgets.RecruitReplacement(choices, candidate, run, content, member =>
                    {
                        if (adventure.RecruitAtCamp(candidate.Id, member))
                            CompleteChoice();
                    });
                    AdventureWidgets.Action(choices, "返回招募名单", "camp-recruit-back", () =>
                    {
                        root.Q("camp-choice").RemoveFromHierarchy();
                        ShowRecruits();
                    });
                });
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
