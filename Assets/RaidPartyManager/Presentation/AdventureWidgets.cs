using System;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal static class AdventureWidgets
    {
        private static bool OnPaper(VisualElement parent) =>
            parent.GetFirstAncestorOfType<AdventureEventWindow>() != null;

        internal static Button Action(VisualElement parent, string text, string name, Action action, bool enabled = true)
        {
            var button = PrototypeStyle.Button(text, name, action);
            button.style.minHeight = 44;
            button.style.marginBottom = 10;
            button.style.paddingLeft = button.style.paddingRight = 12;
            if (OnPaper(parent)) AdventureEventWindow.StylePaperButton(button);
            button.SetEnabled(enabled);
            parent.Add(button);
            return button;
        }

        internal static Button Item(VisualElement parent, EquipmentDefinition item, EncounterData content,
            string name, Action click, bool selected = false, (string Name, string Description)? caption = null)
        {
            var button = PrototypeStyle.Button("", name, click);
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.minHeight = 82;
            button.style.marginBottom = 9;
            button.style.paddingLeft = button.style.paddingRight = 8;
            bool onPaper = OnPaper(parent);
            if (onPaper) AdventureEventWindow.StylePaperButton(button, selected);
            else if (selected) PrototypeStyle.Outline(button, PrototypeStyle.Accent, 4);
            var icon = new Image { image = content.IconFor(item.Id), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = 64;
            icon.style.marginRight = 10;
            button.Add(icon);
            var words = new VisualElement { pickingMode = PickingMode.Ignore };
            words.style.flexGrow = 1;
            words.style.flexBasis = 0;
            words.Add(PrototypeStyle.Text(caption?.Name ?? item.Name, 16,
                onPaper ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent));
            words.Add(PrototypeStyle.Text(caption?.Description ?? item.Description ?? "", 12,
                onPaper ? AdventureEventWindow.PaperMuted : PrototypeStyle.Muted));
            button.Add(words);
            parent.Add(button);
            return button;
        }

        internal static void RecruitReplacement(VisualElement parent, AdventureRun run, EncounterData content, Action refresh)
        {
            var recruit = run.Current.PendingRecruit;
            bool onPaper = OnPaper(parent);
            parent.Add(PrototypeStyle.Text($"{recruit.Name}愿意同行。队伍已满，请选择替换一名队员。", 17,
                onPaper ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent));
            parent.Add(PrototypeStyle.Text("原队员的装备会留在背包中；暂时离队的伙伴保留名额。", 13,
                onPaper ? AdventureEventWindow.PaperMuted : PrototypeStyle.Muted));
            foreach (var member in run.Party.Members)
                Character(parent, member.Character, content, $"replace-recruit-{member.Id}", () =>
                { run.CompleteRecruit(member.Id); refresh(); }, !member.IsAway);
        }

        internal static void Character(VisualElement parent, CharacterDefinition character, EncounterData content,
            string name, Action click, bool enabled = true)
        {
            var button = Action(parent, character.Name + "  ·  " + AdventureFlow.ProfessionName(character.Profession), name, click, enabled);
            var image = new Image { image = content.PortraitFor(character.Id), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.width = 48; image.style.height = 62;
            button.style.flexDirection = FlexDirection.RowReverse;
            button.style.justifyContent = Justify.SpaceBetween;
            button.style.alignItems = Align.Center;
            button.Add(image);
        }

        internal static void BattleRewards(VisualElement parent, AdventureRun run, EncounterData content, Action refresh)
        {
            Color accent = OnPaper(parent) ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent;
            if (!string.IsNullOrEmpty(run.Current.Message)) parent.Add(PrototypeStyle.Text(run.Current.Message, 15, accent));
            if (run.Current.Resolved) return;
            for (int i = 0; i < run.Current.Rewards.Count; i++)
            {
                int index = i;
                Item(parent, run.Current.Rewards[i], content, "claim-loot-" + i, () => { run.ClaimReward(index); refresh(); });
            }
            if (!run.Current.NeedsEvolution) return;
            var candidates = run.EvolutionCandidates;
            var selected = candidates.FirstOrDefault(member => member.Id == run.Current.EvolutionMemberId);
            var choices = new VisualElement { name = "evolution-choices" };
            choices.style.marginTop = 10;
            parent.Add(choices);

            void RenderChoices()
            {
                choices.Clear();
                if (selected == null)
                {
                    choices.Add(PrototypeStyle.Text("选择一位可进阶的队员", 18, accent));
                    foreach (var member in candidates)
                        Character(choices, member.Character, content, $"pick-evolution-member-{member.Id}", () =>
                        {
                            if (!run.ChooseEvolutionMember(member.Id)) return;
                            selected = member;
                            RenderChoices();
                        });
                    return;
                }

                choices.Add(PrototypeStyle.Text($"为{selected.Character.Name}选择进阶分支", 18, accent));
                string memberId = selected.Id;
                foreach (var form in run.EvolutionForms(memberId))
                    Character(choices, form, content, $"evolve-{memberId}-{form.Id}", () =>
                    {
                        if (run.Evolve(memberId, form.Id)) refresh();
                    });
                Action(choices, "返回队员选择", "back-to-evolution-members", () =>
                {
                    run.ChooseEvolutionMember(null);
                    selected = null;
                    RenderChoices();
                });
            }

            RenderChoices();
        }
    }
}
