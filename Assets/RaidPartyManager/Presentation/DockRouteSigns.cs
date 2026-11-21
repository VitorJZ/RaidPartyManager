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
        internal DockRouteSigns(AdventureRun run, AdventureContentAsset art, Texture2D timber,
            Func<float, Vector2> anchor, Func<int,bool> enter, Action nextFloor, bool following = false)
        {
            GameAudio.Play(SoundCue.UiMapReveal, .40f);
            name = "dock-route-signs";
            pickingMode = PickingMode.Ignore;
            if (following) PrototypeStyle.Fill(this);
            else style.flexGrow = 1;
            bool service = following && run.Current.Kind is (NodeKind.Camp or NodeKind.Town);
            float width = service ? 58 : 82;
            float height = service ? 34 : 47;
            style.overflow = Overflow.Hidden;
            var boards = new List<(HangingSign sign, float x)>();
            bool committed = false;
            if (!following && run.InGraveyard && run.Stage == 1 && !run.Failed)
            {
                var chapter = PrototypeStyle.Text($"第 {run.Floor} 层 · {run.RegionName}", 22, PrototypeStyle.Accent);
                chapter.name = "route-region";
                chapter.pickingMode = PickingMode.Ignore;
                chapter.style.position = Position.Absolute;
                chapter.style.alignSelf = Align.Center;
                chapter.style.bottom = Length.Percent(16);
                chapter.style.backgroundColor = new Color(.10f, .08f, .13f, .87f);
                chapter.style.paddingLeft = chapter.style.paddingRight = 14;
                Add(chapter);
            }
            if (!following && (run.Failed || run.CompletedNodes == 0 && !run.FloorCleared))
            {
                var hint = PrototypeStyle.Text(run.Failed ? "本次讨伐结束" : "向下拉动路牌，选择去向", 15);
                hint.name = "route-pull-hint";
                hint.style.position = Position.Absolute;
                hint.style.bottom = Length.Percent(16);
                hint.style.alignSelf = Align.Center;
                hint.style.backgroundColor = new Color(.09f,.075f,.06f,.86f);
                hint.style.paddingLeft = hint.style.paddingRight = 10;
                hint.pickingMode = PickingMode.Ignore;
                Add(hint);
                if (run.Failed) return;
            }
            if (run.FloorCleared)
                AddBoard("route-next-floor", art.RouteSignpost, 0, () => { nextFloor(); return true; }, null);
            else
            {
                var options = following ? run.FollowingOptions : run.RouteOptions;
                for (int i = 0; i < options.Count; i++)
                {
                    if (run.SuspendedChoice.HasValue && run.SuspendedChoice != i) continue;
                    int index = i;
                    var stop = following ? run.FollowingChoice(i) : run.RouteChoice(i);
                    float x = run.SuspendedChoice.HasValue || options.Count == 1 ? 0 : (i == 0 ? -1.68f : 1.68f);
                    AddBoard(run.SuspendedChoice.HasValue ? "route-node-current" : "route-choice-" + i,
                        art.NodeIcon(stop.Kind.Value), x, () => enter(index), stop);
                }
            }

            void AddBoard(string id, Texture image, float x, Func<bool> choose, RouteStop? stop)
            {
                var sign = new HangingSign(id, "", () => { if (!committed) committed = choose(); }, timber) { userData = stop };
                sign.style.position = Position.Absolute;
                sign.style.left = Length.Percent(x == 0 ? 50 : x < 0 ? 35 : 65);
                sign.style.top = -110;
                sign.style.width = width; sign.style.height = height;
                var icon = new Image { name = "route-node-icon", image = image, pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit };
                icon.style.width = icon.style.height = height * .79f;
                sign.Add(icon); boards.Add((sign, x)); Add(sign);
            }

            float age = 0;
            double last = Time.realtimeSinceStartupAsDouble;
            void Pose()
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (!DockPlayhouse.Moving) age += Mathf.Min(.05f, (float)(now-last));
                last = now;
                if (panel == null || !(layout.height > 0)) return;
                var frame = panel.visualTree.worldBound;
                // The falling board and its bridle enter through the same opening as the scenery.
                float rail = DockPlayhouse.StageOpeningTop(panel);
                float inset = Mathf.Max(0, rail - parent.worldBound.yMin);
                if (Mathf.Abs(style.marginTop.value.value - inset) > .05f) style.marginTop = inset;
                float restingTop = service ? 12 : parent.contentRect.height * (following ? .22f : .32f) - inset;
                float t = Mathf.Clamp01(age / .65f);
                float drop = Mathf.SmoothStep(0, 1, t) + Mathf.Sin(t*Mathf.PI)*.10f;
                foreach (var (sign, x) in boards)
                {
                    if (sign.MountedInScenery) continue;
                    float localX;
                    if (service) localX = layout.width * .90f - width * .5f - (x < 0 ? width + 12 : 0);
                    else if (following) localX = layout.width * (.5f + .12f * System.Math.Sign(x));
                    else localX = this.WorldToLocal(new Vector2(frame.xMin + anchor(x).x * frame.width, 0)).x;
                    sign.style.left = localX - width * .5f;
                    sign.style.top = Mathf.Lerp(-110, restingTop, drop);
                }
            }
            RegisterCallback<GeometryChangedEvent>(_ => Pose());
            schedule.Execute(Pose).Every(16);
        }
    }
}
