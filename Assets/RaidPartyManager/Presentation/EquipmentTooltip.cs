using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class EquipmentTooltip : VisualElement
    {
        public const float Width = 208f;

        public EquipmentTooltip(EquipmentDefinition item, Texture2D texture, bool showScroll = true)
        {
            name = "equipment-tooltip";
            style.position = Position.Absolute;
            style.width = Width;
            style.paddingTop = style.paddingBottom = 10;
            style.paddingLeft = style.paddingRight = 11;
            style.backgroundColor = (Color)new Color32(32, 34, 45, 255);
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Outline(this, PrototypeStyle.Accent, 7);

            var art = new EquipmentIcon(item, texture, showScroll) { name = "equipment-tooltip-art" };
            art.style.height = 87;
            art.style.flexGrow = 0;
            art.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(art, PrototypeStyle.Edge, 4);
            Add(art);

            var title = PrototypeStyle.Text(item.Name, 16, PrototypeStyle.Accent);
            title.name = "equipment-tooltip-name";
            title.pickingMode = PickingMode.Ignore;
            title.style.marginTop = 5;
            title.style.paddingBottom = 4;
            title.style.borderBottomWidth = 1;
            title.style.borderBottomColor = PrototypeStyle.Edge;
            Add(title);

            var description = PrototypeStyle.Text(string.IsNullOrWhiteSpace(item.Description)
                ? BonusText(item.Bonus) : item.Description, 13);
            description.name = "equipment-tooltip-description";
            description.pickingMode = PickingMode.Ignore;
            description.style.marginTop = 5;
            Add(description);
            if (item is SkillBookDefinition book)
            {
                var requirement = book.Requirement switch
                {
                    SkillRequirement.Mage => "法师", SkillRequirement.Support => "后勤",
                    SkillRequirement.Archer => "射手", SkillRequirement.Melee => "近战",
                    SkillRequirement.Warrior => "战士", _ => "不限职业"
                };
                var rules = PrototypeStyle.Text($"{requirement} · 消耗 {book.Cost} 点能量", 12, PrototypeStyle.Muted);
                rules.name = "skill-book-requirements";
                rules.style.marginTop = 6;
                Add(rules);
                if (book.Cost > Rules.DefaultEnergy) Add(PrototypeStyle.Text("从第 2 回合起，每回合能量和上限各增加 1 点，直到上限达到所学最高费用。", 12, PrototypeStyle.Accent));
                var permanence = PrototypeStyle.Text("学会后无法拆卸，覆盖会失去旧技能。", 12, PrototypeStyle.Muted);
                Add(permanence);
            }
        }

        public void ShowAbove(VisualElement root, VisualElement anchor)
        {
            const float margin = 6f;
            const float gap = 7f;
            float width = Mathf.Clamp(root.layout.width - 2 * margin, 0, Width);
            style.width = width;
            void Follow(GeometryChangedEvent _) => Place();
            RegisterCallback<GeometryChangedEvent>(Follow);
            anchor.RegisterCallback<GeometryChangedEvent>(Follow);
            RegisterCallback<DetachFromPanelEvent>(_ => anchor.UnregisterCallback<GeometryChangedEvent>(Follow));
            root.Add(this);
            Place();

            void Place()
            {
                var bounds = root.WorldToLocal(anchor.worldBound);
                float height = float.IsNaN(layout.height) ? 0f : layout.height;
                float top = bounds.yMin - gap - height;
                if (top < margin && bounds.yMax + gap + height <= root.layout.height - margin)
                    top = bounds.yMax + gap;
                style.left = Mathf.Clamp(bounds.center.x - width * 0.5f, margin, Mathf.Max(margin, root.layout.width - width - margin));
                style.top = Mathf.Clamp(top, margin, Mathf.Max(margin, root.layout.height - height - margin));
            }
        }
    }
}
