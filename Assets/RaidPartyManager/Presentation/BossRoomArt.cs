using UnityEngine;

namespace RaidPartyManager.Presentation
{
    // One asset per boss. Loading a room brings in only that boss's scenic pieces.
    internal sealed class BossRoomArt : ScriptableObject
    {
        public string roomName;
        public Texture2D[] backdrop;
        public Texture2D floor, leftWing, rightWing;
        // X is the fraction across the stage, Y the fraction upstage, Z the height in metres.
        public Vector3 leftMark, rightMark;
        public Vector2 floorTile;
        public Vector4 keyLight, rimLight, mist;
        public float mistHeight, loopSeconds;

        internal StageLighting Lighting => new StageLighting(keyLight, rimLight, mist, mistHeight);

        internal BattleStageArt.Set Dress() => new BattleStageArt.Set
        {
            region = backdrop[0], backcloth = backdrop[0], floor = floor, leftWing = leftWing,
            rightWing = rightWing, turntable = true, room = this
        };
    }
}
