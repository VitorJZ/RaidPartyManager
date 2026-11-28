using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class FloatingCombatNumbers : VisualElement
    {
        private enum Weight { Light, Solid, Heavy, Massive }

        private static readonly Texture2D IvoryGlyphs = Resources.Load<Texture2D>("DamageGlyphs/Ivory");
        private static readonly Texture2D SteelGlyphs = Resources.Load<Texture2D>("DamageGlyphs/Steel");
        private static readonly Texture2D ShatterGlyphs = Resources.Load<Texture2D>("DamageGlyphs/Shatter");
        private static readonly Texture2D PoisonGlyphs = Resources.Load<Texture2D>("DamageGlyphs/Poison");
        private static readonly Texture2D DamageMarks = Resources.Load<Texture2D>("DamageGlyphs/Types");

        private readonly double startedAt;
        private readonly float verticalOffset;
        private float width, height;
        private Vector2? origin;
        private Weight strongest;
        private Color accent;
        private float flare, flareStart, flareWidth;

        public string TargetId { get; }
        public float StackHeight => height + 5f;

        public FloatingCombatNumbers(BattleEvent entry, int healed, Rect body, double now, float verticalOffset)
        {
            TargetId = entry.TargetId;
            startedAt = now;
            this.verticalOffset = verticalOffset;
            CaptureOrigin(body);
            name = "floating-combat-numbers";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.opacity = 1;

            if (healed > 0)
                AddNumber(healed, "healing", new Color32(116, 255, 125, 255), healing: true);
            else
            {
                foreach (var (type, amount) in entry.DamageTaken)
                {
                    var (kind, color) = entry.PoisonDamage ? ("poison", new Color32(188, 230, 62, 255)) : type switch
                    {
                        DamageType.Physical => ("physical", new Color32(255, 255, 255, 255)),
                        DamageType.Magic => ("magic", new Color32(111, 207, 255, 255)),
                        DamageType.Fire => ("fire", new Color32(255, 158, 63, 255)),
                        DamageType.Lightning => ("lightning", new Color32(203, 142, 255, 255)),
                        DamageType.True => ("true", new Color32(255, 224, 151, 255)),
                        DamageType.Fixed => ("fixed", new Color32(224, 232, 244, 255)),
                        _ => ("other", new Color32(255, 244, 221, 255))
                    };
                    AddNumber(amount, kind, color);
                }
            }
            style.width = width;
            style.height = height;
            if (strongest == Weight.Massive)
                generateVisualContent += DrawFlare;
        }

        private void AddNumber(int amount, string kind, Color color, bool healing = false)
        {
            if (amount <= 0) return;
            var weight = healing ? Weight.Light : amount switch
            {
                >= 24 => Weight.Massive,
                >= 12 => Weight.Heavy,
                >= 6 => Weight.Solid,
                _ => Weight.Light
            };
            bool strongestSoFar = weight > strongest;
            if (strongestSoFar)
            {
                strongest = weight;
                accent = color;
            }
            var (size, atlas) = weight switch
            {
                Weight.Massive => (48, ShatterGlyphs),
                Weight.Heavy => (36, SteelGlyphs),
                Weight.Solid => (28, IvoryGlyphs),
                _ => (23, IvoryGlyphs)
            };
            bool poisonArt = kind == "poison" && PoisonGlyphs != null;
            if (poisonArt) atlas = PoisonGlyphs;
            string value = (healing ? "+" : "−") + amount;
            var label = PrototypeStyle.Text(value, size, color);
            label.name = "combat-number";
            label.AddToClassList("combat-number-" + kind);
            label.pickingMode = PickingMode.Ignore;
            label.style.unityFontStyleAndWeight = weight >= Weight.Heavy ? FontStyle.BoldAndItalic : FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            float labelHeight = Mathf.Ceil(6f + size * 1.18f);
            float labelWidth = atlas != null ? PaintGlyphs(label, atlas, value, kind, poisonArt ? Color.white : color, labelHeight) :
                Mathf.Ceil(10f + value.Length * size * .62f);
            if (atlas == null)
            {
                label.style.unityTextOutlineColor = (Color)new Color32(26, 14, 21, 255);
                label.style.unityTextOutlineWidth = weight >= Weight.Heavy ? 2f : 1f;
            }
            label.style.width = labelWidth;
            label.style.height = labelHeight;
            label.style.flexShrink = 0;
            PrototypeStyle.Middle(label);
            Add(label);
            if (strongestSoFar && weight == Weight.Massive)
            {
                flareStart = width;
                flareWidth = labelWidth;
            }
            width += labelWidth;
            height = Mathf.Max(height, labelHeight);
        }

        private static float PaintGlyphs(Label label, Texture2D atlas, string value, string kind, Color color, float glyphHeight)
        {
            // Keep the label value available to the UI while the artwork renders its glyphs.
            label.style.color = Color.clear;
            var row = PrototypeStyle.Row("combat-number-glyphs");
            row.pickingMode = PickingMode.Ignore;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            PrototypeStyle.Fill(row);
            float rowWidth = 0;
            foreach (char c in value)
            {
                int index = c == '−' ? 10 : c == '+' ? 11 : c - '0';
                var cell = AnimationCels.RectFor(atlas, index, columns: 4, rows: 3);
                float glyphWidth = glyphHeight * cell.width / cell.height * (index >= 10 ? .78f : 1f);
                bool useMark = c == '−' && DamageMarks != null;
                var image = new Image
                {
                    name = useMark ? "combat-number-type-mark" : "combat-number-glyph",
                    image = useMark ? DamageMarks : atlas,
                    sourceRect = useMark ? AnimationCels.RectFor(DamageMarks, DamageMarkIndex(kind), columns: 4, rows: 2) : cell,
                    tintColor = useMark ? Color.white : color,
                    scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore
                };
                image.style.width = glyphWidth;
                image.style.height = glyphHeight;
                image.style.flexShrink = 0;
                row.Add(image);
                rowWidth += glyphWidth;
            }
            label.Add(row);
            return Mathf.Ceil(rowWidth + 4f);
        }

        private static int DamageMarkIndex(string kind) => kind switch
        {
            "physical" => 0,
            "magic" => 1,
            "fire" => 2,
            "lightning" => 3,
            "poison" => 4,
            "true" => 5,
            "fixed" => 6,
            _ => 7
        };

        private double Duration => strongest switch
        {
            Weight.Massive => .76,
            Weight.Heavy => .64,
            Weight.Solid => .54,
            _ => .50
        };

        public bool ActiveAt(double now) => now < startedAt + Duration;
        public bool RecentlyStarted(double now) => now < startedAt + 0.15;

        public void Draw(VisualElement layer, Rect body, double now)
        {
            CaptureOrigin(body);
            if (!origin.HasValue || !(layer.layout.width > 0f && layer.layout.height > 0f))
                return;
            if (parent != layer)
                layer.Add(this);

            var anchor = layer.WorldToLocal(origin.Value);
            float progress = Mathf.Clamp01((float)((now - startedAt) / Duration));
            float punch = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .31f, progress));
            var (stretch, rise, padding) = strongest switch
            {
                Weight.Massive => (.43f, 53f, 30f),
                Weight.Heavy => (.29f, 43f, 8f),
                Weight.Solid => (.13f, 34f, 3f),
                _ => (.05f, 30f, 3f)
            };
            float recoil = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.24f, .52f, progress)) * .18f;
            float scaleX = 1 + stretch * (punch * .55f + recoil);
            float scaleY = 1 + stretch * (punch - recoil);
            float fit = Mathf.Min(1, layer.layout.width / ((width + 2 * padding) * scaleX));
            scaleX *= fit;
            scaleY *= fit;
            style.scale = new Scale(new Vector2(scaleX, scaleY));
            float halfWidth = (width * .5f + padding) * scaleX;
            float halfHeight = height * .5f * scaleY;
            float centreX = Mathf.Clamp(anchor.x, halfWidth, layer.layout.width - halfWidth);
            float centreY = Mathf.Clamp(anchor.y - height * .26f - verticalOffset - progress * rise,
                halfHeight + 5f, Mathf.Max(halfHeight + 5f, layer.layout.height - halfHeight));
            style.left = centreX - width * .5f;
            style.top = centreY - height * .5f;
            style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, progress));
            if (strongest == Weight.Massive)
            {
                flare = Mathf.InverseLerp(.47f, 0f, progress);
                MarkDirtyRepaint();
            }
        }

        private void DrawFlare(MeshGenerationContext context)
        {
            if (flare <= 0) return;
            var pen = context.painter2D;
            var color = Color.Lerp(accent, Color.white, .32f);
            color.a = flare * .91f;
            pen.fillColor = color;
            for (int side = -1; side <= 1; side += 2)
            {
                float edge = flareStart + (side < 0 ? 3f : flareWidth - 3f);
                bool nearAnotherNumber = side < 0 ? flareStart > 0 : flareStart + flareWidth < width;
                for (int ray = -1; ray <= 1; ray++)
                {
                    if (nearAnotherNumber && ray == 0) continue;
                    var direction = new Vector2(side, ray * .8f).normalized;
                    var normal = Vector2.Perpendicular(direction);
                    float lift = ray * (nearAnotherNumber ? 24f : 10f);
                    var tip = new Vector2(edge, height * .5f + lift) + direction * (21 - 10 * flare);
                    float thickness = ray == 0 ? 3.2f : 2.1f;
                    pen.BeginPath();
                    pen.MoveTo(tip - direction * 10f + normal * thickness);
                    pen.LineTo(tip + direction * (ray == 0 ? 10f : 5f));
                    pen.LineTo(tip - direction * 10f - normal * thickness);
                    pen.ClosePath();
                    pen.Fill();
                }
            }
        }

        private void CaptureOrigin(Rect body)
        {
            if (!origin.HasValue && body.width > 0 && body.height > 0)
                origin = new Vector2(body.center.x, body.yMin);
        }
    }
}
