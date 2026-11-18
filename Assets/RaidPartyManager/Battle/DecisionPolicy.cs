using System.Collections.Generic;
using System.Linq;
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
        public string AllyAttackTarget { get; }
        public string LastAttacker { get; }
        public string FocusTarget { get; }

        public DecisionContext(Combatant actor, int tick, int actorHealth, int actorShield, string allyAttackTarget,
            string lastAttacker = null, string focusTarget = null)
        {
            Actor = actor;
            Tick = tick;
            ActorHealth = actorHealth;
            ActorShield = actorShield;
            AllyAttackTarget = allyAttackTarget;
            LastAttacker = lastAttacker;
            FocusTarget = focusTarget;
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
            if (context.Actor.Side == Side.Enemy)
                return new Decision(0, DecisionReason.Default);

            bool saveBook = context.Actor.Character.Personality == Personality.Patient && options.Any(option => option.Book != null);
            if (saveBook)
                for (int i = 0; i < options.Count; i++)
                    if (options[i].Book == null && options[i].Kind == ActionKind.Attack && options[i].ProjectedAmount >= options[i].Health)
                        return new Decision(i, DecisionReason.SaveSkill);

            var bestIndex = 0;
            var bestReason = DecisionReason.Default;
            for (int i = 0; i < options.Count; i++)
            {
                var reason = Preference(context, options[i]);
                if (reason == DecisionReason.Default)
                    continue;
                if (bestReason != DecisionReason.Default)
                {
                    bool better = reason == bestReason
                        ? IsBetter(reason, options[i], options[bestIndex])
                        : Priority(reason) > Priority(bestReason);
                    if (!better)
                        continue;
                }
                bestIndex = i;
                bestReason = reason;
            }
            return new Decision(bestIndex, bestReason);
        }

        private static int Priority(DecisionReason reason) => reason switch
        {
            DecisionReason.EmergencyHeal => 2,
            DecisionReason.UseSkill or DecisionReason.SaveSkill or DecisionReason.AvoidOverkill => 1,
            _ => 0
        };

        private static DecisionReason Preference(DecisionContext context, ActionOption option)
        {
            bool attacks = option.Kind == ActionKind.Attack;
            if (option.Book != null)
                return DecisionReason.UseSkill;
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
                case Personality.Vengeful when attacks && option.Target.Id == context.LastAttacker:
                    return DecisionReason.Retaliate;
                case Personality.Careful when attacks:
                    return DecisionReason.FinishWounded;
                case Personality.Focused when attacks && option.Target.Id == context.FocusTarget:
                    return DecisionReason.StayOnTarget;
                case Personality.Impatient when option.Action == context.Actor.Character.Skill:
                    return DecisionReason.UseSkill;
                case Personality.Patient when context.Actor.Character.Skill != null &&
                    option.Action != context.Actor.Character.Skill:
                    return DecisionReason.SaveSkill;
                case Personality.Greedy when option.Kind == ActionKind.Heal && (long)option.Health * 4 <= option.Target.Stats.Health:
                    return DecisionReason.EmergencyHeal;
                case Personality.Greedy when attacks:
                    return DecisionReason.AvoidOverkill;
                case Personality.Timid or Personality.Careful or Personality.Vengeful or Personality.Stubborn or
                    Personality.Impatient or Personality.Focused or Personality.Patient when option.Kind == ActionKind.Heal:
                    return DecisionReason.WoundedAlly;
                default:
                    return DecisionReason.Default;
            }
        }

        private static bool IsBetter(DecisionReason reason, ActionOption candidate, ActionOption incumbent)
        {
            switch (reason)
            {
                case DecisionReason.FinishWounded:
                    return candidate.Health < incumbent.Health;
                case DecisionReason.AvoidOverkill:
                    int effective = System.Math.Min(candidate.Health, candidate.ProjectedAmount);
                    int previous = System.Math.Min(incumbent.Health, incumbent.ProjectedAmount);
                    return effective > previous || effective == previous &&
                        candidate.ProjectedAmount - effective < incumbent.ProjectedAmount - previous;
                case DecisionReason.UseSkill when candidate.Book != null && incumbent.Book != null:
                case DecisionReason.HighestDamage:
                case DecisionReason.SelfProtection:
                    return candidate.ProjectedAmount > incumbent.ProjectedAmount;
                case DecisionReason.WoundedAlly:
                case DecisionReason.EmergencyHeal:
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
