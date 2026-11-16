using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed class BattleSimulator
    {
        private sealed class Unit
        {
            public Combatant Input { get; }
            public int Health;
            public int Shield;
            public long NextAction;
            public long SkillReadyAt;

            public Unit(Combatant input)
            {
                Input = input;
                Health = input.Health;
                NextAction = input.Character.ActionInterval;
            }
        }

        private readonly IDecisionPolicy policy;

        public BattleSimulator(IDecisionPolicy policy = null)
        {
            this.policy = policy ?? new PersonalityPolicy();
        }

        public BattleResult Simulate(BattleRequest request)
        {
            var units = request.Combatants.OrderBy(unit => unit.Side).ThenBy(unit => unit.Position)
                .Select(input => new Unit(input)).ToArray();
            var unitsById = units.ToDictionary(unit => unit.Input.Id);
            var events = new List<BattleEvent>();
            var attackHistory = new List<(Side Side, string Actor, string Target)>();

            while (true)
            {
                long nextTick = units.Where(unit => unit.Health > 0).Min(unit => unit.NextAction);
                if (nextTick > request.TickLimit)
                    return Finish(BattleOutcome.Timeout, request.TickLimit);

                int tick = (int)nextTick;
                foreach (var actor in units)
                {
                    if (actor.Health == 0 || actor.NextAction != tick)
                        continue;

                    actor.NextAction += actor.Input.Character.ActionInterval;
                    var options = new List<ActionOption>();
                    var skill = actor.Input.Character.Skill;
                    if (skill != null && tick >= actor.SkillReadyAt)
                        AddLegalOptions(actor, skill, units, options);
                    AddLegalOptions(actor, actor.Input.Character.BasicAttack, units, options);
                    if (options.Count == 0)
                        continue;

                    string allyTarget = null;
                    for (int i = attackHistory.Count - 1; i >= 0; i--)
                    {
                        var hit = attackHistory[i];
                        if (hit.Side != actor.Input.Side || hit.Actor == actor.Input.Id ||
                            !options.Any(option => option.Action.Kind == ActionKind.Attack && option.Target.Id == hit.Target))
                            continue;
                        allyTarget = hit.Target;
                        break;
                    }

                    var decision = policy.Choose(new DecisionContext(actor.Input, tick, actor.Health, actor.Shield, allyTarget), options.AsReadOnly());
                    if (decision.OptionIndex < 0 || decision.OptionIndex >= options.Count)
                        throw new InvalidOperationException("The decision policy must choose a legal option.");

                    var choice = options[decision.OptionIndex];
                    var target = unitsById[choice.Target.Id];
                    if (choice.Action == skill)
                        actor.SkillReadyAt = (long)tick + skill.Cooldown;
                    int amount = Apply(actor, target, choice.Action);
                    if (choice.Action.Kind == ActionKind.Attack)
                        attackHistory.Add((actor.Input.Side, actor.Input.Id, target.Input.Id));

                    events.Add(new BattleEvent(tick, BattleEventKind.Action, actor.Input.Id, target.Input.Id,
                        choice.Action.Name, choice.Action.Kind, decision.Reason, amount, target.Health, target.Shield));
                    if (target.Health == 0)
                        events.Add(new BattleEvent(tick, BattleEventKind.Defeated, targetId: target.Input.Id));

                    var outcome = ResolveOutcome(units.Any(unit => unit.Input.Side == Side.Party && unit.Health > 0),
                        units.Any(unit => unit.Input.Side == Side.Enemy && unit.Health > 0));
                    if (outcome.HasValue)
                        return Finish(outcome.Value, tick);
                }
            }

            BattleResult Finish(BattleOutcome outcome, int tick)
            {
                events.Add(new BattleEvent(tick, BattleEventKind.Finished));
                return new BattleResult(request.Id, outcome, units.Select(unit =>
                    new KeyValuePair<string, int>(unit.Input.Id, unit.Health)), events);
            }
        }

        public static BattleOutcome? ResolveOutcome(bool partyAlive, bool enemiesAlive)
        {
            if (!partyAlive)
                return enemiesAlive ? BattleOutcome.PartyDefeated : BattleOutcome.MutualDefeat;
            return enemiesAlive ? (BattleOutcome?)null : BattleOutcome.Victory;
        }

        private static void AddLegalOptions(Unit actor, ActionDefinition action, Unit[] units, List<ActionOption> options)
        {
            if (action.Kind == ActionKind.Attack)
            {
                var enemies = units.Where(unit => unit.Input.Side != actor.Input.Side && unit.Health > 0);
                if (action.Target == TargetRule.FrontEnemy)
                    enemies = enemies.Take(1);
                else if (action.Target == TargetRule.RearEnemy)
                    enemies = enemies.Reverse().Take(1);
                foreach (var target in enemies)
                    options.Add(new ActionOption(action, target.Input, target.Health,
                        Math.Max(0, Damage(actor, target, action) - target.Shield)));
                return;
            }

            if (action.Kind == ActionKind.Shield)
            {
                int gain = action.Power - actor.Shield;
                if (gain > 0)
                    options.Add(new ActionOption(action, actor.Input, actor.Health, gain));
                return;
            }

            foreach (var ally in units)
            {
                if (ally.Input.Side != actor.Input.Side || ally.Health == 0 || ally.Health == ally.Input.Stats.Health ||
                    (action.Target == TargetRule.Self && ally != actor))
                    continue;
                int gain = Math.Min(action.Power, ally.Input.Stats.Health - ally.Health);
                if (gain > 0)
                    options.Add(new ActionOption(action, ally.Input, ally.Health, gain));
            }
        }

        private static int Damage(Unit actor, Unit target, ActionDefinition action)
        {
            int defense = action.DamageType == DamageType.Physical ? target.Input.Stats.Defense : target.Input.Stats.SpecialDefense;
            return Math.Max(1, actor.Input.Stats.Attack + action.Power - defense);
        }

        private static int Apply(Unit actor, Unit target, ActionDefinition action)
        {
            if (action.Kind == ActionKind.Shield)
            {
                int gain = action.Power - actor.Shield;
                actor.Shield = action.Power;
                return gain;
            }

            if (action.Kind == ActionKind.Heal)
            {
                int healed = Math.Min(action.Power, target.Input.Stats.Health - target.Health);
                target.Health += healed;
                return healed;
            }

            int damage = Damage(actor, target, action);
            int absorbed = Math.Min(target.Shield, damage);
            target.Shield -= absorbed;
            int lost = Math.Min(target.Health, damage - absorbed);
            target.Health -= lost;
            return lost;
        }
    }
}
