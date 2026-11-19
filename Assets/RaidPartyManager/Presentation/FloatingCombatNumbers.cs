using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class FloatingCombatNumbers : VisualElement
    {
        private const double Duration = 0.5;
        private readonly double startedAt;
        private float width;
        private readonly float verticalOffset;
        private Vector2? origin;

        public string TargetId { get; }

        public FloatingCombatNumbers(BattleEvent entry, int healed, Rect body, double now, int lane)
        {
            TargetId = entry.TargetId;
            startedAt = now;
            verticalOffset = lane * 27;
            CaptureOrigin(body);
            name = "floating-combat-numbers";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.flexDirection = FlexDirection.Row;
            style.height = 30;
            style.opacity = 1;

            if (healed > 0)
                AddNumber("+" + healed, "healing", new Color32(116, 255, 125, 255));
            else if (entry.PoisonDamage)
                AddNumber("−" + entry.Amount, "poison", new Color32(188, 230, 62, 255));
            else
            {
                foreach (var group in entry.DamageTaken.GroupBy(part => part.Type))
                {
                    var (kind, color) = group.Key switch
                    {
                        DamageType.Physical => ("physical", new Color32(255, 88, 90, 255)),
                        DamageType.Magic => ("magic", new Color32(111, 207, 255, 255)),
                        DamageType.Fire => ("fire", new Color32(255, 158, 63, 255)),
                        DamageType.Lightning => ("lightning", new Color32(203, 142, 255, 255)),
                        _ => ("other", new Color32(255, 244, 221, 255))
                    };
                    AddNumber("−" + group.Sum(part => part.Amount), kind, color);
                }
            }
            style.width = width;

            void AddNumber(string value, string kind, Color color)
            {
                var label = PrototypeStyle.Text(value, 25, color);
                label.name = "combat-number";
                label.AddToClassList("combat-number-" + kind);
                label.pickingMode = PickingMode.Ignore;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.textShadow = new TextShadow { color = new Color(0, 0, 0, 0.95f), offset = new Vector2(1, 2), blurRadius = 4 };
                label.style.width = 10 + value.Length * 16;
                label.style.flexShrink = 0;
                PrototypeStyle.Middle(label);
                Add(label);
                width += label.style.width.value.value;
            }
        }

        public bool ActiveAt(double now) => now < startedAt + Duration;
        public bool RecentlyStarted(double now) => now < startedAt + 0.15;

        public void Draw(VisualElement layer, Rect body, double now)
        {
            CaptureOrigin(body);
            if (!origin.HasValue || !(layer.layout.width > 0f))
                return;
            if (parent != layer)
                layer.Add(this);

            var anchor = layer.WorldToLocal(origin.Value);
            float progress = Mathf.Clamp01((float)((now - startedAt) / Duration));
            style.left = Mathf.Clamp(anchor.x - width * 0.5f, 0, Mathf.Max(0, layer.layout.width - width));
            style.top = Mathf.Max(0, anchor.y - 23f - verticalOffset - progress * 30f);
            style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, progress));
        }

        private void CaptureOrigin(Rect body)
        {
            if (!origin.HasValue && body.width > 0 && body.height > 0)
                origin = new Vector2(body.center.x, body.yMin);
        }
    }
}
