using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // One combat position with overhead health and active status icons below the character.
    internal sealed class BattleUnitCard : VisualElement
    {
        private readonly int maximumHealth;
        private readonly Label healthLabel;
        private readonly VisualElement healthFill;
        private readonly VisualElement portrait;
        private readonly VisualElement badges;
        private readonly Func<StatusKind, Texture2D> statusIcon;
        private readonly BattleStatusTooltip statusTooltip;
        private readonly BattleSpriteView battleSprite;
        private readonly Color sideColor;

        public int Slot { get; }
        public bool CanSwap { get; }
        public bool HasBattleSprite => battleSprite != null;
        public Texture2D Artwork { get; }
        public string CharacterName { get; }
        public Rect BodyBounds => battleSprite?.Figure.worldBound ?? portrait.worldBound;

        public BattleUnitCard(Side side, int position, string unitId, CharacterDefinition character,
            Texture2D art, int maximumHealth, int health, int shield, string subtitle, bool hidden = false,
            Func<StatusKind, Texture2D> statusIcon = null, BattleSpriteView battleSprite = null,
            BattleStatusTooltip statusTooltip = null)
        {
            name = $"{(side == Side.Party ? "party" : "enemy")}-position-{position}";
            Slot = position;
            CanSwap = side == Side.Party && health > 0;
            Artwork = art;
            CharacterName = character?.Name;
            this.maximumHealth = maximumHealth;
            this.statusIcon = statusIcon;
            this.statusTooltip = statusTooltip;
            this.battleSprite = battleSprite;
            sideColor = side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy;
            style.flexGrow = 1;
            style.flexBasis = 0;
            style.minWidth = 0;
            style.marginLeft = style.marginRight = 4;

            var life = new VisualElement { name = "health-bar", pickingMode = PickingMode.Ignore };
            life.style.position = Position.Absolute;
            life.style.left = life.style.right = Length.Percent(16);
            life.style.height = 17;
            life.style.overflow = Overflow.Hidden;
            life.style.backgroundColor = (Color)new Color32(34, 21, 25, 245);
            PrototypeStyle.Outline(life, new Color32(121, 54, 57, 255), 3);
            healthFill = new VisualElement { name = "health-fill", pickingMode = PickingMode.Ignore };
            healthFill.style.position = Position.Absolute;
            healthFill.style.top = healthFill.style.bottom = healthFill.style.left = 0;
            healthFill.style.backgroundColor = (Color)new Color32(133, 43, 49, 255);
            life.Add(healthFill);
            healthLabel = PrototypeStyle.Text("", 11, new Color32(255, 173, 163, 255));
            healthLabel.name = "health-value";
            healthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            healthLabel.style.whiteSpace = WhiteSpace.NoWrap;
            healthLabel.pickingMode = PickingMode.Ignore;
            healthLabel.style.flexGrow = 1;
            PrototypeStyle.Middle(healthLabel);
            life.Add(healthLabel);

            portrait = new VisualElement { name = "combatant-portrait" };
            portrait.style.height = 226;
            portrait.style.marginTop = portrait.style.marginBottom = 4;
            portrait.style.overflow = Overflow.Hidden;
            if (battleSprite == null)
            {
                portrait.style.backgroundColor = character == null ? PrototypeStyle.Inset
                    : Color.Lerp(PrototypeStyle.PanelColor, sideColor, 0.14f);
                PrototypeStyle.Outline(portrait, PrototypeStyle.Edge, 4);
            }
            Add(portrait);

            if (art == null && battleSprite == null)
            {
                var mark = PrototypeStyle.Text(hidden ? "?" : character == null ? "·" : ProfessionMark(character.Profession),
                    character == null ? 26 : 44, character == null ? PrototypeStyle.Edge : sideColor);
                mark.style.flexGrow = 1;
                PrototypeStyle.Middle(mark);
                portrait.Add(mark);
            }

            if (battleSprite != null)
            {
                portrait.Add(battleSprite);
                battleSprite.SetOverhead(life);
            }
            else
            {
                ProfessionFrame.AddTo(portrait, character?.Profession, art);
                if (maximumHealth > 0)
                {
                    life.style.top = 7;
                    portrait.Add(life);
                }
            }

            var positionTag = PrototypeStyle.Text(position.ToString("00"), 11, side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy);
            positionTag.style.position = Position.Absolute;
            positionTag.style.top = positionTag.style.left = 5;
            positionTag.style.backgroundColor = PrototypeStyle.Background;
            positionTag.style.paddingLeft = positionTag.style.paddingRight = 4;
            portrait.Add(positionTag);

            var caption = PrototypeStyle.Row("combatant-caption");
            caption.style.position = Position.Absolute;
            caption.style.left = caption.style.right = caption.style.bottom = 5;
            caption.style.paddingLeft = caption.style.paddingRight = 5;
            caption.style.paddingTop = caption.style.paddingBottom = 4;
            caption.style.minHeight = 27;
            caption.style.alignItems = Align.Center;
            caption.style.backgroundColor = new Color(0.064f, 0.069f, 0.086f, 0.88f);
            var characterName = PrototypeStyle.Text(hidden ? "未知" : character?.Name ?? "空位", 14);
            characterName.name = "combatant-name";
            characterName.style.flexGrow = 1;
            characterName.style.minWidth = 0;
            characterName.style.whiteSpace = WhiteSpace.NoWrap;
            characterName.style.overflow = Overflow.Hidden;
            characterName.style.textOverflow = TextOverflow.Ellipsis;
            caption.Add(characterName);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var personality = PrototypeStyle.Text(subtitle, 11, PrototypeStyle.Muted);
                personality.name = "combatant-subtitle";
                personality.style.flexShrink = 0;
                personality.style.marginLeft = 5;
                personality.style.whiteSpace = WhiteSpace.NoWrap;
                caption.Add(personality);
            }
            portrait.Add(caption);

            var conditions = new VisualElement { name = unitId == null ? "status-row-empty" : $"status-row-{unitId}" };
            conditions.style.minHeight = 29;
            conditions.style.marginTop = 3;
            Add(conditions);
            badges = PrototypeStyle.Row("status-badges");
            badges.style.flexWrap = Wrap.Wrap;
            badges.style.justifyContent = Justify.Center;
            conditions.Add(badges);

            Refresh(health, shield);
        }

        public void Refresh(int health, int shield, IReadOnlyList<StatusStack> statuses = null)
        {
            if (maximumHealth == 0)
                return;

            bool alive = health > 0;
            healthLabel.text = alive ? $"{health} / {maximumHealth}" : $"倒下  0 / {maximumHealth}";
            healthFill.style.width = Length.Percent(Mathf.Clamp(100f * health / maximumHealth, 0f, 100f));
            if (battleSprite != null)
                battleSprite.SetAlive(alive, Time.unscaledTimeAsDouble);
            else
                portrait.style.opacity = alive ? 1f : 0.55f;

            var active = new HashSet<string>();
            if (alive)
            {
                if (shield > 0)
                    UpdateBadge("shield", shield, "护盾", "先吸收受到的伤害。", null, PrototypeStyle.Ally, isShield: true);
                if (statuses != null)
                    foreach (var status in statuses)
                    {
                        var rule = StatusRules.For(status.Kind);
                        UpdateBadge(status.Kind.ToString(), status.Layers, rule.Name,
                            rule.Description, statusIcon?.Invoke(status.Kind), rule.Negative ? PrototypeStyle.Enemy : PrototypeStyle.Accent);
                    }
            }

            for (int index = badges.childCount - 1; index >= 0; index--)
                if (!active.Contains(badges[index].name))
                    badges.RemoveAt(index);

            void UpdateBadge(string id, int count, string title, string description, Texture2D icon, Color color, bool isShield = false)
            {
                string elementName = $"status-badge-{id}";
                active.Add(elementName);
                var badge = badges.Q<StatusBadge>(elementName);
                if (badge == null)
                {
                    badge = new StatusBadge(elementName, title, description, icon, color, isShield, statusTooltip);
                    badges.Add(badge);
                }
                badge.SetCount(count);
            }
        }

        private sealed class StatusBadge : VisualElement
        {
            private readonly Label stacks;
            private readonly string title;
            private readonly BattleStatusTooltip tooltip;
            private int count;

            public StatusBadge(string elementName, string title, string description, Texture2D icon,
                Color edge, bool shield, BattleStatusTooltip tooltip)
            {
                name = elementName;
                this.title = title;
                this.tooltip = tooltip;
                style.width = style.height = 27;
                style.marginLeft = style.marginRight = 2;
                style.marginBottom = 3;
                style.backgroundColor = PrototypeStyle.Inset;
                PrototypeStyle.Outline(this, edge, 4);
                Add(BattleStatusTooltip.CreateIcon(icon, title, edge, shield));

                stacks = PrototypeStyle.Text("", 10);
                stacks.name = "status-stack-count";
                stacks.style.position = Position.Absolute;
                stacks.style.bottom = stacks.style.right = -1;
                stacks.style.paddingLeft = stacks.style.paddingRight = 2;
                stacks.style.backgroundColor = PrototypeStyle.Background;
                stacks.pickingMode = PickingMode.Ignore;
                Add(stacks);

                RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                        tooltip?.Show(this, title, count, description, icon, edge, shield);
                });
                RegisterCallback<PointerLeaveEvent>(_ => tooltip?.Hide(this));
                RegisterCallback<DetachFromPanelEvent>(_ => tooltip?.Hide(this));
            }

            public void SetCount(int next)
            {
                if (count == next)
                    return;
                count = next;
                stacks.text = count.ToString();
                tooltip?.SetCount(this, title, count);
            }
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
