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
        LionGoldStatue, ArcaneCrystal, InsulatingOrb, CoinDevourer, MoneyTreeBranch, EchoChalice, WindChime
    }

    public enum StatusKind
    {
        Vigor, Swift, Resolve, Undying, Greed, Purity, Ember, Poison, Exposed, Weakened,
        Thorns, Stunned, Injured, Cursed, Chill, Frozen
    }

    public static class EquipmentFormation
    {
        // Units entering this calculation have their personal flat equipment bonuses applied.
        public static IReadOnlyDictionary<string, Stats> Resolve(IReadOnlyList<Combatant> units)
        {
            var living = units.Where(unit => unit.Health > 0).ToArray();
            var stats = units.ToDictionary(unit => unit.Id, unit => unit.Stats);
            var sharing = living.ToDictionary(unit => unit.Id,
                unit => living.Where(owner => owner.Equipment.Any(item => item.Effect == EquipmentEffect.RedCord)
                        && FrontOf(living, owner) == unit)
                    .SelectMany(owner => owner.Equipment.Where(item => item.Effect != EquipmentEffect.RedCord)).ToArray());

            int Count(Combatant unit, EquipmentEffect effect) =>
                unit.Equipment.Count(item => item.Effect == effect) + sharing[unit.Id].Count(item => item.Effect == effect);

            foreach (var unit in living)
                foreach (var item in sharing[unit.Id])
                    stats[unit.Id] += new Stats(item.Bonus.Attack, item.Bonus.Defense, item.Bonus.SpecialDefense, 0);

            foreach (var side in living.GroupBy(unit => unit.Side))
            {
                var team = side.ToArray();
                int attack = team.Max(unit => stats[unit.Id].Attack);
                int defense = team.Max(unit => stats[unit.Id].Defense);
                int special = team.Max(unit => stats[unit.Id].SpecialDefense);
                foreach (var unit in team)
                {
                    var current = stats[unit.Id];
                    if (Count(unit, EquipmentEffect.GreedyHand) > 0)
                        current = new Stats(attack, defense, special, current.Health);
                    if (Count(unit, EquipmentEffect.ScarletRose) > 0)
                        current = new Stats(current.Attack + current.Defense + current.SpecialDefense, 0, 0, current.Health);
                    stats[unit.Id] = current;
                }

                var defenseFromBanners = team.ToDictionary(unit => unit.Id,
                    unit => stats[unit.Id].Defense * Count(unit, EquipmentEffect.GuardianBanner));
                foreach (var unit in team)
                {
                    if (Count(unit, EquipmentEffect.ScarletRose) > 0)
                    {
                        var current = stats[unit.Id];
                        stats[unit.Id] = current + new Stats(BannerDefense(unit), 0, 0, 0);
                    }
                    else
                        stats[unit.Id] += new Stats(0, BannerDefense(unit), 0, 0);
                }

                // Copy the highest attack after Scarlet Rose has converted defense, including banner bonuses.
                int highestAttack = team.Max(unit => stats[unit.Id].Attack);
                foreach (var unit in team.Where(unit => Count(unit, EquipmentEffect.GreedyHand) > 0))
                {
                    var current = stats[unit.Id];
                    stats[unit.Id] = new Stats(highestAttack, current.Defense, current.SpecialDefense, current.Health);
                }

                int BannerDefense(Combatant unit) =>
                    team.Where(ally => ally.Position < unit.Position).Sum(ally => defenseFromBanners[ally.Id]);
            }

            if (living.Any(unit => Count(unit, EquipmentEffect.ImperialSeal) > 0))
            {
                var attacks = living.GroupBy(unit => unit.Character.Profession)
                    .ToDictionary(group => group.Key, group => group.Sum(unit => stats[unit.Id].Attack));
                foreach (var unit in living.Where(unit => Count(unit, EquipmentEffect.ImperialSeal) > 0))
                {
                    var current = stats[unit.Id];
                    stats[unit.Id] = new Stats(attacks[unit.Character.Profession], current.Defense, current.SpecialDefense, current.Health);
                }

                foreach (var side in living.GroupBy(unit => unit.Side))
                {
                    int highest = side.Max(unit => stats[unit.Id].Attack);
                    foreach (var unit in side.Where(unit => Count(unit, EquipmentEffect.GreedyHand) > 0 &&
                        Count(unit, EquipmentEffect.ImperialSeal) == 0))
                    {
                        var current = stats[unit.Id];
                        stats[unit.Id] = new Stats(highest, current.Defense, current.SpecialDefense, current.Health);
                    }
                }
            }

            return stats;
        }

        public static Combatant FrontOf(IEnumerable<Combatant> units, Combatant owner) =>
            units.Where(unit => unit.Side == owner.Side && unit.Health > 0 && unit.Position < owner.Position)
                .OrderByDescending(unit => unit.Position).FirstOrDefault();
    }
}
