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
        private readonly bool missed;
        private readonly double startedAt;
        private readonly double poseStart;
        private readonly double releaseAt;
        private readonly double returnAt;
        private readonly Image movingImage;
        private readonly Image standingPose;

        public double ImpactAt { get; }
        public double FinishAt { get; }

        public BattleActionPlayback(BattleSpriteView sprite, ActionKind kind, Texture2D walkSheet,
            Texture2D projectile, bool isArrow, bool missed, double now, float scale)
        {
            this.sprite = sprite;
            this.walkSheet = walkSheet;
            this.projectile = projectile;
            this.isArrow = isArrow;
            this.missed = missed;
            startedAt = now;
            float travel = walkSheet != null ? WalkTime * scale : 0f;
            float poseScale = scale * PosePace;
            poseStart = now + travel;
            float windup = sprite?.PlayAction(kind, poseStart, poseScale, projectile != null || walkSheet != null)
                ?? 0.20f * scale;
            releaseAt = poseStart + windup;
            ImpactAt = releaseAt + (projectile == null ? 0f : (isArrow ? ArrowFlightTime : OrbFlightTime) * scale);
            returnAt = sprite == null ? ImpactAt : poseStart + sprite.ActionLength(kind) * poseScale;
            FinishAt = System.Math.Max(returnAt + travel, ImpactAt);
            if (projectile != null || walkSheet != null)
                movingImage = MakeImage(projectile != null ? "battle-projectile" : "battle-moving-character");
            if (walkSheet == null && sprite?.HasWideAttack == true && kind != ActionKind.Shield)
            {
                standingPose = MakeImage("battle-attack-pose");
                sprite.SetBodyVisible(false, keepLocalEffects: true);
            }
            if (walkSheet != null)
                sprite.SetBodyVisible(false);
        }

        public void HandleImpact()
        {
            if (projectile != null)
                movingImage.RemoveFromHierarchy();
        }

        public void Clear()
        {
            movingImage?.RemoveFromHierarchy();
            standingPose?.RemoveFromHierarchy();
            if (walkSheet != null || standingPose != null)
                sprite.SetBodyVisible(true);
        }

        public void Draw(double now, VisualElement layer, BattleUnitCard actor, BattleUnitCard target)
        {
            if ((movingImage == null && standingPose == null) || actor == null || target == null || layer == null)
                return;

            var source = layer.WorldToLocal(actor.BodyBounds);
            var destination = layer.WorldToLocal(target.BodyBounds);
            if (!(source.width > 0 && destination.width > 0))
                return;

            if (standingPose != null)
                DrawStandingPose(now, layer, source);
            if (movingImage == null)
                return;

            if (projectile != null)
            {
                if (now < releaseAt || now >= ImpactAt)
                    return;
                DrawProjectile(source, destination, Fraction(now, releaseAt, ImpactAt));
            }
            else
            {
                if (!sprite.IsAlive)
                {
                    Clear();
                    return;
                }
                DrawMelee(now, source, destination);
            }

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

        private void DrawProjectile(Rect source, Rect destination, float progress)
        {
            var start = source.min + Vector2.Scale(source.size, new Vector2(0.5f, 0.45f));
            var end = destination.min + Vector2.Scale(destination.size, new Vector2(0.5f, missed ? -0.10f : 0.55f));
            var direction = (end - start).normalized;
            float releaseOffset = !isArrow && sprite?.HasWideAttack == true ? 0.50f : 0.30f;
            start += direction * source.width * releaseOffset;
            float width = isArrow ? 50f : 32f;
            float height = width * projectile.height / projectile.width;
            if (isArrow)
                end -= direction * width * 0.43f;
            var center = Vector2.Lerp(start, end, progress);
            SetImage(movingImage, projectile, new Rect(0, 0, projectile.width, projectile.height));
            movingImage.style.rotate = new Rotate(new Angle(isArrow ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f));
            SetBounds(movingImage, new Rect(center - new Vector2(width, height) * 0.5f, new Vector2(width, height)));
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
            if (now < poseStart || now >= returnAt)
            {
                int index = Mathf.Min(5, Mathf.FloorToInt((now < poseStart ? approaching : retreating) * 6f));
                int width = walkSheet.width / 3;
                int height = walkSheet.height / 2;
                SetImage(movingImage, walkSheet, new Rect(index % 3 * width, index / 3 * height, width, height));
                movingImage.tintColor = Color.white;
            }
            else
            {
                SetImage(movingImage, (Texture2D)sprite.Figure.image, sprite.Figure.sourceRect);
                movingImage.tintColor = sprite.Figure.tintColor;
                size = sprite.PoseSize(source.size);
            }
            float orientation = now >= returnAt ? -direction : direction;
            movingImage.style.scale = new Scale(new Vector3(orientation, 1f, 1f));
            SetBounds(movingImage, new Rect(x - size.x * 0.5f, source.yMax - size.y, size.x, size.y));
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
