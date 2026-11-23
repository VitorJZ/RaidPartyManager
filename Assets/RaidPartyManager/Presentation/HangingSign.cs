using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class HangingSign : Button
    {
        private static readonly Color Rim = new Color32(135, 96, 52, 255);
        private static readonly Color Lettering = new Color32(255, 233, 184, 255);
        private readonly VisualElement rope;
        private readonly Action select;
        private readonly Texture2D grain;
        private readonly bool arrow;
        private bool mountedInScenery;
        private const string FlightLayer = "selected-sign-flight";

        internal HangingSign(string id, string caption, Action select, Texture2D grain = null, bool arrow = false)
        {
            name = id;
            text = caption;
            this.select = select;
            this.grain = grain;
            this.arrow = arrow;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.whiteSpace = WhiteSpace.Normal;
            style.fontSize = 16;
            style.color = Lettering;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.marginTop = style.marginBottom = style.marginLeft = style.marginRight = 0;
            style.paddingLeft = style.paddingRight = 7;
            style.overflow = Overflow.Visible;
            if (arrow)
            {
                style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(this, Color.clear, 0);
                generateVisualContent += DrawArrow;
            }
            else
            {
                style.backgroundColor = (Color)new Color32(74, 47, 26, 255);
                if (grain != null) style.backgroundImage = new StyleBackground(grain);
                style.unityBackgroundImageTintColor = new Color(.66f, .52f, .36f);
                PrototypeStyle.Outline(this, Rim, 4);
            }
            rope = new VisualElement { name = "sign-rope", pickingMode = PickingMode.Ignore };
            rope.style.position = Position.Absolute;
            rope.style.left = rope.style.right = 0;
            rope.style.bottom = Length.Percent(100);
            rope.style.height = 0;
            rope.generateVisualContent += DrawRope;
            Add(rope);
            RegisterCallback<GeometryChangedEvent>(_ => FitRope());
            schedule.Execute(FitRope).Every(16);

            clickable.clickedWithEventInfo += evt =>
            {
                if (panel == null || panel.visualTree.Q(FlightLayer) != null) return;
                if (mountedInScenery || evt is NavigationSubmitEvent) select();
                else FlyToBeam();
            };
        }

        internal const int TownSignWidth = 84, TownSignHeight = 58, ExitTopGap = 32;

        internal static HangingSign Exit(string id, Action leave, bool belowTopBar = false, Texture2D icon = null)
        {
            var sign = new HangingSign(id, "", leave, arrow: icon == null);
            if (icon != null)
            {
                var emblem = new Image { name = "route-node-icon", image = icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                emblem.style.width = emblem.style.height = 44;
                sign.Add(emblem);
                sign.style.left = Length.Percent(50);
                sign.style.marginLeft = -TownSignWidth / 2;
            }
            else sign.style.right = Length.Percent(9);
            sign.style.position = Position.Absolute;
            sign.style.top = (belowTopBar ? 0 : RunTopBar.Height) + ExitTopGap;
            sign.style.width = icon == null ? 68 : TownSignWidth;
            sign.style.height = icon == null ? 35 : TownSignHeight;
            return sign;
        }

        private void FlyToBeam()
        {
            var root = panel.visualTree;
            var startRect = root.WorldToLocal(worldBound);
            var layer = new VisualElement { name = FlightLayer };
            PrototypeStyle.Fill(layer);
            root.Add(layer);
            var opening = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(opening);
            float beam = root.WorldToLocal(new Vector2(0,DockPlayhouse.StageOpeningTop(panel))).y;
            opening.style.top = beam;
            opening.style.overflow = Overflow.Hidden;
            layer.Add(new StageSignMask(opening));

            // Leave the hit target in its rack so reward flights still originate at this choice.
            var board = new HangingSign("retracting-sign",text,()=>{},grain,arrow);
            board.SetEnabled(true);
            board.style.position = Position.Absolute;
            board.style.left = startRect.xMin;
            board.style.width = startRect.width;
            board.style.height = startRect.height;
            board.style.flexDirection = resolvedStyle.flexDirection;
            board.style.justifyContent = resolvedStyle.justifyContent;
            board.style.alignItems = resolvedStyle.alignItems;
            opening.Add(board);
            while (childCount > 1) board.Add(this[1]);
            style.opacity = 0;
            float y = startRect.yMin-beam;
            board.style.top = y;
            double began = Time.realtimeSinceStartupAsDouble;
            layer.schedule.Execute(() =>
            {
                if (panel == null || !enabledInHierarchy)
                {
                    Restore();
                    return;
                }
                float elapsed = (float)(Time.realtimeSinceStartupAsDouble-began);
                float t = Mathf.Clamp01(elapsed/.57f);
                // A small downward follow-through tensions the rope before it snaps taut.
                float top = t < .17f ? y + 10*Mathf.Sin(t/.17f*Mathf.PI*.5f)
                    : Mathf.Lerp(y+10,-startRect.height-12,Mathf.SmoothStep(0,1,(t-.17f)/.83f));
                board.style.top = top;
                if (elapsed < .63f) return;
                Restore();
                select();
                if (panel != null && DockPlayhouse.Moving) style.opacity = 0;
            }).Every(16);

            void Restore()
            {
                while (board.childCount > 1) Add(board[1]);
                style.opacity = 1;
                layer.RemoveFromHierarchy();
            }
        }

        private void FitRope()
        {
            if (mountedInScenery || panel == null || !float.IsFinite(rope.layout.height)) return;
            float beam = DockPlayhouse.StageOpeningTop(panel);
            // The bottom of the rope stays attached to the sign while its top stays at the beam.
            float length = Mathf.Max(0, rope.layout.height - rope.WorldToLocal(new Vector2(rope.worldBound.center.x, beam)).y);
            if (Mathf.Abs(length - rope.style.height.value.value) > .05f)
                rope.style.height = length;
        }

        internal void MountInScenery()
        {
            mountedInScenery = true;
            style.marginLeft = 0;
            rope.style.display = DisplayStyle.None;
            if (arrow) generateVisualContent -= DrawArrow;
            style.backgroundColor = Color.clear;
            style.backgroundImage = StyleKeyword.None;
            PrototypeStyle.Outline(this, Color.clear, 0);
            foreach (var child in Children()) child.style.opacity = 0;
        }

        private void DrawRope(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float y = rope.contentRect.height;
            float x = rope.contentRect.center.x;
            float span = Mathf.Min(x * .62f, 30);
            if (y <= 0) return;
            float knot = y - 9;
            void Line(float x1, float y1, float x2, float y2)
            {
                p.BeginPath(); p.MoveTo(new Vector2(x1,y1)); p.LineTo(new Vector2(x2,y2)); p.Stroke();
            }
            p.strokeColor = new Color32(83,54,30,255); p.lineWidth = 3.8f;
            if (knot > 0) Line(x,0,x,knot);
            p.strokeColor = new Color32(204,167,106,255); p.lineWidth = 1.6f;
            if (knot > 0) Line(x-.5f,0,x-.5f,knot);
            float clippedSpan = span * Mathf.Clamp01(-knot / 9);
            Line(x-clippedSpan,Mathf.Max(0,knot),x-span,y);
            Line(x+clippedSpan,Mathf.Max(0,knot),x+span,y);
        }

        private void DrawArrow(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float w = layout.width, h = layout.height;
            p.fillColor = new Color32(120,77,39,255);
            p.strokeColor = Lettering; p.lineWidth = 1.5f;
            p.BeginPath(); p.MoveTo(new Vector2(2,h*.2f)); p.LineTo(new Vector2(w*.66f,h*.2f));
            p.LineTo(new Vector2(w*.66f,2)); p.LineTo(new Vector2(w-2,h*.5f));
            p.LineTo(new Vector2(w*.66f,h-2)); p.LineTo(new Vector2(w*.66f,h*.8f));
            p.LineTo(new Vector2(2,h*.8f)); p.ClosePath(); p.Fill(); p.Stroke();
            p.strokeColor = new Color32(86,52,30,255); p.lineWidth = 1.3f;
            p.BeginPath(); p.MoveTo(new Vector2(w*.12f,h*.6f)); p.LineTo(new Vector2(w*.34f,h*.57f));
            p.LineTo(new Vector2(w*.56f,h*.6f)); p.Stroke();
            p.fillColor = Lettering;
            foreach (float x in new[] { w*.15f, w*.64f })
            {
                p.BeginPath(); p.Arc(new Vector2(x,h*.34f),1.7f,0,360,ArcDirection.Clockwise); p.Fill();
            }
        }
    }
}
