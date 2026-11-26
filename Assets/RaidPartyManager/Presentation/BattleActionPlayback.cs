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
        private readonly StageActorImage movingImage;
        private readonly StageActorImage standingPose;
        private readonly MeteorPlayback meteor;
        private readonly PiercingArrowPlayback piercingArrow;

        private SoundCue releaseSound;

        internal void SoundAt(double now)
        {
            if (releaseSound == SoundCue.None || now < releaseAt) return;
            GameAudio.Play(releaseSound);
            if (releaseSound == SoundCue.BowRelease) GameAudio.Play(SoundCue.ArrowFlight, .32f);
            releaseSound = SoundCue.None;
        }

        public double ImpactAt { get; }
        public double FinishAt { get; }
        public bool IsPiercing => piercingArrow != null;

        public double NextPiercingImpactAt => piercingArrow.NextImpactAt;
        public bool AdvancePiercingHit() => piercingArrow.AdvanceHit();

        public BattleActionPlayback(BattleSpriteView sprite, ActionKind kind, Texture2D walkSheet,
            Texture2D projectile, Texture2D fragments, bool isArrow, BattleEvent action, IReadOnlyList<BattleEvent> projectileHits,
            double now, float scale, Texture2D special, bool hasImpactEffect, bool critical, bool cannon = false,
            Texture2D piercingParticle = null, SoundCue releaseSound = SoundCue.None, Texture2D signatureProjectile = null)
        {
            this.releaseSound = releaseSound;
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
            bool hasProjectile = projectile != null || signatureProjectile != null;
            float windup = sprite?.PlayAction(kind, poseStart, poseScale,
                hasProjectile || walkSheet != null || hasImpactEffect, special,
                fullPose: DockEnemyEffectPlayback.Supports(action.VisualCue))
                ?? 0.20f * scale;
            releaseAt = poseStart + windup;
            bool dropsMeteor = projectile != null && fragments != null;
            bool pierces = projectile != null && action.VisualCue == BattleVisualCue.PiercingArrow;
            float flightTime = !hasProjectile ? 0f : dropsMeteor ? MeteorPlayback.FallDuration :
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
            flights = !hasProjectile || dropsMeteor || pierces ? Array.Empty<(string, bool, Image)>() :
                (projectileHits ?? new[] { action }).Select(hit =>
                {
                    Image view = signatureProjectile != null
                        ? new AttackSignatureImage("battle-projectile", signatureProjectile, inFlight: true)
                        : MakeImage("battle-projectile");
                    if (projectileHits != null)
                        view.AddToClassList(action.SkillBookId != null ? "skill-volley-projectile" : "dock-broadside-projectile");
                    return (hit.TargetId, hit.Detail == "未命中", view);
                }).ToArray();
            if (walkSheet != null)
                movingImage = MakeActor("battle-moving-character");
            if (walkSheet == null && sprite?.HasWideAttack == true && (kind != ActionKind.Shield || special != null))
                standingPose = MakeActor("battle-attack-pose");
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
            if (!(source.width > 0 && source.height > 0))
            {
                if (movingImage != null || standingPose != null) sprite.SetBodyVisible(true);
                return;
            }

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
            if (!(meleeTarget.width > 0 && meleeTarget.height > 0))
            {
                sprite.SetBodyVisible(true);
                return;
            }
            DrawMelee(now, source, meleeTarget);
            Attach(layer, movingImage);
            sprite.SetBodyVisible(false);
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
            standingPose.SetInk(sprite.Figure.Ink);
            var size = sprite.PoseSize(source.size);
            SetBounds(standingPose, new Rect(source.center.x - size.x * 0.5f, source.yMax - size.y, size.x, size.y));
            standingPose.style.scale = new Scale(new Vector3(sprite.PoseScale.x, sprite.PoseScale.y, 1f));
            standingPose.style.translate = sprite.PoseTranslate;
            Attach(layer, standingPose);
            sprite.SetBodyVisible(false, keepLocalEffects: true);
        }

        private void DrawProjectile(Image view, bool missed, Rect source, Rect destination, float progress)
        {
            var start = source.min + Vector2.Scale(source.size, new Vector2(0.5f, cannon ? 0.73f : 0.45f));
            var end = destination.min + Vector2.Scale(destination.size, new Vector2(0.5f, missed ? -0.10f : 0.55f));
            var direction = (end - start).normalized;
            float releaseOffset = !isArrow && sprite?.HasWideAttack == true ? 0.50f : 0.30f;
            start += direction * source.width * releaseOffset;
            var signature = view as AttackSignatureImage;
            float width = signature != null ? isArrow ? 76f : 66f : broadside ? 56f : isArrow ? 50f : 32f;
            float height = signature != null ? width : width * projectile.height / projectile.width;
            if (isArrow)
                end -= direction * width * 0.43f;
            var center = Vector2.Lerp(start, end, progress);
            if (signature != null)
                signature.ShowFrame(progress);
            else
            {
                SetImage(view, projectile, AnimationCels.RectFor(projectile, AnimationCels.At(progress)));
                view.tintColor = critical ? new Color(1f, 0.34f, 0.28f) : Color.white;
            }
            bool followsPath = signature != null || isArrow;
            view.style.rotate = new Rotate(new Angle(followsPath ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f));
            SetBounds(view, new Rect(center - new Vector2(width, height) * 0.5f, new Vector2(width, height)));
        }

        private void DrawMelee(double now, Rect source, Rect destination)
        {
            float direction = Mathf.Sign(destination.center.x - source.center.x);
            float stoppingDistance = destination.width * 0.30f + source.width * 0.20f;
            float destinationX = destination.center.x - direction * stoppingDistance;
            float approaching = Fraction(now, startedAt, poseStart);
            float retreating = Fraction(now, returnAt, FinishAt);
            float advance = Mathf.SmoothStep(0f, 1f, approaching);
            float withdraw = Mathf.SmoothStep(0f, 1f, retreating);
            float x = Mathf.Lerp(source.center.x, destinationX, advance);
            x = Mathf.Lerp(x, source.center.x, withdraw);
            var size = source.size;
            float lift = 0;
            var scale = Vector2.one;
            if (now < poseStart || now >= returnAt)
            {
                // Step through the cycle by distance covered, so the feet slow down with the body.
                float stride = now < poseStart ? advance : withdraw;
                int index = AnimationCels.At(stride);
                if (sprite.UsesPortrait && walkSheet == sprite.Sheet)
                {
                    SetImage(movingImage, walkSheet, new Rect(0, 0, walkSheet.width, walkSheet.height));
                    lift = Mathf.Abs(Mathf.Sin((now < poseStart ? approaching : retreating) * 3f * Mathf.PI)) * 5f;
                    movingImage.style.rotate = new Rotate(new Angle(direction * (now < poseStart ? 5f : -5f)));
                    movingImage.style.translate = new Translate(0, 0);
                }
                else
                {
                    SetImage(movingImage, walkSheet, AnimationCels.RectFor(walkSheet, index));
                    // Two footfalls per cycle; the body rises a little over each passing pose.
                    lift = Mathf.Abs(Mathf.Sin(stride * 2f * Mathf.PI)) * 1.6f;
                    movingImage.style.translate = new Translate(0, 0);
                }
                movingImage.SetInk(Color.white);
            }
            else
            {
                SetImage(movingImage, (Texture2D)sprite.Figure.image, sprite.Figure.sourceRect);
                movingImage.SetInk(sprite.Figure.Ink);
                size = sprite.PoseSize(source.size);
                scale = sprite.PoseScale;
                movingImage.style.translate = sprite.PoseTranslate;
                if (sprite.UsesPortrait)
                    movingImage.style.rotate = sprite.Figure.style.rotate;
            }
            movingImage.RideHeight = now < poseStart ? RidingWave.Envelope(approaching) : RidingWave.Envelope(retreating);
            float orientation = (now >= returnAt ? -direction : direction) * (sprite.FacesLeft ? -1f : 1f);
            movingImage.style.scale = new Scale(new Vector3(orientation * scale.x, scale.y, 1f));
            SetBounds(movingImage, new Rect(x - size.x * 0.5f, source.yMax - size.y - lift, size.x, size.y));
        }

        private StageActorImage MakeActor(string name)
        {
            var view = new StageActorImage(sprite.Stage, true)
            {
                name = name, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill
            };
            view.style.position = Position.Absolute;
            view.style.transformOrigin = BattleSpriteView.FootOrigin;
            return view;
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
