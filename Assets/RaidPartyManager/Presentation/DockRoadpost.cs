using System.Collections.Generic;
using UnityEngine;

namespace RaidPartyManager.Presentation
{
    // Each carved marker pivots at its own foot when the turntable folds.
    internal sealed class DockRoadpost
    {
        internal readonly Transform Foot;
        internal bool Travelling { get; set; }
        private readonly int direction;
        private readonly Material paint;
        private readonly MaterialPropertyBlock ink = new();
        private readonly Texture2D stone;
        private readonly Mesh plank;
        private const float Width = 1.8f, Height = .80f;

        internal DockRoadpost(int direction, Texture2D stone, StageLighting lighting)
        {
            this.direction = direction;
            Foot = new GameObject(direction < 0 ? "Left stone signpost" : "Right stone signpost").transform;
            this.stone = stone;
            paint = new Material(Shader.Find("Raid Party Manager/Service Scenery"));
            lighting.Apply(paint);
            plank = CarveStone();
            Part("Foot", PrimitiveType.Cube, Foot, new Vector3(0, .13f, 0), new Vector3(1.0f,.26f,.52f), stone, new Color(.63f,.68f,.73f));
            Part("Post", PrimitiveType.Cube, Foot, new Vector3(0,.81f,0), new Vector3(.46f,1.48f,.34f), stone, new Color(.78f,.81f,.84f));
        }

        internal void Place(Camera eye, Bounds deck, float height)
        {
            // Keep the whole foot on the boards, just behind the joint with the apron.
            float z = deck.min.z + .27f;
            float depth = z - eye.transform.position.z;
            float x = eye.ViewportToWorldPoint(new Vector3(direction < 0 ? .25f : .75f, .5f, depth)).x;
            Foot.position = new Vector3(x, deck.max.y + height, z);
            Foot.localScale = Vector3.one * .43f;
        }

        internal Transform AddArrow(Texture emblem)
        {
            bool left = direction < 0;
            var board = new GameObject(left ? "Left arrow" : "Right arrow").transform;
            board.SetParent(Foot, false);
            board.localPosition = new Vector3(0, 1.51f, -.13f);
            board.localRotation = Quaternion.Euler(0, left ? -6 : 6, left ? -3 : 3);
            var face = new GameObject("Carved stone arrow");
            face.transform.SetParent(board, false);
            face.transform.localRotation = Quaternion.Euler(0, left ? 180 : 0, 0);
            face.AddComponent<MeshFilter>().sharedMesh = plank;
            Dress(face.AddComponent<MeshRenderer>(), stone, new Color(.91f,.94f,.99f));
            Part("Route emblem", PrimitiveType.Quad, board, new Vector3(direction * .06f,0,-.146f),
                new Vector3(.63f,.63f,1), emblem, Color.white);
            return board;
        }

        // Project the rectangular portion of the arrow. The point falls inside the same clickable bounds.
        internal static Rect BoundsInViewport(Transform board, Camera eye)
        {
            var bounds = Rect.MinMaxRect(1, 1, 0, 0);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                {
                    var point = eye.WorldToViewportPoint(board.TransformPoint(new Vector3(i * Width / 2,j * Height / 2,-.15f)));
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.y),
                        Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.y));
                }
            return bounds;
        }

        internal void Highlight(Transform board, bool selected)
        {
            Dress(board.GetComponentInChildren<MeshRenderer>(true), stone,
                selected ? new Color(1.32f,1.24f,1.07f) : new Color(.91f,.94f,.99f));
        }

        internal void Dispose()
        {
            if (Foot != null) { Foot.gameObject.SetActive(false); Object.Destroy(Foot.gameObject); }
            Object.Destroy(plank);
            Object.Destroy(paint);
        }

        private void Dress(MeshRenderer renderer, Texture image, Color tint)
        {
            renderer.sharedMaterial = paint;
            ink.SetTexture("_MainTex", image != null ? image : Texture2D.whiteTexture);
            ink.SetColor("_Color", tint);
            ink.SetFloat("_Surface", 1);
            renderer.SetPropertyBlock(ink);
        }

        private GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 point, Vector3 size, Texture image, Color tint)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = point;
            part.transform.localScale = size;
            Object.Destroy(part.GetComponent<Collider>());
            Dress(part.GetComponent<MeshRenderer>(), image, tint);
            return part;
        }

        private static Mesh CarveStone()
        {
            var outline = new[] { new Vector2(-Width/2,-Height/2+.08f), new Vector2(-Width/2+.09f,-Height/2), new Vector2(.50f,-Height/2),
                new Vector2(Width/2,0), new Vector2(.50f,Height/2), new Vector2(-Width/2+.08f,Height/2), new Vector2(-Width/2,Height/2-.10f) };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            void Face(params Vector3[] corners)
            {
                int first = vertices.Count;
                vertices.AddRange(corners);
                foreach (var v in corners) uv.Add(new Vector2(v.x / Width + .5f, v.y / Height + .5f + v.z * .3f));
                for (int j = 2; j < corners.Length; j++) triangles.AddRange(new[] { first, first+j-1, first+j });
            }
            var front = new Vector3[outline.Length];
            var back = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                front[i] = new Vector3(outline[i].x, outline[i].y, -.14f);
                back[i] = new Vector3(outline[i].x, outline[i].y, .09f);
            }
            Face(back);
            for (int i = 0; i < outline.Length; i++)
            {
                int j = (i + 1) % outline.Length;
                Face(front[i], front[j], back[j], back[i]);
            }
            System.Array.Reverse(front);
            Face(front);
            var mesh = new Mesh { name = "Chipped stone waymarker" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
