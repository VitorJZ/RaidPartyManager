using System;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class AdventureEventHub : MonoBehaviour
    {
        [SerializeField] private AdventureContentAsset assets;
        private VisualElement root;
        private VisualElement body;
        private VisualElement screen;
        private RunTopBar topBar;
        private AdventureEventWindow eventWindow;
        private AdventureRun run;
        private Font localFont;
        private FontAsset localFontAsset;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            run = AdventureFlow.Select(assets, AdventureFlow.InspectionMode);
            Render();
        }

        private void OnDisable() => PrototypeStyle.ClearDocument(root, localFont, localFontAsset);

        private void Render()
        {
            root.Clear();
            eventWindow = null;
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.backgroundImage = new StyleBackground(assets.Backdrop);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.unityBackgroundImageTintColor = new Color(0.25f, 0.28f, 0.32f, 1);
            root.style.paddingLeft = root.style.paddingRight = 22;
            root.style.paddingTop = 0;
            root.style.paddingBottom = 14;
            topBar = new RunTopBar(root, run.Party, assets.Encounter, OpenEditor, "event-party-editor", goldName: "adventure-gold");
            root.Add(topBar);
            body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            screen = new VisualElement { name = "adventure-screen" };
            screen.style.flexGrow = 1;
            screen.style.minHeight = 0;
            body.Add(screen);
            root.Add(body);
            var header = PrototypeStyle.Row(); header.style.alignItems = Align.Center;
            var title = PrototypeStyle.Text("弥雾码头", 27, PrototypeStyle.Accent);
            title.style.marginLeft = 14; title.style.flexGrow = 1; header.Add(title);
            header.Add(PrototypeStyle.Text($"第 {run.Floor} 层  ·  已完成 {run.CompletedNodes} 个关卡", 14));
            header.style.marginTop = 8;
            screen.Add(header);
            var toolbar = PrototypeStyle.Row(); toolbar.style.marginTop = 14; toolbar.style.alignItems = Align.Center;
            toolbar.Add(PrototypeStyle.Button("事件检查", "event-tab-inspect", () => Switch(true), run.IsInspection));
            toolbar.Add(PrototypeStyle.Button("关卡试游", "event-tab-route", () => Switch(false), !run.IsInspection));
            toolbar.Add(PrototypeStyle.Button("重置当前试游", "reset-adventure", () =>
            { run = AdventureFlow.Select(assets, run.IsInspection, true); Render(); }));
            if (run.IsInspection)
            {
                toolbar.Add(PrototypeStyle.Button("推进 1 个关卡", "inspection-advance", () => { run.AdvanceInspection(); Render(); }));
                toolbar.Add(PrototypeStyle.Button("下一层", "inspection-next-floor", () => { run.NextFloor(); Render(); }));
            }
            screen.Add(toolbar);
            if (!string.IsNullOrEmpty(run.ReturnMessage))
                screen.Add(PrototypeStyle.Text(run.ReturnMessage, 17, PrototypeStyle.Accent));
            if (run.IsInspection) RenderInspection(); else RenderRoute();
            RenderParty();
            if (run.Current != null) PresentVisit();
        }

        private void Switch(bool inspection)
        { run = AdventureFlow.Select(assets, inspection); Render(); }

        private void RenderInspection()
        {
            var hint = PrototypeStyle.Text("直接选择任意事件或场景。检查队伍与战斗演练独立；上方按钮可推进宝箱归队计时或切换楼层。", 13, PrototypeStyle.Muted);
            hint.style.marginTop = hint.style.marginBottom = 10; screen.Add(hint);
            var scroll = new ScrollView { name = "event-catalog" }; scroll.style.flexGrow = 1; scroll.style.minHeight = 0;
            var grid = PrototypeStyle.Row(); grid.style.flexWrap = Wrap.Wrap; scroll.Add(grid); screen.Add(scroll);
            foreach (AdventureEventKind kind in Enum.GetValues(typeof(AdventureEventKind)))
            {
                var captured = kind;
                Card(grid, AdventureFlow.EventName(kind), Gate(kind), "inspect-event-" + kind, assets.EventArt(kind), () =>
                { if (run.Inspect(NodeKind.Event, captured)) PresentVisit(); });
            }
            foreach (var kind in new[] { NodeKind.Town, NodeKind.Camp, NodeKind.Shop, NodeKind.NormalBattle, NodeKind.EliteBattle, NodeKind.Boss })
            {
                var captured = kind;
                Card(grid, AdventureFlow.NodeName(kind), kind == NodeKind.Town ? "四项服务，选择两项" :
                    kind == NodeKind.Camp ? "恢复最大生命的 30%" : kind == NodeKind.Shop ? "拖拽购买，共用金币与背包" : "进入真实自动战斗",
                    "inspect-node-" + kind, assets.NodeIcon(kind), () => { if (run.Inspect(captured)) PresentVisit(); });
            }
        }

        private void RenderRoute()
        {
            var map = PrototypeStyle.Row("dock-route"); map.style.marginTop = 28;
            foreach (var node in run.Route.Nodes)
            {
                var marker = PrototypeStyle.Text(node.Number == 10 ? "10 · Boss" : node.Number.ToString("00"), 18,
                    node.Number == run.Stage ? PrototypeStyle.Accent : node.Number < run.Stage ? PrototypeStyle.Ally : PrototypeStyle.Muted);
                marker.style.flexGrow = 1; marker.style.paddingTop = marker.style.paddingBottom = 20;
                marker.style.backgroundColor = PrototypeStyle.PanelColor; marker.style.marginRight = 5;
                PrototypeStyle.Middle(marker); PrototypeStyle.Outline(marker, PrototypeStyle.Edge, 4); map.Add(marker);
            }
            screen.Add(map);
            var panel = PrototypeStyle.Panel(); panel.style.flexGrow = 1; screen.Add(panel);
            if (run.Failed)
            {
                panel.Add(PrototypeStyle.Text("本次讨伐结束", 28, PrototypeStyle.Accent));
                panel.Add(PrototypeStyle.Text("可以检查队伍与背包，或重置当前试游后再次出发。", 16));
                return;
            }
            if (run.FloorCleared)
            {
                panel.Add(PrototypeStyle.Text("击败 Boss，本层通关", 28, PrototypeStyle.Accent));
                AdventureWidgets.Action(panel, "进入下一层试游", "route-next-floor", () => { run.NextFloor(); Render(); });
                return;
            }
            panel.Add(PrototypeStyle.Text($"第 {run.Stage} 关  ·  选择前进方向", 24, PrototypeStyle.Accent));
            panel.Add(PrototypeStyle.Text("每层 10 关，包含 3 处分叉。城镇会占用一次营地或商店的名额。", 14, PrototypeStyle.Muted));
            var row = PrototypeStyle.Row(); row.style.flexGrow = 1; row.style.alignItems = Align.Center; panel.Add(row);
            for (int i = 0; i < run.RouteOptions.Count; i++)
            {
                int index = i; var kind = run.RouteOptions[i];
                var card = Card(row, AdventureFlow.NodeName(kind), "选择此路", "route-choice-" + i, assets.NodeIcon(kind), () =>
                { if (run.EnterRoute(index)) PresentVisit(); });
                card.style.width = 260; card.style.height = 200;
            }
        }

        private void RenderParty()
        {
            var row = PrototypeStyle.Row("adventure-party"); row.style.marginTop = 9;
            var content = assets.Encounter;
            foreach (var member in run.Party.Members.OrderBy(m => m.Position ?? 9))
            {
                var panel = PrototypeStyle.Panel(); panel.style.flexGrow = 1; panel.style.flexBasis = 0; panel.style.marginRight = 8;
                var label = PrototypeStyle.Text(member.Character.Name + (member.IsAway ? " · 守候宝箱" : ""), 15, PrototypeStyle.Accent);
                panel.Add(label);
                var away = run.Away.FirstOrDefault(a => a.MemberId == member.Id);
                panel.Add(PrototypeStyle.Text(member.IsAway ? $"再推进 {Math.Max(0, away.ReturnAt - run.CompletedNodes)} 关后归队" :
                    $"生命 {member.Health}／{member.EffectiveStats.Health}", 13, PrototypeStyle.Muted));
                row.Add(panel);
            }
            screen.Add(row);
        }

        private void PresentVisit()
        {
            if (run.Current == null) return;
            var current = run.Current;
            if (current.Kind == NodeKind.Event || current.Battle?.Settled == true)
            {
                screen.SetEnabled(false);
                eventWindow = new AdventureEventWindow(run, assets, Render, topBar.Refresh);
                body.Add(eventWindow);
            }
            else AdventureFlow.OpenNodeScene(current.Kind switch
            { NodeKind.Town => "Town", NodeKind.Camp => "Camp", NodeKind.Shop => "DockShop", _ => "BattlePrototype" });
        }

        private void OpenEditor()
        {
            bool changed = false;
            RunTopBar.EditParty(root, run.Party, assets.Encounter, () =>
            {
                if (!changed) return;
                topBar.Refresh();
                screen.Q("adventure-party")?.RemoveFromHierarchy();
                RenderParty();
                eventWindow?.RefreshAfterPartyEdit();
            }, () => changed = true);
        }

        private static Button Card(VisualElement parent, string title, string description, string name, Texture2D art, Action action)
        {
            var button = PrototypeStyle.Button("", name, action);
            button.style.width = Length.Percent(18.7f); button.style.minWidth = 150;
            button.style.height = 136; button.style.marginLeft = button.style.marginRight = 5;
            button.style.marginBottom = 9; button.style.paddingLeft = button.style.paddingRight = 7;
            var image = new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.height = 76; button.Add(image);
            var heading = PrototypeStyle.Text(title, 17, PrototypeStyle.Accent); PrototypeStyle.Middle(heading); button.Add(heading);
            var caption = PrototypeStyle.Text(description, 11, PrototypeStyle.Muted); PrototypeStyle.Middle(caption); button.Add(caption);
            parent.Add(button); return button;
        }

        private static string Gate(AdventureEventKind kind) => kind switch
        {
            AdventureEventKind.Investor => "首层第 3 关之后 · 每层一次",
            AdventureEventKind.Ambush => "全局事件",
            AdventureEventKind.LostTraveler or AdventureEventKind.FoundCoins or AdventureEventKind.HatTrick => "第 1～3 关事件",
            AdventureEventKind.Forge or AdventureEventKind.LockedChest => "第 4～6 关事件", _ => "第 7～9 关事件"
        };
    }
}
