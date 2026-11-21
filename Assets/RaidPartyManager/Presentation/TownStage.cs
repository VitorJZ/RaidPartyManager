using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TownStage : ServiceStage
    {
        private static readonly float[] Centres = { -4.98f, -1.66f, 1.66f, 4.98f };
        private static readonly float[] Depths = { .18f, .69f, .72f, .20f };
        private static readonly int[] Poses = { 0, 0, 1, 2, 3, 3, 2, 1 };
        private static readonly float[] Tempo = { 2.5f, 3.6f, 2.1f, 3.1f };
        private readonly MeshRenderer[] cast = new MeshRenderer[4];
        private readonly int[] frame = { -1, -1, -1, -1 };

        public TownStage(Transform owner, VisualElement panel, ServiceStageArt art, bool graveyard) :
            base(owner, panel, art)
        {
            HangBackdrop(graveyard ? "Cemetery square" : "Distant harbour", graveyard ? art.mourningSquare : art.townSky);
            if (graveyard) GraveyardFloor();
            else Deck(art.cobbles, new Vector2(1.8f, .78f), new Color(.80f, .78f, .76f));
            for (int i = 0; i < 4; i++)
            {
                int first = stage.childCount;
                float x = Centres[i], z = Depths[i];
                var image = (graveyard ? art.graveyardFacilities : art.facilities)[i];
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
                var foot = new Vector3(x - .40f, 0, z - .82f);
                float size = i == 1 ? 1.91f : 2.08f;
                cast[i] = Flat("Resident " + i, foot + Vector3.up * (size / 2), size, size, art.residents[i]);
                Dressing.Cast[cast[i]] = new DockSet.Player(true);
                Shadow(foot, size * .65f, .50f);
            }
            if (graveyard)
            {
                FloorProp("Florist's wreath cart", art.wreathCart, -5.12f, -2.70f, 1.78f, -12);
                FloorProp("Square bell stand", art.bellStand, 4.45f, -1.65f, 1.24f, 7);
                FloorProp("Square lantern yew", art.lanternTree, 6.6f, -.65f, 1.55f, 7);
            }
            else
            {
                FloorProp("Quayside catch basket", art.creel, 3.12f, -6.45f, 1.19f, -12);
                FloorProp("Dockside hand truck", art.porterDolly, -4.9f, -2.36f, 1.15f, -8);
                FloorProp("Lobster pots", art.lobsterPots, 5.37f, -2.46f, 1.62f, 9);
            }
            Animate(0);
        }

        public void Animate(float time)
        {
            for (int i = 0; i < cast.Length; i++)
            {
                int next = Poses[(Mathf.FloorToInt(time * Tempo[i]) + i * 2) % Poses.Length];
                if (next == frame[i]) continue;
                frame[i] = next;
                SetFrame(cast[i], art.residents[i], 4, next);
            }
        }

        public void PlaceService(VisualElement button, int i)
        {
            float x = Centres[i], z = Depths[i] - .50f;
            Pin(button, new Vector3(x - 1.48f, 3.90f, z), new Vector3(x + 1.48f, -.95f, z));
        }
    }
}
