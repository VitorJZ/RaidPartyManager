using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleEffectPlayback
    {
        private readonly Texture2D atlas;
        private readonly Image image;
        private readonly bool fullBody;
        private readonly int celWidth;
        private readonly int frameCount;
        private readonly double startedAt;
        private readonly double endsAt;
        private int shown = -1;
        public string TargetId { get; }
        public string SkillBookId { get; }

        public BattleEffectPlayback(string targetId, Texture2D atlas, double now, float scale, string skillBookId = null, bool charm = false)
        {
            TargetId = targetId;
            SkillBookId = skillBookId;
            this.atlas = atlas;
            bool isSkill = skillBookId != null;
            fullBody = isSkill || charm;
            frameCount = isSkill ? Mathf.Max(1, atlas.width / atlas.height) : AnimationCels.Count;
            celWidth = atlas.width / frameCount;
            startedAt = now;
            endsAt = now + (fullBody ? 0.72f : 0.45f) * scale;
            image = new Image
            {
                name = charm ? "battle-elven-charm-effect" : isSkill ? "battle-skill-effect" : "battle-impact-effect",
                image = atlas,
                pickingMode = PickingMode.Ignore
            };
            if (isSkill)
                image.AddToClassList(skillBookId);
            image.style.position = Position.Absolute;
        }

        public bool ActiveAt(double now) => now < endsAt;
        public void Clear() => image.RemoveFromHierarchy();

        public void Draw(VisualElement layer, BattleUnitCard target, double now)
        {
            if (layer == null || target == null)
                return;
            // Consuming equipment rebuilds the cards on the impact frame. Wait for
            // the replacement figure to receive its layout before placing its effect.
            var body = target.BodyBounds;
            if (!(body.width > 0 && body.height > 0 && layer.worldBound.width > 0 && layer.worldBound.height > 0))
            {
                Clear();
                return;
            }
            var bounds = layer.WorldToLocal(body);
            float size = fullBody ? Mathf.Clamp(bounds.width * 1.45f, 52f, 168f)
                : Mathf.Clamp(bounds.width * 0.87f, 36f, 112f);
            float progress = Mathf.Clamp01((float)((now - startedAt) / (endsAt - startedAt)));
            int frame = Mathf.Min((int)(progress * frameCount), frameCount - 1);
            if (shown != frame)
            {
                shown = frame;
                image.sourceRect = new Rect(shown * celWidth, 0, celWidth, atlas.height);
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
