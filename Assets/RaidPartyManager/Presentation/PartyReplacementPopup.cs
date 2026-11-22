using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class PartyReplacementPopup : VisualElement
    {
        public PartyReplacementPopup(CharacterDefinition outgoing, IReadOnlyList<CharacterDefinition> roster,
            HashSet<string> inParty, Func<string, Texture2D> portraitFor, Action<CharacterDefinition> select, Action close,
            Side side = Side.Party)
        {
            name = "party-replacement";
            style.width = Length.Percent(90);
            style.height = Length.Percent(88);
            style.maxWidth = 920;
            style.maxHeight = 620;
            style.backgroundColor = PrototypeStyle.PanelColor;
            style.paddingTop = style.paddingBottom = 12;
            style.paddingLeft = style.paddingRight = 15;
            PrototypeStyle.Outline(this, PrototypeStyle.Edge, 6);

            var heading = PrototypeStyle.Row();
            heading.style.alignItems = Align.Center;
            heading.style.justifyContent = Justify.SpaceBetween;
            heading.Add(PrototypeStyle.Text($"替换 {outgoing.Name}", 22));
            heading.Add(PrototypeStyle.Button("关闭", "close-party-replacement", close));
            Add(heading);
            var instructions = PrototypeStyle.Text(side == Side.Party ? "选择一名未入队的角色。原队员的装备会放回共享背包。"
                : "选择一名当前未上阵的敌人。", 12, PrototypeStyle.Muted);
            instructions.style.marginTop = 2;
            instructions.style.marginBottom = 8;
            Add(instructions);

            var list = new ScrollView(ScrollViewMode.Vertical) { name = "replacement-candidates" };
            list.style.flexGrow = 1;
            list.style.minHeight = 0;
            Add(list);
            var cards = PrototypeStyle.Row();
            cards.style.flexWrap = Wrap.Wrap;
            list.Add(cards);
            foreach (var character in roster)
                cards.Add(Candidate(character, inParty.Contains(character.Id), portraitFor(character.Id), select, side));
        }

        private static Button Candidate(CharacterDefinition character, bool inParty, Texture2D portrait,
            Action<CharacterDefinition> select, Side side)
        {
            var button = PrototypeStyle.Button("", $"replace-with-{character.Id}", () => select(character));
            button.style.width = Length.Percent(18.7f);
            button.style.minWidth = 102;
            button.style.height = 138;
            button.style.paddingTop = button.style.paddingBottom = 4;
            button.style.paddingLeft = button.style.paddingRight = 4;
            button.style.marginLeft = button.style.marginRight = 3;
            button.style.marginBottom = 5;
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.style.flexGrow = 1;
            art.style.overflow = Overflow.Hidden;
            art.style.backgroundColor = PrototypeStyle.Inset;
            ProfessionFrame.AddTo(art, character.Profession, portrait);
            button.Add(art);

            var title = PrototypeStyle.Text(character.Name, 13);
            title.pickingMode = PickingMode.Ignore;
            title.style.marginTop = 2;
            PrototypeStyle.Middle(title);
            button.Add(title);
            var state = PrototypeStyle.Text(inParty ? "已上阵" : side == Side.Party ? $"{character.Stage} 阶" : "", 11,
                inParty ? PrototypeStyle.Muted : PrototypeStyle.Accent);
            state.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(state);
            button.Add(state);
            button.tooltip = string.IsNullOrEmpty(character.TraitText) ? "" : "被动 · " + character.TraitText;
            button.SetEnabled(!inParty);
            return button;
        }
    }
}
