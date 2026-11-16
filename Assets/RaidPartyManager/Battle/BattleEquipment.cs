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
                        GrantStatus(actor, TargetableOpponents(actor).First(), Exposed, 1);
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
                            Heal(unit, unit, 3, "生命吊坠", BattleEventKind.Effect);
                        else if (item.Effect == FirstFire)
                            GrantStatus(unit, unit, Ember, 1);
                    }

                    if (unit.Health == 0)
                        break;
                    int poison = unit.Layers(Poison);
                    if (poison > 0)
                    {
                        if (unit.Has(WitchDoctorDoll))
                            Heal(unit, unit, poison, "巫医玩偶", BattleEventKind.Effect);
                        else
                            Strike(null, unit, poison, DamageType.True, "中毒", BattleEventKind.Effect);
                    }
                    if (!Opponents(unit).Any() || !units.Any(ally => ally.Input.Side == unit.Input.Side && ally.Health > 0))
                        return;
                }
            }

            ApplyRoundEndTraits();
            PassMegaphones();
            foreach (var unit in units.Where(unit => unit.Health > 0))
            {
                bool changed = false;
                foreach (var status in DecayingStatuses)
                    changed |= RemoveLayers(unit, status, 1);
                if (changed)
                    Report(BattleEventKind.Effect, unit, unit, "回合状态递减", ActionKind.Shield, detail: "状态层数 −1");
            }
        }

        private void GrantStatus(Unit actor, Unit target, StatusKind status, int layers)
        {
            if (target.Health == 0 || (status != Purity && target.Layers(Purity) > 0) ||
                (status == Chill && target.Layers(Frozen) > 0))
                return;

            int bonus = status == Poison ? 2 * (actor?.Count(PoisonRecipe) ?? 0) : 0;
            int total = target.Layers(status) + layers + bonus + (status == Greed ? 0 : target.Layers(Greed));
            if (status == Purity)
                target.Statuses.Clear();
            if (status == Chill && total >= 3)
            {
                target.Statuses.Remove(Chill);
                target.Statuses[Frozen] = 1;
                Report(BattleEventKind.Effect, actor, target, "冰冻", ActionKind.Shield, detail: "寒冷达到 3 层");
                return;
            }

            target.Statuses[status] = total;
            Report(BattleEventKind.Effect, actor, target, StatusRules.For(status).Name, ActionKind.Shield, detail: $"达到 {total} 层");
        }

        private static bool RemoveLayers(Unit unit, StatusKind status, int amount)
        {
            int prior = unit.Layers(status);
            if (prior == 0)
                return false;
            if (prior <= amount)
                unit.Statuses.Remove(status);
            else
                unit.Statuses[status] = prior - amount;
            return true;
        }

        private static int CurrentAttack(Unit actor) =>
            Math.Max(0, actor.Stats.Attack - (actor.Layers(Weakened) > 0 ? 2 : 0));

        private static int BaseHealing(Unit actor, ActionDefinition action) =>
            action.HealingScalesWithAttack ? CurrentAttack(actor) : action.Power;

        private List<(DamageType Type, int Power)> AttackComponents(Unit actor, Unit target, ActionDefinition action,
            int falloff = 0)
        {
            var components = new List<(DamageType Type, int Power)>();
            if (actor.Has(ExecutionMask) && actor.LastAttackRound != Round)
            {
                components.Add((DamageType.Fixed, Math.Max(1, target.Stats.Health / 10)));
                return components;
            }

            int attack = CurrentAttack(actor) - falloff;
            if (actor.Trait == CharacterTrait.RepeatTargetDamage && actor.LastHitTarget == target.Input.Id)
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

            if (actor.Trait == CharacterTrait.PiercingArrow && !UsesShortbow(actor))
            {
                var forward = units.Where(unit => unit.Health > 0 && unit.Input.Side == actor.Input.Side &&
                        unit.Input.Position < actor.Input.Position).Reverse().Concat(Opponents(actor)).ToArray();
                int falloff = 0;
                var kind = BattleEventKind.Action;
                foreach (var unit in forward)
                {
                    if (actor.Health == 0)
                        break;
                    if (unit.Health == 0)
                        continue;
                    if (Hit(actor, unit, action, kind, reason, falloff: falloff))
                        falloff++;
                    kind = BattleEventKind.Effect;
                }
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
            DecisionReason reason, string equipmentName = null, int falloff = 0, bool canCounter = true)
        {
            string name = equipmentName ?? action.Name;
            bool missed = (actor.Layers(Injured) > 0 && random.NextDouble() < 0.20) ||
                (target.Layers(Swift) > 0 && random.NextDouble() < 0.20);
            var parts = AttackComponents(actor, target, action, falloff);
            actor.LastAttackRound = Round;
            int physicalDealt = 0;
            if (missed)
                Report(kind, actor, target, name, ActionKind.Attack, reason: reason, detail: "未命中");
            else
            {
                int thorns = target.Layers(Thorns);
                int ember = actor.Layers(Ember);
                var damage = ResolveStrike(actor, target, parts, name, kind, reason, ignoreDefense: UsesShortbow(actor));
                actor.LastHitTarget = target.Input.Id;
                if (damage.Total > 0 && actor.Trait == CharacterTrait.PoisonOnHit)
                    GrantStatus(actor, target, Poison, actor.TraitPower);
                physicalDealt = damage.Physical;
                if (thorns > 0 && damage.Total >= 2 && actor.Health > 0)
                    Strike(target, actor, damage.Total / 2, DamageType.Physical, "荆棘", BattleEventKind.Effect);

                if (actor.Health > 0 && ember > 0 && parts[0].Type != DamageType.Fixed && !actor.Has(EternalFire))
                {
                    RemoveLayers(actor, Ember, ember);
                    Report(BattleEventKind.Effect, actor, actor, "余烬", ActionKind.Shield, detail: "消耗余烬");
                }
            }

            if (!missed && canCounter)
                CounterAttack(target, actor);

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
                    case WindGlove when kind == BattleEventKind.Action && random.NextDouble() < 0.30:
                        var targets = TargetableOpponents(actor);
                        var next = targets.Contains(target) ? target :
                            action.Target == TargetRule.RearEnemy ? targets.LastOrDefault() : targets.FirstOrDefault();
                        if (next != null)
                            Hit(actor, next, action, BattleEventKind.Effect, reason, "疾风手套", falloff, canCounter);
                        break;
                }
            }
            return !missed;
        }

        private void CounterAttack(Unit defender, Unit attacker)
        {
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
            if (type == DamageType.Fixed)
                return power;
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
            return (int)Math.Min(int.MaxValue, decimal.Floor(amount));
        }

        private int ProjectedDamage(Unit actor, Unit target, IReadOnlyList<(DamageType Type, int Power)> parts,
            bool ignoreDefense = false) =>
            LimitDamage(target, Math.Max(0, SumDamage(parts.Select(part =>
                Damage(actor, target, part.Power, FinalDamageType(actor, part.Type), ignoreDefense))) - target.Shield));

        private static int LimitDamage(Unit target, int damage) =>
            target.Trait == CharacterTrait.DamageLimit ? Math.Min(damage, target.TraitPower) : damage;

        private static int SumDamage(IEnumerable<int> amounts) =>
            (int)Math.Min(int.MaxValue, amounts.Sum(amount => (long)amount));

        private void Strike(Unit actor, Unit target, int power, DamageType type, string name, BattleEventKind kind,
            DecisionReason reason = DecisionReason.Default) =>
            ResolveStrike(actor, target, new[] { (type, power) }, name, kind, reason);

        private (int Total, int Physical) ResolveStrike(Unit actor, Unit target,
            IReadOnlyList<(DamageType Type, int Power)> parts, string name, BattleEventKind kind, DecisionReason reason, bool ignoreDefense = false)
        {
            // All components read the same defensive state before the combined hit consumes it.
            var resolved = parts.Select(part =>
            {
                var type = FinalDamageType(actor, part.Type);
                return (Type: type, Amount: Damage(actor, target, part.Power, type, ignoreDefense));
            }).ToArray();
            int damage = SumDamage(resolved.Select(part => part.Amount));
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
            string detail = protection ?? DamageDetail(resolved);
            Report(kind, actor, target, name, ActionKind.Attack, dealt, reason, detail);
            if (target.Health == 0)
                Report(BattleEventKind.Defeated, actor, target, name, ActionKind.Attack);
            OnLifeLost(actor, target, dealt);

            foreach (var ally in linked)
            {
                if (dealt <= 0)
                    break;
                int mirrored = LoseHealth(ally, LimitDamage(ally, dealt), out string allyProtection);
                Report(BattleEventKind.Effect, target, ally, "红绳", ActionKind.Attack, mirrored,
                    detail: allyProtection);
                if (ally.Health == 0)
                    Report(BattleEventKind.Defeated, target, ally, "红绳", ActionKind.Attack);
                OnLifeLost(actor, ally, mirrored);
            }

            if (target.Health == 0 || linked.Any(ally => ally.Health == 0))
                UpdateEquipment();
            return (dealt, PhysicalLifeLoss(resolved, absorbed, dealt));
        }

        private static int PhysicalLifeLoss(IEnumerable<(DamageType Type, int Amount)> parts, int shield, int healthLoss)
        {
            int physical = 0;
            foreach (var part in parts)
            {
                if (healthLoss == 0)
                    break;
                int absorbed = Math.Min(shield, part.Amount);
                shield -= absorbed;
                int applied = Math.Min(healthLoss, part.Amount - absorbed);
                healthLoss -= applied;
                if (part.Type == DamageType.Physical)
                    physical += applied;
            }
            return physical;
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
            power * (1 << target.Count(HealingRing));

        private void Heal(Unit actor, Unit target, int power, string name, BattleEventKind kind,
            DecisionReason reason = DecisionReason.Default, Unit hostileTarget = null)
        {
            if (actor.Has(RebelEarring))
            {
                var enemy = hostileTarget ?? target;
                if (enemy.Input.Side == actor.Input.Side || enemy.Health == 0)
                    enemy = TargetableOpponents(actor).FirstOrDefault();
                if (enemy != null && power > 0)
                {
                    Strike(actor, enemy, power, DamageType.Magic, name, kind, reason);
                    if (kind == BattleEventKind.Action)
                        CounterAttack(enemy, actor);
                }
                return;
            }

            int amount = target.Layers(Cursed) > 0 ? 0 :
                Math.Min(HealingPower(target, power), target.Stats.Health - target.Health);
            target.Health += amount;
            if (amount > 0 || kind == BattleEventKind.Action)
                Report(kind, actor, target, name, ActionKind.Heal, amount, reason);
            int roots = target.Count(TreeRoot);
            if (amount > 0 && roots > 0)
                foreach (var enemy in Opponents(target).ToArray())
                    if (enemy.Health > 0)
                        Strike(target, enemy, amount * roots, DamageType.Lightning, "古树根茎", BattleEventKind.Effect);
        }
    }
}
