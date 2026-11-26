using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// Animated title lockup: a tattered war banner unfurls, the forged lettering of 讨伐队 lands character by character,
    /// then the manager's 经理 wax seal is pressed on. Afterwards a lantern glow breathes behind it,
    /// a glint runs across the gold now and then, and a few embers drift up.
    /// </summary>
    internal sealed class MainMenuLogo : VisualElement
    {
        // Layout in units of the logo width.
        private const float CalligraphyWidth = .8f, CalligraphyAspect = 501f / 1500f;
        private const float SwashAspect = 386f / 1500f;
        private const float SealSize = .2f, SealLeft = .755f, SealTop = .13f, SealTilt = 8f;
        private static readonly float[] CharacterCentres = { .175f, .505f, .83f };
        private const float ShineStart = 2.4f, ShinePeriod = 6.5f, ShineSweep = 1.15f;
        private const float StampAt = 1.08f, StampLength = .15f;

        private readonly VisualElement glow, swashWindow, swash, shadow, seal, burst, lockup;
        private readonly VisualElement[] characters = new VisualElement[3];
        private readonly (VisualElement window, VisualElement shine, float width)[] glints;
        private readonly Label subtitle;
        private readonly List<Ember> embers = new();
        private readonly double started = Time.unscaledTimeAsDouble;
        private readonly System.Random random = new(17);
        private float width;

        private sealed class Ember
        {
            public VisualElement Dot;
            public float Born, Life, X, Size, Drift, Phase;
        }

        internal MainMenuLogo()
        {
            name = "main-menu-title";
            tooltip = "讨伐队经理";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.overflow = Overflow.Visible;

            glow = Layer("LogoGlow");
            Add(glow);

            lockup = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(lockup);
            Add(lockup);

            swashWindow = new VisualElement { name = "logo-swash", pickingMode = PickingMode.Ignore };
            swashWindow.style.position = Position.Absolute;
            swashWindow.style.overflow = Overflow.Hidden;
            swash = Layer("LogoSwash");
            swash.style.unityBackgroundImageTintColor = new Color(.62f, .54f, .55f, 1);
            swashWindow.Add(swash);
            lockup.Add(swashWindow);

            shadow = Layer("LogoShadow");
            lockup.Add(shadow);
            for (int index = 0; index < characters.Length; index++)
            {
                characters[index] = Layer("LogoChar" + index);
                characters[index].name = "logo-character-" + index;
                characters[index].style.transformOrigin = new TransformOrigin(Length.Percent(CharacterCentres[index] * 100), Length.Percent(55));
                lockup.Add(characters[index]);
            }

            // A soft-edged glint built from nested windows onto a bright copy of the lettering.
            glints = new[] { (.13f, .1f), (.075f, .16f), (.03f, .26f) }.Select(spec =>
            {
                var window = new VisualElement { pickingMode = PickingMode.Ignore };
                window.style.position = Position.Absolute;
                window.style.overflow = Overflow.Hidden;
                window.style.opacity = spec.Item2;
                var shine = Layer("LogoShine");
                window.Add(shine);
                lockup.Add(window);
                return (window, shine, spec.Item1);
            }).ToArray();

            burst = Layer("LogoEmber");
            burst.style.unityBackgroundImageTintColor = new Color(.85f, .25f, .16f, 1);
            burst.style.opacity = 0;
            lockup.Add(burst);

            seal = Layer("LogoSeal");
            seal.name = "logo-seal";
            seal.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            lockup.Add(seal);

            subtitle = new Label("RAID  ·  PARTY  ·  MANAGER") { name = "logo-subtitle", pickingMode = PickingMode.Ignore };
            subtitle.style.position = Position.Absolute;
            subtitle.style.color = new Color(.91f, .76f, .48f, .82f);
            subtitle.style.unityTextAlign = TextAnchor.MiddleLeft;
            subtitle.style.whiteSpace = WhiteSpace.NoWrap;
            subtitle.style.textShadow = new TextShadow { color = new Color(0, 0, 0, .85f), offset = new Vector2(0, 1), blurRadius = 4 };
            lockup.Add(subtitle);

            for (int index = 0; index < 11; index++)
            {
                var dot = Layer("LogoEmber");
                dot.style.opacity = 0;
                Add(dot);
                embers.Add(new Ember { Dot = dot, Born = -100 });
            }

            Tick();
            schedule.Execute(Tick).Every(16);
        }

        /// <summary>Lays the lockup out for a logo of the given width and returns its height.</summary>
        internal float Fit(float logoWidth)
        {
            width = logoWidth;
            float letteringHeight = width * CalligraphyWidth * CalligraphyAspect;
            float height = width * .42f;
            style.width = width;
            style.height = height;

            Place(glow, -.12f * width, -.1f * width, 1.25f * width, .66f * width);
            float swashHeight = width * SwashAspect;
            Place(swashWindow, -.04f * width, .025f * width, width * 1.02f, swashHeight);
            Place(swash, 0, 0, width * 1.02f, swashHeight);
            Place(shadow, .008f * width, .012f * width, width * CalligraphyWidth, letteringHeight);
            foreach (var character in characters)
                Place(character, 0, 0, width * CalligraphyWidth, letteringHeight);
            foreach (var glint in glints)
            {
                glint.window.style.top = 0;
                glint.window.style.height = letteringHeight;
                glint.window.style.width = glint.width * width;
                glint.shine.style.position = Position.Absolute;
                glint.shine.style.top = 0;
                glint.shine.style.width = width * CalligraphyWidth;
                glint.shine.style.height = letteringHeight;
            }
            float sealSize = SealSize * width;
            Place(seal, SealLeft * width, SealTop * width, sealSize, sealSize);
            Place(burst, SealLeft * width - sealSize * .4f, SealTop * width - sealSize * .4f, sealSize * 1.8f, sealSize * 1.8f);
            subtitle.style.left = .07f * width;
            subtitle.style.top = .335f * width;
            subtitle.style.fontSize = Mathf.Max(10, Mathf.Round(.028f * width));
            Tick();
            return height;
        }

        private void Tick()
        {
            if (width <= 0) return;
            float time = (float)(Time.unscaledTimeAsDouble - started);

            float brush = EaseOut(Phase(time, .22f, .36f));
            swashWindow.style.width = brush * width * 1.02f;

            float letters = 0;
            for (int index = 0; index < characters.Length; index++)
            {
                float land = Phase(time, .5f + index * .13f, .36f);
                float eased = EaseOut(land);
                characters[index].style.opacity = Mathf.Clamp01(land * 1.8f);
                characters[index].style.scale = new Scale(Vector2.one * Mathf.Lerp(1.32f, 1, eased));
                characters[index].style.translate = new Translate(0, Mathf.Lerp(-.02f * width, 0, eased));
                letters += land / characters.Length;
            }
            shadow.style.opacity = letters;

            float stamp = Phase(time, StampAt, StampLength);
            seal.style.opacity = Mathf.Clamp01(stamp * 2.2f);
            seal.style.scale = new Scale(Vector2.one * Mathf.Lerp(2.5f, 1, stamp * stamp));
            seal.style.rotate = new Rotate(Mathf.Lerp(-9, SealTilt, EaseOut(stamp)));

            float impact = Phase(time, StampAt + StampLength, .32f);
            float shake = impact > 0 && impact < 1 ? Mathf.Sin(impact * 26) * (1 - impact) * .006f * width : 0;
            lockup.style.translate = new Translate(shake * .4f, Mathf.Abs(shake));
            burst.style.opacity = impact > 0 && impact < 1 ? (1 - impact) * .55f : 0;
            burst.style.scale = new Scale(Vector2.one * Mathf.Lerp(.4f, 1.9f, EaseOut(impact)));

            float caption = EaseOut(Phase(time, 1.35f, .8f));
            subtitle.style.opacity = caption;
            subtitle.style.letterSpacing = Mathf.Lerp(.002f, .011f, caption) * width;

            float breathe = 1 + Mathf.Sin(time * 1.6f) * .13f + Mathf.Sin(time * 6.9f) * .04f;
            glow.style.opacity = Mathf.Clamp01(Phase(time, .3f, 1.2f)) * .5f * breathe;

            float sweep = time < ShineStart ? -1 : ((time - ShineStart) % ShinePeriod) / ShineSweep;
            float lettering = width * CalligraphyWidth;
            foreach (var glint in glints)
            {
                bool visible = sweep >= 0 && sweep <= 1;
                glint.window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                float centre = Mathf.Lerp(-.12f * width, lettering + .12f * width, EaseInOut(sweep));
                float left = centre - glint.width * width / 2;
                glint.window.style.left = left;
                glint.shine.style.left = -left;
            }

            UpdateEmbers(time);
        }

        private void UpdateEmbers(float time)
        {
            if (time < 1.5f) return;
            float height = width * .42f;
            foreach (var ember in embers)
            {
                float age = time - ember.Born;
                if (age > ember.Life)
                {
                    if (random.NextDouble() > .025) { ember.Dot.style.opacity = 0; continue; }
                    ember.Born = time;
                    ember.Life = 2.6f + (float)random.NextDouble() * 2.4f;
                    ember.X = (float)(.04 + random.NextDouble() * .78) * width;
                    ember.Size = (2.5f + (float)random.NextDouble() * 4.5f) * width / 520f;
                    ember.Drift = ((float)random.NextDouble() - .5f) * .08f * width;
                    ember.Phase = (float)random.NextDouble() * 6.28f;
                    age = 0;
                }
                float life = age / ember.Life;
                float x = ember.X + ember.Drift * life + Mathf.Sin(age * 2.3f + ember.Phase) * .012f * width;
                float y = height * .62f - life * height * .78f;
                ember.Dot.style.left = x - ember.Size / 2;
                ember.Dot.style.top = y - ember.Size / 2;
                ember.Dot.style.width = ember.Dot.style.height = ember.Size;
                ember.Dot.style.opacity = Mathf.Sin(life * Mathf.PI) * (.55f + .35f * Mathf.Sin(age * 9 + ember.Phase));
            }
        }

        private static float Phase(float time, float start, float length) => Mathf.Clamp01((time - start) / length);
        private static float EaseOut(float value) => 1 - Mathf.Pow(1 - value, 3);
        private static float EaseInOut(float value) => value * value * (3 - 2 * value);

        private static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.left = left;
            element.style.top = top;
            element.style.width = width;
            element.style.height = height;
        }

        private static VisualElement Layer(string texture)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("MainMenu/Logo/" + texture));
            return layer;
        }
    }
}
