using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage
    {
        private const float SignWidth = .81f, SignHeight = .47f;
        private readonly List<ShopSign> shopSigns = new();
        private Mesh signArrow;

        private sealed class ShopSign
        {
            public HangingSign Target;
            public Transform Board;
            public Transform[] Ropes;
            public Vector3 Rest;
        }

        internal void MountShopSigns(VisualElement root)
        {
            foreach (var target in root.Query<HangingSign>().ToList())
            {
                bool rear = shopSigns.Count % 2 == 0;
                float side = shopSigns.Count == 0 ? 4.77f : 5.36f;
                Vector3 rest = new(side, rear ? 3.01f : 2.83f, rear ? .64f : -1.85f);
                var board = new GameObject("Shop route sign " + target.name).transform;
                board.SetParent(stage, false);
                var plate = Box("Carved plank", Vector3.zero, new Vector3(SignWidth, SignHeight, .105f), art.timber, WheelIron);
                Part(plate, board, Vector3.zero);
                var face = Box("Raised plank face", Vector3.zero, new Vector3(SignWidth - .055f, SignHeight - .060f, .028f),
                    art.timber, new Color32(154, 100, 58, 255));
                Part(face, board, new Vector3(0, 0, -.061f));
                foreach (float x in new[] { -.33f, .33f })
                {
                    var rivet = Shape("Brass rivet", PrimitiveType.Sphere, Vector3.zero, new Vector3(.04f, .04f, .023f),
                        null, WheelBrass, Surface.Solid);
                    Part(rivet, board, new Vector3(x, 0, -.086f));
                }
                if (target.Q<Image>("route-node-icon")?.image is Texture2D artwork)
                    Part(Flat("Route emblem", Vector3.zero, .33f, .33f, artwork), board, new Vector3(0, 0, -.086f));
                else
                {
                    signArrow ??= MakeSignArrow();
                    var arrow = Flat("Brass direction inlay", Vector3.zero, 1, 1, null);
                    arrow.GetComponent<MeshFilter>().sharedMesh = signArrow;
                    Part(arrow, board, new Vector3(0, 0, -.088f));
                    SetTint(arrow, WheelBrass);
                }
                var ropes = new Transform[2];
                for (int i = 0; i < ropes.Length; i++)
                {
                    var rope = Shape("Shop sign braided line", PrimitiveType.Cylinder, rest, Vector3.one,
                        art.bark, new Color32(184, 135, 85, 255), Surface.Solid, new Vector2(2, 12));
                    Dressing.Flown.Add(rope);
                    ropes[i] = rope.transform;
                }
                target.MountInScenery();
                shopSigns.Add(new ShopSign { Target = target, Board = board, Ropes = ropes, Rest = rest });
            }
            PoseShopSigns(root);
        }

        internal void PoseShopSigns(VisualElement root)
        {
            var frame = root.worldBound;
            if (!(frame.height > 0 && frame.width > 0)) return;
            foreach (var sign in shopSigns)
            {
                if (sign.Target.parent == null) continue;
                bool visible = RecruitmentFocus < .44f && counterFocus < .44f;
                sign.Board.gameObject.SetActive(visible);
                foreach (var rope in sign.Ropes) rope.gameObject.SetActive(visible);
                float depth = sign.Rest.z - camera.transform.position.z;
                float metresPerPixel = depth * 2 * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / frame.height;
                float pull = sign.Target.ScenicPull;
                sign.Board.SetPositionAndRotation(sign.Rest - Vector3.up * (pull * metresPerPixel),
                    Quaternion.Euler(0, 13, pull * -.075f));
                // The rigging descends from above the shot, leaving the overhead scenery clear.
                var aboveFrame = camera.WorldToViewportPoint(sign.Rest);
                aboveFrame.y = 1.03f;
                float hangerY = Mathf.Max(sign.Rest.y + .5f, camera.ViewportToWorldPoint(aboveFrame).y);
                for (int i = 0; i < sign.Ropes.Length; i++)
                {
                    float x = i == 0 ? -.23f : .23f;
                    Vector3 start = new(sign.Rest.x + x, hangerY, sign.Rest.z);
                    Vector3 tip = sign.Board.TransformPoint(new Vector3(x, SignHeight * .47f, 0));
                    Vector3 span = start - tip;
                    var rope = sign.Ropes[i];
                    rope.SetPositionAndRotation((tip + start) * .5f, Quaternion.FromToRotation(Vector3.up, span));
                    rope.localScale = new Vector3(.023f, span.magnitude * .5f, .023f);
                }
                Vector2 a = Project(sign.Rest + new Vector3(-SignWidth * .54f, SignHeight * .55f, -.07f));
                Vector2 b = Project(sign.Rest + new Vector3(SignWidth * .54f, -SignHeight * .55f, -.07f));
                Vector2 local = sign.Target.parent.WorldToLocal(a);
                sign.Target.style.left = local.x;
                sign.Target.style.right = StyleKeyword.Auto;
                sign.Target.style.top = local.y;
                sign.Target.style.width = Mathf.Max(0, b.x - a.x);
                sign.Target.style.height = Mathf.Max(0, b.y - a.y);
            }

            Vector2 Project(Vector3 point)
            {
                var view = camera.WorldToViewportPoint(point);
                return frame.min + Vector2.Scale(frame.size, new Vector2(view.x, 1 - view.y));
            }
        }

        private static Mesh MakeSignArrow()
        {
            var mesh = new Mesh { name = "Brass sign arrow" };
            mesh.vertices = new[] { new Vector3(-.23f, -.060f), new Vector3(.028f, -.060f), new Vector3(.028f, -.124f),
                new Vector3(.225f, 0), new Vector3(.028f, .124f), new Vector3(.028f, .060f), new Vector3(-.23f, .060f) };
            mesh.triangles = new[] { 0, 6, 5, 0, 5, 1, 2, 4, 3 };
            mesh.uv = new Vector2[7];
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
