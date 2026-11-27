using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private readonly Dictionary<Unit, EnemyActionDefinition> chargedEnemyActions = new();
        private readonly HashSet<Unit> phasedEnemies = new();

        private void SetConfiguredEnemyOpeningShields()
        {
            foreach (var actor in units.Where(unit => unit.Input.Side == Side.Enemy && unit.Health > 0))
            {
                int shield = actor.Input.Character.EnemyBehavior?.OpeningShield ?? 0;
                if (shield == 0) continue;
                actor.Shield += shield;
                Report(BattleEventKind.Effect, actor, actor, request.CapitalField ? "铸城护甲" : "墓园护甲", ActionKind.Shield, shield,
                    visual: BattleVisualCue.GraveyardSpecial);
            }
        }

        private bool TryConfiguredEnemyTurn(Unit actor)
        {
            var profile = actor.Input.Character.EnemyBehavior;
            if (profile == null) return false;

            if (profile.PhaseThresholdPercent > 0 &&
                (long)actor.Health * 100 <= (long)actor.Stats.Health * profile.PhaseThresholdPercent &&
                phasedEnemies.Add(actor))
                Report(BattleEventKind.Effect, actor, actor, request.CapitalField ? "炉压过载" : "墓园第二阶段", ActionKind.Shield,
                    detail: $"此后伤害招式的攻击修正 +{profile.PhaseAttackBonus}", visual: BattleVisualCue.GraveyardPhase);

            if (TryBossTurn(actor, profile)) return true;

            if (chargedEnemyActions.Remove(actor, out var charged))
            {
                ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 0);
                ExecuteConfiguredEnemyAction(actor, charged, BattleVisualCue.GraveyardSpecial);
                return true;
            }

            int roll = random.Next(profile.Actions.Sum(action => action.Weight));
            var chosen = profile.Actions[0];
            foreach (var action in profile.Actions)
            {
                if (roll < action.Weight) { chosen = action; break; }
                roll -= action.Weight;
            }
            var cue = chosen == profile.Actions[0] ? BattleVisualCue.None : BattleVisualCue.GraveyardSpecial;
            if (chosen.RequiresCharge)
            {
                chargedEnemyActions.Add(actor, chosen);
                Report(BattleEventKind.Action, actor, actor, chosen.Name + "·蓄力", ActionKind.Shield,
                    detail: "下次可行动时释放，目标按释放时的存活者与站位重新选择", visual: BattleVisualCue.GraveyardCharge);
                ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 1);
                return true;
            }
            ExecuteConfiguredEnemyAction(actor, chosen, cue);
            return true;
        }

        private IEnumerable<Unit> ConfiguredEnemyTargets(Unit actor, EnemyActionDefinition action)
        {
            var opponents = TargetableOpponents(actor).OrderBy(unit => unit.Input.Position).ToArray();
            var allies = units.Where(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0)
                .OrderBy(unit => unit.Input.Position).ToArray();
            // Healing only chooses a living, injured, healable ally. Shields may protect a full-health ally.
            var wounded = action.Kind == EnemyActionKind.Heal
                ? allies.Where(unit => unit.Health < unit.Stats.Health && unit.CanReceiveHealing)
                : allies.AsEnumerable();
            return action.Target switch
            {
                EnemyTargetRule.Front when action.Kind == EnemyActionKind.Attack && !action.RequiresCharge &&
                    action == actor.Input.Character.EnemyBehavior.Actions[0] && IsRanged(actor) && opponents.Length > 0
                    => new[] { RandomTarget(opponents) },
                EnemyTargetRule.Front when action.Kind == EnemyActionKind.Attack => FrontAttackTargets(actor).Take(1),
                EnemyTargetRule.Front => opponents.Take(1),
                EnemyTargetRule.Rear => opponents.TakeLast(1),
                EnemyTargetRule.LowestHealth => opponents.OrderBy(unit => (double)unit.Health / unit.Stats.Health).Take(1),
                EnemyTargetRule.HighestAttack => opponents.OrderByDescending(CurrentAttack).Take(1),
                EnemyTargetRule.LowestDefense => opponents.OrderBy(unit => unit.Stats.Defense).Take(1),
                EnemyTargetRule.CursedOrLowestDefense => opponents.OrderByDescending(unit => unit.Layers(StatusKind.Cursed) > 0)
                    .ThenBy(unit => unit.Stats.Defense).Take(1),
                EnemyTargetRule.ExposedOrFront => opponents.Where(unit => unit.Layers(StatusKind.Exposed) > 0)
                    .Concat(FrontAttackTargets(actor)).Take(1),
                EnemyTargetRule.ExposedOrRear => opponents.OrderByDescending(unit => unit.Layers(StatusKind.Exposed) > 0)
                    .ThenByDescending(unit => unit.Input.Position).Take(1),
                EnemyTargetRule.Self => new[] { actor },
                EnemyTargetRule.WoundedAlly => wounded.OrderBy(unit => (double)unit.Health / unit.Stats.Health).Take(1),
                EnemyTargetRule.FrontTwo => opponents.Take(2),
                EnemyTargetRule.RearTwo => opponents.TakeLast(2),
                EnemyTargetRule.AllEnemies => Opponents(actor).OrderBy(unit => unit.Input.Position),
                EnemyTargetRule.AllAllies => allies,
                EnemyTargetRule.StrongestAlly => allies.OrderByDescending(CurrentAttack).Take(1),
                _ => Array.Empty<Unit>()
            };
        }

        private void ExecuteConfiguredEnemyAction(Unit actor, EnemyActionDefinition action, BattleVisualCue cue, Unit[] targets = null)
        {
            targets ??= ConfiguredEnemyTargets(actor, action).ToArray();
            if (targets.Length == 0)
            {
                var target = IsRanged(actor) ? RandomTarget(TargetableOpponents(actor)) : TargetableOpponents(actor).FirstOrDefault();
                if (target != null)
                    EnemyHit(actor, target, "无合法目标·普攻", BattleVisualCue.None);
                return;
            }

            bool first = true;
            var reactions = new List<Action>();
            foreach (var target in targets)
            {
                if (actor.Health == 0 || target.Health == 0) continue;
                var kind = first ? BattleEventKind.Action : BattleEventKind.Effect;
                switch (action.Kind)
                {
                    case EnemyActionKind.Attack:
                        int phase = phasedEnemies.Contains(actor) ? actor.Input.Character.EnemyBehavior.PhaseAttackBonus : 0;
                        int modifier = action.Power + phase;
                        var attack = new ActionDefinition("enemy-configured", action.Name, ActionKind.Attack,
                            TargetRule.AnyEnemy, Math.Max(0, modifier), damageType: action.DamageType);
                        Hit(actor, target, attack, kind, DecisionReason.Default, visual: cue,
                            falloff: Math.Max(0, -modifier), firstTarget: first, reactions: reactions, singleTarget: targets.Length == 1,
                            onHit: defender =>
                            {
                                if (actor.Health > 0 && defender.Health > 0 && action.Status.HasValue)
                                    GrantStatus(actor, defender, action.Status.Value, action.Layers, kind: BattleEventKind.Effect,
                                        delayDecay: action.DelayDecay);
                            });
                        break;
                    case EnemyActionKind.Status:
                        GrantStatus(actor, target, action.Status.Value, action.Layers, action.Name, cue, kind,
                            delayDecay: action.DelayDecay);
                        break;
                    case EnemyActionKind.Heal:
                        Heal(actor, target, action.Power, action.Name, kind, visual: cue);
                        break;
                    case EnemyActionKind.Shield:
                        target.Shield += action.Power;
                        Report(kind, actor, target, action.Name, ActionKind.Shield, action.Power, visual: cue);
                        break;
                }
                first = false;
            }
            foreach (var reaction in reactions) reaction();
        }
    }
}
