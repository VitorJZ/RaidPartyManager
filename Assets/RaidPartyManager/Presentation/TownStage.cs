using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TownStage : ServiceStage
    {
        private const int ShopSlot = 1;
        private const float SignY = 3.73f, SignWidth = 2.44f, SignHeight = .67f, SignDepth = .61f;
        private static readonly float[] Centres = { -4.98f, -1.66f, 1.66f, 4.98f };
        private static readonly float[] Depths = { .18f, .69f, .72f, .20f };
        private static readonly float[] Tempo = { 2.5f, 3.6f, 2.1f, 3.1f };
        private readonly MeshRenderer[] cast = new MeshRenderer[4];
        private const float CordThickness = .042f;
        private readonly FlownSign[] signs = new FlownSign[4];
        private readonly Vector3[] signRest = new Vector3[4];
        private float beamUnderside;
        private readonly Transform flyBeam;
        private float rigAspect;
        private readonly int[] frame = { -1, -1, -1, -1 };

        public TownStage(Transform owner, VisualElement panel, ServiceStageArt art, bool graveyard, bool capital = false) :
            base(owner, panel, art, capital ? StageLighting.Clockwork(StageVenue.Outdoors) : StageLighting.For(graveyard, StageVenue.Outdoors),
                StageTiltShift.FacilityStrength)
        {
            if (capital) DressCapital(0);
            else
            {
                HangBackdrop(graveyard ? "Cemetery square" : "Distant harbour", graveyard ? art.mourningSquare : art.townSky);
                if (graveyard) GraveyardFloor();
                else Deck(art.cobbles, new Vector2(1.8f, .78f), new Color(.80f, .78f, .76f));
            }
            flyBeam = Fly(Box("Town sign fly beam", new Vector3(0, 0, -.25f), new Vector3(13, .24f, .84f),
                art.timber, Color.white));
            for (int i = 0; i < 4; i++)
            {
                int first = stage.childCount;
                float x = Centres[i], z = Depths[i];
                var image = capital ? CapitalArt("Facility" + i) : (graveyard ? art.graveyardFacilities : art.facilities)[i];
                float height = Mathf.Min(4.28f, 3.45f * image.height / image.width);
                float width = height * image.width / image.height;
                var centre = new Vector3(x, height / 2, z);
                var turn = Quaternion.Euler(0, (i - 1.5f) * 4, 0);
                var edge = Shape("Cardboard edge " + i, PrimitiveType.Quad, centre + new Vector3(.040f, 0, .075f),
                    new Vector3(width, height, 1), image, new Color(.49f, .37f, .24f), Surface.Paper);
                edge.transform.rotation = turn;
                var flat = Flat("Facility " + i, centre, width, height, image, Surface.ScenicFlat);
                flat.transform.rotation = turn;
                Box("Timber brace", new Vector3(x, .70f, z + .30f), new Vector3(.12f, 1.6f, .16f), art.timber, Color.white)
                    .transform.rotation = Quaternion.Euler(-22, 0, 0);
                Box("Scenic sill", new Vector3(x,.068f,z), new Vector3(width * 1.025f,.136f,.48f),
                    art.timber,new Color(.72f,.61f,.48f));
                Shadow(new Vector3(x, 0, z), width * 1.08f, 1.10f);
                for (int j = first; j < stage.childCount; j++)
                    Dressing.Hinges[stage.GetChild(j).GetComponent<MeshRenderer>()] = new Vector2(x,z);
                HangVenueSign(i, x, z);
                var foot = new Vector3(x - .40f, 0, z - .82f);
                float size = i == ShopSlot ? 1.91f : 2.08f;
                float castWidth = i == ShopSlot ? size * 410f / 512f : size;
                cast[i] = Flat("Resident " + i, foot + Vector3.up * (size / 2), castWidth, size, art.residents[i]);
                Dressing.Cast[cast[i]] = new DockSet.Player(true);
                Shadow(foot, size * .65f, .50f);
            }
            if (graveyard)
            {
                FloorProp("Florist's wreath cart", art.wreathCart, -5.12f, -2.70f, 1.78f, -12);
                FloorProp("Square bell stand", art.bellStand, 4.45f, -1.65f, 1.24f, 7);
                FloorProp("Square lantern yew", art.lanternTree, 6.6f, -.65f, 1.55f, 7);
            }
            else if (!capital)
            {
                FloorProp("Quayside catch basket", art.creel, 3.12f, -6.45f, 1.19f, -12);
                FloorProp("Dockside hand truck", art.porterDolly, -4.9f, -2.36f, 1.15f, -8);
                FloorProp("Lobster pots", art.lobsterPots, 5.37f, -2.46f, 1.62f, 9);
            }
            Animate(0);
        }

        public void Animate(float time)
        {
            if (rigAspect != camera.aspect)
            {
                rigAspect = camera.aspect;
                FitFlyBeam();
            }
            PoseSigns();
            for (int i = 0; i < cast.Length; i++)
            {
                int next = i == ShopSlot ? AnimationCels.Loop(time, 1.2) :
                    AnimationCels.Loop(time + i * 2 / Tempo[i], 8f / Tempo[i]);
                if (next == frame[i]) continue;
                frame[i] = next;
                if (i == ShopSlot) PoseBroker(cast[i], next);
                else SetFrame(cast[i], art.residents[i], AnimationCels.Count, next);
            }
        }

        private Transform Fly(MeshRenderer mesh)
        {
            Dressing.Flown.Add(mesh);
            return mesh.transform;
        }

        private void HangVenueSign(int index, float x, float facadeZ)
        {
            signRest[index] = new Vector3(x, SignY, facadeZ - SignDepth);
            var sign = signs[index] = HangingBoard("Hanging placard " + index, "Placard slab", SignFace.Plank,
                SignWidth, SignHeight, .14f, "Sign cord", CordThickness);
            sign.Board.localPosition = signRest[index];
            foreach (var (part, _) in sign.Painted)
            {
                // The venue labels sit on these boards above the cast's focus band.
                part.renderingLayerMask |= StageTiltShift.SharpLayer;
                Dressing.Flown.Add(part);
            }
            foreach (var cord in sign.Ropes) Dressing.Flown.Add(cord);
        }

        /// <summary>The panel sign becomes the lettering of its board, which mirrors its hover, sway and rise.</summary>
        public void MountService(HangingSign target, int i)
        {
            signs[i].Target = target;
            target.MountInScenery();
            target.RetractInScenery = chosen => signs[i].Retract(chosen);
        }

        private void PoseSigns()
        {
            for (int i = 0; i < signs.Length; i++)
                signs[i].Pose(this, stage.TransformPoint(signRest[i]), stage.rotation, beamUnderside);
        }

        private void FitFlyBeam()
        {
            var beam = flyBeam.GetComponent<MeshRenderer>().bounds;
            float depth = camera.WorldToViewportPoint(beam.max).z;
            beamUnderside = camera.ViewportToWorldPoint(new Vector3(.5f, 1.08f, depth)).y;
            flyBeam.position = new Vector3(beam.center.x, beamUnderside + beam.extents.y, beam.center.z);
            PoseSigns();
        }

        public void PlaceService(VisualElement button, int i)
        {
            float x = Centres[i], z = Depths[i] - SignDepth - .075f;
            Pin(button, new Vector3(x - SignWidth / 2, SignY + SignHeight / 2, z),
                new Vector3(x + SignWidth / 2, SignY - SignHeight / 2, z));
        }
    }
}
