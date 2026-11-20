using System;
using RaidPartyManager.Run;
using RaidPartyManager.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class DockEventStage : ServiceStage
    {
        public DockEventStage(Transform owner, VisualElement panel, ServiceStageArt art, bool treasure,
            AdventureEventKind? play = null) : base(owner, panel, art)
        {
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
            HangBackdrop("Event backcloth",painted);
            bool stone = play is AdventureEventKind.AscensionTrial or AdventureEventKind.Forge or AdventureEventKind.Ambush;
            Deck(stone ? art.cobbles : art.timber, stone ? new Vector2(1.8f,.78f) : DockPlayhouse.PlankTile,
                new Color(.76f,.71f,.65f));
            switch (play)
            {
                case AdventureEventKind.Alchemist:
                case AdventureEventKind.HatTrick:
                    break; // The table and its contents dress these sets.
                case AdventureEventKind.Investor:
                    Crate(3.9f,1.35f,0,4);
                    FloorProp("Folded sailcloth",art.parcel,4.9f,.65f,1.4f,-8);
                    break;
                case AdventureEventKind.LostTraveler:
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
                    Crate(-5.3f,2.2f,0,-8);
                    Crate(-5.0f,2.3f,1,4);
                    FloorProp("Downstage mooring",art.mooring,4.45f,-4.25f,1.62f,-9);
                    FloorProp("Creel beside packing cases",art.creel,-3.92f,2.7f,1.25f,7);
                    if (treasure) FloorProp("Cargo beside the sea chest",art.parcel,4.3f,.6f,1.2f,8);
                    break;
            }
        }

        private MeshRenderer chest;
        private Light treasureLight;
        private AdventureContentAsset chestArt;
        private float unlockedAt = -1;
        private bool chestOpen;
        private static readonly Vector3 ChestMark = new(.9f,1.32f,-3.65f);

        internal void PlaceChest(AdventureContentAsset assets, bool opened)
        {
            if (chest != null) return;
            chestArt = assets;
            chestOpen = opened;
            chest = Flat("Harbour treasure chest",ChestMark,2.64f,2.64f,opened ? assets.ChestOpen : assets.ChestClosed);
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
                SetFrame(chest,elapsed < .24f ? chestArt.ChestClosed : elapsed < .46f ? chestArt.ChestAjar : chestArt.ChestOpen,1,0);
                treasureLight.intensity += Mathf.Sin(Mathf.PI*Mathf.Clamp01(elapsed/.72f)) * 1.8f;
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
        private MeshRenderer gull;
        private int beat = -1;
        private static readonly int[] Gestures = { 0,0,1,1,2,2,1,3,3,0 };
        private static readonly int[] Pecks = { 0,0,0,1,2,2,3,3,3,0 };
        internal static Texture2D EventProp(AdventureEventKind kind) =>
            Resources.Load<Texture2D>("StoryEvents/" + kind);

        internal Vector2 TellerHead => camera.WorldToViewportPoint(
            resident.bounds.center + Vector3.up * resident.bounds.extents.y);

        public void EventResident(AdventureVisit visit, AdventureContentAsset assets)
        {
            var kind = visit.Event;
            if (resident != null || !kind.HasValue) return;
            bool lab = kind == AdventureEventKind.Alchemist;
            bool atTable = lab || kind == AdventureEventKind.HatTrick;
            tellerStrip = Resources.Load<Texture2D>("StoryEvents/Cast/" + kind.Value);
            var mark = atTable ? new Vector3(Table.x+(lab ? .8f : 1.25f),.95f,Table.z+.95f) : new Vector3(kind == AdventureEventKind.Ambush ? 2.2f : 3.2f,.95f,-2.15f);
            resident = Flat("Dock storyteller", mark, 1.9f*tellerStrip.width/(4*tellerStrip.height),1.9f,tellerStrip);
            SetFrame(resident,tellerStrip,4,0);
            residentMark = mark;
            residentShadow = Shadow(new Vector3(mark.x,0,mark.z),.85f,.44f);
            Dressing.Cast[resident] = new DockSet.Player(true);

            propArt = EventProp(kind.Value);
            bool coins = kind == AdventureEventKind.FoundCoins;
            float width = atTable ? 1.05f : coins ? 1.78f : 2.22f;
            float height = width * propArt.height / (propArt.width / (coins ? 4f : 1f));
            if (height > 2.25f) { width *= 2.25f / height; height = 2.25f; }
            float x = atTable ? -2.05f : .23f, y = 0, z = atTable ? Table.z+.95f : -2.45f;
            if (lab) { width = .60f; height = width*propArt.height/propArt.width; x = Table.x-1.47f; y = Table.y+.03f; z = Table.z+.15f; }
            if (kind == AdventureEventKind.AscensionTrial) y = .24f;
            var prop = Flat("Event prop " + kind,new Vector3(x,y+height*.5f,z),width,height,propArt);
            if (y == 0) Shadow(new Vector3(x,0,z),width*.91f,.52f);
            if (coins) { gull = prop; SetFrame(gull,propArt,4,0); }
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

        private void BuildTable(bool laboratory)
        {
            var hinge = new Vector2(Table.x, Table.z);
            void Board(string id, Vector3 offset, Vector3 size, Color color)
            {
                var part = Box(id, Table + offset, size, art.timber, color);
                Dressing.Hinges[part] = hinge;
            }
            Board(laboratory ? "Apothecary worktop" : "Hat table top", Vector3.down * .09f, new Vector3(3.7f,.18f,1.65f), Color.white);
            var felt = Box(laboratory ? "Slate work surface" : "Hat table felt", Table + Vector3.up * .012f, new Vector3(3.37f,.024f,1.41f),
                laboratory ? art.timber : null, laboratory ? new Color(.39f,.51f,.46f) : new Color(.16f,.31f,.26f));
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
            if ((hats[0] == null && bottles[0] == null) || zoom <= 0) { next(); return; }
            returningWide = true;
            afterWide = next;
        }

        internal void Tick()
        {
            FrameCamera();
            if (chest != null) AnimateChest();
            int nextBeat = Mathf.FloorToInt(Time.unscaledTime * 4) % Gestures.Length;
            if (resident != null && beat != nextBeat && !DockPlayhouse.Moving)
            {
                beat = nextBeat;
                SetFrame(resident,tellerStrip,4,Gestures[beat]);
                if (gull != null) SetFrame(gull,propArt,4,Pecks[beat]);
            }
            if (hats[0] == null && bottles[0] == null) return;
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
            float distance = Mathf.Max(laboratory ? 4.95f : 4.4f, 6.9f / camera.aspect);
            var aim = Table + Vector3.up * (laboratory ? .43f : .29f);
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
