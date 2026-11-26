using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleUnitCard : VisualElement
    {
        private int maximumHealth;
        private readonly Label healthLabel, characterName;
        private readonly VisualElement healthFill;
        private readonly VisualElement portrait;
        private readonly VisualElement badges, caption, life, conditions;
        private readonly Func<StatusKind, Texture2D> statusIcon;
        private readonly BattleStatusTooltip statusTooltip;
        private readonly BattleSpriteView battleSprite;
        private readonly Color sideColor;

        public Rect BodyBounds => battleSprite?.Figure.worldBound ?? portrait.worldBound;
        private readonly float partyInkSpan;
        public Rect CaptionBounds => characterName.worldBound;
        public float PartyBodyHeight => (battleSprite?.Figure.layout.height ?? portrait.layout.height) * partyInkSpan;
        private const float BadgeSize = 27, BadgeRow = 30;
        public int PositionSlots { get; }

        public BattleUnitCard(Side side, string unitId, CharacterDefinition character,
            Texture2D art, int maximumHealth, int health, int shield, double now, float portraitHeight,
            Func<StatusKind, Texture2D> statusIcon = null, BattleSpriteView battleSprite = null,
            BattleStatusTooltip statusTooltip = null)
        {
            PositionSlots = character.PositionSlots;
            this.maximumHealth = maximumHealth;
            this.statusIcon = statusIcon;
            this.statusTooltip = statusTooltip;
            this.battleSprite = battleSprite;
            var framing = ShopStage.FramingFor(character.RosterNumber);
            partyInkSpan = side == Side.Party ? battleSprite?.UsesPortrait == false ? framing.sideSpan : 1f : 0f;
            float crownInset = side == Side.Party && battleSprite?.UsesPortrait == false
                ? 1f - framing.sideInset - framing.sideSpan : .045f;
            sideColor = side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy;

            life = new VisualElement { name = "health-bar", pickingMode = PickingMode.Ignore };
            life.style.width = Length.Percent(76);
            life.style.maxWidth = PositionSlots switch { 1 => 104, 2 => 150, _ => 188 };
            life.style.alignSelf = Align.Center;
            life.style.height = 17;
            life.style.visibility = maximumHealth > 0 ? Visibility.Visible : Visibility.Hidden;
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
            portrait.style.height = portraitHeight;
            portrait.style.marginTop = 0;
            portrait.style.marginBottom = 3;
            portrait.style.overflow = Overflow.Hidden;
            if (battleSprite == null)
            {
                portrait.style.backgroundColor = Color.Lerp(PrototypeStyle.PanelColor, sideColor, 0.14f);
                PrototypeStyle.Outline(portrait, PrototypeStyle.Edge, 4);
            }

            if (art == null && battleSprite == null)
            {
                var mark = PrototypeStyle.Text(ProfessionMark(character.Profession), 44, sideColor);
                mark.style.flexGrow = 1;
                PrototypeStyle.Middle(mark);
                portrait.Add(mark);
            }

            if (battleSprite != null)
                portrait.Add(battleSprite);
            else
                ProfessionFrame.AddTo(portrait, character.Profession, art);

            caption = PrototypeStyle.Row("combatant-caption");
            caption.style.paddingLeft = caption.style.paddingRight = 5;
            caption.style.paddingTop = caption.style.paddingBottom = 0;
            caption.style.height = 20;
            caption.style.alignItems = Align.Center;
            caption.style.justifyContent = Justify.Center;
            characterName = PrototypeStyle.Text(character.Name, 14);
            characterName.name = "combatant-name";
            characterName.style.maxWidth = Length.Percent(100);
            characterName.style.minWidth = 0;
            PrototypeStyle.Middle(characterName);
            characterName.style.whiteSpace = WhiteSpace.NoWrap;
            characterName.style.overflow = Overflow.Hidden;
            characterName.style.textOverflow = TextOverflow.Ellipsis;
            characterName.style.textShadow = new TextShadow { offset = new Vector2(0, 1.5f), blurRadius = 4, color = Color.black };
            caption.Add(characterName);
            Add(caption);
            Add(portrait);
            Add(life);

            conditions = new VisualElement { name = $"status-row-{unitId}" };
            conditions.style.height = conditions.style.minHeight = 2 * BadgeRow;
            conditions.style.flexShrink = 0;
            conditions.style.marginTop = 3;
            Add(conditions);
            badges = PrototypeStyle.Row("status-badges");
            badges.style.flexWrap = Wrap.Wrap;
            badges.style.justifyContent = Justify.Center;
            conditions.Add(badges);

            conditions.RegisterCallback<GeometryChangedEvent>(_ => FitBadges());
            if (battleSprite != null)
            {
                var figure = battleSprite.Figure;
                RegisterCallback<AttachToPanelEvent>(_ => figure.RegisterCallback<GeometryChangedEvent>(AlignName));
                RegisterCallback<DetachFromPanelEvent>(_ => figure.UnregisterCallback<GeometryChangedEvent>(AlignName));
                portrait.RegisterCallback<GeometryChangedEvent>(AlignName);
                void AlignName(GeometryChangedEvent _)
                {
                    if (figure.layout.height <= 0 || float.IsNaN(figure.layout.height)) return;
                    // Small units share the boss's foot line; their names follow their own idle figure.
                    float inset = battleSprite.layout.y + figure.layout.y + figure.layout.height * crownInset;
                    caption.style.translate = new Translate(0, Mathf.Max(0, inset - 1));
                }
            }
            Refresh(health, shield, now);
        }

        // Hovering the actor on stage opens their details, so the hit area covers the figure's slot.
        public VisualElement AddInspectionTarget(string elementName)
        {
            portrait.Q(elementName)?.RemoveFromHierarchy();
            var target = new VisualElement { name = elementName, pickingMode = PickingMode.Position };
            target.style.position = Position.Absolute;
            target.style.left = target.style.right = target.style.top = target.style.bottom = 0;
            portrait.Add(target);
            return target;
        }

        public void SetEntranceHudOpacity(float opacity)
        {
            caption.style.opacity = life.style.opacity = conditions.style.opacity = opacity;
        }

        public void SetEntranceVisible(bool visible)
        {
            if (battleSprite != null)
                battleSprite.SetBodyVisible(visible);
            else
                portrait.visible = visible;
        }

        public void Refresh(int health, int shield, double now, IReadOnlyList<StatusStack> statuses = null,
            IReadOnlyDictionary<BattleCountdownKind, int> countdowns = null, int updatedMaximum = 0)
        {
            if (updatedMaximum > 0) maximumHealth = updatedMaximum;
            if (maximumHealth == 0)
                return;

            bool alive = health > 0;
            healthLabel.text = alive ? $"{health} / {maximumHealth}" : $"倒下  0 / {maximumHealth}";
            healthFill.style.width = Length.Percent(Mathf.Clamp(100f * health / maximumHealth, 0f, 100f));
            if (battleSprite != null)
                battleSprite.SetAlive(alive, now);
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
                if (countdowns != null)
                    foreach (var timer in countdowns)
                    {
                        var info = BattleCountdownIcon.Details(timer.Key);
                        UpdateBadge("countdown-" + timer.Key, timer.Value, info.Name, info.Description, null, info.Color,
                            countdown: timer.Key, unit: info.Unit);
                    }
            }

            for (int index = badges.childCount - 1; index >= 0; index--)
                if (!active.Contains(badges[index].name))
                    badges.RemoveAt(index);

            FitBadges();

            void UpdateBadge(string id, int count, string title, string description, Texture2D icon, Color color, bool isShield = false,
                BattleCountdownKind? countdown = null, string unit = null)
            {
                string elementName = $"status-badge-{id}";
                active.Add(elementName);
                var badge = badges.Q<StatusBadge>(elementName);
                if (badge == null)
                {
                    badge = new StatusBadge(elementName, title, description, icon, color, isShield, statusTooltip, countdown, unit);
                    badges.Add(badge);
                }
                badge.SetCount(count);
            }
        }

        private void FitBadges()
        {
            if (badges.childCount == 0 || !(conditions.layout.width > 0)) return;
            int perRow = (badges.childCount + 1) / 2;
            float size = Mathf.Min(BadgeSize, Mathf.Max(1, Mathf.Floor(conditions.layout.width / perRow) - 4));
            foreach (var badge in badges.Children())
                badge.style.width = badge.style.height = size;
        }

        private sealed class StatusBadge : VisualElement
        {
            private readonly Label stacks;
            private readonly string title;
            private readonly string unit;
            private readonly BattleStatusTooltip tooltip;
            private int count;

            public StatusBadge(string elementName, string title, string description, Texture2D icon,
                Color edge, bool shield, BattleStatusTooltip tooltip, BattleCountdownKind? countdown, string unit)
            {
                name = elementName;
                this.title = title;
                this.unit = unit;
                this.tooltip = tooltip;
                style.width = style.height = 27;
                style.marginLeft = style.marginRight = 2;
                style.marginBottom = 3;
                style.backgroundColor = PrototypeStyle.Inset;
                PrototypeStyle.Outline(this, edge, 4);
                Add(BattleStatusTooltip.CreateIcon(icon, title, edge, shield, countdown));

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
                        tooltip?.Show(this, title, count, description, icon, edge, shield, countdown, unit);
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
                tooltip?.SetCount(this, title, count, unit);
            }
        }

        private static string ProfessionMark(Profession profession) => profession switch
        {
            Profession.Warrior => "战",
            Profession.Mage => "术",
            Profession.Archer => "弓",
            Profession.Support => "援",
            Profession.Monster => "怪",
            _ => "·"
        };
    }
}
