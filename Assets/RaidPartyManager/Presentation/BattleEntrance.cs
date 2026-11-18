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
            public int DisplayedFrame = -1;

            public Arrival(Combatant unit, EncounterData content, double start)
            {
                Unit = unit;
                Start = start;
                WalkSheet = content.BattleWalkFor(unit.Character.Id);
                var art = content.BattleSpriteFor(unit.Character.Id);
                if (WalkSheet == null && art != null)
                    Figure = new EntranceWalkPose(art, unit.Character.Profession, unit.Side == Side.Enemy);
                else
                    Figure = new Image
                    {
                        image = WalkSheet ?? content.PortraitFor(unit.Character.Id),
                        scaleMode = WalkSheet != null ? ScaleMode.StretchToFill : ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                Figure.name = "entrance-" + unit.Id;
                Figure.style.position = Position.Absolute;
            }
        }

        private readonly List<Arrival> arrivals = new List<Arrival>();
        private double clock;
        private double? previousFrame;
        public bool Active => arrivals.Count > 0;

        public void Add(IEnumerable<Combatant> team, EncounterData content)
        {
            var lineup = team.Where(unit => unit.Health > 0).OrderBy(unit => unit.Position).ToArray();
            for (int i = 0; i < lineup.Length; i++)
                arrivals.Add(new Arrival(lineup[i], content, clock + i * Stagger));
        }

        public void Show(double now, VisualElement layer, IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            if (layer == null)
                return;

            // Wait for layout and let the first loaded frame present a real entrance pose.
            bool ready = layer.layout.width > 0;
            if (ready && previousFrame.HasValue)
                clock += Math.Min(0.08, now - previousFrame.Value);
            previousFrame = ready ? now : null;

            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                var arrival = arrivals[i];
                cards.TryGetValue(arrival.Unit.Id, out var card);
                double elapsed = clock - arrival.Start;
                if (elapsed >= TravelTime)
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

                int frame = (int)(elapsed * 15.0) % 6;
                if (arrival.Figure is EntranceWalkPose pose)
                    pose.ShowFrame(frame);
                else if (arrival.WalkSheet != null && arrival.Figure is Image image && arrival.DisplayedFrame != frame)
                {
                    arrival.DisplayedFrame = frame;
                    int width = arrival.WalkSheet.width / 3;
                    int height = arrival.WalkSheet.height / 2;
                    image.sourceRect = new Rect(frame % 3 * width, frame / 3 * height, width, height);
                    image.MarkDirtyRepaint();
                }

                float startX = arrival.Unit.Side == Side.Party ? -body.width : layer.layout.width;
                float progress = Mathf.SmoothStep(0, 1, (float)(elapsed / TravelTime));
                arrival.Figure.style.left = Mathf.Lerp(startX, body.xMin, progress);
                arrival.Figure.style.top = body.yMin;
                arrival.Figure.style.width = body.width;
                arrival.Figure.style.height = body.height;
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
            ResetClock();
        }

        private void ResetClock()
        {
            clock = 0;
            previousFrame = null;
        }
    }
}
