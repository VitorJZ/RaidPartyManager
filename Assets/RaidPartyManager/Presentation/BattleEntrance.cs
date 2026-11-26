using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleEntrance
    {
        private const double TravelTime = 0.66;
        private const double Stagger = 0.10;
        private const float EntranceStrides = 1.5f;

        private sealed class Arrival
        {
            public readonly Combatant Unit;
            public readonly VisualElement Figure;
            public readonly double Start;
            public readonly Texture2D WalkSheet;
            public readonly BossEntranceCue Cue;
            public readonly Texture2D EntranceSheet;
            public readonly StageActorImage Prop, CoffinRim;
            public double Duration => Cue != null ? Cue.Seconds : TravelTime;
            public readonly bool UsesPortrait;
            public int LastStep = -1;
            public int DisplayedFrame = -1;

            public Arrival(Combatant unit, EncounterData content, double start, BattleStage stage)
            {
                Unit = unit;
                Start = start;
                if (unit.Side == Side.Enemy && unit.Character.Rank == EnemyRank.Boss)
                    Cue = BossEntranceCue.For(unit.Character.Id);
                if (Cue != null)
                {
                    EntranceSheet = Resources.Load<Texture2D>("BossEntrances/" + unit.Character.Id);
                    Prop = LoadProp(unit.Character.Id, Cue.PropOrder);
                    if (Cue.HasCoffinRim)
                        CoffinRim = LoadProp(unit.Character.Id + "-Rim", 6);
                }
                WalkSheet = content.BattleWalkFor(unit.Character.Id);
                var art = content.BattleSpriteFor(unit.Character.Id);
                UsesPortrait = content.BattlePortraitFor(unit.Character.Id) != null;
                if (EntranceSheet == null && WalkSheet == null && art != null)
                    Figure = new EntranceWalkPose(art, unit.Character.Profession, unit.Side == Side.Enemy, stage);
                else
                    Figure = new StageActorImage(stage, true)
                    {
                        image = EntranceSheet ?? WalkSheet ?? content.PortraitFor(unit.Character.Id),
                        scaleMode = EntranceSheet != null || WalkSheet != null ? ScaleMode.StretchToFill : ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                Figure.name = "entrance-" + unit.Id;
                Figure.style.position = Position.Absolute;

                StageActorImage LoadProp(string resource, int order)
                {
                    var sheet = Resources.Load<Texture2D>("BossEntranceProps/" + resource);
                    if (sheet == null) return null;
                    var prop = new StageActorImage(stage, sceneryOrder: order)
                    {
                        name = (resource.EndsWith("-Rim") ? "entrance-rim-" : "entrance-prop-") + unit.Id,
                        image = sheet, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore
                    };
                    prop.style.position = Position.Absolute;
                    // Extended scenery has its suspension chain above the painted key.
                    prop.style.transformOrigin = new TransformOrigin(Length.Percent(50),
                        Length.Percent(100 - 50f * sheet.width / sheet.height));
                    return prop;
                }
            }
        }

        private readonly List<Arrival> arrivals = new List<Arrival>();
        private BattleStage stage;
        private bool fadedHud;
        private double clock;
        private double? previousFrame;
        public float HudOpacity { get; private set; } = 1;
        public bool Active => arrivals.Count > 0;
        public bool EnemyEntrancePending => arrivals.Any(arrival => arrival.Unit.Side == Side.Enemy);

        public void Add(IEnumerable<Combatant> team, EncounterData content, BattleStage stage)
        {
            this.stage = stage;
            var lineup = team.Where(unit => unit.Health > 0).OrderBy(unit => unit.Position).ToArray();
            for (int i = 0; i < lineup.Length; i++)
                arrivals.Add(new Arrival(lineup[i], content, clock + i * Stagger, stage));
        }

        public void Show(double now, VisualElement layer, IReadOnlyDictionary<string, BattleUnitCard> cards, int playbackSpeed)
        {
            if (layer == null)
                return;

            // Wait for layout and let the first loaded frame present a real entrance pose.
            bool ready = layer.layout.width > 0;
            if (ready && previousFrame.HasValue)
                clock += Math.Min(0.08 * playbackSpeed, now - previousFrame.Value);
            previousFrame = ready ? now : null;

            float focus = 0;
            VisualElement subject = null, focusProp = null;
            BossEntranceCue.Shot shot = default;
            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                var arrival = arrivals[i];
                cards.TryGetValue(arrival.Unit.Id, out var card);
                double elapsed = clock - arrival.Start;
                if (elapsed >= arrival.Duration)
                {
                    arrival.Figure.RemoveFromHierarchy();
                    arrival.Prop?.RemoveFromHierarchy();
                    arrival.CoffinRim?.RemoveFromHierarchy();
                    card?.SetEntranceVisible(true);
                    arrivals.RemoveAt(i);
                    continue;
                }

                if (card == null)
                    continue;
                card.SetEntranceVisible(false);
                if (elapsed < 0 || !ready)
                    continue;

                var body = layer.WorldToLocal(card.BodyBounds);
                if (!(body.width > 0 && body.height > 0))
                    continue;

                if (arrival.Figure.parent != layer)
                    layer.Add(arrival.Figure);

                float t = Mathf.Clamp01((float)(elapsed / arrival.Duration));
                if (arrival.Figure is StageActorImage rider) rider.RideHeight = RidingWave.Envelope(t);
                var cue = arrival.Cue;
                if (cue != null)
                {
                    var camera = cue.CameraAt(t);
                    if (camera.Weight > focus)
                    {
                        focus = camera.Weight;
                        subject = arrival.Figure;
                        focusProp = arrival.Prop;
                        shot = camera;
                    }
                    if (arrival.LastStep < 0 && t >= cue.LeadBeat)
                    {
                        GameAudio.Play(cue.LeadSound, .39f);
                        arrival.LastStep = 0;
                    }
                    if (arrival.LastStep < 1 && t >= cue.Beat)
                    {
                        GameAudio.Play(cue.Sound, .52f);
                        arrival.LastStep = 1;
                    }

                    Place(arrival.Figure, body, cue.Actor(t), body.width, arrival.EntranceSheet ?? arrival.WalkSheet, t);
                    if (arrival.Figure is EntranceWalkPose fallback)
                        fallback.ShowFrame(AnimationCels.At(t));
                    if (arrival.Prop != null)
                    {
                        if (arrival.Prop.parent != layer)
                        {
                            int index = layer.IndexOf(arrival.Figure);
                            layer.Insert(cue.PropOrder < 0 ? index : index + 1, arrival.Prop);
                        }
                        var propPose = cue.Prop(t);
                        float propTime = cue.PropProgress(t);
                        Place(arrival.Prop, body, propPose, body.height, arrival.Prop.image, propTime);
                        if (arrival.CoffinRim != null)
                        {
                            if (arrival.CoffinRim.parent != layer)
                                layer.Insert(layer.IndexOf(arrival.Figure) + 1, arrival.CoffinRim);
                            Place(arrival.CoffinRim, body, propPose, body.height, arrival.CoffinRim.image, propTime);
                        }
                    }
                    continue;
                }

                int step = (int)(elapsed / .29);
                if (step != arrival.LastStep)
                {
                    GameAudio.Play(BattleSounds.Step(arrival.Unit.Character), .30f);
                    arrival.LastStep = step;
                }
                // Strides follow the eased travel, ending on a contact pose as the walker reaches the mark.
                float progress = Mathf.SmoothStep(0, 1, t);
                float stride = Mathf.Repeat(progress * EntranceStrides, 1f);
                int frame = AnimationCels.At(stride);
                if (arrival.Figure is EntranceWalkPose pose)
                    pose.ShowFrame(frame);
                else if (arrival.Figure is Image image && arrival.WalkSheet != null &&
                    arrival.DisplayedFrame != frame)
                {
                    arrival.DisplayedFrame = frame;
                    image.sourceRect = AnimationCels.RectFor(arrival.WalkSheet, frame);
                    image.MarkDirtyRepaint();
                }

                float startX = arrival.Unit.Side == Side.Party ? -body.width : layer.layout.width;
                float lift = arrival.UsesPortrait ? Mathf.Abs(Mathf.Sin(progress * 3f * Mathf.PI) * 4f)
                    : arrival.Figure is EntranceWalkPose ? 0 : Mathf.Abs(Mathf.Sin(stride * 2f * Mathf.PI)) * 1.6f;
                arrival.Figure.style.left = Mathf.Lerp(startX, body.xMin, progress);
                arrival.Figure.style.top = body.yMin - lift;
                arrival.Figure.style.width = body.width;
                arrival.Figure.style.height = body.height;
            }

            HudOpacity = stage != null ? 1 - Mathf.Clamp01(focus * 2.5f) : 1;
            stage?.SetBossShot(subject, focusProp, shot);
            if (stage != null && (focus > 0 || fadedHud))
            {
                fadedHud = focus > 0;
                foreach (var card in cards.Values)
                    card.SetEntranceHudOpacity(HudOpacity);
            }
            if (arrivals.Count == 0)
                ResetClock();
        }

        private static void Place(VisualElement element, Rect mark, BossEntranceCue.Pose pose, float baseWidth,
            Texture sheet, float progress)
        {
            float width = baseWidth * pose.Scale;
            float height = mark.height * pose.Scale;
            if (element is StageActorImage { SceneryOrder: not null } && sheet != null)
                height = width * sheet.height / sheet.width;
            element.style.left = mark.center.x + pose.X * mark.height - width * .5f;
            element.style.top = mark.yMax + pose.Y * mark.height - height;
            element.style.width = width;
            element.style.height = height * (1 - pose.CropBottom);
            element.style.rotate = new Rotate(new Angle(pose.Angle));
            element.style.opacity = pose.Alpha;
            if (element is Image image && sheet != null)
            {
                var cel = AnimationCels.RectFor(sheet, AnimationCels.At(progress));
                cel.height *= 1 - pose.CropBottom;
                image.sourceRect = cel;
            }
        }

        public void Clear(IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            foreach (var arrival in arrivals)
            {
                arrival.Figure.RemoveFromHierarchy();
                arrival.Prop?.RemoveFromHierarchy();
                arrival.CoffinRim?.RemoveFromHierarchy();
                if (cards.TryGetValue(arrival.Unit.Id, out var card))
                    card.SetEntranceVisible(true);
            }
            arrivals.Clear();
            stage?.SetBossShot(null, null, default);
            if (fadedHud)
                foreach (var card in cards.Values)
                    card.SetEntranceHudOpacity(1);
            HudOpacity = 1;
            fadedHud = false;
            ResetClock();
        }

        private void ResetClock()
        {
            clock = 0;
            previousFrame = null;
        }
    }
}
