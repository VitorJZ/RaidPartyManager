using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class DockRouteMap : VisualElement
    {
        private const float AdvanceDuration = 0.72f;
        private static readonly Color RouteInk = new Color32(32, 31, 28, 230);
        private static readonly Color Travelled = new Color32(130, 128, 107, 218);
        private static readonly Color Ahead = new Color32(219, 173, 102, 245);
        private static readonly Color Medallion = new Color32(24, 37, 42, 250);

        private readonly Marker? previous;
        private readonly Marker current;
        private readonly List<Marker> choices = new List<Marker>(2);
        private readonly VisualElement roads;
        private IVisualElementScheduledItem animation;
        private double? startedAt;
        private float progress;
        private float reveal;

        private readonly struct Marker
        {
            public readonly RouteStop Stop;
            public readonly VisualElement View;
            public readonly VisualElement IconFrame;

            public Marker(RouteStop stop, VisualElement view, VisualElement iconFrame)
            {
                Stop = stop;
                View = view;
                IconFrame = iconFrame;
            }
        }

        public DockRouteMap(AdventureRun run, AdventureContentAsset assets, Action<int> enter, bool animateAdvance)
        {
            name = "dock-route";
            style.flexGrow = 1;
            style.minHeight = 0;
            style.marginTop = 10;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = (Color)new Color32(11, 25, 30, 118);
            PrototypeStyle.Outline(this, new Color32(85, 107, 107, 170), 8);

            roads = new VisualElement { name = "route-roads", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(roads);
            roads.generateVisualContent += DrawRoads;
            Add(roads);

            int? suspended = run.SuspendedChoice;
            var destination = suspended.HasValue ? run.RouteChoice(suspended.Value) : run.RouteDestination;
            var earlier = destination.HasValue ? run.RouteCurrent : run.RoutePrevious;
            if (earlier is RouteStop prior)
                previous = CreateMarker(prior, "route-node-previous", assets, Travelled);

            current = CreateMarker(destination ?? run.RouteCurrent, "route-node-current",
                assets, PrototypeStyle.Accent, isCurrent: true,
                enter: suspended.HasValue ? () => enter(suspended.Value) : null);
            if (!destination.HasValue && !run.FloorCleared && !run.Failed)
            {
                for (int i = 0; i < run.RouteOptions.Count; i++)
                {
                    int index = i;
                    choices.Add(CreateMarker(run.RouteChoice(index), "route-choice-" + index, assets, Ahead,
                        enter: () => enter(index)));
                }
            }

            progress = animateAdvance && previous.HasValue ? 0f : 1f;
            reveal = progress;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutMarkers());
            if (progress < 1f)
            {
                foreach (var choice in choices)
                    choice.View.visible = false;
                animation = schedule.Execute(Advance).Every(16);
            }
        }

        private Marker CreateMarker(RouteStop stop, string id, AdventureContentAsset assets, Color edge,
            bool isCurrent = false, Action enter = null)
        {
            VisualElement view = enter == null ? new VisualElement { pickingMode = PickingMode.Ignore } : new Button(enter);
            view.name = id;
            view.userData = stop;
            view.style.position = Position.Absolute;
            view.style.alignItems = Align.Center;
            view.style.paddingTop = view.style.paddingRight = view.style.paddingBottom = view.style.paddingLeft = 0;
            view.style.marginLeft = view.style.marginRight = view.style.marginTop = view.style.marginBottom = 0;
            view.style.backgroundColor = Color.clear;
            view.style.borderLeftWidth = view.style.borderRightWidth = view.style.borderTopWidth = view.style.borderBottomWidth = 0;

            var iconFrame = new VisualElement { name = "route-node-medallion", pickingMode = PickingMode.Ignore };
            iconFrame.style.backgroundColor = Medallion;
            iconFrame.style.flexShrink = 0;
            iconFrame.style.paddingLeft = iconFrame.style.paddingRight = iconFrame.style.paddingTop = iconFrame.style.paddingBottom = 7;
            PrototypeStyle.Outline(iconFrame, isCurrent ? Color.clear : edge, 80);
            var icon = new Image
            {
                name = "route-node-icon",
                image = stop.Kind.HasValue ? assets.NodeIcon(stop.Kind.Value) : assets.RouteSignpost,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            icon.style.flexGrow = 1;
            iconFrame.Add(icon);
            if (isCurrent)
            {
                var circle = new VisualElement { name = "route-current-circle", pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(circle);
                circle.style.top = circle.style.bottom = circle.style.left = circle.style.right = -9;
                circle.generateVisualContent += context => DrawCurrentCircle(context.painter2D, circle.contentRect);
                iconFrame.Add(circle);
            }
            view.Add(iconFrame);

            if (enter != null)
            {
                Color hover = new Color32(54, 64, 60, 255);
                view.RegisterCallback<PointerEnterEvent>(_ => iconFrame.style.backgroundColor = hover);
                view.RegisterCallback<PointerLeaveEvent>(_ => iconFrame.style.backgroundColor = Medallion);
                view.RegisterCallback<FocusInEvent>(_ => PrototypeStyle.Outline(iconFrame, PrototypeStyle.Ink, 80));
                view.RegisterCallback<FocusOutEvent>(_ => PrototypeStyle.Outline(iconFrame, edge, 80));
            }
            Add(view);
            return new Marker(stop, view, iconFrame);
        }

        private void Advance()
        {
            if (!(contentRect.height > 0f))
                return;

            double now = Time.unscaledTimeAsDouble;
            startedAt ??= now;
            progress = Mathf.Clamp01((float)(now - startedAt.Value) / AdvanceDuration);
            reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 1f, progress));
            LayoutMarkers();
            if (progress >= 1f)
                animation.Pause();
        }

        private void LayoutMarkers()
        {
            if (!(contentRect.width > 0f && contentRect.height > 0f))
                return;

            float size = Mathf.Clamp(contentRect.height * 0.17f, 36f, 72f);
            if (previous is Marker prior)
                Place(prior);
            Place(current);
            float nodeReveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, reveal));
            foreach (var choice in choices)
            {
                Place(choice);
                choice.View.visible = nodeReveal > 0f;
                choice.View.style.opacity = nodeReveal;
                choice.View.pickingMode = nodeReveal >= 0.65f ? PickingMode.Position : PickingMode.Ignore;
            }
            roads.MarkDirtyRepaint();

            void Place(Marker marker)
            {
                Vector2 point = PositionOf(marker.Stop);
                marker.View.style.left = point.x - size * 0.5f;
                marker.View.style.top = point.y - size * 0.5f;
                marker.View.style.width = size;
                marker.IconFrame.style.width = marker.IconFrame.style.height = size;
            }
        }

        private Vector2 PositionOf(RouteStop stop)
        {
            var location = new Vector2(current.Stop.Lane, current.Stop.Number);
            if (previous is Marker prior && progress < 1f)
                location = Vector2.Lerp(new Vector2(prior.Stop.Lane, prior.Stop.Number), location,
                    Mathf.SmoothStep(0f, 1f, progress));
            float laneWidth = Mathf.Min(180f, contentRect.width * 0.17f);
            float step = contentRect.height * 0.295f;
            float centerY = contentRect.height * Mathf.Lerp(0.68f, 0.53f, Mathf.Clamp01(location.y));
            return new Vector2(contentRect.width * 0.5f + (stop.Lane - location.x) * laneWidth,
                centerY + (location.y - stop.Number) * step);
        }

        private void DrawRoads(MeshGenerationContext context)
        {
            var pen = context.painter2D;
            pen.lineCap = LineCap.Round;
            pen.lineJoin = LineJoin.Round;
            Vector2 origin = PositionOf(current.Stop);
            if (previous is Marker prior)
                DrawRoad(pen, PositionOf(prior.Stop), origin, 1f, Travelled);
            foreach (var choice in choices)
                if (reveal > 0f)
                    DrawRoad(pen, origin, PositionOf(choice.Stop), reveal, Ahead);
        }

        private static void DrawCurrentCircle(Painter2D pen, Rect bounds)
        {
            float size = Mathf.Min(bounds.width, bounds.height);
            if (size <= 0f)
                return;

            float radius = size * 0.43f;
            float scale = Mathf.Clamp(size / 84f, 0.65f, 1.15f);
            pen.lineCap = LineCap.Round;
            pen.lineJoin = LineJoin.Round;
            Stroke(new Color32(193, 43, 41, 110), 6.7f * scale);
            Stroke(new Color32(239, 77, 63, 245), 4.5f * scale);

            void Stroke(Color color, float width)
            {
                const int segments = 64;
                pen.strokeColor = color;
                pen.lineWidth = width;
                pen.BeginPath();
                for (int segment = 0; segment <= segments; segment++)
                {
                    float t = segment / (float)segments;
                    float angle = -0.9f + t * (2f * Mathf.PI + 0.28f);
                    float wobble = 1f + 0.036f * Mathf.Sin(angle * 3f + 0.4f) + 0.019f * Mathf.Sin(angle * 7f);
                    float arc = radius * wobble - 2.4f * scale * Mathf.InverseLerp(0.87f, 1f, t);
                    var point = bounds.center + new Vector2(Mathf.Cos(angle) * 1.04f, Mathf.Sin(angle) * 0.96f) * arc;
                    if (segment == 0)
                        pen.MoveTo(point);
                    else
                        pen.LineTo(point);
                }
                pen.Stroke();
            }
        }

        private static void DrawRoad(Painter2D pen, Vector2 from, Vector2 to, float length, Color ink)
        {
            Stroke(RouteInk, 8f);
            Stroke(ink, 3f);

            void Stroke(Color color, float weight)
            {
                pen.lineWidth = weight;
                pen.strokeColor = color;
                pen.BeginPath();
                pen.MoveTo(from);
                const int segments = 18;
                for (int segment = 1; segment <= segments; segment++)
                {
                    float amount = length * segment / segments;
                    pen.LineTo(new Vector2(Mathf.SmoothStep(from.x, to.x, amount), Mathf.Lerp(from.y, to.y, amount)));
                }
                pen.Stroke();
            }
        }
    }
}
