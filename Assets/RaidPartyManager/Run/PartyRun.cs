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
        public IReadOnlyDictionary<StatusKind, int> StartingStatuses { get; }

        public PartyMemberStart(string id, CharacterDefinition character, int position, int health,
            IReadOnlyDictionary<StatusKind, int> startingStatuses = null)
        {
            Id = id;
            Character = character;
            Position = position;
            Health = health;
            StartingStatuses = startingStatuses;
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
            public readonly PartyMemberStart Start;
            public string Id => Start.Id;
            public CharacterDefinition Character;
            public int? Position;
            public int Health;
            public int PermanentAttack;
            public IReadOnlyDictionary<StatusKind, int> StartingStatuses;
            public readonly string[] Slots = new string[Rules.EquipmentSlots];

            public Member(PartyMemberStart start)
            {
                Start = start;
                Character = start.Character;
                Position = start.Health > 0 ? start.Position : (int?)null;
                Health = start.Health;
                StartingStatuses = start.StartingStatuses;
            }
        }

        private readonly List<Member> members = new List<Member>();
        private readonly Dictionary<string, EquipmentInstance> inventory = new Dictionary<string, EquipmentInstance>();
        private readonly Dictionary<string, EquipmentDefinition> potionDefinitions = new Dictionary<string, EquipmentDefinition>();
        private readonly Dictionary<string, EquipmentDefinition> accessoryDefinitions = new Dictionary<string, EquipmentDefinition>();
        private BattleRequest activeBattle;
        private EquipmentDefinition activeReward;
        private int nextItemId;
        private int nextBattleId;
        private readonly Random rewardRandom = new Random();

        public PartyRun(IEnumerable<PartyMemberStart> startingMembers, IEnumerable<EquipmentDefinition> startingEquipment)
        {
            foreach (var entry in startingMembers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Character == null || !Rules.IsPosition(entry.Position) ||
                    entry.Health < 0 || entry.Health > entry.Character.BaseStats.Health ||
                    members.Any(member => member.Id == entry.Id || member.Position == entry.Position))
                    throw new ArgumentException("Invalid or duplicate starting member.", nameof(startingMembers));

                members.Add(new Member(entry));
            }

            if (members.Count < 1 || members.Count > Rules.Positions)
                throw new ArgumentException("The party needs one to four members.", nameof(startingMembers));
            foreach (var equipment in startingEquipment)
                AddEquipment(equipment);
        }

        public void ResetEncounter()
        {
            activeBattle = null;
            activeReward = null;
            var occupied = new HashSet<int>();
            foreach (var member in members)
            {
                member.Health = Math.Min(member.Start.Health, PersonalStats(member).Health);
                member.StartingStatuses = member.Start.StartingStatuses;
                if (!IsRooted(member) || member.Health == 0 || !member.Position.HasValue)
                    member.Position = null;
                else
                    occupied.Add(member.Position.Value);
            }
            foreach (var member in members.Where(member => member.Health > 0 && !member.Position.HasValue))
            {
                if (occupied.Add(member.Start.Position))
                    member.Position = member.Start.Position;
            }

            // A recruited member's first position may also be an original member's starting slot.
            int open = 1;
            foreach (var member in members.Where(member => member.Health > 0 && !member.Position.HasValue))
            {
                while (occupied.Contains(open))
                    open++;
                member.Position = open++;
            }
        }

        public IReadOnlyList<MemberView> Members
        {
            get
            {
                var formationStats = EquipmentFormation.Resolve(members.Where(IsDeployed).Select(ToCombatant).ToArray());
                return members.Select(member => new MemberView(member.Id, member.Character, member.Position, member.Health,
                    formationStats.TryGetValue(member.Id, out var stats) ? stats : PersonalStats(member), member.Slots)).ToArray();
            }
        }

        public IReadOnlyList<EquipmentInstance> Inventory => inventory.Values.ToArray();
        public int Gold { get; private set; }

        public bool HasRecruitmentVacancy => members.Count(IsDeployed) < Rules.Positions &&
            members.Where(IsDeployed).Any(member => EquipmentOf(member).Any(item => item.Effect == EquipmentEffect.RecruitmentNotice));

        public bool CanStartBattle => activeBattle == null && members.Any(IsDeployed) &&
            (members.All(member => member.Health == 0 || member.Position.HasValue) || HasRecruitmentVacancy);

        // Moving onto an occupied position swaps its occupant into the mover's old position.
        public bool TryPlace(string memberId, int? position)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.Health == 0 ||
                (position.HasValue && !Rules.IsPosition(position.Value)) ||
                (member.Position.HasValue && member.Position != position && IsRooted(member)))
                return false;

            if (position.HasValue)
            {
                var occupant = members.Find(other => other != member && other.Position == position);
                if (occupant != null)
                {
                    if (IsRooted(occupant))
                        return false;
                    occupant.Position = member.Position;
                }
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

            var prior = receiver.Slots[slot];
            if (prior != null && prior != equipmentId && inventory[prior].Definition.Effect == EquipmentEffect.TreeRoot)
                return false;
            if (equipmentId != null && inventory[equipmentId].Definition.Effect == EquipmentEffect.TreeRoot &&
                prior != equipmentId && members.Any(member => member.Slots.Contains(equipmentId)))
                return false;

            if (equipmentId != null)
                foreach (var member in members)
                    for (int i = 0; i < member.Slots.Length; i++)
                        if (member.Slots[i] == equipmentId)
                            member.Slots[i] = null;

            receiver.Slots[slot] = equipmentId;
            foreach (var member in members)
                member.Health = Math.Min(member.Health, PersonalStats(member).Health);
            return true;
        }

        // Future item/event systems validate and pay for evolution before changing the member's form.
        public bool TryChangeForm(string memberId, CharacterDefinition newForm)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || newForm == null)
                return false;

            member.Character = newForm;
            member.Health = Math.Min(member.Health, PersonalStats(member).Health);
            return true;
        }

        public bool TryReplaceMember(string memberId, CharacterDefinition replacement, int? lastPosition = null)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || replacement == null ||
                members.Any(existing => existing.Character.Id == replacement.Id))
                return false;

            int? position = member.Position;
            if (!position.HasValue && member.Health == 0 && lastPosition is int slot &&
                Rules.IsPosition(slot) && members.All(existing => existing.Position != slot))
                position = slot;

            // Equipment instances remain in inventory. Replacing the member releases their assigned slots.
            members[members.IndexOf(member)] = new Member(new PartyMemberStart(
                member.Id, replacement, position ?? member.Start.Position, replacement.BaseStats.Health))
            {
                Position = position
            };
            return true;
        }

        public BattleRequest BeginBattle(IEnumerable<Combatant> enemies, int tickLimit, EquipmentDefinition victoryReward,
            IEnumerable<CharacterDefinition> recruitmentPool = null, IEnumerable<CharacterDefinition> characterCatalog = null)
        {
            if (!CanStartBattle)
                throw new InvalidOperationException("Place all living members before starting a new battle.");

            var deployed = members.Where(IsDeployed).ToArray();
            var party = deployed.Select(ToCombatant);
            var enemyUnits = enemies.ToArray();
            if (enemyUnits.Any(unit => unit.Side != Side.Enemy))
                throw new ArgumentException("Encounter units must be enemies.", nameof(enemies));
            var request = new BattleRequest($"battle-{nextBattleId + 1}", party.Concat(enemyUnits), tickLimit, recruitmentPool, characterCatalog);
            nextBattleId++;
            activeBattle = request;
            activeReward = victoryReward;

            return request;
        }

        public bool TryApplyBattleLoadout(string battleId, BattleLoadout loadout)
        {
            if (activeBattle == null || activeBattle.Id != battleId || loadout == null)
                return false;

            var deployed = activeBattle.Combatants.Where(unit => unit.Side == Side.Party).ToArray();
            var original = deployed.SelectMany(unit => unit.EquipmentSlots.Where(id => id != null).Zip(unit.Equipment,
                (id, item) => (id, item))).ToDictionary(entry => entry.id, entry => entry.item);
            var equipped = loadout.Slots.Values.SelectMany(slots => slots).Where(id => id != null).ToArray();
            if (loadout.Slots.Count > Rules.Positions || loadout.Slots.Values.Any(slots => slots.Count != Rules.EquipmentSlots) ||
                deployed.Any(unit => !loadout.Slots.ContainsKey(unit.Id)) ||
                equipped.Any(id => !inventory.ContainsKey(id)) ||
                loadout.Consumed.Any(id => !original.TryGetValue(id, out var definition) || !definition.IsPotion))
                return false;
            var instances = equipped.Concat(loadout.Consumed).ToArray();
            if (instances.Length != original.Count || instances.Distinct().Count() != original.Count ||
                instances.Any(id => !original.ContainsKey(id)))
                return false;

            foreach (var unit in deployed)
                CopySlots(loadout.Slots[unit.Id], FindMember(unit.Id));
            foreach (var id in loadout.Consumed)
                inventory.Remove(id);
            return true;
        }

        private static void CopySlots(IReadOnlyList<string> slots, Member member)
        {
            for (int slot = 0; slot < member.Slots.Length; slot++)
                member.Slots[slot] = slots[slot];
        }

        public bool TrySettle(BattleResult result, out EquipmentInstance reward)
        {
            reward = null;
            if (activeBattle == null || result == null || result.Id != activeBattle.Id)
                return false;

            var recruits = result.Recruits;
            var allUnits = activeBattle.Combatants.Concat(recruits).ToArray();
            if (result.HealthById.Count != allUnits.Length ||
                allUnits.Select(unit => unit.Id).Distinct().Count() != allUnits.Length ||
                allUnits.Count(unit => unit.Side == Side.Party) > Rules.Positions ||
                recruits.Any(unit => unit.Side != Side.Party || !activeBattle.RecruitmentPool.Contains(unit.Character) ||
                    !result.HealthById.TryGetValue(unit.Id, out int health) || unit.Health != health))
                return false;

            foreach (var unit in allUnits)
                if (!result.HealthById.TryGetValue(unit.Id, out int health) || health < 0 || health > unit.Stats.Health)
                    return false;

            var positions = result.PartyPositionsById;
            if (positions.Count > 0)
            {
                var party = allUnits.Where(unit => unit.Side == Side.Party).ToArray();
                if (positions.Count != party.Length ||
                    party.Any(unit => !positions.TryGetValue(unit.Id, out int position) || !Rules.IsPosition(position)) ||
                    positions.Values.Distinct().Count() != positions.Count)
                    return false;
            }

            if ((recruits.Count > 0 && positions.Count == 0) || result.PotionsEarned < 0 ||
                result.PotionsEarned > Rules.Positions || result.GoldEarned < 0 || result.GoldEarned > int.MaxValue - Gold ||
                result.AccessoriesEarned < 0 || result.AccessoriesEarned > Rules.Positions || result.PermanentAttackGains.Any(gain =>
                    gain.Value < 0 || !allUnits.Any(unit => unit.Side == Side.Party && unit.Id == gain.Key)))
                return false;

            if (result.Loadout != null && (result.Loadout.Slots.Keys.Any(id => !allUnits.Any(unit => unit.Id == id && unit.Side == Side.Party)) ||
                !TryApplyBattleLoadout(result.Id, result.Loadout)))
                return false;

            var deployedIds = new HashSet<string>(activeBattle.Combatants.Where(unit => unit.Side == Side.Party)
                .Select(unit => unit.Id));
            foreach (var member in members.Where(member => deployedIds.Contains(member.Id)))
            {
                member.Health = Math.Min(result.HealthById[member.Id], PersonalStats(member).Health);
                member.PermanentAttack += result.PermanentAttackGains.GetValueOrDefault(member.Id);
                member.StartingStatuses = null;
                if (member.Health == 0)
                    member.Position = null;
                else if (positions.TryGetValue(member.Id, out int position))
                    member.Position = position;
            }

            foreach (var recruit in recruits.Where(unit => unit.Health > 0))
            {
                if (members.Count == Rules.Positions)
                {
                    var reserve = members.Find(member => member.Position == null && member.Health == 0)
                        ?? members.Find(member => member.Position == null);
                    members.Remove(reserve);
                }

                string id = recruit.Id;
                while (FindMember(id) != null)
                    id += "-ally";
                var start = new PartyMemberStart(id, recruit.Character, positions[recruit.Id], recruit.Character.BaseStats.Health);
                var member = new Member(start)
                {
                    Health = recruit.Health, PermanentAttack = result.PermanentAttackGains.GetValueOrDefault(recruit.Id)
                };
                if (result.Loadout != null && result.Loadout.Slots.TryGetValue(recruit.Id, out var slots))
                    CopySlots(slots, member);
                members.Add(member);
            }

            if (result.Outcome == BattleOutcome.Victory && activeReward != null)
                reward = AddEquipment(activeReward);
            Gold += result.GoldEarned;
            AddRandomRewards(potionDefinitions, result.PotionsEarned);
            AddRandomRewards(accessoryDefinitions, result.AccessoriesEarned);
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
            if (definition.IsPotion)
                potionDefinitions.TryAdd(definition.Id, definition);
            else if (definition.Effect != EquipmentEffect.RecruitmentNotice)
                accessoryDefinitions.TryAdd(definition.Id, definition);
            return item;
        }

        private void AddRandomRewards(Dictionary<string, EquipmentDefinition> pool, int count)
        {
            if (count <= 0 || pool.Count == 0)
                return;
            var choices = pool.Values.ToArray();
            for (int i = 0; i < count; i++)
                AddEquipment(choices[rewardRandom.Next(choices.Length)]);
        }

        private static bool IsDeployed(Member member) => member.Health > 0 && member.Position.HasValue;

        private bool IsRooted(Member member) => EquipmentOf(member).Any(item => item.Effect == EquipmentEffect.TreeRoot);

        private IEnumerable<EquipmentDefinition> EquipmentOf(Member member) =>
            member.Slots.Where(id => id != null).Select(id => inventory[id].Definition);

        private Stats PersonalStats(Member member) =>
            EquipmentOf(member).Aggregate(member.Character.BaseStats + new Stats(member.PermanentAttack, 0, 0, 0),
                (stats, item) => stats + item.Bonus);

        private Combatant ToCombatant(Member member) => new Combatant(member.Id, member.Character, Side.Party,
            member.Position.Value, PersonalStats(member), member.Health, EquipmentOf(member), member.StartingStatuses, member.Slots);
    }
}
