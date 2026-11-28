using System;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using RaidPartyManager.Battle;
using UnityEditor;
using Random = System.Random;

namespace RaidPartyManager.Tests
{
    public sealed class ClockworkCapitalTests
    {
        private static EncounterData Load(string district) => AssetDatabase.LoadAssetAtPath<EncounterAsset>(
            $"Assets/RaidPartyManager/PrototypeData/{district}Encounter.asset").ToData();

        [TestCase("EnemyArsonExaminer")]
        [TestCase("EnemyCrucibleRegent")]
        public void FireImmuneEnemiesBlockOnlyTheFirePortionOfAnAttack(string enemyId)
        {
            var enemy = Load("ClockworkCapital").EnemyRoster.Single(unit => unit.Id == enemyId);
            var stats = new Stats(50, 10, 10, 100);
            var fire = Attack(DamageType.Fire, shielded: true);
            Assert.That(fire.Amount, Is.Zero);
            Assert.That(fire.ShieldAbsorbed, Is.Zero);
            Assert.That(fire.Detail, Is.EqualTo("免疫火焰伤害"));

            var physical = Attack(DamageType.Physical);
            var mixed = Attack(DamageType.Physical, addFire: true);
            Assert.That(physical.Amount, Is.GreaterThan(0));
            Assert.That(mixed.Amount, Is.EqualTo(physical.Amount));
            Assert.That(mixed.DamageTaken.Select(part => part.Type), Is.EqualTo(new[] { DamageType.Physical }));
            Assert.That(Attack(DamageType.Lightning).Amount, Is.GreaterThan(0));
            Assert.That(Attack(DamageType.Magic).Amount, Is.GreaterThan(0));

            BattleEvent Attack(DamageType type, bool shielded = false, bool addFire = false)
            {
                var action = new ActionDefinition("basic", "验证攻击", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                    damageType: type, additionalDamage: addFire ? new[] { new DamageComponent(DamageType.Fire) } : null);
                var attacker = new CharacterDefinition("tester", "Tester", Profession.Warrior, Personality.Frank, stats, 10, action);
                var party = new Combatant("tester", attacker, Side.Party, 1, stats, stats.Health);
                var equipment = shielded
                    ? new[] { new EquipmentDefinition("crown", "无面王冠", default, effect: EquipmentEffect.FacelessCrown) } : null;
                var target = new Combatant("target", enemy, Side.Enemy, 1, enemy.BaseStats, enemy.BaseStats.Health, equipment);
                var request = new BattleRequest("fire-immunity", new[] { party, target }, 100, capitalField: true);
                return new BattleSimulator(random: new Random(8)).Start(request).AdvanceRound()
                    .Single(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == party.Id && entry.TargetId == target.Id);
            }
        }

        [Test]
        public void EachRosterMemberCanAppearAndEveryBossHasItsOwnExclusivePair()
        {
            var city = Load("ClockworkCapital");
            var catalogue = city.StartingEquipment.Concat(city.SkillBooks).ToArray();
            var normal = city.EnemyRoster.Where(e => e.Rank == EnemyRank.Normal).ToArray();
            Assert.That(normal, Has.Length.EqualTo(25));
            Assert.That(city.EnemyRoster.Count(e => e.Rank == EnemyRank.Elite), Is.EqualTo(5));
            Assert.That(city.EnemyRoster.Count(e => e.Rank == EnemyRank.Boss), Is.EqualTo(3));
            Assert.That(city.EnemyFormations.Select(g => g.Members[0].Id).Distinct(),
                Is.EquivalentTo(city.EnemyRoster.Select(e => e.Id)));
            foreach (var group in city.EnemyFormations)
                Assert.That(group.Members.Sum(e => e.PositionSlots), Is.LessThanOrEqualTo(4));
            foreach (var boss in city.EnemyRoster.Where(e => e.Rank == EnemyRank.Boss))
            {
                var roll = BossSpoils.Roll(boss.Id, catalogue, new Random(82));
                Assert.That(roll.Choices.All(x => x.RewardBossId == boss.Id && !x.CanDropInBattle), Is.True);
                Assert.That(roll.Choices.Count(x => x is SkillBookDefinition), Is.EqualTo(1));
                Assert.That(roll.Equipment.BossExclusive, Is.False);
                Assert.That(PlayerMarket.SkillPremium(roll.Choices.OfType<SkillBookDefinition>().Single()), Is.EqualTo(100));
            }
        }

        [Test]
        public void ClearingTheSecondFloorPersistsTheCityAndEveryEventSettlesOnlyOnce()
        {
            var dock = Load("Roadside");
            var gear = dock.StartingEquipment.Concat(dock.SkillBooks).ToArray();
            AdventureRun Create(bool inspection)
            {
                var party = new PartyRun(dock.Party.Select(a => new PartyMemberStart(a.Id, a.Character, a.Position, a.Stats.Health)), Array.Empty<EquipmentDefinition>());
                party.AddGold(500);
                return new AdventureRun(party, dock.CharacterCatalog, gear, inspection, new Random(65));
            }
            var saved = Create(false).CheckpointForExit();
            saved.Floor = 2;
            saved.FloorCleared = true;
            var next = AdventureRun.RestoreCheckpoint(saved, dock.CharacterCatalog, gear);
            Assert.That(next.InCapital && next.Stage == 1 && next.Party.Day == saved.Party.Day, Is.True);
            Assert.That(next.AvailableEvents(), Is.EquivalentTo(CapitalStories.Kinds.Append(AdventureEventKind.RecruitmentWheel)));
            Assert.That(next.AvailableEvents().Distinct().Count(), Is.EqualTo(13));
            var broke = Create(true);
            Assert.That(broke.Party.TrySpendGold(broke.Party.Gold, "清空零钱"), Is.True);
            Assert.That(broke.Inspect(NodeKind.Event, AdventureEventKind.SpareHands), Is.True);
            Assert.That(broke.ChooseCapital(0) || broke.ChooseCapital(1), Is.False);
            Assert.That(broke.DeclineEvent() && broke.CloseVisit(), Is.True);
            foreach (var kind in CapitalStories.Kinds)
                for (int option = 0; option < CapitalStories.For(kind).Choices.Length; option++)
                {
                    var run = Create(true);
                    Assert.That(run.Inspect(NodeKind.Event, kind), Is.True);
                    Assert.That(run.ChooseCapital(option), Is.True, $"{kind}/{option}");
                    Assert.That(run.Current.Resolved && !run.ChooseCapital(option), Is.True);
                }
        }

        [TestCase(DamageType.Physical)]
        [TestCase(DamageType.Magic)]
        [TestCase(DamageType.Fire)]
        [TestCase(DamageType.Lightning)]
        [TestCase(DamageType.True)]
        [TestCase(DamageType.Fixed)]
        public void CityFieldScalesResolvedDamageButLeavesMagicIntact(DamageType type)
        {
            var basic = new ActionDefinition("attack", "敲击", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var action = new EnemyActionDefinition("实验冲击", 100, EnemyActionKind.Attack, EnemyTargetRule.Front, 6, type);
            var profile = new EnemyBehaviorDefinition(new[] { action });
            var stats = new Stats(8, 0, 0, 200);
            var enemy = new CharacterDefinition("fixture", "试验机", Profession.Warrior, Personality.Frank, stats, 10, basic,
                rank: EnemyRank.Normal, enemyBehavior: profile);
            var ally = new CharacterDefinition("guard", "守卫", Profession.Warrior, Personality.Frank, stats, 10, basic);
            var units = new[] { new Combatant("ally", ally, Side.Party, 1, stats, 200),
                new Combatant("enemy", enemy, Side.Enemy, 1, stats, 200) };
            int Strike(bool city) => new BattleSimulator(random: new Random(37)).Start(
                new BattleRequest("field", units, 100, capitalField: city)).AdvanceRound()
                .First(e => e.ActorId == "enemy" && e.ActionName == action.Name && e.DamageTaken.Count > 0).Amount;
            int plain = Strike(false);
            Assert.That(plain, Is.GreaterThan(1));
            Assert.That(Strike(true), Is.EqualTo(type == DamageType.Magic ? plain : Math.Max(1, plain / 2)));
        }

        [Test]
        public void DifficultyRisesFromTheDocksAndGraveyardUsingTheSameParty()
        {
            var dock = Load("Roadside");
            var grave = Load("WhisperingGraveyard");
            var city = Load("ClockworkCapital");
            foreach (var rank in new[] { EnemyRank.Normal, EnemyRank.Elite, EnemyRank.Boss })
            {
                var units = city.EnemyRoster.Where(e => e.Rank == rank).ToArray();
                foreach (var previous in new[] { dock, grave })
                {
                    var old = previous.EnemyRoster.Where(e => e.Rank == rank).ToArray();
                    Assert.That(units.Average(e => e.BaseStats.Health), Is.GreaterThan(old.Average(e => e.BaseStats.Health)));
                    Assert.That(units.Average(e => e.BaseStats.Attack), Is.GreaterThan(old.Average(e => e.BaseStats.Attack)));
                }
            }
            var party = new[] { "KindleGuard", "StarfireMage", "DivinePriest", "EclipseArcher" }.Select((id,i) =>
            {
                var c = dock.CharacterCatalog.Single(e => e.Id == id);
                var kit = new[] { "life-pendant", i == 1 ? "fire-ring" : "healing-ring" }
                    .Select(item => dock.StartingEquipment.First(e => e.Id == item)).ToArray();
                var stats = kit.Aggregate(c.BaseStats, (v,e) => v + e.Bonus);
                return new Combatant("ally-" + i, c, Side.Party, i + 1, stats, stats.Health, kit);
            }).ToArray();
            double WinRate(EncounterData area)
            {
                int wins = 0, samples = 0;
                var formations = area.EnemyFormations.Where(g => g.Rank == EnemyRank.Normal && !g.Name.Contains(" · ")).ToArray();
                foreach (var group in formations)
                    for (int seed = 0; seed < 25; seed++)
                    {
                        var units = party.Concat(group.CreateLineup("foe"));
                        var battle = new BattleRequest("capital-balance", units, 480, characterCatalog: dock.CharacterCatalog, capitalField: area == city);
                        if (new BattleSimulator(random: new Random(seed)).Simulate(battle).Outcome == BattleOutcome.Victory) wins++;
                        samples++;
                    }
                TestContext.WriteLine($"Normal encounters: {wins}/{samples} victories.");
                return (double)wins / samples;
            }
            double port = WinRate(dock), cemetery = WinRate(grave), capital = WinRate(city);
            Assert.That(capital, Is.LessThan(port));
            Assert.That(capital, Is.LessThan(cemetery));
        }
    }
}

