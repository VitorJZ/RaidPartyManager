using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EquippedItemBorder : VisualElement
    {
        private readonly Color light;

        public EquippedItemBorder(Color color)
        {
            name = "equipped-item-border";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            PrototypeStyle.Outline(this, color, 5);
            style.borderTopWidth = style.borderRightWidth = style.borderBottomWidth = style.borderLeftWidth = 2;
            light = Color.Lerp(color, Color.white, .45f);
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
            float head = (float)(Time.unscaledTimeAsDouble % 2.4 / 2.4) * perimeter;
            float segment = perimeter * 0.015f;
            for (int index = 8; index >= 0; index--)
            {
                float at = head - index * segment;
                var color = light;
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
