using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class RunTopBar : VisualElement
    {
        public const int Height = 48;
        private const int VisibleItemLimit = 16;
        private const int ItemWidth = 33;
        private const int ItemGap = 4;
        private readonly VisualElement root;
        private readonly PartyRun party;
        private readonly EncounterData content;
        private readonly Func<int> readGold;
        private readonly Label gold;
        private readonly Label calendar;
        private readonly VisualElement coin;
        private readonly VisualElement bag;
        private readonly VisualElement rewardLayer;
        private readonly HashSet<string> flyingItems = new HashSet<string>();
        private Action afterLastFlight;
        private int flyingGold;
        private int visibleItemLimit = VisibleItemLimit;
        private readonly VisualElement items;
        private readonly VisualElement actions;
        private readonly Button teamButton;
        private sealed class RenewalReminder
        {
            public int Day = -1;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PartyRun, RenewalReminder> reminders = new();
        private IVisualElementScheduledItem renewalPulse;
        private PartyEditorView editor;
        private EquipmentTooltip itemTooltip;

        public RunTopBar(VisualElement root, PartyRun party, EncounterData content, Action openEditor = null,
            string editorName = "open-party-editor", Func<int> goldOverride = null, string goldName = "run-gold",
            VisualElement actions = null, Action<bool> settingsChanged = null, Action openEnemyEditor = null,
            Action resetAdventure = null, IReadOnlyList<GoldExpense> expenseOverride = null, VisualElement modalRoot = null)
        {
            this.root = root;
            modalRoot ??= root;
            this.party = party;
            this.content = content;
            this.actions = actions;
            readGold = goldOverride ?? (() => party.Gold);
            name = "run-top-bar";
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = Height;
            style.flexShrink = 0;
            style.paddingLeft = style.paddingRight = 10;
            style.backgroundColor = (Color)new Color32(29, 40, 47, 255);
            style.borderBottomColor = (Color)new Color32(72, 91, 94, 255);
            style.borderBottomWidth = 1;

            var wallet = new Button(OpenLedger) { name = "run-wallet", tooltip = "查看最近三天的开销" };
            wallet.style.flexDirection = FlexDirection.Row;
            wallet.style.backgroundColor = Color.clear;
            wallet.style.borderTopWidth = wallet.style.borderBottomWidth = wallet.style.borderLeftWidth = wallet.style.borderRightWidth = 0;
            wallet.style.alignItems = Align.Center;
            wallet.style.flexShrink = 0;
            wallet.style.marginRight = 13;
            coin = new GoldRewardIcon { name = "run-gold-icon" };
            coin.style.width = coin.style.height = 31;
            calendar = PrototypeStyle.Text("", 15, PrototypeStyle.Ink);
            calendar.name = "run-day"; wallet.Add(calendar);
            wallet.Add(coin);
            gold = PrototypeStyle.Text("", 17, PrototypeStyle.Accent);
            gold.name = goldName;
            gold.style.whiteSpace = WhiteSpace.NoWrap;
            wallet.Add(gold);
            Add(wallet);

            if (openEditor != null)
            {
                teamButton = IconButton("team-icon", editorName, "队伍编辑", DrawTeam, () =>
                {
                    HideTooltip();
                    openEditor();
                });
                if (FirstRunGuide.AtDeparture(party) && !FirstRunGuide.Prepared)
                    PrototypeStyle.Outline(teamButton, PrototypeStyle.Accent, 4);
                teamButton.style.marginRight = 8;
                Add(teamButton);
            }

            bag = new Image { name = "run-backpack-icon", image = content.BackpackIcon, pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit };
            bag.style.width = bag.style.height = 29;
            bag.style.flexShrink = 0;
            bag.style.marginRight = 5;
            Add(bag);
            items = PrototypeStyle.Row("run-backpack-items");
            items.style.alignItems = Align.Center;
            items.style.flexGrow = 1;
            items.style.minWidth = 0;
            items.style.flexBasis = 0;
            items.style.overflow = Overflow.Hidden;
            Add(items);
            items.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width <= 0)
                    return;
                int fit = Mathf.Clamp(Mathf.FloorToInt(evt.newRect.width / (ItemWidth + ItemGap)), 0, VisibleItemLimit);
                if (fit == visibleItemLimit)
                    return;
                visibleItemLimit = fit;
                Refresh();
            });
            rewardLayer = new VisualElement { name = "reward-flight-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(rewardLayer);

            if (actions != null)
                Add(actions);
            var settings = IconButton("settings-icon", "open-settings", "设置", DrawGear, null);
            settings.clicked += OpenSettings;
            settings.style.marginLeft = 8;
            Add(settings);
            RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
            Refresh();

            void OpenLedger()
            {
                HideTooltip();
                settingsChanged?.Invoke(true);
                var underlying = DisableScreen(modalRoot);
                var sheet = ContractPanel.Sheet("金币账单 · 最近三天", out var paper);
                sheet.name = "expense-ledger";
                int today = Math.Max(1, party.Day);
                var entries = expenseOverride ?? party.Expenses;
                for (int day = today; day >= Math.Max(1, today - 2); day--)
                {
                    var lines = entries.Where(e => e.Day == day).Reverse().ToArray();
                    paper.Add(ContractPanel.Text($"第 {day} 天　·　支出 {lines.Sum(e => e.Amount)} 金币", 18));
                    if (lines.Length == 0) paper.Add(ContractPanel.Text("当天没有开销", 14));
                    foreach (var entry in lines)
                        paper.Add(ContractPanel.Text($"{entry.Reason}　　 −{entry.Amount} 金币", 15));
                }
                void Close()
                {
                    sheet.RemoveFromHierarchy();
                    EnableScreen(underlying);
                    settingsChanged?.Invoke(false);
                    root.Q<Button>("run-wallet")?.Focus();
                }
                var close = PrototypeStyle.Button("收起账单", "close-ledger", Close);
                paper.Add(close);
                sheet.RegisterCallback<KeyDownEvent>(evt => {
                    if (evt.keyCode == KeyCode.Escape) { evt.StopPropagation(); Close(); }
                });
                modalRoot.Add(sheet);
                close.Focus();
            }

            void OpenSettings()
            {
                HideTooltip();
                settingsChanged?.Invoke(true);
                var underlying = DisableScreen(modalRoot);
                var panel = new SettingsPanel(() =>
                {
                    EnableScreen(underlying);
                    settingsChanged?.Invoke(false);
                    root.Q<Button>("open-settings")?.Focus();
                });

                if (openEnemyEditor != null && editor == null)
                    panel.AddOption("敌方编辑", "open-enemy-editor", () => { panel.Close(); openEnemyEditor(); });
                if (resetAdventure != null)
                    panel.AddOption("重置讨伐", "reset-adventure", () => { panel.Close(); resetAdventure(); });
                if (AdventureFlow.CanSave(party))
                {
                    var feedback = PrototypeStyle.Text("", 13, PrototypeStyle.Accent);
                    feedback.style.display = DisplayStyle.None;
                    panel.AddOption("保存并退出", "save-and-exit", () => ShowError(AdventureFlow.SaveAndExit(party)));
                    var abandon = panel.AddOption("放弃", "abandon-run", () => ShowError(AdventureFlow.Abandon(party)));
                    abandon.style.color = PrototypeStyle.Enemy;
                    abandon.tooltip = "结束当前讨伐并删除续玩进度";
                    panel.AddFeedback(feedback);

                    void ShowError(string error)
                    {
                        if (error == null)
                            return;
                        feedback.text = error;
                        feedback.style.display = DisplayStyle.Flex;
                    }
                }
                panel.AddOption("返回主菜单", "return-main-menu", () => PrototypeSceneMenu.OpenStandalone("MainMenu"));
                panel.Show(modalRoot);
            }
        }

        private static Button IconButton(string iconName, string buttonName, string tooltip,
            Action<MeshGenerationContext, Rect> draw, Action action)
        {
            var button = PrototypeStyle.Button("", buttonName, action);
            button.tooltip = tooltip;
            button.style.width = 37;
            button.style.height = 34;
            button.style.flexShrink = 0;
            button.style.paddingLeft = button.style.paddingRight = 3;
            button.style.paddingTop = button.style.paddingBottom = 1;
            button.style.justifyContent = Justify.Center;
            var icon = new VisualElement { name = iconName, pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = 26;
            icon.style.flexShrink = 0;
            icon.style.alignSelf = Align.Center;
            icon.generateVisualContent += context => draw(context, icon.contentRect);
            button.Add(icon);
            return button;
        }

        public static void PlaceAtTop(VisualElement parent, RunTopBar bar)
        {
            bar.style.position = Position.Absolute;
            bar.style.top = bar.style.left = bar.style.right = 0;
            parent.Add(bar);
        }

        public void EditParty(Action closed = null, Action edited = null, PartyRun enemyParty = null)
        {
            if (editor != null)
                return;

            var underlying = DisableScreen(root, this);
            StyleEnum<DisplayStyle> actionDisplay = actions?.style.display ?? default;
            bool actionsEnabled = actions?.enabledSelf == true;
            if (actions != null)
            {
                actions.style.display = DisplayStyle.None;
                actions.SetEnabled(false);
            }
            teamButton?.SetEnabled(false);

            Label guide = null;
            editor = new PartyEditorView(enemyParty ?? party, content, Close,
                enemyParty == null ? Side.Party : Side.Enemy, OnEdited, supplyTestItems: !AdventureFlow.CanSave(party));
            PrototypeStyle.Fill(editor);
            editor.style.top = Height;
            root.Add(editor);
            if (enemyParty == null && FirstRunGuide.AtDeparture(party))
            {
                guide = FirstRunGuide.EditorHint(editor);
                bag.style.display = items.style.display = DisplayStyle.None;
                Insert(IndexOf(items), guide);
            }
            Refresh();
            editor.Q<Button>("back-party-editor").Focus();

            void OnEdited()
            {
                Refresh();
                edited?.Invoke();
            }

            void Close()
            {
                if (guide != null)
                {
                    guide.RemoveFromHierarchy();
                    bag.style.display = items.style.display = DisplayStyle.Flex;
                }
                editor.RemoveFromHierarchy();
                editor = null;
                if (actions != null)
                {
                    actions.style.display = actionDisplay;
                    actions.SetEnabled(actionsEnabled);
                }
                teamButton?.SetEnabled(true);
                EnableScreen(underlying);
                Refresh();
                closed?.Invoke();
            }
        }

        private static VisualElement[] DisableScreen(VisualElement root, VisualElement keepEnabled = null)
        {
            var underlying = root.Children().Where(element => element != keepEnabled && element.enabledSelf).ToArray();
            foreach (var element in underlying)
                element.SetEnabled(false);
            return underlying;
        }

        private static void EnableScreen(IEnumerable<VisualElement> underlying)
        {
            foreach (var element in underlying)
                element.SetEnabled(true);
        }

        public bool HasRewardFlights => rewardLayer.childCount > 0;

        public void WhenRewardsArrive(Action action)
        {
            if (HasRewardFlights)
                afterLastFlight = action;
            else
                action();
        }

        public void BringRewardFlightsToFront()
        {
            if (rewardLayer.parent != null)
                rewardLayer.BringToFront();
        }

        public bool CollectReward(VisualElement source, Func<bool> grant)
        {
            var artwork = source?.Q("equipment-icon") ?? source?.Q("reward-gold-icon") ??
                (VisualElement)source?.Q<Image>() ?? source;
            Rect origin = artwork?.worldBound ?? default;
            int previousGold = readGold();
            var previousItems = party.BackpackItems.Select(item => item.Id).ToHashSet();
            if (!grant())
                return false;

            int coins = Mathf.Max(0, readGold() - previousGold);
            var received = party.BackpackItems.Where(item => !previousItems.Contains(item.Id)).ToArray();
            flyingGold += coins;
            foreach (var item in received)
                flyingItems.Add(item.Id);
            Refresh();
            if (coins == 0 && received.Length == 0)
                return true;

            if (rewardLayer.parent == null)
                root.Add(rewardLayer);
            BringRewardFlightsToFront();
            float delay = 0;
            if (coins > 0)
            {
                _ = new RewardFlight(rewardLayer, origin, () => coin, new GoldRewardIcon(), delay, () =>
                {
                    GameAudio.Play(SoundCue.RewardGold);
                    flyingGold -= coins;
                    RefreshGold();
                    FinishFlight();
                });
                delay += 0.06f;
            }
            foreach (var item in received)
            {
                string tileName = "run-backpack-item-" + item.Id;
                var icon = new EquipmentIcon(item.Definition, content.IconFor(item.Definition.Id));
                _ = new RewardFlight(rewardLayer, origin, () => items.Q(tileName) ?? bag, icon, delay, () =>
                {
                    GameAudio.Play(item.Definition is SkillBookDefinition ? SoundCue.RewardSkillbook : SoundCue.RewardLoot);
                    flyingItems.Remove(item.Id);
                    if (items.Q(tileName) is { } tile)
                    {
                        tile.style.opacity = 1;
                        tile.pickingMode = PickingMode.Position;
                    }
                    FinishFlight();
                });
                delay += 0.06f;
            }
            return true;
        }

        private void FinishFlight()
        {
            if (HasRewardFlights)
                return;
            rewardLayer.RemoveFromHierarchy();
            var arrived = afterLastFlight;
            afterLastFlight = null;
            arrived?.Invoke();
        }

        private void RefreshGold()
        {
            calendar.text = party.Day > 0 ? $"第 {party.Day} 天　" : "";
            gold.text = $"金币  {Mathf.Max(0, readGold() - flyingGold)}" +
                (party.PayrollDebt > 0 ? $"　欠薪 {party.PayrollDebt}" : "");
            calendar.tooltip = "进入新节点推进 1 天。工资按合同约定的日期扣款，点击金币可查看账单。";
            if (teamButton == null || party.Day == 0) return;
            bool due = party.NeedsRenewal;
            teamButton.style.backgroundColor = due ? new Color(.35f, .065f, .065f) : PrototypeStyle.Inset;
            var icon = teamButton.Q("team-icon");
            icon.MarkDirtyRepaint();
            UpdateRenewalPulse(icon, due);
            teamButton.tooltip = due ? "右击队员续约 · " + string.Join("、", party.Members
                .Where(m => m.ContractEndDay - party.Day <= 3)
                .Select(m => $"{m.Character.Name}剩 {m.ContractEndDay - party.Day} 天")) : "编队 · 右击队员查看合约";
            var adventure = AdventureFlow.Active;
            if (due && AdventureFlow.OnRoute && adventure.Party == party &&
                (adventure.Current is { Resolved: true, Kind: NodeKind.Event or NodeKind.TreasureChest } ||
                 adventure.Current == null && adventure.LastClosedEventDay == party.Day))
                PlayRenewalReminder();
            if (!party.CanAdvanceDay) gold.text += "　出发前请续约";
        }

        private void PlayRenewalReminder()
        {
            var reminder = reminders.GetValue(party, _ => new RenewalReminder());
            if (reminder.Day == party.Day) return;
            reminder.Day = party.Day;
            GameAudio.Play(SoundCue.UiTurnAdvance);
        }

        private void UpdateRenewalPulse(VisualElement icon, bool due)
        {
            if (!due)
            {
                renewalPulse?.Pause();
                icon.style.opacity = 1f;
                return;
            }

            if (renewalPulse == null)
                renewalPulse = icon.schedule.Execute(() =>
                {
                    double phase = Time.realtimeSinceStartupAsDouble % 1;
                    icon.style.opacity = .65f + .35f * Mathf.Cos((float)phase * Mathf.PI * 2);
                }).Every(60);
            else if (!renewalPulse.isActive)
                renewalPulse.Resume();
        }

        public void Refresh()
        {
            RefreshGold();
            HideTooltip();
            items.Clear();
            var backpack = party.BackpackItems;
            foreach (var instance in backpack.Skip(Math.Max(0, backpack.Count - visibleItemLimit)))
            {
                var item = instance.Definition;
                var art = content.IconFor(item.Id);
                var tile = new VisualElement { name = "run-backpack-item-" + instance.Id };
                tile.style.width = ItemWidth;
                tile.style.height = 32;
                tile.style.flexShrink = 0;
                tile.style.marginRight = ItemGap;
                if (flyingItems.Contains(instance.Id))
                {
                    tile.style.opacity = 0;
                    tile.pickingMode = PickingMode.Ignore;
                }
                tile.style.backgroundColor = PrototypeStyle.Inset;
                PrototypeStyle.Outline(tile, PrototypeStyle.Edge, 3);
                tile.Add(new EquipmentIcon(item, art));
                tile.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                        return;
                    HideTooltip();
                    itemTooltip = new EquipmentTooltip(item, art);
                    itemTooltip.ShowAbove(root, tile);
                });
                tile.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
                items.Add(tile);
            }
        }

        private void HideTooltip()
        {
            var current = itemTooltip;
            itemTooltip = null;
            // UI Toolkit clears the child list before sending detach and pointer-leave events.
            // During those callbacks the tooltip may still report its former parent.
            if (current?.parent is { } parent && parent.hierarchy.IndexOf(current) >= 0)
                current.RemoveFromHierarchy();
        }

        private static void DrawGear(MeshGenerationContext context, Rect rect)
        {
            var pen = context.painter2D;
            float scale = Mathf.Min(rect.width, rect.height) / 26f;
            var center = rect.center;
            pen.fillColor = PrototypeStyle.Ink;
            pen.BeginPath();
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI / 16f;
                float radius = i % 4 is 1 or 2 ? 10.6f : 8.3f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * scale;
                if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
            }
            pen.ClosePath();
            pen.Fill();
            pen.fillColor = PrototypeStyle.PanelColor;
            pen.BeginPath();
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.1f * scale;
                if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
            }
            pen.ClosePath();
            pen.Fill();
        }

        private void DrawTeam(MeshGenerationContext context, Rect rect)
        {
            float scale = Mathf.Min(rect.width, rect.height) / 30f;
            var center = rect.center;
            var pen = context.painter2D;
            bool due = party.NeedsRenewal;
            var rear = due ? new Color(1f, .39f, .35f) : PrototypeStyle.Muted;
            Person(-8, 0.77f, rear);
            Person(8, 0.77f, rear);
            Person(0, 1f, due ? new Color(1f, .62f, .57f) : PrototypeStyle.Ink);

            void Person(float offset, float size, Color color)
            {
                pen.fillColor = color;
                pen.BeginPath();
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    var point = center + new Vector2(offset + Mathf.Cos(angle) * size * 3.6f,
                        Mathf.Sin(angle) * size * 3.6f - size * 5f) * scale;
                    if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
                }
                pen.ClosePath();
                pen.Fill();

                pen.BeginPath();
                pen.MoveTo(center + new Vector2(offset - 6f * size, 8f * size) * scale);
                pen.BezierCurveTo(center + new Vector2(offset - 5.8f * size, -1f * size) * scale,
                    center + new Vector2(offset + 5.8f * size, -1f * size) * scale,
                    center + new Vector2(offset + 6f * size, 8f * size) * scale);
                pen.ClosePath();
                pen.Fill();
            }
        }
    }
}
