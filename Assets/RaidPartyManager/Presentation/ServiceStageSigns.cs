using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Hanging signs that belong to the scenery are the same signs as the panel's: the same carved walnut faces,
    // and the same hover, press, sway and rise to the beam. The panel sign stays as lettering and hit area,
    // and the board below mirrors its state.
    internal abstract partial class ServiceStage
    {
        private static readonly Color Walnut = new(.36f, .25f, .16f), Iron = new(.23f, .21f, .2f), Hemp = new(.86f, .7f, .5f);
        // The rings are forged a little warmer than the bolts, like the bronzed rings on the panel's signs.
        private static readonly Color RingIron = new(.46f, .34f, .23f);
        private static readonly Color FaceTint = new(.94f, .9f, .86f);
        private static Texture2D hempTwist;
        private static Mesh eyeRing;
        private static readonly Dictionary<Vector3, Mesh> signFaces = new();
        private const float RingRadius = 40, RingTube = 9;

        /// <summary>Slice borders and ring placement of each painted face, in its texture pixels.</summary>
        private readonly struct FaceArt
        {
            internal readonly string File;
            internal readonly int Side, Top, Bottom;
            internal readonly float RingInset;
            private FaceArt(string file, int side, int top, int bottom, float ringInset)
            { File = file; Side = side; Top = top; Bottom = bottom; RingInset = ringInset; }
            internal static FaceArt For(SignFace face) => face == SignFace.Square
                ? new FaceArt("SignSquareBoard", 100, 104, 108, 74)
                : new FaceArt("SignPlankBoard", 128, 84, 98, 78);
        }

        /// <summary>A flown board that mirrors a panel sign.</summary>
        protected sealed class FlownSign
        {
            internal HangingSign Target;
            internal Transform Board;
            internal float Height;
            internal Vector3[] Eyes;
            internal MeshRenderer[] Ropes;
            internal float RopeThickness;
            internal readonly List<(MeshRenderer renderer, Color tint)> Painted = new();
            private float lift, glow = -1, shown = -1, dim = -1;
            private double retractAt = -1;
            private Action retracted;

            internal void Paint(MeshRenderer renderer, Color tint) => Painted.Add((renderer, tint));

            internal void Retract(Action chosen)
            {
                if (retractAt >= 0) return;
                retractAt = Time.realtimeSinceStartupAsDouble;
                retracted = chosen;
            }

            /// <summary>Poses the board around its resting place and runs its ropes up to the given height.</summary>
            internal void Pose(ServiceStage owner, Vector3 rest, Quaternion turn, float ropeTop)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
                float h = Height * Board.localScale.y;
                bool lit = Target != null && Target.Lit, pressed = Target != null && Target.Pressed;
                bool enabled = Target == null || Target.enabledInHierarchy;
                // The panel sign lifts two pixels on hover and sinks two when pressed; the board follows by its height.
                lift = Mathf.MoveTowards(lift, pressed ? -.035f : lit ? .035f : 0, dt * .5f);
                float bright = Mathf.MoveTowards(Mathf.Max(glow, 0), lit ? 1 : 0, dt * 6);
                float rise = 0, lettering = 1;
                if (retractAt >= 0)
                {
                    float t = (float)(Time.realtimeSinceStartupAsDouble - retractAt);
                    float above = ropeTop - rest.y + h * 1.2f;
                    // The same cue as the panel's signs: a small dip that tensions the ropes, then a smooth rise.
                    if (t < .1f) rise = -.12f * h * Mathf.Sin(t / .1f * Mathf.PI * .5f);
                    else if (t < .57f) rise = Mathf.Lerp(-.12f * h, above, Mathf.SmoothStep(0, 1, (t - .1f) / .47f));
                    else if (t < 1.05f) rise = above;
                    else if (t < 1.6f)
                    {
                        // If the scene stays, the board drops back and bounces once on its ropes.
                        float d = (t - 1.05f) / .55f;
                        rise = above * (1 - d * d) + Mathf.Sin(d * Mathf.PI) * -.06f * h * d;
                    }
                    else retractAt = -1;
                    // The panel lettering stays at rest, so it only returns once the board is home again.
                    lettering = t < .12f ? 1 - t / .12f : t > 1.54f ? Mathf.Clamp01((t - 1.54f) / .1f) : 0;
                    if (t >= .63f && retracted != null)
                    {
                        var chosen = retracted;
                        retracted = null;
                        chosen();
                    }
                }
                if (Target != null && Mathf.Abs(lettering - shown) > .001f)
                {
                    shown = lettering;
                    if (lettering >= 1) Target.style.opacity = StyleKeyword.Null;
                    else Target.style.opacity = lettering;
                }

                // Swing about the middle of the top edge, between the rings. The panel turns clockwise for positive angles.
                var swing = turn * Quaternion.Euler(0, 0, -(Target?.Sway ?? 0));
                var pivot = rest + turn * new Vector3(0, h / 2, 0);
                Board.SetPositionAndRotation(pivot + swing * new Vector3(0, -h / 2, 0) + Vector3.up * (lift * h + rise), swing);

                float shade = enabled ? 1 : .6f;
                if (bright != glow || shade != dim)
                {
                    glow = bright; dim = shade;
                    foreach (var (renderer, tint) in Painted)
                    {
                        renderer.GetPropertyBlock(owner.properties);
                        owner.properties.SetColor("_Color", tint * Color.Lerp(Color.white, new Color(1.22f, 1.15f, 1.02f), bright) * shade);
                        renderer.SetPropertyBlock(owner.properties);
                    }
                }
                for (int i = 0; i < Ropes.Length; i++)
                {
                    var eye = Board.TransformPoint(Eyes[i]);
                    bool slack = ropeTop - eye.y < .02f;
                    Ropes[i].gameObject.SetActive(Board.gameObject.activeSelf && !slack);
                    if (!slack) owner.Rig(Ropes[i], eye, new Vector3(eye.x, ropeTop, eye.z), RopeThickness);
                }
            }
        }

        /// <summary>Builds a hanging board centred on its root, with eye-rings and ropes ready to rig.</summary>
        protected FlownSign HangingBoard(string name, string body, SignFace face, float width, float height, float depth,
            string rope, float ropeThickness)
        {
            var art = FaceArt.For(face);
            var root = new GameObject(name).transform;
            root.SetParent(stage, false);
            var texture = Resources.Load<Texture2D>("HangingSign/" + art.File);
            float pixel = height / texture.height;
            var sign = new FlownSign { Board = root, Height = height, RopeThickness = ropeThickness, Eyes = new Vector3[2],
                Ropes = new MeshRenderer[2] };

            // The slab is a little smaller than the face so the face's rounded corners stay clean.
            sign.Paint(Attach(Box(body, Vector3.zero, new Vector3(width * .985f, height * .955f, depth), this.art.timber, Walnut),
                root, Vector3.zero), Walnut);
            var front = Attach(Shape("Carved walnut face", PrimitiveType.Quad, Vector3.zero, Vector3.one, texture,
                FaceTint, Surface.Solid), root, new Vector3(0, 0, -depth / 2 - .003f));
            front.GetComponent<MeshFilter>().sharedMesh = SlicedFace(art, texture, width, height, pixel);
            sign.Paint(front, FaceTint);

            if (eyeRing == null) eyeRing = Torus(RingRadius, RingTube);
            float ringY = height / 2 + RingRadius * pixel * .82f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                float x = side * (width / 2 - art.RingInset * pixel);
                var ring = Attach(Shape("Iron eye-ring", PrimitiveType.Quad, Vector3.zero, Vector3.one, null, RingIron, Surface.Solid),
                    root, new Vector3(x, ringY, -depth * .2f));
                ring.GetComponent<MeshFilter>().sharedMesh = eyeRing;
                ring.transform.localScale = Vector3.one * pixel;
                // Turned a little on the bolt so the ring reads as round iron rather than a flat loop.
                ring.transform.localRotation = Quaternion.Euler(0, side * 24, 0);
                sign.Paint(ring, RingIron);
                var plate = Attach(Box("Ring bolt plate", Vector3.zero, new Vector3(26 * pixel, 30 * pixel, depth * .5f), null, Iron),
                    root, new Vector3(x, height / 2 - 4 * pixel, -depth * .2f));
                sign.Paint(plate, Iron);
                sign.Eyes[i] = new Vector3(x, ringY + (RingRadius - RingTube) * pixel, -depth * .2f);
                if (hempTwist == null) hempTwist = HempTwist();
                sign.Ropes[i] = Shape(rope, PrimitiveType.Cylinder, Vector3.zero, new Vector3(ropeThickness, .5f, ropeThickness),
                    hempTwist, Hemp, Surface.Solid);
            }
            return sign;
        }

        /// <summary>Stretches a hemp line between two points and keeps the twist at a constant pitch.</summary>
        protected void Rig(MeshRenderer line, Vector3 from, Vector3 to, float thickness)
        {
            var span = to - from;
            line.transform.SetPositionAndRotation((from + to) / 2, Quaternion.FromToRotation(Vector3.up, span));
            line.transform.localScale = new Vector3(thickness, span.magnitude / 2, thickness);
            line.GetPropertyBlock(properties);
            properties.SetVector("_MainTex_ST", new Vector4(1, span.magnitude / (thickness * 2.2f), 0, 0));
            line.SetPropertyBlock(properties);
        }

        protected static MeshRenderer Attach(MeshRenderer part, Transform parent, Vector3 position)
        {
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            return part;
        }

        // Nine-slice the painted face so the corners and frame keep their proportions at any board size.
        private static Mesh SlicedFace(FaceArt art, Texture2D texture, float width, float height, float pixel)
        {
            var key = new Vector3(Mathf.Round(width * 1000), Mathf.Round(height * 1000), art.Side);
            if (signFaces.TryGetValue(key, out var cached) && cached != null) return cached;
            float w = texture.width, h = texture.height;
            float[] xs = { -width / 2, -width / 2 + art.Side * pixel, width / 2 - art.Side * pixel, width / 2 };
            float[] ys = { -height / 2, -height / 2 + art.Bottom * pixel, height / 2 - art.Top * pixel, height / 2 };
            float[] us = { 0, art.Side / w, 1 - art.Side / w, 1 };
            float[] vs = { 0, art.Bottom / h, 1 - art.Top / h, 1 };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    vertices.Add(new Vector3(xs[i], ys[j], 0));
                    uv.Add(new Vector2(us[i], vs[j]));
                }
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    int a = j * 4 + i;
                    triangles.AddRange(new[] { a, a + 4, a + 5, a, a + 5, a + 1 });
                }
            var mesh = new Mesh { name = "Sliced sign face" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            signFaces[key] = mesh;
            return mesh;
        }

        private static Mesh Torus(float radius, float tube)
        {
            const int around = 28, sides = 10;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int a = 0; a <= around; a++)
            {
                float u = a * Mathf.PI * 2 / around;
                var spoke = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);
                for (int s = 0; s <= sides; s++)
                {
                    float v = s * Mathf.PI * 2 / sides;
                    vertices.Add(spoke * (radius + tube * Mathf.Cos(v)) + Vector3.forward * (tube * Mathf.Sin(v)));
                    uv.Add(new Vector2((float)a / around, (float)s / sides));
                }
            }
            for (int a = 0; a < around; a++)
                for (int s = 0; s < sides; s++)
                {
                    int i = a * (sides + 1) + s, j = i + sides + 1;
                    triangles.AddRange(new[] { i, j, j + 1, i, j + 1, i + 1 });
                }
            var mesh = new Mesh { name = "Forged eye-ring" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Three strands laid diagonally, with dark grooves between them.
        private static Texture2D HempTwist()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Hemp twist", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float strand = Mathf.Repeat((x + y) / (size / 3f), 1);
                    float body = Mathf.Sin(strand * Mathf.PI);
                    float fibre = .9f + .1f * Mathf.Sin((x * 7 + y * 3) * .9f);
                    byte level = (byte)(255 * Mathf.Clamp01((.38f + .62f * Mathf.Pow(body, .6f)) * fibre));
                    pixels[y * size + x] = new Color32(level, level, level, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
