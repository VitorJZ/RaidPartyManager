using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleEffectPlayback
    {
        private const int EnemyFrames = 8;
        private const int SkillFrames = 12;
        private readonly Texture2D atlas;
        private readonly Image image;
        private readonly int frameCount;
        private readonly double startedAt;
        private readonly double endsAt;
        private int shown = -1;
        public string TargetId { get; }
        public string SkillBookId { get; }

        public BattleEffectPlayback(string targetId, Texture2D atlas, double now, float scale, string skillBookId = null)
        {
            TargetId = targetId;
            SkillBookId = skillBookId;
            this.atlas = atlas;
            bool isSkill = skillBookId != null;
            frameCount = isSkill ? SkillFrames : EnemyFrames;
            startedAt = now;
            endsAt = now + (isSkill ? 0.64f : 0.45f) * scale;
            image = new Image
            {
                name = isSkill ? "battle-skill-effect" : "battle-impact-effect",
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
            var bounds = layer.WorldToLocal(target.BodyBounds);
            float size = SkillBookId != null ? Mathf.Clamp(bounds.width * 1.45f, 52f, 168f)
                : Mathf.Clamp(bounds.width * 0.87f, 36f, 112f);
            int frame = Mathf.Clamp((int)((now - startedAt) / (endsAt - startedAt) * frameCount), 0, frameCount - 1);
            if (shown != frame)
            {
                shown = frame;
                int width = atlas.width / frameCount;
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
