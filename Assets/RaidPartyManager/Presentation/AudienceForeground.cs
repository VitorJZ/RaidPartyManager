using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Presentation
{
    // The pit, stage lip and rows live at separate camera depths. Seat dimensions are in picture heights.
    internal sealed class AudienceForeground : IDisposable
    {
        public const float ClearancePercent = 6.4f;
        private const float ApronTop = .060f;
        private readonly Material material;
        private readonly MeshFilter pit, fascia, lip, farSeats, nearSeats;
        private readonly List<Vector3> points = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<int> indices = new();
        private readonly float seatAspect;
        private float previousAspect;

        public AudienceForeground(Transform owner, Shader shader, Texture2D chair, Texture2D timber)
        {
            var house = new GameObject("Audience").transform;
            house.SetParent(owner, false);
            seatAspect = (float)chair.width / chair.height;
            material = new Material(shader) { renderQueue = 3100 };
            if (material.HasProperty("_DepthWrite"))
                material.SetFloat("_DepthWrite", 0);

            pit = Layer("Orchestra pit", Texture2D.whiteTexture, new Color32(21, 18, 21, 255), 60);
            fascia = Layer("Apron face", timber, new Color32(143, 122, 106, 255), 61);
            lip = Layer("Apron nosing", timber, new Color32(202, 174, 136, 255), 62);
            farSeats = Layer("Front stalls", chair, new Color32(151, 143, 142, 255), 63);
            nearSeats = Layer("Near stalls", chair, new Color32(103, 104, 113, 255), 64);

            MeshFilter Layer(string name, Texture2D texture, Color tint, int order)
            {
                var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                obj.transform.SetParent(house, false);
                var filter = obj.GetComponent<MeshFilter>();
                filter.sharedMesh = new Mesh { name = name };
                var r = obj.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.sortingOrder = order;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetTexture("_MainTex", texture);
                block.SetColor("_Color", tint);
                block.SetVector("_MainTex_ST", new Vector4(1, 1, 0, 0));
                r.SetPropertyBlock(block);
                return filter;
            }
        }

        public void Frame(Camera camera, float truck)
        {
            float aspect = camera.aspect;
            if (!Mathf.Approximately(aspect, previousAspect))
            {
                float extent = aspect * .5f + .24f;
                Strip(pit, extent, -.28f, .027f, 1, 1);
                Strip(fascia, extent, .024f, ApronTop - .004f, extent * 4, .36f);
                Strip(lip, extent, ApronTop - .004f, ApronTop, extent * 4, .08f);
                Row(farSeats, extent, .132f, .044f, false);
                Row(nearSeats, extent, .171f, .012f, true);
                previousAspect = aspect;
            }
            Place(pit, 5.02f, 0);
            Place(fascia, 5f, .004f);
            Place(lip, 4.98f, .004f);
            Place(farSeats, 3.8f, .010f);
            Place(nearSeats, 2.6f, .022f);

            void Place(MeshFilter mesh, float depth, float drift)
            {
                var bottom = camera.ViewportToWorldPoint(new Vector3(.5f, 0, depth));
                float height = 2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
                mesh.transform.SetPositionAndRotation(bottom - camera.transform.right * (truck * drift * height),
                    camera.transform.rotation);
                mesh.transform.localScale = Vector3.one * height;
            }
        }

        private void Strip(MeshFilter layer, float extent, float low, float high, float repeat, float grainHeight)
        {
            Quad(-extent, extent, low, high, 0, repeat, .41f, .41f + grainHeight);
            Upload(layer);
        }

        private void Row(MeshFilter layer, float extent, float pitch, float crest, bool stagger)
        {
            int count = Mathf.CeilToInt(extent / pitch) + 1;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < count; i++)
                {
                    // A narrow centre aisle, staggered crowns and a slight rake around the audience.
                    float x = side * (.037f + (i + (stagger ? .85f : .5f)) * pitch);
                    float width = pitch * .985f;
                    float top = crest + .006f * Mathf.Clamp01(Mathf.Abs(x)) + .0016f * Mathf.Sin(i * 2.4f + side);
                    Quad(x - width * .5f, x + width * .5f, top - width / seatAspect, top, 0, 1, 0, 1);
                }
            }
            Upload(layer);
        }

        private void Quad(float left, float right, float low, float high, float u0, float u1, float v0, float v1)
        {
            int i = points.Count;
            points.Add(new Vector3(left, low)); points.Add(new Vector3(left, high));
            points.Add(new Vector3(right, high)); points.Add(new Vector3(right, low));
            uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1));
            uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
            indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
            indices.Add(i); indices.Add(i + 2); indices.Add(i + 3);
        }

        private void Upload(MeshFilter layer)
        {
            var mesh = layer.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(points);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            points.Clear(); uvs.Clear(); indices.Clear();
        }

        public void Dispose()
        {
            foreach (var layer in new[] { pit, fascia, lip, farSeats, nearSeats })
                Object.Destroy(layer.sharedMesh);
            Object.Destroy(material);
            // The audience transforms are disposed with their owning stage.
        }
    }
}
