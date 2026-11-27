using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

namespace RaidPartyManager.Presentation
{
    internal enum StageTurn { Automatic, Forward, Reverse }

    // The venue survives scene loads. Each node leases it for the lifetime of its own scenery.
    [DefaultExecutionOrder(200)]
    public sealed class DockPlayhouse : MonoBehaviour
    {
        private enum Cue { Opening, Playing, Closing, Closed }
        private const string ThanksForPlaying = "感谢游玩";
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
        private float impactTime = float.NegativeInfinity;
        private Camera shakenCamera;
        private Matrix4x4 restingProjection;
        private Material[] stageTimber;
        internal Mesh FloorPattern { get; private set; }
        internal Mesh PanoramaPattern { get; private set; }
        private VisualElement panel, inputShield;
        private VisualElement finale;
        private StyleFloat previousOpacity;
        private MaterialPropertyBlock folds;
        private Cue cue;
        private DockPerformance performance;
        internal DockSet Set { get; private set; }
        internal static bool HoldingSet => current != null && current.performance.HoldsScenery;
        private Action afterClose;
        private const float TownTransitionSpeed = 1.5f;
        private float cueSpeed = 1f;
        private float travel, hold;
        // The hem hangs on a spring behind the track, so the cloth trails, swings and settles.
        private float hem, hemSpeed, flutter, trackOpen;
        private const float HemFrequency = 8.4f, HemDamping = .34f;
        private bool closedFrame;

        internal static bool Moving => current != null && current.owner != null && (current.cue != Cue.Playing || current.performance.Busy);
        internal static DockPlayhouse Current => current;

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
                current.stageTimber = new[] { current.deckFrame.material,
                    current.shell.Find("Port gallery").GetComponent<Renderer>().material,
                    current.shell.Find("Starboard gallery").GetComponent<Renderer>().material,
                    current.shell.Find("Fly loft").GetComponent<Renderer>().material };
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
            if (!continuous)
                current.cueSpeed = owner.gameObject.scene.name is "Town" or "TalentMarket" or "DockShop" ||
                    AdventureFlow.FromTownFacility ? TownTransitionSpeed : 1f;
            current.cue = continuous ? Cue.Playing : Cue.Opening;
            current.travel = continuous ? 1 : 0;
            current.hold = .13f;
            current.hem = current.trackOpen = Ease(current.travel);
            current.hemSpeed = current.flutter = 0;
            current.ApplyCue();
            if (continuous) document.style.opacity = 0;
            return current;
        }

        internal static readonly Vector2 PlankTile = new(3f, 1.12f);
        internal float DownstageLimit => transform.TransformPoint(new Vector3(0, 0, 8.55f)).z;
        internal MeshRenderer DeckFrame => deckFrame;
        internal Bounds DeckBounds => deckFrame.bounds;
        internal Bounds SceneryOpening => new Bounds(opening.position, opening.lossyScale);

        internal Vector4 DressPanorama(Transform cloth, Texture image, float repeats = 1)
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
            float v = Mathf.Min(1, (float)image.width / image.height * repeats * height / (Mathf.PI * radius));
            return new Vector4(repeats, v, (1 - repeats) * .5f, (1 - v) * .5f);
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
                Ease(house.travel));
            seam.z = cloth.max.z;
            var camera = house.framedCamera;
            Vector3 l = camera.WorldToViewportPoint(house.left.transform.TransformPoint(seam));
            Vector3 r = camera.WorldToViewportPoint(house.right.transform.TransformPoint(seam));
            if (l.z < camera.nearClipPlane || r.z < camera.nearClipPlane) return new Vector2(frame.xMin,frame.xMax);
            return new Vector2(Mathf.Lerp(frame.xMin,frame.xMax,l.x),Mathf.Lerp(frame.xMin,frame.xMax,r.x));
        }

        internal void SetLighting(StageLighting lighting)
        {
            Set.Lighting = lighting;
            if (!performance.HoldsScenery) LightTimber(lighting);
        }

        internal void LightTimber(StageLighting lighting)
        {
            foreach (var material in stageTimber) lighting.Apply(material);
        }

        internal void LandRoadstone() => impactTime = Time.unscaledTime;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            Camera.onPreCull += ShakeFrame;
            Camera.onPostRender += RestoreFrame;
        }

        private void OnDisable()
        {
            if (shakenCamera != null) RestoreFrame(shakenCamera);
            impactTime = float.NegativeInfinity;
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            Camera.onPreCull -= ShakeFrame;
            Camera.onPostRender -= RestoreFrame;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => ShakeFrame(camera);
        private void EndCameraRendering(ScriptableRenderContext context, Camera camera) => RestoreFrame(camera);

        private void ShakeFrame(Camera camera)
        {
            float t = (Time.unscaledTime - impactTime) / .28f;
            if (camera != framedCamera || t >= 1 || shakenCamera != null) return;
            restingProjection = camera.projectionMatrix;
            var shake = restingProjection;
            float amplitude = .022f * (1-t) * (1-t);
            shake.m12 += amplitude * Mathf.Cos(23 * t);
            shake.m02 += amplitude * .36f * Mathf.Sin(32 * t);
            shakenCamera = camera;
            camera.projectionMatrix = shake;
        }

        private void RestoreFrame(Camera camera)
        {
            if (camera != shakenCamera) return;
            camera.projectionMatrix = restingProjection;
            shakenCamera = null;
        }

        private void OnDestroy()
        {
            if (stageTimber != null)
                foreach (var material in stageTimber) Destroy(material);
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
            finale?.RemoveFromHierarchy();
            finale = null;
            panel = inputShield = null;
            owner = null;
            afterClose = null;
            if (!performance.Crossing) gameObject.SetActive(false);
        }

        internal static void ChangeSet(Action next, bool toSigns = false, StageTurn turn = StageTurn.Automatic, bool townVisit = false)
        {
            if (current == null || current.panel == null) { next(); return; }
            current.cueSpeed = townVisit ? TownTransitionSpeed : 1f;
            current.performance.EndFirstEntrance();
            if (AdventureFlow.OnRoute || turn != StageTurn.Automatic)
            {
                // Allow an early selection during the initial curtain cue to proceed on the open stage.
                current.cue = Cue.Playing;
                current.travel = 1;
                current.ApplyCue();
                current.performance.Change(next, toSigns, turn);
                return;
            }
            current.CloseCurtain(next);
        }

        internal void ShowFinalCurtain(int gold, long payrollDebt, Action returnToMenu)
        {
            if (cue is Cue.Closing or Cue.Closed) return;
            performance.EndFirstEntrance();
            cueSpeed = 1;
            GameAudio.Play(SoundCue.StageCurtainClose, .58f);
            CloseCurtain(() =>
            {
                finale = new VisualElement { name = "campaign-finale", pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(finale);
                finale.style.justifyContent = Justify.Center;
                finale.style.alignItems = Align.Center;
                finale.style.unityFontDefinition = panel.resolvedStyle.unityFontDefinition;

                var lettering = Resources.Load<Texture2D>("CampaignFinaleTitle");
                if (lettering != null)
                {
                    var title = new Image
                    {
                        name = "campaign-finale-title", image = lettering, scaleMode = ScaleMode.ScaleToFit,
                        tooltip = ThanksForPlaying, pickingMode = PickingMode.Ignore
                    };
                    title.style.width = Length.Percent(72);
                    title.style.maxWidth = 760;
                    title.style.height = Length.Percent(34);
                    title.style.maxHeight = 240;
                    finale.Add(title);
                }
                else
                {
                    var title = PrototypeStyle.Text(ThanksForPlaying, 64, PrototypeStyle.Accent);
                    title.name = "campaign-finale-title";
                    title.pickingMode = PickingMode.Ignore;
                    title.style.unityFontStyleAndWeight = FontStyle.Bold;
                    PrototypeStyle.Middle(title);
                    finale.Add(title);
                }

                string wages = payrollDebt > 0 ? $"仍欠工资 {payrollDebt} 金币" : "工资已结清";
                var accounts = PrototypeStyle.Text($"剩余 {gold} 金币  ·  {wages}", 20);
                accounts.name = "campaign-payroll-summary";
                accounts.pickingMode = PickingMode.Ignore;
                accounts.style.marginTop = 8;
                accounts.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 4, color = Color.black };
                PrototypeStyle.Middle(accounts);
                finale.Add(accounts);

                // Cast sheets stay on the stage under the curtain renderers. Put the title on the
                // screen panel above the camera and outside the gameplay document's opacity fade.
                panel.parent.Add(finale);
                finale.schedule.Execute(returnToMenu).StartingIn(5000);
            });
        }

        private void CloseCurtain(Action next)
        {
            if (cue is Cue.Closing or Cue.Closed) return;
            cue = Cue.Closing;
            closedFrame = false;
            hold = 0;
            afterClose = next;
            ApplyCue();
        }

        private void LateUpdate()
        {
            if (panel == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .08f) * cueSpeed;
            if (cue == Cue.Playing)
            {
                bool arriving = performance.FirstEntrance;
                performance.Tick(panel, dt);
                if (arriving && !performance.FirstEntrance) ApplyCue();
                else if (Swing(dt)) ApplyFolds();
                return;
            }
            if (cue == Cue.Opening) performance.PrepareFirstEntrance();
            if (cue == Cue.Closed)
            {
                if (!closedFrame) { closedFrame = true; return; }
                var next = afterClose;
                afterClose = null;
                next?.Invoke();
                return;
            }
            if (hold > 0) hold -= dt;
            else
            {
                if (cue == Cue.Opening && travel == 0 && owner.gameObject.scene.name == "EventSelect" &&
                    AdventureFlow.OnRoute && AdventureFlow.Active is { Floor: 1, Stage: 1, Current: null })
                    GameAudio.Play(SoundCue.StageCurtainOpen, .54f);
                travel = Mathf.MoveTowards(travel, cue == Cue.Opening ? 1 : 0, dt / (cue == Cue.Opening ? 1f : .8f));
            }
            Swing(dt);
            if (cue == Cue.Opening && travel >= 1) cue = Cue.Playing;
            // Wait for the trailing hem as well, or the next set would show through the gap at the floor.
            if (cue == Cue.Closing && travel <= 0 && hem < .01f) cue = Cue.Closed;
            ApplyCue();
        }

        /// <summary>Quintic ease: the track starts and stops without a jolt.</summary>
        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        /// <summary>Advances the hem spring. Returns false once the cloth hangs still.</summary>
        private bool Swing(float dt)
        {
            float open = Ease(travel);
            float pull = dt > 0 ? Mathf.Abs(open - trackOpen) / dt : 0;
            trackOpen = open;
            if (Mathf.Abs(hem - open) < 1e-4f && Mathf.Abs(hemSpeed) < 1e-3f && flutter < 1e-3f && pull == 0)
            {
                if (hem == open && flutter == 0) return false;
                hem = open; hemSpeed = 0; flutter = 0;
                return true;
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .01f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                hemSpeed += (HemFrequency * HemFrequency * (open - hem) - 2 * HemDamping * HemFrequency * hemSpeed) * h;
                hem += hemSpeed * h;
            }
            float target = Mathf.Clamp01(pull * .55f + Mathf.Abs(hemSpeed) * .35f);
            flutter = Mathf.MoveTowards(flutter, target, dt * (target > flutter ? 6f : 1.6f));
            return true;
        }

        private void ApplyFolds()
        {
            folds.SetFloat("_Travel", Ease(travel));
            folds.SetFloat("_Hem", hem);
            folds.SetFloat("_Flutter", flutter);
            left.SetPropertyBlock(folds);
            right.SetPropertyBlock(folds);
        }

        private void ApplyCue()
        {
            float open = Ease(travel);
            ApplyFolds();
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
