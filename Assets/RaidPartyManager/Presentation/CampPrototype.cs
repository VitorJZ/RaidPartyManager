using System.Collections.Generic;
using System.Linq;
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
        private static readonly (int Position, Vector2 Hip)[] Seats =
        {
            (3, new Vector2(0.35f, 0.36f)),
            (2, new Vector2(0.65f, 0.36f)),
            (4, new Vector2(0.225f, 0.50f)),
            (1, new Vector2(0.775f, 0.50f))
        };

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private Texture2D backdrop;
        [SerializeField] private Texture2D fireArtwork;

        private readonly List<(Image View, Vector2 Hip)> figures = new List<(Image, Vector2)>();
        private VisualElement root;
        private VisualElement stage;
        private Image fire;
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
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Render();
        }

        private void OnDisable()
        {
            root?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            root.Clear();
            figures.Clear();
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.overflow = Overflow.Hidden;
            stage = new VisualElement { name = "camp-stage", pickingMode = PickingMode.Ignore };
            stage.style.position = Position.Absolute;
            stage.style.backgroundImage = new StyleBackground(backdrop);
            stage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.Add(stage);

            var party = run.CaptureFormation(Side.Party);
            foreach (var seat in Seats)
            {
                var member = party.FirstOrDefault(unit => unit.Position == seat.Position);
                if (member == null)
                    continue;
                var art = content.CampSpriteFor(member.Character.Id);
                var view = new Image
                {
                    name = $"camp-seat-{seat.Position}",
                    image = art,
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore
                };
                view.style.position = Position.Absolute;
                view.style.scale = new Scale(new Vector3(seat.Position <= 2 ? -1f : 1f, 1f, 1f));
                figures.Add((view, seat.Hip));
                stage.Add(view);
            }

            fire = new Image
            {
                name = "camp-fire", image = fireArtwork, scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            fire.style.position = Position.Absolute;
            stage.Add(fire);

            var header = PrototypeStyle.Row("camp-header");
            header.style.position = Position.Absolute;
            header.style.top = RunTopBar.Height + 7;
            header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            if (!standalone)
                header.Add(AdventureFlow.ReturnButton());
            var title = PrototypeStyle.Text("营地", 24, PrototypeStyle.Accent);
            title.style.marginLeft = 18;
            title.style.flexGrow = 1;
            header.Add(title);
            root.Add(header);
            RunTopBar.PlaceAtTop(root, new RunTopBar(root, run, content, OpenEditor, "open-camp-editor"));
            var actions = new VisualElement { name = "camp-actions" };
            actions.style.position = Position.Absolute;
            actions.style.bottom = 20;
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
            FitStage();

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

        private void OnGeometryChanged(GeometryChangedEvent evt) => FitStage();

        private void FitStage()
        {
            if (stage == null || backdrop == null || root.contentRect.width <= 0 || root.contentRect.height <= 0)
                return;
            float ratio = (float)backdrop.width / backdrop.height;
            float width = Mathf.Min(root.contentRect.width, root.contentRect.height * ratio);
            float height = width / ratio;
            stage.style.width = width;
            stage.style.height = height;
            stage.style.left = (root.contentRect.width - width) * 0.5f;
            stage.style.top = (root.contentRect.height - height) * 0.5f;
            foreach (var figure in figures)
            {
                float size = width * 0.22f;
                figure.View.style.width = figure.View.style.height = size;
                figure.View.style.left = width * figure.Hip.x - size * 0.5f;
                figure.View.style.top = height * figure.Hip.y - size * 0.56f;
            }
            float fireSize = width * 0.17f;
            fire.style.width = fire.style.height = fireSize;
            fire.style.left = width * 0.5f - fireSize * 0.5f;
            fire.style.top = height * 0.73f - fireSize * 0.82f;
        }

        private void Update()
        {
            if (fire?.panel == null)
                return;
            float time = Time.unscaledTime;
            fire.style.scale = new Scale(new Vector3(1f + Mathf.Sin(time * 6.3f) * 0.012f,
                1f + Mathf.Sin(time * 8.1f) * 0.025f, 1f));
            float light = 0.96f + Mathf.Sin(time * 7.3f) * 0.04f;
            fire.tintColor = new Color(1f, light, light, 1f);
        }
    }
}
