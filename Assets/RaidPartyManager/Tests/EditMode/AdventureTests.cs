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

        private AdventureRun Create(int seed = 17, bool inspection = true)
        {
            var party = new PartyRun(content.Party.Select(u => new PartyMemberStart(u.Id, u.Character,
                u.Position, Math.Max(1, u.Stats.Health - 2))), gear.Where(e => e.Tier == 3).Take(2).Concat(gear.Where(e => e.Tier == 2).Take(2)));
            party.AddGold(1000);
            return new AdventureRun(party, content.CharacterCatalog, gear, inspection, new Random(seed));
        }

        [Test]
        public void EveryForkPathHasThreeMandatoryBattlesAndOneTreasureChest()
        {
            var chestLocations = new HashSet<int>();
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
                Assert.That(route.Nodes.Count(n => (n.Replaces ?? n.Kind) == NodeKind.Camp && n.Number is >= 4 and <= 6), Is.EqualTo(1));
                Assert.That(route.Nodes.Count(n => (n.Replaces ?? n.Kind) == NodeKind.Camp && n.Number is >= 7 and <= 9), Is.EqualTo(1));
                Assert.That(route.Nodes.Count(n => (n.Replaces ?? n.Kind) == NodeKind.Shop), Is.EqualTo(1));
                Assert.That(route.Nodes.Where(n => n.Kind == NodeKind.Town).All(n => n.Number > 5), Is.True);
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
            var destination = run.FollowingChoice(1);
            Assert.That(run.CompletedNodes, Is.Zero);
            Assert.That(run.EnterFollowing(2), Is.False);
            Assert.That(run.Current.Kind, Is.EqualTo(service));
            Assert.That(run.EnterFollowing(1), Is.True);
            Assert.That(run.RouteCurrent.Kind, Is.EqualTo(service));
            Assert.That(run.Current.Kind, Is.EqualTo(NodeKind.NormalBattle));
            Assert.That(run.RouteDestination.Value, Is.EqualTo(destination));
            Assert.That(run.CompletedNodes, Is.EqualTo(1));
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
            Assert.That(next.NextFloor(), Is.True);
            Assert.That(next.AvailableEvents(), Has.No.Member(AdventureEventKind.FoundCoins));
            Assert.That(next.AvailableEvents(), Has.Member(AdventureEventKind.Ambush));
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
        public void RecruitmentOffersTwoProfessionsAndFullPartyReplacementReleasesGear()
        {
            var run = Create(); var old = run.Party.Members.First();
            string item = run.Party.Inventory.First().Id; run.Party.TryEquip(old.Id, 0, item);
            run.Inspect(NodeKind.Event, AdventureEventKind.LostTraveler);
            Assert.That(run.Current.Professions.Count, Is.EqualTo(2));
            var profession = run.Current.Professions.First();
            var preview = run.Current.Recruits.Single(candidate => candidate.Profession == profession);
            run.ChooseProfession(profession);
            var recruit = run.Current.PendingRecruit;
            Assert.That(recruit, Is.SameAs(preview));
            Assert.That(recruit.Profession, Is.EqualTo(profession)); Assert.That(recruit.Stage, Is.EqualTo(1));
            Assert.That(run.CompleteRecruit(old.Id), Is.True);
            Assert.That(run.Party.Members.Count, Is.EqualTo(4));
            Assert.That(run.Party.Members.Single(m => m.Id == old.Id).Character.Id, Is.EqualTo(recruit.Id));
            Assert.That(run.Party.Inventory.Any(i => i.Id == item), Is.True);
            Assert.That(run.Party.Members.Any(m => m.EquipmentIds.Contains(item)), Is.False);
        }

        [Test]
        public void ShopCanSellSkillBooksForGoldAndDeliverThemToTheBookInventory()
        {
            var party = Create().Party;
            var run = new AdventureRun(party, content.CharacterCatalog, content.SkillBooks, true, new Random(34));
            run.Inspect(NodeKind.Shop);
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
        public void TownAllowsOnlyTwoDistinctServicesAndPurchasesUseTheSharedWallet()
        {
            var run = Create(); run.Inspect(NodeKind.Town);
            Assert.That(run.Current.Stock.Count, Is.EqualTo(4));
            Assert.That(run.Current.Stock.Count(o => o.Item.Tier == 1), Is.EqualTo(2));
            Assert.That(run.Rest(), Is.True); Assert.That(run.Rest(), Is.False);
            foreach (var member in run.Party.Members)
                Assert.That(member.Health, Is.EqualTo(member.EffectiveStats.Health));
            int gold = run.Party.Gold, count = run.Party.Inventory.Count, price = run.Current.Stock[0].Price;
            Assert.That(run.Buy(0), Is.True); Assert.That(run.Buy(0), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(gold - price)); Assert.That(run.Party.Inventory.Count, Is.EqualTo(count + 1));
            Assert.That(run.Current.UsedServices.Count, Is.EqualTo(2));
            Assert.That(run.CanUseTown(TownService.Recruit), Is.False); Assert.That(run.TownQuest(), Is.False);
            Assert.That(run.Buy(1), Is.True, "Several purchases still count as a single shop service.");
        }

        [TestCase(NodeKind.NormalBattle, null)]
        [TestCase(NodeKind.EliteBattle, null)]
        [TestCase(NodeKind.Boss, null)]
        [TestCase(NodeKind.Event, AdventureEventKind.Ambush)]
        [TestCase(NodeKind.Event, AdventureEventKind.AscensionTrial)]
        public void ActualBattlesPayCorrectGoldAndOnlyOneChosenReward(NodeKind kind, AdventureEventKind? eventKind)
        {
            var run = Create();
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
            int minimum = eventKind == AdventureEventKind.Ambush ? 5 : enemies.Sum(e => run.Current.Battle.EliteIds.Contains(e.Id) ? 8 : 3);
            int maximum = eventKind == AdventureEventKind.Ambush ? 10 : enemies.Sum(e => run.Current.Battle.EliteIds.Contains(e.Id) ? 12 : 5);
            Assert.That(run.Party.Gold - startingGold, Is.InRange(minimum, maximum));
            Assert.That(run.SettleBattle(result), Is.False);
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
                Assert.That(run.Current.Rewards.All(e => e.Tier >= 1), Is.True);
                int count = run.Party.Inventory.Count;
                Assert.That(run.ClaimReward(0), Is.True); Assert.That(run.ClaimReward(1), Is.False);
                Assert.That(run.Party.Inventory.Count, Is.EqualTo(count + 1));
            }
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

        [Test]
        public void QuestTracksVictoriesAcrossVisitsAndPaysOnlyOnce()
        {
            var run = Create();
            foreach (var member in run.Party.Members)
                run.Party.TryEquip(member.Id, 0, run.Party.AddEquipment(new EquipmentDefinition("power", "test", new Stats(100, 0, 0, 0))).Id);
            run.Inspect(NodeKind.Town); Assert.That(run.TownQuest(), Is.True); run.Leave();
            for (int i = 0; i < 2; i++)
            {
                run.Inspect(NodeKind.NormalBattle);
                var request = run.Party.BeginBattle(run.PrepareEnemies(content.EnemyRoster, content.EnemyFormations), 200, null);
                run.BindBattle(request.Id); var result = new BattleSimulator().Simulate(request);
                Assert.That(run.Party.TrySettle(result, out _), Is.True); Assert.That(run.SettleBattle(result), Is.True);
                run.ClaimReward(0); run.CloseVisit();
            }
            Assert.That(run.QuestWins, Is.EqualTo(2));
            run.Inspect(NodeKind.Town); int gold = run.Party.Gold;
            Assert.That(run.TownQuest(), Is.True); Assert.That(run.TownQuest(), Is.False);
            Assert.That(run.Party.Gold, Is.EqualTo(gold + 15)); Assert.That(run.QuestActive, Is.False);
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
