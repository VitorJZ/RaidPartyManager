using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public static class BossSpoils
    {
        public static (EquipmentDefinition[] Choices, EquipmentDefinition Equipment) Roll(string bossId,
            IEnumerable<EquipmentDefinition> catalogue, Random random)
        {
            var items = catalogue.GroupBy(item => item.Id).Select(group => group.First()).ToArray();
            var choices = items.Where(item => item.BossExclusive && item.RewardBossId == bossId)
                .OrderBy(item => item is SkillBookDefinition).ToArray();
            if (choices.Length != 2 || choices.Count(item => item is SkillBookDefinition) != 1)
                throw new InvalidOperationException($"Missing equipment or skill book reward for {bossId}.");

            int roll = random.Next(100);
            var pool = items.Where(item => !item.BossExclusive && !item.IsPotion && item.Effect != EquipmentEffect.RecruitmentNotice && item is not SkillBookDefinition &&
                (roll < 10 ? item.Tier == 0 : item.CanDropInBattle && (roll < 40 ? item.Tier == 1 : item.Tier >= 2))).ToArray();
            if (pool.Length == 0) throw new InvalidOperationException("Missing boss bonus equipment tier.");
            return (choices, pool[random.Next(pool.Length)]);
        }
    }
}
