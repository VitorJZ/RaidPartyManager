using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    [Serializable]
    public struct CampTraining
    {
        public const int Limit = 2;
        private const int HealthPerAward = 3;

        public int Attack, Defense, SpecialDefense, Health;

        private int Awards => Attack + Defense + SpecialDefense + Health;
        public int Sessions => Awards / 2;
        public Stats Bonus => new Stats(Attack, Defense, SpecialDefense, Health * HealthPerAward);

        internal bool IsValid => Attack is >= 0 and <= Limit && Defense is >= 0 and <= Limit &&
            SpecialDefense is >= 0 and <= Limit && Health is >= 0 and <= Limit &&
            Awards <= 2 * Limit && Awards % 2 == 0 &&
            Attack <= Sessions && Defense <= Sessions && SpecialDefense <= Sessions && Health <= Sessions;

        internal static CampTraining Roll(Random random)
        {
            int first = random.Next(4);
            int second = random.Next(3);
            if (second >= first) second++;
            return new CampTraining
            {
                Attack = first == 0 || second == 0 ? 1 : 0,
                Defense = first == 1 || second == 1 ? 1 : 0,
                SpecialDefense = first == 2 || second == 2 ? 1 : 0,
                Health = first == 3 || second == 3 ? 1 : 0
            };
        }

        internal CampTraining With(CampTraining gains) => new CampTraining
        {
            Attack = Attack + gains.Attack,
            Defense = Defense + gains.Defense,
            SpecialDefense = SpecialDefense + gains.SpecialDefense,
            Health = Health + gains.Health
        };

        public string Describe()
        {
            var bonuses = new List<string>(4);
            if (Attack > 0) bonuses.Add($"攻击 +{Attack}");
            if (Defense > 0) bonuses.Add($"物防 +{Defense}");
            if (SpecialDefense > 0) bonuses.Add($"特防 +{SpecialDefense}");
            if (Health > 0) bonuses.Add($"最大生命 +{Bonus.Health}");
            return string.Join("、", bonuses);
        }
    }
}
