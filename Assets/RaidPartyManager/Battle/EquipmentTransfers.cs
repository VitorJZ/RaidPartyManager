using System;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private void ApplyMegaphoneBonus(Unit unit, int growth)
        {
            unit.AttackGrowth += growth;
            UpdateEquipment();
            Report(BattleEventKind.Effect, unit, unit, "传话筒", ActionKind.Shield,
                detail: $"攻击 +{growth}", visual: BattleVisualCue.RoundStartBonus, equipment: EquipmentEffect.Megaphone);
            Heal(unit, unit, growth, "传话筒", BattleEventKind.Effect);
        }

        private void PassMegaphone(Unit owner, EquippedItem item)
        {
            var eligible = Teammates(owner).Where(unit => unit != owner)
                .Select(unit => (Unit: unit, Slot: Enumerable.Range(0, unit.Slots.Length)
                    .Where(slot => unit.Slots[slot] == null && !HasReservedSlot(unit, slot)).DefaultIfEmpty(-1).First()))
                .Where(entry => entry.Slot >= 0).ToArray();
            if (eligible.Length == 0)
                return;

            var (receiver, slot) = eligible[random.Next(eligible.Length)];
            owner.Slots[Array.IndexOf(owner.Slots, item)] = null;
            receiver.Slots[slot] = item;
            item.Transfers++;
            owner.ApplySlotChanges();
            receiver.ApplySlotChanges();
            UpdateFormation();

            BattleLoadout loadout = null;
            if (owner.Input.Side == Side.Party && item.Id != null)
                loadout = currentLoadout = CaptureLoadout();
            Report(BattleEventKind.Effect, owner, receiver, "传话筒", ActionKind.Shield,
                detail: $"传递第 {item.Transfers} 次", loadout: loadout, visual: BattleVisualCue.RoundEndTransfer, equipment: EquipmentEffect.Megaphone);
        }
    }
}
