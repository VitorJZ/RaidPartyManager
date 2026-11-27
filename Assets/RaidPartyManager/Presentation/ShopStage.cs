using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage : ServiceStage
    {
        internal enum Expression { Idle, Happy, Delighted, Laugh }

        private static readonly Vector3 CounterEye = new(0, 2.76f, -8.05f);
        private static readonly Quaternion CounterAngle = Quaternion.LookRotation(new Vector3(0, 1.26f, -2.7f) - CounterEye);
        private readonly Texture2D storytellerIdle;
        private const int CounterPortraitPixels = 330;
        private const float ItemSize = .61f;
        private const float CounterHeight = 1.02f;
        private readonly MeshRenderer merchant;
        private readonly Texture2D reactions;
        private readonly Transform[] tabletop = new Transform[4];
        private readonly Transform[] priceFaces = new Transform[4];
        private readonly Vector3 merchantCentre = new(0, 1.65f, -1.65f);
        private int lastCel = -1;
        private Expression lastExpression;
        private Vector3 wideEye;
        private Quaternion wideAngle;
        private Matrix4x4 wideProjection;
        private float wideAspect;
        private float counterFocus;
        internal bool CounterFocused { get; set; }
        internal bool CounterReady => CounterFocused && counterFocus >= 1f && focus <= 0;
        internal bool WideView => !CounterFocused && !RecruitmentFocused && counterFocus <= 0 && focus <= 0;

        public ShopStage(Transform owner, VisualElement panel, ServiceStageArt art, Texture2D interior, Texture2D reactions,
            AdventureRun run, StageVenue venue) : base(owner, panel, art, run?.InCapital == true
                ? StageLighting.Clockwork(venue) : StageLighting.For(run?.InGraveyard == true, venue),
                venue == StageVenue.Interior ? StageTiltShift.FacilityStrength : 1f)
        {
            bool graveyard = run?.InGraveyard == true;
            bool capital = run?.InCapital == true;
            this.reactions = reactions;
            if (venue == StageVenue.Outdoors)
            {
                if (capital) DressCapital(Mathf.Clamp((run.Stage - 1) / 3, 0, 2));
                else if (graveyard)
                {
                    HangBackdrop("Yew walk", art.yewPath);
                    GraveyardFloor();
                    FloorProp("Trail ferns", art.ferns, -3.8f, .6f, 2.55f, -8);
                    FloorProp("Funeral urn", art.stoneUrn, 4.5f, .4f, 1.4f, 8);
                }
                else
                {
                    HangBackdrop("Distant harbour", art.townSky);
                    Deck(art.timber, DockPlayhouse.PlankTile, new Color(.76f, .71f, .65f));
                    FloorProp("Signal buoy rack", art.buoyRack, -3.8f, .45f, 2.25f, -6);
                    FloorProp("Downstage mooring", art.mooring, 4.8f, -.7f, 1.5f, 8);
                }
            }
            else
            {
                if (capital) DressCapital(1);
                else
                {
                    HangBackdrop(graveyard ? "Cemetery chandlery" : "Harbour chandlery", graveyard ? art.wakeShop : interior);
                    if (graveyard) GraveyardFloor();
                    else Deck(art.timber, DockPlayhouse.PlankTile, new Color(.76f, .77f, .79f));
                }
                FloorProp("Cloth deliveries",art.parcel,4.62f,-.95f,1.48f,-8);
                if (!capital) FloorProp(graveyard ? "Mortuary tapers" : "Oil merchant's shelves", graveyard ? art.candleRack : art.chandlerShelf,
                    -3.9f, .65f, 2.18f, -6);
                if (!capital) FloorProp(graveyard ? "Funeral urn" : "Tackle by the counter", graveyard ? art.stoneUrn : art.tackleChest,
                    3.67f, -.54f, graveyard ? 1.18f : 1.9f, 8);
                if (!capital && !graveyard) FloorProp("Creel by the loading bay",art.creel,-3.48f,-1.6f,1.23f,9);
                storytellerIdle = Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle");
                merchant = Flat("shop-merchant", merchantCentre, 2f, 1.62f, storytellerIdle);
                Dressing.Cast[merchant] = new DockSet.Player(true);
                SetMerchant(Expression.Idle, 0, 0);
            }
            var wood = new Color(.82f, .73f, .62f);
            var brass = new Color(.76f, .52f, .26f);
            Shadow(new Vector3(0, 0, -2.58f), 3.85f, 1.55f);
            Box("Counter carcass", new Vector3(0, .45f, -2.60f), new Vector3(3.22f, .90f, .86f), art.timber, wood);
            Flat("Counter joinery", new Vector3(0, .45f, -3.038f), 3.18f, .86f, art.counterFront);
            Box("Counter plinth", new Vector3(0, .07f, -2.60f), new Vector3(3.31f, .14f, .93f), art.timber, wood);
            Box("Counter top", new Vector3(0, .96f, -2.60f), new Vector3(3.42f, .12f, 1.03f), art.timber, wood);
            Box("Counter brass inlay", new Vector3(0, .94f, -3.12f), new Vector3(3.30f, .029f, .02f), null, brass);
            foreach (var renderer in stage.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name.StartsWith("Counter "))
                    Dressing.Hinges[renderer] = new Vector2(0,-2.6f);
            CaptureWideView();
        }

        private static float StockX(int index) => (index - 1.5f) * .80f;

        public void SetTableItem(int index, EquipmentDefinition item, Texture2D icon, bool sold)
        {
            if (tabletop[index] != null)
            {
                tabletop[index].gameObject.SetActive(false);
                Object.Destroy(tabletop[index].gameObject);
            }
            var display = new GameObject("Table stock " + index).transform;
            display.SetParent(stage, false);
            display.localPosition = new Vector3(StockX(index), CounterHeight, -2.53f);
            tabletop[index] = display;
            Part(Box("Stock display tray", Vector3.zero, new Vector3(.69f, .04f, .52f), art.timber,
                new Color32(104, 71, 50, 255)), display, new Vector3(0, .02f, -.04f));
            bool scroll = item is SkillBookDefinition;
            var artwork = scroll ? Resources.Load<Texture2D>("SkillBookScroll") ?? icon : icon;
            var body = Part(Flat("Stock artwork", Vector3.zero, ItemSize, ItemSize, artwork, Surface.ScenicFlat),
                display, new Vector3(0, .34f, .03f));
            body.transform.localRotation = Quaternion.Euler(12f, 0, 0);
            SetStockSilhouette(body);
            if (scroll && icon != null)
            {
                var emblem = Part(Flat("Scroll emblem", Vector3.zero, .61f, .61f, icon), body.transform, new Vector3(0, 0, -.012f));
                SetStockSilhouette(emblem);
            }
            var tabletSize = new Vector3(.52f, .18f, .045f);
            var tablet = Part(Box("Counter price tablet", Vector3.zero, tabletSize, art.timber,
                new Color32(142, 100, 65, 255)), display, new Vector3(0, .095f, -.49f));
            tablet.transform.localRotation = Quaternion.Euler(-13f, 0, 0);
            var face = Part(Shape("Counter price paper", PrimitiveType.Quad, Vector3.zero,
                new Vector3(tabletSize.x, tabletSize.y, 1), null, new Color32(237, 211, 155, 255), Surface.Paper),
                display, Vector3.zero).transform;
            face.SetPositionAndRotation(tablet.transform.TransformPoint(new Vector3(0, 0, -.53f)), tablet.transform.rotation);
            priceFaces[index] = face;
            foreach (var renderer in display.GetComponentsInChildren<MeshRenderer>())
                Dressing.Hinges[renderer] = new Vector2(0, -2.6f);
            display.gameObject.SetActive(!sold);
        }

        public void SellTableItem(int index) => tabletop[index].gameObject.SetActive(false);

        private void SetStockSilhouette(MeshRenderer renderer)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_CutoutThreshold", .38f);
            renderer.SetPropertyBlock(properties);
        }

        public void SetMerchant(Expression expression, float time, float celebration)
        {
            bool idle = expression == Expression.Idle;
            int frame = AnimationCels.Loop(time, 1.2);
            if (lastCel != frame || lastExpression != expression)
            {
                var uv = idle ? BrokerIdleFrame(frame, CounterPortraitPixels) : AnimationCels.UV((expression == Expression.Delighted ? AnimationCels.Count : 0) + frame, 8, 8);
                merchant.GetPropertyBlock(properties);
                merchant.transform.localScale = new Vector3(1.62f * (idle ? 410f / CounterPortraitPixels :
                    reactions.width / (float)reactions.height), 1.62f, 1);
                properties.SetTexture("_MainTex", idle ? storytellerIdle : reactions);
                properties.SetVector("_MainTex_ST", uv);
                merchant.SetPropertyBlock(properties);
                lastCel = frame;
                lastExpression = expression;
            }
            float hop = Mathf.Abs(Mathf.Sin((1 - celebration) * Mathf.PI * 3)) * .19f * celebration;
            merchant.transform.position = merchantCentre + Vector3.up * hop;
            merchant.transform.localRotation = Quaternion.Euler(0, 0, expression == Expression.Idle ? 0 :
                Mathf.Sin(time * (expression == Expression.Laugh ? 18 : 4)) * (expression == Expression.Laugh ? 3 : .9f));
        }

        private void CaptureWideView()
        {
            wideAspect = camera.aspect;
            wideEye = camera.transform.position;
            wideAngle = camera.transform.rotation;
            wideProjection = camera.projectionMatrix;
        }

        public void UpdateCamera(float elapsed)
        {
            if (wideAspect != camera.aspect) CaptureWideView();
            counterFocus = Mathf.MoveTowards(counterFocus, CounterFocused ? 1 : 0, elapsed / .32f);
            focus = Mathf.MoveTowards(focus, RecruitmentFocused ? 1 : 0, elapsed / .46f);
            float counter = Mathf.SmoothStep(0, 1, counterFocus);
            float recruit = Mathf.SmoothStep(0, 1, focus);
            camera.transform.SetPositionAndRotation(
                Vector3.Lerp(Vector3.Lerp(wideEye, CounterEye, counter), RecruitmentEye, recruit),
                Quaternion.Slerp(wideAngle, CounterAngle, counter * (1 - recruit)));
            var projection = wideProjection;
            projection.m12 *= (1 - counter) * (1 - recruit);
            camera.projectionMatrix = projection;
        }

        public void PlaceOffer(VisualElement view, Label price, int index)
        {
            float x = StockX(index);
            var a = camera.WorldToViewportPoint(new Vector3(x - .36f, 1.70f, -2.51f));
            var b = camera.WorldToViewportPoint(new Vector3(x + .36f, 1.015f, -3.045f));
            view.style.position = Position.Absolute;
            view.style.left = Length.Percent(100 * a.x);
            view.style.top = Length.Percent(100 * (1 - a.y));
            view.style.width = Length.Percent(100 * (b.x - a.x));
            view.style.height = Length.Percent(100 * (a.y - b.y));

            // The paper follows the tablet's plane. Only the lettering needs a UI projection.
            var face = priceFaces[index];
            var centre = camera.WorldToViewportPoint(face.position);
            var left = camera.WorldToViewportPoint(face.TransformPoint(new Vector3(-.5f, 0, 0)));
            var right = camera.WorldToViewportPoint(face.TransformPoint(new Vector3(.5f, 0, 0)));
            var top = camera.WorldToViewportPoint(face.TransformPoint(new Vector3(0, .5f, 0)));
            var bottom = camera.WorldToViewportPoint(face.TransformPoint(new Vector3(0, -.5f, 0)));
            float width = right.x - left.x;
            float height = top.y - bottom.y;
            price.style.left = Length.Percent(100 * (centre.x - width / 2 - a.x) / (b.x - a.x));
            price.style.top = Length.Percent(100 * (a.y - centre.y - height / 2) / (a.y - b.y));
            price.style.width = Length.Percent(100 * width / (b.x - a.x));
            price.style.height = Length.Percent(100 * height / (a.y - b.y));
            float panelHeight = view.panel?.visualTree.layout.height ?? 0;
            if (panelHeight > 0) price.style.fontSize = Mathf.Clamp(panelHeight * height * .55f, 8f, 22f);
        }

        public void PlaceCounter(VisualElement target) =>
            Pin(target, new Vector3(-1.71f, 1.04f, -2.08f), new Vector3(1.71f, .04f, -3.13f));

        public void PlaceCounterVisit(VisualElement target) =>
            Pin(target, new Vector3(-1.71f, 1.72f, -2.43f), new Vector3(1.71f, .04f, -3.13f));
    }
}
