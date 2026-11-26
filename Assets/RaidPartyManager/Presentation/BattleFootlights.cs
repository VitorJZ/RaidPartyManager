using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Stage-lip art for the battle apron: each energy cell is a footlight, each equipment slot a prop on a shelf.
    internal static class ApronArt
    {
        private static Texture2D body, flame, spark, riser, chalk, seal, glow, beam;

        public static Texture2D Body => body ??= Resources.Load<Texture2D>("Footlights/LampBody");
        public static Texture2D Flame => flame ??= Resources.Load<Texture2D>("Footlights/Flame");
        public static Texture2D Spark => spark ??= Resources.Load<Texture2D>("Footlights/Spark");
        public static Texture2D Riser => riser ??= Resources.Load<Texture2D>("Footlights/PropRiser");
        public static Texture2D Chalk => chalk ??= Resources.Load<Texture2D>("Footlights/ChalkMark");
        public static Texture2D Seal => seal ??= Resources.Load<Texture2D>("Footlights/SealCloth");

        // Soft light and smoke key badly from generated art, so they are a procedural radial falloff.
        public static Texture2D Glow
        {
            get
            {
                if (glow != null) return glow;
                const int size = 64;
                glow = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ApronGlow", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                        float falloff = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * falloff * falloff));
                    }
                glow.SetPixels32(pixels);
                glow.Apply();
                return glow;
            }
        }

        // Footlights throw light up the stage: bright at the lip, soft-edged, fading before it reaches the actor.
        public static Texture2D Beam
        {
            get
            {
                if (beam != null) return beam;
                const int width = 32, height = 96;
                beam = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "ApronBeam", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        // Row 0 is the lip. The cone narrows as it rises and dies out well before the actor's health bar.
                        float rise = (y + .5f) / height;
                        float across = Mathf.Abs((x + .5f) / width * 2 - 1) / Mathf.Lerp(1f, .42f, rise);
                        float edge = across >= 1 ? 0 : Mathf.Pow(1 - across * across, 1.6f);
                        float fade = Mathf.Pow(1 - rise, 2.4f);
                        pixels[y * width + x] = new Color32(255, 255, 255, (byte)(255 * edge * fade));
                    }
                beam.SetPixels32(pixels);
                beam.Apply();
                return beam;
            }
        }

        public static VisualElement Soft(string name, Color tint)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.backgroundImage = Glow;
            element.style.unityBackgroundImageTintColor = tint;
            return element;
        }
    }

    // One character's run of footlights. A lit lamp is one unspent energy; casting a skill snuffs one out.
    internal sealed class FootlightRow : VisualElement
    {
        // LampBody.png is 118x180; a run of four lamps shrinks to fit the actor's column.
        public const float LampHeight = 60, LampAspect = 118f / 180f;
        private readonly List<Footlight> lamps = new();
        private VisualElement wash, pool;
        private float brightness = -1;

        // Light reaching the props: how many lamps burn, and how their flames are breathing right now.
        public float Brightness => Mathf.Max(0, brightness);
        public float Flicker { get; private set; }

        public FootlightRow(BattleEnergy energy, int previouslyLit)
        {
            name = "equipment-energy";
            pickingMode = PickingMode.Position;
            style.flexDirection = FlexDirection.Row;
            style.justifyContent = Justify.Center;
            style.alignItems = Align.FlexEnd;
            style.height = LampHeight;
            style.flexShrink = 0;
            Paint(energy, previouslyLit);
            schedule.Execute(Tick).Every(16);
        }

        public void Paint(BattleEnergy energy) => Paint(energy, -1);

        private void Paint(BattleEnergy energy, int previouslyLit)
        {
            tooltip = $"能量  {energy.Remaining}/{energy.Capacity}";
            while (lamps.Count < energy.Capacity)
            {
                var lamp = new Footlight(lamps.Count, previouslyLit < 0 || lamps.Count < previouslyLit,
                    energy.Capacity > 3 ? LampHeight * .78f : LampHeight);
                lamps.Add(lamp);
                Add(lamp);
            }
            while (lamps.Count > energy.Capacity)
            {
                lamps[^1].RemoveFromHierarchy();
                lamps.RemoveAt(lamps.Count - 1);
            }
            double now = Time.unscaledTimeAsDouble;
            int relit = 0;
            for (int i = 0; i < lamps.Count; i++)
            {
                bool on = i < energy.Remaining;
                // Refilled lamps catch one after another, the way a stagehand walks the lip with a taper.
                if (on && !lamps[i].Lit) lamps[i].Set(true, now + .14 * relit++);
                else if (!on && lamps[i].Lit) lamps[i].Set(false, now);
            }
        }

        // The beam and floor pool belong to the actor's column, behind the props; their strength follows the lit lamps.
        public void Illuminate(VisualElement beam, VisualElement floorPool)
        {
            wash = beam;
            pool = floorPool;
            brightness = -1;
            Tick();
        }

        private void Tick()
        {
            double now = Time.unscaledTimeAsDouble;
            int glowing = 0;
            foreach (var lamp in lamps)
            {
                lamp.Tick(now);
                if (lamp.GlowingAt(now)) glowing++;
            }
            if (wash == null) return;
            float target = lamps.Count == 0 ? 0 : (float)glowing / lamps.Count;
            brightness = brightness < 0 ? target : Mathf.MoveTowards(brightness, target, .025f);
            float sum = 0;
            foreach (var lamp in lamps) if (lamp.GlowingAt(now)) sum += lamp.Flicker;
            Flicker = glowing > 0 ? sum / glowing : .5f;
            float flicker = .86f + .2f * Flicker;
            wash.style.opacity = .24f * brightness * flicker;
            pool.style.opacity = .5f * brightness * flicker;
        }

        private sealed class Footlight : VisualElement
        {
            private const double SnuffTime = 1.0, CatchTime = .5;
            // Where the wick sits in LampBody.png, as fractions of the lamp box (x from left, y from top).
            private const float WickX = .5f, WickY = .51f, FlameHeight = .36f, FlameAspect = 96f / 216f;
            private readonly float LampWidth, LampHeight;
            private readonly VisualElement halo, body, flame, core, shadow;
            private readonly VisualElement[] smoke = new VisualElement[3];
            private readonly VisualElement[] sparks = new VisualElement[2];
            private readonly double[] sparkBorn = { double.NegativeInfinity, double.NegativeInfinity };
            private readonly float[] sparkDrift = new float[2];
            private readonly float seed;
            private double changedAt = double.NegativeInfinity, nextSpark;
            private bool shownLit;

            public bool Lit { get; private set; }
            public float Flicker { get; private set; }
            public bool GlowingAt(double now) => Lit && now >= changedAt;

            public Footlight(int index, bool lit, float height)
            {
                LampHeight = height;
                LampWidth = height * LampAspect;
                name = $"energy-cell-{index + 1}";
                pickingMode = PickingMode.Ignore;
                seed = index * 1.73f + Random.value * 10;
                style.width = LampWidth;
                style.height = LampHeight;
                style.marginLeft = style.marginRight = 1;
                style.overflow = Overflow.Visible;
                nextSpark = Time.unscaledTimeAsDouble + Random.Range(.4f, 2.2f);

                // Contact shadow anchors the brass base to the boards.
                shadow = ApronArt.Soft("footlight-shadow", new Color(0, 0, 0, .7f));
                shadow.style.left = shadow.style.right = Length.Percent(4);
                shadow.style.bottom = -3;
                shadow.style.height = LampHeight * .2f;
                Add(shadow);

                halo = ApronArt.Soft("footlight-glow", FlameColor);
                halo.style.width = LampWidth * 2.3f;
                halo.style.height = LampHeight;
                halo.style.left = LampWidth * (WickX - 1.15f);
                halo.style.top = LampHeight * (WickY - FlameHeight * .45f) - LampHeight * .5f;
                Add(halo);

                for (int i = 0; i < smoke.Length; i++)
                {
                    smoke[i] = ApronArt.Soft("footlight-smoke", new Color(.72f, .64f, .82f, 0));
                    smoke[i].style.display = DisplayStyle.None;
                    Add(smoke[i]);
                }

                body = Layer("footlight-lamp", ApronArt.Body);
                body.style.left = body.style.right = body.style.top = body.style.bottom = 0;
                Add(body);

                // The flame is its own layer so it can sway, stretch and gutter like a live wick.
                float flameHeight = LampHeight * FlameHeight, flameWidth = flameHeight * FlameAspect;
                flame = Layer("footlight-flame", ApronArt.Flame);
                flame.style.width = flameWidth;
                flame.style.height = flameHeight;
                flame.style.left = LampWidth * WickX - flameWidth * .5f;
                flame.style.top = LampHeight * WickY - flameHeight;
                flame.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100));
                Add(flame);
                core = Layer("footlight-flame-core", ApronArt.Flame);
                core.style.width = flameWidth * .55f;
                core.style.height = flameHeight * .55f;
                core.style.left = LampWidth * WickX - flameWidth * .275f;
                core.style.top = LampHeight * WickY - flameHeight * .58f;
                core.style.unityBackgroundImageTintColor = new Color(1, .93f, 1, .85f);
                core.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100));
                Add(core);

                for (int i = 0; i < sparks.Length; i++)
                {
                    sparks[i] = Layer("footlight-spark", ApronArt.Spark ?? ApronArt.Glow);
                    sparks[i].style.display = DisplayStyle.None;
                    Add(sparks[i]);
                }

                Lit = shownLit = lit;
                Show(lit);
                if (!lit) changedAt = double.NegativeInfinity;
            }

            private static VisualElement Layer(string name, Texture2D art)
            {
                var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                layer.style.position = Position.Absolute;
                layer.style.backgroundImage = art;
                layer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                return layer;
            }

            private static Color FlameColor => new Color32(194, 105, 255, 255);

            public void Set(bool lit, double at)
            {
                Lit = lit;
                changedAt = at;
                EnableInClassList("footlight-lit", lit);
                EnableInClassList("footlight-out", !lit);
            }

            private void Show(bool lit)
            {
                shownLit = lit;
                EnableInClassList("footlight-lit", Lit);
                EnableInClassList("footlight-out", !Lit);
                // A cold lamp keeps its brass but loses the light on it: darker, cooler, sooty glass.
                body.style.unityBackgroundImageTintColor = lit ? Color.white : new Color(.5f, .47f, .56f);
                if (ApronArt.Body == null)
                    body.style.backgroundColor = lit ? FlameColor : new Color(.25f, .2f, .28f);
            }

            public void Tick(double now)
            {
                double age = now - changedAt;
                float t = (float)now;
                float sway = Mathf.PerlinNoise(seed, t * 4.6f);
                float lick = Mathf.PerlinNoise(seed + 3.1f, t * 7.3f);
                float jitter = Mathf.PerlinNoise(seed + 9.7f, t * 15f);
                Flicker = sway;

                // Intensity 0..1: grows with an overshoot when lit, collapses quickly when snuffed.
                float intensity;
                if (Lit)
                {
                    if (age < 0) intensity = 0;
                    else
                    {
                        if (!shownLit) Show(true);
                        float k = Mathf.Clamp01((float)(age / CatchTime));
                        intensity = k >= 1 ? 1 : 1 - Mathf.Pow(1 - k, 3) + Mathf.Sin(k * Mathf.PI) * .35f;
                    }
                }
                else
                {
                    float k = Mathf.Clamp01((float)(age / .2));
                    intensity = 1 - k;
                    if (k >= 1 && shownLit) Show(false);
                }

                float stretch = .86f + .3f * sway + .08f * jitter;
                float girth = 1.08f - .18f * sway + .06f * lick;
                float lean = (lick - .5f) * 12f;
                flame.style.display = core.style.display = intensity > .01f ? DisplayStyle.Flex : DisplayStyle.None;
                flame.style.scale = new Scale(new Vector2(girth * Mathf.Lerp(1.4f, 1f, intensity), stretch * intensity));
                flame.style.rotate = new Rotate(new Angle(lean));
                flame.style.opacity = .9f + .1f * jitter;
                float coreStretch = .8f + .3f * lick;
                core.style.scale = new Scale(new Vector2(1, coreStretch * intensity));
                core.style.rotate = new Rotate(new Angle(lean * .6f));

                float flash = Lit ? 0 : Mathf.Clamp01(1 - (float)(age / .35));
                halo.style.opacity = intensity * (.58f + .3f * sway + .1f * jitter) + flash;
                float grow = .9f + .18f * sway + .3f * flash;
                halo.style.scale = new Scale(new Vector2(grow, grow));
                shadow.style.scale = new Scale(new Vector2(1 + .06f * sway * intensity, 1));

                TickSparks(now, intensity);
                TickSmoke(age);
            }

            // Now and then a lit wick spits an ember that drifts up and dies.
            private void TickSparks(double now, float intensity)
            {
                if (intensity > .9f && now >= nextSpark)
                {
                    int slot = sparkBorn[0] <= sparkBorn[1] ? 0 : 1;
                    sparkBorn[slot] = now;
                    sparkDrift[slot] = Random.Range(-9f, 9f);
                    nextSpark = now + Random.Range(.9f, 2.8f);
                }
                for (int i = 0; i < sparks.Length; i++)
                {
                    float life = (float)((now - sparkBorn[i]) / 1.1);
                    var spark = sparks[i];
                    if (life < 0 || life >= 1) { spark.style.display = DisplayStyle.None; continue; }
                    spark.style.display = DisplayStyle.Flex;
                    float size = Mathf.Lerp(5, 2, life);
                    spark.style.width = spark.style.height = size;
                    spark.style.left = LampWidth * WickX - size * .5f + sparkDrift[i] * life + Mathf.Sin(life * 9 + i) * 2;
                    spark.style.top = LampHeight * (WickY - FlameHeight) - size * .5f - life * 34;
                    spark.style.opacity = Mathf.Clamp01((1 - life) * 1.6f);
                }
            }

            // A snuffed lamp trails three curls of violet smoke.
            private void TickSmoke(double age)
            {
                bool smoking = !Lit && age >= 0 && age < SnuffTime + .3;
                for (int i = 0; i < smoke.Length; i++)
                {
                    float t = smoking ? (float)((age - i * .13) / SnuffTime) : -1;
                    var puff = smoke[i];
                    if (t <= 0 || t >= 1) { puff.style.display = DisplayStyle.None; continue; }
                    puff.style.display = DisplayStyle.Flex;
                    float size = Mathf.Lerp(8, 26, t);
                    float drift = Mathf.Sin(t * 5 + i * 2) * 6;
                    puff.style.width = puff.style.height = size;
                    puff.style.left = LampWidth * WickX - size * .5f + drift;
                    puff.style.top = LampHeight * (WickY - FlameHeight * .5f) - size * .5f - t * 36;
                    puff.style.unityBackgroundImageTintColor = new Color(.72f, .64f, .82f, .6f * (1 - t) * Mathf.Clamp01(t * 6));
                }
            }
        }
    }
}
