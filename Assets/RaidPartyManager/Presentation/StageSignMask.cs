using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Keep the logical canvas fixed while the physical travellers uncover its centre.
    internal sealed class StageSignMask : VisualElement
    {
        internal StageSignMask(VisualElement signs)
        {
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            var aperture = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(aperture);
            aperture.style.overflow = Overflow.Hidden;
            Add(aperture);
            var canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(canvas);
            aperture.Add(canvas);
            canvas.Add(signs);
            void Fit()
            {
                if (panel == null || !(layout.width > 0)) return;
                Vector2 edges = DockPlayhouse.CurtainOpening(panel);
                float left = Mathf.Clamp(edges.x-worldBound.xMin,0,layout.width);
                float right = Mathf.Clamp(worldBound.xMax-edges.y,0,layout.width-left);
                aperture.style.left = left;
                aperture.style.right = right;
                canvas.style.left = -left;
                canvas.style.right = -right;
            }
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            schedule.Execute(Fit).Every(16);
        }
    }
}
