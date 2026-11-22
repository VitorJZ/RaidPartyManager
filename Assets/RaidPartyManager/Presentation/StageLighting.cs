using UnityEngine;

namespace RaidPartyManager.Presentation
{
    internal enum StageVenue { Outdoors, Interior, Firelight }

    // Each set keeps its cue on its own materials, including copies travelling on the turntable.
    internal readonly struct StageLighting
    {
        private readonly Vector4 key, back, mist;
        private readonly float mistHeight;
        private static readonly int Key = Shader.PropertyToID("_StageKey");
        private static readonly int Back = Shader.PropertyToID("_StageBack");
        private static readonly int Mist = Shader.PropertyToID("_StageMist");
        private static readonly int Height = Shader.PropertyToID("_MistHeight");

        internal StageLighting(Vector4 key, Vector4 back, Vector4 mist, float mistHeight)
        {
            this.key = key;
            this.back = back;
            this.mist = mist;
            this.mistHeight = mistHeight;
        }

        internal static StageLighting Rehearsal => new(new(1.04f, .99f, .92f, -4.4f),
            new(.71f, .81f, .98f, -.5f), Vector4.zero, 1);

        internal static StageLighting Clockwork(StageVenue venue) => new(
            venue == StageVenue.Firelight ? new(.80f,.70f,.69f,-3.6f) : new(1.19f,.98f,.76f,-3.6f),
            new(.38f,.91f,1.04f,-.6f), new(.42f,.55f,.54f,venue == StageVenue.Interior ? .018f : .067f), 1.45f);

        internal static StageLighting For(bool cemetery, StageVenue venue = StageVenue.Outdoors)
        {
            // RGB is lamp output. Key.w locates the front bar lamp in X; back.w is the acting mark in Z.
            Vector4 key = cemetery ? new(.74f, .90f, 1.18f, 4.4f) : new(1.20f, .99f, .74f, -4.4f);
            Vector4 back = cemetery ? new(.73f, .83f, 1.32f, -.4f) : new(.53f, .88f, 1.02f, -.6f);
            Vector4 mist = cemetery ? new(.29f, .37f, .49f, .092f) : new(.41f, .53f, .56f, .055f);
            if (venue == StageVenue.Interior)
            {
                key = cemetery ? new(1.11f, .96f, .86f, 3.2f) : new(1.28f, 1.06f, .83f, -3.2f);
                back.w = -1.6f;
                mist.w *= .22f;
            }
            else if (venue == StageVenue.Firelight)
            {
                key = new(.56f, .67f, .88f, key.w);
                back.w = .8f;
                mist.w *= .60f;
            }
            return new StageLighting(key, back, mist, cemetery ? .88f : 1.85f);
        }

        internal void Apply(Material material)
        {
            material.SetVector(Key, key);
            material.SetVector(Back, back);
            material.SetVector(Mist, mist);
            material.SetFloat(Height, mistHeight);
        }

        internal static StageLighting Crossfade(StageLighting from, StageLighting to, float t) =>
            new(Vector4.Lerp(from.key, to.key, t), Vector4.Lerp(from.back, to.back, t),
                Vector4.Lerp(from.mist, to.mist, t), Mathf.Lerp(from.mistHeight, to.mistHeight, t));
    }
}
