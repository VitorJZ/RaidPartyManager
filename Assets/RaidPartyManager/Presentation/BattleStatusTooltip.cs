using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleStatusTooltip
    {
        private readonly VisualElement root;
        private readonly VisualElement panel;
        private readonly VisualElement artwork;
        private readonly Label title;
        private readonly Label description;
        private VisualElement anchor;

        public BattleStatusTooltip(VisualElement root)
        {
            this.root = root;
            panel = PrototypeStyle.Panel();
            panel.name = "battle-status-tooltip";
            panel.style.position = Position.Absolute;
            panel.style.marginTop = 0;
            panel.pickingMode = PickingMode.Ignore;
            panel.RegisterCallback<GeometryChangedEvent>(_ => PositionPanel());

            artwork = new VisualElement { name = "battle-status-tooltip-art", pickingMode = PickingMode.Ignore };
            artwork.style.width = artwork.style.height = 76;
            artwork.style.alignSelf = Align.Center;
            artwork.style.backgroundColor = PrototypeStyle.Inset;
            panel.Add(artwork);

            title = PrototypeStyle.Text("", 16);
            title.name = "battle-status-tooltip-name";
            title.style.marginTop = 7;
            title.pickingMode = PickingMode.Ignore;
            panel.Add(title);

            description = PrototypeStyle.Text("", 13);
            description.name = "battle-status-tooltip-description";
            description.style.marginTop = 4;
            description.pickingMode = PickingMode.Ignore;
            panel.Add(description);
        }

        public void Show(VisualElement badge, string status, int count, string effect, Texture2D icon, Color color, bool shield,
            BattleCountdownKind? countdown = null, string unit = null)
        {
            anchor = badge;
            title.style.color = color;
            SetCount(badge, status, count, unit);
            description.text = effect;
            PrototypeStyle.Outline(panel, color, 6);
            PrototypeStyle.Outline(artwork, PrototypeStyle.Edge, 4);
            artwork.Clear();
            artwork.Add(CreateIcon(icon, status, color, shield, countdown));
            panel.style.width = Mathf.Clamp(root.layout.width - 12f, 0f, 224f);
            if (panel.parent == null)
                root.Add(panel);
            else
                panel.BringToFront();
            PositionPanel();
        }

        public void SetCount(VisualElement badge, string status, int count, string unit)
        {
            if (anchor == badge)
                title.text = unit == null ? $"{status} ×{count}" : $"{status} · 剩余 {count} {unit}";
        }

        public void Hide(VisualElement badge = null)
        {
            if (badge != null && anchor != badge)
                return;
            anchor = null;
            panel.RemoveFromHierarchy();
        }

        private void PositionPanel()
        {
            if (anchor == null)
                return;

            const float margin = 6f;
            const float gap = 7f;
            var bounds = root.WorldToLocal(anchor.worldBound);
            float width = panel.style.width.value.value;
            float height = float.IsNaN(panel.layout.height) ? 0f : panel.layout.height;
            float below = bounds.yMax + gap;
            float top = below + height <= root.layout.height - margin
                ? below : bounds.yMin - gap - height;
            panel.style.left = Mathf.Clamp(bounds.center.x - width * 0.5f, margin, Mathf.Max(margin, root.layout.width - width - margin));
            panel.style.top = Mathf.Clamp(top, margin, Mathf.Max(margin, root.layout.height - height - margin));
        }

        internal static VisualElement CreateIcon(Texture2D art, string title, Color color, bool shield,
            BattleCountdownKind? countdown = null)
        {
            if (countdown.HasValue)
                return new BattleCountdownIcon(countdown.Value, color);
            if (art != null)
                return new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore,
                    style = { flexGrow = 1 } };

            if (!shield)
            {
                var marker = PrototypeStyle.Text(title.Substring(0, 1), 13, color);
                marker.pickingMode = PickingMode.Ignore;
                marker.style.flexGrow = 1;
                PrototypeStyle.Middle(marker);
                return marker;
            }

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.flexGrow = 1;
            icon.generateVisualContent += context =>
            {
                var size = icon.contentRect.size;
                float unit = Mathf.Min(size.x, size.y);
                var center = size * 0.5f;
                var pen = context.painter2D;
                pen.lineWidth = Mathf.Max(1f, unit * 0.035f);
                pen.strokeColor = color;
                pen.fillColor = Color.Lerp(color, PrototypeStyle.Background, 0.65f);
                pen.BeginPath();
                pen.MoveTo(center + new Vector2(-0.28f, -0.30f) * unit);
                pen.LineTo(center + new Vector2(0.28f, -0.30f) * unit);
                pen.LineTo(center + new Vector2(0.23f, 0.10f) * unit);
                pen.LineTo(center + new Vector2(0f, 0.33f) * unit);
                pen.LineTo(center + new Vector2(-0.23f, 0.10f) * unit);
                pen.ClosePath();
                pen.Fill();
                pen.Stroke();
            };
            return icon;
        }
    }
}
