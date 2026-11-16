using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal static class PrototypeStyle
    {
        public static readonly Color Background = new Color32(15, 17, 21, 255);
        public static readonly Color Ink = new Color32(244, 238, 224, 255);
        public static readonly Color Muted = new Color32(178, 173, 161, 255);
        public static readonly Color Accent = new Color32(231, 191, 115, 255);
        public static readonly Color Ally = new Color32(115, 186, 175, 255);
        public static readonly Color Enemy = new Color32(227, 133, 112, 255);
        public static readonly Color PanelColor = new Color32(27, 29, 34, 255);
        public static readonly Color Inset = new Color32(20, 22, 27, 255);
        public static readonly Color Edge = new Color32(65, 62, 61, 255);
        private static readonly Color ButtonColor = new Color32(48, 45, 43, 255);

        public static void ApplySystemFont(VisualElement root, out Font font, out FontAsset fontAsset)
        {
            string fontName = Array.Find(Font.GetOSInstalledFontNames(), name => name == "Microsoft YaHei" ||
                name == "Microsoft YaHei UI" || name == "Noto Sans CJK SC" || name == "PingFang SC");
            font = fontName == null ? null : Font.CreateDynamicFontFromOSFont(fontName, 16);
            fontAsset = font == null ? null : FontAsset.CreateFontAsset(font);
            if (fontAsset != null)
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);
        }

        public static void ClearDocument(VisualElement root, Font font, FontAsset fontAsset)
        {
            if (root != null)
            {
                root.Clear();
                root.style.unityFontDefinition = StyleKeyword.Null;
            }
            if (fontAsset != null)
                UnityEngine.Object.Destroy(fontAsset);
            if (font != null)
                UnityEngine.Object.Destroy(font);
        }

        public static VisualElement Row(string name = null)
        {
            var row = new VisualElement { name = name };
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        public static VisualElement Panel()
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingTop = panel.style.paddingBottom = 10;
            panel.style.paddingLeft = panel.style.paddingRight = 11;
            panel.style.marginTop = 10;
            Outline(panel, Edge, 5);
            return panel;
        }

        public static void Outline(VisualElement element, Color color, int radius)
        {
            var style = element.style;
            style.borderTopWidth = style.borderRightWidth = style.borderBottomWidth = style.borderLeftWidth = 1;
            style.borderTopColor = style.borderRightColor = style.borderBottomColor = style.borderLeftColor = color;
            style.borderTopLeftRadius = style.borderTopRightRadius =
                style.borderBottomLeftRadius = style.borderBottomRightRadius = radius;
        }

        public static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.top = element.style.right = element.style.bottom = element.style.left = 0;
        }

        public static Label Text(string content, int size, Color? color = null)
        {
            var label = new Label(content);
            label.style.color = color ?? Ink;
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = label.style.marginBottom = 0;
            return label;
        }

        public static Button Button(string content, string name, Action onClick, bool emphasized = false)
        {
            var button = new Button(onClick) { text = content, name = name };
            button.style.fontSize = 13;
            button.style.minHeight = 26;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.marginTop = button.style.marginBottom = 2;
            button.style.color = emphasized ? Background : Ink;
            button.style.backgroundColor = emphasized ? Accent : ButtonColor;
            Outline(button, emphasized ? Accent : Edge, 3);
            return button;
        }

        public static void Middle(VisualElement element) =>
            element.style.unityTextAlign = TextAnchor.MiddleCenter;
    }
}
