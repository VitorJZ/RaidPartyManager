using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace RaidPartyManager.Tests
{
    public sealed class GraveyardTests
    {
        private const string Path = "Assets/RaidPartyManager/PrototypeData/WhisperingGraveyardEncounter.asset";
        private static readonly ActionDefinition Basic = new("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);

        private static EncounterData Load() => AssetDatabase.LoadAssetAtPath<EncounterAsset>(Path).ToData();

        [Test]
        public void GraveyardHasExactlyThirtyDistinctEnemiesWithCorrectRanksAndSlotBudgets()
        {
            var content = Load();
            Assert.That(content.EnemyRoster.Count, Is.EqualTo(30));
            Assert.That(content.EnemyRoster.Select(enemy => enemy.Id).Distinct().Count(), Is.EqualTo(30));
            Assert.That(content.EnemyRoster.Count(enemy => enemy.Rank == EnemyRank.Normal), Is.EqualTo(23));
            Assert.That(content.EnemyRoster.Count(enemy => enemy.Rank == EnemyRank.Elite), Is.EqualTo(5));
            Assert.That(content.EnemyRoster.Count(enemy => enemy.Rank == EnemyRank.Boss), Is.EqualTo(2));
            foreach (var enemy in content.EnemyRoster)
            {
                Assert.That(enemy.PositionSlots, Is.EqualTo(enemy.Rank == EnemyRank.Normal ? 1 : enemy.Rank == EnemyRank.Elite ? 2 : 3));
                Assert.That(enemy.EnemyBehavior, Is.Not.Null, enemy.Name);
                Assert.That(enemy.EnemyBehavior.Actions.Sum(action => action.Weight), Is.EqualTo(100), enemy.Name);
            }
            var dock = AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();
            Assert.That(dock.EnemyRoster.Count, Is.EqualTo(23), "The dock must keep its own region pool.");
            Assert.That(dock.EnemyRoster.Select(enemy => enemy.Id).Intersect(content.EnemyRoster.Select(enemy => enemy.Id)), Is.Empty);
        }

        [Test]
        public void EveryGraveyardEnemyHasImportedTransparentPortraitAndAllRuntimeAnimationAtlases()
        {
            var content = Load();
            foreach (var enemy in content.EnemyRoster)
            {
                foreach (var texture in new[] { content.PortraitFor(enemy.Id), content.BattleSpriteFor(enemy.Id),
                             content.BattleAttackFor(enemy.Id), content.BattleWalkFor(enemy.Id),
                             content.BattleSpecialFor(enemy.Id, BattleVisualCue.GraveyardSpecial),
                             content.BattleEffectFor(enemy.Id, BattleVisualCue.GraveyardSpecial) })
                {
                    Assert.That(texture, Is.Not.Null, enemy.Name);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                    Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True, enemy.Name);
                    Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), enemy.Name);
                    Assert.That(importer.mipmapEnabled, Is.False, enemy.Name);
                    Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None), enemy.Name);
                }
                Assert.That((content.BattleSpriteFor(enemy.Id).width, content.BattleSpriteFor(enemy.Id).height), Is.EqualTo((3072, 768)));
                Assert.That((content.BattleAttackFor(enemy.Id).width, content.BattleAttackFor(enemy.Id).height), Is.EqualTo((1024, 768)));
                var effect = content.BattleEffectFor(enemy.Id, BattleVisualCue.GraveyardSpecial);
                Assert.That(effect.width, Is.EqualTo(16 * effect.height), enemy.Name);
                Assert.That((content.BattleWalkFor(enemy.Id).width, content.BattleWalkFor(enemy.Id).height), Is.EqualTo((768, 768)));
            }
        }

        [Test]
        public void EveryAuthoredActionCanBeSelectedAndResolvedUsingItsRealProfile()
        {
            foreach (var enemy in Load().EnemyRoster.Where(e => e.Rank != EnemyRank.Boss))
            {
                int cumulative = 0;
                foreach (var action in enemy.EnemyBehavior.Actions)
                {
                    int roll = cumulative + action.Weight / 2;
                    cumulative += action.Weight;
                    var session = new BattleSimulator(random: new FixedRoll(roll)).Start(Request(enemy, helper: true));
                    var events = session.AdvanceRound().ToList();
                    if (action.RequiresCharge)
                    {
                        Assert.That(events.Any(entry => entry.ActionName == action.Name + "·蓄力"), Is.True, enemy.Name);
                        Assert.That(events.Any(entry => entry.ActionName == action.Name), Is.False, enemy.Name);
                        events.AddRange(session.AdvanceRound());
                    }
                    Assert.That(events.Any(entry => entry.ActorId == "enemy" && entry.ActionName == action.Name), Is.True,
                        $"{enemy.Name}: {action.Name}");
                }
            }
        }

        [Test]
        public void HealingWithNoLegalTargetFallsBackToAnAttack()
        {
            var profile = new EnemyBehaviorDefinition(new[] { new EnemyActionDefinition("治疗", 100,
                EnemyActionKind.Heal, EnemyTargetRule.WoundedAlly, 5) });
            var result = new BattleSimulator(random: new FixedRoll()).Start(Request(Enemy(profile))).AdvanceRound();
            Assert.That(result.Any(entry => entry.ActorId == "enemy" && entry.ActionName == "无合法目标·普攻"), Is.True);
            Assert.That(result.Any(entry => entry.ActorId == "enemy" && entry.ActionKind == ActionKind.Heal), Is.False);
        }

        [Test]
        public void ChargedAttackTakesTwoActionsAndStrikesTheRearTarget()
        {
            var profile = new EnemyBehaviorDefinition(new[] { new EnemyActionDefinition("后排重击", 100,
                EnemyActionKind.Attack, EnemyTargetRule.Rear, 3, requiresCharge: true) });
            var session = new BattleSimulator(random: new FixedRoll()).Start(Request(Enemy(profile)));
            var first = session.AdvanceRound();
            Assert.That(first.Any(entry => entry.ActionName == "后排重击"), Is.False);
            Assert.That(first.Any(entry => entry.Countdown?.Kind == BattleCountdownKind.EnemyCharge && entry.Countdown?.Remaining == 1), Is.True);
            var second = session.AdvanceRound();
            Assert.That(second.Single(entry => entry.ActionName == "后排重击").TargetId, Is.EqualTo("rear"));
            Assert.That(second.Any(entry => entry.Countdown?.Kind == BattleCountdownKind.EnemyCharge && entry.Countdown?.Remaining == 0), Is.True);
        }

        [Test]
        public void SingleTargetRulesIgnoreDeadUnitsAndRespectFrontRearAndHealthRatios()
        {
            var frontAttack = new EnemyActionDefinition("前排", 100, EnemyActionKind.Attack, EnemyTargetRule.Front);
            var rearAttack = new EnemyActionDefinition("后排", 100, EnemyActionKind.Attack, EnemyTargetRule.Rear);
            var lowest = new EnemyActionDefinition("低血", 100, EnemyActionKind.Attack, EnemyTargetRule.LowestHealth);
            foreach (var (action, expected) in new[] { (frontAttack, "front"), (rearAttack, "rear"), (lowest, "rear") })
            {
                var enemy = Enemy(new EnemyBehaviorDefinition(new[] { action }));
                var request = new BattleRequest("target", new[] { Dummy("dead", 1, 0), Dummy("front", 2, 999),
                    Dummy("rear", 4, 500), new Combatant("enemy", enemy, Side.Enemy, 1, enemy.BaseStats, enemy.BaseStats.Health) }, 100);
                var result = new BattleSimulator(random: new FixedRoll()).Start(request).AdvanceRound();
                Assert.That(result.Single(entry => entry.ActorId == "enemy" && entry.Kind == BattleEventKind.Action).TargetId,
                    Is.EqualTo(expected));
            }
        }

        [Test]
        public void GroupAttacksPublishOneActionAndOrderedPerTargetEffects()
        {
            var action = new EnemyActionDefinition("墓园群攻", 100, EnemyActionKind.Attack, EnemyTargetRule.AllEnemies, -3);
            var result = new BattleSimulator(random: new FixedRoll()).Start(Request(Enemy(new EnemyBehaviorDefinition(new[] { action })))).AdvanceRound();
            var attacks = result.Where(entry => entry.ActionName == action.Name).ToArray();
            Assert.That(attacks.Select(entry => entry.TargetId), Is.EqualTo(new[] { "front", "rear" }));
            Assert.That(attacks.Count(entry => entry.Kind == BattleEventKind.Action), Is.EqualTo(1));
        }

        [TestCase("EnemyNamelessKing")]
        [TestCase("EnemySepulchralMatron")]
        public void BossPhaseTriggersOnceAtHalfHealthAndAddsOneAttackPower(string id)
        {
            var boss = Load().EnemyRoster.Single(enemy => enemy.Id == id);
            var healthy = new BattleSimulator(random: new FixedRoll()).Start(Request(boss));
            var half = new BattleSimulator(random: new FixedRoll()).Start(Request(boss, enemyHealth: boss.BaseStats.Health / 2));
            var normal = healthy.AdvanceRound();
            var phased = half.AdvanceRound();
            string attack = boss.EnemyBehavior.Actions[0].Name;
            Assert.That(phased.Single(entry => entry.ActionName == attack).Amount,
                Is.EqualTo(normal.Single(entry => entry.ActionName == attack).Amount + 1));
            Assert.That(phased.Count(entry => entry.VisualCue == BattleVisualCue.GraveyardPhase), Is.EqualTo(1));
            Assert.That(half.AdvanceRound().Any(entry => entry.VisualCue == BattleVisualCue.GraveyardPhase), Is.False);
        }

        [TestCase("WhisperingGraveyard", "EnemyNamelessKing", "葬王点名", "赦免")]
        [TestCase("WhisperingGraveyard", "EnemySepulchralMatron", "守柩祈祷", "护柩回生")]
        [TestCase("ClockworkCapital", "EnemyRoyalEngine", "汽笛鸣响", "终点清场")]
        [TestCase("ClockworkCapital", "EnemyAstralGovernor", "星轨定位", "星光折返")]
        [TestCase("ClockworkCapital", "EnemyCrucibleRegent", "炉温上升", "全城开炉")]
        public void BossPatternsResolveTheirDistinctSecondBeat(string region, string id, string setup, string result)
        {
            var data = AssetDatabase.LoadAssetAtPath<EncounterAsset>($"Assets/RaidPartyManager/PrototypeData/{region}Encounter.asset").ToData();
            var boss = data.EnemyRoster.Single(e => e.Id == id);
            var fight = new BattleSimulator(random: new FixedRoll()).Start(Request(boss, helper: true, enemyHealth: boss.BaseStats.Health - 6));
            var log = Enumerable.Range(0, 5).SelectMany(_ => fight.AdvanceRound()).Where(e => e.ActorId == "enemy").ToArray();
            Assert.That(log.Any(e => e.ActionName == setup), Is.True);
            Assert.That(log.Any(e => e.ActionName == result), Is.True);
        }

        [Test]
        public void OpeningShieldsAreGrantedOnlyOnce()
        {
            var enemy = Load().EnemyRoster.Single(unit => unit.Id == "EnemyTombShield");
            var battle = new BattleSimulator(random: new FixedRoll()).Start(Request(enemy));
            Assert.That(battle.AdvanceRound().Single(entry => entry.ActionName == "墓园护甲").Amount, Is.EqualTo(4));
            Assert.That(battle.AdvanceRound().Any(entry => entry.ActionName == "墓园护甲"), Is.False);
        }

        [Test]
        public void SeededGraveyardBattlesProduceTheSameEventSequence()
        {
            var enemy = Load().EnemyRoster.Single(unit => unit.Id == "EnemyNamelessKing");
            string[] Replay() => new BattleSimulator(random: new Random(712)).Simulate(Request(enemy)).Events
                .Select(entry => $"{entry.Tick}:{entry.Kind}:{entry.ActorId}:{entry.TargetId}:{entry.ActionName}:{entry.Amount}:{entry.Detail}:{entry.VisualCue}:{entry.Countdown?.Remaining}").ToArray();
            Assert.That(Replay(), Is.EqualTo(Replay()));
        }

        [Test]
        public void GraveyardPresetsUseTheMeasuredFormationsAndDraftOnlyNormalGroups()
        {
            var graveyard = Load();
            Assert.That(graveyard.EnemyFormations.Select(group => group.Rank), Is.EquivalentTo(
                Enumerable.Repeat(EnemyRank.Normal, 3).Concat(Enumerable.Repeat(EnemyRank.Elite, 5))
                    .Concat(Enumerable.Repeat(EnemyRank.Boss, 2))));
            foreach (var group in graveyard.EnemyFormations)
            {
                var units = group.CreateLineup("test-enemy");
                Assert.That(units.SelectMany(unit => Enumerable.Range(unit.Position, unit.Character.PositionSlots)),
                    Is.EqualTo(new[] { 1, 2, 3, 4 }), group.Name);
                Assert.That(units.All(unit => unit.Health == unit.Character.BaseStats.Health), Is.True, group.Name);
            }
            var random = new Random(53);
            var normal = graveyard.EnemyFormations.Where(group => group.Rank == EnemyRank.Normal).ToArray();
            for (int i = 0; i < 12; i++)
            {
                var draft = graveyard.Draft(Side.Enemy, random);
                Assert.That(normal.Any(group => group.Members.Select(member => member.Id).SequenceEqual(
                    draft.Select(unit => unit.Character.Id))), Is.True);
            }
        }

        [Test]
        public void RecordReproducibleDifficultySamples()
        {
            var graveyard = Load();
            var dock = AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();
            var cases = new[] { (Name: "码头普通对照", Formation: dock.EnemyFormations.Single(group => group.Name == "码头巡防")) }
                .Concat(graveyard.EnemyFormations.Select(group => (Name: group.Name, Formation: group))).ToArray();
            var records = new List<string> { "遭遇,队伍,样本数,胜利数,胜率,平均回合,胜利时平均倒下数,胜利时剩余生命比例" };
            foreach (bool prepared in new[] { false, true })
            {
                var names = prepared ? new[] {"KindleGuard", "StarfireMage", "DivinePriest", "EclipseArcher"}
                    : new[] {"CampGuard", "StarMage", "DawnHealer", "SilverArcher"};
                var loadouts = new[] {new[] {"life-pendant", "healing-ring"}, new[] {"red-cord", "fire-ring"},
                    new[] {"life-pendant", "elven-charm"}, new[] {"shortbow", "bludgeon"}};
                var party = names.Select((name, index) =>
                {
                    var character = dock.CharacterCatalog.Single(entry => entry.Id == name);
                    var gear = prepared ? loadouts[index].Select(id => dock.StartingEquipment.First(item => item.Id == id)).ToArray()
                        : Array.Empty<EquipmentDefinition>();
                    var stats = gear.Aggregate(character.BaseStats, (total, item) => total + item.Bonus);
                    return new Combatant("ally-" + index, character, Side.Party, index + 1, stats, stats.Health, gear);
                }).ToArray();
                int maximumHealth = party.Sum(unit => unit.Stats.Health);
                foreach (var fixture in cases)
                {
                    var enemies = fixture.Formation.CreateLineup("sample-enemy");
                    int victories = 0, rounds = 0, defeated = 0, remaining = 0;
                    for (int seed = 0; seed < 100; seed++)
                    {
                        var result = new BattleSimulator(random: new Random(seed)).Simulate(
                            new BattleRequest("difficulty", party.Concat(enemies), 480, characterCatalog: dock.CharacterCatalog));
                        rounds += result.Events.Count(entry => entry.Kind == BattleEventKind.RoundStarted);
                        Assert.That(result.HealthById.Values.All(health => health >= 0), Is.True);
                        if (result.Outcome != BattleOutcome.Victory) continue;
                        victories++;
                        defeated += party.Count(unit => result.HealthById[unit.Id] == 0);
                        remaining += party.Sum(unit => result.HealthById[unit.Id]);
                    }
                    string Number(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
                    records.Add(string.Join(",", fixture.Name, prepared ? "二阶八件装备" : "一阶未配装", 100,
                        victories, Number(victories / 100d), Number(rounds / 100d),
                        victories > 0 ? Number((double)defeated / victories) : "n.a.",
                        victories > 0 ? Number((double)remaining / victories / maximumHealth) : "n.a."));
                }
            }
            Directory.CreateDirectory("Logs/Graveyard");
            File.WriteAllLines("Logs/Graveyard/Balance.csv", records, new System.Text.UTF8Encoding(true));
            var normal = graveyard.EnemyRoster.Where(unit => unit.Rank == EnemyRank.Normal).ToArray();
            Assert.That(normal.Average(unit => unit.BaseStats.Health), Is.GreaterThan(dock.EnemyRoster
                .Where(unit => unit.Rank == EnemyRank.Normal).Average(unit => unit.BaseStats.Health) * 1.2));
        }

        private static CharacterDefinition Enemy(EnemyBehaviorDefinition profile) => new("fixture", "敌人", Profession.Warrior,
            Personality.Frank, new Stats(8, 1, 1, 100), 10, Basic, rank: EnemyRank.Normal, enemyBehavior: profile);

        private static Combatant Dummy(string id, int position, int health = 999, Side side = Side.Party)
        {
            var shield = new ActionDefinition("guard", "守护", ActionKind.Shield, TargetRule.Self, 1, 1);
            var stats = new Stats(0, 2, 2, 999);
            var character = new CharacterDefinition(id, id, Profession.Warrior, Personality.Worrier, stats, 10, Basic, shield);
            return new Combatant(id, character, side, position, stats, health);
        }

        private static BattleRequest Request(CharacterDefinition enemy, bool helper = false, int? enemyHealth = null)
        {
            var entries = new[] { Dummy("front", 1), Dummy("rear", 4),
                new Combatant("enemy", enemy, Side.Enemy, 1, enemy.BaseStats, enemyHealth ?? enemy.BaseStats.Health) }.ToList();
            if (helper) entries.Add(Dummy("helper", 4, 10, Side.Enemy));
            return new BattleRequest("graveyard", entries, 100);
        }

        private sealed class FixedRoll : Random
        {
            private readonly int roll;
            public FixedRoll(int roll = 0) => this.roll = roll;
            public override int Next(int maxValue) => maxValue == 100 ? roll : maxValue - 1;
            public override double NextDouble() => 0.99;
        }
    }
}
