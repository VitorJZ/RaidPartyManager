using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    public enum EquipmentEffect
    {
        None, LifePendant, ElvenCharm, HealingRing, GreedyHand, WindGlove, GuardianBanner,
        FireRing, IceRing, LightningRing, VigorPotion, SwiftPotion, ResolvePotion, GreedPotion,
        ThornsPotion, RecruitmentNotice, RubyPendant, WitchDoctorDoll, EagleEye, RebelEarring,
        EternalFire, FirstFire, RitualMask, MurkyWater, PurityPotion, RedCord, InsightTotem,
        ScarletRose, Bludgeon, LoneWolf, ImperialSeal, PoisonRecipe, StealthCloak, CounterHorn,
        Bagua, Megaphone, TreeRoot, Shortbow, ExecutionMask, ThunderEye, ElementalQuartz, StaticPrism, PanaceaBottle,
        LionGoldStatue, ArcaneCrystal, InsulatingOrb, CoinDevourer, MoneyTreeBranch, EchoChalice, WindChime,
        HarborWhistle, SunkenFrostChain, FacelessCrown, ColdSoulReliquary, StoneArmor, PressureGovernor, CelestialGyro, CrucibleHeart,
        ZuixianGourd, StrangeSeed
    }

    public enum StatusKind
    {
        Vigor, Swift, Resolve, Undying, Greed, Purity, Ember, Poison, Exposed, Weakened,
        Thorns, Stunned, Injured, Cursed, Chill, Frozen, Wet, Evaporation, ForestMark
    }

    public static class EquipmentFormation
    {
        // Units entering this calculation have their personal flat equipment bonuses applied.
        public static IReadOnlyDictionary<string, Stats> Resolve(IReadOnlyList<Combatant> units)
        {
            var living = units.Where(unit => unit.Health > 0)
                .OrderBy(unit => unit.Position).ThenBy(unit => unit.Side).ToArray();
            var stats = units.ToDictionary(unit => unit.Id, unit => unit.Stats);
            var sharedGear = living
                .Where(owner => owner.Equipment.Any(item => item.Effect == EquipmentEffect.RedCord))
                .Select(owner => (Owner: owner, Front: AdjacentFrontOf(living, owner)))
                .Where(link => link.Front != null)
                .ToDictionary(link => link.Front.Id, link => link.Owner.Equipment
                    .Where(item => item.Effect != EquipmentEffect.RedCord).ToArray());

            // Flat bonuses, including those shared by Red Cord, are inputs to the ordered effects.
            foreach (var (id, gear) in sharedGear)
                foreach (var item in gear)
                    stats[id] += new Stats(item.Bonus.Attack, item.Bonus.Defense, item.Bonus.SpecialDefense, 0);

            // Read the current values at this slot. Never revisit a copy or conversion later in the pass.
            foreach (var unit in living)
            {
                var gear = unit.Equipment.Concat(sharedGear.GetValueOrDefault(unit.Id) ?? Array.Empty<EquipmentDefinition>());
                foreach (var item in gear)
                {
                    var current = stats[unit.Id];
                    switch (item.Effect)
                    {
                        case EquipmentEffect.GreedyHand:
                            var team = living.Where(ally => ally.Side == unit.Side).Select(ally => stats[ally.Id]).ToArray();
                            stats[unit.Id] = new Stats(team.Max(value => value.Attack), team.Max(value => value.Defense),
                                team.Max(value => value.SpecialDefense), current.Health);
                            break;
                        case EquipmentEffect.ScarletRose:
                            stats[unit.Id] = new Stats(StatValue((long)current.Attack + current.Defense + current.SpecialDefense),
                                0, 0, current.Health);
                            break;
                        case EquipmentEffect.GuardianBanner:
                            foreach (var ally in living.Where(ally => ally.Side == unit.Side && ally.Position > unit.Position))
                            {
                                var rear = stats[ally.Id];
                                stats[ally.Id] = new Stats(rear.Attack, StatValue((long)rear.Defense + current.Defense),
                                    rear.SpecialDefense, rear.Health);
                            }
                            break;
                        case EquipmentEffect.ImperialSeal:
                            long attack = living.Where(other => other.Character.Profession == unit.Character.Profession)
                                .Sum(other => (long)stats[other.Id].Attack);
                            stats[unit.Id] = new Stats(StatValue(attack), current.Defense, current.SpecialDefense, current.Health);
                            break;
                    }
                }
            }

            return stats;
        }

        private static int StatValue(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

        public static Combatant AdjacentFrontOf(IEnumerable<Combatant> units, Combatant owner) =>
            units.FirstOrDefault(unit => unit.Side == owner.Side && unit.Health > 0 &&
                unit.Position + unit.Character.PositionSlots == owner.Position);
    }
}
