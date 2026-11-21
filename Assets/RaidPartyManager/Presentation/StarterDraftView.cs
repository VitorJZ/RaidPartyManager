using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class StarterDraftView : VisualElement
    {
        private const int SelectionLimit = 2;

        public StarterDraftView(EncounterData content, IReadOnlyList<CharacterDefinition> candidates,
            Action<IReadOnlyList<CharacterDefinition>> depart)
        {
            name = "starter-draft";
            style.flexGrow = 1;
            style.minHeight = 0;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.alignItems = Align.Center;
            Add(scroll);

            var panel = PrototypeStyle.Panel();
            panel.style.width = Length.Percent(100);
            panel.style.maxWidth = 1000;
            panel.style.marginTop = 22;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            panel.style.paddingTop = panel.style.paddingBottom = 15;
            scroll.Add(panel);

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;
            var heading = PrototypeStyle.Text("选择起始队员", 27, PrototypeStyle.Accent);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.flexGrow = 1;
            header.Add(heading);
            var count = PrototypeStyle.Text("", 14, PrototypeStyle.Ink);
            count.name = "starter-draft-count";
            header.Add(count);
            panel.Add(header);

            var cards = PrototypeStyle.Row("starter-candidates");
            cards.style.alignItems = Align.Stretch;
            panel.Add(cards);
            var selected = new HashSet<CharacterDefinition>();
            var start = PrototypeStyle.Button("拟定合约", "confirm-starters", () =>
            {
                if (selected.Count != SelectionLimit) return;
                var starters = candidates.Where(selected.Contains).ToArray();
                var sheet = ContractPanel.Sheet("起始队员 · 入队合约", out var paper);
                foreach (var c in starters)
                    paper.Add(ContractPanel.Text(ContractPanel.Summary(c, Run.CrewContract.Offer(c, 1)), 17));
                paper.Add(ContractPanel.Text($"两份首约的基础薪资共 {starters.Sum(c => Run.CrewContract.Offer(c, 1).BaseTotal)} 金币，由创队经费当场结清。签约后队伍可用资金为 50 金币。", 15));
                paper.Add(PrototypeStyle.Button("签约并出发", "sign-starters", () => depart(starters), true));
                paper.Add(PrototypeStyle.Button("重新选人", "revise-starters", () => sheet.RemoveFromHierarchy()));
                Add(sheet);
            }, true);
            start.style.minWidth = 120;
            start.style.minHeight = 36;
            var buttons = new List<Button>(candidates.Count);
            foreach (var character in candidates)
            {
                var button = Candidate(character, content.PortraitFor(character.Id), () =>
                {
                    if (!selected.Remove(character) && selected.Count < SelectionLimit)
                        selected.Add(character);
                    Refresh();
                });
                buttons.Add(button);
                cards.Add(button);
            }

            var footer = PrototypeStyle.Row();
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.marginTop = 13;
            footer.Add(PrototypeSceneMenu.ReturnButton());
            footer.Add(start);
            panel.Add(footer);
            Refresh();

            void Refresh()
            {
                count.text = $"已选 {selected.Count} / {SelectionLimit}";
                start.SetEnabled(selected.Count == SelectionLimit);
                for (int index = 0; index < candidates.Count; index++)
                {
                    var card = buttons[index];
                    bool chosen = selected.Contains(candidates[index]);
                    card.EnableInClassList("starter-candidate-selected", chosen);
                    card.style.backgroundColor = chosen ? new Color(0.25f, 0.20f, 0.15f) : PrototypeStyle.Inset;
                    PrototypeStyle.Outline(card, chosen ? PrototypeStyle.Accent : PrototypeStyle.Edge, 5);
                    card.SetEnabled(chosen || selected.Count < SelectionLimit);
                }
            }
        }

        private static Button Candidate(CharacterDefinition character, Texture2D artwork, Action toggle)
        {
            var card = PrototypeStyle.Button("", "starter-candidate-" + character.Id, toggle);
            card.userData = character;
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.minWidth = 0;
            card.style.marginLeft = card.style.marginRight = 4;
            card.style.paddingLeft = card.style.paddingRight = 7;
            card.style.paddingTop = card.style.paddingBottom = 8;
            card.style.alignItems = Align.Stretch;
            card.style.justifyContent = Justify.FlexStart;
            var portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            portrait.style.height = 258;
            portrait.style.flexShrink = 0;
            ProfessionFrame.AddFittedTo(portrait, character.Profession, artwork);
            card.Add(portrait);

            var name = PrototypeStyle.Text(character.Name, 17, PrototypeStyle.Ink);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.marginTop = 5;
            PrototypeStyle.Middle(name);
            card.Add(name);
            var profession = PrototypeStyle.Text(AdventureFlow.ProfessionName(character.Profession), 12, PrototypeStyle.Accent);
            PrototypeStyle.Middle(profession);
            card.Add(profession);

            var stats = character.BaseStats;
            var attributes = PrototypeStyle.Text($"攻击 {stats.Attack}  防御 {stats.Defense}  特防 {stats.SpecialDefense}  生命 {stats.Health}",
                11, PrototypeStyle.Muted);
            attributes.style.marginTop = attributes.style.marginBottom = 5;
            PrototypeStyle.Middle(attributes);
            card.Add(attributes);
            var trait = PrototypeStyle.Text(character.TraitText, 12);
            trait.style.minHeight = 41;
            PrototypeStyle.Middle(trait);
            card.Add(trait);
            return card;
        }
    }
}
