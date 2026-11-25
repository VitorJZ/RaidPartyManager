using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class GardenBloomPlayback
    {
        private const float Duration = 3.55f;
        private readonly VisualElement layer = new() { name = "gardener-recruitment", pickingMode = PickingMode.Ignore };
        private readonly StageActorImage seed;
        private readonly StageActorImage blossom;
        private readonly StageActorImage gardener;
        private readonly Texture2D walk;
        private readonly Texture2D pick;
        private readonly Label caption;
        private readonly Rect wearer;
        private readonly double started;
        private bool seedPlayed, bloomPlayed, pluckPlayed, greeted;

        public GardenBloomPlayback(BattleStage stage, Texture2D seedArt, Texture2D walk, Rect wearer, double started)
        {
            this.wearer = wearer;
            this.started = started;
            this.walk = walk;
            pick = Resources.Load<Texture2D>("StoryEvents/GraveSeed/PickFlower");
            PrototypeStyle.Fill(layer);
            seed = Figure(stage, seedArt);
            blossom = Figure(stage, Resources.Load<Texture2D>("StoryEvents/GraveSeed/Bloom"));
            gardener = Figure(stage, walk);
            caption = PrototypeStyle.Text("「这朵花是你们种的？那我也跟你们走！」", 20, PrototypeStyle.Ink);
            caption.pickingMode = PickingMode.Ignore;
            caption.style.position = Position.Absolute;
            caption.style.bottom = Length.Percent(18);
            caption.style.alignSelf = Align.Center;
            caption.style.backgroundColor = PrototypeStyle.Background;
            caption.style.paddingLeft = caption.style.paddingRight = 16;
            caption.style.paddingTop = caption.style.paddingBottom = 6;
            layer.Add(caption);
        }

        public bool Draw(VisualElement root, double now)
        {
            float time = (float)(now - started);
            if (time >= Duration)
            {
                layer.RemoveFromHierarchy();
                return false;
            }

            if (layer.parent != root) root.Add(layer);
            float width = root.contentRect.width;
            if (width <= 0) return true;
            float actorSize = Mathf.Clamp(wearer.height * 1.05f, 82, 205);
            float ground = wearer.yMax + actorSize * .16f;
            float flowerX = Mathf.Clamp(wearer.center.x + actorSize * .67f, actorSize * 1.15f, width * .81f);
            const float touchdown = .59f, fullyOpen = 1.54f, arrive = 2.32f, pluck = 2.72f;

            if (!seedPlayed)
            {
                GameAudio.Play(SoundCue.EventSeedBounce, .52f);
                seedPlayed = true;
            }
            if (!bloomPlayed && time >= touchdown)
            {
                GameAudio.Play(SoundCue.EventSeedBloom, .52f);
                bloomPlayed = true;
            }
            if (!pluckPlayed && time >= pluck)
            {
                GameAudio.Play(SoundCue.EventGardenerPluck, .52f);
                pluckPlayed = true;
            }

            seed.visible = time < touchdown;
            if (seed.visible)
            {
                float hop = Mathf.Clamp01(time / touchdown);
                float y = Mathf.Lerp(wearer.center.y, ground, hop) - Mathf.Sin(hop * Mathf.PI) * actorSize * .70f;
                Place(seed, Mathf.Lerp(wearer.center.x, flowerX, hop), y, actorSize * .25f);
            }

            blossom.visible = time >= touchdown && time < pluck;
            if (blossom.visible)
            {
                Place(blossom, flowerX, ground, actorSize * .58f);
                if (blossom.image != null)
                    blossom.sourceRect = AnimationCels.RectFor(blossom.image, AnimationCels.At((time - touchdown) / (fullyOpen - touchdown)));
            }

            gardener.visible = walk != null && time >= 1.26f;
            if (gardener.visible)
            {
                float t = Mathf.InverseLerp(1.26f, arrive, time);
                float standingX = flowerX - actorSize * .37f;
                float x = Mathf.Lerp(-actorSize * .5f, standingX, t);
                Place(gardener, x, ground + actorSize * .045f, actorSize);
                bool picking = pick != null && time >= arrive;
                gardener.image = picking ? pick : walk;
                int frame = picking ? AnimationCels.At((time - arrive) / .74f) : AnimationCels.Loop(now - started, .66);
                gardener.sourceRect = AnimationCels.RectFor(gardener.image, frame);
            }

            caption.visible = time >= pluck;
            if (caption.visible && !greeted)
            {
                GameAudio.Play(SoundCue.CompanionJoin);
                greeted = true;
            }
            return true;
        }

        private StageActorImage Figure(BattleStage stage, Texture2D art)
        {
            var image = new StageActorImage(stage, inMotion: true)
            {
                image = art, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore
            };
            image.style.position = Position.Absolute;
            layer.Add(image);
            return image;
        }

        private static void Place(VisualElement image, float x, float floor, float size)
        {
            image.style.width = image.style.height = size;
            image.style.left = x - size / 2;
            image.style.top = floor - size * .94f;
        }
    }
}
