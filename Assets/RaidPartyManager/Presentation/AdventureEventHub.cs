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
        [SerializeField] private ServiceStageArt stageArt;
        [SerializeField] private BattleStageArt encounterStageArt;
        [SerializeField] private Texture2D encounterArrow;
        private BattlePrototype eventBattle;
        private DockEventStage stage;
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
            ShowRun();
        }

        private void ShowRun()
        {
            if (run?.HasBattleRewardScreen == true && run.Current.Event == null)
                root.schedule.Execute(() => AdventureFlow.OpenNodeScene("BattlePrototype"));
            else
                Render();
        }

        private void Update() => stage?.Tick();

        private void OnDisable()
        {
            stage?.Dispose();
            stage = null;
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            if (run == null || run.IsInspection && run.Current == null)
            {
                stage?.Dispose();
                stage = null;
            }
            root.Clear();
            eventWindow = null;
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.backgroundImage = new StyleBackground(assets.Backdrop);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.unityBackgroundImageTintColor = new Color(0.25f, 0.28f, 0.32f, 1);
            root.style.paddingLeft = root.style.paddingRight = 22;
            root.style.paddingTop = 0;
            root.style.paddingBottom = 14;
            if (run == null)
            {
                var content = assets.Encounter;
                var candidates = AdventureFlow.DrawStarters(content, new System.Random());
                root.Add(new StarterDraftView(content, candidates, selected =>
                {
                    run ??= AdventureFlow.StartRoute(content, selected);
                    Render();
                }));
                return;
            }

            topBar = new RunTopBar(root, run.Party, assets.Encounter, OpenEditor, "event-party-editor",
                goldName: "adventure-gold", resetAdventure: run.IsInspection ? null : ResetAdventure);
            root.Add(topBar);
            body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            screen = new VisualElement { name = "adventure-screen" };
            screen.style.flexGrow = 1;
            screen.style.minHeight = 0;
            body.Add(screen);
            root.Add(body);
            if (run.IsInspection)
                RenderInspection();
            else
                RenderRoute();
            if (run.IsInspection) RenderParty();
            if (run.Current != null) PresentVisit();
        }

        private void Switch(bool inspection)
        { run = AdventureFlow.Select(assets, inspection); ShowRun(); }

        private void ResetAdventure()
        {
            run = AdventureFlow.Select(assets, run.IsInspection, true);
            Render();
        }

        private void RenderInspection()
        {
            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            header.style.marginTop = 8;
            var title = PrototypeStyle.Text("弥雾码头", 27, PrototypeStyle.Accent);
            title.style.marginLeft = 14;
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(PrototypeStyle.Text($"第 {run.Floor} 层  ·  已完成 {run.CompletedNodes} 个关卡", 14));
            screen.Add(header);

            var toolbar = PrototypeStyle.Row();
            toolbar.style.marginTop = 14;
            toolbar.style.alignItems = Align.Center;
            toolbar.Add(PrototypeStyle.Button("事件检查", "event-tab-inspect", () => Switch(true), true));
            toolbar.Add(PrototypeStyle.Button("讨伐路线", "event-tab-route", () => Switch(false)));
            toolbar.Add(PrototypeStyle.Button("重置当前试游", "reset-adventure", ResetAdventure));
            toolbar.Add(PrototypeStyle.Button("推进 1 个关卡", "inspection-advance", () => { run.AdvanceInspection(); Render(); }));
            toolbar.Add(PrototypeStyle.Button("下一层", "inspection-next-floor", () => { run.NextFloor(); Render(); }));
            screen.Add(toolbar);
            if (!string.IsNullOrEmpty(run.ReturnMessage))
                screen.Add(PrototypeStyle.Text(run.ReturnMessage, 17, PrototypeStyle.Accent));

            var hint = PrototypeStyle.Text("任选一幕试演。问号事件结束后自动返回列表，下次试演从头开始。", 13, PrototypeStyle.Muted);
            hint.style.marginTop = hint.style.marginBottom = 10; screen.Add(hint);
            var scroll = new ScrollView { name = "event-catalog" }; scroll.style.flexGrow = 1; scroll.style.minHeight = 0;
            var grid = PrototypeStyle.Row(); grid.style.flexWrap = Wrap.Wrap; scroll.Add(grid); screen.Add(scroll);
            foreach (AdventureEventKind kind in Enum.GetValues(typeof(AdventureEventKind)))
            {
                var captured = kind;
                Card(grid, AdventureFlow.EventName(kind), Gate(kind), "inspect-event-" + kind, DockEventStage.EventProp(kind), () =>
                { if (run.Inspect(NodeKind.Event, captured)) PresentVisit(); }, kind == AdventureEventKind.FoundCoins);
            }
            foreach (var kind in new[] { NodeKind.Town, NodeKind.Camp, NodeKind.Shop, NodeKind.NormalBattle, NodeKind.EliteBattle, NodeKind.Boss, NodeKind.TreasureChest })
            {
                var captured = kind;
                Card(grid, AdventureFlow.NodeName(kind), kind == NodeKind.Town ? "四项服务，选择两项" :
                    kind == NodeKind.TreasureChest ? "开箱并挑选一件物品" :
                    kind == NodeKind.Camp ? "招募、升阶或休息，三选一" : kind == NodeKind.Shop ? "拖拽购买，共用金币与背包" : "进入真实自动战斗",
                    "inspect-node-" + kind, assets.NodeIcon(kind), () => { if (run.Inspect(captured)) PresentVisit(); });
            }
        }

        private void RenderRoute()
        {
            if (run.Current != null) return;
            stage ??= new DockEventStage(transform, root, stageArt, false);
            if (!DockPlayhouse.HoldingSet && run.CompletedNodes == 0)
                stage.ShowCompany(run.Party, assets.Encounter);
            root.style.backgroundImage = StyleKeyword.None;
            root.style.backgroundColor = Color.clear;
            screen.Add(new StageSignMask(new DockRouteSigns(run, assets, stageArt.timber, stage.Anchor, EnterRoute, () =>
            {
                if (run.NextFloor()) Render();
            })));
        }

        private bool EnterRoute(int index)
        {
            if (!run.EnterRoute(index))
            {
                if (!run.Party.CanStartBattle && screen.Q("route-entry-message") == null)
                {
                    var warning = PrototypeStyle.Text("请先在队伍编辑中为在场队员安排站位。", 16, PrototypeStyle.Accent);
                    warning.name = "route-entry-message";
                    screen.Add(warning);
                }
                return false;
            }
            var kind = run.Current.Kind;
            AdventureFlow.OpenNodeScene(AdventureFlow.SceneFor(kind));
            return true;
        }

        private void RenderParty()
        {
            var footer = new VisualElement { name = "adventure-party" };
            footer.style.marginTop = 9;
            if (!run.IsInspection && !string.IsNullOrEmpty(run.ReturnMessage))
            {
                var notice = PrototypeStyle.Text(run.ReturnMessage, 14, PrototypeStyle.Accent);
                notice.style.marginBottom = 4;
                footer.Add(notice);
            }
            var row = PrototypeStyle.Row();
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
            footer.Add(row);
            screen.Add(footer);
        }

        private void PresentVisit()
        {
            if (run.Current == null) return;
            var current = run.Current;
            if (current.Kind is NodeKind.Event or NodeKind.TreasureChest || current.Battle?.Settled == true)
            {
                stage ??= new DockEventStage(transform, root, stageArt, current.Kind == NodeKind.TreasureChest, current.Event);
                root.style.backgroundImage = StyleKeyword.None;
                root.style.backgroundColor = Color.clear;
                screen.style.display = DisplayStyle.None;
                screen.SetEnabled(false);
                stage.EventResident(current, assets);
                if (run.HasBattleRewardScreen && current.Event.HasValue)
                {
                    BeginEventBattle();
                    return;
                }
                eventWindow = new AdventureEventWindow(run, assets, stageArt.timber, stage, () =>
                {
                    if (run.IsInspection)
                    {
                        if (current.Event.HasValue) run = AdventureFlow.Select(assets, true, true);
                        Render();
                    }
                    else PrototypeSceneMenu.OpenScene("EventSelect");
                }, topBar, () => stage.ShowCompany(run.Party, assets.Encounter), () => StartCoroutine(ExitTeller()));
                body.Add(eventWindow);
            }
            else AdventureFlow.OpenNodeScene(current.Kind switch
            { NodeKind.Town => "Town", NodeKind.Camp => "Camp", NodeKind.Shop => "DockShop", _ => "BattlePrototype" });
        }

        private System.Collections.IEnumerator ExitTeller()
        {
            eventWindow.style.display = DisplayStyle.None;
            root.SetEnabled(false);
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime/.72f)
            {
                stage.TellerExit(t);
                yield return null;
            }
            root.SetEnabled(true);
            BeginEventBattle();
        }

        private void BeginEventBattle()
        {
            stage.TellerExit(1);
            stage.HideCompany();
            root.Clear();
            root.style.backgroundImage = StyleKeyword.None;
            eventBattle = gameObject.AddComponent<BattlePrototype>();
            eventBattle.UseEventStage(stage,assets.EncounterAsset,encounterStageArt,assets.EventParchment,encounterArrow,FinishEventBattle);
        }

        private void FinishEventBattle()
        {
            eventBattle.enabled = false;
            Destroy(eventBattle);
            eventBattle = null;
            if (localFontAsset != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(localFontAsset);
            if (!run.Current.Resolved) stage.TellerExit(0);
            Render();
            if (run.Current?.Resolved == true && eventWindow != null)
                eventWindow.Q<TellerSpeech>()?.RemoveFromHierarchy();
        }

        private void OpenEditor()
        {
            bool changed = false;
            topBar.EditParty(() =>
            {
                if (!changed) return;
                screen.Q("adventure-party")?.RemoveFromHierarchy();
                if (run.IsInspection) RenderParty();
                if (run.Current != null || !DockPlayhouse.HoldingSet)
                    stage?.ShowCompany(run.Party, assets.Encounter);
                eventWindow?.RefreshAfterPartyEdit();
            }, () => changed = true);
        }

        private static Button Card(VisualElement parent, string title, string description, string name, Texture2D art, Action action, bool strip = false)
        {
            var button = PrototypeStyle.Button("", name, action);
            button.style.width = Length.Percent(18.7f); button.style.minWidth = 150;
            button.style.height = 136; button.style.marginLeft = button.style.marginRight = 5;
            button.style.marginBottom = 9; button.style.paddingLeft = button.style.paddingRight = 7;
            var image = new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            if (strip) image.sourceRect = new Rect(0,0,art.width/4,art.height);
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
