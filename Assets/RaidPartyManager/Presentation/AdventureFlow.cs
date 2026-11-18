using System;
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
        internal static AdventureRun Active { get; private set; }
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
            InspectionMode = inspectionMode;
            if (reset || (inspectionMode ? inspection : route) == null)
            {
                var content = assets.Encounter;
                var random = new System.Random();
                var initial = content.CharacterCatalog.Where(c => c.RosterNumber > 0 && c.Stage == 1).ToArray();
                var members = Enum.GetValues(typeof(Profession)).Cast<Profession>().Select(profession =>
                {
                    var pool = initial.Where(c => c.Profession == profession).ToArray();
                    var branches = pool.Where(c => c.EvolutionIds.Count > 0).ToArray();
                    if (branches.Length > 0) pool = branches;
                    return pool[random.Next(pool.Length)];
                }).Select((c, i) =>
                        new PartyMemberStart("crew-" + (i + 1), c, i + 1, c.BaseStats.Health)).ToArray();
                var gear = content.StartingEquipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
                var bag = gear.Where(e => e.Tier == 3).OrderBy(_ => random.Next()).Take(2)
                    .Concat(gear.Where(e => e.Tier == 2).OrderBy(_ => random.Next()).Take(2)).ToArray();
                var party = new PartyRun(members, bag);
                party.AddGold(inspectionMode ? 100 : 30);
                var created = new AdventureRun(party, content.CharacterCatalog, gear, inspectionMode, random);
                if (inspectionMode) inspection = created; else route = created;
            }
            Active = inspectionMode ? inspection : route;
            destination = "EventSelect";
            return Active;
        }

        internal static void OpenHub()
        {
            ClearSceneContext();
            InspectionMode = true;
            destination = "EventSelect";
            PrototypeSceneMenu.OpenScene("EventSelect");
        }

        internal static AdventureRun ForScene(string scene) => destination == scene ? Active : null;

        internal static void OpenNodeScene(string scene, bool fromTown = false)
        {
            origin = fromTown ? "Town" : "EventSelect";
            destination = scene;
            PrototypeSceneMenu.OpenScene(scene);
        }

        internal static Button ReturnButton()
        {
            bool town = origin == "Town";
            return PrototypeStyle.Button(town ? "返回城镇" : "返回事件选择", "return-event-select", () =>
            {
                if (town)
                {
                    destination = "Town"; origin = "EventSelect";
                    PrototypeSceneMenu.OpenScene("Town");
                    return;
                }
                if (destination != "BattlePrototype") Active.Leave();
                else if (Active.Current?.Resolved == true) Active.CloseVisit();
                destination = "EventSelect"; origin = null;
                PrototypeSceneMenu.OpenScene("EventSelect");
            });
        }

        internal static string NodeName(NodeKind kind) => kind switch
        {
            NodeKind.Event => "随机事件", NodeKind.Camp => "营地", NodeKind.Shop => "商店", NodeKind.Town => "城镇",
            NodeKind.NormalBattle => "普通战", NodeKind.EliteBattle => "精英战", _ => "Boss 战"
        };

        internal static string EventName(AdventureEventKind kind) => kind switch
        {
            AdventureEventKind.Investor => "引资的创业者", AdventureEventKind.Ambush => "随机敌人",
            AdventureEventKind.LostTraveler => "呼朋引伴", AdventureEventKind.FoundCoins => "捡到钱了",
            AdventureEventKind.HatTrick => "帽子戏法", AdventureEventKind.Forge => "锻造台",
            AdventureEventKind.LockedChest => "上锁的宝箱", AdventureEventKind.AscensionTrial => "升阶试炼", _ => "炼药师的小白鼠"
        };

        internal static string ProfessionName(Profession profession) => profession switch
        { Profession.Warrior => "战士", Profession.Mage => "法师", Profession.Archer => "射手", _ => "后勤" };

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
                "锁孔叫海盐堵了个严实，箱子一晃，里面倒有东西在响。\n一名队员蹲下来端详了片刻。“得留个人慢慢弄。你们先赶路，我开了锁再追上来。”",
            AdventureEventKind.AscensionTrial =>
                "守门人把一包东西搁在台阶上。门缝里透出火光，里头有人拖着一柄重剑走过石地。\n“礼物在这儿。你们也可以往里走，看看自己还有什么本事没使出来。”",
            _ =>
                "炼药师把几只没贴签的瓶子一字排开，有一只的瓶塞还在轻轻响。\n“随便挑一瓶。钱就免了，改天记得告诉我尝起来怎么样。”"
        };
    }
}
