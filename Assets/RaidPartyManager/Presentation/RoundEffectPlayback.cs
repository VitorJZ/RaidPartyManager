using System;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class RoundEffectPlayback
    {
        private static readonly Color Fire = new Color32(255, 150, 64, 255);
        private static readonly Color Heal = new Color32(143, 246, 183, 255);
        private static readonly Color Poison = new Color32(211, 126, 244, 255);
        private static readonly Color Lightning = new Color32(130, 205, 255, 255);
        private static readonly Color Stars = new Color32(220, 209, 255, 255);
        private static readonly Color Gold = new Color32(255, 220, 138, 255);

        private readonly BattleVisualCue cue;
        private readonly VisualElement view;
        private readonly Label caption;
        private readonly Image carriedItem;
        private readonly double startedAt;
        private readonly double duration;
        private readonly Color color;
        private readonly bool convertsHealingToDamage;
        private readonly int labelLane;
        private Vector2 target;
        private Vector2 itemPosition;
        private float radius;
        private float progress;
        private float opacity;

        public string TargetId { get; }
        public string ActorId { get; }
        public bool AtRoundStart { get; }
        public static bool IsRoundEnd(BattleVisualCue visual) =>
            visual is >= BattleVisualCue.RoundEndEmber and <= BattleVisualCue.RoundEndTransfer;
        public static bool IsRoundStart(BattleVisualCue visual) =>
            visual is >= BattleVisualCue.RoundStartExposed and <= BattleVisualCue.RoundStartBonus;

        public RoundEffectPlayback(BattleEvent entry, int absorbed, double now, float scale, Texture2D transferIcon, int labelLane, bool drawParticles = true)
        {
            TargetId = entry.TargetId;
            ActorId = entry.ActorId;
            cue = entry.VisualCue;
            startedAt = now;
            AtRoundStart = IsRoundStart(cue);
            this.labelLane = labelLane;
            duration = AtRoundStart ? 0.56 : Math.Max(0.16, (cue == BattleVisualCue.RoundEndTransfer ? 0.74 : 0.62) * scale);
            convertsHealingToDamage = entry.ActionKind == ActionKind.Attack &&
                cue is BattleVisualCue.RoundEndHeal or BattleVisualCue.RoundEndPoisonHeal;
            color = cue switch
            {
                BattleVisualCue.RoundEndEmber => Fire,
                BattleVisualCue.RoundEndHeal or BattleVisualCue.RoundEndPoisonHeal => convertsHealingToDamage ? Poison : Heal,
                BattleVisualCue.RoundEndPoison => Poison,
                BattleVisualCue.RoundEndLightning => Lightning,
                BattleVisualCue.RoundEndTransfer or BattleVisualCue.RoundStartBonus => Gold,
                BattleVisualCue.RoundStartExposed => new Color32(255, 141, 109, 255),
                BattleVisualCue.RoundStartPoison => new Color32(187, 224, 95, 255),
                BattleVisualCue.RoundStartBlessing => Heal,
                _ => Stars
            };
            string phase = AtRoundStart ? "round-start-" : "round-end-";
            view = new VisualElement { name = phase + "effect", pickingMode = PickingMode.Ignore };
            view.AddToClassList(phase + cue);
            PrototypeStyle.Fill(view);
            if (drawParticles) view.generateVisualContent += Paint;

            caption = PrototypeStyle.Text(Caption(entry, absorbed), 13, color);
            caption.name = phase + "effect-label";
            caption.pickingMode = PickingMode.Ignore;
            caption.style.position = Position.Absolute;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.backgroundColor = (Color)new Color32(23, 21, 34, 236);
            caption.style.height = 22;
            caption.style.paddingLeft = caption.style.paddingRight = 6;
            caption.style.overflow = Overflow.Hidden;
            caption.style.textOverflow = TextOverflow.Ellipsis;
            PrototypeStyle.Outline(caption, WithAlpha(color, 0.75f), 4);
            view.Add(caption);

            if (cue == BattleVisualCue.RoundEndTransfer && transferIcon != null)
            {
                carriedItem = new Image { image = transferIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                carriedItem.style.position = Position.Absolute;
                carriedItem.style.width = carriedItem.style.height = 30;
                view.Insert(0, carriedItem);
            }
        }

        private static string Caption(BattleEvent entry, int absorbed)
        {
            string value = entry.ActionKind switch
            {
                ActionKind.Heal => $"+{entry.Amount} 生命",
                ActionKind.Attack when entry.Amount > 0 => $"−{entry.Amount} 生命",
                ActionKind.Attack when absorbed > 0 => $"−{absorbed} 护盾",
                ActionKind.Attack => "已抵挡",
                _ when entry.VisualCue == BattleVisualCue.RoundEndEmber =>
                    $"余烬 ×{entry.StatusStacks?.FirstOrDefault(stack => stack.Kind == StatusKind.Ember).Layers ?? 0}",
                _ => entry.Detail
            };
            return string.IsNullOrEmpty(value) ? entry.ActionName : $"{entry.ActionName}  {value}";
        }

        public bool ActiveAt(double now) => now < startedAt + duration;
        public bool HoldsTargetAt(double now) => now < startedAt + Math.Max(0.055, duration * 0.39);
        public void Clear() => view.RemoveFromHierarchy();

        public void Draw(VisualElement layer, BattleUnitCard caster, BattleUnitCard recipient, double now)
        {
            if (layer == null || recipient == null)
                return;
            Rect body = layer.WorldToLocal(recipient.BodyBounds);
            if (body.width <= 0 || body.height <= 0)
                return;

            if (view.parent != layer)
                layer.Add(view);

            target = body.center;
            radius = Mathf.Clamp(Mathf.Min(body.width, body.height) * 0.46f, 25, 65);
            progress = AnimationCels.Phase((float)((now - startedAt) / duration));
            opacity = Mathf.Clamp01(progress * 8f) * (1f - Mathf.SmoothStep(0.53f, 1f, progress));
            float labelWidth = Mathf.Min(layer.layout.width, Mathf.Clamp(caption.text.Sum(letter => letter > 127 ? 13 : 7) + 14, 92, 190));
            caption.style.width = labelWidth;
            caption.style.left = Mathf.Clamp(target.x - labelWidth * 0.5f, 0f, Mathf.Max(0f, layer.layout.width - labelWidth));
            caption.style.top = Mathf.Max(0f,
                target.y - radius * 0.16f - progress * Mathf.Min(80, target.y * 0.8f) + labelLane * 24);
            caption.style.opacity = Mathf.Clamp01((1f - progress) * 3.8f);

            if (carriedItem != null)
            {
                Vector2 source = caster != null ? layer.WorldToLocal(caster.BodyBounds).center : target;
                float flight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress * 1.5f));
                itemPosition = Vector2.Lerp(source, target, flight) - Vector2.down * Mathf.Sin(flight * Mathf.PI) * radius;
                carriedItem.style.left = itemPosition.x - 15;
                carriedItem.style.top = itemPosition.y - 15;
                carriedItem.style.opacity = opacity;
            }

            view.MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            if (opacity <= 0)
                return;
            var pen = context.painter2D;
            switch (cue)
            {
                case BattleVisualCue.RoundEndEmber:
                    for (int spark = -2; spark <= 2; spark++)
                    {
                        float rise = (progress + (spark + 2) * 0.16f) % 1f;
                        var at = target + new Vector2(spark * radius * 0.23f + Mathf.Sin(rise * 6 + spark) * 5f,
                            radius * (0.65f - rise * 1.6f));
                        pen.BeginPath();
                        pen.MoveTo(at + Vector2.down * 7f);
                        pen.QuadraticCurveTo(at + new Vector2(11f, 3f), at + new Vector2(-1f, -9f));
                        pen.strokeColor = WithAlpha(color, opacity * (1f - rise * 0.45f));
                        pen.lineWidth = 3.5f - rise * 1.9f;
                        pen.Stroke();
                        Dot(pen, at, 2.5f, color, opacity);
                    }
                    break;
                case BattleVisualCue.RoundEndPoison:
                case BattleVisualCue.RoundStartPoison:
                case BattleVisualCue.RoundEndPoisonHeal when convertsHealingToDamage:
                case BattleVisualCue.RoundEndHeal when convertsHealingToDamage:
                    for (int bubble = 0; bubble < 7; bubble++)
                    {
                        var at = RisingPoint(bubble, 7);
                        Dot(pen, at, radius * (0.10f + 0.06f * (bubble % 3)), color, opacity * 0.26f);
                        Ring(pen, at, radius * (0.07f + 0.03f * (bubble % 3)), color, opacity * 0.95f);
                    }
                    break;
                case BattleVisualCue.RoundEndLightning:
                    for (int branch = -1; branch <= 1; branch++)
                    {
                        float flicker = Mathf.Sin(Mathf.Floor(progress * 14f) + branch * 2f) * radius * 0.2f;
                        var start = target + new Vector2(branch * radius * 0.66f, -radius * 1.35f);
                        pen.BeginPath();
                        pen.MoveTo(start);
                        pen.LineTo(start + new Vector2(flicker, radius * 0.44f));
                        pen.LineTo(start + new Vector2(flicker - radius * 0.28f, radius * 0.69f));
                        pen.LineTo(Vector2.Lerp(start, target, 0.93f) + Vector2.down * radius * 0.36f);
                        pen.strokeColor = WithAlpha(color, opacity);
                        pen.lineWidth = branch == 0 ? 4f : 2f;
                        pen.Stroke();
                    }
                    break;
                case BattleVisualCue.RoundEndTransfer:
                    Ring(pen, carriedItem != null ? itemPosition : target, 11f + 9f * progress, Gold, opacity);
                    break;
                default:
                    Ring(pen, target, radius * (0.37f + progress * 0.77f), color, opacity * 0.82f);
                    for (int spark = 0; spark < 5; spark++)
                    {
                        float width = spark % 2 == 0 ? 5f : 3f;
                        Vector2 at = RisingPoint(spark, 5);
                        Color tint = cue == BattleVisualCue.RoundEndPoisonHeal && spark < 2 ? Poison : color;
                        pen.BeginPath();
                        pen.MoveTo(at - Vector2.right * width);
                        pen.LineTo(at + Vector2.right * width);
                        pen.MoveTo(at - Vector2.down * width);
                        pen.LineTo(at + Vector2.down * width);
                        pen.strokeColor = WithAlpha(tint, opacity);
                        pen.lineWidth = 2f;
                        pen.Stroke();
                    }
                    if (cue == BattleVisualCue.RoundEndFormShift)
                        Ring(pen, target, radius * (1.23f - progress * 0.72f), Lightning, opacity);
                    break;
            }
        }

        private Vector2 RisingPoint(int index, int count)
        {
            float angle = index * 2 * Mathf.PI / count + progress * 1.7f;
            return target + new Vector2(Mathf.Cos(angle) * radius * (0.53f + progress * 0.35f),
                Mathf.Sin(angle) * radius * 0.42f - progress * radius * 0.94f);
        }

        private static void Ring(Painter2D pen, Vector2 at, float size, Color color, float alpha)
        {
            Circle(pen, at, size);
            pen.strokeColor = WithAlpha(color, alpha);
            pen.lineWidth = 1.7f;
            pen.Stroke();
        }

        private static void Dot(Painter2D pen, Vector2 at, float size, Color color, float alpha)
        {
            Circle(pen, at, size);
            pen.fillColor = WithAlpha(color, alpha);
            pen.Fill();
        }

        private static void Circle(Painter2D pen, Vector2 at, float size)
        {
            pen.BeginPath();
            pen.Arc(at, size, 0, 360, ArcDirection.Clockwise);
            pen.ClosePath();
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    }
}
