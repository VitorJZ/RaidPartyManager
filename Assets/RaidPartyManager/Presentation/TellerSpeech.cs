using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TellerSpeech : VisualElement
    {
        internal TellerSpeech(DockEventStage stage, string line, VisualElement notes)
        {
            name = "storyteller-speech";
            style.position = Position.Absolute;
            style.width = Length.Percent(33);
            style.maxWidth = 335;
            style.visibility = Visibility.Hidden;
            var color = new Color32(249,246,231,255);
            var tip = new VisualElement { pickingMode = PickingMode.Ignore };
            tip.style.position = Position.Absolute;
            tip.style.width = tip.style.height = 15;
            tip.style.bottom = -6;
            tip.style.backgroundColor = (Color)color;
            tip.style.rotate = new Rotate(new Angle(45));
            Add(tip);
            var body = new VisualElement();
            body.style.backgroundColor = (Color)color;
            body.style.borderTopLeftRadius = body.style.borderTopRightRadius = 14;
            body.style.borderBottomLeftRadius = body.style.borderBottomRightRadius = 14;
            body.style.paddingLeft = body.style.paddingRight = 14;
            body.style.paddingTop = body.style.paddingBottom = 9;
            Add(body);
            var label = PrototypeStyle.Text(line, 16, AdventureEventWindow.PaperInk);
            label.name = "event-description";
            body.Add(label);
            body.Add(notes);
            schedule.Execute(() =>
            {
                if (panel == null || !(layout.height > 0)) return;
                var head = stage.TellerHead;
                var screen = panel.visualTree.worldBound;
                var anchor = parent.WorldToLocal(new Vector2(screen.xMin + head.x * screen.width,
                    screen.yMin + (1-head.y) * screen.height));
                float left = Mathf.Clamp(anchor.x - layout.width*.48f, 4, Mathf.Max(4,parent.layout.width-layout.width-4));
                style.left = left;
                style.top = Mathf.Max(4,anchor.y-layout.height-10);
                tip.style.left = Mathf.Clamp(anchor.x-left-7,12,layout.width-24);
                style.visibility = Visibility.Visible;
            }).Every(16);
        }
    }
}
