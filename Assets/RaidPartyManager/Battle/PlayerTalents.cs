using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.StatusKind;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private readonly List<(Unit Caster, int Until)> nightWatches = new();
        private IEnumerable<Unit> Teammates(Unit unit) =>
            units.Where(u => u.Input.Side == unit.Input.Side && u.Health > 0).OrderBy(u => u.Input.Position);
        private Unit Behind(Unit unit) => Teammates(unit).FirstOrDefault(u => u.Input.Position > unit.Input.Position);

        private static int GiveShield(Unit unit, int amount)
        {
            if (amount > 0 && unit.FirstTrigger(PlayerTrait.LeakyShield)) amount = Math.Max(0, amount - 2);
            unit.Shield += amount;
            return amount;
        }

        private int ShieldAlly(Unit actor, Unit target, int amount, string name, string book = null)
        {
            int gained = GiveShield(target, amount);
            Report(BattleEventKind.Effect, actor, target, name, ActionKind.Shield, gained, skillBookId: book);
            return gained;
        }

        private void RefundHardFight(Unit actor)
        {
            if (request.HardFight && actor.FirstTrigger(PlayerTrait.BigMatch))
            {
                actor.SpendEnergy(-1);
                Report(BattleEventKind.Effect, actor, actor, "硬仗奖金", ActionKind.Shield, actorEnergy: actor.Energy);
            }
        }

        private void StartPlayerTraits()
        {
            foreach (var unit in units.Where(u => u.Health > 0).ToArray())
                switch (unit.Talent)
                {
                    case PlayerTrait.Vanguard when unit.Input.Position == 1:
                        ShieldAlly(unit, unit, 3, "前排担当"); break;
                    case PlayerTrait.RearGuard when unit.Input.Position == 4:
                        var front = Teammates(unit).LastOrDefault(u => u.Input.Position < unit.Input.Position);
                        if (front != null) ShieldAlly(unit, front, 2, "压阵护卫");
                        break;
                    case PlayerTrait.ExposedStart: GrantStatus(unit, unit, Exposed, 1, "开场露怯"); break;
                    case PlayerTrait.RecklessStart:
                        int lost = LoseHealth(unit, 2, out _, out var loadout, out bool charmTriggered);
                        Report(BattleEventKind.Effect, unit, unit, "豁命开场", ActionKind.Attack, lost, loadout: loadout,
                            damageTaken: new[] { (DamageType.Fixed, lost) }, charmTriggered: charmTriggered);
                        if (unit.Health == 0) Report(BattleEventKind.Defeated, unit, unit, "豁命开场", ActionKind.Attack);
                        else { TriggerCrown(unit); GrantStatus(unit, unit, Vigor, 1, "豁命开场"); }
                        break;
                    case PlayerTrait.Distracted:
                        unit.SpendEnergy(1);
                        Report(BattleEventKind.Effect, unit, unit, "开场走神", ActionKind.Shield, targetEnergy: unit.Energy); break;
                }
        }

        private void StartPlayerRound()
        {
            foreach (var unit in units.Where(u => u.Health > 0).ToArray())
            {
                if (unit.Health == 0) continue;
                if (Round > 1 && unit.Charge())
                    Report(BattleEventKind.Effect, unit, unit, "蓄能", ActionKind.Shield, targetEnergy: unit.Energy);
                if (Round == 2 && unit.Talent == PlayerTrait.HelpingHand && Behind(unit) is {} rear)
                    Heal(unit, rear, 2, "搭把手", BattleEventKind.Effect);
                if (Round == 2 && unit.Talent == PlayerTrait.Warmup)
                    GrantStatus(unit, unit, Swift, 1);
                if (unit.ThunderRound == Round && unit.Layers(Stunned) == 0)
                    foreach (var enemy in Opponents(unit).ToArray())
                        Strike(unit, enemy, 2 * CurrentAttack(unit) + 6, DamageType.Lightning, "雷暴裁决", BattleEventKind.Effect, skillBookId: "skill-book-30");
            }
            foreach (var watch in nightWatches)
                foreach (var ally in Teammates(watch.Caster).ToArray())
                    Heal(watch.Caster, ally, 2, "守夜灯火", BattleEventKind.Effect, skillBookId: "skill-book-26");
            nightWatches.RemoveAll(w => w.Until <= Round);
        }

        private void FallenTraits(Unit fallen)
        {
            if (fallen.Trait == CharacterTrait.ChorusArmory)
            {
                foreach (var ally in Teammates(fallen).Where(unit => unit != fallen).ToArray())
                {
                    var candidates = ally.Equipped.ToArray();
                    if (candidates.Length == 0) continue;
                    var item = candidates[random.Next(candidates.Length)];
                    item.Disabled = true;
                    ally.ApplySlotChanges();
                    RefreshFormationEquipment();
                    var loadout = ally.Input.Side == Side.Party && item.Id != null ? currentLoadout = CaptureLoadout() : null;
                    Report(BattleEventKind.Effect, fallen, ally, "颂歌落幕", ActionKind.Shield,
                        detail: $"{item.Definition.Name}本场失效", loadout: loadout, targetEnergy: ally.Energy);
                }
            }
            fallen.ThunderRound = 0;
            fallen.Intercepts = 0;
            bool compensation = fallen.FirstTrigger(PlayerTrait.Compensation);
            foreach (var ally in Teammates(fallen).ToArray())
            {
                if (compensation) ShieldAlly(fallen, ally, 2, "工伤赔偿");
                if (ally.Input.Position > fallen.Input.Position && ally.FirstTrigger(PlayerTrait.Successor))
                {
                    GrantStatus(ally, ally, Vigor, 1, "接班人");
                    ShieldAlly(ally, ally, 2, "接班人");
                }
            }
        }
    }
}
