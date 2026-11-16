using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public sealed class PartyMemberStart
    {
        public string Id { get; }
        public CharacterDefinition Character { get; }
        public int Position { get; }
        public int Health { get; }

        public PartyMemberStart(string id, CharacterDefinition character, int position, int health)
        {
            Id = id;
            Character = character;
            Position = position;
            Health = health;
        }
    }

    public sealed class EquipmentInstance
    {
        public string Id { get; }
        public EquipmentDefinition Definition { get; }

        internal EquipmentInstance(string id, EquipmentDefinition definition)
        {
            Id = id;
            Definition = definition;
        }
    }

    public sealed class MemberView
    {
        public string Id { get; }
        public CharacterDefinition Character { get; }
        public int? Position { get; }
        public int Health { get; }
        public Stats EffectiveStats { get; }
        public IReadOnlyList<string> EquipmentIds { get; }

        internal MemberView(string id, CharacterDefinition character, int? position, int health, Stats stats, string[] slots)
        {
            Id = id;
            Character = character;
            Position = position;
            Health = health;
            EffectiveStats = stats;
            EquipmentIds = Array.AsReadOnly((string[])slots.Clone());
        }
    }

    public sealed class PartyRun
    {
        private sealed class Member
        {
            public string Id;
            public CharacterDefinition Character;
            public int? Position;
            public int Health;
            public readonly string[] Slots = new string[Rules.EquipmentSlots];
        }

        private readonly List<Member> members = new List<Member>();
        private readonly Dictionary<string, EquipmentInstance> inventory = new Dictionary<string, EquipmentInstance>();
        private BattleRequest activeBattle;
        private EquipmentDefinition activeReward;
        private int nextItemId;
        private int nextBattleId;

        public PartyRun(IEnumerable<PartyMemberStart> startingMembers, IEnumerable<EquipmentDefinition> startingEquipment)
        {
            foreach (var entry in startingMembers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Character == null || !Rules.IsPosition(entry.Position) ||
                    entry.Health < 0 || entry.Health > entry.Character.BaseStats.Health ||
                    members.Any(member => member.Id == entry.Id || member.Position == entry.Position))
                    throw new ArgumentException("Invalid or duplicate starting member.", nameof(startingMembers));

                members.Add(new Member
                {
                    Id = entry.Id, Character = entry.Character,
                    Position = entry.Health > 0 ? entry.Position : (int?)null, Health = entry.Health
                });
            }

            if (members.Count < 1 || members.Count > Rules.Positions)
                throw new ArgumentException("The party needs one to four members.", nameof(startingMembers));
            foreach (var equipment in startingEquipment)
                AddEquipment(equipment);
        }

        public IReadOnlyList<MemberView> Members => members.Select(member =>
            new MemberView(member.Id, member.Character, member.Position, member.Health, EffectiveStats(member), member.Slots)).ToArray();

        public IReadOnlyList<EquipmentInstance> Inventory => inventory.Values.ToArray();

        public bool CanStartBattle => activeBattle == null && members.Any(member => member.Health > 0) &&
            members.All(member => member.Health == 0 || member.Position.HasValue);

        // Moving onto an occupied position swaps its occupant into the mover's old position.
        public bool TryPlace(string memberId, int? position)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.Health == 0 ||
                (position.HasValue && !Rules.IsPosition(position.Value)))
                return false;

            if (position.HasValue)
            {
                var occupant = members.Find(other => other != member && other.Position == position);
                if (occupant != null)
                    occupant.Position = member.Position;
            }

            member.Position = position;
            return true;
        }

        // Assigning an existing instance moves it out of any prior slot. Replaced gear stays in the bag.
        public bool TryEquip(string memberId, int slot, string equipmentId)
        {
            var receiver = FindMember(memberId);
            if (activeBattle != null || receiver == null || slot < 0 || slot >= Rules.EquipmentSlots ||
                (equipmentId != null && !inventory.ContainsKey(equipmentId)))
                return false;

            if (equipmentId != null)
                foreach (var member in members)
                    for (int i = 0; i < member.Slots.Length; i++)
                        if (member.Slots[i] == equipmentId)
                            member.Slots[i] = null;

            receiver.Slots[slot] = equipmentId;
            foreach (var member in members)
                member.Health = Math.Min(member.Health, EffectiveStats(member).Health);
            return true;
        }

        // Future item/event systems validate and pay for evolution before changing the member's form.
        public bool TryChangeForm(string memberId, CharacterDefinition newForm)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || newForm == null)
                return false;

            member.Character = newForm;
            member.Health = Math.Min(member.Health, EffectiveStats(member).Health);
            return true;
        }

        public BattleRequest BeginBattle(IEnumerable<Combatant> enemies, int tickLimit, EquipmentDefinition victoryReward)
        {
            if (!CanStartBattle)
                throw new InvalidOperationException("Place all living members before starting a new battle.");

            var party = members.Where(member => member.Health > 0).Select(member =>
                new Combatant(member.Id, member.Character, Side.Party, member.Position.Value,
                    EffectiveStats(member), member.Health));
            var enemyUnits = enemies.ToArray();
            if (enemyUnits.Any(unit => unit.Side != Side.Enemy))
                throw new ArgumentException("Encounter units must be enemies.", nameof(enemies));
            var request = new BattleRequest($"battle-{nextBattleId + 1}", party.Concat(enemyUnits), tickLimit);
            nextBattleId++;
            activeBattle = request;
            activeReward = victoryReward;
            return request;
        }

        public bool TrySettle(BattleResult result, out EquipmentInstance reward)
        {
            reward = null;
            if (activeBattle == null || result == null || result.Id != activeBattle.Id ||
                result.HealthById.Count != activeBattle.Combatants.Count)
                return false;

            foreach (var unit in activeBattle.Combatants)
                if (!result.HealthById.TryGetValue(unit.Id, out int health) || health < 0 || health > unit.Stats.Health)
                    return false;

            foreach (var member in members)
            {
                if (result.HealthById.TryGetValue(member.Id, out int health))
                    member.Health = health;
                if (member.Health == 0)
                    member.Position = null;
            }

            if (result.Outcome == BattleOutcome.Victory && activeReward != null)
                reward = AddEquipment(activeReward);
            activeBattle = null;
            activeReward = null;
            return true;
        }

        private Member FindMember(string id) => members.Find(member => member.Id == id);

        private EquipmentInstance AddEquipment(EquipmentDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            var item = new EquipmentInstance($"gear-{++nextItemId}", definition);
            inventory.Add(item.Id, item);
            return item;
        }

        private Stats EffectiveStats(Member member)
        {
            var stats = member.Character.BaseStats;
            foreach (var id in member.Slots)
                if (id != null)
                    stats += inventory[id].Definition.Bonus;
            return stats;
        }
    }
}
