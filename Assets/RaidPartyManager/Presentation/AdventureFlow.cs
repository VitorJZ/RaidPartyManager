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
        private static AdventureRun inspection;
        private static AdventureRun route;
        private static string destination;
        private static string origin;
        private static int lastMapProgress = -1;
        private static string checkpointPathOverride;
        private static string CheckpointPath => checkpointPathOverride ??
            Path.Combine(Application.persistentDataPath, "raid-adventure-v1.json");
        private static string invalidatedPath;
        private static string InvalidatedSaveKey => "RaidPartyManager.InvalidatedSave:" + CheckpointPath;
        internal static bool HasSave => File.Exists(CheckpointPath) && invalidatedPath != CheckpointPath &&
            PlayerPrefs.GetInt(InvalidatedSaveKey, 0) == 0;
        internal static AdventureRun Active { get; private set; }
        internal static bool InspectionMode { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            inspection = route = Active = null;
            lastMapProgress = -1;
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

            lastMapProgress = -1;
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
            lastMapProgress = -1;
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
            InspectionMode = inspectionMode;
            destination = "EventSelect";
            PrototypeSceneMenu.OpenScene("EventSelect");
        }

        internal static bool ShowMapAdvance(AdventureRun run)
        {
            if (run.RouteDestination.HasValue)
                return false;

            bool advanced = !run.SuspendedChoice.HasValue && lastMapProgress >= 0 &&
                run.CompletedNodes == lastMapProgress + 1;
            lastMapProgress = run.CompletedNodes;
            return advanced;
        }

        internal static AdventureRun ForScene(string scene) => destination == scene ? Active : null;

        internal static void OpenNodeScene(string scene, bool fromTown = false)
        {
            origin = fromTown ? "Town" : "EventSelect";
            destination = scene;
            PrototypeSceneMenu.OpenScene(scene);
        }

        internal static Button ReturnButton(Action onReturn = null)
        {
            string caption = origin == "Town" ? "返回城镇" : "返回事件选择";
            return PrototypeStyle.Button(caption, "return-event-select", onReturn ?? ReturnFromNode);
        }

        internal static void ReturnFromNode()
        {
            if (origin == "Town")
            {
                destination = "Town"; origin = "EventSelect";
                PrototypeSceneMenu.OpenScene("Town");
                return;
            }
            if (destination != "BattlePrototype") Active.Leave();
            else if (Active.Current?.Resolved == true) Active.CloseVisit();
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
            AdventureEventKind.Investor =>
                "创业者用一颗旧螺帽压住图纸的一角。纸上的船已经扬帆，水里的那艘却连桅杆都还没有。\n“添一枚也是添。等它能下水了，我记得来过的人。”",
            AdventureEventKind.Ambush =>
                "栈桥两头的灯一盏接一盏熄了。雾里有人把刀鞘掂在手上，另一个人的腰间响了一声，像是几枚硬币碰在一起。\n“到这儿就够了。”",
            AdventureEventKind.LostTraveler =>
                "旅行者把地图翻了个面，还是没找到要去的地方。见你们穿过雾，他赶紧卷起地图。\n“你们认得路？要是顺路，能不能一起走一段？”",
            AdventureEventKind.FoundCoins =>
                "一只湿漉漉的钱袋卡在栈桥的木板缝里。海鸥啄了两下，嫌它不能吃，扭头飞走了。",
            AdventureEventKind.HatTrick =>
                "戏法师用指节敲敲最左边的帽檐，抬起来让你看了一眼。\n“记好它在哪儿。等我停手，你指一顶给我看。”",
            AdventureEventKind.Forge =>
                "铁匠从炉膛里拨开一层白灰，底下的炭还红着。\n“翻翻背包。找两件舍得熔掉的旧物，说不定还能打出点新花样。”",
            AdventureEventKind.LockedChest =>
                "锁孔叫海盐堵了个严实，箱子一晃，里面倒有东西在响。\n一名队员蹲下来端详了片刻。“想开的话，得留个人慢慢弄。等锁开了，留下的人再追上来。”",
            AdventureEventKind.AscensionTrial =>
                "守门人把一包东西搁在台阶上。门缝里透出火光，里头有人拖着一柄重剑走过石地。\n“礼物在这儿。你们也可以往里走，看看自己还有什么本事没使出来。”",
            _ =>
                "炼药师把几只没贴签的瓶子一字排开，有一只的瓶塞还在轻轻响。\n“随便挑一瓶。钱就免了，改天记得告诉我尝起来怎么样。”"
        };
    }
}
