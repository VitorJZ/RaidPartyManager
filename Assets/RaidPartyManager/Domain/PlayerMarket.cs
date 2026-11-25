using System;
using System.Linq;
using System.Collections.Generic;

namespace RaidPartyManager.Domain
{
    public enum PlayerTrait
    {
        None, Warehouse, Scholar, Brawn, Tough, Warded,
        Sturdy, Opener, Vanguard, HelpingHand, Grit,
        Bandages, Rebate, IronGut, RecklessStart, BigMatch,
        LightGear, Distracted, OldWound, SlowHealing, AppearanceFee,
        Watchful, Successor, ShieldPiercer, Unshaken, RearGuard,
        Therapy, Contractor, FrostAffinity, AgileSwap, BackWeighted,
        Finisher, Compensation, CurseWard, Selfless, Warmup,
        LeakyShield, ExposedStart, Disobedient, ColdSensitive, Tuition, Bargaining
    }

    public static class PlayerMarket
    {
        private static readonly (string Name, string Effect, int Percent)[] traits =
        {
            ("", "", 0),
            ("随身库房", "额外拥有1个装备栏", 100),
            ("兼修", "额外拥有1个技能栏", 55),
            ("天生蛮力", "攻击力+1", 40),
            ("硬骨头", "防御力+1", 30),
            ("辟邪体质", "特防+1", 30),
            ("皮实", "生命值+2", 35),
            ("开门红", "每场战斗，首次普攻命中敌人时伤害+2。多目标普攻只加成首个命中的敌人。", 25),
            ("前排担当", "开战时，若自身位于1号位，获得3点护盾。", 25),
            ("搭把手", "第2回合开始时，为自身后方最近的存活队友恢复2点生命值。身后没有存活队友时跳过。", 20),
            ("硬撑", "每场战斗，自身首次在受伤后生命值不高于50%且仍存活时，获得1层坚毅。", 45),
            ("随身绷带", "战斗胜利结算时，若自身存活，恢复2点生命值。", 25),
            ("暗拿回扣", "每到一个城镇，首次在商店购买装备时返还 2 金币。", 15),
            ("铁胃", "每场战斗首次被施加中毒时，抵消其中1层。", 12),
            ("豁命开场", "开战时失去2点生命值，并获得1层盛气。", -5),
            ("硬仗奖金", "每场精英战或Boss战首次施放技能后，返还1点能量。", 30),
            ("轻装癖", "穿戴恰好1件装备时，防御力与特防各+1。穿戴3件或更多装备时，两者各−1。", 0),
            ("开场走神", "开战时，自身当前能量−1，最低为0点。", -18),
            ("旧伤复发", "每场战斗，首次受到敌人物理伤害并扣除生命时，若自身仍存活，额外失去1点生命值（最低降至1点）。", -10),
            ("难愈体质", "每场战斗，首次在未满血时受到治疗，治疗量减少2点（最低为0点）。", -12),
            ("到场费", "普通战获胜且自身出战存活时，扣除本场金币奖励中的1金币。", -20),
            ("盯梢", "每回合首次普攻命中敌方最后一名存活单位时，对其造成的伤害+1。", 30),
            ("接班人", "每场战斗首次有前方队友倒下时，自身获得1层盛气和2点护盾。", 15),
            ("拆盾手", "无视敌方的护盾。", 65),
            ("见惯阵仗", "每场战斗首次被施加负面状态时，抵消该状态。", 75),
            ("压阵护卫", "开战时，若自身位于4号位，为前方最近的存活队友提供2点护盾。", 10),
            ("公费理疗", "营地休息时，自身额外恢复2点生命值。", 8),
            ("委托熟客", "交付城镇委托时，队伍每有1名持有此特性的存活成员，额外获得2金币。", 10),
            ("冰霜亲和", "对冰冻的敌人额外造成50%的伤害。", 45),
            ("临场调位", "若本场首发站位与上一次出战不同，开战时获得1层迅捷。", 12),
            ("重心靠后", "位于1、2号位时，防御力−1（最低为0）。位于3、4号位时，特防+1。", 0),
            ("挑软柿子", "普攻对半血或以下的敌人伤害+1，对其他敌人伤害−1（最低为1）。", 0),
            ("工伤赔偿", "每场首次倒下时，其他存活队友各获得2点护盾。若队伍获胜，从本场金币奖励中支付最多2金币赔偿。", 0),
            ("硬扛诅咒", "每场首次被施加诅咒时，若自身至少有1点能量，则消耗1点能量并抵消该次诅咒。", 0),
            ("舍己救人", "治疗技能对其他队友的回复量+1，对自身的回复量−1。", 10),
            ("还在热身", "第1回合无法施放技能。第2回合开始时，若自身存活，获得1层迅捷。", -5),
            ("护盾漏风", "每场战斗首次获得护盾时，护盾值少获得2点（最低为0）。", -8),
            ("开场露怯", "开战时，自身获得1层破绽。", -15),
            ("不服调度", "若本场首发站位与上一次出战不同，开战时获得1层萎靡。", -5),
            ("怕冷", "每场战斗首次被施加寒冷时，额外获得1层寒冷。", -8),
            ("技能外包", "学习技能时，队伍额外支付2金币培训费。金币不足时，无法学习该技能。", -10),
            ("讨价还价", "队伍每有1名成员持有此特性，普通装备和技能书的回收率就提高20个百分点。基础回收率为50%，可超过100%。", 20),
        };

        private static readonly int[] prices = { 0, 19, 40, 37, 23, 34, 42, 32, 90, 28, 20, 35, 36, 19, 37, 43, 17, 37, 27, 27, 34, 45, 18, 41, 26, 17, 34, 39, 16, 24, 20, 19, 12, 16, 48, 15, 40, 25 };
        private static readonly int[] skillRates = { 0, 15, 23, 20, 35, 15, 30, 13, 15, 18, 25, 18, 23, 10, 28, 8, 28, 10, 30, 20, 13, 20, 23, 30, 38, 40, 38, 43, 43, 50, 55, 30, 30, 35, 35, 0, 0, 0, 30 };
        public static Stats GearAndPosition(PlayerTrait trait, int gear, int? position, Stats stats)
        {
            if (trait is not (PlayerTrait.LightGear or PlayerTrait.BackWeighted)) return stats;
            int gearBonus = trait != PlayerTrait.LightGear ? 0 : gear == 1 ? 1 : gear >= 3 ? -1 : 0;
            int front = trait == PlayerTrait.BackWeighted && position is 1 or 2 ? -1 : 0;
            int rear = trait == PlayerTrait.BackWeighted && position is 3 or 4 ? 1 : 0;
            return new Stats(stats.Attack, Math.Max(0, stats.Defense + gearBonus + front),
                Math.Max(0, stats.SpecialDefense + gearBonus + rear), stats.Health);
        }
        public static int TraitCount => traits.Length - 1;
        public static string Describe(PlayerTrait trait)
        {
            var entry = traits[(int)trait];
            return trait == PlayerTrait.None ? "特性：无" : $"特性：{entry.Name}　{entry.Effect}";
        }
        public static int BaseValue(CharacterDefinition character) =>
            character.RosterNumber > 0 && character.RosterNumber < prices.Length ? prices[character.RosterNumber] : 18;
        public static double SkillPremium(SkillBookDefinition skill)
        {
            if (skill?.BossExclusive == true) return 100;
            if (skill == null || !skill.Id.StartsWith("skill-book-", StringComparison.Ordinal) ||
                !int.TryParse(skill.Id.Substring(11), out int id) || id < 1 || id >= skillRates.Length) return 0;
            return skillRates[id] * (skill.Cost <= Rules.DefaultEnergy ? 1d : .25d);
        }

        public static int Value(CharacterDefinition character, PlayerTrait trait, IEnumerable<SkillBookDefinition> learned = null,
            int trainingSessions = 0)
        {
            var skills = (learned ?? Enumerable.Empty<SkillBookDefinition>()).Where(skill => skill != null).ToArray();
            var rates = skills.Where(skill => !skill.BossExclusive).Select(SkillPremium).Where(rate => rate > 0).ToList();
            double total = rates.Count == 0 ? 0 : .6 * rates.Sum() + .4 * rates.Max();
            int ordinary = (int)Math.Round(BaseValue(character) * (1 + traits[(int)trait].Percent / 100d) * (1 + total / 100), MidpointRounding.AwayFromZero);
            int trainingPremium = Math.Max(trainingSessions, (int)Math.Round(ordinary * trainingSessions / 10d, MidpointRounding.AwayFromZero));
            return (ordinary + trainingPremium) * (1 + skills.Count(skill => skill.BossExclusive));
        }
    }
}
