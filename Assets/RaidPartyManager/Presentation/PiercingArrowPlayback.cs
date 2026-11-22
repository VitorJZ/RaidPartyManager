using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class PiercingArrowPlayback
    {
        private const float ArrowWidth = 82f;
        private const int ParticleCount = 9;
        private readonly double releaseAt;
        private readonly double flightEnd;
        private readonly string[] targets;
        private readonly float[] impactProgress;
        private readonly VisualElement view;
        private readonly Image arrow;
        private readonly Image[] particles;
        private Vector2? launchTip;
        private float direction;
        private float travel;

        public string LastTargetId => targets[^1];

        public PiercingArrowPlayback(Texture2D projectile, Texture2D particle, IReadOnlyList<BattleEvent> hits,
            double releaseAt, double flightEnd)
        {
            this.releaseAt = releaseAt;
            this.flightEnd = flightEnd;
            targets = hits.Select(hit => hit.TargetId).ToArray();
            impactProgress = new float[targets.Length];
            for (int i = 0; i < impactProgress.Length; i++)
                impactProgress[i] = 0.8f * (i + 1) / impactProgress.Length;

            view = new VisualElement { name = "battle-piercing-flight", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(view);
            view.style.overflow = Overflow.Hidden;
            arrow = Sprite("battle-piercing-arrow", projectile);
            view.Add(arrow);

            particles = new Image[particle == null ? 0 : ParticleCount];
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i] = Sprite("battle-arrow-particle", particle);
                view.Add(particles[i]);
            }
        }

        public static float FlightDuration(int hits) => 0.55f + 0.15f * hits;

        public double ImpactAt(string targetId)
        {
            int index = System.Array.IndexOf(targets, targetId);
            return releaseAt + (flightEnd - releaseAt) * impactProgress[index];
        }

        public void Draw(double now, VisualElement layer, Rect source, IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            if (now >= flightEnd)
            {
                Clear();
                return;
            }

            // Capture the original formation before hit poses displace the target sprites.
            if (!launchTip.HasValue && !SetTrajectory(layer, source, cards) || now < releaseAt)
                return;

            if (view.parent != layer)
            {
                view.RemoveFromHierarchy();
                layer.Add(view);
            }

            float progress = Mathf.Clamp01((float)((now - releaseAt) / (flightEnd - releaseAt)));
            var center = launchTip.Value + new Vector2(direction * (travel * progress - ArrowWidth * 0.5f), 0);
            view.style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impactProgress[^1], 1, progress));
            int frame = AnimationCels.At(progress);
            arrow.sourceRect = AnimationCels.RectFor(arrow.image, frame);
            Place(arrow, center, ArrowWidth, 12f);
            arrow.style.scale = new Scale(new Vector3(direction, 1, 1));

            float shimmer = AnimationCels.Phase(progress);
            for (int i = 0; i < particles.Length; i++)
            {
                float along = i / (particles.Length - 1f);
                float phase = 2 * Mathf.PI * (3.2f * shimmer + 1.25f * along);
                float near = 0.5f + 0.5f * Mathf.Cos(phase);
                var offset = new Vector2(direction * (along - 0.5f) * ArrowWidth * 0.9f, 12f * Mathf.Sin(phase));
                float size = Mathf.Lerp(7f, 15f, near);
                particles[i].sourceRect = AnimationCels.RectFor(particles[i].image, (frame + i * 3) % AnimationCels.Count);
                Place(particles[i], center + offset, size, size);
                particles[i].style.opacity = Mathf.Lerp(0.25f, 1f, near);
            }
        }

        public void Clear() => view.RemoveFromHierarchy();

        private bool SetTrajectory(VisualElement layer, Rect source, IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            var targetCenters = new float[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                if (!cards.TryGetValue(targets[i], out var card) || !(card.BodyBounds.width > 0f))
                    return false;
                targetCenters[i] = layer.WorldToLocal(card.BodyBounds).center.x;
            }
            direction = Mathf.Sign(targetCenters[^1] - source.center.x);
            float releaseOffset = Mathf.Min(source.width * 0.37f, Mathf.Abs(targetCenters[0] - source.center.x) * 0.45f);
            var start = new Vector2(source.center.x + direction * releaseOffset, source.yMin + 0.5f * source.height);
            travel = Mathf.Abs(targetCenters[^1] - start.x) + ArrowWidth + 20f;
            for (int i = 0; i < targetCenters.Length; i++)
                impactProgress[i] = Mathf.Clamp01((targetCenters[i] - start.x) * direction / travel);
            launchTip = start;
            return true;
        }

        private static Image Sprite(string name, Texture2D texture)
        {
            var image = new Image { name = name, image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            return image;
        }

        private static void Place(Image image, Vector2 center, float width, float height)
        {
            image.style.left = center.x - width * 0.5f;
            image.style.top = center.y - height * 0.5f;
            image.style.width = width;
            image.style.height = height;
        }
    }
}
