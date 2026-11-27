using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Deform the original idle pixels so robes, carried weapons, and faces keep their art.
    internal sealed class EntranceWalkPose : VisualElement
    {
        private const int Columns = 14;
        private const int Rows = 18;
        private readonly Texture2D sheet;
        private readonly bool facesLeft;
        private readonly float stride;
        private readonly float lift;
        private int frame = -1;
        private readonly StageActorImage paper;

        public EntranceWalkPose(Texture2D sheet, Profession profession, bool facesLeft, BattleStage stage)
        {
            this.sheet = sheet;
            this.facesLeft = facesLeft;
            (stride, lift) = MotionFor(profession);
            pickingMode = PickingMode.Ignore;
            if (stage == null)
                generateVisualContent += Draw;
            else
            {
                paper = new StageActorImage(stage, inMotion: true, keepsStageFocus: facesLeft)
                {
                    name = "walking-paper",
                    image = sheet,
                    sourceRect = new Rect(0, 0, sheet.width / AnimationCels.Count, sheet.height / 4),
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore,
                    Walk = new Vector4(1, 0, facesLeft ? -1 : 1, stride)
                };
                PrototypeStyle.Fill(paper);
                Add(paper);
            }
        }

        internal static (float Stride, float Lift) MotionFor(Profession profession) => profession switch
        {
            Profession.Archer => (0.105f, 0.074f),
            Profession.Mage => (0.078f, 0.052f),
            Profession.Support => (0.068f, 0.048f),
            _ => (0.084f, 0.062f)
        };

        public void ShowFrame(int index)
        {
            if (frame == index) return;
            frame = index;
            if (paper != null)
            {
                float phase = index * 2 * Mathf.PI / AnimationCels.Count;
                paper.Walk = new Vector4(Mathf.Cos(phase), Mathf.Sin(phase), facesLeft ? -1 : 1, stride);
            }
            else
                MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Vector2 size = contentRect.size;
            if (frame < 0 || size.x <= 0 || size.y <= 0)
                return;

            // Contact, passing, and lift alternate between the two legs over the walk cycle.
            float phase = frame * 2f * Mathf.PI / AnimationCels.Count;
            float footSwing = Mathf.Cos(phase);
            float footLift = Mathf.Sin(phase);
            float rise = Mathf.Abs(footLift);
            float direction = facesLeft ? -1f : 1f;
            var mesh = context.Allocate((Columns + 1) * (Rows + 1), Columns * Rows * 6, sheet);

            for (int row = 0; row <= Rows; row++)
            {
                float y = row / (float)Rows;
                float knees = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.53f, 0.91f, y));
                float upperBody = 1f - y;
                for (int column = 0; column <= Columns; column++)
                {
                    float x = column / (float)Columns;
                    float lane = facesLeft ? 1f - x : x;
                    float leg = Mathf.SmoothStep(-1f, 1f, Mathf.InverseLerp(0.35f, 0.62f, lane));
                    float step = leg * footSwing * stride * knees;
                    float raisedFoot = Mathf.Max(0, leg * footLift) * lift * knees;
                    Vector2 point = new Vector2(
                        size.x * (x + direction * (step + 0.045f * upperBody - 0.025f * upperBody * footSwing)),
                        size.y * (y - raisedFoot - 0.025f * rise * (1f - knees)));
                    float frameX = (0.5f + x * (sheet.width / AnimationCels.Count - 1)) / sheet.width;
                    float frameY = 1f - (0.5f + y * (sheet.height / 4 - 1)) / sheet.height;
                    mesh.SetNextVertex(new Vertex
                    {
                        position = new Vector3(point.x, point.y, Vertex.nearZ),
                        tint = Color.white,
                        uv = new Vector2(frameX, frameY)
                    });
                }
            }

            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    int top = row * (Columns + 1) + column;
                    int bottom = top + Columns + 1;
                    mesh.SetNextIndex((ushort)top);
                    mesh.SetNextIndex((ushort)(top + 1));
                    mesh.SetNextIndex((ushort)bottom);
                    mesh.SetNextIndex((ushort)(top + 1));
                    mesh.SetNextIndex((ushort)(bottom + 1));
                    mesh.SetNextIndex((ushort)bottom);
                }
        }
    }
}
