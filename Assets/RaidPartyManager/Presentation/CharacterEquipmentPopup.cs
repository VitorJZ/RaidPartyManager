using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class CharacterEquipmentPopup : VisualElement
    {
        private const int Columns = 4;
        private const int PageSize = Columns * Columns;
        private const int HoldMilliseconds = 300;
        private const float TileSize = 73;

        private sealed class Cell
        {
            public VisualElement View;
            public EquipmentInstance Item;
            public bool IsBackpack;
            public int Index;
        }

        private readonly PartyRun run;
        private readonly string memberId;
        private readonly Texture2D portraitArt;
        private readonly List<string> bag;
        private readonly Action changed;
        private readonly Action close;
        private int page;
        private int? keyboardSlot;
        private VisualElement tooltipPanel;
        private Cell hovered;
        private Cell source;
        private Cell dropTarget;
        private VisualElement ghost;
        private IVisualElementScheduledItem hold;
        private int pointerId = -1;
        private Vector2 pointer;

        public CharacterEquipmentPopup(PartyRun run, string memberId, Texture2D portraitArt, List<string> bag,
            Action changed, Action close)
        {
            this.run = run;
            this.memberId = memberId;
            this.portraitArt = portraitArt;
            this.bag = bag;
            this.changed = changed;
            this.close = close;

            PrototypeStyle.Fill(this);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(evt => { if (evt.pointerId == pointerId) CancelInteraction(); });
            RegisterCallback<PointerCaptureOutEvent>(evt => { if (evt.pointerId == pointerId) CancelInteraction(); });
            RegisterCallback<DetachFromPanelEvent>(_ => CancelInteraction());

            SyncBackpack();
            Render();
        }

        // The positions are view state. PartyRun continues to own every item and its equipment assignment.
        private void SyncBackpack()
        {
            var equipped = new HashSet<string>(run.Members.SelectMany(member => member.EquipmentIds));
            var unused = new HashSet<string>(run.Inventory.Select(item => item.Id).Where(id => !equipped.Contains(id)));
            for (int index = 0; index < bag.Count; index++)
                if (bag[index] != null && !unused.Remove(bag[index]))
                    bag[index] = null;
            foreach (var item in run.Inventory.Where(item => unused.Contains(item.Id)))
            {
                int empty = bag.IndexOf(null);
                if (empty >= 0)
                    bag[empty] = item.Id;
                else
                    bag.Add(item.Id);
            }
        }

        private void Render()
        {
            Clear();
            var selected = run.Members.Single(member => member.Id == memberId);
            var inventory = run.Inventory.ToDictionary(item => item.Id);
            var dialog = PrototypeStyle.Panel();
            dialog.name = "character-details";
            dialog.style.width = Length.Percent(89);
            dialog.style.maxWidth = 930;
            dialog.style.marginTop = 0;
            dialog.style.paddingTop = dialog.style.paddingBottom = 16;
            dialog.style.paddingLeft = dialog.style.paddingRight = 19;

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            var name = PrototypeStyle.Text(selected.Character.Name, 24);
            name.name = "details-character-name";
            header.Add(name);
            var dismiss = PrototypeStyle.Button("关闭", "close-character-details", close);
            dismiss.style.width = dismiss.style.height = 43;
            dismiss.style.paddingLeft = dismiss.style.paddingRight = 0;
            dismiss.style.whiteSpace = WhiteSpace.NoWrap;
            PrototypeStyle.Middle(dismiss);
            PrototypeStyle.Outline(dismiss, PrototypeStyle.Accent, 23);
            header.Add(dismiss);
            dialog.Add(header);

            var body = PrototypeStyle.Row();
            body.style.alignItems = Align.Stretch;
            var profile = PrototypeStyle.Panel();
            profile.name = "character-profile";
            profile.style.flexGrow = 5;
            profile.style.flexBasis = 0;
            profile.style.minWidth = 0;
            profile.style.marginRight = 12;
            var left = PrototypeStyle.Row();
            left.Add(BuildCharacter(selected));

            var equipped = new VisualElement();
            equipped.name = "character-equipment";
            equipped.style.width = 106;
            equipped.style.marginLeft = 10;
            equipped.style.alignItems = Align.Center;
            equipped.style.justifyContent = Justify.SpaceBetween;
            for (int slot = 0; slot < Rules.EquipmentSlots; slot++)
            {
                var item = selected.EquipmentIds[slot] == null ? null : inventory[selected.EquipmentIds[slot]];
                var entry = new VisualElement();
                entry.style.alignItems = Align.Center;
                var label = PrototypeStyle.Text(item?.Definition.Name ?? $"装备槽 {slot + 1}", 12,
                    item != null ? PrototypeStyle.Ink : PrototypeStyle.Muted);
                label.name = $"equipped-name-{slot}";
                label.style.marginBottom = 3;
                PrototypeStyle.Middle(label);
                entry.Add(label);
                entry.Add(BuildCell(false, slot, item));
                equipped.Add(entry);
            }
            left.Add(equipped);
            profile.Add(left);
            body.Add(profile);
            body.Add(BuildBackpack(inventory));
            dialog.Add(body);
            Add(dialog);
        }

        private VisualElement BuildCharacter(MemberView member)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.flexBasis = 0;
            column.style.minWidth = 0;
            var portrait = new VisualElement { name = "details-portrait" };
            portrait.style.height = 270;
            portrait.style.width = Length.Percent(100);
            portrait.style.maxWidth = 198;
            portrait.style.alignSelf = Align.Center;
            portrait.style.backgroundColor = PrototypeStyle.Inset;
            portrait.style.overflow = Overflow.Hidden;
            PrototypeStyle.Outline(portrait, PrototypeStyle.Edge, 5);
            ProfessionFrame.AddTo(portrait, member.Character.Profession, portraitArt);
            if (portraitArt == null)
            {
                var caption = PrototypeStyle.Text(member.Character.Name, 16, PrototypeStyle.Muted);
                caption.style.flexGrow = 1;
                PrototypeStyle.Middle(caption);
                portrait.Add(caption);
            }
            column.Add(portrait);

            var details = PrototypeStyle.Row();
            details.style.marginTop = 8;
            details.style.width = Length.Percent(100);
            details.style.maxWidth = 198;
            details.style.alignSelf = Align.Center;
            var values = new VisualElement();
            values.style.flexGrow = 1;
            var stats = member.EffectiveStats;
            var baseline = member.Character.BaseStats;
            values.Add(PrototypeStyle.Text($"攻击  {StatText(stats.Attack, baseline.Attack)}", 13));
            values.Add(PrototypeStyle.Text($"防御  {StatText(stats.Defense, baseline.Defense)}", 13));
            values.Add(PrototypeStyle.Text($"特防  {StatText(stats.SpecialDefense, baseline.SpecialDefense)}", 13));
            details.Add(values);
            var health = PrototypeStyle.Text($"当前生命\n{HealthText(member.Health, stats.Health)}", 13, PrototypeStyle.Ally);
            health.name = "details-health";
            health.style.paddingLeft = health.style.paddingRight = 6;
            health.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Middle(health);
            PrototypeStyle.Outline(health, PrototypeStyle.Edge, 4);
            details.Add(health);
            column.Add(details);
            return column;
        }

        private VisualElement BuildBackpack(IReadOnlyDictionary<string, EquipmentInstance> inventory)
        {
            var backpack = PrototypeStyle.Panel();
            backpack.name = "character-backpack";
            backpack.style.flexGrow = 6;
            backpack.style.flexBasis = 0;
            backpack.style.minWidth = 0;
            var header = PrototypeStyle.Row();
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.Add(PrototypeStyle.Text("共享背包", 17, PrototypeStyle.Accent));

            int pages = Math.Max(1, (bag.FindLastIndex(id => id != null) + PageSize) / PageSize);
            page = Math.Min(page, pages - 1);
            if (pages > 1)
            {
                var navigation = PrototypeStyle.Row();
                var back = PrototypeStyle.Button("上一页", "bag-previous", () => { page--; ChangePage(); });
                var next = PrototypeStyle.Button("下一页", "bag-next", () => { page++; ChangePage(); });
                back.SetEnabled(page > 0);
                next.SetEnabled(page + 1 < pages);
                navigation.Add(back);
                navigation.Add(PrototypeStyle.Text($"{page + 1}/{pages}", 13, PrototypeStyle.Muted));
                navigation.Add(next);
                header.Add(navigation);
            }
            backpack.Add(header);

            var grid = new VisualElement { name = "backpack-grid" };
            grid.style.alignItems = Align.Center;
            grid.style.marginTop = 5;
            for (int row = 0; row < Columns; row++)
            {
                var line = PrototypeStyle.Row();
                for (int col = 0; col < Columns; col++)
                {
                    int index = page * PageSize + row * Columns + col;
                    var item = index < bag.Count && bag[index] != null ? inventory[bag[index]] : null;
                    line.Add(BuildCell(true, index, item));
                }
                grid.Add(line);
            }
            backpack.Add(grid);
            return backpack;
        }

        private void ChangePage()
        {
            CancelInteraction();
            Render();
        }

        private VisualElement BuildCell(bool isBackpack, int index, EquipmentInstance item)
        {
            var view = new VisualElement
            {
                name = isBackpack ? $"bag-cell-{index}" : $"edit-slot-{index}",
                focusable = item != null || !isBackpack,
                tabIndex = item != null || !isBackpack ? 0 : -1
            };
            var cell = new Cell { View = view, Item = item, IsBackpack = isBackpack, Index = index };
            view.userData = cell;
            view.style.width = view.style.height = TileSize;
            view.style.flexShrink = 0;
            view.style.marginLeft = view.style.marginRight = 3;
            view.style.marginTop = view.style.marginBottom = 3;
            view.style.backgroundColor = item == null ? PrototypeStyle.Inset : (Color)new Color32(36, 38, 45, 255);
            RestoreCellOutline(cell);
            if (item != null)
            {
                var art = new EquipmentIcon(item.Definition) { name = isBackpack ? $"edit-item-{item.Id}" : "equipment-icon" };
                view.Add(art);
                view.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse && source == null)
                        ShowTooltip(cell);
                });
                view.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (hovered == cell)
                        HideTooltip();
                    if (source == cell && ghost == null)
                        CancelInteraction();
                });
                view.RegisterCallback<PointerDownEvent>(evt => BeginHold(evt, cell));
            }
            else if (!isBackpack)
            {
                var empty = PrototypeStyle.Text("空槽", 12, PrototypeStyle.Muted);
                empty.pickingMode = PickingMode.Ignore;
                empty.style.flexGrow = 1;
                PrototypeStyle.Middle(empty);
                view.Add(empty);
            }

            view.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (source != null)
                    return;
                if (isBackpack && item != null && keyboardSlot.HasValue)
                {
                    var slot = this.Q($"edit-slot-{keyboardSlot.Value}")?.userData as Cell;
                    Swap(cell, slot);
                }
                else if (!isBackpack)
                {
                    keyboardSlot = index;
                    HideTooltip();
                    Render();
                    this.Q($"edit-slot-{index}")?.Focus();
                }
                evt.StopPropagation();
            });
            return view;
        }

        private void BeginHold(PointerDownEvent evt, Cell cell)
        {
            if (evt.button != 0 || source != null)
                return;
            source = cell;
            pointerId = evt.pointerId;
            pointer = evt.position;
            hold = schedule.Execute(StartDrag).StartingIn(HoldMilliseconds);
            evt.StopPropagation();
        }

        private void StartDrag()
        {
            if (source == null || panel == null)
                return;
            HideTooltip();
            ghost = new EquipmentIcon(source.Item.Definition) { name = "equipment-drag-preview" };
            ghost.style.position = Position.Absolute;
            ghost.style.width = ghost.style.height = TileSize - 7;
            ghost.style.backgroundColor = PrototypeStyle.PanelColor;
            PrototypeStyle.Outline(ghost, PrototypeStyle.Accent, 6);
            source.View.style.opacity = 0.48f;
            Add(ghost);
            this.CapturePointer(pointerId);
            MoveGhost();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (source == null || evt.pointerId != pointerId)
                return;

            pointer = evt.position;
            if (ghost != null)
            {
                MoveGhost();
                SetDropTarget(TargetAt(pointer));
                evt.StopPropagation();
            }
            else if (!source.View.worldBound.Contains(pointer))
                CancelInteraction();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (source == null || evt.pointerId != pointerId)
                return;

            Cell from = source;
            Cell target = ghost != null ? TargetAt(evt.position) : null;
            CancelInteraction();
            if (target != null)
                Swap(from, target);
            evt.StopPropagation();
        }

        private Cell TargetAt(Vector2 position)
        {
            var picked = panel?.Pick(position);
            for (var element = picked; element != null && element != this; element = element.parent)
            {
                if (element.userData is Cell cell && cell.IsBackpack != source.IsBackpack)
                    return cell;
            }
            return null;
        }

        private void SetDropTarget(Cell target)
        {
            if (dropTarget == target)
                return;
            if (dropTarget != null)
                RestoreCellOutline(dropTarget);
            dropTarget = target;
            if (dropTarget != null)
                PrototypeStyle.Outline(dropTarget.View, PrototypeStyle.Ally, 5);
        }

        private void RestoreCellOutline(Cell cell) => PrototypeStyle.Outline(cell.View,
            !cell.IsBackpack && keyboardSlot == cell.Index ? PrototypeStyle.Accent : PrototypeStyle.Edge, 5);

        private void MoveGhost()
        {
            var local = this.WorldToLocal(pointer);
            ghost.style.left = local.x - (TileSize - 7) / 2f;
            ghost.style.top = local.y - (TileSize - 7) / 2f;
        }

        private void CancelInteraction()
        {
            int captured = pointerId;
            pointerId = -1;
            hold?.Pause();
            hold = null;
            if (source != null)
                source.View.style.opacity = 1f;
            source = null;
            SetDropTarget(null);
            ghost?.RemoveFromHierarchy();
            ghost = null;
            HideTooltip();
            if (captured >= 0 && panel != null && this.HasPointerCapture(captured))
                this.ReleasePointer(captured);
        }

        private void Swap(Cell from, Cell target)
        {
            if (target == null || from.IsBackpack == target.IsBackpack)
                return;

            var bagCell = from.IsBackpack ? from : target;
            var worn = from.IsBackpack ? target : from;
            if (!run.TryEquip(memberId, worn.Index, bagCell.Item?.Id))
                return;

            while (bag.Count <= bagCell.Index)
                bag.Add(null);
            bag[bagCell.Index] = worn.Item?.Id;
            keyboardSlot = null;
            HideTooltip();
            changed();
            Render();
            this.Q($"edit-slot-{worn.Index}")?.Focus();
        }

        private void ShowTooltip(Cell cell)
        {
            HideTooltip();
            hovered = cell;
            tooltipPanel = PrototypeStyle.Panel();
            tooltipPanel.name = "equipment-tooltip";
            tooltipPanel.style.position = Position.Absolute;
            tooltipPanel.style.marginTop = 0;
            tooltipPanel.style.width = 208;
            tooltipPanel.style.backgroundColor = (Color)new Color32(32, 34, 40, 255);
            PrototypeStyle.Outline(tooltipPanel, PrototypeStyle.Accent, 7);

            var item = cell.Item.Definition;
            var art = new EquipmentIcon(item);
            art.style.height = 87;
            art.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(art, PrototypeStyle.Edge, 4);
            tooltipPanel.Add(art);
            var name = PrototypeStyle.Text(item.Name, 16, PrototypeStyle.Accent);
            name.name = "equipment-tooltip-name";
            name.style.marginTop = 5;
            name.style.paddingBottom = 4;
            name.style.borderBottomWidth = 1;
            name.style.borderBottomColor = PrototypeStyle.Edge;
            tooltipPanel.Add(name);
            var description = PrototypeStyle.Text(string.IsNullOrWhiteSpace(item.Description) ? BonusText(item.Bonus) : item.Description,
                13, PrototypeStyle.Ink);
            description.name = "equipment-tooltip-description";
            description.style.marginTop = 5;
            tooltipPanel.Add(description);
            IgnorePicking(tooltipPanel);

            var bounds = this.WorldToLocal(cell.View.worldBound);
            float x = bounds.xMax + 8;
            if (x + 208 > layout.width)
                x = bounds.xMin - 216;
            tooltipPanel.style.left = Mathf.Clamp(x, 4, Mathf.Max(4, layout.width - 212));
            tooltipPanel.style.top = Mathf.Clamp(bounds.yMin, 4, Mathf.Max(4, layout.height - 195));
            Add(tooltipPanel);

            static void IgnorePicking(VisualElement element)
            {
                element.pickingMode = PickingMode.Ignore;
                foreach (var child in element.Children())
                    IgnorePicking(child);
            }
        }

        private void HideTooltip()
        {
            tooltipPanel?.RemoveFromHierarchy();
            tooltipPanel = null;
            hovered = null;
        }
    }
}
