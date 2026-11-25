using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleTellerView : VisualElement
    {
        // The keyframes are registered to a 252-pixel cel, with 240 pixels of figure and a five-pixel boot inset.
        internal const float InkSpan = 240f / 252f, BootInset = 5f / 252f;
        private readonly Image fallback;
        private readonly VisualElement bubble, tip;
        private readonly Label speech;
        private readonly Texture2D[] clips = new Texture2D[8];
        private readonly BattleStage stage;
        private int lastFrame = -1;
        private TellerMood lastMood;

        public BattleTellerView(BattleStage stage, Texture2D paper)
        {
            this.stage = stage;
            name = "battle-teller";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            foreach (TellerMood mood in System.Enum.GetValues(typeof(TellerMood)))
                clips[(int)mood] = Resources.Load<Texture2D>("BattleCommentator/" + mood);
            if (stage == null)
            {
                fallback = new Image { name = "commentator-figure", pickingMode = PickingMode.Ignore };
                fallback.style.position = Position.Absolute;
                Add(fallback);
            }
            bubble = new VisualElement { name = "commentator-bubble", pickingMode = PickingMode.Ignore };
            bubble.style.position = Position.Absolute;
            bubble.style.width = Length.Percent(37);
            bubble.style.maxWidth = 316;
            Add(bubble);
            var cream = new Color32(251, 235, 201, 255);
            tip = new VisualElement { pickingMode = PickingMode.Ignore };
            tip.style.position = Position.Absolute;
            tip.style.width = 17;
            tip.style.top = Length.Percent(100);
            tip.generateVisualContent += ctx =>
            {
                float h = tip.contentRect.height;
                if (h <= 0) return;
                var pen = ctx.painter2D;
                pen.fillColor = cream;
                pen.strokeColor = new Color32(120, 76, 46, 255);
                pen.lineWidth = 1;
                pen.BeginPath();
                pen.MoveTo(new Vector2(0, 0));
                pen.LineTo(new Vector2(8.5f, h - 1));
                pen.LineTo(new Vector2(17, 0));
                pen.Fill();
                pen.Stroke();
            };
            bubble.Add(tip);
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.backgroundColor = (Color)cream;
            if (paper != null) body.style.backgroundImage = new StyleBackground(paper);
            body.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            PrototypeStyle.Outline(body, new Color32(120, 76, 46, 255), 12);
            body.style.paddingLeft = body.style.paddingRight = 12;
            body.style.paddingTop = body.style.paddingBottom = 6;
            bubble.Add(body);
            speech = PrototypeStyle.Text("", 17, new Color32(58, 32, 36, 255));
            speech.name = "commentator-line";
            PrototypeStyle.Middle(speech);
            speech.pickingMode = PickingMode.Ignore;
            body.Add(speech);
        }

        public void Draw(BattleCommentary voice, double now, Rect feet, float partyHeight,
            Dictionary<string, BattleUnitCard>.ValueCollection cards)
        {
            if (!(layout.width > 0 && layout.height > 0)) return;
            var sheet = clips[(int)voice.Mood];
            int frame = AnimationCels.Loop(System.Math.Max(0, now - voice.PoseStart), voice.Mood == TellerMood.Watch ? 2.7 : 1.65);
            Vector2 head;
            bool visible = true;
            if (stage != null)
            {
                visible = stage.DrawTeller(sheet, frame, partyHeight / layout.height, out var anchor);
                head = new Vector2(anchor.x * layout.width, (1 - anchor.y) * layout.height);
            }
            else
            {
                float h = partyHeight / InkSpan;
                float w = sheet != null ? h * sheet.width / sheet.height : h * .74f;
                head = new Vector2(layout.width * .5f, feet.yMax - worldBound.yMin - partyHeight - 18);
                fallback.style.width = w;
                fallback.style.height = h;
                fallback.style.left = head.x - w * .5f;
                fallback.style.top = head.y - h * (1 - InkSpan - BootInset);
                fallback.image = sheet;
                if (sheet != null && (frame != lastFrame || lastMood != voice.Mood))
                {
                    fallback.sourceRect = AnimationCels.RectFor(sheet, frame);
                    fallback.MarkDirtyRepaint();
                }
            }
            lastMood = voice.Mood;
            lastFrame = frame;
            bubble.style.visibility = visible && voice.Text != null ? Visibility.Visible : Visibility.Hidden;
            if (speech.text != (voice.Text ?? "")) speech.text = voice.Text ?? "";
            if (!visible || voice.Text == null || !(bubble.layout.width > 0)) return;
            // Keep the pointer at her head. Slide the paper toward the shorter rank when a boss occupies the other side.
            float wBubble = bubble.layout.width, hBubble = bubble.layout.height;
            float centered = head.x - wBubble * .5f;
            float left = 0, top = 0, best = float.PositiveInfinity;
            TryPlace(centered);
            TryPlace(head.x - wBubble + 13);
            TryPlace(head.x - 13);
            void TryPlace(float x)
            {
                x = Mathf.Clamp(x, 4, Mathf.Max(4, layout.width - wBubble - 4));
                float bottom = head.y - 9;
                foreach (var card in cards)
                {
                    var box = card.CaptionBounds;
                    if (box.xMax > worldBound.xMin + x && box.xMin < worldBound.xMin + x + wBubble)
                        bottom = Mathf.Min(bottom, box.yMin - worldBound.yMin - 4);
                }
                float y = Mathf.Max(RunTopBar.Height + 3, bottom - hBubble);
                float distance = Mathf.Abs(head.y - y - hBubble - 9) + .09f * Mathf.Abs(centered - x);
                if (distance >= best) return;
                best = distance;
                left = x;
                top = y;
            }
            bubble.style.left = left;
            bubble.style.top = top;
            tip.style.height = Mathf.Max(0, head.y - top - bubble.layout.height - 2);
            tip.style.left = Mathf.Clamp(head.x - left - 8.5f, 4, bubble.layout.width - 21);
        }
    }
}
