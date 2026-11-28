using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleEffectPlayback
    {
        private readonly Texture2D atlas;
        private readonly VisualElement view;
        private readonly bool fullBody;
        private readonly bool rebirth;
        private readonly Image itemIcon;
        private readonly int equipmentLane;
        private readonly AttackSignatureImage signatureImage;
        private readonly int celWidth;
        private readonly int frameCount;
        private readonly double startedAt;
        private readonly double endsAt;
        private int shown = -1;
        public string TargetId { get; }
        public string SkillBookId { get; }
        public EquipmentEffect Equipment { get; }

        public BattleEffectPlayback(string targetId, Texture2D atlas, double now, float scale, string skillBookId = null, bool charm = false,
            EquipmentEffect equipment = EquipmentEffect.None, Texture2D icon = null, int equipmentLane = 0, bool rebirth = false)
        {
            TargetId = targetId;
            SkillBookId = skillBookId;
            this.atlas = atlas;
            Equipment = equipment;
            this.equipmentLane = equipmentLane;
            this.rebirth = rebirth;
            bool isEquipment = equipment != EquipmentEffect.None;
            bool isSkill = skillBookId != null;
            fullBody = isSkill || charm || isEquipment;
            frameCount = isSkill ? Mathf.Max(1, atlas.width / atlas.height) : AnimationCels.Count;
            celWidth = atlas.width / frameCount;
            startedAt = now;
            endsAt = now + (rebirth ? BattleSpriteView.RebirthLength * scale :
                isEquipment ? Mathf.Max(0.14f, 0.54f * scale) : (fullBody ? 0.72f : 0.45f) * scale);
            var image = new Image
            {
                name = rebirth ? "battle-rebirth-aura" : isEquipment ? "battle-equipment-effect" :
                    charm ? "battle-elven-charm-effect" : isSkill ? "battle-skill-effect" : "battle-impact-effect",
                image = atlas,
                pickingMode = PickingMode.Ignore
            };
            if (isSkill)
                image.AddToClassList(skillBookId);
            if (isEquipment)
            {
                image.AddToClassList("equipment-" + equipment);
                if (icon != null)
                {
                    itemIcon = new Image { name = "triggered-equipment-icon", image = icon, pickingMode = PickingMode.Ignore };
                    itemIcon.style.position = Position.Absolute;
                    itemIcon.style.width = itemIcon.style.height = 24;
                    image.Add(itemIcon);
                }
            }
            view = image;
            view.style.position = Position.Absolute;
        }

        internal BattleEffectPlayback(string targetId, Texture2D signature, bool facesLeft, double now, float scale)
        {
            TargetId = targetId;
            startedAt = now;
            endsAt = now + .40f * scale;
            signatureImage = new AttackSignatureImage("battle-signature-impact", signature, inFlight: false);
            view = signatureImage;
            if (facesLeft) view.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
        }

        internal bool BlocksNextAction => signatureImage == null && Equipment == EquipmentEffect.None;

        public bool ActiveAt(double now) => now < endsAt;
        public void Clear() => view.RemoveFromHierarchy();

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
            float size = rebirth ? bounds.width * 1.4f
                : SkillBookId == "skill-book-01" ? Mathf.Clamp(bounds.width * 1.78f, 64f, 186f)
                : fullBody ? Mathf.Clamp(bounds.width * 1.45f, 52f, 168f)
                : signatureImage != null ? Mathf.Clamp(bounds.width * 1.10f, 49f, 124f)
                : Mathf.Clamp(bounds.width * 0.87f, 36f, 112f);
            float progress = Mathf.Clamp01((float)((now - startedAt) / (endsAt - startedAt)));
            if (signatureImage != null)
                signatureImage.ShowFrame(progress);
            else
            {
                var image = (Image)view;
                int frame = Mathf.Min((int)(progress * frameCount), frameCount - 1);
                if (shown != frame)
                {
                    shown = frame;
                    image.sourceRect = rebirth || Equipment != EquipmentEffect.None ? AnimationCels.RectFor(atlas, shown) :
                        new Rect(shown * celWidth, 0, celWidth, atlas.height);
                    image.MarkDirtyRepaint();
                }
            }
            if (itemIcon != null)
            {
                itemIcon.style.left = (size - 24) / 2;
                itemIcon.style.top = size * (0.29f - 0.22f * progress);
                itemIcon.style.opacity = Mathf.Clamp01(progress * 10f) * Mathf.Clamp01((1 - progress) * 4);
            }
            float offset = equipmentLane == 0 ? 0 : (equipmentLane % 2 == 0 ? -1 : 1) * size * .16f;
            view.style.left = bounds.center.x - size * 0.5f + offset;
            view.style.top = rebirth ? bounds.yMax - size * .90f :
                bounds.center.y - size * 0.5f - (equipmentLane / 3) * size * .18f;
            view.style.width = view.style.height = size;
            if (view.parent != layer)
            {
                Clear();
                layer.Add(view);
            }
        }
    }
}
