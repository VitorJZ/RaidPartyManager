using UnityEngine;

namespace RaidPartyManager.Presentation
{
    internal static class AnimationCels
    {
        public const int Count = 16;

        public static int At(float progress) => Mathf.Clamp(Mathf.FloorToInt(progress * Count), 0, Count - 1);
        public static float Phase(float progress) => At(progress) / (Count - 1f);
        public static int Loop(double time, double seconds) => (int)(time / seconds * Count) % Count;

        // UI Toolkit uses a top-left source rectangle; shader UVs start at the bottom left.
        public static Rect RectFor(Texture texture, int frame, int columns = 4, int rows = 4)
        {
            float w = (float)texture.width / columns, h = (float)texture.height / rows;
            return new Rect(frame % columns * w, frame / columns * h, w, h);
        }

        public static Vector4 UV(int frame, int columns = 4, int rows = 4, bool mirror = false)
        {
            float w = 1f / columns, h = 1f / rows;
            return new Vector4(mirror ? -w : w, h, (frame % columns + (mirror ? 1 : 0)) * w,
                1 - (frame / columns + 1) * h);
        }
    }
}

