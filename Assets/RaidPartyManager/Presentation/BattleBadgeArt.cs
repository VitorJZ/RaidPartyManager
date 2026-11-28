using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal static class BattleBadgeArt
    {
        private static readonly Texture2D Atlas = Resources.Load<Texture2D>("BattleBadgeIcons");

        public static Image Icon(BattleCountdownKind? countdown)
        {
            if (Atlas == null) return null;
            int cell = countdown switch
            {
                null => 0,
                BattleCountdownKind.CalamityChant => 1,
                BattleCountdownKind.ScavengerEscape => 2,
                BattleCountdownKind.AdmiralBroadside => 3,
                BattleCountdownKind.EnemyCharge => 4,
                BattleCountdownKind.StoneGuard => 5,
                BattleCountdownKind.GoldenBell => 6,
                _ => 7
            };
            return new Image
            {
                name = countdown.HasValue ? "battle-countdown-icon" : "battle-shield-icon",
                image = Atlas,
                sourceRect = AnimationCels.RectFor(Atlas, cell, columns: 4, rows: 2),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
                style = { flexGrow = 1 }
            };
        }

        public static (string Name, string Description, string Unit, Color Color) CountdownDetails(BattleCountdownKind kind) => kind switch
        {
            BattleCountdownKind.CalamityChant => ("灾厄吟唱", "每 3 回合结束时，若仍然存活，对所有敌方单位发动 3 次雷击，每次造成等同于当前攻击力的雷电伤害。",
                "回合", new Color32(211, 169, 255, 255)),
            BattleCountdownKind.ScavengerEscape => ("携赃逃跑", "拾荒者将在倒计时结束时带走偷来的装备。提前击败它可夺回装备。",
                "回合", new Color32(255, 195, 109, 255)),
            BattleCountdownKind.AdmiralBroadside => ("封港炮击", "下一次行动时炮击对方最后两名存活队员。眩晕会推迟行动。",
                "次行动", new Color32(255, 135, 117, 255)),
            BattleCountdownKind.EnemyCharge => ("墓园蓄力", "下次可行动时释放重击。眩晕会推迟释放，目标按释放时的站位重新选择。",
                "次行动", new Color32(155, 221, 207, 255)),
            BattleCountdownKind.StoneGuard => ("坚如磐石", "下一次受到大于 7 点的伤害时完全抵挡，挡下后解除。",
                "次", new Color32(196, 210, 222, 255)),
            _ => ("金钟罩", "本回合敌方攻击造成的伤害全部挡下，回合结束后解除。",
                "回合", new Color32(247, 218, 139, 255))
        };
    }
}
