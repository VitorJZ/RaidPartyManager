using System;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Domain.EquipmentEffect;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private void ApplyMegaphoneBonus()
        {
            foreach (var unit in units.Where(unit => unit.Health > 0).ToArray())
            {
                if (unit.Health == 0 || !Opponents(unit).Any())
                    continue;
                int growth = unit.Equipped.Where(item => item.Definition.Effect == Megaphone).Sum(item => item.Transfers);
                if (growth > 0)
                {
                    unit.AttackGrowth += growth;
                    Report(BattleEventKind.Effect, unit, unit, "传话筒", ActionKind.Shield, detail: $"攻击 +{growth}");
                    Heal(unit, unit, growth, "传话筒", BattleEventKind.Effect);
                }
            }
        }

        private void PassMegaphones()
        {
            // Capture each item once, so a later recipient cannot forward it twice in one round.
            var transfers = units.Where(unit => unit.Health > 0)
                .SelectMany(owner => owner.Equipped.Where(item => item.Definition.Effect == Megaphone)
                    .Select(item => (Owner: owner, Item: item))).ToArray();
            foreach (var (owner, item) in transfers)
            {
                if (!Opponents(owner).Any())
                    continue;
                var eligible = units.Where(unit => unit != owner && unit.Input.Side == owner.Input.Side &&
                    unit.Health > 0 && Array.IndexOf(unit.Slots, null) != -1).ToArray();
                if (eligible.Length == 0)
                    continue;

                var receiver = eligible[random.Next(eligible.Length)];
                owner.Slots[Array.IndexOf(owner.Slots, item)] = null;
                receiver.Slots[Array.IndexOf(receiver.Slots, null)] = item;
                item.Transfers++;
                owner.ApplySlotChanges();
                receiver.ApplySlotChanges();
                UpdateFormation();

                BattleLoadout loadout = null;
                if (owner.Input.Side == Side.Party && item.Id != null)
                    loadout = currentLoadout = CaptureLoadout();
                Report(BattleEventKind.Effect, owner, receiver, "传话筒", ActionKind.Shield,
                    detail: $"传递第 {item.Transfers} 次", loadout: loadout);
            }
        }
    }
}
