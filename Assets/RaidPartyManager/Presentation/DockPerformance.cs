using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Presentation
{
    internal sealed class DockPerformance
    {
        private enum Cue { Live, WalkOff, HoldForSigns, Picking, Fold, LoadSet, Turn, Raise, WalkOn }
        private Cue cue;
        private readonly DockPlayhouse venue;
        private DockSet live;
        private DockSetSnapshot oldSet, newSet, openingCast;
        private float entranceTime;
        private DockRollingFloor boards;
        private GameObject machine;
        private Material panorama;
        private float time;
        private bool toSigns;
        private Action move;
        private VisualElement curtainGuard;
        private readonly MaterialPropertyBlock ink = new();

        internal DockPerformance(DockPlayhouse house) => venue = house;
        internal bool FirstEntrance => openingCast != null;
        internal bool Busy => FirstEntrance || cue != Cue.Live && cue != Cue.Picking;
        internal bool Crossing => cue == Cue.HoldForSigns || cue == Cue.LoadSet;
        internal bool HoldsScenery => oldSet != null;

        internal void Bind(DockSet set) => live = set;

        internal void PrepareFirstEntrance()
        {
            if (openingCast != null || live == null || live.NativeEntrance || live.Cast.Count == 0) return;
            openingCast = new DockSetSnapshot(live, venue.DeckBounds, castOnly: true);
            openingCast.HideCast();
            foreach (var actor in live.Cast.Keys) if (actor != null) actor.forceRenderingOff = true;
            entranceTime = 0;
        }

        internal void EndFirstEntrance()
        {
            if (openingCast == null) return;
            foreach (var actor in live.Cast.Keys) if (actor != null) actor.forceRenderingOff = false;
            openingCast.Dispose();
            openingCast = null;
        }

        internal void Unbind()
        {
            EndFirstEntrance();
            live = null;
            curtainGuard?.RemoveFromHierarchy();
            curtainGuard = null;
            if (!Crossing) Clear();
        }

        internal void Change(Action callback, bool signs)
        {
            if (Busy || live == null) return;
            move = callback;
            toSigns = signs;
            if (cue == Cue.Picking)
            {
                SetCue(Cue.Fold);
                return;
            }
            oldSet = new DockSetSnapshot(live, venue.DeckBounds);
            SetLive(false);
            SetCue(Cue.WalkOff);
        }

        private void SetCue(Cue next) { cue = next; time = 0; }

        private void SetLive(bool show)
        {
            foreach (var renderer in live.Root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.forceRenderingOff = !show;
        }

        private void Mask(VisualElement panel, bool hide)
        {
            if (hide)
            {
                panel.style.opacity = 0;
                if (curtainGuard == null)
                {
                    curtainGuard = new VisualElement { name = "scene-change-input", focusable = true };
                    PrototypeStyle.Fill(curtainGuard);
                    curtainGuard.RegisterCallback<NavigationSubmitEvent>(e => e.StopPropagation());
                    curtainGuard.RegisterCallback<KeyDownEvent>(e => e.StopPropagation());
                }
                if (curtainGuard.parent != panel) { panel.Add(curtainGuard); curtainGuard.Focus(); }
                else if (panel[panel.childCount-1] != curtainGuard) curtainGuard.BringToFront();
            }
            else
            {
                panel.style.opacity = StyleKeyword.Null;
                curtainGuard?.RemoveFromHierarchy();
                curtainGuard = null;
            }
        }

        internal void Tick(VisualElement panel)
        {
            if (live == null || panel == null) return;
            if (openingCast != null)
            {
                entranceTime += Mathf.Min(Time.unscaledDeltaTime,.08f);
                openingCast.CastAt(entranceTime/.78f, true, entranceTime);
                if (entranceTime >= .8f) EndFirstEntrance();
            }
            if (cue == Cue.Live || cue == Cue.Picking) return;
            Mask(panel, true);
            time += Mathf.Min(Time.unscaledDeltaTime, .08f);
            switch (cue)
            {
                case Cue.WalkOff:
                    oldSet.CastAt(1 - time/.77f, false, time);
                    float haul = Mathf.SmoothStep(0, 1, time/.68f);
                    oldSet.Fly(haul);
                    venue.HoistMoon(haul);
                    if (time < .79f) break;
                    oldSet.HideCast();
                    if (toSigns) Load(Cue.HoldForSigns);
                    else SetCue(Cue.Fold);
                    break;
                case Cue.HoldForSigns:
                    // The outgoing document still owns the camera while LoadSceneAsync is pending.
                    if (move != null || !arrived) break;
                    SetLive(false);
                    venue.SetMoonVisible(oldSet.Moon);
                    Mask(panel, false);
                    SetCue(Cue.Picking);
                    break;
                case Cue.Fold:
                    oldSet.Fold(Mathf.SmoothStep(0, 1, time/.42f));
                    if (time >= .42f) Load(Cue.LoadSet);
                    break;
                case Cue.LoadSet:
                    if (!arrived || live.Floor == null || live.Sky == null) break;
                    newSet = new DockSetSnapshot(live, venue.DeckBounds);
                    SetLive(false);
                    newSet.HideCast(); newSet.Fly(1); newSet.Fold(1); newSet.ShowPieces(false);
                    SetUpTurn();
                    SetCue(Cue.Turn);
                    break;
                case Cue.Turn:
                    float t = Mathf.SmoothStep(0, 1, time/1.4f);
                    boards.Turn(180 * t);
                    panorama.SetFloat("_Roll", t);
                    venue.SetMoonVisible(t >= .5f ? newSet.Moon : oldSet.Moon);
                    if (t < 1) break;
                    FreeMachine();
                    oldSet.Dispose(); oldSet = null;
                    newSet.Floor.enabled = newSet.Sky.enabled = true;
                    SetCue(Cue.Raise);
                    break;
                case Cue.Raise:
                    float rise = Mathf.SmoothStep(0, 1, time/.54f);
                    newSet.Fold(1-rise);
                    newSet.Fly(1-rise);
                    venue.HoistMoon(1-rise);
                    if (rise < 1) break;
                    if (live.NativeEntrance) Finish(panel);
                    else SetCue(Cue.WalkOn);
                    break;
                case Cue.WalkOn:
                    newSet.CastAt(time/.85f, true, time);
                    if (time >= .89f) Finish(panel);
                    break;
            }
        }

        private bool arrived;
        internal void StageBound() { if (Crossing) arrived = true; }

        private void Load(Cue next)
        {
            var callback = move;
            move = null; arrived = false;
            SetCue(next);
            callback();
        }

        private void SetUpTurn()
        {
            machine = new GameObject("Dock scene machinery");
            Object.DontDestroyOnLoad(machine);
            boards = new DockRollingFloor(venue.DeckFrame, machine.transform, oldSet, newSet);
            oldSet.Sky.enabled = newSet.Sky.enabled = false;
            var drum = new GameObject("Travelling backcloth", typeof(MeshFilter), typeof(MeshRenderer));
            drum.transform.SetParent(machine.transform);
            drum.transform.SetPositionAndRotation(newSet.Sky.transform.position, newSet.Sky.transform.rotation);
            drum.transform.localScale = newSet.Sky.transform.lossyScale;
            drum.GetComponent<MeshFilter>().sharedMesh = venue.PanoramaPattern;
            panorama = new Material(Resources.Load<Shader>("RollHarbourCloth"));
            void Print(string key, MeshRenderer source)
            {
                source.GetPropertyBlock(ink);
                panorama.SetTexture(key, ink.GetTexture("_MainTex"));
                panorama.SetVector(key + "_ST", ink.GetVector("_MainTex_ST"));
            }
            Print("_MainTex", oldSet.Sky); Print("_NextTex", newSet.Sky);
            drum.GetComponent<MeshRenderer>().sharedMaterial = panorama;
            venue.SetMoonVisible(oldSet.Moon);
        }

        private void Finish(VisualElement panel)
        {
            SetLive(true);
            newSet.Dispose(); newSet = null;
            Mask(panel, false);
            SetCue(Cue.Live);
        }

        private void FreeMachine()
        {
            boards?.Dispose(); boards = null;
            if (machine != null) { machine.SetActive(false); Object.Destroy(machine); }
            if (panorama != null) Object.Destroy(panorama);
        }

        private void Clear()
        {
            FreeMachine();
            oldSet?.Dispose(); newSet?.Dispose();
            oldSet = newSet = null;
            move = null;
            SetCue(Cue.Live);
        }
    }
}
