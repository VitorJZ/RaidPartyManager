using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage : ServiceStage
    {
        internal enum Expression { Idle, Happy, Delighted, Laugh }

        private const float EquipmentDesk = -2.30f, BookDesk = 2.10f;
        private static readonly Vector3 CounterEye = new(0, 2.86f, -9.20f);
        private static readonly Vector3 CounterAim = new(0, 1.26f, -2.7f);
        private static readonly Quaternion CounterAngle = Quaternion.LookRotation(CounterAim - CounterEye);
        private const float EquipmentWidth = 4.16f, BookWidth = 3.20f;
        private const int StockCapacity = AdventureShopOffer.EquipmentSlots + AdventureShopOffer.BookSlots;
        private readonly Texture2D storytellerIdle;
        private const int CounterPortraitPixels = 330;
        private const float ItemSize = .61f;
        private const float CounterHeight = 1.02f;
        private readonly MeshRenderer merchant;
        private readonly Texture2D reactions;
        private readonly Transform[] tabletop = new Transform[StockCapacity];
        private readonly Transform[] priceFaces = new Transform[StockCapacity];
        private readonly MeshFilter merchantFilter;
        private readonly Mesh merchantRestMesh, merchantWalkMesh;
        private float merchantX = EquipmentDesk, deskX = EquipmentDesk, counterX = EquipmentDesk, stridePhase;
        private bool merchantWalking;
        private int lastCel = -1;
        private Expression lastExpression;
        private Vector3 wideEye;
        private Quaternion wideAngle;
        private Matrix4x4 wideProjection;
        private float wideAspect;
        private float counterFocus;
        internal bool CounterFocused { get; set; }
        internal bool BooksFocused => deskX == BookDesk;
        internal bool CounterReady => CounterFocused && counterFocus >= 1f && focus <= 0 && counterX == deskX && merchantX == deskX;
        internal bool CounterAvailable(bool books) =>
            !RecruitmentFocused && focus <= 0 && (CounterFocused ? BooksFocused != books : counterFocus <= 0);

        internal void FocusCounter(bool books)
        {
            deskX = books ? BookDesk : EquipmentDesk;
            CounterFocused = true;
        }
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
                merchant = Flat("shop-merchant", new Vector3(merchantX, 1.65f, -1.65f), 2f, 1.62f, storytellerIdle);
                merchantFilter = merchant.GetComponent<MeshFilter>();
                merchantRestMesh = merchantFilter.sharedMesh;
                merchantWalkMesh = BattleStage.MakeSheet(14, 18);
                Dressing.Cast[merchant] = new DockSet.Player(true);
                SetMerchant(Expression.Idle, 0, 0);
            }
            if (venue == StageVenue.Outdoors)
                BuildCounter(0, 3.42f);
            else
            {
                BuildCounter(EquipmentDesk, EquipmentWidth);
                BuildCounter(BookDesk, BookWidth);
            }
            CaptureWideView();
        }

        private void BuildCounter(float x, float width)
        {
            var wood = new Color(.82f, .73f, .62f);
            var brass = new Color(.76f, .52f, .26f);
            Shadow(new Vector3(x, 0, -2.58f), width + .43f, 1.55f);
            var hinge = new Vector2(x, -2.6f);
            Dressing.Hinges[Box("Counter carcass", new Vector3(x, .45f, -2.60f), new Vector3(width - .2f, .90f, .86f), art.timber, wood)] = hinge;
            Dressing.Hinges[Flat("Counter joinery", new Vector3(x, .45f, -3.038f), width - .24f, .86f, art.counterFront)] = hinge;
            Dressing.Hinges[Box("Counter plinth", new Vector3(x, .07f, -2.60f), new Vector3(width - .11f, .14f, .93f), art.timber, wood)] = hinge;
            Dressing.Hinges[Box("Counter top", new Vector3(x, .96f, -2.60f), new Vector3(width, .12f, 1.03f), art.timber, wood)] = hinge;
            Dressing.Hinges[Box("Counter brass inlay", new Vector3(x, .94f, -3.12f), new Vector3(width - .12f, .029f, .02f), null, brass)] = hinge;
        }

        internal void ClearTableStock()
        {
            foreach (var display in tabletop)
            {
                if (display == null) continue;
                foreach (var renderer in display.GetComponentsInChildren<MeshRenderer>(true)) Dressing.Hinges.Remove(renderer);
                display.gameObject.SetActive(false);
                Object.Destroy(display.gameObject);
            }
            System.Array.Clear(tabletop, 0, tabletop.Length);
            System.Array.Clear(priceFaces, 0, priceFaces.Length);
        }

        public void SetTableItem(int index, int tableSlot, EquipmentDefinition item, Texture2D icon, bool sold)
        {
            bool scroll = item is SkillBookDefinition;
            float centre = scroll ? BookDesk : EquipmentDesk;
            int capacity = scroll ? AdventureShopOffer.BookSlots : AdventureShopOffer.EquipmentSlots;
            float x = centre + (tableSlot - (capacity - 1) * .5f) * .76f;
            var display = new GameObject("Table stock " + index).transform;
            display.SetParent(stage, false);
            display.localPosition = new Vector3(x, CounterHeight, -2.53f);
            tabletop[index] = display;
            Part(Box("Stock display tray", Vector3.zero, new Vector3(.69f, .04f, .52f), art.timber,
                new Color32(104, 71, 50, 255)), display, new Vector3(0, .02f, -.04f));
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
                Dressing.Hinges[renderer] = new Vector2(centre, -2.6f);
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
            bool moving = merchantX != deskX;
            bool running = deskX == EquipmentDesk;
            bool idle = expression == Expression.Idle;
            int frame = AnimationCels.Loop(time, 1.2);
            if (moving != merchantWalking || lastCel != frame || lastExpression != expression || moving)
            {
                merchant.GetPropertyBlock(properties);
                if (moving)
                {
                    float height = 1.62f * 512f / CounterPortraitPixels;
                    merchantFilter.sharedMesh = merchantWalkMesh;
                    float sign = running ? 1 : -1;
                    merchant.transform.localScale = new Vector3(sign * height * 410f / 512f, height, 1);
                    properties.SetTexture("_MainTex", storytellerIdle);
                    properties.SetVector("_MainTex_ST", BrokerIdleFrame(frame));
                    properties.SetVector("_Walk", new Vector4(Mathf.Cos(stridePhase), Mathf.Sin(stridePhase), -sign, running ? .15f : .09f));
                }
                else
                {
                    var uv = idle ? BrokerIdleFrame(frame, CounterPortraitPixels) :
                        AnimationCels.UV((expression == Expression.Delighted ? AnimationCels.Count : 0) + frame, 8, 8);
                    merchantFilter.sharedMesh = merchantRestMesh;
                    merchant.transform.localScale = new Vector3(1.62f * (idle ? 410f / CounterPortraitPixels :
                        reactions.width / (float)reactions.height), 1.62f, 1);
                    properties.SetTexture("_MainTex", idle ? storytellerIdle : reactions);
                    properties.SetVector("_MainTex_ST", uv);
                    properties.SetVector("_Walk", Vector4.zero);
                }
                merchant.SetPropertyBlock(properties);
                lastCel = frame;
                lastExpression = expression;
                merchantWalking = moving;
            }
            float y = moving ? .014f + Mathf.Abs(Mathf.Sin(stridePhase)) * (running ? .077f : .035f)
                : 1.65f + Mathf.Abs(Mathf.Sin((1 - celebration) * Mathf.PI * 3)) * .19f * celebration;
            merchant.transform.position = new Vector3(merchantX, y, -1.65f);
            merchant.transform.localRotation = Quaternion.Euler(0, 0, moving ? -Mathf.Sign(deskX - merchantX) * (running ? 8f : 4f)
                : idle ? 0 : Mathf.Sin(time * (expression == Expression.Laugh ? 18 : 4)) * (expression == Expression.Laugh ? 3 : .9f));
        }

        protected override Bounds FocusBounds()
        {
            var bounds = base.FocusBounds();
            foreach (var actor in recruitCast.Values) IncludeActor(ref bounds, actor.Body);
            if (counterFocus > 0)
            {
                var stock = new Bounds(CounterAim + Vector3.right * counterX, new Vector3(EquipmentWidth, .94f, 1.03f));
                bounds = StageTiltShift.BlendFocus(bounds, stock, Mathf.SmoothStep(0, 1, counterFocus));
            }
            if (focus > 0)
            {
                var wheelBounds = new Bounds(wheelFixture.position, new Vector3(2.04f, 2.36f, .38f));
                bounds = StageTiltShift.BlendFocus(bounds, wheelBounds, Mathf.SmoothStep(0, 1, focus));
            }
            return bounds;
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
            float previousX = merchantX;
            merchantX = Mathf.MoveTowards(merchantX, deskX, elapsed * (BooksFocused ? 3.7f : 6.8f));
            stridePhase += Mathf.Abs(merchantX - previousX) * (BooksFocused ? 5.9f : 4.6f);
            counterX = counterFocus <= 0 ? deskX : Mathf.MoveTowards(counterX, deskX, elapsed * 12);
            counterFocus = Mathf.MoveTowards(counterFocus, CounterFocused ? 1 : 0, elapsed / .32f);
            focus = Mathf.MoveTowards(focus, RecruitmentFocused ? 1 : 0, elapsed / .46f);
            float counter = Mathf.SmoothStep(0, 1, counterFocus);
            float recruit = Mathf.SmoothStep(0, 1, focus);
            camera.transform.SetPositionAndRotation(
                Vector3.Lerp(Vector3.Lerp(wideEye, CounterEye + Vector3.right * (counterX * .62f) +
                    Vector3.back * Mathf.Max(0, (1.5f - camera.aspect) * 6), counter), RecruitmentEye, recruit),
                Quaternion.Slerp(wideAngle, CounterAngle, counter * (1 - recruit)));
            var projection = wideProjection;
            projection.m12 *= (1 - counter) * (1 - recruit);
            camera.projectionMatrix = projection;
        }

        public void PlaceOffer(VisualElement view, Label price, int index)
        {
            float x = tabletop[index].position.x;
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

        public void PlaceCounterVisit(VisualElement target, bool books)
        {
            float x = books ? BookDesk : EquipmentDesk;
            float halfWidth = (books ? BookWidth : EquipmentWidth) * .5f;
            var left = camera.WorldToViewportPoint(new Vector3(x - halfWidth, 1.52f, -2.58f));
            var right = camera.WorldToViewportPoint(new Vector3(x + halfWidth, .04f, -3.13f));
            float start = Mathf.Clamp01(left.x), end = Mathf.Clamp01(right.x);
            target.style.position = Position.Absolute;
            target.style.left = Length.Percent(start * 100);
            target.style.width = Length.Percent(Mathf.Max(0, end - start) * 100);
            target.style.top = Length.Percent((1 - left.y) * 100);
            target.style.height = Length.Percent(Mathf.Max(0, left.y - right.y) * 100);
        }
    }
}
