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
                .Select(character => character.RosterNumber), Is.EquivalentTo(Enumerable.Range(1, 36)));
            Assert.That(scene.CharacterCatalog.Where(character => character.RosterNumber > 0).Select(character => character.Skill),
                Is.All.Null);
            var singer = scene.CharacterCatalog.Single(character => character.RosterNumber == 36);
            Assert.That(singer.Trait, Is.EqualTo(CharacterTrait.SoakEnemyEachRound));
            Assert.That(scene.RecruitmentPool.Select(c => c.Id), Does.Not.Contain(singer.Id));
            Assert.That(scene.ProjectileFor(singer).name, Is.EqualTo("DeepSeaWhisper-WaveBlade"));
            Assert.That(scene.StatusIcon(StatusKind.Wet), Is.Not.Null);
            Assert.That(scene.StatusIcon(StatusKind.Evaporation), Is.Not.Null);
            var result = new BattleSimulator(random: new Random(11)).Simulate(new BattleRequest("tide",
                new[] { new Combatant("singer", singer, Side.Party, 1, singer.BaseStats, 15),
                    Fighter("front", Side.Enemy, 1, health: 100), Fighter("rear", Side.Enemy, 2, health: 100) }, singer.ActionInterval));
            Assert.That(result.Events.Count(e => e.ActorId == "singer" && e.TargetStatuses?.Contains("潮湿 1") == true &&
                e.ActionName == "深海轻语"), Is.EqualTo(1));
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
                var portrait = scene.PortraitFor(entry.Id);
                Assert.That(portrait, Is.Not.Null, $"Missing party portrait for {entry.Name}.");
                Assert.That(portrait.name, Is.EqualTo(entry.Id + "-Expressive"), $"Wrong party portrait for {entry.Name}.");
                Assert.That((float)portrait.width / portrait.height, Is.InRange(0.625f, 0.71f), entry.Name);
                var seated = scene.CampSpriteFor(entry.Id);
                Assert.That(seated, Is.Not.Null, $"Missing camp seated artwork for {entry.Name}.");
                Assert.That(seated.width, Is.EqualTo(seated.height), entry.Name);
                var sprite = scene.BattleSpriteFor(entry.Id);
                var attack = scene.BattleAttackFor(entry.Id);
                var walk = scene.BattleWalkFor(entry.Id);
                Assert.That(sprite != null && sprite.width % 16 == 0 && sprite.width / 16 == sprite.height / 4, Is.True,
                    $"Missing or invalid idle and hit atlas for {entry.Name}.");
                Assert.That(attack != null && attack.width % 4 == 0 && attack.height % 4 == 0, Is.True,
                    $"Missing or invalid attack atlas for {entry.Name}.");
                Assert.That(walk != null && walk.width % 4 == 0 && walk.height == walk.width, Is.True,
                    $"Missing or invalid sixteen-frame walk atlas for {entry.Name}.");
            }
        }

        [Test]
        public void DefaultDraftsUseFourDistinctUnitsFromEachIllustratedRoster()
        {
            var encounter = LoadEncounter();
            Assert.That(encounter.EnemyRoster.Select(unit => unit.Name), Is.EquivalentTo(new[]
            {
                "拨线枪手", "灰灯术徒", "拦路蛮兵", "裂陶术士", "敲钟判官", "绕后猎手", "誓线护卫", "收尸弩手", "铜铃庸医",
                "持叉渔夫", "逐潮人", "沉船拾荒者", "甲板水手", "水线泥鳗", "盐壳蟹", "吞灯海蛾", "雾哨火枪手", "补船匠",
                "沉锚寄居蟹", "雾胃灯笼鱼", "缆桩看守长", "封港提督", "沉灯巨魟"
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
                    Assert.That(lineup.All(unit => unit.Character.Rank != EnemyRank.Boss), Is.True);
                }
                Assert.That(draws.Select(lineup => string.Join(",", lineup.Select(unit => unit.Character.Id)))
                    .Distinct().Count(), Is.GreaterThan(1));
            }

            var animated = encounter.EnemyRoster.Where(enemy => enemy.Rank != EnemyRank.Unspecified &&
                encounter.BattleWalkFor(enemy.Id) != null && encounter.PortraitFor(enemy.Id)?.name.EndsWith("-Portrait") == true)
                .ToArray();
            foreach (var enemy in encounter.EnemyRoster)
            {
                Assert.That(encounter.PortraitFor(enemy.Id), Is.Not.Null, enemy.Name);
                Assert.That(encounter.BattleSpriteFor(enemy.Id), Is.Not.Null, enemy.Name);
                Assert.That(encounter.BattleAttackFor(enemy.Id), Is.Not.Null, enemy.Name);
                if (enemy.Reach == AttackReach.Melee)
                    Assert.That(encounter.BattleWalkFor(enemy.Id), Is.Not.Null, enemy.Name);
            }
            Assert.That(animated.Count(enemy => enemy.Trait is >= CharacterTrait.EnemyTideFollower ||
                enemy.Id == "EnemyForkFisher"), Is.EqualTo(14));
            Assert.That(encounter.EnemyRoster.Count(enemy => enemy.Profession == Profession.Monster), Is.EqualTo(6));
            Assert.That(encounter.RecruitmentPool.All(character => character.Rank == EnemyRank.Unspecified), Is.True);
        }

        [TestCase(NodeKind.NormalBattle, EnemyRank.Normal, 17)]
        [TestCase(NodeKind.EliteBattle, EnemyRank.Elite, 4)]
        [TestCase(NodeKind.Boss, EnemyRank.Boss, 2)]
        public void DockEncountersSelectAuthoredRanksAndKeepWorkbookStats(NodeKind node, EnemyRank rank, int expected)
        {
            var encounter = LoadEncounter();
            Assert.That(encounter.EnemyRoster.Count(enemy => enemy.Rank == rank), Is.EqualTo(expected));
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 100; seed++)
            {
                var members = encounter.Party.Select(ally => new PartyMemberStart(ally.Id, ally.Character, ally.Position, ally.Health));
                var party = new PartyRun(members, Array.Empty<EquipmentDefinition>());
                var run = new AdventureRun(party, encounter.CharacterCatalog, encounter.StartingEquipment, true, new Random(seed));
                Assert.That(run.Inspect(node), Is.True);
                var enemies = run.PrepareEnemies(encounter.EnemyRoster, encounter.EnemyFormations);
                Assert.That(run.PrepareEnemies(encounter.EnemyRoster, encounter.EnemyFormations), Is.SameAs(enemies));
                Assert.That(encounter.EnemyFormations.Where(group => group.Rank == rank).Any(group =>
                    group.Members.Take(enemies.Count).Select(member => member.Id)
                        .SequenceEqual(enemies.Select(enemy => enemy.Character.Id))), Is.True);
                Assert.That(run.Current.Battle.EliteIds,
                    Is.EquivalentTo(rank == EnemyRank.Normal ? Array.Empty<string>() : new[] { enemies[0].Id }));
                var primary = enemies.First();
                Assert.That(primary.Character.Rank, Is.EqualTo(rank));
                Assert.That(primary.Character.PositionSlots, Is.EqualTo((int)rank));
                Assert.That(enemies.Skip(1).All(enemy => enemy.Character.Rank == EnemyRank.Normal), Is.True);
                Assert.That(primary.Stats, Is.EqualTo(primary.Character.BaseStats));
                var slots = enemies.SelectMany(enemy => Enumerable.Range(enemy.Position, enemy.Character.PositionSlots)).ToArray();
                Assert.That(slots, Is.EqualTo(Enumerable.Range(1, slots.Length)));
                Assert.That(slots.Length, Is.LessThanOrEqualTo(Rules.Positions));
                foreach (var enemy in enemies.Where(enemy => enemy.Character.Rank == rank))
                    seen.Add(enemy.Character.Id);
            }
            Assert.That(seen, Has.Count.EqualTo(expected));
        }

        [TestCase(NodeKind.NormalBattle, 2)]
        [TestCase(NodeKind.EliteBattle, 1)]
        [TestCase(NodeKind.Boss, 1)]
        public void DockFormationsScaleToAStartingPair(NodeKind node, int expectedCount)
        {
            var encounter = LoadEncounter();
            var party = new PartyRun(encounter.Party.Take(2).Select(unit =>
                new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), Array.Empty<EquipmentDefinition>());
            var run = new AdventureRun(party, encounter.CharacterCatalog, encounter.StartingEquipment, true, new Random(19));
            Assert.That(run.Inspect(node), Is.True);
            var enemies = run.PrepareEnemies(encounter.EnemyRoster, encounter.EnemyFormations);
            Assert.That(enemies, Has.Count.EqualTo(expectedCount));
            Assert.That(encounter.EnemyFormations.Any(group => group.Rank == enemies[0].Character.Rank &&
                group.Members.Take(expectedCount).Select(member => member.Id).SequenceEqual(enemies.Select(enemy => enemy.Character.Id))),
                Is.True);
        }

        [Test]
        public void OversizedEnemyUsesContiguousPositionsInEditorAndBattleValidation()
        {
            var encounter = LoadEncounter();
            var starts = encounter.Draft(Side.Enemy, new Random(37)).Select(
                enemy => new PartyMemberStart(enemy.Id, enemy.Character, enemy.Position, enemy.Health)).ToArray();
            var editor = new PartyRun(starts, Array.Empty<EquipmentDefinition>());
            var boss = DockEnemy(encounter, "DrownedLanternRay");
            Assert.That(editor.TryReplaceMember(starts[0].Id, boss.Character), Is.True);
            Assert.That(editor.Members.Count(member => member.Position == null), Is.EqualTo(2));
            Assert.That(editor.CaptureFormation(Side.Enemy).Select(enemy => enemy.Position), Is.EqualTo(new[] { 1, 4 }));
            Assert.That(editor.TryPlace(starts[0].Id, 3), Is.False);
            Assert.That(editor.TryPlace(starts[0].Id, 2), Is.True);
            Assert.That(editor.CaptureFormation(Side.Enemy).Select(enemy => enemy.Position), Is.EquivalentTo(new[] { 1, 2 }));

            var ally = Fighter("hero", Side.Party, 1);
            var overlapping = Fighter("overlap", Side.Enemy, 2);
            Assert.Throws<ArgumentException>(() => new BattleRequest("overlap", new[] { ally, boss, overlapping }, 30));
        }

        [TestCase(1, true)]
        [TestCase(6, false)]
        public void ScavengerOnlyKeepsTheStolenItemWhenItEscapes(int attack, bool escapes)
        {
            var encounter = LoadEncounter();
            var hero = Fighter("hero", Side.Party, 1, attack);
            var item = new EquipmentDefinition("supply", "测试圣杯", new Stats(0, 1, 0, 0), effect: EquipmentEffect.EchoChalice);
            var party = new PartyRun(new[] { new PartyMemberStart(hero.Id, hero.Character, 1, 100) }, new[] { item });
            var instance = party.Inventory.Single();
            Assert.That(party.TryEquip(hero.Id, 0, instance.Id), Is.True);
            var thief = DockEnemy(encounter, "WreckScavenger");
            var request = party.BeginBattle(new[] { thief }, 80, null);
            var result = new BattleSimulator(random: new FixedOrder()).Simulate(request);
            var theft = result.Events.Single(entry => entry.ActionName == "顺手牵羊");
            Assert.That(theft.Loadout.Stolen, Is.EqualTo(new[] { instance.Id }));
            Assert.That(theft.TargetEnergy.Value.Capacity, Is.EqualTo(2));
            var escapeTimer = result.Events.Where(entry => entry.Countdown?.Kind == BattleCountdownKind.ScavengerEscape)
                .Select(entry => entry.Countdown.Value.Remaining).ToArray();
            Assert.That(escapeTimer.First(), Is.EqualTo(3));
            Assert.That(escapeTimer.Last(), Is.Zero);
            if (escapes)
                Assert.That(escapeTimer, Is.EqualTo(new[] { 3, 2, 1, 0 }));
            Assert.That(party.TryApplyBattleLoadout(request.Id, theft.Loadout), Is.True);
            Assert.That(party.Inventory, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.Events.Any(entry => entry.ActionName == "拿回装备"), Is.EqualTo(!escapes));
            if (escapes)
                Assert.That(result.Events.Single(entry => entry.Kind == BattleEventKind.Retreated).Tick, Is.EqualTo(40));
            else
                Assert.That(result.Events.Single(entry => entry.ActionName == "拿回装备").TargetEnergy.Value.Capacity, Is.EqualTo(4));
            Assert.That(AdventureRun.RollKillGold(result, new[] { thief }, new Random(0)), escapes ? Is.Zero : Is.GreaterThan(0));
            Assert.That(party.TrySettle(result, out _), Is.True);
            Assert.That(party.Inventory.Count, Is.EqualTo(escapes ? 0 : 1));
        }

        [Test]
        public void LanternMothWeakensTheNextPlayerAttack()
        {
            var moth = DockEnemy(LoadEncounter(), "LanternMoth");
            var hero = Fighter("hero", Side.Party, 1, attack: 4);
            var result = new BattleSimulator(random: new FixedOrder(0.8))
                .Simulate(new BattleRequest("moth", new[] { hero, moth }, 20));
            Assert.That(result.Events.Where(entry => entry.ActorId == hero.Id && entry.ActionKind == ActionKind.Attack)
                .Select(entry => entry.Amount), Is.EqualTo(new[] { 4, 2 }));
        }

        [Test]
        public void AnchorArmorBreaksAfterSixShieldAndLowersDefenseForRemainingHits()
        {
            var crab = DockEnemy(LoadEncounter(), "AnchorHermit");
            var hero = Fighter("hero", Side.Party, 1, attack: 9);
            var result = new BattleSimulator(random: new FixedOrder())
                .Simulate(new BattleRequest("anchor-armor", new[] { hero, crab }, 30));
            Assert.That(result.Events.Single(entry => entry.ActionName == "沉锚护甲").TargetShield, Is.EqualTo(6));
            Assert.That(result.Events.Count(entry => entry.ActionName == "沉锚护甲碎裂"), Is.EqualTo(1));
            Assert.That(result.Events.Single(entry => entry.ActionName == "沉锚护甲碎裂").VisualCue,
                Is.EqualTo(BattleVisualCue.AnchorShatter));
            Assert.That(result.Events.Where(entry => entry.ActorId == hero.Id && entry.ActionKind == ActionKind.Attack)
                .Select(entry => entry.Amount), Is.EqualTo(new[] { 0, 4, 7 }));
        }

        [TestCase("AnchorHermit", BattleVisualCue.AnchorExpose)]
        [TestCase("FogAngler", BattleVisualCue.AnglerCurse)]
        [TestCase("MooringWarden", BattleVisualCue.WardenBrace)]
        [TestCase("BlockadeAdmiral", BattleVisualCue.AdmiralWhistle)]
        [TestCase("DrownedLanternRay", BattleVisualCue.RayMist)]
        [TestCase("DrownedLanternRay", BattleVisualCue.RaySurge)]
        public void EliteAndBossAbilitiesUseTheirOwnBodyAnimation(string enemy, BattleVisualCue cue)
        {
            var encounter = LoadEncounter();
            var special = encounter.BattleSpecialFor("Enemy" + enemy, cue);
            Assert.That(special, Is.Not.Null);
            Assert.That(special.name, Is.EqualTo(enemy + "-Special"));
            Assert.That(special, Is.Not.SameAs(encounter.BattleAttackFor("Enemy" + enemy)));
        }

        [Test]
        public void AdmiralWhistlesThenBombardsTheLastTwoLivingHeroes()
        {
            var admiral = DockEnemy(LoadEncounter(), "BlockadeAdmiral");
            var heroes = new[] { Fighter("front", Side.Party, 1), Fighter("middle", Side.Party, 2), Fighter("rear", Side.Party, 4) };
            var result = new BattleSimulator(random: new FixedOrder(0.8))
                .Simulate(new BattleRequest("blockade", heroes.Append(admiral), 20));
            Assert.That(result.Events.Single(entry => entry.ActionName == "鸣笛预警").Tick, Is.EqualTo(10));
            Assert.That(result.Events.Where(entry => entry.VisualCue == BattleVisualCue.AdmiralWarning).Select(entry => entry.TargetId),
                Is.EqualTo(new[] { "rear", "middle" }));
            var shots = result.Events.Where(entry => entry.ActionName == "封港炮击").ToArray();
            Assert.That(shots.Select(entry => entry.TargetId), Is.EqualTo(new[] { "rear", "middle" }));
            Assert.That(shots.All(entry => entry.Tick == 20 && entry.Amount == 6), Is.True);
            Assert.That(shots.All(entry => entry.VisualCue == BattleVisualCue.AdmiralBroadside), Is.True);
            Assert.That(result.Events.Where(entry => entry.Countdown?.Kind == BattleCountdownKind.AdmiralBroadside)
                .Select(entry => (entry.Tick, entry.Countdown.Value.Remaining)), Is.EqualTo(new[] { (10, 1), (20, 0) }));
        }

        [TestCase(Side.Party, 5, 2)]
        [TestCase(Side.Enemy, 5, 2)]
        [TestCase(Side.Party, 6, 1)]
        [TestCase(Side.Enemy, 6, 1)]
        public void MeleeCanBypassSlotOneAtHalfHealthOnEitherTeam(Side side, int frontHealth, int expected)
        {
            var basic = new ActionDefinition("melee", "测试近战", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var stats = new Stats(1, 0, 0, 10);
            var fighter = new CharacterDefinition("melee", "近战", Profession.Warrior, Personality.Frank,
                stats, 1, basic, reach: AttackReach.Melee);
            var opposite = side == Side.Party ? Side.Enemy : Side.Party;
            var units = new[] {
                new Combatant("actor", fighter, side, 1, stats, 10),
                new Combatant("target1", fighter, opposite, 1, stats, frontHealth),
                new Combatant("target2", fighter, opposite, 2, stats, 10)
            };
            var result = new BattleSimulator(random: new FixedOrder()).Simulate(new BattleRequest("front", units, 10));
            Assert.That(result.Events.First(e => e.ActorId == "actor" && e.Kind == BattleEventKind.Action).TargetId,
                Is.EqualTo("target" + expected));
        }

        [Test]
        public void RayFreezesHeroesWhenCrossingHalfHealthAndUpgradesItsFog()
        {
            var ray = DockEnemy(LoadEncounter(), "DrownedLanternRay");
            var front = Fighter("front", Side.Party, 1, attack: 30);
            var rear = Fighter("rear", Side.Party, 4);
            var result = new BattleSimulator(random: new FixedOrder(0.8))
                .Simulate(new BattleRequest("lantern-ray", new[] { front, rear, ray }, 10));
            Assert.That(result.Events.Count(entry => entry.ActionName == "沉灯翻涌"), Is.EqualTo(1));
            Assert.That(result.Events.Where(entry => entry.Kind == BattleEventKind.EnemyVisual && entry.VisualCue == BattleVisualCue.RaySurge)
                .Select(entry => entry.TargetId), Is.EquivalentTo(new[] { "front", "rear" }));
            Assert.That(result.Events.Single(entry => entry.ActionName == "沉灯寒雾").Detail, Does.Contain("2 层寒冷"));
            Assert.That(result.Events.Where(entry => entry.ActionName == "冰冻").Select(entry => entry.TargetId),
                Is.EquivalentTo(new[] { "front", "rear" }));
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
            Assert.That(run.Gold, Is.EqualTo(8));
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
            Assert.That(arrows.Select(entry => entry.VisualCue), Is.All.EqualTo(BattleVisualCue.PiercingArrow));
            Assert.That(arrows.Select(entry => entry.Kind),
                Is.EqualTo(new[] { BattleEventKind.Action, BattleEventKind.Effect, BattleEventKind.Effect }));
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
            Assert.That(result.Events.Where(entry => entry.Countdown?.Kind == BattleCountdownKind.CalamityChant)
                .Select(entry => entry.Countdown.Value.Remaining), Is.EqualTo(new[] { 3, 2, 1, 3 }));
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
            var simulator = new BattleSimulator(random: new Random(1));
            var request = run.BeginBattle(new[] { enemy }, 15, null, characterCatalog: new[] { ally, weaver, chronomancer });
            var result = simulator.Simulate(request);

            Assert.That(result.Events.First(entry => entry.Kind == BattleEventKind.FormChanged).Form.Id, Is.EqualTo(chronomancer.Id));
            Assert.That(result.PermanentAttackGains["star"], Is.EqualTo(1));
            Assert.That(run.TrySettle(result, out _), Is.True);
            var original = run.Members.Single(member => member.Id == "star");
            Assert.That(original.Character.Id, Is.EqualTo(ally.Id));
            Assert.That(original.EffectiveStats.Attack, Is.EqualTo(ally.BaseStats.Attack + 1));
        }

        private static EncounterData LoadEncounter() => AssetDatabase.LoadAssetAtPath<EncounterAsset>(
            "Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();

        private static CharacterDefinition Load(string asset) => AssetDatabase.LoadAssetAtPath<CharacterAsset>(
            $"Assets/RaidPartyManager/PrototypeData/{asset}.asset").ToDefinition();

        private static Combatant DockEnemy(EncounterData data, string id)
        {
            var enemy = data.EnemyRoster.Single(character => character.Id == "Enemy" + id);
            return new Combatant(id, enemy, Side.Enemy, 1, enemy.BaseStats, enemy.BaseStats.Health);
        }

        private static Combatant Unit(CharacterDefinition character, string id, int position) =>
            new Combatant(id, character, Side.Party, position, character.BaseStats, character.BaseStats.Health);

        private static Combatant Fighter(string id, Side side, int position, int attack = 1, int health = 100)
        {
            var stats = new Stats(attack, 0, 0, health);
            var basic = new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var character = new CharacterDefinition(id, id, Profession.Warrior, Personality.Frank, stats, 1, basic);
            return new Combatant(id, character, side, position, stats, health);
        }

        private sealed class FixedOrder : Random
        {
            private readonly double roll;
            public FixedOrder(double roll = 0.1) => this.roll = roll;
            public override int Next(int maxValue) => maxValue - 1;
            public override double NextDouble() => roll;
        }
    }
}
