using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEditor;
using static RaidPartyManager.Domain.EquipmentEffect;

namespace RaidPartyManager.Tests
{
    public sealed class EquipmentTests
    {
        [Test]
        public void TestEncounterStocksFourOfEachBattleReadyIllustratedItem()
        {
            var encounter = AssetDatabase.LoadAssetAtPath<EncounterAsset>(
                "Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();
            var stacks = encounter.StartingEquipment.GroupBy(item => item.Id).ToArray();
            Assert.That(stacks, Has.Length.EqualTo(58));
            Assert.That(stacks.All(stack => stack.Count() == 4 && encounter.IconFor(stack.Key) != null), Is.True);
            Assert.That(stacks.Select(stack => stack.First().Effect).Where(effect => effect != None),
                Is.EquivalentTo(Enum.GetValues(typeof(EquipmentEffect)).Cast<EquipmentEffect>().Where(effect => effect is not (None or CoinDevourer))));
        }

        [Test]
        public void DeployedChantAddsASlotToHerselfAndEveryEmployeeAndReleasesTheItemsWhenBenched()
        {
            var singer = Unit("singer", Side.Party, trait: CharacterTrait.ChorusArmory);
            var ally = Unit("ally", Side.Party, 2);
            var run = new PartyRun(new[] { singer, ally }.Select(c => new PartyMemberStart(c.Id, c.Character, c.Position, c.Health)),
                Gear(EchoChalice));
            var cup = run.Inventory.Single();
            var secondCup = run.AddEquipment(cup.Definition);
            Assert.That(run.TryEquip(singer.Id, 3, secondCup.Id), Is.True);
            Assert.That(run.TryEquip(ally.Id, 3, cup.Id), Is.True);
            Assert.That(run.TryPlace(ally.Id, null), Is.True);
            Assert.That(run.Members[1].EquipmentIds[3], Is.EqualTo(cup.Id));
            Assert.That(run.TryPlace(singer.Id, null), Is.True);
            Assert.That(run.Members[1].EquipmentIds.Count, Is.EqualTo(3));
            Assert.That(run.BackpackItems.Select(item => item.Id), Is.EquivalentTo(new[] { cup.Id, secondCup.Id }));
            Assert.That(run.Members[0].EquipmentIds.Count, Is.EqualTo(3));
            Assert.That(run.TryPlace(singer.Id, 1), Is.True);
            Assert.That(run.Members[1].EquipmentIds[3], Is.Null);
            Assert.That(run.Members[0].EquipmentIds[3], Is.Null);
        }

        [Test]
        public void ChantsDeathDisablesOneItemPerEquippedAllyUntilTheNextBattle()
        {
            var singer = Unit("singer", Side.Party, stats: new Stats(1, 0, 0, 1), trait: CharacterTrait.ChorusArmory);
            var mage = Unit("mage", Side.Party, 2);
            var guard = Unit("guard", Side.Party, 3);
            var bare = Unit("bare", Side.Party, 4);
            var foe = Unit("foe", Side.Enemy, stats: new Stats(2, 0, 0, 200));
            var run = new PartyRun(new[] { singer, mage, guard, bare }.Select(c => new PartyMemberStart(c.Id, c.Character, c.Position, c.Health)),
                Gear(EchoChalice, MoneyTreeBranch));
            var cup = run.Inventory.Single(item => item.Definition.Effect == EchoChalice);
            var branch = run.Inventory.Single(item => item.Definition.Effect == MoneyTreeBranch);
            var other = run.AddEquipment(new EquipmentDefinition("unchanged", "护肩", new Stats(0, 1, 0, 0)));
            var singersCup = run.AddEquipment(cup.Definition);
            Assert.That(run.TryEquip(singer.Id, 3, singersCup.Id), Is.True);
            Assert.That(run.TryEquip(mage.Id, 0, other.Id), Is.True);
            Assert.That(run.TryEquip(mage.Id, 3, cup.Id) && run.TryEquip(guard.Id, 0, branch.Id), Is.True);
            var simulator = new BattleSimulator(random: new FixedRoll(.9));
            var battle = simulator.Start(run.BeginBattle(new[] { foe }, 3, null));
            var first = battle.AdvanceRound();
            var disabled = first.Where(e => e.ActionName == "颂歌落幕").ToArray();
            Assert.That(disabled.Select(e => e.TargetId), Is.EquivalentTo(new[] { mage.Id, guard.Id }));
            Assert.That(disabled.Last().Loadout.Disabled, Is.EquivalentTo(new[] { cup.Id, branch.Id }));
            Assert.That(battle.ReadPartyEnergy().Single(e => e.Key == mage.Id).Value.Capacity, Is.EqualTo(2));
            Assert.That(run.TryApplyBattleLoadout(battle.Id, disabled.Last().Loadout), Is.True);
            var second = battle.AdvanceRound();
            Assert.That(second.Any(e => e.ActionName == "摇钱树枝"), Is.False);
            while (battle.Result == null) battle.AdvanceRound();
            Assert.That(run.TryFinishBattle(battle.Result, out _), Is.True);
            Assert.That(run.Members.Single(m => m.Id == mage.Id).EquipmentIds[3], Is.EqualTo(cup.Id));
            var next = simulator.Start(run.BeginBattle(new[] { foe }, 1, null));
            Assert.That(next.ReadPartyEnergy().Single(e => e.Key == mage.Id).Value.Capacity, Is.EqualTo(4));
        }

        [TestCase(VigorPotion, "盛气 3", "盛气 2")]
        [TestCase(SwiftPotion, "迅捷 3", "迅捷 2")]
        [TestCase(ResolvePotion, "坚毅 3", "坚毅 2")]
        [TestCase(GreedPotion, "贪婪 1", "贪婪 1")]
        [TestCase(ThornsPotion, "荆棘 2", "荆棘 1")]
        [TestCase(PurityPotion, "纯净 2", "纯净 1")]
        public void EachPotionAppliesTheDocumentedLayersAndDecay(EquipmentEffect potion, string start, string end)
        {
            var result = Fight(Unit("party", Side.Party, gear: Gear(potion)), Unit("enemy", Side.Enemy));
            var events = result.Events.Where(entry => entry.TargetId == "party").ToArray();
            Assert.That(events.First().TargetStatuses, Is.EqualTo(start));
            Assert.That(events.Last().TargetStatuses, Is.EqualTo(end));
        }

        [TestCase(GreedPotion, VigorPotion, "盛气 10", 15)]
        [TestCase(VigorPotion, GreedPotion, "盛气 6", 11)]
        public void OpeningPotionsUseSlotOrderAndResolveRepeatedUsesTogether(
            EquipmentEffect first, EquipmentEffect second, string vigor, int damage)
        {
            var party = Unit("party", Side.Party);
            var run = new PartyRun(new[] { new PartyMemberStart(party.Id, party.Character, 1, party.Health) },
                Gear(first, second, MurkyWater));
            var inventory = run.Inventory;
            Assert.That(run.TryEquip(party.Id, 2, inventory[2].Id), Is.True);
            Assert.That(run.TryEquip(party.Id, 1, inventory[1].Id), Is.True);
            Assert.That(run.TryEquip(party.Id, 0, inventory[0].Id), Is.True);

            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy) }, 1, null);
            var battle = new BattleSimulator(random: new FixedRoll(0.9)).Start(request);
            var events = battle.AdvanceRound();
            Assert.That(run.TryApplyBattleLoadout(request.Id, battle.OpeningLoadout), Is.True);
            Assert.That(run.Inventory.Select(item => item.Id), Is.EqualTo(new[] { inventory[2].Id }));
            Assert.That(run.Members.Single().EquipmentIds, Is.EqualTo(new string[] { null, null, inventory[2].Id }));
            var opening = events.First(entry => entry.ActionName == "开战状态");
            Assert.That(opening.TargetStatuses, Does.Contain("贪婪 2").And.Contain(vigor));
            Assert.That(Action(battle.Result, "party").Amount, Is.EqualTo(damage));
            Assert.That(battle.Formation.Single(unit => unit.Id == party.Id).Equipment.Select(item => item.Effect),
                Is.EqualTo(new[] { MurkyWater }));
        }

        [Test]
        public void SharedPotionConsumesOneInstanceAndCanBeReplacedByABattleReward()
        {
            var front = Unit("front", Side.Party);
            var mixer = AssetDatabase.LoadAssetAtPath<CharacterAsset>(
                "Assets/RaidPartyManager/PrototypeData/RadiantMixer.asset").ToDefinition();
            var run = new PartyRun(new[]
            {
                new PartyMemberStart(front.Id, front.Character, 1, front.Health),
                new PartyMemberStart("mixer", mixer, 2, mixer.BaseStats.Health)
            }, Gear(RedCord, VigorPotion));
            var cord = run.Inventory.Single(item => item.Definition.Effect == RedCord);
            var dose = run.Inventory.Single(item => item.Definition.IsPotion);
            Assert.That(run.TryEquip("mixer", 0, cord.Id), Is.True);
            Assert.That(run.TryEquip("mixer", 1, dose.Id), Is.True);

            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy) }, mixer.ActionInterval, null);
            var result = new BattleSimulator(random: new FixedRoll(0.9)).Simulate(request);
            Assert.That(run.TryApplyBattleLoadout(request.Id, result.Loadout), Is.True);
            Assert.That(run.Inventory.Single().Id, Is.EqualTo(cord.Id));

            Assert.That(result.Events.Where(entry => entry.ActionName == "开战状态")
                .Select(entry => entry.TargetId), Is.EquivalentTo(new[] { front.Id, "mixer" }));
            Assert.That(result.Events.Count(entry => entry.ActionName == "消耗药水"), Is.EqualTo(1));
            Assert.That(run.TrySettle(result, out _), Is.True);
            var reward = run.Inventory.Single(item => item.Definition.IsPotion);
            Assert.That(reward.Id, Is.Not.EqualTo(dose.Id));
            Assert.That(run.Members.Single(member => member.Id == "mixer").EquipmentIds[1], Is.Null);
        }

        [Test]
        public void PurityClearsOpeningPoisonAndBlocksTotemUntilItDecays()
        {
            var result = Fight(Unit("party", Side.Party, gear: Gear(InsightTotem)),
                Unit("enemy", Side.Enemy, gear: Gear(PurityPotion),
                    statuses: new Dictionary<StatusKind, int> { { StatusKind.Poison, 3 } }));
            Assert.That(result.Events.Any(entry => entry.ActionName == "破绽" || entry.ActionName == "中毒"), Is.False);
            Assert.That(result.Events.First(entry => entry.TargetId == "enemy").TargetStatuses, Is.EqualTo("纯净 2"));
        }

        [TestCase(1, 3, 30)]
        [TestCase(2, 4, 30)]
        [TestCase(1, 4, 1)]
        public void IceRingChillsTheTargetAndUnitsTouchingItsSlots(int targetSlots, int rearPosition, int targetHealth)
        {
            var attacker = Unit("attacker", Side.Party, gear: Gear(IceRing));
            var front = Unit("front", Side.Enemy, 1, gear: Gear(StealthCloak));
            var target = Unit("target", Side.Enemy, 2, health: targetHealth, positionSlots: targetSlots);
            var rear = Unit("rear", Side.Enemy, rearPosition);
            var result = Fight(attacker, front, target, rear);

            Assert.That(Action(result, attacker.Id).TargetId, Is.EqualTo(target.Id));
            var chilled = result.Events.Where(e => e.ActorId == attacker.Id && e.ActionName == "寒冷").ToArray();
            var expected = new List<string> { front.Id };
            if (result.HealthById[target.Id] > 0) expected.Add(target.Id);
            if (rearPosition == 2 + targetSlots) expected.Add(rear.Id);
            Assert.That(chilled.Select(e => e.TargetId), Is.EquivalentTo(expected));
            Assert.That(chilled.Select(e => e.TargetStatuses), Is.All.EqualTo("寒冷 1"));
        }

        [Test]
        public void FreezingTriplesFireAndLightningIgnoresBothDefenses()
        {
            var result = Fight(Unit("chill", Side.Party, gear: Gear(IceRing, IceRing, IceRing)),
                Unit("fire", Side.Party, 2, gear: Gear(FireRing)),
                Unit("lightning", Side.Party, 3, gear: Gear(LightningRing)),
                Unit("enemy", Side.Enemy, stats: new Stats(2, 6, 2, 50)));
            Assert.That(result.Events.Any(entry => entry.TargetStatuses == "冰冻 1"), Is.True);
            Assert.That(Action(result, "fire").Amount, Is.EqualTo(9));
            Assert.That(Action(result, "fire").TargetStatuses, Is.Empty);
            Assert.That(Action(result, "lightning").Amount, Is.EqualTo(5));
        }

        [Test]
        public void RoundEndGearRepeatsHealingAndAddsEmbersRetainedByEternalFire()
        {
            var actor = Unit("party", Side.Party, health: 10, gear: Gear(LifePendant, RitualMask, HealingRing));
            var result = Fight(actor, Unit("enemy", Side.Enemy, stats: new Stats(2, 0, 0, 60)));
            var heals = result.Events.Where(entry => entry.ActionName == "生命吊坠").Select(entry => entry.Amount);
            Assert.That(heals, Is.EqualTo(new[] { 6, 6 }));
            Assert.That(result.HealthById["party"], Is.EqualTo(20));

            var withEternal = Fight(3, Unit("party", Side.Party, gear: Gear(FirstFire, RitualMask, EternalFire)),
                Unit("enemy", Side.Enemy, stats: new Stats(1, 0, 1, 100)));
            var withoutEternal = Fight(3, Unit("party", Side.Party, gear: Gear(FirstFire, RitualMask)),
                Unit("enemy", Side.Enemy, stats: new Stats(1, 0, 1, 100)));
            Assert.That(withEternal.Events.Where(entry => entry.ActorId == "party" && entry.Kind == BattleEventKind.Action)
                .Select(entry => entry.Amount), Is.EqualTo(new[] { 5, 10, 16 }));
            Assert.That(withoutEternal.Events.Where(entry => entry.ActorId == "party" && entry.Kind == BattleEventKind.Action)
                .Select(entry => entry.Amount), Is.EqualTo(new[] { 5, 10, 10 }));
        }

        [TestCase(FirstFire, LifePendant, BattleVisualCue.RoundEndEmber, BattleVisualCue.RoundEndHeal)]
        [TestCase(LifePendant, FirstFire, BattleVisualCue.RoundEndHeal, BattleVisualCue.RoundEndEmber)]
        public void RitualMaskRepeatsRoundEndGearInSlotOrder(
            EquipmentEffect first, EquipmentEffect second, BattleVisualCue firstEffect, BattleVisualCue secondEffect)
        {
            var party = Unit("party", Side.Party, health: 10, gear: Gear(first, second, RitualMask));
            var result = Fight(party, Unit("enemy", Side.Enemy));
            var triggers = result.Events.Where(entry => entry.Kind == BattleEventKind.Effect &&
                (entry.VisualCue == BattleVisualCue.RoundEndEmber || entry.VisualCue == BattleVisualCue.RoundEndHeal))
                .Select(entry => entry.VisualCue);
            Assert.That(triggers, Is.EqualTo(new[] { firstEffect, secondEffect, firstEffect, secondEffect }));
        }

        [TestCase(IceRing, WindGlove, 10)]
        [TestCase(WindGlove, IceRing, 5)]
        public void AdditionalHitSeesOnlyTheEquipmentEffectsAlreadyResolved(
            EquipmentEffect first, EquipmentEffect second, int extraDamage)
        {
            var party = Unit("party", Side.Party, gear: Gear(first, second));
            var enemy = Unit("enemy", Side.Enemy, statuses: new Dictionary<StatusKind, int> { { StatusKind.Chill, 2 } });
            var result = new BattleSimulator(random: new FixedRoll(0.1)).Simulate(new BattleRequest("gear", new[] { party, enemy }, 1));

            Assert.That(result.Events.Single(entry => entry.ActionName == "疾风手套").Amount, Is.EqualTo(extraDamage));
            Assert.That(result.Events.Count(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == party.Id),
                Is.EqualTo(1));
        }

        [Test]
        public void PhysicalLifestealPoisonHealingAndHealingReversalFollowTheirTargets()
        {
            var actor = Unit("party", Side.Party, health: 10, gear: Gear(RubyPendant, WitchDoctorDoll),
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Poison, 3 } });
            var result = Fight(actor, Unit("enemy", Side.Enemy));
            Assert.That(result.Events.Single(entry => entry.ActionName == "红水晶吊坠").Amount, Is.EqualTo(2));
            var poisonHeal = result.Events.Single(entry => entry.ActionName == "巫医玩偶");
            Assert.That(poisonHeal.Amount, Is.EqualTo(3));
            Assert.That(poisonHeal.VisualCue, Is.EqualTo(BattleVisualCue.RoundEndPoisonHeal));

            var heal = new ActionDefinition("heal", "heal", ActionKind.Heal, TargetRule.WoundedAlly, 6, 2);
            var reversed = Fight(Unit("party", Side.Party, gear: Gear(RebelEarring), skill: heal),
                Unit("enemy", Side.Enemy));
            Assert.That(Action(reversed, "party").TargetId, Is.EqualTo("enemy"));
            Assert.That(Action(reversed, "party").ActionKind, Is.EqualTo(ActionKind.Attack));
            Assert.That(Action(reversed, "party").Amount, Is.EqualTo(6));
            Assert.That(Action(reversed, "party").Detail, Is.EqualTo("魔法"));

            var reversedPendant = Fight(Unit("party", Side.Party, gear: Gear(LifePendant, RebelEarring)),
                Unit("enemy", Side.Enemy)).Events.Single(entry => entry.VisualCue == BattleVisualCue.RoundEndHeal);
            Assert.That(reversedPendant.TargetId, Is.EqualTo("enemy"));
            Assert.That(reversedPendant.ActionKind, Is.EqualTo(ActionKind.Attack));
        }

        [Test]
        public void ACompositeHitUsesSeparateDefensesAndConsumesFrozenOnceAfterAllComponents()
        {
            var basic = new ActionDefinition("mixed", "mixed", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                damageType: DamageType.Physical, additionalDamage: new[] { new DamageComponent(DamageType.Magic) });
            var attacker = Unit("party", Side.Party, stats: new Stats(8, 0, 0, 30), basic: basic,
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Ember, 2 } });
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(1, 4, 2, 100),
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Frozen, 1 } });

            var actions = Fight(2, attacker, enemy).Events
                .Where(entry => entry.ActorId == attacker.Id && entry.Kind == BattleEventKind.Action).ToArray();

            Assert.That(actions.Select(entry => entry.Amount), Is.EqualTo(new[] { 32, 10 }));
            Assert.That(actions[0].Detail, Is.EqualTo("物理 8 + 魔法 12 + 火焰 12"));
            Assert.That(actions[0].DamageTaken, Is.EqualTo(new[]
                { (DamageType.Physical, 8), (DamageType.Magic, 12), (DamageType.Fire, 12) }));
            Assert.That(actions[1].Detail, Is.EqualTo("物理 4 + 魔法 6"));

            var wounded = Unit("enemy", Side.Enemy, health: 13, stats: enemy.Stats, statuses: enemy.StartingStatuses);
            var lethal = Action(Fight(attacker, wounded), attacker.Id);
            Assert.That(lethal.Amount, Is.EqualTo(13));
            Assert.That(lethal.DamageTaken, Is.EqualTo(new[] { (DamageType.Physical, 8), (DamageType.Magic, 5) }));
            Assert.That(actions[0].TargetStatuses, Is.Empty);
        }

        [TestCase(DamageType.Physical)]
        [TestCase(DamageType.Magic)]
        public void AHitDealsAtLeastOneDamageAgainstHighDefenseAndResolve(DamageType type)
        {
            var basic = new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 0, damageType: type);
            var attacker = Unit("party", Side.Party, basic: basic);
            var target = Unit("enemy", Side.Enemy, stats: new Stats(2, 10, 10, 30),
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Resolve, 1 } });

            Assert.That(Action(Fight(attacker, target), attacker.Id).Amount, Is.EqualTo(1));
        }

        [Test]
        public void MageAttacksUseMagicWhenNoDamageTypeWasSpecified()
        {
            var mage = Unit("mage", Side.Party, profession: Profession.Mage);
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(2, 10, 1, 30));

            var action = Action(Fight(mage, enemy), mage.Id);

            Assert.That(action.Amount, Is.EqualTo(4));
            Assert.That(action.Detail, Is.EqualTo("魔法"));
        }

        [TestCase("DawnHealer", 7)]
        [TestCase("DivinePriest", 9)]
        public void HealingTraitUsesCurrentAttackEveryRoundAndRebelEarringTargetsEnemiesWithMagic(
            string assetName, int expectedHealing)
        {
            var character = AssetDatabase.LoadAssetAtPath<CharacterAsset>(
                $"Assets/RaidPartyManager/PrototypeData/{assetName}.asset").ToDefinition();
            var wounded = Unit("wounded", Side.Party, 2, health: 10);
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(1, 12, 3, 30));
            var bonus = new EquipmentDefinition("attack", "attack", new Stats(4, 0, 0, 0));
            var earring = Gear(RebelEarring).Single();
            var run = new PartyRun(new[]
            {
                new PartyMemberStart("healer", character, 1, character.BaseStats.Health),
                new PartyMemberStart(wounded.Id, wounded.Character, wounded.Position, wounded.Health)
            }, new[] { bonus, earring });
            Assert.That(run.TryEquip("healer", 0, run.Inventory.Single(item => item.Definition == bonus).Id), Is.True);
            var simulator = new BattleSimulator(random: new FixedRoll(0.9));

            var healing = simulator.Simulate(run.BeginBattle(new[] { enemy }, 2 * character.ActionInterval, null));
            var turns = healing.Events.Where(entry => entry.ActorId == "healer" && entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(turns.Select(entry => entry.TargetId), Is.EqualTo(new[] { wounded.Id, wounded.Id }));
            Assert.That(turns.Select(entry => entry.ActionKind), Is.All.EqualTo(ActionKind.Heal));
            Assert.That(turns.Select(entry => entry.Amount), Is.All.EqualTo(expectedHealing));
            Assert.That(run.TrySettle(healing, out _), Is.True);

            Assert.That(run.TryEquip("healer", 1, run.Inventory.Single(item => item.Definition == earring).Id), Is.True);
            var policy = new EnemyTargetPolicy("healer");
            var reversed = new BattleSimulator(policy, new FixedRoll(0.9)).Simulate(
                run.BeginBattle(new[] { enemy }, character.ActionInterval, null));
            var attack = Action(reversed, "healer");
            Assert.That(attack.TargetId, Is.EqualTo(enemy.Id));
            Assert.That(attack.ActionKind, Is.EqualTo(ActionKind.Attack));
            Assert.That(attack.Detail, Is.EqualTo("魔法"));
            Assert.That(attack.Amount, Is.EqualTo(expectedHealing - enemy.Stats.SpecialDefense));
        }

        private sealed class EnemyTargetPolicy : IDecisionPolicy
        {
            private readonly string actorId;
            private readonly DefaultBattlePolicy policy = new DefaultBattlePolicy();

            public EnemyTargetPolicy(string actorId) => this.actorId = actorId;

            public Decision Choose(DecisionContext context, IReadOnlyList<ActionOption> options)
            {
                if (context.Actor.Id == actorId)
                {
                    Assert.That(options.Any(option => option.Action.Kind == ActionKind.Heal), Is.True);
                    Assert.That(options.All(option => option.Target.Side == Side.Enemy && option.Kind == ActionKind.Attack),
                        Is.True);
                }
                return policy.Choose(context, options);
            }
        }

        [Test]
        public void CharmBlocksOneLethalHitAndNonStackingThornsReflectOnce()
        {
            var result = Fight(2, Unit("party", Side.Party, health: 3, gear: Gear(ElvenCharm, ThornsPotion, ThornsPotion)),
                Unit("enemy", Side.Enemy, stats: new Stats(30, 0, 0, 60)));
            var hits = result.Events.Where(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == "enemy").ToArray();
            Assert.That(result.Events.Where(entry => entry.CharmTriggered).ToArray(), Is.EqualTo(new[] { hits[0] }));
            Assert.That(hits[0].TargetHealth, Is.EqualTo(15));
            Assert.That(hits[1].TargetHealth, Is.Zero);
            Assert.That(result.Events.Count(entry => entry.ActionName == "荆棘" && entry.ActionKind == ActionKind.Attack),
                Is.EqualTo(1));
        }

        [Test]
        public void CursedCharmStillSignalsItsTargetWhenNoHealingIsPossible()
        {
            var unit = Unit("front", Side.Party, health: 1, gear: Gear(ElvenCharm),
                statuses: new Dictionary<StatusKind, int> { [StatusKind.Cursed] = 4 });
            var result = Fight(unit, Unit("enemy", Side.Enemy, stats: new Stats(40, 0, 0, 60)));
            var trigger = result.Events.Single(entry => entry.CharmTriggered);
            Assert.That((trigger.TargetId, trigger.TargetHealth), Is.EqualTo(("front", 1)));
            Assert.That(result.Events.Any(entry => entry.ActionName == "精灵护符" && entry.ActionKind == ActionKind.Heal), Is.False);
        }

        [Test]
        public void LethalHitsConsumeOnlyTheCharmThatProtectedTheTarget()
        {
            var front = Unit("front", Side.Party);
            var rear = Unit("rear", Side.Party, 2);
            var run = new PartyRun(new[]
            {
                new PartyMemberStart(front.Id, front.Character, 1, front.Health),
                new PartyMemberStart(rear.Id, rear.Character, 2, rear.Health)
            }, Gear(ElvenCharm, ElvenCharm, RedCord));
            var charms = run.Inventory.Where(item => item.Definition.Effect == ElvenCharm).ToArray();
            var cord = run.Inventory.Single(item => item.Definition.Effect == RedCord);
            Assert.That(run.TryEquip(front.Id, 2, charms[0].Id), Is.True);
            Assert.That(run.TryEquip(rear.Id, 1, charms[1].Id), Is.True);
            Assert.That(run.TryEquip(rear.Id, 0, cord.Id), Is.True);
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(20, 0, 0, 60));
            var request = run.BeginBattle(new[] { enemy }, 3, null);
            var battle = new BattleSimulator(random: new FixedRoll(0.9)).Start(request);

            Assert.That(battle.AdvanceRound().All(entry => entry.Loadout == null), Is.True);
            var personal = battle.AdvanceRound().Single(entry => entry.Loadout != null);
            Assert.That(personal.TargetHealth, Is.EqualTo(15));
            Assert.That(personal.Loadout.Consumed, Is.EqualTo(new[] { charms[0].Id }));
            Assert.That(run.TryApplyBattleLoadout(request.Id, personal.Loadout), Is.True);
            Assert.That(run.Members.Single(member => member.Id == front.Id).EquipmentIds[2], Is.Null);
            Assert.That(run.Members.Single(member => member.Id == rear.Id).EquipmentIds[1], Is.EqualTo(charms[1].Id));

            var shared = battle.AdvanceRound().Single(entry => entry.Loadout != null);
            Assert.That(personal.CharmTriggered && shared.CharmTriggered, Is.True);
            Assert.That(shared.TargetId, Is.EqualTo(front.Id));
            Assert.That(shared.TargetHealth, Is.EqualTo(15));
            Assert.That(shared.Loadout.Consumed, Is.EquivalentTo(charms.Select(item => item.Id)));
            Assert.That(run.TryApplyBattleLoadout(request.Id, shared.Loadout), Is.True);
            Assert.That(run.TrySettle(battle.Result, out _), Is.True);
            Assert.That(run.Members.Single(member => member.Id == rear.Id).EquipmentIds,
                Is.EqualTo(new[] { cord.Id, null, null }));
            Assert.That(run.Inventory.Single().Id, Is.EqualTo(cord.Id));
        }

        [Test]
        public void TotemAndFirstTargetInjuryWorkAndWindGloveAddsAHitWithinOneAction()
        {
            var result = new BattleSimulator(random: new FixedRoll(0.1)).Simulate(new BattleRequest("gear",
                new[] { Unit("party", Side.Party, gear: Gear(InsightTotem, Bludgeon, WindGlove)), Unit("enemy", Side.Enemy) }, 1));
            Assert.That(Action(result, "party").Amount, Is.EqualTo(7));
            Assert.That(result.Events.Single(entry => entry.ActionName == "疾风手套").Amount, Is.EqualTo(5));
            Assert.That(Action(result, "enemy").Detail, Is.EqualTo("未命中"));
            Assert.That(result.Events.Count(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == "party"),
                Is.EqualTo(1));
        }

        [Test]
        public void EagleEyeCountsOnlyLivingUnitsBetweenOpponents()
        {
            var result = Fight(Unit("front", Side.Party), Unit("dead", Side.Party, 2, health: 0),
                Unit("rear", Side.Party, 4, gear: Gear(EagleEye), basic: new ActionDefinition(
                    "rear-shot", "rear-shot", ActionKind.Attack, TargetRule.RearEnemy, 0)),
                Unit("enemy-front", Side.Enemy), Unit("enemy-rear", Side.Enemy, 4));
            Assert.That(Action(result, "rear").Amount, Is.EqualTo(9));
        }

        [Test]
        public void FormationPassivesUseTeamStatsAndRedCordSharesGearAndDamage()
        {
            var front = Unit("front", Side.Party, stats: new Stats(3, 3, 2, 30), gear: Gear(GuardianBanner));
            var rear = Unit("rear", Side.Party, 2, gear: Gear(GreedyHand, ScarletRose, RedCord));
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(4, 0, 0, 100));
            var stats = EquipmentFormation.Resolve(new[] { front, rear, enemy });
            Assert.That(stats["rear"].Attack, Is.EqualTo(10));
            Assert.That(stats["rear"].Defense, Is.Zero);
            Assert.That(stats["front"].Attack, Is.EqualTo(10));

            var result = Fight(front, rear, enemy);
            var enemyAction = Action(result, "enemy");
            var mirrored = result.Events.Single(entry => entry.ActionName == "红绳" && entry.Kind == BattleEventKind.Effect);
            Assert.That(mirrored.Amount, Is.EqualTo(enemyAction.Amount));
            Assert.That(mirrored.TargetId, Is.EqualTo("rear"));

            var banner = EquipmentFormation.Resolve(new[] { front, Unit("rear", Side.Party, 3) });
            Assert.That(banner["rear"].Defense, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RedCordDoesNotShareEquipmentOrMirrorDamageAcrossAVacancy(bool hasFallenAlly)
        {
            var front = Unit("front", Side.Party, stats: new Stats(3, 3, 2, 30));
            var wearer = Unit("wearer", Side.Party, 3, stats: new Stats(5, 2, 1, 30), gear: Gear(RedCord, ScarletRose));
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(6, 0, 0, 100));
            var lineup = new List<Combatant> { front, wearer, enemy };
            if (hasFallenAlly)
                lineup.Add(Unit("fallen", Side.Party, 2, health: 0));

            var stats = EquipmentFormation.Resolve(lineup);
            Assert.That(stats[front.Id], Is.EqualTo(front.Stats));
            Assert.That(stats[wearer.Id].Attack, Is.EqualTo(8));
            var result = Fight(lineup.ToArray());
            Assert.That(Action(result, front.Id).Amount, Is.EqualTo(3));
            Assert.That(result.Events.Any(entry => entry.ActionName == "红绳"), Is.False);
            Assert.That(result.HealthById[wearer.Id], Is.EqualTo(wearer.Health));
        }

        [Test]
        public void GreedyHandCopiesAllysAttackAfterScarletRoseConversion()
        {
            var roseHolder = Unit("rose", Side.Party, stats: new Stats(4, 5, 3, 30));
            var handHolder = Unit("hand", Side.Party, 2);
            var run = new PartyRun(new[] { roseHolder, handHolder }.Select(unit =>
                new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), Gear(ScarletRose, GreedyHand));
            var inventory = run.Inventory;
            Assert.That(run.TryEquip(roseHolder.Id, 0, inventory.Single(item => item.Definition.Effect == ScarletRose).Id), Is.True);
            Assert.That(run.TryEquip(handHolder.Id, 0, inventory.Single(item => item.Definition.Effect == GreedyHand).Id), Is.True);

            var members = run.Members;
            Assert.That(members.Single(member => member.Id == roseHolder.Id).EffectiveStats.Attack, Is.EqualTo(12));
            Assert.That(members.Single(member => member.Id == handHolder.Id).EffectiveStats.Attack, Is.EqualTo(12));

            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy) }, 1, null);
            var result = new BattleSimulator(random: new FixedRoll(0.9)).Simulate(request);
            Assert.That(Action(result, handHolder.Id).Amount, Is.EqualTo(12));
        }

        [Test]
        public void RecruitmentNoticeSummonsIntoVacancyAndSurvivorJoinsAtSettlement()
        {
            var fighter = Unit("fighter", Side.Party);
            var recruit = Unit("candidate", Side.Party).Character;
            var run = new PartyRun(new[] { new PartyMemberStart(fighter.Id, fighter.Character, 1, fighter.Health) },
                Gear(RecruitmentNotice));
            Assert.That(run.TryEquip(fighter.Id, 0, run.Inventory.Single().Id), Is.True);
            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy, health: 8) }, 3, null, new[] { recruit });
            var result = new BattleSimulator(random: new FixedRoll(0.9)).Simulate(request);
            Assert.That(result.Events.Any(entry => entry.ActionName == "寻人启事"), Is.True);
            Assert.That(result.Recruits.Single().Position, Is.EqualTo(2));
            Assert.That(run.TrySettle(result, out _), Is.True);
            Assert.That(run.Members.Single(member => member.Id != fighter.Id).Character, Is.SameAs(recruit));
            Assert.That(run.Inventory.Any(i => i.Definition.Effect == RecruitmentNotice), Is.False);
        }

        [Test]
        public void UnusedNoticeSurvivesWhenNoRecruitIsAvailable()
        {
            var fighter = Unit("fighter", Side.Party);
            var run = new PartyRun(new[] { new PartyMemberStart(fighter.Id, fighter.Character, 1, fighter.Health) },
                Gear(RecruitmentNotice));
            var notice = run.Inventory.Single().Id;
            Assert.That(run.TryEquip(fighter.Id, 0, notice), Is.True);
            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy, health: 8) }, 3, null);
            var result = new BattleSimulator(random: new FixedRoll(.9)).Simulate(request);
            Assert.That(result.Recruits, Is.Empty);
            Assert.That(run.TrySettle(result, out _), Is.True);
            Assert.That(run.Inventory.Any(i => i.Id == notice), Is.True);
        }

        [Test]
        public void RecruitmentFillsAnOpenFormationSlotWithoutLosingReserveEquipment()
        {
            var character = Unit("fighter", Side.Party).Character;
            var party = Enumerable.Range(1, 4).Select(slot =>
                new PartyMemberStart($"fighter-{slot}", character, slot, character.BaseStats.Health));
            var run = new PartyRun(party, Gear(RecruitmentNotice, LifePendant));
            var inventory = run.Inventory;
            run.TryEquip("fighter-1", 0, inventory[0].Id);
            run.TryEquip("fighter-4", 0, inventory[1].Id);
            Assert.That(run.TryPlace("fighter-4", null), Is.True);
            Assert.That(run.CanStartBattle, Is.True);

            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy, health: 7) }, 3, null, new[] { character });
            var result = new BattleSimulator(random: new FixedRoll(0.9)).Simulate(request);
            Assert.That(run.TrySettle(result, out _), Is.True);
            Assert.That(run.Members.Count, Is.EqualTo(5));
            Assert.That(run.Members.Single(member => member.Position == 4).Id, Is.EqualTo(result.Recruits.Single().Id));
            Assert.That(run.Inventory, Does.Contain(inventory[1]));
            Assert.That(run.Members.Single(m => m.Id == "fighter-4").EquipmentIds, Does.Contain(inventory[1].Id));

            var recruitId = result.Recruits.Single().Id;
            Assert.That(run.TryEquip(recruitId, 0, inventory[1].Id), Is.True);
            run.ResetEncounter();
            Assert.That(run.Members.Single(member => member.Id == recruitId).EquipmentIds[0], Is.EqualTo(inventory[1].Id));
        }

        [Test]
        public void DodgeAndNonStackingDamageReductionApplyDuringAttacks()
        {
            var hero = Unit("hero", Side.Party, gear: Gear(SwiftPotion));
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(8, 0, 0, 30));
            var dodge = new BattleSimulator(random: new FixedRoll(0.1)).Simulate(
                new BattleRequest("dodge", new[] { hero, enemy }, 1));
            Assert.That(Action(dodge, "enemy").Detail, Is.EqualTo("未命中"));
            Assert.That(dodge.HealthById["hero"], Is.EqualTo(hero.Health));

            var defended = Unit("hero", Side.Party, gear: Gear(ResolvePotion, ResolvePotion));
            var result = Fight(defended, enemy);
            Assert.That(Action(result, "enemy").Amount, Is.EqualTo(4));
            Assert.That(Action(result, "enemy").TargetStatuses, Is.EqualTo("坚毅 6"));
        }

        [Test]
        public void BaguaAlternatesBetweenChillAndFireAndQuartzConvertsBeforeThunderBonus()
        {
            var odd = Unit("odd", Side.Party, gear: Gear(Bagua));
            var even = Unit("even", Side.Party, 2, gear: Gear(Bagua, ElementalQuartz, ThunderEye));
            var enemy = Unit("enemy", Side.Enemy, statuses: new Dictionary<StatusKind, int> { [StatusKind.Chill] = 1 });
            var result = Fight(odd, even, enemy);

            Assert.That(result.Events.Any(entry => entry.ActorId == odd.Id && entry.ActionName == "寒冷"), Is.True);
            Assert.That(Action(result, even.Id).Detail, Is.EqualTo("物理 5 + 雷电 5"));
            Assert.That(Action(result, even.Id).Amount, Is.EqualTo(10));
            Assert.That(Action(result, even.Id).TargetStatuses, Is.EqualTo("寒冷 1"));
        }

        [Test]
        public void ColdAmplifiesFirePartsAndIsConsumedBeforeTheNextAttack()
        {
            var fire = Unit("fire", Side.Party, gear: Gear(FireRing));
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(2, 0, 1, 50),
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Chill, 2 } });
            var actions = Fight(2, fire, enemy).Events
                .Where(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == fire.Id).ToArray();

            Assert.That(actions.Select(entry => entry.Amount), Is.EqualTo(new[] { 8, 4 }));
            Assert.That(actions[0].TargetStatuses, Is.Empty);
        }

        [Test]
        public void CloakSkipsHiddenEnemiesAndCountersDoNotChain()
        {
            var party = Unit("party", Side.Party, gear: Gear(CounterHorn));
            var hidden = Unit("hidden", Side.Enemy, gear: Gear(StealthCloak));
            var visible = Unit("visible", Side.Enemy, 2, gear: Gear(CounterHorn));
            var result = Fight(party, hidden, visible);

            Assert.That(Action(result, party.Id).TargetId, Is.EqualTo(visible.Id));
            Assert.That(result.Events.Count(entry => entry.ActionName == "反击的号角"), Is.EqualTo(3));

            var secondCloak = Unit("second", Side.Enemy, 2, gear: Gear(StealthCloak));
            Assert.That(Action(Fight(party, hidden, secondCloak), party.Id).TargetId, Is.EqualTo(hidden.Id));
        }

        [Test]
        public void ResistantFrontCanStillBeHitByAShortbowAndAnExecutionReplacesOnlyTheFirstAttack()
        {
            var backShot = new ActionDefinition("back", "后排", ActionKind.Attack, TargetRule.RearEnemy, 0);
            var archer = Unit("archer", Side.Party, gear: Gear(Shortbow, ExecutionMask, WindGlove),
                reach: AttackReach.Ranged, basic: backShot, stats: new Stats(8, 0, 0, 40));
            var front = Unit("front", Side.Enemy, stats: new Stats(2, 12, 6, 40));
            var rear = Unit("rear", Side.Enemy, 2);
            var result = new BattleSimulator(random: new FixedRoll(0.1)).Simulate(
                new BattleRequest("shortbow", new[] { archer, front, rear }, 1));

            Assert.That(Action(result, archer.Id).TargetId, Is.EqualTo(front.Id));
            Assert.That(Action(result, archer.Id).Amount, Is.EqualTo(4));
            Assert.That(result.Events.Single(entry => entry.ActionName == "疾风手套").Amount, Is.EqualTo(8));
        }

        [Test]
        public void RootCannotBeUnequippedOrMovedAndActualHealingDamagesEveryEnemy()
        {
            var party = Unit("party", Side.Party, health: 10);
            var ally = Unit("ally", Side.Party, 2);
            var run = new PartyRun(new[] { party, ally }.Select(unit =>
                new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), Gear(TreeRoot, LifePendant));
            var inventory = run.Inventory;
            Assert.That(run.TryEquip(party.Id, 0, inventory[0].Id), Is.True);
            Assert.That(run.TryEquip(party.Id, 1, inventory[1].Id), Is.True);
            Assert.That(run.TryEquip(party.Id, 0, null), Is.False);
            Assert.That(run.TryEquip(ally.Id, 0, inventory[0].Id), Is.False);
            Assert.That(run.TryPlace(ally.Id, 1), Is.False);
            var enemy = Unit("enemy", Side.Enemy);
            var enemyRear = Unit("rear", Side.Enemy, 2);
            var battle = new BattleSimulator(random: new FixedRoll(0.9)).Start(
                run.BeginBattle(new[] { enemy, enemyRear }, 2, null));
            var events = battle.AdvanceRound();

            Assert.That(events.Where(entry => entry.ActionName == "古树根茎").Select(entry => entry.Amount),
                Is.EqualTo(new[] { 3, 3 }));
        }

        [Test]
        public void MegaphoneMovesTheExistingInstanceAndGrantsStatsToItsNextHolder()
        {
            var first = Unit("first", Side.Party);
            var second = Unit("second", Side.Party, 2, health: 10);
            var run = new PartyRun(new[] { first, second }.Select(unit =>
                new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), Gear(Megaphone));
            string instance = run.Inventory.Single().Id;
            Assert.That(run.TryEquip(first.Id, 2, instance), Is.True);
            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy, stats: new Stats(1, 0, 0, 100)) }, 2, null);
            var session = new BattleSimulator(random: new FixedRoll(0.9)).Start(request);
            var cycle = session.AdvanceRound();
            Assert.That(run.TryApplyBattleLoadout(request.Id, cycle.Single(entry => entry.Loadout != null).Loadout), Is.True);
            Assert.That(run.Members.Single(unit => unit.Id == second.Id).EquipmentIds[0], Is.EqualTo(instance));
            var next = session.AdvanceRound();
            Assert.That(next.Any(entry => entry.ActorId == second.Id && entry.ActionName == "传话筒" &&
                entry.ActionKind == ActionKind.Heal && entry.Amount == 1), Is.True);
            Assert.That(next.Single(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == second.Id).Amount, Is.EqualTo(6));
            Assert.That(run.TrySettle(session.Result, out _), Is.True);
            Assert.That(run.Inventory.Single().Id, Is.EqualTo(instance));
            Assert.That(run.Members.Single(unit => unit.Id == first.Id).EquipmentIds[0], Is.EqualTo(instance));
        }

        [TestCase(0.1, true)]
        [TestCase(0.9, false)]
        public void PanaceaRollsOnceAndPreservesTheSamePotionInstanceWhenSuccessful(double roll, bool saved)
        {
            var actor = Unit("party", Side.Party);
            var run = new PartyRun(new[] { new PartyMemberStart(actor.Id, actor.Character, 1, actor.Health) },
                Gear(VigorPotion, MurkyWater, PanaceaBottle));
            var items = run.Inventory;
            for (int slot = 0; slot < items.Count; slot++)
                Assert.That(run.TryEquip(actor.Id, slot, items[slot].Id), Is.True);
            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy) }, 1, null);
            var result = new BattleSimulator(random: new FixedRoll(roll)).Simulate(request);

            Assert.That(Action(result, actor.Id).Amount, Is.EqualTo(11));
            Assert.That(run.TrySettle(result, out _), Is.True);
            Assert.That(run.Inventory.Any(item => item.Id == items[0].Id), Is.EqualTo(saved));
            Assert.That(run.Members.Single().EquipmentIds[0], Is.EqualTo(saved ? items[0].Id : null));
        }

        [Test]
        public void LoneWolfAndImperialSealUseLivingFormationAndPrismCountsStatusLayers()
        {
            var solo = Unit("solo", Side.Party, gear: Gear(LoneWolf, ImperialSeal));
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(3, 0, 0, 50));
            Assert.That(Action(Fight(solo, enemy), solo.Id).Amount, Is.EqualTo(20));

            var prism = Unit("prism", Side.Party, gear: Gear(StaticPrism, ThunderEye),
                statuses: new Dictionary<StatusKind, int> { { StatusKind.Resolve, 2 } });
            var chilled = Unit("enemy", Side.Enemy, statuses: new Dictionary<StatusKind, int> { { StatusKind.Chill, 1 } });
            Assert.That(Action(Fight(prism, chilled), prism.Id).Amount, Is.EqualTo(14));
        }

        [Test]
        public void LightningRingBypassesBothDefensesWhilePhysicalAttacksStillUseDefense()
        {
            var wall = Unit("wall", Side.Enemy, stats: new Stats(1, 40, 70, 100));
            var attacker = Unit("ring", Side.Party, gear: Gear(LightningRing));
            Assert.That(Action(Fight(attacker, wall), attacker.Id).Amount, Is.EqualTo(attacker.Stats.Attack));
            Assert.That(Action(Fight(Unit("plain", Side.Party), wall), "plain").Amount, Is.EqualTo(1));
        }

        [Test]
        public void RebirthDoublesAttackBeforeDefenseAndLeavesFlatDamageAlone()
        {
            var attack = new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 2,
                additionalDamage: new[] { new DamageComponent(DamageType.Fixed, 3, false) });
            var hero = Unit("reborn", Side.Party, health: 1, basic: attack, trait: CharacterTrait.Rebirth);
            var foe = Unit("wall", Side.Enemy, stats: new Stats(3,4,0,100));
            var battle = new BattleSimulator(random: new FixedRoll(.9)).Start(new BattleRequest("rebirth", new[] { hero, foe }, 3));
            battle.AdvanceRound();
            Assert.That(battle.ReadPartyStats().Single().Value.Attack, Is.EqualTo(10));
            var renewed = battle.AdvanceRound().First(e => e.ActorId == hero.Id && e.Kind == BattleEventKind.Action);
            Assert.That(renewed.Amount, Is.EqualTo(11)); // (5 × 2 + 2 - 4) + 3
        }

        [Test]
        public void ArcherDyingDuringVolleyRemovesTheRemainingBonusShot()
        {
            var shooter = Unit("volley", Side.Party, profession: Profession.Archer, trait: CharacterTrait.ArcherVolley);
            var linked = Unit("linked", Side.Party, 2, health: 3, profession: Profession.Archer, gear: Gear(RedCord));
            var third = Unit("third", Side.Party, 3, profession: Profession.Archer);
            var foe = Unit("counter", Side.Enemy, stats: new Stats(2,0,0,100), gear: Gear(CounterHorn));
            var battle = Fight(shooter, linked, third, foe);
            Assert.That(battle.HealthById[linked.Id], Is.Zero);
            Assert.That(battle.Events.Count(e => e.ActorId == shooter.Id && e.ActionName == "逐星连射"), Is.EqualTo(1));
        }

        [Test]
        public void ChalicesAddTwoEnergyPerCopyUpToEight()
        {
            for (int count = 0; count <= 4; count++)
            {
                var gear = Gear(Enumerable.Repeat(EchoChalice, count).ToArray());
                Assert.That(BattleEnergy.Full(gear).Capacity, Is.EqualTo(Math.Min(2 + count * 2, 8)));
            }
        }

        [Test]
        public void LionStatueBuffsAdjacentAlliesAndBranchGoldSettles()
        {
            var attacker = Unit("attacker", Side.Party, gear: Gear(MoneyTreeBranch));
            var holder = Unit("holder", Side.Party, 2, gear: Gear(LionGoldStatue));
            var far = Unit("far", Side.Party, 4);
            var enemy = Unit("enemy", Side.Enemy, stats: new Stats(1, 0, 0, 60));
            var result = new BattleSimulator(random: new FixedRoll(0.9)).Simulate(
                new BattleRequest("gold-statue", new[] { attacker, holder, far, enemy }, 1, partyGold: 100));

            Assert.That(Action(result, attacker.Id).Amount, Is.EqualTo(attacker.Stats.Attack * 2));
            Assert.That(Action(result, holder.Id).Amount, Is.EqualTo(holder.Stats.Attack));
            Assert.That(Action(result, far.Id).Amount, Is.EqualTo(far.Stats.Attack));
            Assert.That(result.GoldEarned, Is.EqualTo(2));
        }

        [Test]
        public void CrystalRestoresMagicLifeLossAndOrbBlocksOnlyTheNonphysicalComponent()
        {
            var basic = new ActionDefinition("mix", "mix", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                damageType: DamageType.Physical, additionalDamage: new[] { new DamageComponent(DamageType.Magic) });
            var caster = Unit("caster", Side.Party, gear: Gear(ArcaneCrystal, WindChime),
                basic: basic, stats: new Stats(5, 0, 0, 30), health: 10);
            var defender = Unit("enemy", Side.Enemy, stats: new Stats(1, 2, 3, 30));

            var ordinary = Fight(caster, defender);
            Assert.That(Action(ordinary, caster.Id).Amount, Is.EqualTo(7));
            Assert.That(ordinary.Events.Single(entry => entry.ActionName == "魔导水晶").Amount, Is.EqualTo(1));

            var insulated = Unit("enemy", Side.Enemy, gear: Gear(InsulatingOrb), stats: defender.Stats);
            var mixed = Fight(caster, insulated);
            Assert.That(Action(mixed, caster.Id).Amount, Is.EqualTo(4));
            Assert.That(mixed.Events.Any(entry => entry.ActionName == "魔导水晶"), Is.False);
        }

        [Test]
        public void StoneArmorAddsTwoShieldAtTheStartOfEveryRound()
        {
            var result = Fight(3, Unit("wearer", Side.Party, gear: Gear(StoneArmor)), Unit("foe", Side.Enemy));
            Assert.That(result.Events.Where(e => e.ActionName == "岩石铠甲").Select(e => e.Amount),
                Is.EqualTo(new[] { 2, 2, 2 }));
        }

        [Test]
        public void AdmiralWhistleStrikesTheLastEnemyOncePerRoundAndFrostChainChillsAllAtOpening()
        {
            var result = Fight(2, Unit("wearer", Side.Party, gear: Gear(HarborWhistle, SunkenFrostChain)),
                Unit("front", Side.Enemy), Unit("rear", Side.Enemy, 4));
            var whistle = result.Events.Where(e => e.ActionName == "封港信笛").ToArray();
            Assert.That(whistle.Select(e => e.TargetId), Is.EqualTo(new[] { "rear", "rear" }));
            Assert.That(whistle.All(e => e.DamageTaken.SequenceEqual(new[] { (DamageType.Lightning, 4) })), Is.True);
            var chain = result.Events.Where(e => e.ActionName == "沉灯霜链").ToArray();
            Assert.That(chain.Select(e => e.TargetId), Is.EquivalentTo(new[] { "front", "rear" }));
            Assert.That(chain.All(e => e.TargetStatuses?.Contains("寒冷 2") == true), Is.True);
        }

        [TestCase(15, 1)]
        [TestCase(14, 2)]
        public void CrownTriggersOnceAndOnlyStrictlyBelowHalf(int health, int shields)
        {
            var result = Fight(2, Unit("king", Side.Party, health: health, gear: Gear(FacelessCrown)),
                Unit("foe", Side.Enemy, stats: new Stats(1, 0, 0, 30)));
            var events = result.Events.Where(e => e.ActionName == "无面王冠").ToArray();
            Assert.That(events.Count(e => e.Amount == 5), Is.EqualTo(shields));
            Assert.That(events.Any(e => e.TargetStatuses?.Contains("盛气 2") == true), Is.EqualTo(shields == 2));
        }

        [Test]
        public void ReliquaryProtectsTheWearerAndAdjacentAllyAndItsChillEndsWhenShieldBreaks()
        {
            var result = Fight(2, Unit("wearer", Side.Party, gear: Gear(ColdSoulReliquary)),
                Unit("near", Side.Party, 2), Unit("far", Side.Party, 4),
                Unit("foe", Side.Enemy, stats: new Stats(8, 0, 0, 100)));
            var events = result.Events.Where(e => e.ActionName == "冷魂圣匣").ToArray();
            Assert.That(events.Where(e => e.Amount > 0).Select(e => (e.TargetId, e.Amount)),
                Is.EquivalentTo(new[] { ("wearer", 5), ("near", 3) }));
            Assert.That(events.Count(e => e.ActorId == "wearer" && e.TargetId == "foe"), Is.EqualTo(1));
            Assert.That(events.Any(e => e.ActorId == "near" && e.TargetId == "foe"), Is.True);
            Assert.That(events.Any(e => e.ActorId == "far"), Is.False);
        }

        private static BattleEvent Action(BattleResult result, string actor) =>
            result.Events.First(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == actor);

        private static BattleResult Fight(params Combatant[] units) => Fight(1, units);

        private static BattleResult Fight(int rounds, params Combatant[] units) =>
            new BattleSimulator(random: new FixedRoll(0.9)).Simulate(new BattleRequest("gear", units, rounds));

        private static EquipmentDefinition[] Gear(params EquipmentEffect[] items) =>
            items.Select(effect => new EquipmentDefinition(effect.ToString(), effect.ToString(), default, effect: effect)).ToArray();

        private static Combatant Unit(string id, Side side, int position = 1, int? health = null, Stats? stats = null,
            EquipmentDefinition[] gear = null, IReadOnlyDictionary<StatusKind, int> statuses = null,
            ActionDefinition skill = null, ActionDefinition basic = null, Profession profession = Profession.Warrior,
            AttackReach reach = AttackReach.Unspecified, int positionSlots = 1, CharacterTrait trait = CharacterTrait.None)
        {
            var attributes = stats ?? new Stats(side == Side.Party ? 5 : 2, 0, 0, 30);
            var definition = new CharacterDefinition(id, id, profession, Personality.Caring, attributes, 1,
                basic ?? new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 0), skill,
                reach: reach, positionSlots: positionSlots, trait: trait);
            return new Combatant(id, definition, side, position, attributes, health ?? attributes.Health, gear, statuses);
        }

        private sealed class FixedRoll : Random
        {
            private readonly double roll;

            public FixedRoll(double roll) => this.roll = roll;
            public override int Next(int maxValue) => maxValue - 1;
            public override double NextDouble() => roll;
        }
    }
}
