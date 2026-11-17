using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleEffectPlayback
    {
        private const int Frames = 8;
        private readonly Texture2D atlas;
        private readonly Image image;
        private readonly double startedAt;
        private readonly double endsAt;
        private int shown = -1;
        public string TargetId { get; }

        public BattleEffectPlayback(string targetId, Texture2D atlas, double now, float scale)
        {
            TargetId = targetId;
            this.atlas = atlas;
            startedAt = now;
            endsAt = now + 0.45f * scale;
            image = new Image { name = "battle-impact-effect", image = atlas, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
        }

        public bool ActiveAt(double now) => now < endsAt;
        public void Clear() => image.RemoveFromHierarchy();

        public void Draw(VisualElement layer, BattleUnitCard target, double now)
        {
            if (layer == null || target == null)
                return;
            var bounds = layer.WorldToLocal(target.BodyBounds);
            float size = Mathf.Clamp(bounds.width * 0.87f, 36f, 112f);
            int frame = Mathf.Min(Frames - 1, (int)((now - startedAt) / (endsAt - startedAt) * Frames));
            if (shown != frame)
            {
                shown = frame;
                int width = atlas.width / Frames;
                image.sourceRect = new Rect(shown * width, 0, width, atlas.height);
                image.MarkDirtyRepaint();
            }
            image.style.left = bounds.center.x - size * 0.5f;
            image.style.top = bounds.center.y - size * 0.5f;
            image.style.width = image.style.height = size;
            if (image.parent != layer)
            {
                Clear();
                layer.Add(image);
            }
        }
    }
}
