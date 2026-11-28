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
        private const double ToxicMistDollChance = .30;

        private bool TryCastAstralSpell(Unit actor)
        {
            var options = new List<ActionOption>();
            AddSkillOptions(actor, options, actor.TraitSource.InnateSpells, astralCast: true);
            var spells = options.GroupBy(option => option.Book).ToArray();
            if (spells.Length == 0) return false;

            // Pick the spell first so single-target spells do not get extra rolls for each enemy.
            var cast = spells[random.Next(spells.Length)].OrderByDescending(option => option.ProjectedAmount).First();
            CastLearnedSkill(actor, unitsById[cast.Target.Id], cast.Book, DecisionReason.UseSkill, cast.EnergyCost);
            return true;
        }

        private void AddSkillOptions(Unit actor, List<ActionOption> options, IReadOnlyList<SkillBookDefinition> books,
            bool astralCast = false)
        {
            var enemies = Opponents(actor).ToArray();
            var direct = enemies.Where(enemy => enemy.CanBeTargeted).ToArray();
            var allies = units.Where(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0).ToArray();

            int reachableEnergy = actor.Energy.Remaining + Math.Max(0, actor.ChargeTarget - actor.Energy.Capacity);
            int reserveCost = 0;
            long reserveValue = 0;
            foreach (var book in books)
            {
                if (!CanAttackThisRound(actor) && !CanCastWithoutAttacking(actor, book.SkillEffect)) continue;
                int required = astralCast ? 1 : book.Cost;
                bool charging = required > actor.Energy.Remaining;
                if (required > reachableEnergy) continue;
                var action = new ActionDefinition(book.Id, book.Name, ActionKind.Shield, TargetRule.Self, 0);
                int attack = CurrentAttack(actor);
                switch (book.SkillEffect)
                {
                    case ExpressClearance:
                        Add(actor, direct.Take(2).Sum(e => (long)Estimate(e, attack + 4, DamageType.Physical) + 2));
                        break;
                    case MeridianTransit:
                        Add(actor, enemies.Sum(e => (long)Estimate(e, attack + 2, DamageType.Magic)));
                        break;
                    case AnnealingOrder:
                        Add(actor, allies.Sum(e => (long)HealingGain(e, 4) + 6));
                        break;
                    case BlockadeSalvo:
                        Add(actor, direct.Reverse().Take(2).Sum(e => (long)Estimate(e, attack + 3, DamageType.Physical) + 1));
                        break;
                    case DeepwaterSurge:
                        foreach (var foe in TargetableOpponents(actor))
                            Add(foe, Estimate(foe, attack + 3, DamageType.Magic) + 3L);
                        break;
                    case BurialEdict when direct.FirstOrDefault() is { } front:
                        Add(front, Estimate(front, 2 * attack + 2, DamageType.Physical) + 2L * enemies.Length);
                        break;
                    case LongNightPrayer:
                        Add(actor, allies.Sum(ally => 5L + HealingGain(ally, 5) +
                            ally.Statuses.Keys.Count(status => StatusRules.For(status).Negative)));
                        break;
                    case HookArrow when direct.LastOrDefault() is { } rear:
                        Add(rear, Estimate(rear, attack + 3, DamageType.Physical) + 1); break;
                    case TwinCover: Add(actor, Behind(actor) != null ? 8 : 4); break;
                    case FieldMedic:
                        foreach (var ally in allies) Add(ally, (ally.Trait == CharacterTrait.AbyssWard ? 0 : Math.Min(8, ally.Stats.Health - ally.Health)) +
                            4 * (ally.Layers(Poison) + ally.Layers(Cursed))); break;
                    case ColdTide: Add(actor, enemies.Sum(e => (long)Estimate(e, attack + 1, DamageType.Magic) + 2)); break;
                    case BronzeMirror when actor.MirrorShield == 0: Add(actor, 20); break;
                    case NightWatch: Add(actor, allies.Sum(a => (long)HealingGain(a, 7))); break;
                    case SiegeBolt:
                        foreach (var foe in direct) Add(foe, foe.Shield + Estimate(foe, 2 * attack + 4, DamageType.Physical) +
                            NearbyEnemies(actor, foe).Where(e => e != foe).Sum(e => (long)Estimate(e, attack + 2, DamageType.Physical))); break;
                    case Intercept when allies.Length > 1 && (actor.Intercepts == 0 || actor.InterceptUntil < Round): Add(actor, 15); break;
                    case BackPay:
                        foreach (var dead in units.Where(u => u.Input.Side == actor.Input.Side && u.Health == 0 && !u.CorpseConsumed && u.Trait != CharacterTrait.AbyssWard))
                            Add(dead, dead.Stats.Health + 5); break;
                    case ThunderVerdict when actor.ThunderRound <= Round:
                        Add(actor, enemies.Sum(e => (long)Estimate(e, 2 * attack + 6, DamageType.Lightning))); break;
                    case WarCry when !actor.DoubleNextAttack && attack > 0:
                        Add(actor, AttackTargets(actor, actor.Input.Character.BasicAttack).Select(target =>
                        {
                            var parts = AttackComponents(actor, target, actor.Input.Character.BasicAttack);
                            bool ignoresDefense = UsesShortbow(actor);
                            int normal = ProjectedDamage(actor, target, parts, ignoresDefense, weaponAttack: true);
                            int doubled = ProjectedDamage(actor, target, parts, ignoresDefense, weaponAttack: true, multiplier: 2);
                            return Math.Min(target.Health, doubled) - Math.Min(target.Health, normal);
                        }).DefaultIfEmpty(0).Max());
                        break;
                    case BlazingFire:
                        Add(actor, enemies.Sum(enemy => (long)Estimate(enemy, attack + 1, DamageType.Fire)));
                        break;
                    case AbsoluteZero:
                        Add(actor, direct.Count(enemy => CanAffect(enemy, Frozen)) * 2);
                        break;
                    case Sunshine:
                        Add(actor, actor.Has(EquipmentEffect.RebelEarring)
                            ? enemies.Sum(enemy => (long)Estimate(enemy, 3, DamageType.Magic))
                            : allies.Sum(ally => HealingValue(ally, 3)));
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
                                Add(target, HealingValue(target, 10));
                        break;
                    case SweepNextAttack when actor.SweepSkill == null && (enemies.Length > 1 || astralCast) && attack > 0:
                        Add(actor, (long)(enemies.Length - 1) * attack);
                        break;
                    case LightningWhip:
                        foreach (var target in TargetableOpponents(actor))
                            Add(target, Math.Min(target.Health, Estimate(target, 3, DamageType.Lightning) * (long)(enemies.Length + 1)));
                        break;
                    case ToxicMist:
                        var poisonTargets = TargetableOpponents(actor).Where(enemy => CanAffect(enemy, Poison)).ToArray();
                        foreach (var target in poisonTargets)
                            Add(target, PoisonValue(target, attack));
                        if (!charging && attack > 0 && !poisonTargets.Any(target => PoisonValue(target, attack) > 0) &&
                            ToxicMistDollTarget(actor) is { } doll && random.NextDouble() < ToxicMistDollChance)
                            Add(doll, HealingValue(doll, attack));
                        break;
                    case VenomSurge when !charging:
                        int enemyPoison = 3 * book.EnergyCost(actor.Energy.Remaining);
                        Add(actor, enemies.Where(enemy => CanAffect(enemy, Poison))
                            .Sum(enemy => PoisonValue(enemy, enemyPoison)) -
                            (actor.Has(EquipmentEffect.WitchDoctorDoll) ? 0 : book.EnergyCost(actor.Energy.Remaining)));
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
                    if (projected <= 0 && !astralCast) return;
                    if (charging)
                    {
                        if (projected > reserveValue || projected == reserveValue && book.Cost < reserveCost)
                            (reserveValue, reserveCost) = (projected, book.Cost);
                        return;
                    }
                    options.Add(new ActionOption(action, target.Snapshot(), target.Health,
                        (int)Math.Min(int.MaxValue, projected), book: book,
                        lastCastRound: actor.LastBookCast.GetValueOrDefault(book.Id),
                        energyCost: astralCast ? 1 : book.EnergyCost(actor.Energy.Remaining)));
                }

                int Estimate(Unit target, int power, DamageType type) =>
                    Math.Min(target.Health, ProjectedDamage(actor, target, new[] { (type, power) }));
            }
            if (reserveCost > 0)
                options.RemoveAll(option => option.Book.EnergyCost(actor.Energy.Remaining) + reserveCost > reachableEnergy &&
                    reserveValue > option.ProjectedAmount);
        }

        private static bool CanCastWithoutAttacking(Unit actor, SkillEffect effect) => effect switch
        {
            WarCry or StoneGuard or SweepNextAttack or Sidestep or GoldenBell or HeroicAnthem or
                TwinCover or BronzeMirror or Intercept or BackPay => true,
            Sunshine or HealingHands or FieldMedic or NightWatch or LongNightPrayer or AnnealingOrder or FleshFeast
                => !actor.Has(EquipmentEffect.RebelEarring),
            _ => false
        };

        private static int HealingGain(Unit target, int power) => !target.CanReceiveHealing ? 0 :
            Math.Min(HealingPower(target, power), target.Stats.Health - target.Health);

        private static long HealingValue(Unit target, int power) =>
            HealingGain(target, power) * (4L * target.Health <= target.Stats.Health ? 2L : 1L);

        private static bool CanAffect(Unit target, StatusKind status) =>
            target.Layers(Purity) == 0 && (status == Poison || target.Layers(status) == 0);

        private static long PoisonValue(Unit target, int layers)
        {
            // Estimate the extra damage over the next two ticks after accounting for existing stacks.
            long existing = target.Layers(Poison);
            long total = existing + layers;
            return Math.Min(target.Health, total + Math.Max(0, total - 1))
                - Math.Min(target.Health, existing + Math.Max(0, existing - 1));
        }

        private Unit ToxicMistDollTarget(Unit actor) => Teammates(actor)
            .Where(ally => ally.Has(EquipmentEffect.WitchDoctorDoll) && CanAffect(ally, Poison) && ally.CanReceiveHealing)
            .OrderByDescending(ally => ally.Stats.Health - ally.Health).FirstOrDefault();

        private static int ArrowPower(Unit target) => Math.Max(1, target.Health / 5);

        private IEnumerable<Unit> NearbyEnemies(Unit actor, Unit target) =>
            Opponents(actor).Where(enemy => Math.Abs(enemy.Input.Position - target.Input.Position) <= 1);

        private void CastLearnedSkill(Unit actor, Unit target, SkillBookDefinition book, DecisionReason reason, int spent)
        {
            actor.SpendEnergy(spent);
            actor.LastSkillRound = Round;
            actor.LastBookCast[book.Id] = Round;
            Report(BattleEventKind.Action, actor, actor, book.Name, ActionKind.Shield,
                detail: $"消耗 {spent} 点能量", actorEnergy: actor.Energy, skillBookId: book.Id);

            RefundHardFight(actor);
            int attack = CurrentAttack(actor);
            switch (book.SkillEffect)
            {
                case ExpressClearance:
                    foreach (var enemy in DirectOpponents(actor).OrderBy(e => e.Input.Position).Take(2).ToArray())
                    {
                        DamageEnemy(enemy, attack + 4, DamageType.Physical);
                        if (enemy.Health > 0) Apply(enemy, Exposed, 2);
                    }
                    break;
                case MeridianTransit:
                    foreach (var enemy in Opponents(actor).ToArray()) DamageEnemy(enemy, attack + 2, DamageType.Magic);
                    break;
                case AnnealingOrder:
                    foreach (var ally in Allies().ToArray())
                    {
                        Restore(ally, 4);
                        if (ally.Health > 0) ShieldAlly(actor, ally, 6, book.Name, book.Id);
                    }
                    break;
                case BlockadeSalvo:
                    foreach (var enemy in DirectOpponents(actor).Reverse().Take(2).ToArray())
                    {
                        DamageEnemy(enemy, attack + 3, DamageType.Physical);
                        if (enemy.Health > 0) Apply(enemy, Exposed);
                    }
                    break;
                case DeepwaterSurge:
                    DamageEnemy(target, attack + 3, DamageType.Magic);
                    foreach (var enemy in Opponents(actor).ToArray()) Apply(enemy, Chill, enemy == target ? 3 : 1);
                    break;
                case BurialEdict:
                    DamageEnemy(target, 2 * attack + 2, DamageType.Physical);
                    foreach (var enemy in Opponents(actor).ToArray()) Apply(enemy, Cursed, 2);
                    break;
                case LongNightPrayer:
                    foreach (var ally in Allies().ToArray())
                    {
                        foreach (var status in ally.Statuses.Keys.Where(status => StatusRules.For(status).Negative).ToArray())
                            RemoveLayers(ally, status, int.MaxValue, actor);
                        Show(ally);
                        Restore(ally, 5);
                        if (ally.Health > 0) ShieldAlly(actor, ally, 5, book.Name, book.Id);
                    }
                    break;
                case HookArrow:
                    DamageEnemy(target, attack + 3, DamageType.Physical);
                    if (target.Health > 0) Apply(target, Exposed);
                    break;
                case TwinCover:
                    ShieldAlly(actor, actor, 4, book.Name, book.Id);
                    var behind = Behind(actor);
                    if (behind != null) ShieldAlly(actor, behind, behind.Health * 2 <= behind.Stats.Health ? 7 : 4, book.Name, book.Id);
                    break;
                case FieldMedic:
                    int cleansed = (RemoveLayers(target, Poison, int.MaxValue, actor) ? 1 : 0) +
                        (RemoveLayers(target, Cursed, int.MaxValue, actor) ? 1 : 0);
                    Restore(target, 8 + 2 * cleansed); break;
                case ColdTide:
                    foreach (var enemy in Opponents(actor).ToArray())
                    {
                        DamageEnemy(enemy, attack + 1, DamageType.Magic);
                        if (enemy.Health > 0) Apply(enemy, Chill, 2);
                    }
                    break;
                case BronzeMirror:
                    actor.MirrorShield += ShieldAlly(actor, actor, 20, book.Name, book.Id); break;
                case NightWatch:
                    foreach (var ally in Allies().ToArray()) Restore(ally, 3);
                    nightWatches.Add((actor, Round + 2)); break;
                case SiegeBolt:
                    var neighbors = NearbyEnemies(actor, target).Where(e => e != target).ToArray();
                    target.Shield = target.MirrorShield = 0;
                    target.ShieldSources.Clear();
                    target.ReliquaryWard = false;
                    DamageEnemy(target, 2 * attack + 4, DamageType.Physical);
                    foreach (var enemy in neighbors) DamageEnemy(enemy, attack + 2, DamageType.Physical);
                    break;
                case Intercept:
                    actor.Intercepts = 3; actor.InterceptUntil = Round + 1; Show(actor); break;
                case BackPay:
                    actor.Rating.Saves++;
                    target.Health = Math.Max(1, (int)Math.Ceiling(target.Stats.Health * .4));
                    target.SpendEnergy(target.Energy.Remaining);
                    target.BlockedRound = Round;
                    UpdateEquipment();
                    Report(BattleEventKind.Effect, actor, target, book.Name, ActionKind.Heal, target.Health,
                        form: target.Input.Character, targetEnergy: target.Energy, skillBookId: book.Id);
                    ShieldAlly(actor, target, 5, book.Name, book.Id);
                    break;
                case ThunderVerdict: actor.ThunderRound = Round + 1; Show(actor); break;
                case WarCry:
                    actor.DoubleNextAttack = true;
                    Show(actor);
                    break;
                case BlazingFire:
                    foreach (var enemy in Opponents(actor).ToArray())
                        DamageEnemy(enemy, attack + 1, DamageType.Fire);
                    break;
                case AbsoluteZero:
                    foreach (var enemy in DirectOpponents(actor).Where(enemy => CanAffect(enemy, Frozen)).Take(2).ToArray())
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
                    ReportCountdown(actor, BattleCountdownKind.StoneGuard, 1);
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
                    if (target.Input.Side != actor.Input.Side && ToxicMistDollTarget(actor) is { } doll &&
                        random.NextDouble() < ToxicMistDollChance)
                        target = doll;
                    Apply(target, Poison, attack);
                    break;
                case VenomSurge:
                    foreach (var enemy in Opponents(actor).ToArray())
                        Apply(enemy, Poison, 3 * spent);
                    Apply(actor, Poison, spent);
                    break;
                case FleshFeast:
                    target.CorpseConsumed = true;
                    actor.BonusMaximumHealth++;
                    UpdateEquipment();
                    Show(target);
                    Report(BattleEventKind.Effect, actor, actor, book.Name, ActionKind.Shield,
                        detail: "生命上限永久 +1", form: actor.Input.Character, skillBookId: book.Id);
                    Restore(actor, 3);
                    break;
                case Sidestep:
                    Apply(actor, Swift, 2);
                    Apply(actor, Vigor, 2);
                    break;
                case GoldenBell:
                    foreach (var ally in Allies())
                    {
                        if (ally.ProtectedRound != Round) ally.BellCaster = actor;
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
