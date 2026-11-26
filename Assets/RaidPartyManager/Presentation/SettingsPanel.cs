using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// The manager's proclamation scroll: unrolls from its top rod to show the volume sliders (gold-coin handles on
    /// quill strokes) and any run actions in pen lettering, and rolls back up when dismissed.
    /// </summary>
    internal sealed class SettingsPanel : VisualElement
    {
        private const string ArtRoot = "Settings/";
        private const float UnrollSeconds = .52f, RollUpSeconds = .26f;
        // Brocade borders of the mounting, in texture pixels, kept unstretched by 9-slicing.
        private const int SliceTop = 206, SliceBottom = 186, SliceSide = 30, ScrollArtWidth = 988;
        private const float RollerAspect = 107f / 1200f;

        private static readonly Color Ink = new Color32(34, 26, 22, 255);
        private static readonly Color InkSoft = new Color32(34, 26, 22, 150);
        private static readonly Color Cinnabar = new Color32(146, 26, 32, 255);
        private static readonly Dictionary<string, string> Lettering = new()
        {
            ["设置"] = "WordSettings", ["主音量"] = "WordMaster", ["背景声"] = "WordAmbience", ["音效"] = "WordEffects",
            ["保存并退出"] = "WordSave", ["放弃"] = "WordAbandon", ["返回主菜单"] = "WordMenu",
            ["重置讨伐"] = "WordReset", ["敌方编辑"] = "WordEnemy", ["收起"] = "WordClose"
        };

        private readonly VisualElement scroll, sheet, content;
        private readonly ScrollView options;
        private readonly Button close;
        private readonly Action dismissed;
        private readonly List<(Button button, VisualElement word, VisualElement underline)> actions = new();
        private bool closing;

        internal SettingsPanel(Action dismissed)
        {
            this.dismissed = dismissed;
            name = "run-settings";
            PrototypeStyle.Fill(this);
            style.backgroundColor = new Color(.02f, .03f, .05f, 0);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            Animate(this, 300, "background-color");

            scroll = new VisualElement { name = "settings-scroll", pickingMode = PickingMode.Ignore };
            scroll.style.width = Length.Percent(88);
            scroll.style.maxWidth = 420;
            scroll.style.alignItems = Align.Stretch;
            Add(scroll);
            scroll.Add(Roller("settings-roller-top"));

            sheet = new VisualElement { name = "settings-sheet" };
            sheet.style.overflow = Overflow.Hidden;
            sheet.style.marginLeft = sheet.style.marginRight = Length.Percent(4.5f);
            sheet.style.height = 0;
            sheet.style.flexShrink = 0;
            var paper = Art("Scroll");
            if (paper.value.texture != null)
            {
                sheet.style.backgroundImage = paper;
                sheet.style.unitySliceTop = SliceTop;
                sheet.style.unitySliceBottom = SliceBottom;
                sheet.style.unitySliceLeft = sheet.style.unitySliceRight = SliceSide;
            }
            else
                sheet.style.backgroundColor = (Color)new Color32(226, 212, 184, 255);
            scroll.Add(sheet);
            scroll.Add(Roller("settings-roller-bottom"));

            content = new VisualElement { name = "settings-content" };
            content.style.flexShrink = 0;
            content.style.paddingTop = 58;
            content.style.paddingBottom = 56;
            content.style.paddingLeft = content.style.paddingRight = 34;
            content.style.alignItems = Align.Stretch;
            sheet.Add(content);

            var title = Word("设置", 46, Ink, PrototypeStyle.Accent);
            title.style.alignSelf = Align.Center;
            title.style.marginBottom = 4;
            content.Add(title);
            var rule = Art("Underline");
            if (rule.value.texture != null)
            {
                var stroke = new VisualElement { pickingMode = PickingMode.Ignore };
                stroke.style.alignSelf = Align.Center;
                stroke.style.width = 120;
                stroke.style.height = 20;
                stroke.style.backgroundImage = rule;
                stroke.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                stroke.style.unityBackgroundImageTintColor = new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, .8f);
                stroke.style.marginBottom = 12;
                content.Add(stroke);
            }

            options = new ScrollView(ScrollViewMode.Vertical) { name = "settings-options" };
            options.style.minHeight = 0;
            options.style.flexShrink = 1;
            options.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            content.Add(options);
            AddVolume("主音量", "master-volume", VolumeChannel.Master);
            AddVolume("背景声", "ambience-volume", VolumeChannel.Ambience);
            AddVolume("音效", "effects-volume", VolumeChannel.Effects);

            close = Entry("收起", "close-settings", Close, 30);
            close.style.alignSelf = Align.Center;
            close.style.width = 120;
            close.style.marginTop = 14;
            content.Add(close);

            schedule.Execute(Roll).Every(16);
            scroll.RegisterCallback<GeometryChangedEvent>(_ => FitMounting());
            RegisterCallback<GeometryChangedEvent>(_ => FitToScreen());
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                evt.StopPropagation();
                Close();
            });
            RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target == this) GameAudio.SaveVolumes();
            });
        }

        private void AddVolume(string label, string id, VolumeChannel channel)
        {
            var row = PrototypeStyle.Row();
            row.style.alignItems = Align.Center;
            row.style.minHeight = 46;
            row.Add(Word(label, 26, Ink, Ink, 84));

            var slider = new SliderInt(0, 100) { name = id, value = GameAudio.Volume(channel) };
            slider.style.flexGrow = 1;
            slider.style.minWidth = 0;
            slider.style.marginLeft = 6;
            slider.style.marginRight = 6;
            DressSlider(slider);
            row.Add(slider);

            var value = PrototypeStyle.Text($"{slider.value}%", 15, Ink);
            value.name = id + "-value";
            value.style.width = 46;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(value);
            options.Add(row);

            bool previewPending = false;
            slider.RegisterValueChangedCallback(evt =>
            {
                GameAudio.SetVolume(channel, evt.newValue);
                value.text = $"{evt.newValue}%";
                previewPending = channel != VolumeChannel.Ambience;
            });
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => Preview(), TrickleDown.TrickleDown);
            slider.RegisterCallback<KeyUpEvent>(_ => Preview());
            void Preview()
            {
                if (!previewPending) return;
                previewPending = false;
                GameAudio.Play(SoundCue.UiConfirm, .50f);
            }
        }

        /// <summary>Replaces the stock slider look with an ink stroke, a cinnabar fill and a bronze coin.</summary>
        private static void DressSlider(SliderInt slider)
        {
            var track = slider.Q("unity-tracker");
            var dragger = slider.Q("unity-dragger");
            var border = slider.Q("unity-dragger-border");
            var drag = slider.Q("unity-drag-container");
            if (track == null || dragger == null || drag == null) return;

            var stroke = Art("Track");
            track.style.backgroundColor = Color.clear;
            track.style.borderTopWidth = track.style.borderBottomWidth = track.style.borderLeftWidth = track.style.borderRightWidth = 0;
            track.style.height = 10;
            track.style.marginTop = -5;
            if (stroke.value.texture != null)
            {
                track.style.backgroundImage = stroke;
                track.style.unityBackgroundImageTintColor = InkSoft;
            }
            else
                track.style.backgroundColor = InkSoft;

            var fill = new VisualElement { name = slider.name + "-fill", pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.overflow = Overflow.Hidden;
            var wash = new VisualElement { pickingMode = PickingMode.Ignore };
            wash.style.position = Position.Absolute;
            wash.style.left = wash.style.top = 0;
            if (stroke.value.texture != null)
            {
                wash.style.backgroundImage = stroke;
                wash.style.unityBackgroundImageTintColor = Cinnabar;
            }
            else
                wash.style.backgroundColor = Cinnabar;
            fill.Add(wash);
            track.parent.Insert(track.parent.IndexOf(track) + 1, fill);

            float coinSize = 30;
            dragger.style.width = dragger.style.height = coinSize;
            dragger.style.marginTop = -coinSize / 2;
            dragger.style.top = Length.Percent(50);
            dragger.style.backgroundColor = Color.clear;
            dragger.style.borderTopWidth = dragger.style.borderBottomWidth = dragger.style.borderLeftWidth = dragger.style.borderRightWidth = 0;
            var coin = Art("Coin");
            if (coin.value.texture != null) dragger.style.backgroundImage = coin;
            else
            {
                dragger.style.backgroundColor = (Color)new Color32(150, 110, 52, 255);
                dragger.style.borderTopLeftRadius = dragger.style.borderTopRightRadius =
                    dragger.style.borderBottomLeftRadius = dragger.style.borderBottomRightRadius = coinSize / 2;
            }
            dragger.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            Animate(dragger, 140, "scale");
            if (border != null) border.style.display = DisplayStyle.None;
            slider.style.height = 34;

            slider.RegisterCallback<PointerEnterEvent>(_ => dragger.style.scale = new Scale(Vector2.one * 1.15f));
            slider.RegisterCallback<PointerLeaveEvent>(_ => dragger.style.scale = new Scale(Vector2.one));
            slider.RegisterCallback<FocusInEvent>(_ => dragger.style.scale = new Scale(Vector2.one * 1.15f));
            slider.RegisterCallback<FocusOutEvent>(_ => dragger.style.scale = new Scale(Vector2.one));
            slider.RegisterValueChangedCallback(_ => Sync());
            drag.RegisterCallback<GeometryChangedEvent>(_ => Sync());

            void Sync()
            {
                var area = track.layout;
                if (float.IsNaN(area.width) || area.width <= 0) return;
                float knob = dragger.layout.width;
                if (float.IsNaN(knob)) knob = 0;
                float centre = area.x + knob / 2 + (area.width - knob) * (slider.value - slider.lowValue) /
                    Mathf.Max(1f, slider.highValue - slider.lowValue);
                fill.style.left = area.x;
                fill.style.top = area.y;
                fill.style.height = area.height;
                fill.style.width = Mathf.Clamp(centre - area.x, 0, area.width);
                wash.style.width = area.width;
                wash.style.height = area.height;
            }
            dragger.RegisterCallback<GeometryChangedEvent>(_ => Sync());
        }

        internal Button AddOption(string text, string id, Action action)
        {
            var button = Entry(text, id, action, 28);
            button.style.marginTop = 2;
            options.Add(button);
            return button;
        }

        /// <summary>Marks an action as destructive so its lettering is written in cinnabar.</summary>
        internal static void MarkDanger(Button button)
        {
            button.AddToClassList("settings-danger");
            button.userData = Cinnabar;
            var word = button.Q("settings-lettering");
            if (word != null) word.style.unityBackgroundImageTintColor = Cinnabar;
        }

        internal void AddFeedback(Label message)
        {
            message.style.color = Cinnabar;
            message.style.unityTextAlign = TextAnchor.MiddleCenter;
            message.style.marginTop = 4;
            options.Add(message);
        }

        internal void Show(VisualElement parent)
        {
            parent.Add(this);
            GameAudio.Play(SoundCue.UiParchmentOpen, .35f);
            schedule.Execute(() => style.backgroundColor = new Color(.02f, .03f, .05f, .66f)).StartingIn(16);
            close.Focus();
        }

        internal void Close()
        {
            if (closing) return;
            closing = true;
            GameAudio.SaveVolumes();
            name = "run-settings-closing";
            pickingMode = PickingMode.Ignore;
            // While it rolls up, the old scroll is scenery only: nothing in it can be picked or found by name.
            foreach (var element in this.Query<VisualElement>().ToList())
            {
                element.pickingMode = PickingMode.Ignore;
                if (element != this) element.name = null;
            }
            style.backgroundColor = new Color(.02f, .03f, .05f, 0);
            dismissed();
        }

        private bool mounted;
        private float unrolled;
        private double lastRoll = -1;

        /// <summary>
        /// Drives the unroll and roll-up by hand: the sheet height is the eased progress times the content's
        /// current height, so content that settles mid-animation only retargets the motion instead of restarting it.
        /// </summary>
        private void Roll()
        {
            double now = Time.unscaledTimeAsDouble;
            float step = lastRoll < 0 ? 0 : (float)(now - lastRoll);
            lastRoll = now;
            if (closing && (!mounted || unrolled <= 0)) { RemoveFromHierarchy(); return; }
            // Wait until the mounting margins are known so the scroll opens in one movement.
            if (!mounted) return;
            float full = content.layout.height;
            if (float.IsNaN(full) || full <= 0) return;
            unrolled = closing
                ? Mathf.Max(0, unrolled - step / RollUpSeconds)
                : Mathf.Min(1, unrolled + step / UnrollSeconds);
            float eased = closing ? unrolled * unrolled : 1 - Mathf.Pow(1 - unrolled, 3);
            sheet.style.height = eased * full;
            if (closing && unrolled <= 0) RemoveFromHierarchy();
        }

        private void FitMounting()
        {
            float width = scroll.layout.width;
            if (float.IsNaN(width) || width <= 0) return;
            float sheetWidth = width * .91f, scale = sheetWidth / ScrollArtWidth;
            sheet.style.unitySliceScale = scale;
            content.style.paddingTop = SliceTop * scale + 16;
            content.style.paddingBottom = SliceBottom * scale + 14;
            content.style.paddingLeft = content.style.paddingRight = Mathf.Max(24, sheetWidth * .09f);
            foreach (var roller in scroll.Query(className: "settings-roller").ToList())
                roller.style.height = width * RollerAspect;
            mounted = true;
        }

        private void FitToScreen()
        {
            float available = layout.height;
            if (float.IsNaN(available) || available <= 0) return;
            options.style.maxHeight = Mathf.Max(120, available - 400);
        }

        private Button Entry(string text, string id, Action action, float letteringHeight)
        {
            var button = new Button(action) { name = id, text = text };
            var style = button.style;
            style.backgroundColor = Color.clear;
            style.color = Color.clear;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 0;
            style.marginLeft = style.marginRight = 0;
            style.height = letteringHeight + 14;
            style.flexShrink = 0;
            style.overflow = Overflow.Visible;

            var underline = new VisualElement { name = "settings-underline", pickingMode = PickingMode.Ignore };
            underline.style.position = Position.Absolute;
            underline.style.left = Length.Percent(18);
            underline.style.right = Length.Percent(18);
            underline.style.bottom = -2;
            underline.style.height = letteringHeight * .55f;
            underline.style.backgroundImage = Art("Underline");
            underline.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            underline.style.unityBackgroundImageTintColor = new Color(Ink.r, Ink.g, Ink.b, .55f);
            underline.style.opacity = 0;
            underline.style.scale = new Scale(new Vector2(.4f, 1));
            underline.style.transformOrigin = new TransformOrigin(0, Length.Percent(50));
            Animate(underline, 200, "opacity", "scale");
            button.Add(underline);

            var word = Word(text, letteringHeight, Ink, Cinnabar);
            word.name = "settings-lettering";
            word.style.position = Position.Absolute;
            word.style.left = word.style.right = 0;
            word.style.top = 4;
            word.style.width = StyleKeyword.Auto;
            button.Add(word);

            actions.Add((button, word, underline));
            button.RegisterCallback<PointerEnterEvent>(_ => Light(true));
            button.RegisterCallback<PointerLeaveEvent>(_ => Light(button.focusController?.focusedElement == button));
            button.RegisterCallback<FocusInEvent>(_ => Light(true));
            button.RegisterCallback<FocusOutEvent>(_ => Light(false));
            return button;

            void Light(bool lit)
            {
                var rest = button.userData is Color danger ? danger : Ink;
                underline.style.opacity = lit ? 1 : 0;
                underline.style.scale = new Scale(new Vector2(lit ? 1 : .4f, 1));
                underline.style.unityBackgroundImageTintColor = lit && button.userData is Color
                    ? new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, .6f) : new Color(Ink.r, Ink.g, Ink.b, .55f);
                SetInk(word, lit ? Cinnabar : rest);
            }
        }

        /// <summary>Brush lettering for <paramref name="text"/>, falling back to a label when no artwork exists.</summary>
        private static VisualElement Word(string text, float height, Color ink, Color litInk, float width = 0)
        {
            var art = Lettering.TryGetValue(text, out string file) ? Resources.Load<Texture2D>(ArtRoot + file) : null;
            if (art == null)
            {
                var label = PrototypeStyle.Text(text, Mathf.RoundToInt(height * .62f), ink);
                label.pickingMode = PickingMode.Ignore;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.height = height;
                if (width > 0) label.style.width = width;
                return label;
            }
            var word = new VisualElement { pickingMode = PickingMode.Ignore };
            word.style.height = height;
            word.style.width = width > 0 ? width : height * art.width / art.height;
            word.style.flexShrink = 0;
            word.style.backgroundImage = art;
            word.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            word.style.unityBackgroundImageTintColor = ink;
            Animate(word, 180, "-unity-background-image-tint-color");
            return word;
        }

        private static void SetInk(VisualElement word, Color ink)
        {
            if (word is Label label) label.style.color = ink;
            else word.style.unityBackgroundImageTintColor = ink;
        }

        private static VisualElement Roller(string id)
        {
            var roller = new VisualElement { name = id, pickingMode = PickingMode.Ignore };
            roller.AddToClassList("settings-roller");
            roller.style.height = 30;
            roller.style.flexShrink = 0;
            var art = Art("Roller");
            if (art.value.texture != null)
            {
                roller.style.backgroundImage = art;
                roller.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                roller.style.backgroundColor = (Color)new Color32(70, 40, 26, 255);
                roller.style.marginLeft = roller.style.marginRight = Length.Percent(2);
            }
            return roller;
        }

        private static StyleBackground Art(string file) =>
            new(Resources.Load<Texture2D>(ArtRoot + file));

        private static void Animate(VisualElement element, int milliseconds, params string[] properties)
        {
            var names = new List<StylePropertyName>();
            foreach (string property in properties) names.Add(new StylePropertyName(property));
            element.style.transitionProperty = names;
            element.style.transitionDuration = new List<TimeValue> { new(milliseconds, TimeUnit.Millisecond) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
        }
    }
}
