using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class PartyEditorView : VisualElement
    {
        private readonly PartyRun run;
        private readonly IReadOnlyList<CharacterDefinition> roster;
        private readonly Func<MemberView, int?> positionOf;
        private readonly Func<string, Texture2D> portraitFor;
        private readonly Func<string, Texture2D> iconFor;
        private readonly VisualElement overview;
        private readonly VisualElement modalLayer;
        private string memberId;
        private readonly List<string> backpackCells = new List<string>();

        public PartyEditorView(PartyRun run, IReadOnlyList<CharacterDefinition> catalog, Func<MemberView, int?> positionOf,
            Func<string, Texture2D> portraitFor, Func<string, Texture2D> iconFor, Action back)
        {
            this.run = run;
            roster = catalog.Where(character => character.RosterNumber > 0).OrderBy(character => character.RosterNumber).ToArray();
            this.positionOf = positionOf;
            this.portraitFor = portraitFor;
            this.iconFor = iconFor;
            name = "party-editor";
            style.flexGrow = 1;
            style.backgroundColor = PrototypeStyle.Background;

            overview = new ScrollView();
            overview.style.flexGrow = 1;
            overview.contentContainer.style.paddingLeft = overview.contentContainer.style.paddingRight = 22;
            overview.contentContainer.style.paddingTop = overview.contentContainer.style.paddingBottom = 18;
            Add(overview);

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            var backButton = PrototypeStyle.Button("返回", "back-party-editor", back);
            backButton.style.width = 100;
            header.Add(backButton);
            var title = PrototypeStyle.Text("角色编辑", 27);
            title.style.flexGrow = 1;
            PrototypeStyle.Middle(title);
            header.Add(title);
            var mainMenuButton = PrototypeSceneMenu.ReturnButton();
            mainMenuButton.style.width = 100;
            header.Add(mainMenuButton);
            overview.Add(header);

            modalLayer = new VisualElement { name = "character-details-layer" };
            PrototypeStyle.Fill(modalLayer);
            modalLayer.style.backgroundColor = new Color(0f, 0f, 0f, 0.66f);
            modalLayer.style.alignItems = Align.Center;
            modalLayer.style.justifyContent = Justify.Center;
            modalLayer.style.display = DisplayStyle.None;
            Add(modalLayer);

            RenderRoster();
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                if (memberId != null)
                    CloseModal();
                else
                    back();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        private void RenderRoster()
        {
            var oldRoster = overview.Q("editor-roster");
            oldRoster?.RemoveFromHierarchy();
            var layout = new VisualElement { name = "editor-roster" };
            layout.style.marginTop = 28;

            var drag = new FormationSwap(layout, (from, to) =>
            {
                if (run.TryPlace(from.MemberId, to.Position))
                    RenderRoster();
            }, slot => OpenDetails(slot.MemberId));
            layout.Add(PrototypeStyle.Text("长按立绘换位，左键查看装备，右键替换队员。", 13, PrototypeStyle.Muted));
            var row = PrototypeStyle.Row("editor-positions");
            row.style.justifyContent = Justify.SpaceAround;
            var members = run.Members;
            var displayed = new HashSet<string>();
            for (int position = Rules.Positions; position >= 1; position--)
            {
                var member = members.FirstOrDefault(entry => entry.Position == position)
                    ?? members.FirstOrDefault(entry => positionOf(entry) == position);
                if (member != null)
                    displayed.Add(member.Id);
                row.Add(BuildCard(position, member, drag));
            }
            layout.Add(row);

            var bench = PrototypeStyle.Row();
            bench.style.alignItems = Align.Center;
            bench.style.flexWrap = Wrap.Wrap;
            bench.style.marginTop = 16;
            var benchTarget = PrototypeStyle.Text("拖到此处移出站位", 13, PrototypeStyle.Muted);
            benchTarget.name = "editor-bench";
            benchTarget.style.paddingLeft = benchTarget.style.paddingRight = 10;
            benchTarget.style.paddingTop = benchTarget.style.paddingBottom = 6;
            PrototypeStyle.Outline(benchTarget, PrototypeStyle.Edge, 4);
            bench.Add(benchTarget);
            drag.Register(new FormationSwap.Slot { View = benchTarget, Border = benchTarget, CanDrop = true });
            foreach (var member in members.Where(entry => !displayed.Contains(entry.Id)))
            {
                var button = PrototypeStyle.Button(member.Character.Name, $"edit-member-{member.Id}",
                    () => OpenDetails(member.Id));
                RegisterReplacement(button, member);
                bench.Add(button);
                drag.Register(new FormationSwap.Slot
                {
                    View = button, Border = button, MemberId = member.Id, Name = member.Character.Name,
                    Artwork = portraitFor(member.Character.Id), CanDrag = member.Health > 0
                });
            }
            layout.Add(bench);
            overview.Add(layout);
        }

        private VisualElement BuildCard(int position, MemberView member, FormationSwap drag)
        {
            var card = new VisualElement { name = $"edit-position-{position}" };
            card.style.width = Length.Percent(17);
            card.style.maxWidth = 224;
            var namePlate = PrototypeStyle.Text(member?.Character.Name ?? "空位", 18,
                member != null ? PrototypeStyle.Ink : PrototypeStyle.Muted);
            namePlate.name = "character-name";
            namePlate.style.height = 42;
            namePlate.style.backgroundColor = PrototypeStyle.PanelColor;
            PrototypeStyle.Middle(namePlate);
            PrototypeStyle.Outline(namePlate, PrototypeStyle.Edge, 4);
            card.Add(namePlate);

            var portrait = new VisualElement { name = "character-portrait" };
            portrait.style.height = 364;
            portrait.style.marginTop = portrait.style.marginBottom = 7;
            portrait.style.overflow = Overflow.Hidden;
            portrait.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(portrait, PrototypeStyle.Edge, 4);
            var art = portraitFor(member?.Character.Id);
            if (art == null)
            {
                var placeholder = PrototypeStyle.Text(member == null ? "—" : member.Character.Name, 16, PrototypeStyle.Muted);
                placeholder.style.flexGrow = 1;
                PrototypeStyle.Middle(placeholder);
                portrait.Add(placeholder);
            }

            ProfessionFrame.AddTo(portrait, member?.Character.Profession, art);

            var tag = PrototypeStyle.Text($"{position} 号位", 11, PrototypeStyle.Accent);
            tag.style.position = Position.Absolute;
            tag.style.top = tag.style.left = 6;
            tag.style.paddingLeft = tag.style.paddingRight = 5;
            tag.style.backgroundColor = PrototypeStyle.Background;
            portrait.Add(tag);
            if (member != null && member.Health == 0)
                portrait.style.opacity = 0.55f;
            card.Add(portrait);

            var life = new VisualElement { name = "character-health" };
            life.style.height = 33;
            life.style.overflow = Overflow.Hidden;
            life.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(life, PrototypeStyle.Edge, 4);
            if (member != null && member.Health > 0)
            {
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(fill);
                fill.style.right = StyleKeyword.Auto;
                fill.style.width = Length.Percent(100f * member.Health / member.EffectiveStats.Health);
                fill.style.backgroundColor = Color.Lerp(PrototypeStyle.Ally, PrototypeStyle.Inset, 0.62f);
                life.Add(fill);
            }
            var hp = PrototypeStyle.Text(member == null ? "—" : $"生命  {HealthText(member.Health, member.EffectiveStats.Health)}", 14);
            hp.style.flexGrow = 1;
            PrototypeStyle.Middle(hp);
            life.Add(hp);
            card.Add(life);

            if (member != null)
            {
                var open = new Button(() => OpenDetails(member.Id))
                {
                    name = $"edit-member-{member.Id}",
                    tooltip = $"打开{member.Character.Name}的面板和背包"
                };
                PrototypeStyle.Fill(open);
                open.style.marginTop = open.style.marginRight = open.style.marginBottom = open.style.marginLeft = 0;
                open.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(open, Color.clear, 4);
                card.Add(open);
                RegisterReplacement(card, member);
                open.RegisterCallback<PointerEnterEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Accent, 4));
                open.RegisterCallback<PointerLeaveEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Edge, 4));
            }
            drag.Register(new FormationSwap.Slot
            {
                View = card, Border = portrait, Position = position, MemberId = member?.Id, Name = member?.Character.Name,
                Artwork = art, CanDrag = member != null && member.Health > 0, CanDrop = true
            });
            return card;
        }

        private void OpenDetails(string id)
        {
            var member = run.Members.Single(candidate => candidate.Id == id);
            OpenModal(id, new CharacterEquipmentPopup(run, id, portraitFor(member.Character.Id), iconFor, backpackCells,
                RenderRoster, CloseModal), "close-character-details");
        }

        private void RegisterReplacement(VisualElement target, MemberView member)
        {
            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1 || memberId != null)
                    return;
                var inParty = new HashSet<string>(run.Members.Select(entry => entry.Character.Id));
                var popup = new PartyReplacementPopup(member.Character, roster, inParty, portraitFor, replacement =>
                {
                    if (run.TryReplaceMember(member.Id, replacement, positionOf(member)))
                    {
                        RenderRoster();
                        CloseModal();
                    }
                }, CloseModal);
                OpenModal(member.Id, popup, "close-party-replacement");
                evt.StopPropagation();
            });
        }

        private void OpenModal(string id, VisualElement popup, string closeButton)
        {
            memberId = id;
            overview.SetEnabled(false);
            modalLayer.style.display = DisplayStyle.Flex;
            modalLayer.Add(popup);
            popup.Q<Button>(closeButton).Focus();
        }

        private void CloseModal()
        {
            string previous = memberId;
            memberId = null;
            modalLayer.Clear();
            modalLayer.style.display = DisplayStyle.None;
            overview.SetEnabled(true);
            overview.Q<Button>($"edit-member-{previous}")?.Focus();
        }
    }
}
