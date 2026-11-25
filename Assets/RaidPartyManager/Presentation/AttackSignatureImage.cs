using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class AttackSignatureImage : Image
    {
        private readonly Texture2D atlas;
        private readonly int lastCel;
        private int frame = -1;

        internal AttackSignatureImage(string name, Texture2D atlas, bool inFlight)
        {
            this.name = name;
            this.atlas = atlas;
            image = atlas;
            scaleMode = ScaleMode.StretchToFill;
            lastCel = inFlight ? 8 : AnimationCels.Count - 1;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            AddToClassList(atlas.name);
            ShowFrame(0f);
        }

        internal void ShowFrame(float progress)
        {
            int next = Mathf.Min(lastCel, Mathf.FloorToInt(progress * (lastCel + 1)));
            if (next == frame) return;
            frame = next;
            sourceRect = AnimationCels.RectFor(atlas, frame);
            MarkDirtyRepaint();
        }
    }
}
