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
        private static bool StagedStory(AdventureEventKind? kind) => GraveStories.Contains(kind) || CapitalStories.Contains(kind);

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
        private string tideGear, tideBook;
        private readonly TreasureChestView treasureChest;
        private bool awaitingSigns, leftTable, leaving;

        internal AdventureEventWindow(AdventureRun run, AdventureContentAsset art, DockEventStage stage, Action close, RunTopBar topBar, Action refreshCast, Action startEncounter)
        {
            this.run = run; this.art = art; this.close = close; content = art.Encounter;
            this.topBar = topBar;
            this.refreshCast = refreshCast;
            this.startEncounter = startEncounter;
            this.stage = stage;
            name = "event-window";
            PrototypeStyle.Fill(this);
            style.overflow = Overflow.Hidden;
            GameAudio.Play(SoundCue.UiParchmentOpen, .39f);
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
                SceneryFlipbook.Animate(scenery, art.Backdrop);
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
            if (eventKind == AdventureEventKind.Auction)
            {
                Insert(0, new AuctionTableView(run, stage, topBar, Leave, this.Q<Label>("event-description")));
                topBar.Refresh();
                return;
            }
            choices = new EventChoiceRack(notes);
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
                if (run.Leave()) AdventureFlow.AutoSave(run);
                close();
            }, toSigns: true));
        }

        internal void RefreshAfterPartyEdit()
        {
            var visit = run.Current;
            if (visit.Event == AdventureEventKind.Auction) { topBar.Refresh(); return; }
            if (visit.Event == AdventureEventKind.Forge)
            {
                var available = run.ForgeInputs.Select(item => item.Id).ToHashSet();
                forge.RemoveAll(id => !available.Contains(id));
            }
            if (StagedStory(visit.Event) || visit.PendingRecruit != null || visit.Event is AdventureEventKind.TidePact or AdventureEventKind.Forge or AdventureEventKind.LockedChest or
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
                !GraveStories.Contains(visit.Event) && visit.Event is not (AdventureEventKind.LockedChest or AdventureEventKind.GraveSeed) && visit.PendingRecruit == null)
                choices.Choice("向对方告辞", "decline-event", Decline);
            choices.Arrange();
            if ((!run.IsInspection || visit.Event.HasValue && !StagedStory(visit.Event)) && visit.Resolved && !awaitingSigns)
            {
                awaitingSigns = true;
                AdventureFlow.AutoSaveResolvedVisit(run);
                topBar.WhenRewardsArrive(() => schedule.Execute(() =>
                {
                    if (stage.GraveCueFinished && enabledInHierarchy && !leaving && run.Current == visit && (visit.Event is not (AdventureEventKind.HatTrick or AdventureEventKind.Alchemist) || leftTable || stage.RevealFinished))
                    {
                        if (run.IsInspection) Leave();
                        else AdventureFlow.AddFollowingSigns(this, topBar);
                    }
                }).Every(25).Until(() => leaving || run.Current != visit || this.Q<DockRouteSigns>() != null));
            }
        }

        private void PopulateChoices()
        {
            refreshCast();
            topBar.Refresh();
            var visit = run.Current;
            var speech = this.Q<TellerSpeech>()?.Q<Label>("event-description");
            if (speech != null && visit.Event is { } topic)
                speech.text = topic switch
                {
                    _ when visit.Resolved => visit.Message,
                    _ when StagedStory(topic) && !string.IsNullOrEmpty(visit.Message) => visit.Message,
                    _ when visit.PendingRecruit != null => "队伍满了。点选哪位老队员的牌子，他就会离队，把位置让给新人。",
                    AdventureEventKind.LockedChest when choosingChestCompanion => "选一位留下开锁的队员。要是还想带他赶路，点击「返回」就能重选。",
                    AdventureEventKind.Forge when run.ForgeInputs.Count < 2 => "这炉要用两件旧装备。您背包里的闲置装备还没凑够，可以整理装备，也可以点击告辞牌离开。",
                    AdventureEventKind.Investor when run.InvestmentPaid => "两件装备已经交给你们了。船也出港了，咱们继续赶路吧。",
                    AdventureEventKind.Investor when !visit.InvestorAvailable => "船主今天已经收好账本，这一趟不能再投钱了。",
                    _ => AdventureFlow.Narrative(topic)
                };
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
                if (speech == null && hatGame == null) choices.Note(PaperText(visit.Message, 17, PaperAccent));
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
            if (!StagedStory(visit.Event) && !string.IsNullOrEmpty(visit.Message))
                choices.Note(PaperText(visit.Message, 14, PaperAccent));
            if (StagedStory(visit.Event))
            {
                bool city = CapitalStories.Contains(visit.Event);
                var offers = city
                    ? CapitalStories.For(visit.Event.Value).Choices.Select((c, i) => (key: i.ToString(), text: c.Label, ready: run.CanTakeCapitalChoice(c)))
                    : run.GraveChoices().Select(o => (key: o.key, text: o.text, ready: true));
                foreach (var option in offers)
                {
                    choices.Choice(option.text, (city ? "capital-" : "grave-") + option.key, () =>
                    {
                        if (!(city ? run.ChooseCapital(int.Parse(option.key)) : run.ChooseGrave(option.key))) return;
                        var kind = visit.Event.Value;
                        var sound = StorySounds.Choice(kind, option.key, visit.Resolved);
                        if (kind == AdventureEventKind.SoulWard)
                        {
                            stage.SyncPatients(run, content, true);
                            GameAudio.Play(sound, .51f);
                        }
                        else stage.CueGraveAction(sound, kind == AdventureEventKind.BellRope && option.key == "three" ? 3 : 1);
                        choices.Reset(false);
                        choices.SetEnabled(false);
                        schedule.Execute(() =>
                        {
                            if (leaving || run.Current != visit || !stage.GraveCueFinished) return;
                            choices.SetEnabled(true);
                            RenderChoices();
                        }).Every(25).Until(() => leaving || run.Current != visit || choices.enabledSelf);
                    }, option.ready);
                }
                return;
            }
            switch (visit.Event)
            {
                case AdventureEventKind.Investor:
                    if (run.InvestmentPaid || !visit.InvestorAvailable)
                        break;
                    if (run.Investment > 0)
                        choices.Note(PaperText($"账本还在。此前已投入 {run.Investment}／50 金币。", 16));
                    var amount = new IntegerField("出资金额") { name = "investment-amount", value = Math.Min(run.Party.Gold, 1) };
                    amount.style.marginTop = amount.style.marginBottom = 15;
                    amount.labelElement.style.color = PaperInk;
                    amount.labelElement.style.minWidth = 90;
                    amount.labelElement.style.width = 110;
                    choices.Note(amount);
                    choices.Note(PaperText($"可投入 1～{run.Party.Gold} 金币", 13, PaperMuted));
                    AdventureWidgets.Action(choices, "投入填写的金额", "event-invest",
                        () => Collect("event-invest", () => run.Donate(amount.value)), run.Party.Gold > 0);
                    AdventureWidgets.Action(choices, "投入全部金币", "event-invest-all",
                        () => Collect("event-invest-all", () => run.Donate(run.Party.Gold)), run.Party.Gold > 0);
                    break;
                case AdventureEventKind.GraveSeed:
                    choices.Choice("拿走 50 金币", "grave-seed-gold",
                        () => Collect("grave-seed-gold", () => run.TakeGraveSeedReward(false)));
                    choices.Choice("拿走奇怪的种子", "grave-seed-item",
                        () => Collect("grave-seed-item", () => run.TakeGraveSeedReward(true)));
                    break;
                case AdventureEventKind.TidePact:
                    ShowTidePact();
                    break;
                case AdventureEventKind.FoundCoins:
                    AdventureWidgets.Action(choices, "拾起钱袋", "event-collect-coins", () => Collect("event-collect-coins", run.CollectCoins));
                    break;
                case AdventureEventKind.LostTraveler:
                    foreach (var recruit in visit.Recruits)
                    {
                        var profession = recruit.Profession;
                        AdventureWidgets.Action(choices, $"邀请{AdventureFlow.ProfessionName(profession)}同行",
                            "event-profession-" + profession, () => { if (run.ChooseProfession(profession) && run.Current.PendingRecruit == null) GameAudio.Play(SoundCue.CompanionJoin); RenderChoices(); },
                            !run.Party.Members.Any(member => member.Character.Id == recruit.Id));
                    }
                    if (visit.Professions.Count == 0) choices.Note(PaperText("这次没有可以邀请的旅人，可以继续赶路。", 15));
                    break;
                case AdventureEventKind.Forge:
                    choices.Note(PaperText($"挑两件背包里的旧物  {forge.Count}／2", 17, PaperAccent));
                    AdventureWidgets.Action(choices, "确认消耗两件装备", "event-forge-confirm",
                        () => Collect("event-forge-confirm", () =>
                        {
                            if (!run.Forge(forge[0], forge[1]))
                            {
                                RenderChoices();
                                return false;
                            }
                            GameAudio.Play(SoundCue.ForgeComplete);
                            return true;
                        }), forge.Count == 2);
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
                        { if (run.LeaveCompanion(member.Id)) GameAudio.Play(SoundCue.ChestLatchRelease); RenderChoices(); }, run.Party.CanSendAway(member.Id), member);
                    AdventureWidgets.Action(choices, "返回", "chest-back", () =>
                    { choosingChestCompanion = false; RenderChoices(); });
                    break;
                }
                case AdventureEventKind.AscensionTrial:
                    AdventureWidgets.Action(choices, "拿包裹离开", "event-trial-gift",
                        () => Collect("event-trial-gift", run.TakeTrialGift));
                    AdventureWidgets.Action(choices, "挑战精英，争取升阶", "event-trial-battle", StartBattle,
                        run.Party.CanStartBattle && run.EvolutionCandidates.Count > 0);
                    break;
                case AdventureEventKind.Ambush:
                    AdventureWidgets.Action(choices, "迎战伏兵", "event-ambush-battle", StartBattle, run.Party.CanStartBattle);
                    break;
                case AdventureEventKind.Alchemist:
                    ShowPotionBottles(visit);
                    break;
            }
        }

        private void ShowTidePact()
        {
            var items = run.TideOfferings.ToArray();
            var gear = items.Where(i => i.Definition is not SkillBookDefinition).ToArray();
            var books = items.Where(i => i.Definition is SkillBookDefinition).ToArray();
            bool room = run.Party.Members.Count < PartyRun.RosterLimit;
            if (!room || gear.Length == 0 || books.Length == 0)
            {
                if (!room) choices.Note(PaperText("队伍已满，需要一个空位。", 14));
                if (gear.Length == 0) choices.Note(PaperText("背包里缺少可交付的闲置装备。", 14));
                if (books.Length == 0) choices.Note(PaperText("背包里缺少未学习的技能书。", 14));
                return;
            }
            tideGear = Picker("装备", "tide-gear", gear, tideGear, id => tideGear = id);
            tideBook = Picker("技能书", "tide-book", books, tideBook, id => tideBook = id);
            choices.Choice("交付两件物品并签约", "sign-tide-pact", () =>
            {
                if (run.OfferTidePact(tideGear, tideBook))
                {
                    stage.HideTideSinger();
                    GameAudio.Play(SoundCue.CompanionJoin);
                }
                RenderChoices();
            }, tideGear != null && tideBook != null);

            string Picker(string label, string id, EquipmentInstance[] stock, string selected, Action<string> set)
            {
                // Identical copies are interchangeable. Keep the exact instance selected until signing.
                var options = stock.GroupBy(item => item.Definition.Id).Select(group =>
                    group.FirstOrDefault(item => item.Id == selected) ?? group.First()).ToArray();
                int index = Array.FindIndex(options, item => item.Id == selected);
                var names = options.Select(item => item.Definition.Name).Prepend("请选择").ToList();
                var field = new DropdownField(label, names, index + 1) { name = id };
                field.labelElement.style.minWidth = 50;
                field.labelElement.style.width = 50;
                field.labelElement.style.color = PaperInk;
                field.RegisterValueChangedCallback(_ =>
                {
                    set(field.index > 0 ? options[field.index - 1].Id : null);
                    RenderChoices();
                });
                choices.Note(field);
                return index >= 0 ? options[index].Id : null;
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
