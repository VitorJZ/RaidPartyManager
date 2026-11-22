using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Rows are idle, primary action, hit reaction, and death.
    internal sealed class BattleSpriteView : VisualElement
    {
        private enum Motion { Idle, Action, Hit, Death }

        private const int Columns = AnimationCels.Count;
        private const int Rows = 4;
        private const int AttackColumns = 4;
        private const int AttackRows = 4;
        private const float IdleLength = 1.6f;
        private static readonly float[] HitEnds = { 0.07f, 0.17f, 0.28f, 0.40f };
        private static readonly float[] DeathEnds = { 0.08f, 0.19f, 0.32f, 0.46f, 0.60f, 0.74f, 0.87f, 1f };

        private readonly StageActorImage figure;
        private readonly VisualElement effects;
        private readonly Profession profession;
        private readonly Texture2D attackSheet;
        private Texture2D specialSheet;
        private readonly float idleOffset;
        private readonly float[] actionEnds;
        private readonly float impactTime;
        private Motion motion;
        private double motionStart;
        private bool? alive;
        private float actionScale = 1f;
        private ActionKind action;
        private int displayedFrame = -1;
        private float effectProgress = -1f;
        private bool usesExternalEffect;
        private bool fullActionPose;
        private readonly SoundCue fallSound;
        private bool pendingFall;
        private bool retreating;
        private Texture2D retreatWalk;

        public Texture2D Sheet { get; }
        public bool UsesPortrait { get; }
        public StageActorImage Figure => figure;
        public BattleStage Stage { get; }
        public bool IsAlive => alive == true;
        public bool HasWideAttack => attackSheet != null || specialSheet != null;
        public bool FacesLeft { get; }

        public BattleSpriteView(Texture2D sheet, Texture2D attackSheet, Profession profession, int position, bool facesLeft,
            BattleStage stage, bool usesPortrait = false, SoundCue fallSound = SoundCue.None)
        {
            name = "profession-portrait-art";
            Stage = stage;
            Sheet = sheet;
            this.fallSound = fallSound;
            UsesPortrait = usesPortrait;
            this.attackSheet = attackSheet;
            this.profession = profession;
            FacesLeft = facesLeft;
            idleOffset = position * 0.17f;
            var (ends, contactFrame) = profession switch
            {
                Profession.Warrior => (new[] { 0.06f, 0.15f, 0.27f, 0.42f, 0.55f, 0.62f, 0.70f, 0.78f }, 4),
                Profession.Mage => (new[] { 0.08f, 0.16f, 0.24f, 0.42f, 0.57f, 0.65f, 0.76f, 0.85f }, 4),
                Profession.Archer => (new[] { 0.07f, 0.14f, 0.23f, 0.34f, 0.54f, 0.65f, 0.74f, 0.83f }, 5),
                _ => (new[] { 0.07f, 0.15f, 0.27f, 0.42f, 0.57f, 0.68f, 0.77f, 0.88f }, 4)
            };
            actionEnds = ends;
            impactTime = ends[(facesLeft ? 4 : contactFrame) - 1];
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.top = 5;
            style.bottom = 3;
            style.left = style.right = 5;

            figure = new StageActorImage(stage)
            {
                name = "battle-sprite",
                image = sheet,
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            figure.style.position = Position.Absolute;
            figure.style.bottom = 0;
            if (UsesPortrait)
                figure.sourceRect = new Rect(0, 0, sheet.width, sheet.height);
            Add(figure);

            effects = new VisualElement { name = "battle-sprite-effects", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(effects);
            effects.generateVisualContent += DrawActionEffect;
            Add(effects);

            RegisterCallback<GeometryChangedEvent>(evt => PositionFigure(evt.newRect.size));
            ShowFrame(0);
        }

        private void PositionFigure(Vector2 size)
        {
            if (!(size.x > 0f && size.y > 0f))
                return;

            float ratio = UsesPortrait ? (float)Sheet.width / Sheet.height :
                (float)(Sheet.width / Columns) / (Sheet.height / Rows);
            float width = Mathf.Min(size.x, size.y * ratio);
            float height = width / ratio;
            figure.style.left = (size.x - width) * 0.5f;
            figure.style.width = width;
            figure.style.height = height;
        }

        private bool SimpleGuard => action == ActionKind.Shield && specialSheet == null && !fullActionPose;
        public float ActionLength => SimpleGuard ? 0.48f : actionEnds[^1];

        public float PlayAction(ActionKind kind, double now, float scale, bool usesExternalEffect, Texture2D special,
            bool fullPose = false)
        {
            action = kind;
            specialSheet = special;
            displayedFrame = -1;
            actionScale = scale;
            this.usesExternalEffect = usesExternalEffect;
            fullActionPose = fullPose;
            SetMotion(Motion.Action, now);
            return (SimpleGuard ? 0.16f : impactTime) * scale;
        }

        public void SetBodyVisible(bool visible, bool keepLocalEffects = false)
        {
            figure.visible = visible;
            effects.visible = visible || keepLocalEffects;
        }

        public Vector2 PoseSize(Vector2 idleSize) => UsesPortrait ? idleSize : Vector2.Scale(idleSize,
            new Vector2(Figure.sourceRect.width / (Sheet.width / Columns), Figure.sourceRect.height / (Sheet.height / Rows)));

        public void PlayHit(double now)
        {
            if (alive == true)
                SetMotion(Motion.Hit, now);
        }

        public void PlayRetreat(double now, Texture2D walkSheet)
        {
            retreating = true;
            retreatWalk = walkSheet;
            SetAlive(false, now);
        }

        public bool IsReactingAt(double now) => motion switch
        {
            Motion.Hit => now < motionStart + HitEnds[^1],
            Motion.Death => now < motionStart + DeathEnds[^1],
            _ => false
        };

        public void SetAlive(bool isAlive, double now)
        {
            if (alive == isAlive)
                return;
            pendingFall = !isAlive && alive == true && !retreating;
            if (isAlive)
            {
                retreating = false;
                retreatWalk = null;
                figure.style.scale = new Scale(Vector3.one);
                figure.style.opacity = 1;
                SetMotion(Motion.Idle, alive.HasValue ? now : now - idleOffset);
            }
            else
                SetMotion(Motion.Death, alive.HasValue ? now : now - DeathEnds[^1]);
            alive = isAlive;
        }

        private void SetMotion(Motion next, double now)
        {
            motion = next;
            motionStart = now;
        }

        public void UpdateFrame(double now)
        {
            float elapsed = Mathf.Max(0f, (float)(now - motionStart));
            if (retreating)
            {
                DrawRetreat(elapsed);
                return;
            }
            float actionLength = ActionLength;
            if ((motion == Motion.Hit && elapsed >= HitEnds[^1]) ||
                (motion == Motion.Action && elapsed >= actionLength * actionScale))
            {
                SetMotion(Motion.Idle, now);
                elapsed = 0f;
            }

            float offset = 0f;
            float lift = 0f;
            float light = -1f;
            var tint = Color.white;
            int frame;
            switch (motion)
            {
                case Motion.Death:
                    if (pendingFall && elapsed >= DeathEnds[4])
                    {
                        GameAudio.Play(fallSound);
                        pendingFall = false;
                    }
                    frame = 3 * Columns + FrameAt(DeathEnds, elapsed);
                    break;
                case Motion.Hit:
                    frame = 2 * Columns + FrameAt(HitEnds, elapsed);
                    offset = -Mathf.Sin(elapsed / HitEnds[^1] * Mathf.PI) * 3f;
                    tint = Color.Lerp(new Color(1f, 0.61f, 0.48f), Color.white, Mathf.Clamp01(elapsed / 0.16f));
                    break;
                case Motion.Action:
                    elapsed /= actionScale;
                    bool guarding = SimpleGuard;
                    frame = guarding ? 2 * Columns + AnimationCels.At(elapsed / actionLength)
                        : Columns + FrameAt(actionEnds, elapsed);
                    if (profession == Profession.Warrior && action == ActionKind.Attack)
                        offset = Mathf.Sin(elapsed / actionLength * Mathf.PI) * 4f;
                    float contact = guarding ? 0.16f : impactTime;
                    if (!usesExternalEffect && elapsed >= contact)
                        light = (elapsed - contact) / (actionLength - contact);
                    break;
                default:
                    float cycle = Mathf.Repeat(elapsed, IdleLength);
                    frame = Mathf.FloorToInt(cycle * Columns / IdleLength);
                    lift = -0.45f * (1f - Mathf.Cos(cycle / IdleLength * 2f * Mathf.PI));
                    break;
            }
            ShowFrame(frame);
            if (UsesPortrait)
                PosePortrait(elapsed, actionLength, ref offset, ref lift);
            figure.SetInk(tint);
            figure.style.translate = new Translate(FacesLeft ? -offset : offset, lift, 0f);
            if (effectProgress >= 0f || light >= 0f)
            {
                effectProgress = light < 0 ? -1 : AnimationCels.Phase(light);
                effects.MarkDirtyRepaint();
            }
        }

        private void DrawRetreat(float elapsed)
        {
            float progress = Mathf.Clamp01(elapsed / DeathEnds[^1]);
            if (retreatWalk != null)
            {
                int frame = AnimationCels.Loop(elapsed, 6f / 16f);
                if (displayedFrame != frame || figure.image != retreatWalk)
                {
                    displayedFrame = frame;
                    figure.image = retreatWalk;
                    figure.sourceRect = AnimationCels.RectFor(retreatWalk, frame);
                    figure.MarkDirtyRepaint();
                }
            }
            else
                ShowFrame(0);

            figure.SetInk(Color.white);
            figure.style.scale = new Scale(new Vector3(-1, 1, 1));
            figure.style.translate = new Translate((FacesLeft ? 1 : -1) * 78f * progress, 0);
            figure.style.opacity = 1f - Mathf.SmoothStep(0, 1, progress);
        }

        private void PosePortrait(float elapsed, float actionLength, ref float offset, ref float lift)
        {
            float lean = 0;
            float opacity = 1;
            if (motion == Motion.Action)
            {
                float progress = Mathf.Clamp01(elapsed / actionLength);
                float thrust = Mathf.Sin(progress * Mathf.PI);
                if (action == ActionKind.Attack)
                {
                    offset += thrust * (profession == Profession.Monster ? 11f : 7f);
                    lean = thrust * 7f;
                }
                else
                {
                    lift -= thrust * 4f;
                    lean = -thrust * 5f;
                }
            }
            else if (motion == Motion.Hit)
            {
                lean = -Mathf.Sin(elapsed / HitEnds[^1] * Mathf.PI) * 9f;
            }
            else if (motion == Motion.Death)
            {
                float progress = Mathf.Clamp01(elapsed / DeathEnds[^1]);
                lift += 13f * progress;
                lean = -22f * progress;
                opacity = 1f - progress;
            }
            else if (profession == Profession.Monster)
            {
                lift *= 3f;
            }
            figure.style.rotate = new Rotate(new Angle(FacesLeft ? -lean : lean));
            figure.style.opacity = opacity;
        }

        private static int FrameAt(float[] frameEnds, float elapsed)
        {
            int frame = 0;
            while (frame < frameEnds.Length - 1 && elapsed >= frameEnds[frame])
                frame++;
            float start = frame == 0 ? 0 : frameEnds[frame - 1];
            float subpose = Mathf.InverseLerp(start, frameEnds[frame], elapsed);
            return AnimationCels.At((frame + subpose) / frameEnds.Length);
        }

        private void ShowFrame(int frame)
        {
            if (UsesPortrait)
                return;
            if (displayedFrame == frame)
                return;
            displayedFrame = frame;
            var actionTexture = specialSheet ?? attackSheet;
            bool usingAttack = actionTexture != null && frame >= Columns && frame < 2 * Columns;
            var texture = usingAttack ? actionTexture : Sheet;
            int columns = usingAttack ? AttackColumns : Columns;
            int rows = usingAttack ? AttackRows : Rows;
            int index = usingAttack ? frame - Columns : frame;
            int width = texture.width / columns;
            int height = texture.height / rows;
            figure.image = texture;
            figure.sourceRect = new Rect(index % columns * width, index / columns * height, width, height);
            // Changing the texture region does not invalidate UI Toolkit's cached drawing.
            figure.MarkDirtyRepaint();
        }

        private void DrawActionEffect(MeshGenerationContext context)
        {
            if (effectProgress < 0f)
                return;

            float progress = effectProgress;
            float fade = Mathf.Sin(Mathf.Clamp01(progress * 1.2f + 0.12f) * Mathf.PI);
            var pen = context.painter2D;
            var center = new Vector2(effects.contentRect.width * (FacesLeft ? 0.28f : 0.72f), effects.contentRect.height * 0.52f);
            pen.lineWidth = 1.65f;
            if (action == ActionKind.Shield)
            {
                pen.strokeColor = new Color(1f, 0.77f, 0.37f, fade);
                Curve(center, new Vector2(-9f, -20f), new Vector2(29f, -4f), new Vector2(-4f, 23f));
                return;
            }

            switch (profession)
            {
                case Profession.Warrior:
                    pen.strokeColor = new Color(1f, 0.66f, 0.29f, fade);
                    pen.lineWidth = 2.7f;
                    Curve(center, new Vector2(-10f, -26f), new Vector2(29f, -11f), new Vector2(8f, 24f));
                    break;
                case Profession.Mage:
                    pen.strokeColor = new Color(0.63f, 0.81f, 1f, fade);
                    Spark(center + new Vector2(progress * 16f, -4f), 10f);
                    Spark(center + new Vector2(-10f, 14f), 4f);
                    Spark(center + new Vector2(6f, -20f), 4f);
                    break;
                case Profession.Support:
                    pen.strokeColor = new Color(1f, 0.89f, 0.58f, fade);
                    if (action == ActionKind.Heal)
                    {
                        var middle = new Vector2(effects.contentRect.width * 0.53f, center.y - progress * 13f);
                        Curve(middle, new Vector2(-21f, 9f), new Vector2(0f, 25f), new Vector2(21f, 9f));
                        Spark(middle + new Vector2(-14f, -6f), 5f);
                        Spark(middle + new Vector2(15f, -17f), 6f);
                    }
                    else
                    {
                        Spark(center + new Vector2(progress * 18f, -5f), 9f);
                        Spark(center + new Vector2(-9f, 4f), 4f);
                    }
                    break;
            }

            void Curve(Vector2 origin, Vector2 start, Vector2 handle, Vector2 end)
            {
                pen.BeginPath();
                pen.MoveTo(origin + Facing(start));
                pen.QuadraticCurveTo(origin + Facing(handle), origin + Facing(end));
                pen.Stroke();
            }

            Vector2 Facing(Vector2 point) => FacesLeft ? new Vector2(-point.x, point.y) : point;

            void Spark(Vector2 point, float radius)
            {
                pen.BeginPath();
                pen.MoveTo(point + Vector2.up * radius);
                pen.LineTo(point + Vector2.right * radius * 0.4f);
                pen.LineTo(point + Vector2.down * radius);
                pen.LineTo(point + Vector2.left * radius * 0.4f);
                pen.ClosePath();
                pen.Stroke();
            }
        }
    }
}
