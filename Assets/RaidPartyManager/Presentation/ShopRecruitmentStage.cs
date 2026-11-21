using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage
    {
        private static readonly Vector3 RecruitmentCentre = new(3.12f, 1.43f, -3.36f);
        private static readonly Color32 WheelBrass = new(200, 148, 75, 255);
        private static readonly Color32 WheelIron = new(56, 43, 34, 255);
        private static readonly int Desaturation = Shader.PropertyToID("_Desaturation");
        private const float SpinDuration = 1.45f;
        private readonly MeshRenderer[] wheelSegments = new MeshRenderer[ShopRecruitment.SlotCount];
        private readonly MeshRenderer[] wheelPortraits = new MeshRenderer[ShopRecruitment.SlotCount];
        private IReadOnlyList<CharacterDefinition> wheelSlots;
        private Transform wheel;
        private Mesh segmentMesh, portraitMesh;
        private float focus;
        private float wheelAngle;
        private float spinStart;
        private float spinArc;
        private float spinElapsed;
        internal bool RecruitmentFocused { get; set; }
        internal bool RecruitmentSpinning { get; private set; }
        internal float RecruitmentFocus => focus;
        private Vector3 RecruitmentEye => new(1.24f, 1.25f, house.DeckBounds.min.z - 4.67f);

        internal void BuildRecruitmentWheel(ShopRecruitment recruitment, EncounterData content)
        {
            wheelSlots = recruitment.Slots;
            var fixture = new GameObject("Recruitment wheel fixture").transform;
            fixture.SetParent(stage, false);
            fixture.localPosition = RecruitmentCentre;
            wheel = new GameObject("Recruitment wheel rotor").transform;
            wheel.SetParent(fixture, false);

            var darkWood = new Color32(116, 77, 48, 255);
            foreach (float side in new[] { -1f, 1f })
            {
                var shoulder = new Vector3(side * .21f, -.49f, .14f);
                var foot = new Vector3(side * .65f, -1.39f, .16f);
                var column = Shape("Wheel trestle leg", PrimitiveType.Cylinder, Vector3.zero,
                    new Vector3(.13f, Vector3.Distance(foot, shoulder) * .5f, .19f), art.timber, darkWood, Surface.Solid);
                Part(column, fixture, (foot + shoulder) * .5f).transform.localRotation =
                    Quaternion.FromToRotation(Vector3.up, shoulder - foot);
                Part(Shape("Wheel trestle shoe", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.21f, .037f, .42f),
                    null, WheelIron, Surface.Solid), fixture, foot);
            }
            Shadow(RecruitmentCentre - Vector3.up * RecruitmentCentre.y, 1.5f, 1.06f);

            WheelDisc("Rim", fixture, 1.02f, .11f, .045f, WheelBrass);
            WheelDisc("Inner iron rim", fixture, .985f, .115f, .035f, WheelIron);
            segmentMesh = BuildWheelSegment();
            portraitMesh = BuildWheelSegment(portrait: true);
            for (int i = 0; i < wheelSlots.Count; i++)
            {
                var segment = Shape("Recruitment wedge " + i, PrimitiveType.Cube, Vector3.zero, Vector3.one, null,
                    i % 2 == 0 ? new Color32(125, 65, 48, 255) : new Color32(48, 90, 92, 255), Surface.Solid);
                segment.GetComponent<MeshFilter>().sharedMesh = segmentMesh;
                Part(segment, wheel, Vector3.zero);
                segment.transform.localRotation = Quaternion.Euler(0, 0, i * 45);
                wheelSegments[i] = segment;

                var slot = wheelSlots[i];
                if (slot == null) continue;
                var face = Flat("Recruitment portrait " + i, Vector3.zero, 1, 1, content.PortraitFor(slot.Id));
                face.GetComponent<MeshFilter>().sharedMesh = portraitMesh;
                wheelPortraits[i] = Part(face, segment.transform, Vector3.zero);
            }
            WheelDisc("Hub", wheel, .237f, .15f, -.066f, WheelBrass);
            WheelDisc("Hub cap", wheel, .167f, .19f, -.095f, WheelIron);
            for (int i = 0; i < 16; i++)
            {
                Vector3 point = Quaternion.Euler(0, 0, i * 22.5f) * new Vector3(0, .997f, -.071f);
                var pin = Shape("Rim stud " + i, PrimitiveType.Sphere, Vector3.zero, new Vector3(.037f, .037f, .025f),
                    null, WheelBrass, Surface.Solid);
                Part(pin, fixture, point);
            }
            var pointer = new GameObject("Recruitment wheel pointer").transform;
            pointer.SetParent(fixture, false);
            pointer.localPosition = new Vector3(0, 1.006f, -.19f);
            Part(Box("Pointer left", Vector3.zero, new Vector3(.067f, .22f, .08f), null, WheelBrass),
                pointer, new Vector3(-.06f, .017f, 0)).transform.localRotation = Quaternion.Euler(0, 0, 34);
            Part(Box("Pointer right", Vector3.zero, new Vector3(.067f, .22f, .08f), null, WheelBrass),
                pointer, new Vector3(.06f, .017f, 0)).transform.localRotation = Quaternion.Euler(0, 0, -34);
            if (recruitment.Candidates.LastOrDefault() is { } previous)
                SetWheelAngle(SlotAngle(previous));
            RefreshWheel(recruitment);
        }

        private static MeshRenderer Part(MeshRenderer part, Transform parent, Vector3 position)
        {
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            return part;
        }

        private void WheelDisc(string name, Transform parent, float radius, float depth, float z, Color color)
        {
            var disc = Shape(name, PrimitiveType.Cylinder, Vector3.zero, new Vector3(2 * radius, depth / 2, 2 * radius),
                null, color, Surface.Solid);
            Part(disc, parent, new Vector3(0, 0, z)).transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        private void SetTint(MeshRenderer renderer, Color tint, bool desaturate = false)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_Color", tint);
            properties.SetFloat(Desaturation, desaturate ? 1 : 0);
            renderer.SetPropertyBlock(properties);
        }

        internal void RefreshWheel(ShopRecruitment recruitment)
        {
            for (int i = 0; i < wheelSlots.Count; i++)
            {
                bool available = recruitment.IsAvailable(wheelSlots[i]);
                Color tint = !available ? new Color32(71, 69, 66, 255) : wheelSlots[i].Stage == 2 ?
                    new Color32(162, 113, 47, 255) : i % 2 == 0 ? new Color32(125, 65, 48, 255) : new Color32(48, 90, 92, 255);
                SetTint(wheelSegments[i], tint);
                if (wheelPortraits[i] != null)
                    SetTint(wheelPortraits[i], available ? Color.white : new Color(.57f, .57f, .57f), !available);
            }
        }

        internal void SpinRecruitmentWheel(CharacterDefinition result)
        {
            spinStart = wheelAngle;
            spinArc = 1080 + Mathf.Repeat(SlotAngle(result) - spinStart, 360);
            spinElapsed = 0;
            RecruitmentSpinning = true;
        }

        private float SlotAngle(CharacterDefinition result)
        {
            for (int i = 0; i < wheelSlots.Count; i++)
                if (wheelSlots[i] == result) return -45 * i;
            return 0;
        }

        private void SetWheelAngle(float degrees)
        {
            wheelAngle = Mathf.Repeat(degrees, 360);
            wheel.localRotation = Quaternion.Euler(0, 0, wheelAngle);
        }

        internal bool UpdateRecruitment(float elapsed)
        {
            if (!RecruitmentSpinning) return false;
            spinElapsed = Mathf.Min(spinElapsed + elapsed, SpinDuration);
            float remainder = 1 - spinElapsed / SpinDuration;
            SetWheelAngle(spinStart + spinArc * (1 - remainder * remainder * remainder));
            RecruitmentSpinning = spinElapsed < SpinDuration;
            return !RecruitmentSpinning;
        }

        internal void PlaceRecruitmentTarget(VisualElement view) => Pin(view,
            RecruitmentCentre + new Vector3(-1.03f, 1.12f, -.12f), RecruitmentCentre + new Vector3(1.03f, -1.32f, -.12f));

        internal bool OverRecruitmentWheel(Vector2 pointer, Rect frame)
        {
            var centre = camera.WorldToViewportPoint(RecruitmentCentre + Vector3.back * .08f);
            var radius = camera.WorldToViewportPoint(RecruitmentCentre + Vector3.up * 1.05f + Vector3.back * .08f) - centre;
            Vector2 projected = new(frame.x + centre.x * frame.width, frame.y + (1 - centre.y) * frame.height);
            return (pointer - projected).sqrMagnitude <= Mathf.Pow(radius.y * frame.height, 2);
        }

        internal void PlaceRecruitmentControls(VisualElement view) => Pin(view,
            RecruitmentCentre + new Vector3(-.69f, -.96f, -.33f), RecruitmentCentre + new Vector3(.69f, -1.45f, -.33f));

        public override void Dispose()
        {
            if (segmentMesh != null) Object.Destroy(segmentMesh);
            if (portraitMesh != null) Object.Destroy(portraitMesh);
            if (signArrow != null) Object.Destroy(signArrow);
            if (recruitWalkMesh != null) Object.Destroy(recruitWalkMesh);
            base.Dispose();
        }

        private static Mesh BuildWheelSegment(bool portrait = false)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            const int arcSteps = 10;
            float inner = portrait ? .245f : .21f;
            float outer = portrait ? .957f : .97f;
            float edge = portrait ? 20.75f : 21.45f;
            float front = portrait ? -.104f : -.102f;
            const float back = .009f;
            for (int step = 0; step < arcSteps; step++)
            {
                float a = Mathf.Lerp(-edge, edge, (float)step / arcSteps);
                float b = Mathf.Lerp(-edge, edge, (float)(step + 1) / arcSteps);
                Quad(Point(inner, b, front), Point(outer, b, front), Point(outer, a, front), Point(inner, a, front));
                if (portrait) continue;
                Quad(Point(outer, a, front), Point(outer, b, front), Point(outer, b, back), Point(outer, a, back));
                Quad(Point(inner, a, back), Point(inner, b, back), Point(outer, b, back), Point(outer, a, back));
                if (step == 0)
                    Quad(Point(inner, a, front), Point(outer, a, front), Point(outer, a, back), Point(inner, a, back));
                if (step == arcSteps - 1)
                    Quad(Point(outer, b, front), Point(inner, b, front), Point(inner, b, back), Point(outer, b, back));
            }
            var mesh = new Mesh { name = portrait ? "Portrait cut to fit wheel sector" : "Extruded wheel sector" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            return mesh;

            Vector3 Point(float radius, float angle, float z) =>
                new(-radius * Mathf.Sin(angle * Mathf.Deg2Rad), radius * Mathf.Cos(angle * Mathf.Deg2Rad), z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                if (portrait)
                {
                    AddPortraitUv(a); AddPortraitUv(b); AddPortraitUv(c); AddPortraitUv(d);
                }
                else uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
            }

            void AddPortraitUv(Vector3 point) => uv.Add(new Vector2(.5f + point.x / .74f,
                Mathf.Lerp(.17f, .99f, Mathf.InverseLerp(.22f, .96f, point.y))));
        }
    }
}
