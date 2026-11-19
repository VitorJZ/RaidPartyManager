using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class RewardFlight : VisualElement
    {
        private const float Duration = 0.46f;

        public RewardFlight(VisualElement layer, Rect source, Func<VisualElement> destination,
            VisualElement icon, float delay, Action arrived)
        {
            name = "reward-flight";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            PrototypeStyle.Fill(icon);
            icon.style.flexGrow = 0;
            Add(icon);

            var root = layer.parent;
            bool hasSource = source.width > 0 && source.height > 0;
            var origin = hasSource ? root.WorldToLocal(source.center) : root.contentRect.center;
            float startSize = hasSource ? Mathf.Clamp(Mathf.Min(source.width, source.height), 22, 72) : 52;
            SetPose(origin, startSize, 0);
            layer.Add(this);
            double startedAt = Time.unscaledTimeAsDouble + delay;
            var animation = schedule.Execute(Advance).Every(16);
            RegisterCallback<DetachFromPanelEvent>(_ => animation.Pause());

            void Advance()
            {
                float progress = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - startedAt) / Duration));
                var bounds = destination().worldBound;
                var target = bounds.width > 0 && bounds.height > 0
                    ? root.WorldToLocal(bounds.center)
                    : new Vector2(origin.x, 24f);
                float eased = 1f - (1f - progress) * (1f - progress);
                float rise = Mathf.Min(78f, Mathf.Abs(target.y - origin.y) * 0.26f + 28f);
                var position = Vector2.Lerp(origin, target, eased);
                float lift = Mathf.Min(Mathf.Sin(Mathf.PI * progress) * rise,
                    Mathf.Max(0, position.y - target.y) * 0.55f);
                position.y -= lift;
                float endSize = bounds.width > 0 && bounds.height > 0
                    ? Mathf.Clamp(Mathf.Min(bounds.width, bounds.height) * 0.85f, 21, 31)
                    : 26;
                SetPose(position, Mathf.Lerp(startSize, endSize, eased),
                    Mathf.Clamp01(progress * 15f) * Mathf.Clamp01((1f - progress) * 18f));

                if (progress < 1)
                    return;
                RemoveFromHierarchy();
                arrived();
            }
        }

        private void SetPose(Vector2 position, float size, float opacity)
        {
            style.left = position.x - size * 0.5f;
            style.top = position.y - size * 0.5f;
            style.width = style.height = size;
            style.opacity = opacity;
        }
    }
}
