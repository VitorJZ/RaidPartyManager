using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    // A value snapshot of one participant, also stored with unclaimed adventure rewards.
    [Serializable]
    public struct BattleRating
    {
        public string UnitId, CharacterId, Name;
        public Side Side;
        public int Position, Kills, Saves, Interceptions, SkillBlocks;
        public bool Fallen, Retreated;
        public long HealthDamage, ShieldDamage, PoisonDamage, Healing, DamageTaken, ShieldAbsorbed;
        public long ResistancePrevented;
        public long AllyShielding, DebuffLayers, BuffLayers, CleansedLayers;
        public int ScoreTenths;

        public long Damage => HealthDamage + ShieldDamage;
        public long RatedDamageTaken => DamageTaken + ResistancePrevented;
        public decimal DamagePoints => .20m * HealthDamage + .10m * ShieldDamage;
        public decimal HealingPoints => .25m * Healing;
        public decimal DefensePoints => .08m * RatedDamageTaken + .16m * ShieldAbsorbed + .20m * AllyShielding +
            .40m * Interceptions + .40m * SkillBlocks;
        public decimal StatusPoints => .40m * DebuffLayers + .20m * BuffLayers + .20m * CleansedLayers;
        public decimal KillAndSavePoints => .60m * Kills + .60m * Saves;
        public decimal EarnedPoints => Math.Min(10m, DamagePoints + HealingPoints + DefensePoints + StatusPoints + KillAndSavePoints);

        public static BattleRating[] ScoreBattle(IEnumerable<BattleRating> participants)
        {
            var rows = participants.ToArray();
            for (int i = 0; i < rows.Length; i++)
            {
                decimal points = rows[i].EarnedPoints;
                rows[i].ScoreTenths = points == 10m ? 100 :
                    Math.Min(99, (int)decimal.Round(10m * points, 0, MidpointRounding.AwayFromZero));
            }
            return rows;
        }

        public static int? AverageTenths(IEnumerable<int?> scores)
        {
            decimal? mean = scores.Select(score => (decimal?)score).Average();
            return mean.HasValue ? (int)decimal.Round(mean.Value, 0, MidpointRounding.AwayFromZero) : null;
        }
    }
}
