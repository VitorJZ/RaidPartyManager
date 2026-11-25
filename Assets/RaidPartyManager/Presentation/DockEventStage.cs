using System;
using RaidPartyManager.Run;
using RaidPartyManager.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class DockEventStage : ServiceStage
    {
        internal bool Cemetery { get; }
        internal bool Capital { get; }

        internal Texture2D RouteStone => art.graveyardStone;

        public DockEventStage(Transform owner, VisualElement panel, ServiceStageArt art, bool treasure,
            AdventureEventKind? play = null, bool graveyard = false, bool capital = false, int cityDistrict = 0)
            : base(owner, panel, art, capital || CapitalStories.Contains(play) ? StageLighting.Clockwork(VenueFor(play)) : StageLighting.For(graveyard || GraveStories.Contains(play) || play == AdventureEventKind.GraveSeed, VenueFor(play)))
        {
            Cemetery = graveyard || GraveStories.Contains(play) || play == AdventureEventKind.GraveSeed;
            Capital = capital || CapitalStories.Contains(play);
            if (Capital)
            {
                DressCapital(CapitalStories.Contains(play) ? CapitalStories.For(play.Value).District : cityDistrict);
                return;
            }
            string cloth = play switch
            {
                AdventureEventKind.Investor => "SailLoft",
                AdventureEventKind.Ambush or AdventureEventKind.LockedChest => "Alley",
                AdventureEventKind.HatTrick => "Pavilion",
                AdventureEventKind.Forge => "Foundry",
                AdventureEventKind.AscensionTrial => "TrialGate",
                AdventureEventKind.Alchemist => "Laboratory",
                _ => null
            };
            var painted = cloth != null ? Resources.Load<Texture2D>("StoryEvents/Backcloth/" + cloth)
                : play == AdventureEventKind.LostTraveler ? art.campSky : art.townSky;
            if (Cemetery)
            {
                DressCemetery(play);
                return;
            }
            HangBackdrop("Event backcloth", painted);
            bool stone = play is AdventureEventKind.AscensionTrial or AdventureEventKind.Forge or AdventureEventKind.Ambush;
            Deck(stone ? art.cobbles : art.timber, stone ? new Vector2(1.8f,.78f) : DockPlayhouse.PlankTile,
                new Color(.76f,.71f,.65f));

            switch (play)
            {
                case AdventureEventKind.Alchemist:
                    FloorProp("Apothecary's drying herbs", art.herbDryer, -3.5f, .45f, 1.95f, -8);
                    FloorProp("Stock of glass bottles", art.chandlerShelf, 4.5f, .70f, 1.9f, 7);
                    break;
                case AdventureEventKind.HatTrick:
                    FloorProp("Performer’s wardrobe", art.vestmentStand, -4.25f, .64f, 2.05f, -7);
                    FloorProp("Travelling show baggage", art.canvasBundles, 4.60f, -.70f, 1.75f, 8);
                    break;
                case AdventureEventKind.Investor:
                    FloorProp("Sailmaker's tackle", art.tackleChest, -3.5f, -.2f, 2.3f, -5);
                    Crate(3.9f,1.35f,0,4);
                    FloorProp("Folded sailcloth",art.parcel,4.9f,.65f,1.4f,-8);
                    break;
                case AdventureEventKind.LostTraveler:
                    FloorProp("Travellers' packs", art.canvasBundles, -3.7f, -.76f, 2.0f, -6);
                    FloorProp("Trail ferns",art.ferns,3.65f,.60f,3.2f,-8);
                    FloorProp("Trail provisions",art.parcel,-.85f,-1.2f,1.05f,6);
                    break;
                case AdventureEventKind.Forge:
                    FloorProp("Kiln fuel",art.firewood,3.8f,-1.1f,1.75f,-5);
                    break;
                case AdventureEventKind.AscensionTrial:
                    Box("Herald's stone step",new Vector3(.1f,.12f,-2.45f),
                        new Vector3(2.6f,.24f,1.0f),art.cobbles,Color.gray);
                    break;
                case AdventureEventKind.Ambush:
                case AdventureEventKind.LockedChest:
                    Crate(4.3f,1.25f,0,-8);
                    Crate(5.2f,2.5f,0,4);
                    if (play == AdventureEventKind.LockedChest) Crate(5.2f,2.5f,1,4);
                    break;
                default:
                    FloorProp("Signal buoy rack", art.buoyRack, -3.45f, .45f, 2.25f, -6);
                    FloorProp("Wharf hand truck", art.porterDolly, 4.34f, -.35f, 1.33f, 7);
                    Crate(-5.3f,2.2f,0,-8);
                    Crate(-5.0f,2.3f,1,4);
                    FloorProp("Downstage mooring",art.mooring,4.45f,-4.25f,1.62f,-9);
                    FloorProp("Creel beside packing cases",art.creel,-3.92f,2.7f,1.25f,7);
                    if (treasure) FloorProp("Cargo beside the sea chest",art.parcel,4.3f,.6f,1.2f,8);
                    break;
            }
        }

        private static StageVenue VenueFor(AdventureEventKind? play) => play switch
        {
            AdventureEventKind.Auction or AdventureEventKind.Obituary or AdventureEventKind.ClaimDesk
                or AdventureEventKind.DeadLetters or AdventureEventKind.Sponsorship or AdventureEventKind.BorrowedName
                or AdventureEventKind.Alchemist or AdventureEventKind.Investor or AdventureEventKind.HatTrick => StageVenue.Interior,
            AdventureEventKind.SoupQueue or AdventureEventKind.Forge => StageVenue.Firelight,
            _ => StageVenue.Outdoors
        };

        private void DressCemetery(AdventureEventKind? play)
        {
            switch (play)
            {
                case AdventureEventKind.GraveSeed:
                    HangBackdrop("Cemetery greenhouse garden", art.yewPath);
                    Deck(art.soil, new Vector2(2.7f, 2.5f), new Color(.50f,.57f,.46f), true);
                    FloorProp("Garden ferns", art.ferns, -4.1f, .80f, 2.6f, -8);
                    FloorProp("Cemetery water urn", art.stoneUrn, 4.45f, -.54f, 1.24f, 6);
                    break;
                case AdventureEventKind.Auction:
                    HangBackdrop("Estate auction chapel", art.estateSale);
                    Deck(art.guildParquet, new Vector2(2.4f, 1.5f), new Color(.53f,.44f,.40f));
                    FloorProp("Estate glass cabinet", art.lotCabinet, -3.8f, .55f, 2.40f, -7);
                    FloorProp("Mourning bust", art.drapedReliquary, 4.8f, -.65f, 1.35f, 6);
                    FloorProp("Chapel votives", art.candleRack, -4.95f, -1.65f, 1.7f, -10);
                    break;
                case AdventureEventKind.Obituary:
                case AdventureEventKind.ClaimDesk:
                case AdventureEventKind.DeadLetters:
                case AdventureEventKind.Sponsorship:
                case AdventureEventKind.BorrowedName:
                    HangBackdrop("Mortuary records office", art.mortuaryOffice);
                    Deck(art.timber, DockPlayhouse.PlankTile, new Color(.64f,.62f,.54f));
                    FloorProp("Pigeonhole archive", art.recordPigeonholes, -3.25f, .85f, 2.55f, -5);
                    FloorProp("Sexton’s seal press", art.sealPress, -4.85f, -1.5f, 1.45f, -9);
                    FloorProp("Records counter", art.registryDesk, -4.3f, .25f, 2.85f, -7);
                    FloorProp("Notices for the bereaved", art.contractBoard, 4.9f, .8f, 1.6f, 6);
                    break;
                case AdventureEventKind.HoundWalk:
                case AdventureEventKind.WeepingWall:
                case AdventureEventKind.WhisperWall:
                case AdventureEventKind.Epitaph:
                case AdventureEventKind.Ravens:
                    HangBackdrop("Yew walk", art.yewPath);
                    Deck(art.soil, new Vector2(2.7f, 2.5f), new Color(.47f,.57f,.48f), true);
                    FloorProp("Abandoned funeral barrow", art.funeralBarrow, -3.35f, -.94f, 2.1f, -9);
                    FloorProp("Weathered memorial urn", art.stoneUrn, 4.85f, -.84f, 1.12f, 5);
                    FloorProp("Yew path ferns", art.ferns, -4.2f, -.15f, 2.9f, -8);
                    FloorProp("Leaning markers", art.gravestones, 4.5f, .85f, 2.55f, 7);
                    break;
                case AdventureEventKind.SoulWard:
                    HangBackdrop("Soul ward cloister", art.watchCloister);
                    GraveyardFloor();
                    FloorProp("Infirmary stove", art.vigilStove, -4.4f, .75f, 1.8f, -7);
                    FloorProp("Infirmary linen cupboard", art.vestmentStand, 4.4f, 1.1f, 1.8f, 7);
                    BuildWardBeds();
                    break;
                case AdventureEventKind.Pallbearer:
                case AdventureEventKind.BellRope:
                case AdventureEventKind.SoupQueue:
                    HangBackdrop("Watch cloister", art.watchCloister);
                    GraveyardFloor();
                    FloorProp("Watch stove", art.vigilStove, -3.45f, -1.62f, 1.4f, -7);
                    FloorProp("Cloister blanket bench", art.blanketBench, 3.78f, .52f, 2.25f, 7);
                    FloorProp("Vigil bell", art.bellArch, -4.55f, .53f, 2.8f, -6);
                    FloorProp("Watchman's firewood", art.firewood, 4.6f, -.5f, 1.65f, 7);
                    break;
                default:
                    HangBackdrop("Event backcloth", art.graveyardSky);
                    GraveyardFloor();
                    FloorProp("Gatekeeper’s bier", art.funeralBarrow, -4.62f, -1.54f, 1.86f, -10);
                    FloorProp("Gate offerings", art.candleRack, 4.2f, -.86f, 1.70f, 8);
                    FloorProp("Cemetery gate", art.cemeteryGate, -3.75f, -.35f, 3.8f, -6);
                    FloorProp("Distant headstones", art.gravestones, .65f, 1.15f, 2.65f, 3);
                    FloorProp("Lantern yew", art.lanternTree, 5f, .8f, 2.7f, 8);
                    break;
            }
        }

        private MeshRenderer chest;
        private Light treasureLight;
        private Texture2D chestOpening;
        private Texture2D openedChest;
        private float unlockedAt = -1;
        private bool chestOpen;
        private static readonly Vector3 ChestMark = new(.9f,1.32f,-3.65f);

        internal void PlaceChest(AdventureContentAsset assets, bool opened)
        {
            if (chest != null) return;
            chestOpening = Resources.Load<Texture2D>("StoryEvents/ChestOpening");
            openedChest = assets.ChestOpen;
            chestOpen = opened;
            chest = Flat(Cemetery ? "Graveyard treasure chest" : "Harbour treasure chest",ChestMark,2.64f,2.64f,opened ? assets.ChestOpen : assets.ChestClosed);
            Shadow(new Vector3(ChestMark.x,0,ChestMark.z),2.48f,1.15f);
            treasureLight = new GameObject("Light from chest").AddComponent<Light>();
            treasureLight.transform.SetParent(stage,false);
            treasureLight.transform.position = ChestMark + new Vector3(0,-.24f,-.25f);
            treasureLight.type = LightType.Point;
            treasureLight.range = 3.1f;
            treasureLight.color = new Color(1,.68f,.2f);
        }

        internal void UnlockChest() => unlockedAt = Time.unscaledTime;
        internal bool ChestOpen => chestOpen;
        internal Rect ChestBounds => ProjectBounds(chest.bounds);

        private void AnimateChest()
        {
            float elapsed = unlockedAt < 0 ? -1 : Time.unscaledTime - unlockedAt;
            float pulse = .12f * Mathf.Sin(Time.unscaledTime * 3);
            treasureLight.intensity = (chestOpen ? .9f : .68f) + pulse;
            if (elapsed >= 0 && !chestOpen)
            {
                float shake = Mathf.Sin(elapsed*55) * 3 * (1-Mathf.Clamp01(elapsed/.24f));
                chest.transform.rotation = Quaternion.Euler(0,0,shake);
                if (elapsed >= .62f) SetFrame(chest, openedChest, 1, 0);
                else SetFrame(chest, chestOpening, AnimationCels.Count, AnimationCels.At(elapsed / .62f));
                treasureLight.intensity += Mathf.Sin(Mathf.PI*Mathf.Clamp01(elapsed/.72f)) * 1.8f;
                if (elapsed >= .62f) GameAudio.Play(SoundCue.ChestOpen);
                chestOpen = elapsed >= .62f;
            }
            FireUniforms(treasureLight,Time.unscaledTime);
        }

        private MeshRenderer resident, residentShadow;
        private Vector3 residentMark;
        internal void TellerExit(float progress)
        {
            float t = Mathf.SmoothStep(0,1,progress);
            var mark = residentMark + Vector3.right * (house.DeckBounds.max.x + 3 - residentMark.x) * t;
            resident.transform.position = mark + Vector3.up * (Mathf.Abs(Mathf.Sin(progress*19))* .065f);
            residentShadow.transform.position = new Vector3(mark.x,.014f,mark.z);
            resident.enabled = residentShadow.enabled = progress < 1;
        }

        internal BattleStage BattleCast(VisualElement panel, BattleStageArt battleArt, double now) =>
            new BattleStage(stage,camera,panel,battleArt,null,now,house);
        private readonly MeshRenderer[] bottles = new MeshRenderer[3];
        private readonly MeshRenderer[] hats = new MeshRenderer[3];
        private readonly MeshRenderer[] hatShadows = new MeshRenderer[3];
        private static readonly Vector3 Table = new(1.05f, 1.05f, -2.85f);
        private Vector3 wideEye;
        private Quaternion wideRotation;
        private Matrix4x4 wideLens;
        private float lensAspect, zoom, revealUntil = -1;
        private bool returningWide, hatRoundInProgress;
        private Action afterWide;
        internal bool RevealFinished => returningWide && zoom <= 0;
        internal bool AtTable => zoom >= 1 && !returningWide;

        private Texture2D tellerStrip, propArt;
        private MeshRenderer gull, graveProp, tideSinger;
        private Texture2D tideFront;
        internal void HideTideSinger()
        {
            if (tideSinger != null) tideSinger.gameObject.SetActive(false);
        }
        private AdventureEventKind graveKind;
        private float gravePhaseStarted;
        private int gravePhase;
        private bool graveCuePlaying;
        internal bool GraveCueFinished => !graveCuePlaying && !PatientsMoving;
        internal void CueGraveAction()
        {
            if (graveProp == null) return;
            gravePhase = 0;
            gravePhaseStarted = -1;
            SetFrame(graveProp, propArt, AnimationCels.Count, 0);
            SetFrame(resident, tellerStrip, AnimationCels.Count, 0);
            graveCuePlaying = true;
        }

        private void AnimateGrave()
        {
            if (!graveCuePlaying) return;
            var cue = CapitalStories.Contains(graveKind) ? (lead: .32f, action: .56f, hold: .36f) : GraveStories.Timing(graveKind);
            float now = Time.unscaledTime;
            if (gravePhaseStarted < 0) gravePhaseStarted = now;
            float duration = gravePhase == 0 ? cue.lead : gravePhase == 1 ? cue.action : cue.hold;
            int first = gravePhase == 0 ? 0 : gravePhase == 1 ? 5 : 12;
            int end = gravePhase == 0 ? 5 : gravePhase == 1 ? 12 : AnimationCels.Count;
            float progress = Mathf.Clamp01((now - gravePhaseStarted) / duration);
            int cel = Mathf.Min(end - 1, first + (int)(progress * (end - first)));
            SetFrame(graveProp, propArt, AnimationCels.Count, cel);
            SetFrame(resident, tellerStrip, AnimationCels.Count, cel);
            if (progress < 1) return;
            if (++gravePhase < 3) gravePhaseStarted = now;
            else graveCuePlaying = false;
        }

        private int beat = -1;
        internal static Texture2D EventProp(AdventureEventKind kind) =>
            Resources.Load<Texture2D>("StoryEvents/" + kind);

        internal Vector2 TellerHead => camera.WorldToViewportPoint(
            resident.bounds.center + Vector3.up * resident.bounds.extents.y);

        public void EventResident(AdventureRun run, AdventureContentAsset assets)
        {
            var visit = run.Current;
            var kind = visit.Event;
            if (resident != null || !kind.HasValue) return;
            bool lab = kind == AdventureEventKind.Alchemist;
            bool atTable = lab || kind is AdventureEventKind.HatTrick or AdventureEventKind.Auction;
            bool city = CapitalStories.Contains(kind);
            bool cemetery = GraveStories.Contains(kind) || kind == AdventureEventKind.GraveSeed || city;
            tellerStrip = Resources.Load<Texture2D>("StoryEvents/Cast/" + (city ? (CapitalStories.For(kind.Value).District switch { 0 => "LostTraveler", 1 => "Forge", _ => "Investor" }) : cemetery ? "GraveChronicler" : kind == AdventureEventKind.TidePact ? "LostTraveler" : kind.Value.ToString()));
            var mark = atTable ? new Vector3(Table.x+(lab ? .8f : 1.25f),.95f,Table.z+.95f) : new Vector3(kind == AdventureEventKind.Ambush ? 2.2f : 3.2f,.95f,-2.15f);
            if (kind == AdventureEventKind.Auction)
            {
                mark = new Vector3(Table.x + 1.78f, 1.36f, Table.z + 1.03f);
                Box("Auctioneer's rostrum", new Vector3(mark.x, .205f, mark.z), new Vector3(1.15f,.41f,.94f),
                    art.timber, new Color(.54f,.43f,.38f));
            }
            if (kind == AdventureEventKind.SoulWard) mark = new Vector3(3.5f, .95f, .05f);
            resident = Flat("Dock storyteller", mark, 1.9f*tellerStrip.width/tellerStrip.height,1.9f,tellerStrip);
            SetFrame(resident,tellerStrip,AnimationCels.Count,0);
            residentMark = mark;
            residentShadow = Shadow(new Vector3(mark.x,kind == AdventureEventKind.Auction ? .41f : 0,mark.z),.85f,.44f);
            Dressing.Cast[resident] = new DockSet.Player(true);

            if (kind == AdventureEventKind.Auction)
            {
                BuildTable(false, auction: true);
                for (int i = 0; i < cases.Length; i++)
                {
                    int kindIndex = run.AuctionKindAt(i);
                    var strip = Resources.Load<Texture2D>("StoryEvents/" + CaseArt[kindIndex]);
                    var foot = Table + new Vector3((i - 1) * 1.15f, .026f, -.14f);
                    // Seat the painted baseline, inside the square cel, against the velvet.
                    var markOnTable = foot + Vector3.up * (1.04f * (CaseBaseline[kindIndex] - .5f));
                    caseShadows[i] = Shadow(foot, .97f, .48f, .46f);
                    Dressing.Hinges[caseShadows[i]] = new Vector2(Table.x, Table.z);
                    cases[i] = Flat("Auction case " + i, markOnTable, 1.04f, 1.04f, strip);
                    caseStrips[i] = strip;
                    SetFrame(cases[i], strip, AnimationCels.Count, run.AuctionInspected == i ? AnimationCels.Count - 1 : 0);
                    Dressing.Hinges[cases[i]] = new Vector2(Table.x, Table.z);
                }
                returningWide = visit.Resolved;
                return;
            }
            if (kind == AdventureEventKind.SoulWard)
            {
                SyncPatients(run, assets.Encounter, false);
                return;
            }
            if (kind == AdventureEventKind.TidePact)
            {
                tideFront = assets.EventInkArt(AdventureEventKind.TidePact);
                tideSinger = Flat("Deep sea visitor", new Vector3(.1f, 1.08f, -2.4f), 2.25f, 2.25f, tideFront);
                SetFrame(tideSinger, tideFront, AnimationCels.Count, 0);
                Dressing.Cast[tideSinger] = new DockSet.Player(true);
                return;
            }
            if (kind == AdventureEventKind.GraveSeed)
            {
                var greenhouse = assets.EventInkArt(AdventureEventKind.GraveSeed);
                if (greenhouse != null)
                {
                    const float greenhouseHeight = 2.75f;
                    float greenhouseWidth = greenhouseHeight * greenhouse.width / greenhouse.height;
                    var foot = new Vector3(-1.05f, 0, -2.55f);
                    Shadow(foot, greenhouseWidth * .91f, 1.2f);
                    Flat("Cemetery greenhouse", foot + Vector3.up * (greenhouseHeight / 2), greenhouseWidth, greenhouseHeight, greenhouse);
                }
                return;
            }
            propArt = EventProp(kind.Value);
            bool coins = kind == AdventureEventKind.FoundCoins;
            float width = atTable ? 1.05f : coins ? 1.78f : 2.22f;
            float height = width * propArt.height / propArt.width;
            if (height > 2.25f) { width *= 2.25f / height; height = 2.25f; }
            float x = atTable ? -2.05f : .23f, y = 0, z = atTable ? Table.z+.95f : -2.45f;
            if (lab) { width = .60f; height = width*propArt.height/propArt.width; x = Table.x-1.47f; y = Table.y+.03f; z = Table.z+.15f; }
            if (kind == AdventureEventKind.AscensionTrial) y = .24f;
            var prop = Flat("Event prop " + kind,new Vector3(x,y+height*.5f,z),width,height,propArt);
            if (y == 0) Shadow(new Vector3(x,0,z),width*.91f,.52f);
            if (cemetery)
            {
                graveProp = prop;
                graveKind = kind.Value;
                SetFrame(prop, propArt, AnimationCels.Count, 0);
                CueGraveAction();
            }
            if (coins) { gull = prop; SetFrame(gull,propArt,AnimationCels.Count,0); }
            if (!atTable) return;
            BuildTable(lab);
            if (!lab) { BuildHats(assets.Hat); return; }
            var content = assets.Encounter;
            for (int i = 0; i < visit.Potions.Count; i++)
            {
                var bottle = Table + new Vector3((i-1)*.91f, .32f, -.14f);
                bottles[i] = Flat("Apothecary bottle " + i,bottle,.66f,.66f,content.IconFor(visit.Potions[i].Id));
                Dressing.Hinges[bottles[i]] = new Vector2(Table.x,Table.z);
            }
            Dressing.Hinges[prop] = new Vector2(Table.x,Table.z);
            returningWide = visit.Resolved;
        }

        internal Rect BottleTarget(int index)
        {
            return ProjectBounds(bottles[index].bounds);
        }

        private Rect ProjectBounds(Bounds bounds)
        {
            var low = camera.WorldToViewportPoint(bounds.min);
            var high = camera.WorldToViewportPoint(bounds.max);
            return Rect.MinMaxRect(low.x,1-high.y,high.x,1-low.y);
        }

        internal void TakeBottle(int index)
        {
            bottles[index].enabled = false;
            returningWide = true;
        }

        private static readonly string[] CaseArt = { "RattleCase", "HerbalCase", "IronCase" };
        private static readonly float[] CaseBaseline = { 432f / 448, 413f / 448, 420f / 448 };
        private readonly MeshRenderer[] cases = new MeshRenderer[3];
        private readonly MeshRenderer[] caseShadows = new MeshRenderer[3];
        private readonly Texture2D[] caseStrips = new Texture2D[3];
        private readonly float[] caseOpening = { -1, -1, -1 };
        internal Rect AuctionTarget(int slot) => ProjectBounds(cases[slot].bounds);
        internal bool HitCase(int slot, Vector2 viewport)
        {
            var card = cases[slot];
            if (card == null || !card.enabled) return false;
            var ray = camera.ViewportPointToRay(viewport);
            var plane = new Plane(card.transform.forward, card.transform.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            var local = card.transform.InverseTransformPoint(ray.GetPoint(distance));
            float u = local.x + .5f, v = local.y + .5f;
            if (u < 0 || u > 1 || v < 0 || v > 1) return false;
            card.GetPropertyBlock(properties);
            var uv = properties.GetVector("_MainTex_ST");
            return caseStrips[slot].GetPixelBilinear(u * uv.x + uv.z, v * uv.y + uv.w).a > .15f;
        }

        internal void OpenCase(int slot) => caseOpening[slot] = Time.unscaledTime;

        private void AnimateCases()
        {
            for (int i = 0; i < caseOpening.Length; i++)
            {
                if (caseOpening[i] < 0 || !cases[i].enabled) continue;
                int frame = AnimationCels.At((Time.unscaledTime - caseOpening[i]) / .45f);
                SetFrame(cases[i], caseStrips[i], AnimationCels.Count, frame);
                if (frame == AnimationCels.Count - 1) caseOpening[i] = -1;
            }
        }
        internal void TakeCase(int slot) => cases[slot].enabled = caseShadows[slot].enabled = false;
        internal void FocusCases(bool focus) => returningWide = !focus;

        private void BuildTable(bool laboratory, bool auction = false)
        {
            var hinge = new Vector2(Table.x, Table.z);
            void Board(string id, Vector3 offset, Vector3 size, Color color)
            {
                var part = Box(id, Table + offset, size, art.timber, color);
                Dressing.Hinges[part] = hinge;
            }
            Board(auction ? "Auction table top" : laboratory ? "Apothecary worktop" : "Hat table top", Vector3.down * .09f, new Vector3(3.7f,.18f,1.65f), Color.white);
            var felt = Box(auction ? "Auction velvet" : laboratory ? "Slate work surface" : "Hat table felt", Table + Vector3.up * .012f, new Vector3(3.37f,.024f,1.41f),
                laboratory ? art.timber : null, auction ? new Color(.37f,.12f,.16f) : laboratory ? new Color(.39f,.51f,.46f) : new Color(.16f,.31f,.26f));
            Dressing.Hinges[felt] = hinge;
            foreach (float x in new[] { -1.54f, 1.54f })
            foreach (float z in new[] { -.61f, .61f })
                Board("Trestle leg", new Vector3(x,-.58f,z), new Vector3(.14f,.94f,.14f), Color.gray);
            if (!laboratory) return;
            var cabinet = Box("Apothecary cabinet", Table + new Vector3(0,-.6f,-.28f),
                new Vector3(3.3f,.87f,.85f),art.counterFront,new Color(.70f,.74f,.63f));
            Dressing.Hinges[cabinet] = hinge;
            Board("Cabinet rail",new Vector3(0,-.23f,-.73f), new Vector3(3.36f,.10f,.12f),Color.gray);
            for (int i = 0; i < 3; i++)
            {
                float x = (i-1)*1.05f;
                Board("Herb drawer",new Vector3(x,-.51f,-.73f),new Vector3(.95f,.4f,.08f),new Color(.62f,.50f,.37f));
                Board("Drawer pull",new Vector3(x,-.50f,-.80f),new Vector3(.19f,.045f,.08f),new Color(.95f,.75f,.41f));
            }
        }

        private void BuildHats(Texture2D hatArt)
        {
            var hinge = new Vector2(Table.x,Table.z);
            for (int i = 0; i < hats.Length; i++)
            {
                hats[i] = Flat("Trick hat " + i, Table, 1f, .64f, hatArt);
                // Trim the transparent margin so the painted brim touches the felt.
                hats[i].GetPropertyBlock(properties);
                properties.SetVector("_MainTex_ST", new Vector4(.95f,.61f,.025f,.195f));
                hats[i].SetPropertyBlock(properties);
                hatShadows[i] = Shape("Hat contact shadow " + i, PrimitiveType.Quad, Table, Vector3.one,
                    null, new Color(0,0,0,.42f), Surface.Shadow);
                hatShadows[i].transform.rotation = Quaternion.Euler(90,0,0);
                Dressing.Hinges[hats[i]] = hinge;
                MoveHat(i, i, 0, 0);
            }
        }

        internal void MoveHat(int id, float slot, float depth, float lift)
        {
            var mark = Table + new Vector3((slot-1)*1.08f,.03f,depth);
            hats[id].transform.position = mark + Vector3.up * (.30f+lift);
            hatShadows[id].transform.position = mark;
            hatShadows[id].transform.localScale = new Vector3(.90f-lift*.23f,.42f,1);
            hats[id].transform.rotation = Quaternion.Euler(0,0,depth*14);
        }

        internal Rect TableTarget(int id, bool prize)
        {
            var centre = hats[id].transform.position;
            var half = new Vector3(.50f,.32f,0);
            if (prize)
            {
                centre.y = Table.y + .24f;
                centre.z = Table.z - .10f;
                half = new Vector3(.25f,.25f,0);
            }
            var low = camera.WorldToViewportPoint(centre - half);
            var high = camera.WorldToViewportPoint(centre + half);
            return Rect.MinMaxRect(low.x, 1-high.y, high.x, 1-low.y);
        }

        internal void PresentHatRound(bool resolved)
        {
            if (!resolved) hatRoundInProgress = true;
            else if (!hatRoundInProgress) returningWide = true;
            else if (revealUntil < 0) revealUntil = Time.unscaledTime + 2.1f;
        }

        internal void LeaveTable(Action next)
        {
            if (afterWide != null) return;
            if ((hats[0] == null && bottles[0] == null && cases[0] == null) || zoom <= 0) { next(); return; }
            returningWide = true;
            afterWide = next;
        }

        internal void Tick()
        {
            FrameCamera();
            if (!DockPlayhouse.Moving) AnimatePatients();
            if (chest != null) AnimateChest();
            int nextBeat = AnimationCels.Loop(Time.unscaledTimeAsDouble, 2.5);
            if (cases[0] != null) AnimateCases();
            if (graveProp != null && !DockPlayhouse.Moving) AnimateGrave();
            if (resident != null && !graveCuePlaying && beat != nextBeat && !DockPlayhouse.Moving)
            {
                beat = nextBeat;
                SetFrame(resident,tellerStrip,AnimationCels.Count,beat);
                if (tideSinger != null) SetFrame(tideSinger, tideFront, AnimationCels.Count, beat);
                if (gull != null) SetFrame(gull,propArt,AnimationCels.Count,beat);
            }
            if (hats[0] == null && bottles[0] == null && cases[0] == null) return;
            if (lensAspect != camera.aspect)
            {
                lensAspect = camera.aspect;
                wideEye = camera.transform.position;
                wideRotation = camera.transform.rotation;
                wideLens = camera.projectionMatrix;
            }
            if (DockPlayhouse.Moving) return;
            if (revealUntil >= 0 && Time.unscaledTime >= revealUntil) returningWide = true;
            // Complete the reverse shot before the outgoing props fold onto the turntable.
            zoom = Mathf.MoveTowards(zoom, returningWide ? 0 : 1, Time.unscaledDeltaTime / .82f);
            if (zoom == 0 && returningWide)
            {
                camera.transform.SetPositionAndRotation(wideEye,wideRotation);
                camera.projectionMatrix = wideLens;
                var next = afterWide;
                afterWide = null;
                next?.Invoke();
                return;
            }
            float t = Mathf.SmoothStep(0,1,zoom);
            bool laboratory = bottles[0] != null;
            bool auction = cases[0] != null;
            float distance = Mathf.Max(auction || laboratory ? 4.95f : 4.4f, 6.9f / camera.aspect);
            var aim = Table + Vector3.up * (auction ? .57f : laboratory ? .43f : .29f);
            var eye = aim + new Vector3(0,distance*(laboratory ? .23f : .40f),-distance);
            camera.transform.SetPositionAndRotation(Vector3.Lerp(wideEye,eye,t),
                Quaternion.Slerp(wideRotation,Quaternion.LookRotation(aim-eye),t));
            var lens = wideLens;
            lens.m12 *= 1-t;
            camera.projectionMatrix = lens;
        }

        private void Crate(float x, float z, float baseY, float angle)
        {
            var chest = Box("Packing case", new Vector3(x, baseY + .50f, z),
                new Vector3(1.45f, 1, .93f), art.timber, new Color(.75f, .68f, .55f));
            chest.transform.rotation = Quaternion.Euler(0, angle, 0);
            foreach (float edge in new[] { -.61f, .61f })
            {
                Vector3 position = chest.transform.position + chest.transform.rotation * new Vector3(edge, 0, -.48f);
                var brace = Box("Packing cleat", position, new Vector3(.11f, 1.02f, .065f), art.timber, Color.gray);
                brace.transform.rotation = chest.transform.rotation;
                Dressing.Hinges[brace] = new Vector2(x,z);
            }
            if (baseY == 0) Shadow(new Vector3(x, 0, z), 1.8f, 1.3f);
        }
    }
}
