using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class SettingsPanel : VisualElement
    {
        private readonly ScrollView options;
        private readonly Button close;
        private readonly Action dismissed;

        internal SettingsPanel(Action dismissed)
        {
            this.dismissed = dismissed;
            name = "run-settings";
            PrototypeStyle.Fill(this);
            style.backgroundColor = new Color(0, 0, 0, .7f);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;

            var panel = PrototypeStyle.Panel();
            panel.style.width = Length.Percent(88);
            panel.style.maxWidth = 360;
            panel.style.maxHeight = Length.Percent(92);
            panel.style.marginTop = 0;
            panel.style.paddingTop = panel.style.paddingBottom = 13;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            Add(panel);

            var title = PrototypeStyle.Text("设置", 21, PrototypeStyle.Accent);
            title.style.marginBottom = 10;
            PrototypeStyle.Middle(title);
            panel.Add(title);
            options = new ScrollView(ScrollViewMode.Vertical);
            options.style.minHeight = 0;
            panel.Add(options);
            AddVolume("主音量", "master-volume", VolumeChannel.Master);
            AddVolume("背景声", "ambience-volume", VolumeChannel.Ambience);
            AddVolume("音效", "effects-volume", VolumeChannel.Effects);

            close = PrototypeStyle.Button("关闭", "close-settings", Close);
            close.style.height = 36;
            close.style.flexShrink = 0;
            close.style.marginTop = 9;
            panel.Add(close);
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
            row.style.minHeight = 34;
            var slider = new SliderInt(label, 0, 100) { name = id, value = GameAudio.Volume(channel) };
            slider.style.flexGrow = 1;
            slider.style.minWidth = 0;
            slider.labelElement.style.minWidth = slider.labelElement.style.width = 58;
            slider.labelElement.style.flexGrow = 0;
            slider.labelElement.style.color = PrototypeStyle.Ink;
            var value = PrototypeStyle.Text($"{slider.value}%", 14, PrototypeStyle.Accent);
            value.name = id + "-value";
            value.style.width = 42;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(slider);
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

        internal Button AddOption(string text, string id, Action action)
        {
            var button = PrototypeStyle.Button(text, id, action);
            button.style.height = 36;
            options.Add(button);
            return button;
        }

        internal void AddFeedback(Label message) => options.Add(message);

        internal void Show(VisualElement parent)
        {
            parent.Add(this);
            close.Focus();
        }

        internal void Close()
        {
            RemoveFromHierarchy();
            dismissed();
        }
    }
}
