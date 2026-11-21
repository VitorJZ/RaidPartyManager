using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Presentation
{
    internal static class PrototypeText
    {
        public static string HealthText(int current, int maximum) => current == 0 ? $"倒下 · 0/{maximum}" : $"{current}/{maximum}";
        public static string StatText(int effective, int baseline)
        {
            int change = effective - baseline;
            return change == 0 ? $"{effective}" : $"{effective} ({change:+0;-0})";
        }

        public static string BonusText(Stats stats)
        {
            var parts = new List<string>();
            if (stats.Attack > 0) parts.Add($"攻击 +{stats.Attack}");
            if (stats.Defense > 0) parts.Add($"防御 +{stats.Defense}");
            if (stats.SpecialDefense > 0) parts.Add($"特防 +{stats.SpecialDefense}");
            if (stats.Health > 0) parts.Add($"生命上限 +{stats.Health}");
            return string.Join(" / ", parts);
        }


    }
}
