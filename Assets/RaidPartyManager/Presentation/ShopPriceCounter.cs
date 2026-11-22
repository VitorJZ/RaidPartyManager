using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class ShopPriceCounter : VisualElement
    {
        private const float DigitHeight = 31;
        private const float RollDuration = .27f;
        private readonly Label[] previous = new Label[2];
        private readonly Label[] current = new Label[2];
        private float elapsed = RollDuration;
        internal int Price { get; private set; }

        internal ShopPriceCounter(int price)
        {
            name = "shop-recruit-price";
            Price = price;
            style.alignSelf = Align.Center;
            style.flexDirection = FlexDirection.Row;
            style.backgroundColor = (Color)new Color32(60, 43, 31, 255);
            style.paddingLeft = style.paddingRight = 5;
            style.paddingTop = style.paddingBottom = 3;
            PrototypeStyle.Outline(this, new Color32(182, 137, 69, 255), 4);

            string digits = price.ToString("D2");
            for (int i = 0; i < digits.Length; i++)
            {
                var window = new VisualElement();
                window.style.position = Position.Relative;
                window.style.overflow = Overflow.Hidden;
                window.style.width = 25;
                window.style.height = DigitHeight;
                window.style.marginLeft = window.style.marginRight = 1;
                window.style.backgroundColor = (Color)new Color32(31, 24, 22, 255);
                previous[i] = Digit(digits[i]);
                previous[i].style.top = -DigitHeight;
                current[i] = Digit(digits[i]);
                current[i].name = "shop-price-digit-" + i;
                current[i].style.top = 0;
                window.Add(previous[i]);
                window.Add(current[i]);
                Add(window);
            }
        }

        private static Label Digit(char digit)
        {
            var label = PrototypeStyle.Text(digit.ToString(), 24, new Color32(250, 222, 163, 255));
            label.style.position = Position.Absolute;
            label.style.left = label.style.right = 0;
            label.style.height = DigitHeight;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            PrototypeStyle.Middle(label);
            return label;
        }

        internal void RollTo(int price)
        {
            if (price == Price) return;
            string digits = price.ToString("D2");
            for (int i = 0; i < digits.Length; i++)
            {
                previous[i].text = current[i].text;
                current[i].text = digits[i].ToString();
            }
            Price = price;
            elapsed = 0;
            PositionDigits(0);
        }

        internal void Tick(float deltaTime)
        {
            if (elapsed >= RollDuration) return;
            elapsed = Mathf.Min(elapsed + deltaTime, RollDuration);
            PositionDigits(Mathf.SmoothStep(0, 1, elapsed / RollDuration));
        }

        private void PositionDigits(float progress)
        {
            for (int i = 0; i < current.Length; i++)
            {
                bool changed = previous[i].text != current[i].text;
                previous[i].style.top = changed ? -DigitHeight * progress : -DigitHeight;
                current[i].style.top = changed ? DigitHeight * (1 - progress) : 0;
            }
        }
    }
}
