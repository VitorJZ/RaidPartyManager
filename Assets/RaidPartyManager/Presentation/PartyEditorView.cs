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
        private readonly Func<MemberView, int?> positionOf;
        private readonly Func<string, Texture2D> portraitFor;
        private readonly VisualElement overview;
        private readonly VisualElement modalLayer;
        private string memberId;
        private readonly List<string> backpackCells = new List<string>();

        public PartyEditorView(PartyRun run, Func<MemberView, int?> positionOf,
            Func<string, Texture2D> portraitFor, Action back)
        {
            this.run = run;
            this.positionOf = positionOf;
            this.portraitFor = portraitFor;
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
            var balance = new VisualElement();
            balance.style.width = 100;
            header.Add(balance);
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
                    CloseDetails();
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

            var row = PrototypeStyle.Row("editor-positions");
            row.style.justifyContent = Justify.SpaceAround;
            var members = run.Members;
            for (int position = Rules.Positions; position >= 1; position--)
                row.Add(BuildCard(position, members.FirstOrDefault(member => positionOf(member) == position)));
            layout.Add(row);

            var unplaced = members.Where(member => !positionOf(member).HasValue).ToArray();
            if (unplaced.Length > 0)
            {
                var bench = PrototypeStyle.Row();
                bench.style.alignItems = Align.Center;
                bench.style.flexWrap = Wrap.Wrap;
                bench.style.marginTop = 16;
                bench.Add(PrototypeStyle.Text("未布阵", 13, PrototypeStyle.Muted));
                foreach (var member in unplaced)
                    bench.Add(PrototypeStyle.Button(member.Character.Name, $"edit-member-{member.Id}",
                        () => OpenDetails(member.Id)));
                layout.Add(bench);
            }
            overview.Add(layout);
        }

        private VisualElement BuildCard(int position, MemberView member)
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
                open.RegisterCallback<PointerEnterEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Accent, 4));
                open.RegisterCallback<PointerLeaveEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Edge, 4));
            }
            return card;
        }

        private void OpenDetails(string id)
        {
            memberId = id;
            overview.SetEnabled(false);
            modalLayer.style.display = DisplayStyle.Flex;
            var member = run.Members.Single(candidate => candidate.Id == id);
            modalLayer.Add(new CharacterEquipmentPopup(run, id, portraitFor(member.Character.Id), backpackCells,
                RenderRoster, CloseDetails));
            modalLayer.Q<Button>("close-character-details").Focus();
        }

        private void CloseDetails()
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
