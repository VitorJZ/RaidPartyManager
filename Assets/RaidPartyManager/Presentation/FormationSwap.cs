using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class FormationSwap
    {
        private const int HoldMilliseconds = 300;
        private const int PreviewWidth = 76;
        private const int PreviewHeight = 96;

        public sealed class Slot
        {
            public VisualElement View;
            public VisualElement Border;
            public int? Position;
            public string MemberId;
            public string Name;
            public Texture2D Artwork;
            public bool CanDrag;
            public bool CanDrop;
            public bool Borderless;
        }

        private readonly VisualElement formation;
        private readonly Action<Slot, Slot> swap;
        private readonly Action<Slot> click;
        private readonly Dictionary<VisualElement, Slot> slots = new Dictionary<VisualElement, Slot>();
        private Slot source;
        private Slot destination;
        private int pointerId = -1;
        private Vector2 pointer;
        private IVisualElementScheduledItem hold;
        private VisualElement preview;

        public FormationSwap(VisualElement formation, Action<Slot, Slot> swap, Action<Slot> click = null)
        {
            this.formation = formation;
            this.swap = swap;
            this.click = click;
            formation.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            formation.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            formation.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            formation.RegisterCallback<PointerCancelEvent>(evt => CancelPointer(evt.pointerId));
            formation.RegisterCallback<PointerCaptureOutEvent>(evt => CancelPointer(evt.pointerId));
            formation.RegisterCallback<DetachFromPanelEvent>(_ => CancelDrag());
        }

        public void Register(Slot slot) => slots.Add(slot.View, slot);

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || source != null)
                return;
            var slot = FindSlot(evt.target as VisualElement);
            if (slot == null || !slot.CanDrag)
                return;

            source = slot;
            pointer = evt.position;
            pointerId = evt.pointerId;
            formation.CapturePointer(pointerId);
            hold = formation.schedule.Execute(StartDrag).StartingIn(HoldMilliseconds);
            evt.StopImmediatePropagation();
        }

        private void StartDrag()
        {
            if (source == null || formation.panel == null)
                return;

            preview = source.Artwork == null
                ? PrototypeStyle.Text(source.Name, 13)
                : new Image { image = source.Artwork, scaleMode = ScaleMode.ScaleToFit };
            preview.name = "formation-drag-preview";
            preview.pickingMode = PickingMode.Ignore;
            preview.style.position = Position.Absolute;
            preview.style.width = PreviewWidth;
            preview.style.height = PreviewHeight;
            preview.style.backgroundColor = PrototypeStyle.PanelColor;
            PrototypeStyle.Outline(preview, PrototypeStyle.Accent, 5);
            source.View.style.opacity = 0.55f;
            formation.Add(preview);
            MovePreview();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (source == null || evt.pointerId != pointerId)
                return;

            pointer = evt.position;
            if (preview != null)
            {
                MovePreview();
                Highlight(DropTarget(pointer));
                evt.StopPropagation();
            }
            else if (!source.View.worldBound.Contains(pointer))
                CancelDrag();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (source == null || evt.pointerId != pointerId)
                return;

            var from = source;
            var to = preview == null ? null : DropTarget(evt.position);
            bool tapped = preview == null && source.View.worldBound.Contains(evt.position);
            CancelDrag();
            evt.StopImmediatePropagation();
            if (to != null)
                swap(from, to);
            else if (tapped)
                click?.Invoke(from);
        }

        private Slot FindSlot(VisualElement element)
        {
            for (; element != null && element != formation; element = element.parent)
                if (slots.TryGetValue(element, out var slot))
                    return slot;
            return null;
        }

        private Slot DropTarget(Vector2 position)
        {
            var slot = FindSlot(formation.panel?.Pick(position));
            return slot != null && slot != source && slot.CanDrop ? slot : null;
        }

        private void Highlight(Slot next)
        {
            if (destination == next)
                return;
            if (destination != null)
                HighlightSlot(destination, false);
            destination = next;
            if (destination != null)
                HighlightSlot(destination, true);
        }

        private static void HighlightSlot(Slot slot, bool highlighted)
        {
            if (slot.Borderless)
                slot.Border.style.backgroundColor = highlighted ? new Color(0.45f, 0.73f, 0.68f, 0.16f) : Color.clear;
            else
                PrototypeStyle.Outline(slot.Border, highlighted ? PrototypeStyle.Ally : PrototypeStyle.Edge, 4);
        }

        private void MovePreview()
        {
            var local = formation.WorldToLocal(pointer);
            preview.style.left = local.x - PreviewWidth / 2f;
            preview.style.top = local.y - PreviewHeight / 2f;
        }

        private void CancelPointer(int id)
        {
            if (id == pointerId)
                CancelDrag();
        }

        private void CancelDrag()
        {
            int captured = pointerId;
            pointerId = -1;
            hold?.Pause();
            hold = null;
            if (source != null)
                source.View.style.opacity = 1f;
            source = null;
            Highlight(null);
            preview?.RemoveFromHierarchy();
            preview = null;
            if (captured >= 0 && formation.panel != null && formation.HasPointerCapture(captured))
                formation.ReleasePointer(captured);
        }
    }
}
