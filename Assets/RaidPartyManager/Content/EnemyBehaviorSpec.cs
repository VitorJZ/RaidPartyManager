using System;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [Serializable]
    internal sealed class EnemyActionSpec
    {
        [SerializeField] private string name;
        [SerializeField, Min(1)] private int weight = 100;
        [SerializeField] private EnemyActionKind kind;
        [SerializeField] private EnemyTargetRule target;
        [SerializeField] private int power;
        [SerializeField] private DamageType damageType;
        [SerializeField] private int status = -1;
        [SerializeField, Min(0)] private int layers;
        [SerializeField] private bool requiresCharge;
        [SerializeField] private bool delayDecay;

        public EnemyActionDefinition ToDefinition() => new EnemyActionDefinition(name, weight, kind, target,
            power, damageType, status >= 0 ? (StatusKind?)status : null, layers, requiresCharge, delayDecay);
    }

    [Serializable]
    internal sealed class EnemyBehaviorSpec
    {
        [SerializeField] private EnemyActionSpec[] actions = Array.Empty<EnemyActionSpec>();
        [SerializeField, Min(0)] private int openingShield;
        [SerializeField, Range(0, 100)] private int phaseThresholdPercent;
        [SerializeField, Min(0)] private int phaseAttackBonus;

        public EnemyBehaviorDefinition ToDefinition() => actions == null || actions.Length == 0 ? null :
            new EnemyBehaviorDefinition(actions.Select(action => action.ToDefinition()), openingShield,
                phaseThresholdPercent, phaseAttackBonus);
    }
}
