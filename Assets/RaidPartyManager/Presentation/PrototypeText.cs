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

        public static string PersonalityText(Personality personality) => personality switch
        {
            Personality.Aggressive => "激进", Personality.Cautious => "慎重", Personality.Reckless => "鲁莽",
            Personality.Gentle => "温柔", Personality.Cooperative => "配合", Personality.Calm => "冷静",
            Personality.Timid => "胆小", Personality.Vengeful => "记仇", Personality.Careful => "谨慎",
            Personality.Stubborn => "固执", Personality.Impatient => "急躁", Personality.Focused => "专注",
            Personality.Patient => "隐忍", Personality.Greedy => "贪心",
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
            Personality.Timid => "挨了狠招，或是连半条命都撑不住，这位便寻机会往后躲。",
            Personality.Vengeful => "方才谁打的那一下，他可记得一清二楚。",
            Personality.Careful => "刀该落在哪儿？自然是命最薄的那位。",
            Personality.Stubborn => "脚下这块地是他认准的，打起来谁也劝不动。",
            Personality.Impatient => "只要手上那招能使，哪还肯再等一回合。",
            Personality.Focused => "认准一个就追到底，等对方倒下才另找。",
            Personality.Patient => "能用平常招式解决的事，何必亮出压箱底的本领。",
            Personality.Greedy => "出手专挑最值的那一击，多费半分力气都心疼。",
            _ => string.Empty
        };
    }
}
