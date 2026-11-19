using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleActionPlayback
    {
        private const float WalkTime = 0.78f;
        private const float PosePace = 1.65f;
        private const float ArrowFlightTime = 0.65f;
        private const float OrbFlightTime = 0.80f;
        private readonly BattleSpriteView sprite;
        private readonly Texture2D walkSheet;
        private readonly Texture2D projectile;
        private readonly bool isArrow;
        private readonly bool critical;
        private readonly bool broadside;
        private readonly bool cannon;
        private readonly string targetId;
        private readonly (string TargetId, bool Missed, Image View)[] flights;
        private readonly double startedAt;
        private readonly double poseStart;
        private readonly double releaseAt;
        private readonly double returnAt;
        private readonly Image movingImage;
        private readonly Image standingPose;
        private readonly MeteorPlayback meteor;
        private readonly PiercingArrowPlayback piercingArrow;

        public double ImpactAt { get; }
        public double FinishAt { get; }
        public bool IsPiercing => piercingArrow != null;
        public bool LogsAtImpact => meteor != null || IsPiercing;

        public double PiercingImpactAt(string id) => piercingArrow.ImpactAt(id);
        public bool HasMorePiercingHits(string id) => id != piercingArrow.LastTargetId;

        public BattleActionPlayback(BattleSpriteView sprite, ActionKind kind, Texture2D walkSheet,
            Texture2D projectile, Texture2D fragments, bool isArrow, BattleEvent action, IReadOnlyList<BattleEvent> projectileHits,
            double now, float scale, Texture2D special, bool hasImpactEffect, bool critical, bool cannon = false,
            Texture2D piercingParticle = null)
        {
            this.sprite = sprite;
            this.walkSheet = walkSheet;
            this.projectile = projectile;
            this.isArrow = isArrow;
            targetId = action.TargetId;
            this.critical = critical;
            broadside = action.VisualCue == BattleVisualCue.AdmiralBroadside;
            this.cannon = cannon;
            startedAt = now;
            float travel = walkSheet != null ? WalkTime * scale : 0f;
            float poseScale = scale * PosePace;
            poseStart = now + travel;
            float windup = sprite?.PlayAction(kind, poseStart, poseScale,
                projectile != null || walkSheet != null || hasImpactEffect, special,
                fullPose: DockEnemyEffectPlayback.Supports(action.VisualCue))
                ?? 0.20f * scale;
            releaseAt = poseStart + windup;
            bool dropsMeteor = projectile != null && fragments != null;
            bool pierces = projectile != null && action.VisualCue == BattleVisualCue.PiercingArrow;
            float flightTime = projectile == null ? 0f : dropsMeteor ? MeteorPlayback.FallDuration :
                pierces ? PiercingArrowPlayback.FlightDuration(projectileHits.Count) : isArrow ? ArrowFlightTime : OrbFlightTime;
            ImpactAt = releaseAt + flightTime * scale;
            returnAt = sprite == null ? ImpactAt : poseStart + sprite.ActionLength * poseScale;
            FinishAt = Math.Max(returnAt + travel, ImpactAt + (dropsMeteor ? MeteorPlayback.BreakDuration * scale : 0f));
            if (dropsMeteor)
            {
                var targets = projectileHits ?? new[] { action };
                var hits = targets.Where(hit => hit.Detail != "未命中").ToArray();
                var landing = hits.Length > 0 ? hits[(hits.Length - 1) / 2] : targets[(targets.Count - 1) / 2];
                meteor = new MeteorPlayback(projectile, fragments, landing.TargetId, hits.Length == 0,
                    releaseAt, ImpactAt, scale);
            }
            if (pierces)
                piercingArrow = new PiercingArrowPlayback(projectile, piercingParticle, projectileHits, releaseAt, ImpactAt);
            flights = projectile == null || dropsMeteor || pierces ? Array.Empty<(string, bool, Image)>() :
                (projectileHits ?? new[] { action }).Select(hit =>
                {
                    var view = MakeImage("battle-projectile");
                    if (projectileHits != null)
                        view.AddToClassList(action.SkillBookId != null ? "skill-volley-projectile" : "dock-broadside-projectile");
                    return (hit.TargetId, hit.Detail == "未命中", view);
                }).ToArray();
            if (walkSheet != null)
                movingImage = MakeImage("battle-moving-character");
            if (walkSheet == null && sprite?.HasWideAttack == true && (kind != ActionKind.Shield || special != null))
            {
                standingPose = MakeImage("battle-attack-pose");
                sprite.SetBodyVisible(false, keepLocalEffects: true);
            }
            if (walkSheet != null)
                sprite.SetBodyVisible(false);
        }

        public void HandleImpact()
        {
            foreach (var flight in flights)
                flight.View.RemoveFromHierarchy();
        }

        public void Clear()
        {
            HandleImpact();
            meteor?.Clear();
            piercingArrow?.Clear();
            movingImage?.RemoveFromHierarchy();
            standingPose?.RemoveFromHierarchy();
            if (walkSheet != null || standingPose != null)
                sprite.SetBodyVisible(true);
        }

        public void Draw(double now, VisualElement layer, VisualElement meteorLayer, BattleUnitCard actor,
            IReadOnlyDictionary<string, BattleUnitCard> cards)
        {
            if (layer == null)
                return;
            if (meteor != null && meteorLayer != null && cards.TryGetValue(meteor.TargetId, out var landingTarget))
                meteor.Draw(now, meteorLayer, landingTarget.BodyBounds);
            if (actor == null)
                return;
            var source = layer.WorldToLocal(actor.BodyBounds);
            if (source.width <= 0f)
                return;

            if (standingPose != null)
            {
                var poseLayer = meteor != null && meteorLayer != null ? meteorLayer : layer;
                var poseBounds = poseLayer == layer ? source : poseLayer.WorldToLocal(actor.BodyBounds);
                DrawStandingPose(now, poseLayer, poseBounds);
            }
            piercingArrow?.Draw(now, layer, source, cards);
            if (flights.Length > 0 && now >= releaseAt && now < ImpactAt)
            {
                float progress = Fraction(now, releaseAt, ImpactAt);
                foreach (var flight in flights)
                {
                    if (!cards.TryGetValue(flight.TargetId, out var target))
                        continue;
                    var destination = layer.WorldToLocal(target.BodyBounds);
                    if (destination.width <= 0f)
                        continue;
                    DrawProjectile(flight.View, flight.Missed, source, destination, progress);
                    Attach(layer, flight.View);
                }
            }
            if (movingImage == null || !cards.TryGetValue(targetId, out var defender))
                return;
            if (!sprite.IsAlive)
            {
                Clear();
                return;
            }
            var meleeTarget = layer.WorldToLocal(defender.BodyBounds);
            if (meleeTarget.width <= 0f)
                return;
            DrawMelee(now, source, meleeTarget);
            Attach(layer, movingImage);
        }

        private void DrawStandingPose(double now, VisualElement layer, Rect source)
        {
            if (now >= returnAt || !sprite.IsAlive)
            {
                standingPose.RemoveFromHierarchy();
                sprite.SetBodyVisible(true);
                return;
            }
            SetImage(standingPose, (Texture2D)sprite.Figure.image, sprite.Figure.sourceRect);
            standingPose.tintColor = sprite.Figure.tintColor;
            var size = sprite.PoseSize(source.size);
            SetBounds(standingPose, new Rect(source.center.x - size.x * 0.5f, source.yMax - size.y, size.x, size.y));
            Attach(layer, standingPose);
        }

        private void DrawProjectile(Image view, bool missed, Rect source, Rect destination, float progress)
        {
            var start = source.min + Vector2.Scale(source.size, new Vector2(0.5f, cannon ? 0.73f : 0.45f));
            var end = destination.min + Vector2.Scale(destination.size, new Vector2(0.5f, missed ? -0.10f : 0.55f));
            var direction = (end - start).normalized;
            float releaseOffset = !isArrow && sprite?.HasWideAttack == true ? 0.50f : 0.30f;
            start += direction * source.width * releaseOffset;
            float width = broadside ? 56f : isArrow ? 50f : 32f;
            float height = width * projectile.height / projectile.width;
            if (isArrow)
                end -= direction * width * 0.43f;
            var center = Vector2.Lerp(start, end, progress);
            SetImage(view, projectile, new Rect(0, 0, projectile.width, projectile.height));
            view.tintColor = critical ? new Color(1f, 0.34f, 0.28f) : Color.white;
            view.style.rotate = new Rotate(new Angle(isArrow ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f));
            SetBounds(view, new Rect(center - new Vector2(width, height) * 0.5f, new Vector2(width, height)));
        }

        private void DrawMelee(double now, Rect source, Rect destination)
        {
            float direction = Mathf.Sign(destination.center.x - source.center.x);
            float stoppingDistance = destination.width * 0.30f + source.width * 0.20f;
            float destinationX = destination.center.x - direction * stoppingDistance;
            float approaching = Fraction(now, startedAt, poseStart);
            float retreating = Fraction(now, returnAt, FinishAt);
            float x = Mathf.Lerp(source.center.x, destinationX, Mathf.SmoothStep(0f, 1f, approaching));
            x = Mathf.Lerp(x, source.center.x, Mathf.SmoothStep(0f, 1f, retreating));
            var size = source.size;
            float lift = 0;
            if (now < poseStart || now >= returnAt)
            {
                int index = Mathf.Min(5, Mathf.FloorToInt((now < poseStart ? approaching : retreating) * 6f));
                if (sprite.UsesPortrait && walkSheet == sprite.Sheet)
                {
                    SetImage(movingImage, walkSheet, new Rect(0, 0, walkSheet.width, walkSheet.height));
                    lift = Mathf.Abs(Mathf.Sin((now < poseStart ? approaching : retreating) * 3f * Mathf.PI)) * 5f;
                    movingImage.style.rotate = new Rotate(new Angle(direction * (now < poseStart ? 5f : -5f)));
                    movingImage.style.translate = new Translate(0, 0);
                }
                else
                {
                    int width = walkSheet.width / 3;
                    int height = walkSheet.height / 2;
                    SetImage(movingImage, walkSheet, new Rect(index % 3 * width, index / 3 * height, width, height));
                }
                movingImage.tintColor = Color.white;
            }
            else
            {
                SetImage(movingImage, (Texture2D)sprite.Figure.image, sprite.Figure.sourceRect);
                movingImage.tintColor = sprite.Figure.tintColor;
                size = sprite.PoseSize(source.size);
                if (sprite.UsesPortrait)
                {
                    movingImage.style.rotate = sprite.Figure.style.rotate;
                    movingImage.style.translate = sprite.Figure.style.translate;
                }
            }
            float orientation = (now >= returnAt ? -direction : direction) * (sprite.FacesLeft ? -1f : 1f);
            movingImage.style.scale = new Scale(new Vector3(orientation, 1f, 1f));
            SetBounds(movingImage, new Rect(x - size.x * 0.5f, source.yMax - size.y - lift, size.x, size.y));
        }

        private static Image MakeImage(string name)
        {
            var image = new Image { name = name, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            return image;
        }

        private static void Attach(VisualElement layer, Image image)
        {
            if (image.parent == layer)
                return;
            image.RemoveFromHierarchy();
            layer.Add(image);
        }

        private static void SetImage(Image image, Texture2D texture, Rect frame)
        {
            if (image.image != texture)
                image.image = texture;
            if (image.sourceRect == frame)
                return;
            image.sourceRect = frame;
            image.MarkDirtyRepaint();
        }

        private static void SetBounds(Image image, Rect bounds)
        {
            image.style.left = bounds.xMin;
            image.style.top = bounds.yMin;
            image.style.width = bounds.width;
            image.style.height = bounds.height;
        }

        private static float Fraction(double now, double start, double end) =>
            end > start ? Mathf.Clamp01((float)((now - start) / (end - start))) : 0f;
    }
}
