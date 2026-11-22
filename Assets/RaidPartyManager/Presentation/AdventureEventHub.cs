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
        private IVisualElementScheduledItem sceneryPlayback;
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
                root.schedule.Execute(() => AdventureFlow.OpenNodeScene(AdventureFlow.SceneFor(run.Current.Kind)));
            else
                Render();
        }

        private void Update() => stage?.Tick();

        private void HideBackdrop()
        {
            sceneryPlayback?.Pause();
            sceneryPlayback = null;
            root.style.backgroundImage = StyleKeyword.None;
        }

        private void OnDisable()
        {
            HideBackdrop();
            stage?.Dispose();
            stage = null;
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            GameAudio.SetScene(gameObject.scene.name, run);
            if (run == null || run.IsInspection && run.Current == null)
            {
                stage?.Dispose();
                stage = null;
            }
            root.Clear();
            eventWindow = null;
            root.style.backgroundColor = PrototypeStyle.Background;
            HideBackdrop();
            if (run?.IsInspection == true && run.Current == null)
                sceneryPlayback = SceneryFlipbook.Animate(root, assets.Backdrop);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.unityBackgroundImageTintColor = new Color(0.25f, 0.28f, 0.32f, 1);
            root.style.paddingLeft = root.style.paddingRight = 22;
            root.style.paddingTop = 0;
            root.style.paddingBottom = 14;
            if (run == null)
            {
                var content = assets.Encounter;
                root.style.backgroundColor = Color.clear;
                stage = new DockEventStage(transform, root, stageArt, false);
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
            var title = PrototypeStyle.Text(run.RegionName, 27, PrototypeStyle.Accent);
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
            toolbar.Add(PrototypeStyle.Button("下一层", "inspection-next-floor", NextFloor));
            screen.Add(toolbar);
            if (!string.IsNullOrEmpty(run.ReturnMessage))
                screen.Add(PrototypeStyle.Text(run.ReturnMessage, 17, PrototypeStyle.Accent));

            var hint = PrototypeStyle.Text("任选一幕试演。墓园和遗都事件结算后可阅读结果，拉动出口牌返回。", 13, PrototypeStyle.Muted);
            hint.style.marginTop = hint.style.marginBottom = 10; screen.Add(hint);
            var scroll = new ScrollView { name = "event-catalog" }; scroll.style.flexGrow = 1; scroll.style.minHeight = 0;
            var grid = PrototypeStyle.Row(); grid.style.flexWrap = Wrap.Wrap; scroll.Add(grid); screen.Add(scroll);
            foreach (AdventureEventKind kind in Enum.GetValues(typeof(AdventureEventKind)))
            {
                var captured = kind;
                Card(grid, AdventureFlow.EventName(kind), Gate(kind), "inspect-event-" + kind, (GraveStories.Contains(kind) || CapitalStories.Contains(kind)) ? assets.EventArt(kind) : DockEventStage.EventProp(kind), () =>
                { if (run.Inspect(NodeKind.Event, captured)) PresentVisit(); }, kind == AdventureEventKind.FoundCoins);
            }
            foreach (var kind in new[] { NodeKind.Town, NodeKind.Camp, NodeKind.NormalBattle, NodeKind.EliteBattle, NodeKind.Boss, NodeKind.TreasureChest })
            {
                var captured = kind;
                Card(grid, AdventureFlow.NodeName(kind), kind == NodeKind.Town ? "人才市场、商店、委托可反复往返，营地可用一次" :
                    kind == NodeKind.TreasureChest ? "开箱并挑选一件物品" :
                    kind == NodeKind.Camp ? "升阶或休息，二选一" : "进入真实自动战斗",
                    "inspect-node-" + kind, assets.NodeIcon(kind), () => { if (run.Inspect(captured)) PresentVisit(); });
            }
        }

        private void RenderRoute()
        {
            if (run.Current != null) return;
            if (stage != null && (stage.Cemetery != run.InGraveyard || stage.Capital != run.InCapital))
            {
                stage.Dispose();
                stage = null;
            }
            stage ??= new DockEventStage(transform, root, stageArt, false, graveyard: run.InGraveyard, capital: run.InCapital, cityDistrict: Mathf.Clamp((run.Stage - 1) / 3, 0, 2));
            if (run.Stage == 1 && (run.InGraveyard || run.InCapital || !DockPlayhouse.HoldingSet))
                stage.ShowCompany(run.Party, assets.Encounter);
            HideBackdrop();
            root.style.backgroundColor = Color.clear;
            screen.Add(new StageSignMask(new DockRouteSigns(run, assets, stageArt.graveyardStone, EnterRoute, NextFloor)));
        }

        private void NextFloor()
        {
            if (!run.Party.HasBorrowedName) { AdvanceFloor(); return; }
            if (body.Q("grave-name-register") != null) return;
            var page = new VisualElement { name = "grave-name-register" };
            PrototypeStyle.Fill(page);
            page.style.backgroundColor = new Color(.08f, .07f, .09f, .95f);
            page.style.alignItems = Align.Center;
            page.style.justifyContent = Justify.Center;
            var prompt = PrototypeStyle.Text("出园簿上有三个姓名。哪一个属于你的队员？", 20, PrototypeStyle.Accent);
            page.Add(prompt);
            string real = run.Party.BorrowedRealName;
            var names = assets.Encounter.CharacterCatalog.Where(c => c.RosterNumber > 0).Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n) && n != real)
                .Distinct().Take(2).Append(real).OrderBy(n => n, StringComparer.Ordinal);
            foreach (string candidate in names)
                page.Add(PrototypeStyle.Button(candidate, "reclaim-" + candidate, () =>
                {
                    if (!run.Party.ReclaimName(candidate)) { prompt.text = "笔迹对不上。再仔细看一次。"; return; }
                    AdvanceFloor();
                }));
            body.Add(page);
        }

        private void AdvanceFloor()
        {
            if (!run.NextFloor()) return;
            if (run.IsInspection) Render();
            else DockPlayhouse.ChangeSet(() => PrototypeSceneMenu.OpenScene("EventSelect"), turn: StageTurn.Forward);
        }

        private bool EnterRoute(int index, StageTurn turn)
        {
            if (!run.EnterRoute(index))
            {
                GameAudio.Play(SoundCue.UiRestricted);
                if ((!run.Party.CanStartBattle || !run.Party.CanAdvanceDay) && screen.Q("route-entry-message") == null)
                {
                    var warning = PrototypeStyle.Text(!run.Party.CanAdvanceDay ? "没有队员能履约到明天。请在编队中右击队员续约。" : "请先在队伍编辑中为在场队员安排站位。", 16, PrototypeStyle.Accent);
                    warning.name = "route-entry-message";
                    screen.Add(warning);
                }
                return false;
            }
            GameAudio.Play(SoundCue.UiMapMarker);
            var kind = run.Current.Kind;
            AdventureFlow.OpenNodeScene(AdventureFlow.SceneFor(kind), turn: turn);
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
            GameAudio.SetScene(gameObject.scene.name, run);
            var current = run.Current;
            if (current.Kind is NodeKind.Event or NodeKind.TreasureChest || current.Battle?.Settled == true)
            {
                stage ??= new DockEventStage(transform, root, stageArt, current.Kind == NodeKind.TreasureChest, current.Event, run.InGraveyard && !current.Event.HasValue, run.InCapital, Mathf.Clamp((run.Stage - 1) / 3, 0, 2));
                HideBackdrop();
                root.style.backgroundColor = Color.clear;
                screen.style.display = DisplayStyle.None;
                screen.SetEnabled(false);
                stage.EventResident(run, assets);
                if (run.HasBattleRewardScreen && current.Event.HasValue)
                {
                    BeginEventBattle();
                    return;
                }
                eventWindow = new AdventureEventWindow(run, assets, stageArt.timber, stage, () =>
                {
                    if (run.IsInspection)
                    {
                        if (current.Event.HasValue && !GraveStories.Contains(current.Event) && !CapitalStories.Contains(current.Event)) run = AdventureFlow.Select(assets, true, true);
                        Render();
                    }
                    else PrototypeSceneMenu.OpenScene("EventSelect");
                }, topBar, () => stage.ShowEventCompany(run, assets.Encounter), () => StartCoroutine(ExitTeller()));
                body.Add(eventWindow);
            }
            else AdventureFlow.OpenNodeScene(AdventureFlow.SceneFor(current.Kind));
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
            HideBackdrop();
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
                    stage?.ShowEventCompany(run, assets.Encounter);
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
            if (strip) image.sourceRect = AnimationCels.RectFor(art, 0);
            image.style.height = 76; button.Add(image);
            var heading = PrototypeStyle.Text(title, 17, PrototypeStyle.Accent); PrototypeStyle.Middle(heading); button.Add(heading);
            var caption = PrototypeStyle.Text(description, 11, PrototypeStyle.Muted); PrototypeStyle.Middle(caption); button.Add(caption);
            parent.Add(button); return button;
        }

        private static string Gate(AdventureEventKind kind) => CapitalStories.Contains(kind) ? "机巧遗都 · 随机事件" : GraveStories.Contains(kind) ? "低语墓园 · 随机事件" : kind switch
        {
            AdventureEventKind.Investor => "首层第 3 关之后 · 每层一次",
            AdventureEventKind.Ambush => "全局事件",
            AdventureEventKind.LostTraveler or AdventureEventKind.FoundCoins or AdventureEventKind.HatTrick => "第 1～3 关事件",
            AdventureEventKind.Forge or AdventureEventKind.LockedChest => "第 4～6 关事件", _ => "第 7～9 关事件"
        };
    }
}
