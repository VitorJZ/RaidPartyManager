using System;
using UnityEngine;

namespace RaidPartyManager.Presentation
{
    public sealed class BattleStageArt : ScriptableObject
    {
        [Serializable]
        public sealed class Set
        {
            public Texture2D region;
            public Texture2D backcloth;
            public Texture2D floor;
            public Texture2D upstage;
            public bool moored;
            public bool turntable;
            public Texture2D leftWing;
            public Texture2D rightWing;
            public Texture2D leftDressing, rightDressing;
        }

        public Texture2D mooring, creel;
        public Texture2D seatBack, apronWood, breakers;
        public Shader paperShader;
        public Texture2D drapery;
        public Set[] sets;
        public Set For(Texture2D region) => Array.Find(sets, set => set.region == region);
    }
}
