using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EmployeeAttributeChart : VisualElement
    {
        private const int Levels = 5;
        private const float CaptionWidth = 53;
        private const float CaptionHeight = 30;
        private const float SideGap = 4;
        private const float Horizontal = .8660254f;
        private static readonly Vector2[] Directions = {
            Vector2.up * -1, new(Horizontal, -.5f), new(Horizontal, .5f),
            Vector2.up, new(-Horizontal, .5f), new(-Horizontal, -.5f)
        };
        private readonly (string Key, string Title, int Value, string Bonus)[] axes;
        private readonly VisualElement plot;
        private readonly VisualElement[] captions = new VisualElement[Directions.Length];

        internal EmployeeAttributeChart(EmployeeAttributes ratings)
        {
            name = "employee-attributes";
            style.marginTop = 8;
            style.paddingTop = 7;
            style.flexShrink = 0;
            style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(this, PrototypeStyle.Edge, 5);

            var title = PrototypeStyle.Text("员工属性", 13, PrototypeStyle.Accent);
            PrototypeStyle.Middle(title);
            Add(title);

            axes = new[] {
                ("loyalty", "忠诚程度", ratings.Loyalty, "最大生命 +3"),
                ("obedience", "战术服从", ratings.Obedience, "特防 +1"),
                ("pay-preference", "薪酬偏好", ratings.PayPreference, "能量上限 +1"),
                ("playing-time", "上场需求", ratings.PlayingTime, "装备栏 +1"),
                ("satisfaction", "满意程度", ratings.Satisfaction, "攻击 +1"),
                ("hardiness", "吃苦耐劳", ratings.Hardiness, "防御 +1")
            };
            plot = new VisualElement { name = "employee-attribute-radar" };
            plot.style.height = 146;
            plot.generateVisualContent += DrawChart;
            plot.RegisterCallback<GeometryChangedEvent>(_ => PlaceCaptions());
            Add(plot);

            for (int i = 0; i < axes.Length; i++)
            {
                var axis = axes[i];
                var caption = new VisualElement { tooltip = $"{axis.Title}达到 5 点时，{axis.Bonus}" };
                caption.style.position = Position.Absolute;
                caption.style.width = CaptionWidth;
                caption.style.height = CaptionHeight;
                var label = PrototypeStyle.Text(axis.Title, 10, PrototypeStyle.Ink);
                var level = PrototypeStyle.Text(axis.Value.ToString(), 14, PrototypeStyle.Accent);
                level.name = "employee-rating-" + axis.Key;
                level.style.unityFontStyleAndWeight = FontStyle.Bold;
                PrototypeStyle.Middle(label);
                PrototypeStyle.Middle(level);
                caption.Add(label);
                caption.Add(level);
                plot.Add(caption);
                captions[i] = caption;
            }
        }

        private static float Radius(Rect rect) => Mathf.Max(0, Mathf.Min(
            (rect.width * .5f - CaptionWidth - SideGap) / Horizontal, rect.height * .5f - CaptionHeight - 3));

        private void PlaceCaptions()
        {
            var rect = plot.contentRect;
            float radius = Radius(rect);
            for (int i = 0; i < Directions.Length; i++)
            {
                var direction = Directions[i];
                var centre = rect.center + direction * radius;
                if (direction.x == 0)
                    centre.y += direction.y * (CaptionHeight * .5f + SideGap);
                else
                    centre.x += Mathf.Sign(direction.x) * (CaptionWidth * .5f + SideGap);
                captions[i].style.left = centre.x - CaptionWidth * .5f;
                captions[i].style.top = centre.y - CaptionHeight * .5f;
            }
        }

        private void DrawChart(MeshGenerationContext context)
        {
            var rect = plot.contentRect;
            float radius = Radius(rect);
            if (radius <= 0) return;
            var pen = context.painter2D;
            pen.lineWidth = 1f;
            for (int ring = 1; ring <= Levels; ring++)
            {
                Hexagon(ring / (float)Levels);
                pen.strokeColor = ring == Levels ? PrototypeStyle.Muted : PrototypeStyle.Edge;
                pen.Stroke();
            }

            pen.BeginPath();
            foreach (var direction in Directions)
            {
                pen.MoveTo(rect.center);
                pen.LineTo(rect.center + direction * radius);
            }
            pen.strokeColor = PrototypeStyle.Edge;
            pen.Stroke();

            Hexagon(0, true);
            pen.fillColor = new Color(PrototypeStyle.Accent.r, PrototypeStyle.Accent.g, PrototypeStyle.Accent.b, .28f);
            pen.Fill();
            pen.strokeColor = PrototypeStyle.Accent;
            pen.lineWidth = 1.5f;
            pen.Stroke();

            void Hexagon(float scale, bool useRatings = false)
            {
                pen.BeginPath();
                for (int i = 0; i < Directions.Length; i++)
                {
                    float amount = useRatings ? axes[i].Value / (float)Levels : scale;
                    var point = rect.center + Directions[i] * (radius * amount);
                    if (i == 0) pen.MoveTo(point);
                    else pen.LineTo(point);
                }
                pen.ClosePath();
            }
        }
    }
}
