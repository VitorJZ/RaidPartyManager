using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEditor;

namespace RaidPartyManager.Tests
{
    public sealed class PartyRunTests
    {
        private static readonly EquipmentDefinition Loot = new EquipmentDefinition("mark", "mark", new Stats(3, 0, 0, 0));
        private static readonly EquipmentDefinition Armor = new EquipmentDefinition("armor", "armor", new Stats(0, 1, 1, 2));

        [Test]
        public void EquipmentTransfersAsOneInstanceAndCannotHealThroughReequipping()
        {
            var run = MakeRun();
            var armor = run.Inventory.Single();
            Assert.That(run.TryEquip("a", 0, armor.Id), Is.True);
            Assert.That(run.Members[0].EffectiveStats.Health, Is.EqualTo(12));
            Assert.That(run.Members[0].Health, Is.EqualTo(10));
            Assert.That(run.TryEquip("b", 2, armor.Id), Is.True);
            Assert.That(run.Members[0].EquipmentIds[0], Is.Null);
            Assert.That(run.Members[0].EffectiveStats.Health, Is.EqualTo(10));
            Assert.That(run.Members[1].EquipmentIds[2], Is.EqualTo(armor.Id));
            Assert.That(run.Members[1].EffectiveStats.Defense, Is.EqualTo(3));
            Assert.That(run.Inventory.Single(), Is.SameAs(armor));
            Assert.That(run.TryEquip("b", 2, null), Is.True);
            Assert.That(run.Members[1].EquipmentIds[2], Is.Null);
        }

        [Test]
        public void EditorSuppliesRefillTheBackpackAndKeepEquippedGearAndLearnedSkills()
        {
            var run = MakeRun();
            var original = run.Inventory.Single();
            Assert.That(run.TryEquip("a", 0, original.Id), Is.True);
            var book = new SkillBookDefinition("roar", "战争咆哮", "", SkillRequirement.None, 1, 2);
            var catalog = new EquipmentDefinition[] { Armor, Armor, book };

            var release = run.SupplyEditorBackpack(catalog, 4);
            Assert.That(run.Inventory.Count, Is.EqualTo(4));
            Assert.That(run.SkillBooks.Count, Is.EqualTo(4));
            var borrowed = run.Inventory.First(item => item.Id != original.Id);
            Assert.That(run.TryEquip("b", 0, borrowed.Id), Is.True);
            Assert.That(run.TryLearnSkill("b", 0, run.SkillBooks[0].Id), Is.True);
            release();

            Assert.That(run.Inventory, Is.EquivalentTo(new[] { original, borrowed }));
            Assert.That(run.SkillBooks, Is.Empty);
            Assert.That(run.Members.Single(member => member.Id == "b").EquipmentIds[0], Is.EqualTo(borrowed.Id));
            Assert.That(run.Members.Single(member => member.Id == "b").LearnedSkills[0], Is.SameAs(book));

            release = run.SupplyEditorBackpack(catalog, 4);
            Assert.That(run.Inventory.Count, Is.EqualTo(4));
            Assert.That(run.SkillBooks.Count, Is.EqualTo(4));
            release();
            Assert.That(run.Inventory, Is.EquivalentTo(new[] { original, borrowed }));
        }

        [Test]
        public void FormationSwapsAndRequiresAllLivingMembersToBePlaced()
        {
            var run = MakeRun();
            Assert.That(run.TryPlace("a", 2), Is.True);
            Assert.That(run.Members.Select(member => member.Position), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(run.TryPlace("b", null), Is.True);
            Assert.That(run.CanStartBattle, Is.False);
            Assert.Throws<InvalidOperationException>(() => run.BeginBattle(Enemies(), 10, Loot));
            Assert.That(run.TryPlace("b", 4), Is.True);
            Assert.That(run.CanStartBattle, Is.True);
        }

        [Test]
        public void VictoryWritesHealthAndGrantsLootOnlyOnceWithoutRaisingBaseStats()
        {
            var run = MakeRun();
            var character = run.Members[0].Character;
            var armor = run.Inventory.Single();
            Assert.That(run.TryEquip("b", 0, armor.Id), Is.True);
            var battle = run.BeginBattle(Enemies(), 10, Loot);
            Assert.That(run.TryEquip("a", 0, armor.Id), Is.False);
            Assert.That(run.TryPlace("a", 3), Is.False);
            var result = new BattleResult(battle.Id, BattleOutcome.Victory,
                new Dictionary<string, int> { { "a", 7 }, { "b", 0 }, { "enemy", 0 } }, Array.Empty<BattleEvent>(),
                new Dictionary<string, int> { { "a", 2 }, { "b", 1 } });
            Assert.That(run.TrySettle(result, out var reward), Is.True);
            Assert.That(reward.Definition, Is.SameAs(Loot));
            Assert.That(run.TrySettle(result, out _), Is.False);
            Assert.That(run.Inventory.Count, Is.EqualTo(2));
            Assert.That(run.Members[0].Character, Is.SameAs(character));
            Assert.That(run.Members[0].EffectiveStats.Attack, Is.EqualTo(character.BaseStats.Attack));
            Assert.That(run.Members[0].Health, Is.EqualTo(7));
            Assert.That(run.Members[1].Health, Is.EqualTo(1));
            Assert.That(run.Members[1].Position, Is.EqualTo(1));
            Assert.That(run.Members[1].EquipmentIds[0], Is.EqualTo(armor.Id));
            Assert.That(result.HealthById["b"], Is.Zero);

            var next = run.BeginBattle(Enemies(), 10, Loot);
            Assert.That(next.Combatants.Single(unit => unit.Id == "a").Health, Is.EqualTo(7));
            Assert.That(next.Combatants.Single(unit => unit.Id == "b").Health, Is.EqualTo(1));
            Assert.That(run.TrySettle(result, out _), Is.False);
        }

        [Test]
        public void RoundBreakSwapPersistsIntoTheNextBattle()
        {
            var run = MakeRun();
            var simulator = new BattleSimulator();
            var session = simulator.Start(run.BeginBattle(Enemies(), 10, Loot));
            session.AdvanceRound();
            Assert.That(session.TrySwapPartyPositions(1, 2), Is.True);
            session.AdvanceRound();

            Assert.That(session.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(run.TrySettle(session.Result, out _), Is.True);
            Assert.That(run.Members.Select(member => member.Position), Is.EqualTo(new[] { 2, 1 }));

            var next = run.BeginBattle(Enemies(), 10, Loot);
            Assert.That(next.Combatants.Single(unit => unit.Id == "b").Position, Is.EqualTo(1));
        }

        [Test]
        public void InvalidSettlementDoesNotConsumeTheBattleAndDefeatGrantsNothing()
        {
            var run = MakeRun();
            var battle = run.BeginBattle(Enemies(), 10, Loot);
            Assert.That(run.TrySettle(Result(battle.Id, BattleOutcome.Victory, 99, 0, 0), out _), Is.False);
            Assert.That(run.Members[0].Health, Is.EqualTo(10));
            Assert.That(run.TrySettle(Result(battle.Id, BattleOutcome.PartyDefeated, 0, 0, 1), out var reward), Is.True);
            Assert.That(reward, Is.Null);
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
            Assert.That(run.Members.Select(member => member.Health), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(run.Members.Select(member => member.Position), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void SkillBooksCanOverwriteKnownSkillsAndTheNewSkillsSurviveEvolution()
        {
            var run = MakeRun();
            var roar = new SkillBookDefinition("roar", "战争咆哮", "", SkillRequirement.None, 1, 2);
            var guard = new SkillBookDefinition("shield", "坚如磐石", "", SkillRequirement.Warrior, 1, 3);
            var fire = new SkillBookDefinition("fire", "熊熊烈火", "", SkillRequirement.Mage, 2, 1);
            var first = run.AddEquipment(roar);
            var duplicate = run.AddEquipment(roar);
            var second = run.AddEquipment(guard);
            var restricted = run.AddEquipment(fire);
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
            Assert.That(run.Members[0].SkillSlotCount, Is.EqualTo(1));
            Assert.That(run.TryEquip("a", 0, first.Id), Is.False);
            Assert.That(run.TryConsumeItems(new[] { first.Id }), Is.False);
            Assert.That(run.TryLearnSkill("a", 1, first.Id), Is.False);
            Assert.That(run.TryLearnSkill("a", 0, restricted.Id), Is.False);
            Assert.That(run.SkillBooks.Count, Is.EqualTo(4));
            var battle = run.BeginBattle(Enemies(), 10, Loot);
            Assert.That(run.TryLearnSkill("a", 0, first.Id), Is.False);
            Assert.That(run.TrySettle(Result(battle.Id, BattleOutcome.Victory, 7, 0, 0), out _), Is.True);
            Assert.That(run.TryLearnSkill("a", 0, first.Id), Is.True);
            Assert.That(run.TryLearnSkill("b", 0, first.Id), Is.False);
            Assert.That(run.TryLearnSkill("a", 0, restricted.Id), Is.False);
            Assert.That(run.TryLearnSkill("a", 0, duplicate.Id), Is.False);
            Assert.That(run.TryLearnSkill("a", 0, second.Id), Is.True);
            Assert.That(run.Members[0].LearnedSkills[0], Is.SameAs(guard));
            Assert.That(run.SkillBooks.Select(book => book.Id), Is.EquivalentTo(new[] { duplicate.Id, restricted.Id }));

            var basic = run.Members[0].Character.BasicAttack;
            var evolved = new CharacterDefinition("evolved", "evolved", Profession.Warrior, Personality.Reckless,
                new Stats(8, 1, 1, 12), 1, basic, stage: 2);
            Assert.That(run.TryChangeForm("a", evolved), Is.True);
            Assert.That(run.Members[0].SkillSlotCount, Is.EqualTo(2));
            var extraGuard = run.AddEquipment(guard);
            Assert.That(run.TryLearnSkill("a", 1, extraGuard.Id), Is.False);
            Assert.That(run.TryLearnSkill("a", 1, duplicate.Id), Is.True);
            run.ResetEncounter();
            Assert.That(run.Members[0].LearnedSkills, Is.EqualTo(new[] { guard, roar }));
            Assert.That(run.SkillBooks, Is.EquivalentTo(new[] { restricted, extraGuard }));
        }

        [Test]
        public void FormChangeRetainsIdentityAndIndependentEquipment()
        {
            var run = MakeRun();
            var armor = run.Inventory.Single();
            run.TryEquip("a", 0, armor.Id);
            var form = Character("evolved", new Stats(8, 1, 1, 12));
            Assert.That(run.TryChangeForm("a", form), Is.True);
            var evolved = run.Members.First();
            Assert.That(evolved.Id, Is.EqualTo("a"));
            Assert.That(evolved.Character, Is.SameAs(form));
            Assert.That(evolved.EffectiveStats.Health, Is.EqualTo(14));
            Assert.That(evolved.EquipmentIds[0], Is.EqualTo(armor.Id));
        }

        [Test]
        public void AuthoredEncounterDemonstratesDifferentFirstRearHitAndWinsWithDefaultFormation()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset");
            Assert.That(asset, Is.Not.Null);
            var encounter = asset.ToData();
            var results = new[] { false, true }.Select(swap =>
            {
                var run = new PartyRun(encounter.Party.Select(unit =>
                    new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), encounter.StartingEquipment);
                if (swap)
                    Assert.That(run.TryPlace("guard", 4), Is.True);
                var request = run.BeginBattle(encounter.Enemies, encounter.TickLimit, encounter.VictoryReward);
                return new BattleSimulator().Simulate(request);
            }).ToArray();

            Assert.That(results[0].Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(results.Select(result => result.Events.First(entry => entry.ActorId == "flanker").TargetId),
                Is.EqualTo(new[] { "archer", "guard" }));
            Assert.That(results[0].Events.Any(entry => entry.ActionKind == ActionKind.Heal), Is.True);
            Assert.That(results[0].Events.Any(entry => entry.ActionKind == ActionKind.Shield), Is.True);
        }

        private static PartyRun MakeRun()
        {
            var hero = Character("hero", new Stats(5, 2, 2, 10));
            return new PartyRun(new[] { new PartyMemberStart("a", hero, 1, 10), new PartyMemberStart("b", hero, 2, 10) },
                new[] { Armor });
        }

        private static Combatant[] Enemies()
        {
            var enemy = Character("enemy", new Stats(5, 2, 2, 10));
            return new[] { new Combatant("enemy", enemy, Side.Enemy, 1, enemy.BaseStats, 10) };
        }

        private static CharacterDefinition Character(string id, Stats stats) => new CharacterDefinition(
            id, id, Profession.Warrior, Personality.Reckless, stats, 1,
            new ActionDefinition("basic", "basic", ActionKind.Attack, TargetRule.FrontEnemy, 0));

        private static BattleResult Result(string id, BattleOutcome outcome, int a, int b, int enemy) =>
            new BattleResult(id, outcome, new Dictionary<string, int> { {"a", a}, {"b", b}, {"enemy", enemy} },
                Array.Empty<BattleEvent>());
    }
}
