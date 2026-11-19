using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class DockEnemyEffectPlayback
    {
        private static readonly Color Amber = new Color32(255, 173, 79, 255);
        private static readonly Color Rust = new Color32(222, 111, 66, 255);
        private static readonly Color Gold = new Color32(255, 224, 143, 255);
        private static readonly Color Ice = new Color32(136, 230, 255, 255);
        private static readonly Color DeepBlue = new Color32(80, 120, 211, 255);
        private static readonly Color Lure = new Color32(165, 251, 176, 255);
        private static readonly Color Smoke = new Color32(99, 119, 135, 255);
        private readonly BattleVisualCue cue;
        private readonly VisualElement canvas;
        private readonly double startsAt;
        private readonly double endsAt;
        private Vector2 source;
        private Vector2 target;
        private float size;
        private float phase;
        private float glow;

        public string ActorId { get; }
        public string TargetId { get; }

        public static bool Supports(BattleVisualCue visual) =>
            visual is >= BattleVisualCue.DockWarCry and <= BattleVisualCue.RaySurge;

        public DockEnemyEffectPlayback(BattleEvent entry, double now, float scale)
        {
            cue = entry.VisualCue;
            ActorId = entry.ActorId;
            TargetId = entry.TargetId;
            startsAt = now;
            float lifetime = cue switch
            {
                BattleVisualCue.RaySurge => 1.02f,
                BattleVisualCue.AnchorShatter or BattleVisualCue.AdmiralBroadside or BattleVisualCue.RayMist => 0.76f,
                _ => 0.56f
            };
            endsAt = now + lifetime * scale;
            canvas = new VisualElement { name = "dock-enemy-effect", pickingMode = PickingMode.Ignore };
            canvas.AddToClassList("dock-fx-" + cue);
            PrototypeStyle.Fill(canvas);
            canvas.generateVisualContent += Paint;
        }

        public bool ActiveAt(double now) => now < endsAt;
        public void Clear() => canvas.RemoveFromHierarchy();

        public void Draw(VisualElement layer, BattleUnitCard attacker, BattleUnitCard defender, double now)
        {
            if (layer == null || defender == null)
                return;

            var body = layer.WorldToLocal(defender.BodyBounds);
            if (body.width <= 0 || body.height <= 0)
                return;
            var caster = attacker != null ? layer.WorldToLocal(attacker.BodyBounds) : body;
            source = caster.center;
            target = body.center;
            size = Mathf.Clamp(Mathf.Min(body.width, body.height) * 0.48f, 26f, 82f);
            phase = Mathf.Clamp01((float)((now - startsAt) / (endsAt - startsAt)));
            glow = Mathf.Clamp01(phase * 9f) * (1f - Mathf.SmoothStep(0.30f, 1f, phase));
            if (canvas.parent != layer)
                layer.Add(canvas);
            canvas.MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            if (glow <= 0)
                return;

            var pen = context.painter2D;
            switch (cue)
            {
                case BattleVisualCue.AnchorWard:
                case BattleVisualCue.SaltShell:
                    Ward(pen, cue == BattleVisualCue.AnchorWard ? Amber : Ice);
                    break;
                case BattleVisualCue.AnchorExpose:
                    Tether(pen, Rust, 9);
                    Fracture(pen, target, Rust);
                    Outline(pen, target, size * (0.42f + 0.58f * phase), Rust, 0.7f);
                    break;
                case BattleVisualCue.AnchorShatter:
                    Burst(pen, target, Rust, 10, 0.9f);
                    Fracture(pen, target, Gold);
                    break;
                case BattleVisualCue.AnglerCurse:
                    Tether(pen, Lure, 5);
                    Wisp(pen, target, Lure);
                    break;
                case BattleVisualCue.WardenBrace:
                    Tether(pen, Gold, 12);
                    Ward(pen, Gold);
                    break;
                case BattleVisualCue.AdmiralWhistle:
                    for (int wave = 0; wave < 3; wave++)
                        Outline(pen, source + Vector2.up * size * 0.42f,
                            size * (0.26f + phase * (0.72f + wave * 0.24f)), Gold, 0.9f - wave * 0.23f);
                    break;
                case BattleVisualCue.AdmiralWarning:
                    Aim(pen);
                    break;
                case BattleVisualCue.AdmiralBroadside:
                    Broadside(pen);
                    break;
                case BattleVisualCue.RayTail:
                    Tether(pen, Ice, 12);
                    Burst(pen, target, Ice, 5, 0.68f);
                    break;
                case BattleVisualCue.RayMist:
                case BattleVisualCue.RaySurge:
                    Frost(pen, cue == BattleVisualCue.RaySurge);
                    break;
                case BattleVisualCue.EelFrost:
                    Burst(pen, target, Ice, 5, 0.63f);
                    break;
                case BattleVisualCue.MothAsh:
                    Wisp(pen, target, Amber);
                    break;
                case BattleVisualCue.CaulkerMend:
                    Ward(pen, Lure);
                    break;
                case BattleVisualCue.ScavengerSteal:
                    Diamond(pen, Vector2.Lerp(target, source, phase) + Vector2.up * Mathf.Sin(phase * Mathf.PI) * size,
                        size * 0.17f, Gold);
                    break;
                case BattleVisualCue.TideMark:
                    Fracture(pen, target, Ice);
                    break;
                case BattleVisualCue.DockWarCry:
                    Outline(pen, source, size * (0.47f + phase), Amber, 0.9f);
                    Burst(pen, source, Amber, 5, 0.54f);
                    break;
            }
        }

        private void Tether(Painter2D pen, Color color, int count)
        {
            float travel = Mathf.SmoothStep(0.25f, 1f, Mathf.Clamp01(phase * 2.5f));
            for (int link = 1; link <= count; link++)
            {
                float step = (float)link / count * travel;
                var point = Vector2.Lerp(source, target, step) - Vector2.up * Mathf.Sin(step * Mathf.PI) * size * 0.74f;
                Outline(pen, point, size * 0.10f, color, 0.68f);
            }
        }

        private void Ward(Painter2D pen, Color color)
        {
            float width = size * (0.47f + phase * 0.22f);
            Color stroke = Colored(color, glow * 0.82f);
            pen.BeginPath();
            pen.MoveTo(target + new Vector2(0, -width));
            pen.LineTo(target + new Vector2(width, -width * 0.55f));
            pen.LineTo(target + new Vector2(width * 0.60f, width * 0.56f));
            pen.LineTo(target + new Vector2(0, width * 0.90f));
            pen.LineTo(target + new Vector2(-width * 0.60f, width * 0.56f));
            pen.LineTo(target + new Vector2(-width, -width * 0.55f));
            pen.ClosePath();
            pen.fillColor = Colored(color, glow * 0.12f);
            pen.Fill();
            pen.strokeColor = stroke;
            pen.lineWidth = 2.5f;
            pen.Stroke();
            Outline(pen, target, width * 0.35f, color, 0.7f);
        }

        private void Fracture(Painter2D pen, Vector2 at, Color color)
        {
            for (int crack = 0; crack < 5; crack++)
            {
                var direction = Direction(crack * 1.26f + 0.31f);
                var elbow = new Vector2(-direction.y, direction.x);
                pen.BeginPath();
                pen.MoveTo(at);
                pen.LineTo(at + direction * size * 0.28f + elbow * size * 0.09f);
                pen.LineTo(at + direction * size * (0.58f + phase * 0.19f));
                pen.strokeColor = Colored(color, glow);
                pen.lineWidth = 2.2f;
                pen.Stroke();
            }
        }

        private void Aim(Painter2D pen)
        {
            float radius = size * (0.61f - 0.22f * phase);
            Outline(pen, target, radius, Rust, 1f);
            for (int mark = 0; mark < 4; mark++)
            {
                Vector2 axis = Direction(mark * Mathf.PI * 0.5f);
                pen.BeginPath();
                pen.MoveTo(target + axis * radius * 0.75f);
                pen.LineTo(target + axis * radius * 1.28f);
                pen.strokeColor = Colored(Gold, glow * 0.94f);
                pen.lineWidth = 2.5f;
                pen.Stroke();
            }
        }

        private void Broadside(Painter2D pen)
        {
            float spread = size * (0.29f + 0.47f * phase);
            Outline(pen, target + Vector2.down * size * 0.46f, spread, Amber, 0.94f);
            Burst(pen, target, Amber, 8, 0.92f);
            for (int cloud = 0; cloud < 4; cloud++)
            {
                var at = target + new Vector2((cloud - 1.5f) * spread * 0.43f, -spread * (0.15f + phase));
                Disc(pen, at, spread * (0.36f + cloud * 0.08f), Smoke, glow * 0.30f);
            }
        }

        private void Frost(Painter2D pen, bool surge)
        {
            float spread = size * (surge ? 1.24f : 0.86f);
            for (int cloud = 0; cloud < 5; cloud++)
            {
                var at = target + new Vector2((cloud - 2f) * spread * (0.35f + phase * 0.14f),
                    spread * (0.34f - 0.23f * phase) + Mathf.Sin(cloud * 2f) * spread * 0.21f);
                Disc(pen, at, spread * (0.27f + 0.19f * phase), cloud % 2 == 0 ? Ice : DeepBlue, glow * 0.19f);
            }
            Outline(pen, target, spread * (0.40f + phase * 0.42f), Ice, 0.73f);
            Burst(pen, target, Ice, surge ? 11 : 6, surge ? 1.0f : 0.76f);
        }

        private void Wisp(Painter2D pen, Vector2 at, Color color)
        {
            for (int light = 0; light < 5; light++)
            {
                var orbit = Direction(phase * 4.9f + light * 1.26f) * size * (0.30f + light * 0.10f);
                var point = at + new Vector2(orbit.x, orbit.y * 0.65f) + Vector2.up * size * phase * 0.48f;
                Disc(pen, point, size * 0.18f, color, glow * 0.28f);
                Diamond(pen, point, size * 0.071f, color);
            }
        }

        private void Burst(Painter2D pen, Vector2 center, Color color, int count, float spread)
        {
            for (int particle = 0; particle < count; particle++)
            {
                var ray = Direction(particle * 6.2832f / count + 0.19f);
                float reach = size * spread * (0.38f + phase * (0.47f + (particle % 3) * 0.18f));
                Diamond(pen, center + ray * reach, size * (0.09f + particle % 3 * 0.027f) * (1f - phase * 0.49f), color);
            }
        }

        private void Diamond(Painter2D pen, Vector2 at, float radius, Color color)
        {
            pen.BeginPath();
            pen.MoveTo(at + Vector2.up * radius);
            pen.LineTo(at + Vector2.right * radius * 0.52f);
            pen.LineTo(at + Vector2.down * radius);
            pen.LineTo(at + Vector2.left * radius * 0.52f);
            pen.ClosePath();
            pen.fillColor = Colored(color, glow);
            pen.Fill();
        }

        private void Outline(Painter2D pen, Vector2 at, float radius, Color color, float alpha)
        {
            Circle(pen, at, radius);
            pen.strokeColor = Colored(color, glow * alpha);
            pen.lineWidth = Mathf.Clamp(size * 0.044f, 1.7f, 3.8f);
            pen.Stroke();
        }

        private static void Disc(Painter2D pen, Vector2 at, float radius, Color color, float alpha)
        {
            Circle(pen, at, radius);
            pen.fillColor = Colored(color, alpha);
            pen.Fill();
        }

        private static void Circle(Painter2D pen, Vector2 at, float radius)
        {
            pen.BeginPath();
            pen.Arc(at, radius, 0, 360, ArcDirection.Clockwise);
            pen.ClosePath();
        }

        private static Vector2 Direction(float radians) => new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        private static Color Colored(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    }
}
