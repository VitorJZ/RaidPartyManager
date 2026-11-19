using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    public enum EnemyActionKind { Attack, Status, Heal, Shield }
    public enum EnemyTargetRule
    {
        Front, Rear, LowestHealth, HighestAttack, LowestDefense, Self, WoundedAlly,
        FrontTwo, RearTwo, AllEnemies, AllAllies, StrongestAlly
    }

    // Pure data. Content owns serialization; Battle owns rolls, targeting and execution.
    public sealed class EnemyActionDefinition
    {
        public string Name { get; }
        public int Weight { get; }
        public EnemyActionKind Kind { get; }
        public EnemyTargetRule Target { get; }
        public int Power { get; }
        public DamageType DamageType { get; }
        public StatusKind? Status { get; }
        public int Layers { get; }
        public bool RequiresCharge { get; }
        public bool DelayDecay { get; }

        public EnemyActionDefinition(string name, int weight, EnemyActionKind kind, EnemyTargetRule target,
            int power = 0, DamageType damageType = DamageType.Physical, StatusKind? status = null,
            int layers = 0, bool requiresCharge = false, bool delayDecay = false)
        {
            if (string.IsNullOrWhiteSpace(name) || weight <= 0 || layers < 0 ||
                kind == EnemyActionKind.Status && (!status.HasValue || layers == 0) ||
                (kind is EnemyActionKind.Heal or EnemyActionKind.Shield) && power <= 0)
                throw new ArgumentException("Enemy actions need a name, positive weight and valid effect.");
            Name = name;
            Weight = weight;
            Kind = kind;
            Target = target;
            Power = power;
            DamageType = damageType;
            Status = status;
            Layers = layers;
            RequiresCharge = requiresCharge;
            DelayDecay = delayDecay;
        }
    }

    public sealed class EnemyBehaviorDefinition
    {
        public IReadOnlyList<EnemyActionDefinition> Actions { get; }
        public int OpeningShield { get; }
        public int PhaseThresholdPercent { get; }
        public int PhaseAttackBonus { get; }

        public EnemyBehaviorDefinition(IEnumerable<EnemyActionDefinition> actions, int openingShield = 0,
            int phaseThresholdPercent = 0, int phaseAttackBonus = 0)
        {
            var entries = actions?.ToArray() ?? Array.Empty<EnemyActionDefinition>();
            if (entries.Length == 0 || entries.Any(action => action == null) || openingShield < 0 ||
                phaseThresholdPercent < 0 || phaseThresholdPercent > 100 || phaseAttackBonus < 0 ||
                entries.Sum(action => (long)action.Weight) > int.MaxValue)
                throw new ArgumentException("Invalid enemy behavior profile.");
            Actions = Array.AsReadOnly(entries);
            OpeningShield = openingShield;
            PhaseThresholdPercent = phaseThresholdPercent;
            PhaseAttackBonus = phaseAttackBonus;
        }
    }
}
