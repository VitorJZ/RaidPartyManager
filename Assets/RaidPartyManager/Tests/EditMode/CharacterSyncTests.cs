using System;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEditor;

namespace RaidPartyManager.Tests
{
    public sealed class CharacterSyncTests
    {
        [Test]
        public void EncounterIncludesSyncedStatsRecruitsPortraitsAndBattleForms()
        {
            var scene = AssetDatabase.LoadAssetAtPath<EncounterAsset>(
                "Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();

            Assert.That(scene.Party.Select(unit => unit.Stats.Health), Is.EqualTo(new[] { 10, 7, 12, 8 }));
            Assert.That(scene.CharacterCatalog.Where(character => character.RosterNumber > 0)
                .Select(character => character.RosterNumber), Is.EquivalentTo(Enumerable.Range(1, 35)));
            Assert.That(scene.CharacterCatalog.Where(character => character.RosterNumber > 0).Select(character => character.Skill),
                Is.All.Null);
            var officer = scene.RecruitmentPool.Single(character => character.RosterNumber == 33);
            var archer = scene.RecruitmentPool.Single(character => character.RosterNumber == 34);
            var darkForm = scene.CharacterCatalog.Single(character => character.RosterNumber == 35);
            Assert.That(officer.Trait, Is.EqualTo(CharacterTrait.RevealEnemies));
            Assert.That(archer.BattleFormId, Is.EqualTo(darkForm.Id));
            Assert.That(darkForm.BattleFormId, Is.EqualTo(archer.Id));
            Assert.That(scene.PortraitFor(officer.Id), Is.Not.Null);
            Assert.That(scene.PortraitFor(archer.Id), Is.Not.Null);
            Assert.That(scene.PortraitFor(darkForm.Id), Is.Not.Null);

            foreach (var entry in scene.CharacterCatalog.Where(character => character.RosterNumber > 0))
            {
                var sprite = scene.BattleSpriteFor(entry.Id);
                var attack = scene.BattleAttackFor(entry.Id);
                var walk = scene.BattleWalkFor(entry.Id);
                Assert.That(sprite != null && sprite.width % 8 == 0 && sprite.height % 4 == 0, Is.True,
                    $"Missing or invalid idle and hit atlas for {entry.Name}.");
                Assert.That(attack != null && attack.width % 4 == 0 && attack.height % 2 == 0, Is.True,
                    $"Missing or invalid attack atlas for {entry.Name}.");
                if (entry.Reach == AttackReach.Melee)
                    Assert.That(walk != null && walk.width % 3 == 0 && walk.height % 2 == 0, Is.True,
                        $"Missing or invalid six-frame walk atlas for {entry.Name}.");
            }
        }

        [Test]
        public void DefaultDraftsUseFourDistinctUnitsFromEachIllustratedRoster()
        {
            var encounter = LoadEncounter();
            Assert.That(encounter.EnemyRoster.Select(unit => unit.Name), Is.EquivalentTo(new[]
            {
                "拨线枪手", "灰灯术徒", "拦路蛮兵", "裂陶术士", "敲钟判官", "绕后猎手", "誓线护卫", "收尸弩手", "铜铃庸医"
            }));

            foreach (var side in new[] { Side.Party, Side.Enemy })
            {
                var random = new Random(917);
                var draws = Enumerable.Range(0, 6).Select(_ => encounter.Draft(side, random)).ToArray();
                foreach (var lineup in draws)
                {
                    Assert.That(lineup.Select(unit => unit.Position), Is.EqualTo(new[] { 1, 2, 3, 4 }));
                    Assert.That(lineup.Select(unit => unit.Character.Id).Distinct().Count(), Is.EqualTo(4));
                    Assert.That(lineup.All(unit => unit.Side == side && unit.Health == unit.Stats.Health), Is.True);
                }
                Assert.That(draws.Select(lineup => string.Join(",", lineup.Select(unit => unit.Character.Id)))
                    .Distinct().Count(), Is.GreaterThan(1));
            }

            foreach (var enemy in encounter.EnemyRoster)
            {
                Assert.That(encounter.PortraitFor(enemy.Id), Is.Not.Null, enemy.Name);
                Assert.That(encounter.BattleSpriteFor(enemy.Id), Is.Not.Null, enemy.Name);
                Assert.That(encounter.BattleAttackFor(enemy.Id), Is.Not.Null, enemy.Name);
                if (enemy.Reach == AttackReach.Melee)
                    Assert.That(encounter.BattleWalkFor(enemy.Id), Is.Not.Null, enemy.Name);
            }
        }

        [Test]
        public void ThirdBellRingUsesMissingHealthAndHasWaveArtForEveryTarget()
        {
            var encounter = LoadEncounter();
            var bell = encounter.EnemyRoster.Single(unit => unit.Id == "EnemyBellMagistrate");
            Assert.That(encounter.BattleSpecialFor(bell.Id, BattleVisualCue.BellRing), Is.Not.Null);
            Assert.That(encounter.BattleEffectFor(bell.Id, BattleVisualCue.BellWave), Is.Not.Null);
            var enemy = new Combatant("bell", bell, Side.Enemy, 1, bell.BaseStats, bell.BaseStats.Health);
            var front = Fighter("front", Side.Party, 1);
            var rear = Fighter("rear", Side.Party, 4);
            var result = new BattleSimulator(random: new FixedOrder())
                .Simulate(new BattleRequest("bells", new[] { front, rear, enemy }, bell.ActionInterval * 4));

            Assert.That(result.Events.Count(entry => entry.VisualCue == BattleVisualCue.BellRing), Is.EqualTo(3));
            var wave = result.Events.Where(entry => entry.VisualCue == BattleVisualCue.BellWave).ToArray();
            Assert.That(wave.Select(entry => entry.TargetId), Is.EqualTo(new[] { front.Id, rear.Id }));
            Assert.That(wave.Select(entry => entry.Amount), Is.EqualTo(new[] { 6, 6 }));
            Assert.That(wave.All(entry => entry.Tick == bell.ActionInterval * 3), Is.True);
        }

        [Test]
        public void StarterFamiliesAddFirstHitDamageAndDrawSingleTargetAttacks()
        {
            var soldier = Unit(Load("ValiantSoldier"), "soldier", 1);
            var defender = Unit(Load("HeavyBulwark"), "defender", 2);
            var enemy = Fighter("enemy", Side.Enemy, 1, attack: 4);
            var result = new BattleSimulator(random: new FixedOrder()).Simulate(
                new BattleRequest("frontline", new[] { soldier, defender, enemy }, 20));

            var actions = result.Events.Where(entry => entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(actions.Where(entry => entry.ActorId == soldier.Id).Select(entry => entry.Amount),
                Is.EqualTo(new[] { 8, 6 }));
            Assert.That(actions.Where(entry => entry.ActorId == enemy.Id).Select(entry => entry.TargetId),
                Is.EqualTo(new[] { defender.Id, defender.Id }));
        }

        [Test]
        public void SurvivingTreasureHuntersSettleTheirGoldAndAccessory()
        {
            var goldHunter = Load("GoldHunter");
            var relicHunter = Load("RelicHunter");
            var accessory = new EquipmentDefinition("ring", "ring", default);
            var run = new PartyRun(new[]
            {
                new PartyMemberStart("gold", goldHunter, 1, goldHunter.BaseStats.Health),
                new PartyMemberStart("relic", relicHunter, 2, relicHunter.BaseStats.Health)
            }, new[] { accessory });
            var enemy = Fighter("enemy", Side.Enemy, 1, health: 40);
            var result = new BattleSimulator(random: new FixedOrder()).Simulate(run.BeginBattle(new[] { enemy }, 10, null));

            Assert.That(run.TrySettle(result, out _), Is.True);
            Assert.That(run.Gold, Is.EqualTo(5));
            Assert.That(run.Inventory.Select(item => item.Definition.Id), Is.EqualTo(new[] { accessory.Id, accessory.Id }));
        }

        [Test]
        public void ArcherPiercesForwardAndDarkFormBorrowsFromNearestRearAlly()
        {
            var archer = Unit(Load("TianGong"), "archer", 2);
            var darkForm = Load("DiSha");
            var front = Fighter("front", Side.Party, 1);
            var rear = Fighter("rear", Side.Party, 3, attack: 4);
            var enemy = Fighter("enemy", Side.Enemy, 1);
            var backEnemy = Fighter("back-enemy", Side.Enemy, 3);
            var battle = new BattleSimulator(random: new FixedOrder()).Start(
                new BattleRequest("forms", new[] { archer, front, rear, enemy, backEnemy }, 24,
                    characterCatalog: new[] { archer.Character, darkForm }));

            var first = battle.AdvanceRound();
            var arrows = first.Where(entry => entry.ActorId == archer.Id && entry.ActionKind == ActionKind.Attack).ToArray();
            Assert.That(arrows.Select(entry => entry.TargetId), Is.EqualTo(new[] { front.Id, enemy.Id, backEnemy.Id }));
            Assert.That(arrows.Select(entry => entry.Amount), Is.EqualTo(new[] { 7, 6, 5 }));
            Assert.That(first.Single(entry => entry.Kind == BattleEventKind.FormChanged).Form.Id, Is.EqualTo(darkForm.Id));

            var second = battle.AdvanceRound();
            Assert.That(second.Any(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == rear.Id), Is.False);
            Assert.That(second.Single(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == archer.Id).Amount,
                Is.EqualTo(9));
            Assert.That(second.Single(entry => entry.Kind == BattleEventKind.FormChanged).Form.Id, Is.EqualTo(archer.Character.Id));
        }

        [Test]
        public void MageGrowthEscalatesAndLightningHitsEveryEnemyOnRoundThree()
        {
            var mage = Unit(Load("StarMage"), "mage", 1);
            var chanter = Unit(Load("CalamityChanter"), "chanter", 2);
            var front = Fighter("front", Side.Enemy, 1);
            var rear = Fighter("rear", Side.Enemy, 2);
            var result = new BattleSimulator(random: new FixedOrder()).Simulate(
                new BattleRequest("growth", new[] { mage, chanter, front, rear }, 33));

            Assert.That(result.Events.Where(entry => entry.ActionName == "星辉增长").Select(entry => entry.Detail),
                Is.EqualTo(new[] { "攻击 +1", "攻击 +2", "攻击 +3" }));
            var lightning = result.Events.Where(entry => entry.ActionName == "灾厄吟唱").ToArray();
            Assert.That(lightning.Select(entry => entry.TargetId), Is.EqualTo(new[] { front.Id, rear.Id }));
            Assert.That(lightning.All(entry => entry.Tick == 33 && entry.Amount == 30 && entry.Detail == "雷电"), Is.True);
        }

        [Test]
        public void FateWeaverEvolvesForThisBattleAndChronomancerKillBonusSurvivesSettlement()
        {
            var chronomancer = Load("Chronomancer");
            var ally = Load("StarMage");
            var weaver = Load("FateWeaver");
            var enemy = Fighter("enemy", Side.Enemy, 1, health: 4);
            var run = new PartyRun(new[]
            {
                new PartyMemberStart("star", ally, 1, ally.BaseStats.Health),
                new PartyMemberStart("weaver", weaver, 2, weaver.BaseStats.Health)
            }, Array.Empty<EquipmentDefinition>());
            var simulator = new BattleSimulator(random: new FixedOrder());
            var request = run.BeginBattle(new[] { enemy }, 15, null, characterCatalog: new[] { ally, weaver, chronomancer });
            var result = simulator.Simulate(request);

            Assert.That(result.Events.First(entry => entry.Kind == BattleEventKind.FormChanged).Form.Id, Is.EqualTo(chronomancer.Id));
            Assert.That(result.PermanentAttackGains["star"], Is.EqualTo(2));
            Assert.That(run.TrySettle(result, out _), Is.True);
            var original = run.Members.Single(member => member.Id == "star");
            Assert.That(original.Character.Id, Is.EqualTo(ally.Id));
            Assert.That(original.EffectiveStats.Attack, Is.EqualTo(ally.BaseStats.Attack + 2));
        }

        private static EncounterData LoadEncounter() => AssetDatabase.LoadAssetAtPath<EncounterAsset>(
            "Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();

        private static CharacterDefinition Load(string asset) => AssetDatabase.LoadAssetAtPath<CharacterAsset>(
            $"Assets/RaidPartyManager/PrototypeData/{asset}.asset").ToDefinition();

        private static Combatant Unit(CharacterDefinition character, string id, int position) =>
            new Combatant(id, character, Side.Party, position, character.BaseStats, character.BaseStats.Health);

        private static Combatant Fighter(string id, Side side, int position, int attack = 1, int health = 100)
        {
            var stats = new Stats(attack, 0, 0, health);
            var basic = new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var character = new CharacterDefinition(id, id, Profession.Warrior, Personality.Reckless, stats, 1, basic);
            return new Combatant(id, character, side, position, stats, health);
        }

        private sealed class FixedOrder : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
            public override double NextDouble() => 0.1;
        }
    }
}
