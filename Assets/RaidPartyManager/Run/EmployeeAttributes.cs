using System;

namespace RaidPartyManager.Run
{
    [Serializable]
    public struct EmployeeAttributes
    {
        public int Loyalty;
        public int Obedience;
        public int PayPreference;
        public int PlayingTime;
        public int Satisfaction;
        public int Hardiness;

        internal bool IsValid => IsRating(Loyalty) && IsRating(Obedience) &&
            IsRating(PayPreference) && IsRating(PlayingTime) && IsRating(Satisfaction) && IsRating(Hardiness);

        private static bool IsRating(int rating) => rating is >= 1 and <= 5;

        internal bool TryRaise(EmployeeRating rating, out int level)
        {
            level = rating switch
            {
                EmployeeRating.Loyalty => Loyalty,
                EmployeeRating.Obedience => Obedience,
                EmployeeRating.PayPreference => PayPreference,
                EmployeeRating.PlayingTime => PlayingTime,
                EmployeeRating.Satisfaction => Satisfaction,
                EmployeeRating.Hardiness => Hardiness,
                _ => 0
            };
            if (level is < 1 or >= 5) return false;
            level++;
            switch (rating)
            {
                case EmployeeRating.Loyalty: Loyalty = level; break;
                case EmployeeRating.Obedience: Obedience = level; break;
                case EmployeeRating.PayPreference: PayPreference = level; break;
                case EmployeeRating.PlayingTime: PlayingTime = level; break;
                case EmployeeRating.Satisfaction: Satisfaction = level; break;
                case EmployeeRating.Hardiness: Hardiness = level; break;
            }
            return true;
        }

        internal static EmployeeAttributes Roll(Random random, int minimum = 1) => new EmployeeAttributes
        {
            Loyalty = random.Next(minimum, 6),
            Obedience = random.Next(minimum, 6),
            PayPreference = random.Next(minimum, 6),
            PlayingTime = random.Next(minimum, 6),
            Satisfaction = 3,
            Hardiness = random.Next(minimum, 6)
        };

        internal static EmployeeAttributes ForLegacySave(string characterId)
        {
            // Match the original character-based profile when no ratings were saved.
            uint seed = 2166136261;
            foreach (char letter in characterId)
                seed = unchecked((seed ^ letter) * 16777619);
            return Roll(new Random(unchecked((int)seed)));
        }
    }
}
