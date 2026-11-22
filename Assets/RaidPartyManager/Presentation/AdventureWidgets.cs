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
        private static void Note(VisualElement parent, VisualElement element)
        {
            if (parent is EventChoiceRack rack) rack.Note(element);
            else parent.Add(element);
        }

        internal static Button Action(VisualElement parent, string text, string name, Action action, bool enabled = true)
        {
            if (parent is EventChoiceRack rack) return rack.Choice(text, name, action, enabled);
            var button = PrototypeStyle.Button(text, name, action);
            button.style.minHeight = 44;
            button.style.marginBottom = 10;
            button.style.paddingLeft = button.style.paddingRight = 12;
            button.SetEnabled(enabled);
            parent.Add(button);
            return button;
        }

        internal static Button Item(EventChoiceRack rack, EquipmentDefinition item, EncounterData content,
            string name, Action click, bool selected = false, (string Name, string Description)? caption = null)
        {
            var icon = content.IconFor(item.Id);
            var button = rack.Choice("", name, click);
            if (selected) PrototypeStyle.Outline(button, PrototypeStyle.Accent, 4);
            var image = EquipmentIcon.CreateArtwork(item, icon);
            image.style.height = image.style.width = 36;
            button.Add(image);
            var label = PrototypeStyle.Text(caption?.Name ?? item.Name, 14);
            label.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(label);
            button.Add(label);

            VisualElement preview = null;
            void Hide() { preview?.RemoveFromHierarchy(); preview = null; }
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                Hide();
                var window = rack.GetFirstAncestorOfType<AdventureEventWindow>();
                if (window == null) return;
                if (caption.HasValue)
                {
                    var clue = PrototypeStyle.Text(caption.Value.Description, 14);
                    clue.style.backgroundColor = PrototypeStyle.PanelColor;
                    clue.style.position = Position.Absolute;
                    clue.style.paddingLeft = clue.style.paddingRight = 8;
                    var bounds = window.WorldToLocal(button.worldBound);
                    clue.style.left = Mathf.Clamp(bounds.center.x - 98, 3, Mathf.Max(3, window.layout.width - 199));
                    clue.style.width = 196;
                    clue.style.top = bounds.yMax + 4;
                    clue.pickingMode = PickingMode.Ignore;
                    window.Add(clue);
                    preview = clue;
                }
                else
                {
                    var info = new EquipmentTooltip(item, icon);
                    info.ShowAbove(window, button);
                    preview = info;
                }
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            button.RegisterCallback<PointerDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
            button.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
            return button;
        }

        internal static void RecruitReplacement(VisualElement parent, AdventureRun run, EncounterData content, Action refresh)
        {
            RecruitReplacement(parent, run.Current.PendingRecruit, run.Party, content, id =>
            {
                if (run.CompleteRecruit(id)) GameAudio.Play(SoundCue.CompanionJoin);
                refresh();
            }, revealRecruit: run.Current.Event != AdventureEventKind.LostTraveler);
        }

        internal static void RecruitReplacement(VisualElement parent, CharacterDefinition recruit, PartyRun party,
            EncounterData content, Action<string> replace, bool revealRecruit = true)
        {
            bool onPaper = parent is EventChoiceRack;
            var title = PrototypeStyle.Text($"{(revealRecruit ? recruit.Name : "有位旅人")}愿意同行。队伍已满，请选择替换一名队员。", 17,
                onPaper ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent);
            title.name = "pending-recruit-preview";
            Note(parent, title);
            if (revealRecruit) CharacterChoicePreview.Attach(title, recruit, content.PortraitFor(recruit.Id));
            Note(parent, PrototypeStyle.Text("原队员的装备会留在背包中；暂时离队的伙伴保留名额。", 13,
                onPaper ? AdventureEventWindow.PaperMuted : PrototypeStyle.Muted));
            foreach (var member in party.Members)
                Character(parent, member.Character, content, $"replace-recruit-{member.Id}",
                    () => replace(member.Id), !member.IsAway, member);
        }

        internal static Button Character(VisualElement parent, CharacterDefinition character, EncounterData content,
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
            return button;
        }

        internal static void BattleRewards(EventChoiceRack parent, AdventureRun run, EncounterData content, Action refresh,
            Action<string, Func<bool>> collect)
        {
            if (!string.IsNullOrEmpty(run.Current.Message)) parent.Note(PrototypeStyle.Text(run.Current.Message, 15, AdventureEventWindow.PaperAccent));
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
            Color accent = parent is EventChoiceRack ? AdventureEventWindow.PaperAccent : PrototypeStyle.Accent;
            var candidates = run.EvolutionCandidates;
            var selected = candidates.FirstOrDefault(member => member.Id == selectedId);
            var rack = parent as EventChoiceRack;
            var choices = rack == null ? new VisualElement { name = "evolution-choices" } : parent;
            if (rack == null) { choices.style.marginTop = 10; parent.Add(choices); }
            else rack.NameRow("evolution-choices");

            void RenderChoices()
            {
                if (rack == null) choices.Clear();
                if (selected == null)
                {
                    Note(choices, PrototypeStyle.Text("选择一位可进阶的队员", 18, accent));
                    foreach (var member in candidates)
                        Character(choices, member.Character, content, $"pick-evolution-member-{member.Id}", () =>
                        {
                            if (persistSelection?.Invoke(member.Id) == false) return;
                            selected = member;
                            if (rack == null) RenderChoices(); else completed();
                        }, member: member);
                    return;
                }

                var heading = PrototypeStyle.Text($"为{selected.Character.Name}选择进阶分支", 18, accent);
                heading.name = "evolution-current-preview";
                Note(choices, heading);
                CharacterChoicePreview.Attach(heading, selected.Character, content.PortraitFor(selected.Character.Id), selected);
                string memberId = selected.Id;
                foreach (var form in run.EvolutionForms(memberId))
                    Character(choices, form, content, $"evolve-{memberId}-{form.Id}", () =>
                    {
                        if (evolve(memberId, form.Id))
                        {
                            GameAudio.Play(SoundCue.CharacterEvolve);
                            completed();
                        }
                    });
                Action(choices, "返回队员选择", "back-to-evolution-members", () =>
                {
                    persistSelection?.Invoke(null);
                    selected = null;
                    if (rack == null) RenderChoices(); else completed();
                });
            }

            RenderChoices();
        }
    }
}
