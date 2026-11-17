using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleCharacterDetails : VisualElement
    {
        private readonly Label health;
        private readonly Label attack;
        private readonly Label defense;
        private readonly Label specialDefense;
        private readonly Label conditions;

        public BattleCharacterDetails(int position, CharacterDefinition character, IReadOnlyList<EquipmentDefinition> equipment)
        {
            name = "battle-character-details";
            style.position = Position.Absolute;
            style.backgroundColor = PrototypeStyle.PanelColor;
            style.paddingTop = style.paddingBottom = 10;
            style.paddingLeft = style.paddingRight = 12;
            PrototypeStyle.Outline(this, PrototypeStyle.Accent, 6);

            Add(Field("name", $"{character.Name} · {position} 号位", 16, PrototypeStyle.Accent));
            Add(Field("personality", $"性格  {PersonalityText(character.Personality)}", 12, PrototypeStyle.Muted));
            if (!string.IsNullOrWhiteSpace(character.TraitText))
                Add(Field("trait", $"固定特性  {character.TraitText}", 12, PrototypeStyle.Muted));

            health = Field("health", "", 15, PrototypeStyle.Ally);
            health.style.marginTop = 7;
            Add(health);

            var attributes = PrototypeStyle.Row();
            attributes.style.flexWrap = Wrap.Wrap;
            attack = Field("attack", "");
            defense = Field("defense", "");
            specialDefense = Field("special-defense", "");
            attack.style.marginRight = defense.style.marginRight = 14;
            attributes.Add(attack);
            attributes.Add(defense);
            attributes.Add(specialDefense);
            Add(attributes);

            conditions = Field("statuses", "");
            conditions.style.marginTop = 6;
            Add(conditions);

            var gearHeading = Field("equipment", "装备", 13, PrototypeStyle.Accent);
            gearHeading.style.marginTop = 6;
            Add(gearHeading);
            for (int slot = 0; slot < Rules.EquipmentSlots; slot++)
            {
                var item = equipment[slot];
                Add(Field($"slot-{slot + 1}", $"{slot + 1} · {item?.Name ?? "空槽"}", 13,
                    item != null ? PrototypeStyle.Ink : PrototypeStyle.Muted));
                if (item == null)
                    continue;
                var effect = Field($"item-effect-{slot + 1}", string.IsNullOrWhiteSpace(item.Description)
                    ? BonusText(item.Bonus) : item.Description, 11, PrototypeStyle.Muted);
                effect.style.paddingLeft = 16;
                Add(effect);
            }

            IgnorePicking(this);
        }

        public void Refresh(Stats stats, int currentHealth, int shield, IReadOnlyList<StatusStack> statuses)
        {
            health.text = $"生命  {HealthText(currentHealth, stats.Health)}";
            attack.text = $"攻击  {stats.Attack}";
            defense.text = $"防御  {stats.Defense}";
            specialDefense.text = $"特防  {stats.SpecialDefense}";

            var active = new List<string>();
            if (currentHealth == 0)
                active.Add("倒下");
            else
            {
                if (shield > 0)
                    active.Add($"护盾 {shield}");
                if (statuses != null)
                    active.AddRange(statuses.Select(status => $"{StatusRules.For(status.Kind).Name} ×{status.Layers}"));
            }
            conditions.text = $"状态  {(active.Count == 0 ? "无" : string.Join("、", active))}";
        }

        public void PositionAt(VisualElement root, VisualElement portrait)
        {
            const float margin = 6;
            const float gap = 8;
            var anchor = root.WorldToLocal(portrait.worldBound);
            float width = Mathf.Clamp(root.layout.width - 2 * margin, 0, 300);
            float height = float.IsNaN(layout.height) ? 0 : layout.height;
            style.width = width;
            style.left = Mathf.Clamp(anchor.xMin, margin, Mathf.Max(margin, root.layout.width - width - margin));
            style.top = Mathf.Clamp(anchor.yMin - height - gap, margin, Mathf.Max(margin, root.layout.height - height - margin));
        }

        private static Label Field(string key, string text, int size = 13, Color? color = null)
        {
            var label = PrototypeStyle.Text(text, size, color);
            label.name = $"battle-detail-{key}";
            return label;
        }

        private static void IgnorePicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children())
                IgnorePicking(child);
        }
    }
}
