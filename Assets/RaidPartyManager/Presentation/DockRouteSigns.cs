using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class DockRouteSigns : VisualElement
    {
        private readonly List<(Button button, Texture icon, int direction)> boards = new();
        private readonly List<Transform> arrows = new();
        private readonly List<DockRoadpost> stones = new();
        private bool landed, committed;

        internal DockRouteSigns(AdventureRun run, AdventureContentAsset art, Texture2D stone,
            Func<int,StageTurn,bool> enter, Action nextFloor, bool following = false) : this(stone, following)
        {
            name = "dock-route-signs";
            if (!following && (run.InGraveyard || run.InCapital) && run.Stage == 1 && !run.Failed)
            {
                var chapter = PrototypeStyle.Text($"第 {run.Floor} 层 · {run.RegionName}" +
                    (run.InCapital ? "\n双方非魔法伤害减半，最低 1 点。" : ""), 22, PrototypeStyle.Accent);
                chapter.name = "route-region";
                chapter.pickingMode = PickingMode.Ignore;
                chapter.style.position = Position.Absolute;
                chapter.style.alignSelf = Align.Center;
                chapter.style.bottom = Length.Percent(16);
                chapter.style.backgroundColor = new Color(.10f, .08f, .13f, .87f);
                chapter.style.paddingLeft = chapter.style.paddingRight = 14;
                Add(chapter);
            }
            if (run.Failed)
            {
                var ended = PrototypeStyle.Text("本次讨伐结束", 15);
                ended.style.alignSelf = Align.Center;
                Add(ended);
                return;
            }
            if (run.FloorCleared)
                AddBoard("route-next-floor", art.RouteSignpost, 1, () => { nextFloor(); return true; }, null);
            else
            {
                var options = following ? run.FollowingOptions : run.RouteOptions;
                for (int i = 0; i < options.Count; i++)
                {
                    if (run.SuspendedChoice.HasValue && run.SuspendedChoice != i) continue;
                    int index = i;
                    var stop = following ? run.FollowingChoice(i) : run.RouteChoice(i);
                    int direction = options.Count > 1 && !run.SuspendedChoice.HasValue && i == 0 ? -1 : 1;
                    AddBoard(run.SuspendedChoice.HasValue ? "route-node-current" : "route-choice-" + i,
                        art.NodeIcon(stop.Kind.Value), direction, () => enter(index, direction < 0 ? StageTurn.Reverse : StageTurn.Forward), stop);
                }
            }
        }

        private void AddBoard(string id, Texture icon, int direction, Func<bool> choose, RouteStop? stop)
        {
            var button = new Button(() => { if (landed && !committed) committed = choose(); }) { name = id, userData = stop };
            button.SetEnabled(false);
            button.style.position = Position.Absolute;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
            button.style.opacity = 0;
            int index = boards.Count;
            void Highlight(bool on)
            {
                if (index < arrows.Count && !stones[index].Travelling) stones[index].Highlight(arrows[index], on);
            }
            button.RegisterCallback<PointerEnterEvent>(_ => Highlight(true));
            button.RegisterCallback<PointerLeaveEvent>(_ => Highlight(false));
            button.RegisterCallback<FocusInEvent>(_ => Highlight(true));
            button.RegisterCallback<FocusOutEvent>(_ => Highlight(false));
            boards.Add((button, icon, direction));
            Add(button);
        }

        private DockRouteSigns(Texture2D stone, bool fill)
        {
            GameAudio.Play(SoundCue.UiMapReveal, .40f);
            pickingMode = PickingMode.Ignore;
            if (fill) PrototypeStyle.Fill(this);
            else style.flexGrow = 1;
            const float fallSeconds = .50f;
            float dropStart = -1;
            DockSet set = null;

            void Pose()
            {
                var eye = Camera.main;
                var house = DockPlayhouse.Current;
                if (panel == null || boards.Count == 0 || eye == null || house == null || DockPlayhouse.Moving || committed) return;
                if (!(layout.width > 0)) return;
                if (dropStart < 0)
                {
                    set = house.Set;
                    foreach (var board in boards)
                    {
                        var stonePost = new DockRoadpost(board.direction, stone, set.Lighting);
                        stones.Add(stonePost);
                        set.Roadposts.Add(stonePost);
                        arrows.Add(stonePost.AddArrow(board.icon));
                    }
                    dropStart = Time.unscaledTime - (set.RoadstonesLanded ? fallSeconds : 0);
                }
                float t = Mathf.Clamp01((Time.unscaledTime - dropStart) / fallSeconds);
                float depth = house.DeckBounds.min.z + .27f - eye.transform.position.z;
                float loft = Mathf.Max(4, eye.ViewportToWorldPoint(new Vector3(.5f, 1.12f, depth)).y - house.DeckBounds.max.y);
                foreach (var stonePost in stones)
                    if (!stonePost.Travelling) stonePost.Place(eye, house.DeckBounds, loft * (1 - t*t));
                if (!landed && t >= 1)
                {
                    landed = true;
                    if (!set.RoadstonesLanded)
                    {
                        set.RoadstonesLanded = true;
                        house.LandRoadstone();
                        GameAudio.Play(SoundCue.StageStoneSignLand, .55f);
                    }
                    foreach (var board in boards) board.button.SetEnabled(true);
                }
                Rect frame = panel.visualTree.worldBound;
                for (int i = 0; i < boards.Count; i++)
                {
                    var r = DockRoadpost.BoundsInViewport(arrows[i], eye);
                    var corner = this.WorldToLocal(frame.min + new Vector2(r.xMin * frame.width, (1-r.yMax) * frame.height));
                    var button = boards[i].button;
                    button.style.left = corner.x;
                    button.style.top = corner.y;
                    button.style.width = r.width * frame.width;
                    button.style.height = r.height * frame.height;
                }
            }
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                foreach (var sign in stones)
                {
                    set.Roadposts.Remove(sign);
                    if (!sign.Travelling) sign.Dispose();
                }
                stones.Clear(); arrows.Clear(); dropStart = -1; landed = false;
            });
            RegisterCallback<GeometryChangedEvent>(_ => Pose());
            schedule.Execute(Pose).Every(16);
        }
    }
}
