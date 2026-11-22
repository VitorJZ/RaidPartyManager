using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal static class AdventureFlow
    {
        private static AdventureContentAsset routeArt;
        private static AdventureRun inspection;
        private static AdventureRun route;
        private static string destination;
        private static string origin;
        private static string checkpointPathOverride;
        private static string CheckpointPath => checkpointPathOverride ??
            Path.Combine(Application.persistentDataPath, "raid-adventure-v1.json");
        private static string invalidatedPath;
        private static string InvalidatedSaveKey => "RaidPartyManager.InvalidatedSave:" + CheckpointPath;
        internal static bool HasSave => File.Exists(CheckpointPath) && invalidatedPath != CheckpointPath &&
            PlayerPrefs.GetInt(InvalidatedSaveKey, 0) == 0;
        internal static AdventureRun Active { get; private set; }
        internal static bool OnRoute => destination != null && Active is { IsInspection: false } && !InspectionMode;
        internal static bool InspectionMode { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            inspection = route = Active = null;
            destination = origin = null;
            InspectionMode = true;
        }

        private static bool IsBattleScene(string scene) => scene is "BattlePrototype" or "WhisperingGraveyard" or "ClockworkCapital";

        internal static void ClearSceneContext()
        {
            if (IsBattleScene(destination)) Active?.AbortBattle();
            destination = origin = null;
        }

        internal static AdventureRun Select(AdventureContentAsset assets, bool inspectionMode, bool reset = false)
        {
            routeArt = assets;
            InspectionMode = inspectionMode;
            if (inspectionMode)
            {
                if (reset || inspection == null)
                {
                    var content = assets.Encounter;
                    var random = new System.Random();
                    inspection = CreateRun(content, DrawInspectionParty(content, random), true, random);
                }
            }
            else if (reset)
                route = null;
            else if (route == null && HasSave)
                route = LoadCheckpoint(assets.Encounter);

            Active = inspectionMode ? inspection : route;
            destination = "EventSelect";
            return Active;
        }

        internal static CharacterDefinition[] DrawStarters(EncounterData content, System.Random random)
        {
            var pool = content.CharacterCatalog.Where(c => c.RosterNumber > 0 && c.Stage == 1).ToList();
            var warriors = pool.Where(c => c.Profession == Profession.Warrior).ToArray();
            var warrior = warriors[random.Next(warriors.Length)];
            pool.Remove(warrior);

            var starters = new List<CharacterDefinition>(4);
            for (int i = 0; i < 3; i++)
            {
                int pick = random.Next(pool.Count);
                starters.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            starters.Insert(random.Next(4), warrior);
            return starters.ToArray();
        }

        private static CharacterDefinition[] DrawInspectionParty(EncounterData content, System.Random random)
        {
            var initial = content.CharacterCatalog.Where(c => c.RosterNumber > 0 && c.Stage == 1).ToArray();
            return new[] { Profession.Warrior, Profession.Mage, Profession.Archer, Profession.Support }.Select(profession =>
            {
                var pool = initial.Where(c => c.Profession == profession).ToArray();
                var branches = pool.Where(c => c.EvolutionIds.Count > 0).ToArray();
                if (branches.Length > 0) pool = branches;
                return pool[random.Next(pool.Length)];
            }).ToArray();
        }

        internal static AdventureRun StartRoute(EncounterData content, IReadOnlyList<CharacterDefinition> selected)
        {
            if (selected.Count != 2 || selected[0].Id == selected[1].Id)
                throw new ArgumentException("A new route needs two different starters.", nameof(selected));

            return Active = route = CreateRun(content, selected, false, new System.Random());
        }

        private static AdventureRun CreateRun(EncounterData content, IReadOnlyList<CharacterDefinition> starters,
            bool inspectionMode, System.Random random)
        {
            var members = starters.Select((character, index) => new PartyMemberStart(
                "crew-" + (index + 1), character, index + 1, character.BaseStats.Health));
            var gear = content.StartingEquipment.GroupBy(item => item.Id).Select(group => group.First()).ToArray();
            var bag = inspectionMode
                ? gear.Where(item => item.AvailableInRegularPool && item.Price > 0 && item.Tier == 3).OrderBy(_ => random.Next()).Take(2)
                    .Concat(gear.Where(item => item.AvailableInRegularPool && item.Price > 0 && item.Tier == 2).OrderBy(_ => random.Next()).Take(2))
                : gear.Where(item => !item.IsPotion && item.Tier is 1 or 2).OrderBy(_ => random.Next()).Take(2);
            var party = new PartyRun(members, bag);
            party.AddGold(inspectionMode ? 100 : 50);
            return new AdventureRun(party, content.CharacterCatalog, gear.Concat(content.SkillBooks), inspectionMode, random, foundingGrant: true);
        }

        private static AdventureRun LoadCheckpoint(EncounterData content)
        {
            try
            {
                var saved = JsonUtility.FromJson<AdventureCheckpoint>(File.ReadAllText(CheckpointPath));
                if (saved is { Failed: true })
                {
                    InvalidateCheckpoint();
                    return null;
                }
                var items = content.StartingEquipment.Concat(content.SkillBooks)
                    .Append(content.VictoryReward).Where(item => item != null);
                return AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, items);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or KeyNotFoundException)
            {
                Debug.LogWarning("Unable to load the saved adventure: " + exception.Message);
                return null;
            }
        }

        internal static bool CanSave(PartyRun party) =>
            destination != null && Active is { IsInspection: false } && ReferenceEquals(Active.Party, party);

        internal static string SaveAndExit(PartyRun party)
        {
            if (!CanSave(party))
                return "当前没有可保存的讨伐。";

            string path = CheckpointPath;
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, JsonUtility.ToJson(Active.CheckpointForExit()));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning("Unable to save the adventure: " + exception.Message);
                return "保存失败，请检查存档目录后重试。";
            }

            ClearInvalidatedCheckpoint();
            EndRouteAndOpenMenu();
            return null;
        }

        internal static string Abandon(PartyRun party)
        {
            if (!CanSave(party))
                return "当前没有可放弃的讨伐。";

            if (!TryDeleteCheckpoint())
                return "放弃失败，请检查存档目录后重试。";

            Active.AbortBattle();
            EndRouteAndOpenMenu();
            return null;
        }

        internal static void EndWipedRoute()
        {
            InvalidateCheckpoint();
            ClearRoute();
        }

        private static void InvalidateCheckpoint()
        {
            if (TryDeleteCheckpoint())
                return;

            // If the file is read-only, keep it from offering a continue on the next launch.
            invalidatedPath = CheckpointPath;
            try
            {
                PlayerPrefs.SetInt(InvalidatedSaveKey, 1);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException exception)
            {
                Debug.LogWarning("Unable to mark the old adventure invalid: " + exception.Message);
            }
        }

        private static bool TryDeleteCheckpoint()
        {
            string path = CheckpointPath;
            try
            {
                File.Delete(path + ".tmp");
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // A run can end before the save directory has been created.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning("Unable to delete the saved adventure: " + exception.Message);
                return false;
            }
            ClearInvalidatedCheckpoint();
            return true;
        }

        private static void ClearInvalidatedCheckpoint()
        {
            if (invalidatedPath == CheckpointPath)
                invalidatedPath = null;
            if (!PlayerPrefs.HasKey(InvalidatedSaveKey))
                return;
            PlayerPrefs.DeleteKey(InvalidatedSaveKey);
            PlayerPrefs.Save();
        }

        private static void EndRouteAndOpenMenu()
        {
            ClearRoute();
            PrototypeSceneMenu.OpenScene("MainMenu");
        }

        private static void ClearRoute()
        {
            Active = route = null;
            destination = origin = null;
        }

        internal static void OpenHub() => OpenHub(true);

        internal static void OpenRoute()
        {
            if (HasSave)
                route = null;
            OpenHub(false);
        }

        private static void OpenHub(bool inspectionMode)
        {
            ClearSceneContext();
            if (inspectionMode && inspection?.Current?.Event.HasValue == true) inspection = null;
            InspectionMode = inspectionMode;
            destination = "EventSelect";
            PrototypeSceneMenu.OpenScene("EventSelect");
        }

        internal static bool FromTownFacility => origin == "Town";

        internal static AdventureRun ForScene(string scene) => destination == scene ? Active : null;

        internal static AdventureRun TalentMarketRun(AdventureContentAsset assets)
        {
            if (ForScene("TalentMarket") is { } current) return current;
            var run = Select(assets, true);
            if (run.Current?.Kind != NodeKind.Town) run.Inspect(NodeKind.Town);
            origin = "Town";
            destination = "TalentMarket";
            return run;
        }

        internal static void OpenNodeScene(string scene, bool fromTown = false, StageTurn turn = StageTurn.Automatic)
        {
            DockPlayhouse.ChangeSet(() =>
            {
                origin = fromTown ? "Town" : "EventSelect";
                destination = scene;
                PrototypeSceneMenu.OpenScene(scene);
            }, turn: scene == "TalentMarket" ? StageTurn.Forward : turn,
                townVisit: fromTown || scene == "Town" || destination == "Town" || origin == "Town");
        }

        internal static string SceneFor(NodeKind kind) => kind switch
        {
            NodeKind.Event or NodeKind.TreasureChest => "EventSelect",
            NodeKind.Camp => "Camp", NodeKind.Shop or NodeKind.Town => "Town",
            _ => Active?.InCapital == true ? "ClockworkCapital" : Active?.InGraveyard == true ? "WhisperingGraveyard" : "BattlePrototype"
        };

        internal static void AddFollowingSigns(VisualElement parent, Texture2D stone, RunTopBar bar)
        {
            var run = Active;
            var visit = run.Current;
            DockRouteSigns signs = null;
            signs = new DockRouteSigns(run, routeArt, stone, Choose, null, following: true);
            parent.Add(new StageSignMask(signs));

            bool Choose(int index, StageTurn turn)
            {
                if (FromTownFacility || run.Current != visit || DockPlayhouse.Moving) return false;
                var kind = run.FollowingChoice(index).Kind.Value;
                if (!run.Party.CanAdvanceDay || kind is (NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !run.Party.CanStartBattle)
                {
                    if (signs.Q("route-entry-message") == null)
                    {
                        var note = PrototypeStyle.Text(!run.Party.CanAdvanceDay ? "请在编队中续约后再出发。" : "请先为队员安排出战站位。", 14);
                        note.name = "route-entry-message";
                        note.style.alignSelf = Align.Center;
                        signs.Add(note);
                    }
                    return false;
                }
                parent.SetEnabled(false);
                bar.WhenRewardsArrive(() =>
                {
                    if (FromTownFacility || run.Current != visit || !run.EnterFollowing(index))
                    {
                        parent.SetEnabled(true);
                        return;
                    }
                    GameAudio.Play(SoundCue.UiMapMarker);
                    OpenNodeScene(SceneFor(kind), turn: turn);
                });
                return true;
            }
        }

        internal static void AddTownReturnSign(VisualElement parent, Texture2D stone, string buttonName,
            Action returnToTown, Func<bool> visible = null)
        {
            var sign = new DockRouteSigns(stone, routeArt.NodeIcon(NodeKind.Town), buttonName, returnToTown, visible);
            parent.Add(new StageSignMask(sign));
        }

        internal static void AddServiceNavigation(VisualElement parent, Texture2D stone, RunTopBar bar, Func<bool> visible = null)
        {
            if (FromTownFacility)
                AddTownReturnSign(parent, stone, "return-event-select", () => bar.WhenRewardsArrive(ReturnFromNode), visible);
            else if (OnRoute && Active.CanChooseFollowing)
                AddFollowingSigns(parent, stone, bar);
            else
                parent.Add(new StageSignMask(ReturnButton(() => bar.WhenRewardsArrive(ReturnFromNode))));
        }

        internal static Button ReturnButton(Action onReturn = null)
        {
            return HangingSign.Exit("return-event-select", onReturn ?? ReturnFromNode);
        }

        internal static void ReturnFromNode()
        {
            // A district change must turn to the incoming set instead of holding the outgoing battle behind its signs.
            bool signs = origin != "Town" && Active?.CrossingChapter != true &&
                (!IsBattleScene(destination) || Active?.Current?.Resolved == true);
            DockPlayhouse.ChangeSet(FinishReturn, signs, destination == "TalentMarket" ? StageTurn.Reverse : StageTurn.Automatic,
                townVisit: origin == "Town" || destination == "Town");
        }

        private static void FinishReturn()
        {
            if (origin == "Town")
            {
                destination = "Town"; origin = "EventSelect";
                PrototypeSceneMenu.OpenScene("Town");
                return;
            }
            if (!IsBattleScene(destination)) Active.Leave();
            else if (Active.Current?.Resolved == true)
            {
                bool inspectedEvent = Active.IsInspection && Active.Current.Event.HasValue;
                Active.CloseVisit();
                if (inspectedEvent) Active = inspection = null;
            }
            destination = "EventSelect"; origin = null;
            PrototypeSceneMenu.OpenScene("EventSelect");
        }

        internal static string NodeName(NodeKind kind) => kind switch
        {
            NodeKind.Event => "随机事件", NodeKind.Camp => "营地", NodeKind.Shop => "商店", NodeKind.Town => "城镇",
            NodeKind.NormalBattle => "普通战", NodeKind.EliteBattle => "精英战",
            NodeKind.TreasureChest => "宝箱", _ => "Boss 战"
        };

        internal static string EventName(AdventureEventKind kind) => CapitalStories.Contains(kind) ? CapitalStories.For(kind).Title : GraveStories.Contains(kind) ? GraveStories.Title(kind) : kind switch
        {
            AdventureEventKind.Investor => "引资的创业者", AdventureEventKind.Ambush => "随机敌人",
            AdventureEventKind.LostTraveler => "呼朋引伴", AdventureEventKind.FoundCoins => "捡到钱了",
            AdventureEventKind.HatTrick => "帽子戏法", AdventureEventKind.Forge => "锻造台",
            AdventureEventKind.LockedChest => "上锁的宝箱", AdventureEventKind.AscensionTrial => "升阶试炼", _ => "炼药师的小白鼠"
        };

        internal static string ProfessionName(Profession profession) => profession switch
        {
            Profession.Warrior => "战士", Profession.Mage => "法师", Profession.Archer => "射手",
            Profession.Monster => "怪物", _ => "后勤"
        };

        internal static string Narrative(AdventureEventKind kind) => CapitalStories.Contains(kind) ? CapitalStories.For(kind).Intro + "\n" + string.Join("\n", CapitalStories.For(kind).Choices.Select(c => c.Label + "：" + c.Terms)) : GraveStories.Contains(kind) ? GraveStories.Intro(kind) : kind switch
        {
            AdventureEventKind.Investor => "船主还差钱造船。填好金额，再拉下出资牌。账上凑满 50 金币，她才送两件装备，不足 50 就先记账。",
            AdventureEventKind.Ambush => "灯一灭，埋伏的人就围上来了！准备好就拉下应战牌，打赢这场能拿 12～18 金币。",
            AdventureEventKind.LostTraveler => "雾里有人答应来投奔咱们。您喊一声，等人走到跟前，就知道谁应了招。",
            AdventureEventKind.FoundCoins => "海鸥正啄地上的钱袋呢。拉下拾取牌，把剩下的金币收进队伍钱包吧。",
            AdventureEventKind.HatTrick => "先记住展示宝物的那顶帽子。拉下「开始交换」，一直盯住它。等帽子停稳，点中它就能带走宝物。",
            AdventureEventKind.Forge => "铁匠肯用两件旧装备换一件更好的。先挑两件背包里没穿戴的，再确认锻造。确认后，两件旧装备都会用掉。",
            AdventureEventKind.LockedChest => "这把锁一时半会儿打不开。留一名队员开箱，他会缺席接下来三个节点，再带着收获归队。赶时间也可以直接走。",
            AdventureEventKind.AscensionTrial => "门外的包裹可以直接拿走。要是想让队员升阶，就放下包裹，挑战门里的精英。两条路只能选一条。",
            _ => "炼药师请客，三瓶药只能挑一瓶。把鼠标移到瓶上看线索，再点一瓶带走。药瓶装备给队员后，会在开战时用掉。"
        };
    }
}
