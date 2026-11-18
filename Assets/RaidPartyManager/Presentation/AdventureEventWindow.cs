using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class AdventureEventWindow : VisualElement
    {
        internal static readonly Color PaperInk = new Color32(48, 30, 19, 255);
        internal static readonly Color PaperAccent = new Color32(109, 52, 24, 255);
        internal static readonly Color PaperMuted = new Color32(92, 65, 42, 255);
        private static readonly Color PaperRule = new Color32(122, 80, 43, 118);

        private readonly AdventureRun run;
        private readonly AdventureContentAsset art;
        private readonly EncounterData content;
        private readonly Action close;
        private readonly ScrollView choices;
        private readonly Action refreshTopBar;
        private readonly List<string> forge = new List<string>();
        private bool choosingChestCompanion;

        internal AdventureEventWindow(AdventureRun run, AdventureContentAsset art, Action close, Action refreshTopBar)
        {
            this.run = run; this.art = art; this.close = close; content = art.Encounter;
            this.refreshTopBar = refreshTopBar;
            name = "event-window";
            PrototypeStyle.Fill(this);
            style.backgroundColor = new Color(0, 0, 0, 0.78f);
            style.alignItems = Align.Center; style.justifyContent = Justify.Center;
            var panel = PrototypeStyle.Panel();
            panel.name = "event-panel";
            panel.style.width = Length.Percent(92); panel.style.maxWidth = 1080;
            panel.style.height = Length.Percent(91); panel.style.maxHeight = 690;
            panel.style.paddingLeft = panel.style.paddingRight = 26;
            panel.style.paddingTop = panel.style.paddingBottom = 20;
            panel.style.backgroundColor = (Color)new Color32(241, 219, 177, 255);
            if (art.EventParchment != null)
            {
                panel.style.backgroundImage = new StyleBackground(art.EventParchment);
                panel.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            PrototypeStyle.Outline(panel, new Color32(112, 68, 34, 255), 5);
            Add(panel);

            var spread = PrototypeStyle.Row("event-spread");
            spread.style.flexGrow = 1;
            spread.style.minHeight = 0;
            panel.Add(spread);

            var eventKind = run.Current.Event;
            var inkArt = eventKind.HasValue ? art.EventInkArt(eventKind.Value) : null;
            var image = new Image
            {
                name = "event-illustration",
                image = inkArt ?? (eventKind.HasValue ? art.EventArt(eventKind.Value) : art.Backdrop),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            image.style.flexGrow = 1;
            image.style.flexBasis = 0;
            image.style.minWidth = 0;
            image.style.marginRight = 14;
            if (inkArt != null && art.EventInkShader != null)
                ApplyInkLayer(image, inkArt, art.EventInkShader);
            spread.Add(image);

            var right = new VisualElement { name = "event-right-page" };
            right.style.flexGrow = 1;
            right.style.flexBasis = 0;
            right.style.minWidth = 0;
            right.style.marginLeft = 14;
            spread.Add(right);

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;
            var heading = PaperText(eventKind.HasValue ? AdventureFlow.EventName(eventKind.Value) :
                AdventureFlow.NodeName(run.Current.Kind), 26, PaperAccent);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.flexGrow = 1;
            heading.style.flexBasis = 0;
            heading.style.minWidth = 0;
            heading.style.marginRight = 8;
            header.Add(heading);
            var leave = PrototypeStyle.Button("离开", "close-event", Leave);
            StylePaperButton(leave);
            leave.style.minWidth = 58;
            header.Add(leave);
            right.Add(header);

            var story = new ScrollView(ScrollViewMode.Vertical) { name = "event-story" };
            story.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            story.style.flexGrow = 0;
            story.style.flexShrink = 0;
            story.style.maxHeight = Length.Percent(38);
            story.style.marginTop = 12;
            story.style.marginBottom = 16;
            story.style.paddingBottom = 14;
            story.style.borderBottomWidth = 1;
            story.style.borderBottomColor = PaperRule;
            right.Add(story);
            var text = PaperText(eventKind.HasValue ? AdventureFlow.Narrative(eventKind.Value) :
                "战斗结束。收好战利品，整顿队伍，再去下一站。", 16);
            text.name = "event-description";
            story.Add(text);

            choices = new ScrollView(ScrollViewMode.Vertical) { name = "event-options" };
            choices.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            choices.contentContainer.style.minWidth = 0;
            choices.contentContainer.style.width = Length.Percent(100);
            choices.style.flexGrow = 1;
            choices.style.flexBasis = 0;
            choices.style.minHeight = 0;
            right.Add(choices);
            RenderChoices();
        }

        private static void ApplyInkLayer(Image image, Texture2D source, Shader shader)
        {
            RenderTexture ink = null;
            image.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                // Preserve the original texture's transparency and let light areas show the actual paper beneath.
                var material = new Material(shader);
                var previous = RenderTexture.active;
                ink = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                ink.filterMode = FilterMode.Bilinear;
                try
                {
                    Graphics.Blit(source, ink, material);
                    image.image = ink;
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.Destroy(material);
                }
            });
            image.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (ink == null) return;
                image.image = source;
                RenderTexture.ReleaseTemporary(ink);
                ink = null;
            });
        }

        private static Label PaperText(string text, int size, Color? color = null) =>
            PrototypeStyle.Text(text, size, color ?? PaperInk);

        internal static void StylePaperButton(Button button, bool selected = false)
        {
            button.style.color = PaperInk;
            PrototypeStyle.Outline(button, selected ? PaperAccent : PaperRule, 4);
            Color normal = new Color32(121, 74, 34, selected ? (byte)47 : (byte)19);
            Color highlighted = new Color32(121, 74, 34, 61);
            button.style.backgroundColor = normal;
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = highlighted);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = normal);
            button.RegisterCallback<FocusInEvent>(_ => button.style.borderBottomColor = PaperAccent);
            button.RegisterCallback<FocusOutEvent>(_ => button.style.borderBottomColor = selected ? PaperAccent : PaperRule);
        }

        private void Leave()
        {
            if (run.Current.Resolved) run.CloseVisit(); else run.Leave();
            close();
        }

        internal void RefreshAfterPartyEdit()
        {
            var visit = run.Current;
            if (visit.Event == AdventureEventKind.Forge)
            {
                var available = run.ForgeInputs.Select(item => item.Id).ToHashSet();
                forge.RemoveAll(id => !available.Contains(id));
            }
            if (visit.PendingRecruit != null || visit.Event is AdventureEventKind.Forge or AdventureEventKind.LockedChest or
                AdventureEventKind.Ambush or AdventureEventKind.AscensionTrial)
                RenderChoices();
        }

        private void RenderChoices()
        {
            choices.Clear();
            refreshTopBar();
            var visit = run.Current;
            if (visit.Event == AdventureEventKind.HatTrick)
                choices.Add(new HatGameView(run, art.Hat, content, RenderChoices));
            if (visit.Resolved)
            {
                choices.Add(PaperText(visit.Message, 17, PaperAccent));
                AdventureWidgets.Action(choices, "继续前进", "finish-event", Leave);
                return;
            }
            if (visit.Battle?.Settled == true)
            {
                AdventureWidgets.BattleRewards(choices, run, content, RenderChoices);
                return;
            }
            if (visit.PendingRecruit != null)
            {
                AdventureWidgets.RecruitReplacement(choices, run, content, RenderChoices);
                return;
            }
            if (!string.IsNullOrEmpty(visit.Message)) choices.Add(PaperText(visit.Message, 14, PaperAccent));
            switch (visit.Event)
            {
                case AdventureEventKind.Investor:
                    if (run.InvestmentPaid || !visit.InvestorAvailable)
                    {
                        choices.Add(PaperText(run.InvestmentPaid
                            ? "泊位空了。缆桩上系着一张短笺，写着「多谢」。"
                            : "创业者合上账本，夹起图纸。“今天就到这儿吧，改日再聊。”", 16));
                        break;
                    }
                    if (run.Investment > 0)
                        choices.Add(PaperText("他翻开账本，你认出了自己上回留下的签名。", 16));
                    var amount = new IntegerField("出资金额") { name = "investment-amount", value = Math.Min(run.Party.Gold, 1) };
                    amount.style.marginTop = amount.style.marginBottom = 15;
                    amount.labelElement.style.color = PaperInk;
                    amount.labelElement.style.minWidth = 90;
                    amount.labelElement.style.width = 110;
                    choices.Add(amount);
                    choices.Add(PaperText($"可投入 1～{run.Party.Gold} 金币", 13, PaperMuted));
                    AdventureWidgets.Action(choices, "出一份力", "event-invest", () =>
                    { run.Donate(amount.value); RenderChoices(); }, run.Party.Gold > 0);
                    AdventureWidgets.Action(choices, "把金币全留给他", "event-invest-all", () =>
                    { run.Donate(run.Party.Gold); RenderChoices(); }, run.Party.Gold > 0);
                    break;
                case AdventureEventKind.FoundCoins:
                    AdventureWidgets.Action(choices, "拾起钱袋", "event-collect-coins", () => { run.CollectCoins(); RenderChoices(); });
                    break;
                case AdventureEventKind.LostTraveler:
                    choices.Add(PaperText("你打算怎么搭话？", 18, PaperAccent));
                    foreach (var profession in visit.Professions)
                        AdventureWidgets.Action(choices, $"“你是{AdventureFlow.ProfessionName(profession)}？”", "event-profession-" + profession,
                            () => { run.ChooseProfession(profession); RenderChoices(); });
                    if (visit.Professions.Count == 0) choices.Add(PaperText("旅行者问清了路，笑着向你们道别。", 15));
                    break;
                case AdventureEventKind.Forge:
                    choices.Add(PaperText($"挑两件背包里的旧物  {forge.Count}／2", 17, PaperAccent));
                    AdventureWidgets.Action(choices, "把旧物交给铁匠", "event-forge-confirm", () =>
                    { run.Forge(forge[0], forge[1]); RenderChoices(); }, forge.Count == 2);
                    foreach (var item in run.ForgeInputs)
                        AdventureWidgets.Item(choices, item.Definition, content, "forge-item-" + item.Id, () =>
                        {
                            if (forge.Contains(item.Id)) forge.Remove(item.Id);
                            else if (forge.Count < 2) forge.Add(item.Id);
                            RenderChoices();
                        }, forge.Contains(item.Id));
                    break;
                case AdventureEventKind.LockedChest:
                {
                    var present = run.Party.Members.Where(m => !m.IsAway && m.Health > 0 && m.Position.HasValue).ToArray();
                    if (!choosingChestCompanion)
                    {
                        choices.Add(PaperText("要留一位同伴试试这把锁吗？", 18, PaperAccent));
                        AdventureWidgets.Action(choices, "留一人开宝箱", "chest-choose-companion", () =>
                        { choosingChestCompanion = true; RenderChoices(); }, present.Any(m => run.Party.CanSendAway(m.Id)));
                        AdventureWidgets.Action(choices, "把箱子留在原处", "chest-skip", Leave);
                        break;
                    }

                    choices.Add(PaperText("谁留下开锁，晚些再跟上？", 18, PaperAccent));
                    foreach (var member in present)
                        AdventureWidgets.Character(choices, member.Character, content, "leave-companion-" + member.Id, () =>
                        { run.LeaveCompanion(member.Id); RenderChoices(); }, run.Party.CanSendAway(member.Id));
                    AdventureWidgets.Action(choices, "返回", "chest-back", () =>
                    { choosingChestCompanion = false; RenderChoices(); });
                    break;
                }
                case AdventureEventKind.AscensionTrial:
                    AdventureWidgets.Action(choices, "收下台阶上的包裹", "event-trial-gift", () =>
                    { run.TakeTrialGift(); RenderChoices(); });
                    AdventureWidgets.Action(choices, "跨过门槛，接受挑战", "event-trial-battle", StartBattle,
                        run.Party.CanStartBattle && run.EvolutionCandidates.Count > 0);
                    break;
                case AdventureEventKind.Ambush:
                    AdventureWidgets.Action(choices, "拔出武器，迎上去", "event-ambush-battle", StartBattle, run.Party.CanStartBattle);
                    break;
                case AdventureEventKind.Alchemist:
                    for (int i = 0; i < visit.Potions.Count; i++)
                    {
                        int index = i;
                        AdventureWidgets.Item(choices, visit.Potions[i], content, "event-potion-" + i, () =>
                        { run.ChoosePotion(index); RenderChoices(); }, caption: PotionClue(visit.Potions[i].Effect));
                    }
                    break;
            }
        }

        private static (string Name, string Description) PotionClue(EquipmentEffect effect) => effect switch
        {
            EquipmentEffect.VigorPotion => ("温热的药瓶", "手贴上瓶身，像捂着一枚刚出炉的栗子。"),
            EquipmentEffect.SwiftPotion => ("浮雾的药瓶", "你才转动手腕，瓶里的雾已经滑到了另一头。"),
            EquipmentEffect.ResolvePotion => ("沉甸甸的药瓶", "炼药师用它压住一摞被风吹乱的纸。"),
            EquipmentEffect.GreedPotion => ("泛光的药瓶", "瓶底的亮点一靠近钱袋，就全凑到了同一侧。"),
            EquipmentEffect.ThornsPotion => ("带刺的药瓶", "软木塞上有个小洞，一根细刺刚从里头钻出来。"),
            EquipmentEffect.PurityPotion => ("澄清的药瓶", "瓶口落了一点炉灰，还没沉底就看不见了。"),
            _ => ("没贴签的药瓶", "瓶塞封得很紧，炼药师也没多说。")
        };

        private void StartBattle()
        {
            if (run.StartEventBattle()) AdventureFlow.OpenNodeScene("BattlePrototype");
        }
    }
}
