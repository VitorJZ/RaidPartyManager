using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class GoldRewardIcon : VisualElement
    {
        public GoldRewardIcon()
        {
            name = "reward-gold-icon";
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var pen = context.painter2D;
            float unit = Mathf.Min(contentRect.width, contentRect.height) / 80f;
            var center = contentRect.center;
            pen.lineWidth = 2f * unit;
            pen.strokeColor = new Color32(121, 75, 36, 255);
            Oval(new Vector2(-11, 20), 22, 9, new Color32(185, 119, 51, 255));
            Oval(new Vector2(-11, 15), 22, 9, new Color32(242, 182, 72, 255));
            Oval(new Vector2(10, 14), 22, 9, new Color32(167, 99, 39, 255));
            Oval(new Vector2(10, 9), 22, 9, new Color32(243, 183, 75, 255));
            Oval(new Vector2(2, -7), 21, 22, new Color32(180, 112, 45, 255));
            Oval(new Vector2(-2, -7), 21, 22, new Color32(254, 198, 84, 255));
            Oval(new Vector2(-2, -7), 15, 16, new Color32(226, 156, 52, 255));
            pen.fillColor = new Color32(255, 227, 147, 255);
            pen.BeginPath();
            pen.MoveTo(center + new Vector2(-2, -18) * unit);
            pen.LineTo(center + new Vector2(6, -7) * unit);
            pen.LineTo(center + new Vector2(-2, 4) * unit);
            pen.LineTo(center + new Vector2(-10, -7) * unit);
            pen.ClosePath();
            pen.Fill();

            void Oval(Vector2 offset, float width, float height, Color color)
            {
                pen.fillColor = color;
                pen.BeginPath();
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * Mathf.PI / 12f;
                    var point = center + (offset + new Vector2(Mathf.Cos(angle) * width, Mathf.Sin(angle) * height)) * unit;
                    if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
                }
                pen.ClosePath();
                pen.Fill();
                pen.Stroke();
            }
        }
    }
}
