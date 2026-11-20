using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class CampStage : ServiceStage
    {
        protected override float PropScale => .68f;
        private readonly Light firelight;
        private readonly MeshRenderer flames;
        private readonly Texture2D fireCels;
        private int lastCel = -1;
        private static readonly int[] Flicker = { 0, 1, 2, 1, 0, 3 };
        private readonly MeshRenderer[] seats = new MeshRenderer[4];
        private static readonly Vector3[] Feet = {
            new(3.60f,0,-1.05f), new(2.08f,0,1.72f),
            new(-2.08f,0,1.72f), new(-3.60f,0,-1.05f)
        };

        public CampStage(Transform owner, VisualElement panel, ServiceStageArt art, Texture2D fire) :
            base(owner, panel, art)
        {
            HangBackdrop("Pine clearing", art.campSky);
            Deck(art.soil, new Vector2(2.8f, 2.6f), new Color(.51f, .58f, .68f), true);
            for (int side = -1; side <= 1; side += 2)
            {
                int first = stage.childCount;
                var leaves = Flat("Fern groundrow", new Vector3(side * 4.7f, .64f, 3.0f + side * .18f),
                    side * 4.25f, 1.32f, art.ferns, Surface.ScenicFlat);
                leaves.transform.rotation = Quaternion.Euler(0, -side * 12, 0);
                var a = new Vector3(side * 4.32f, .37f, -2.00f);
                var b = new Vector3(side * 1.42f, .37f, 3.25f);
                var direction = (b - a).normalized;
                var axis = Quaternion.FromToRotation(Vector3.up, direction);
                var log = Shape("Pine bench", PrimitiveType.Cylinder, (a + b) * .5f, new Vector3(.79f, (b - a).magnitude * .5f, .79f),
                    art.bark, new Color(.80f, .79f, .78f), Surface.Solid, new Vector2(2, 1));
                log.transform.rotation = axis;
                foreach (var end in new[] { a - direction * .014f, b + direction * .014f })
                {
                    var cap = Shape("Cut end", PrimitiveType.Cylinder, end, new Vector3(.77f, .009f, .77f),
                        art.endGrain, Color.white, Surface.Solid);
                    cap.transform.rotation = axis;
                }
                foreach (float t in new[] { .16f, .81f })
                {
                    var sleeper = Vector3.Lerp(a, b, t);
                    sleeper.y = .085f;
                    Box("Bench trestle foot", sleeper, new Vector3(1.35f, .17f, .26f), art.timber, new Color(.65f,.57f,.43f))
                        .transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                for (int j = first+1; j < stage.childCount; j++)
                    Dressing.Hinges[stage.GetChild(j).GetComponent<MeshRenderer>()] =
                        new Vector2((a.x+b.x)*.5f, (a.z+b.z)*.5f)*PropScale;
                Shadow((a + b) * .5f - Vector3.up * .45f, 3.6f, 7.4f, .48f);
            }
            FloorProp("Kindling bundle",art.firewood,5.45f,-3.35f,2.15f,-11);
            var pit = new Vector3(0, 0, -1.66f);
            Shadow(pit, 2.6f, 1.85f, .68f);
            for (int i = 0; i < 11; i++)
            {
                float a = i * Mathf.PI * 2 / 11;
                var rock = Shape("Hearth stone", PrimitiveType.Sphere, pit + new Vector3(Mathf.Cos(a) * .90f, .12f, Mathf.Sin(a) * .70f),
                    new Vector3(.47f, .28f + (i % 3) * .024f, .40f), art.cobbles, new Color(.75f, .72f, .68f), Surface.Solid);
                rock.transform.rotation = Quaternion.Euler(7 * i, 17 * i, 5 * i);
            }
            fireCels = fire;
            flames = Flat("camp-fire", pit + Vector3.up * .90f, 1.84f, 1.84f, fireCels, Surface.Flame);
            Flat("Rising embers", pit + new Vector3(0, 1.35f, -.20f), 3.05f, 3.2f, null, Surface.Embers);
            firelight = new GameObject("Campfire point light").AddComponent<Light>();
            firelight.transform.SetParent(stage, false);
            firelight.transform.localPosition = (pit + Vector3.up * .87f) * PropScale;
            firelight.type = LightType.Point;
            firelight.color = new Color(1f, .48f, .16f);
            firelight.range = 7 * PropScale;
            for (int i = 0; i < 4; i++)
            {
                var foot = Feet[i];
                seats[i] = Flat("camp-seat-" + (i + 1), foot + new Vector3(0, 1.22f, -.22f), i < 2 ? -2.55f : 2.55f, 2.55f, null);
                seats[i].enabled = false;
                Shadow(foot, 1.4f, .68f, .42f);
            }
            Animate(0);
        }

        public void SeatParty(PartyRun party, EncounterData content)
        {
            foreach (var seat in seats) seat.enabled = false;
            foreach (var entry in party.CaptureFormation(Side.Party))
            {
                var image = content.CampSpriteFor(entry.Character.Id);
                var seat = seats[entry.Position - 1];
                if (image == null) continue;
                Dressing.Cast[seat] = new DockSet.Player(false, content.BattleWalkFor(entry.Character.Id),
                    content.BattleSpriteFor(entry.Character.Id), true);
                SetFrame(seat, image, 1, 0);
                seat.enabled = true;
            }
        }

        public void Animate(float time)
        {
            int cel = Flicker[Mathf.FloorToInt(time * 7) % Flicker.Length];
            if (cel != lastCel)
            {
                SetFrame(flames, fireCels, 4, cel);
                lastCel = cel;
            }
            firelight.intensity = 1.38f + .17f * Mathf.Sin(time * 7.1f) + .10f * Mathf.Sin(time * 11.2f + .8f);
            FireUniforms(firelight, time);
        }
    }
}
