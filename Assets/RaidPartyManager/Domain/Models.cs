using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    public enum Profession { Warrior, Mage, Archer, Support }
    public enum AttackReach { Unspecified, Melee, Ranged }
    public enum CharacterTrait
    {
        None, DamageLimit, GainEmberWhenHit, RepeatTargetDamage, ArcherVolley, PoisonOnHit,
        MissingHealthDamage, EscalatingAttack, PermanentAttackOnKill, IgnoreMagicDefense, LightningChant,
        Rebirth, GeneratePotion, PoisonOpponents, EvolveAlly, CopyAdvancedTrait, BlessAlly, RevealEnemies,
        PiercingArrow, BorrowAllyAttack
    }
    public enum Personality { Aggressive, Cautious, Reckless, Gentle, Cooperative, Calm }
    public enum Side { Party, Enemy }
    public enum ActionKind { Attack, Heal, Shield }
    public enum DamageType { Physical, Magic, Fire, Lightning, True, Default, Fixed }
    public enum TargetRule { AnyEnemy, FrontEnemy, RearEnemy, WoundedAlly, Self }
    public enum BattleOutcome { Victory, PartyDefeated, MutualDefeat, Timeout }
    public enum DecisionReason { Default, HighestDamage, SelfProtection, FavorAttack, WoundedAlly, FollowAlly, LowestHealth }
    public enum BattleEventKind { Action, Defeated, Finished, Effect, FormChanged }

    public static class Rules
    {
        public const int Positions = 4;
        public const int EquipmentSlots = 3;

        public static bool IsPosition(int position) => position >= 1 && position <= Positions;
    }

    public readonly struct Stats
    {
        public int Attack { get; }
        public int Defense { get; }
        public int SpecialDefense { get; }
        public int Health { get; }

        public Stats(int attack, int defense, int specialDefense, int health)
        {
            Attack = attack;
            Defense = defense;
            SpecialDefense = specialDefense;
            Health = health;
        }

        public static Stats operator +(Stats first, Stats second) => new Stats(
            first.Attack + second.Attack, first.Defense + second.Defense,
            first.SpecialDefense + second.SpecialDefense, first.Health + second.Health);
    }

    public readonly struct DamageComponent
    {
        public DamageType Type { get; }
        public int Power { get; }
        public bool ScalesWithAttack { get; }

        public DamageComponent(DamageType type, int power = 0, bool scalesWithAttack = true)
        {
            if (power < 0)
                throw new ArgumentOutOfRangeException(nameof(power));
            Type = type;
            Power = power;
            ScalesWithAttack = scalesWithAttack;
        }
    }

    public sealed class ActionDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public ActionKind Kind { get; }
        public TargetRule Target { get; }
        public int Power { get; }
        public int Cooldown { get; }
        public DamageType DamageType { get; }
        public bool HealingScalesWithAttack { get; }
        public IReadOnlyList<DamageComponent> AdditionalDamage { get; }

        public ActionDefinition(string id, string name, ActionKind kind, TargetRule target, int power,
            int cooldown = 0, DamageType damageType = DamageType.Default, bool healingScalesWithAttack = false,
            IEnumerable<DamageComponent> additionalDamage = null)
        {
            if (string.IsNullOrWhiteSpace(id) || power < 0 || cooldown < 0)
                throw new ArgumentException("An action needs an ID and nonnegative power and cooldown.");
            bool validTarget = kind == ActionKind.Attack
                ? target == TargetRule.AnyEnemy || target == TargetRule.FrontEnemy || target == TargetRule.RearEnemy
                : kind == ActionKind.Heal ? target == TargetRule.WoundedAlly || target == TargetRule.Self
                : kind == ActionKind.Shield && target == TargetRule.Self;
            if (!validTarget)
                throw new ArgumentException("Target rule does not match the action kind.", nameof(target));
            Id = id;
            Name = name;
            Kind = kind;
            Target = target;
            Power = power;
            Cooldown = cooldown;
            DamageType = damageType;
            HealingScalesWithAttack = healingScalesWithAttack;
            AdditionalDamage = Array.AsReadOnly(additionalDamage?.ToArray() ?? Array.Empty<DamageComponent>());
        }
    }

    public sealed class CharacterDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Profession Profession { get; }
        public Personality Personality { get; }
        public Stats BaseStats { get; }
        public int ActionInterval { get; }
        public ActionDefinition BasicAttack { get; }
        public ActionDefinition Skill { get; }
        public int RosterNumber { get; }
        public int Stage { get; }
        public AttackReach Reach { get; }
        public CharacterTrait Trait { get; }
        public int TraitPower { get; }
        public string TraitText { get; }
        public string BattleFormId { get; }
        public IReadOnlyList<string> EvolutionIds { get; }

        public CharacterDefinition(string id, string name, Profession profession, Personality personality,
            Stats baseStats, int actionInterval, ActionDefinition basicAttack, ActionDefinition skill = null,
            int rosterNumber = 0, int stage = 1, AttackReach reach = AttackReach.Unspecified,
            CharacterTrait trait = CharacterTrait.None, int traitPower = 0, string traitText = null,
            string battleFormId = null, IEnumerable<string> evolutionIds = null)
        {
            if (string.IsNullOrWhiteSpace(id) || baseStats.Health <= 0 || actionInterval <= 0)
                throw new ArgumentException("A character needs an ID, positive health and an action interval.");
            if (basicAttack == null || basicAttack.Kind != ActionKind.Attack || basicAttack.Cooldown != 0)
                throw new ArgumentException("A basic attack must be an attack with no cooldown.", nameof(basicAttack));
            if (skill != null && (skill.Cooldown == 0 || skill.Id == basicAttack.Id))
                throw new ArgumentException("A skill needs a cooldown and its own ID.", nameof(skill));
            Id = id;
            Name = name;
            Profession = profession;
            Personality = personality;
            BaseStats = baseStats;
            ActionInterval = actionInterval;
            BasicAttack = basicAttack;
            Skill = skill;
            RosterNumber = rosterNumber;
            Stage = stage;
            Reach = reach;
            Trait = trait;
            TraitPower = traitPower;
            TraitText = traitText;
            BattleFormId = battleFormId;
            EvolutionIds = Array.AsReadOnly(evolutionIds?.ToArray() ?? Array.Empty<string>());
        }
    }

    public sealed class EquipmentDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Stats Bonus { get; }
        public string Description { get; }
        public EquipmentEffect Effect { get; }
        public bool IsPotion => Effect is EquipmentEffect.VigorPotion or EquipmentEffect.SwiftPotion or
            EquipmentEffect.ResolvePotion or EquipmentEffect.GreedPotion or EquipmentEffect.ThornsPotion or
            EquipmentEffect.PurityPotion;

        public EquipmentDefinition(string id, string name, Stats bonus, string description = null,
            EquipmentEffect effect = EquipmentEffect.None)
        {
            if (string.IsNullOrWhiteSpace(id) || bonus.Attack < 0 || bonus.Defense < 0 ||
                bonus.SpecialDefense < 0 || bonus.Health < 0)
                throw new ArgumentException("Prototype equipment needs an ID and nonnegative bonuses.");
            Id = id;
            Name = name;
            Bonus = bonus;
            Description = description;
            Effect = effect;
        }
    }

    public sealed class Combatant
    {
        public string Id { get; }
        public CharacterDefinition Character { get; }
        public Side Side { get; }
        public int Position { get; }
        public Stats Stats { get; }
        public int Health { get; }
        // Equipment follows the owner’s slot order, with empty slots omitted.
        public IReadOnlyList<EquipmentDefinition> Equipment { get; }
        public IReadOnlyList<string> EquipmentSlots { get; }
        public IReadOnlyDictionary<StatusKind, int> StartingStatuses { get; }

        public Combatant(string id, CharacterDefinition character, Side side, int position, Stats stats, int health,
            IEnumerable<EquipmentDefinition> equipment = null,
            IReadOnlyDictionary<StatusKind, int> startingStatuses = null, IEnumerable<string> equipmentSlots = null)
        {
            if (string.IsNullOrWhiteSpace(id) || !Rules.IsPosition(position) ||
                stats.Health <= 0 || health < 0 || health > stats.Health)
                throw new ArgumentException("Invalid combatant ID, position or health.");
            Id = id;
            Character = character ?? throw new ArgumentNullException(nameof(character));
            Side = side;
            Position = position;
            Stats = stats;
            Health = health;
            Equipment = Array.AsReadOnly(equipment?.ToArray() ?? Array.Empty<EquipmentDefinition>());
            EquipmentSlots = Array.AsReadOnly(equipmentSlots?.ToArray() ?? Array.Empty<string>());
            if (EquipmentSlots.Count != 0 && (EquipmentSlots.Count != Rules.EquipmentSlots ||
                EquipmentSlots.Count(slot => slot != null) != Equipment.Count))
                throw new ArgumentException("Combat equipment slots must match the supplied equipped instances.", nameof(equipmentSlots));
            StartingStatuses = new System.Collections.ObjectModel.ReadOnlyDictionary<StatusKind, int>(
                startingStatuses?.Where(status => status.Value > 0).ToDictionary(status => status.Key, status => status.Value)
                ?? new Dictionary<StatusKind, int>());
        }
    }

    public sealed class BattleRequest
    {
        public string Id { get; }
        public IReadOnlyList<Combatant> Combatants { get; }
        public int TickLimit { get; }
        public IReadOnlyList<CharacterDefinition> RecruitmentPool { get; }
        public IReadOnlyDictionary<string, CharacterDefinition> CharacterCatalog { get; }

        public BattleRequest(string id, IEnumerable<Combatant> combatants, int tickLimit,
            IEnumerable<CharacterDefinition> recruitmentPool = null, IEnumerable<CharacterDefinition> characterCatalog = null)
        {
            if (string.IsNullOrWhiteSpace(id) || tickLimit <= 0)
                throw new ArgumentException("A battle needs an ID and a positive time limit.");
            var units = combatants.ToArray();
            if (units.Select(unit => unit.Id).Distinct(StringComparer.Ordinal).Count() != units.Length ||
                units.Select(unit => (unit.Side, unit.Position)).Distinct().Count() != units.Length ||
                !units.Any(unit => unit.Side == Side.Party && unit.Health > 0) ||
                !units.Any(unit => unit.Side == Side.Enemy && unit.Health > 0))
                throw new ArgumentException("Both sides need a living unit, with unique IDs and positions.", nameof(combatants));
            Id = id;
            Combatants = Array.AsReadOnly(units);
            TickLimit = tickLimit;
            RecruitmentPool = Array.AsReadOnly(recruitmentPool?.ToArray() ?? Array.Empty<CharacterDefinition>());
            CharacterCatalog = new System.Collections.ObjectModel.ReadOnlyDictionary<string, CharacterDefinition>(
                (characterCatalog ?? Enumerable.Empty<CharacterDefinition>()).Concat(RecruitmentPool)
                    .Concat(units.Select(unit => unit.Character)).GroupBy(character => character.Id)
                    .ToDictionary(group => group.Key, group => group.First()));
        }
    }

    public sealed class BattleLoadout
    {
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Slots { get; }
        public IReadOnlyList<string> Consumed { get; }

        public BattleLoadout(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> slots, IEnumerable<string> consumed)
        {
            Slots = new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(
                slots.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)Array.AsReadOnly(entry.Value.ToArray())));
            Consumed = Array.AsReadOnly(consumed.ToArray());
        }
    }

    // Action and equipment events report target state after resolution. Defeat follows the action or effect that caused it.
    public readonly struct BattleEvent
    {
        public int Tick { get; }
        public BattleEventKind Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ActionName { get; }
        public ActionKind ActionKind { get; }
        public DecisionReason Reason { get; }
        public int Amount { get; }
        public int TargetHealth { get; }
        public int TargetShield { get; }
        public string TargetStatuses { get; }
        public string Detail { get; }
        public CharacterDefinition Form { get; }
        public int MaximumHealth { get; }
        public IReadOnlyList<StatusStack> StatusStacks { get; }
        public BattleLoadout Loadout { get; }
        public IReadOnlyList<KeyValuePair<string, Stats>> PartyStatChanges { get; }
        public AttackReach AttackReach { get; }

        public BattleEvent(int tick, BattleEventKind kind, string actorId = null, string targetId = null,
            string actionName = null, ActionKind actionKind = ActionKind.Attack,
            DecisionReason reason = DecisionReason.Default, int amount = 0, int targetHealth = 0, int targetShield = 0,
            string targetStatuses = null, string detail = null, CharacterDefinition form = null, int maximumHealth = 0,
            IReadOnlyList<StatusStack> statusStacks = null, BattleLoadout loadout = null,
            IReadOnlyList<KeyValuePair<string, Stats>> partyStatChanges = null, AttackReach attackReach = AttackReach.Unspecified)
        {
            Tick = tick;
            Kind = kind;
            ActorId = actorId;
            TargetId = targetId;
            ActionName = actionName;
            ActionKind = actionKind;
            Reason = reason;
            Amount = amount;
            TargetHealth = targetHealth;
            TargetShield = targetShield;
            TargetStatuses = targetStatuses;
            Detail = detail;
            Form = form;
            MaximumHealth = maximumHealth;
            StatusStacks = statusStacks;
            Loadout = loadout;
            PartyStatChanges = partyStatChanges;
            AttackReach = attackReach;
        }
    }

    public sealed class BattleResult
    {
        public string Id { get; }
        public BattleOutcome Outcome { get; }
        public IReadOnlyDictionary<string, int> HealthById { get; }
        public IReadOnlyDictionary<string, int> PartyPositionsById { get; }
        public IReadOnlyList<BattleEvent> Events { get; }
        public IReadOnlyList<Combatant> Recruits { get; }
        public IReadOnlyDictionary<string, int> PermanentAttackGains { get; }
        public int PotionsEarned { get; }
        public BattleLoadout Loadout { get; }

        public BattleResult(string id, BattleOutcome outcome, IEnumerable<KeyValuePair<string, int>> health,
            IEnumerable<BattleEvent> events, IEnumerable<KeyValuePair<string, int>> partyPositions = null,
            IEnumerable<Combatant> recruits = null, IEnumerable<KeyValuePair<string, int>> permanentAttackGains = null,
            int potionsEarned = 0, BattleLoadout loadout = null)
        {
            Id = id;
            Outcome = outcome;
            HealthById = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                health.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            PartyPositionsById = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                (partyPositions ?? Enumerable.Empty<KeyValuePair<string, int>>())
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            Events = Array.AsReadOnly(events.ToArray());
            Recruits = Array.AsReadOnly(recruits?.ToArray() ?? Array.Empty<Combatant>());
            PermanentAttackGains = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                (permanentAttackGains ?? Enumerable.Empty<KeyValuePair<string, int>>())
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            PotionsEarned = potionsEarned;
            Loadout = loadout;
        }
    }
}
