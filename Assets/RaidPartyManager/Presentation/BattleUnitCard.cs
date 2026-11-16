using System;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Represents one fixed position. HP belongs above the portrait and conditions below it.
    internal sealed class BattleUnitCard : VisualElement
    {
        private readonly int maximumHealth;
        private readonly Label healthLabel;
        private readonly VisualElement healthFill;
        private readonly VisualElement portrait;
        private readonly Label statusLabel;
        private readonly Color sideColor;

        public BattleUnitCard(Side side, int position, string unitId, CharacterDefinition character,
            Texture2D art, int maximumHealth, int health, int shield, string subtitle, bool selected, Action place)
        {
            name = $"{(side == Side.Party ? "party" : "enemy")}-position-{position}";
            this.maximumHealth = maximumHealth;
            sideColor = side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy;
            style.flexGrow = 1;
            style.flexBasis = 0;
            style.minWidth = 0;
            style.marginLeft = style.marginRight = 4;

            var life = new VisualElement { name = "health-bar" };
            life.style.height = 22;
            life.style.overflow = Overflow.Hidden;
            life.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(life, PrototypeStyle.Edge, 3);
            healthFill = new VisualElement { name = "health-fill" };
            healthFill.style.position = Position.Absolute;
            healthFill.style.top = healthFill.style.bottom = healthFill.style.left = 0;
            healthFill.style.backgroundColor = Color.Lerp(sideColor, PrototypeStyle.Background, 0.42f);
            life.Add(healthFill);
            healthLabel = PrototypeStyle.Text("", 12);
            healthLabel.style.flexGrow = 1;
            PrototypeStyle.Middle(healthLabel);
            life.Add(healthLabel);
            Add(life);

            portrait = new VisualElement { name = "combatant-portrait" };
            portrait.style.height = 226;
            portrait.style.marginTop = portrait.style.marginBottom = 4;
            portrait.style.overflow = Overflow.Hidden;
            portrait.style.backgroundColor = character == null ? PrototypeStyle.Inset
                : Color.Lerp(PrototypeStyle.PanelColor, sideColor, 0.14f);
            PrototypeStyle.Outline(portrait, selected ? PrototypeStyle.Accent : PrototypeStyle.Edge, 4);
            Add(portrait);

            if (art != null)
            {
                var image = new Image { image = art, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(image);
                portrait.Add(image);
            }
            else
            {
                var mark = PrototypeStyle.Text(character == null ? "·" : ProfessionMark(character.Profession),
                    character == null ? 26 : 44, character == null ? PrototypeStyle.Edge : sideColor);
                mark.style.flexGrow = 1;
                PrototypeStyle.Middle(mark);
                portrait.Add(mark);
            }

            var positionTag = PrototypeStyle.Text(position.ToString("00"), 11, side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy);
            positionTag.style.position = Position.Absolute;
            positionTag.style.top = positionTag.style.left = 5;
            positionTag.style.backgroundColor = PrototypeStyle.Background;
            positionTag.style.paddingLeft = positionTag.style.paddingRight = 4;
            portrait.Add(positionTag);

            var caption = new VisualElement();
            caption.style.position = Position.Absolute;
            caption.style.left = caption.style.right = caption.style.bottom = 0;
            caption.style.paddingLeft = caption.style.paddingRight = 5;
            caption.style.paddingTop = caption.style.paddingBottom = 4;
            caption.style.backgroundColor = new Color(0.064f, 0.069f, 0.086f, 0.88f);
            caption.Add(PrototypeStyle.Text(character?.Name ?? "空位", 14));
            if (!string.IsNullOrEmpty(subtitle))
                caption.Add(PrototypeStyle.Text(subtitle, 11, PrototypeStyle.Muted));
            if (place != null)
                caption.Add(PrototypeStyle.Button($"放到 {position} 号位", $"position-{position}", place, selected));
            portrait.Add(caption);

            statusLabel = PrototypeStyle.Text("", 12, PrototypeStyle.Muted);
            statusLabel.name = unitId == null ? "status-empty" : $"status-{unitId}";
            statusLabel.style.height = 23;
            statusLabel.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Middle(statusLabel);
            PrototypeStyle.Outline(statusLabel, PrototypeStyle.Edge, 3);
            Add(statusLabel);
            Refresh(health, shield);
        }

        public void Refresh(int health, int shield)
        {
            if (maximumHealth == 0)
            {
                healthFill.style.width = 0;
                healthLabel.text = "—";
                statusLabel.text = "—";
                return;
            }

            healthLabel.text = $"{health} / {maximumHealth}";
            healthFill.style.width = Length.Percent(100f * health / maximumHealth);
            statusLabel.text = health == 0 ? "倒下" : shield > 0 ? $"护盾 {shield}" : "正常";
            statusLabel.style.color = health == 0 ? PrototypeStyle.Enemy : shield > 0 ? sideColor : PrototypeStyle.Muted;
            portrait.style.opacity = health == 0 ? 0.55f : 1f;
        }

        private static string ProfessionMark(Profession profession) => profession switch
        {
            Profession.Warrior => "战",
            Profession.Mage => "术",
            Profession.Archer => "弓",
            Profession.Support => "援",
            _ => "·"
        };
    }
}
