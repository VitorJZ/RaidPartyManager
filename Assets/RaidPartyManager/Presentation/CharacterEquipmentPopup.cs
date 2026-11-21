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
        private const float SlotSize = 65;
        private const float BagCellMargin = 2;
        private const float BackpackPadding = 9;

        private enum CellKind { EquipmentBag, EquipmentSlot, BookBag, SkillSlot }

        private sealed class Cell
        {
            public VisualElement View;
            public EquipmentInstance Item;
            public SkillBookDefinition LearnedSkill;
            public string EquippedBy;
            public CellKind Kind;
            public int Index;
            public EquipmentDefinition Definition => Item?.Definition ?? LearnedSkill;
            public bool IsSlot => Kind is CellKind.EquipmentSlot or CellKind.SkillSlot;
        }

        private readonly PartyRun run;
        private readonly string memberId;
        private readonly Texture2D portraitArt;
        private readonly List<string> bag;
        private readonly Func<string, Texture2D> iconFor;
        private readonly Action changed;
        private readonly Action close;
        private readonly Action openContract;
        private readonly int[] pages = new int[2];
        private bool showSkillBooks;
        private bool showEmployeeDossier;
        private int Page
        {
            get => pages[showSkillBooks ? 1 : 0];
            set => pages[showSkillBooks ? 1 : 0] = value;
        }
        private (CellKind Kind, int Index)? keyboardSlot;
        private VisualElement tooltipPanel;
        private Cell hovered;
        private Cell source;
        private Cell dropTarget;
        private VisualElement ghost;
        private IVisualElementScheduledItem hold;
        private int pointerId = -1;
        private Vector2 pointer;

        public CharacterEquipmentPopup(PartyRun run, string memberId, Texture2D portraitArt, Func<string, Texture2D> iconFor, List<string> bag,
            Action changed, Action close, Action openContract, bool startAtDossier)
        {
            this.run = run;
            this.memberId = memberId;
            this.portraitArt = portraitArt;
            this.iconFor = iconFor;
            this.bag = bag;
            this.changed = changed;
            this.close = close;
            this.openContract = openContract;
            showEmployeeDossier = startAtDossier;

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
            var inventory = run.Inventory;
            var unplaced = inventory.Select(item => item.Id).ToHashSet();
            for (int index = 0; index < bag.Count; index++)
                if (bag[index] != null && !unplaced.Remove(bag[index]))
                    bag[index] = null;
            foreach (var item in inventory.Where(item => unplaced.Contains(item.Id)))
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
            dialog.style.maxWidth = 730;
            dialog.style.marginTop = 0;
            dialog.style.paddingTop = dialog.style.paddingBottom = 16;
            dialog.style.paddingLeft = dialog.style.paddingRight = 19;

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 5;
            var name = PrototypeStyle.Text(selected.Character.Name, 24);
            name.name = "details-character-name";
            name.style.minWidth = 0;
            name.style.flexShrink = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.tooltip = selected.Character.Name;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            header.Add(name);
            var tabs = PrototypeStyle.Row("character-details-tabs");
            tabs.style.flexGrow = 1;
            tabs.style.flexShrink = 0;
            tabs.style.marginLeft = 12;
            if (!selected.IsAway)
            {
                AddTab("装备与技能", "open-character-loadout", false);
                AddTab("员工档案", "open-employee-dossier", true);
            }
            header.Add(tabs);
            var dismiss = PrototypeStyle.Button("关闭", "close-character-details", close);
            dismiss.style.width = dismiss.style.height = 43;
            dismiss.style.flexShrink = 0;
            dismiss.style.paddingLeft = dismiss.style.paddingRight = 0;
            dismiss.style.whiteSpace = WhiteSpace.NoWrap;
            PrototypeStyle.Middle(dismiss);
            PrototypeStyle.Outline(dismiss, PrototypeStyle.Accent, 23);
            header.Add(dismiss);
            dialog.Add(header);
            if (showEmployeeDossier)
            {
                dialog.style.height = Length.Percent(90);
                dialog.Add(new EmployeeDossier(selected, run.EmployeeProfileFor(memberId), openContract));
                Add(dialog);
                return;
            }
            if (selected.Trait != PlayerTrait.None)
                dialog.Add(PrototypeStyle.Text(PlayerMarket.Describe(selected.Trait), 13, PrototypeStyle.Accent));

            var body = PrototypeStyle.Row();
            body.style.alignItems = Align.Stretch;
            var profile = PrototypeStyle.Panel();
            profile.name = "character-profile";
            profile.style.flexGrow = 1;
            profile.style.flexBasis = 0;
            profile.style.flexDirection = FlexDirection.Row;
            profile.style.minWidth = 0;
            profile.style.marginRight = 12;
            profile.Add(BuildCharacter(selected));
            profile.Add(BuildSlots(selected, inventory));
            body.Add(profile);
            body.Add(BuildBackpack(inventory, selected.EquipmentIds.Any(id => id != null)));
            body.Add(BuildBookmarks());
            dialog.Add(body);
            Add(dialog);

            void AddTab(string title, string id, bool dossier)
            {
                var button = PrototypeStyle.Button(title, id, () =>
                {
                    if (showEmployeeDossier == dossier) return;
                    CancelInteraction();
                    showEmployeeDossier = dossier;
                    Render();
                    this.Q<Button>(id).Focus();
                }, showEmployeeDossier == dossier);
                button.style.marginRight = 6;
                button.style.whiteSpace = WhiteSpace.NoWrap;
                tabs.Add(button);
            }
        }

        private VisualElement BuildSlots(MemberView member, IReadOnlyDictionary<string, EquipmentInstance> inventory)
        {
            var slots = new VisualElement { name = "character-slots" };
            slots.style.marginLeft = 12;
            slots.style.flexShrink = 0;

            var equipped = new VisualElement { name = "character-equipment" };
            for (int slot = 0; slot < member.EquipmentIds.Count; slot++)
            {
                var id = member.EquipmentIds[slot];
                equipped.Add(BuildCell(CellKind.EquipmentSlot, slot, id == null ? null : inventory[id]));
            }
            slots.Add(equipped);

            var skills = new VisualElement { name = "details-skill-slots" };
            skills.style.marginTop = 4;
            for (int slot = 0; slot < member.SkillSlotCount; slot++)
                skills.Add(BuildCell(CellKind.SkillSlot, slot, learned: member.LearnedSkills[slot]));
            slots.Add(skills);
            return slots;
        }

        private VisualElement BuildCharacter(MemberView member)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.flexBasis = 0;
            column.style.minWidth = 0;
            var portrait = new VisualElement { name = "details-portrait" };
            portrait.style.height = 250;
            portrait.style.flexShrink = 0;
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

            var stats = member.EffectiveStats;
            var baseline = member.Character.BaseStats;
            var attributes = PrototypeStyle.Row("details-attributes");
            attributes.style.marginTop = 5;
            attributes.Add(Attribute("攻击", StatText(stats.Attack, baseline.Attack), "details-attack"));
            attributes.Add(Attribute("防御", StatText(stats.Defense, baseline.Defense), "details-defense"));
            attributes.Add(Attribute("特防", StatText(stats.SpecialDefense, baseline.SpecialDefense), "details-special-defense"));
            column.Add(attributes);

            if (!string.IsNullOrWhiteSpace(member.Character.TraitText))
            {
                var trait = PrototypeStyle.Text(member.Character.TraitText, 12, PrototypeStyle.Muted);
                trait.name = "details-fixed-trait";
                trait.style.marginTop = 8;
                trait.style.flexGrow = 1;
                trait.style.minHeight = 48;
                trait.style.paddingTop = trait.style.paddingBottom = 7;
                trait.style.paddingLeft = trait.style.paddingRight = 8;
                trait.style.backgroundColor = PrototypeStyle.Inset;
                PrototypeStyle.Outline(trait, PrototypeStyle.Edge, 4);
                column.Add(trait);
            }
            return column;
        }

        private static VisualElement Attribute(string title, string value, string name)
        {
            var stat = PrototypeStyle.Text($"{title} {value}", 12);
            stat.name = name;
            stat.style.flexGrow = 1;
            PrototypeStyle.Middle(stat);
            return stat;
        }

        private VisualElement BuildBookmarks()
        {
            var bookmarks = new VisualElement { name = "backpack-bookmarks" };
            bookmarks.style.paddingTop = 32;
            bookmarks.style.flexShrink = 0;
            Bookmark(false, "装备", "backpack-equipment-tab");
            Bookmark(true, "技能书", "backpack-skill-tab");
            return bookmarks;

            void Bookmark(bool books, string title, string name)
            {
                bool active = showSkillBooks == books;
                var tab = PrototypeStyle.Button(title, name, () =>
                {
                    if (showSkillBooks == books) return;
                    showSkillBooks = books;
                    keyboardSlot = null;
                    ChangePage();
                    this.Q(name)?.Focus();
                });
                tab.style.width = 62;
                tab.style.height = 36;
                tab.style.paddingLeft = tab.style.paddingRight = 3;
                tab.style.marginLeft = -1;
                tab.style.marginRight = 0;
                tab.style.marginBottom = 4;
                tab.style.whiteSpace = WhiteSpace.NoWrap;
                PrototypeStyle.Outline(tab, active ? PrototypeStyle.Accent : PrototypeStyle.Edge, 0);
                tab.style.borderTopRightRadius = tab.style.borderBottomRightRadius = 6;
                tab.style.backgroundColor = active ? (Color)new Color32(67, 49, 35, 255) : PrototypeStyle.Inset;
                tab.style.color = active ? PrototypeStyle.Accent : PrototypeStyle.Muted;
                bookmarks.Add(tab);
            }
        }

        private VisualElement BuildBackpack(IReadOnlyDictionary<string, EquipmentInstance> inventory, bool canUnequip)
        {
            var backpack = PrototypeStyle.Panel();
            backpack.name = "character-backpack";
            backpack.style.width = Columns * (TileSize + 2 * BagCellMargin) + 2 * BackpackPadding + 2;
            backpack.style.maxWidth = Length.Percent(50);
            backpack.style.minWidth = 0;
            backpack.style.flexShrink = 0;
            backpack.style.paddingLeft = backpack.style.paddingRight = BackpackPadding;
            var header = PrototypeStyle.Row();
            header.style.marginBottom = 5;
            header.style.alignItems = Align.Center;
            header.style.flexWrap = Wrap.Wrap;
            header.Add(PrototypeStyle.Text("共享背包", 17, PrototypeStyle.Accent));

            var books = showSkillBooks ? run.SkillBooks : null;
            var wearers = showSkillBooks ? null : run.Members
                .SelectMany(member => member.EquipmentIds.Select((id, slot) =>
                    (Id: id, Label: $"{member.Character.Name} · 装备槽 {slot + 1}")))
                .Where(entry => entry.Id != null).ToDictionary(entry => entry.Id, entry => entry.Label);
            int occupiedSpan = showSkillBooks ? books.Count : bag.FindLastIndex(id => id != null) + 1;
            int pageCount = Math.Max(1, (occupiedSpan + PageSize - 1) / PageSize);
            if (!showSkillBooks && canUnequip && occupiedSpan == pageCount * PageSize && !bag.Take(occupiedSpan).Contains(null))
                pageCount++;
            Page = Math.Min(Page, pageCount - 1);
            if (pageCount > 1)
            {
                var navigation = PrototypeStyle.Row();
                var back = PrototypeStyle.Button("上一页", "bag-previous", () => { Page--; ChangePage(); });
                var next = PrototypeStyle.Button("下一页", "bag-next", () => { Page++; ChangePage(); });
                back.SetEnabled(Page > 0);
                next.SetEnabled(Page + 1 < pageCount);
                navigation.Add(back);
                var pageLabel = PrototypeStyle.Text($"{Page + 1}/{pageCount}", 13, PrototypeStyle.Muted);
                pageLabel.name = "equipment-page";
                navigation.Add(pageLabel);
                navigation.Add(next);
                header.Add(navigation);
            }
            backpack.Add(header);

            var grid = new VisualElement { name = "backpack-grid" };
            grid.style.marginTop = 5;
            for (int row = 0; row < Columns; row++)
            {
                var line = PrototypeStyle.Row();
                for (int col = 0; col < Columns; col++)
                {
                    int index = Page * PageSize + row * Columns + col;
                    var item = showSkillBooks ? (index < books.Count ? books[index] : null)
                        : (index < bag.Count && bag[index] != null ? inventory[bag[index]] : null);
                    string wearer = item == null ? null : wearers?.GetValueOrDefault(item.Id);
                    line.Add(BuildCell(showSkillBooks ? CellKind.BookBag : CellKind.EquipmentBag, index, item, equippedBy: wearer));
                }
                grid.Add(line);
            }
            backpack.Add(grid);
            if (showSkillBooks && books.Count == 0)
                backpack.Add(PrototypeStyle.Text("背包里暂时没有技能书", 13, PrototypeStyle.Muted));
            return backpack;
        }

        private void ChangePage()
        {
            CancelInteraction();
            Render();
        }

        private static string CellName(CellKind kind, int index) => kind switch
        {
            CellKind.EquipmentBag => $"bag-cell-{index}",
            CellKind.EquipmentSlot => $"edit-slot-{index}",
            CellKind.BookBag => $"book-cell-{index}",
            _ => $"skill-slot-{index}"
        };

        private VisualElement BuildCell(CellKind kind, int index, EquipmentInstance item = null,
            SkillBookDefinition learned = null, string equippedBy = null)
        {
            bool selectable = item != null || kind is CellKind.EquipmentSlot or CellKind.SkillSlot;
            var view = new VisualElement { name = CellName(kind, index), focusable = selectable, tabIndex = selectable ? 0 : -1 };
            var cell = new Cell { View = view, Kind = kind, Index = index, Item = item, LearnedSkill = learned, EquippedBy = equippedBy };
            view.userData = cell;
            view.style.width = view.style.height = cell.IsSlot ? SlotSize : TileSize;
            view.style.flexShrink = cell.IsSlot ? 0 : 1;
            view.style.minWidth = 0;
            view.style.marginLeft = view.style.marginRight = cell.IsSlot ? 0 : BagCellMargin;
            view.style.marginBottom = cell.IsSlot ? 5 : BagCellMargin;
            if (!cell.IsSlot)
                view.style.marginTop = BagCellMargin;
            view.style.backgroundColor = cell.Definition == null ? PrototypeStyle.Inset : (Color)new Color32(36, 38, 45, 255);
            RestoreCellOutline(cell);
            if (cell.Definition != null)
            {
                var art = new EquipmentIcon(cell.Definition, iconFor(cell.Definition.Id),
                    showScroll: kind != CellKind.SkillSlot)
                {
                    name = kind switch
                    {
                        CellKind.EquipmentBag => $"edit-item-{item.Id}",
                        CellKind.BookBag => $"edit-book-{item.Id}",
                        CellKind.SkillSlot => "learned-skill-icon",
                        _ => "equipment-icon"
                    }
                };
                view.Add(art);
                if (equippedBy != null)
                    view.Add(new EquippedItemBorder());
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
                if (item != null)
                    view.RegisterCallback<PointerDownEvent>(evt => BeginHold(evt, cell));
            }
            if (cell.IsSlot)
            {
                bool isEquipment = kind == CellKind.EquipmentSlot;
                string slotName = isEquipment ? $"装备槽 {index + 1}" : $"技能槽 {index + 1}";
                var caption = PrototypeStyle.Text(cell.Definition?.Name ?? slotName, 10,
                    cell.Definition == null ? PrototypeStyle.Muted : PrototypeStyle.Ink);
                caption.name = isEquipment ? $"equipped-name-{index}" : $"skill-name-{index}";
                caption.pickingMode = PickingMode.Ignore;
                caption.style.whiteSpace = WhiteSpace.NoWrap;
                caption.style.overflow = Overflow.Hidden;
                caption.style.textOverflow = TextOverflow.Ellipsis;
                caption.style.flexGrow = cell.Definition == null ? 1 : 0;
                PrototypeStyle.Middle(caption);
                view.Add(caption);
            }

            view.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (source != null || !selectable)
                    return;
                if (!cell.IsSlot && keyboardSlot is { } selected)
                {
                    if (this.Q(CellName(selected.Kind, selected.Index))?.userData is Cell slot && CanDrop(cell, slot))
                        ApplyDrop(cell, slot);
                }
                else if (cell.IsSlot)
                {
                    keyboardSlot = (kind, index);
                    showSkillBooks = kind == CellKind.SkillSlot;
                    ChangePage();
                    this.Q(view.name)?.Focus();
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
            ghost = new EquipmentIcon(source.Item.Definition, iconFor(source.Item.Definition.Id)) { name = "equipment-drag-preview" };
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
            bool wasDragging = ghost != null;
            Cell target = ghost != null ? TargetAt(evt.position) : null;
            CancelInteraction();
            if (target != null)
                ApplyDrop(from, target);
            else if (wasDragging) GameAudio.Play(SoundCue.UiRestricted, .37f);
            evt.StopPropagation();
        }

        private Cell TargetAt(Vector2 position)
        {
            var picked = panel?.Pick(position);
            for (var element = picked; element != null && element != this; element = element.parent)
            {
                if (element.userData is Cell cell && CanDrop(source, cell))
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
            keyboardSlot == (cell.Kind, cell.Index) || cell.LearnedSkill != null ? PrototypeStyle.Accent : PrototypeStyle.Edge, 5);

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

        private bool CanDrop(Cell from, Cell target) => (from.Kind, target.Kind) switch
        {
            (CellKind.EquipmentBag, CellKind.EquipmentSlot) or (CellKind.EquipmentSlot, CellKind.EquipmentBag) => true,
            (CellKind.BookBag, CellKind.SkillSlot) => run.CanLearnSkill(memberId, target.Index, from.Item?.Id),
            _ => false
        };

        private void ApplyDrop(Cell from, Cell target)
        {
            Cell slot;
            if (from.Kind == CellKind.BookBag)
            {
                if (!run.TryLearnSkill(memberId, target.Index, from.Item.Id))
                    return;
                GameAudio.Play(SoundCue.SkillLearn);
                slot = target;
            }
            else
            {
                var bagCell = from.Kind == CellKind.EquipmentBag ? from : target;
                slot = from.Kind == CellKind.EquipmentSlot ? from : target;
                bool unequipping = from == slot && (bagCell.Item == null || bagCell.Item.Id == slot.Item.Id);
                if (!run.TryEquip(memberId, slot.Index, unequipping ? null : bagCell.Item?.Id))
                    return;

                GameAudio.Play(unequipping ? SoundCue.Unequip : SoundCue.Equip);
                if (unequipping && bagCell.Item == null)
                {
                    int oldCell = bag.IndexOf(slot.Item.Id);
                    if (oldCell >= 0)
                        bag[oldCell] = null;
                    while (bag.Count <= bagCell.Index)
                        bag.Add(null);
                    bag[bagCell.Index] = slot.Item.Id;
                }
            }

            keyboardSlot = null;
            HideTooltip();
            changed();
            Render();
            this.Q(CellName(slot.Kind, slot.Index))?.Focus();
        }

        private void ShowTooltip(Cell cell)
        {
            HideTooltip();
            hovered = cell;
            var item = cell.Definition;
            tooltipPanel = new EquipmentTooltip(item, iconFor(item.Id), showScroll: cell.Kind != CellKind.SkillSlot);
            if (cell.EquippedBy != null)
            {
                var wearer = PrototypeStyle.Text($"已穿戴 · {cell.EquippedBy}", 12, PrototypeStyle.Ally);
                wearer.name = "equipped-item-wearer";
                wearer.pickingMode = PickingMode.Ignore;
                wearer.style.marginTop = 5;
                tooltipPanel.Add(wearer);
            }

            var bounds = this.WorldToLocal(cell.View.worldBound);
            float x = bounds.xMax + 8;
            if (x + EquipmentTooltip.Width > layout.width)
                x = bounds.xMin - EquipmentTooltip.Width - 8;
            tooltipPanel.style.left = Mathf.Clamp(x, 4, Mathf.Max(4, layout.width - EquipmentTooltip.Width - 4));
            tooltipPanel.style.top = Mathf.Clamp(bounds.yMin, 4, Mathf.Max(4, layout.height - (item is SkillBookDefinition ? 249 : cell.EquippedBy != null ? 220 : 195)));
            Add(tooltipPanel);
        }

        private void HideTooltip()
        {
            tooltipPanel?.RemoveFromHierarchy();
            tooltipPanel = null;
            hovered = null;
        }
    }
}
