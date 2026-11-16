using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // One combat position with its health and active effects beneath the portrait.
    internal sealed class BattleUnitCard : VisualElement
    {
        private readonly int maximumHealth;
        private readonly Label healthLabel;
        private readonly VisualElement healthFill;
        private readonly VisualElement portrait;
        private readonly Label statusLabel;
        private readonly VisualElement badges;
        private readonly Label statusDetail;
        private readonly Func<StatusKind, Texture2D> statusIcon;
        private readonly Color sideColor;

        public int Slot { get; }
        public bool CanSwap { get; }
        public Texture2D Artwork { get; }
        public string CharacterName { get; }

        public BattleUnitCard(Side side, int position, string unitId, CharacterDefinition character,
            Texture2D art, int maximumHealth, int health, int shield, string subtitle, bool selected, bool hidden = false,
            Func<StatusKind, Texture2D> statusIcon = null)
        {
            name = $"{(side == Side.Party ? "party" : "enemy")}-position-{position}";
            Slot = position;
            CanSwap = side == Side.Party && health > 0;
            Artwork = art;
            CharacterName = character?.Name;
            this.maximumHealth = maximumHealth;
            this.statusIcon = statusIcon;
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

            portrait = new VisualElement { name = "combatant-portrait" };
            portrait.style.height = 226;
            portrait.style.marginTop = portrait.style.marginBottom = 4;
            portrait.style.overflow = Overflow.Hidden;
            portrait.style.backgroundColor = character == null ? PrototypeStyle.Inset
                : Color.Lerp(PrototypeStyle.PanelColor, sideColor, 0.14f);
            PrototypeStyle.Outline(portrait, selected ? PrototypeStyle.Accent : PrototypeStyle.Edge, 4);
            Add(portrait);

            if (art == null)
            {
                var mark = PrototypeStyle.Text(hidden ? "?" : character == null ? "·" : ProfessionMark(character.Profession),
                    character == null ? 26 : 44, character == null ? PrototypeStyle.Edge : sideColor);
                mark.style.flexGrow = 1;
                PrototypeStyle.Middle(mark);
                portrait.Add(mark);
            }

            ProfessionFrame.AddTo(portrait, character?.Profession, art);

            var positionTag = PrototypeStyle.Text(position.ToString("00"), 11, side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy);
            positionTag.style.position = Position.Absolute;
            positionTag.style.top = positionTag.style.left = 5;
            positionTag.style.backgroundColor = PrototypeStyle.Background;
            positionTag.style.paddingLeft = positionTag.style.paddingRight = 4;
            portrait.Add(positionTag);

            var caption = new VisualElement();
            caption.style.position = Position.Absolute;
            caption.style.left = caption.style.right = caption.style.bottom = 5;
            caption.style.paddingLeft = caption.style.paddingRight = 5;
            caption.style.paddingTop = caption.style.paddingBottom = 4;
            caption.style.backgroundColor = new Color(0.064f, 0.069f, 0.086f, 0.88f);
            caption.Add(PrototypeStyle.Text(hidden ? "未知" : character?.Name ?? "空位", 14));
            if (!string.IsNullOrEmpty(subtitle))
                caption.Add(PrototypeStyle.Text(subtitle, 11, PrototypeStyle.Muted));
            portrait.Add(caption);
            Add(life);

            var conditions = new VisualElement { name = unitId == null ? "status-row-empty" : $"status-row-{unitId}" };
            conditions.style.minHeight = 29;
            conditions.style.marginTop = 3;
            Add(conditions);
            badges = PrototypeStyle.Row("status-badges");
            badges.style.flexWrap = Wrap.Wrap;
            badges.style.justifyContent = Justify.Center;
            conditions.Add(badges);

            statusLabel = PrototypeStyle.Text("", 12, PrototypeStyle.Muted);
            statusLabel.name = unitId == null ? "status-empty" : $"status-{unitId}";
            statusLabel.style.height = 23;
            statusLabel.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Middle(statusLabel);
            PrototypeStyle.Outline(statusLabel, PrototypeStyle.Edge, 3);
            conditions.Add(statusLabel);

            statusDetail = PrototypeStyle.Text("", 11);
            statusDetail.name = "status-detail";
            statusDetail.style.paddingLeft = statusDetail.style.paddingRight = 6;
            statusDetail.style.paddingTop = statusDetail.style.paddingBottom = 4;
            statusDetail.style.whiteSpace = WhiteSpace.Normal;
            statusDetail.style.backgroundColor = PrototypeStyle.Inset;
            statusDetail.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Outline(statusDetail, PrototypeStyle.Edge, 3);
            conditions.Add(statusDetail);
            Refresh(health, shield);
        }

        public void Refresh(int health, int shield, IReadOnlyList<StatusStack> statuses = null)
        {
            badges.Clear();
            statusDetail.style.display = DisplayStyle.None;
            statusLabel.style.display = DisplayStyle.Flex;
            if (maximumHealth == 0)
            {
                healthFill.style.width = 0;
                healthLabel.text = "—";
                statusLabel.text = "—";
                return;
            }

            healthLabel.text = $"{health} / {maximumHealth}";
            healthFill.style.width = Length.Percent(100f * health / maximumHealth);
            bool alive = health > 0;
            statusLabel.text = alive ? "正常" : "倒下";
            statusLabel.style.color = alive ? PrototypeStyle.Muted : PrototypeStyle.Enemy;
            portrait.style.opacity = alive ? 1f : 0.55f;

            if (!alive)
                return;
            if (shield > 0)
                AddBadge("shield", "盾", shield, "护盾", "先吸收受到的伤害。", null, PrototypeStyle.Ally);
            if (statuses != null)
                foreach (var status in statuses)
                {
                    var rule = StatusRules.For(status.Kind);
                    AddBadge(status.Kind.ToString(), rule.Name.Substring(0, 1), status.Layers, rule.Name,
                        rule.Description, statusIcon?.Invoke(status.Kind), rule.Negative ? PrototypeStyle.Enemy : PrototypeStyle.Accent);
                }
            if (badges.childCount > 0)
                statusLabel.style.display = DisplayStyle.None;
        }

        private void AddBadge(string id, string fallback, int count, string title, string detail, Texture2D icon, Color edge)
        {
            var badge = new VisualElement { name = $"status-badge-{id}" };
            badge.style.width = 25;
            badge.style.height = 24;
            badge.style.marginLeft = badge.style.marginRight = 1;
            badge.style.marginBottom = 2;
            badge.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(badge, edge, 4);
            badges.Add(badge);
            if (icon != null)
            {
                var art = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                art.style.flexGrow = 1;
                badge.Add(art);
            }
            else
            {
                var marker = PrototypeStyle.Text(fallback, 12, edge);
                PrototypeStyle.Middle(marker);
                marker.pickingMode = PickingMode.Ignore;
                marker.style.flexGrow = 1;
                badge.Add(marker);
            }

            var amount = PrototypeStyle.Text(count.ToString(), 10);
            amount.name = "status-stack-count";
            amount.style.position = Position.Absolute;
            amount.style.bottom = -1;
            amount.style.right = -1;
            amount.style.paddingLeft = amount.style.paddingRight = 2;
            amount.style.backgroundColor = PrototypeStyle.Background;
            amount.pickingMode = PickingMode.Ignore;
            badge.Add(amount);

            badge.RegisterCallback<PointerEnterEvent>(_ =>
            {
                statusDetail.text = $"{title} ×{count}\n{detail}";
                statusDetail.style.display = DisplayStyle.Flex;
            });
            badge.RegisterCallback<PointerLeaveEvent>(_ => statusDetail.style.display = DisplayStyle.None);
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
