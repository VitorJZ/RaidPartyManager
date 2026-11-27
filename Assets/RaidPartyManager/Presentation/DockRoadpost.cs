using System.Collections.Generic;
using UnityEngine;

namespace RaidPartyManager.Presentation
{
    // A carved stone waymarker. Each one pivots at its own foot when the turntable folds.
    internal sealed class DockRoadpost
    {
        internal readonly Transform Foot;
        internal bool Travelling { get; set; }
        private readonly int direction;
        // Stone is shaded like the house timber: baked vertex colour over a grain texture, lit from above.
        private readonly Material stone, paint, shade;
        private readonly MaterialPropertyBlock ink = new();
        private readonly MeshRenderer shadow;
        private readonly List<MeshRenderer> slab = new();
        private Transform board;
        private float hover, hoverTarget, landedAt = -1;
        private bool placed;

        // Arrow slab size in the foot's local units. The clickable area is the slab's rectangle, barbs included.
        private const float Width = 1.9f, Height = .86f, Thickness = .2f, Bevel = .05f;
        private const float BodyHalf = .31f, Neck = .35f, Notch = .13f, MedallionX = -.3f;
        private const float ArrowHeight = 1.5f, Reach = .06f;
        private static readonly Color Moss = new(.27f, .35f, .2f), Cool = new(.43f, .41f, .52f), Warm = new(.53f, .48f, .5f),
            Lichen = new(.7f, .7f, .6f);
        private static Texture2D grain;
        // Shared across markers. Compared with Unity's null, since play mode can end without a domain reload.
        private static Mesh plinth, step, pillar, capstone, cap, ring, disc;
        private static readonly Mesh[] arrows = new Mesh[2];

        internal DockRoadpost(int direction, StageLighting lighting)
        {
            this.direction = direction;
            Foot = new GameObject(direction < 0 ? "Left stone signpost" : "Right stone signpost").transform;
            if (grain == null) grain = Resources.Load<Texture2D>("Waypost/CarvedStone");
            stone = new Material(Shader.Find("RaidPartyManager/Dock Playhouse"));
            if (grain != null) stone.SetTexture("_Grain", grain);
            lighting.Apply(stone);
            paint = new Material(Shader.Find("Raid Party Manager/Service Scenery"));
            lighting.Apply(paint);
            shade = new Material(paint);
            shade.SetFloat("_DepthWrite", 0);
            if (plinth == null) plinth = RoundedBox(new Vector3(1.04f, .18f, .58f), .035f, 1, .84f, 1f);
            if (step == null) step = RoundedBox(new Vector3(.78f, .13f, .46f), .03f, 1, .9f, .7f);
            if (pillar == null) pillar = RoundedBox(new Vector3(.42f, 1.64f, .32f), .04f, .88f, 1f, .45f);
            if (capstone == null) capstone = RoundedBox(new Vector3(.6f, .12f, .46f), .035f, 1, 1.03f, 0);
            if (cap == null) cap = RoundedBox(new Vector3(.44f, .1f, .34f), .045f, .8f, .96f, 0);

            // The foot slab must stay level on the boards, so impacts squash the marker rather than tilt it.
            Solid("Foot", Foot, plinth, new Vector3(0, .09f, 0));
            Solid("Plinth step", Foot, step, new Vector3(0, .245f, 0));
            Solid("Post", Foot, pillar, new Vector3(0, 1.13f, 0));
            Solid("Capstone", Foot, capstone, new Vector3(0, 2.01f, 0));
            Solid("Cap", Foot, cap, new Vector3(0, 2.115f, 0));

            var contact = Quad("Contact shadow", Foot, new Vector3(0, .004f, .02f), new Vector3(1.7f, .85f, 1));
            contact.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow = contact.GetComponent<MeshRenderer>();
            shadow.sharedMaterial = shade;
            Shadow(0);
        }

        internal void Place(Camera eye, Bounds deck, float height)
        {
            // Keep the whole foot on the boards, just behind the joint with the apron.
            float z = deck.min.z + .27f;
            float depth = z - eye.transform.position.z;
            float x = eye.ViewportToWorldPoint(new Vector3(direction < 0 ? .25f : .75f, .5f, depth)).x;
            Foot.position = new Vector3(x, deck.max.y + height, z);
            Shadow(Mathf.Clamp01(1 - height / 2.4f));
            float now = Time.unscaledTime;
            // Markers rebuilt on a set where they already stand do not land again.
            if (height <= 0 && landedAt < 0) landedAt = placed ? now : now - 10;
            placed = true;
            float t = landedAt < 0 ? -1 : now - landedAt;
            bool settling = t is >= 0 and < 1f;
            // The stone squats on impact and the slab rings briefly on its mounting.
            float squash = settling ? .055f * Mathf.Exp(-t * 9f) * Mathf.Cos(t * 24f) : 0;
            Foot.localScale = new Vector3(1 + squash * .5f, 1 - squash, 1 + squash * .5f) * .43f;
            if (board == null) return;
            float previous = hover;
            hover = Mathf.MoveTowards(hover, hoverTarget, Time.unscaledDeltaTime * 6f);
            float ease = Mathf.SmoothStep(0, 1, hover);
            float ringing = settling ? 2.2f * Mathf.Exp(-t * 6f) * Mathf.Sin(t * 21f) : 0;
            board.localPosition = new Vector3(direction * (Reach + .06f * ease), ArrowHeight + .025f * ease, -.19f - .03f * ease);
            board.localRotation = Quaternion.Euler(0, direction * 6, direction * (2 + 1.5f * ease) + ringing);
            if (hover == previous) return;
            // Raising the house level above one warms and brightens the carved slab.
            ink.Clear();
            ink.SetFloat("_HouseLight", 1 + .3f * ease);
            foreach (var renderer in slab) renderer.SetPropertyBlock(ink);
        }

        internal Transform AddArrow(Texture emblem)
        {
            bool left = direction < 0;
            board = new GameObject(left ? "Left arrow" : "Right arrow").transform;
            board.SetParent(Foot, false);
            board.localPosition = new Vector3(direction * Reach, ArrowHeight, -.19f);
            board.localRotation = Quaternion.Euler(0, direction * 6, direction * 2);
            int side = left ? 0 : 1;
            if (arrows[side] == null) arrows[side] = ArrowSlab(left ? -1 : 1);
            if (ring == null) ring = MedallionRing(.2f, .25f);
            if (disc == null) disc = Disc(.205f);
            var centre = new Vector3(direction * MedallionX, 0, -Thickness / 2);
            slab.Add(Solid("Carved stone arrow", board, arrows[side], Vector3.zero));
            slab.Add(Solid("Medallion ring", board, ring, centre));
            slab.Add(Solid("Medallion recess", board, disc, centre + Vector3.back * .002f));
            var inlay = Quad("Route emblem", board, centre + Vector3.back * .008f, new Vector3(.34f, .34f, 1)).GetComponent<MeshRenderer>();
            inlay.sharedMaterial = paint;
            ink.Clear();
            ink.SetTexture("_MainTex", emblem != null ? emblem : Texture2D.whiteTexture);
            ink.SetColor("_Color", new Color(1f, .95f, .86f));
            ink.SetFloat("_Surface", 1);
            inlay.SetPropertyBlock(ink);
            return board;
        }

        // Project the slab's rectangle. The arrow's point and barbs fall inside the same clickable bounds.
        internal static Rect BoundsInViewport(Transform board, Camera eye)
        {
            var bounds = Rect.MinMaxRect(1, 1, 0, 0);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                {
                    var point = eye.WorldToViewportPoint(board.TransformPoint(new Vector3(i * Width / 2, j * Height / 2, -Thickness / 2)));
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.y),
                        Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.y));
                }
            return bounds;
        }

        internal void Highlight(bool selected) => hoverTarget = selected ? 1 : 0;

        internal void Dispose()
        {
            if (Foot != null) { Foot.gameObject.SetActive(false); Object.Destroy(Foot.gameObject); }
            Object.Destroy(stone);
            Object.Destroy(paint);
            Object.Destroy(shade);
        }

        private void Shadow(float strength)
        {
            ink.Clear();
            ink.SetTexture("_MainTex", Texture2D.whiteTexture);
            ink.SetColor("_Color", new Color(1, 1, 1, .7f * strength * strength));
            ink.SetFloat("_Surface", 3);
            shadow.SetPropertyBlock(ink);
        }

        private MeshRenderer Solid(string name, Transform parent, Mesh mesh, Vector3 point)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = point;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = stone;
            return renderer;
        }

        private static GameObject Quad(string name, Transform parent, Vector3 point, Vector3 size)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Quad);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = point;
            part.transform.localScale = size;
            Object.Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        private static float Hash(Vector3 p) => Mathf.Repeat(Mathf.Sin(Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f, 1);

        /// <summary>Broad blotches of warmer and cooler stone, like a weathered slab seen from the stalls.</summary>
        private static float Mottle(Vector3 p) => Mathf.Clamp01(.5f
            + .24f * Mathf.Sin(p.x * 6.1f + p.y * 3.3f + 1.7f) * Mathf.Sin(p.y * 4.7f - p.z * 5.9f + .4f)
            + .17f * Mathf.Sin(p.x * 14.3f - p.y * 10.9f + p.z * 8.7f) * Mathf.Cos(p.y * 12.1f + p.x * 4.2f)
            + .1f * (Hash(p * 9.7f) - .5f));

        /// <summary>Weathered stone colour: darker low down and on undersides, paler on worn edges, moss in the low joints.</summary>
        private static Color Weather(Vector3 p, Vector3 n, float fromBottom, float moss, float tone)
        {
            float edge = 1 - Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z)));
            float shade = Mathf.Lerp(.72f, 1, Mathf.Clamp01(fromBottom / .3f)) * (n.y < -.5f ? .7f : 1);
            shade *= 1 + .45f * edge * Mathf.Clamp01(n.y + .6f);
            var colour = Color.Lerp(Cool, Warm, Mottle(p)) * shade * tone;
            if (Hash(p * 17.3f) > .94f) colour = Color.Lerp(colour, Lichen * tone, .55f);
            float growth = moss * Mathf.Clamp01(1 - fromBottom / .26f) * (n.y > -.3f ? 1 : 0) * (Hash(p * 11.7f) > .35f ? 1 : .35f);
            colour = Color.Lerp(colour, Moss * (.8f + .4f * Hash(p * 5.1f)), growth);
            colour.a = 1;
            return colour;
        }

        /// <summary>A box whose edges are rounded with radius r. A taper below 1 narrows the top.</summary>
        private static Mesh RoundedBox(Vector3 size, float r, float taper, float tone, float moss)
        {
            var h = size / 2;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            // Rounded edges get two rows; flat faces are divided finely enough to carry the baked weathering.
            float[] Stops(float half)
            {
                var stops = new List<float> { -half, -half + r * .3f };
                int cells = Mathf.Max(1, Mathf.CeilToInt((2 * (half - r)) / .07f));
                for (int k = 0; k <= cells; k++) stops.Add(Mathf.Lerp(-half + r, half - r, (float)k / cells));
                stops.Add(half - r * .3f); stops.Add(half);
                return stops.ToArray();
            }
            for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    var su = Stops(h[u]); var sv = Stops(h[v]);
                    int first = vertices.Count;
                    for (int i = 0; i < su.Length; i++)
                        for (int j = 0; j < sv.Length; j++)
                        {
                            var p = Vector3.zero;
                            p[axis] = sign * h[axis]; p[u] = su[i]; p[v] = sv[j];
                            var inner = new Vector3(Mathf.Clamp(p.x, -h.x + r, h.x - r), Mathf.Clamp(p.y, -h.y + r, h.y - r),
                                Mathf.Clamp(p.z, -h.z + r, h.z - r));
                            var n = (p - inner).normalized;
                            var q = inner + n * r;
                            float narrow = Mathf.Lerp(1, taper, (q.y + h.y) / size.y);
                            q.x *= narrow; q.z *= narrow;
                            vertices.Add(q); normals.Add(n); colours.Add(Weather(q, n, q.y + h.y, moss, tone));
                        }
                    int columns = sv.Length;
                    for (int i = 0; i < su.Length - 1; i++)
                        for (int j = 0; j < columns - 1; j++)
                        {
                            int a = first + i * columns + j, b = a + columns, c = b + 1, d = a + 1;
                            var facing = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                            if (Vector3.Dot(facing, normals[a] + normals[c]) < 0) triangles.AddRange(new[] { a, c, b, a, d, c });
                            else triangles.AddRange(new[] { a, b, c, a, c, d });
                        }
                }
            var mesh = new Mesh { name = "Dressed stone block" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A thick arrow slab with a fishtail tail, barbed head and chamfered faces.</summary>
        private static Mesh ArrowSlab(int pointing)
        {
            float tail = -Width / 2, tip = Width / 2, barb = Height / 2;
            var outline = new List<Vector2>
            {
                new(tail, -BodyHalf), new(Neck, -BodyHalf), new(Neck - .04f, -barb), new(tip, 0),
                new(Neck - .04f, barb), new(Neck, BodyHalf), new(tail, BodyHalf), new(tail + Notch, 0)
            };
            if (pointing < 0)
            {
                for (int i = 0; i < outline.Count; i++) outline[i] = new Vector2(-outline[i].x, outline[i].y);
                outline.Reverse();
            }
            var inset = new List<Vector2>();
            for (int i = 0; i < outline.Count; i++)
            {
                var p = outline[(i + outline.Count - 1) % outline.Count]; var v = outline[i]; var n = outline[(i + 1) % outline.Count];
                var e1 = (v - p).normalized; var e2 = (n - v).normalized;
                var n1 = new Vector2(-e1.y, e1.x); var n2 = new Vector2(-e2.y, e2.x);
                var bisector = (n1 + n2).normalized;
                inset.Add(v + bisector * (Bevel / Mathf.Max(.35f, Vector2.Dot(bisector, n1))));
            }
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            float t = Thickness / 2;
            // The moss sits in the lower corner by the fishtail, the side that faces the pillar's weather.
            Color Paint(Vector3 q, Vector3 n, float lift)
            {
                var colour = Weather(q, n, q.y + Height / 2, 0, 1.06f) * lift;
                float tailward = Mathf.Clamp01((-q.x * pointing - .45f) / .5f) * Mathf.Clamp01((-q.y - .08f) / .2f);
                colour = Color.Lerp(colour, Moss, tailward * (Hash(q * 9.1f) > .45f ? .8f : .25f));
                colour.a = 1;
                return colour;
            }
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float lift)
            {
                int first = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                var n = Vector3.Cross(b - a, c - a).normalized;
                foreach (var q in new[] { a, b, c, d }) colours.Add(Paint(q, n, lift));
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            // Caps are split into small triangles so the face carries the same weathering as the pillar.
            void Cap(List<Vector2> shape, float z, bool front)
            {
                var n = front ? Vector3.back : Vector3.forward;
                void Split(Vector2 a, Vector2 b, Vector2 c, int depth)
                {
                    if (depth == 0 || (b - a).sqrMagnitude + (c - b).sqrMagnitude + (a - c).sqrMagnitude < .02f)
                    {
                        int first = vertices.Count;
                        foreach (var p in new[] { a, b, c })
                        {
                            var q = new Vector3(p.x, p.y, z);
                            vertices.Add(q); colours.Add(Paint(q, n, front ? 1 : .8f));
                        }
                        triangles.AddRange(front ? new[] { first, first + 2, first + 1 } : new[] { first, first + 1, first + 2 });
                        return;
                    }
                    Vector2 ab = (a + b) / 2, bc = (b + c) / 2, ca = (c + a) / 2;
                    Split(a, ab, ca, depth - 1); Split(ab, b, bc, depth - 1); Split(ca, bc, c, depth - 1); Split(ab, bc, ca, depth - 1);
                }
                foreach (var tri in Triangulate(shape)) Split(shape[tri.x], shape[tri.y], shape[tri.z], 6);
            }
            Cap(inset, -t, true);
            Cap(inset, t, false);
            for (int i = 0; i < outline.Count; i++)
            {
                int j = (i + 1) % outline.Count;
                Vector3 O(int k, float z) => new(outline[k].x, outline[k].y, z);
                Vector3 I(int k, float z) => new(inset[k].x, inset[k].y, z);
                // Chamfers are worn pale; the sawn sides stay darker.
                Face(I(i, -t), I(j, -t), O(j, -t + Bevel), O(i, -t + Bevel), 1.07f);
                Face(O(i, -t + Bevel), O(j, -t + Bevel), O(j, t - Bevel), O(i, t - Bevel), .86f);
                Face(O(i, t - Bevel), O(j, t - Bevel), I(j, t), I(i, t), .8f);
            }
            var mesh = new Mesh { name = "Carved arrow slab" };
            mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Ear-clips a counter-clockwise outline.</summary>
        private static List<Vector3Int> Triangulate(List<Vector2> shape)
        {
            var left = new List<int>();
            for (int i = 0; i < shape.Count; i++) left.Add(i);
            var result = new List<Vector3Int>();
            static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            for (int guard = 0; left.Count > 3 && guard < 200; guard++)
                for (int i = 0; i < left.Count; i++)
                {
                    int a = left[(i + left.Count - 1) % left.Count], b = left[i], c = left[(i + 1) % left.Count];
                    if (Cross(shape[a], shape[b], shape[c]) <= 0) continue;
                    bool clear = true;
                    foreach (int k in left)
                        if (k != a && k != b && k != c && Cross(shape[a], shape[b], shape[k]) > 0 &&
                            Cross(shape[b], shape[c], shape[k]) > 0 && Cross(shape[c], shape[a], shape[k]) > 0) { clear = false; break; }
                    if (!clear) continue;
                    result.Add(new Vector3Int(a, b, c));
                    left.RemoveAt(i);
                    break;
                }
            if (left.Count == 3) result.Add(new Vector3Int(left[0], left[1], left[2]));
            return result;
        }

        /// <summary>A raised round moulding standing proud of the slab face.</summary>
        private static Mesh MedallionRing(float inner, float outer)
        {
            const int segments = 40;
            var profile = new[] { new Vector2(inner, 0), new Vector2(inner + .006f, -.022f), new Vector2((inner + outer) / 2, -.032f),
                new Vector2(outer - .006f, -.022f), new Vector2(outer, 0) };
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2 / segments;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                for (int k = 0; k < profile.Length; k++)
                {
                    var q = radial * profile[k].x + Vector3.forward * profile[k].y;
                    vertices.Add(q);
                    // The crest of the moulding is polished pale by weather.
                    float crest = k == 2 ? 1.12f : 1;
                    var colour = Color.Lerp(Cool, Warm, .55f) * 1.08f * crest * (.93f + .14f * Hash(q * 13f));
                    colour.a = 1;
                    colours.Add(colour);
                }
            }
            int rows = profile.Length;
            for (int s = 0; s < segments; s++)
                for (int k = 0; k < rows - 1; k++)
                {
                    int a = s * rows + k, b = a + rows;
                    triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            var mesh = new Mesh { name = "Medallion moulding" };
            mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Disc(float radius)
        {
            const int segments = 40;
            var dark = new Color(.2f, .2f, .26f);
            var vertices = new List<Vector3> { Vector3.zero };
            var colours = new List<Color> { dark };
            var triangles = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                float angle = s * Mathf.PI * 2 / segments;
                vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
                // The recess darkens towards its rim where the moulding overhangs it.
                colours.Add(dark * .75f);
                triangles.AddRange(new[] { 0, 1 + (s + 1) % segments, 1 + s });
            }
            var mesh = new Mesh { name = "Medallion recess" };
            mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
