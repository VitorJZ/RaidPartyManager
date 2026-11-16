using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // The portrait mesh and the trim share the same inner contour at every size.
    internal static class ProfessionFrame
    {
        private static readonly Color Shadow = new Color32(22, 17, 20, 255);

        public static void AddTo(VisualElement portrait, Profession? profession, Texture2D art)
        {
            if (!profession.HasValue)
                return;

            if (art != null)
            {
                var artwork = new VisualElement { name = "profession-portrait-art", pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(artwork);
                artwork.generateVisualContent += context =>
                    DrawArtwork(context, artwork.contentRect.size, profession.Value, art);
                portrait.Add(artwork);
            }

            var frame = new VisualElement
            {
                name = $"profession-frame-{profession.Value.ToString().ToLowerInvariant()}",
                pickingMode = PickingMode.Ignore
            };
            PrototypeStyle.Fill(frame);
            frame.generateVisualContent += context => Draw(context, frame.contentRect.size, profession.Value);
            portrait.Add(frame);
        }

        private static void Draw(MeshGenerationContext context, Vector2 size, Profession profession)
        {
            if (size.x < 20 || size.y < 24)
                return;

            var (metal, light) = profession switch
            {
                Profession.Warrior => (new Color32(184, 105, 76, 255), new Color32(255, 183, 133, 255)),
                Profession.Mage => (new Color32(138, 111, 191, 255), new Color32(224, 198, 255, 255)),
                Profession.Archer => (new Color32(91, 151, 115, 255), new Color32(192, 231, 163, 255)),
                Profession.Support => (new Color32(188, 149, 85, 255), new Color32(255, 235, 183, 255)),
                _ => (new Color32(109, 105, 101, 255), new Color32(195, 188, 173, 255))
            };

            var pen = context.painter2D;
            float scale = FrameScale(size.x);
            var outer = Contour(size, 4f * scale, scale, profession);
            var inner = Contour(size, InnerInset(scale), scale, profession);
            Outline(pen, outer, Shadow, 7f * scale);
            Outline(pen, outer, metal, 3.4f * scale);
            Outline(pen, inner, light, 1.0f * scale);

            CornerMarks(pen, size, scale, profession, metal, light);
            Crest(pen, new Vector2(size.x / 2f, 10f * scale), scale, profession, metal, light);
        }

        private static float FrameScale(float width) => Mathf.Clamp(width / 145f, 0.45f, 1.25f);

        private static float InnerInset(float scale) => 7.8f * scale;

        private static Vector2[] Contour(Vector2 size, float inset, float scale, Profession profession)
        {
            float corner = scale * (profession switch
            {
                Profession.Mage => 9f,
                Profession.Archer => 10f,
                Profession.Support => 7f,
                _ => 5f
            });
            float right = size.x - inset;
            float bottom = size.y - inset;
            // Quadratic corners are sampled here so the textured mesh and border use identical points.
            int steps = profession == Profession.Support ? 6 : 1;
            var points = new Vector2[4 * (steps + 1)];
            int next = 0;
            AddCorner(new Vector2(inset, inset + corner), new Vector2(inset, inset), new Vector2(inset + corner, inset));
            AddCorner(new Vector2(right - corner, inset), new Vector2(right, inset), new Vector2(right, inset + corner));
            AddCorner(new Vector2(right, bottom - corner), new Vector2(right, bottom), new Vector2(right - corner, bottom));
            AddCorner(new Vector2(inset + corner, bottom), new Vector2(inset, bottom), new Vector2(inset, bottom - corner));
            return points;

            void AddCorner(Vector2 start, Vector2 control, Vector2 end)
            {
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    points[next++] = Vector2.Lerp(Vector2.Lerp(start, control, t), Vector2.Lerp(control, end, t), t);
                }
            }
        }

        private static void DrawArtwork(MeshGenerationContext context, Vector2 size, Profession profession, Texture2D art)
        {
            if (size.x < 20 || size.y < 24)
                return;

            float scale = FrameScale(size.x);
            float inset = InnerInset(scale);
            var points = Contour(size, inset, scale, profession);
            var mesh = context.Allocate(points.Length + 1, points.Length * 3, art);
            if (mesh.vertexCount == 0)
                return;

            // Preserve a centered cover crop. The triangles stop at the frame's inner line.
            Vector2 center = size / 2f;
            float zoom = Mathf.Max((size.x - 2f * inset) / art.width, (size.y - 2f * inset) / art.height);
            var imageSize = new Vector2(art.width * zoom, art.height * zoom);
            mesh.SetNextVertex(VertexAt(center));
            foreach (var point in points)
                mesh.SetNextVertex(VertexAt(point));
            for (int index = 0; index < points.Length; index++)
            {
                mesh.SetNextIndex(0);
                mesh.SetNextIndex((ushort)(index + 1));
                mesh.SetNextIndex((ushort)((index + 1) % points.Length + 1));
            }

            Vertex VertexAt(Vector2 point) => new Vertex
            {
                position = new Vector3(point.x, point.y, Vertex.nearZ),
                tint = Color.white,
                uv = new Vector2(0.5f + (point.x - center.x) / imageSize.x, 0.5f - (point.y - center.y) / imageSize.y)
            };
        }

        private static void Outline(Painter2D pen, Vector2[] points, Color color, float width)
        {
            pen.strokeColor = color;
            pen.lineWidth = width;
            pen.BeginPath();
            pen.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                pen.LineTo(points[i]);
            pen.ClosePath();
            pen.Stroke();
        }

        private static void CornerMarks(Painter2D pen, Vector2 size, float scale, Profession profession,
            Color metal, Color light)
        {
            for (int i = 0; i < 4; i++)
            {
                bool right = i % 2 == 1;
                bool bottom = i >= 2;
                var origin = new Vector2(right ? size.x : 0f, bottom ? size.y : 0f);
                Vector2 Point(float x, float y) => origin + new Vector2(
                    x * scale * (right ? -1 : 1), y * scale * (bottom ? -1 : 1));

                switch (profession)
                {
                    case Profession.Warrior:
                        Shape(pen, Shadow, metal, scale, Point(1, 12), Point(12, 1), Point(26, 1),
                            Point(26, 7), Point(15, 7), Point(7, 15), Point(7, 26), Point(1, 26));
                        Shape(pen, light, metal, scale / 2, Point(9, 9), Point(12, 9), Point(12, 12), Point(9, 12));
                        break;
                    case Profession.Mage:
                        Shape(pen, Shadow, light, scale, Point(13, 5), Point(21, 13), Point(13, 21), Point(5, 13));
                        Shape(pen, metal, light, scale / 2, Point(13, 9), Point(17, 13), Point(13, 17), Point(9, 13));
                        break;
                    case Profession.Archer:
                        Shape(pen, metal, Lightened(metal, light), scale, Point(7, 18), Point(10, 8), Point(22, 4), Point(17, 13));
                        Shape(pen, metal, Lightened(metal, light), scale, Point(5, 27), Point(4, 19), Point(13, 13), Point(10, 22));
                        break;
                    case Profession.Support:
                        Shape(pen, Shadow, light, scale, Point(12, 5), Point(14, 10), Point(19, 12),
                            Point(14, 14), Point(12, 19), Point(10, 14), Point(5, 12), Point(10, 10));
                        break;
                }
            }
        }

        private static Color Lightened(Color metal, Color light) => Color.Lerp(metal, light, 0.74f);

        private static void Crest(Painter2D pen, Vector2 center, float scale, Profession profession, Color metal, Color light)
        {
            Vector2 Point(float x, float y) => center + new Vector2(x, y) * scale;
            switch (profession)
            {
                case Profession.Warrior:
                    Shape(pen, Shadow, light, scale, Point(-11, -7), Point(11, -7), Point(9, 4), Point(0, 10), Point(-9, 4));
                    Shape(pen, metal, metal, scale, Point(-4, -3), Point(4, -3), Point(0, 4));
                    break;
                case Profession.Mage:
                    Shape(pen, Shadow, light, scale, Point(0, -8), Point(12, 0), Point(0, 8), Point(-12, 0));
                    Shape(pen, metal, light, scale / 2, Point(0, -4), Point(5, 0), Point(0, 4), Point(-5, 0));
                    break;
                case Profession.Archer:
                    Shape(pen, Shadow, light, scale, Point(0, -8), Point(12, 5), Point(0, 2), Point(-12, 5));
                    break;
                case Profession.Support:
                    Shape(pen, Shadow, light, scale, Point(0, -8), Point(4, -3), Point(11, 0), Point(4, 3),
                        Point(0, 8), Point(-4, 3), Point(-11, 0), Point(-4, -3));
                    break;
            }
        }

        private static void Shape(Painter2D pen, Color fill, Color stroke, float width, params Vector2[] points)
        {
            pen.fillColor = fill;
            pen.strokeColor = stroke;
            pen.lineWidth = width;
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
