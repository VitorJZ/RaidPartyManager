// Manual audit: copy this file into Assets/RaidPartyManager/Tests/EditMode in a disposable
// project copy containing the current Assets, Packages and ProjectSettings.
// Run Unity -batchmode -nographics -projectPath COPY -runTests -testPlatform EditMode
// -testFilter CampaignBalanceAudit.SampleCampaigns -testResults RESULTS.xml -logFile AUDIT.log.
// Environment: RAID_AUDIT_OUTPUT=results.json; RAID_AUDIT_SAMPLES=500 (seeds 0..499);
// optional RAID_AUDIT_SCENARIO=before|higher|premium|selected (unset runs all four).
// Comma-separated scenarios are accepted. RAID_AUDIT_PREPARE_PACT=1 reserves offerings
// for the guaranteed dock recruit, to compare cautious and informed operating policies.
// Does not alter production pricing: price overrides exist only in this audit process.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using Random = System.Random;

namespace RaidPartyManager.Tests
{
    public sealed class CampaignBalanceAudit
    {
        private static bool preparePact;
        // Identical policy, seeds and content for every price table; no reloads or enemy lookahead.
        private static readonly int[] Higher = { 0,
            95, 115, 115, 95, 115, 120, 105, 130, 120,
            90, 110, 120, 90, 110, 115, 85, 105, 105,
            100, 125, 125, 110, 135, 135, 95, 115, 115,
            85, 105, 110, 100, 95, 75, 140, 130, 125, 115,
            100, 120, 135, 140 };
        private static readonly int[] Selected = { 0,
            80, 95, 95, 80, 95, 100, 85, 105, 100,
            80, 95, 100, 80, 90, 100, 70, 85, 85,
            85, 105, 105, 90, 110, 110, 80, 95, 95,
            70, 85, 90, 80, 80, 60, 115, 110, 100, 95,
            85, 105, 110, 115 };
        private static readonly double[] Power = { 0,
            20, 29, 28, 19, 26, 31, 19, 31, 27, 21, 29, 31, 18, 26, 28,
            19, 28, 25, 22, 34, 29, 23, 34, 32, 14, 24, 21, 20, 25,
            24, 22, 16, 19, 37, 28, 27, 26, 19, 29, 24, 31 };

        [Serializable] public sealed class Audit
        {
            public int Samples;
            public string Policy;
            public List<Scenario> Scenarios = new();
        }
        [Serializable] public sealed class Scenario
        {
            public string Name;
            public int[] BasePrices;
            public List<Trial> Trials = new();
        }
        [Serializable] public sealed class Trial
        {
            public int Seed, FloorsCleared, FailedFloor, FailedStage, Gold, Recruits, Purchases, Battles;
            public string Outcome, Starters, Team, FailureEnemies;
            public List<string> Trace = new();
        }

        [Test, Explicit("Manual full-campaign economy audit")]
        public void SampleCampaigns()
        {
            var scenes = new[] { "Roadside", "WhisperingGraveyard", "ClockworkCapital" }
                .Select(name => AssetDatabase.LoadAssetAtPath<EncounterAsset>(
                    $"Assets/RaidPartyManager/PrototypeData/{name}Encounter.asset").ToData()).ToArray();
            var priceField = typeof(PlayerMarket).GetField("prices", BindingFlags.NonPublic | BindingFlags.Static);
            var livePrices = (int[])priceField.GetValue(null);
            var original = livePrices.ToArray();
            var previous = new[] { 0, 19, 40, 37, 23, 34, 42, 32, 90, 28, 20, 35, 36, 19, 37, 43, 17, 37, 27, 27, 34, 45, 18, 41, 26, 17, 34, 39, 16, 24, 20, 19, 12, 16, 48, 15, 40, 25, 18, 18, 18, 18 };
            Assert.That(livePrices.Length, Is.EqualTo(Higher.Length));
            int count = int.TryParse(Environment.GetEnvironmentVariable("RAID_AUDIT_SAMPLES"), out int n) ? n : 500;
            preparePact = Environment.GetEnvironmentVariable("RAID_AUDIT_PREPARE_PACT") == "1";
            var audit = new Audit { Samples = count, Policy = "Four-offer starters; health-aware formation; evolve/rest/train; recruit until four; equip/learn/sell surplus; safe event choices; native battle AI; no reloads. RNG for private reward/employee streams seeded by reflection in disposable test only." };
            if (preparePact) audit.Policy += " Reserve one book and an equipment offering before the guaranteed TidePact; buy the cheapest book at the first town if needed.";
            var variants = new[] {
                ("before", previous), ("higher", Higher),
                ("premium", Higher.Select(p => (int)Math.Round(p * 1.2, MidpointRounding.AwayFromZero)).ToArray()),
                ("selected", Selected) };
            string only = Environment.GetEnvironmentVariable("RAID_AUDIT_SCENARIO");
            try
            {
                foreach (var variant in variants)
                {
                    if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(variant.Item1)) continue;
                    // Mutate entries: Mono may inline a readonly array reference after the first call.
                    Array.Copy(variant.Item2, livePrices, livePrices.Length);
                    foreach (var c in scenes[0].CharacterCatalog.Where(c => c.RosterNumber > 0))
                        Assert.That(PlayerMarket.BaseValue(c), Is.EqualTo(variant.Item2[c.RosterNumber]));
                    var scenario = new Scenario { Name = variant.Item1, BasePrices = variant.Item2 };
                    audit.Scenarios.Add(scenario);
                    Assert.That(JsonUtility.ToJson(Play(scenes, 0)), Is.EqualTo(JsonUtility.ToJson(Play(scenes, 0))),
                        "Every RNG stream, including the founders' initial goals, must be reproducible.");
                    for (int seed = 0; seed < count; seed++)
                    {
                        scenario.Trials.Add(Play(scenes, seed));
                        if ((seed + 1) % 50 == 0) Debug.Log($"AUDIT {scenario.Name}: {seed + 1}/{count}; wins {scenario.Trials.Count(t => t.FloorsCleared == 3)}");
                    }
                }
            }
            finally { Array.Copy(original, livePrices, livePrices.Length); }
            string path = Environment.GetEnvironmentVariable("RAID_AUDIT_OUTPUT") ?? "/tmp/raid-campaign-audit.json";
            File.WriteAllText(path, JsonUtility.ToJson(audit, true));
            Assert.That(audit.Scenarios, Is.Not.Empty);
            Assert.That(audit.Scenarios.SelectMany(s => s.Trials).Any(t => t.Outcome == "stalled"), Is.False);
        }

        private static double Strength(CharacterDefinition c) => c.RosterNumber > 0 && c.RosterNumber < Power.Length ? Power[c.RosterNumber] : 0;
        private static bool Healer(CharacterDefinition c) => c.Id is "DawnHealer" or "DivinePriest";
        private static double Fitness(MemberView m) => Strength(m.Character) + m.EffectiveStats.Attack * .45 +
            m.Training.Sessions * 1.5 + m.EquipmentIds.Count(x => x != null) * .7 +
            m.LearnedSkills.Count(x => x != null) - (1 - m.Health / (double)Math.Max(1, m.EffectiveStats.Health)) * 12;

        private static Trial Play(EncounterData[] scenes, int seed)
        {
            var random = new Random(seed);
            var content = scenes[0];
            var pool = content.CharacterCatalog.Where(c => c.RosterNumber > 0 && c.Stage == 1 && !AdventureRun.IsGardenerForm(c.Id)).ToList();
            var warriors = pool.Where(c => c.Profession == Profession.Warrior).ToArray();
            var warrior = warriors[random.Next(warriors.Length)]; pool.Remove(warrior);
            var offers = new List<CharacterDefinition>();
            for (int i = 0; i < 3; i++) { int pick = random.Next(pool.Count); offers.Add(pool[pick]); pool.RemoveAt(pick); }
            offers.Insert(random.Next(4), warrior);
            var pair = (from a in offers from b in offers where a.Id != b.Id
                let tank = a.Profession == Profession.Warrior || b.Profession == Profession.Warrior
                let score = Strength(a) + Strength(b) + (tank ? 8 : 0) + (Healer(a) || Healer(b) ? 8 : 0)
                orderby score descending select new[] { a, b }).First();
            pair = pair.OrderByDescending(Tank).ToArray();
            var gear = content.StartingEquipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
            var bag = gear.Where(e => !e.IsPotion && e.Tier is 1 or 2).OrderBy(_ => random.Next()).Take(2).ToArray();
            var party = new PartyRun(pair.Select((c, i) => new PartyMemberStart("crew-" + (i + 1), c, i + 1, c.BaseStats.Health)), bag, false);
            // These streams have time-based production defaults. Fix only their seeds, preserving their algorithms.
            typeof(PartyRun).GetField("rewardRandom", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(party, new Random(seed ^ 714389));
            typeof(PartyRun).GetField("employeeRandom", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(party, new Random(seed ^ 438721));
            // The public constructor creates initial employee goals before a test can seed its RNG.
            // Recreate those initial goals through the same production method after seeding.
            var initialMembers = ((System.Collections.IEnumerable)typeof(PartyRun)
                .GetField("members", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(party)).Cast<object>().ToArray();
            var refreshGoals = typeof(PartyRun).GetMethod("RefreshEmployeeGoals", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var member in initialMembers)
                member.GetType().GetField("Career").SetValue(member, default(EmployeeCareer));
            foreach (var member in initialMembers) refreshGoals.Invoke(party, new[] { member });
            party.AddGold(100);
            var run = new AdventureRun(party, content.CharacterCatalog, gear.Concat(content.SkillBooks), false, random, true);
            var row = new Trial { Seed = seed, Starters = string.Join("/", pair.Select(c => c.Name)) };
            int guard = 0;
            bool pactSeen = false;
            while (!run.Failed && !run.CampaignWon && guard++ < 40)
            {
                bool reserveBook = preparePact && run.Floor == 1 && !pactSeen;
                Prepare(party, reserveBook);
                int floor = run.Floor, stage = run.Stage;
                var routes = run.RouteOptions;
                int choice = Enumerable.Range(0, routes.Count).OrderByDescending(i => RouteValue(routes[i], run)).First();
                Require(run.EnterRoute(choice), "enter route", seed);
                var visit = run.Current;
                row.Trace.Add($"{floor}-{stage} {visit.Kind}/{visit.Event} gold={party.Gold} members={party.Members.Count}");
                if (visit.Battle != null)
                {
                    var scene = scenes[floor - 1];
                    var enemies = run.PrepareEnemies(scene.EnemyRoster, scene.EnemyFormations);
                    var request = party.BeginBattle(enemies, scene.TickLimit, null, content.RecruitmentPool,
                        content.CharacterCatalog, visit.Kind is NodeKind.EliteBattle or NodeKind.Boss, run.InCapital);
                    run.BindBattle(request.Id);
                    var result = new BattleSimulator(random: new Random(unchecked(seed * 104729 + floor * 7919 + stage * 997))).Simulate(request);
                    row.Battles++;
                    if (result.Outcome != BattleOutcome.Victory)
                    {
                        Require(party.TrySettle(result, out _), "settle loss", seed);
                        Require(run.SettleBattle(result), "settle route loss", seed);
                        row.FailedFloor = floor; row.FailedStage = stage; row.Outcome = result.Outcome.ToString();
                        row.FailureEnemies = string.Join("/", enemies.Select(e => e.Character.Name));
                        break;
                    }
                    Require(party.TryFinishBattle(result, out var loot), "settle party win", seed);
                    Require(run.SettleBattle(result, out int gold), "settle route win", seed);
                    var rewards = run.RecordBattleRewards(gold + result.GoldEarned, loot);
                    rewards.ClaimGold(party);
                    for (int i = 0; i < rewards.Items.Count; i++) rewards.ClaimItem(party, i);
                    if (visit.NeedsEvolution) Evolve(run, false);
                    if (!visit.Resolved && visit.Rewards.Count > 0) TakeReward(run);
                    if (visit.Kind == NodeKind.Boss) row.FloorsCleared = floor;
                }
                else if (visit.Kind == NodeKind.Town) Town(run, row, reserveBook);
                else if (visit.Kind == NodeKind.Camp) Camp(run);
                else if (visit.Kind == NodeKind.TreasureChest) { Require(run.OpenTreasureChest(), "open chest", seed); TakeReward(run); }
                else if (visit.Kind == NodeKind.Event) Event(run);
                if (visit.Event == AdventureEventKind.TidePact) pactSeen = true;
                if (run.CampaignWon) break;
                Require(run.Leave(), "leave", seed);
            }
            row.Outcome ??= run.CampaignWon ? "Victory" : "stalled";
            row.Gold = party.Gold;
            row.Team = string.Join("/", party.Members.OrderBy(m => m.Position ?? 99).Select(m =>
                $"{m.Character.Name}[{m.Position?.ToString() ?? "bench"},HP{m.Health}/{m.EffectiveStats.Health},A{m.EffectiveStats.Attack},D{m.EffectiveStats.Defense},S{m.EffectiveStats.SpecialDefense},train{m.Training.Sessions};gear:{string.Join("+", m.EquipmentIds.Where(x => x != null).Select(id => party.Inventory.FirstOrDefault(i => i.Id == id)?.Definition.Name))};skills:{string.Join("+", m.LearnedSkills.Where(x => x != null).Select(b => b.Name))}]") );
            return row;
        }

        private static void Require(bool value, string step, int seed) { if (!value) throw new Exception($"Audit {step} failed at seed {seed}"); }
        private static double Tank(CharacterDefinition c) => c.BaseStats.Defense * 2 + c.BaseStats.SpecialDefense + c.BaseStats.Health * .25 - (Healer(c) ? 12 : 0);
        private static double RouteValue(NodeKind kind, AdventureRun run) => kind switch
        {
            NodeKind.Town => 100, NodeKind.Camp => 90, NodeKind.TreasureChest => 80,
            NodeKind.Event => 65, NodeKind.NormalBattle => 45, NodeKind.EliteBattle => 20, _ => 0
        };

        private static void Prepare(PartyRun party, bool reserveBook = false)
        {
            var members = party.Members.Where(m => !m.IsAway && m.Health > 0).OrderByDescending(Fitness).ToList();
            var selected = members.Take(4).ToList();
            var healer = members.Where(m => Healer(m.Character)).OrderByDescending(Fitness).FirstOrDefault();
            if (healer != null && !selected.Contains(healer)) { selected.RemoveAt(selected.Count - 1); selected.Add(healer); }
            foreach (var m in members.Where(m => !selected.Contains(m))) party.TryPlace(m.Id, null);
            selected = selected.OrderByDescending(m => Tank(m.Character) + m.Health * .15).ToList();
            // DiSha disables the nearest rear ally's attacks: put the weakest attacker directly behind it.
            if (selected.Count >= 2 && selected[0].Character.Id == "DiSha")
            { var weak = selected.Skip(1).OrderBy(m => Healer(m.Character) ? -1 : Fitness(m)).First(); selected.Remove(weak); selected.Insert(1, weak); }
            for (int i = 0; i < selected.Count; i++) party.TryPlace(selected[i].Id, i + 1);
            // Keep useful gear on its owner. Fill empty slots, then replace weak non-root items with upgrades.
            for (int pass = 0; pass < 2; pass++)
            foreach (var m in party.Members.Where(m => m.Position.HasValue).OrderBy(m => m.Position))
            for (int slot = 0; slot < m.EquipmentIds.Count; slot++)
            {
                string currentId = party.Members.First(x => x.Id == m.Id).EquipmentIds[slot];
                var current = party.Inventory.FirstOrDefault(x => x.Id == currentId);
                if (current?.Definition.Effect == EquipmentEffect.TreeRoot) continue;
                var best = party.BackpackItems.Where(x => x.Definition is not SkillBookDefinition)
                    .OrderByDescending(x => GearValue(x.Definition, m)).FirstOrDefault();
                if (best != null && GearValue(best.Definition, m) > (current == null ? 0 : GearValue(current.Definition, m)) + .1)
                    party.TryEquip(m.Id, slot, best.Id);
            }
            foreach (var m in party.Members.Where(m => m.Position.HasValue))
            for (int slot = 0; slot < m.SkillSlotCount; slot++)
            {
                if (reserveBook && party.SkillBooks.Count <= 1) continue;
                if (slot < m.LearnedSkills.Count && m.LearnedSkills[slot] != null) continue;
                var best = party.SkillBooks.Where(b => party.CanLearnSkill(m.Id, slot, b.Id))
                    .OrderByDescending(b => BookValue((SkillBookDefinition)b.Definition, m)).FirstOrDefault();
                if (best != null && BookValue((SkillBookDefinition)best.Definition, m) > 0) party.TryLearnSkill(m.Id, slot, best.Id);
            }
        }

        private static double GearValue(EquipmentDefinition e, MemberView m)
        {
            double value = e.Bonus.Attack * 3 + e.Bonus.Defense * 2 + e.Bonus.SpecialDefense * 1.5 + e.Bonus.Health * .8;
            value += e.Effect switch
            {
                EquipmentEffect.HealingRing => 13, EquipmentEffect.ElvenCharm => 15, EquipmentEffect.LifePendant => 12,
                EquipmentEffect.ArcaneCrystal => m.Character.Profession == Profession.Mage ? 20 : 1,
                EquipmentEffect.MoneyTreeBranch => 17, EquipmentEffect.RecruitmentNotice => 16,
                EquipmentEffect.GreedyHand => 15, EquipmentEffect.IceRing => 16, EquipmentEffect.LightningRing => 13,
                EquipmentEffect.FireRing => 12, EquipmentEffect.WindGlove => 14, EquipmentEffect.ExecutionMask => 14,
                EquipmentEffect.StrangeSeed => 14, EquipmentEffect.ColdSoulReliquary => 18,
                EquipmentEffect.FacelessCrown => 18, EquipmentEffect.SunkenFrostChain => 18, EquipmentEffect.HarborWhistle => 18,
                EquipmentEffect.EchoChalice => 8, EquipmentEffect.ImperialSeal => 12,
                EquipmentEffect.GuardianBanner => m.Position == 1 ? 13 : 1,
                EquipmentEffect.ScarletRose => Healer(m.Character) || m.Position != 1 ? 9 : -5,
                EquipmentEffect.RitualMask => 10, EquipmentEffect.ZuixianGourd => 11,
                _ => e.IsPotion ? 5 : e.Effect == EquipmentEffect.TreeRoot ? 2 : e.Effect == EquipmentEffect.RedCord ? 2 : 6
            };
            return value;
        }

        private static double BookValue(SkillBookDefinition book, MemberView m)
        {
            if (Healer(m.Character) && book.SkillEffect is not (SkillEffect.HealingHands or SkillEffect.LongNightPrayer or SkillEffect.FieldMedic)) return 0;
            double value = book.SkillEffect switch
            {
                SkillEffect.HealingHands => 15, SkillEffect.LongNightPrayer => 18, SkillEffect.FieldMedic => 12,
                SkillEffect.ColdTide => 14, SkillEffect.DeepwaterSurge => 17, SkillEffect.BlockadeSalvo => 16,
                SkillEffect.CloudPiercer => 10, SkillEffect.FleshFeast => 12, SkillEffect.VenomSurge => 8,
                _ => 10
            };
            return book.Cost <= 2 + m.BonusEnergy ? value : book.Cost <= 4 ? value * .4 : 0;
        }

        private static void Town(AdventureRun run, Trial row, bool reserveBook)
        {
            var p = run.Party;
            run.ClaimCommission();
            if (!run.QuestActive) run.AcceptCommission(run.Current.Commissions.OrderBy(j => j.Difficulty).First().Id);
            Prepare(p, reserveBook);
            string offeringBook = reserveBook ? p.SkillBooks.FirstOrDefault()?.Id : null;
            // Sell genuinely spare gear/books once. No buying for resale or cycling character sales.
            var surplus = p.SaleableItems.Where(item => item.Id != offeringBook && item.Definition.Effect != EquipmentEffect.StrangeSeed &&
                (item.Definition is SkillBookDefinition book ? !p.Members.Any(m =>
                    book.CanLearn(m.Character) && m.LearnedSkills.Count(x => x != null) < m.SkillSlotCount) :
                    p.Members.All(m => m.EquipmentIds.All(x => x != null)) || p.Inventory.Count > 4 * 3))
                .Select(i => i.Id).ToArray();
            if (surplus.Length > 0) p.TrySellItems(surplus, out _);
            if (reserveBook && p.SkillBooks.Count == 0)
            {
                var bookDeal = run.Current.Stock.Select((offer, index) => (offer, index)).Where(x =>
                    !x.offer.Sold && x.offer.Item is SkillBookDefinition && x.offer.Price <= p.Gold)
                    .OrderBy(x => x.offer.Price).FirstOrDefault();
                if (bookDeal.offer != null && run.Buy(bookDeal.index)) row.Purchases++;
            }
            while (p.Members.Count < 4)
            {
                bool needsHealer = !p.Members.Any(m => Healer(m.Character));
                var candidate = run.Current.Recruits.Where(c => run.TownRecruitPrice(c) <= p.Gold)
                    .OrderByDescending(c => Strength(c) + (needsHealer && Healer(c) ? 13 : 0) - run.TownRecruitPrice(c) * .025).FirstOrDefault();
                if (candidate == null || !run.ChooseTownRecruit(candidate.Id)) break;
                row.Recruits++; Prepare(p, reserveBook);
            }
            for (int buys = 0; buys < 4; buys++)
            {
                var deals = run.Current.Stock.Select((o, i) => (o, i)).Where(x => !x.o.Sold && x.o.Price <= p.Gold).ToArray();
                if (deals.Length == 0) break;
                var best = deals.OrderByDescending(x => ShopValue(x.o.Item, p) / Math.Max(1, x.o.Price)).First();
                if (ShopValue(best.o.Item, p) < 7 || p.Members.Count < 4 && p.Gold - best.o.Price < 60) break;
                if (!run.Buy(best.i)) break;
                row.Purchases++; Prepare(p, reserveBook);
            }
            Camp(run); Prepare(p, reserveBook);
        }

        private static double ShopValue(EquipmentDefinition e, PartyRun p) => p.Members.Where(m => m.Position.HasValue).Select(m =>
            e is SkillBookDefinition b ? !b.CanLearn(m.Character) || m.LearnedSkills.Count(x => x != null) >= m.SkillSlotCount ? 0 : BookValue(b, m) :
            GearValue(e, m) - m.EquipmentIds.Select(id => p.Inventory.FirstOrDefault(i => i.Id == id)).Select(item => item == null ? 0 : GearValue(item.Definition, m)).DefaultIfEmpty(0).Min()).DefaultIfEmpty(0).Max();

        private static bool Evolve(AdventureRun run, bool camp)
        {
            var option = run.EvolutionCandidates.Where(m => m.Position.HasValue)
                .SelectMany(m => run.EvolutionForms(m.Id).Select(c => (m, c,
                    gain: Strength(c) - Strength(m.Character) + (Healer(c) ? 3 : 0))))
                .OrderByDescending(x => x.gain).FirstOrDefault();
            return option.c != null && (camp ? run.EvolveAtCamp(option.m.Id, option.c.Id) : run.Evolve(option.m.Id, option.c.Id));
        }

        private static void Camp(AdventureRun run)
        {
            if (!run.CanUseCamp) return;
            var deployed = run.Party.Members.Where(m => m.Position.HasValue).ToArray();
            double hp = deployed.Sum(m => m.Health) / (double)Math.Max(1, deployed.Sum(m => m.EffectiveStats.Health));
            if (hp < .68 || deployed.Any(m => m.Health < m.EffectiveStats.Health * .3 && m.Character.Id != "AbyssKeeper")) run.Rest();
            else if (!Evolve(run, true))
            {
                var trainee = run.TrainingCandidates.Where(m => m.Position.HasValue).OrderByDescending(Fitness).FirstOrDefault();
                if (trainee != null) run.TrainAtCamp(trainee.Id); else run.Rest();
            }
        }

        private static void TakeReward(AdventureRun run)
        {
            int best = Enumerable.Range(0, run.Current.Rewards.Count).OrderByDescending(i =>
                ShopValue(run.Current.Rewards[i], run.Party) + (run.Current.Rewards[i].BossExclusive ? 3 : 0)).First();
            Require(run.ClaimReward(best), "claim reward", -1);
        }

        private static void Event(AdventureRun run)
        {
            if (run.Current.Event == AdventureEventKind.FoundCoins) run.CollectCoins();
            else if (run.Current.Event == AdventureEventKind.LostTraveler)
            {
                var profession = !run.Party.Members.Any(m => Healer(m.Character)) && run.Current.Professions.Contains(Profession.Support)
                    ? Profession.Support : run.Current.Professions.First();
                run.ChooseProfession(profession);
            }
            else if (run.Current.Event == AdventureEventKind.TidePact && run.Party.Members.Count < PartyRun.RosterLimit)
            {
                var book = run.Party.SkillBooks.FirstOrDefault();
                var gear = run.TideOfferings.Where(i => i.Definition is not SkillBookDefinition).FirstOrDefault();
                if (gear == null)
                {
                    foreach (var m in run.Party.Members)
                    {
                        int index = m.EquipmentIds.ToList().FindIndex(id => id != null &&
                            run.Party.Inventory.Any(i => i.Id == id && !i.Definition.IsPotion && i.Definition.Effect is not (EquipmentEffect.TreeRoot or EquipmentEffect.RecruitmentNotice)));
                        if (index >= 0) { run.Party.TryEquip(m.Id, index, null); break; }
                    }
                    gear = run.TideOfferings.Where(i => i.Definition is not SkillBookDefinition).FirstOrDefault();
                }
                if (book != null && gear != null) run.OfferTidePact(gear.Id, book.Id);
            }
            else if (run.Current.Event == AdventureEventKind.GraveSeed) run.TakeGraveSeedReward(run.Party.Members.Count < PartyRun.RosterLimit);
            else if (CapitalStories.Contains(run.Current.Event))
            {
                var story = CapitalStories.For(run.Current.Event.Value);
                var choices = story.Choices.Select((c, i) => (c, i)).Where(x => run.CanTakeCapitalChoice(x.c) && x.c.Chance == 100).ToArray();
                if (choices.Length > 0) run.ChooseCapital(choices.OrderBy(x => x.c.Cost).First().i);
            }
            if (!run.Current.Resolved) run.DeclineEvent();
        }
    }
}
