using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Presentation
{
    internal static class PrototypeText
    {
        public static string HealthText(int current, int maximum) => current == 0 ? $"倒下 · 0/{maximum}" : $"{current}/{maximum}";
        public static string StatText(int effective, int baseline) => effective == baseline ? $"{effective}" : $"{effective} (+{effective - baseline})";

        public static string BonusText(Stats stats)
        {
            var parts = new List<string>();
            if (stats.Attack > 0) parts.Add($"攻击 +{stats.Attack}");
            if (stats.Defense > 0) parts.Add($"防御 +{stats.Defense}");
            if (stats.SpecialDefense > 0) parts.Add($"特防 +{stats.SpecialDefense}");
            if (stats.Health > 0) parts.Add($"生命上限 +{stats.Health}");
            return string.Join(" / ", parts);
        }

        public static string PersonalityText(Personality personality) => personality switch
        {
            Personality.Aggressive => "激进", Personality.Cautious => "慎重", Personality.Reckless => "鲁莽",
            Personality.Gentle => "温柔", Personality.Cooperative => "配合", Personality.Calm => "冷静",
            _ => personality.ToString()
        };

        public static string PersonalityDescription(Personality personality) => personality switch
        {
            Personality.Aggressive => "哪一招打得最疼，便使哪一招。",
            Personality.Cautious => "留得青山在，能护住自己就先护住自己。",
            Personality.Reckless => "护身的事一会儿再说，先攻上去！",
            Personality.Gentle => "刀兵且慢，先给最撑不住的同伴疗伤。",
            Personality.Cooperative => "同伴的刀箭往哪儿去，便跟上去合攻一处。",
            Personality.Calm => "满场刀光也不急，先找血条最短的敌人收尾。",
            _ => string.Empty
        };
    }
}
