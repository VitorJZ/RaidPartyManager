using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.EquipmentEffect;
using static RaidPartyManager.Domain.StatusKind;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private static readonly Dictionary<EquipmentEffect, (StatusKind Status, int Layers)> Potions =
            new Dictionary<EquipmentEffect, (StatusKind, int)>
            {
                { VigorPotion, (Vigor, 3) }, { SwiftPotion, (Swift, 3) }, { ResolvePotion, (Resolve, 3) },
                { GreedPotion, (Greed, 1) }, { ThornsPotion, (Thorns, 2) }, { PurityPotion, (Purity, 2) }
            };

        private static readonly StatusKind[] DecayingStatuses =
        {
            Vigor, Swift, Resolve, Purity, Poison, Weakened, Thorns, Stunned, Cursed
        };

        private static string StatusText(Unit unit) => string.Join("  ", unit.Statuses
            .OrderBy(status => status.Key).Select(status => $"{StatusRules.For(status.Key).Name} {status.Value}"));

        private void StartBattle()
        {
            SetOpeningEnemyShields();
            SetConfiguredEnemyOpeningShields();
            foreach (var actor in units.Where(unit => unit.Health > 0).ToArray())
                ResolveOpeningGear(actor, canSummon: true);
            foreach (var recruit in recruits)
                ResolveOpeningGear(recruit, canSummon: false);
            RemoveOpeningPotions();
            ApplyBattleStartTraits();
        }

        private void ResolveOpeningGear(Unit actor, bool canSummon)
        {
            int uses = 1 + actor.Count(MurkyWater);
            foreach (var item in actor.Gear)
            {
                if (canSummon && item.Effect == RecruitmentNotice)
                    Summon(actor);
                else if (Potions.TryGetValue(item.Effect, out var potion))
                    for (int use = 0; use < uses; use++)
                        GrantStatus(actor, actor, potion.Status, potion.Layers);
            }

            if (actor.Statuses.Count > 0)
                Report(BattleEventKind.Effect, actor, actor, "开战状态", ActionKind.Shield, detail: StatusText(actor));
        }

        private void RemoveOpeningPotions()
        {
            var spent = new List<(Unit Unit, EquippedItem[] Potions)>();
            foreach (var unit in units.Where(unit => unit.Health > 0))
            {
                int protections = unit.Count(PanaceaBottle);
                var used = unit.Equipped.Where(item => item.Definition.IsPotion).ToArray();
                var consumed = used.Where(item => !Enumerable.Range(0, protections).Any(attempt => random.NextDouble() < 0.5))
                    .ToArray();
                int saved = used.Length - consumed.Length;
                if (saved > 0)
                    Report(BattleEventKind.Effect, unit, unit, "万能药瓶", ActionKind.Shield, detail: $"保留 {saved} 瓶药水");
                if (consumed.Length == 0)
                    continue;

                for (int slot = 0; slot < unit.Slots.Length; slot++)
                    if (consumed.Contains(unit.Slots[slot]))
                        unit.Slots[slot] = null;
                unit.ApplySlotChanges();
                spent.Add((unit, consumed));
                foreach (var item in consumed)
                    if (unit.Input.Side == Side.Party && item.Id != null)
                        consumedEquipment.Add(item.Id);
            }

            if (spent.Count == 0)
                return;
            UpdateFormation();
            if (consumedEquipment.Count > 0)
                OpeningLoadout = currentLoadout = CaptureLoadout();
            foreach (var (unit, used) in spent)
                Report(BattleEventKind.Effect, unit, unit, "消耗药水", ActionKind.Shield,
                    detail: string.Join("、", used.Select(item => item.Definition.Name)),
                    form: unit.Stats.Health != unit.Original.Stats.Health ? unit.Input.Character : null);
        }

        private void Summon(Unit actor)
        {
            if (request.RecruitmentPool.Count == 0 || actor.Input.Side != Side.Party)
                return;
            int slot = Enumerable.Range(1, Rules.Positions)
                .FirstOrDefault(position => units.All(unit => unit.Input.Side != Side.Party || unit.Input.Position != position));
            if (slot == 0)
                return;

            string id = $"recruit-{request.Id}-{recruits.Count + 1}";
            while (unitsById.ContainsKey(id))
                id += "-ally";
            var character = request.RecruitmentPool[random.Next(request.RecruitmentPool.Count)];
            var recruit = new Unit(new Combatant(id, character, Side.Party, slot, character.BaseStats, character.BaseStats.Health));
            units.Add(recruit);
            recruits.Add(recruit);
            unitsById.Add(id, recruit);
            UpdateFormation();
            Report(BattleEventKind.Effect, actor, recruit, "寻人启事", ActionKind.Heal, detail: $"加入 {slot} 号位", form: character);
        }

        private void StartRound()
        {
            foreach (var actor in units.Where(unit => unit.Health > 0))
                foreach (var item in actor.Gear)
                    if (item.Effect == InsightTotem)
                        GrantStatus(actor, TargetableOpponents(actor).First(), Exposed, 1,
                            "洞察图腾", BattleVisualCue.RoundStartExposed, BattleEventKind.Effect);
            ApplyMegaphoneBonus();
        }

        private void EndRound()
        {
            foreach (var unit in units.ToArray())
            {
                for (int repeat = 0; unit.Health > 0 && repeat <= unit.Count(RitualMask); repeat++)
                {
                    foreach (var item in unit.Gear)
                    {
                        if (unit.Health == 0)
                            break;
                        if (item.Effect == LifePendant)
                            Heal(unit, unit, 3, "生命吊坠", BattleEventKind.Effect, visual: BattleVisualCue.RoundEndHeal);
                        else if (item.Effect == FirstFire)
                            GrantStatus(unit, unit, Ember, 1, "初始之火", BattleVisualCue.RoundEndEmber, BattleEventKind.Effect);
                    }

                    if (unit.Health == 0)
                        break;
                    int poison = unit.Layers(Poison);
                    if (poison > 0)
                    {
                        if (unit.Has(WitchDoctorDoll))
                            Heal(unit, unit, poison, "巫医玩偶", BattleEventKind.Effect, visual: BattleVisualCue.RoundEndPoisonHeal);
                        else
                            Strike(null, unit, poison, DamageType.True, "中毒", BattleEventKind.Effect, visual: BattleVisualCue.RoundEndPoison);
                    }
                    if (!Opponents(unit).Any() || !units.Any(ally => ally.Input.Side == unit.Input.Side && ally.Health > 0))
                        return;
                }
            }

            ApplyRoundEndTraits();
            PassMegaphones();
            ResolveScavengerEscapes();
            foreach (var unit in units.Where(unit => unit.Health > 0))
            {
                bool changed = false;
                foreach (var status in DecayingStatuses)
                    if (unit.DelayStatusDecay?.Remove(status) != true)
                        changed |= RemoveLayers(unit, status, 1);
                if (changed)
                    Report(BattleEventKind.Effect, unit, unit, "回合状态递减", ActionKind.Shield, detail: "状态层数 −1");
            }
        }

        private void GrantStatus(Unit actor, Unit target, StatusKind status, int layers,
            string actionName = null, BattleVisualCue visual = BattleVisualCue.None, BattleEventKind? kind = null,
            string skillBookId = null, bool delayDecay = false)
        {
            var eventKind = kind ?? (actionName == null ? BattleEventKind.Effect : BattleEventKind.Action);
            if (target.Health == 0 || (status != Purity && target.Layers(Purity) > 0) ||
                (status == Chill && target.Layers(Frozen) > 0))
            {
                if (actionName != null)
                    Report(eventKind, actor, target, actionName, ActionKind.Shield, detail: "状态未生效", visual: visual);
                return;
            }

            int bonus = status == Poison ? 2 * (actor?.Count(PoisonRecipe) ?? 0) : 0;
            int previous = target.Layers(status);
            int total = previous + layers + bonus + (status == Greed ? 0 : target.Layers(Greed));
            if (status == Purity)
            {
                target.Statuses.Clear();
                target.DelayStatusDecay?.Clear();
            }
            if (status == Chill && total >= 3)
            {
                target.Statuses.Remove(Chill);
                target.Statuses[Frozen] = 1;
                Report(eventKind, actor, target, "冰冻", ActionKind.Shield, detail: "寒冷达到 3 层",
                    visual: visual, skillBookId: skillBookId);
                return;
            }

            target.Statuses[status] = total;
            if (delayDecay && previous == 0)
                (target.DelayStatusDecay ??= new HashSet<StatusKind>()).Add(status);
            var statusName = StatusRules.For(status).Name;
            Report(eventKind, actor, target, actionName ?? statusName, ActionKind.Shield,
                detail: $"{(actionName == null ? "" : statusName + " ")}达到 {total} 层", visual: visual,
                skillBookId: skillBookId);
        }

        private static bool RemoveLayers(Unit unit, StatusKind status, int amount)
        {
            int prior = unit.Layers(status);
            if (prior == 0)
                return false;
            if (prior <= amount)
            {
                unit.Statuses.Remove(status);
                unit.DelayStatusDecay?.Remove(status);
            }
            else
                unit.Statuses[status] = prior - amount;
            return true;
        }

        private static int CurrentAttack(Unit actor) =>
            EffectiveAttack(actor.Stats.Attack, actor.Layers(Weakened) > 0);

        private static int EffectiveAttack(int attack, bool weakened) => Math.Max(0, attack - (weakened ? 2 : 0));

        private static int BaseHealing(Unit actor, ActionDefinition action) =>
            action.HealingScalesWithAttack ? CurrentAttack(actor) : action.Power;

        private List<(DamageType Type, int Power)> AttackComponents(Unit actor, Unit target, ActionDefinition action,
            int falloff = 0)
        {
            var components = new List<(DamageType Type, int Power)>();
            if (actor.Has(ExecutionMask) && actor.LastAttackRound != Round)
            {
                components.Add((DamageType.True, Math.Max(1, target.Stats.Health / 10)));
                return components;
            }

            int attack = CurrentAttack(actor) - falloff;
            if (actor.Trait == CharacterTrait.RepeatTargetDamage && actor.LastHitTarget == target.Input.Id)
                attack += actor.TraitPower;
            if (actor.Trait == CharacterTrait.FirstDamageToTarget && !actor.DamagedTargets.Contains(target.Input.Id))
                attack += actor.TraitPower;
            if (actor.Trait == CharacterTrait.MissingHealthDamage)
                attack += actor.Stats.Health - actor.Health;
            int eagleEyes = actor.Count(EagleEye);
            if (eagleEyes > 0)
            {
                int between = units.Count(unit => unit.Health > 0 &&
                    ((unit.Input.Side == actor.Input.Side && unit.Input.Position < actor.Input.Position) ||
                     (unit.Input.Side == target.Input.Side && unit.Input.Position < target.Input.Position)));
                attack += 2 * eagleEyes * between;
            }

            DamageType fallback = actor.Input.Character.Profession == Profession.Mage ? DamageType.Magic : DamageType.Physical;
            DamageType? element = null;
            foreach (var item in actor.Gear)
            {
                if (item.Effect == FireRing || item.Effect == LightningRing)
                {
                    element = item.Effect == FireRing ? DamageType.Fire : DamageType.Lightning;
                    break;
                }
            }

            DamageType ResolveType(DamageType type) => element ?? (type == DamageType.Default ? fallback : type);
            components.Add((ResolveType(action.DamageType), attack + action.Power));
            foreach (var part in action.AdditionalDamage)
                components.Add((ResolveType(part.Type), part.Power + (part.ScalesWithAttack ? attack : 0)));
            if (actor.Trait == CharacterTrait.EnemyFogSignalGunner && target.Layers(Chill) > 0)
                components.Add((DamageType.Fire, 2));

            int ember = actor.Layers(Ember);
            if (ember > 0)
                components.Add((DamageType.Fire, 3 * ember));
            int bagua = actor.Input.Position % 2 == 0 ? actor.Count(Bagua) : 0;
            if (bagua > 0)
                components.Add((DamageType.Fire, 3 * bagua));
            int sparks = actor.Count(StaticPrism) * (actor.Statuses.Values.Sum() + target.Statuses.Values.Sum());
            for (int spark = 0; spark < sparks; spark++)
                components.Add((DamageType.Lightning, 1));
            return components;
        }

        private void Attack(Unit actor, Unit target, ActionDefinition action, DecisionReason reason)
        {
            if (!actor.HasAttacked)
            {
                actor.HasAttacked = true;
                foreach (var item in actor.Gear)
                    if (item.Effect == Bludgeon)
                        GrantStatus(actor, target, Injured, 1);
            }

            if (actor.SweepSkill is { } sweep)
            {
                int multiplier = actor.DoubleNextAttack ? 2 : 1;
                actor.DoubleNextAttack = false;
                actor.SweepSkill = null;
                var sweepKind = BattleEventKind.Action;
                // All sweep hits belong to one release. Resolve retaliation and bonus attacks afterward.
                var reactions = new List<Action>();
                foreach (var enemy in Opponents(actor).ToArray())
                {
                    if (actor.Health == 0)
                        break;
                    if (enemy.Health == 0)
                        continue;
                    Hit(actor, enemy, action, sweepKind, reason, sweep.Name, multiplier: multiplier,
                        firstTarget: sweepKind == BattleEventKind.Action, skillBookId: sweep.Id, reactions: reactions);
                    sweepKind = BattleEventKind.Effect;
                }
                foreach (var reaction in reactions)
                    reaction();
                return;
            }

            if (actor.Trait == CharacterTrait.PiercingArrow && !UsesShortbow(actor))
            {
                var forward = units.Where(unit => unit.Health > 0 && unit.Input.Side == actor.Input.Side &&
                        unit.Input.Position < actor.Input.Position).Reverse().Concat(Opponents(actor)).ToArray();
                int falloff = 0;
                var kind = BattleEventKind.Action;
                // The arrow is already in flight when these hits trigger counterattacks or bonus shots.
                var reactions = new List<Action>();
                foreach (var unit in forward)
                {
                    if (actor.Health == 0)
                        break;
                    if (unit.Health == 0)
                        continue;
                    if (Hit(actor, unit, action, kind, reason, falloff: falloff, visual: BattleVisualCue.PiercingArrow,
                        firstTarget: kind == BattleEventKind.Action, reactions: reactions))
                        falloff++;
                    kind = BattleEventKind.Effect;
                }
                foreach (var reaction in reactions)
                    reaction();
                return;
            }

            Hit(actor, target, action, BattleEventKind.Action, reason);
            if (actor.Trait == CharacterTrait.ArcherVolley)
            {
                int shots = units.Count(unit => unit != actor && unit.Health > 0 &&
                    unit.Input.Side == actor.Input.Side && unit.Input.Character.Profession == Profession.Archer);
                for (int shot = 0; shot < shots && actor.Health > 0; shot++)
                {
                    var visible = TargetableOpponents(actor);
                    var next = visible.Contains(target) ? target : visible.FirstOrDefault();
                    if (next == null)
                        break;
                    Hit(actor, next, action, BattleEventKind.Effect, reason, "逐星连射");
                }
            }
        }

        private bool Hit(Unit actor, Unit target, ActionDefinition action, BattleEventKind kind,
            DecisionReason reason, string equipmentName = null, int falloff = 0, bool canCounter = true,
            BattleVisualCue visual = BattleVisualCue.None, int multiplier = 1, bool firstTarget = true, string skillBookId = null,
            List<Action> reactions = null)
        {
            string name = equipmentName ?? action.Name;
            if (actor.DoubleNextAttack)
            {
                actor.DoubleNextAttack = false;
                multiplier *= 2;
            }
            bool missed = (actor.Layers(Injured) > 0 && random.NextDouble() < 0.20) ||
                (target.Layers(Swift) > 0 && random.NextDouble() < 0.20);
            bool execution = actor.Has(ExecutionMask) && actor.LastAttackRound != Round;
            var parts = AttackComponents(actor, target, action, falloff);
            actor.LastAttackRound = Round;
            if (firstTarget && actor.Input.Side == Side.Party)
            {
                int gain = Math.Min(actor.Count(MoneyTreeBranch), int.MaxValue - request.PartyGold - equipmentGold);
                if (gain > 0)
                {
                    equipmentGold += gain;
                    Report(BattleEventKind.Effect, actor, actor, "摇钱树枝", ActionKind.Shield, detail: $"金币 +{gain}");
                }
            }
            int physicalDealt = 0;
            if (missed)
                Report(kind, actor, target, name, ActionKind.Attack, reason: reason, detail: "未命中", weaponAttack: true,
                    visual: visual, skillBookId: skillBookId);
            else
            {
                int thorns = target.Layers(Thorns);
                int ember = actor.Layers(Ember);
                var damage = ResolveStrike(actor, target, parts, name, kind, reason,
                    ignoreDefense: UsesShortbow(actor), weaponAttack: true, visual: visual, multiplier: multiplier,
                    skillBookId: skillBookId);
                actor.LastHitTarget = target.Input.Id;
                if (damage.Total > 0 && actor.Trait == CharacterTrait.PoisonOnHit)
                    GrantStatus(actor, target, Poison, actor.TraitPower);
                physicalDealt = damage.Physical;
                if (thorns > 0 && damage.Total >= 2)
                    React(() =>
                    {
                        if (actor.Health > 0)
                            Strike(target, actor, damage.Total / 2, DamageType.Physical, "荆棘", BattleEventKind.Effect);
                    });

                if (actor.Health > 0 && ember > 0 && !execution && !actor.Has(EternalFire))
                {
                    RemoveLayers(actor, Ember, ember);
                    Report(BattleEventKind.Effect, actor, actor, "余烬", ActionKind.Shield, detail: "消耗余烬");
                }
            }

            if (!missed && canCounter)
                React(() => CounterAttack(target, actor));

            foreach (var item in actor.Gear)
            {
                if (actor.Health == 0)
                    break;

                switch (item.Effect)
                {
                    case RubyPendant when physicalDealt >= 2:
                        Heal(actor, actor, physicalDealt / 2, "红水晶吊坠", BattleEventKind.Effect, hostileTarget: target);
                        break;
                    case IceRing when !missed:
                    case Bagua when !missed && actor.Input.Position % 2 != 0:
                        GrantStatus(actor, target, Chill, 1);
                        break;
                    case WindGlove when kind == BattleEventKind.Action:
                        React(() =>
                        {
                            if (actor.Health == 0 || random.NextDouble() >= 0.30)
                                return;
                            var targets = TargetableOpponents(actor);
                            var next = targets.Contains(target) ? target :
                                action.Target == TargetRule.RearEnemy ? targets.LastOrDefault() : targets.FirstOrDefault();
                            if (next != null)
                                Hit(actor, next, action, BattleEventKind.Effect, reason, "疾风手套", falloff, canCounter);
                        });
                        break;
                }
            }
            return !missed;

            void React(Action reaction)
            {
                if (reactions == null) reaction();
                else reactions.Add(reaction);
            }
        }

        private void CounterAttack(Unit defender, Unit attacker)
        {
            if (defender.LastSkillRound == Round)
                return;
            int counters = defender.Count(CounterHorn);
            for (int count = 0; count < counters && defender.Health > 0 && attacker.Health > 0; count++)
                Hit(defender, attacker, defender.Input.Character.BasicAttack, BattleEventKind.Effect,
                    DecisionReason.Default, "反击的号角", canCounter: false);
        }

        private static DamageType FinalDamageType(Unit actor, DamageType type)
        {
            if (type != DamageType.Fire && type != DamageType.Lightning || actor == null || actor.Count(ElementalQuartz) % 2 == 0)
                return type;
            return type == DamageType.Fire ? DamageType.Lightning : DamageType.Fire;
        }

        private int Damage(Unit actor, Unit target, int power, DamageType type, bool ignoreDefense)
        {
            if (type != DamageType.Physical && target.Has(InsulatingOrb))
                return 0;
            if (type == DamageType.Fixed)
                return (int)Math.Min(int.MaxValue, (long)power + (actor?.Count(WindChime) ?? 0));
            int defense = ignoreDefense ? 0 : type == DamageType.Physical ? target.Stats.Defense :
                (type == DamageType.Magic || type == DamageType.Fire) && actor?.Trait != CharacterTrait.IgnoreMagicDefense
                    ? target.Stats.SpecialDefense : 0;
            if (type == DamageType.Lightning && actor != null)
                power += 2 * actor.Count(ThunderEye);
            decimal amount = Math.Max(1, power - defense);
            if (actor != null)
            {
                amount *= 1m + actor.Layers(Vigor) * 0.20m;
                if (actor.Trait == CharacterTrait.Rebirth && actor.Revived)
                    amount *= 2;
                if (actor.Input.Side == Side.Party && (request.PartyGold > 0 || equipmentGold > 0))
                {
                    int statues = units.Where(ally => ally.Health > 0 && ally.Input.Side == actor.Input.Side &&
                        Math.Abs(ally.Input.Position - actor.Input.Position) == 1).Sum(ally => ally.Count(LionGoldStatue));
                    amount *= 1m + (request.PartyGold + equipmentGold) * (decimal)statues / 100;
                }
                int wolves = actor.Count(LoneWolf);
                if (wolves > 0)
                {
                    int open = Rules.Positions - units.Count(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0);
                    amount *= 1m + 0.5m * open * wolves;
                }
            }
            if (target.Layers(Resolve) > 0)
                amount *= 0.5m;
            if (target.Layers(Exposed) > 0)
                amount *= 1.5m;
            if (target.Layers(Frozen) > 0)
                amount *= type == DamageType.Fire ? 3 : 2;
            if (type == DamageType.Fire)
                amount *= 1m + 0.5m * target.Layers(Chill);
            return (int)Math.Clamp(decimal.Floor(amount) + (actor?.Count(WindChime) ?? 0), 1, int.MaxValue);
        }

        private int ProjectedDamage(Unit actor, Unit target, IReadOnlyList<(DamageType Type, int Power)> parts,
            bool ignoreDefense = false) =>
            LimitDamage(target, Math.Max(0, DamageAfterGuard(target, SumDamage(parts.Select(part =>
                Damage(actor, target, part.Power, FinalDamageType(actor, part.Type), ignoreDefense)))) - target.Shield));

        private int DamageAfterGuard(Unit target, int damage) =>
            damage > 0 ? Math.Max(1, damage - DockGuardReduction(target)) : 0;

        private static int LimitDamage(Unit target, int damage) =>
            target.Trait == CharacterTrait.DamageLimit ? Math.Min(damage, target.TraitPower) : damage;

        private static int SumDamage(IEnumerable<int> amounts) =>
            (int)Math.Min(int.MaxValue, amounts.Sum(amount => (long)amount));

        private void Strike(Unit actor, Unit target, int power, DamageType type, string name, BattleEventKind kind,
            DecisionReason reason = DecisionReason.Default, BattleVisualCue visual = BattleVisualCue.None,
            string skillBookId = null) =>
            ResolveStrike(actor, target, new[] { (type, power) }, name, kind, reason, visual: visual, skillBookId: skillBookId);

        private (int Total, int Physical) ResolveStrike(Unit actor, Unit target,
            IReadOnlyList<(DamageType Type, int Power)> parts, string name, BattleEventKind kind, DecisionReason reason,
            bool ignoreDefense = false, bool weaponAttack = false, BattleVisualCue visual = BattleVisualCue.None,
            int multiplier = 1, string skillBookId = null)
        {
            // All components read the same defensive state before the combined hit consumes it.
            var resolved = parts.Select(part =>
            {
                var type = FinalDamageType(actor, part.Type);
                int amount = (int)Math.Min(int.MaxValue, (long)Damage(actor, target, part.Power, type, ignoreDefense) * multiplier);
                return (Type: type, Amount: amount);
            }).ToArray();
            int total = SumDamage(resolved.Select(part => part.Amount));
            int guarded = DamageAfterGuard(target, total);
            if (guarded < total)
            {
                int first = Array.FindIndex(resolved, part => part.Amount > 0);
                resolved[first].Amount -= total - guarded;
            }
            int damage = actor != null && actor.Input.Side != target.Input.Side && Round > 0 && target.ProtectedRound == Round
                ? 0 : guarded;
            if (damage > 0)
            {
                RemoveLayers(target, Exposed, 1);
                RemoveLayers(target, Frozen, target.Layers(Frozen));
                if (resolved.Any(part => part.Type == DamageType.Fire && part.Amount > 0))
                    RemoveLayers(target, Chill, target.Layers(Chill));
            }

            int absorbed = Math.Min(target.Shield, damage);
            target.Shield -= absorbed;
            int healthDamage = damage - absorbed;
            var linked = healthDamage > 0 ? LinkedAllies(target) : new List<Unit>();
            int dealt = LoseHealth(target, LimitDamage(target, healthDamage), out string protection);
            var lifeLost = LifeLossByType(resolved, absorbed, dealt);
            int physicalLost = lifeLost.Where(part => part.Type == DamageType.Physical).Sum(part => part.Amount);
            bool poisonDamage = visual == BattleVisualCue.RoundEndPoison;
            string detail = protection ?? DamageDetail(resolved);
            Report(kind, actor, target, name, ActionKind.Attack, dealt, reason, detail, weaponAttack: weaponAttack,
                visual: visual, skillBookId: skillBookId, damageTaken: lifeLost, poisonDamage: poisonDamage);
            if (target.Health == 0)
                Report(BattleEventKind.Defeated, actor, target, name, ActionKind.Attack);
            if (target.Health > 0 && absorbed > 0 && target.Shield == 0)
                BreakAnchorArmor(target);
            ReactToEnemyHealthLoss(target, dealt, physicalLost);
            OnLifeLost(actor, target, dealt);
            if (dealt > 0 && weaponAttack && actor.Input.Side != target.Input.Side &&
                target.Trait == CharacterTrait.ExposeMeleeAttacker && IsMeleeStrike(actor))
                GrantStatus(target, actor, Exposed, target.TraitPower);

            foreach (var ally in linked)
            {
                if (dealt <= 0)
                    break;
                if (actor != null && actor.Input.Side != ally.Input.Side && Round > 0 && ally.ProtectedRound == Round)
                    continue;
                bool physicalOnly = ally.Has(InsulatingOrb);
                int linkedDamage = physicalOnly ? physicalLost : dealt;
                if (linkedDamage == 0)
                    continue;
                int mirrored = LoseHealth(ally, LimitDamage(ally, linkedDamage), out string allyProtection);
                var mirroredParts = physicalOnly ? lifeLost.Where(part => part.Type == DamageType.Physical) : lifeLost;
                Report(BattleEventKind.Effect, target, ally, "红绳", ActionKind.Attack, mirrored,
                    detail: allyProtection, damageTaken: LifeLossByType(mirroredParts, 0, mirrored), poisonDamage: poisonDamage);
                if (ally.Health == 0)
                    Report(BattleEventKind.Defeated, target, ally, "红绳", ActionKind.Attack);
                ReactToEnemyHealthLoss(ally, mirrored, physicalOnly ? mirrored : Math.Min(mirrored, physicalLost));
                OnLifeLost(actor, ally, mirrored);
            }

            if (target.Health == 0 || linked.Any(ally => ally.Health == 0))
                UpdateEquipment();
            int magicLost = lifeLost.Where(part => part.Type is DamageType.Magic or DamageType.Fire).Sum(part => part.Amount);
            int crystals = actor?.Count(ArcaneCrystal) ?? 0;
            if (magicLost > 0 && crystals > 0 && actor.Health > 0 && !actor.ResolvingCrystalHeal)
            {
                actor.ResolvingCrystalHeal = true;
                // The earrings can turn the heal back into magic damage.
                try
                {
                    Heal(actor, actor, (int)Math.Min(int.MaxValue, (long)magicLost * crystals),
                        "魔导水晶", BattleEventKind.Effect, hostileTarget: target);
                }
                finally
                {
                    actor.ResolvingCrystalHeal = false;
                }
            }
            return (dealt, physicalLost);
        }

        private static bool IsMeleeStrike(Unit actor) => UsesShortbow(actor) ||
            actor.Input.Character.Reach == AttackReach.Melee ||
            (actor.Input.Character.Reach == AttackReach.Unspecified && actor.Input.Character.Profession == Profession.Warrior);

        private static IReadOnlyList<(DamageType Type, int Amount)> LifeLossByType(
            IEnumerable<(DamageType Type, int Amount)> parts, int shield, int healthLoss)
        {
            if (healthLoss == 0)
                return Array.Empty<(DamageType Type, int Amount)>();

            var loss = new List<(DamageType Type, int Amount)>();
            foreach (var part in parts)
            {
                if (healthLoss == 0)
                    break;
                int absorbed = Math.Min(shield, part.Amount);
                shield -= absorbed;
                int applied = Math.Min(healthLoss, part.Amount - absorbed);
                healthLoss -= applied;
                if (applied > 0)
                    loss.Add((part.Type, applied));
            }
            return loss.AsReadOnly();
        }

        private static string DamageDetail(IReadOnlyList<(DamageType Type, int Amount)> parts)
        {
            if (parts.Count > 1)
                return string.Join(" + ", parts.Select(part => $"{DamageName(part.Type)} {part.Amount}"));
            var type = parts[0].Type;
            return type is DamageType.Magic or DamageType.Fire or DamageType.Lightning or DamageType.Fixed
                ? DamageName(type) : null;
        }

        private static string DamageName(DamageType type) => type switch
        {
            DamageType.Physical => "物理",
            DamageType.Magic => "魔法",
            DamageType.Fire => "火焰",
            DamageType.Lightning => "雷电",
            DamageType.Fixed => "固定",
            _ => "真实"
        };

        private List<Unit> LinkedAllies(Unit target)
        {
            var linked = new List<Unit> { target };
            for (int index = 0; index < linked.Count; index++)
            {
                var unit = linked[index];
                foreach (var ally in units.Where(ally => ally.Health > 0 &&
                    (ally == unit.LinkedFront || ally.LinkedFront == unit)))
                    if (!linked.Contains(ally))
                        linked.Add(ally);
            }
            linked.RemoveAt(0);
            return linked;
        }

        private static int LoseHealth(Unit target, int damage, out string protection)
        {
            protection = null;
            if (damage <= 0)
                return 0;

            if (target.StoneGuard && damage > 7)
            {
                target.StoneGuard = false;
                protection = "坚如磐石抵挡了伤害";
                return 0;
            }

            int before = target.Health;
            int minimum = target.Layers(Undying) > 0 ? 1 : 0;
            int remaining = Math.Max(minimum, before - damage);
            if (remaining == 0 && target.CharmsUsed < target.Count(ElvenCharm))
            {
                target.CharmsUsed++;
                target.Health = target.Layers(Cursed) > 0 ? 1 : Math.Max(1, target.Stats.Health / 2);
                protection = "精灵护符抵挡致命伤害";
                return 0;
            }
            if (remaining == 0 && target.Trait == CharacterTrait.Rebirth && !target.Revived)
            {
                target.Revived = true;
                target.Health = target.Stats.Health;
                protection = "堕落神侍复活";
                return 0;
            }

            target.Health = remaining;
            return before - remaining;
        }

        private static int HealingPower(Unit target, int power) =>
            (int)Math.Min(int.MaxValue, (long)power * (1 << target.Count(HealingRing)));

        private void Heal(Unit actor, Unit target, int power, string name, BattleEventKind kind,
            DecisionReason reason = DecisionReason.Default, Unit hostileTarget = null, BattleVisualCue visual = BattleVisualCue.None,
            string skillBookId = null)
        {
            if (actor.Has(RebelEarring))
            {
                var enemy = hostileTarget ?? target;
                if (enemy.Input.Side == actor.Input.Side || enemy.Health == 0)
                    enemy = TargetableOpponents(actor).FirstOrDefault();
                if (enemy != null && power > 0)
                {
                    Strike(actor, enemy, power, DamageType.Magic, name, kind, reason, visual, skillBookId);
                    if (kind == BattleEventKind.Action)
                        CounterAttack(enemy, actor);
                }
                return;
            }

            int amount = target.Layers(Cursed) > 0 ? 0 :
                Math.Min(HealingPower(target, power), target.Stats.Health - target.Health);
            target.Health += amount;
            if (amount > 0 || kind == BattleEventKind.Action)
                Report(kind, actor, target, name, ActionKind.Heal, amount, reason, visual: visual, skillBookId: skillBookId);
            int roots = target.Count(TreeRoot);
            if (amount > 0 && roots > 0)
                foreach (var enemy in Opponents(target).ToArray())
                    if (enemy.Health > 0)
                        Strike(target, enemy, amount * roots, DamageType.Lightning, "古树根茎", BattleEventKind.Effect,
                            visual: visual is BattleVisualCue.RoundEndHeal or BattleVisualCue.RoundEndPoisonHeal
                                ? BattleVisualCue.RoundEndLightning : BattleVisualCue.None);
        }
    }
}
