using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleCountdownIcon : VisualElement
    {
        public static (string Name, string Description, string Unit, Color Color) Details(BattleCountdownKind kind) => kind switch
        {
            BattleCountdownKind.CalamityChant => ("灾厄吟唱", "每 3 回合结束时，若仍然存活，对所有敌方单位造成雷电伤害。",
                "回合", new Color32(211, 169, 255, 255)),
            BattleCountdownKind.ScavengerEscape => ("携赃逃跑", "拾荒者将在倒计时结束时带走偷来的装备。提前击败它可夺回装备。",
                "回合", new Color32(255, 195, 109, 255)),
            BattleCountdownKind.AdmiralBroadside => ("封港炮击", "下一次行动时炮击对方最后两名存活队员。眩晕会推迟行动。",
                "次行动", new Color32(255, 135, 117, 255)),
            _ => ("金钟罩", "本回合抵挡敌方攻击，回合结束后解除。",
                "回合", new Color32(247, 218, 139, 255))
        };

        public BattleCountdownIcon(BattleCountdownKind kind, Color color)
        {
            name = "battle-countdown-icon";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            generateVisualContent += context =>
            {
                var bounds = contentRect;
                float scale = Mathf.Min(bounds.width, bounds.height) * 0.39f;
                if (scale <= 0) return;
                var pen = context.painter2D;
                pen.strokeColor = color;
                pen.lineWidth = Mathf.Max(1, 0.11f * scale);
                pen.BeginPath();
                pen.Arc(bounds.center, scale, 0, 360, ArcDirection.Clockwise);
                pen.ClosePath();
                pen.Stroke();

                switch (kind)
                {
                    case BattleCountdownKind.CalamityChant:
                        Path(true, new(-.12f, -.73f), new(-.55f, .05f), new(-.07f, .05f),
                            new(-.26f, .73f), new(.56f, -.20f), new(.12f, -.20f), new(.31f, -.73f));
                        break;
                    case BattleCountdownKind.ScavengerEscape:
                        Path(true, new(-.49f, -.56f), new(.49f, -.56f), new(.12f, 0),
                            new(.49f, .56f), new(-.49f, .56f), new(-.12f, 0));
                        Path(false, new(-.27f, .32f), new(.27f, .32f));
                        break;
                    case BattleCountdownKind.AdmiralBroadside:
                        Path(true, new(-.59f, .11f), new(.31f, -.38f), new(.48f, -.11f),
                            new(-.42f, .37f));
                        Path(false, new(-.47f, .57f), new(.1f, .57f), new(-.13f, .15f));
                        Path(false, new(.52f, -.38f), new(.70f, -.50f));
                        break;
                    default:
                        Path(true, new(-.58f, .45f), new(-.40f, .12f), new(-.35f, -.31f),
                            new(-.18f, -.56f), new(.18f, -.56f), new(.35f, -.31f), new(.40f, .12f), new(.58f, .45f));
                        Path(false, new(-.10f, .62f), new(.10f, .62f));
                        break;
                }

                void Path(bool closed, params Vector2[] points)
                {
                    pen.BeginPath();
                    pen.MoveTo(bounds.center + points[0] * scale);
                    for (int index = 1; index < points.Length; index++)
                        pen.LineTo(bounds.center + points[index] * scale);
                    if (closed)
                        pen.ClosePath();
                    pen.Stroke();
                }
            };
        }
    }
}
