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
            var icon = EquipmentIcon.CreateArtwork(item, content.IconFor(item.Id));
            icon.style.flexGrow = icon.style.flexShrink = 0;
            icon.style.width = icon.style.height = 64;
            icon.style.marginRight = 10;
            button.Add(icon);
            var words = new VisualElement { pickingMode = PickingMode.Ignore };
            words.style.flexGrow = 1;
            words.style.flexBasis = 0;
            string itemName = item is SkillBookDefinition ? $"技能书 · {item.Name}" : item.Name;
            words.Add(PrototypeStyle.Text(caption?.Name ?? itemName, 16,
                onPaper ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent));
            words.Add(PrototypeStyle.Text(caption?.Description ?? item.Description ?? "", 12,
                onPaper ? AdventureEventWindow.PaperMuted : PrototypeStyle.Muted));
            button.Add(words);
            parent.Add(button);
            return button;
        }

        internal static void RecruitReplacement(VisualElement parent, AdventureRun run, EncounterData content, Action refresh)
        {
            RecruitReplacement(parent, run.Current.PendingRecruit, run.Party, content, id =>
            {
                run.CompleteRecruit(id);
                refresh();
            });
        }

        internal static void RecruitReplacement(VisualElement parent, CharacterDefinition recruit, PartyRun party,
            EncounterData content, Action<string> replace)
        {
            bool onPaper = OnPaper(parent);
            var title = PrototypeStyle.Text($"{recruit.Name}愿意同行。队伍已满，请选择替换一名队员。", 17,
                onPaper ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent);
            title.name = "pending-recruit-preview";
            parent.Add(title);
            CharacterChoicePreview.Attach(title, recruit, content.PortraitFor(recruit.Id));
            parent.Add(PrototypeStyle.Text("原队员的装备会留在背包中；暂时离队的伙伴保留名额。", 13,
                onPaper ? AdventureEventWindow.PaperMuted : PrototypeStyle.Muted));
            foreach (var member in party.Members)
                Character(parent, member.Character, content, $"replace-recruit-{member.Id}",
                    () => replace(member.Id), !member.IsAway, member);
        }

        internal static void Character(VisualElement parent, CharacterDefinition character, EncounterData content,
            string name, Action click, bool enabled = true, MemberView member = null)
        {
            var button = Action(parent, character.Name + "  ·  " + AdventureFlow.ProfessionName(character.Profession), name, click, enabled);
            var image = new Image { image = content.PortraitFor(character.Id), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.width = 48; image.style.height = 62;
            button.style.flexDirection = FlexDirection.RowReverse;
            button.style.justifyContent = Justify.SpaceBetween;
            button.style.alignItems = Align.Center;
            button.Add(image);
            CharacterChoicePreview.Attach(button, character, content.PortraitFor(character.Id), member);
        }

        internal static void BattleRewards(VisualElement parent, AdventureRun run, EncounterData content, Action refresh,
            Action<string, Func<bool>> collect)
        {
            Color accent = OnPaper(parent) ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent;
            if (!string.IsNullOrEmpty(run.Current.Message)) parent.Add(PrototypeStyle.Text(run.Current.Message, 15, accent));
            if (run.Current.Resolved) return;
            for (int i = 0; i < run.Current.Rewards.Count; i++)
            {
                int index = i;
                string id = "claim-loot-" + i;
                Item(parent, run.Current.Rewards[i], content, id, () => collect(id, () => run.ClaimReward(index)));
            }
            if (run.Current.NeedsEvolution)
                EvolutionChoices(parent, run, content, run.Evolve, refresh, run.Current.EvolutionMemberId, run.ChooseEvolutionMember);
        }

        internal static void EvolutionChoices(VisualElement parent, AdventureRun run, EncounterData content,
            Func<string, string, bool> evolve, Action completed, string selectedId = null, Func<string, bool> persistSelection = null)
        {
            Color accent = OnPaper(parent) ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent;
            var candidates = run.EvolutionCandidates;
            var selected = candidates.FirstOrDefault(member => member.Id == selectedId);
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
                            if (persistSelection?.Invoke(member.Id) == false) return;
                            selected = member;
                            RenderChoices();
                        }, member: member);
                    return;
                }

                var heading = PrototypeStyle.Text($"为{selected.Character.Name}选择进阶分支", 18, accent);
                heading.name = "evolution-current-preview";
                choices.Add(heading);
                CharacterChoicePreview.Attach(heading, selected.Character, content.PortraitFor(selected.Character.Id), selected);
                string memberId = selected.Id;
                foreach (var form in run.EvolutionForms(memberId))
                    Character(choices, form, content, $"evolve-{memberId}-{form.Id}", () =>
                    {
                        if (evolve(memberId, form.Id)) completed();
                    });
                Action(choices, "返回队员选择", "back-to-evolution-members", () =>
                {
                    persistSelection?.Invoke(null);
                    selected = null;
                    RenderChoices();
                });
            }

            RenderChoices();
        }
    }
}
