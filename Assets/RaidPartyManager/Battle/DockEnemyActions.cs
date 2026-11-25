using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.CharacterTrait;
using static RaidPartyManager.Domain.StatusKind;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private readonly Dictionary<Unit, (Unit Owner, int Slot, EquippedItem Item, int EscapeRound)> scavengedItems = new();
        private readonly HashSet<string> escapedWithEquipment = new();

        private bool TryDockEnemyTurn(Unit actor)
        {
            switch (actor.Trait)
            {
                case EnemyTideFollower when random.NextDouble() >= 0.70:
                    GrantStatus(actor, TargetableOpponents(actor).First(), Wet, 1, "拍岸泼潮", BattleVisualCue.TideMark);
                    return true;
                case EnemyWreckScavenger:
                    var victim = FrontAttackTargets(actor).First();
                    Hit(actor, victim, actor.Input.Character.BasicAttack, BattleEventKind.Action, DecisionReason.Default, "普攻",
                        onHit: defender =>
                        {
                            if (actor.Health > 0 && !actor.EnemyTriggered) Scavenge(actor, defender);
                        });
                    return true;
                case EnemyDeckSailor:
                    var softest = TargetableOpponents(actor).OrderBy(unit => unit.Stats.Defense).First();
                    EnemyHit(actor, softest, "普攻", BattleVisualCue.None);
                    return true;
                case EnemyMudEel when random.NextDouble() >= 0.70:
                    GrantStatus(actor, TargetableOpponents(actor).First(), Chill, 1, "盐霜吐息", BattleVisualCue.EelFrost);
                    return true;
                case EnemyLanternMoth when random.NextDouble() >= 0.60:
                    var frailest = Opponents(actor).OrderBy(unit => (double)unit.Health / unit.Stats.Health).First();
                    GrantStatus(actor, frailest, Weakened, 1, "吞灯余灰", BattleVisualCue.MothAsh, delayDecay: true);
                    return true;
                case EnemyShipCaulker when random.NextDouble() >= 0.60:
                    var wounded = units.Where(unit => unit.Input.Side == Side.Enemy && unit.Health > 0 &&
                        unit.Health < unit.Stats.Health && unit.Layers(Cursed) == 0)
                        .OrderBy(unit => (double)unit.Health / unit.Stats.Health).FirstOrDefault();
                    if (wounded == null)
                        return false;
                    Heal(actor, wounded, 1, "补缝", BattleEventKind.Action, visual: BattleVisualCue.CaulkerMend);
                    return true;
                case EnemyAnchorHermit when random.NextDouble() >= 0.65:
                    GrantStatus(actor, Opponents(actor).First(), Exposed, 1, "沉锚破防", BattleVisualCue.AnchorExpose);
                    return true;
                case EnemyFogAngler when random.NextDouble() >= 0.60:
                    var healthiest = Opponents(actor).OrderByDescending(unit => unit.Health).First();
                    GrantStatus(actor, healthiest, Cursed, 1, "引魂鱼灯", BattleVisualCue.AnglerCurse, delayDecay: true);
                    return true;
                case EnemyMooringWarden when random.NextDouble() >= 0.55:
                    var next = NextLivingAllyBehind(actor);
                    if (next == null)
                        return false;
                    GrantStatus(actor, next, Resolve, 2, "稳住缆绳", BattleVisualCue.WardenBrace);
                    return true;
                case EnemyBlockadeAdmiral:
                    return AdmiralTurn(actor);
                case EnemyDrownedLanternRay:
                    if (random.NextDouble() < 0.70)
                        EnemyHit(actor, Opponents(actor).First(), "沉灯甩尾", BattleVisualCue.RayTail);
                    else
                        CoverInFog(actor, actor.EnemyTriggered ? 2 : 1, "沉灯寒雾", BattleEventKind.Action, BattleVisualCue.RayMist);
                    return true;
                default:
                    return false;
            }
        }

        private bool AdmiralTurn(Unit actor)
        {
            if (actor.EnemyTriggered)
            {
                actor.EnemyTriggered = false;
                bool first = true;
                foreach (var target in Opponents(actor).Reverse().Take(2).ToArray())
                {
                    Strike(actor, target, CurrentAttack(actor), DamageType.Physical, "封港炮击",
                        first ? BattleEventKind.Action : BattleEventKind.Effect, visual: BattleVisualCue.AdmiralBroadside);
                    if (first)
                        ReportCountdown(actor, BattleCountdownKind.AdmiralBroadside, 0);
                    first = false;
                }
                return true;
            }

            if (random.NextDouble() < 0.65)
                return false;
            actor.EnemyTriggered = true;
            Report(BattleEventKind.Action, actor, actor, "鸣笛预警", ActionKind.Shield,
                detail: "下次行动将炮击最后两名存活的对手", visual: BattleVisualCue.AdmiralWhistle);
            ReportCountdown(actor, BattleCountdownKind.AdmiralBroadside, 1);
            foreach (var target in Opponents(actor).Reverse().Take(2))
                ShowEnemyVisual(actor, target, BattleVisualCue.AdmiralWarning);
            return true;
        }

        private void CoverInFog(Unit actor, int layers, string name, BattleEventKind kind, BattleVisualCue visual)
        {
            Report(kind, actor, actor, name, ActionKind.Shield, detail: $"所有对手受到 {layers} 层寒冷", visual: visual);
            foreach (var target in Opponents(actor).ToArray())
            {
                ShowEnemyVisual(actor, target, visual);
                GrantStatus(actor, target, Chill, layers);
            }
        }

        private void ShowEnemyVisual(Unit actor, Unit target, BattleVisualCue cue) =>
            events.Add(new BattleEvent(Tick, BattleEventKind.EnemyVisual, actor.Input.Id, target.Input.Id,
                actionKind: ActionKind.Shield, targetHealth: target.Health, targetShield: target.Shield, maximumHealth: target.Stats.Health, visualCue: cue));

        private Unit NextLivingAllyBehind(Unit actor) => units.FirstOrDefault(unit =>
            unit.Input.Side == actor.Input.Side && unit.Input.Position > actor.Input.Position && unit.Health > 0);

        private int DockGuardReduction(Unit target) =>
            target.Trait == EnemyMooringWarden && NextLivingAllyBehind(target) != null ? 1 : 0;

        private void SetOpeningEnemyShields()
        {
            foreach (var crab in units.Where(unit => unit.Health > 0 && unit.Trait == EnemyAnchorHermit))
            {
                crab.Shield += 6;
                Report(BattleEventKind.Effect, crab, crab, "沉锚护甲", ActionKind.Shield, 6, visual: BattleVisualCue.AnchorWard);
            }
        }

        private void BreakAnchorArmor(Unit target)
        {
            if (target.Trait != EnemyAnchorHermit || target.EnemyTriggered)
                return;
            target.EnemyTriggered = true;
            UpdateEquipment();
            Report(BattleEventKind.Effect, target, target, "沉锚护甲碎裂", ActionKind.Shield,
                detail: "本场战斗防御 −2", visual: BattleVisualCue.AnchorShatter);
        }

        private void ReactToEnemyHealthLoss(Unit target, int lifeLoss, int physicalLifeLoss)
        {
            if (lifeLoss <= 0)
                return;
            if (target.Health == 0)
                RecoverScavengedItem(target);
            else if (!target.EnemyTriggered && target.Trait == EnemySaltCrab && physicalLifeLoss > 0)
            {
                target.EnemyTriggered = true;
                GrantStatus(target, target, Resolve, 1, "盐壳硬化", BattleVisualCue.SaltShell, BattleEventKind.Effect);
            }
            else if (!target.EnemyTriggered && target.Trait == EnemyDrownedLanternRay &&
                target.Health <= target.Stats.Health / 2)
            {
                target.EnemyTriggered = true;
                CoverInFog(target, 2, "沉灯翻涌", BattleEventKind.Effect, BattleVisualCue.RaySurge);
            }
        }

        private void Scavenge(Unit actor, Unit victim)
        {
            actor.EnemyTriggered = true;
            var items = victim.Slots.Where(item => item != null).ToArray();
            if (items.Length == 0)
                return;
            var item = items[random.Next(items.Length)];
            int slot = Array.IndexOf(victim.Slots, item);
            victim.Slots[slot] = null;
            scavengedItems.Add(actor, (victim, slot, item, Round + 3));
            victim.ApplySlotChanges();
            UpdateEquipment();
            Report(BattleEventKind.Effect, actor, victim, "顺手牵羊", ActionKind.Shield,
                detail: $"偷走了{item.Definition.Name}", loadout: PublishScavengedLoadout(item),
                visual: BattleVisualCue.ScavengerSteal, targetEnergy: victim.Energy);
            ReportCountdown(actor, BattleCountdownKind.ScavengerEscape, 3);
        }

        private bool HasReservedSlot(Unit owner, int slot) =>
            scavengedItems.Values.Any(stash => stash.Owner == owner && stash.Slot == slot);

        private void RecoverScavengedItem(Unit scavenger)
        {
            if (!scavengedItems.Remove(scavenger, out var stash))
                return;
            ReportCountdown(scavenger, BattleCountdownKind.ScavengerEscape, 0);
            stash.Owner.Slots[stash.Slot] = stash.Item;
            stash.Owner.ApplySlotChanges();
            UpdateEquipment();
            Report(BattleEventKind.Effect, scavenger, stash.Owner, "拿回装备", ActionKind.Shield,
                detail: $"取回了{stash.Item.Definition.Name}", loadout: PublishScavengedLoadout(stash.Item), targetEnergy: stash.Owner.Energy);
        }

        private BattleLoadout PublishScavengedLoadout(EquippedItem item) =>
            item.Id == null ? null : currentLoadout = CaptureLoadout();

        private void ResolveScavengerEscapes()
        {
            foreach (var stash in scavengedItems.Where(entry => entry.Value.EscapeRound <= Round)
                .OrderBy(entry => entry.Key.Input.Position).ToArray())
            {
                var enemy = stash.Key;
                var item = stash.Value.Item;
                scavengedItems.Remove(enemy);
                ReportCountdown(enemy, BattleCountdownKind.ScavengerEscape, 0);
                if (item.Id != null)
                    escapedWithEquipment.Add(item.Id);
                enemy.Health = 0;
                UpdateEquipment();
                Report(BattleEventKind.Retreated, enemy, enemy, "溜入雾中", ActionKind.Shield,
                    detail: $"携带{item.Definition.Name}逃跑");
            }
        }
    }
}
