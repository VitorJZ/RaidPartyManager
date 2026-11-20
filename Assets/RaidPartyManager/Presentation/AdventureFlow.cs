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

        internal static void ClearSceneContext()
        {
            if (destination == "BattlePrototype") Active?.AbortBattle();
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
                ? gear.Where(item => item.Price > 0 && item.Tier == 3).OrderBy(_ => random.Next()).Take(2)
                    .Concat(gear.Where(item => item.Price > 0 && item.Tier == 2).OrderBy(_ => random.Next()).Take(2))
                : gear.Where(item => !item.IsPotion && item.Tier is 1 or 2).OrderBy(_ => random.Next()).Take(2);
            var party = new PartyRun(members, bag);
            if (inspectionMode)
                party.AddGold(100);
            return new AdventureRun(party, content.CharacterCatalog, gear.Concat(content.SkillBooks), inspectionMode, random);
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

        internal static AdventureRun ForScene(string scene) => destination == scene ? Active : null;

        internal static void OpenNodeScene(string scene, bool fromTown = false)
        {
            DockPlayhouse.ChangeSet(() =>
            {
                origin = fromTown ? "Town" : "EventSelect";
                destination = scene;
                PrototypeSceneMenu.OpenScene(scene);
            });
        }

        internal static string SceneFor(NodeKind kind) => kind switch
        {
            NodeKind.Event or NodeKind.TreasureChest => "EventSelect",
            NodeKind.Camp => "Camp", NodeKind.Shop => "DockShop", NodeKind.Town => "Town",
            _ => "BattlePrototype"
        };

        internal static void AddFollowingSigns(VisualElement parent, Texture2D timber, RunTopBar bar)
        {
            var run = Active;
            var visit = run.Current;
            DockRouteSigns signs = null;
            signs = new DockRouteSigns(run, routeArt, timber, null, Choose, null, following: true);
            parent.Add(new StageSignMask(signs));

            bool Choose(int index)
            {
                if (run.Current != visit || DockPlayhouse.Moving) return false;
                var kind = run.FollowingChoice(index).Kind.Value;
                if (kind is (NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !run.Party.CanStartBattle)
                {
                    if (signs.Q("route-entry-message") == null)
                    {
                        var note = PrototypeStyle.Text("请先为队员安排出战站位。", 14);
                        note.name = "route-entry-message";
                        note.style.alignSelf = Align.Center;
                        signs.Add(note);
                    }
                    return false;
                }
                parent.SetEnabled(false);
                bar.WhenRewardsArrive(() =>
                {
                    if (run.Current != visit || !run.EnterFollowing(index))
                    {
                        parent.SetEnabled(true);
                        return;
                    }
                    OpenNodeScene(SceneFor(kind));
                });
                return true;
            }
        }

        internal static void AddServiceNavigation(VisualElement parent, Texture2D timber, RunTopBar bar)
        {
            if (OnRoute && Active.CanChooseFollowing)
            {
                AddFollowingSigns(parent, timber, bar);
                if (origin != "Town") return;
                var back = PrototypeStyle.Button("返回城镇", "return-event-select", () => bar.WhenRewardsArrive(ReturnFromNode));
                back.style.position = Position.Absolute;
                back.style.left = 20;
                back.style.top = RunTopBar.Height + 45;
                parent.Add(back);
            }
            else parent.Add(new StageSignMask(ReturnButton(() => bar.WhenRewardsArrive(ReturnFromNode))));
        }

        internal static Button ReturnButton(Action onReturn = null)
        {
            return HangingSign.Exit("return-event-select", onReturn ?? ReturnFromNode);
        }

        internal static void ReturnFromNode()
        {
            bool signs = origin != "Town" && (destination != "BattlePrototype" || Active.Current?.Resolved == true);
            DockPlayhouse.ChangeSet(FinishReturn, signs);
        }

        private static void FinishReturn()
        {
            if (origin == "Town")
            {
                destination = "Town"; origin = "EventSelect";
                PrototypeSceneMenu.OpenScene("Town");
                return;
            }
            if (destination != "BattlePrototype") Active.Leave();
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

        internal static string EventName(AdventureEventKind kind) => kind switch
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

        internal static string Narrative(AdventureEventKind kind) => kind switch
        {
            AdventureEventKind.Investor => "诸位瞧，图纸上的船都快出海了，船坞里还欠一根桅杆。船主翻开账本，给您的名字留了个空。",
            AdventureEventKind.Ambush => "方才还有三盏灯，这会儿一盏也没了。听，那声刀出鞘，可不是我手里的醒铃！",
            AdventureEventKind.LostTraveler => "这位客官对着海图走了半日。您提醒她一句？她手里那张，拿倒了。",
            AdventureEventKind.FoundCoins => "啪嗒！钱袋开了口。您瞧那海鸥，鱼虾不理，倒先叼走了一枚金币。",
            AdventureEventKind.HatTrick => "且看桌上这三顶帽子。那宝贝刚躲进哪一顶，您可记准了？一眨眼，它们就要换座喽。",
            AdventureEventKind.Forge => "铁匠说，她这炉子专吃旧家伙，少了两件还不开口。至于吐出来个什么，她自己也想瞧瞧。",
            AdventureEventKind.LockedChest => "这箱子呀，锁比箱板还结实。您听里头那一响！要解开它，得有个人舍得在这儿坐上半晌。",
            AdventureEventKind.AscensionTrial => "看门人把礼匣搁在门槛外，自己退进门里磨刀去了。收礼的路跟试胆的路，就在这儿分开。",
            _ => "三瓶药排了一溜，炼药师偏把签儿揭了。她说这回免钱，只求您回来告诉她，哪瓶会打嗝。"
        };
    }
}
