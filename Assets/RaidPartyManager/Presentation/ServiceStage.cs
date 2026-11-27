using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // Geometry is authored in metres. Interaction rectangles are projected back onto the UI panel.
    internal abstract partial class ServiceStage : IDisposable
    {
        protected const float CompanySpriteSize = 1.62f;
        protected enum Surface { Backdrop, Solid, Paper, Shadow, Flame, Embers, ForestGround, Decking, ScenicFlat }
        protected readonly Camera camera;
        protected readonly Transform stage;
        protected readonly ServiceStageArt art;
        protected readonly Material paint;
        private readonly Material backdropPaint, softPaint, paperPaint;
        protected readonly DockPlayhouse house;
        protected readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Vector3 previousEye;
        private readonly Quaternion previousRotation;
        private readonly float previousFov;
        private readonly bool previousOrtho;
        private readonly CameraClearFlags previousClear;
        private readonly Color previousColor;
        private MeshRenderer cyclorama, ground;
        private Texture2D paintedScene;
        private float panoramaRepeats, panoramaVerticalOffset;
        private Vector2 metresPerTile;
        private float framedAspect;
        private readonly Dictionary<int, MeshRenderer> company = new();
        private int idleCel = -1;
        protected DockSet Dressing => house.Set;

        internal void ShowCompany(PartyRun party, EncounterData content, Predicate<string> visible = null)
        {
            foreach (var actor in company.Values) actor.enabled = false;
            foreach (var unit in party.CaptureFormation(Side.Party))
            {
                if (visible != null && !visible(unit.Id)) continue;
                int place = unit.Position;
                var idle = content.BattleSpriteFor(unit.Character.Id);
                var image = idle ?? content.BattlePortraitFor(unit.Character.Id) ?? content.PortraitFor(unit.Character.Id);
                if (image == null) continue;
                if (!company.TryGetValue(place, out var actor))
                {
                    // Leave the counter, price cards and town doorways clear behind the visitors.
                    float z = -5.3f + .42f * ((place-1) % 2);
                    float x = -1.83f - .86f * (place-1);
                    actor = Flat("Company " + place, new Vector3(x,.80f,z),CompanySpriteSize,CompanySpriteSize,image);
                    company[place] = actor;
                }
                actor.enabled = true;
                if (idle == null) SetFrame(actor, image, 1, 0);
                Dressing.Cast[actor] = new DockSet.Player(false, content.BattleWalkFor(unit.Character.Id), idle);
            }
            idleCel = -1;
            AnimateCompany();
        }

        internal void HideCompany()
        {
            foreach (var actor in company.Values) actor.enabled = false;
        }

        private void AnimateCompany()
        {
            if (company.Count == 0) return;
            int frame = AnimationCels.Loop(Time.unscaledTimeAsDouble, 1.6);
            if (frame == idleCel) return;
            idleCel = frame;
            foreach (var pair in company)
            {
                var actor = pair.Value;
                if (!actor.enabled || Dressing.Cast[actor].Idle == null) continue;
                int cel = (frame + 2 * pair.Key) % AnimationCels.Count;
                actor.GetPropertyBlock(properties);
                properties.SetTexture("_MainTex", Dressing.Cast[actor].Idle);
                properties.SetVector("_MainTex_ST",AnimationCels.UV(cel, AnimationCels.Count, 4));
                actor.SetPropertyBlock(properties);
            }
        }
        protected virtual float PropScale => 1f;

        protected ServiceStage(Transform owner, VisualElement panel, ServiceStageArt art, StageLighting lighting)
        {
            this.art = art;
            camera = Camera.main;
            previousEye = camera.transform.position;
            previousRotation = camera.transform.rotation;
            previousOrtho = camera.orthographic;
            previousFov = camera.fieldOfView;
            previousClear = camera.clearFlags;
            previousColor = camera.backgroundColor;
            camera.orthographic = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(25, 33, 41, 255);
            stage = new GameObject("Service theatre").transform;
            stage.SetParent(owner, false);
            paint = new Material(art.shader);
            backdropPaint = new Material(paint) { renderQueue = 1970 };
            softPaint = new Material(paint) { renderQueue = 3000 };
            softPaint.SetFloat("_DepthWrite", 0);
            house = DockPlayhouse.Bind(stage, panel);
            house.SetLighting(lighting);
            lighting.Apply(paint);
            // Leave one queue between solid scenery and the cast for the travelling wave.
            paperPaint = new Material(paint) { renderQueue = paint.renderQueue + 2 };
            lighting.Apply(backdropPaint);
            lighting.Apply(softPaint);
            FrameCamera();
        }

        public void FrameCamera()
        {
            AnimateCompany();
            Dressing.Scenery?.Apply(cyclorama, properties);
            if (framedAspect == camera.aspect) return;
            framedAspect = camera.aspect;
            house.Frame(camera);
            FitScenery();
        }

        private void FitScenery()
        {
            var bounds = house.DeckBounds;
            if (ground != null)
            {
                ground.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y + .001f, bounds.min.z);
                ground.transform.localScale = new Vector3(bounds.size.x, bounds.size.z, 1);
                ground.GetPropertyBlock(properties);
                properties.SetVector("_MainTex_ST", new Vector4(bounds.size.x / metresPerTile.x,
                    bounds.size.z / metresPerTile.y, bounds.min.x / metresPerTile.x, bounds.min.z / metresPerTile.y));
                properties.SetFloat("_BackEdge", bounds.max.z);
                ground.SetPropertyBlock(properties);
            }
            if (cyclorama == null) return;
            var uv = house.DressPanorama(cyclorama.transform, paintedScene, panoramaRepeats);
            uv.w += panoramaVerticalOffset;
            cyclorama.GetPropertyBlock(properties);
            properties.SetVector("_MainTex_ST", uv);
            cyclorama.SetPropertyBlock(properties);
        }

        protected MeshRenderer Shape(string name, PrimitiveType shape, Vector3 position, Vector3 scale,
            Texture2D image, Color tint, Surface surface, Vector2 tiling = default)
        {
            var obj = GameObject.CreatePrimitive(shape);
            obj.name = name;
            obj.transform.SetParent(stage, false);
            obj.transform.localPosition = position * PropScale;
            obj.transform.localScale = scale * PropScale;
            var collider = obj.GetComponent<Collider>();
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = surface switch
            {
                Surface.Backdrop => backdropPaint,
                Surface.Paper => paperPaint,
                Surface.Shadow or Surface.Embers => softPaint,
                _ => paint
            };
            properties.Clear();
            properties.SetFloat("_Surface", (float)surface);
            properties.SetTexture("_MainTex", image != null ? image : Texture2D.whiteTexture);
            properties.SetColor("_Color", tint);
            if (tiling == default) tiling = Vector2.one;
            properties.SetVector("_MainTex_ST", new Vector4(tiling.x, tiling.y, 0, 0));
            renderer.SetPropertyBlock(properties);
            if (surface is Surface.Shadow or Surface.Embers) Dressing.Atmosphere.Add(renderer);
            return renderer;
        }

        protected MeshRenderer Flat(string name, Vector3 centre, float width, float height, Texture2D image,
            Surface surface = Surface.Paper)
        {
            return Shape(name, PrimitiveType.Quad, centre, new Vector3(width, height, 1), image, Color.white, surface);
        }

        protected void FloorProp(string name, Texture2D image, float x, float z, float width, float yaw)
        {
            float height = width * image.height / image.width;
            var foot = new Vector3(x,0,z);
            var prop = Flat(name, foot + Vector3.up * (.012f + height*.5f), width, height, image, Surface.ScenicFlat);
            prop.transform.localRotation = Quaternion.Euler(0,yaw,0);
            Shadow(foot,width*.94f,height*.47f,.38f);
        }

        protected static Texture2D CapitalArt(string id) => Resources.Load<Texture2D>("Clockwork/" + id);

        protected void DressCapital(int district)
        {
            var (back, left, right) = district switch
            {
                1 => ("Furnace", "BoilerUnion", "StreetCurrent"),
                2 => ("Meridian", "LastMeridian", "ElectionPress"),
                _ => ("Terminus", "Gate", "WrongSwitch")
            };
            HangBackdrop(back, CapitalArt(back));
            Deck(CapitalArt("IronDeck"), new Vector2(3.1f, 2.4f), new Color(.76f,.77f,.75f));
            FloorProp(left, CapitalArt(left), -4.9f, .55f, 2.5f, -7);
            FloorProp(right, CapitalArt(right), 4.8f, -1.25f, 1.9f, 8);
        }

        protected void HangBackdrop(string name, Texture2D image, float horizontalRepeats = 1, float verticalOffset = 0)
        {
            paintedScene = image;
            panoramaRepeats = horizontalRepeats;
            panoramaVerticalOffset = verticalOffset;
            cyclorama = Flat(name, Vector3.zero, 1, 1, image, Surface.Backdrop);
            Dressing.Sky = cyclorama;
            Dressing.Scenery = new SceneryFlipbook(image);
            Dressing.Scenery.Apply(cyclorama, properties);
            cyclorama.GetComponent<MeshFilter>().sharedMesh = house.PanoramaPattern;
            FitScenery();
        }

        protected MeshRenderer Box(string name, Vector3 centre, Vector3 size, Texture2D texture, Color tint) =>
            Shape(name, PrimitiveType.Cube, centre, size, texture, tint, Surface.Solid);

        protected void GraveyardFloor() =>
            Deck(art.graveyardStone, new Vector2(5.4f, 1.8f), new Color(.78f, .75f, .83f));

        protected void Deck(Texture2D texture, Vector2 tileSize, Color tint, bool feather = false)
        {
            metresPerTile = tileSize;
            ground = Shape("Stage floor", PrimitiveType.Quad, Vector3.zero, Vector3.one, texture, tint,
                feather ? Surface.ForestGround : Surface.Decking);
            Dressing.Floor = ground;
            ground.GetComponent<MeshFilter>().sharedMesh = house.FloorPattern;
            ground.transform.rotation = Quaternion.Euler(90, 0, 0);
            FitScenery();
        }

        protected MeshRenderer Shadow(Vector3 point, float width, float depth, float alpha = .55f)
        {
            var r = Shape("Contact shadow", PrimitiveType.Quad, point + Vector3.up * .014f, new Vector3(width, depth, 1),
                null, new Color(1, 1, 1, alpha), Surface.Shadow);
            r.transform.rotation = Quaternion.Euler(90, 0, 0);
            return r;
        }

        protected static Vector4 BrokerIdleFrame(int frame, int visiblePixels = 512)
        {
            var uv = AnimationCels.UV(frame);
            float crop = uv.y * visiblePixels / 512f;
            uv.w += uv.y - crop;
            uv.y = crop;
            return uv;
        }

        protected void PoseBroker(MeshRenderer actor, int frame)
        {
            actor.GetPropertyBlock(properties);
            properties.SetVector("_MainTex_ST", BrokerIdleFrame(frame));
            actor.SetPropertyBlock(properties);
        }

        protected void SetFrame(MeshRenderer flat, Texture2D texture, int count, int frame)
        {
            flat.GetPropertyBlock(properties);
            properties.SetTexture("_MainTex", texture);
            properties.SetVector("_MainTex_ST", count == AnimationCels.Count ? AnimationCels.UV(frame) : new Vector4(1f / count, 1, (float)frame / count, 0));
            flat.SetPropertyBlock(properties);
        }

        protected void Pin(VisualElement view, Vector3 topLeft, Vector3 lowerRight)
        {
            // Percentages resolve on the very first layout, even before the document has pixel bounds.
            var a = camera.WorldToViewportPoint(topLeft * PropScale);
            var b = camera.WorldToViewportPoint(lowerRight * PropScale);
            view.style.position = Position.Absolute;
            view.style.left = Length.Percent(100 * a.x);
            view.style.top = Length.Percent(100 * (1 - a.y));
            view.style.width = Length.Percent(100 * (b.x - a.x));
            view.style.height = Length.Percent(100 * (a.y - b.y));
        }

        protected void FireUniforms(Light light, float time)
        {
            var p = light.transform.position;
            var source = new Vector4(p.x, p.y, p.z, light.intensity);
            paint.SetVector("_FireLight", source);
            paint.SetFloat("_FireRange", light.range);
            paint.SetFloat("_FlameTime", time);
            paperPaint.SetVector("_FireLight", source);
            paperPaint.SetFloat("_FireRange", light.range);
            paperPaint.SetFloat("_FlameTime", time);
            softPaint.SetVector("_FireLight", source);
            softPaint.SetFloat("_FlameTime", time);
        }

        public virtual void Dispose()
        {
            house.Unbind(stage);
            stage.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(stage.gameObject);
            UnityEngine.Object.Destroy(paint);
            UnityEngine.Object.Destroy(paperPaint);
            UnityEngine.Object.Destroy(softPaint);
            UnityEngine.Object.Destroy(backdropPaint);
            if (camera == null) return;
            camera.ResetProjectionMatrix();
            camera.transform.SetPositionAndRotation(previousEye, previousRotation);
            camera.fieldOfView = previousFov;
            camera.orthographic = previousOrtho;
            camera.clearFlags = previousClear;
            camera.backgroundColor = previousColor;
        }
    }
}
