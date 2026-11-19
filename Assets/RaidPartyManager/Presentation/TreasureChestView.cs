using System;
using RaidPartyManager.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TreasureChestView : VisualElement
    {
        private readonly VisualElement poses = new VisualElement { pickingMode = PickingMode.Ignore };
        private readonly Image closed;
        private readonly Image ajar;
        private readonly Image open;
        private IVisualElementScheduledItem animation;

        public TreasureChestView(AdventureContentAsset art, bool opened)
        {
            name = "treasure-chest-art";
            style.flexGrow = 1;
            style.flexBasis = 0;
            style.minWidth = 0;
            style.marginRight = 14;
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(poses);
            Add(poses);
            closed = Pose("chest-closed", art.ChestClosed, opened ? 0 : 1);
            ajar = Pose("chest-ajar", art.ChestAjar, 0);
            open = Pose("chest-open", art.ChestOpen, opened ? 1 : 0);
            RegisterCallback<DetachFromPanelEvent>(_ => animation?.Pause());

            Image Pose(string id, Texture2D texture, float opacity)
            {
                var image = new Image
                {
                    name = id, image = texture, scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                PrototypeStyle.Fill(image);
                image.style.opacity = opacity;
                if (texture != null && art.EventInkShader != null)
                    AdventureEventWindow.ApplyInkLayer(image, texture, art.EventInkShader);
                poses.Add(image);
                return image;
            }
        }

        public void Play(Action complete)
        {
            double start = Time.unscaledTimeAsDouble;
            animation = schedule.Execute(() =>
            {
                float time = (float)(Time.unscaledTimeAsDouble - start);
                float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.16f, 0.34f, time));
                float swing = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.34f, 0.53f, time));
                closed.style.opacity = 1 - lift;
                ajar.style.opacity = lift * (1 - swing);
                open.style.opacity = swing;

                float rattle = Mathf.Sin(time * 53f) * 2.4f * (1 - Mathf.Clamp01(time / 0.18f));
                float settle = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.34f, 0.66f, time));
                poses.style.rotate = new Rotate(new Angle(rattle - settle * 1.4f));
                poses.style.translate = new Translate(0, -settle * 5f);
                if (time < 0.66f)
                    return;

                animation.Pause();
                complete();
            }).Every(16);
        }
    }
}
