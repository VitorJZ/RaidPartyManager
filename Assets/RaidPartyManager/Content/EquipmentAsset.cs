using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [CreateAssetMenu(menuName = "Raid Party Manager/Equipment")]
    public sealed class EquipmentAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private EquipmentEffect effect;
        [SerializeField] private Texture2D icon;
        [SerializeField, Range(-1, 3)] private int shopTier = 2;
        [SerializeField, Min(1)] private int shopPrice = 20;
        [SerializeField] private bool excludeFromBattleLoot;

        [SerializeField] private string rewardBossId;

        public Texture2D Icon => icon;
        public int ShopTier => shopTier;
        public int ShopPrice => shopPrice;
        [SerializeField, TextArea] private string description;
        [SerializeField, Min(0)] private int attackBonus;
        [SerializeField, Min(0)] private int defenseBonus;
        [SerializeField, Min(0)] private int specialDefenseBonus;
        [SerializeField, Min(0)] private int healthBonus;

        public EquipmentDefinition ToDefinition() =>
            new EquipmentDefinition(id, displayName, new Stats(attackBonus, defenseBonus, specialDefenseBonus, healthBonus), description, effect, shopTier, shopPrice, !excludeFromBattleLoot, rewardBossId);
    }
}
