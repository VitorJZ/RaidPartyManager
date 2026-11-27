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
    public sealed class AdventureTests
    {
        private EncounterData content;
        private EquipmentDefinition[] gear;

        [SetUp]
        public void LoadContent()
        {
            content = AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset").ToData();
            gear = content.StartingEquipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
        }

        private AdventureRun Create(int seed = 17, bool inspection = true, Random random = null)
        {
            var party = new PartyRun(content.Party.Select(u => new PartyMemberStart(u.Id, u.Character,
                u.Position, Math.Max(1, u.Stats.Health - 2))), gear.Where(e => e.Tier == 3).Take(2).Concat(gear.Where(e => e.Tier == 2).Take(2)));
            party.AddGold(1000);
            return new AdventureRun(party, content.CharacterCatalog, gear, inspection, random ?? new Random(seed));
        }

        [Test]
        public void DockTownGuaranteesOneTidePactAndTheFreeContractSurvivesReload()
        {
            var opening = Create(42, false);
            Assert.That(opening.DaysUntilFinalPayroll, Is.EqualTo(31));
            var saved = opening.CheckpointForExit();
            saved.Stage = 9;
            saved.Nodes[8].Kind = NodeKind.Town;
            saved.Nodes[8].Fork = true;
            saved.Nodes[8].AlternativePlusOne = (int)NodeKind.NormalBattle + 1;
            var catalog = gear.Concat(content.SkillBooks).ToArray();
            AdventureRun Restore(AdventureCheckpoint data) =>
                AdventureRun.RestoreCheckpoint(data, content.CharacterCatalog, catalog);
            var run = Restore(saved);
            Assert.That(run.DaysUntilFinalPayroll, Is.EqualTo(23));
            Assert.That(run.RouteOptions, Is.EqualTo(new[] { NodeKind.Town }));
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.DaysUntilFinalPayroll, Is.EqualTo(22));
            var equipment = run.Party.AddEquipment(gear.First(e => !e.IsPotion && e.Effect != EquipmentEffect.RecruitmentNotice));
            var book = run.Party.AddEquipment(content.SkillBooks.First());
            Assert.That(run.OfferTidePact(equipment.Id, book.Id), Is.False);
            run = Restore(run.CheckpointForExit());
            Assert.That(run.EnterFollowing(0), Is.True);
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.TidePact));
            Assert.That(run.DaysUntilFinalPayroll, Is.EqualTo(21));
            run = Restore(run.CheckpointForExit());
            Assert.That(run.DaysUntilFinalPayroll, Is.EqualTo(22));
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.TidePact));
            Assert.That(run.OfferTidePact(equipment.Id, equipment.Id), Is.False);
            int gold = run.Party.Gold;
            Assert.That(run.OfferTidePact(equipment.Id, book.Id), Is.True);
            Assert.That(run.OfferTidePact(equipment.Id, book.Id), Is.False);
            Assert.That(run.Party.BackpackItems.Select(i => i.Id), Does.Not.Contain(equipment.Id).And.Not.Contain(book.Id));
            Assert.That(run.Party.Gold, Is.EqualTo(gold));
            run = Restore(run.CheckpointForExit());
            var contract = run.Party.Members.Single(m => m.Character.Id == AdventureRun.TideSingerId).Contract;
            Assert.That((contract.StartDay, contract.Days, contract.BaseTotal, contract.DailyWage),
                Is.EqualTo((run.Party.Day, 99, 0L, 0)));
            Assert.That(run.Stage, Is.EqualTo(10));
            Assert.That(run.RouteOptions, Is.EqualTo(new[] { NodeKind.Boss }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CemeteryOffersTheSeedForkOnceEvenAcrossReload(bool enterSeedEvent)
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Floor = 2;
            saved.Stage = 2;
            saved.Nodes[1].Kind = NodeKind.Camp;
            saved.Nodes[1].Fork = true;
            saved.Nodes[1].AlternativePlusOne = (int)NodeKind.Event + 1;
            saved.Nodes[2].Kind = NodeKind.Event;
            saved.Nodes[2].Fork = false;
            AdventureRun Restore(AdventureCheckpoint state) =>
                AdventureRun.RestoreCheckpoint(state, content.CharacterCatalog, gear);
            var run = Restore(saved);
            Assert.That(run.RouteOptions, Is.EqualTo(new[] { NodeKind.Camp, NodeKind.Event }));
            run = Restore(run.CheckpointForExit());
            Assert.That(run.EnterRoute(enterSeedEvent ? 1 : 0), Is.True);
            if (enterSeedEvent)
            {
                Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.GraveSeed));
                int gold = run.Party.Gold;
                Assert.That(run.TakeGraveSeedReward(false), Is.True);
                Assert.That(run.Party.Gold, Is.EqualTo(gold + 50));
                Assert.That(run.TakeGraveSeedReward(true), Is.False);
            }
            else Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.Camp));

            Assert.That(run.Leave(), Is.True);
            run = Restore(run.CheckpointForExit());
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Current.Event, Is.Not.Null.And.Not.EqualTo(AdventureEventKind.GraveSeed));
        }

        [Test]
        public void SeedFollowsItsEquipmentInstanceAndRecruitsOnlyAfterEnoughDeployedBattles()
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Floor = 2;
            saved.Stage = 2;
            saved.GraveSeedStage = saved.Stage;
            saved.Options = new[] { NodeKind.NormalBattle, NodeKind.Event };
            AdventureRun Restore(AdventureCheckpoint state) =>
                AdventureRun.RestoreCheckpoint(state, content.CharacterCatalog, gear);
            var run = Restore(saved);
            Assert.That(run.EnterRoute(1) && run.TakeGraveSeedReward(true) && run.CloseVisit(), Is.True);
            var seed = run.Party.Inventory.Single(item => item.Definition.Effect == EquipmentEffect.StrangeSeed);
            Assert.That(seed.Definition.AvailableInRegularPool, Is.False);
            Assert.That((seed.Definition.Tier, seed.Definition.Price), Is.EqualTo((3, 10)));

            CheckpointItem SeedState() => run.CheckpointForExit().Party.Inventory.Single(i => i.Id == seed.Id);
            int threshold = SeedState().BattlesToBloom;
            Assert.That(threshold, Is.InRange(3, 5));

            void Enter(NodeKind kind)
            {
                var state = run.CheckpointForExit();
                state.Options = new[] { kind };
                run = Restore(state);
                Assert.That(run.EnterRoute(0), Is.True);
            }

            string Win()
            {
                Enter(NodeKind.NormalBattle);
                var foes = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
                var battle = run.Party.BeginBattle(foes, 100, null);
                run.BindBattle(battle.Id);
                var result = new BattleResult(battle.Id, BattleOutcome.Victory,
                    battle.Combatants.ToDictionary(c => c.Id, c => c.Side == Side.Enemy ? 0 : c.Health),
                    Array.Empty<BattleEvent>());
                Assert.That(run.Party.TryFinishBattle(result, out _), Is.True);
                Assert.That(run.SettleBattle(result), Is.True);
                Assert.That(run.SettleBattle(result), Is.False);
                string wearer = run.Current.Battle.BloomWearer;
                Assert.That(run.Leave(), Is.True);
                return wearer;
            }

            Assert.That(Win(), Is.Null);
            Assert.That(SeedState().BattlesWithSeed, Is.Zero);
            var first = run.Party.Members[0].Id;
            var next = run.Party.Members[1].Id;
            Assert.That(run.Party.TryEquip(first, 0, seed.Id), Is.True);
            Assert.That(Win(), Is.Null);
            var state = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(
                UnityEngine.JsonUtility.ToJson(run.CheckpointForExit()));
            run = Restore(state);
            Assert.That(run.Party.TryEquip(first, 0, null) && run.Party.TryEquip(next, 0, seed.Id), Is.True);
            for (int battles = 2; battles <= threshold; battles++)
                Assert.That(Win(), Is.EqualTo(battles == threshold ? next : null));

            Assert.That(run.Party.Inventory.Any(i => i.Id == seed.Id), Is.False);
            var recruit = run.Party.Members.Single(m => m.Character.Id == AdventureRun.GraveGardenerId);
            Enter(NodeKind.Camp);
            Assert.That(run.EvolveAtCamp(recruit.Id, "RotBloomWitch"), Is.True);
            Assert.That(run.Party.Members.Single(m => m.Id == recruit.Id).Character.Id, Is.EqualTo("RotBloomWitch"));
        }

        [Test]
        public void BrokerHonoursHerCounterAndRemembersThreeRefusalsUntilTheNextTown()
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var member = run.Party.Members.First();
            Assert.That(run.DiscussSale(member.Id), Is.True);
            Assert.That(run.OfferSale(member.Id, int.MaxValue), Is.EqualTo(MarketReply.Counteroffer));
            int offer = run.Current.BrokerCounter;
            var resumed = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(run.CheckpointForExit()));
            run = AdventureRun.RestoreCheckpoint(resumed, content.CharacterCatalog, gear);
            Assert.That(run.Current.BrokerRefusals, Is.EqualTo(1));
            int before = run.Party.Gold;
            Assert.That(run.OfferSale(member.Id, offer), Is.EqualTo(MarketReply.Sold));
            Assert.That(run.Party.Gold, Is.EqualTo(before + offer - member.Contract.ReleaseFee));
            Assert.That(run.OfferSale(member.Id, offer), Is.EqualTo(MarketReply.Unavailable));
            var next = run.Party.Members.First();
            Assert.That(run.DiscussSale(next.Id), Is.True);
            Assert.That(run.OfferSale(next.Id, int.MaxValue), Is.EqualTo(MarketReply.Counteroffer));
            Assert.That(run.OfferSale(next.Id, int.MaxValue), Is.EqualTo(MarketReply.Closed));
            Assert.That(run.DiscussSale(next.Id), Is.False);
            Assert.That(run.Leave(), Is.True);
            var nextTown = run.CheckpointForExit();
            nextTown.Options = new[] { NodeKind.Town };
            run = AdventureRun.RestoreCheckpoint(nextTown, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.DiscussSale(next.Id), Is.True);
            Assert.That(run.Current.BrokerRefusals, Is.Zero);
        }

        [Test]
        public void BrokerRespondsToTheSizeOfTheAskAndRemembersConcessions()
        {
            var initial = Create(42, false).CheckpointForExit();
            initial.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(initial, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            string id = run.Party.Members[0].Id;
            Assert.That(run.DiscussSale(id), Is.True);
            var startingQuote = run.CheckpointForExit();
            int opening = run.Current.BrokerCounter;
            int moderate = opening + Math.Max(12, opening * 40 / 100);
            Assert.That(run.OfferSale(id, moderate), Is.EqualTo(MarketReply.Counteroffer));
            int engaged = run.Current.BrokerCounter;
            run = AdventureRun.RestoreCheckpoint(startingQuote, content.CharacterCatalog, gear);
            Assert.That(run.OfferSale(id, int.MaxValue), Is.EqualTo(MarketReply.Counteroffer));
            int cautious = run.Current.BrokerCounter;
            Assert.That(cautious, Is.GreaterThanOrEqualTo(opening + 3));
            Assert.That(engaged, Is.GreaterThan(cautious));
            run = AdventureRun.RestoreCheckpoint(run.CheckpointForExit(), content.CharacterCatalog, gear);
            Assert.That(run.Current.BrokerLastAsk, Is.EqualTo(int.MaxValue));
            Assert.That(run.OfferSale(id, cautious + 3), Is.EqualTo(MarketReply.Sold));
        }

        [TestCase(PlayerTrait.None, 3)]
        [TestCase(PlayerTrait.Warehouse, 4)]
        public void LegacyPartyWithChantGainsItsExtraSlotAndKeepsTheEquippedCopyOnReload(PlayerTrait talent, int oldCount)
        {
            var saved = Create(23, false).CheckpointForExit();
            var singer = saved.Party.Members[0];
            var ally = saved.Party.Members[1];
            singer.Character = singer.StartingCharacter = "Chant";
            ally.Trait = talent;
            ally.Slots = new string[oldCount];
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var cup = run.Party.AddEquipment(gear.Single(item => item.Effect == EquipmentEffect.EchoChalice));
            Assert.That(run.Party.TryEquip(ally.Id, oldCount, cup.Id), Is.True);
            run = AdventureRun.RestoreCheckpoint(run.CheckpointForExit(), content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.Single(m => m.Id == ally.Id).EquipmentIds[oldCount], Is.EqualTo(cup.Id));
            Assert.That(run.Party.TrySendAway(singer.Id), Is.True);
            Assert.That(run.Party.Members.Single(m => m.Id == ally.Id).EquipmentIds.Count, Is.EqualTo(oldCount));
            Assert.That(run.Party.BackpackItems.Count(item => item.Id == cup.Id), Is.EqualTo(1));
        }

        [TestCase(0, 50, 21, 17)]
        [TestCase(1, 70, 30, 24)]
        [TestCase(2, 90, 38, 31)]
        [TestCase(3, 110, 47, 38)]
        [TestCase(4, 130, 55, 45)]
        public void BargainingCountsEveryRosterMemberForQuotesAndBulkSales(int holders, int rate, int armorPrice, int bookPrice)
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            Assert.That(catalogue.Where(item => item.Price <= 0).Select(item => item.Id), Is.Empty);
            var saved = Create(23, false).CheckpointForExit();
            for (int i = 0; i < saved.Party.Members.Length; i++)
                saved.Party.Members[i].Trait = i < holders ? PlayerTrait.Bargaining : PlayerTrait.None;
            var crew = saved.Party.Members;
            crew[0].Position = 0; // On the bench.
            crew[1].Position = 0;
            crew[1].IsAway = true;
            crew[2].Position = 0;
            crew[2].Health = 0;
            var party = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, catalogue).Party;
            var armor = party.AddEquipment(gear.Single(item => item.Effect == EquipmentEffect.StoneArmor));
            var book = party.AddEquipment(content.SkillBooks.First(item => !item.BossExclusive && item.Tier == 1));
            var boss = party.AddEquipment(content.SkillBooks.First(item => item.BossExclusive));
            Assert.That(party.ResalePercent, Is.EqualTo(rate));
            Assert.That(party.ResalePrice(armor.Definition), Is.EqualTo(armorPrice));
            Assert.That(party.ResalePrice(book.Definition), Is.EqualTo(bookPrice));
            Assert.That(party.ResalePrice(boss.Definition), Is.EqualTo(100));
            int gold = party.Gold;
            Assert.That(party.TrySellItems(new[] { armor.Id, book.Id, boss.Id }, out int total), Is.True);
            Assert.That(total, Is.EqualTo(armorPrice + bookPrice + 100));
            Assert.That(party.Gold, Is.EqualTo(gold + total));
        }

        [TestCase(3, 3, 9)]
        [TestCase(4, 3, 8)]
        [TestCase(3, 5, 7)]
        [TestCase(5, 4, 6)]
        [TestCase(5, 5, 5)]
        public void EmployeeRatingsDiscountTheMinimumWithoutLoweringTheStandard(int loyalty, int obedience, int wage)
        {
            var ratings = new EmployeeAttributes { Loyalty = loyalty, Obedience = obedience };
            Assert.That(CrewContract.MinimumWage(10, default, ratings), Is.EqualTo(wage));
        }

        [Test]
        public void LearningABossSkillDoublesTheMembersValue()
        {
            var party = Create().Party;
            var member = party.Members.First();
            var boss = content.SkillBooks.First(book => book.BossExclusive && book.CanLearn(member.Character));
            var copy = party.AddEquipment(boss);
            Assert.That(party.Members.First().MarketValue, Is.EqualTo(member.MarketValue));
            Assert.That(party.TryLearnSkill(member.Id, 0, copy.Id), Is.True);
            Assert.That(party.Members.First().MarketValue, Is.EqualTo(2 * member.MarketValue));
            var ordinary = content.SkillBooks.First(book => !book.BossExclusive && PlayerMarket.SkillPremium(book) > 0);
            int trained = PlayerMarket.Value(member.Character, member.Trait, new[] { ordinary });
            Assert.That(PlayerMarket.Value(member.Character, member.Trait, new[] { ordinary, boss }), Is.EqualTo(2 * trained));
        }

        [Test]
        public void AnExperiencedLoyalEmployeeCanSignBelowStandardAfterReload()
        {
            var saved = Create(44, false).CheckpointForExit();
            var character = saved.Party.Members[0];
            character.EmployeeAttributes.Loyalty = character.EmployeeAttributes.Obedience = 5;
            character.EmployeeCareer.Damage = 100000;
            var route = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var employee = route.Party.Members.First(m => m.Id == character.Id);
            Assert.That(employee.EmployeeAttributes.Loyalty, Is.EqualTo(5));
            int basis = CrewContract.ExpectedWage(employee.Character, employee.Trait, employee.LearnedSkills);
            int expected = route.Party.ExpectedDailyWage(employee.Id);
            Assert.That(expected, Is.InRange(basis, basis + basis * 35 / 100));
            int minimum = CrewContract.MinimumWage(expected, default, employee.EmployeeAttributes);
            Assert.That(minimum, Is.LessThan(expected));
            Assert.That(route.Party.SetContract(employee.Id, 10, default, 10 * minimum - 1), Is.False);
            Assert.That(route.Party.SetContract(employee.Id, 10, default, 10 * minimum), Is.True);
        }

        [Test]
        public void ResaleUsesBasePricesAndPersistsTheRemovalOfOnlyTheSoldCopy()
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            var saved = Create(23, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var route = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, catalogue);
            Assert.That(route.EnterRoute(0), Is.True);
            var party = route.Party;
            var stone = gear.Single(item => item.Effect == EquipmentEffect.StoneArmor);
            var worn = party.AddEquipment(stone);
            var spare = party.AddEquipment(stone);
            var normal = party.AddEquipment(content.SkillBooks.First(book => book.Tier == 1));
            var bossBook = party.AddEquipment(content.SkillBooks.First(book => book.BossExclusive));
            var bossGear = party.AddEquipment(gear.First(item => item.BossExclusive));
            Assert.That(party.TryEquip(party.Members.First().Id, 0, worn.Id), Is.True);
            int gold = party.Gold;
            Assert.That(party.TrySellItems(new[] { spare.Id, worn.Id }, out int proceeds), Is.False);
            Assert.That(proceeds, Is.Zero);
            Assert.That(party.TrySellItems(new[] { spare.Id, spare.Id }, out _), Is.False);
            Assert.That(party.TryTakeItemsForSale(new[] { spare.Id, normal.Id }, 1, out _), Is.False);
            Assert.That(party.Gold, Is.EqualTo(gold));
            Assert.That(party.TrySellItem(worn.Id), Is.False);
            Assert.That(party.TrySellItem(spare.Id), Is.True);
            Assert.That(party.TrySellItem(spare.Id), Is.False);
            Assert.That(party.TrySellItem(normal.Id), Is.True);
            Assert.That(party.TrySellItem(bossBook.Id), Is.True);
            Assert.That(party.TrySellItem(bossGear.Id), Is.True);
            var json = UnityEngine.JsonUtility.ToJson(route.CheckpointForExit());
            route = AdventureRun.RestoreCheckpoint(UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json),
                content.CharacterCatalog, catalogue);
            Assert.That(route.Party.Gold, Is.EqualTo(gold + 21 + 17 + 200));
            Assert.That(route.Party.Members.First().EquipmentIds, Does.Contain(worn.Id));
            Assert.That(route.Party.TrySellItem(bossBook.Id), Is.False);
        }

        [Test]
        public void CareerAndPermanentHealthSurviveSettlementAndRaiseTheNextWageQuote()
        {
            var run = Create(71, false);
            var member = run.Party.Members.First();
            int wage = run.Party.ExpectedDailyWage(member.Id);
            var foe = content.EnemyRoster.First();
            var enemy = new Combatant("foe", foe, Side.Enemy, 1, foe.BaseStats, foe.BaseStats.Health);
            var request = run.Party.BeginBattle(new[] { enemy }, 100, null);
            var events = new[] {
                new BattleEvent(1, BattleEventKind.Action, member.Id, enemy.Id, amount: 180),
                new BattleEvent(1, BattleEventKind.Action, enemy.Id, member.Id, damageTaken: new[] { (DamageType.Physical, 3) }, shieldAbsorbed: 4),
                new BattleEvent(1, BattleEventKind.Effect, member.Id, member.Id, "生命吊坠", ActionKind.Heal, amount: 1),
                new BattleEvent(2, BattleEventKind.Effect, member.Id, member.Id, "血肉盛宴", ActionKind.Heal, amount: 2)
            };
            var result = new BattleResult(request.Id, BattleOutcome.Victory,
                request.Combatants.ToDictionary(u => u.Id, u => u.Id == member.Id ? u.Stats.Health + 1 : u.Health), events,
                permanentHealthGains: new Dictionary<string,int> { { member.Id, 1 } });
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.True);
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.False);
            var saved = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(run.CheckpointForExit()));
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var profile = run.Party.EmployeeProfileFor(member.Id).Career;
            Assert.That(profile.DamageTaken, Is.EqualTo(3));
            Assert.That(profile.Healed, Is.EqualTo(3));
            Assert.That(run.Party.Members.First().EffectiveStats.Health, Is.EqualTo(member.EffectiveStats.Health + 1));
            Assert.That(run.Party.Members.First().Health, Is.EqualTo(member.EffectiveStats.Health + 1));
            Assert.That(run.Party.ExpectedDailyWage(member.Id), Is.GreaterThan(wage));
            Assert.That(run.Party.Members.First().Contract.DailyWage, Is.EqualTo(member.Contract.DailyWage));
        }

        [Test]
        public void InstallmentsSurvivePromotionAndResumeAndOnlyActualPaymentsEnterTheLedger()
        {
            var founder = Create(42, false).CheckpointForExit();
            founder.Party.Members = founder.Party.Members.Take(1).ToArray();
            founder.Party.Members[0].Contract.EndDay = 2;
            founder.Party.Members[0].Contract.BasePaid = true;
            var run = AdventureRun.RestoreCheckpoint(founder, content.CharacterCatalog, gear);
            var member = run.Party.Members.Single();
            var form = content.CharacterCatalog.First(c => c.Personality != member.Personality && c.PositionSlots == 1 &&
                run.Party.Members.All(other => other.Character.Id != c.Id));
            Assert.That(run.Party.TryChangeForm(member.Id, form), Is.True);
            int wage = run.Party.ExpectedDailyWage(member.Id);
            var terms = new PerformanceBonuses { Installments = new WageInstallments { Enabled = true, Count = 3, IntervalDays = 2 } };
            int balance = run.Party.Gold;
            int upfront = wage + 1;
            Assert.That(run.Party.SetContract(member.Id, 5, terms, upfront), Is.False);
            Assert.That(run.Party.SetContract(member.Id, 12, terms, upfront), Is.True);
            var agreement = run.Party.Members.Single().Renewals.Single();
            Assert.That(agreement.InstallmentsPaid, Is.Zero);
            Assert.That(agreement.NextDueDay, Is.EqualTo(4));
            Assert.That(agreement.InterestPercent, Is.EqualTo(WageInstallments.PremiumPercent(member.Personality)));
            Assert.That(run.Party.Expenses.Single().Amount, Is.EqualTo(upfront));
            long outlay = upfront;
            for (int day = 2; day <= 8; day++)
            {
                var saved = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(run.CheckpointForExit()));
                saved.Options = new[] { NodeKind.Town };
                run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
                Assert.That(run.EnterRoute(0), Is.True);
                var current = run.Party.Members.Single();
                int periods = (day - 2) / 2;
                if (day > 2 && day % 2 == 0) outlay += agreement.ScheduledPayment(periods);
                Assert.That(current.Contract.InstallmentsPaid, Is.EqualTo(periods));
                Assert.That(current.Character.Personality, Is.EqualTo(member.Personality));
                Assert.That(run.Party.Gold, Is.EqualTo(balance - outlay));
                Assert.That(run.Leave(), Is.True);
            }
            Assert.That(run.Party.Members.Single().Contract.CurrentReleaseFee, Is.Zero);
            Assert.That(outlay, Is.EqualTo(12L * wage + agreement.ScheduledInterest));
        }

        [Test]
        public void PayingOneEmployeesPrincipalLowersTheOpenPeriodAndLaterInterestAfterReload()
        {
            var saved = Create(42, false).CheckpointForExit();
            var first = saved.Party.Members[0];
            first.Contract = new CrewContract
            {
                DeferredTerms = true, StartDay = 1, EndDay = 31, DailyWage = 10, InterestPercent = 20,
                Rates = new PerformanceBonuses
                {
                    Installments = new WageInstallments { Enabled = true, Count = 3, IntervalDays = 10 }
                }
            };
            saved.Party.Gold = 100;
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.Members[0].Contract.AccruedInterest, Is.EqualTo(20));
            Assert.That(run.Party.Members[0].Contract.ScheduledInterest, Is.EqualTo(40));

            saved = RoundTrip(run.CheckpointForExit());
            saved.Party.Day = 4;
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.WageBalanceFor(first.Id), Is.EqualTo(306));
            Assert.That(run.Party.TryRepayWages(first.Id, 101), Is.False);
            Assert.That(run.Party.TryRepayWages(first.Id, 60), Is.True);

            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            var updated = run.Party.Members[0].Contract;
            Assert.That(run.Party.Gold, Is.EqualTo(40));
            Assert.That(run.Party.WageBalanceFor(first.Id), Is.EqualTo(246));
            Assert.That(updated.RemainingPrincipal, Is.EqualTo(240));
            Assert.That(updated.ScheduledInterest, Is.EqualTo(30));
            Assert.That(Enumerable.Range(1, 3).Select(updated.ScheduledPayment), Is.EqualTo(new long[] { 117, 109, 44 }));
            Assert.That(run.Party.Members[1].Contract.PrincipalPaidEarly, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FinalBossDismissesUnpaidMembersBeforeStartingAndKeepsTheRemainingTeamOnReload(bool saveOneMember)
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Floor = 3;
            saved.Stage = DockRoute.Stages;
            saved.Options = new[] { NodeKind.Boss };
            saved.Party.Day = 30;
            saved.Party.Gold = 30;
            saved.Party.PayrollDebt = 5;
            saved.Party.Members = saved.Party.Members.Take(2).ToArray();
            var first = saved.Party.Members[0];
            var second = saved.Party.Members[1];
            first.Contract = Installment(0);
            second.Contract = Installment(45);
            second.UnpaidWages = 5;

            CrewContract Installment(int deposit) => new CrewContract
            {
                DeferredTerms = true, StartDay = 1, EndDay = 61, DailyWage = 1, SigningFee = deposit,
                Rates = new PerformanceBonuses
                {
                    Installments = new WageInstallments { Enabled = true, Count = 1, IntervalDays = 60 }
                }
            };

            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.DaysUntilFinalPayroll, Is.EqualTo(1));
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.DaysUntilFinalPayroll, Is.Zero);
            Assert.That(run.NeedsFinalPayroll, Is.True);
            Assert.That(run.Party.TryRepayWages(first.Id, 10), Is.True);
            if (saveOneMember) Assert.That(run.Party.TryRepayWages(second.Id, 20), Is.True);

            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            Assert.That(run.PreparingFinalBoss, Is.True);
            Assert.That(run.DaysUntilFinalPayroll, Is.Zero);
            Assert.That(run.Party.Day, Is.EqualTo(31));
            Assert.That(run.Party.WageBalanceFor(first.Id), Is.EqualTo(50));
            Assert.That(run.Party.WageBalanceFor(second.Id), Is.EqualTo(saveOneMember ? 0 : 20));
            Assert.That(run.ConfirmFinalPayroll(), Is.EqualTo(saveOneMember));
            Assert.That(run.Party.PayrollDebt, Is.EqualTo(saveOneMember ? 50 : 70));
            Assert.That(run.Failed, Is.EqualTo(!saveOneMember));

            if (!saveOneMember)
            {
                Assert.That(run.Party.Members, Is.Empty);
                return;
            }

            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.Single().Id, Is.EqualTo(second.Id));
            Assert.That(run.PayrollDepartures.Count, Is.EqualTo(1));
            Assert.That(run.Party.CanStartBattle, Is.True);
            Assert.That(run.NeedsFinalPayroll, Is.False);
            Assert.That(run.ConfirmFinalPayroll(), Is.True);
            Assert.That(run.Party.PayrollDebt, Is.EqualTo(50));
            run.BindBattle("final-boss");
            Assert.That(run.DaysUntilFinalPayroll, Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FutureInstallmentsStartOnlyWhenThePreviousContractEnds(bool sell)
        {
            var run = Create();
            var party = run.Party;
            var member = party.Members.First();
            int wage = party.ExpectedDailyWage(member.Id);
            var rates = new PerformanceBonuses { Installments = new WageInstallments { Enabled = true, Count = 3, IntervalDays = 4 }};
            int deposit = wage + 1;
            Assert.That(party.SetContract(member.Id, 12, rates, deposit), Is.True);
            var terms = party.Members.First(m => m.Id == member.Id).Renewals.Single();
            int before = party.Gold;
            int offer = party.TransferOffer(member.Id);
            if (sell)
            {
                Assert.That(party.SellMember(member.Id), Is.True);
                Assert.That(party.SellMember(member.Id), Is.False);
                Assert.That(party.Gold, Is.EqualTo(before + offer - member.Contract.ExitCost));
            }
            else
            {
                Assert.That(party.SetContract(member.Id, 12, default, 12L * wage), Is.True);
                Assert.That(party.Gold, Is.EqualTo(before - 12 * wage));
            }
            if (sell) Assert.That(party.Expenses.Any(e => e.Reason.Contains("违约金")), Is.False);
            else Assert.That(party.Members.First(m => m.Id == member.Id).Renewals.Last().StartDay, Is.EqualTo(terms.EndDay));
        }

        [TestCase(Personality.Caring, 100)]
        [TestCase(Personality.Optimist, 100)]
        [TestCase(Personality.Stoic, 107)]
        [TestCase(Personality.Frank, 112)]
        [TestCase(Personality.Competitive, 117)]
        [TestCase(Personality.Frugal, 122)]
        public void InstallmentPremiumUsesTheEmployeesOwnPersonality(Personality personality, int required) =>
            Assert.That(100 + WageInstallments.PremiumPercent(personality), Is.EqualTo(required));

        [TestCase(false)]
        [TestCase(true)]
        public void OneDayContractsUseTheExactBidAndPaidPrincipalNeverAccruesInterest(bool financed)
        {
            var run = Create();
            var party = run.Party;
            var member = party.Members.First();
            int wage = party.ExpectedDailyWage(member.Id);
            var terms = new PerformanceBonuses { Installments = new WageInstallments { Enabled = financed, Count = 1, IntervalDays = 1 } };
            int bid = financed ? wage : wage + 1;
            int before = party.Gold;
            Assert.That(party.SetContract(member.Id, 1, terms, bid), Is.True);
            var deal = party.Members.First().Renewals.Single();
            Assert.That(deal.EndDay, Is.EqualTo(member.Contract.EndDay + 1));
            Assert.That(deal.BaseTotal, Is.EqualTo(bid));
            Assert.That(party.Gold, Is.EqualTo(before - bid));
            Assert.That(deal.ScheduledInterest, Is.Zero);
            Assert.That(deal.CurrentReleaseFee, Is.Zero);
            Assert.That(deal.NextDueDay, Is.Null);
        }

        [Test]
        public void SaleSettlesOverduePrincipalOnceAndLeavesNoDuplicatePayrollDebt()
        {
            var saved = Create(17, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            foreach (var starter in saved.Party.Members) starter.Contract.BasePaid = true;
            saved.Party.Members[0].Contract.EndDay = 2;
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var party = run.Party;
            var member = party.Members.First();
            int wage = party.ExpectedDailyWage(member.Id);
            var terms = new PerformanceBonuses { Installments = new WageInstallments { Enabled = true, Count = 1, IntervalDays = 1 } };
            Assert.That(party.SetContract(member.Id, 5, terms, 5L * wage - 2), Is.True);
            Assert.That(party.TrySpendGold(party.Gold), Is.True);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(party.PayrollDebt, Is.Zero);
            Assert.That(run.Leave(), Is.True);
            saved = run.CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            party = run.Party;
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(party.PayrollDebt, Is.GreaterThanOrEqualTo(2));
            party.AddGold(1);
            var deal = party.Members.First().Contract;
            Assert.That(deal.Arrears, Is.EqualTo(party.PayrollDebt));
            long net = party.TransferOffer(member.Id) - deal.ExitCost;
            Assert.That(party.SellMember(member.Id), Is.True);
            Assert.That(party.PayrollDebt, Is.Zero);
            Assert.That(party.Gold, Is.EqualTo(net));
            party.AddGold(5);
            Assert.That(party.Gold, Is.EqualTo(net + 5));
        }

        [Test]
        public void RenewalPrepaysTheFullTermAndInsufficientFundsKeepThePreviousDeal()
        {
            var saved = Create(17, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            saved.Party.Members = saved.Party.Members.Take(1).ToArray();
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var member = run.Party.Members.Single();
            int wage = run.Party.ExpectedDailyWage(member.Id);
            int balance = run.Party.Gold;
            Assert.That(run.Party.SetContract(member.Id, 12, default, 12L * wage), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(balance - 12 * wage));
            var signed = run.Party.Members.Single().Contract;
            Assert.That(run.Party.SetContract(member.Id, int.MaxValue - run.Party.Day, default, (long)int.MaxValue * wage), Is.False);
            Assert.That(run.Party.Members.Single().Contract, Is.SameAs(signed));
            Assert.That(run.Party.Members.Single().Renewals.Single().StartDay, Is.EqualTo(signed.EndDay));
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(balance - 12 * wage - signed.DailyWage));
        }

        [Test]
        public void GraveLettersUseTheirCluesAndPayOnlyOnce()
        {
            var run = Create();
            run.Inspect(NodeKind.Event, AdventureEventKind.DeadLetters);
            for (int i = 0; i < 3; i++)
            {
                string note = run.Current.Message;
                int recipient = note.Contains("齿轮") ? 0 : note.Contains("船桨") ? 1 : 2;
                Assert.That(run.ChooseGrave("letter" + recipient), Is.True, note);
                Assert.That(run.ChooseGrave("letter" + recipient), Is.False);
            }
            Assert.That(run.Current.Resolved, Is.True);
            Assert.That(run.Current.Message, Does.Contain("投对了 3 封"));
            Assert.That(run.Current.Message, Does.Contain("回礼包裹"));
        }

        [Test]
        public void GraveHealthPreviewMatchesBothBedsAndProtectsTheGoalLimit()
        {
            var run = Create();
            var first = run.Party.Members[0];
            var second = run.Party.Members[1];
            run.Inspect(NodeKind.Event, AdventureEventKind.SoulWard);
            Assert.That(run.ChooseGrave("member-" + first.Id), Is.True);
            Assert.That(run.ChooseGrave("member-" + second.Id), Is.True);
            string preview = run.Current.Message;
            Assert.That(run.ChooseGrave("stitch"), Is.True);
            var now = run.Party.Members;
            Assert.That(now[0].Health + now[1].Health, Is.EqualTo(first.Health + second.Health));
            Assert.That(preview, Does.Contain(first.Health + " → " + now[0].Health));
            Assert.That(preview, Does.Contain(second.Health + " → " + now[1].Health));
            run.Inspect(NodeKind.Event, AdventureEventKind.Epitaph);
            Assert.That(run.ChooseGrave("member-" + first.Id), Is.True);
            Assert.That(run.ChooseGrave("vow2"), Is.True);
            var replacement = run.GraveChoices().First(o => o.key.StartsWith("replace") || o.key == "free");
            Assert.That(run.ChooseGrave(replacement.key), Is.True);
            var goals = run.Party.EmployeeProfileFor(first.Id).Goals;
            Assert.That(goals.Count, Is.LessThanOrEqualTo(2));
            Assert.That(goals.Count(g => g.SubjectId == "grave-epitaph"), Is.EqualTo(1));
        }

        [Test]
        public void GraveBellPorterAliasAndInterviewAffectTheActualNextBattle()
        {
            var run = Create();
            var employee = run.Party.Members.First(m => m.Position.HasValue);
            run.Inspect(NodeKind.Event, AdventureEventKind.Obituary);
            string interview = run.GraveChoices().First(o => o.key.StartsWith("member-")).key;
            Assert.That(run.ChooseGrave(interview), Is.True);
            run.Inspect(NodeKind.Event, AdventureEventKind.Pallbearer);
            Assert.That(run.ChooseGrave("member-" + employee.Id), Is.True);
            run.Inspect(NodeKind.Event, AdventureEventKind.BellRope);
            Assert.That(run.ChooseGrave("once"), Is.True);
            run.Inspect(NodeKind.Event, AdventureEventKind.BorrowedName);
            Assert.That(run.ChooseGrave("member-" + employee.Id), Is.True);
            Assert.That(run.Party.Members.First(m => m.Id == employee.Id).Character.Name, Is.EqualTo("某位员工"));
            run.Inspect(NodeKind.NormalBattle);
            var enemies = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            var request = run.Party.BeginBattle(enemies, 200, null);
            Assert.That(request.Combatants.First(c => c.Id == employee.Id).Character.Name, Is.EqualTo("某位员工"));
            var combat = new BattleSimulator().Simulate(request);
            var round = combat.Events.TakeWhile(e => e.Kind != BattleEventKind.RoundStarted).ToArray();
            Assert.That(round.Count(e => e.ActionName == "墓钟 · 守夜"), Is.EqualTo(2));
            var firstRound = combat.Events.SkipWhile(e => e.Kind != BattleEventKind.RoundStarted)
                .Skip(1).TakeWhile(e => e.Kind != BattleEventKind.RoundStarted);
            Assert.That(firstRound.Any(e => e.Kind == BattleEventKind.Action && e.ActorId == employee.Id), Is.False);
            var result = new BattleResult(request.Id, BattleOutcome.Victory,
                request.Combatants.ToDictionary(u => u.Id, u => u.Side == Side.Enemy ? 0 : u.Health), Array.Empty<BattleEvent>());
            var gold = run.Party.Gold;
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.True);
            Assert.That(run.Party.Gold, Is.GreaterThanOrEqualTo(gold + 12));
            var next = run.Party.BeginBattle(enemies, 200, null);
            Assert.That(next.PallbearerId, Is.Null);
            Assert.That(next.GraveBell, Is.Zero);
            Assert.That(run.Party.ReclaimName("错名"), Is.False);
            Assert.That(run.Party.ReclaimName(employee.Character.Name), Is.True);
            Assert.That(run.Party.Members.First(m => m.Id == employee.Id).Character.Name, Is.EqualTo(employee.Character.Name));
        }

        [Test]
        public void LegacyPartyGetsContractsAndReplayingABattleNeverDoubleBillsWages()
        {
            var saved = Create(31, false).CheckpointForExit();
            saved.Party.Day = 0;
            saved.Party.Gold = 0;
            foreach (var m in saved.Party.Members)
            {
                m.Contract = null; m.Trait = PlayerTrait.None;
                m.Slots = m.Slots.Take(3).ToArray();
                m.Skills = m.Skills.Take(2).ToArray();
            }
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.All(m => m.Trait != PlayerTrait.None && m.Contract.EndDay == 13), Is.True);
            int wages = run.Party.Members.Sum(m => m.Contract.DailyWage);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Party.PayrollDebt, Is.EqualTo(wages));
            run = AdventureRun.RestoreCheckpoint(run.CheckpointForExit(), content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Party.Day, Is.EqualTo(2));
            Assert.That(run.Party.PayrollDebt, Is.EqualTo(wages));
            run.Party.AddGold(wages + 3);
            Assert.That(run.Party.PayrollDebt, Is.Zero);
            Assert.That(run.Party.Gold, Is.EqualTo(3));
        }

        [TestCase(9)]
        [TestCase(10)]
        public void OlderOversizedSquadsCanContinueWithoutGainingMoreMembers(int size)
        {
            var saved = Create(17, false).CheckpointForExit();
            var old = saved.Party.Members.ToList();
            var candidates = content.CharacterCatalog.Where(c => c.RosterNumber > 0 && old.All(m => m.Character != c.Id)).ToArray();
            foreach (var candidate in candidates.Take(size - old.Count))
            {
                var reserve = UnityEngine.JsonUtility.FromJson<CheckpointMember>(UnityEngine.JsonUtility.ToJson(old[0]));
                reserve.Id = "legacy-" + candidate.Id;
                reserve.StartingCharacter = reserve.Character = candidate.Id;
                reserve.Position = 0;
                reserve.Health = reserve.StartingHealth = candidate.BaseStats.Health;
                old.Add(reserve);
            }
            saved.Party.Members = old.ToArray();
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.Count, Is.EqualTo(size));
            Assert.That(run.Party.TryRecruit(candidates.Last()), Is.False);
            var enemies = content.Enemies.Take(1);
            Assert.That(run.Party.BeginBattle(enemies, 100, null, candidates).RecruitLimit, Is.Zero);
        }

        [Test]
        public void CampMemoriesKeepTheirVoiceAcrossSavingAndFadeAfterThreeDays()
        {
            var run = Create(29, false);
            var employee = run.Party.Members.First();
            Assert.That(run.Party.Rest(.3), Is.GreaterThan(0));
            var line = EmployeeVoice.For(employee.Personality).Say(EmployeeThought.CampRest);
            var json = UnityEngine.JsonUtility.ToJson(run.CheckpointForExit());
            var saved = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json);
            saved.Party.Day += 2;
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.EmployeeProfileFor(employee.Id).Thoughts, Does.Contain(line));
            saved.Party.Day++;
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.EmployeeProfileFor(employee.Id).Thoughts, Does.Not.Contain(line));
        }

        [Test]
        public void EmployeeTenureAndContractGoalsAreSavedAcrossRenewal()
        {
            var saved = Create(41, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var starter = saved.Party.Members[0];
            starter.Contract.DailyWage = 1;
            starter.Contract.EndDay = 4;
            starter.EmployeeCareer.Work = starter.EmployeeCareer.Development = default;
            starter.EmployeeCareer.ServiceDays = 2;
            starter.EmployeeCareer.HighestWage = 1;
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var profile = run.Party.EmployeeProfileFor(starter.Id);
            Assert.That(profile.Career.ServiceDays, Is.EqualTo(3));
            var raise = profile.Goals.Single(goal => goal.Kind == EmployeeGoalKind.Raise);
            var voice = EmployeeVoice.For(run.Party.Members.Single(m => m.Id == starter.Id).Personality);
            Assert.That(profile.Thoughts, Does.Contain(voice.Say(EmployeeThought.Renewal, 2)));
            Assert.That(profile.Thoughts, Does.Contain(voice.Say(EmployeeThought.LowPay, 1, raise.Target)));
            Assert.That(profile.Goals.Count, Is.EqualTo(2));
            Assert.That(profile.Goals.Any(goal => goal.Kind == EmployeeGoalKind.Renewal), Is.True);
            Assert.That(run.Party.SetContract(starter.Id, 12, default, 12 * raise.Target), Is.True);
            Assert.That(run.Party.EmployeeProfileFor(starter.Id).Career.GoalsCompleted, Is.EqualTo(2));
            Assert.That(run.Party.SetContract(starter.Id, 12, default, 12), Is.False);
            Assert.That(run.Party.EmployeeProfileFor(starter.Id).Goals.Any(goal => goal.Kind == EmployeeGoalKind.Raise), Is.False);
            Assert.That(run.Party.SetContract(starter.Id, 12, default, 12 * raise.Target), Is.True);
            var renewed = run.Party.Members.Single(member => member.Id == starter.Id);
            Assert.That(renewed.Renewals.Last().DailyWage, Is.EqualTo(raise.Target));

            var json = UnityEngine.JsonUtility.ToJson(run.CheckpointForExit());
            run = AdventureRun.RestoreCheckpoint(UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json), content.CharacterCatalog, gear);
            profile = run.Party.EmployeeProfileFor(starter.Id);
            Assert.That(profile.Career.ServiceDays, Is.EqualTo(3));
            Assert.That(profile.Career.GoalsCompleted, Is.EqualTo(2));
            Assert.That(run.Party.Members.Single(member => member.Id == starter.Id).EmployeeAttributes,
                Is.EqualTo(renewed.EmployeeAttributes));
        }

        [Test]
        public void LegacyContractsRetainTheirSignedRatesUntilRenewal()
        {
            var legacy = UnityEngine.JsonUtility.FromJson<CrewContract>("{\"Bonuses\":15,\"DailyWage\":4}");
            Assert.That(legacy.AgreedRates.Appearance, Is.EqualTo(2));
            Assert.That(legacy.AgreedRates.Kill, Is.EqualTo(3));
            Assert.That(legacy.AgreedRates.DamageTaken, Is.EqualTo(2));
            Assert.That(legacy.AgreedRates.Shield, Is.EqualTo(2));
            var c = Create().Party.Members.First().Character;
            var baseWage = CrewContract.Offer(c, 1).DailyWage;
            var low = CrewContract.Offer(c, 1, 12, new PerformanceBonuses { Appearance = 1 });
            var high = CrewContract.Offer(c, 1, 12, new PerformanceBonuses { Appearance = 4 });
            Assert.That(low.DailyWage, Is.EqualTo(baseWage));
            Assert.That(high.DailyWage, Is.LessThan(low.DailyWage));
        }

        [Test]
        public void PerformanceBonusesUseCombatEventsAndDoNotPayTwice()
        {
            var run = Create();
            var member = run.Party.Members.First();
            var terms = new PerformanceBonuses { Appearance = 7, Kill = 13, DamageTaken = 5, Shield = 11 };
            member.Contract.Rates = terms;
            run.Inspect(NodeKind.NormalBattle);
            var enemies = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            var foe = enemies[0];
            var request = run.Party.BeginBattle(enemies, 100, null);
            var hits = new[] {
                new BattleEvent(1, BattleEventKind.Action, foe.Id, member.Id, damageTaken: new[] { (DamageType.Physical, 5) }, shieldAbsorbed: 11),
                new BattleEvent(2, BattleEventKind.Defeated, member.Id, foe.Id)
            };
            var result = new BattleResult(request.Id, BattleOutcome.Victory,
                request.Combatants.ToDictionary(u => u.Id, u => u.Side == Side.Enemy ? 0 : u.Health), hits);
            int gold = run.Party.Gold;
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.True);
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(gold - 7 - 13 - 5 - 22));
        }

        [TestCase(PlayerTrait.AgileSwap, StatusKind.Swift)]
        [TestCase(PlayerTrait.Disobedient, StatusKind.Weakened)]
        public void PositionTraitsCompareActualDeploymentsAcrossSaves(PlayerTrait trait, StatusKind status)
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            var original = Create(19, false).CheckpointForExit();
            var member = original.Party.Members[0];
            member.Trait = trait;
            var run = AdventureRun.RestoreCheckpoint(original, content.CharacterCatalog, catalogue);
            Combatant Fighter(PartyRun party) => party.CaptureFormation(Side.Party).Single(unit => unit.Id == member.Id);
            Assert.That(Fighter(run.Party).StartingStatuses.ContainsKey(status), Is.False);
            var enemy = content.Enemies.First();
            var battle = run.Party.BeginBattle(new[] { enemy }, 100, null);
            var result = new BattleResult(battle.Id, BattleOutcome.Victory,
                battle.Combatants.ToDictionary(unit => unit.Id, unit => unit.Side == Side.Party ? unit.Health : 0),
                Array.Empty<BattleEvent>());
            Assert.That(run.Party.TryFinishBattle(result, out _), Is.True);
            var saved = run.CheckpointForExit();
            var restored = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, catalogue).Party;
            Assert.That(Fighter(restored).StartingStatuses.ContainsKey(status), Is.False);
            int originalSlot = Fighter(restored).Position;
            int other = originalSlot == 1 ? 2 : 1;
            Assert.That(restored.TryPlace(member.Id, other), Is.True);
            Assert.That(Fighter(restored).StartingStatuses[status], Is.EqualTo(1));
            Assert.That(restored.TryPlace(member.Id, originalSlot), Is.True);
            Assert.That(Fighter(restored).StartingStatuses.ContainsKey(status), Is.False);
        }

        [Test]
        public void FoundersAndLaterHiresStartWithAllSixRatingsAtThree()
        {
            var party = Create().Party;
            var recruit = content.CharacterCatalog.First(c => party.Members.All(m => m.Character.Id != c.Id));
            Assert.That(party.TryRecruit(recruit), Is.True);
            foreach (var member in party.Members)
            {
                Assert.That(FiveRatings(member.EmployeeAttributes), Is.All.EqualTo(3));
                Assert.That(member.EmployeeAttributes.Satisfaction, Is.EqualTo(3));
            }
        }

        private static int[] FiveRatings(EmployeeAttributes ratings) => new[]
        {
            ratings.Loyalty, ratings.Obedience, ratings.PayPreference, ratings.PlayingTime, ratings.Hardiness
        };

        [Test]
        public void MaxEmployeeRatingsStackWithEnergyTrainingAndSurviveReload()
        {
            var saved = Create(47, false).CheckpointForExit();
            var entry = saved.Party.Members[0];
            entry.Trait = PlayerTrait.Warehouse;
            entry.Slots = new string[4];
            entry.Training = new CampTraining { Energy = 1, CompletedSessions = 1 };
            var baseline = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear).Party.Members.First();
            entry.EmployeeAttributes = new EmployeeAttributes { Loyalty = 5, Obedience = 5, Satisfaction = 5,
                Hardiness = 5, PayPreference = 5, PlayingTime = 5 };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var member = run.Party.Members.First();
            Assert.That(member.EffectiveStats, Is.EqualTo(baseline.EffectiveStats + new Stats(1, 1, 1, 3)));
            Assert.That(member.EquipmentIds.Count, Is.EqualTo(5));
            var cup = run.Party.AddEquipment(gear.Single(item => item.Effect == EquipmentEffect.EchoChalice));
            Assert.That(run.Party.TryEquip(member.Id, 4, cup.Id), Is.True);
            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.First().EquipmentIds[4], Is.EqualTo(cup.Id));
            var opponent = content.Enemies[0];
            var wall = new Combatant("wall", opponent.Character, Side.Enemy, 1, new Stats(1, 0, 0, 999), 999);
            var lineup = run.Party.CaptureFormation(Side.Party).Concat(new[] { wall });
            var battle = new BattleSimulator().Start(new BattleRequest("energy-training", lineup, 200));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(battle.ReadPartyEnergy().Single(p => p.Key == member.Id).Value.Capacity, Is.EqualTo(6));
                battle.AdvanceRound();
            }
        }

        [Test]
        public void LegacyPairedTrainingKeepsItsGainsAndUsesOneVisit()
        {
            var saved = Create(47, false).CheckpointForExit();
            var entry = saved.Party.Members[0];
            entry.Training = new CampTraining { Attack = 1, Defense = 1 };
            saved.Options = new[] { NodeKind.Camp };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0) && run.TrainAtCamp(entry.Id), Is.True);
            var gains = run.Party.Members.First().Training;
            Assert.That(gains.Attack, Is.GreaterThanOrEqualTo(1));
            Assert.That(gains.Defense, Is.GreaterThanOrEqualTo(1));
            Assert.That(gains.Attack + gains.Defense + gains.SpecialDefense + gains.Health + gains.Energy, Is.EqualTo(3));
            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            Assert.That(run.Party.Members.First().Training.Sessions, Is.EqualTo(2));
        }

        [Test]
        public void TrainingSharesCampActionAndKeepsBothSessionsAcrossCampsOnTheSameFloor()
        {
            var saved = Create(41, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Camp };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var before = run.Party.Members.First();
            int beforeOffer = run.Party.TransferOffer(before.Id);
            Assert.That(run.TrainAtCamp("missing"), Is.False);
            Assert.That(run.TrainAtCamp(before.Id), Is.True);
            var trained = run.Party.Members.First();
            Assert.That(trained.Training.Sessions, Is.EqualTo(1));
            Assert.That(new[] { trained.Training.Attack, trained.Training.Defense, trained.Training.SpecialDefense, trained.Training.Health, trained.Training.Energy }.Sum(),
                Is.EqualTo(1));
            var bonus = trained.Training.Bonus;
            Assert.That(trained.EffectiveStats.Attack, Is.EqualTo(before.EffectiveStats.Attack + bonus.Attack));
            Assert.That(trained.Health, Is.EqualTo(before.Health + bonus.Health));
            Assert.That(trained.MarketValue, Is.GreaterThan(before.MarketValue));
            Assert.That(run.Party.TransferOffer(trained.Id), Is.GreaterThan(beforeOffer));
            Assert.That(run.Rest(), Is.False);
            Assert.That(run.TrainAtCamp(before.Id), Is.False);

            // Saving after the completed camp advances the route and keeps the training.
            saved = RoundTrip(run.CheckpointForExit());
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Floor, Is.EqualTo(1));
            Assert.That(run.Party.Members.First().Training, Is.EqualTo(trained.Training));
            saved = run.CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.TrainAtCamp(before.Id), Is.True);
            Assert.That(run.CanUseTown(TownService.Camp), Is.False);
            run = AdventureRun.RestoreCheckpoint(RoundTrip(run.CheckpointForExit()), content.CharacterCatalog, gear);
            Assert.That(run.CanUseTown(TownService.Camp), Is.False);
            Assert.That(run.Party.Members.First().Training.Sessions, Is.EqualTo(2));
            Assert.That(run.TrainingCandidates.Select(member => member.Id), Does.Not.Contain(before.Id));
            Assert.That(run.Leave(), Is.True);
            saved = run.CheckpointForExit();
            saved.Options = new[] { NodeKind.Camp };
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.TrainAtCamp(before.Id), Is.False);
            Assert.That(run.Rest(), Is.True);
        }

        [Test]
        public void ASecondTrainingCanRepeatTheSameStatAfterEvolution()
        {
            var run = Create(random: new FirstChoiceRandom());
            var trainee = run.EvolutionCandidates.First();
            var form = run.EvolutionForms(trainee.Id).First();
            Assert.That(run.Inspect(NodeKind.Camp), Is.True);
            Assert.That(run.TrainAtCamp(trainee.Id), Is.True);
            Assert.That(run.Inspect(NodeKind.Camp), Is.True);
            Assert.That(run.EvolveAtCamp(trainee.Id, form.Id), Is.True);
            Assert.That(run.Inspect(NodeKind.Camp), Is.True);
            Assert.That(run.TrainAtCamp(trainee.Id), Is.True);
            var member = run.Party.Members.Single(m => m.Id == trainee.Id);
            Assert.That(member.Character.Id, Is.EqualTo(form.Id));
            Assert.That(member.Training, Is.EqualTo(new CampTraining { Attack = 2, CompletedSessions = 2 }));
        }

        private sealed class FirstChoiceRandom : Random
        {
            public override int Next(int maxValue) => 0;
        }

        [Test]
        public void MarketHiresKeepTheirQuotedTrainingAndUseItAgainstTheirCampLimit()
        {
            var run = Enumerable.Range(41, 12).Select(seed =>
            {
                var saved = Create(seed, false).CheckpointForExit();
                saved.Options = new[] { NodeKind.Town };
                var town = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
                Assert.That(town.EnterRoute(0), Is.True);
                return town;
            }).First(town => town.Current.Recruits.Any(c => town.Party.TownCandidateTraining(c).Sessions == 2));
            var candidate = run.Current.Recruits.First(c => run.Party.TownCandidateTraining(c).Sessions == 2);
            var training = run.Party.TownCandidateTraining(candidate);
            var price = run.Party.SigningPrice(candidate);
            var savedTown = RoundTrip(run.CheckpointForExit());
            run = AdventureRun.RestoreCheckpoint(savedTown, content.CharacterCatalog, gear);
            Assert.That(run.Party.TownCandidateTraining(candidate), Is.EqualTo(training));
            Assert.That(run.Party.SigningPrice(candidate), Is.EqualTo(price));

            int gold = run.Party.Gold;
            Assert.That(run.ChooseTownRecruit(candidate.Id), Is.True);
            var hire = run.Party.Members.Single(m => m.Character.Id == candidate.Id);
            Assert.That(hire.Training, Is.EqualTo(training));
            Assert.That(hire.MarketValue, Is.GreaterThan(PlayerMarket.Value(candidate, hire.Trait)));
            Assert.That(hire.Health, Is.EqualTo(hire.EffectiveStats.Health));
            Assert.That(hire.Contract.DailyWage, Is.EqualTo(CrewContract.ExpectedWage(candidate, hire.Trait, trainingSessions: 2)));
            Assert.That(run.Party.Gold, Is.EqualTo(gold - price - hire.Contract.BaseTotal));
            Assert.That(run.TrainAtCamp(hire.Id), Is.False);
            Assert.That(run.CanUseCamp, Is.True);
        }

        private static AdventureCheckpoint RoundTrip(AdventureCheckpoint checkpoint) =>
            UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(checkpoint));

        [Test]
        public void TownContractsChargeOnceAndRoundTripWithTheirCandidateAndReserve()
        {
            var saved = Create(41, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var starter = saved.Party.Members[0];
            starter.EmployeeAttributes = default;
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var legacyRatings = run.Party.Members.Single(m => m.Id == starter.Id).EmployeeAttributes;
            Assert.That(FiveRatings(legacyRatings), Is.All.InRange(1, 5));
            Assert.That(legacyRatings.Satisfaction, Is.EqualTo(3));
            var wages = run.Party.Members.Sum(m => m.Contract.DailyWage);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Party.Day, Is.EqualTo(2));
            Assert.That(run.Party.Gold, Is.EqualTo(saved.Party.Gold - wages));
            var board = run.Current.Recruits;
            Assert.That(board.Select(c => c.Id).Distinct().Count(), Is.EqualTo(8));
            var candidate = board[0];
            var talent = run.Party.TalentFor(candidate);
            int price = run.Party.SigningPrice(candidate), balance = run.Party.Gold;
            Assert.That(price, Is.GreaterThan(PlayerMarket.Value(candidate, talent)));
            Assert.That(run.ChooseTownRecruit(candidate.Id, 14, new PerformanceBonuses { Kill = -1 }), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(balance));
            Assert.That(run.ChooseTownRecruit(candidate.Id, 0), Is.False);
            Assert.That(run.ChooseTownRecruit(candidate.Id, 14, new PerformanceBonuses { Kill = 200 }, 1), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(balance));
            Assert.That(run.Party.Members.Any(m => m.Character.Id == candidate.Id), Is.False);
            Assert.That(run.ChooseTownRecruit(candidate.Id, 14, new PerformanceBonuses { Kill = 17 }), Is.True);
            Assert.That(run.ChooseTownRecruit(candidate.Id), Is.False);
            var ratings = run.Party.Members.Single(m => m.Character.Id == candidate.Id).EmployeeAttributes;
            Assert.That(FiveRatings(ratings), Is.All.InRange(1, 5));
            Assert.That(ratings.Satisfaction, Is.EqualTo(3));
            var json = UnityEngine.JsonUtility.ToJson(run.CheckpointForExit());
            var restored = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json);
            Assert.That(restored.Party.Members.Single(m => m.Character == candidate.Id).EmployeeAttributes,
                Is.EqualTo(ratings));
            run = AdventureRun.RestoreCheckpoint(restored, content.CharacterCatalog, gear);
            var hire = run.Party.Members.Single(m => m.Character.Id == candidate.Id);
            Assert.That(hire.EmployeeAttributes, Is.EqualTo(ratings));
            Assert.That(run.Party.Members.Single(m => m.Id == starter.Id).EmployeeAttributes, Is.EqualTo(legacyRatings));
            Assert.That(run.Party.Day, Is.EqualTo(2));
            Assert.That(run.Party.Gold, Is.EqualTo(balance - price - hire.Contract.BaseTotal));
            Assert.That(hire.MarketValue, Is.EqualTo(PlayerMarket.Value(candidate, talent, trainingSessions: hire.Training.Sessions)));
            Assert.That(hire.Contract.AgreedRates.Kill, Is.EqualTo(17));
            Assert.That(hire.Trait, Is.EqualTo(talent));
            Assert.That(hire.Contract.EndDay, Is.EqualTo(16));
            Assert.That(hire.Contract.Bonuses, Is.EqualTo(BonusTerms.Kill));
            Assert.That(hire.Contract.CurrentReleaseFee, Is.Zero);
            Assert.That(hire.Position, Is.Null);
            foreach (var c in content.CharacterCatalog.Where(c => c.RosterNumber > 0)) run.Party.TryRecruit(c);
            Assert.That(run.Party.Members.Count, Is.EqualTo(6));
            var nextHire = content.CharacterCatalog.First(c => c.RosterNumber > 0 &&
                run.Party.Members.All(m => m.Character.Id != c.Id));
            Assert.That(run.Party.TryRecruit(nextHire), Is.False);
            Assert.That(run.Party.CaptureFormation(Side.Party).Count, Is.EqualTo(4));
            Assert.That(run.Party.CanStartBattle, Is.True);
            int offer = run.Party.TransferOffer(hire.Id);
            Assert.That(offer, Is.LessThan(hire.MarketValue));
            Assert.That(run.Party.SellMember(hire.Id), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(balance - price - hire.Contract.BaseTotal + offer - hire.Contract.ReleaseFee));
            Assert.That(run.Party.TryRecruit(nextHire), Is.True);
            Assert.That(run.Party.Members.Count, Is.EqualTo(6));
        }

        [Test]
        public void UnsignedMemberStaysThroughExpiryAndLeavesTheNextDayWithTheirGearInTheBag()
        {
            var checkpoint = Create(14, false).CheckpointForExit();
            checkpoint.Options = new[] { NodeKind.Town };
            checkpoint.Party.Day = 10;
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(run.Party.NeedsRenewal, Is.True);
            var leaving = run.Party.Members.First();
            var item = run.Party.Inventory.First();
            Assert.That(run.Party.TryEquip(leaving.Id, 0, item.Id), Is.True);
            foreach (var m in run.Party.Members.Skip(1)) Assert.That(run.Party.SetContract(m.Id, 15, BonusTerms.Appearance), Is.True);
            int expiry = leaving.Contract.EndDay;
            for (int day = 11; day <= expiry + 1; day++)
            {
                Assert.That(run.EnterRoute(0), Is.True);
                Assert.That(run.Party.Day, Is.EqualTo(day));
                Assert.That(run.Leave(), Is.True);
                checkpoint = run.CheckpointForExit();
                checkpoint.Options = new[] { NodeKind.Town };
                run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
                if (day != expiry) continue;
                Assert.That(run.Party.NeedsRenewal, Is.True);
                Assert.That(run.Party.CaptureFormation(Side.Party).Any(unit => unit.Id == leaving.Id), Is.True);
            }
            Assert.That(run.Party.NeedsRenewal, Is.False);
            Assert.That(run.Party.Members.Any(m => m.Id == leaving.Id), Is.False);
            Assert.That(run.Party.Inventory.Any(i => i.Id == item.Id), Is.True);
            Assert.That(run.Party.Expenses.Any(e => e.Day == expiry + 1 && e.Reason.Contains(leaving.Character.Name)), Is.False);
        }

        [Test]
        public void LastMemberCanRenewOnTheExpiryDayAndTheNewContractStartsImmediately()
        {
            var saved = Create(14, false).CheckpointForExit();
            saved.Party.Members = saved.Party.Members.Take(1).ToArray();
            saved.Party.Day = saved.Party.Members[0].Contract.EndDay - 1;
            saved.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            var party = run.Party;
            Assert.That(party.CanAdvanceDay, Is.True);
            Assert.That(run.EnterRoute(0), Is.True);
            var member = party.Members.Single();
            Assert.That(member.Contract.EndDay, Is.EqualTo(party.Day));
            Assert.That(party.CanAdvanceDay, Is.False);
            long fee = 3L * party.ExpectedDailyWage(member.Id);
            Assert.That(party.SetContract(member.Id, 3, default, fee), Is.True);
            var renewed = party.Members.Single();
            Assert.That(renewed.Contract.StartDay, Is.EqualTo(party.Day));
            Assert.That(renewed.Renewals, Is.Empty);
            Assert.That(party.CanAdvanceDay, Is.True);
        }

        [Test]
        public void EveryForkPathHasThreeMandatoryBattlesAndOneTreasureChest()
        {
            var chestLocations = new HashSet<int>();
            var townLocations = new HashSet<int>();
            for (int seed = 0; seed < 120; seed++)
            {
                var route = new DockRoute(new Random(seed));
                Assert.That(route.Nodes.Select(n => n.Number), Is.EqualTo(Enumerable.Range(1, 10)));
                Assert.That(route.Nodes[0].Kind, Is.EqualTo(NodeKind.NormalBattle));
                Assert.That(route.Nodes[9].Kind, Is.EqualTo(NodeKind.Boss));
                Assert.That(route.Nodes.Count(n => n.IsFork), Is.EqualTo(5));
                Assert.That(route.Nodes.Count(n => !n.IsFork && n.Kind is NodeKind.NormalBattle or NodeKind.EliteBattle), Is.EqualTo(3));
                var chest = route.Nodes.Single(n => n.Kind == NodeKind.TreasureChest);
                Assert.That(chest.IsFork, Is.False);
                Assert.That(chest.Number, Is.InRange(3, 8));
                chestLocations.Add(chest.Number);
                Assert.That(route.Nodes.Count(n => n.Kind == NodeKind.Camp && n.Number is >= 4 and <= 6), Is.EqualTo(1));
                Assert.That(route.Nodes.Count(n => n.Kind == NodeKind.Camp && n.Number is >= 7 and <= 9), Is.EqualTo(1));
                var town = route.Nodes.Single(n => n.Kind == NodeKind.Town);
                Assert.That(town.IsFork, Is.True);
                Assert.That(town.Number, Is.InRange(2, 9));
                Assert.That(route.Nodes.Any(n => n.Kind == NodeKind.Shop || n.Alternative == NodeKind.Shop), Is.False);
                townLocations.Add(town.Number);
                var random = new Random(seed * 13 + 4);
                for (int path = 0; path < 32; path++)
                {
                    int elites = 0, fights = 0, fork = 0, chests = 0;
                    for (int stage = 1; stage <= 10; stage++)
                    {
                        var options = route.Options(stage, 0, elites, random);
                        Assert.That(options.Count, Is.EqualTo(route.Nodes[stage - 1].IsFork ? 2 : 1));
                        Assert.That(options.Distinct().Count(), Is.EqualTo(options.Count));
                        Assert.That(options.Count(n => n is NodeKind.Camp or NodeKind.Shop or NodeKind.Town), Is.LessThanOrEqualTo(1));
                        if (options.Contains(NodeKind.TreasureChest))
                            Assert.That(options.Count, Is.EqualTo(1));
                        var chosen = options[options.Count == 2 ? (path >> fork++) & 1 : 0];
                        if (chosen == NodeKind.TreasureChest) chests++;
                        if (chosen == NodeKind.EliteBattle) { elites++; Assert.That(stage, Is.InRange(5, 9)); }
                        if (chosen is NodeKind.NormalBattle or NodeKind.EliteBattle) fights++;
                    }
                    Assert.That(fights, Is.GreaterThanOrEqualTo(3));
                    Assert.That(elites, Is.InRange(1, 3));
                    Assert.That(chests, Is.EqualTo(1));
                }
            }
            Assert.That(chestLocations, Is.EquivalentTo(Enumerable.Range(3, 6)));
            Assert.That(townLocations, Is.EquivalentTo(Enumerable.Range(2, 8)));
        }

        [TestCase(NodeKind.Camp)]
        [TestCase(NodeKind.Town)]
        [TestCase(NodeKind.Shop)]
        public void ServiceCanBeSkippedByChoosingThePreviewedBranch(NodeKind service)
        {
            var saved = Create(17, false).CheckpointForExit();
            saved.Nodes[0].Kind = service;
            saved.Nodes[1].Kind = NodeKind.Event;
            saved.Nodes[1].Fork = true;
            saved.Nodes[1].AlternativePlusOne = (int)NodeKind.NormalBattle + 1;
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            if (service != NodeKind.Camp)
            {
                Assert.That(run.FollowingOptions, Is.EqualTo(new[] { NodeKind.Event }));
                Assert.That(run.EnterFollowing(0), Is.True);
                Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.TidePact));
                Assert.That(run.DeclineEvent(), Is.True);
            }
            var destination = run.FollowingChoice(1);
            Assert.That(run.CompletedNodes, Is.EqualTo(service == NodeKind.Camp ? 0 : 1));
            Assert.That(run.EnterFollowing(2), Is.False);
            var expected = service == NodeKind.Camp ? NodeKind.Camp : NodeKind.Event;
            Assert.That(run.Current.Kind, Is.EqualTo(expected));
            int departureDay = run.Party.Day;
            AdventureCheckpoint completed = null;
            Assert.That(run.EnterFollowing(1, () => completed = run.CheckpointForExit()), Is.True);
            Assert.That(completed.Party.Day, Is.EqualTo(departureDay));
            Assert.That(completed.Choice, Is.EqualTo(-1));
            Assert.That(completed.CompletedNodes, Is.EqualTo(run.CompletedNodes));
            Assert.That(run.RouteCurrent.Kind, Is.EqualTo(expected));
            Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.NormalBattle));
            Assert.That(run.RouteDestination.Value, Is.EqualTo(destination));
            Assert.That(run.CompletedNodes, Is.EqualTo(service == NodeKind.Camp ? 1 : 2));
        }

        [TestCase(NodeKind.Shop)]
        [TestCase(NodeKind.Town)]
        public void ShopDrawsAndPurchasesSurviveSavingBeforeAndAfterSigning(NodeKind kind)
        {
            var checkpoint = Create(17, false).CheckpointForExit();
            checkpoint.Options = new[] { kind };
            checkpoint.Party.Members = checkpoint.Party.Members.Take(2).ToArray();
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var originalIds = run.Party.Members.Select(member => member.Character.Id).ToArray();
            var slots = run.Current.ShopRecruitment.Slots.Select(slot => slot.Id).ToArray();
            Assert.That(slots.Distinct().Count(), Is.EqualTo(8));
            Assert.That(run.Current.ShopRecruitment.Slots.Count(slot => slot.Stage == 2), Is.EqualTo(1));
            Assert.That(slots.Intersect(originalIds), Is.Empty);
            int gold = run.Party.Gold;
            int itemPrice = run.Current.Stock[0].Price - 2 * run.Party.Members.Count(m => m.Trait == PlayerTrait.Rebate);
            Assert.That(run.Buy(0), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(run.DrawShopRecruit(), Is.True);
            var drawn = run.Current.ShopRecruitment.Candidates.Select(candidate => candidate.Id).ToArray();
            Assert.That(drawn.Distinct().Count(), Is.EqualTo(3));
            Assert.That(slots, Is.SupersetOf(drawn));
            Assert.That(run.Party.Members.Count, Is.EqualTo(2));
            Assert.That(run.Party.Gold, Is.EqualTo(gold - itemPrice - 60));

            run = ResumeShop(run);
            Assert.That(run.Current.Stock[0].Sold, Is.True);
            Assert.That(run.Buy(0), Is.False);
            Assert.That(run.Current.ShopRecruitment.Slots.Select(slot => slot.Id), Is.EqualTo(slots));
            Assert.That(run.Current.ShopRecruitment.Candidates.Select(candidate => candidate.Id), Is.EqualTo(drawn));
            Assert.That(run.Current.ShopRecruitment.NextPrice, Is.EqualTo(30));
            Assert.That(run.Party.Gold, Is.EqualTo(gold - itemPrice - 60));
            Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.Town));
            Assert.That(run.Current.UsedServices, Is.EquivalentTo(new[] { TownService.Shop }));
            Assert.That(run.SignShopRecruit(drawn[0]), Is.True);
            Assert.That(run.Party.Members.Select(member => member.Character.Id), Has.Member(drawn[0]));

            run = ResumeShop(run);
            Assert.That(run.Current.ShopRecruitment.Signed, Is.True);
            Assert.That(run.SignShopRecruit(drawn[1]), Is.False);
            Assert.That(run.DrawShopRecruit(), Is.False);
            Assert.That(run.Party.Members.Count, Is.EqualTo(3));
            Assert.That(run.Party.Gold, Is.EqualTo(gold - itemPrice - 60 - run.Party.Members.Single(m => m.Character.Id == drawn[0]).Contract.BaseTotal));

            AdventureRun ResumeShop(AdventureRun current)
            {
                string json = UnityEngine.JsonUtility.ToJson(current.CheckpointForExit());
                var saved = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json);
                return AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            }
        }

        [Test]
        public void LegacyShopBecomesATownWithItsSoldStockAndFutureCampIntact()
        {
            var checkpoint = Create(17, false).CheckpointForExit();
            checkpoint.Options = new[] { NodeKind.Town };
            var original = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(original.EnterRoute(0) && original.Buy(0), Is.True);
            var saved = original.CheckpointForExit();
            saved.Options[0] = saved.Nodes[0].Kind = NodeKind.Shop;
            saved.ShopVisit.TownRecruits = Array.Empty<string>();
            saved.ShopVisit.Stock[1].Price = 0;
            saved.Nodes[1].Kind = NodeKind.Town;
            saved.Nodes[1].Replaces = (int)NodeKind.Camp;
            saved.FollowingOptions = new[] { NodeKind.Town, NodeKind.Event };

            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.Town));
            Assert.That(run.Current.Recruits.Count, Is.EqualTo(8));
            Assert.That(run.Current.Stock[0].Sold && !run.Buy(0), Is.True);
            Assert.That(run.Current.Stock[0].Price, Is.EqualTo(original.Current.Stock[0].Price));
            Assert.That(run.Current.Stock[1].Price, Is.EqualTo(run.Current.Stock[1].Item.Price));
            Assert.That(run.Party.Gold, Is.EqualTo(original.Party.Gold));
            Assert.That(run.Party.Day, Is.EqualTo(original.Party.Day));
            Assert.That(run.FollowingOptions, Is.EqualTo(new[] { NodeKind.Event }));
            Assert.That(run.Route.Nodes[1].Kind, Is.EqualTo(NodeKind.Camp));

            var secondSave = run.CheckpointForExit();
            secondSave.ShopVisit.TownRecruits = Array.Empty<string>(); // A real town with a sold-out market.
            run = AdventureRun.RestoreCheckpoint(secondSave, content.CharacterCatalog, gear);
            Assert.That(run.Current.Recruits, Is.Empty);
            Assert.That(run.EnterFollowing(0), Is.True);
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.TidePact));
            Assert.That(run.DeclineEvent() && run.EnterFollowing(0), Is.True);
            Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.Camp));
        }

        [Test]
        public void ShopPriceWrapsThroughAFreeSpinAcrossVisits()
        {
            var checkpoint = Create(17, false).CheckpointForExit();
            checkpoint.Options = new[] { NodeKind.Shop };
            checkpoint.Nodes[1].Kind = NodeKind.Shop;
            checkpoint.ShopRecruitPricePlusOne = 96;
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            int gold = run.Party.Gold;
            Assert.That(run.Current.ShopRecruitment.NextPrice, Is.EqualTo(95));
            Assert.That(run.DrawShopRecruit(), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(gold - 95));
            Assert.That(run.Current.ShopRecruitment.NextPrice, Is.Zero);
            Assert.That(run.Leave(), Is.True);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.TidePact));
            Assert.That(run.DeclineEvent() && run.EnterFollowing(0), Is.True);
            Assert.That(run.Current.ShopRecruitment.Candidates, Is.Empty);
            Assert.That(run.Current.ShopRecruitment.NextPrice, Is.Zero);
            Assert.That(run.DrawShopRecruit(), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(gold - 95 - 2 * run.Party.Members.Sum(m => m.Contract.DailyWage)));
            Assert.That(run.Current.ShopRecruitment.NextPrice, Is.EqualTo(5));
            for (int i = 1; i < ShopRecruitment.SlotCount; i++) Assert.That(run.DrawShopRecruit(), Is.True);
            Assert.That(run.Current.ShopRecruitment.Candidates, Is.EquivalentTo(run.Current.ShopRecruitment.Slots));
            int remainingGold = run.Party.Gold;
            Assert.That(run.DrawShopRecruit(), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(remainingGold));
        }

        [Test]
        public void RandomEventsAreRememberedAfterDecliningAndResumingOnAnotherFloor()
        {
            var checkpoint = Create(17, false).CheckpointForExit();
            checkpoint.Options = new[] { NodeKind.Event };
            checkpoint.Choice = 0; // Use a fixed node seed for a repeatable encounter.
            checkpoint.Seed = Enumerable.Range(0, 100).First(seed => new Random(seed).Next(4) == 2);
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.FoundCoins));

            var suspended = AdventureRun.RestoreCheckpoint(run.CheckpointForExit(), content.CharacterCatalog, gear);
            Assert.That(suspended.EnterRoute(0), Is.True);
            Assert.That(suspended.Current.Event, Is.EqualTo(AdventureEventKind.FoundCoins));
            Assert.That(suspended.DeclineEvent() && suspended.CloseVisit(), Is.True);

            var saved = suspended.CheckpointForExit();
            Assert.That(saved.SeenEvents, Is.EqualTo(new[] { AdventureEventKind.FoundCoins }));
            saved.FloorCleared = true;
            var next = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(next.Floor, Is.EqualTo(2));
            Assert.That(next.Stage, Is.EqualTo(1));
            Assert.That(next.NextFloor(), Is.False);
            Assert.That(next.Party.Day, Is.EqualTo(saved.Party.Day));
            Assert.That(next.AvailableEvents(), Has.No.Member(AdventureEventKind.FoundCoins));
            Assert.That(next.AvailableEvents().All(kind => GraveStories.Contains(kind)), Is.True);
            Assert.That(next.AvailableEvents(), Has.Member(AdventureEventKind.Obituary));
        }

        [Test]
        public void ExhaustedEventPoolStillRollsAmbushWithoutMarkingItSeen()
        {
            var checkpoint = Create(17, false).CheckpointForExit();
            checkpoint.SeenEvents = Enum.GetValues(typeof(AdventureEventKind)).Cast<AdventureEventKind>()
                .Where(kind => kind != AdventureEventKind.Ambush).ToArray();
            checkpoint.Options = new[] { NodeKind.Event };
            checkpoint.Nodes[1].Kind = NodeKind.Event;
            checkpoint.Nodes[1].Fork = false;
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(run.AvailableEvents(), Is.EqualTo(new[] { AdventureEventKind.Ambush }));
                Assert.That(run.EnterRoute(0), Is.True);
                Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.Ambush));
                Assert.That(run.DeclineEvent() && run.CloseVisit(), Is.True);
            }
            Assert.That(run.CheckpointForExit().SeenEvents, Is.EquivalentTo(checkpoint.SeenEvents));
        }

        [Test]
        public void TreasureChestOffersThreeDistinctNonPotionItemsAfterOpening()
        {
            var drawn = new List<EquipmentDefinition>();
            for (int seed = 0; seed < 40; seed++)
            {
                var party = Create(seed).Party;
                var run = new AdventureRun(party, content.CharacterCatalog, gear.Concat(content.SkillBooks), true, new Random(seed));
                Assert.That(run.Inspect(NodeKind.TreasureChest), Is.True);
                var choices = run.Current.Rewards;
                drawn.AddRange(choices);
                Assert.That(choices.Select(item => item.Id).Distinct().Count(), Is.EqualTo(3));
                Assert.That(choices.All(item => !item.IsPotion && item.Tier is >= 1 and <= 3), Is.True);
                Assert.That(run.ClaimReward(0), Is.False);
                Assert.That(run.OpenTreasureChest(), Is.True);
                Assert.That(run.OpenTreasureChest(), Is.False);
                var expected = choices[1];
                Assert.That(run.ClaimReward(1), Is.True);
                Assert.That(run.ClaimReward(0), Is.False);
                Assert.That(party.Inventory.Concat(party.SkillBooks).Last(item => item.Definition.Id == expected.Id).Definition,
                    Is.SameAs(expected));
                Assert.That(run.CloseVisit(), Is.True);
            }
            Assert.That(drawn.Any(item => item is SkillBookDefinition), Is.True);
        }

        [Test]
        public void InvestmentAccumulatesAcrossFloorsPaysExactlyOnceAndStopsAppearing()
        {
            var run = Create(); int initial = run.Party.Inventory.Count;
            run.Inspect(NodeKind.Event, AdventureEventKind.Investor);
            Assert.That(run.Donate(20), Is.True);
            Assert.That(run.Donate(20), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(980));
            run.CloseVisit(); run.Inspect(NodeKind.Event, AdventureEventKind.Investor);
            Assert.That(run.Current.InvestorAvailable, Is.False);
            Assert.That(run.Donate(30), Is.False);
            run.Leave(); run.NextFloor(); run.Inspect(NodeKind.Event, AdventureEventKind.Investor);
            Assert.That(run.Donate(30), Is.True);
            Assert.That(run.InvestmentPaid, Is.True);
            Assert.That(run.Party.Inventory.Count, Is.EqualTo(initial + 2));
            Assert.That(run.Party.Inventory.Skip(initial).All(i => i.Definition.Tier == 1), Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(950));
            run.CloseVisit(); run.NextFloor();
            Assert.That(run.AvailableEvents().Contains(AdventureEventKind.Investor), Is.False);
        }

        [Test]
        public void ForgeConsumesOnlyValidInputsAndCreatesAnUnownedHigherItem()
        {
            var run = Create(); run.Inspect(NodeKind.Event, AdventureEventKind.Forge);
            var inputs = run.Party.Inventory.Take(2).ToArray();
            var owned = run.Party.Inventory.Select(i => i.Definition.Id).ToArray();
            int count = run.Party.Inventory.Count;
            Assert.That(run.Forge(inputs[0].Id, inputs[0].Id), Is.False);
            Assert.That(run.Party.Inventory.Count, Is.EqualTo(count));
            Assert.That(run.Forge(inputs[0].Id, inputs[1].Id), Is.True);
            Assert.That(run.Party.Inventory.Count, Is.EqualTo(count - 1));
            var item = run.Party.Inventory.Single(i => !owned.Contains(i.Definition.Id));
            foreach (var employee in run.Party.Members)
            {
                string line = EmployeeVoice.For(employee.Personality).Say(EmployeeThought.Forge, item.Definition.Name);
                Assert.That(run.Party.EmployeeProfileFor(employee.Id).Thoughts, Does.Contain(line));
            }
            Assert.That(item.Definition.Tier, Is.LessThan(inputs.Min(i => i.Definition.Tier)));
            Assert.That(run.Party.Inventory.Any(i => inputs.Any(old => old.Id == i.Id)), Is.False);
            Assert.That(run.Forge(inputs[0].Id, inputs[1].Id), Is.False);

            var blockedParty = new PartyRun(content.Party.Select(u => new PartyMemberStart(u.Id, u.Character, u.Position, u.Health)), gear);
            var blocked = new AdventureRun(blockedParty, content.CharacterCatalog, gear, true, new Random(9));
            blocked.Inspect(NodeKind.Event, AdventureEventKind.Forge);
            var pair = blocked.ForgeInputs.Where(i => i.Definition.Tier == 3).Take(2).ToArray();
            Assert.That(blocked.Forge(pair[0].Id, pair[1].Id), Is.False);
            Assert.That(blocked.Party.Inventory.Count, Is.EqualTo(gear.Length));
        }

        [Test]
        public void ChestReservesTheCompanionAndTheirEquipmentThenReturnsAfterThreeMoreNodes()
        {
            var run = Create(); var guard = run.Party.Members.First();
            string itemId = run.Party.Inventory.First().Id;
            Assert.That(run.Party.TryEquip(guard.Id, 0, itemId), Is.True);
            run.Inspect(NodeKind.Event, AdventureEventKind.LockedChest);
            Assert.That(run.LeaveCompanion(guard.Id), Is.True);
            run.CloseVisit();
            Assert.That(run.Party.CaptureFormation(Side.Party).Count, Is.EqualTo(3));
            Assert.That(run.Party.CanStartBattle, Is.True);
            Assert.That(run.Party.TryPlace(guard.Id, 1), Is.False);
            Assert.That(run.Party.TryEquip(guard.Id, 0, null), Is.False);
            Assert.That(run.Party.TryReplaceMember(guard.Id, content.CharacterCatalog.First(c => c.RosterNumber == 2)), Is.False);
            for (int i = 0; i < 3; i++)
            {
                run.Inspect(NodeKind.Event, AdventureEventKind.FoundCoins); run.CollectCoins(); run.CloseVisit();
                Assert.That(run.Party.Members.First(m => m.Id == guard.Id).IsAway, Is.EqualTo(i < 2));
            }
            Assert.That(run.Party.CaptureFormation(Side.Party).Count, Is.EqualTo(4));
            Assert.That(run.Party.Members.First(m => m.Id == guard.Id).EquipmentIds[0], Is.EqualTo(itemId));
            Assert.That(run.ReturnMessage, Does.Contain(guard.Character.Name));
            var memories = run.Party.EmployeeProfileFor(guard.Id).Career;
            Assert.That(memories.LatestMemory.Topic, Is.EqualTo(EmployeeThought.ChestReturn));
            Assert.That(memories.PreviousMemory.Topic, Is.EqualTo(EmployeeThought.ChestDuty));
            var witness = run.Party.Members.First(m => m.Id != guard.Id && m.Position.HasValue);
            Assert.That(run.Party.EmployeeProfileFor(witness.Id).Career.LatestMemory.Topic, Is.EqualTo(EmployeeThought.FoundCoins));
            int gold = run.Party.Gold, items = run.Party.Inventory.Count;
            run.AdvanceInspection();
            Assert.That(run.Party.Gold, Is.EqualTo(gold)); Assert.That(run.Party.Inventory.Count, Is.EqualTo(items));
        }

        [Test]
        public void HatGameTracksEverySwapRejectsEarlyPicksAndRevealsAllRewards()
        {
            var run = Create(); run.Inspect(NodeKind.Event, AdventureEventKind.HatTrick);
            int count = run.Party.Inventory.Count;
            Assert.That(run.Current.HatRewards.Select(i => i.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(run.PickHat(0), Is.False);
            var expected = new[] { 0, 1, 2 };
            for (int i = 0; i < run.Current.HatSwaps.Count; i++)
            {
                var pair = run.Current.HatSwaps[i];
                (expected[pair.First], expected[pair.Second]) = (expected[pair.Second], expected[pair.First]);
                Assert.That(run.StepHat(i), Is.True);
                Assert.That(run.StepHat(i), Is.False);
            }
            Assert.That(run.Current.HatPositions, Is.EqualTo(expected));
            Assert.That(run.PickHat(Array.IndexOf(expected, 0)), Is.True);
            Assert.That(run.Current.HatWon, Is.True);
            Assert.That(run.Party.Inventory.Count, Is.EqualTo(count + 1));
            Assert.That(run.PickHat(0), Is.False);
        }

        [Test]
        public void RecruitmentOffersTwoProfessionsAndAddsTheFifthMemberToReserves()
        {
            var run = Create(); var old = run.Party.Members.First();
            string item = run.Party.Inventory.First().Id; run.Party.TryEquip(old.Id, 0, item);
            run.Inspect(NodeKind.Event, AdventureEventKind.LostTraveler);
            Assert.That(run.Current.Professions.Count, Is.EqualTo(2));
            var profession = run.Current.Professions.First();
            var preview = run.Current.Recruits.Single(candidate => candidate.Profession == profession);
            Assert.That(run.ChooseProfession(profession), Is.True);
            Assert.That(run.Current.PendingRecruit, Is.Null);
            var recruit = run.Party.Members.Single(m => m.Character == preview);
            Assert.That(recruit.Position, Is.Null);
            Assert.That(recruit.Contract.EndDay - run.Party.Day, Is.EqualTo(12));
            Assert.That(run.Party.Members.Count, Is.EqualTo(5));
            Assert.That(run.Party.Members.Single(m => m.Id == old.Id).EquipmentIds, Does.Contain(item));
        }

        [Test]
        public void ShopCanSellSkillBooksForGoldAndDeliverThemToTheBookInventory()
        {
            var party = Create().Party;
            var run = new AdventureRun(party, content.CharacterCatalog, content.SkillBooks, true, new Random(34));
            run.Inspect(NodeKind.Town);
            Assert.That(run.Current.Stock.Count, Is.EqualTo(4));
            var offer = run.Current.Stock[0];
            Assert.That(offer.Item, Is.InstanceOf<SkillBookDefinition>());
            Assert.That(offer.Price, Is.GreaterThan(0));
            int gold = party.Gold, count = party.SkillBooks.Count;
            Assert.That(run.Buy(0), Is.True);
            Assert.That(run.Buy(0), Is.False);
            Assert.That(party.Gold, Is.EqualTo(gold - offer.Price));
            Assert.That(party.SkillBooks.Count, Is.EqualTo(count + 1));
            Assert.That(party.SkillBooks.Any(i => i.Definition.Id == offer.Item.Id), Is.True);
        }

        [Test]
        public void HiringAReserveMerchantRepricesTheCurrentStockAndSurvivesReload()
        {
            var saved = Create(14, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var undiscounted = run.Current.Stock.Select(o => o.Price).ToArray();
            var merchant = content.CharacterCatalog.Single(c => c.Trait == CharacterTrait.ShopDiscount);

            Assert.That(run.Party.TryRecruit(merchant), Is.True);
            Assert.That(run.Party.Members.Single(m => m.Character.Id == merchant.Id).Position, Is.Null);
            saved = UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(run.CheckpointForExit()));
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);

            Assert.That(run.Current.Stock.Select(o => o.Price), Is.EqualTo(undiscounted.Select(price => (price + 1) / 2)));
            int price = run.Current.Stock[0].Price;
            Assert.That(run.Buy(0), Is.True);
            Assert.That(run.Party.Expenses.Last(e => e.Reason.StartsWith("购买")).Amount, Is.EqualTo(price));
        }

        [Test]
        public void OnlyCampIsLimitedAndAllFourTownServicesCanBeUsed()
        {
            var run = Create(); run.Inspect(NodeKind.Town);
            Assert.That(run.Current.Stock.Count, Is.EqualTo(4));
            Assert.That(run.Current.Stock.Count(o => o.Item.Tier == 1), Is.EqualTo(2));
            Assert.That(run.Rest(), Is.True); Assert.That(run.Rest(), Is.False);
            foreach (var member in run.Party.Members)
                Assert.That(member.Health, Is.EqualTo(member.EffectiveStats.Health));
            int gold = run.Party.Gold, count = run.Party.Inventory.Count, price = run.Current.Stock[0].Price;
            Assert.That(run.Buy(0), Is.True); Assert.That(run.Buy(0), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(gold - price + 2 * run.Party.Members.Count(m => m.Trait == PlayerTrait.Rebate))); Assert.That(run.Party.Inventory.Count, Is.EqualTo(count + 1));
            Assert.That(run.Current.UsedServices.Count, Is.EqualTo(2));
            Assert.That(run.CanUseTown(TownService.Camp), Is.False);
            Assert.That(run.CanUseTown(TownService.Shop), Is.True);
            Assert.That(run.TownQuest(), Is.True);
            var candidates = run.Current.Recruits.ToArray();
            Assert.That(run.ChooseTownRecruit(candidates[0].Id), Is.True);
            Assert.That(run.ChooseTownRecruit(candidates[1].Id), Is.True);
            Assert.That(run.Current.Recruits.Select(c => c.Id), Is.EqualTo(candidates.Skip(2).Select(c => c.Id)));
            Assert.That(run.CanUseTown(TownService.Quest), Is.True);
            Assert.That(run.Buy(1), Is.True);
            run.Leave();
            run.Inspect(NodeKind.Town);
            Assert.That(run.Rest(), Is.True);
        }

        [Test]
        public void SavedTownKeepsCampUseAndPurchasesWhileOtherFacilitiesStayOpen()
        {
            var checkpoint = Create(41, false).CheckpointForExit();
            checkpoint.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(checkpoint, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var member = run.EvolutionCandidates.First();
            Assert.That(run.EvolveAtCamp(member.Id, run.EvolutionForms(member.Id).First().Id), Is.True);
            Assert.That(run.Rest(), Is.False);
            Assert.That(run.Buy(0), Is.True);
            Assert.That(run.TownQuest(), Is.True);
            var candidates = run.Current.Recruits.Select(c => c.Id).ToArray();
            Assert.That(run.ChooseTownRecruit(candidates[0]), Is.True);
            int day = run.Party.Day, gold = run.Party.Gold;
            var saved = run.CheckpointForExit();
            // Older town saves kept signed names on the board.
            saved.ShopVisit.TownRecruits = candidates;
            run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.Party.Day, Is.EqualTo(day));
            Assert.That(run.Party.Gold, Is.EqualTo(gold));
            Assert.That(run.Rest(), Is.False);
            Assert.That(run.CanUseCamp, Is.False);
            Assert.That(run.QuestActive, Is.True);
            Assert.That(run.CanUseTown(TownService.Quest), Is.True);
            Assert.That(run.Buy(0), Is.False);
            Assert.That(run.Buy(1), Is.True);
            Assert.That(run.Current.Recruits.Select(c => c.Id), Does.Not.Contain(candidates[0]));
            Assert.That(run.ChooseTownRecruit(candidates[1]), Is.True);
        }

        [TestCase(NodeKind.NormalBattle, null)]
        [TestCase(NodeKind.EliteBattle, null)]
        [TestCase(NodeKind.Boss, null)]
        [TestCase(NodeKind.Event, AdventureEventKind.Ambush)]
        [TestCase(NodeKind.Event, AdventureEventKind.AscensionTrial)]
        public void ActualBattlesPayCorrectGoldAndOnlyOneChosenReward(NodeKind kind, AdventureEventKind? eventKind)
        {
            var run = kind == NodeKind.Boss ? new AdventureRun(Create().Party, content.CharacterCatalog,
                gear.Concat(content.SkillBooks), true, new Random(17)) : Create();
            var weapon = new EquipmentDefinition("test-power", "test", new Stats(100, 0, 0, 0));
            foreach (var member in run.Party.Members)
                run.Party.TryEquip(member.Id, 0, run.Party.AddEquipment(weapon).Id);
            run.Inspect(kind, eventKind);
            if (eventKind.HasValue) Assert.That(run.StartEventBattle(), Is.True);
            var enemies = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            int startingGold = run.Party.Gold;
            var request = run.Party.BeginBattle(enemies, 200, null, content.RecruitmentPool, content.CharacterCatalog);
            run.BindBattle(request.Id);
            var result = new BattleSimulator().Simulate(request);
            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(run.Party.TrySettle(result, out _), Is.True);
            Assert.That(run.SettleBattle(result), Is.True);
            int minimum = eventKind == AdventureEventKind.Ambush ? 12 : enemies.Sum(e => run.Current.Battle.EliteIds.Contains(e.Id) ? 14 : 6);
            int maximum = eventKind == AdventureEventKind.Ambush ? 18 : enemies.Sum(e => run.Current.Battle.EliteIds.Contains(e.Id) ? 20 : 9);
            Assert.That(run.Party.Gold - startingGold, Is.InRange(minimum, maximum));
            Assert.That(run.SettleBattle(result), Is.False);
            var recorded = run.FloorBattles.Single();
            Assert.That(recorded.Kind, Is.EqualTo(kind));
            Assert.That(recorded.Members.Select(m => (m.UnitId, m.ScoreTenths)),
                Is.EqualTo(result.Ratings.Where(r => r.Side == Side.Party).Select(r => (r.UnitId, r.ScoreTenths))));
            if (eventKind == AdventureEventKind.AscensionTrial)
            {
                var member = run.EvolutionCandidates.First(); var form = run.EvolutionForms(member.Id).First();
                var slots = member.EquipmentIds.ToArray();
                Assert.That(run.Evolve(member.Id, form.Id), Is.True);
                Assert.That(run.Party.Members.Single(m => m.Id == member.Id).EquipmentIds, Is.EqualTo(slots));
                Assert.That(run.Evolve(member.Id, form.Id), Is.False);
            }
            else if (eventKind != AdventureEventKind.Ambush)
            {
                Assert.That(run.Current.Rewards.Count, Is.EqualTo(kind == NodeKind.NormalBattle ? 1 : 2));
                Assert.That(run.Current.Rewards.All(e => kind == NodeKind.Boss ? e.RewardBossId == enemies[0].Character.Id : e.CanDropInBattle && !e.BossExclusive), Is.True);
                int count = run.Party.Inventory.Count;
                Assert.That(run.ClaimReward(0), Is.True); Assert.That(run.ClaimReward(1), Is.False);
                Assert.That(run.Party.Inventory.Count, Is.EqualTo(count + 1));
            }
        }

        [TestCase(1, NodeKind.NormalBattle, 6, 9, 14, 20)]
        [TestCase(2, NodeKind.NormalBattle, 10, 13, 21, 27)]
        [TestCase(3, NodeKind.NormalBattle, 14, 17, 28, 34)]
        [TestCase(2, NodeKind.EliteBattle, 10, 13, 21, 27)]
        [TestCase(3, NodeKind.EliteBattle, 14, 17, 28, 34)]
        [TestCase(2, NodeKind.Boss, 10, 13, 21, 27)]
        [TestCase(3, NodeKind.Boss, 14, 17, 28, 34)]
        public void AdventureBattleGoldUsesTheLayerOfTheEncounter(int floor, NodeKind kind,
            int normalMinimum, int normalMaximum, int eliteMinimum, int eliteMaximum)
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Floor = floor;
            saved.Stage = kind == NodeKind.Boss ? DockRoute.Stages : 5;
            saved.Options = new[] { kind };
            var encounters = floor switch
            {
                2 => AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/WhisperingGraveyardEncounter.asset").ToData(),
                3 => AssetDatabase.LoadAssetAtPath<EncounterAsset>("Assets/RaidPartyManager/PrototypeData/ClockworkCapitalEncounter.asset").ToData(),
                _ => content
            };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear.Concat(content.SkillBooks));
            Assert.That(run.EnterRoute(0), Is.True);
            var enemies = run.PrepareEnemies(encounters.EnemyRoster, encounters.EnemyFormations);
            run.BindBattle("layer-gold");
            var victory = new BattleResult("layer-gold", BattleOutcome.Victory,
                enemies.ToDictionary(enemy => enemy.Id, _ => 0), Array.Empty<BattleEvent>());
            Assert.That(run.SettleBattle(victory, out int gold), Is.True);
            int elite = run.Current.Battle.EliteIds.Count;
            int normal = enemies.Count - elite;
            int minimum = normal * normalMinimum + elite * eliteMinimum;
            int maximum = normal * normalMaximum + elite * eliteMaximum;
            int appearanceFees = kind == NodeKind.NormalBattle ? run.Party.Members.Count(m => m.Trait == PlayerTrait.AppearanceFee) : 0;
            Assert.That(gold, Is.InRange(minimum - appearanceFees, maximum));
        }

        [TestCase("EnemyBlockadeAdmiral", EquipmentEffect.HarborWhistle, 31)]
        [TestCase("EnemyDrownedLanternRay", EquipmentEffect.SunkenFrostChain, 32)]
        [TestCase("EnemyNamelessKing", EquipmentEffect.FacelessCrown, 33)]
        [TestCase("EnemySepulchralMatron", EquipmentEffect.ColdSoulReliquary, 34)]
        public void EachBossOffersOnlyItsOwnEquipmentAndBook(string boss, EquipmentEffect effect, int number)
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            var options = BossSpoils.Roll(boss, catalogue, new Random(23)).Choices;
            Assert.That(options, Has.Length.EqualTo(2));
            Assert.That(options[0].Effect, Is.EqualTo(effect));
            Assert.That(options[0].Price, Is.EqualTo(100));
            Assert.That(options[1].Id, Is.EqualTo($"skill-book-{number}"));
            Assert.That(options.All(item => item.RewardBossId == boss && !item.CanDropInBattle &&
                !item.AvailableInRegularPool && AdventureShopOffer.BasePrice(item) == 100), Is.True);
            Assert.That(options.Select(item => content.IconFor(item.Id)), Has.None.Null);
        }

        [Test]
        public void FloorHistoryCountsSettledFightsOnceAndAveragesBattlesEquallyAcrossReloads()
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            AdventureRun Reload(AdventureCheckpoint state) => AdventureRun.RestoreCheckpoint(
                UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(state)), content.CharacterCatalog, catalogue);
            var saved = Create(71, false).CheckpointForExit();
            saved.Nodes[1].Kind = NodeKind.EliteBattle;
            saved.Nodes[1].Fork = false;
            saved.Nodes[1].AlternativePlusOne = 0;
            var run = Reload(saved);
            string a = run.Party.Members[0].Id, b = run.Party.Members[1].Id;
            BattleRating Rated(string id, int damage) => new BattleRating { UnitId = id, Name = id, Side = Side.Party, HealthDamage = damage };
            void Settle(params BattleRating[] party)
            {
                var enemies = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
                string key = "rated-" + run.Stage;
                run.BindBattle(key);
                var foe = new BattleRating { Side = Side.Enemy, UnitId = enemies[0].Id, HealthDamage = 50 };
                var result = new BattleResult(key, BattleOutcome.Victory, enemies.ToDictionary(e => e.Id, _ => 0),
                    Array.Empty<BattleEvent>(), ratings: BattleRating.ScoreBattle(party.Append(foe)));
                Assert.That(run.SettleBattle(result, out int gold), Is.True);
                Assert.That(run.SettleBattle(result, out _), Is.False);
                run.RecordBattleRewards(gold, Array.Empty<EquipmentDefinition>());
            }

            Assert.That(run.EnterRoute(0), Is.True);
            Settle(Rated(a, 0), Rated(b, 40));
            Assert.That(run.ClaimReward(0) && run.CloseVisit(), Is.True);
            Assert.That(run.EnterRoute(0), Is.True);
            var interrupted = run.CheckpointForExit();
            Assert.That(interrupted.FloorBattles.Select(f => f.Stage), Is.EqualTo(new[] { 1 }));
            run = Reload(interrupted);
            Assert.That(run.EnterRoute(0), Is.True);
            Settle(Rated(a, 30));
            run = Reload(run.CheckpointForExit());
            Assert.That(run.FloorBattles.Select(f => f.Kind), Is.EqualTo(new[] { NodeKind.NormalBattle, NodeKind.EliteBattle }));
            Assert.That(run.FloorBattles.Select(f => f.ScoreFor(null)), Is.EqualTo(new int?[] { 40, 60 }));
            Assert.That(BattleRating.AverageTenths(run.FloorBattles.Select(f => f.ScoreFor(null))), Is.EqualTo(50));
            Assert.That(run.FloorBattles.Select(f => f.ScoreFor(a)), Is.EqualTo(new int?[] { 0, 60 }));
            Assert.That(run.FloorBattles.Select(f => f.ScoreFor(b)), Is.EqualTo(new int?[] { 80, null }));
            Assert.That(BattleRating.AverageTenths(run.FloorBattles.Select(f => f.ScoreFor(b))), Is.EqualTo(80));
        }

        [Test]
        public void OldRewardSaveRescoresItsKnownFightWithoutInventingEarlierBattles()
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            var saved = Create(23, false).CheckpointForExit();
            saved.FirstRatedStage = 0;
            saved.FloorBattles = null;
            saved.Stage = DockRoute.Stages;
            saved.Options = new[] { NodeKind.Boss };
            saved.Choice = 0;
            saved.HasBattleReward = true;
            saved.BattleReward = new BattleRewardCheckpoint { Kind = NodeKind.Boss, Resolved = true, Gold = 5,
                Items = Array.Empty<string>(), Choices = Array.Empty<string>(),
                Ratings = new[] { new BattleRating { UnitId = "medic", Name = "医师", Healing = 4, ScoreTenths = 100 } } };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, catalogue);
            Assert.That(run.FirstRatedStage, Is.EqualTo(DockRoute.Stages));
            Assert.That(run.Current.Battle.Ratings.Single().ScoreTenths, Is.EqualTo(10));
            Assert.That(run.FloorBattles.Single().ScoreFor(null), Is.EqualTo(10));
            run = AdventureRun.RestoreCheckpoint(run.CheckpointForExit(), content.CharacterCatalog, catalogue);
            Assert.That(run.FloorBattles.Single().Stage, Is.EqualTo(DockRoute.Stages));
            Assert.That(run.FloorBattles[0].ScoreFor("absent"), Is.Null);
            Assert.That(run.Current.Battle.Reward.ClaimGold(run.Party) && run.CloseVisit(), Is.True);
            Assert.That(run.Floor, Is.EqualTo(2));
            Assert.That(run.FloorBattles, Is.Empty);
            Assert.That(run.FirstRatedStage, Is.EqualTo(1));
        }

        [Test]
        public void BossChoiceAndGuaranteedRewardsCanBeCollectedSeparatelyAcrossReloads()
        {
            var catalogue = gear.Concat(content.SkillBooks).ToArray();
            var saved = Create(23, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Boss };
            foreach (var member in saved.Party.Members) member.Health = 1;
            saved.Party.Members[1].Position = 0;
            var route = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, catalogue);
            Assert.That(route.EnterRoute(0), Is.True);
            var enemies = route.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            route.BindBattle("boss-reward");
            var victory = new BattleResult("boss-reward", BattleOutcome.Victory,
                enemies.ToDictionary(enemy => enemy.Id, _ => 0), Array.Empty<BattleEvent>(), ratings: BattleRating.ScoreBattle(new[] {
                    new BattleRating { UnitId = route.Party.Members[0].Id, CharacterId = route.Party.Members[0].Character.Id,
                        Name = route.Party.Members[0].Character.Name, Side = Side.Party, DebuffLayers = 4, PoisonDamage = 6, HealthDamage = 6 },
                    new BattleRating { UnitId = enemies[0].Id, CharacterId = enemies[0].Character.Id,
                        Name = enemies[0].Character.Name, Side = Side.Enemy, Fallen = true, DamageTaken = 6 }
                }));
            Assert.That(route.SettleBattle(victory, out int gold), Is.True);
            foreach (var member in route.Party.Members)
                Assert.That(member.Health, Is.EqualTo(Math.Min(member.EffectiveStats.Health, 1 + (member.EffectiveStats.Health + 1) / 2)));
            var healed = route.Party.Members.Select(m => m.Health).ToArray();
            Assert.That(route.SettleBattle(victory, out _), Is.False);
            Assert.That(route.Party.Members.Select(m => m.Health), Is.EqualTo(healed));
            var guaranteed = route.RecordBattleRewards(gold, Array.Empty<EquipmentDefinition>());
            var bonus = guaranteed.Items.Single();
            var choice = route.Current.Rewards[1];
            Assert.That(choice, Is.InstanceOf<SkillBookDefinition>());
            Assert.That(route.ClaimReward(1), Is.True);
            string json = UnityEngine.JsonUtility.ToJson(route.CheckpointForExit());
            route = AdventureRun.RestoreCheckpoint(UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(json),
                content.CharacterCatalog, catalogue);
            Assert.That(route.Current.Battle.Ratings, Is.EqualTo(victory.Ratings));
            Assert.That(route.Current.Rewards, Is.Empty);
            Assert.That(route.ClaimReward(0), Is.False);
            Assert.That(route.Party.SkillBooks.Count(item => item.Definition.Id == choice.Id), Is.EqualTo(1));
            guaranteed = route.Current.Battle.Reward;
            Assert.That(guaranteed.Items.Single().Id, Is.EqualTo(bonus.Id));
            Assert.That(guaranteed.ClaimItem(route.Party, 0), Is.True);
            Assert.That(guaranteed.ClaimItem(route.Party, 0), Is.False);
            Assert.That(guaranteed.ClaimGold(route.Party), Is.True);
            Assert.That(guaranteed.HasUnclaimed, Is.False);
        }

        [TestCase(0, 0)] [TestCase(14, 0)] [TestCase(15, 1)] [TestCase(44, 1)]
        [TestCase(45, 2)] [TestCase(99, 2)]
        public void BossBonusRollUsesExactTierBoundariesAndIncludesRegularStoneArmor(int roll, int expectedTier)
        {
            var ordinaryT0 = gear.First(item => !item.BossExclusive && item.Tier == 0);
            var stone = gear.Single(item => item.Effect == EquipmentEffect.StoneArmor);
            var ordinaryT2 = gear.First(item => item.Tier == 2 && item.CanDropInBattle &&
                !item.IsPotion && item.Effect != EquipmentEffect.RecruitmentNotice);
            var catalogue = gear.Where(item => item.BossExclusive).Concat(content.SkillBooks)
                .Concat(new[] { ordinaryT0, stone, ordinaryT2 });
            var reward = BossSpoils.Roll("EnemyBlockadeAdmiral", catalogue,
                new RewardSample { Fraction = (roll + .5) / 100 }).Equipment;
            Assert.That(reward.Tier, Is.EqualTo(expectedTier));
            Assert.That(reward, Is.Not.InstanceOf<SkillBookDefinition>());
            Assert.That(reward.BossExclusive, Is.False);
            Assert.That(stone.CanDropInBattle, Is.True);
            Assert.That(stone.Price, Is.EqualTo(43));
            if (expectedTier == 1) Assert.That(reward, Is.SameAs(stone));
        }

        [Test]
        public void ExpensiveSkillBooksHaveHalfTheBattleRewardWeightOfTwoCostBooks()
        {
            var books = Enumerable.Range(1, 3).SelectMany(tier => new[] { 2, 3 }.Select(cost =>
                new SkillBookDefinition($"book-{tier}-{cost}", "技能书", "", SkillRequirement.None, cost, tier)));
            var random = new RewardSample();
            var run = new AdventureRun(Create().Party, content.CharacterCatalog, books, true, random);
            var costs = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                run.Inspect(NodeKind.NormalBattle);
                var enemies = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
                run.BindBattle("reward");
                var victory = new BattleResult("reward", BattleOutcome.Victory, enemies.ToDictionary(e => e.Id, _ => 0),
                    Array.Empty<BattleEvent>());
                random.Fraction = (i + 0.5) / 6;
                Assert.That(run.SettleBattle(victory, out _), Is.True);
                costs.Add(((SkillBookDefinition)run.Current.Rewards.Single()).Cost);
            }
            Assert.That(costs.Count(cost => cost == 2), Is.EqualTo(4));
            Assert.That(costs.Count(cost => cost == 3), Is.EqualTo(2));
        }

        [TestCase(false, .019, 0)] [TestCase(false, .02, 1)]
        [TestCase(true, .039, 0)] [TestCase(true, .04, 1)]
        public void RegularBattlesOfferOnlyNonExclusiveEquipmentAtTierZero(bool elite, double roll, int expectedTier)
        {
            var random = new RewardSample();
            var route = Create(random: random);
            route.Inspect(elite ? NodeKind.EliteBattle : NodeKind.NormalBattle);
            var foes = route.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            route.BindBattle("drop-test");
            random.Fraction = roll;
            var victory = new BattleResult("drop-test", BattleOutcome.Victory, foes.ToDictionary(e => e.Id, _ => 0), Array.Empty<BattleEvent>());
            Assert.That(route.SettleBattle(victory, out _), Is.True);
            var item = route.Current.Rewards[0];
            Assert.That(item.Tier, Is.EqualTo(expectedTier));
            Assert.That(item.AvailableInRegularPool, Is.True);
        }

        private sealed class RewardSample : Random
        {
            public double Fraction = 0.5;
            public override double NextDouble() => Fraction;
            public override int Next(int maxValue) => (int)(Fraction * maxValue);
        }

        [TestCase(NodeKind.NormalBattle, 1)]
        [TestCase(NodeKind.EliteBattle, 2)]
        public void SkillBooksUseTheBattleRewardRollAndEnterTheBookPageWhenClaimed(NodeKind kind, int rewards)
        {
            var party = Create().Party;
            var run = new AdventureRun(party, content.CharacterCatalog, content.SkillBooks, true, new Random(13));
            foreach (var member in party.Members)
                party.TryEquip(member.Id, 0, party.AddEquipment(new EquipmentDefinition("test-power", "test", new Stats(100, 0, 0, 0))).Id);
            Assert.That(run.Inspect(kind), Is.True);
            var request = party.BeginBattle(run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations), 200, null);
            run.BindBattle(request.Id);
            var result = new BattleSimulator().Simulate(request);
            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(party.TrySettle(result, out _), Is.True);
            Assert.That(run.SettleBattle(result), Is.True);
            Assert.That(run.Current.Rewards.Select(reward => reward.Id).Distinct().Count(), Is.EqualTo(rewards));
            Assert.That(run.Current.Rewards, Is.All.InstanceOf<SkillBookDefinition>());
            var chosen = run.Current.Rewards[0];
            int gearCount = party.Inventory.Count;
            Assert.That(run.ClaimReward(0), Is.True);
            Assert.That(run.ClaimReward(0), Is.False);
            Assert.That(party.SkillBooks.Single().Definition, Is.SameAs(chosen));
            Assert.That(party.Inventory.Count, Is.EqualTo(gearCount));
        }

        [Test]
        public void ContentHasEveryEventIllustrationAndSceneArtwork()
        {
            var art = AssetDatabase.LoadAssetAtPath<AdventureContentAsset>("Assets/RaidPartyManager/PrototypeData/DockAdventure.asset");
            foreach (AdventureEventKind kind in Enum.GetValues(typeof(AdventureEventKind)))
            {
                Assert.That(art.EventArt(kind), Is.Not.Null, kind.ToString());
                var ink = art.EventInkArt(kind);
                Assert.That(ink, Is.Not.Null, kind.ToString());
                Assert.That(ink, Is.Not.SameAs(art.EventArt(kind)), kind.ToString());
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(ink)) as TextureImporter;
                Assert.That(importer != null && importer.DoesSourceTextureHaveAlpha(), Is.True, kind.ToString());
            }
            Assert.That(art.EventInkShader, Is.Not.Null);
            Assert.That(art.Hat, Is.Not.Null); Assert.That(art.TownBackdrop, Is.Not.Null);
            Assert.That(art.EventParchment, Is.Not.Null);
            Assert.That(art.RouteSignpost, Is.Not.Null);
            var icons = Enum.GetValues(typeof(NodeKind)).Cast<NodeKind>().Select(art.NodeIcon).ToArray();
            Assert.That(icons, Is.All.Not.Null);
            Assert.That(icons.Distinct().Count(), Is.EqualTo(icons.Length));
        }

        [TestCase("vermin", NodeKind.NormalBattle, 1)]
        [TestCase("drill", NodeKind.NormalBattle, 1)]
        [TestCase("clinic", NodeKind.NormalBattle, 4)]
        [TestCase("breach", NodeKind.NormalBattle, 7)]
        [TestCase("bulwark", NodeKind.NormalBattle, 5)]
        [TestCase("hunt", NodeKind.NormalBattle, 0)]
        [TestCase("hunt", NodeKind.EliteBattle, 1)]
        public void CommissionCreditsTheChosenObjectiveOnce(string id, NodeKind kind, int expected)
        {
            var run = Create();
            for (int i = 0; i < 40 && !run.QuestActive; i++)
            {
                run.Inspect(NodeKind.Town);
                run.AcceptCommission(id);
            }
            Assert.That(run.QuestActive, Is.True);
            run.Inspect(kind);
            var opponents = run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
            string enemy = opponents.First().Id, ally = run.Party.Members.First().Id;
            var health = opponents.ToDictionary(unit => unit.Id, _ => 0);
            health[ally] = 1;
            var events = new[] {
                new BattleEvent(1, BattleEventKind.Action, ally, enemy, actorEnergy: new BattleEnergy(3, 1), skillBookId: "book"),
                new BattleEvent(1, BattleEventKind.Effect, ally, enemy, damageTaken: new[] { (DamageType.Magic, 7) }, shieldAbsorbed: 2),
                new BattleEvent(1, BattleEventKind.Effect, enemy, ally, shieldAbsorbed: 5),
                new BattleEvent(1, BattleEventKind.Effect, ally, ally, actionKind: ActionKind.Heal, amount: 4),
                new BattleEvent(1, BattleEventKind.Effect, enemy, enemy, actionKind: ActionKind.Heal, amount: 8),
                new BattleEvent(1, BattleEventKind.Defeated, ally, enemy)
            };
            run.BindBattle("credit");
            var victory = new BattleResult("credit", BattleOutcome.Victory, health, events);
            Assert.That(run.SettleBattle(victory), Is.True);
            Assert.That(run.SettleBattle(victory), Is.False);
            Assert.That(run.QuestProgress, Is.EqualTo(expected));
        }

        [Test]
        public void CommissionBoardAndAcceptedQuoteSurviveReload()
        {
            var saved = Create(42, false).CheckpointForExit();
            saved.Options = new[] { NodeKind.Town };
            var run = AdventureRun.RestoreCheckpoint(saved, content.CharacterCatalog, gear);
            Assert.That(run.EnterRoute(0), Is.True);
            var offers = run.Current.Commissions.ToArray();
            Assert.That(offers.Select(job => job.Difficulty), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(offers.Select(job => job.Gold), Is.Ordered.Ascending);
            run = Restore(run);
            Assert.That(run.Current.Commissions.Select(job => job.Id), Is.EqualTo(offers.Select(job => job.Id)));
            Assert.That(run.AcceptCommission(offers[1].Id), Is.True);
            int quote = run.TownQuestGold;
            run = Restore(run);
            Assert.That(run.Commission.Id, Is.EqualTo(offers[1].Id));
            Assert.That(run.TownQuestGold, Is.EqualTo(quote));
            Assert.That(run.ClaimCommission(), Is.False);
            Assert.That(run.AcceptCommission(offers[0].Id), Is.False);

            AdventureRun Restore(AdventureRun previous) => AdventureRun.RestoreCheckpoint(
                UnityEngine.JsonUtility.FromJson<AdventureCheckpoint>(UnityEngine.JsonUtility.ToJson(previous.CheckpointForExit())),
                content.CharacterCatalog, gear);
        }

        [Test]
        public void QuestTracksVictoriesAcrossVisitsAndPaysOnlyOnce()
        {
            var run = Create();
            foreach (var member in run.Party.Members)
                run.Party.TryEquip(member.Id, 0, run.Party.AddEquipment(new EquipmentDefinition("power", "test", new Stats(100, 0, 0, 0))).Id);
            run.Inspect(NodeKind.Town);
            for (int attempts = 0; attempts < 100 && run.Current.Commissions.All(job => job.Id != "patrol"); attempts++)
                run.Inspect(NodeKind.Town);
            Assert.That(run.AcceptCommission("patrol"), Is.True);
            Assert.That(run.AcceptCommission(run.Current.Commissions[1].Id), Is.False);
            run.Leave();
            for (int i = 0; i < 2; i++)
            {
                run.Inspect(NodeKind.NormalBattle);
                var request = run.Party.BeginBattle(run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations), 200, null);
                run.BindBattle(request.Id); var result = new BattleSimulator().Simulate(request);
                Assert.That(run.Party.TrySettle(result, out _), Is.True); Assert.That(run.SettleBattle(result), Is.True);
                run.ClaimReward(0); run.CloseVisit();
            }
            Assert.That(run.QuestProgress, Is.EqualTo(2));
            run.Inspect(NodeKind.Town); int gold = run.Party.Gold;
            Assert.That(run.TownQuest(), Is.True);
            int paid = gold + 22 + 2 * run.Party.Members.Count(m => m.Trait == PlayerTrait.Contractor && m.Health > 0);
            Assert.That(run.Party.Gold, Is.EqualTo(paid));
            Assert.That(run.QuestActive, Is.False);
            Assert.That(run.TownQuest(), Is.True);
            Assert.That(run.QuestProgress, Is.Zero);
            Assert.That(run.TownQuest(), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(paid));
        }

        [Test]
        public void EveryPaintedBackdropHasSixteenSeparateAnimationCels()
        {
            const string art = "Assets/RaidPartyManager/Art/";
            var galleries = new[] { "BattleBackgrounds", "DockPlayhouse/Panoramas", "DockPlayhouse/Resources/StoryEvents/Backcloth" };
            var originals = galleries.SelectMany(dir => System.IO.Directory.GetFiles(art + dir)
                .Where(f => f.EndsWith(".jpg") || f.EndsWith(".png")))
                .Concat(System.IO.Directory.GetFiles(art + "ServiceStages/Graveyard", "*.jpg"))
                .Concat(new[] { "ServiceStages/MarketHall.jpg", "ServiceStages/Harbour.jpg", "ServiceStages/PineClearing.jpg",
                    "Shop/DockShop-Interior.png", "Events/Town-Background.png", "Camp/Camp-Background.png",
                    "BattleStage/DockSky.png", "BattleStage/GraveyardSky.png" }.Select(f => art + f));
            foreach (var path in originals)
            {
                var original = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
                string format = original.width > 2 * original.height ? "Panoramas" : "Backdrops";
                var frames = Enumerable.Range(0, 16).Select(i => AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(
                    $"{art}DockPlayhouse/Resources/SceneryCels/{format}/{i}/{original.name}.jpg")).ToArray();
                Assert.That(frames.All(t => t != null), Is.True, path);
                Assert.That(frames.Select(t => t.imageContentsHash).Distinct().Count(), Is.EqualTo(16), path);
            }
        }

        [Test]
        public void RecruitmentNoticesCannotFillAnAbsentCompanionsReservedSlot()
        {
            var run = Create(); var members = run.Party.Members;
            var notice = gear.Single(e => e.Effect == EquipmentEffect.RecruitmentNotice);
            run.Party.TryEquip(members[1].Id, 0, run.Party.AddEquipment(notice).Id);
            run.Inspect(NodeKind.Event, AdventureEventKind.LockedChest);
            Assert.That(run.LeaveCompanion(members[0].Id), Is.True); run.CloseVisit();
            run.Inspect(NodeKind.NormalBattle);
            var request = run.Party.BeginBattle(run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations), 200, null,
                content.RecruitmentPool, content.CharacterCatalog);
            Assert.That(request.RecruitmentPool.Count, Is.EqualTo(0));
            Assert.That(request.Combatants.Count(c => c.Side == Side.Party), Is.EqualTo(3));
            run.Party.CancelBattle();
        }

        [Test]
        public void SmallStartingBagsStillUseTheFullPotionPoolForCharacterTraits()
        {
            var mixer = content.CharacterCatalog.Single(c => c.Trait == CharacterTrait.GeneratePotion);
            var party = new PartyRun(new[] { new PartyMemberStart("mixer", mixer, 1, mixer.BaseStats.Health) }, Array.Empty<EquipmentDefinition>());
            party.TryEquip("mixer", 0, party.AddEquipment(new EquipmentDefinition("power", "test", new Stats(100, 0, 0, 0))).Id);
            var run = new AdventureRun(party, content.CharacterCatalog, gear, true, new Random(13));
            Assert.That(party.Inventory.Count, Is.EqualTo(1), "Registering a reward catalog must not grant its items.");
            run.Inspect(NodeKind.NormalBattle);
            var request = party.BeginBattle(run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations), 200, null);
            run.BindBattle(request.Id); var result = new BattleSimulator().Simulate(request);
            Assert.That(result.PotionsEarned, Is.EqualTo(1));
            Assert.That(party.TrySettle(result, out _), Is.True);
            Assert.That(party.Inventory.Count(i => i.Definition.IsPotion), Is.EqualTo(1));
        }
    }
}
