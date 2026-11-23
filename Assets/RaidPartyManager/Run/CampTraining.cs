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

        public int Attack, Defense, SpecialDefense, Health, Energy;
        // Older checkpoints stored only the two awards earned at each visit.
        public int CompletedSessions;

        private int Awards => Attack + Defense + SpecialDefense + Health + Energy;
        public int Sessions => CompletedSessions == 0 ? Awards / 2 : CompletedSessions;
        public Stats Bonus => new Stats(Attack, Defense, SpecialDefense, Health * HealthPerAward);

        internal bool IsValid => Attack is >= 0 and <= Limit && Defense is >= 0 and <= Limit &&
            SpecialDefense is >= 0 and <= Limit && Health is >= 0 and <= Limit &&
            Energy >= 0 && Energy <= CompletedSessions && CompletedSessions is >= 0 and <= Limit &&
            (CompletedSessions > 0 || Awards % 2 == 0) && Sessions <= Limit &&
            Sessions <= Awards && Awards <= 2 * Sessions &&
            Attack <= Sessions && Defense <= Sessions && SpecialDefense <= Sessions && Health <= Sessions;

        internal static CampTraining Roll(Random random)
        {
            int project = random.Next(5);
            return new CampTraining
            {
                Attack = project == 0 ? 1 : 0,
                Defense = project == 1 ? 1 : 0,
                SpecialDefense = project == 2 ? 1 : 0,
                Health = project == 3 ? 1 : 0,
                Energy = project == 4 ? 1 : 0,
                CompletedSessions = 1
            };
        }

        internal CampTraining With(CampTraining gains) => new CampTraining
        {
            Attack = Attack + gains.Attack,
            Defense = Defense + gains.Defense,
            SpecialDefense = SpecialDefense + gains.SpecialDefense,
            Energy = Energy + gains.Energy,
            CompletedSessions = Sessions + gains.Sessions,
            Health = Health + gains.Health
        };

        public string Describe()
        {
            var bonuses = new List<string>(4);
            if (Attack > 0) bonuses.Add($"攻击 +{Attack}");
            if (Defense > 0) bonuses.Add($"物防 +{Defense}");
            if (SpecialDefense > 0) bonuses.Add($"特防 +{SpecialDefense}");
            if (Health > 0) bonuses.Add($"最大生命 +{Bonus.Health}");
            if (Energy > 0) bonuses.Add($"能量上限 +{Energy}");
            return string.Join("、", bonuses);
        }
    }
}
