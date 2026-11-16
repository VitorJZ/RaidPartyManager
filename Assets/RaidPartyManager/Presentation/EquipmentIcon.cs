using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EquipmentIcon : VisualElement
    {
        public EquipmentIcon(EquipmentDefinition item, Texture2D artwork = null)
        {
            name = "equipment-icon";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            if (artwork == null)
                generateVisualContent += context => Paint(context.painter2D, contentRect, item);
            else
            {
                var image = new Image { image = artwork, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.flexGrow = 1;
                Add(image);
            }
        }

        private static void Paint(Painter2D pen, Rect area, EquipmentDefinition item)
        {
            float unit = Mathf.Min(area.width, area.height) / 75f;
            if (unit <= 0f)
                return;

            Vector2 Point(float x, float y) => area.center + new Vector2(x, y) * unit;
            if (item == null)
                return;

            bool mark = item.Bonus.Attack > 0;
            var accent = mark ? new Color32(223, 121, 94, 255) : new Color32(153, 194, 208, 255);
            var metal = mark ? new Color32(107, 48, 43, 255) : new Color32(58, 89, 110, 255);
            var dark = new Color32(20, 22, 28, 255);
            pen.lineWidth = 2f * unit;
            Polygon(pen, dark, metal, Point(-29, 0), Point(-19, -24), Point(19, -24), Point(29, 0),
                Point(19, 24), Point(-19, 24));
            if (mark)
            {
                Polygon(pen, metal, accent, Point(0, -27), Point(21, -8), Point(17, 17), Point(0, 27),
                    Point(-17, 17), Point(-21, -8));
                Polygon(pen, accent, accent, Point(-7, 14), Point(-2, 3), Point(-10, 1), Point(7, -17),
                    Point(2, -3), Point(10, -2));
            }
            else
            {
                Polygon(pen, metal, accent, Point(-19, -19), Point(19, -19), Point(16, 9), Point(8, 19), Point(0, 24),
                    Point(-8, 19), Point(-16, 9));
                Polygon(pen, accent, accent, Point(0, -13), Point(7, -2), Point(0, 10), Point(-7, -2));
            }
        }

        private static void Polygon(Painter2D pen, Color fill, Color edge, params Vector2[] points)
        {
            pen.fillColor = fill;
            pen.strokeColor = edge;
            pen.BeginPath();
            pen.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                pen.LineTo(points[i]);
            pen.ClosePath();
            pen.Fill();
            pen.Stroke();
        }
    }
}
