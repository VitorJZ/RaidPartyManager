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

        public static EmployeeAttributes Starting => new EmployeeAttributes
        {
            Loyalty = 3, Obedience = 3, PayPreference = 3, PlayingTime = 3, Satisfaction = 3, Hardiness = 3
        };
    }
}
