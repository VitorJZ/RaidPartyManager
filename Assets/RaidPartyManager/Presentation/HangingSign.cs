using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal enum SignFace { Plank, Square, Arrow }

    internal sealed class HangingSign : Button
    {
        internal static readonly Color Lettering = new Color32(255, 226, 160, 255);
        internal static readonly Color LitLettering = new Color32(255, 243, 212, 255);
        internal static readonly TextShadow Carved = new() { offset = new Vector2(0, 1.5f), blurRadius = 1.5f, color = new Color(.1f, .05f, .02f, .95f) };
        internal static readonly TextShadow Gleam = new() { offset = Vector2.zero, blurRadius = 7, color = new Color(1f, .72f, .3f, .7f) };
        internal static readonly Color Resting = new Color(.82f, .8f, .78f);
        private static readonly Color HempShade = new Color32(78, 52, 29, 255), HempStrand = new Color32(201, 163, 104, 255);

        /// <summary>Painted board split from its hanging rings so the board alone can be 9-sliced.</summary>
        private sealed class Art
        {
            internal Texture2D Board, Ring;
            internal int Side, Top, Bottom, RingInset;
            internal float RopeX = .5f;
            static Art plank, square, arrow;

            static Texture2D Load(string file) => Resources.Load<Texture2D>("HangingSign/" + file);
            internal static Art For(SignFace face) => face switch
            {
                SignFace.Square => square ??= new Art { Board = Load("SignSquareBoard"), Ring = Load("SignSquareRing"), Side = 100, Top = 104, Bottom = 108, RingInset = 32 },
                SignFace.Arrow => arrow ??= new Art { Board = Load("SignArrow"), RopeX = .471f },
                _ => plank ??= new Art { Board = Load("SignPlankBoard"), Ring = Load("SignPlankRing"), Side = 128, Top = 84, Bottom = 98, RingInset = 35 },
            };
        }

        private readonly VisualElement rope;
        private readonly Image[] rings = new Image[2];
        private readonly Action select;
        private readonly SignFace face;
        private readonly Art art;
        private float scale = 1, sway;
        private double swayStart = double.NegativeInfinity;
        private bool mountedInScenery, lit, pressed;
        /// <summary>
        /// A sign mounted in the scenery keeps this element as its lettering and hit area, while a 3D board
        /// mirrors the same hover, press and sway. Choosing it hands the rise to the beam to that board.
        /// </summary>
        internal Action<Action> RetractInScenery;
        internal bool Lit => lit;
        internal bool Pressed => pressed;
        /// <summary>The current swing in degrees, clockwise on screen.</summary>
        internal float Sway => sway;
        /// <summary>False for signs such as pagers that act in place instead of flying up to the beam.</summary>
        internal bool FliesWhenChosen = true;
        private const string FlightLayer = "selected-sign-flight";

        internal HangingSign(string id, string caption, Action select, SignFace face = SignFace.Plank)
        {
            name = id;
            text = caption;
            this.select = select;
            this.face = face;
            art = Art.For(face);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.whiteSpace = WhiteSpace.Normal;
            style.fontSize = 16;
            style.color = Lettering;
            style.textShadow = Carved;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.marginTop = style.marginBottom = style.marginLeft = style.marginRight = 0;
            style.paddingLeft = style.paddingRight = 7;
            style.paddingTop = style.paddingBottom = 2;
            style.overflow = Overflow.Visible;
            style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(this, Color.clear, 0);
            style.transformOrigin = new TransformOrigin(Length.Percent(50), 0);
            if (art.Board != null)
            {
                style.backgroundImage = art.Board;
                style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                style.unitySliceLeft = style.unitySliceRight = art.Side;
                style.unitySliceTop = art.Top;
                style.unitySliceBottom = art.Bottom;
            }
            else style.backgroundColor = (Color)new Color32(74, 47, 26, 255);
            style.unityBackgroundImageTintColor = Resting;
            rope = new VisualElement { name = "sign-rope", pickingMode = PickingMode.Ignore };
            rope.style.position = Position.Absolute;
            rope.style.left = rope.style.right = 0;
            rope.style.bottom = Length.Percent(100);
            rope.style.height = 0;
            rope.generateVisualContent += DrawRope;
            if (art.Ring != null)
                for (int i = 0; i < 2; i++)
                {
                    rings[i] = new Image { image = art.Ring, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    rings[i].style.position = Position.Absolute;
                    rings[i].style.bottom = 0;
                    rope.Add(rings[i]);
                }
            Add(rope);
            RegisterCallback<GeometryChangedEvent>(_ => { Fit(); FitRope(); });
            schedule.Execute(Tick).Every(16);
            RegisterCallback<PointerEnterEvent>(_ => { lit = true; swayStart = Time.realtimeSinceStartupAsDouble; Light(); });
            RegisterCallback<PointerLeaveEvent>(_ => { lit = pressed = false; Light(); });
            RegisterCallback<PointerDownEvent>(_ => { pressed = true; Light(); }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => { pressed = false; Light(); }, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => { pressed = false; Light(); }, TrickleDown.TrickleDown);

            clickable.clickedWithEventInfo += evt =>
            {
                if (panel == null || panel.visualTree.Q(FlightLayer) != null) return;
                if (!FliesWhenChosen || evt is NavigationSubmitEvent) select();
                else if (mountedInScenery)
                {
                    if (RetractInScenery != null) RetractInScenery(select);
                    else select();
                }
                else FlyToBeam();
            };
        }

        /// <summary>Scales the board's carved frame and iron rings with the sign's height, keeping the corners inside narrow signs.</summary>
        private void Fit()
        {
            if (mountedInScenery || art.Board == null) return;
            float w = layout.width, h = layout.height;
            if (!(w > 0) || !(h > 0)) return;
            scale = art.Ring == null ? h / art.Board.height : Mathf.Min(h / art.Board.height, w * .45f / art.Side);
            if (art.Ring != null)
            {
                style.unitySliceScale = scale;
                float pad = Mathf.Max(6, art.Side * scale * .3f);
                if (Mathf.Abs(resolvedStyle.paddingLeft - pad) > .5f) style.paddingLeft = style.paddingRight = pad;
                for (int i = 0; i < 2; i++)
                {
                    rings[i].style.width = art.Ring.width * scale;
                    rings[i].style.height = art.Ring.height * scale;
                    if (i == 0) rings[i].style.left = art.RingInset * scale; else rings[i].style.right = art.RingInset * scale;
                }
            }
            rope.MarkDirtyRepaint();
        }

        private void Light()
        {
            style.color = lit ? LitLettering : Lettering;
            style.textShadow = lit ? Gleam : Carved;
            style.unityBackgroundImageTintColor = lit ? Color.white : Resting;
            style.translate = new Translate(0, pressed ? 2 : lit ? -2 : 0);
        }

        private void Tick()
        {
            FitRope();
            // A light push sets the board swinging on its ropes, dying away within a second.
            float t = (float)(Time.realtimeSinceStartupAsDouble - swayStart);
            sway = t is >= 0 and < 1.4f ? 2.4f * Mathf.Exp(-t * 3.2f) * Mathf.Sin(t * 11f) : 0;
            if (Mathf.Abs(sway - resolvedStyle.rotate.angle.value) > .01f) style.rotate = new Rotate(new Angle(sway));
        }

        internal const int TownSignWidth = 84, TownSignHeight = 58, ExitTopGap = 32;

        internal static HangingSign Exit(string id, Action leave, bool belowTopBar = false, Texture2D icon = null)
        {
            var sign = new HangingSign(id, "", leave, icon == null ? SignFace.Arrow : SignFace.Square);
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
            sign.style.width = icon == null ? 74 : TownSignWidth;
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
            var board = new HangingSign("retracting-sign",text,()=>{},face);
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
                // Clear rather than set 1, so the theme can still dim the sign while it is disabled.
                style.opacity = StyleKeyword.Null;
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
            style.backgroundColor = Color.clear;
            style.backgroundImage = StyleKeyword.None;
            PrototypeStyle.Outline(this, Color.clear, 0);
            foreach (var child in Children()) child.style.opacity = 0;
        }

        private void DrawRope(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float bottom = rope.contentRect.height;
            if (bottom <= 0) return;
            if (art.Ring == null)
            {
                // The arrow's ring is painted into the board, just below the sign's top edge.
                Hemp(p, layout.width * art.RopeX, bottom + layout.height * .05f);
                return;
            }
            float ring = art.Ring.width * scale, inset = art.RingInset * scale;
            // Each rope runs behind the top of its ring, so it reads as tied through it.
            float end = bottom - art.Ring.height * scale * .8f;
            Hemp(p, inset + ring * .5f, end);
            Hemp(p, layout.width - inset - ring * .5f, end);

            void Hemp(Painter2D painter, float x, float length)
            {
                if (length <= 0) return;
                painter.lineCap = LineCap.Round;
                painter.strokeColor = HempShade; painter.lineWidth = 3.6f;
                painter.BeginPath(); painter.MoveTo(new Vector2(x, 0)); painter.LineTo(new Vector2(x, length)); painter.Stroke();
                painter.strokeColor = HempStrand; painter.lineWidth = 1.1f;
                painter.BeginPath();
                for (float y = 1; y < length - 1.5f; y += 3.4f)
                {
                    painter.MoveTo(new Vector2(x - 1.1f, y));
                    painter.LineTo(new Vector2(x + 1.1f, Mathf.Min(length, y + 2.2f)));
                }
                painter.Stroke();
            }
        }
    }
}
