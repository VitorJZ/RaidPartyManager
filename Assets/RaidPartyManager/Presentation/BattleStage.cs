using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Presentation
{
    // X runs across the playing boards. The scenic flats stand upstage, behind the cast.
    internal sealed class BattleStage : IDisposable
    {
        private const int HazeSegments = 12;
        private const float CameraDistance = DockPlayhouse.CameraDistance;
        private const float FieldOfView = DockPlayhouse.FieldOfView;
        private static readonly int Ink = Shader.PropertyToID("_Color");
        private static readonly int Map = Shader.PropertyToID("_MainTex");
        private static readonly int UV = Shader.PropertyToID("_MainTex_ST");
        private static readonly int Walk = Shader.PropertyToID("_Walk");
        private static readonly int Contact = Shader.PropertyToID("_ContactShadow");
        // Values shared with DockMoonlight.cginc.
        private enum Surface { Unlit, Sky, Boards, Paper, Haze, CastShadow }
        private static readonly int MoonSurface = Shader.PropertyToID("_MoonSurface");
        private static readonly int InkTexelSize = Shader.PropertyToID("_InkTexelSize");
        private static readonly int MoonWorld = Shader.PropertyToID("_MoonWorld");
        private static readonly int PlayingHalfWidth = Shader.PropertyToID("_PlayingHalfWidth");
        private static readonly int MoonLanding = Shader.PropertyToID("_MoonLanding");
        private readonly bool moonlit, castOnly;
        private readonly Mesh hazeMesh;
        private readonly MeshRenderer haze, leftShadow, rightShadow;
        private readonly Vector3[] hazeVertices;
        private readonly Camera camera;
        private readonly BattleStageArt.Set set;
        private readonly GameObject stage;
        private readonly VisualElement panel;
        private readonly Material paper;
        private readonly Mesh quad, walkMesh;
        private readonly MaterialPropertyBlock block = new();
        private readonly MeshRenderer sky, deck, upstage, left, right, curtainLeft, curtainRight;
        private readonly Dictionary<StageActorImage, Actor> actors = new();
        private readonly Stack<Actor> spare = new();
        private readonly Vector3 oldPosition;
        private readonly Quaternion oldRotation;
        private readonly float oldFov;
        private readonly bool oldOrtho;
        private readonly CameraClearFlags oldClear;
        private readonly Color oldBackground;
        private float halfWidth, halfHeight, cameraHeight, pointerX, dolly;
        private readonly double openedAt;
        private readonly float drapeAspect;
        private bool disposed;
        private readonly AudienceForeground audience;
        private readonly DockPlayhouse house;
        private readonly Transform lantern;
        private readonly Light moonLamp;
        private float castDistance;
        internal bool HasDockHouse => house != null;
        private readonly Texture2D printedSurf;
        private readonly MeshRenderer surf, derrickFooting, mooring, creel, leftDressing, rightDressing;
        private readonly BattleStageArt stageArt;
        private readonly SceneryFlipbook scenery;

        private sealed class Actor
        {
            public MeshRenderer body, shadow, castShadow;
        }

        public BattleStage(Transform owner, Camera sceneCamera, VisualElement panel, BattleStageArt art,
            BattleStageArt.Set set, double now, DockPlayhouse existingHouse = null)
        {
            camera = sceneCamera;
            this.set = set;
            stageArt = art;
            this.panel = panel;
            openedAt = now;
            oldPosition = camera.transform.position;
            oldRotation = camera.transform.rotation;
            oldFov = camera.fieldOfView;
            oldOrtho = camera.orthographic;
            oldClear = camera.clearFlags;
            oldBackground = camera.backgroundColor;
            stage = new GameObject("Paper Theatre");
            stage.transform.SetParent(owner, false);
            paper = new Material(art.paperShader);
            castOnly = existingHouse != null;
            moonlit = !castOnly && set.moored && set.backcloth != null;
            quad = MakeSheet(1, 1);
            walkMesh = MakeSheet(14, 18);
            if (castOnly)
            {
                house = existingHouse;
                paper.SetInt("_PortalTest",(int)CompareFunction.NotEqual);
                RenderPipelineManager.beginCameraRendering += BeforeCamera;
                Camera.onPreCull += BeforeCull;
                Update(now,.3f,0,0);
                return;
            }
            camera.orthographic = false;
            camera.fieldOfView = FieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(25, 37, 48, 255);
            if (moonlit) paper.EnableKeyword("RPM_DOCK_MOON");
            if (moonlit || set.turntable) paper.SetInt("_PortalTest", (int)CompareFunction.NotEqual);
            scenery = new SceneryFlipbook(set.backcloth != null ? set.backcloth : set.region);
            sky = Flat("Cyclorama", scenery.Current, -100, Surface.Sky);
            deck = Flat("Stage floor", set.floor != null ? set.floor : set.region, -60, Surface.Boards);
            upstage = Flat("Upstage", set.upstage, set.moored ? -72 : -48);
            if (set.moored && upstage != null)
                Shape(upstage, set.upstage, new Vector4(1, .88f, 0, .12f), new Color(.88f, .92f, .95f), surface: Surface.Paper, scenic: true);
            left = Flat("Left wing", set.leftWing, -40);
            right = Flat("Right wing", set.rightWing, -38);
            leftDressing = Flat("Stage left dressing", set.leftDressing, -42);
            rightDressing = Flat("Stage right dressing", set.rightDressing, -43);
            printedSurf = art.breakers;
            if (set.moored)
                surf = Flat("Breaker groundrow", printedSurf, -69);
            if (moonlit)
            {
                mooring = Flat("Downstage mooring", art.mooring, -31);
                creel = Flat("Derrick catch basket", art.creel, -36);
            }
            if (moonlit || set.turntable)
            {
                house = DockPlayhouse.Bind(stage.transform, panel);
                house.Set.Sky = sky;
                house.Set.Scenery = scenery;
                house.Set.Floor = deck;
                house.Set.NativeEntrance = true;
                sky.GetComponent<MeshFilter>().sharedMesh = house.PanoramaPattern;
                deck.GetComponent<MeshFilter>().sharedMesh = house.FloorPattern;
            }
            else
                audience = new AudienceForeground(stage.transform, art.paperShader, art.seatBack, art.apronWood);
            if (moonlit)
            {
                if (right != null)
                {
                    // A timber shoe gives the flat a visible contact face on the playing boards.
                    var shoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shoe.name = "Derrick timber footing";
                    shoe.transform.SetParent(stage.transform, false);
                    var collider = shoe.GetComponent<Collider>();
                    collider.enabled = false;
                    Object.Destroy(collider);
                    derrickFooting = shoe.GetComponent<MeshRenderer>();
                    derrickFooting.sharedMaterial = paper;
                    derrickFooting.sortingOrder = -39;
                    Shape(derrickFooting, set.floor != null ? set.floor : set.region, new Vector4(.84f,.35f,0,0),
                        new Color(.63f,.55f,.43f), surface: Surface.Boards);
                }
                lantern = house.LightMoon();
                moonLamp = lantern.GetComponentInChildren<Light>();
                hazeMesh = MakeSheet(1, HazeSegments);
                hazeMesh.MarkDynamic();
                hazeVertices = new Vector3[2 * (HazeSegments + 1)];
                haze = Flat("Moonlit harbour mist", Texture2D.whiteTexture, -52, Surface.Haze);
                haze.GetComponent<MeshFilter>().sharedMesh = hazeMesh;
                leftShadow = MakeWingShadow(left, set.leftWing);
                rightShadow = MakeWingShadow(right, set.rightWing);
                house.Set.Atmosphere.UnionWith(new[] { haze, leftShadow, rightShadow });
            }
            drapeAspect = (float)art.drapery.width / art.drapery.height;
            if (house == null)
            {
                curtainLeft = Flat("Left drape", art.drapery, 32, Surface.Unlit);
                curtainRight = Flat("Right drape", art.drapery, 33, Surface.Unlit);
            }
            panel.RegisterCallback<PointerMoveEvent>(OnPointer, TrickleDown.TrickleDown);
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            Camera.onPreCull += BeforeCull;
            Update(now, 0.3f, 0f, 0f);
        }

        private void OnPointer(PointerMoveEvent evt)
        {
            if (panel.layout.width > 0)
                pointerX = Mathf.Clamp(evt.position.x / panel.layout.width * 2 - 1, -1, 1);
        }

        public void Update(double now, float baseline, float actionFocus, float dt)
        {
            float t = Mathf.Max(0, (float)(now - openedAt));
            if (house != null)
                house.Frame(camera);
            halfHeight = CameraDistance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            halfWidth = halfHeight * camera.aspect;
            if (house != null)
            {
                cameraHeight = camera.transform.position.y;
                // Intersect the feet of the HUD formation with the boards instead of moving the auditorium.
                var ray = camera.ViewportPointToRay(new Vector3(.5f, Mathf.Clamp(baseline, .08f, .46f), 0));
                float floorZ = ray.origin.z - ray.direction.z * ray.origin.y / ray.direction.y;
                castDistance = CameraDistance + Mathf.Max(house.DownstageLimit, floorZ);
                if (castOnly) { DrawActors(); return; }
            }
            else
            {
                cameraHeight = (.5f - Mathf.Clamp(baseline, .08f, .46f)) * 2 * halfHeight;
                float target = .52f * pointerX + .30f * actionFocus;
                dolly = Mathf.Lerp(dolly, target, 1 - Mathf.Exp(-3.5f * dt));
                float x = dolly - 1.35f * (1 - Mathf.SmoothStep(0, 1, t / 1.35f));
                camera.transform.SetPositionAndRotation(new Vector3(x, cameraHeight, -CameraDistance), Quaternion.identity);
                var projection = Matrix4x4.Perspective(FieldOfView, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
                projection.m02 = -x / halfWidth;
                camera.projectionMatrix = projection;
                audience.Frame(camera, x);
            }

            var backdrop = set.backcloth != null ? set.backcloth : set.region;
            float farZ = house != null ? house.SceneryOpening.center.z : 24f;
            float farScale = DepthScale(farZ);
            if (house != null)
            {
                var uv = house.DressPanorama(sky.transform, backdrop);
                Shape(sky, scenery.Current, uv, Color.white, surface: Surface.Sky);
            }
            else
            {
                Shape(sky, scenery.Current, new Vector4(1,1,0,0), Color.white, surface: Surface.Sky);
                float skyHeight = Mathf.Max(halfHeight * 2.05f, halfWidth * 2.12f * backdrop.height / backdrop.width);
                Place(sky, .5f, .06f, farZ, skyHeight * farScale, backdrop);
                float skyWidth = Mathf.Max(sky.transform.localScale.x, halfWidth * 2.22f * farScale);
                sky.transform.localScale = new Vector3(skyWidth, sky.transform.localScale.y, 1);
            }

            float deckBack = house != null ? farZ : 5f;
            float deckWidth = Mathf.Max(22f, halfWidth * 2.4f * DepthScale(deckBack));
            var front = new Vector3(0, -.02f, -13f);
            if (house != null)
            {
                var timber = house.DeckBounds;
                // One millimetre of scenic cloth over the load-bearing boards.
                front = new Vector3(timber.center.x, timber.max.y + .001f, timber.min.z);
                deckWidth = timber.size.x;
            }
            float deckDepth = deckBack - front.z;
            deck.transform.SetPositionAndRotation(front, Quaternion.Euler(90, 0, 0));
            deck.transform.localScale = new Vector3(deckWidth, deckDepth, 1);
            var tile = set.turntable ? new Vector2(5.4f, 1.8f) : house != null ? DockPlayhouse.PlankTile : new Vector2(9f, 4.2f);
            var deckUv = set.floor != null
                ? new Vector4(deckWidth / tile.x, deckDepth / tile.y, 0, 0)
                : new Vector4(1, .19f, 0, 0);
            if (house != null && set.floor != null)
            {
                // Keep the plank grain stationary under the actors as the camera reframes the apron.
                deckUv.z = (front.x - deckWidth * .5f) / tile.x;
                deckUv.w = front.z / tile.y;
            }
            Shape(deck, set.floor != null ? set.floor : set.region, deckUv, Color.white, surface: Surface.Boards);

            if (upstage != null)
            {
                if (set.moored)
                {
                    float tide = Mathf.Sin(t * 1.15f);
                    float shipZ = house != null ? front.z + .80f * deckDepth : 8.2f;
                    float shipHeight = 2.55f * DepthScale(shipZ);
                    var berth = new Vector3(-.10f * halfWidth, .015f, shipZ);
                    upstage.transform.position = berth + Vector3.up * (.028f * (1 + tide));
                    upstage.transform.localScale = new Vector3(shipHeight * set.upstage.width / set.upstage.height, shipHeight, 1);
                    if (surf != null)
                    {
                        float waveHeight = .55f * DepthScale(shipZ);
                        surf.transform.position = berth + new Vector3(.035f*tide, 0, -.45f);
                        surf.transform.localScale = new Vector3(waveHeight * printedSurf.width / printedSurf.height, waveHeight, 1);
                        surf.transform.rotation = Quaternion.Euler(0, -3, -.43f * tide);
                    }
                    upstage.transform.rotation = Quaternion.Euler(0, 0, .45f * tide);
                }
                else
                {
                    float z = set.turntable ? front.z + .86f * deckDepth : deckBack - .2f;
                    upstage.transform.position = new Vector3(-.17f * halfWidth * DepthScale(z), 0, z);
                    upstage.transform.localScale = new Vector3(2.75f * set.upstage.width / set.upstage.height, 2.75f, 1);
                }
            }
            SetWing(left, set.leftWing, house != null ? .177f : set.rightWing == null ? .18f : .10f, house != null ? front.z + deckDepth * .72f : 3.5f, house != null ? 2.20f : 3.0f, t);
            SetWing(right, set.rightWing, house != null ? .828f : .88f, house != null ? front.z + deckDepth * .72f : 4.6f, house != null ? 2.62f : 3.55f, t - 0.12f);
            if (house != null)
            {
                SetWing(leftDressing, set.leftDressing, .261f, front.z + .79f * deckDepth, 1.47f, t);
                SetWing(rightDressing, set.rightDressing, .755f, front.z + .68f * deckDepth, 1.12f, t);
                SetWing(mooring, stageArt.mooring, .125f, front.z + .56f * deckDepth, .48f, t);
                SetWing(creel, stageArt.creel, .787f, front.z + .62f * deckDepth, .64f, t);
            }
            if (derrickFooting != null)
            {
                var foot = right.transform;
                derrickFooting.transform.SetPositionAndRotation(foot.position + new Vector3(.15f, .075f, .02f),
                    Quaternion.Euler(0, -7, 0));
                derrickFooting.transform.localScale = new Vector3(foot.localScale.x * .80f, .15f, .58f);
                foot.position += Vector3.up * .143f;
            }
            if (moonlit)
                AimMoonlight();
            if (curtainLeft != null)
            {
                SetDrape(curtainLeft, false);
                SetDrape(curtainRight, true);
            }
            DrawActors();
        }

        private void AimMoonlight()
        {
            var source = lantern.position;
            var landing = new Vector3(.12f * halfWidth, .01f, -.45f);
            moonLamp.transform.LookAt(landing);
            float poolWidth = Mathf.Max(3.4f, .72f * halfWidth);
            paper.SetFloat(PlayingHalfWidth, halfWidth);
            paper.SetVector(MoonWorld, source);
            paper.SetVector(MoonLanding, new Vector4(landing.x, landing.y, landing.z, poolWidth));

            // Stretch the mist from the lamp face to the pool on the boards.
            var mouth = Vector3.right * poolWidth * 1.02f;
            var discEdge = Vector3.right * (lantern.lossyScale.y * .73f);
            var frame = haze.transform;
            // Subdivide the taper so the soft streaks follow the fan across its full depth.
            for (int row = 0; row <= HazeSegments; row++)
            {
                float t = (float)row / HazeSegments;
                var centre = Vector3.Lerp(landing, source, t);
                var edge = Vector3.Lerp(mouth, discEdge, t);
                hazeVertices[row * 2] = frame.InverseTransformPoint(centre - edge);
                hazeVertices[row * 2 + 1] = frame.InverseTransformPoint(centre + edge);
            }
            hazeMesh.vertices = hazeVertices;
            hazeMesh.RecalculateBounds();
            // The shadow shader moves vertices away from their caster and onto the ground.
            if (leftShadow != null) leftShadow.bounds = deck.bounds;
            if (rightShadow != null) rightShadow.bounds = deck.bounds;
        }

        private MeshRenderer MakeWingShadow(MeshRenderer wing, Texture2D ink)
        {
            if (wing == null) return null;
            var shadow = MakeCastShadow(wing);
            Shape(shadow, ink, new Vector4(1, 1, 0, 0), Color.white, surface: Surface.CastShadow);
            return shadow;
        }

        private MeshRenderer MakeCastShadow(MeshRenderer caster)
        {
            var cast = Sheet("Moon shadow", -53);
            cast.transform.SetParent(caster.transform, false);
            return cast;
        }

        private void SetWing(MeshRenderer wing, Texture2D texture, float x, float z, float height, float t)
        {
            if (wing == null)
                return;
            float w = height * texture.width / texture.height;
            wing.transform.position = new Vector3((x * 2 - 1) * halfWidth * DepthScale(z), 0, z);
            wing.transform.localScale = new Vector3(w, height, 1);
            wing.transform.localRotation = Quaternion.Euler(
                (house != null && AdventureFlow.OnRoute ? 0 : -67f) * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.82f))), x < 0.5f ? 7 : -7, 0);
        }

        private void SetDrape(MeshRenderer piece, bool flip)
        {
            const float z = -4f;
            float k = DepthScale(z);
            float height = halfHeight * 0.78f * k;
            // The drape is pinned by its outer top corner, so it only frames the playing area.
            float width = Mathf.Min(height * drapeAspect, halfWidth * 0.42f * k);
            float edge = (halfWidth * 1.025f + 0.23f) * k;
            piece.transform.position = new Vector3((flip ? 1 : -1) * (edge - width * .5f),
                cameraHeight + halfHeight * 1.04f * k - height, z);
            piece.transform.localScale = new Vector3(flip ? -width : width, height, 1);
        }

        private static float DepthScale(float z) => (CameraDistance + z) / CameraDistance;

        private void Place(MeshRenderer view, float x, float y, float z, float height, Texture2D texture)
        {
            float k = DepthScale(z);
            view.transform.position = new Vector3((2 * x - 1) * halfWidth * k,
                cameraHeight + (2 * y - 1 + camera.projectionMatrix.m12) * halfHeight * k, z);
            view.transform.localScale = new Vector3(height * texture.width / texture.height, height, 1);
        }

        private MeshRenderer Flat(string name, Texture2D texture, int order, Surface surface = Surface.Paper)
        {
            if (texture == null)
                return null;
            var view = Sheet(name, order);
            Shape(view, texture, new Vector4(1, 1, 0, 0), Color.white,
                surface: surface, scenic: surface == Surface.Paper);
            return view;
        }

        private MeshRenderer Sheet(string name, int order)
        {
            var piece = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(stage.transform, false);
            piece.GetComponent<MeshFilter>().sharedMesh = quad;
            var view = piece.GetComponent<MeshRenderer>();
            view.sharedMaterial = paper;
            view.sortingOrder = order;
            view.shadowCastingMode = ShadowCastingMode.Off;
            view.receiveShadows = false;
            return view;
        }

        private void Shape(MeshRenderer view, Texture texture, Vector4 uv, Color color, Vector4 walk = default,
            bool shadow = false, Surface surface = Surface.Unlit, bool scenic = false)
        {
            block.Clear();
            block.SetTexture(Map, texture);
            block.SetVector(UV, uv);
            block.SetColor(Ink, color);
            block.SetVector(Walk, walk);
            block.SetFloat(Contact, shadow ? 1f : 0f);
            block.SetFloat("_ScenicFlat", scenic ? 1f : 0f);
            if (moonlit)
            {
                block.SetFloat(MoonSurface, (float)surface);
                if (surface == Surface.Sky || surface == Surface.Paper)
                    block.SetVector(InkTexelSize, new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
            }
            view.SetPropertyBlock(block);
        }

        public void Attach(StageActorImage image)
        {
            if (disposed || actors.ContainsKey(image))
                return;
            var actor = spare.Count == 0 ? new Actor { body = Sheet("Actor", 0), shadow = Sheet("Foot shadow", -10) } : spare.Pop();
            actor.body.name = image.name;
            actor.body.sortingOrder = image.InMotion ? 5 : 1;
            var mesh = image.Walk.z != 0 ? walkMesh : quad;
            actor.body.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (moonlit)
            {
                actor.castShadow ??= MakeCastShadow(actor.body);
                actor.castShadow.GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            Shape(actor.shadow, Texture2D.whiteTexture, new Vector4(1, 1, 0, 0), new Color(0.015f, 0.019f, 0.027f, .52f), shadow: true);
            if (house != null)
            {
                house.Set.Cast[actor.body] = image.Performer;
                house.Set.Atmosphere.Add(actor.shadow);
                if (actor.castShadow != null) house.Set.Atmosphere.Add(actor.castShadow);
            }
            actors.Add(image, actor);
        }

        public void Detach(StageActorImage image)
        {
            if (!actors.Remove(image, out var actor))
                return;
            actor.body.enabled = actor.shadow.enabled = false;
            if (moonlit) actor.castShadow.enabled = false;
            spare.Push(actor);
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera rendering) => BeforeCull(rendering);
        private void BeforeCull(Camera rendering)
        {
            if (rendering == camera)
                DrawActors();
        }

        private void DrawActors()
        {
            var screen = panel.worldBound;
            if (!(screen.width > 0 && screen.height > 0))
                return;
            foreach (var pair in actors)
            {
                var image = pair.Key;
                var actor = pair.Value;
                var rect = image.contentRect;
                bool show = image.visible && image.image != null && rect.width > 0 && rect.height > 0 &&
                    image.resolvedStyle.opacity > 0.001f && screen.Overlaps(image.worldBound);
                actor.body.enabled = actor.shadow.enabled = show;
                if (moonlit) actor.castShadow.enabled = show;
                if (!show)
                    continue;
                var source = image.sourceRect;
                var texture = image.image;
                if (source.width <= 0 || source.height <= 0)
                    source = new Rect(0, 0, texture.width, texture.height);
                if (image.scaleMode == ScaleMode.ScaleToFit)
                {
                    float factor = Mathf.Min(rect.width / source.width, rect.height / source.height);
                    var size = source.size * factor;
                    rect = new Rect(rect.center - size * .5f, size);
                }
                var bottomLeft = Project(image.LocalToWorld(new Vector2(rect.xMin, rect.yMax)), screen);
                var bottomRight = Project(image.LocalToWorld(rect.max), screen);
                var topLeft = Project(image.LocalToWorld(rect.min), screen);
                var up = topLeft - bottomLeft;
                var across = bottomRight - bottomLeft;
                float width = across.magnitude;
                var foot = (bottomLeft + bottomRight) * .5f;
                float sign = Vector3.Cross(across, up).z < 0 ? -1 : 1;
                actor.body.transform.SetPositionAndRotation(foot, Quaternion.Euler(0, 0, Mathf.Atan2(-up.x, up.y) * Mathf.Rad2Deg));
                actor.body.transform.localScale = new Vector3(sign * width, up.magnitude, 1);
                var tint = image.Ink;
                tint.a *= image.resolvedStyle.opacity;
                var frameUV = new Vector4((source.width - 1) / texture.width, (source.height - 1) / texture.height,
                    (source.x + .5f) / texture.width, 1 - (source.yMax - .5f) / texture.height);
                Shape(actor.body, texture, frameUV, tint, image.Walk, surface: Surface.Paper);
                if (moonlit)
                {
                    actor.castShadow.bounds = deck.bounds;
                    Shape(actor.castShadow, texture, frameUV, tint, image.Walk, surface: Surface.CastShadow);
                }
                actor.shadow.transform.position = new Vector3(foot.x, house != null ? .004f : 0, foot.z - .43f);
                actor.shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
                actor.shadow.transform.localScale = new Vector3(width * .56f, .9f, 1);
            }
        }

        private Vector3 Project(Vector2 p, Rect screen)
        {
            float x = (p.x - screen.xMin) / screen.width;
            float y = 1 - (p.y - screen.yMin) / screen.height;
            if (house != null)
                return camera.ViewportToWorldPoint(new Vector3(x, y, castDistance));
            return new Vector3((2 * x - 1) * halfWidth, cameraHeight + (2 * y - 1) * halfHeight, 0);
        }

        internal static Mesh MakeSheet(int cols, int rows)
        {
            int stride = cols + 1;
            var points = new Vector3[stride * (rows + 1)];
            var uv = new Vector2[points.Length];
            var indices = new int[cols * rows * 6];
            int index = 0;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= cols; x++)
                {
                    int i = y * stride + x;
                    uv[i] = new Vector2((float)x / cols, (float)y / rows);
                    points[i] = new Vector3(uv[i].x - .5f, uv[i].y, 0);
                    if (x == cols || y == rows)
                        continue;
                    indices[index++] = i;
                    indices[index++] = i + stride;
                    indices[index++] = i + 1;
                    indices[index++] = i + 1;
                    indices[index++] = i + stride;
                    indices[index++] = i + stride + 1;
                }
            return new Mesh { name = "Printed sheet", vertices = points, uv = uv, triangles = indices };
        }

        public void Dispose()
        {
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            Camera.onPreCull -= BeforeCull;
            panel.UnregisterCallback<PointerMoveEvent>(OnPointer, TrickleDown.TrickleDown);
            if (!castOnly && camera != null)
            {
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.orthographic = oldOrtho;
                camera.fieldOfView = oldFov;
                camera.clearFlags = oldClear;
                camera.backgroundColor = oldBackground;
            }
            if (castOnly)
            {
                foreach (var mesh in stage.GetComponentsInChildren<MeshRenderer>())
                {
                    house.Set.Cast.Remove(mesh);
                    house.Set.Atmosphere.Remove(mesh);
                }
            }
            else house?.Unbind(stage.transform);
            audience?.Dispose();
            stage.SetActive(false);
            Object.Destroy(stage);
            Object.Destroy(quad);
            Object.Destroy(walkMesh);
            if (hazeMesh != null) Object.Destroy(hazeMesh);
            Object.Destroy(paper);
            actors.Clear();
            spare.Clear();
        }
    }
}
