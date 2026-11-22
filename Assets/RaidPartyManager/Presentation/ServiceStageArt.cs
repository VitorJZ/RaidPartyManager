using UnityEngine;

namespace RaidPartyManager.Presentation
{
    public sealed class ServiceStageArt : ScriptableObject
    {
        public Texture2D graveyardSky, gravestones, graveyardStone, cemeteryGate, lanternTree, bellArch;
        public Texture2D mourningSquare, watchCloister, estateSale, mortuaryOffice, yewPath;
        public Texture2D wakeShop, vestryHall;
        public Texture2D recordPigeonholes, sealPress, funeralBarrow, stoneUrn, vigilStove, blanketBench;
        public Texture2D wreathCart, bellStand, lotCabinet, drapedReliquary, candleRack, vestmentStand;
        public Texture2D buoyRack, lobsterPots, canvasBundles, herbDryer, chandlerShelf, guildTrophies, tackleChest, porterDolly;
        public Texture2D[] graveyardFacilities;
        public Texture2D mooring, firewood, parcel, creel;
        public Texture2D seatBack, ferns;
        public Texture2D marketHall, registryDesk, contractBoard, guildParquet;
        public Shader shader;
        public Texture2D campSky, soil, bark, endGrain, townSky, timber, cobbles, counterFront;
        // Recruitment, shop, camp and quest residents use 4 × 4 animation atlases.
        public Texture2D[] facilities, residents;
    }
}
