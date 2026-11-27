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
        private const float TileSize = 73;
        private const float SlotSize = 65;
        private const float BagCellMargin = 2;
        private const float BackpackPadding = 9;
        private static readonly Color[] MemberColors =
        {
            new Color32(135, 200, 255, 255), new Color32(129, 211, 166, 255),
            new Color32(231, 195, 112, 255), new Color32(194, 166, 251, 255),
            new Color32(243, 162, 144, 255), new Color32(239, 158, 197, 255),
            new Color32(108, 213, 217, 255), new Color32(228, 165, 101, 255),
            new Color32(181, 203, 117, 255), new Color32(188, 196, 210, 255)
        };

        private enum CellKind { EquipmentBag, EquipmentSlot, BookBag, SkillSlot }

        private sealed class Cell
        {
            public VisualElement View;
            public EquipmentInstance Item;
            public SkillBookDefinition LearnedSkill;
            public (string Label, Color Color) Wearer;
            public CellKind Kind;
            public int Index;
            public EquipmentDefinition Definition => Item?.Definition ?? LearnedSkill;
            public bool IsSlot => Kind is CellKind.EquipmentSlot or CellKind.SkillSlot;
            public bool IsWornEquipment => Item != null && (Kind == CellKind.EquipmentSlot || Wearer.Label != null);
        }

        private readonly PartyRun run;
        private string memberId;
        private readonly Func<string, Texture2D> portraitFor;
        private readonly List<string> bag;
        private readonly Func<string, Texture2D> iconFor;
        private readonly Action changed;
        private readonly Action close;
        private readonly Action<string> openContract;
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
        private int pointerId = -1;
        private Vector2 pointer;

        internal string SelectedMemberId => memberId;

        public CharacterEquipmentPopup(PartyRun run, string memberId, Func<string, Texture2D> portraitFor,
            Func<string, Texture2D> iconFor, List<string> bag, Action changed, Action close,
            Action<string> openContract, bool startAtDossier)
        {
            this.run = run;
            this.memberId = memberId;
            this.portraitFor = portraitFor;
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
            var members = run.Members.OrderBy(member => member.Position ?? int.MaxValue).ToArray();
            var selected = members.Single(member => member.Id == memberId);
            bool displayDossier = showEmployeeDossier || selected.IsAway;

            var frame = PrototypeStyle.Row("character-details-frame");
            frame.style.width = Length.Percent(95);
            frame.style.maxWidth = 774;
            frame.style.alignItems = Align.Stretch;
            frame.Add(BuildMemberBookmarks(members));

            var dialog = PrototypeStyle.Panel();
            dialog.name = "character-details";
            dialog.style.flexGrow = 1;
            dialog.style.flexBasis = 0;
            dialog.style.minWidth = 0;
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
            header.Add(ProfessionFrame.Tag(selected.Character.Profession, 14));
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
            frame.Add(dialog);
            Add(frame);

            if (displayDossier)
            {
                frame.style.height = Length.Percent(90);
                dialog.Add(new EmployeeDossier(selected, run.EmployeeProfileFor(memberId),
                    run.ContractsEnabled ? () => openContract(memberId) : null));
                return;
            }

            var inventory = run.Inventory.ToDictionary(item => item.Id);
            if (selected.Trait != PlayerTrait.None)
                dialog.Add(PrototypeStyle.Text(PlayerMarket.Describe(selected.Trait), 13, PrototypeStyle.Trait));

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
            body.Add(BuildBackpack(inventory, members, selected.EquipmentIds.Any(id => id != null)));
            body.Add(BuildBookmarks());
            dialog.Add(body);

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
            var portraitArt = portraitFor(member.Character.Id);
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
            var baseline = member.Character.BaseStats + member.Training.Bonus;
            var attributes = PrototypeStyle.Row("details-attributes");
            attributes.style.marginTop = 5;
            attributes.Add(Attribute("攻击", StatText(stats.Attack, baseline.Attack), "details-attack"));
            attributes.Add(Attribute("防御", StatText(stats.Defense, baseline.Defense), "details-defense"));
            attributes.Add(Attribute("特防", StatText(stats.SpecialDefense, baseline.SpecialDefense), "details-special-defense"));
            column.Add(attributes);

            if (member.Training.Sessions > 0)
            {
                var training = PrototypeStyle.Text(
                    $"训练 {member.Training.Sessions}/{CampTraining.Limit} 次 · {member.Training.Describe()}", 12, PrototypeStyle.Muted);
                training.name = "details-training";
                column.Add(training);
            }

            if (!string.IsNullOrWhiteSpace(member.Character.TraitText))
            {
                var trait = PrototypeStyle.Text("被动：" + member.Character.TraitText, 12, PrototypeStyle.Passive);
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

        private VisualElement BuildMemberBookmarks(IReadOnlyList<MemberView> members)
        {
            var bookmarks = new VisualElement { name = "loadout-member-bookmarks" };
            bookmarks.style.width = 44;
            bookmarks.style.flexShrink = 0;
            bookmarks.style.justifyContent = Justify.Center;
            for (int i = 0; i < Math.Max(PartyRun.RosterLimit, members.Count); i++)
            {
                var member = members.ElementAtOrDefault(i);
                bool selected = member?.Id == memberId;
                string name = $"loadout-member-{i + 1}";
                var bookmark = PrototypeStyle.Button((i + 1).ToString(), name, member == null ? null : () =>
                {
                    if (member.Id == memberId) return;
                    CancelInteraction();
                    keyboardSlot = null;
                    memberId = member.Id;
                    Render();
                    this.Q<Button>(name).Focus();
                });
                bookmark.tooltip = member == null ? "空位" : member.Character.Name + (member.IsAway ? " · 外出中" : "");
                bookmark.EnableInClassList("loadout-member-selected", selected);
                bookmark.SetEnabled(member != null);
                bookmark.style.height = 32;
                bookmark.style.paddingLeft = bookmark.style.paddingRight = 0;
                bookmark.style.marginLeft = 0;
                bookmark.style.marginRight = -1;
                bookmark.style.fontSize = 16;
                bookmark.style.unityFontStyleAndWeight = FontStyle.Bold;
                var color = MemberColors[i % MemberColors.Length];
                bookmark.style.backgroundColor = member == null ? PrototypeStyle.Inset : color;
                bookmark.style.color = member == null ? PrototypeStyle.Muted : PrototypeStyle.Background;
                PrototypeStyle.Outline(bookmark, selected ? PrototypeStyle.Ink : PrototypeStyle.Edge, 0);
                if (selected)
                    bookmark.style.borderTopWidth = bookmark.style.borderLeftWidth = bookmark.style.borderBottomWidth = 2;
                bookmark.style.borderTopLeftRadius = bookmark.style.borderBottomLeftRadius = 5;
                bookmarks.Add(bookmark);
            }
            return bookmarks;
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

        private VisualElement BuildBackpack(IReadOnlyDictionary<string, EquipmentInstance> inventory,
            IReadOnlyList<MemberView> members, bool canUnequip)
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
            var wearers = showSkillBooks ? null : members
                .SelectMany((member, index) => member.EquipmentIds.Select((id, slot) =>
                    (Id: id, Label: $"{member.Character.Name} · 装备槽 {slot + 1}", Color: MemberColors[index % MemberColors.Length])))
                .Where(entry => entry.Id != null).ToDictionary(entry => entry.Id, entry => (entry.Label, entry.Color));
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
                    var wearer = item == null || wearers == null ? default : wearers.GetValueOrDefault(item.Id);
                    line.Add(BuildCell(showSkillBooks ? CellKind.BookBag : CellKind.EquipmentBag, index, item, wearer: wearer));
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
            if (showSkillBooks) FirstRunGuide.SupplyBook(run);
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
            SkillBookDefinition learned = null, (string Label, Color Color) wearer = default)
        {
            bool selectable = item != null || kind is CellKind.EquipmentSlot or CellKind.SkillSlot;
            var view = new VisualElement { name = CellName(kind, index), focusable = selectable, tabIndex = selectable ? 0 : -1 };
            var cell = new Cell { View = view, Kind = kind, Index = index, Item = item, LearnedSkill = learned, Wearer = wearer };
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
                if (wearer.Label != null)
                    view.Add(new EquippedItemBorder(wearer.Color));
                // Every character has a first skill slot. Learning may replace the skill in it.
                if (kind == CellKind.BookBag && run.CanLearnSkill(memberId, 0, item.Id))
                {
                    var glow = new VisualElement { name = "learnable-skill-border", pickingMode = PickingMode.Ignore };
                    PrototypeStyle.Fill(glow);
                    PrototypeStyle.Outline(glow, PrototypeStyle.SkillGold, 5);
                    glow.style.borderTopWidth = glow.style.borderRightWidth = glow.style.borderBottomWidth = glow.style.borderLeftWidth = 2;
                    glow.schedule.Execute(() => glow.style.opacity = .2f + .8f * Mathf.PingPong(Time.unscaledTime / .62f, 1f)).Every(50);
                    view.Add(glow);
                }
                view.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse && source == null)
                        ShowTooltip(cell);
                });
                view.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (hovered == cell)
                        HideTooltip();
                });
                if (item != null)
                    view.RegisterCallback<PointerDownEvent>(evt => PressItem(evt, cell));
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

        private void PressItem(PointerDownEvent evt, Cell cell)
        {
            if (evt.button != 0 || source != null)
                return;
            source = cell;
            pointerId = evt.pointerId;
            pointer = evt.position;
            // Capture before moving so a fast first move outside the item still starts the drag.
            this.CapturePointer(pointerId);
            evt.StopPropagation();
        }

        private void StartDrag()
        {
            HideTooltip();
            ghost = new EquipmentIcon(source.Item.Definition, iconFor(source.Item.Definition.Id)) { name = "equipment-drag-preview" };
            ghost.style.position = Position.Absolute;
            ghost.style.width = ghost.style.height = TileSize - 7;
            ghost.style.backgroundColor = PrototypeStyle.PanelColor;
            PrototypeStyle.Outline(ghost, PrototypeStyle.Accent, 6);
            source.View.style.opacity = 0.48f;
            Add(ghost);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (source == null || evt.pointerId != pointerId)
                return;

            Vector2 position = evt.position;
            if (ghost == null && position == pointer)
                return;

            pointer = position;
            if (ghost == null)
                StartDrag();
            MoveGhost();
            SetDropTarget(TargetAt(pointer));
            evt.StopPropagation();
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

        private void RestoreCellOutline(Cell cell)
        {
            var color = cell.Kind switch
            {
                CellKind.EquipmentSlot => PrototypeStyle.EquipmentSilver,
                CellKind.SkillSlot => PrototypeStyle.SkillGold,
                _ => PrototypeStyle.Edge
            };
            PrototypeStyle.Outline(cell.View, color, 5);
            var style = cell.View.style;
            int width = keyboardSlot == (cell.Kind, cell.Index) ? 3 : cell.IsSlot ? 2 : 1;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = width;
        }

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

        private static bool IsEquipmentSwap(Cell from, Cell target) =>
            from.IsWornEquipment && target.IsWornEquipment && from.Item.Id != target.Item.Id;

        private bool CanDrop(Cell from, Cell target)
        {
            if (IsEquipmentSwap(from, target))
                return run.CanSwapEquipment(from.Item.Id, target.Item.Id);
            return (from.Kind, target.Kind) switch
            {
                (CellKind.EquipmentBag, CellKind.EquipmentSlot) or (CellKind.EquipmentSlot, CellKind.EquipmentBag) => true,
                (CellKind.BookBag, CellKind.SkillSlot) => run.CanLearnSkill(memberId, target.Index, from.Item?.Id),
                _ => false
            };
        }

        private void ApplyDrop(Cell from, Cell target)
        {
            Cell focus = target;
            if (IsEquipmentSwap(from, target))
            {
                if (!run.TrySwapEquipment(from.Item.Id, target.Item.Id))
                    return;
                GameAudio.Play(SoundCue.Equip);
            }
            else if (from.Kind == CellKind.BookBag)
            {
                if (!run.TryLearnSkill(memberId, target.Index, from.Item.Id))
                    return;
                FirstRunGuide.Practiced(run, GuideStep.Learned);
                GameAudio.Play(SoundCue.SkillLearn);
            }
            else
            {
                var bagCell = from.Kind == CellKind.EquipmentBag ? from : target;
                var slot = from.Kind == CellKind.EquipmentSlot ? from : target;
                focus = slot;
                bool unequipping = from == slot && (bagCell.Item == null || bagCell.Item.Id == slot.Item.Id);
                if (!run.TryEquip(memberId, slot.Index, unequipping ? null : bagCell.Item?.Id))
                    return;

                if (!unequipping) FirstRunGuide.Practiced(run, GuideStep.Equipped);
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
            this.Q(CellName(focus.Kind, focus.Index))?.Focus();
        }

        private void ShowTooltip(Cell cell)
        {
            HideTooltip();
            hovered = cell;
            var item = cell.Definition;
            tooltipPanel = new EquipmentTooltip(item, iconFor(item.Id), showScroll: cell.Kind != CellKind.SkillSlot);
            if (cell.Wearer.Label != null)
            {
                var wearer = PrototypeStyle.Text($"已穿戴 · {cell.Wearer.Label}", 12, cell.Wearer.Color);
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
            tooltipPanel.style.top = Mathf.Clamp(bounds.yMin, 4, Mathf.Max(4, layout.height - (item is SkillBookDefinition ? 249 : cell.Wearer.Label != null ? 220 : 195)));
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
