using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class SceneryFlipbook
    {
        internal const int FrameCount = AnimationCels.Count;
        private readonly Texture2D[] frames = new Texture2D[FrameCount];
        private readonly double seconds = 1.5;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        internal SceneryFlipbook(Texture2D source)
        {
            string format = source.width > 2 * source.height ? "Panoramas" : "Backdrops";
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Resources.Load<Texture2D>($"SceneryCels/{format}/{i}/{source.name}") ?? source;
        }

        internal SceneryFlipbook(Texture2D[] cels, float seconds)
        {
            frames = cels;
            this.seconds = seconds;
        }

        internal Texture2D Current => frames[AnimationCels.Loop(Time.unscaledTimeAsDouble, seconds)];

        internal void Apply(MeshRenderer renderer, MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(block);
            var frame = Current;
            if (block.GetTexture(MainTex) == frame) return;
            block.SetTexture(MainTex, frame);
            renderer.SetPropertyBlock(block);
        }

        internal static IVisualElementScheduledItem Animate(VisualElement element, Texture2D source)
        {
            var strip = new SceneryFlipbook(source);
            Texture2D last = null;
            void Draw()
            {
                var cel = strip.Current;
                if (last == cel) return;
                last = cel;
                if (element is Image image) image.image = cel;
                else element.style.backgroundImage = new StyleBackground(cel);
            }
            Draw();
            return element.schedule.Execute(Draw).Every(40);
        }
    }
}
