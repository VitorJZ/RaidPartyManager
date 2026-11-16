using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public readonly struct ActionOption
    {
        public ActionDefinition Action { get; }
        public Combatant Target { get; }
        public ActionKind Kind { get; }
        public int Health { get; }
        public int ProjectedAmount { get; }

        public ActionOption(ActionDefinition action, Combatant target, int health, int projectedAmount, ActionKind? kind = null)
        {
            Action = action;
            Kind = kind ?? action.Kind;
            Target = target;
            Health = health;
            ProjectedAmount = projectedAmount;
        }
    }

    public readonly struct DecisionContext
    {
        public Combatant Actor { get; }
        public int Tick { get; }
        public int ActorHealth { get; }
        public int ActorShield { get; }
        public string AllyAttackTarget { get; }

        public DecisionContext(Combatant actor, int tick, int actorHealth, int actorShield, string allyAttackTarget)
        {
            Actor = actor;
            Tick = tick;
            ActorHealth = actorHealth;
            ActorShield = actorShield;
            AllyAttackTarget = allyAttackTarget;
        }
    }

    public readonly struct Decision
    {
        public int OptionIndex { get; }
        public DecisionReason Reason { get; }

        public Decision(int optionIndex, DecisionReason reason)
        {
            OptionIndex = optionIndex;
            Reason = reason;
        }
    }

    // A policy picks by index from already legal options. It never changes live combat state.
    public interface IDecisionPolicy
    {
        Decision Choose(DecisionContext context, IReadOnlyList<ActionOption> options);
    }

    public sealed class PersonalityPolicy : IDecisionPolicy
    {
        public Decision Choose(DecisionContext context, IReadOnlyList<ActionOption> options)
        {
            var bestIndex = 0;
            var bestReason = DecisionReason.Default;
            for (int i = 0; i < options.Count; i++)
            {
                var reason = Preference(context, options[i]);
                if (reason == DecisionReason.Default)
                    continue;
                if (bestReason != DecisionReason.Default &&
                    !IsBetter(reason, options[i], options[bestIndex]))
                    continue;
                bestIndex = i;
                bestReason = reason;
            }
            return new Decision(bestIndex, bestReason);
        }

        private static DecisionReason Preference(DecisionContext context, ActionOption option)
        {
            bool attacks = option.Kind == ActionKind.Attack;
            switch (context.Actor.Character.Personality)
            {
                case Personality.Aggressive when attacks:
                    return DecisionReason.HighestDamage;
                case Personality.Cautious when option.Target.Id == context.Actor.Id && !attacks && option.ProjectedAmount > 0:
                    return DecisionReason.SelfProtection;
                case Personality.Reckless when attacks:
                    return DecisionReason.FavorAttack;
                case Personality.Gentle when option.Kind == ActionKind.Heal:
                    return DecisionReason.WoundedAlly;
                case Personality.Cooperative when attacks && option.Target.Id == context.AllyAttackTarget:
                    return DecisionReason.FollowAlly;
                case Personality.Calm when attacks:
                    return DecisionReason.LowestHealth;
                default:
                    return DecisionReason.Default;
            }
        }

        private static bool IsBetter(DecisionReason reason, ActionOption candidate, ActionOption incumbent)
        {
            switch (reason)
            {
                case DecisionReason.HighestDamage:
                case DecisionReason.SelfProtection:
                    return candidate.ProjectedAmount > incumbent.ProjectedAmount;
                case DecisionReason.WoundedAlly:
                case DecisionReason.LowestHealth:
                    // Cross multiplication avoids floating-point ties on health percentages.
                    return (long)candidate.Health * incumbent.Target.Stats.Health <
                        (long)incumbent.Health * candidate.Target.Stats.Health;
                default:
                    return false;
            }
        }
    }
}
