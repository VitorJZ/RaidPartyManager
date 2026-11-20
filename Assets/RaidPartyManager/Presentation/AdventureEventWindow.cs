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

        private readonly AdventureRun run;
        private readonly AdventureContentAsset art;
        private readonly EncounterData content;
        private readonly Action close, refreshCast, startEncounter;
        private readonly DockEventStage stage;
        private HatGameView hatGame;
        private VisualElement potionTargets;
        private readonly EventChoiceRack choices;
        private readonly HangingSign exit;
        private readonly RunTopBar topBar;
        private readonly List<string> forge = new List<string>();
        private bool choosingChestCompanion;
        private readonly TreasureChestView treasureChest;
        private bool awaitingSigns, leftTable, leaving;
        private readonly Texture2D timber;

        internal AdventureEventWindow(AdventureRun run, AdventureContentAsset art, Texture2D timber, DockEventStage stage, Action close, RunTopBar topBar, Action refreshCast, Action startEncounter)
        {
            this.run = run; this.art = art; this.close = close; content = art.Encounter;
            this.topBar = topBar;
            this.refreshCast = refreshCast;
            this.startEncounter = startEncounter;
            this.stage = stage; this.timber = timber;
            name = "event-window";
            PrototypeStyle.Fill(this);
            style.overflow = Overflow.Hidden;
            var eventKind = run.Current.Event;
            var notes = new VisualElement();
            if (eventKind.HasValue)
            {
                Add(new TellerSpeech(stage, AdventureFlow.Narrative(eventKind.Value), notes));
            }
            else if (run.Current.Kind == NodeKind.TreasureChest)
            {
                stage.PlaceChest(art,run.Current.TreasureOpened);
                treasureChest = new TreasureChestView(stage,run,content,CollectFrom);
                Add(treasureChest);
            }
            else
            {
                var panel = new VisualElement { name = "event-panel" };
                panel.style.position = Position.Absolute;
                panel.style.right = Length.Percent(7);
                panel.style.bottom = Length.Percent(9);
                panel.style.width = Length.Percent(49);
                panel.style.maxWidth = 600;
                panel.style.height = Length.Percent(30);
                panel.style.minHeight = 136;
                panel.style.maxHeight = 195;
                panel.style.paddingLeft = panel.style.paddingRight = 16;
                panel.style.paddingTop = panel.style.paddingBottom = 10;
                panel.style.backgroundColor = (Color)new Color32(241,219,177,255);
                if (art.EventParchment != null)
                {
                    panel.style.backgroundImage = new StyleBackground(art.EventParchment);
                    panel.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                }
                PrototypeStyle.Outline(panel, new Color32(112,68,34,255), 5);
                Add(panel);
                var spread = PrototypeStyle.Row("event-spread");
                spread.style.flexGrow = 1;
                panel.Add(spread);
                var scenery = new Image { image = art.Backdrop, scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore };
                scenery.style.flexGrow = 1;
                scenery.style.flexBasis = scenery.style.minWidth = 0;
                scenery.style.marginRight = 14;
                spread.Add(scenery);
                var page = new VisualElement { name = "event-right-page" };
                page.style.flexGrow = 2;
                page.style.flexBasis = page.style.minWidth = 0;
                page.style.marginLeft = 14;
                spread.Add(page);
                var heading = PaperText(AdventureFlow.NodeName(run.Current.Kind),26,PaperAccent);
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                page.Add(heading);
                var story = new ScrollView(ScrollViewMode.Vertical) { name = "event-story" };
                story.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                story.style.flexGrow = 1;
                story.style.minHeight = 0;
                story.style.marginTop = 4;
                page.Add(story);
                var text = PaperText("战斗结束。收好战利品，整顿队伍，再去下一站。",16);
                text.name = "event-description";
                story.Add(text);
                story.Add(notes);
            }
            choices = new EventChoiceRack(timber, notes);
            if (eventKind.HasValue)
            {
                choices.style.left = Length.Percent(8);
                choices.style.right = Length.Percent(49);
            }
            if (eventKind == AdventureEventKind.HatTrick)
            {
                // Keep the performer's hands and the centre hat clear during the close-up.
                choices.style.left = Length.Percent(2);
                choices.style.right = StyleKeyword.Auto;
                choices.style.width = Length.Percent(16);
                choices.style.top = Length.Percent(10);
            }
            Add(new StageSignMask(choices));
            if (run.IsInspection)
            {
                exit = HangingSign.Exit("close-event", Leave, belowTopBar: true);
                Add(new StageSignMask(exit));
            }
            RenderChoices();
        }

        private static Label PaperText(string text, int size, Color? color = null) =>
            PrototypeStyle.Text(text, size, color ?? PaperInk);

        private void Collect(string id, Func<bool> grant) => CollectFrom(choices.Q<Button>(id), grant);

        private void CollectFrom(VisualElement source, Func<bool> grant)
        {
            if (topBar.CollectReward(source, grant))
                RenderChoices();
        }

        private void Decline()
        {
            if (run.IsInspection) Leave();
            else if (run.DeclineEvent())
            {
                leftTable = run.Current.Event is AdventureEventKind.HatTrick or AdventureEventKind.Alchemist;
                if (leftTable)
                {
                    hatGame?.RemoveFromHierarchy();
                    potionTargets?.RemoveFromHierarchy();
                    choices.Reset(false);
                    stage.LeaveTable(RenderChoices);
                }
                else RenderChoices();
            }
        }

        private void Leave()
        {
            if (topBar.HasRewardFlights)
            {
                topBar.WhenRewardsArrive(Leave);
                return;
            }

            if (leaving) return;
            leaving = true;
            stage.LeaveTable(() => DockPlayhouse.ChangeSet(() =>
            {
                if (run.Current.Resolved) run.CloseVisit(); else run.Leave();
                close();
            }, toSigns: true));
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
                AdventureEventKind.Ambush or AdventureEventKind.AscensionTrial or AdventureEventKind.LostTraveler)
                RenderChoices();
        }

        private void RenderChoices()
        {
            hatGame?.RemoveFromHierarchy();
            hatGame = null;
            potionTargets?.RemoveFromHierarchy();
            potionTargets = null;
            choices.Reset(run.Current.Event == AdventureEventKind.Forge);
            PopulateChoices();
            var visit = run.Current;
            if (!run.IsInspection && visit.Kind == NodeKind.Event && !visit.Resolved && visit.Battle == null &&
                visit.Event != AdventureEventKind.LockedChest && visit.PendingRecruit == null)
                choices.Choice("向对方告辞", "decline-event", Decline);
            choices.Arrange();
            if ((!run.IsInspection || visit.Event.HasValue) && visit.Resolved && !awaitingSigns)
            {
                awaitingSigns = true;
                topBar.WhenRewardsArrive(() => schedule.Execute(() =>
                {
                    if (enabledInHierarchy && !leaving && run.Current == visit && (visit.Event is not (AdventureEventKind.HatTrick or AdventureEventKind.Alchemist) || leftTable || stage.RevealFinished))
                    {
                        if (run.IsInspection) Leave();
                        else AdventureFlow.AddFollowingSigns(this, timber, topBar);
                    }
                }).Every(25).Until(() => leaving || run.Current != visit || this.Q<DockRouteSigns>() != null));
            }
        }

        private void PopulateChoices()
        {
            refreshCast();
            topBar.Refresh();
            var visit = run.Current;
            if (exit != null)
            {
                exit.name = visit.Resolved ? "finish-event" : "close-event";
                exit.style.display = treasureChest == null || visit.Resolved ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (visit.Event == AdventureEventKind.HatTrick && !leftTable)
            {
                hatGame = new HatGameView(run, stage, content, CollectFrom, choices);
                Insert(0, hatGame);
            }
            if (treasureChest != null)
            {
                treasureChest.Refresh();
                return;
            }
            if (visit.Resolved)
            {
                if (hatGame == null) choices.Note(PaperText(visit.Message, 17, PaperAccent));
                return;
            }
            if (visit.Battle?.Settled == true)
            {
                AdventureWidgets.BattleRewards(choices, run, content, RenderChoices, Collect);
                return;
            }
            if (visit.PendingRecruit != null)
            {
                AdventureWidgets.RecruitReplacement(choices, run, content, RenderChoices);
                return;
            }
            if (!string.IsNullOrEmpty(visit.Message)) choices.Note(PaperText(visit.Message, 14, PaperAccent));
            switch (visit.Event)
            {
                case AdventureEventKind.Investor:
                    if (run.InvestmentPaid || !visit.InvestorAvailable)
                    {
                        choices.Note(PaperText(run.InvestmentPaid
                            ? "泊位空了。缆桩上系着一张短笺，写着「多谢」。"
                            : "创业者合上账本，夹起图纸。“今天就到这儿吧，改日再聊。”", 16));
                        break;
                    }
                    if (run.Investment > 0)
                        choices.Note(PaperText("她翻开账本，你认出了自己上回留下的签名。", 16));
                    var amount = new IntegerField("出资金额") { name = "investment-amount", value = Math.Min(run.Party.Gold, 1) };
                    amount.style.marginTop = amount.style.marginBottom = 15;
                    amount.labelElement.style.color = PaperInk;
                    amount.labelElement.style.minWidth = 90;
                    amount.labelElement.style.width = 110;
                    choices.Note(amount);
                    choices.Note(PaperText($"可投入 1～{run.Party.Gold} 金币", 13, PaperMuted));
                    AdventureWidgets.Action(choices, "出一份力", "event-invest",
                        () => Collect("event-invest", () => run.Donate(amount.value)), run.Party.Gold > 0);
                    AdventureWidgets.Action(choices, "把金币全留给她", "event-invest-all",
                        () => Collect("event-invest-all", () => run.Donate(run.Party.Gold)), run.Party.Gold > 0);
                    break;
                case AdventureEventKind.FoundCoins:
                    AdventureWidgets.Action(choices, "拾起钱袋", "event-collect-coins", () => Collect("event-collect-coins", run.CollectCoins));
                    break;
                case AdventureEventKind.LostTraveler:
                    choices.Note(PaperText("你打算怎么搭话？", 18, PaperAccent));
                    foreach (var recruit in visit.Recruits)
                    {
                        var profession = recruit.Profession;
                        var option = AdventureWidgets.Action(choices, $"“你是{AdventureFlow.ProfessionName(profession)}？”",
                            "event-profession-" + profession, () => { run.ChooseProfession(profession); RenderChoices(); },
                            !run.Party.Members.Any(member => member.Character.Id == recruit.Id));
                        CharacterChoicePreview.Attach(option, recruit, content.PortraitFor(recruit.Id));
                    }
                    if (visit.Professions.Count == 0) choices.Note(PaperText("旅行者问清了路，笑着向你们道别。", 15));
                    break;
                case AdventureEventKind.Forge:
                    choices.Note(PaperText($"挑两件背包里的旧物  {forge.Count}／2", 17, PaperAccent));
                    AdventureWidgets.Action(choices, "把旧物交给铁匠", "event-forge-confirm",
                        () => Collect("event-forge-confirm", () => run.Forge(forge[0], forge[1])), forge.Count == 2);
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
                        choices.Note(PaperText("要留一位同伴试试这把锁吗？", 18, PaperAccent));
                        AdventureWidgets.Action(choices, "留一人开宝箱", "chest-choose-companion", () =>
                        { choosingChestCompanion = true; RenderChoices(); }, present.Any(m => run.Party.CanSendAway(m.Id)));
                        AdventureWidgets.Action(choices, "把箱子留在原处", "chest-skip", Decline);
                        break;
                    }

                    choices.Note(PaperText("谁留下开锁，晚些再跟上？", 18, PaperAccent));
                    foreach (var member in present)
                        AdventureWidgets.Character(choices, member.Character, content, "leave-companion-" + member.Id, () =>
                        { run.LeaveCompanion(member.Id); RenderChoices(); }, run.Party.CanSendAway(member.Id), member);
                    AdventureWidgets.Action(choices, "返回", "chest-back", () =>
                    { choosingChestCompanion = false; RenderChoices(); });
                    break;
                }
                case AdventureEventKind.AscensionTrial:
                    AdventureWidgets.Action(choices, "收下台阶上的包裹", "event-trial-gift",
                        () => Collect("event-trial-gift", run.TakeTrialGift));
                    AdventureWidgets.Action(choices, "跨过门槛，接受挑战", "event-trial-battle", StartBattle,
                        run.Party.CanStartBattle && run.EvolutionCandidates.Count > 0);
                    break;
                case AdventureEventKind.Ambush:
                    AdventureWidgets.Action(choices, "拔出武器，迎上去", "event-ambush-battle", StartBattle, run.Party.CanStartBattle);
                    break;
                case AdventureEventKind.Alchemist:
                    ShowPotionBottles(visit);
                    break;
            }
        }

        private void ShowPotionBottles(AdventureVisit visit)
        {
            potionTargets = new VisualElement { name = "potion-table", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(potionTargets);
            Insert(0,potionTargets);
            var table = potionTargets;
            for (int i = 0; i < visit.Potions.Count; i++)
            {
                int index = i;
                var clue = PotionClue(visit.Potions[i].Effect);
                var bottle = new Button { name = "event-potion-" + index };
                bottle.style.position = Position.Absolute;
                bottle.style.marginLeft = bottle.style.marginTop = 0;
                bottle.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(bottle,Color.clear,0);
                table.Add(bottle);
                var tag = PaperText(clue.Name + " · " + clue.Description,14);
                tag.pickingMode = PickingMode.Ignore;
                tag.style.backgroundColor = new Color(.98f,.96f,.89f);
                tag.style.position = Position.Absolute;
                tag.style.width = 174;
                tag.style.left = Length.Percent(0);
                tag.style.top = Length.Percent(100);
                tag.style.paddingLeft = tag.style.paddingRight = 7;
                tag.style.paddingTop = tag.style.paddingBottom = 4;
                tag.style.display = DisplayStyle.None;
                bottle.style.overflow = Overflow.Visible;
                bottle.Add(tag);
                bottle.RegisterCallback<PointerEnterEvent>(_ => tag.style.display = DisplayStyle.Flex);
                bottle.RegisterCallback<PointerLeaveEvent>(_ => tag.style.display = DisplayStyle.None);
                bottle.RegisterCallback<FocusInEvent>(_ => tag.style.display = DisplayStyle.Flex);
                bottle.RegisterCallback<FocusOutEvent>(_ => tag.style.display = DisplayStyle.None);
                bottle.clicked += () =>
                {
                    if (!stage.AtTable || run.Current != visit || visit.Resolved) return;
                    CollectFrom(bottle, () =>
                    {
                        if (!run.ChoosePotion(index)) return false;
                        stage.TakeBottle(index);
                        return true;
                    });
                };
                bottle.SetEnabled(false);
                bottle.schedule.Execute(() =>
                {
                    Rect uv = stage.BottleTarget(index);
                    var screen = table.panel.visualTree.worldBound;
                    Vector2 pos = table.WorldToLocal(screen.min + Vector2.Scale(uv.min,screen.size));
                    bottle.style.left = pos.x;
                    bottle.style.top = pos.y;
                    bottle.style.width = uv.width * screen.width;
                    bottle.style.height = uv.height * screen.height;
                    bottle.SetEnabled(stage.AtTable && !leaving);
                }).Every(16);
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
            if (run.StartEventBattle()) startEncounter();
        }
    }
}
