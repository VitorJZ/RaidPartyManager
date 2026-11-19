using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.SkillEffect;
using static RaidPartyManager.Domain.StatusKind;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private void AddLearnedSkillOptions(Unit actor, List<ActionOption> options)
        {
            var enemies = Opponents(actor).ToArray();
            var allies = units.Where(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0).ToArray();

            foreach (var book in actor.Original.LearnedSkills.Where(book => book.Cost <= actor.Energy.Remaining))
            {
                var action = new ActionDefinition(book.Id, book.Name, ActionKind.Shield, TargetRule.Self, 0);
                int attack = CurrentAttack(actor);
                switch (book.SkillEffect)
                {
                    case WarCry when !actor.DoubleNextAttack && attack > 0:
                        Add(actor, attack);
                        break;
                    case BlazingFire:
                        Add(actor, enemies.Sum(enemy => (long)Estimate(enemy, attack + 1, DamageType.Fire)));
                        break;
                    case AbsoluteZero:
                        Add(actor, enemies.Count(enemy => CanAffect(enemy, Frozen)) * 2);
                        break;
                    case Sunshine:
                        Add(actor, actor.Has(EquipmentEffect.RebelEarring)
                            ? enemies.Sum(enemy => (long)Estimate(enemy, 3, DamageType.Magic))
                            : allies.Sum(ally => (long)HealingGain(ally, 3)));
                        break;
                    case StoneGuard when !actor.StoneGuard:
                        Add(actor, 3);
                        break;
                    case HealingHands:
                        if (actor.Has(EquipmentEffect.RebelEarring))
                            foreach (var target in TargetableOpponents(actor))
                                Add(target, Estimate(target, 10, DamageType.Magic));
                        else
                            foreach (var target in allies)
                                Add(target, HealingGain(target, 10));
                        break;
                    case SweepNextAttack when actor.SweepSkill == null && enemies.Length > 1 && attack > 0:
                        Add(actor, (long)(enemies.Length - 1) * attack);
                        break;
                    case LightningWhip:
                        foreach (var target in TargetableOpponents(actor))
                            Add(target, Estimate(target, 3, DamageType.Lightning) * (long)(enemies.Length + 1));
                        break;
                    case ToxicMist:
                        foreach (var target in TargetableOpponents(actor).Where(enemy => CanAffect(enemy, Poison)))
                            Add(target, attack);
                        break;
                    case FleshFeast:
                        foreach (var corpse in units.Where(unit => unit.Input.Side != actor.Input.Side &&
                            unit.Health == 0 && !unit.CorpseConsumed))
                            Add(corpse, 3);
                        break;
                    case Sidestep when actor.Layers(Purity) == 0:
                        Add(actor, 3);
                        break;
                    case GoldenBell when allies.Any(ally => ally.ProtectedRound != Round):
                        Add(actor, allies.Length * 2);
                        break;
                    case FieryEyes:
                        Add(actor, enemies.Count(enemy => CanAffect(enemy, Exposed)));
                        break;
                    case CrimsonReap:
                        foreach (var target in TargetableOpponents(actor))
                        {
                            int damage = Estimate(target, attack, DamageType.True);
                            Add(target, damage + (long)HealingGain(actor, damage));
                        }
                        break;
                    case HeroicAnthem:
                        Add(actor, allies.Count(ally => ally.Statuses.Keys.Any(status => StatusRules.For(status).Negative)) * 2);
                        break;
                    case Intimidate:
                        Add(actor, enemies.Count(enemy => CanAffect(enemy, Stunned)) * 3);
                        break;
                    case Earthquake:
                        foreach (var target in TargetableOpponents(actor))
                            Add(target, NearbyEnemies(actor, target).Sum(enemy => (long)Estimate(enemy, attack + 3, DamageType.Physical)));
                        break;
                    case CloudPiercer:
                        foreach (var target in TargetableOpponents(actor))
                            Add(target, Estimate(target, ArrowPower(target), DamageType.True));
                        break;
                }

                void Add(Unit target, long projected)
                {
                    if (projected > 0)
                        options.Add(new ActionOption(action, target.Snapshot(), target.Health,
                            (int)Math.Min(int.MaxValue, projected), book: book));
                }

                int Estimate(Unit target, int power, DamageType type) =>
                    Math.Min(target.Health, ProjectedDamage(actor, target, new[] { (type, power) }));
            }
        }

        private static int HealingGain(Unit target, int power) => target.Layers(Cursed) > 0 ? 0 :
            Math.Min(HealingPower(target, power), target.Stats.Health - target.Health);

        private static bool CanAffect(Unit target, StatusKind status) =>
            target.Layers(Purity) == 0 && (status == Poison || target.Layers(status) == 0);

        private static int ArrowPower(Unit target) => Math.Max(1, target.Health / 10);

        private IEnumerable<Unit> NearbyEnemies(Unit actor, Unit target) =>
            Opponents(actor).Where(enemy => Math.Abs(enemy.Input.Position - target.Input.Position) <= 1);

        private void CastLearnedSkill(Unit actor, Unit target, SkillBookDefinition book, DecisionReason reason)
        {
            actor.SpendEnergy(book.Cost);
            actor.LastSkillRound = Round;
            Report(BattleEventKind.Action, actor, actor, book.Name, ActionKind.Shield,
                detail: $"消耗 {book.Cost} 点能量", actorEnergy: actor.Energy, skillBookId: book.Id);

            int attack = CurrentAttack(actor);
            switch (book.SkillEffect)
            {
                case WarCry:
                    actor.DoubleNextAttack = true;
                    Show(actor);
                    break;
                case BlazingFire:
                    foreach (var enemy in Opponents(actor).ToArray())
                        DamageEnemy(enemy, attack + 1, DamageType.Fire);
                    break;
                case AbsoluteZero:
                    foreach (var enemy in Opponents(actor).Where(enemy => CanAffect(enemy, Frozen)).Take(2).ToArray())
                        Apply(enemy, Frozen);
                    break;
                case Sunshine:
                    var targets = actor.Has(EquipmentEffect.RebelEarring) ? Opponents(actor) : Allies();
                    foreach (var unit in targets.ToArray())
                        Restore(unit, 3);
                    break;
                case StoneGuard:
                    actor.StoneGuard = true;
                    Show(actor);
                    break;
                case HealingHands:
                    Restore(target, 10);
                    break;
                case SweepNextAttack:
                    actor.SweepSkill = book;
                    break;
                case LightningWhip:
                    int strikes = 1 + Opponents(actor).Count();
                    for (int i = 0; i < strikes && target.Health > 0 && actor.Health > 0; i++)
                        DamageEnemy(target, 3, DamageType.Lightning);
                    break;
                case ToxicMist:
                    Apply(target, Poison, attack);
                    break;
                case FleshFeast:
                    target.CorpseConsumed = true;
                    actor.BonusMaximumHealth += 3;
                    actor.Health += 3;
                    UpdateEquipment();
                    Show(target);
                    Report(BattleEventKind.Effect, actor, actor, book.Name, ActionKind.Heal, 3,
                        detail: "生命上限 +3", form: actor.Input.Character, skillBookId: book.Id);
                    break;
                case Sidestep:
                    Apply(actor, Swift, 2);
                    Apply(actor, Vigor, 2);
                    break;
                case GoldenBell:
                    foreach (var ally in Allies())
                    {
                        ally.ProtectedRound = Round;
                        Show(ally);
                        ReportCountdown(ally, BattleCountdownKind.GoldenBell, 1);
                    }
                    break;
                case FieryEyes:
                    foreach (var enemy in Opponents(actor))
                        Apply(enemy, Exposed);
                    break;
                case CrimsonReap:
                    int dealt = DamageEnemy(target, attack, DamageType.True);
                    if (actor.Health > 0 && dealt > 0)
                        Heal(actor, actor, dealt, book.Name, BattleEventKind.Effect, hostileTarget: target, skillBookId: book.Id);
                    break;
                case HeroicAnthem:
                    foreach (var ally in Allies())
                        Apply(ally, Purity);
                    break;
                case Intimidate:
                    foreach (var enemy in Opponents(actor))
                        Apply(enemy, Stunned);
                    break;
                case Earthquake:
                    foreach (var enemy in NearbyEnemies(actor, target).ToArray())
                        DamageEnemy(enemy, attack + 3, DamageType.Physical);
                    break;
                case CloudPiercer:
                    DamageEnemy(target, ArrowPower(target), DamageType.True);
                    break;
            }

            IEnumerable<Unit> Allies() => units.Where(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0);

            void Show(Unit unit) => events.Add(new BattleEvent(Tick, BattleEventKind.SkillVisual,
                actor.Input.Id, unit.Input.Id, actionKind: ActionKind.Shield, skillBookId: book.Id));

            void Apply(Unit unit, StatusKind status, int amount = 1) =>
                GrantStatus(actor, unit, status, amount, book.Name, kind: BattleEventKind.Effect, skillBookId: book.Id);

            int DamageEnemy(Unit enemy, int power, DamageType type) => enemy.Health > 0
                ? ResolveStrike(actor, enemy, new[] { (type, power) }, book.Name, BattleEventKind.Effect, reason,
                    skillBookId: book.Id).Total : 0;

            void Restore(Unit unit, int power) => Heal(actor, unit, power, book.Name, BattleEventKind.Effect,
                hostileTarget: unit.Input.Side != actor.Input.Side ? unit : null, skillBookId: book.Id);
        }
    }
}
