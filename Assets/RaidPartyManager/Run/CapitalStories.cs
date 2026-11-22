using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public enum CapitalReward { Gold, Gear, Book, Potion, Rest, Treat, Morale }

    public sealed class CapitalChoice
    {
        public readonly string Label;
        public readonly int Cost, Chance, Amount;
        public readonly CapitalReward Reward;

        public CapitalChoice(string label, int cost, int chance, CapitalReward reward, int amount)
        {
            Label = label; Cost = cost; Chance = chance; Reward = reward; Amount = amount;
        }

        public string Terms => (Cost > 0 ? $"支付 {Cost} 金币，" : "") + (Reward switch
        {
            CapitalReward.Gold => $"获得 {Amount} 金币",
            CapitalReward.Gear => Amount switch { 1 => "获得一件军用装备", 2 => "获得一件工坊装备", _ => "获得一件民用装备" },
            CapitalReward.Book => "获得一本随机普通技能书",
            CapitalReward.Potion => "获得一瓶药水",
            CapitalReward.Rest => $"全队在岗生还者恢复 {Amount}% 最大生命",
            CapitalReward.Treat => $"伤势最重的在岗队员恢复最多 {Amount} 点生命",
            _ => "在岗生还者的满意度提高 1 级，最高 5 级"
        }) + (Chance < 100 ? $"。成功率 {Chance}%，失败时一人损失最多 3 点生命，至少保留 1 点" : "");
    }

    public sealed class CapitalStory
    {
        public readonly string Title, Intro;
        public readonly int District;
        public readonly CapitalChoice[] Choices;
        public CapitalStory(string title, string intro, int district, params CapitalChoice[] choices)
        { Title = title; Intro = intro; District = district; Choices = choices; }
    }

    public static class CapitalStories
    {
        private static readonly CapitalStory[] Stories =
        {
            new("永不下班的闸机", "闸机还在等最后一位乘客。它肯收检修费，也会给持票员工补发车资。", 0,
                new CapitalChoice("补办检修票", 6, 100, CapitalReward.Gear, 2),
                new CapitalChoice("领取误点赔款", 0, 100, CapitalReward.Gold, 9)),
            new("投递于九十年前", "气邮管里卡着两件东西：寄给车站的零件包，和交给队医的急件。只能给一条管线加压。", 0,
                new CapitalChoice("接通货运管", 0, 100, CapitalReward.Gear, 3),
                new CapitalChoice("接通急救管", 0, 100, CapitalReward.Potion, 1)),
            new("迟到一生的考勤钟", "停表那天，全城都算迟到。把时间拨回去，工人名下的工资才会发出来。", 0,
                new CapitalChoice("为工人补签", 0, 100, CapitalReward.Morale, 1),
                new CapitalChoice("申领夜班津贴", 0, 100, CapitalReward.Gold, 14)),
            new("最后一份供水特许", "水塔只够开一次阀。送进消毒池能给伤员清创，送进铸币所则能洗出沉底的小钱。", 0,
                new CapitalChoice("开放消毒池", 0, 100, CapitalReward.Rest, 22),
                new CapitalChoice("冲洗铸币槽", 0, 100, CapitalReward.Gold, 12)),
            new("无人认领的行李", "秤盘亮起黄灯。保管员留下两种结案办法：付仓租开箱，或按废铜重量出售。", 0,
                new CapitalChoice("付仓租开箱", 13, 100, CapitalReward.Gear, 1),
                new CapitalChoice("整箱称重", 0, 100, CapitalReward.Gold, 15)),
            new("门诊叫到零号", "铜护士叫了一个不存在的号。付费可让全队接诊，免费床位只够伤得最重的一人。", 2,
                new CapitalChoice("买团体诊票", 9, 100, CapitalReward.Rest, 35),
                new CapitalChoice("使用免费床位", 0, 100, CapitalReward.Treat, 8)),
            new("道岔的少数意见", "一条支线通向废料仓，另一条写着“试车自负”。废料商付现钱，试车成功能拖回军用装备。", 0,
                new CapitalChoice("走废料线", 0, 100, CapitalReward.Gold, 11),
                new CapitalChoice("试跑军用线", 0, 60, CapitalReward.Gear, 1)),
            new("一盏灯的市政预算", "街灯还剩一匣能源。把灯点亮，值夜的队员会很高兴；也能卸下电芯换钱。", 1,
                new CapitalChoice("点亮街区", 0, 100, CapitalReward.Morale, 1),
                new CapitalChoice("回收备用电芯", 0, 100, CapitalReward.Gold, 16)),
            new("只有一张的选票", "印票机还在选市长。投给图书馆可以换一本技能书；投给公共诊所，伤员能免费就诊。", 2,
                new CapitalChoice("投给图书馆", 0, 100, CapitalReward.Book, 1),
                new CapitalChoice("投给公共诊所", 0, 100, CapitalReward.Rest, 25)),
            new("锅炉要求加班费", "锅炉把压力表指向红区。付煤钱可慢慢泄压取货；直接扳阀能省钱，失手会烫伤一名队员。", 1,
                new CapitalChoice("买煤缓慢泄压", 8, 100, CapitalReward.Gear, 2),
                new CapitalChoice("抢开急泄阀", 0, 65, CapitalReward.Gear, 2)),
            new("听众席上的灰尘", "机械乐团等最后一场掌声。留下来当观众可振奋队员，也可以接下乐团的拆台工作领酬。", 2,
                new CapitalChoice("听完安可曲", 0, 100, CapitalReward.Morale, 1),
                new CapitalChoice("拆台领工钱", 0, 100, CapitalReward.Gold, 18)),
            new("保质期一百年", "温室的抽屉标着两种用途。药用种荚能换一瓶药；打开培养床能为全队处理伤口。", 2,
                new CapitalChoice("交换种荚", 0, 100, CapitalReward.Potion, 1),
                new CapitalChoice("启动培养床", 4, 100, CapitalReward.Rest, 28)),
            new("借来的一双手", "维修台的双手还能工作。装上精密模具可加工一件装备，改装书写头则可抄录一本技能书。", 1,
                new CapitalChoice("购买精密模具", 11, 100, CapitalReward.Gear, 1),
                new CapitalChoice("装上书写头", 5, 100, CapitalReward.Book, 1)),
            new("黄金标准差半克", "银行的天平差半克才肯结账。按旧币面值可稳拿赔付，申请重新称重则可能一枚也拿不到，还会被夹伤手指。", 1,
                new CapitalChoice("按面值结算", 0, 100, CapitalReward.Gold, 15),
                new CapitalChoice("要求重新称重", 0, 55, CapitalReward.Gold, 32)),
            new("最后一次正午", "圆顶开了一条缝。归零仪器可读出星图手册；放弃校准，拆下镜片也值一笔钱。", 2,
                new CapitalChoice("支付校准费", 8, 100, CapitalReward.Book, 1),
                new CapitalChoice("出售备用镜片", 0, 100, CapitalReward.Gold, 19)),
        };
        public static bool Contains(AdventureEventKind? kind) => kind.HasValue &&
            kind.Value >= AdventureEventKind.FareGate && kind.Value <= AdventureEventKind.LastMeridian;
        public static CapitalStory For(AdventureEventKind kind) => Stories[(int)kind - (int)AdventureEventKind.FareGate];
        public static readonly IReadOnlyList<AdventureEventKind> Kinds =
            Enumerable.Range((int)AdventureEventKind.FareGate, Stories.Length).Select(i => (AdventureEventKind)i).ToArray();
    }

    public sealed partial class AdventureRun
    {
        private IEnumerable<EquipmentDefinition> CapitalPool(CapitalChoice choice) => shopCatalogue.Where(e => choice.Reward switch
        {
            CapitalReward.Book => e is SkillBookDefinition,
            CapitalReward.Potion => e.IsPotion,
            CapitalReward.Gear => e.Tier == choice.Amount && !e.IsPotion && e is not SkillBookDefinition && e.Effect != EquipmentEffect.RecruitmentNotice,
            _ => false
        });

        public bool CanTakeCapitalChoice(CapitalChoice choice) => Party.Gold >= choice.Cost &&
            (choice.Reward is not (CapitalReward.Gear or CapitalReward.Book or CapitalReward.Potion) || CapitalPool(choice).Any());

        public bool ChooseCapital(int index)
        {
            if (!CapitalStories.Contains(Current?.Event) || !At(Current.Event.Value)) return false;
            var story = CapitalStories.For(Current.Event.Value);
            if (index < 0 || index >= story.Choices.Length) return false;
            var choice = story.Choices[index];
            if (!CanTakeCapitalChoice(choice) || choice.Cost > 0 && !Party.TrySpendGold(choice.Cost, story.Title)) return false;
            var crew = Party.Members.Where(m => !m.IsAway && m.Health > 0).ToArray();
            string result;
            if (choice.Chance < 100 && random.Next(100) >= choice.Chance)
            {
                var casualty = crew.Length == 0 ? null : crew[random.Next(crew.Length)];
                int lost = casualty == null ? 0 : -Party.AdjustCapitalHealth(casualty.Id, -3);
                result = casualty == null ? "机器卡住了，什么也没拿到。" : $"机器卡住了，{casualty.Character.Name}损失 {lost} 点生命。";
            }
            else if (choice.Reward == CapitalReward.Gold)
            { Party.AddGold(choice.Amount); result = $"队伍收到 {choice.Amount} 金币。"; }
            else if (choice.Reward == CapitalReward.Rest)
                result = $"全队合计恢复了 {Party.Rest(choice.Amount / 100d)} 点生命。";
            else if (choice.Reward == CapitalReward.Treat)
            {
                var patient = crew.OrderBy(m => (double)m.Health / m.EffectiveStats.Health).FirstOrDefault();
                result = patient == null ? "没有需要接诊的队员。" : $"{patient.Character.Name}恢复了 {Party.AdjustCapitalHealth(patient.Id, choice.Amount)} 点生命。";
            }
            else if (choice.Reward == CapitalReward.Morale)
            {
                foreach (var member in crew) Party.GraveMemory(member.Id, $"我还记得遗都的「{story.Title}」，那天过得值。", true);
                result = "队员把这件事记进了档案，满意度提高了。";
            }
            else
            {
                var pool = CapitalPool(choice).ToArray();
                var gift = pool[random.Next(pool.Length)]; Party.AddEquipment(gift);
                result = $"拿到了{gift.Name}，已经放进背包。";
            }
            Finish($"{choice.Label}。{result}");
            return true;
        }
    }

    public sealed partial class PartyRun
    {
        internal int AdjustCapitalHealth(string id, int delta)
        {
            var member = FindMember(id);
            if (member == null || member.Health <= 0 || member.IsAway) return 0;
            int before = member.Health;
            member.Health = Math.Clamp(before + delta, 1, PersonalStats(member).Health);
            return member.Health - before;
        }
    }
}
