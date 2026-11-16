using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    public enum Profession { Warrior, Mage, Archer, Support }
    public enum Personality { Aggressive, Cautious, Reckless, Gentle, Cooperative, Calm }
    public enum Side { Party, Enemy }
    public enum ActionKind { Attack, Heal, Shield }
    public enum DamageType { Physical, Magic }
    public enum TargetRule { AnyEnemy, FrontEnemy, RearEnemy, WoundedAlly, Self }
    public enum BattleOutcome { Victory, PartyDefeated, MutualDefeat, Timeout }
    public enum DecisionReason { Default, HighestDamage, SelfProtection, FavorAttack, WoundedAlly, FollowAlly, LowestHealth }
    public enum BattleEventKind { Action, Defeated, Finished }

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

    public sealed class ActionDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public ActionKind Kind { get; }
        public TargetRule Target { get; }
        public int Power { get; }
        public int Cooldown { get; }
        public DamageType DamageType { get; }

        public ActionDefinition(string id, string name, ActionKind kind, TargetRule target, int power,
            int cooldown = 0, DamageType damageType = DamageType.Physical)
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

        public CharacterDefinition(string id, string name, Profession profession, Personality personality,
            Stats baseStats, int actionInterval, ActionDefinition basicAttack, ActionDefinition skill = null)
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
        }
    }

    public sealed class EquipmentDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Stats Bonus { get; }
        public string Description { get; }

        public EquipmentDefinition(string id, string name, Stats bonus, string description = null)
        {
            if (string.IsNullOrWhiteSpace(id) || bonus.Attack < 0 || bonus.Defense < 0 ||
                bonus.SpecialDefense < 0 || bonus.Health < 0)
                throw new ArgumentException("Prototype equipment needs an ID and nonnegative bonuses.");
            Id = id;
            Name = name;
            Bonus = bonus;
            Description = description;
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

        public Combatant(string id, CharacterDefinition character, Side side, int position, Stats stats, int health)
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
        }
    }

    public sealed class BattleRequest
    {
        public string Id { get; }
        public IReadOnlyList<Combatant> Combatants { get; }
        public int TickLimit { get; }

        public BattleRequest(string id, IEnumerable<Combatant> combatants, int tickLimit)
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
        }
    }

    // An action event reports target state after resolution. A defeat event follows the action that caused it.
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

        public BattleEvent(int tick, BattleEventKind kind, string actorId = null, string targetId = null,
            string actionName = null, ActionKind actionKind = ActionKind.Attack,
            DecisionReason reason = DecisionReason.Default, int amount = 0, int targetHealth = 0, int targetShield = 0)
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
        }
    }

    public sealed class BattleResult
    {
        public string Id { get; }
        public BattleOutcome Outcome { get; }
        public IReadOnlyDictionary<string, int> HealthById { get; }
        public IReadOnlyList<BattleEvent> Events { get; }

        public BattleResult(string id, BattleOutcome outcome, IEnumerable<KeyValuePair<string, int>> health,
            IEnumerable<BattleEvent> events)
        {
            Id = id;
            Outcome = outcome;
            HealthById = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                health.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            Events = Array.AsReadOnly(events.ToArray());
        }
    }
}
