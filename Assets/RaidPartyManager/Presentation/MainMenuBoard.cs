using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// The title screen's bounty board: an iron-bound oak notice board hanging into frame on two chains,
    /// with the menu actions pinned to it as parchment notices and a wooden tag hung beneath.
    /// </summary>
    internal sealed class MainMenuBoard : VisualElement
    {
        // Anchor points measured on the cropped artwork, as fractions of each texture.
        private static readonly Vector2[] ChainAnchors = { new(.186f, 0), new(.814f, 0) };
        private const float BoardAspect = 1113f / 1100f;
        private static readonly Color Ink = new Color32(38, 27, 18, 255);
        private static readonly Color InkLit = new Color32(140, 24, 30, 255);

        private readonly VisualElement board = new() { name = "menu-board", pickingMode = PickingMode.Ignore };
        private readonly VisualElement tagHook = new() { name = "menu-board-tag-hook", pickingMode = PickingMode.Ignore };
        private readonly List<Notice> notices = new();
        private readonly double started = Time.unscaledTimeAsDouble;
        private float boardTop, boardWidth;
        private bool settled;

        private sealed class Notice
        {
            public Button Button;
            public VisualElement Shadow, Paper;
            public Label Caption, Detail;
            public VisualElement Word;
            public float Tilt, Width;
            public Vector2 Spot;
            public bool Hovered, Focused;
            public Color RestInk = Ink, LitInk = InkLit, Outline = Ink, LitOutline = InkLit;
        }

        internal MainMenuBoard()
        {
            name = "main-menu";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.top = 0;
            style.transformOrigin = new TransformOrigin(Length.Percent(50), 0);
            generateVisualContent += DrawRopes;

            board.style.position = Position.Absolute;
            board.style.backgroundImage = Load("MenuBoard");
            Add(board);

            tagHook.style.position = Position.Absolute;
            tagHook.style.transformOrigin = new TransformOrigin(Length.Percent(50), 0);
            Add(tagHook);

            style.translate = new Translate(0, Length.Percent(-110));
            schedule.Execute(Sway).Every(16);
        }

        internal Button AddCommission(string caption, string id, Action action, string detail, string word) =>
            Pin(caption, id, action, "MenuNoteCommission", 1000f / 756f, new Vector2(.5f, .29f), .56f, -1.4f, 34, detail,
                word, string.IsNullOrEmpty(detail) ? new Vector4(15, 27, 19, 30) : new Vector4(15, 20, 19, 42));

        internal Button AddNotice(string caption, string id, Action action, bool folded, string word) => folded
            ? Pin(caption, id, action, "MenuNoteFolded", 820f / 242f, new Vector2(.68f, .765f), .38f, -2.2f, 20, null,
                word, new Vector4(11, 21, 13, 11))
            : Pin(caption, id, action, "MenuNoteTorn", 820f / 276f, new Vector2(.31f, .745f), .35f, 2.6f, 20, null,
                word, new Vector4(16, 17, 9, 13));

        internal Button AddTag(string caption, string id, Action action, string word)
        {
            var notice = CreateNotice(caption, id, action, "MenuTag", 686f / 468f, Vector2.zero, 0, 18, null,
                word, new Vector4(27, 52, 27, 15));
            if (notice.Word != null)
            {
                var shade = new VisualElement { pickingMode = PickingMode.Ignore };
                shade.style.position = Position.Absolute;
                shade.style.left = shade.style.right = shade.style.top = shade.style.bottom = 0;
                shade.style.backgroundImage = notice.Word.style.backgroundImage;
                shade.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                shade.style.unityBackgroundImageTintColor = new Color(.1f, .04f, .02f, .9f);
                shade.style.translate = new Translate(1, 2);
                notice.Word.Add(shade);
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(face);
                face.style.backgroundImage = notice.Word.style.backgroundImage;
                face.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                notice.Word.style.backgroundImage = StyleKeyword.None;
                notice.Word.Add(face);
                notice.Word = face;
                Animate(face, 180, "-unity-background-image-tint-color");
            }
            notice.RestInk = new Color32(238, 214, 170, 255);
            notice.LitInk = new Color32(255, 226, 150, 255);
            notice.Outline = notice.LitOutline = new Color32(40, 18, 10, 255);
            notice.Caption.style.unityTextOutlineWidth = .12f;
            Refresh(notice);
            notice.Caption.style.top = Length.Percent(41);
            notice.Caption.style.textShadow = new TextShadow { color = new Color(0, 0, 0, .8f), offset = new Vector2(0, 1), blurRadius = 2 };
            tagHook.Add(notice.Button);
            return notice.Button;
        }

        private Button Pin(string caption, string id, Action action, string art, float aspect, Vector2 spot,
            float width, float tilt, int size, string detail, string word, Vector4 wordInsets)
        {
            var notice = CreateNotice(caption, id, action, art, aspect, spot, width, size, detail, word, wordInsets);
            notice.Tilt = tilt;
            board.Add(notice.Button);
            return notice.Button;
        }

        private Notice CreateNotice(string caption, string id, Action action, string art, float aspect, Vector2 spot,
            float width, int size, string detail, string word, Vector4 wordInsets)
        {
            var button = new Button(action) { name = id, text = caption };
            var style = button.style;
            style.position = Position.Absolute;
            style.backgroundColor = Color.clear;
            style.color = Color.clear;
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 0;
            style.borderTopWidth = style.borderRightWidth = style.borderBottomWidth = style.borderLeftWidth = 0;
            style.overflow = Overflow.Visible;
            style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(6));
            Animate(button, 180, "scale", "rotate");

            var shadow = Layer(art);
            shadow.style.unityBackgroundImageTintColor = new Color(0, 0, 0, .5f);
            shadow.style.translate = new Translate(3, 5);
            Animate(shadow, 180, "translate", "-unity-background-image-tint-color");
            var paper = Layer(art);
            paper.style.unityBackgroundImageTintColor = new Color(.9f, .88f, .85f);
            Animate(paper, 180, "-unity-background-image-tint-color");
            button.Add(shadow);
            button.Add(paper);

            var label = new Label(caption) { name = id + "-caption", pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = label.style.right = 0;
            label.style.top = 0;
            label.style.bottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = Ink;
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = label.style.marginBottom = 0;
            label.style.unityTextOutlineWidth = .24f;
            label.style.unityTextOutlineColor = Ink;
            Animate(label, 180, "color");
            button.Add(label);

            Label detailLabel = null;
            if (!string.IsNullOrEmpty(detail))
            {
                detailLabel = new Label(detail) { name = "menu-run-progress", pickingMode = PickingMode.Ignore };
                detailLabel.style.position = Position.Absolute;
                detailLabel.style.left = detailLabel.style.right = 0;
                detailLabel.style.bottom = Length.Percent(17);
                detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                detailLabel.style.color = (Color)new Color32(126, 40, 26, 255);
                detailLabel.style.letterSpacing = 2;
                button.Add(detailLabel);
                label.style.bottom = Length.Percent(12);
            }

            VisualElement wordArt = null;
            var wordTexture = string.IsNullOrEmpty(word) ? null : Resources.Load<Texture2D>("MainMenu/" + word);
            if (wordTexture != null)
            {
                wordArt = new VisualElement { name = id + "-lettering", pickingMode = PickingMode.Ignore };
                wordArt.style.position = Position.Absolute;
                wordArt.style.left = Length.Percent(wordInsets.x);
                wordArt.style.top = Length.Percent(wordInsets.y);
                wordArt.style.right = Length.Percent(wordInsets.z);
                wordArt.style.bottom = Length.Percent(wordInsets.w);
                wordArt.style.backgroundImage = wordTexture;
                wordArt.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                Animate(wordArt, 180, "-unity-background-image-tint-color");
                button.Add(wordArt);
                label.style.display = DisplayStyle.None;
            }

            var notice = new Notice
            {
                Word = wordArt,
                Button = button, Shadow = shadow, Paper = paper, Caption = label, Detail = detailLabel,
                Spot = spot, Width = width
            };
            notice.Button.userData = aspect;
            button.RegisterCallback<PointerEnterEvent>(_ => { notice.Hovered = true; Refresh(notice); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { notice.Hovered = false; Refresh(notice); });
            button.RegisterCallback<FocusInEvent>(_ => { notice.Focused = true; Refresh(notice); });
            button.RegisterCallback<FocusOutEvent>(_ => { notice.Focused = false; Refresh(notice); });
            notices.Add(notice);
            Refresh(notice);
            return notice;

            VisualElement Layer(string texture)
            {
                var layer = new VisualElement { pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(layer);
                layer.style.backgroundImage = Load(texture);
                return layer;
            }
        }

        private static void Refresh(Notice notice)
        {
            bool lit = notice.Hovered || notice.Focused;
            notice.Button.style.scale = new Scale(Vector2.one * (lit ? 1.06f : 1f));
            notice.Button.style.rotate = new Rotate(lit ? notice.Tilt * .25f : notice.Tilt);
            notice.Shadow.style.translate = lit ? new Translate(7, 12) : new Translate(3, 5);
            notice.Shadow.style.unityBackgroundImageTintColor = new Color(0, 0, 0, lit ? .38f : .5f);
            notice.Paper.style.unityBackgroundImageTintColor = lit ? Color.white : new Color(.9f, .88f, .85f);
            notice.Caption.style.color = lit ? notice.LitInk : notice.RestInk;
            notice.Caption.style.unityTextOutlineColor = lit ? notice.LitOutline : notice.Outline;
            if (notice.Word != null)
                notice.Word.style.unityBackgroundImageTintColor = lit ? notice.LitInk : notice.RestInk;
        }

        /// <summary>Sizes the hanging board for the current screen and returns the space it covers.</summary>
        internal Rect Fit(float width, float height, bool wide)
        {
            boardWidth = Mathf.Min(height * (wide ? .74f : .56f), width * (wide ? .42f : .86f));
            float boardHeight = boardWidth * BoardAspect;
            boardTop = wide ? height * .085f : height * .30f;
            float left = wide ? width * .045f : (width - boardWidth) / 2;
            style.left = left;
            style.width = boardWidth;
            style.height = boardTop + boardHeight;
            board.style.top = boardTop;
            board.style.width = boardWidth;
            board.style.height = boardHeight;

            foreach (var notice in notices)
            {
                if (notice.Button.parent != board) continue;
                float aspect = (float)notice.Button.userData;
                float noteWidth = boardWidth * notice.Width;
                var style = notice.Button.style;
                style.width = noteWidth;
                style.height = noteWidth / aspect;
                style.left = boardWidth * notice.Spot.x - noteWidth / 2;
                style.top = boardHeight * notice.Spot.y;
                float scale = boardWidth / 520f;
                notice.Caption.style.fontSize = Mathf.Round((notice.Width > .45f ? 44 : 22) * scale);
                notice.Caption.style.letterSpacing = Mathf.Round((notice.Width > .45f ? 12 : 6) * scale);
                if (notice.Detail != null) notice.Detail.style.fontSize = Mathf.Round(15 * scale);
            }

            float tagWidth = boardWidth * .27f;
            tagHook.style.left = boardWidth * .5f - tagWidth / 2;
            tagHook.style.top = boardTop + boardHeight * .965f;
            tagHook.style.width = tagWidth;
            tagHook.style.height = tagWidth * 468f / 686f;
            foreach (var child in tagHook.Children())
            {
                PrototypeStyle.Fill(child);
                foreach (var notice in notices)
                    if (notice.Button == child)
                    {
                        notice.Caption.style.fontSize = Mathf.Round(17 * boardWidth / 520f);
                        notice.Caption.style.letterSpacing = Mathf.Round(4 * boardWidth / 520f);
                    }
            }
            MarkDirtyRepaint();

            if (!settled)
            {
                settled = true;
                Animate(this, 900, "translate");
                style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutBack) };
                schedule.Execute(() => style.translate = new Translate(0, 0)).StartingIn(80);
            }
            return new Rect(left, 0, boardWidth, boardTop + boardHeight + tagWidth * .7f);
        }

        private void Sway()
        {
            float time = (float)(Time.unscaledTimeAsDouble - started);
            style.rotate = new Rotate(Mathf.Sin(time * .85f) * .45f + Mathf.Sin(time * 2.1f) * .08f);
            tagHook.style.rotate = new Rotate(Mathf.Sin(time * 1.35f + 1.1f) * 2.2f);
        }

        private void DrawRopes(MeshGenerationContext context)
        {
            if (boardWidth <= 0) return;
            var painter = context.painter2D;
            float link = Mathf.Max(9, boardWidth * .03f), thickness = Mathf.Max(2, boardWidth * .0065f);
            painter.lineCap = LineCap.Round;
            foreach (var anchor in ChainAnchors)
            {
                float x = anchor.x * boardWidth, bottom = boardTop + 2;
                int index = 0;
                // Forged links alternate between face-on ovals and edge-on bars, continuing the painted chains upwards.
                for (float y = bottom; y > -link; y -= link * .78f, index++)
                {
                    painter.strokeColor = new Color32(24, 22, 24, 255);
                    painter.lineWidth = thickness + 1.6f;
                    Link(x, y, index % 2 == 0);
                    painter.strokeColor = index % 2 == 0 ? new Color32(92, 88, 86, 255) : new Color32(70, 66, 64, 255);
                    painter.lineWidth = thickness;
                    Link(x, y, index % 2 == 0);
                }
            }

            void Link(float x, float bottom, bool open)
            {
                float top = bottom - link;
                painter.BeginPath();
                if (open)
                {
                    float half = link * .3f;
                    painter.MoveTo(new Vector2(x - half, top + half));
                    painter.ArcTo(new Vector2(x - half, top), new Vector2(x, top), half);
                    painter.ArcTo(new Vector2(x + half, top), new Vector2(x + half, top + half), half);
                    painter.LineTo(new Vector2(x + half, bottom - half));
                    painter.ArcTo(new Vector2(x + half, bottom), new Vector2(x, bottom), half);
                    painter.ArcTo(new Vector2(x - half, bottom), new Vector2(x - half, bottom - half), half);
                    painter.ClosePath();
                }
                else
                {
                    painter.MoveTo(new Vector2(x, top + link * .1f));
                    painter.LineTo(new Vector2(x, bottom - link * .1f));
                }
                painter.Stroke();
            }
        }

        internal static StyleBackground Load(string texture) =>
            new(Resources.Load<Texture2D>("MainMenu/" + texture));

        internal static void Animate(VisualElement element, int milliseconds, params string[] properties)
        {
            var names = new List<StylePropertyName>();
            foreach (string property in properties) names.Add(new StylePropertyName(property));
            element.style.transitionProperty = names;
            element.style.transitionDuration = new List<TimeValue> { new(milliseconds, TimeUnit.Millisecond) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
        }
    }
}
