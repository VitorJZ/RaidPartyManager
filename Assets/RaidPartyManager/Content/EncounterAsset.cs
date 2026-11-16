using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    public sealed class EncounterData
    {
        public string Name { get; }
        public IReadOnlyList<Combatant> Party { get; }
        public IReadOnlyList<Combatant> Enemies { get; }
        public IReadOnlyList<EquipmentDefinition> StartingEquipment { get; }
        public EquipmentDefinition VictoryReward { get; }
        public int TickLimit { get; }
        private readonly IReadOnlyDictionary<string, Texture2D> portraits;

        internal EncounterData(string name, Combatant[] party, Combatant[] enemies,
            EquipmentDefinition[] startingEquipment, EquipmentDefinition victoryReward, int tickLimit, IReadOnlyDictionary<string, Texture2D> portraits)
        {
            Name = name;
            Party = Array.AsReadOnly(party);
            Enemies = Array.AsReadOnly(enemies);
            StartingEquipment = Array.AsReadOnly(startingEquipment);
            VictoryReward = victoryReward;
            TickLimit = tickLimit;
            this.portraits = portraits;
        }

        public Texture2D PortraitFor(string definitionId) =>
            definitionId != null && portraits.TryGetValue(definitionId, out var portrait) ? portrait : null;
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Encounter")]
    public sealed class EncounterAsset : ScriptableObject
    {
        [Serializable]
        private sealed class UnitEntry
        {
            [SerializeField] private string instanceId;
            [SerializeField] private CharacterAsset character;
            [SerializeField, Range(1, Rules.Positions)] private int position = 1;
            [Tooltip("-1 uses the character's maximum health.")]
            [SerializeField, Min(-1)] private int startingHealth = -1;

            public Texture2D Portrait => character.Portrait;

            public Combatant ToCombatant(Side side)
            {
                if (character == null)
                    throw new InvalidOperationException($"Encounter unit '{instanceId}' needs a character asset.");
                var definition = character.ToDefinition();
                int health = startingHealth == -1 ? definition.BaseStats.Health : startingHealth;
                return new Combatant(instanceId, definition, side, position, definition.BaseStats, health);
            }
        }

        [SerializeField] private string displayName;
        [SerializeField] private UnitEntry[] party = Array.Empty<UnitEntry>();
        [SerializeField] private UnitEntry[] enemies = Array.Empty<UnitEntry>();
        [SerializeField] private EquipmentAsset[] startingEquipment = Array.Empty<EquipmentAsset>();
        [SerializeField] private EquipmentAsset victoryReward;
        [SerializeField, Min(1)] private int tickLimit = 200;

        public EncounterData ToData()
        {
            var partyInputs = party.Select(entry => entry.ToCombatant(Side.Party)).ToArray();
            var enemyInputs = enemies.Select(entry => entry.ToCombatant(Side.Enemy)).ToArray();
            // Fail at startup if a content author has created an invalid formation or duplicate ID.
            _ = new BattleRequest("encounter-validation", partyInputs.Concat(enemyInputs), tickLimit);
            var portraits = party.Concat(enemies).Zip(partyInputs.Concat(enemyInputs),
                    (entry, unit) => (unit.Character.Id, entry.Portrait))
                .Where(entry => entry.Portrait != null)
                .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Portrait, StringComparer.Ordinal);
            return new EncounterData(displayName, partyInputs, enemyInputs,
                startingEquipment.Select(item => item.ToDefinition()).ToArray(),
                victoryReward != null ? victoryReward.ToDefinition() : null, tickLimit, portraits);
        }
    }
}
