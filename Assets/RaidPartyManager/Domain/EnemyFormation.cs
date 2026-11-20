using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    // An ordered encounter, from the front line outwards; oversized enemies consume consecutive slots.
    public sealed class EnemyFormation
    {
        public string Name { get; }
        public EnemyRank Rank => Members[0].Rank;
        public IReadOnlyList<CharacterDefinition> Members { get; }

        public EnemyFormation(string name, IEnumerable<CharacterDefinition> members)
        {
            var lineup = members.ToArray();
            if (string.IsNullOrWhiteSpace(name) || lineup.Length == 0 ||
                lineup.Any(member => member == null || member.Rank == EnemyRank.Unspecified) ||
                lineup.Skip(1).Any(member => member.Rank != EnemyRank.Normal) ||
                lineup.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != lineup.Length ||
                lineup.Sum(member => member.PositionSlots) > Rules.Positions)
                throw new ArgumentException($"Invalid enemy formation '{name}'.");
            Name = name;
            Members = Array.AsReadOnly(lineup);
        }

        // Resizing drops rear followers first, preserving the lead enemy and the planned order.
        public IReadOnlyList<Combatant> CreateLineup(string idPrefix, int count = Rules.Positions)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count));
            var units = new Combatant[Math.Min(count, Members.Count)];
            int position = 1;
            for (int i = 0; i < units.Length; i++)
            {
                var character = Members[i];
                var stats = character.BaseStats;
                units[i] = new Combatant($"{idPrefix}-{i + 1}", character, Side.Enemy, position, stats, stats.Health);
                position += character.PositionSlots;
            }
            return Array.AsReadOnly(units);
        }
    }
}
