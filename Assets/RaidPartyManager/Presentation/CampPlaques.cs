using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// A camp action carved on the same walnut-and-iron plank as the hanging signs. It stands on the apron
    /// instead of hanging, but lights, lifts and presses exactly like a sign.
    /// </summary>
    internal sealed class CampPlaque : Button
    {
        private const int Side = 128, Top = 84, Bottom = 98;
        private static readonly Color Note = new Color32(226, 205, 165, 255);
        private static readonly Color Spent = new Color(.5f, .47f, .45f);
        private static Texture2D board;

        private readonly Label title, detail;
        private readonly Image emblem;
        private bool lit, pressed, offered = true;

        internal CampPlaque(string id, string caption, Texture2D emblemArt, Action choose) : base(choose)
        {
            name = id;
            text = "";
            if (board == null) board = Resources.Load<Texture2D>("HangingSign/SignPlankBoard");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.marginTop = style.marginBottom = 0;
            style.marginLeft = style.marginRight = 5;
            style.paddingTop = 1;
            style.paddingBottom = 4;
            style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(this, Color.clear, 0);
            style.backgroundImage = board;
            style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            style.unitySliceLeft = style.unitySliceRight = Side;
            style.unitySliceTop = Top;
            style.unitySliceBottom = Bottom;

            emblem = new Image { name = "camp-emblem", image = emblemArt, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            emblem.style.flexShrink = 0;
            emblem.style.marginRight = 9;
            Add(emblem);
            var words = new VisualElement { pickingMode = PickingMode.Ignore };
            words.style.flexShrink = 1;
            words.style.alignItems = Align.Center;
            title = PrototypeStyle.Text(caption, 21, HangingSign.Lettering);
            title.pickingMode = PickingMode.Ignore;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 3;
            detail = PrototypeStyle.Text("", 12, Note);
            detail.name = id + "-detail";
            detail.pickingMode = PickingMode.Ignore;
            detail.style.whiteSpace = WhiteSpace.NoWrap;
            detail.style.textShadow = HangingSign.Carved;
            words.Add(title);
            words.Add(detail);
            Add(words);

            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            RegisterCallback<PointerEnterEvent>(_ => { lit = true; Light(); });
            RegisterCallback<PointerLeaveEvent>(_ => { lit = pressed = false; Light(); });
            RegisterCallback<PointerDownEvent>(_ => { pressed = true; Light(); }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => { pressed = false; Light(); }, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => { pressed = false; Light(); }, TrickleDown.TrickleDown);
            Light();
        }

        /// <summary>Sets whether the action can be taken now, and the line under its name that says what it does or why it can't.</summary>
        internal void Offer(bool available, string note)
        {
            offered = available;
            SetEnabled(available);
            detail.text = note;
            Light();
        }

        private void Fit()
        {
            float h = layout.height;
            if (!(h > 0)) return;
            float scale = h / board.height;
            style.unitySliceScale = scale;
            float pad = Side * scale * .42f;
            if (Mathf.Abs(resolvedStyle.paddingLeft - pad) > .5f) style.paddingLeft = style.paddingRight = pad;
            float size = Mathf.Round(h * .6f);
            if (Mathf.Abs(emblem.resolvedStyle.width - size) > .5f) emblem.style.width = emblem.style.height = size;
        }

        private void Light()
        {
            bool glow = lit && offered;
            // The theme's own disabled fade would wash the wood grey; a spent plaque darkens instead.
            style.opacity = 1;
            style.unityBackgroundImageTintColor = !offered ? Spent : glow ? Color.white : HangingSign.Resting;
            title.style.color = !offered ? new Color(.7f, .62f, .5f) : glow ? HangingSign.LitLettering : HangingSign.Lettering;
            title.style.textShadow = glow ? HangingSign.Gleam : HangingSign.Carved;
            detail.style.color = offered ? Note : new Color(.68f, .6f, .5f);
            emblem.tintColor = !offered ? new Color(.55f, .5f, .45f, .8f) : glow ? Color.white : new Color(.92f, .9f, .86f);
            style.translate = new Translate(0, glow && pressed ? 2 : glow ? -2 : 0);
        }
    }

    /// <summary>The pinned parchment slip the quest board uses for hints, sized to its line of text.</summary>
    internal sealed class CampNote : VisualElement
    {
        private const int Left = 120, Right = 46, Edge = 40;
        private static readonly Color Ink = new Color32(52, 33, 19, 235);
        private static Texture2D slip;
        internal readonly Label Words;

        internal CampNote(string id, string words)
        {
            if (slip == null) slip = Resources.Load<Texture2D>("QuestBoard/HintSlip");
            pickingMode = PickingMode.Ignore;
            style.alignSelf = Align.Center;
            style.maxWidth = Length.Percent(100);
            style.minHeight = 42;
            style.marginTop = 7;
            style.justifyContent = Justify.Center;
            style.backgroundImage = slip;
            style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            style.unitySliceLeft = Left;
            style.unitySliceRight = Right;
            style.unitySliceTop = style.unitySliceBottom = Edge;
            style.unitySliceScale = 42f / slip.height;
            style.paddingLeft = 38;
            style.paddingRight = 24;
            style.paddingTop = 7;
            style.paddingBottom = 8;
            Words = PrototypeStyle.Text(words, 14, Ink);
            Words.name = id;
            Words.pickingMode = PickingMode.Ignore;
            Words.style.unityFontStyleAndWeight = FontStyle.Bold;
            PrototypeStyle.Middle(Words);
            Add(Words);
        }
    }
}
