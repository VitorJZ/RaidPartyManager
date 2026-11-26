using System;
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
        public int LastCastRound { get; }
        public int EnergyCost { get; }

        public ActionOption(ActionDefinition action, Combatant target, int health, int projectedAmount, ActionKind? kind = null,
            SkillBookDefinition book = null, int lastCastRound = 0, int? energyCost = null)
        {
            Action = action;
            Kind = kind ?? action.Kind;
            Target = target;
            Health = health;
            ProjectedAmount = projectedAmount;
            Book = book;
            LastCastRound = lastCastRound;
            EnergyCost = energyCost ?? book?.Cost ?? 0;
        }
    }

    public readonly struct DecisionContext
    {
        public Combatant Actor { get; }
        public int Tick { get; }
        public int ActorHealth { get; }
        public int ActorShield { get; }
        public bool RandomRangedAttack { get; }

        public DecisionContext(Combatant actor, int tick, int actorHealth, int actorShield, bool randomRangedAttack = false)
        {
            Actor = actor;
            Tick = tick;
            ActorHealth = actorHealth;
            ActorShield = actorShield;
            RandomRangedAttack = randomRangedAttack;
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

            int routine = -1, bestBook = -1;
            for (int i = 0; i < options.Count; i++)
            {
                var candidate = options[i];
                if (candidate.Book != null)
                {
                    if (bestBook < 0 || SkillValue(candidate) > SkillValue(options[bestBook]))
                        bestBook = i;
                }
                else if (routine < 0 || Priority(candidate) > Priority(options[routine]) ||
                    Priority(candidate) == Priority(options[routine]) && BetterTarget(candidate, options[routine]))
                    routine = i;
            }

            int best = routine;
            double routineValue = routine < 0 ? double.NegativeInfinity : RoutineValue(context, options[routine], options);
            if (bestBook >= 0 && SkillValue(options[bestBook]) > routineValue)
            {
                best = bestBook;
                int strongest = SkillValue(options[bestBook]);
                for (int i = 0; i < options.Count; i++)
                {
                    var candidate = options[i];
                    if (candidate.Book == null) continue;
                    int value = SkillValue(candidate);
                    if (value <= routineValue || 5L * value < 4L * strongest) continue;
                    if (candidate.LastCastRound < options[best].LastCastRound ||
                        candidate.LastCastRound == options[best].LastCastRound && value > SkillValue(options[best]))
                        best = i;
                }
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
            option.Kind switch
            {
                ActionKind.Heal => 2,
                ActionKind.Shield => 1,
                _ => 0
            };

        private static bool BetterTarget(ActionOption candidate, ActionOption current)
        {
            if (candidate.Kind == ActionKind.Heal)
                return (long)candidate.Health * current.Target.Stats.Health < (long)current.Health * candidate.Target.Stats.Health;
            return candidate.ProjectedAmount > current.ProjectedAmount;
        }

        private static int SkillValue(ActionOption option) =>
            option.ProjectedAmount - Math.Max(0, option.EnergyCost - 1);

        private static double RoutineValue(DecisionContext context, ActionOption choice, IReadOnlyList<ActionOption> options)
        {
            if (choice.Kind != ActionKind.Attack) return choice.ProjectedAmount;
            if (context.RandomRangedAttack && choice.Action == context.Actor.Character.BasicAttack &&
                choice.Action.Target == TargetRule.AnyEnemy)
            {
                return options.Where(option => option.Book == null && option.Action == choice.Action)
                    .Average(option => Math.Min(option.Health, option.ProjectedAmount));
            }
            return Math.Min(choice.Health, choice.ProjectedAmount);
        }
    }
}
