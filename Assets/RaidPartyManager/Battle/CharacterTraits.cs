using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.CharacterTrait;
using static RaidPartyManager.Domain.StatusKind;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private static readonly ActionDefinition HealAllyAction = new ActionDefinition(
            "trait-heal", "治疗", ActionKind.Heal, TargetRule.WoundedAlly, 0, healingScalesWithAttack: true);

        private void ApplyBattleStartTraits()
        {
            foreach (var unit in units.Where(unit => unit.Health > 0 && unit.Trait == CopyAdvancedTrait))
            {
                var available = request.CharacterCatalog.Values.Where(form => form.Stage == 2 && form.Trait != None).ToArray();
                if (available.Length == 0)
                    continue;
                var source = available[random.Next(available.Length)];
                unit.CopiedTrait = source.Trait;
                unit.CopiedTraitPower = source.TraitPower;
                Report(BattleEventKind.Effect, unit, unit, "复制被动", ActionKind.Shield, detail: source.Name, copiedTraitSource: source);
            }

            foreach (var unit in units.Where(unit => unit.Health > 0 && unit.Trait == EvolveAlly).ToArray())
            {
                var allies = units.Where(ally => ally != unit && ally.Input.Side == unit.Input.Side && ally.Health > 0 &&
                    ally.Input.Character.Stage == 1 && ally.Input.Character.EvolutionIds.Any(request.CharacterCatalog.ContainsKey))
                    .ToArray();
                if (allies.Length == 0)
                    continue;

                var target = allies[random.Next(allies.Length)];
                var forms = target.Input.Character.EvolutionIds.Where(request.CharacterCatalog.ContainsKey).ToArray();
                ChangeForm(unit, target, request.CharacterCatalog[forms[random.Next(forms.Length)]], "本场进阶");
            }
        }

        private void ApplyRoundStartTraits()
        {
            var living = units.Where(unit => unit.Health > 0).ToArray();
            foreach (var unit in living)
                unit.BorrowedAttack = 0;
            UpdateEquipment();

            foreach (var unit in living)
            {
                switch (unit.Trait)
                {
                    case SoakEnemyEachRound:
                        var targets = Opponents(unit).ToArray();
                        if (targets.Length > 0)
                            GrantStatus(unit, targets[random.Next(targets.Length)], Wet, unit.TraitPower,
                                "深海轻语", kind: BattleEventKind.Effect);
                        break;
                    case PoisonOpponents:
                        foreach (var enemy in Opponents(unit))
                            GrantStatus(unit, enemy, Poison, unit.TraitPower,
                                "幽雾炼药", BattleVisualCue.RoundStartPoison, BattleEventKind.Effect);
                        break;
                    case BorrowAllyAttack:
                        var behind = living.Where(ally => ally.Input.Side == unit.Input.Side &&
                                ally.Input.Position > unit.Input.Position)
                            .OrderBy(ally => ally.Input.Position).FirstOrDefault();
                        if (behind != null)
                        {
                            unit.BorrowedAttack = CurrentAttack(behind);
                            Report(BattleEventKind.Effect, unit, unit, "地煞借力", ActionKind.Shield,
                                detail: $"借用{behind.Input.Character.Name}的攻击力，攻击 +{unit.BorrowedAttack}",
                                visual: BattleVisualCue.RoundStartBonus);
                        }
                        break;
                }
            }

            if (living.Any(unit => unit.BorrowedAttack != 0))
                UpdateEquipment();
        }

        private void ApplyRoundEndTraits()
        {
            foreach (var unit in units.Where(unit => unit.Health > 0).ToArray())
            {
                if (unit.Health == 0 || !Opponents(unit).Any())
                    continue;

                if (unit.Trait == EscalatingAttack)
                {
                    int gain = ++unit.GrowthTriggers * unit.TraitPower;
                    unit.AttackGrowth += gain;
                    Report(BattleEventKind.Effect, unit, unit, "星辉增长", ActionKind.Shield, detail: $"攻击 +{gain}",
                        visual: BattleVisualCue.RoundEndGrowth);
                }
                else if (unit.Trait == LightningChant && Round % 3 == 0)
                {
                    for (int strike = 0; strike < unit.TraitPower && unit.Health > 0; strike++)
                    {
                        int power = CurrentAttack(unit);
                        foreach (var enemy in Opponents(unit).ToArray())
                        {
                            if (unit.Health == 0) break;
                            if (enemy.Health > 0)
                                Strike(unit, enemy, power, DamageType.Lightning, "灾厄吟唱", BattleEventKind.Effect,
                                    visual: BattleVisualCue.RoundEndLightning);
                        }
                    }
                }

                var formId = unit.Input.Character.BattleFormId;
                if (unit.Health > 0 && formId != null && request.CharacterCatalog.TryGetValue(formId, out var form))
                    ChangeForm(unit, unit, form, "切换形态", BattleVisualCue.RoundEndFormShift);
            }

            UpdateEquipment();
        }

        private void ChangeForm(Unit source, Unit target, CharacterDefinition form, string reason,
            BattleVisualCue visual = BattleVisualCue.None)
        {
            form = form.WithPersonality(target.Input.Character.Personality);
            target.ChangeForm(form);
            UpdateFormation();
            Report(BattleEventKind.FormChanged, source, target, reason, ActionKind.Shield, detail: form.Name, form: form,
                visual: visual);
        }

        private void OnLifeLost(Unit source, Unit target, int amount)
        {
            if (amount > 0 && target.Health > 0 && target.Health * 2 <= target.Stats.Health && target.FirstTrigger(PlayerTrait.Grit))
                GrantStatus(target, target, StatusKind.Resolve, 1, "硬撑");
            if (amount == 0)
                return;

            if (target.Health > 0 && target.Trait == GainEmberWhenHit)
                GrantStatus(target, target, Ember, target.TraitPower);
            if (source == null || source.Input.Side == target.Input.Side)
                return;
            if (source.Trait == FirstDamageToTarget)
                source.DamagedTargets.Add(target.Input.Id);
            if (target.Health == 0 && source.Trait == PermanentAttackOnKill)
            {
                source.PermanentAttack += source.TraitPower;
                Report(BattleEventKind.Effect, source, source, "击杀成长", ActionKind.Shield,
                    detail: $"攻击永久 +{source.TraitPower}");
            }
            if (source.Trait == HealOnDamage && source.Health > 0 && amount >= 2 && !source.ResolvingDamageHeal)
            {
                // Equipment can turn healing into another hit. Settle that hit without re-entering lifesteal.
                source.ResolvingDamageHeal = true;
                Heal(source, source, amount / 2, "残晖汲取", BattleEventKind.Effect, hostileTarget: target);
                source.ResolvingDamageHeal = false;
            }
        }
    }
}
