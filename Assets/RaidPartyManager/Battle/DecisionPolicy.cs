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
        public SkillBookDefinition Book { get; }

        public ActionOption(ActionDefinition action, Combatant target, int health, int projectedAmount, ActionKind? kind = null,
            SkillBookDefinition book = null)
        {
            Action = action;
            Kind = kind ?? action.Kind;
            Target = target;
            Health = health;
            ProjectedAmount = projectedAmount;
            Book = book;
        }
    }

    public readonly struct DecisionContext
    {
        public Combatant Actor { get; }
        public int Tick { get; }
        public int ActorHealth { get; }
        public int ActorShield { get; }

        public DecisionContext(Combatant actor, int tick, int actorHealth, int actorShield)
        {
            Actor = actor;
            Tick = tick;
            ActorHealth = actorHealth;
            ActorShield = actorShield;
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

    public sealed class DefaultBattlePolicy : IDecisionPolicy
    {
        public Decision Choose(DecisionContext context, IReadOnlyList<ActionOption> options)
        {
            if (context.Actor.Side == Side.Enemy)
                return new Decision(0, DecisionReason.Default);

            int best = 0;
            for (int i = 1; i < options.Count; i++)
            {
                var candidate = options[i];
                var current = options[best];
                int priority = Priority(candidate).CompareTo(Priority(current));
                if (priority > 0 || priority == 0 && BetterTarget(candidate, current))
                    best = i;
            }

            var chosen = options[best];
            var reason = chosen.Book != null ? DecisionReason.UseSkill : chosen.Kind switch
            {
                ActionKind.Heal => DecisionReason.WoundedAlly,
                ActionKind.Shield => DecisionReason.SelfProtection,
                _ => DecisionReason.HighestDamage
            };
            return new Decision(best, reason);
        }

        private static int Priority(ActionOption option) =>
            option.Book != null ? 3 : option.Kind switch
            {
                ActionKind.Heal => 2,
                ActionKind.Shield => 1,
                _ => 0
            };

        private static bool BetterTarget(ActionOption candidate, ActionOption current)
        {
            if (candidate.Book == null && candidate.Kind == ActionKind.Heal)
                return (long)candidate.Health * current.Target.Stats.Health < (long)current.Health * candidate.Target.Stats.Health;
            return candidate.ProjectedAmount > current.ProjectedAmount;
        }
    }
}
