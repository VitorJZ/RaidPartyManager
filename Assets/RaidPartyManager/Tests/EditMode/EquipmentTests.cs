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
            var stacks = encounter.StartingEquipment.Where(item => item.Effect != StrangeSeed).GroupBy(item => item.Id).ToArray();
            Assert.That(stacks, Has.Length.EqualTo(59));
            Assert.That(stacks.All(stack => stack.Count() == 4 && encounter.IconFor(stack.Key) != null), Is.True);
            var gourd = stacks.Single(stack => stack.Key == "zuixian-gourd").First();
            Assert.That((gourd.Tier, gourd.Price, gourd.CanDropInBattle), Is.EqualTo((1, 33, true)));
            Assert.That(stacks.Select(stack => stack.First().Effect).Where(effect => effect != None),
                Is.EquivalentTo(Enum.GetValues(typeof(EquipmentEffect)).Cast<EquipmentEffect>().Where(effect => effect is not (None or CoinDevourer or StrangeSeed))));
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

        [TestCase(VigorPotion, "盛气 3", "盛气 3")]
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

        [TestCase(GreedPotion, VigorPotion, 15, 15)]
        [TestCase(VigorPotion, GreedPotion, 11, 11)]
        public void AllPotionsFollowSlotOrderAndEachRepeatsWithMurkyWater(
            EquipmentEffect first, EquipmentEffect second, int firstDamage, int secondDamage)
        {
            var party = Unit("party", Side.Party);
            var run = new PartyRun(new[] { new PartyMemberStart(party.Id, party.Character, 1, party.Health) },
                Gear(first, second, MurkyWater));
            var inventory = run.Inventory;
            for (int slot = 0; slot < inventory.Count; slot++)
                Assert.That(run.TryEquip(party.Id, slot, inventory[slot].Id), Is.True);

            var request = run.BeginBattle(new[] { Unit("enemy", Side.Enemy) }, 2, null);
            var battle = new BattleSimulator(random: new FixedRoll(.9)).Start(request);
            var round = battle.AdvanceRound();
            var effects = round.Where(e => e.VisualCue == BattleVisualCue.RoundStartBlessing).ToArray();
            Assert.That(effects.Select(e => e.ActionName),
                Is.EqualTo(new[] { first.ToString(), first.ToString(), second.ToString(), second.ToString() }));
            Assert.That(round.Where(e => e.Equipment != None).Select(e => e.Equipment),
                Is.EqualTo(new[] { first, MurkyWater, first, second, MurkyWater, second }));
            Assert.That(round.ToList().FindIndex(e => e.Kind == BattleEventKind.RoundStarted),
                Is.LessThan(round.ToList().FindIndex(e => e.VisualCue == BattleVisualCue.RoundStartBlessing)));
            foreach (var entry in round.Where(e => e.Loadout != null))
                Assert.That(run.TryApplyBattleLoadout(request.Id, entry.Loadout), Is.True);
            Assert.That(run.Members.Single().EquipmentIds, Is.EqualTo(new[] { null, null, inventory[2].Id }));
            Assert.That(run.Inventory.Single().Id, Is.EqualTo(inventory[2].Id));

            var next = battle.AdvanceRound();
            Assert.That(next.Any(e => e.VisualCue == BattleVisualCue.RoundStartBlessing), Is.False);
            Assert.That(battle.Result.Events.Where(e => e.ActorId == party.Id && e.Kind == BattleEventKind.Action)
                .Select(e => e.Amount), Is.EqualTo(new[] { firstDamage, secondDamage }));
        }

        [Test]
        public void GourdCountsAlliedPotionEffectsCapsAtSixAndDoesNotStackAcrossCopies()
        {
            var wearer = Unit("wearer", Side.Party, gear: Gear(ZuixianGourd, ZuixianGourd));
            var drinker = Unit("drinker", Side.Party, 2, gear: Gear(MurkyWater, VigorPotion, PanaceaBottle));
            var foe = Unit("foe", Side.Enemy, stats: new Stats(1, 0, 0, 250), gear: Gear(GreedPotion));
            var result = new BattleSimulator(random: new FixedRoll(.1)).Simulate(
                new BattleRequest("gourd", new[] { wearer, drinker, foe }, 3));

            Assert.That(result.Events.Where(e => e.ActionName == "醉仙葫芦").Select(e => e.Tick),
                Is.EqualTo(new[] { 1, 1, 2 }));
            Assert.That(result.Events.Where(e => e.ActionName == VigorPotion.ToString()).Select(e => e.Tick),
                Is.EqualTo(new[] { 1, 1, 2, 2, 3, 3 }));
            Assert.That(result.Events.Where(e => e.ActionName == "万能药瓶").Select(e => e.Tick), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(result.Events.Where(e => e.ActorId == wearer.Id && e.Kind == BattleEventKind.Action).Select(e => e.Amount),
                Is.EqualTo(new[] { 9, 11, 11 }));
            Assert.That(result.PermanentAttackGains, Is.Empty);
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

            Assert.That(result.Events.Where(entry => entry.VisualCue == BattleVisualCue.RoundStartBlessing)
                .Select(entry => entry.TargetId), Is.EquivalentTo(new[] { front.Id, "mixer" }));
            Assert.That(result.Events.Count(entry => entry.ActionName == "消耗药水"), Is.EqualTo(1));
            Assert.That(run.TrySettle(result, out _), Is.True);
            var reward = run.Inventory.Single(item => item.Definition.IsPotion);
            Assert.That(reward.Id, Is.Not.EqualTo(dose.Id));
            Assert.That(run.Members.Single(member => member.Id == "mixer").EquipmentIds[1], Is.Null);
        }

        [Test]
        public void RedCordSharesEachPotionWithoutConsumingAnyBottleTwice()
        {
            var front = Unit("front", Side.Party, gear: Gear(VigorPotion));
            var rear = Unit("rear", Side.Party, 2, gear: Gear(RedCord, VigorPotion, VigorPotion));
            var result = Fight(front, rear, Unit("foe", Side.Enemy));

            var effects = result.Events.Where(e => e.Equipment == VigorPotion).ToArray();
            Assert.That(effects.Select(e => e.TargetId), Is.EqualTo(new[] { front.Id, front.Id, front.Id, rear.Id, rear.Id }));
            Assert.That(effects.Select(e => e.TargetStatuses), Is.EqualTo(new[] { "盛气 3", "盛气 6", "盛气 9", "盛气 3", "盛气 6" }));
            Assert.That(result.Events.Where(e => e.ActionName == "消耗药水").Select(e => e.ActorId),
                Is.EqualTo(new[] { front.Id, rear.Id, rear.Id }));
        }

        [Test]
        public void PurityPotionClearsPoisonAndEarlierTotemAtTheStartOfTheRound()
        {
            var result = Fight(Unit("party", Side.Party, gear: Gear(InsightTotem)),
                Unit("enemy", Side.Enemy, gear: Gear(PurityPotion),
                    statuses: new Dictionary<StatusKind, int> { { StatusKind.Poison, 3 } }));
            Assert.That(result.Events.Any(entry => entry.ActionName == "破绽" || entry.ActionName == "中毒"), Is.False);
            Assert.That(result.Events.Single(entry => entry.TargetId == "enemy" &&
                entry.VisualCue == BattleVisualCue.RoundStartBlessing).TargetStatuses, Is.EqualTo("纯净 2"));
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
        public void ForestMarkStacksAcrossRoundsForItsSourceAndAddsDamageOncePerHit()
        {
            var mixed = new ActionDefinition("mixed", "mixed", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                additionalDamage: new[] { new DamageComponent(DamageType.Magic, 2, false) });
            var archer = Unit("hunter", Side.Party, profession: Profession.Archer, basic: mixed,
                trait: CharacterTrait.ForestMarkOnAttack, traitPower: 1);
            var ally = Unit("ally", Side.Party, 2);
            var foe = Unit("foe", Side.Enemy, stats: new Stats(1, 0, 0, 100));
            var result = Fight(3, archer, ally, foe);

            Assert.That(result.Events.Where(e => e.ActionName == "森之印").Select(e => e.TargetStatuses),
                Is.EqualTo(new[] { "森之印 1", "森之印 2", "森之印 3" }));
            Assert.That(result.Events.Where(e => e.ActorId == archer.Id && e.Kind == BattleEventKind.Action).Select(e => e.Amount),
                Is.EqualTo(new[] { 8, 9, 10 }));
            Assert.That(result.Events.Where(e => e.ActorId == ally.Id && e.Kind == BattleEventKind.Action).Select(e => e.Amount),
                Is.EqualTo(new[] { 5, 5, 5 }));
        }

        [Test]
        public void FrozenTargetIgnoresChillUntilTheHitThatThawsIt()
        {
            var striker = Unit("striker", Side.Party, gear: Gear(SunkenFrostChain, IceRing));
            var foe = Unit("foe", Side.Enemy, statuses: new Dictionary<StatusKind, int>
                { [StatusKind.Frozen] = 1, [StatusKind.Chill] = 2 });
            Assert.That(foe.StartingStatuses.Keys, Is.EqualTo(new[] { StatusKind.Frozen }));

            var result = Fight(striker, foe);
            var chain = result.Events.Single(e => e.ActionName == "沉灯霜链");
            Assert.That(chain.TargetStatuses, Is.EqualTo("冰冻 1"));
            Assert.That(Action(result, striker.Id).TargetStatuses, Is.Empty);
            Assert.That(result.Events.Single(e => e.Equipment == IceRing).TargetStatuses, Is.EqualTo("寒冷 1"));
        }

        [Test]
        public void DirectFreezeClearsExistingChill()
        {
            var model = Unit("caster", Side.Party);
            var book = new SkillBookDefinition("absolute-zero", "绝对零度", "", SkillRequirement.None, 1, 1, SkillEffect.AbsoluteZero);
            var caster = new Combatant(model.Id, model.Character, model.Side, 1, model.Stats, model.Health, learnedSkills: new[] { book });
            var foe = Unit("foe", Side.Enemy, stats: new Stats(1, 10, 0, 30), statuses: new Dictionary<StatusKind, int> { [StatusKind.Chill] = 2 });

            var result = Fight(caster, foe);

            Assert.That(result.Events.Single(e => e.SkillBookId == book.Id && e.TargetId == foe.Id).TargetStatuses,
                Is.EqualTo("冰冻 1"));
        }

        [Test]
        public void FrozenUnitSkipsTurnsAndCanActAsSoonAsAnEnemyHitShattersTheIce()
        {
            var frozen = Unit("frozen", Side.Party, statuses: new Dictionary<StatusKind, int> { [StatusKind.Frozen] = 1 });
            var other = Unit("other", Side.Party, 2);
            var attack = new ActionDefinition("rear", "rear", ActionKind.Attack, TargetRule.RearEnemy, 0);
            var foe = Unit("foe", Side.Enemy, basic: attack);
            var blocked = Fight(2, frozen, other, foe);
            Assert.That(blocked.Events.Any(e => e.ActorId == frozen.Id && e.Kind == BattleEventKind.Action), Is.False);

            var thawed = Fight(frozen, foe);
            var resumed = Action(thawed, frozen.Id);
            Assert.That((resumed.Tick, resumed.Amount), Is.EqualTo((1, 5)));
            Assert.That(thawed.Events.Where(e => e.Kind == BattleEventKind.Action).Select(e => e.ActorId),
                Is.EqualTo(new[] { foe.Id, frozen.Id }));
            Assert.That(Action(thawed, foe.Id).Amount, Is.EqualTo(4));
            Assert.That(Action(thawed, foe.Id).TargetStatuses, Is.Empty);
        }

        [Test]
        public void BreakingIceDoesNotGrantASecondTurnToAUnitThatAlreadyActed()
        {
            var hero = Unit("hero", Side.Party, statuses: new Dictionary<StatusKind, int> { [StatusKind.Chill] = 2 });
            var freezer = Unit("freezer", Side.Enemy, gear: Gear(IceRing));
            var breaker = Unit("breaker", Side.Enemy, 2);
            var result = Fight(hero, freezer, breaker);

            Assert.That(result.Events.Any(e => e.TargetId == hero.Id && e.ActionName == "冰冻"), Is.True);
            Assert.That(Action(result, breaker.Id).TargetStatuses, Is.Empty);
            Assert.That(result.Events.Where(e => e.Kind == BattleEventKind.Action).Select(e => e.ActorId),
                Is.EqualTo(new[] { hero.Id, freezer.Id, breaker.Id }));
        }

        [Test]
        public void AUnitThawedByRoundEndPoisonCanStillUseItsTurn()
        {
            var frozen = Unit("frozen", Side.Party, statuses: new Dictionary<StatusKind, int>
                { [StatusKind.Frozen] = 1, [StatusKind.Poison] = 1 });
            var other = Unit("other", Side.Party, 2);
            var rear = new ActionDefinition("rear", "rear", ActionKind.Attack, TargetRule.RearEnemy, 0);
            var foe = Unit("foe", Side.Enemy, basic: rear);
            var result = Fight(frozen, other, foe);

            var thaw = result.Events.Single(e => e.TargetId == frozen.Id && e.ActionName == "中毒");
            Assert.That(thaw.Amount, Is.EqualTo(2));
            Assert.That(thaw.TargetStatuses, Does.Not.Contain("冰冻"));
            Assert.That(result.Events.Where(e => e.Kind == BattleEventKind.Action).Select(e => e.ActorId),
                Is.EqualTo(new[] { other.Id, foe.Id, frozen.Id }));
        }

        [Test]
        public void RoundEndGearRepeatsHealingAndAddsEmbersRetainedByEternalFire()
        {
            var actor = Unit("party", Side.Party, health: 10, gear: Gear(LifePendant, RitualMask, HealingRing));
            var result = Fight(actor, Unit("enemy", Side.Enemy, stats: new Stats(2, 0, 0, 60)));
            var heals = result.Events.Where(entry => entry.ActionName == "生命吊坠").Select(entry => entry.Amount);
            Assert.That(heals, Is.EqualTo(new[] { 4, 4 }));
            Assert.That(result.HealthById["party"], Is.EqualTo(16));

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
            Assert.That(result.Events.Where(entry => entry.Equipment != None).Select(entry => entry.Equipment),
                Is.EqualTo(new[] { first, second, RitualMask, first, second }));
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
        public void WetReactsToTheResolvedElementAndSteamHalvesOnlyTheNextAttack()
        {
            var wet = new Dictionary<StatusKind, int> { { StatusKind.Wet, 1 } };
            var foe = Unit("foe", Side.Enemy, stats: new Stats(8, 0, 0, 100), statuses: wet);
            Combatant Caster(DamageType type, params EquipmentEffect[] items) =>
                Unit("caster", Side.Party, gear: Gear(items), basic: new ActionDefinition("basic", "basic",
                    ActionKind.Attack, TargetRule.FrontEnemy, 0, damageType: type));
            Assert.That(Action(Fight(Caster(DamageType.Lightning), foe), "caster").Amount, Is.EqualTo(7));
            var dry = Fight(2, Caster(DamageType.Lightning, ElementalQuartz), foe);
            Assert.That(dry.Events.Single(e => e.ActorId == "caster" && e.TargetId == foe.Id && e.ActionName == "蒸发").TargetStatuses,
                Does.Contain("蒸发 1").And.Not.Contain("潮湿"));
            Assert.That(dry.Events.Where(e => e.ActorId == foe.Id && e.Kind == BattleEventKind.Action)
                .Select(e => e.Amount), Is.EqualTo(new[] { 4, 8 }));
            var poisoner = Unit("poisoner", Side.Party, trait: CharacterTrait.PoisonOpponents, traitPower: 2);
            var poison = Fight(poisoner, foe).Events.Single(e => e.ActionName == "幽雾炼药");
            Assert.That(poison.TargetStatuses, Does.Contain("中毒 3").And.Contain("潮湿 1"));
            var cold = Unit("cold", Side.Enemy, statuses: new Dictionary<StatusKind, int> { [StatusKind.Chill] = 2 });
            var singer = Unit("singer", Side.Party, trait: CharacterTrait.SoakEnemyEachRound, traitPower: 1);
            var reaction = Fight(singer, cold).Events.Single(e => e.ActionName == "冰冻");
            Assert.That(reaction.TargetStatuses, Is.EqualTo("冰冻 1"));
            var both = Unit("both", Side.Enemy, statuses: new Dictionary<StatusKind, int>
                { [StatusKind.Chill] = 2, [StatusKind.Wet] = 1 });
            Assert.That(both.StartingStatuses.Keys, Is.EquivalentTo(new[] { StatusKind.Frozen }));
            var frost = Fight(Caster(DamageType.Physical, IceRing), foe);
            Assert.That(frost.Events.Single(e => e.ActionName == "冰冻").TargetStatuses,
                Does.Contain("冰冻 1").And.Not.Contain("潮湿").And.Not.Contain("寒冷"));
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

        [TestCase("DawnHealer", 6)]
        [TestCase("DivinePriest", 8)]
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
            var followUp = result.Events.Single(entry => entry.ActionName == "疾风手套");
            Assert.That(followUp.Amount, Is.EqualTo(5));
            Assert.That(followUp.Equipment, Is.EqualTo(WindGlove));
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
            Assert.That(stats["rear"].Attack, Is.EqualTo(13));
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

        [TestCase(GreedyHand, ScarletRose, 15, 18)]
        [TestCase(ScarletRose, GreedyHand, 11, 14)]
        public void FormationGearIsAppliedInSlotOrderAndDoesNotReapplyEarlierCopies(
            EquipmentEffect first, EquipmentEffect second, int expectedFront, int expectedRear)
        {
            var front = Unit("front", Side.Party, stats: new Stats(4, 2, 2, 100), gear: Gear(first, second));
            var rear = Unit("rear", Side.Party, 2, stats: new Stats(11, 2, 1, 100), gear: Gear(GreedyHand, ScarletRose));
            var enemy = Unit("foe", Side.Enemy, stats: new Stats(0, 0, 0, 500), profession: Profession.Mage);
            var formation = new[] { rear, enemy, front };
            var stats = EquipmentFormation.Resolve(formation);
            Assert.That(stats[front.Id].Attack, Is.EqualTo(expectedFront));
            Assert.That(stats[rear.Id].Attack, Is.EqualTo(expectedRear));
            var battle = new BattleSimulator(random: new FixedRoll(.9)).Start(new BattleRequest("copies", formation, 4));
            for (int round = 0; round < 4; round++)
            {
                battle.AdvanceRound();
                Assert.That(battle.ReadPartyStats().ToDictionary(pair => pair.Key, pair => pair.Value),
                    Is.EquivalentTo(stats.Where(pair => pair.Key != enemy.Id)));
            }
        }

        [Test]
        public void SameTimingPassivesOfBothSidesPrecedeGearInPositionAndSlotOrder()
        {
            var front = Unit("front", Side.Party, gear: Gear(StoneArmor, CelestialGyro),
                trait: CharacterTrait.PoisonOpponents, traitPower: 1);
            var rear = Unit("rear", Side.Party, 3, gear: Gear(StoneArmor), trait: CharacterTrait.EscalatingAttack, traitPower: 1);
            var enemy = Unit("foe", Side.Enemy, 2, gear: Gear(StoneArmor, FirstFire),
                trait: CharacterTrait.PoisonOpponents, traitPower: 1);
            var result = Fight(rear, enemy, front);

            Assert.That(result.Events.Where(e => e.ActionName is "幽雾炼药" or "岩石铠甲" or "摄政星仪")
                .Select(e => (e.ActorId, e.ActionName)), Is.EqualTo(new[]
                {
                    (front.Id, "幽雾炼药"), (enemy.Id, "幽雾炼药"), (enemy.Id, "幽雾炼药"),
                    (front.Id, "岩石铠甲"), (front.Id, "摄政星仪"), (front.Id, "摄政星仪"),
                    (enemy.Id, "岩石铠甲"), (rear.Id, "岩石铠甲")
                }));
            Assert.That(result.Events.Where(e => e.ActionName is "星辉增长" or "初始之火")
                .Select(e => e.ActionName), Is.EqualTo(new[] { "星辉增长", "初始之火" }));
        }

        [TestCase(HarborWhistle, IceRing, 6)]
        [TestCase(IceRing, HarborWhistle, 8)]
        public void AfterAttackGearUsesTheStatusLeftByEarlierSlots(
            EquipmentEffect first, EquipmentEffect second, int whistleDamage)
        {
            var actor = Unit("actor", Side.Party, gear: Gear(first, second));
            var enemy = Unit("foe", Side.Enemy, statuses: new Dictionary<StatusKind, int> { [StatusKind.Wet] = 1 });
            var result = Fight(enemy, actor);
            Assert.That(result.Events.Single(entry => entry.ActionName == "封港信笛").Amount, Is.EqualTo(whistleDamage));
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
            Assert.That(result.Ratings.Single(row => row.UnitId == result.Recruits.Single().Id).CharacterId, Is.EqualTo(recruit.Id));
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
        public void ExecutionMaskAddsTrueDamageOnlyOnTheFirstAttackEachRound()
        {
            var backShot = new ActionDefinition("back", "后排", ActionKind.Attack, TargetRule.RearEnemy, 0);
            var archer = Unit("archer", Side.Party, gear: Gear(Shortbow, ExecutionMask, WindGlove),
                reach: AttackReach.Ranged, basic: backShot, stats: new Stats(8, 0, 0, 40),
                statuses: new Dictionary<StatusKind, int> { [StatusKind.Ember] = 1 });
            var front = Unit("front", Side.Enemy, stats: new Stats(2, 12, 6, 100));
            var rear = Unit("rear", Side.Enemy, 2);
            var result = new BattleSimulator(random: new FixedRoll(0.1)).Simulate(
                new BattleRequest("shortbow", new[] { archer, front, rear }, 2));

            var attacks = result.Events.Where(e => e.ActorId == archer.Id && e.Kind == BattleEventKind.Action).ToArray();
            Assert.That(attacks.Select(e => e.TargetId), Is.EqualTo(new[] { front.Id, front.Id }));
            Assert.That(attacks[0].DamageTaken, Is.EqualTo(new[]
                { (DamageType.Physical, 8), (DamageType.Fire, 3), (DamageType.True, 10) }));
            Assert.That(attacks[1].DamageTaken, Is.EqualTo(new[] { (DamageType.Physical, 8), (DamageType.True, 10) }));
            Assert.That(result.Events.Where(e => e.ActionName == "疾风手套").Select(e => e.Amount), Is.EqualTo(new[] { 8, 8 }));
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
            Assert.That(run.TrySwapEquipment(inventory[0].Id, inventory[1].Id), Is.False);
            Assert.That(run.TrySwapEquipment(inventory[1].Id, inventory[0].Id), Is.False);
            Assert.That(run.TryPlace(ally.Id, 1), Is.False);
            var enemy = Unit("enemy", Side.Enemy);
            var enemyRear = Unit("rear", Side.Enemy, 2);
            var battle = new BattleSimulator(random: new FixedRoll(0.9)).Start(
                run.BeginBattle(new[] { enemy, enemyRear }, 2, null));
            var events = battle.AdvanceRound();

            Assert.That(events.Where(entry => entry.ActionName == "古树根茎").Select(entry => entry.Amount),
                Is.EqualTo(new[] { 2, 2 }));
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
        public void TwoPanaceasAlwaysPreserveEachPotionForTheNextRound()
        {
            var actor = Unit("party", Side.Party, stats: new Stats(4, 0, 0, 30),
                gear: Gear(VigorPotion, VigorPotion, PanaceaBottle, PanaceaBottle));
            var result = Fight(2, actor, Unit("enemy", Side.Enemy, stats: new Stats(1, 2, 0, 100)));

            Assert.That(result.Events.Where(e => e.Equipment == VigorPotion).Select(e => e.Tick),
                Is.EqualTo(new[] { 1, 1, 2, 2 }));
            Assert.That(result.Events.Where(e => e.Equipment == PanaceaBottle).Select(e => e.Tick),
                Is.EqualTo(new[] { 1, 1, 2, 2 }));
            Assert.That(result.Events.Any(e => e.ActionName == "消耗药水"), Is.False);
            Assert.That(result.Events.Where(e => e.ActorId == actor.Id && e.ActionKind == ActionKind.Attack)
                .Select(e => e.Amount), Is.EqualTo(new[] { 8, 14 }));
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
        public void FallenAcolyteRestoresFullHealthOnceAndDoublesAttackAndBothDefenses()
        {
            var character = AssetDatabase.LoadAssetAtPath<CharacterAsset>(
                "Assets/RaidPartyManager/PrototypeData/FallenAcolyte.asset").ToDefinition();
            var stats = character.BaseStats;
            Assert.That((stats.Attack, stats.Defense, stats.SpecialDefense, stats.Health), Is.EqualTo((8, 2, 2, 8)));
            var attack = new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 2,
                additionalDamage: new[] { new DamageComponent(DamageType.Fixed, 3, false) });
            var hero = Unit("reborn", Side.Party, health: 1, stats: stats, basic: attack, trait: character.Trait);
            var mixedAttack = new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                additionalDamage: new[] { new DamageComponent(DamageType.Magic, 6, false) });
            var foe = Unit("wall", Side.Enemy, stats: new Stats(6, 4, 0, 100), basic: mixedAttack);
            var battle = new BattleSimulator(random: new FixedRoll(.9)).Start(new BattleRequest("rebirth", new[] { hero, foe }, 3));
            var first = battle.AdvanceRound();
            var rebirth = first.Single(e => e.VisualCue == BattleVisualCue.Rebirth);
            Assert.That((rebirth.TargetId, rebirth.TargetHealth, rebirth.MaximumHealth), Is.EqualTo((hero.Id, 8, 8)));
            var revived = battle.ReadPartyStats().Single().Value;
            Assert.That((revived.Attack, revived.Defense, revived.SpecialDefense, revived.Health), Is.EqualTo((16, 4, 4, 8)));
            var second = battle.AdvanceRound();
            Assert.That(second.Single(e => e.ActorId == hero.Id && e.Kind == BattleEventKind.Action).Amount, Is.EqualTo(17));
            Assert.That(second.Single(e => e.ActorId == foe.Id && e.Kind == BattleEventKind.Action).DamageTaken,
                Is.EqualTo(new[] { (DamageType.Physical, 2), (DamageType.Magic, 2) }));
            var last = battle.AdvanceRound();
            Assert.That(last.Any(e => e.VisualCue == BattleVisualCue.Rebirth), Is.False);
            Assert.That(battle.Result.HealthById[hero.Id], Is.Zero);
        }

        [Test]
        public void ArcherVolleyCountsDeployedAndReserveArchersFromThePartyRun()
        {
            var shooter = Unit("volley", Side.Party, profession: Profession.Archer, trait: CharacterTrait.ArcherVolley);
            var deployed = Unit("other-archer", Side.Party, 2, profession: Profession.Archer);
            var guard = Unit("guard", Side.Party, 3);
            var medic = Unit("medic", Side.Party, 4, profession: Profession.Support);
            var run = new PartyRun(new[] { shooter, deployed, guard, medic }
                .Select(unit => new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), Gear());
            Assert.That(run.TryRecruit(Unit("reserve-archer", Side.Party, profession: Profession.Archer).Character), Is.True);
            Assert.That(run.TryRecruit(Unit("reserve-guard", Side.Party).Character), Is.True);
            var foe = Unit("enemy-archer", Side.Enemy, stats: new Stats(2, 0, 0, 100), profession: Profession.Archer);
            var request = run.BeginBattle(new[] { foe }, 1, null);
            Assert.That(request.Combatants.Any(unit => unit.Character.Id == "reserve-archer"), Is.False);

            var battle = new BattleSimulator(random: new FixedRoll(.9)).Simulate(request);
            Assert.That(battle.Events.Count(e => e.ActorId == shooter.Id && e.ActionName == "逐星连射"), Is.EqualTo(2));
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
            var wearer = Unit("wearer", Side.Party, gear: Gear(ColdSoulReliquary));
            var foe = Unit("foe", Side.Enemy, stats: new Stats(8, 0, 0, 100));
            var result = Fight(wearer, Unit("near", Side.Party, 2), Unit("far", Side.Party, 4), foe);
            var events = result.Events.Where(e => e.ActionName == "冷魂圣匣").ToArray();
            Assert.That(events.Where(e => e.Amount > 0).Select(e => (e.TargetId, e.Amount)),
                Is.EquivalentTo(new[] { ("wearer", 5), ("near", 3) }));
            Assert.That(events.Count(e => e.ActorId == "wearer" && e.TargetId == "foe"), Is.EqualTo(1));
            Assert.That(result.Events.Any(e => e.ActorId == "near" && e.TargetId == "foe" && e.ActionName == "冰冻"), Is.True);
            Assert.That(result.Events.Any(e => e.ActorId == foe.Id && e.Kind == BattleEventKind.Action), Is.False);
            Assert.That(events.Any(e => e.ActorId == "far"), Is.False);

            // Without an adjacent ward, the foe can attack before the second application could freeze it.
            var duel = Fight(2, wearer, foe);
            Assert.That(duel.Events.Count(e => e.ActionName == "冷魂圣匣" && e.TargetId == foe.Id), Is.EqualTo(1));
        }

        [Test]
        public void PoisonRatingsTrackIndividualSourcesAfterDecayAndIgnoreImmunityAndStartingLayers()
        {
            var a = Unit("a", Side.Party, trait: CharacterTrait.PoisonOpponents, traitPower: 2);
            var b = Unit("b", Side.Party, 2, trait: CharacterTrait.PoisonOpponents, traitPower: 3);
            var foe = Unit("foe", Side.Enemy, stats: new Stats(1, 0, 0, 150),
                statuses: new Dictionary<StatusKind, int> { [StatusKind.Poison] = 1 });
            var immune = Unit("immune", Side.Enemy, 2, statuses: new Dictionary<StatusKind, int> { [StatusKind.Purity] = 3 });
            var ratings = Fight(2, a, b, foe, immune).Ratings.ToDictionary(r => r.UnitId);
            Assert.That((ratings["a"].DebuffLayers, ratings["a"].PoisonDamage), Is.EqualTo((4L, 6L)));
            Assert.That((ratings["b"].DebuffLayers, ratings["b"].PoisonDamage), Is.EqualTo((6L, 9L)));
            Assert.That(ratings["foe"].DamageTaken + ratings["immune"].DamageTaken,
                Is.EqualTo(ratings["a"].Damage + ratings["b"].Damage + 1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MistAlchemistPreservesBothSidesPoisonOnlyWhileAlive(bool survivesFirstRound)
        {
            var form = AssetDatabase.LoadAssetAtPath<CharacterAsset>(
                "Assets/RaidPartyManager/PrototypeData/MistAlchemist.asset").ToDefinition();
            var alchemist = new Combatant("alchemist", form, Side.Party, 1, form.BaseStats,
                survivesFirstRound ? form.BaseStats.Health : 1, startingStatuses: new Dictionary<StatusKind, int>
                {
                    [StatusKind.Poison] = 1, [StatusKind.Stunned] = 3
                });
            var statuses = new Dictionary<StatusKind, int> { [StatusKind.Poison] = 3, [StatusKind.Stunned] = 3 };
            var ally = Unit("ally", Side.Party, 2, statuses: statuses);
            var enemy = Unit("enemy", Side.Enemy, statuses: statuses);
            var rear = Unit("rear", Side.Enemy, 2, statuses: statuses);
            var battle = new BattleSimulator(random: new FixedRoll(0.9)).Start(new BattleRequest(
                "poison-aura", new[] { alchemist, ally, enemy, rear }, 2 * form.ActionInterval));
            var events = battle.AdvanceRound().Concat(battle.AdvanceRound()).ToArray();

            Assert.That(events.Where(e => e.ActionName == "幽雾炼药").Select(e => e.TargetId), Is.EqualTo(
                survivesFirstRound ? new[] { enemy.Id, rear.Id, enemy.Id, rear.Id } : new[] { enemy.Id, rear.Id }));
            Assert.That(events.Where(e => e.PoisonDamage && e.TargetId == ally.Id).Select(e => e.Amount),
                Is.EqualTo(new[] { 3, survivesFirstRound ? 3 : 2 }));
            Assert.That(events.Where(e => e.PoisonDamage && e.TargetId == enemy.Id).Select(e => e.Amount),
                Is.EqualTo(new[] { 4, survivesFirstRound ? 5 : 3 }));
        }

        [Test]
        public void VenomSurgesSelfPoisonAwardsNeitherOffenseNorTankingCredit()
        {
            var model = Unit("caster", Side.Party, stats: new Stats(1, 0, 0, 60));
            var book = new SkillBookDefinition("venom", "毒潮", "", SkillRequirement.None, 2, 1, SkillEffect.VenomSurge);
            var caster = new Combatant(model.Id, model.Character, model.Side, 1, model.Stats, model.Health, learnedSkills: new[] { book });
            var fight = Fight(caster, Unit("front", Side.Enemy), Unit("rear", Side.Enemy, 2));
            var row = fight.Ratings.Single(r => r.UnitId == caster.Id);
            int selfPoison = fight.Events.Single(e => e.PoisonDamage && e.TargetId == caster.Id).Amount;
            Assert.That(selfPoison, Is.EqualTo(2));
            Assert.That(row.DamageTaken, Is.EqualTo(caster.Health - fight.HealthById[caster.Id] - selfPoison));
            Assert.That((row.DebuffLayers, row.PoisonDamage), Is.EqualTo((12L, 12L)));
        }

        [Test]
        public void FieldMedicGetsCreditForRemovedLayersAndCleansedPoisonStopsDealingDamage()
        {
            var model = Unit("medic", Side.Party);
            var book = new SkillBookDefinition("field-medic", "战地急救", "", SkillRequirement.None, 1, 1, SkillEffect.FieldMedic);
            var medic = new Combatant(model.Id, model.Character, model.Side, 1, model.Stats, model.Health, learnedSkills: new[] { book });
            var patient = Unit("patient", Side.Party, 2, health: 22,
                statuses: new Dictionary<StatusKind, int> { [StatusKind.Poison] = 3, [StatusKind.Cursed] = 2 });
            var enemy = Unit("poisoner", Side.Enemy, trait: CharacterTrait.PoisonOpponents, traitPower: 1);
            var ratings = Fight(medic, patient, enemy).Ratings.ToDictionary(row => row.UnitId);
            Assert.That(ratings["medic"].CleansedLayers, Is.EqualTo(6));
            Assert.That(ratings["medic"].Healing, Is.EqualTo(8));
            Assert.That(ratings["poisoner"].PoisonDamage, Is.EqualTo(1));
        }

        [Test]
        public void AOneHealthPoisonKillCreditsTheDominantSourceAndNoOverkill()
        {
            var stunned = new Dictionary<StatusKind, int> { [StatusKind.Stunned] = 1 };
            var a = Unit("a", Side.Party, statuses: stunned, trait: CharacterTrait.PoisonOpponents, traitPower: 9);
            var b = Unit("b", Side.Party, 2, statuses: stunned, trait: CharacterTrait.PoisonOpponents, traitPower: 1);
            var ratings = Fight(a, b, Unit("foe", Side.Enemy, health: 1)).Ratings.ToDictionary(r => r.UnitId);
            Assert.That((ratings["a"].PoisonDamage, ratings["a"].Kills), Is.EqualTo((1L, 1)));
            Assert.That((ratings["b"].PoisonDamage, ratings["b"].Kills), Is.EqualTo((0L, 0)));
        }

        [Test]
        public void AllyShieldCreditTracksOnlyTheBarrierActuallySpentOnTheAlly()
        {
            var tank = Unit("tank", Side.Party, statuses: new Dictionary<StatusKind, int> { [StatusKind.Purity] = 3 });
            var support = Unit("support", Side.Party, 2, gear: Gear(ColdSoulReliquary));
            var enemy = Unit("foe", Side.Enemy, stats: new Stats(8, 0, 0, 100),
                statuses: new Dictionary<StatusKind, int> { [StatusKind.Purity] = 3 });
            var ratings = Fight(tank, support, enemy).Ratings.ToDictionary(r => r.UnitId);
            Assert.That((ratings["tank"].ShieldAbsorbed, ratings["tank"].DamageTaken), Is.EqualTo((3L, 5L)));
            Assert.That((ratings["support"].AllyShielding, ratings["support"].ShieldAbsorbed), Is.EqualTo((3L, 0L)));
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
            AttackReach reach = AttackReach.Unspecified, int positionSlots = 1, CharacterTrait trait = CharacterTrait.None, int traitPower = 0)
        {
            var attributes = stats ?? new Stats(side == Side.Party ? 5 : 2, 0, 0, 30);
            var definition = new CharacterDefinition(id, id, profession, Personality.Caring, attributes, 1,
                basic ?? new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 0), skill,
                reach: reach, positionSlots: positionSlots, trait: trait, traitPower: traitPower);
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
