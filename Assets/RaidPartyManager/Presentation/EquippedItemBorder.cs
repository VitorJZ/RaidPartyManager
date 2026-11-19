using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EquippedItemBorder : VisualElement
    {
        private static readonly Color Edge = new Color32(79, 159, 161, 228);
        private static readonly Color Light = new Color32(222, 255, 238, 255);

        public EquippedItemBorder()
        {
            name = "equipped-item-border";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            generateVisualContent += Paint;
            schedule.Execute(MarkDirtyRepaint).Every(33);
        }

        private void Paint(MeshGenerationContext context)
        {
            var bounds = contentRect;
            if (bounds.width < 5 || bounds.height < 5)
                return;

            float left = bounds.xMin + 1.5f;
            float right = bounds.xMax - 1.5f;
            float top = bounds.yMin + 1.5f;
            float bottom = bounds.yMax - 1.5f;
            float width = right - left;
            float height = bottom - top;
            float perimeter = 2 * (width + height);
            var pen = context.painter2D;
            pen.strokeColor = Edge;
            pen.lineWidth = 1.5f;
            pen.BeginPath();
            pen.MoveTo(new Vector2(left, top));
            pen.LineTo(new Vector2(right, top));
            pen.LineTo(new Vector2(right, bottom));
            pen.LineTo(new Vector2(left, bottom));
            pen.ClosePath();
            pen.Stroke();

            float head = (float)(Time.unscaledTimeAsDouble % 2.4 / 2.4) * perimeter;
            float segment = perimeter * 0.015f;
            for (int index = 8; index >= 0; index--)
            {
                float at = head - index * segment;
                var color = Light;
                color.a *= 1 - index / 9f;
                pen.strokeColor = color;
                pen.lineWidth = Mathf.Lerp(2.8f, 1.25f, index / 8f);
                pen.BeginPath();
                pen.MoveTo(Point(at));
                pen.LineTo(Point(at + segment));
                pen.Stroke();
            }

            Vector2 Point(float distance)
            {
                distance = Mathf.Repeat(distance, perimeter);
                if (distance < width) return new Vector2(left + distance, top);
                distance -= width;
                if (distance < height) return new Vector2(right, top + distance);
                distance -= height;
                if (distance < width) return new Vector2(right - distance, bottom);
                return new Vector2(left, bottom - (distance - width));
            }
        }
    }
}
