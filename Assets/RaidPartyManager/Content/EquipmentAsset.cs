using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [CreateAssetMenu(menuName = "Raid Party Manager/Equipment")]
    public sealed class EquipmentAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, Min(0)] private int attackBonus;
        [SerializeField, Min(0)] private int defenseBonus;
        [SerializeField, Min(0)] private int specialDefenseBonus;
        [SerializeField, Min(0)] private int healthBonus;

        public EquipmentDefinition ToDefinition() =>
            new EquipmentDefinition(id, displayName, new Stats(attackBonus, defenseBonus, specialDefenseBonus, healthBonus));
    }
}
