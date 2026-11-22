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

        private sealed class Arrival
        {
            public readonly Combatant Unit;
            public readonly VisualElement Figure;
            public readonly double Start;
            public readonly Texture2D WalkSheet;
            public readonly BossEntranceCue Cue;
            public readonly Texture2D EntranceSheet;
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
                    EntranceSheet = Resources.Load<Texture2D>("BossEntrances/" + unit.Character.Id);
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
            VisualElement subject = null;
            float focusHeight = .45f;
            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                var arrival = arrivals[i];
                cards.TryGetValue(arrival.Unit.Id, out var card);
                double elapsed = clock - arrival.Start;
                if (elapsed >= arrival.Duration)
                {
                    arrival.Figure.RemoveFromHierarchy();
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
                var cue = arrival.Cue;
                if (cue == null)
                {
                    int step = (int)(elapsed / .29);
                    if (step != arrival.LastStep)
                    {
                        GameAudio.Play(BattleSounds.Step(arrival.Unit.Character), .30f);
                        arrival.LastStep = step;
                    }
                }
                else
                {
                    if (arrival.LastStep < 0 && t >= cue.Beat)
                    {
                        GameAudio.Play(cue.Sound, .52f);
                        arrival.LastStep = 0;
                    }
                    float weight = cue.ShotWeight(t);
                    if (weight > focus)
                    {
                        focus = weight;
                        subject = arrival.Figure;
                        focusHeight = cue.FocusHeight;
                    }
                }
                int frame = arrival.EntranceSheet != null ? AnimationCels.At(t) : AnimationCels.Loop(elapsed, .4);
                if (arrival.Figure is EntranceWalkPose pose)
                    pose.ShowFrame(frame);
                else if (arrival.Figure is Image image && (arrival.EntranceSheet != null || arrival.WalkSheet != null) &&
                    arrival.DisplayedFrame != frame)
                {
                    arrival.DisplayedFrame = frame;
                    image.sourceRect = AnimationCels.RectFor(arrival.EntranceSheet ?? arrival.WalkSheet, frame);
                    image.MarkDirtyRepaint();
                }

                float startX = arrival.Unit.Side == Side.Party ? -body.width : layer.layout.width;
                float progress = Mathf.SmoothStep(0, 1, cue == null ? t : Mathf.Clamp01(t / cue.Beat));
                var offset = cue?.Offset(t) ?? Vector2.zero;
                float lift = cue == null && arrival.UsesPortrait ? Mathf.Abs(Mathf.Sin(progress * 3f * Mathf.PI) * 4f) : 0;
                arrival.Figure.style.left = Mathf.Lerp(startX, body.xMin, progress) + body.width * offset.x;
                arrival.Figure.style.top = body.yMin + body.height * offset.y - lift;
                arrival.Figure.style.width = body.width;
                arrival.Figure.style.height = body.height;
            }

            HudOpacity = stage != null ? 1 - Mathf.Clamp01(focus * 2.5f) : 1;
            stage?.SetBossShot(subject, focusHeight, focus);
            if (stage != null && (focus > 0 || fadedHud))
            {
                fadedHud = focus > 0;
                foreach (var card in cards.Values)
                    card.SetEntranceHudOpacity(HudOpacity);
            }
            if (arrivals.Count == 0)
                ResetClock();
        }

        public void Clear(IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            foreach (var arrival in arrivals)
            {
                arrival.Figure.RemoveFromHierarchy();
                if (cards.TryGetValue(arrival.Unit.Id, out var card))
                    card.SetEntranceVisible(true);
            }
            arrivals.Clear();
            stage?.SetBossShot(null, 0, 0);
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
