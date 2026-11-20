using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // The venue survives scene loads. Each node leases it for the lifetime of its own scenery.
    [DefaultExecutionOrder(200)]
    public sealed class DockPlayhouse : MonoBehaviour
    {
        private enum Cue { Opening, Playing, Closing, Closed }
        private static DockPlayhouse current;
        internal const float CameraDistance = 16.25f;
        internal const float FieldOfView = 34f;
        private const float EyeHeight = 2.30f;
        private const float PlayingLine = .38f;
        private Transform owner, stalls, portal, shell, opening, moonShackle, moon;
        private Transform liftLine, headerLowerEdge;
        private Vector3 lanternRest, lineRest, lineSize;
        private Camera framedCamera;
        private float framedAspect;
        private MeshRenderer left, right, deckFrame;
        private Vector3 deckJoint;
        internal Mesh FloorPattern { get; private set; }
        internal Mesh PanoramaPattern { get; private set; }
        private VisualElement panel, inputShield;
        private StyleFloat previousOpacity;
        private MaterialPropertyBlock folds;
        private Cue cue;
        private DockPerformance performance;
        internal DockSet Set { get; private set; }
        internal static bool HoldingSet => current != null && current.performance.HoldsScenery;
        private Action afterClose;
        private float travel, hold;
        private bool closedFrame;

        internal static bool Moving => current != null && current.owner != null && (current.cue != Cue.Playing || current.performance.Busy);

        internal static DockPlayhouse Bind(Transform owner, VisualElement document)
        {
            if (current == null)
            {
                var root = Instantiate(Resources.Load<GameObject>("MistDockPlayhouse"));
                root.name = "Mist Dock Playhouse";
                DontDestroyOnLoad(root);
                current = root.AddComponent<DockPlayhouse>();
                current.performance = new DockPerformance(current);
                current.folds = new MaterialPropertyBlock();
                current.left = root.transform.Find("Curtain L").GetComponent<MeshRenderer>();
                current.right = root.transform.Find("Curtain R").GetComponent<MeshRenderer>();
                current.stalls = root.transform.Find("Stalls");
                current.portal = root.transform.Find("Portal and traveller rail");
                current.headerLowerEdge = current.portal.Find("Header lower edge");
                current.shell = root.transform.Find("Half-round stage");
                current.opening = current.shell.Find("Backcloth opening");
                current.PanoramaPattern = current.opening.GetComponent<MeshFilter>().sharedMesh;
                current.moonShackle = current.shell.Find("Fly loft/Moon shackle");
                current.moon = current.moonShackle.Find("Flown moon lantern");
                current.liftLine = current.moon.Find("Hemp lift line");
                current.lanternRest = current.moon.localPosition;
                current.lineRest = current.liftLine.localPosition;
                current.lineSize = current.liftLine.localScale;
                current.deckFrame = current.shell.Find("Deck and joists").GetComponent<MeshRenderer>();
                current.FloorPattern = current.deckFrame.transform.Find("Floorcloth pattern").GetComponent<MeshFilter>().sharedMesh;
                current.deckJoint = root.transform.InverseTransformPoint(current.deckFrame.transform.position);
            }
            current.Unbind(current.owner);
            current.owner = owner;
            current.Set = new DockSet(owner);
            current.performance.Bind(current.Set);
            current.performance.StageBound();
            current.moon.gameObject.SetActive(false);
            current.framedCamera = null;
            current.panel = document;
            current.previousOpacity = document.style.opacity;
            current.inputShield = new VisualElement { name = "curtain-input-shield", focusable = true };
            PrototypeStyle.Fill(current.inputShield);
            current.inputShield.RegisterCallback<NavigationMoveEvent>(e => e.StopPropagation());
            current.inputShield.RegisterCallback<NavigationSubmitEvent>(e => e.StopPropagation());
            current.inputShield.RegisterCallback<NavigationCancelEvent>(e => e.StopPropagation());
            current.inputShield.RegisterCallback<KeyDownEvent>(e => e.StopPropagation());
            current.gameObject.SetActive(true);
            bool continuous = current.performance.Crossing;
            current.cue = continuous ? Cue.Playing : Cue.Opening;
            current.travel = continuous ? 1 : 0;
            current.hold = .13f;
            current.ApplyCue();
            if (continuous) document.style.opacity = 0;
            return current;
        }

        internal static readonly Vector2 PlankTile = new(3f, 1.12f);
        internal Vector3 FlyBarEye(float x) => moonShackle.parent.TransformPoint(new Vector3(x, 2.033f, 12));
        internal float DownstageLimit => transform.TransformPoint(new Vector3(0, 0, 8.55f)).z;
        internal MeshRenderer DeckFrame => deckFrame;
        internal Bounds DeckBounds => deckFrame.bounds;
        internal Bounds SceneryOpening => new Bounds(opening.position, opening.lossyScale);

        internal Vector4 DressPanorama(Transform cloth, Texture image)
        {
            var deck = DeckBounds;
            float radius = deck.size.z;
            // Only the visible part of the painting needs ink. The fly tower extends far above the sightline.
            float distance = deck.max.z - framedCamera.transform.position.z;
            float sightline = framedCamera.ViewportToWorldPoint(new Vector3(.5f, 1, distance)).y;
            float height = Mathf.Min(opening.lossyScale.y, sightline - deck.max.y + .55f);
            cloth.position = new Vector3(deck.center.x, deck.max.y, deck.min.z);
            cloth.localScale = new Vector3(radius, height, radius);
            // U follows the entire 180-degree sweep at constant arc length.
            float v = Mathf.Min(1, (float)image.width / image.height * height / (Mathf.PI * radius));
            return new Vector4(1, v, 0, (1-v)*.5f);
        }

        // Project the physical lip of the portal into the same panel as its hanging signs.
        internal static float StageOpeningTop(IPanel panel)
        {
            var frame = panel.visualTree.worldBound;
            if (current == null || current.owner == null || current.framedCamera == null) return frame.yMin;
            var camera = current.framedCamera;
            var edge = camera.WorldToViewportPoint(current.headerLowerEdge.position);
            if (edge.z < camera.nearClipPlane) return frame.yMin;
            return Mathf.Lerp(frame.yMin, frame.yMax, 1f - edge.y);
        }

        internal static Vector2 CurtainOpening(IPanel panel)
        {
            Rect frame = panel.visualTree.worldBound;
            var house = current;
            if (house == null || house.owner == null || house.framedCamera == null)
                return new Vector2(frame.xMin,frame.xMax);
            // Match the fold compression in House.shader. Use the far edge of the seam
            // to clear the thickness of the folds as well as their centre line.
            Bounds cloth = house.right.localBounds;
            var seam = cloth.center;
            seam.x = Mathf.Lerp(cloth.min.x,cloth.max.x + (cloth.min.x-cloth.max.x)*.112f,
                Mathf.SmoothStep(0,1,house.travel));
            seam.z = cloth.max.z;
            var camera = house.framedCamera;
            Vector3 l = camera.WorldToViewportPoint(house.left.transform.TransformPoint(seam));
            Vector3 r = camera.WorldToViewportPoint(house.right.transform.TransformPoint(seam));
            if (l.z < camera.nearClipPlane || r.z < camera.nearClipPlane) return new Vector2(frame.xMin,frame.xMax);
            return new Vector2(Mathf.Lerp(frame.xMin,frame.xMax,l.x),Mathf.Lerp(frame.xMin,frame.xMax,r.x));
        }

        internal void SetMoonVisible(bool on) => moon.gameObject.SetActive(on);

        internal void HoistMoon(float amount)
        {
            // Reel the long straight section onto the fly bar; leave the bridle intact.
            float reeled = amount * (lineSize.y - .5f);
            moon.localPosition = lanternRest + Vector3.up * (2 * reeled * moon.localScale.y);
            liftLine.localPosition = lineRest - Vector3.up * reeled;
            liftLine.localScale = lineSize - Vector3.up * reeled;
        }

        internal Transform LightMoon()
        {
            Set.Moon = true;
            if (!performance.Crossing) HoistMoon(0);
            moon.gameObject.SetActive(true);
            return moon;
        }

        // All dock sets are viewed from this seat. A shifted lens leaves room for the heads and the stalls
        // without tilting the proscenium or changing the vanishing point between acts.
        internal void Frame(Camera camera)
        {
            float aspect = camera.aspect;
            if (camera == framedCamera && aspect == framedAspect) return;
            framedCamera = camera;
            framedAspect = aspect;
            float fit = Mathf.Max(1, (16f / 10f) / aspect);
            float tangent = Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) * fit;
            camera.fieldOfView = 2 * Mathf.Rad2Deg * Mathf.Atan(tangent);
            camera.transform.SetPositionAndRotation(new Vector3(0, EyeHeight, -CameraDistance), Quaternion.identity);
            var lens = Matrix4x4.Perspective(camera.fieldOfView, aspect, camera.nearClipPlane, camera.farClipPlane);
            lens.m12 = 1 - 2 * PlayingLine - EyeHeight / (CameraDistance * tangent);
            camera.projectionMatrix = lens;

            float ratio = aspect / (16f / 9f);
            float lift = lens.m12 * tangent * 7.86f;
            float scale = EyeHeight / (-deckJoint.y * fit - lift);
            transform.SetPositionAndRotation(camera.transform.position + Vector3.up * (lift * scale), Quaternion.identity);
            transform.localScale = new Vector3(fit * ratio, fit, 1) * scale;
            // Project the downstage masking from the same sightlines while keeping it ahead of the shaft.
            var maskingOffset = Vector3.up * ((portal.localScale.z - 1) * lift / fit);
            portal.localPosition = left.transform.localPosition = right.transform.localPosition = maskingOffset;
            // Keep the crest of the nearer seat row in the same narrow strip beneath the apron.
            stalls.localPosition = Vector3.up * (-lens.m12 * tangent * (7.86f - 5.16f) * .60f);
            stalls.localScale = new Vector3(1 / ratio, 1, 1);
            // The outer proscenium can fit the window independently of the circular playing area.
            shell.localScale = new Vector3(1 / (fit * ratio), 1, 1);
            moonShackle.localScale = new Vector3(fit, 1, fit);
        }

        internal void Unbind(Transform lease)
        {
            if (this == null || ReferenceEquals(owner, null) || lease != owner) return;
            performance.Unbind();
            if (panel != null) panel.style.opacity = previousOpacity;
            inputShield?.RemoveFromHierarchy();
            panel = inputShield = null;
            owner = null;
            afterClose = null;
            if (!performance.Crossing) gameObject.SetActive(false);
        }

        internal static void ChangeSet(Action next, bool toSigns = false)
        {
            if (current == null || current.panel == null) { next(); return; }
            current.performance.EndFirstEntrance();
            if (AdventureFlow.OnRoute)
            {
                // Allow an early selection during the initial curtain cue to proceed on the open stage.
                current.cue = Cue.Playing;
                current.travel = 1;
                current.ApplyCue();
                current.performance.Change(next, toSigns);
                return;
            }
            if (current.cue is Cue.Closing or Cue.Closed) return;
            current.cue = Cue.Closing;
            current.closedFrame = false;
            current.hold = 0;
            current.afterClose = next;
            current.ApplyCue();
        }

        private void LateUpdate()
        {
            if (panel == null) return;
            if (cue == Cue.Playing)
            {
                bool arriving = performance.FirstEntrance;
                performance.Tick(panel);
                if (arriving && !performance.FirstEntrance) ApplyCue();
                return;
            }
            if (cue == Cue.Opening) performance.PrepareFirstEntrance();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .08f);
            if (cue == Cue.Closed)
            {
                if (!closedFrame) { closedFrame = true; return; }
                var next = afterClose;
                afterClose = null;
                next?.Invoke();
                return;
            }
            if (hold > 0) hold -= dt;
            else travel = Mathf.MoveTowards(travel, cue == Cue.Opening ? 1 : 0, dt / (cue == Cue.Opening ? .84f : .68f));
            if (cue == Cue.Opening && travel >= 1) cue = Cue.Playing;
            if (cue == Cue.Closing && travel <= 0) cue = Cue.Closed;
            ApplyCue();
        }

        private void ApplyCue()
        {
            float open = Mathf.SmoothStep(0, 1, travel);
            folds.SetFloat("_Travel", open);
            left.SetPropertyBlock(folds);
            right.SetPropertyBlock(folds);
            if (cue == Cue.Playing && !performance.FirstEntrance)
            {
                panel.style.opacity = previousOpacity;
                inputShield.RemoveFromHierarchy();
            }
            else
            {
                panel.style.opacity = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.74f, .98f, open));
                if (inputShield.parent != panel) panel.Add(inputShield);
                else if (panel[panel.childCount - 1] != inputShield) inputShield.BringToFront();
                if (panel.focusController?.focusedElement != inputShield) inputShield.Focus();
            }
        }
    }
}
