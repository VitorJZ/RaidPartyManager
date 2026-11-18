using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class RunTopBar : VisualElement
    {
        public const int Height = 48;
        private const int VisibleItemLimit = 16;
        private readonly VisualElement root;
        private readonly PartyRun party;
        private readonly EncounterData content;
        private readonly Func<int> readGold;
        private readonly Label gold;
        private readonly VisualElement items;
        private EquipmentTooltip tooltip;

        public RunTopBar(VisualElement root, PartyRun party, EncounterData content, Action openEditor = null,
            string editorName = "open-party-editor", Func<int> goldOverride = null, string goldName = "run-gold",
            VisualElement actions = null, Action<bool> settingsChanged = null, Action openEnemyEditor = null)
        {
            this.root = root;
            this.party = party;
            this.content = content;
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

            var wallet = PrototypeStyle.Row("run-wallet");
            wallet.style.alignItems = Align.Center;
            wallet.style.flexShrink = 0;
            wallet.style.marginRight = 13;
            var coin = new GoldRewardIcon { name = "run-gold-icon" };
            coin.style.width = coin.style.height = 31;
            wallet.Add(coin);
            gold = PrototypeStyle.Text("", 17, PrototypeStyle.Accent);
            gold.name = goldName;
            gold.style.whiteSpace = WhiteSpace.NoWrap;
            wallet.Add(gold);
            Add(wallet);

            if (openEditor != null)
            {
                var team = IconButton("team-icon", editorName, "队伍编辑", DrawTeam, () =>
                {
                    HideTooltip();
                    openEditor();
                });
                team.style.marginRight = 8;
                Add(team);
            }

            var bag = new Image { name = "run-backpack-icon", image = content.BackpackIcon, pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit };
            bag.style.width = bag.style.height = 29;
            bag.style.flexShrink = 0;
            bag.style.marginRight = 5;
            Add(bag);
            items = PrototypeStyle.Row("run-backpack-items");
            items.style.alignItems = Align.Center;
            items.style.flexGrow = 1;
            items.style.minWidth = 0;
            items.style.overflow = Overflow.Hidden;
            Add(items);

            if (actions != null)
                Add(actions);
            var settings = IconButton("settings-icon", "open-settings", "设置", DrawGear, null);
            settings.clicked += OpenSettings;
            settings.style.marginLeft = 8;
            Add(settings);
            RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
            Refresh();

            void OpenSettings()
            {
                HideTooltip();
                settingsChanged?.Invoke(true);
                var underlying = DisableScreen(root);
                var overlay = new VisualElement { name = "run-settings" };
                PrototypeStyle.Fill(overlay);
                overlay.style.backgroundColor = new Color(0, 0, 0, 0.7f);
                overlay.style.alignItems = Align.Center;
                overlay.style.justifyContent = Justify.Center;

                var panel = PrototypeStyle.Panel();
                panel.style.width = Length.Percent(88);
                panel.style.maxWidth = 320;
                panel.style.marginTop = 0;
                panel.style.paddingTop = panel.style.paddingBottom = 13;
                panel.style.paddingLeft = panel.style.paddingRight = 16;
                var title = PrototypeStyle.Text("设置", 21, PrototypeStyle.Accent);
                title.style.marginBottom = 10;
                PrototypeStyle.Middle(title);
                panel.Add(title);

                if (openEnemyEditor != null)
                    AddOption("敌方编辑", "open-enemy-editor", () => { Close(); openEnemyEditor(); });
                AddOption("返回主菜单", "return-main-menu", () => PrototypeSceneMenu.OpenStandalone("MainMenu"));
                var close = AddOption("关闭", "close-settings", Close);
                overlay.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Escape) return;
                    Close();
                    evt.StopPropagation();
                });
                overlay.Add(panel);
                root.Add(overlay);
                close.Focus();

                Button AddOption(string text, string id, Action click)
                {
                    var button = PrototypeStyle.Button(text, id, click);
                    button.style.height = 36;
                    panel.Add(button);
                    return button;
                }

                void Close()
                {
                    overlay.RemoveFromHierarchy();
                    EnableScreen(underlying);
                    settingsChanged?.Invoke(false);
                    settings.Focus();
                }
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

        public static void EditParty(VisualElement root, PartyRun party, EncounterData content, Action refresh, Action edited = null)
        {
            var underlying = DisableScreen(root);
            PartyEditorView editor = null;
            editor = new PartyEditorView(party, content, Close, edited: edited);
            PrototypeStyle.Fill(editor);
            root.Add(editor);
            editor.Q<Button>("back-party-editor").Focus();

            void Close()
            {
                editor.RemoveFromHierarchy();
                EnableScreen(underlying);
                refresh();
            }
        }

        private static VisualElement[] DisableScreen(VisualElement root)
        {
            var underlying = root.Children().Where(element => element.enabledSelf).ToArray();
            foreach (var element in underlying)
                element.SetEnabled(false);
            return underlying;
        }

        private static void EnableScreen(IEnumerable<VisualElement> underlying)
        {
            foreach (var element in underlying)
                element.SetEnabled(true);
        }

        public void Refresh()
        {
            gold.text = $"金币  {readGold()}";
            HideTooltip();
            items.Clear();
            var assigned = new HashSet<string>(party.Members.SelectMany(member => member.EquipmentIds));
            foreach (var instance in party.Inventory.Where(item => !assigned.Contains(item.Id)).Concat(party.SkillBooks)
                .Take(VisibleItemLimit))
            {
                var item = instance.Definition;
                var art = content.IconFor(item.Id);
                var tile = new VisualElement { name = "run-backpack-item-" + instance.Id };
                tile.style.width = 33;
                tile.style.height = 32;
                tile.style.flexShrink = 0;
                tile.style.marginRight = 4;
                tile.style.backgroundColor = PrototypeStyle.Inset;
                PrototypeStyle.Outline(tile, PrototypeStyle.Edge, 3);
                tile.Add(new EquipmentIcon(item, art));
                tile.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                        return;
                    HideTooltip();
                    tooltip = new EquipmentTooltip(item, art);
                    tooltip.ShowAbove(root, tile);
                });
                tile.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
                items.Add(tile);
            }
        }

        private void HideTooltip()
        {
            tooltip?.RemoveFromHierarchy();
            tooltip = null;
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

        private static void DrawTeam(MeshGenerationContext context, Rect rect)
        {
            float scale = Mathf.Min(rect.width, rect.height) / 30f;
            var center = rect.center;
            var pen = context.painter2D;
            Person(-8, 0.77f, PrototypeStyle.Muted);
            Person(8, 0.77f, PrototypeStyle.Muted);
            Person(0, 1f, PrototypeStyle.Ink);

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
