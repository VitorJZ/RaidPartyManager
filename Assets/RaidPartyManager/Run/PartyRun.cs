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
        internal long AcquisitionOrder { get; }
        internal int SeedBloomFromNode { get; set; }

        internal EquipmentInstance(string id, EquipmentDefinition definition, long acquisitionOrder)
        {
            Id = id;
            Definition = definition;
            AcquisitionOrder = acquisitionOrder;
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
        public PlayerTrait Trait { get; }
        public Personality Personality { get; }
        public EmployeeAttributes EmployeeAttributes { get; }
        public CrewContract Contract { get; }
        public IReadOnlyList<CrewContract> Renewals { get; }
        public int ContractEndDay => (Renewals.LastOrDefault() ?? Contract)?.EndDay ?? 0;
        public CampTraining Training { get; }
        public int BonusEnergy => Training.Energy + EmployeeAttributes.BonusEnergy;
        public int MarketValue => PlayerMarket.Value(Character, Trait, LearnedSkills, Training.Sessions);
        public int SkillSlotCount => SkillSlotsFor(Character) + (Trait == PlayerTrait.Scholar ? 1 : 0);

        public bool IsAway { get; }

        internal static int SkillSlotsFor(CharacterDefinition character) => character.Stage >= 2 ? 2 : 1;

        internal MemberView(string id, CharacterDefinition character, int? position, int health, Stats stats, string[] slots,
            SkillBookDefinition[] learnedSkills, bool isAway, PlayerTrait trait, CrewContract contract,
            EmployeeAttributes employeeAttributes, Personality personality, CampTraining training, CrewContract[] renewals)
        {
            Trait = trait;
            EmployeeAttributes = employeeAttributes;
            Personality = personality;
            Contract = contract;
            Renewals = Array.AsReadOnly(renewals);
            Id = id;
            Character = character;
            Training = training;
            Position = position;
            Health = health;
            EffectiveStats = stats;
            EquipmentIds = Array.AsReadOnly((string[])slots.Clone());
            LearnedSkills = Array.AsReadOnly(learnedSkills.Take(trait == PlayerTrait.Scholar ? 3 : 2).ToArray());
            IsAway = isAway;
        }
    }

    public sealed partial class PartyRun
    {
        private sealed class Member
        {
            public readonly PartyMemberStart Start;
            public string Id => Start.Id;
            public bool CanRecoverHealth => Character.Trait != CharacterTrait.AbyssWard;
            public CharacterDefinition Character;
            public int? Position;
            public int Health;
            public int PermanentAttack, PermanentHealth;
            public CampTraining Training;
            public bool IsAway;
            public IReadOnlyDictionary<StatusKind, int> StartingStatuses;
            public PlayerTrait Trait;
            public EmployeeAttributes Attributes;
            public EmployeeCareer Career;
            public CrewContract Contract;
            public long UnpaidWages;
            public readonly List<CrewContract> Renewals = new();
            public CrewContract LastContract => Renewals.LastOrDefault() ?? Contract;
            public string[] Slots = new string[Rules.EquipmentSlots];
            public readonly SkillBookDefinition[] LearnedSkills = new SkillBookDefinition[3];

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
        private HashSet<string> stolenDuringBattle = new();
        private readonly HashSet<string> editorSupplies = new HashSet<string>();
        private readonly Dictionary<string, EquipmentDefinition> potionDefinitions = new Dictionary<string, EquipmentDefinition>();
        private readonly Dictionary<string, EquipmentDefinition> accessoryDefinitions = new Dictionary<string, EquipmentDefinition>();
        private BattleRequest activeBattle;
        private EquipmentDefinition activeReward;
        private int nextItemId;
        private long nextAcquisitionOrder;
        private int nextBookId;
        private int nextEditorItemId;
        private int nextBattleId;
        private readonly Random rewardRandom = new Random();
        private readonly Random employeeRandom = new Random();

        public PartyRun(IEnumerable<PartyMemberStart> startingMembers, IEnumerable<EquipmentDefinition> startingEquipment,
            bool contractsEnabled = true) : this(contractsEnabled)
        {
            members.AddRange(ValidateRoster(startingMembers));
            RefreshEquipmentSlots();
            foreach (var equipment in startingEquipment)
                AddEquipment(equipment);
            foreach (var member in members)
                RefreshEmployeeGoals(member);
        }

        private PartyRun(bool contractsEnabled) => ContractsEnabled = contractsEnabled;

        internal PartyCheckpoint CaptureCheckpoint()
        {
            var assigned = AssignedEquipmentIds();
            return new PartyCheckpoint
            {
                GravePromises = grave.Copy(), Day = Day, PayrollPausedDays = payrollPausedDays, PayrollDebt = PayrollDebt, TalentSeed = talentSeed,
                Expenses = expenses.ToArray(), Gold = Gold, NextItemId = nextItemId, NextBookId = nextBookId,
                NextEditorItemId = nextEditorItemId, NextBattleId = nextBattleId,
                Inventory = inventory.Values.Where(item => !editorSupplies.Contains(item.Id) || assigned.Contains(item.Id))
                    .OrderBy(item => item.AcquisitionOrder)
                    .Select(item => new CheckpointItem
                    {
                        Id = item.Id, Definition = item.Definition.Id,
                        SeedBloomFromNode = item.SeedBloomFromNode
                    }).ToArray(),
                Members = members.Select(member => new CheckpointMember
                {
                    Trait = member.Trait, Contract = member.Contract?.Copy(), Renewals = member.Renewals.Select(c => c.Copy()).ToArray(), EmployeeAttributes = member.Attributes, UnpaidWages = member.UnpaidWages,
                    EmployeeCareer = member.Career,
                    Id = member.Id, StartingCharacter = member.Start.Character.Id, StartingPosition = member.Start.Position,
                    StartingHealth = member.Start.Health, InitialStatuses = CaptureStatuses(member.Start.StartingStatuses),
                    Character = member.Character.Id, Position = member.Position ?? 0, Health = member.Health,
                    PermanentAttack = member.PermanentAttack, PermanentHealth = member.PermanentHealth, IsAway = member.IsAway, Statuses = CaptureStatuses(member.StartingStatuses),
                    Training = member.Training,
                    Slots = (string[])member.Slots.Clone(), Skills = member.LearnedSkills.Select(skill => skill?.Id).ToArray()
                }).ToArray()
            };
        }

        internal static PartyRun RestoreCheckpoint(PartyCheckpoint saved, IReadOnlyDictionary<string, CharacterDefinition> characters,
            IReadOnlyDictionary<string, EquipmentDefinition> items, int legacySeedBloomFromNode, bool contractsEnabled = true)
        {
            // Squads saved under earlier limits keep their signed employees.
            const int legacyRosterLimit = 10;
            if (saved?.Members == null || saved.Inventory == null || saved.Day < 0 || saved.PayrollDebt < 0 ||
                saved.PayrollPausedDays < 0 || saved.PayrollPausedDays > Math.Max(0, saved.Day - 1) ||
                saved.Members.Length is < 1 or > legacyRosterLimit)
                throw new ArgumentException("Invalid saved party.");

            var party = new PartyRun(contractsEnabled)
            {
                grave = saved.GravePromises?.Copy() ?? new GravePromises(), Day = saved.Day, payrollPausedDays = saved.PayrollPausedDays,
                PayrollDebt = saved.PayrollDebt, talentSeed = saved.TalentSeed,
                Gold = saved.Gold, nextItemId = saved.NextItemId, nextBookId = saved.NextBookId,
                nextEditorItemId = saved.NextEditorItemId, nextBattleId = saved.NextBattleId
            };
            if (saved.Expenses != null) party.expenses.AddRange(saved.Expenses.Where(e => e.Amount > 0 && e.Day >= Math.Max(1, saved.Day - 2) && e.Day <= Math.Max(1, saved.Day)));
            foreach (var item in saved.Inventory)
            {
                var definition = items[item.Definition];
                int bloomFrom = item.SeedBloomFromNode;
                if (bloomFrom == 0 && definition.Effect == EquipmentEffect.StrangeSeed && item.BattlesToBloom >= 3)
                    bloomFrom = legacySeedBloomFromNode;
                party.inventory.Add(item.Id, new EquipmentInstance(item.Id, definition, ++party.nextAcquisitionOrder)
                {
                    SeedBloomFromNode = bloomFrom
                });
                party.RegisterReward(definition);
            }
            foreach (var entry in saved.Members)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Slots == null || !(Rules.ValidEquipmentSlots(entry.Slots.Length, entry.Trait, entry.EmployeeAttributes.ExtraEquipmentSlot) ||
                    entry.EmployeeAttributes.ExtraEquipmentSlot && entry.Slots.Length == Rules.PersonalEquipmentSlots(entry.Trait)) ||
                    (int)entry.Trait < 0 || (int)entry.Trait > PlayerMarket.TraitCount ||
                    saved.Day > 0 && (entry.Contract == null || !entry.Contract.ValidAt(party.ContractDay)) ||
                    entry.Skills?.Length is not (2 or 3) || !Rules.IsPosition(entry.StartingPosition) ||
                    entry.Position != 0 && !Rules.IsPosition(entry.Position) || entry.Health < 0 || entry.PermanentHealth < 0 ||
                    entry.UnpaidWages < 0 || !entry.Training.IsValid)
                    throw new ArgumentException("Invalid saved party member.");
                var member = new Member(new PartyMemberStart(entry.Id, characters[entry.StartingCharacter], entry.StartingPosition,
                    entry.StartingHealth, RestoreStatuses(entry.InitialStatuses)))
                {
                    Trait = entry.Trait, Contract = saved.Day == 0 ? null : entry.Contract.Copy(), Slots = new string[entry.Slots.Length],
                    UnpaidWages = entry.UnpaidWages,
                    Character = characters[entry.Character].WithPersonality(characters[entry.StartingCharacter].Personality),
                    Position = entry.Position == 0 ? (int?)null : entry.Position,
                    Health = entry.Health, PermanentAttack = entry.PermanentAttack, PermanentHealth = entry.PermanentHealth, IsAway = entry.IsAway,
                    Training = entry.Training,
                    StartingStatuses = RestoreStatuses(entry.Statuses),
                    Attributes = entry.EmployeeAttributes.IsValid ? entry.EmployeeAttributes :
                        EmployeeAttributes.Starting,
                    Career = entry.EmployeeCareer.Initialized ? entry.EmployeeCareer :
                        new EmployeeCareer { ServiceDays = Math.Max(0, saved.Day - (entry.Contract?.StartDay ?? saved.Day)) }
                };
                if (contractsEnabled) member.Contract?.OpenInterestPeriod(party.ContractDay);
                foreach (var next in entry.Renewals ?? Array.Empty<CrewContract>())
                {
                    if (member.LastContract == null || next == null || next.StartDay != member.LastContract.EndDay || !next.ValidAt(party.ContractDay))
                        throw new ArgumentException("Invalid renewal schedule.");
                    member.Renewals.Add(next.Copy());
                }
                for (int i = 0; i < member.Slots.Length; i++)
                    if (!string.IsNullOrEmpty(entry.Slots[i]))
                        member.Slots[i] = party.inventory[entry.Slots[i]].Id;
                for (int i = 0; i < entry.Skills.Length; i++)
                {
                    if (string.IsNullOrEmpty(entry.Skills[i]))
                        continue;
                    if (!(items[entry.Skills[i]] is SkillBookDefinition skill))
                        throw new ArgumentException("Invalid saved skill.");
                    member.LearnedSkills[i] = skill;
                }
                party.members.Add(member);
            }
            party.RefreshEquipmentSlots();
            foreach (var member in party.members)
                party.RefreshEmployeeGoals(member);
            return party;
        }

        private static CheckpointStatus[] CaptureStatuses(IReadOnlyDictionary<StatusKind, int> statuses) =>
            statuses?.Select(entry => new CheckpointStatus { Kind = entry.Key, Layers = entry.Value }).ToArray();

        private static IReadOnlyDictionary<StatusKind, int> RestoreStatuses(CheckpointStatus[] statuses) =>
            statuses == null || statuses.Length == 0 ? null : statuses.ToDictionary(status => status.Kind, status => status.Layers);

        private List<Member> ValidateRoster(IEnumerable<PartyMemberStart> startingMembers)
        {
            var roster = new List<Member>();
            foreach (var entry in startingMembers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Character == null || !Rules.Fits(entry.Position, entry.Character.PositionSlots) ||
                    entry.Health < 0 || entry.Health > entry.Character.BaseStats.Health ||
                    roster.Any(member => member.Id == entry.Id ||
                        Rules.Overlaps(entry.Position, entry.Character.PositionSlots, member.Start.Position, member.Character.PositionSlots)))
                    throw new ArgumentException("Invalid or duplicate starting member.", nameof(startingMembers));

                roster.Add(new Member(entry) { Attributes = EmployeeAttributes.Starting });
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
            foreach (var member in members) PrepareHire(member);
            RefreshEquipmentSlots();
        }

        public void ResetEncounter()
        {
            CancelBattle();
            var occupied = new HashSet<int>();
            foreach (var member in members)
            {
                if (member.IsAway)
                    continue;
                member.Health = Math.Min(member.Start.Health + member.Training.Bonus.Health + member.Attributes.Bonus.Health +
                    (member.Trait == PlayerTrait.Sturdy ? 2 : 0), PersonalStats(member).Health);
                member.StartingStatuses = member.Start.StartingStatuses;
                if (!IsRooted(member) || member.Health == 0 || !member.Position.HasValue)
                    member.Position = null;
                else
                    occupied.UnionWith(Enumerable.Range(member.Position.Value, member.Character.PositionSlots));
            }
            foreach (var member in members.Where(member => !member.IsAway && member.Health > 0 && !member.Position.HasValue))
            {
                if (CanFit(member, member.Start.Position))
                {
                    member.Position = member.Start.Position;
                    occupied.UnionWith(Enumerable.Range(member.Start.Position, member.Character.PositionSlots));
                }
            }

            // A recruited member's first position may also be an original member's starting slot.
            foreach (var member in members.Where(member => !member.IsAway && member.Health > 0 && !member.Position.HasValue))
            {
                int open = Enumerable.Range(1, Rules.Positions).FirstOrDefault(slot => CanFit(member, slot));
                if (open > 0)
                {
                    member.Position = open;
                    occupied.UnionWith(Enumerable.Range(open, member.Character.PositionSlots));
                }
            }

            RefreshEquipmentSlots();

            bool CanFit(Member member, int position) => Rules.Fits(position, member.Character.PositionSlots) &&
                !Enumerable.Range(position, member.Character.PositionSlots).Any(occupied.Contains);
        }

        public IReadOnlyList<MemberView> Members
        {
            get
            {
                var formationStats = EquipmentFormation.Resolve(CaptureFormation(Side.Party));
                return members.Select(member => new MemberView(member.Id, member.Character, member.Position, member.Health,
                    PlayerMarket.GearAndPosition(member.Trait, member.Slots.Count(s => s != null), member.Position,
                        formationStats.TryGetValue(member.Id, out var stats) ? stats : PersonalStats(member)), member.Slots,
                    member.LearnedSkills, member.IsAway, member.Trait, member.Contract, member.Attributes, member.Start.Character.Personality,
                    member.Training, member.Renewals.ToArray())).ToArray();
            }
        }

        public IReadOnlyList<EquipmentInstance> Inventory => inventory.Values.Where(item =>
            item.Definition is not SkillBookDefinition && !stolenDuringBattle.Contains(item.Id)).ToArray();
        public IReadOnlyList<EquipmentInstance> SkillBooks => inventory.Values.Where(item => item.Definition is SkillBookDefinition).ToArray();
        public IReadOnlyList<EquipmentInstance> BackpackItems
        {
            get
            {
                var assigned = AssignedEquipmentIds();
                return inventory.Values.Where(item => !assigned.Contains(item.Id) && !stolenDuringBattle.Contains(item.Id))
                    .OrderBy(item => item.AcquisitionOrder).ToArray();
            }
        }
        public IEnumerable<EquipmentInstance> SaleableItems => BackpackItems.Where(item => !editorSupplies.Contains(item.Id));
        public int Gold { get; private set; }

        public IReadOnlyList<Combatant> CaptureFormation(Side side) =>
            members.Where(IsDeployed).Select(member => ToCombatant(member, side)).ToArray();

        public bool HasRecruitmentVacancy => members.Count < RosterLimit && members.Count(IsDeployed) + members.Count(member => member.IsAway) < Rules.Positions &&
            members.Where(IsDeployed).Any(member => EquipmentOf(member).Any(item => item.Effect == EquipmentEffect.RecruitmentNotice));

        public bool CanStartBattle => activeBattle == null && members.Any(IsDeployed) &&
            (Day > 0 || members.Count > Rules.Positions || members.All(member => member.IsAway || member.Health == 0 || member.Position.HasValue) || HasRecruitmentVacancy);

        // Moving onto an occupied position swaps its occupant into the mover's old position.
        public bool TryPlace(string memberId, int? position)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.IsAway || member.Health == 0 ||
                (position.HasValue && !Rules.Fits(position.Value, member.Character.PositionSlots)) ||
                (member.Position.HasValue && member.Position != position && IsRooted(member)))
                return false;

            if (position.HasValue)
            {
                var occupants = members.Where(other => other != member &&
                    Overlaps(other, position.Value, member.Character.PositionSlots)).ToArray();
                if (occupants.Length > 1)
                    return false;
                if (occupants.Length == 1)
                {
                    var occupant = occupants[0];
                    if (IsRooted(occupant) || member.Position is int former &&
                        (!Rules.Fits(former, occupant.Character.PositionSlots) ||
                         Rules.Overlaps(former, occupant.Character.PositionSlots, position.Value, member.Character.PositionSlots) ||
                         members.Any(other => other != member && other != occupant && Overlaps(other, former, occupant.Character.PositionSlots))))
                        return false;
                    occupant.Position = member.Position;
                }
            }

            member.Position = position;
            RefreshEquipmentSlots();
            return true;
        }

        private static bool Overlaps(Member member, int position, int width) => member.Position is int occupied &&
            Rules.Overlaps(position, width, occupied, member.Character.PositionSlots);

        // Assigning an existing instance moves it out of any prior slot. Replaced gear stays in the bag.
        public bool TryEquip(string memberId, int slot, string equipmentId)
        {
            var receiver = FindMember(memberId);
            if (activeBattle != null || receiver == null || receiver.IsAway || slot < 0 || slot >= receiver.Slots.Length ||
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
            FinishEquipmentChange(receiver);
            return true;
        }

        public bool CanSwapEquipment(string firstId, string secondId) =>
            TryGetEquipmentSwap(firstId, secondId, out _, out _);

        public bool TrySwapEquipment(string firstId, string secondId)
        {
            if (!TryGetEquipmentSwap(firstId, secondId, out var first, out var second))
                return false;

            first.Wearer.Slots[first.Slot] = secondId;
            second.Wearer.Slots[second.Slot] = firstId;
            FinishEquipmentChange(first.Wearer, second.Wearer);
            return true;
        }

        private bool TryGetEquipmentSwap(string firstId, string secondId,
            out (Member Wearer, int Slot) first, out (Member Wearer, int Slot) second)
        {
            first = second = default;
            if (activeBattle != null || firstId == secondId || !IsMovable(firstId) || !IsMovable(secondId))
                return false;

            first = EquippedSlot(firstId);
            second = EquippedSlot(secondId);
            return first.Wearer is { IsAway: false } && second.Wearer is { IsAway: false };

            bool IsMovable(string id) => id != null && inventory.TryGetValue(id, out var item) &&
                item.Definition.Effect != EquipmentEffect.TreeRoot;
        }

        private (Member Wearer, int Slot) EquippedSlot(string id)
        {
            foreach (var member in members)
            {
                int slot = Array.IndexOf(member.Slots, id);
                if (slot >= 0) return (member, slot);
            }
            return default;
        }

        private void FinishEquipmentChange(Member receiver, Member other = null)
        {
            foreach (var member in members)
                member.Health = Math.Min(member.Health, PersonalStats(member).Health);
            RefreshEmployeeGoals(receiver);
            if (other != null && other != receiver) RefreshEmployeeGoals(other);
        }

        public bool CanLearnSkill(string memberId, int slot, string bookId)
        {
            var member = FindMember(memberId);
            return activeBattle == null && member != null && !member.IsAway && member.Health > 0 &&
                slot >= 0 && slot < MemberView.SkillSlotsFor(member.Character) + (member.Trait == PlayerTrait.Scholar ? 1 : 0) &&
                (member.Trait != PlayerTrait.Tuition || Gold >= 2) &&
                bookId != null && inventory.TryGetValue(bookId, out var item) && item.Definition is SkillBookDefinition book &&
                book.CanLearn(member.Character) && !member.LearnedSkills.Any(known => known?.Id == book.Id);
        }

        public bool TryLearnSkill(string memberId, int slot, string bookId)
        {
            if (!CanLearnSkill(memberId, slot, bookId))
                return false;

            var member = FindMember(memberId);
            if (member.Trait == PlayerTrait.Tuition) TrySpendGold(2, member.Character.Name + " · 学习技能");
            member.LearnedSkills[slot] = (SkillBookDefinition)inventory[bookId].Definition;
            inventory.Remove(bookId);
            RefreshEmployeeGoals(member);
            return true;
        }

        // Future item/event systems validate and pay for evolution before changing the member's form.
        public bool TryChangeForm(string memberId, CharacterDefinition newForm)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || member.IsAway || newForm == null ||
                member.Position is int position && (!Rules.Fits(position, newForm.PositionSlots) ||
                    members.Any(other => other != member && Overlaps(other, position, newForm.PositionSlots))))
                return false;

            bool promoted = member.Health > 0 && newForm.Stage > member.Character.Stage;
            if (member.Character.Id != newForm.Id) Remember(member, EmployeeThought.Promotion, newForm.Name);
            member.Character = newForm.WithPersonality(member.Start.Character.Personality);
            RefreshEquipmentSlots();
            int maximumHealth = PersonalStats(member).Health;
            member.Health = promoted ? maximumHealth : Math.Min(member.Health, maximumHealth);
            RefreshEmployeeGoals(member);
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

            int lastStart = Rules.Positions - replacement.PositionSlots + 1;
            if (position.HasValue)
                position = Math.Min(lastStart, position.Value);
            var displaced = position.HasValue ? members.Where(other => other != member &&
                Overlaps(other, position.Value, replacement.PositionSlots)).OrderBy(other => other.Position).ToArray()
                : Array.Empty<Member>();
            long exitCost = TransferExitCostFor(member.Id);
            if (displaced.Any(IsRooted) || exitCost > Gold) return false;
            Gold -= (int)exitCost;
            if (ContractsEnabled) RecordExitExpenses(member);

            // Equipment instances remain in inventory. Replacing the member releases their assigned slots.
            var incoming = new Member(new PartyMemberStart(
                member.Id, replacement, position ?? Math.Min(lastStart, member.Start.Position), replacement.BaseStats.Health))
            {
                Position = position
            };
            PrepareHire(incoming);
            members[members.IndexOf(member)] = incoming;
            foreach (var other in displaced)
                other.Position = null;
            foreach (var other in displaced)
            {
                int open = Enumerable.Range(1, Rules.Positions).FirstOrDefault(candidate =>
                    Rules.Fits(candidate, other.Character.PositionSlots) &&
                    members.All(placed => !Overlaps(placed, candidate, other.Character.PositionSlots)));
                other.Position = open == 0 ? (int?)null : open;
            }
            RefreshEquipmentSlots();
            return true;
        }

        public BattleRequest BeginBattle(IEnumerable<Combatant> enemies, int tickLimit, EquipmentDefinition victoryReward,
            IEnumerable<CharacterDefinition> recruitmentPool = null, IEnumerable<CharacterDefinition> characterCatalog = null, bool hardFight = false, bool capitalField = false)
        {
            if (!CanStartBattle)
                throw new InvalidOperationException("Place all living members before starting a new battle.");

            var party = CaptureFormation(Side.Party);
            var enemyUnits = enemies.ToArray();
            if (enemyUnits.Any(unit => unit.Side != Side.Enemy))
                throw new ArgumentException("Encounter units must be enemies.", nameof(enemies));
            // An absent companion retains a party slot, including against recruitment-notice summons.
            if (members.Count >= RosterLimit || members.Any(member => member.IsAway) && members.Count(member => member.Health > 0) >= Rules.Positions)
                recruitmentPool = null;
            int reserveArchers = members.Count(member => member.Health > 0 && !IsDeployed(member) &&
                member.Character.Profession == Profession.Archer);
            int reserveAccessoryHunters = members.Count(member => member.Health > 0 && !IsDeployed(member) &&
                member.Character.Trait == CharacterTrait.AccessoryOnSurvival);
            var request = new BattleRequest($"battle-{nextBattleId + 1}", party.Concat(enemyUnits), tickLimit,
                recruitmentPool, characterCatalog, Gold, Math.Max(0, RosterLimit - members.Count), hardFight, grave.Porter,
                grave.Bell, capitalField, reserveArcherCount: reserveArchers, reserveAccessoryHunterCount: reserveAccessoryHunters,
                partyRoster: members.Select(member => member.Character));
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
            bool summonedSingerPossible = activeBattle.RecruitmentPool.Any(c => c.Trait == CharacterTrait.ChorusArmory) &&
                loadout.Slots.Keys.Any(id => deployed.All(unit => unit.Id != id));
            bool deployedSinger = deployed.Any(unit => unit.Character.Trait == CharacterTrait.ChorusArmory);
            bool SlotCountMatches(string id, int count)
            {
                var unit = deployed.FirstOrDefault(originalUnit => originalUnit.Id == id);
                int expected = unit?.EquipmentSlots.Count ?? Rules.EquipmentSlots;
                return count == expected || count == Rules.PersonalEquipmentSlots(unit?.Talent ?? PlayerTrait.None, unit?.ExtraEquipmentSlot ?? false) + 1 &&
                    (summonedSingerPossible || unit == null && deployedSinger);
            }
            if (loadout.Slots.Count > Rules.Positions || loadout.Slots.Any(pair => !SlotCountMatches(pair.Key, pair.Value.Count)) ||
                loadout.Disabled.Any(id => !equipped.Contains(id)) ||
                deployed.Any(unit => !loadout.Slots.ContainsKey(unit.Id)) ||
                equipped.Any(id => !inventory.ContainsKey(id)) ||
                loadout.Consumed.Any(id => !original.TryGetValue(id, out var definition) ||
                    !(definition.IsPotion || definition.Effect is EquipmentEffect.ElvenCharm or EquipmentEffect.RecruitmentNotice)))
                return false;
            var instances = equipped.Concat(loadout.Consumed).Concat(loadout.Stolen).ToArray();
            if (instances.Length != original.Count || instances.Distinct().Count() != original.Count ||
                instances.Any(id => !original.ContainsKey(id)))
                return false;

            foreach (var unit in deployed)
                FindMember(unit.Id).Slots = loadout.Slots[unit.Id].ToArray();
            foreach (var id in loadout.Consumed)
                inventory.Remove(id);
            stolenDuringBattle = new HashSet<string>(loadout.Stolen);
            return true;
        }

        public bool TrySettle(BattleResult result, out EquipmentInstance reward)
        {
            reward = null;
            var guaranteed = result?.Outcome == BattleOutcome.Victory ? activeReward : null;
            if (!TryFinishBattle(result, out var loot))
                return false;
            AddGold(result.GoldEarned);
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
                if (!result.HealthById.TryGetValue(unit.Id, out int health) || health < 0 || health > (long)unit.Stats.Health + (recruits.Contains(unit) ? 0 : result.PermanentHealthGains.GetValueOrDefault(unit.Id)))
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
                result.AccessoriesEarned < 0 || result.AccessoriesEarned > Rules.Positions + activeBattle.ReserveAccessoryHunterCount ||
                result.PermanentAttackGains.Concat(result.PermanentHealthGains).Any(gain =>
                    gain.Value < 0 || !allUnits.Any(unit => unit.Side == Side.Party && unit.Id == gain.Key)))
                return false;

            if (result.Loadout != null && (result.Loadout.Slots.Keys.Any(id => !allUnits.Any(unit => unit.Id == id && unit.Side == Side.Party)) ||
                !TryApplyBattleLoadout(result.Id, result.Loadout)))
                return false;

            var deployedIds = new HashSet<string>(activeBattle.Combatants.Where(unit => unit.Side == Side.Party)
                .Select(unit => unit.Id));
            SettlePerformance(result, deployedIds);
            foreach (var member in members.Where(member => deployedIds.Contains(member.Id)))
            {
                member.PermanentHealth += result.PermanentHealthGains.GetValueOrDefault(member.Id);
                member.Health = Math.Max(1, Math.Min(result.HealthById[member.Id], PersonalStats(member).Health));
                if (result.Outcome == BattleOutcome.Victory && member.CanRecoverHealth && member.Trait == PlayerTrait.Bandages && result.HealthById[member.Id] > 0)
                    member.Health = Math.Min(PersonalStats(member).Health, member.Health + 2);
                member.PermanentAttack += result.PermanentAttackGains.GetValueOrDefault(member.Id);
                member.StartingStatuses = null;
                if (positions.TryGetValue(member.Id, out int position))
                    member.Position = position;
            }

            foreach (var recruit in recruits.Where(unit => unit.Health > 0))
            {
                if (members.Count >= RosterLimit) break;
                string id = recruit.Id;
                while (FindMember(id) != null)
                    id += "-ally";
                var start = new PartyMemberStart(id, recruit.Character, positions[recruit.Id], recruit.Character.BaseStats.Health);
                var member = new Member(start)
                {
                    Health = recruit.Health, PermanentHealth = result.PermanentHealthGains.GetValueOrDefault(recruit.Id),
                    PermanentAttack = result.PermanentAttackGains.GetValueOrDefault(recruit.Id)
                };
                PrepareHire(member);
                if (result.Loadout != null && result.Loadout.Slots.TryGetValue(recruit.Id, out var slots))
                    member.Slots = slots.ToArray();
                members.Add(member);
            }

            deployedIds.UnionWith(recruits.Where(unit => unit.Health > 0).Select(unit => unit.Id));
            SettleEmployeeBattle(result, deployedIds);
            SettleGravePromises(result, deployedIds);

            var rewards = new List<EquipmentDefinition>();
            if (result.Outcome == BattleOutcome.Victory && activeReward != null)
            {
                rewards.Add(activeReward);
                RegisterReward(activeReward);
            }
            RollRandomRewards(potionDefinitions, result.PotionsEarned, rewards);
            RollRandomRewards(accessoryDefinitions, result.AccessoriesEarned, rewards);
            loot = rewards.AsReadOnly();
            foreach (var id in stolenDuringBattle)
                inventory.Remove(id);
            stolenDuringBattle.Clear();
            activeBattle = null;
            activeReward = null;
            RefreshEquipmentSlots();
            return true;
        }

        private void RefreshEquipmentSlots()
        {
            bool chorusActive = members.Any(m => IsDeployed(m) && m.Character.Trait == CharacterTrait.ChorusArmory);
            foreach (var member in members)
            {
                int count = Rules.PersonalEquipmentSlots(member.Trait, member.Attributes.ExtraEquipmentSlot) + (chorusActive ? 1 : 0);
                if (count == member.Slots.Length) continue;
                // Items live in inventory; releasing a surplus slot puts its item back in the backpack.
                bool losingSlot = count < member.Slots.Length;
                Array.Resize(ref member.Slots, count);
                if (losingSlot) member.Health = Math.Min(member.Health, PersonalStats(member).Health);
            }
        }

        private Member FindMember(string id) => members.Find(member => member.Id == id);

        public int ResalePercent => 50 + 20 * members.Count(m => m.Trait == PlayerTrait.Bargaining);
        public bool HasShopDiscount => members.Any(m => m.Character.Trait == CharacterTrait.ShopDiscount);
        public int ResalePrice(EquipmentDefinition item) => AdventureShopOffer.ResalePrice(item, ResalePercent);

        public bool TrySellItem(string itemId) => TrySellItems(new[] { itemId }, out _);

        public bool TrySellItems(IReadOnlyCollection<string> ids, out int proceeds)
        {
            if (!TryTakeItemsForSale(ids, int.MaxValue - Gold, out proceeds)) return false;
            AddGold(proceeds);
            return true;
        }

        // The standalone shop uses its own wallet. Validate the entire basket before moving any item.
        public bool TryTakeItemForSale(string itemId, int walletCapacity, out int price) =>
            TryTakeItemsForSale(new[] { itemId }, walletCapacity, out price);

        public bool TryTakeItemsForSale(IReadOnlyCollection<string> ids, int walletCapacity, out int price)
        {
            price = 0;
            if (activeBattle != null || ids == null || ids.Count == 0) return false;
            var assigned = AssignedEquipmentIds();
            var basket = new HashSet<string>();
            long total = 0;
            foreach (var id in ids)
            {
                if (id == null || !basket.Add(id) || !inventory.TryGetValue(id, out var item) ||
                    assigned.Contains(id) || stolenDuringBattle.Contains(id) || editorSupplies.Contains(id)) return false;
                total += ResalePrice(item.Definition);
            }
            if (total > walletCapacity) return false;
            foreach (var id in basket) inventory.Remove(id);
            price = (int)total;
            return true;
        }

        public void AddGold(int amount)
        {
            if (amount < 0 || Gold > int.MaxValue - amount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            int repaid = ContractsEnabled ? (int)Math.Min(amount, PayrollDebt) : 0;
            PayrollDebt -= repaid;
            long remaining = repaid;
            foreach (var member in members)
            {
                if (remaining == 0) break;
                remaining -= RepayArrears(member, remaining);
            }
            RecordExpense("补付拖欠薪资", repaid);
            Gold += amount - repaid;
        }

        private readonly List<GoldExpense> expenses = new();
        public IReadOnlyList<GoldExpense> Expenses => ContractsEnabled ? expenses.AsReadOnly() :
            expenses.Where(e => !IsPayrollExpense(e.Reason)).ToArray();
        private void RecordExpense(string reason, long amount)
        {
            if (amount > 0) expenses.Add(new GoldExpense { Day = Math.Max(1, Day), Reason = reason, Amount = amount });
        }

        public bool TrySpendGold(int amount) => TrySpendGold(amount, "其他支出");

        public bool TrySpendGold(int amount, string reason)
        {
            if (activeBattle != null || amount < 0 || Gold < amount)
                return false;
            Gold -= amount;
            RecordExpense(reason, amount);
            return true;
        }

        public void CancelBattle()
        {
            if (stolenDuringBattle.Count > 0 && activeBattle != null)
            {
                foreach (var unit in activeBattle.Combatants.Where(unit => unit.Side == Side.Party))
                {
                    var member = FindMember(unit.Id);
                    for (int slot = 0; slot < unit.EquipmentSlots.Count; slot++)
                    {
                        string item = unit.EquipmentSlots[slot];
                        if (member != null && member.Slots[slot] == null && item != null && stolenDuringBattle.Contains(item))
                            member.Slots[slot] = item;
                    }
                }
            }
            stolenDuringBattle.Clear();
            activeBattle = null;
            activeReward = null;
            RefreshEquipmentSlots();
        }

        internal void RecoverAfterBoss()
        {
            foreach (var member in members.Where(member => member.CanRecoverHealth))
            {
                int maximum = PersonalStats(member).Health;
                member.Health = (int)Math.Min(maximum, (long)member.Health + ((long)maximum + 1) / 2);
            }
        }

        public int Rest(double fraction)
        {
            if (activeBattle != null || fraction <= 0 || fraction > 1)
                return 0;
            int healed = 0;
            foreach (var member in members.Where(member => member.Health > 0 && !member.IsAway && member.CanRecoverHealth))
            {
                int before = member.Health;
                int maximum = PersonalStats(member).Health;
                member.Health = Math.Min(maximum, member.Health + (int)Math.Ceiling(maximum * fraction) + (member.Trait == PlayerTrait.Therapy ? 2 : 0));
                if (member.Health > before) Remember(member, EmployeeThought.CampRest);
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

        internal (string Wearer, bool WaitingForRoom) FinishSeedBattle(BattleResult result, CharacterDefinition gardener, int battleNode)
        {
            if (activeBattle != null || gardener == null ||
                members.Any(member => AdventureRun.IsGardenerForm(member.Character.Id)))
                return default;

            foreach (var member in members.Where(member => result.HealthById.ContainsKey(member.Id)).ToArray())
            {
                for (int slot = 0; slot < member.Slots.Length; slot++)
                {
                    var itemId = member.Slots[slot];
                    if (itemId == null || !inventory.TryGetValue(itemId, out var seed) ||
                        seed.Definition.Effect != EquipmentEffect.StrangeSeed ||
                        seed.SeedBloomFromNode <= 0 || battleNode < seed.SeedBloomFromNode)
                        continue;
                    // The new member can change the slot layout, so clear by instance ID after recruitment.
                    if (!TryRecruit(gardener)) return (null, true);
                    int currentSlot = Array.IndexOf(member.Slots, itemId);
                    if (currentSlot >= 0) member.Slots[currentSlot] = null;
                    inventory.Remove(itemId);
                    RefreshEquipmentSlots();
                    return (member.Id, false);
                }
            }
            return default;
        }

        public bool TryRecruit(CharacterDefinition character) => TryRecruit(character, default);

        internal bool TryRecruit(CharacterDefinition character, CampTraining training)
        {
            if (activeBattle != null || character == null || members.Any(member => member.Character.Id == character.Id))
                return false;
            if (members.Count >= RosterLimit) return false;
            int position = Enumerable.Range(1, Rules.Positions).FirstOrDefault(slot => !members.Any(m => Overlaps(m, slot, character.PositionSlots)) && Rules.Fits(slot, character.PositionSlots));
            string id = "recruit-" + character.Id;
            while (FindMember(id) != null)
                id += "-ally";
            var recruit = new Member(new PartyMemberStart(id, character, Math.Max(1, position), character.BaseStats.Health))
            {
                Health = character.BaseStats.Health + training.Bonus.Health,
                Training = training
            };
            if (position == 0) recruit.Position = null;
            PrepareHire(recruit);
            members.Add(recruit);
            RefreshEquipmentSlots();
            return true;
        }

        internal bool TrySignTidePact(CharacterDefinition singer, string equipmentId, string bookId)
        {
            var gear = SaleableItems.FirstOrDefault(item => item.Id == equipmentId)?.Definition;
            if (ContractDay < 1 || ContractDay > int.MaxValue - 99 || gear == null || gear is SkillBookDefinition ||
                gear.IsPotion || gear.Effect == EquipmentEffect.RecruitmentNotice ||
                !SaleableItems.Any(item => item.Id == bookId && item.Definition is SkillBookDefinition) ||
                !TryRecruit(singer)) return false;

            inventory.Remove(equipmentId);
            inventory.Remove(bookId);
            SignNewHire(singer.Id, new CrewContract { StartDay = ContractDay, EndDay = ContractDay + 99, TidePact = true });
            return true;
        }

        internal bool TryTrain(string id, Random inspectionRandom, out CampTraining gains, out int remaining)
        {
            gains = default;
            remaining = 0;
            var member = FindMember(id);
            if (activeBattle != null || member == null || member.IsAway || member.Health <= 0 ||
                member.Training.Sessions >= CampTraining.Limit)
                return false;

            // A visit's roll survives save and reload without moving the route's RNG forward.
            var roll = inspectionRandom ?? new Random(unchecked(talentSeed ^ (Day * 7919 + 47281)));
            gains = CampTraining.Roll(roll);
            member.Training = member.Training.With(gains);
            member.Health += gains.Bonus.Health;
            remaining = CampTraining.Limit - member.Training.Sessions;
            RefreshEmployeeGoals(member);
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
            RefreshEquipmentSlots();
            return true;
        }

        public bool TryReturnMember(string memberId, int preferredPosition)
        {
            var member = FindMember(memberId);
            if (activeBattle != null || member == null || !member.IsAway)
                return false;
            int position = Rules.IsPosition(preferredPosition) && members.All(other => other.Position != preferredPosition)
                ? preferredPosition : Enumerable.Range(1, Rules.Positions).FirstOrDefault(slot => members.All(other => other.Position != slot));
            member.IsAway = false;
            member.Position = position == 0 ? (int?)null : position;
            RefreshEquipmentSlots();
            return true;
        }

        // The returned action removes unused supplies when the editor closes.
        public Action SupplyEditorBackpack(IEnumerable<EquipmentDefinition> catalog, int copies)
        {
            if (activeBattle != null)
                return null;

            var available = inventory.Values.GroupBy(item => item.Definition.Id)
                .ToDictionary(group => group.Key, group => group.Count());
            var supplied = new List<string>();
            foreach (var definition in catalog.Where(item => item != null && !item.BossExclusive).GroupBy(item => item.Id).Select(group => group.First()))
            {
                int missing = copies - available.GetValueOrDefault(definition.Id);
                for (int i = 0; i < missing; i++)
                {
                    string id = $"editor-{++nextEditorItemId}";
                    inventory.Add(id, new EquipmentInstance(id, definition, ++nextAcquisitionOrder));
                    editorSupplies.Add(id);
                    supplied.Add(id);
                }
            }
            return supplied.Count == 0 ? null : ReleaseUnused;

            void ReleaseUnused()
            {
                var equipped = AssignedEquipmentIds();
                foreach (string id in supplied)
                {
                    editorSupplies.Remove(id);
                    if (!equipped.Contains(id))
                        inventory.Remove(id);
                }
            }
        }

        private HashSet<string> AssignedEquipmentIds() =>
            new HashSet<string>(members.SelectMany(member => member.Slots).Where(id => id != null));

        public EquipmentInstance AddEquipment(EquipmentDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            string id = definition is SkillBookDefinition ? $"book-{++nextBookId}" : $"gear-{++nextItemId}";
            var item = new EquipmentInstance(id, definition, ++nextAcquisitionOrder);
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
            if (!definition.AvailableInRegularPool || definition is SkillBookDefinition || definition.Tier < 0 || definition.Price <= 0)
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
            EquipmentOf(member).Aggregate(member.Character.BaseStats + member.Training.Bonus + member.Attributes.Bonus + TraitBonus(member) +
                new Stats(member.PermanentAttack, 0, 0, member.PermanentHealth),
                (stats, item) => stats + item.Bonus);

        private static IReadOnlyDictionary<StatusKind, int> OpeningStatuses(Member member)
        {
            int previous = member.Career.LastStartingPosition;
            if (previous == 0 || member.Position == previous || member.Trait is not (PlayerTrait.AgileSwap or PlayerTrait.Disobedient))
                return member.StartingStatuses;
            var status = member.Trait == PlayerTrait.AgileSwap ? StatusKind.Swift : StatusKind.Weakened;
            var opening = member.StartingStatuses?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<StatusKind, int>();
            opening[status] = opening.GetValueOrDefault(status) + 1;
            return opening;
        }

        private Combatant ToCombatant(Member member, Side side) => new Combatant(member.Id, member.Character, side,
            member.Position.Value, PersonalStats(member), member.Health, EquipmentOf(member), OpeningStatuses(member), member.Slots,
            member.LearnedSkills, member.Trait, member.Training.Energy + member.Attributes.BonusEnergy, member.Attributes.ExtraEquipmentSlot);
    }
}
