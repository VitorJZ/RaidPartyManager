using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// The curtain-call review: after a battle the playhouse hangs a velvet banner over a gilt-ruled parchment playbill
    /// that lists every performer's rating and lays the spoils out in brass display frames.
    /// </summary>
    internal static class SettlementArt
    {
        private const string ArtRoot = "Settlement/";
        internal const string RevealClass = "settlement-reveal";
        private const int SheetSlice = 180;
        private const float BannerWidth = 360;
        private const float BannerHeight = BannerWidth * 365f / 1502f;

        // Ink on parchment.
        internal static readonly Color Ink = new Color32(52, 33, 19, 255);
        internal static readonly Color InkSoft = new Color32(52, 33, 19, 170);
        internal static readonly Color InkFaint = new Color32(52, 33, 19, 92);
        internal static readonly Color Rule = new Color32(52, 33, 19, 40);
        internal static readonly Color Wash = new Color32(52, 33, 19, 16);
        internal static readonly Color AllyInk = new Color32(30, 92, 84, 255);
        internal static readonly Color EnemyInk = new Color32(148, 46, 28, 255);
        internal static readonly Color Ochre = new Color32(140, 90, 22, 255);
        internal static readonly Color Gilt = new Color32(255, 230, 176, 255);
        internal static readonly Color Ash = new Color32(222, 214, 202, 255);

        internal static readonly TextShadow Embroidered = new()
            { offset = new Vector2(0, 2), blurRadius = 2.5f, color = new Color(.16f, .02f, .02f, .95f) };

        private static Texture2D spotlight;

        internal static Texture2D Art(string file) => Resources.Load<Texture2D>(ArtRoot + file);

        /// <summary>Builds the darkened stage, the banner and the playbill; returns the playbill content column.</summary>
        internal static VisualElement Stage(VisualElement overlay, string panelId, string title, string subtitle, bool victory, bool entering)
        {
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0.03f, 0.024f, 0.03f, entering ? 0 : .8f);
            Animate(overlay, 320, "background-color");

            var glow = new VisualElement { name = "settlement-spotlight", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(glow);
            glow.style.backgroundImage = Spotlight();
            glow.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            glow.style.unityBackgroundImageTintColor = victory ? new Color(1f, .78f, .45f, .26f) : new Color(.62f, .68f, .8f, .16f);
            glow.style.opacity = entering ? 0 : 1;
            Animate(glow, 600, "opacity");
            overlay.Add(glow);

            var frame = new VisualElement { name = "settlement-frame", pickingMode = PickingMode.Ignore };
            frame.style.width = Length.Percent(94);
            frame.style.maxWidth = 860;
            frame.style.maxHeight = Length.Percent(97);
            frame.style.alignItems = Align.Stretch;
            overlay.Add(frame);

            var sheet = new VisualElement { name = panelId };
            sheet.style.flexShrink = 1;
            sheet.style.minHeight = 0;
            sheet.style.marginTop = BannerHeight * .34f;
            sheet.style.paddingTop = BannerHeight * .7f;
            sheet.style.paddingBottom = 28;
            sheet.style.paddingLeft = sheet.style.paddingRight = 34;
            var paper = Art("Sheet");
            if (paper != null)
            {
                // The gilt corner flourishes sit inside the outer 180px of the sheet art.
                sheet.style.backgroundImage = paper;
                sheet.style.unitySliceLeft = sheet.style.unitySliceRight = SheetSlice;
                sheet.style.unitySliceTop = sheet.style.unitySliceBottom = SheetSlice;
                sheet.style.unitySliceScale = .34f;
            }
            else
            {
                sheet.style.backgroundColor = (Color)new Color32(226, 208, 170, 255);
                PrototypeStyle.Outline(sheet, Ochre, 6);
            }

            var banner = Banner(title, victory);
            frame.Add(sheet);
            frame.Add(banner);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var line = Lettering(subtitle, 13, victory ? Ochre : InkSoft, bold: true);
                line.name = "settlement-subtitle";
                line.style.alignSelf = Align.Center;
                line.style.letterSpacing = 4;
                line.style.marginBottom = 4;
                line.AddToClassList(RevealClass);
                sheet.Add(line);
            }

            if (entering)
            {
                sheet.style.opacity = 0;
                sheet.style.translate = new Translate(0, 22);
                banner.style.opacity = 0;
                banner.style.scale = new Scale(new Vector2(.72f, .72f));
                banner.style.translate = new Translate(0, -24);
                overlay.schedule.Execute(() =>
                {
                    overlay.style.backgroundColor = new Color(0.03f, 0.024f, 0.03f, .8f);
                    glow.style.opacity = 1;
                    sheet.style.opacity = 1;
                    sheet.style.translate = new Translate(0, 0);
                }).ExecuteLater(16);
                overlay.schedule.Execute(() =>
                {
                    banner.style.opacity = 1;
                    banner.style.scale = new Scale(Vector2.one);
                    banner.style.translate = new Translate(0, 0);
                }).ExecuteLater(140);
            }
            Animate(sheet, 380, "opacity", "translate");
            AnimateBack(banner, 460, "opacity", "scale", "translate");
            return sheet;
        }

        private static VisualElement Banner(string title, bool victory)
        {
            var banner = new VisualElement { name = "settlement-banner", pickingMode = PickingMode.Ignore };
            banner.style.position = Position.Absolute;
            banner.style.top = 0;
            banner.style.alignSelf = Align.Center;
            banner.style.width = BannerWidth;
            banner.style.height = BannerHeight;
            banner.style.alignItems = Align.Center;
            banner.style.justifyContent = Justify.Center;
            banner.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(40));
            var art = Art(victory ? "BannerVictory" : "BannerDefeat");
            if (art != null)
            {
                banner.style.backgroundImage = art;
                banner.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else
            {
                banner.style.backgroundColor = victory ? new Color(.48f, .09f, .1f) : new Color(.27f, .27f, .26f);
                PrototypeStyle.Outline(banner, victory ? new Color32(214, 170, 88, 255) : new Color32(140, 136, 130, 255), 3);
            }
            var words = Lettering(Spaced(title), 30, victory ? Gilt : Ash, bold: true);
            words.name = "settlement-title";
            words.style.letterSpacing = 6;
            words.style.textShadow = victory ? Embroidered : new TextShadow
                { offset = new Vector2(0, 2), blurRadius = 2, color = new Color(.05f, .05f, .06f, .95f) };
            banner.Add(words);
            return banner;
        }

        private static string Spaced(string text) => string.Join(" ", text.ToCharArray());

        /// <summary>A section heading inked over a gilt divider, playbill style.</summary>
        internal static VisualElement SectionHeading(string text, float dividerWidth = 300)
        {
            var heading = new VisualElement { name = "settlement-heading-" + text, pickingMode = PickingMode.Ignore };
            heading.style.alignItems = Align.Center;
            heading.style.flexShrink = 0;
            heading.style.marginTop = 10;
            heading.style.marginBottom = 2;
            heading.AddToClassList(RevealClass);
            var label = Lettering(Spaced(text), 16, Ink, bold: true);
            label.style.letterSpacing = 2;
            heading.Add(label);
            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.style.width = dividerWidth;
            divider.style.maxWidth = Length.Percent(80);
            divider.style.height = dividerWidth * 124f / 1481f;
            divider.style.marginTop = -1;
            if (Art("Divider") is { } art)
            {
                divider.style.backgroundImage = art;
                divider.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else
            {
                divider.style.height = 1;
                divider.style.backgroundColor = InkFaint;
            }
            heading.Add(divider);
            return heading;
        }

        /// <summary>The red claim ribbon shared with the commission board, used for every way out of the settlement.</summary>
        internal static Button Ribbon(string text, string id, Action press)
        {
            var ribbon = new Button(press) { name = id, text = string.Empty };
            Bare(ribbon);
            ribbon.style.alignSelf = Align.Center;
            ribbon.style.alignItems = Align.Center;
            ribbon.style.justifyContent = Justify.Center;
            ribbon.style.flexShrink = 0;
            ribbon.style.marginTop = 10;
            var art = Resources.Load<Texture2D>("QuestBoard/ClaimRibbon");
            float aspect = art != null ? (float)art.width / art.height : 5.8f;
            ribbon.style.width = 250;
            ribbon.style.height = 250 / aspect;
            if (art != null)
            {
                ribbon.style.backgroundImage = art;
                ribbon.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else
            {
                ribbon.style.backgroundColor = (Color)new Color32(122, 32, 26, 255);
                PrototypeStyle.Outline(ribbon, new Color32(201, 158, 82, 255), 4);
            }
            var words = Lettering(Spaced(text), 18, new Color32(255, 236, 196, 255), bold: true);
            words.style.textShadow = new TextShadow { offset = new Vector2(0, 1.5f), blurRadius = 1.5f, color = new Color(.18f, .02f, .02f, .9f) };
            ribbon.Add(words);
            ribbon.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            Animate(ribbon, 140, "scale");
            ribbon.RegisterCallback<PointerEnterEvent>(_ => { if (ribbon.enabledSelf) ribbon.style.scale = new Scale(Vector2.one * 1.06f); });
            ribbon.RegisterCallback<PointerLeaveEvent>(_ => ribbon.style.scale = new Scale(Vector2.one));
            ribbon.AddToClassList(RevealClass);
            return ribbon;
        }

        /// <summary>A spoil laid on velvet in a brass display frame, with its name inked on the playbill beneath.</summary>
        internal static Button Spoil(string caption, string kicker, string id, VisualElement icon, Action<Button> claim)
        {
            var button = new Button { name = id, text = string.Empty };
            button.clicked += () => claim(button);
            Bare(button);
            button.style.width = 96;
            button.style.marginLeft = button.style.marginRight = 3;
            button.style.marginBottom = 8;
            button.style.alignItems = Align.Center;
            button.style.flexShrink = 0;
            button.tooltip = "点击收下";

            var slot = new VisualElement { name = "spoil-frame", pickingMode = PickingMode.Ignore };
            slot.style.width = slot.style.height = 76;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(60));
            var frame = Art("LootFrame");
            if (frame != null)
            {
                slot.style.backgroundImage = frame;
                slot.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else
            {
                slot.style.backgroundColor = (Color)new Color32(78, 22, 28, 255);
                PrototypeStyle.Outline(slot, new Color32(176, 132, 64, 255), 6);
            }
            var halo = new VisualElement { name = "spoil-halo", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(halo);
            halo.style.backgroundImage = Spotlight();
            halo.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            halo.style.unityBackgroundImageTintColor = new Color(1f, .86f, .55f, .55f);
            halo.style.opacity = 0;
            Animate(halo, 180, "opacity");
            slot.Add(halo);
            icon.style.width = icon.style.height = 50;
            icon.style.flexGrow = 0;
            slot.Add(icon);
            Animate(slot, 160, "scale", "translate");
            button.Add(slot);

            var name = Lettering(caption, 12, Ink, bold: true);
            name.style.unityTextAlign = TextAnchor.UpperCenter;
            name.style.marginTop = 4;
            button.Add(name);
            if (!string.IsNullOrEmpty(kicker))
            {
                var kind = Lettering(kicker, 11, InkSoft);
                kind.style.unityTextAlign = TextAnchor.UpperCenter;
                kind.style.letterSpacing = 2;
                button.Add(kind);
            }

            void Lift(bool up)
            {
                slot.style.scale = new Scale(Vector2.one * (up ? 1.07f : 1f));
                slot.style.translate = new Translate(0, up ? -3 : 0);
                halo.style.opacity = up ? 1 : 0;
            }
            button.RegisterCallback<PointerEnterEvent>(_ => Lift(true));
            button.RegisterCallback<PointerLeaveEvent>(_ => Lift(false));
            button.RegisterCallback<FocusInEvent>(_ => Lift(true));
            button.RegisterCallback<FocusOutEvent>(_ => Lift(false));
            button.AddToClassList(RevealClass);
            return button;
        }

        /// <summary>Note lines written on the playbill (boss recovery, garden seed, run summary).</summary>
        internal static Label Note(string text, Color? color = null, int size = 14)
        {
            var label = Lettering(text, size, color ?? Ink);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = 6;
            label.AddToClassList(RevealClass);
            return label;
        }

        /// <summary>A ledger line in the side column: a name on the left, its figure on the right.</summary>
        internal static VisualElement Entry(string name, string value, Color valueColor)
        {
            var row = PrototypeStyle.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 22;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Rule;
            row.AddToClassList(RevealClass);
            var label = Lettering(name, 13, Ink);
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(label);
            var figure = Lettering(value, 13, valueColor, bold: true);
            figure.style.flexShrink = 0;
            figure.style.marginLeft = 6;
            row.Add(figure);
            return row;
        }

        /// <summary>Tints a scroll view's bar in faded ink so it reads as part of the paper.</summary>
        internal static ScrollView InkScroll(ScrollView scroll)
        {
            scroll.style.flexShrink = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var bar = scroll.verticalScroller;
            bar.style.width = 8;
            bar.style.borderLeftWidth = 0;
            bar.style.backgroundColor = Color.clear;
            bar.lowButton.style.display = bar.highButton.style.display = DisplayStyle.None;
            var slider = bar.slider;
            slider.style.marginTop = slider.style.marginBottom = slider.style.marginLeft = slider.style.marginRight = 0;
            slider.style.width = Length.Percent(100);
            if (slider.Q(className: BaseSlider<float>.trackerUssClassName) is { } tracker)
            {
                tracker.style.backgroundColor = Wash;
                tracker.style.left = 2;
                tracker.style.width = 4;
                tracker.style.marginLeft = tracker.style.marginRight = 0;
                tracker.style.borderTopWidth = tracker.style.borderBottomWidth = tracker.style.borderLeftWidth = tracker.style.borderRightWidth = 0;
            }
            if (slider.Q(className: BaseSlider<float>.draggerUssClassName) is { } dragger)
            {
                dragger.style.backgroundColor = InkFaint;
                dragger.style.left = 1;
                dragger.style.width = 6;
                dragger.style.marginLeft = dragger.style.marginRight = 0;
                dragger.style.borderTopWidth = dragger.style.borderBottomWidth = dragger.style.borderLeftWidth = dragger.style.borderRightWidth = 0;
                dragger.style.borderTopLeftRadius = dragger.style.borderTopRightRadius =
                    dragger.style.borderBottomLeftRadius = dragger.style.borderBottomRightRadius = 4;
            }
            return scroll;
        }

        /// <summary>
        /// Splits the playbill into the cast list (scrolling, left) and a side column (right) for the spoils or the
        /// battle's aftermath, so the spoils stay in view however long the cast list runs.
        /// </summary>
        internal static (ScrollView main, ScrollView side) Columns(VisualElement sheet, string sideTitle)
        {
            var body = PrototypeStyle.Row("settlement-body");
            body.style.flexShrink = 1;
            body.style.minHeight = 0;
            var main = InkScroll(new ScrollView(ScrollViewMode.Vertical) { name = "settlement-cast" });
            main.style.flexGrow = 1;
            main.style.flexBasis = 0;
            main.style.minWidth = 0;
            main.contentContainer.style.paddingRight = 6;
            body.Add(main);

            var rule = new VisualElement { name = "settlement-column-rule", pickingMode = PickingMode.Ignore };
            rule.style.width = 1;
            rule.style.marginLeft = 14;
            rule.style.marginRight = 12;
            rule.style.marginTop = 14;
            rule.style.marginBottom = 6;
            rule.style.backgroundColor = new Color(Ochre.r, Ochre.g, Ochre.b, .45f);
            body.Add(rule);

            var side = InkScroll(new ScrollView(ScrollViewMode.Vertical) { name = "settlement-side" });
            side.style.width = 212;
            side.style.flexShrink = 0;
            side.Add(SectionHeading(sideTitle, 160));
            body.Add(side);
            sheet.Add(body);
            return (main, side);
        }

        /// <summary>Plays the playbill's lines in one after another, top to bottom.</summary>
        internal static void Reveal(VisualElement sheet)
        {
            int order = 0;
            sheet.Query(className: RevealClass).ForEach(element =>
            {
                if (element.style.display == DisplayStyle.None) return;
                element.style.opacity = 0;
                element.style.translate = new Translate(0, 6);
                // Keep "scale" in the list: the ribbon's hover swell transitions it too.
                Animate(element, 260, "opacity", "translate", "scale");
                long delay = 220 + Mathf.Min(order++, 16) * 45L;
                element.schedule.Execute(() =>
                {
                    element.style.opacity = 1;
                    element.style.translate = new Translate(0, 0);
                }).ExecuteLater(delay);
            });
        }

        internal static Label Lettering(string words, int size, Color color, bool bold = false)
        {
            var label = PrototypeStyle.Text(words, size, color);
            label.pickingMode = PickingMode.Ignore;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        internal static void Bare(Button button)
        {
            var s = button.style;
            s.backgroundColor = Color.clear;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 0;
            s.marginTop = s.marginBottom = s.marginLeft = s.marginRight = 0;
            s.paddingTop = s.paddingBottom = s.paddingLeft = s.paddingRight = 0;
            s.overflow = Overflow.Visible;
        }

        internal static void Animate(VisualElement element, int milliseconds, params string[] properties) =>
            Transition(element, milliseconds, EasingMode.EaseOutCubic, properties);

        private static void AnimateBack(VisualElement element, int milliseconds, params string[] properties) =>
            Transition(element, milliseconds, EasingMode.EaseOutBack, properties);

        private static void Transition(VisualElement element, int milliseconds, EasingMode easing, string[] properties)
        {
            var names = new List<StylePropertyName>();
            foreach (string property in properties) names.Add(new StylePropertyName(property));
            element.style.transitionProperty = names;
            element.style.transitionDuration = new List<TimeValue> { new(milliseconds, TimeUnit.Millisecond) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(easing) };
        }

        /// <summary>A soft white radial falloff, tinted per use for the stage light and the spoil halo.</summary>
        internal static Texture2D Spotlight()
        {
            if (spotlight != null) return spotlight;
            const int size = 128;
            spotlight = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "SettlementSpotlight", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                float falloff = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                byte alpha = (byte)Mathf.RoundToInt(255 * falloff * falloff * (3 - 2 * falloff));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            spotlight.SetPixels32(pixels);
            spotlight.Apply();
            return spotlight;
        }
    }
}
