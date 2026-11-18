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
        public IReadOnlyList<SkillBookDefinition> LearnedSkills { get; }
        public int SkillSlotCount => SkillSlotsFor(Character);

        public bool IsAway { get; }

        internal static int SkillSlotsFor(CharacterDefinition character) => character.Stage >= 2 ? 2 : 1;

        internal MemberView(string id, CharacterDefinition character, int? position, int health, Stats stats, string[] slots,
            SkillBookDefinition[] learnedSkills, bool isAway)
        {
            Id = id;
            Character = character;
            Position = position;
            Health = health;
            EffectiveStats = stats;
            EquipmentIds = Array.AsReadOnly((string[])slots.Clone());
            LearnedSkills = Array.AsReadOnly((SkillBookDefinition[])learnedSkills.Clone());
            IsAway = isAway;
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
            public bool IsAway;
            public IReadOnlyDictionary<StatusKind, int> StartingStatuses;
            public readonly string[] Slots = new string[Rules.EquipmentSlots];
            public readonly SkillBookDefinition[] LearnedSkills = new SkillBookDefinition[2];

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
        private int nextBookId;
        private int nextEditorItemId;
        private int nextBattleId;
        private readonly Random rewardRandom = new Random();

        public PartyRun(IEnumerable<PartyMemberStart> startingMembers, IEnumerable<EquipmentDefinition> startingEquipment)
        {
            members.AddRange(ValidateRoster(startingMembers));
            foreach (var equipment in startingEquipment)
                AddEquipment(equipment);
        }

        private static List<Member> ValidateRoster(IEnumerable<PartyMemberStart> startingMembers)
        {
            var roster = new List<Member>();
            foreach (var entry in startingMembers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Character == null || !Rules.IsPosition(entry.Position) ||
                    entry.Health < 0 || entry.Health > entry.Character.BaseStats.Health ||
                    roster.Any(member => member.Id == entry.Id || member.Start.Position == entry.Position))
                    throw new ArgumentException("Invalid or duplicate starting member.", nameof(startingMembers));

                roster.Add(new Member(entry));
            }

            if (roster.Count < 1 || roster.Count > Rules.Positions)
                throw new ArgumentException("The party needs one to four members.", nameof(startingMembers));
            return roster;
        }

        public void ResetRoster(IEnumerable<PartyMemberStart> startingMembers)
        {
            var roster = ValidateRoster(startingMembers);
            ResetEncounter();
            members.Clear();
            members.AddRange(roster);
        }

        public void ResetEncounter()
        {
            activeBattle = null;
            activeReward = null;
            var occupied = new HashSet<int>();
            foreach (var member in members)
            {
                if (member.IsAway)
                    continue;
                member.Health = Math.Min(member.Start.Health, PersonalStats(member).Health);
                member.StartingStatuses = member.Start.StartingStatuses;
                if (!IsRooted(member) || member.Health == 0 || !member.Position.HasValue)
                    member.Position = null;
                else
                    occupied.Add(member.Position.Value);
            }
            foreach (var member in members.Where(member => !member.IsAway && member.Health > 0 && !member.Position.HasValue))
            {
                if (occupied.Add(member.Start.Position))
                    member.Position = member.Start.Position;
            }

            // A recruited member's first position may also be an original member's starting slot.
            int open = 1;
            foreach (var member in members.Where(member => !member.IsAway && member.Health > 0 && !member.Position.HasValue))
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
                var formationStats = EquipmentFormation.Resolve(CaptureFormation(Side.Party));
                return members.Select(member => new MemberView(member.Id, member.Character, member.Position, member.Health,
                    formationStats.TryGetValue(member.Id, out var stats) ? stats : PersonalStats(member), member.Slots,
                    member.LearnedSkills, member.IsAway)).ToArray();
            }
        }

        public IReadOnlyList<EquipmentInstance> Inventory => inventory.Values.Where(item => item.Definition is not SkillBookDefinition).ToArray();
        public IReadOnlyList<EquipmentInstance> SkillBooks => inventory.Values.Where(item => item.Definition is SkillBookDefinition).ToArray();
        public int Gold { get; private set; }

        public IReadOnlyList<Combatant> CaptureFormation(Side side) =>
            members.Where(IsDeployed).Select(member => ToCombatant(member, side)).ToArray();

        public bool HasRecruitmentVacancy => members.Count(IsDeployed) + members.Count(member => member.IsAway) < Rules.Positions &&
            members.Where(IsDeployed).Any(member => EquipmentOf(member).Any(item => item.Effect == EquipmentEffect.RecruitmentNotice));

        public bool CanStartBattle => activeBattle == null && members.Any(IsDeployed) &&
            (members.All(member => member.IsAway || member.Health == 0 || member.Position.HasValue) || HasRecruitmentVacancy);

        // Moving onto an occupied position swaps its occupant into the mover's old position.
        public bool TryPlace(string memberId, int? position)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.IsAway || member.Health == 0 ||
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
            if (activeBattle != null || receiver == null || receiver.IsAway || slot < 0 || slot >= Rules.EquipmentSlots ||
                (equipmentId != null && (!inventory.TryGetValue(equipmentId, out var item) || item.Definition is SkillBookDefinition)))
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

        public bool CanLearnSkill(string memberId, int slot, string bookId)
        {
            var member = FindMember(memberId);
            return activeBattle == null && member != null && !member.IsAway && member.Health > 0 &&
                slot >= 0 && slot < MemberView.SkillSlotsFor(member.Character) && member.LearnedSkills[slot] == null &&
                bookId != null && inventory.TryGetValue(bookId, out var item) && item.Definition is SkillBookDefinition book &&
                book.CanLearn(member.Character) && !member.LearnedSkills.Any(known => known?.Id == book.Id);
        }

        public bool TryLearnSkill(string memberId, int slot, string bookId)
        {
            if (!CanLearnSkill(memberId, slot, bookId))
                return false;

            FindMember(memberId).LearnedSkills[slot] = (SkillBookDefinition)inventory[bookId].Definition;
            inventory.Remove(bookId);
            return true;
        }

        // Future item/event systems validate and pay for evolution before changing the member's form.
        public bool TryChangeForm(string memberId, CharacterDefinition newForm)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.IsAway || newForm == null)
                return false;

            member.Character = newForm;
            member.Health = Math.Min(member.Health, PersonalStats(member).Health);
            return true;
        }

        public bool TryReplaceMember(string memberId, CharacterDefinition replacement, int? lastPosition = null)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.IsAway || replacement == null ||
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

            var party = CaptureFormation(Side.Party);
            var enemyUnits = enemies.ToArray();
            if (enemyUnits.Any(unit => unit.Side != Side.Enemy))
                throw new ArgumentException("Encounter units must be enemies.", nameof(enemies));
            // An absent companion retains a party slot, including against recruitment-notice summons.
            if (members.Any(member => member.IsAway) && members.Count(member => member.Health > 0) >= Rules.Positions)
                recruitmentPool = null;
            var request = new BattleRequest($"battle-{nextBattleId + 1}", party.Concat(enemyUnits), tickLimit, recruitmentPool, characterCatalog, Gold);
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
            var guaranteed = result?.Outcome == BattleOutcome.Victory ? activeReward : null;
            if (!TryFinishBattle(result, out var loot))
                return false;
            Gold += result.GoldEarned;
            for (int i = 0; i < loot.Count; i++)
            {
                var item = AddEquipment(loot[i]);
                if (i == 0 && guaranteed != null)
                    reward = item;
            }
            return true;
        }

        // Commit the outcome before presenting the rewards for collection.
        public bool TryFinishBattle(BattleResult result, out IReadOnlyList<EquipmentDefinition> loot)
        {
            loot = Array.Empty<EquipmentDefinition>();
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
                member.Health = Math.Max(1, Math.Min(result.HealthById[member.Id], PersonalStats(member).Health));
                member.PermanentAttack += result.PermanentAttackGains.GetValueOrDefault(member.Id);
                member.StartingStatuses = null;
                if (positions.TryGetValue(member.Id, out int position))
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

            var rewards = new List<EquipmentDefinition>();
            if (result.Outcome == BattleOutcome.Victory && activeReward != null)
            {
                rewards.Add(activeReward);
                RegisterReward(activeReward);
            }
            RollRandomRewards(potionDefinitions, result.PotionsEarned, rewards);
            RollRandomRewards(accessoryDefinitions, result.AccessoriesEarned, rewards);
            loot = rewards.AsReadOnly();
            activeBattle = null;
            activeReward = null;
            return true;
        }

        private Member FindMember(string id) => members.Find(member => member.Id == id);

        public void AddGold(int amount)
        {
            if (amount < 0 || Gold > int.MaxValue - amount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Gold += amount;
        }

        public bool TrySpendGold(int amount)
        {
            if (activeBattle != null || amount < 0 || Gold < amount)
                return false;
            Gold -= amount;
            return true;
        }

        public void CancelBattle()
        {
            activeBattle = null;
            activeReward = null;
        }

        public int Rest(double fraction)
        {
            if (activeBattle != null || fraction <= 0 || fraction > 1)
                return 0;
            int healed = 0;
            foreach (var member in members.Where(member => member.Health > 0 && !member.IsAway))
            {
                int before = member.Health;
                int maximum = PersonalStats(member).Health;
                member.Health = Math.Min(maximum, member.Health + (int)Math.Ceiling(maximum * fraction));
                healed += member.Health - before;
            }
            return healed;
        }

        public bool TryConsumeItems(IReadOnlyList<string> ids)
        {
            if (activeBattle != null || ids == null || ids.Count == 0 || ids.Distinct().Count() != ids.Count ||
                ids.Any(id => id == null || !inventory.TryGetValue(id, out var item) || item.Definition is SkillBookDefinition ||
                    members.Any(member => member.Slots.Contains(id))))
                return false;
            foreach (var id in ids)
                inventory.Remove(id);
            return true;
        }

        public bool TryRecruit(CharacterDefinition character)
        {
            if (activeBattle != null || character == null || members.Any(member => member.Character.Id == character.Id))
                return false;
            var dead = members.FirstOrDefault(member => member.Health == 0);
            if (members.Count >= Rules.Positions && dead == null)
                return false;
            if (dead != null)
                return TryReplaceMember(dead.Id, character, Enumerable.Range(1, Rules.Positions)
                    .First(slot => members.All(member => member.Position != slot)));
            int position = Enumerable.Range(1, Rules.Positions).First(slot => members.All(member => member.Position != slot));
            string id = "recruit-" + character.Id;
            while (FindMember(id) != null)
                id += "-ally";
            members.Add(new Member(new PartyMemberStart(id, character, position, character.BaseStats.Health)));
            return true;
        }

        public bool CanSendAway(string memberId) => CanSendAway(FindMember(memberId));

        private bool CanSendAway(Member member) => activeBattle == null && member != null && !member.IsAway &&
            IsDeployed(member) && !IsRooted(member) && members.Count(IsDeployed) > 1;

        public bool TrySendAway(string memberId)
        {
            var member = FindMember(memberId);
            if (!CanSendAway(member))
                return false;
            member.IsAway = true;
            member.Position = null;
            return true;
        }

        public bool TryReturnMember(string memberId, int preferredPosition)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || !member.IsAway)
                return false;
            int position = Rules.IsPosition(preferredPosition) && members.All(other => other.Position != preferredPosition)
                ? preferredPosition : Enumerable.Range(1, Rules.Positions).FirstOrDefault(slot => members.All(other => other.Position != slot));
            if (!Rules.IsPosition(position))
                return false;
            member.IsAway = false;
            member.Position = position;
            return true;
        }

        // The returned action removes unused supplies when the editor closes.
        public Action SupplyEditorBackpack(IEnumerable<EquipmentDefinition> catalog, int copies)
        {
            if (activeBattle != null)
                return null;

            var assigned = AssignedEquipmentIds();
            var available = inventory.Values.Where(item => !assigned.Contains(item.Id))
                .GroupBy(item => item.Definition.Id).ToDictionary(group => group.Key, group => group.Count());
            var supplied = new List<string>();
            foreach (var definition in catalog.Where(item => item != null).GroupBy(item => item.Id).Select(group => group.First()))
            {
                int missing = copies - available.GetValueOrDefault(definition.Id);
                for (int i = 0; i < missing; i++)
                {
                    string id = $"editor-{++nextEditorItemId}";
                    inventory.Add(id, new EquipmentInstance(id, definition));
                    supplied.Add(id);
                }
            }
            return supplied.Count == 0 ? null : ReleaseUnused;

            void ReleaseUnused()
            {
                var equipped = AssignedEquipmentIds();
                foreach (string id in supplied)
                    if (!equipped.Contains(id))
                        inventory.Remove(id);
            }
        }

        private HashSet<string> AssignedEquipmentIds() =>
            new HashSet<string>(members.SelectMany(member => member.Slots).Where(id => id != null));

        public EquipmentInstance AddEquipment(EquipmentDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            string id = definition is SkillBookDefinition ? $"book-{++nextBookId}" : $"gear-{++nextItemId}";
            var item = new EquipmentInstance(id, definition);
            inventory.Add(item.Id, item);
            RegisterReward(definition);
            return item;
        }

        public void RegisterRewardCatalog(IEnumerable<EquipmentDefinition> definitions)
        {
            foreach (var definition in definitions)
                RegisterReward(definition ?? throw new ArgumentNullException(nameof(definitions)));
        }

        private void RegisterReward(EquipmentDefinition definition)
        {
            if (definition.Tier < 0 || definition.Price <= 0)
                return;

            if (definition.IsPotion)
                potionDefinitions.TryAdd(definition.Id, definition);
            else if (definition.Effect != EquipmentEffect.RecruitmentNotice)
                accessoryDefinitions.TryAdd(definition.Id, definition);
        }

        private void RollRandomRewards(Dictionary<string, EquipmentDefinition> pool, int count, List<EquipmentDefinition> rewards)
        {
            if (count <= 0 || pool.Count == 0)
                return;
            var choices = pool.Values.ToArray();
            for (int i = 0; i < count; i++)
                rewards.Add(choices[rewardRandom.Next(choices.Length)]);
        }

        private static bool IsDeployed(Member member) => member.Health > 0 && member.Position.HasValue;

        private bool IsRooted(Member member) => EquipmentOf(member).Any(item => item.Effect == EquipmentEffect.TreeRoot);

        private IEnumerable<EquipmentDefinition> EquipmentOf(Member member) =>
            member.Slots.Where(id => id != null).Select(id => inventory[id].Definition);

        private Stats PersonalStats(Member member) =>
            EquipmentOf(member).Aggregate(member.Character.BaseStats + new Stats(member.PermanentAttack, 0, 0, 0),
                (stats, item) => stats + item.Bonus);

        private Combatant ToCombatant(Member member, Side side) => new Combatant(member.Id, member.Character, side,
            member.Position.Value, PersonalStats(member), member.Health, EquipmentOf(member), member.StartingStatuses, member.Slots,
            member.LearnedSkills.Take(MemberView.SkillSlotsFor(member.Character)));
    }
}
