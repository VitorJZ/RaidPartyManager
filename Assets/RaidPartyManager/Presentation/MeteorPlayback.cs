using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class MeteorPlayback
    {
        public const float FallDuration = 0.72f;
        public const float BreakDuration = 0.72f;
        private static readonly Vector2[] FragmentDirections =
        {
            new Vector2(-0.91f, -0.67f), new Vector2(0.70f, -0.86f),
            new Vector2(-0.56f, -0.31f), new Vector2(1.04f, -0.38f)
        };

        private readonly double startsAt;
        private readonly double impactAt;
        private readonly double disappearsAt;
        private readonly bool missed;
        private readonly VisualElement view;
        private readonly VisualElement tail;
        private readonly Image rock;
        private readonly Image[] fragments;
        private Vector2? impactPoint;
        private float size;
        private float travel;

        public string TargetId { get; }

        public MeteorPlayback(Texture2D rockTexture, Texture2D fragmentSheet, string targetId, bool missed,
            double startsAt, double impactAt, float animationScale)
        {
            TargetId = targetId;
            this.missed = missed;
            this.startsAt = startsAt;
            this.impactAt = impactAt;
            disappearsAt = impactAt + BreakDuration * animationScale;
            view = new VisualElement { name = "battle-meteor-effect", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(view);
            tail = new VisualElement { name = "battle-meteor-tail", pickingMode = PickingMode.Ignore };
            tail.style.position = Position.Absolute;
            tail.generateVisualContent += DrawTail;
            view.Add(tail);
            rock = Sprite("battle-meteor", rockTexture);
            view.Add(rock);

            fragments = new Image[FragmentDirections.Length];
            int width = fragmentSheet.width / 2;
            int height = fragmentSheet.height / 2;
            for (int i = 0; i < fragments.Length; i++)
            {
                var piece = Sprite("battle-meteor-fragment", fragmentSheet);
                piece.sourceRect = new Rect(i % 2 * width, i / 2 * height, width, height);
                fragments[i] = piece;
            }
        }

        public void Draw(double now, VisualElement layer, Rect target)
        {
            if (now < startsAt)
                return;

            if (!impactPoint.HasValue)
            {
                var bounds = layer.WorldToLocal(target);
                if (bounds.width <= 0f || bounds.height <= 0f)
                    return;
                impactPoint = new Vector2(bounds.center.x + (missed ? bounds.width * 0.38f : 0f),
                    bounds.yMin + bounds.height * 0.39f);
                size = Mathf.Clamp(bounds.width * 1.06f, 76f, 144f);
                travel = Mathf.Clamp(bounds.height * 2.25f, 165f, 330f);
            }
            var landing = impactPoint.Value;
            if (view.parent != layer)
                layer.Add(view);
            if (now < impactAt)
            {
                float fall = Mathf.Clamp01((float)((now - startsAt) / (impactAt - startsAt)));
                float distance = travel * (1 - fall * (0.20f + 0.80f * fall));
                var center = landing - new Vector2(0, distance);
                float tailHeight = size * (0.85f + 0.85f * fall);
                Place(tail, new Vector2(center.x, center.y - tailHeight * 0.45f), size * 1.04f, tailHeight);
                tail.style.opacity = 0.60f + 0.40f * fall;
                Place(rock, center, size, size);
                rock.style.rotate = new Rotate(new Angle(-16f + 24f * fall));
                return;
            }

            if (rock.parent != null)
            {
                rock.RemoveFromHierarchy();
                tail.RemoveFromHierarchy();
                foreach (var fragment in fragments)
                    view.Add(fragment);
            }
            float scatter = Mathf.Clamp01((float)((now - impactAt) / (disappearsAt - impactAt)));
            float spread = 1 - (1 - scatter) * (1 - scatter);
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.37f, 1, scatter));
            for (int i = 0; i < fragments.Length; i++)
            {
                var direction = FragmentDirections[i];
                var offset = direction * (size * (0.24f + spread * 0.96f));
                offset.y += size * (scatter * scatter * 0.64f + (i >= 2 ? 0.26f : 0f));
                var piece = fragments[i];
                float width = size * (i < 2 ? 0.64f : 0.53f);
                Place(piece, landing + offset, width, width);
                piece.style.rotate = new Rotate(new Angle(Mathf.Sign(direction.x) * (42 + 16 * i) * scatter));
                piece.style.opacity = fade;
            }
        }

        public void Clear() => view.RemoveFromHierarchy();

        private static Image Sprite(string name, Texture2D texture)
        {
            var image = new Image { name = name, image = texture, pickingMode = PickingMode.Ignore };
            image.scaleMode = ScaleMode.StretchToFill;
            image.style.position = Position.Absolute;
            return image;
        }

        private static void Place(VisualElement element, Vector2 center, float width, float height)
        {
            element.style.left = center.x - width * 0.5f;
            element.style.top = center.y - height * 0.5f;
            element.style.width = width;
            element.style.height = height;
        }

        private void DrawTail(MeshGenerationContext context)
        {
            var bounds = tail.contentRect;
            float x = bounds.center.x;
            float height = bounds.height;
            float width = bounds.width;
            var pen = context.painter2D;
            DrawPlume(new Color32(115, 33, 202, 72), width * 0.45f, 0f);
            DrawPlume(new Color32(206, 78, 251, 137), width * 0.29f, height * 0.24f);
            DrawPlume(new Color32(248, 192, 255, 191), width * 0.12f, height * 0.44f);

            void DrawPlume(Color color, float reach, float top)
            {
                pen.fillColor = color;
                pen.BeginPath();
                pen.MoveTo(new Vector2(x, top));
                pen.LineTo(new Vector2(x - reach * 0.45f, height * 0.56f));
                pen.LineTo(new Vector2(x - reach, height * 0.93f));
                pen.LineTo(new Vector2(x + reach, height * 0.93f));
                pen.LineTo(new Vector2(x + reach * 0.65f, height * 0.64f));
                pen.ClosePath();
                pen.Fill();
            }
        }
    }
}
