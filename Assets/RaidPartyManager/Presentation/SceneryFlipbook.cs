using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class SceneryFlipbook
    {
        internal const int FrameCount = 6;
        private readonly Texture2D[] frames = new Texture2D[FrameCount];
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        internal SceneryFlipbook(Texture2D source)
        {
            string format = source.width > 2 * source.height ? "Panoramas" : "Backdrops";
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Resources.Load<Texture2D>($"SceneryCels/{format}/{i}/{source.name}") ?? source;
        }

        internal Texture2D Current => frames[(int)(Time.unscaledTimeAsDouble * 4) % FrameCount];

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
            return element.schedule.Execute(Draw).Every(100);
        }
    }
}
