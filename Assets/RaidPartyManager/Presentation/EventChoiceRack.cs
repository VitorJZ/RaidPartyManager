using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EventChoiceRack : VisualElement
    {
        private readonly VisualElement notes, row;
        private readonly HangingSign previous, next;
        private readonly List<HangingSign> signs = new();
        private int page;

        internal EventChoiceRack(VisualElement notes)
        {
            this.notes = notes;
            name = "event-options";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = Length.Percent(8);
            style.right = Length.Percent(15);
            style.top = Length.Percent(17);
            row = PrototypeStyle.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.FlexStart;
            Add(row);
            previous = Pager("earlier-event-options", "‹", -1);
            next = Pager("later-event-options", "›", 1);
            RegisterCallback<GeometryChangedEvent>(_ => Arrange());

            HangingSign Pager(string id, string glyph, int direction)
            {
                var nav = new HangingSign(id, glyph, () => { page += direction; Arrange(); }, SignFace.Square) { FliesWhenChosen = false };
                nav.style.width = 35; nav.style.height = 42;
                nav.style.fontSize = 24;
                nav.style.position = Position.Absolute;
                if (direction < 0) nav.style.left = 0; else nav.style.right = 0;
                nav.style.display = DisplayStyle.None;
                Add(nav);
                return nav;
            }
        }

        internal void Reset(bool keepPage)
        {
            if (!keepPage) page = 0;
            row.name = null;
            notes.Clear();
            row.Clear();
            signs.Clear();
        }

        internal void NameRow(string id) => row.name = id;

        internal void Note(VisualElement note) => notes.Add(note);

        internal HangingSign Choice(string text, string id, Action select, bool enabled = true)
        {
            var sign = new HangingSign(id, text, select);
            sign.style.height = 61;
            sign.style.marginLeft = sign.style.marginRight = 4;
            sign.SetEnabled(enabled);
            row.Add(sign);
            signs.Add(sign);
            return sign;
        }

        internal void Arrange()
        {
            float width = layout.width;
            if (!(width > 0)) width = 620;
            int fullCapacity = Mathf.Max(1, Mathf.FloorToInt(width / 145));
            bool paged = signs.Count > fullCapacity;
            int capacity = paged ? Mathf.Max(1, Mathf.FloorToInt((width - 82) / 145)) : fullCapacity;
            int pages = Mathf.Max(1, Mathf.CeilToInt((float)signs.Count / capacity));
            page = Mathf.Clamp(page, 0, pages - 1);
            previous.style.display = next.style.display = paged ? DisplayStyle.Flex : DisplayStyle.None;
            previous.SetEnabled(page > 0);
            next.SetEnabled(page + 1 < pages);
            float signWidth = Mathf.Min(158, (width - (paged ? 82 : 0)) / Mathf.Max(1, Mathf.Min(capacity, signs.Count)) - 8);
            for (int i = 0; i < signs.Count; i++)
            {
                signs[i].style.width = signWidth;
                signs[i].style.display = i / capacity == page ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
