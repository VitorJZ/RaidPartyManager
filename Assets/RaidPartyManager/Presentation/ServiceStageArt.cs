using UnityEngine;

namespace RaidPartyManager.Presentation
{
    public sealed class ServiceStageArt : ScriptableObject
    {
        public Texture2D mooring, firewood, parcel, creel;
        public Texture2D seatBack, ferns;
        public Shader shader;
        public Texture2D campSky, soil, bark, endGrain, townSky, timber, cobbles, counterFront;
        // Recruit, shop, camp, quest; each resident strip has four equally sized cels.
        public Texture2D[] facilities, residents;
    }
}
