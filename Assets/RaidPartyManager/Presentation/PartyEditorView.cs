using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class PartyEditorView : VisualElement
    {
        private readonly PartyRun run;
        private readonly Side side;
        private readonly Action edited;
        private readonly IReadOnlyList<CharacterDefinition> roster;
        private readonly Func<string, Texture2D> portraitFor;
        private readonly Func<string, Texture2D> iconFor;
        private readonly Texture2D statusBackdrop;
        private readonly Texture2D contractsBackdrop;
        private readonly VisualElement overview;
        private readonly VisualElement modalLayer;
        private string memberId;
        private bool showContracts;
        private int reservePage;

        private const float EmptyPortraitAspect = 2f / 3f;
        private const int DefaultReserveSlots = PartyRun.RosterLimit - Rules.Positions;
        private const float PartyCardCaptionHeight = 24f;
        private const float PartyPortraitMargin = 2f;
        private readonly List<string> backpackCells = new List<string>();

        public PartyEditorView(PartyRun run, EncounterData content, Action back, Side side = Side.Party, Action edited = null,
            bool supplyTestItems = true)
        {
            this.run = run;
            this.side = side;
            this.edited = edited;
            roster = side == Side.Party
                ? content.CharacterCatalog.Where(character => character.RosterNumber > 0)
                    .OrderBy(character => character.RosterNumber).ToArray()
                : content.EnemyRoster;
            portraitFor = content.PortraitFor;
            iconFor = content.IconFor;
            if (side == Side.Party && supplyTestItems)
            {
                var catalog = content.StartingEquipment.Concat(content.SkillBooks).Append(content.VictoryReward);
                Action releaseSupply = null;
                RegisterCallback<AttachToPanelEvent>(_ => releaseSupply = run.SupplyEditorBackpack(catalog, 4));
                RegisterCallback<DetachFromPanelEvent>(_ => releaseSupply?.Invoke());
            }
            name = "party-editor";
            style.flexGrow = 1;
            style.backgroundColor = PrototypeStyle.Background;
            if (side == Side.Party)
            {
                statusBackdrop = Resources.Load<Texture2D>("PartyStatusBackdrop");
                if (run.ContractsEnabled) contractsBackdrop = Resources.Load<Texture2D>("PartyContractsBackdrop");
                style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }

            overview = side == Side.Party ? new VisualElement { name = "party-editor-overview" } : new ScrollView();
            overview.style.flexGrow = 1;
            overview.style.minHeight = 0;
            overview.contentContainer.style.paddingLeft = overview.contentContainer.style.paddingRight = 22;
            overview.contentContainer.style.paddingTop = overview.contentContainer.style.paddingBottom = side == Side.Party ? 14 : 18;
            Add(overview);

            var header = PrototypeStyle.Row("party-editor-header");
            header.style.alignItems = Align.Center;
            const int buttonWidth = 100;
            const int headerGap = 6;
            var leftSpacer = new VisualElement { pickingMode = PickingMode.Ignore };
            leftSpacer.style.width = buttonWidth;
            leftSpacer.style.flexShrink = 0;
            if (side == Side.Party && run.ContractsEnabled)
            {
                var toggle = PrototypeStyle.Button("查看合约", "roster-display", null);
                toggle.style.width = buttonWidth;
                toggle.clicked += () => {
                    showContracts = !showContracts;
                    toggle.text = showContracts ? "查看状态" : "查看合约";
                    RenderRoster();
                };
                var contracts = PrototypeStyle.Row();
                contracts.style.flexShrink = 0;
                contracts.Add(toggle);
                if (run.Day > 0)
                {
                    var repay = PrototypeStyle.Button("偿还薪资", "roster-repay-wages", null);
                    repay.clicked += () =>
                    {
                        var popup = ContractPanel.RepayWages(run, () =>
                        {
                            CloseModal();
                            repay.Focus();
                        }, RenderEditedRoster);
                        OpenModal(null, popup, "dismiss-paper");
                    };
                    repay.style.width = buttonWidth;
                    repay.style.marginLeft = headerGap;
                    contracts.Add(repay);
                }
                header.Add(contracts);
            }
            else header.Add(leftSpacer);
            var title = PrototypeStyle.Text(side == Side.Party ? "角色编辑" : "敌方编辑", 27);
            title.style.flexGrow = 1;
            PrototypeStyle.Middle(title);
            header.Add(title);
            var backButton = PrototypeStyle.Button("返回", "back-party-editor", back);
            backButton.style.width = buttonWidth;
            backButton.style.flexShrink = 0;
            if (side == Side.Party && run.ContractsEnabled && run.Day > 0) backButton.style.marginLeft = buttonWidth + headerGap;
            header.Add(backButton);
            overview.Add(header);
            if (side == Side.Enemy && content.EnemyFormations.Count > 0)
                overview.Add(BuildFormationPicker(content.EnemyFormations));

            modalLayer = new VisualElement { name = "character-details-layer" };
            PrototypeStyle.Fill(modalLayer);
            modalLayer.style.backgroundColor = new Color(0f, 0f, 0f, 0.66f);
            modalLayer.style.alignItems = Align.Center;
            modalLayer.style.justifyContent = Justify.Center;
            modalLayer.style.display = DisplayStyle.None;
            Add(modalLayer);

            RenderRoster();
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                if (modalLayer.childCount > 0)
                    CloseModal();
                else
                    back();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        private VisualElement BuildFormationPicker(IReadOnlyList<EnemyFormation> formations)
        {
            const string placeholder = "选择预设敌阵…";
            var choices = new List<string> { placeholder };
            choices.AddRange(formations.Select(group => $"{RankLabel(group.Rank)} · {group.Name}"));
            var row = PrototypeStyle.Row("enemy-formation-picker");
            row.style.alignItems = Align.Center;
            row.style.marginTop = 12;
            var picker = new DropdownField(choices, 0) { name = "enemy-formation" };
            picker.style.width = 256;
            row.Add(picker);
            var apply = PrototypeStyle.Button("载入敌阵", "apply-enemy-formation", () =>
            {
                int index = picker.index - 1;
                if (index < 0 || index >= formations.Count)
                    return;
                run.ResetRoster(formations[index].CreateLineup("enemy").Select(unit =>
                    new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)));
                RenderEditedRoster();
            });
            apply.SetEnabled(false);
            picker.RegisterValueChangedCallback(_ => apply.SetEnabled(picker.index > 0));
            row.Add(apply);
            return row;

            static string RankLabel(EnemyRank rank) => rank switch
            {
                EnemyRank.Elite => "精英",
                EnemyRank.Boss => "首领",
                _ => "普通"
            };
        }

        private static VisualElement PartyRow(string name, float weight)
        {
            var row = PrototypeStyle.Row(name);
            row.style.flexGrow = weight;
            row.style.flexBasis = 0;
            row.style.minHeight = 0;
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.Center;
            return row;
        }

        private void RenderRoster()
        {
            if (side == Side.Party)
                style.backgroundImage = new StyleBackground(showContracts ? contractsBackdrop : statusBackdrop);
            var oldRoster = overview.Q("editor-roster");
            oldRoster?.RemoveFromHierarchy();
            var layout = new VisualElement { name = "editor-roster" };
            layout.style.marginTop = side == Side.Party ? 5 : 28;
            if (side == Side.Party)
            {
                layout.style.alignSelf = Align.Center;
                layout.style.width = Length.Percent(100);
                layout.style.maxWidth = 840;
                layout.style.flexGrow = 1;
                layout.style.flexBasis = 0;
                layout.style.minHeight = 0;
            }

            var drag = new FormationSwap(layout, (from, to) =>
            {
                if (side == Side.Enemy && !to.Position.HasValue && run.Members.Count(member => member.Position.HasValue) == 1)
                    return;
                var destination = to.Position;
                if (side == Side.Enemy && destination.HasValue)
                {
                    int width = run.Members.Single(member => member.Id == from.MemberId).Character.PositionSlots;
                    destination = Math.Min(destination.Value, Rules.Positions - width + 1);
                }
                bool swapWithReserve = side == Side.Party && from.Position.HasValue && !to.Position.HasValue && to.MemberId != null;
                if (swapWithReserve ? run.TryPlace(to.MemberId, from.Position) : run.TryPlace(from.MemberId, destination))
                {
                    GameAudio.Play(SoundCue.UiSwapPositions);
                    RenderEditedRoster();
                }
            }, slot => OpenMember(slot.MemberId));
            var members = run.Members;
            string instructions = "长按立绘换位，点击立绘可替换敌人。";
            if (side == Side.Party)
            {
                string controls = showContracts ? "点击查看合约，长按换位。" :
                    run.Day == 0 ? "点击查看装备，长按换位，右击替换队员。" :
                    run.ContractsEnabled ? "点击查看装备，长按换位，右击查看合约。" :
                        "点击查看装备，长按换位，右击查看员工档案。";
                instructions = $"队员 {members.Count}/{PartyRun.RosterLimit}　·　{controls}";
            }
            var hint = PrototypeStyle.Text(instructions, 13, PrototypeStyle.Muted);
            hint.style.flexShrink = 0;
            layout.Add(hint);
            if (side == Side.Party) layout.Add(PrototypeStyle.Text("出战队伍", 15, PrototypeStyle.Accent));
            var row = side == Side.Party ? PartyRow("editor-positions", 1.15f) : PrototypeStyle.Row("editor-positions");
            if (side == Side.Enemy) row.style.justifyContent = Justify.SpaceAround;
            var displayed = new HashSet<string>();
            for (int index = 0; index < Rules.Positions; index++)
            {
                int position = side == Side.Party ? Rules.Positions - index : index + 1;
                var member = members.FirstOrDefault(entry => entry.Position == position);
                if (member != null)
                    displayed.Add(member.Id);
                row.Add(BuildCard(position, member, drag, row));
                if (side == Side.Enemy && member != null)
                    index += member.Character.PositionSlots - 1;
            }
            layout.Add(row);
            if (side == Side.Party)
            {
                var reserves = members.Where(member => !displayed.Contains(member.Id)).ToArray();
                int slots = Mathf.Clamp(reserves.Length, DefaultReserveSlots, Rules.Positions);
                int pages = Math.Max(1, (reserves.Length + slots - 1) / slots);
                reservePage = Mathf.Clamp(reservePage, 0, pages - 1);
                var heading = PrototypeStyle.Row("editor-bench");
                heading.style.marginTop = 6;
                heading.style.alignItems = Align.Center;
                heading.style.minHeight = 24;
                heading.style.flexShrink = 0;
                var title = PrototypeStyle.Text("闲置成员", 15, PrototypeStyle.Accent);
                title.style.flexGrow = 1;
                heading.Add(title);
                if (pages > 1)
                {
                    var prev = PrototypeStyle.Button("上一页", "reserve-prev", () => { reservePage--; RenderRoster(); });
                    prev.SetEnabled(reservePage > 0);
                    heading.Add(prev);
                    heading.Add(PrototypeStyle.Text($"{reservePage + 1}/{pages}", 13));
                    var next = PrototypeStyle.Button("下一页", "reserve-next", () => { reservePage++; RenderRoster(); });
                    next.SetEnabled(reservePage + 1 < pages);
                    heading.Add(next);
                }
                drag.Register(new FormationSwap.Slot { View = heading, Border = heading, Borderless = true, CanDrop = true });
                layout.Add(heading);
                var seats = PartyRow("reserve-positions", 1);
                for (int i = 0; i < slots; i++)
                    seats.Add(BuildCard(i + 1, reserves.ElementAtOrDefault(reservePage * slots + i), drag, seats, true));
                layout.Add(seats);
                overview.Add(layout);
                return;
            }
            if (members.All(member => displayed.Contains(member.Id)))
            {
                overview.Add(layout);
                return;
            }

            var bench = PrototypeStyle.Row();
            bench.style.alignItems = Align.Center;
            bench.style.flexWrap = Wrap.Wrap;
            bench.style.marginTop = 16;
            var benchTarget = PrototypeStyle.Text("拖到此处移出站位", 13, PrototypeStyle.Muted);
            benchTarget.name = "editor-bench";
            benchTarget.style.paddingLeft = benchTarget.style.paddingRight = 10;
            benchTarget.style.paddingTop = benchTarget.style.paddingBottom = 6;
            PrototypeStyle.Outline(benchTarget, PrototypeStyle.Edge, 4);
            bench.Add(benchTarget);
            drag.Register(new FormationSwap.Slot { View = benchTarget, Border = benchTarget, CanDrop = true });
            foreach (var member in members.Where(entry => !displayed.Contains(entry.Id)))
            {
                var button = PrototypeStyle.Button(member.Character.Name + (member.IsAway ? " · 守候宝箱" : ""), $"edit-member-{member.Id}",
                    () => OpenMember(member.Id));
                button.SetEnabled(!member.IsAway || member.Contract != null);
                RegisterReplacement(button, member);
                bench.Add(button);
                drag.Register(new FormationSwap.Slot
                {
                    View = button, Border = button, MemberId = member.Id, Name = member.Character.Name,
                    Artwork = portraitFor(member.Character.Id), CanDrag = member.Health > 0 && !member.IsAway
                });
            }
            layout.Add(bench);
            overview.Add(layout);
        }

        private VisualElement BuildCard(int position, MemberView member, FormationSwap drag, VisualElement row, bool reserve = false)
        {
            var card = new VisualElement { name = reserve ? $"reserve-position-{position}" : $"edit-position-{position}" };
            int span = member?.Character.PositionSlots ?? 1;
            card.style.width = Length.Percent(side == Side.Party ? 22 : 17 * span);
            if (side == Side.Enemy) card.style.maxWidth = 224 * span;
            var namePlate = PrototypeStyle.Text(member?.Character.Name ?? "空位", 18,
                member != null ? PrototypeStyle.Ink : PrototypeStyle.Muted);
            namePlate.name = "character-name";
            namePlate.style.height = side == Side.Party ? PartyCardCaptionHeight : 42;
            if (side == Side.Party)
            {
                namePlate.style.whiteSpace = WhiteSpace.NoWrap;
                namePlate.style.overflow = Overflow.Hidden;
                namePlate.style.textOverflow = TextOverflow.Ellipsis;
            }
            namePlate.style.backgroundColor = PrototypeStyle.PanelColor;
            PrototypeStyle.Middle(namePlate);
            PrototypeStyle.Outline(namePlate, PrototypeStyle.Edge, 4);
            card.Add(namePlate);

            var art = portraitFor(member?.Character.Id);
            float aspect = art != null ? (float)art.width / art.height : EmptyPortraitAspect;
            if (side == Side.Party)
            {
                card.style.marginLeft = card.style.marginRight = 4;
                float maxWidth = reserve ? 152 : 184;
                card.style.maxWidth = maxWidth;
                row.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    float portraitHeight = Mathf.Max(0, evt.newRect.height - 2 * (PartyCardCaptionHeight + PartyPortraitMargin));
                    card.style.maxWidth = Mathf.Min(maxWidth, portraitHeight * aspect);
                });
            }
            var portrait = new VisualElement { name = "character-portrait" };
            portrait.style.alignSelf = Align.Center;
            portrait.style.width = Length.Percent(100);
            if (side == Side.Enemy) portrait.style.maxWidth = 364f * aspect;
            portrait.style.aspectRatio = new StyleRatio(new Ratio(aspect));
            portrait.style.flexShrink = 0;
            portrait.style.marginTop = portrait.style.marginBottom = side == Side.Party ? PartyPortraitMargin : 7;
            portrait.style.overflow = Overflow.Hidden;
            portrait.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(portrait, PrototypeStyle.Edge, 4);
            if (art == null)
            {
                var placeholder = PrototypeStyle.Text(member == null ? "—" : member.Character.Name, 16, PrototypeStyle.Muted);
                placeholder.style.flexGrow = 1;
                PrototypeStyle.Middle(placeholder);
                portrait.Add(placeholder);
                ProfessionFrame.AddTo(portrait, member?.Character.Profession, null);
            }
            else ProfessionFrame.AddFittedTo(portrait, member.Character.Profession, art);

            if (!reserve)
            {
                var tag = PrototypeStyle.Text($"{position} 号位", 11, PrototypeStyle.Accent);
                tag.name = "position-tag";
                tag.style.position = Position.Absolute;
                tag.style.top = tag.style.left = 6;
                tag.style.paddingLeft = tag.style.paddingRight = 5;
                tag.style.backgroundColor = PrototypeStyle.Background;
                portrait.Add(tag);
            }
            if (member != null && member.Health == 0)
                portrait.style.opacity = 0.55f;
            card.Add(portrait);

            var life = new VisualElement { name = "character-health" };
            life.style.height = side == Side.Party ? PartyCardCaptionHeight : 33;
            life.style.overflow = Overflow.Hidden;
            life.style.backgroundColor = PrototypeStyle.Inset;
            PrototypeStyle.Outline(life, PrototypeStyle.Edge, 4);
            if (!showContracts && member != null && member.Health > 0)
            {
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(fill);
                fill.style.right = StyleKeyword.Auto;
                fill.style.width = Length.Percent(100f * member.Health / member.EffectiveStats.Health);
                fill.style.backgroundColor = Color.Lerp(PrototypeStyle.Ally, PrototypeStyle.Inset, 0.62f);
                life.Add(fill);
            }
            var hp = PrototypeStyle.Text(member == null ? "—" : $"生命  {HealthText(member.Health, member.EffectiveStats.Health)}", 14);
            if (showContracts)
            {
                hp.name = "contract-remaining";
                hp.text = member == null ? "—" : "无合约";
                if (member?.Contract != null)
                {
                    int days = Math.Max(0, member.ContractEndDay - run.ContractDay);
                    hp.text = $"合约剩余 {days} 天";
                    if (days <= 3) hp.style.color = (Color)new Color32(255, 115, 103, 255);
                }
            }
            else if (member?.IsAway == true) hp.text = "暂离 · 守候宝箱";
            hp.style.flexGrow = 1;
            PrototypeStyle.Middle(hp);
            life.Add(hp);
            card.Add(life);

            if (member != null)
            {
                var open = new Button(() => OpenMember(member.Id))
                {
                    name = $"edit-member-{member.Id}",
                    tooltip = side != Side.Party ? $"替换{member.Character.Name}" :
                        showContracts && member.Contract != null ? $"查看{member.Character.Name}的合约" :
                        $"打开{member.Character.Name}的面板和背包"
                };
                PrototypeStyle.Fill(open);
                open.style.marginTop = open.style.marginRight = open.style.marginBottom = open.style.marginLeft = 0;
                open.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(open, Color.clear, 4);
                card.Add(open);
                RegisterReplacement(card, member);
                open.RegisterCallback<PointerEnterEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Accent, 4));
                open.RegisterCallback<PointerLeaveEvent>(_ => PrototypeStyle.Outline(namePlate, PrototypeStyle.Edge, 4));
            }
            drag.Register(new FormationSwap.Slot
            {
                View = card, Border = portrait, Position = reserve ? (int?)null : position, MemberId = member?.Id, Name = member?.Character.Name,
                Artwork = art, CanDrag = member != null && member.Health > 0 && !member.IsAway,
                CanDrop = !reserve || member == null || member.Health > 0 && !member.IsAway
            });
            return card;
        }

        private void OpenDetails(string id, bool dossier = false)
        {
            OpenModal(id, new CharacterEquipmentPopup(run, id, portraitFor, iconFor, backpackCells,
                RenderEditedRoster, CloseModal, ShowContract, dossier), "close-character-details");

            void ShowContract(string selectedId)
            {
                CloseModal();
                OpenContract(run.Members.Single(candidate => candidate.Id == selectedId), () =>
                {
                    if (run.Members.Any(candidate => candidate.Id == selectedId)) OpenDetails(selectedId, dossier: true);
                });
            }
        }

        private void OpenMember(string id)
        {
            var member = run.Members.Single(m => m.Id == id);
            if (side != Side.Party) OpenReplacement(member);
            else if (showContracts && member.Contract != null) OpenContract(member);
            else OpenDetails(id);
        }

        private void RegisterReplacement(VisualElement target, MemberView member)
        {
            if (side == Side.Party && run.Day > 0)
                target.tooltip = run.ContractsEnabled ? "右击查看合约" : "右击查看员工档案";
            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1 || memberId != null)
                    return;
                if (side != Side.Party || run.Day == 0) OpenReplacement(member);
                else if (run.ContractsEnabled && member.Contract != null) OpenContract(member);
                else OpenDetails(member.Id, dossier: true);
                evt.StopPropagation();
            });
        }

        private void OpenContract(MemberView member, Action closed = null)
        {
            if (!run.ContractsEnabled) return;
            var popup = ContractPanel.Negotiate(member.Character, run.ContractDay, member.Trait, 0, member.Contract,
                run.ExpectedDailyWage(member.Id), member.EmployeeAttributes,
                (days, terms, fee) => run.SetContract(member.Id, days, terms, fee),
                () => { CloseModal(); RenderEditedRoster(); closed?.Invoke(); }, member.Renewals);
            OpenModal(member.Id, popup, "dismiss-paper");
            FirstRunGuide.Practiced(run, GuideStep.ReadContract);
        }

        private void OpenReplacement(MemberView member)
        {
            var inParty = new HashSet<string>(run.Members.Select(entry => entry.Character.Id));
            var popup = new PartyReplacementPopup(member.Character, roster, inParty, portraitFor, replacement =>
            {
                if (run.TryReplaceMember(member.Id, replacement, member.Position))
                {
                    RenderEditedRoster();
                    CloseModal();
                }
            }, CloseModal, side);
            OpenModal(member.Id, popup, "close-party-replacement");
        }

        private void RenderEditedRoster()
        {
            edited?.Invoke();
            RenderRoster();
        }

        private void OpenModal(string id, VisualElement popup, string closeButton)
        {
            memberId = id;
            overview.SetEnabled(false);
            modalLayer.style.display = DisplayStyle.Flex;
            modalLayer.Add(popup);
            popup.Q<Button>(closeButton).Focus();
        }

        private void CloseModal()
        {
            string previous = modalLayer.Q<CharacterEquipmentPopup>()?.SelectedMemberId ?? memberId;
            memberId = null;
            modalLayer.Clear();
            modalLayer.style.display = DisplayStyle.None;
            overview.SetEnabled(true);
            overview.Q<Button>($"edit-member-{previous}")?.Focus();
        }
    }
}
