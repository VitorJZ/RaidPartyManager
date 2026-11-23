using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public enum NodeKind { Event, Camp, Shop, Town, NormalBattle, EliteBattle, Boss, TreasureChest }
    public enum AdventureEventKind { Investor, Ambush, LostTraveler, FoundCoins, HatTrick, Forge, LockedChest, AscensionTrial, Alchemist, Obituary, Pallbearer, ClaimDesk, Auction, Sponsorship, HoundWalk, DeadLetters, WeepingWall, Ravens, BellRope, SoulWard, SoupQueue, WhisperWall, Epitaph, BorrowedName, FareGate, DeadMail, TimeOffice, WaterCharter, LostFreight, ClinicQueue, WrongSwitch, StreetCurrent, ElectionPress, BoilerUnion, SilentOrchestra, SeedArchive, SpareHands, GoldStandard, LastMeridian, TidePact }
    public enum TownService { Recruit, Shop, Camp, Quest }

    public sealed class RouteNode
    {
        public int Number { get; }
        public NodeKind Kind { get; internal set; }
        public bool IsFork { get; internal set; }
        public NodeKind? Alternative { get; internal set; }
        internal RouteNode(int number, NodeKind kind) { Number = number; Kind = kind; }
    }

    public sealed class DockRoute
    {
        public const int Stages = 10;
        public IReadOnlyList<RouteNode> Nodes { get; }

        public DockRoute(Random random)
        {
            var nodes = Enumerable.Range(1, Stages).Select(n => new RouteNode(n,
                n == Stages ? NodeKind.Boss : NodeKind.NormalBattle)).ToArray();
            int chest = random.Next(3, 9);
            nodes[chest - 1].Kind = NodeKind.TreasureChest;

            int earlyCamp = Pick(4, 6, chest);
            int lateCamp = Pick(7, 9, chest);
            nodes[earlyCamp - 1].Kind = nodes[lateCamp - 1].Kind = NodeKind.Camp;
            int elite = Pick(5, 9, chest, earlyCamp, lateCamp);
            nodes[elite - 1].Kind = NodeKind.EliteBattle;
            int town = Pick(2, 9, chest, earlyCamp, lateCamp, elite);
            nodes[town - 1].Kind = NodeKind.Town;
            int secondNormal = Pick(3, 9, chest, earlyCamp, lateCamp, elite, town);

            foreach (var node in nodes.Where(node => node.Number > 1 && node.Number < Stages &&
                node.Number != secondNormal && node.Number != elite && node.Number != chest))
            {
                node.IsFork = true;
                node.Alternative = node.Kind == NodeKind.NormalBattle
                    ? (node.Number >= 5 && random.NextDouble() < 0.5 ? NodeKind.EliteBattle : NodeKind.Event)
                    : (node.Number <= 3 || random.NextDouble() < 0.5 ? NodeKind.Event : NodeKind.NormalBattle);
            }
            Nodes = Array.AsReadOnly(nodes);

            int Pick(int first, int last, params int[] exclude)
            {
                var candidates = Enumerable.Range(first, last - first + 1).Except(exclude).ToArray();
                return candidates[random.Next(candidates.Length)];
            }
        }

        internal DockRoute(AdventureCheckpoint saved)
        {
            Nodes = Array.AsReadOnly(saved.Nodes.Select((node, index) =>
                new RouteNode(index + 1, saved.RestoreKind(index + 1, node.Kind))
                {
                    IsFork = node.Fork,
                    Alternative = node.AlternativePlusOne == 0 ? (NodeKind?)null :
                        saved.RestoreKind(index + 1, (NodeKind)(node.AlternativePlusOne - 1))
                }).ToArray());
        }

        public IReadOnlyList<NodeKind> Options(int number, int eventsTaken, int elitesTaken, Random random)
        {
            var node = Nodes[number - 1];
            if (!node.IsFork)
                return new[] { node.Kind };
            if (node.Alternative is NodeKind alternative)
                return new[] { node.Kind, alternative };

            // Older saves keep their generated floor until the next floor.
            NodeKind other = eventsTaken == 0 && (number < 5 || elitesTaken >= 3 || random.NextDouble() < 0.5)
                ? NodeKind.Event : NodeKind.EliteBattle;
            return new[] { NodeKind.NormalBattle, other };
        }
    }

    public readonly struct RouteStop
    {
        public int Number { get; }
        public int Lane { get; }
        public NodeKind? Kind { get; }

        internal RouteStop(int number, int lane, NodeKind kind)
        {
            Number = number;
            Lane = lane;
            Kind = kind;
        }
    }

    public sealed class AdventureShopOffer
    {
        public EquipmentDefinition Item { get; }
        public int Price { get; }
        public bool Sold { get; internal set; }
        internal AdventureShopOffer(EquipmentDefinition item, int price) { Item = item; Price = price; }

        public static int BasePrice(EquipmentDefinition item) => item.Price;

        public static int ResalePrice(EquipmentDefinition item, int recoveryPercent = 50) =>
            item.BossExclusive ? 100 : Math.Max(1, (int)((long)BasePrice(item) * recoveryPercent / 100));

        public static IReadOnlyList<AdventureShopOffer> Draw(IEnumerable<EquipmentDefinition> catalogue, Random random, bool discount)
        {
            var stock = catalogue.Where(e => e.AvailableInRegularPool && BasePrice(e) > 0).GroupBy(e => e.Id).Select(g => g.First()).ToArray();
            var choices = stock.Where(e => e.Tier == 1).OrderBy(_ => random.Next()).Take(2)
                .Concat(stock.Where(e => e.Tier is 2 or 3).OrderBy(_ => random.Next()).Take(2)).OrderBy(_ => random.Next());
            return choices.Select(e => new AdventureShopOffer(e, (int)Math.Ceiling(
                Math.Ceiling(BasePrice(e) * (.7 + random.NextDouble() * .6)) * (discount ? .5 : 1)))).ToArray();
        }
    }

    public sealed class AwayCompanion
    {
        public string MemberId { get; }
        public int Position { get; }
        public int ReturnAt { get; }
        internal AwayCompanion(string id, int position, int at) { MemberId = id; Position = position; ReturnAt = at; }
    }

    public sealed class BattleRewards
    {
        private readonly EquipmentDefinition[] items;
        public IReadOnlyList<EquipmentDefinition> Items { get; }
        public int Gold { get; private set; }
        public bool HasUnclaimed => Gold > 0 || items.Any(item => item != null);

        public BattleRewards(int gold, IEnumerable<EquipmentDefinition> items)
        {
            Gold = gold;
            this.items = items.ToArray();
            Items = Array.AsReadOnly(this.items);
        }

        public bool ClaimGold(PartyRun party)
        {
            if (Gold <= 0) return false;
            party.AddGold(Gold);
            Gold = 0;
            return true;
        }

        public bool ClaimItem(PartyRun party, int index)
        {
            if (index < 0 || index >= items.Length || items[index] == null) return false;
            party.AddEquipment(items[index]);
            items[index] = null;
            return true;
        }
    }

    public sealed class AdventureBattle
    {
        public NodeKind Kind { get; }
        public string RequestId { get; internal set; }
        public bool Settled { get; internal set; }
        public BattleRewards Reward { get; internal set; }
        internal EquipmentDefinition BonusEquipment { get; set; }
        public IReadOnlyList<Combatant> Enemies { get; internal set; }
        public HashSet<string> EliteIds { get; } = new HashSet<string>();
        internal AdventureBattle(NodeKind kind) { Kind = kind; }
    }

    public sealed class AdventureVisit
    {
        public NodeKind Kind { get; }
        public AdventureEventKind? Event { get; }
        public bool Resolved { get; internal set; }
        public bool Rested { get; internal set; }
        public bool TreasureOpened { get; internal set; }
        public string Message { get; internal set; } = "";
        public IReadOnlyList<EquipmentDefinition> Rewards { get; internal set; } = Array.Empty<EquipmentDefinition>();
        public IReadOnlyList<EquipmentDefinition> Potions { get; internal set; } = Array.Empty<EquipmentDefinition>();
        public IReadOnlyList<Profession> Professions { get; internal set; } = Array.Empty<Profession>();
        public IReadOnlyList<CharacterDefinition> Recruits { get; internal set; } = Array.Empty<CharacterDefinition>();
        public CharacterDefinition PendingRecruit { get; internal set; }
        public EquipmentDefinition TrialGift { get; internal set; }
        public bool NeedsEvolution { get; internal set; }
        public string EvolutionMemberId { get; internal set; }
        public int BrokerRefusals { get; internal set; }
        public int BrokerCounter { get; internal set; }
        public int BrokerLastAsk { get; internal set; }
        public string BrokerMember { get; internal set; }
        public HashSet<TownService> UsedServices { get; } = new HashSet<TownService>();
        public IReadOnlyList<TownCommission> Commissions { get; internal set; } = Array.Empty<TownCommission>();
        public IReadOnlyList<AdventureShopOffer> Stock { get; internal set; } = Array.Empty<AdventureShopOffer>();
        public ShopRecruitment ShopRecruitment { get; internal set; }
        public IReadOnlyList<EquipmentDefinition> HatRewards { get; internal set; } = Array.Empty<EquipmentDefinition>();
        public IReadOnlyList<(int First, int Second)> HatSwaps { get; internal set; } = Array.Empty<(int, int)>();
        public int[] HatPositions { get; } = new[] { 0, 1, 2 };
        public int HatStep { get; internal set; }
        public int PickedHat { get; internal set; } = -1;
        public bool HatWon { get; internal set; }
        public bool InvestorAvailable { get; internal set; }
        public AdventureBattle Battle { get; internal set; }
        internal AdventureVisit(NodeKind kind, AdventureEventKind? eventKind) { Kind = kind; Event = eventKind; }
    }

    public sealed partial class AdventureRun
    {
        private Random random;
        public const string TideSingerId = "DeepSeaWhisper";
        private readonly CharacterDefinition tideSinger;
        private readonly CharacterDefinition[] characters;
        private readonly EquipmentDefinition[] equipment;
        private readonly EquipmentDefinition[] shopCatalogue;
        private readonly EquipmentDefinition[] battleRewardTickets;
        private readonly EquipmentDefinition[] bossCatalogue;
        private readonly EquipmentDefinition[] treasureLoot;
        private readonly HashSet<int> investorFloors = new HashSet<int>();
        private readonly HashSet<AdventureEventKind> seenEvents = new();
        private readonly List<AwayCompanion> away = new List<AwayCompanion>();
        private int eventsTaken, elitesTaken;
        private IReadOnlyList<NodeKind> routeOptions;
        private AdventureCheckpoint nodeCheckpoint;
        private int shopRecruitPrice = ShopRecruitment.StartingPrice;

        public PartyRun Party { get; }
        public bool IsInspection { get; }
        public int Floor { get; private set; } = 1;
        public bool InGraveyard => (Floor - 1) % 3 == 1;
        public bool InCapital => (Floor - 1) % 3 == 2;
        public string RegionName => InCapital ? "机巧遗都" : InGraveyard ? "低语墓园" : "弥雾码头";
        public bool CrossingChapter => !IsInspection && Floor <= 2 && Stage == DockRoute.Stages &&
            !Failed && Current is { Kind: NodeKind.Boss, Resolved: true, Battle: { Settled: true } };
        public bool CampaignWon => !IsInspection && InCapital && !Failed && Stage == DockRoute.Stages &&
            (Current == null ? FloorCleared :
                Current is { Kind: NodeKind.Boss, Resolved: true, Battle: { Settled: true } } && !HasBattleRewardScreen);
        public int Stage { get; private set; } = 1;
        public int CompletedNodes { get; private set; }
        public int LastClosedEventDay { get; private set; } = -1;
        public int Investment { get; private set; }
        public bool InvestmentPaid { get; private set; }
        public bool InvestorVisitedThisFloor => investorFloors.Contains(Floor);
        public bool FloorCleared { get; private set; }
        public bool Failed { get; private set; }
        public DockRoute Route { get; private set; }
        public RouteStop RouteCurrent { get; private set; }
        public RouteStop? RoutePrevious { get; private set; }
        public RouteStop? RouteDestination { get; private set; }
        public int? SuspendedChoice { get; private set; }
        public AdventureVisit Current { get; private set; }
        public bool HasBattleRewardScreen => Current?.Battle?.Reward is { } reward &&
            (reward.HasUnclaimed || !Current.Resolved && Current.Rewards.Count > 0);
        public IReadOnlyList<AwayCompanion> Away => away.ToArray();
        public string ReturnMessage { get; private set; } = "";
        public TownCommission Commission { get; private set; }
        public bool QuestActive => Commission != null;
        public int QuestProgress { get; private set; }
        private int commissionGold;

        public AdventureRun(PartyRun party, IEnumerable<CharacterDefinition> characters,
            IEnumerable<EquipmentDefinition> equipment, bool inspection, Random random = null, bool foundingGrant = false)
        {
            Party = party ?? throw new ArgumentNullException(nameof(party));
            var roster = characters.Where(c => c.RosterNumber > 0).ToArray();
            tideSinger = roster.FirstOrDefault(c => c.Id == TideSingerId);
            this.characters = roster.Where(c => c.Id != TideSingerId).ToArray();
            var items = equipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
            bossCatalogue = items;
            items = items.Where(item => item.AvailableInRegularPool).ToArray();
            shopCatalogue = items;
            this.equipment = items.Where(e => e is not SkillBookDefinition && e.Tier >= 0 && e.Price > 0).ToArray();
            battleRewardTickets = items.Where(item => item.CanDropInBattle)
                .SelectMany(item => Enumerable.Repeat(item, item is SkillBookDefinition { Cost: > 2 } ? 1 : 2)).ToArray();
            treasureLoot = items.Where(e => !e.IsPotion && e.Tier is >= 1 and <= 3).ToArray();
            Party.RegisterRewardCatalog(this.equipment);
            this.random = random ?? new Random();
            IsInspection = inspection;
            Route = new DockRoute(this.random);
            Party.StartContracts(this.random.Next());
            if (foundingGrant) Party.FundFoundingContracts();
        }

        public AdventureCheckpoint CheckpointForExit()
        {
            if (IsInspection)
                throw new InvalidOperationException("An inspection cannot be saved.");

            if (Current?.Battle?.Reward is { } reward && (HasBattleRewardScreen || !Current.Resolved))
            {
                var saved = CaptureCheckpoint(random.Next(), nodeCheckpoint.Choice);
                saved.HasBattleReward = true;
                saved.BattleReward = new BattleRewardCheckpoint
                {
                    Kind = Current.Battle.Kind,
                    Event = Current.Event.HasValue ? (int)Current.Event.Value : -1,
                    Resolved = Current.Resolved, Message = Current.Message,
                    NeedsEvolution = Current.NeedsEvolution, EvolutionMember = Current.EvolutionMemberId,
                    Gold = reward.Gold, Items = reward.Items.Select(item => item?.Id).ToArray(),
                    Choices = Current.Resolved ? Array.Empty<string>() : Current.Rewards.Select(item => item.Id).ToArray()
                };
                return saved;
            }

            if (Current is { Resolved: false, ShopRecruitment: { } recruitment })
            {
                var saved = CaptureCheckpoint(random.Next(), nodeCheckpoint.Choice);
                saved.HasShopVisit = true;
                saved.ShopVisit = new ShopVisitCheckpoint
                {
                    Commissions = Current.Commissions.Select(job => job.Id).ToArray(),
                    Signed = recruitment.Signed, Slots = recruitment.Slots.Select(slot => slot?.Id).ToArray(),
                    Candidates = recruitment.Candidates.Select(candidate => candidate.Id).ToArray(),
                    Stock = Current.Stock.Select(offer => new ShopOfferCheckpoint
                        { Item = offer.Item.Id, Price = offer.Price, Sold = offer.Sold }).ToArray(),
                    UsedServices = Current.UsedServices.ToArray(),
                    TownRecruits = Current.Recruits.Select(candidate => candidate.Id).ToArray(),
                    BrokerRefusals = Current.BrokerRefusals, BrokerCounter = Current.BrokerCounter, BrokerMember = Current.BrokerMember,
                    BrokerLastAsk = Current.BrokerLastAsk,
                    PendingRecruit = Current.PendingRecruit?.Id, Rested = Current.Rested, Message = Current.Message
                };
                return saved;
            }

            if (Current is { Resolved: false })
                return nodeCheckpoint;

            return CaptureCheckpoint(SuspendedChoice.HasValue ? nodeCheckpoint.Seed : random.Next(),
                SuspendedChoice ?? (Current != null ? nodeCheckpoint.Choice : -1), Current?.Resolved == true);
        }

        private AdventureCheckpoint CaptureCheckpoint(int seed, int choice, bool finishVisit = false) =>
            new AdventureCheckpoint
            {
                Party = Party.CaptureCheckpoint(), Seed = seed, Floor = Floor, Stage = Stage, CompletedNodes = CompletedNodes,
                EventsTaken = eventsTaken, ElitesTaken = elitesTaken, Investment = Investment, InvestmentPaid = InvestmentPaid,
                InvestorFloors = investorFloors.ToArray(), SeenEvents = seenEvents.OrderBy(kind => kind).ToArray(),
                FloorCleared = FloorCleared, Failed = Failed,
                QuestActive = QuestActive, CommissionId = Commission?.Id, QuestProgress = QuestProgress, CommissionGold = commissionGold, ReturnMessage = ReturnMessage,
                ShopRecruitPricePlusOne = shopRecruitPrice + 1,
                Nodes = Route.Nodes.Select(node => new RouteCheckpointNode
                {
                    Kind = node.Kind, Fork = node.IsFork, Replaces = -1,
                    AlternativePlusOne = node.Alternative.HasValue ? (int)node.Alternative.Value + 1 : 0
                }).ToArray(),
                Options = (LeavingForPact ? PactStop : routeOptions)?.ToArray(), FollowingOptions = followingOptions?.ToArray(), Current = new CheckpointStop(RouteCurrent),
                Previous = new CheckpointStop(RoutePrevious.GetValueOrDefault()), HasPrevious = RoutePrevious.HasValue,
                Choice = choice, FinishVisit = finishVisit,
                Away = away.Select(companion => new CheckpointCompanion
                {
                    MemberId = companion.MemberId, Position = companion.Position, ReturnAt = companion.ReturnAt
                }).ToArray()
            };

        public static AdventureRun RestoreCheckpoint(AdventureCheckpoint saved, IEnumerable<CharacterDefinition> characters,
            IEnumerable<EquipmentDefinition> equipment)
        {
            if (saved == null || saved.Version != AdventureCheckpoint.CurrentVersion || saved.Nodes?.Length != DockRoute.Stages ||
                saved.Stage is < 1 or > DockRoute.Stages || saved.Floor < 1 || saved.CompletedNodes < 0 ||
                (saved.ShopRecruitPricePlusOne != 0 &&
                    (saved.ShopRecruitPricePlusOne is < 1 or > 96 || (saved.ShopRecruitPricePlusOne - 1) % 5 != 0)) ||
                saved.Choice < -1 || (saved.Choice >= 0 && (saved.Options == null || saved.Choice >= saved.Options.Length)) ||
                ((saved.FinishVisit || saved.HasBattleReward || saved.HasShopVisit) && saved.Choice < 0))
                throw new ArgumentException("Invalid adventure checkpoint.");

            var catalog = characters.ToArray();
            var items = equipment.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
            var party = PartyRun.RestoreCheckpoint(saved.Party, catalog.ToDictionary(character => character.Id), items);
            bool insideNode = saved.HasShopVisit || saved.HasBattleReward || saved.FinishVisit;
            party.StartContracts(saved.Seed, saved.CompletedNodes + (insideNode ? 2 : 1));
            var run = new AdventureRun(party, catalog, items.Values, false, new Random(saved.Seed))
            {
                Floor = saved.Floor, Stage = saved.Stage, CompletedNodes = saved.CompletedNodes, eventsTaken = saved.EventsTaken,
                elitesTaken = saved.ElitesTaken, Investment = saved.Investment, InvestmentPaid = saved.InvestmentPaid,
                FloorCleared = saved.FloorCleared, Failed = saved.Failed,
                Commission = saved.QuestActive ? TownCommission.Find(string.IsNullOrEmpty(saved.CommissionId) ? "patrol" : saved.CommissionId) : null,
                QuestProgress = string.IsNullOrEmpty(saved.CommissionId) ? saved.QuestWins : saved.QuestProgress,
                commissionGold = saved.CommissionGold,
                ReturnMessage = saved.ReturnMessage ?? "", Route = new DockRoute(saved),
                RouteCurrent = saved.Current.Restore(saved), RoutePrevious = saved.HasPrevious ? saved.Previous.Restore(saved) : (RouteStop?)null,
                followingOptions = saved.RestoreOptions(saved.Stage + 1, saved.FollowingOptions),
                routeOptions = saved.RestoreOptions(saved.Stage, saved.Options),
                shopRecruitPrice = saved.ShopRecruitPricePlusOne == 0 ? ShopRecruitment.StartingPrice : saved.ShopRecruitPricePlusOne - 1,
                random = new Random(saved.Seed)
            };
            run.investorFloors.UnionWith(saved.InvestorFloors ?? Array.Empty<int>());
            run.seenEvents.UnionWith(saved.SeenEvents ?? Array.Empty<AdventureEventKind>());
            // Earlier saves recorded investor visits separately.
            if (run.investorFloors.Count > 0)
                run.RecordEvent(AdventureEventKind.Investor);
            run.away.AddRange((saved.Away ?? Array.Empty<CheckpointCompanion>()).Select(companion =>
                new AwayCompanion(companion.MemberId, companion.Position, companion.ReturnAt)));
            if (saved.HasBattleReward)
            {
                var reward = saved.BattleReward;
                if (reward == null || reward.Gold < 0 || reward.Gold > int.MaxValue - party.Gold ||
                    reward.Items == null || reward.Choices == null)
                    throw new ArgumentException("Invalid battle rewards.");
                run.RouteDestination = run.RouteChoice(saved.Choice);
                run.nodeCheckpoint = saved;
                var eventKind = reward.Event < 0 ? (AdventureEventKind?)null : (AdventureEventKind)reward.Event;
                run.RecordEvent(eventKind);
                run.Current = new AdventureVisit(run.RouteDestination.Value.Kind.Value, eventKind)
                {
                    Resolved = reward.Resolved, Message = reward.Message ?? "", NeedsEvolution = reward.NeedsEvolution,
                    EvolutionMemberId = string.IsNullOrEmpty(reward.EvolutionMember) ? null : reward.EvolutionMember,
                    Rewards = reward.Choices.Select(id => items[id]).ToArray(),
                    Battle = new AdventureBattle(reward.Kind)
                    {
                        Settled = true,
                        Reward = new BattleRewards(reward.Gold,
                            reward.Items.Select(id => string.IsNullOrEmpty(id) ? null : items[id]))
                    }
                };
            }
            else if (saved.HasShopVisit)
            {
                var shop = saved.ShopVisit;
                var destination = run.RouteChoice(saved.Choice);
                if (destination.Kind != NodeKind.Town || shop == null || shop.Candidates == null || shop.Slots == null ||
                    shop.Stock == null || shop.UsedServices == null || shop.TownRecruits == null ||
                    shop.Stock.Any(offer => string.IsNullOrEmpty(offer.Item) || offer.Price < 0 ||
                        !items.TryGetValue(offer.Item, out var item) || !item.AvailableInRegularPool || item.Price <= 0))
                    throw new ArgumentException("Invalid shop visit.");

                var byId = catalog.ToDictionary(character => character.Id);
                var recruitment = ShopRecruitment.Restore(party, byId, shop.Slots, shop.Candidates, shop.Signed, run.shopRecruitPrice);
                run.RouteDestination = destination;
                run.nodeCheckpoint = saved;
                run.Current = new AdventureVisit(destination.Kind.Value, null)
                {
                    BrokerRefusals = Math.Clamp(shop.BrokerRefusals, 0, BrokerPatience),
                    BrokerCounter = Math.Max(0, shop.BrokerCounter), BrokerMember = shop.BrokerMember,
                    BrokerLastAsk = Math.Max(0, shop.BrokerLastAsk),
                    Commissions = TownCommission.RestoreBoard(shop.Commissions, run.random),
                    ShopRecruitment = recruitment, Rested = shop.Rested, Message = shop.Message ?? "",
                    Stock = shop.Stock.Select(offer => new AdventureShopOffer(items[offer.Item],
                        offer.Price == 0 ? items[offer.Item].Price : offer.Price)
                        { Sold = offer.Sold }).ToArray(),
                    Recruits = shop.TownRecruits.Select(id => byId[id]).Where(c => party.Members.All(m => m.Character.Id != c.Id)).ToArray(),
                    PendingRecruit = string.IsNullOrEmpty(shop.PendingRecruit) ? null : byId[shop.PendingRecruit]
                };
                // A standalone shop checkpoint has stock but never had a talent market.
                if (saved.Options[saved.Choice] == NodeKind.Shop)
                    run.Current.Recruits = run.DrawTownRecruits();
                else if (saved.Party.Day == 0 && run.Current.Recruits.Count < 8)
                {
                    var existing = run.Current.Recruits.Select(c => c.Id).Concat(party.Members.Select(m => m.Character.Id)).ToHashSet();
                    var additions = run.characters.Where(c => !existing.Contains(c.Id)).OrderBy(_ => run.random.Next());
                    run.Current.Recruits = run.Current.Recruits.Concat(additions).Take(8).ToArray();
                }
                run.Current.UsedServices.UnionWith(shop.UsedServices);
            }
            else if (saved.FinishVisit)
            {
                run.RouteDestination = run.RouteChoice(saved.Choice);
                run.AdvanceCompletedVisit();
            }
            else if (saved.Choice >= 0)
            {
                run.SuspendedChoice = saved.Choice;
                run.nodeCheckpoint = saved;
            }
            // Previously a save could be parked on the cleared dock waiting for a next-floor sign.
            run.EnterClearedChapter();
            return run;
        }

        private bool PactAhead => tideSinger != null && !InGraveyard && !InCapital &&
            !seenEvents.Contains(AdventureEventKind.TidePact);
        private bool LeavingForPact => PactAhead && RouteCurrent.Kind == NodeKind.Town && RouteCurrent.Number == Stage;
        private static readonly IReadOnlyList<NodeKind> PactStop = Array.AsReadOnly(new[] { NodeKind.Event });

        private IReadOnlyList<NodeKind> OptionsAt(int stage) =>
            PactAhead && Route.Nodes[stage - 1].Kind == NodeKind.Town
                ? new[] { NodeKind.Town } : Route.Options(stage, eventsTaken, elitesTaken, random);

        public IReadOnlyList<NodeKind> RouteOptions => routeOptions ??= LeavingForPact ? PactStop : OptionsAt(Stage);

        private IReadOnlyList<NodeKind> followingOptions;

        public bool CanChooseFollowing => !IsInspection && !Failed && Stage < DockRoute.Stages &&
            RouteDestination.HasValue && Current != null && (Current.Resolved ||
                Current.Kind is NodeKind.Camp or NodeKind.Town);

        public IReadOnlyList<NodeKind> FollowingOptions => CanChooseFollowing
            ? (Current.Kind == NodeKind.Town && PactAhead ? PactStop : followingOptions ??= OptionsAt(Stage + 1))
            : Array.Empty<NodeKind>();

        public RouteStop FollowingChoice(int index)
        {
            var options = FollowingOptions;
            int lane = RouteDestination.Value.Lane + (options.Count > 1 ? (index == 0 ? -1 : 1) : 0);
            return new RouteStop(Current.Kind == NodeKind.Town && PactAhead ? Stage : Stage + 1, lane, options[index]);
        }

        public bool EnterFollowing(int index)
        {
            var options = FollowingOptions;
            if (!Party.CanAdvanceDay || index < 0 || index >= options.Count ||
                options[index] is (NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !Party.CanStartBattle)
                return false;
            return Leave() && EnterRoute(index);
        }

        public bool DeclineEvent()
        {
            if (Current?.Kind != NodeKind.Event || Current.Resolved || Current.Battle != null) return false;
            Finish("你向对方道了别，准备赶路。");
            return true;
        }

        public RouteStop RouteChoice(int index)
        {
            var options = RouteOptions;
            int lane = RouteCurrent.Lane;
            if (options.Count > 1)
                lane += index == 0 ? -1 : 1;
            return new RouteStop(Stage, lane, options[index]);
        }

        public bool EnterRoute(int choice)
        {
            if (!Party.CanAdvanceDay || IsInspection || Current != null || Failed || FloorCleared || choice < 0 || choice >= RouteOptions.Count ||
                (SuspendedChoice.HasValue && choice != SuspendedChoice.Value))
                return false;
            var destination = RouteChoice(choice);
            var kind = destination.Kind.Value;
            if ((kind is NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !Party.CanStartBattle)
                return false;

            // Keep the generated node stable when a checkpoint is reentered.
            int seed = SuspendedChoice.HasValue ? nodeCheckpoint.Seed : random.Next();
            nodeCheckpoint = CaptureCheckpoint(seed, choice);
            Party.AdvanceDay();
            away.RemoveAll(a => !Party.Members.Any(m => m.Id == a.MemberId));
            random = new Random(seed);
            SuspendedChoice = null;
            RouteDestination = destination;
            bool tidePact = kind == NodeKind.Event && LeavingForPact;
            if (kind == NodeKind.Event) eventsTaken++;
            if (kind == NodeKind.EliteBattle) elitesTaken++;
            Open(kind, tidePact ? AdventureEventKind.TidePact : kind == NodeKind.Event ? RollEvent() : (AdventureEventKind?)null);
            return true;
        }

        public bool Inspect(NodeKind kind, AdventureEventKind? eventKind = null)
        {
            if (!IsInspection || (kind == NodeKind.Event && !eventKind.HasValue) ||
                Current?.Battle != null && !Current.Battle.Settled)
                return false;
            if (Current?.Resolved == true) CloseVisit();
            Current = null;
            Open(kind == NodeKind.Shop ? NodeKind.Town : kind, eventKind);
            return true;
        }

        private void Open(NodeKind kind, AdventureEventKind? eventKind)
        {
            var visit = Current = new AdventureVisit(kind, eventKind);
            if (!IsInspection) RecordEvent(eventKind);
            if (GraveStories.Contains(eventKind)) OpenGraveStory(eventKind.Value);
            if (eventKind == AdventureEventKind.Investor)
            {
                visit.InvestorAvailable = !InvestmentPaid && !InvestorVisitedThisFloor;
                investorFloors.Add(Floor);
            }
            if (kind == NodeKind.TreasureChest)
                visit.Rewards = treasureLoot.OrderBy(_ => random.Next()).Take(3).ToArray();
            if (kind == NodeKind.Town)
            {
                visit.Commissions = TownCommission.Draw(random);
                visit.Message = Party.DeliverWreath();
                visit.Stock = CreateStock();
                visit.Recruits = DrawTownRecruits();
                visit.ShopRecruitment = new ShopRecruitment(Party, characters, random, shopRecruitPrice);
            }
            if (kind is NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss)
                visit.Battle = new AdventureBattle(kind);
            if (eventKind == AdventureEventKind.LostTraveler)
            {
                var groups = RecruitCandidates().GroupBy(character => character.Profession)
                    .OrderBy(_ => random.Next()).Take(2).ToArray();
                visit.Recruits = groups.Select(group => group.ElementAt(random.Next(group.Count()))).ToArray();
                visit.Professions = visit.Recruits.Select(character => character.Profession).ToArray();
            }
            if (eventKind == AdventureEventKind.Alchemist)
                visit.Potions = equipment.Where(e => e.IsPotion).OrderBy(_ => random.Next()).Take(3).ToArray();
            if (eventKind == AdventureEventKind.AscensionTrial)
                visit.TrialGift = PickItem(random.NextDouble() < 0.7 ? 2 : 1);
            if (eventKind == AdventureEventKind.HatTrick)
            {
                var hats = new List<EquipmentDefinition>();
                for (int i = 0; i < 3; i++)
                {
                    double roll = random.NextDouble();
                    hats.Add(PickItem(roll < 0.05 ? 1 : roll < 0.40 ? 2 : 3, hats.Select(e => e.Id)));
                }
                visit.HatRewards = hats;
                visit.HatSwaps = Enumerable.Range(0, 7).Select(_ =>
                {
                    int first = random.Next(3);
                    return (first, (first + random.Next(1, 3)) % 3);
                }).ToArray();
            }
        }

        public IReadOnlyList<AdventureEventKind> AvailableEvents()
        {
            if (InCapital)
            {
                var fresh = CapitalStories.Kinds.Where(kind => !seenEvents.Contains(kind)).ToArray();
                return fresh.Length > 0 ? fresh : CapitalStories.Kinds;
            }
            if (InGraveyard)
            {
                var eligible = GraveStories.Kinds.Where(GraveEligible).ToArray();
                var fresh = eligible.Where(kind => !seenEvents.Contains(kind)).ToArray();
                return fresh.Length > 0 ? fresh : eligible;
            }
            var list = new List<AdventureEventKind> { AdventureEventKind.Ambush };
            if (!InvestmentPaid && !InvestorVisitedThisFloor && (Floor > 1 || Stage >= 4))
                list.Add(AdventureEventKind.Investor);
            if (Stage <= 3) list.AddRange(new[] { AdventureEventKind.LostTraveler, AdventureEventKind.FoundCoins, AdventureEventKind.HatTrick });
            else if (Stage <= 6) list.AddRange(new[] { AdventureEventKind.Forge, AdventureEventKind.LockedChest });
            else if (Stage <= 9) list.AddRange(new[] { AdventureEventKind.AscensionTrial, AdventureEventKind.Alchemist });
            return list.Where(kind => !seenEvents.Contains(kind)).ToArray();
        }

        private void RecordEvent(AdventureEventKind? kind)
        {
            if (kind is { } seen && seen != AdventureEventKind.Ambush)
                seenEvents.Add(seen);
        }

        private AdventureEventKind RollEvent()
        {
            var choices = AvailableEvents();
            return choices[random.Next(choices.Count)];
        }

        public bool Donate(int amount)
        {
            if (!At(AdventureEventKind.Investor) || InvestmentPaid || !Current.InvestorAvailable || amount < 1 ||
                !Party.TrySpendGold(amount, "捐赠")) return false;
            investorFloors.Add(Floor);
            Investment += amount;
            Party.RememberRoadEvent(EmployeeThought.Investment, amount.ToString());
            if (Investment >= 50)
            {
                var first = PickItem(1);
                var second = PickItem(1, new[] { first.Id });
                Party.AddEquipment(first); Party.AddEquipment(second);
                InvestmentPaid = true;
                Finish($"创业者在账本上划下最后一笔。临走前，她送给你{first.Name}和{second.Name}。");
            }
            else Finish($"创业者接过 {amount} 枚金币，把你的名字记在账本上。“改日再来看看船吧。”");
            return true;
        }

        public bool CollectCoins()
        {
            if (!At(AdventureEventKind.FoundCoins)) return false;
            int gold = random.Next(10, 21); Party.AddGold(gold);
            Party.RememberRoadEvent(EmployeeThought.FoundCoins, gold.ToString());
            Finish($"钱袋里有 {gold} 枚金币，已经收进你的行囊。"); return true;
        }

        public bool ChooseProfession(Profession profession)
        {
            if (!At(AdventureEventKind.LostTraveler) || !Current.Professions.Contains(profession) || Current.PendingRecruit != null)
                return false;
            var recruit = Current.Recruits.FirstOrDefault(character => character.Profession == profession);
            if (recruit == null || Party.Members.Any(member => member.Character.Id == recruit.Id))
                return false;
            Current.PendingRecruit = recruit;
            return CompleteRecruit(null);
        }

        public bool ChooseTownRecruit(string characterId, int days = 12, PerformanceBonuses terms = default, long? signingFee = null)
        {
            if (!CanUseTown(TownService.Recruit) || days < 1 || days > int.MaxValue - Party.Day || !terms.Valid) return false;
            var candidate = Current.Recruits.FirstOrDefault(c => c.Id == characterId);
            if (candidate == null) return false;
            var training = Party.TownCandidateTraining(candidate);
            var draft = Party.CandidateContract(candidate, days, terms, signingFee, training);
            if (draft == null) return false;
            int price = Party.SigningPrice(candidate);
            long total = price + draft.DueAtSigning;
            if (total > Party.Gold || !Party.TryRecruit(candidate, training)) return false;
            Party.TrySpendGold(price, candidate.Name + " · 人才市场转会费");
            Party.PaySigningWage(candidate.Name, draft);
            Party.SignNewHire(characterId, draft);
            Current.Recruits = Current.Recruits.Where(c => c.Id != characterId).ToArray();
            Current.UsedServices.Add(TownService.Recruit);
            Current.Message = $"{candidate.Name}加入讨伐队。转会费 {price} 金币，签字费 {draft.DueAtSigning} 金币，折算日薪 {draft.BaseTotal / (decimal)days:0.##} 金币。";
            return true;
        }

        public bool CompleteRecruit(string replaceMemberId)
        {
            var recruit = Current?.PendingRecruit;
            if (recruit == null || Current.Resolved || Current.Kind == NodeKind.Town && !CanUseTown(TownService.Recruit)) return false;
            bool success = replaceMemberId == null ? Party.TryRecruit(recruit) : Party.TryReplaceMember(replaceMemberId, recruit);
            // Keep the rolled character pending when a full party needs a replacement choice.
            if (!success) return false;
            Current.PendingRecruit = null;
            if (Current.Kind == NodeKind.Town)
            {
                Current.Recruits = Current.Recruits.Where(c => c.Id != recruit.Id).ToArray();
                Current.UsedServices.Add(TownService.Recruit);
                Current.Message = $"{recruit.Name}加入了讨伐队，原队员的装备留在背包中。";
            }
            else
            {
                Party.RememberRoadEvent(EmployeeThought.Traveler, recruit.Name, exceptCharacter: recruit.Id);
                Finish($"{recruit.Name}拍了拍旅途的尘土，加入了讨伐队。");
            }
            return true;
        }

        public IEnumerable<EquipmentInstance> TideOfferings => Party.SaleableItems.Where(item =>
            !item.Definition.IsPotion && item.Definition.Effect != EquipmentEffect.RecruitmentNotice);

        public bool OfferTidePact(string equipmentId, string bookId)
        {
            if (!At(AdventureEventKind.TidePact) || tideSinger == null ||
                !Party.TrySignTidePact(tideSinger, equipmentId, bookId)) return false;
            Party.RememberRoadEvent(EmployeeThought.Traveler, tideSinger.Name, exceptCharacter: tideSinger.Id);
            Finish("深海轻语收下装备和技能书，乘浪跟上了队伍。她已签下 99 天的零薪合同。");
            return true;
        }

        public IReadOnlyList<EquipmentInstance> ForgeInputs => Party.Inventory.Where(e =>
            !Party.Members.Any(m => m.EquipmentIds.Contains(e.Id))).ToArray();

        public bool Forge(string firstId, string secondId)
        {
            if (!At(AdventureEventKind.Forge) || firstId == secondId) return false;
            var inputs = ForgeInputs.Where(e => e.Id == firstId || e.Id == secondId).ToArray();
            if (inputs.Length != 2) return false;
            var owned = new HashSet<string>(Party.Inventory.Select(e => e.Definition.Id));
            int best = inputs.Min(e => e.Definition.Tier);
            var pool = equipment.Where(e => e.Tier < best && !owned.Contains(e.Id)).ToArray();
            if (pool.Length == 0) { Current.Message = "铁匠把旧物推了回来。“这两件先收好，换个搭配试试。”"; return false; }
            var output = pool[random.Next(pool.Length)];
            if (!Party.TryConsumeItems(new[] { firstId, secondId })) return false;
            Party.RememberRoadEvent(EmployeeThought.Forge, output.Name);
            Party.AddEquipment(output); Finish($"两件旧物在炉火中化为一体。你得到了{output.Name}，已放入背包。"); return true;
        }

        public bool LeaveCompanion(string memberId)
        {
            if (!At(AdventureEventKind.LockedChest)) return false;
            var member = Party.Members.FirstOrDefault(m => m.Id == memberId);
            if (member?.Position == null || !Party.TrySendAway(memberId)) return false;
            Party.RememberPersonalEvent(memberId, EmployeeThought.ChestDuty);
            away.Add(new AwayCompanion(memberId, member.Position.Value, CompletedNodes + 4));
            Finish($"{member.Character.Name}在箱子旁坐下，开始清理锁孔里的盐。“你们先走，开了我就跟上。”"); return true;
        }

        public bool StepHat(int step)
        {
            if (!At(AdventureEventKind.HatTrick) || step != Current.HatStep || step >= Current.HatSwaps.Count) return false;
            var pair = Current.HatSwaps[step];
            var positions = Current.HatPositions;
            (positions[pair.First], positions[pair.Second]) = (positions[pair.Second], positions[pair.First]);
            Current.HatStep++; return true;
        }

        public bool PickHat(int position)
        {
            if (!At(AdventureEventKind.HatTrick) || position < 0 || position > 2 || Current.HatStep != Current.HatSwaps.Count)
                return false;
            Current.PickedHat = position;
            Current.HatWon = Current.HatPositions[position] == 0;
            if (Current.HatWon)
            {
                var reward = Current.HatRewards[0]; Party.AddEquipment(reward);
                Party.RememberRoadEvent(EmployeeThought.HatWon, reward.Name);
                Finish($"你找到了装着{reward.Name}的帽子！物品已放入背包。");
            }
            else
            {
                Party.RememberRoadEvent(EmployeeThought.HatLost, "");
                Finish("你盯错了帽子。戏法师掀开所有帽子，让你看清了每顶帽子下的物品。");
            }
            return true;
        }

        public bool ChoosePotion(int index)
        {
            if (!At(AdventureEventKind.Alchemist) || index < 0 || index >= Current.Potions.Count) return false;
            var potion = Current.Potions[index]; Party.AddEquipment(potion);
            Party.RememberRoadEvent(EmployeeThought.Potion, potion.Name);
            Finish($"炼药师这才给瓶子贴上标签。你把{potion.Name}收进了背包。"); return true;
        }

        public bool TakeTrialGift()
        {
            if (!At(AdventureEventKind.AscensionTrial) || Current.TrialGift == null) return false;
            Party.RememberRoadEvent(EmployeeThought.TrialGift, Current.TrialGift.Name);
            Party.AddEquipment(Current.TrialGift); Finish($"你接受了试炼者的礼物：{Current.TrialGift.Name}已放入背包。"); return true;
        }

        public bool StartEventBattle()
        {
            if (Current == null || Current.Resolved || Current.Battle != null ||
                Current.Event != AdventureEventKind.Ambush && Current.Event != AdventureEventKind.AscensionTrial) return false;
            Current.Battle = new AdventureBattle(Current.Event == AdventureEventKind.Ambush ? NodeKind.NormalBattle : NodeKind.EliteBattle);
            return true;
        }

        public void BindBattle(string requestId)
        {
            if (Current?.Battle != null && !Current.Battle.Settled) Current.Battle.RequestId = requestId;
        }

        public IReadOnlyList<Combatant> PrepareEnemies(IReadOnlyList<CharacterDefinition> roster,
            IReadOnlyList<EnemyFormation> formations = null)
        {
            var ticket = Current?.Battle ?? throw new InvalidOperationException("No encounter selected.");
            if (ticket.Enemies != null) return ticket.Enemies;
            var rank = ticket.Kind switch
            {
                NodeKind.Boss => EnemyRank.Boss,
                NodeKind.EliteBattle => EnemyRank.Elite,
                _ => EnemyRank.Normal
            };
            int allies = Party.CaptureFormation(Side.Party).Count;
            int count = rank == EnemyRank.Normal ? Math.Max(1, allies) :
                Current.Event == AdventureEventKind.AscensionTrial ? 1 : Math.Min(3, Math.Max(1, allies - 1));
            var groups = formations?.Where(group => group.Rank == rank &&
                (rank != EnemyRank.Normal || group.Members.Count >= count)).ToArray();
            if (groups is { Length: > 0 })
            {
                ticket.Enemies = groups[random.Next(groups.Length)].CreateLineup("route-enemy", count);
                if (rank != EnemyRank.Normal)
                    ticket.EliteIds.Add(ticket.Enemies[0].Id);
                return ticket.Enemies;
            }

            EnemyRank RankOf(CharacterDefinition character) => character.Rank != EnemyRank.Unspecified
                ? character.Rank
                : character.Trait is CharacterTrait.EnemyBell or CharacterTrait.EnemyRearAmbush or CharacterTrait.EnemyResolve
                    ? EnemyRank.Elite : EnemyRank.Normal;
            var normal = roster.Where(c => RankOf(c) == EnemyRank.Normal).OrderBy(_ => random.Next()).ToArray();
            var selected = new List<(CharacterDefinition Character, bool Elite)>();
            if (ticket.Kind != NodeKind.NormalBattle)
            {
                var candidates = roster.Where(c => RankOf(c) == rank).ToArray();
                var primary = candidates.Length > 0 ? candidates[random.Next(candidates.Length)] :
                    roster.FirstOrDefault(c => c.Trait == CharacterTrait.EnemyBell) ?? roster.First();
                selected.Add((primary, true));
            }
            int usedSlots = selected.Sum(entry => entry.Character.PositionSlots);
            foreach (var candidate in normal.Where(c => selected.All(entry => entry.Character.Id != c.Id)))
            {
                if (selected.Count >= count)
                    break;
                if (usedSlots + candidate.PositionSlots > Rules.Positions)
                    continue;
                selected.Add((candidate, false));
                usedSlots += candidate.PositionSlots;
            }
            var units = new List<Combatant>();
            int position = 1;
            for (int i = 0; i < selected.Count; i++)
            {
                var entry = selected[i];
                string id = "route-enemy-" + (i + 1);
                var stats = entry.Character.BaseStats;
                if (entry.Elite)
                {
                    ticket.EliteIds.Add(id);
                    if (entry.Character.Rank == EnemyRank.Unspecified)
                        stats = new Stats(stats.Attack + (ticket.Kind == NodeKind.Boss ? 2 : 1), stats.Defense + 1,
                            stats.SpecialDefense + 1, (int)Math.Ceiling(stats.Health * (ticket.Kind == NodeKind.Boss ? 2 : 1.4)));
                }
                units.Add(new Combatant(id, entry.Character, Side.Enemy, position, stats, stats.Health));
                position += entry.Character.PositionSlots;
            }
            ticket.Enemies = units.ToArray();
            return ticket.Enemies;
        }

        public bool SettleBattle(BattleResult result)
        {
            if (!SettleBattle(result, out int gold))
                return false;
            Party.AddGold(Math.Min(gold, int.MaxValue - Party.Gold));
            if (Current.Battle.BonusEquipment != null)
            {
                Party.AddEquipment(Current.Battle.BonusEquipment);
                Current.Battle.BonusEquipment = null;
            }
            return true;
        }

        public bool SettleBattle(BattleResult result, out int gold)
        {
            gold = 0;
            var ticket = Current?.Battle;
            if (ticket == null || ticket.Settled || result == null || ticket.RequestId != result.Id || ticket.Enemies == null ||
                ticket.Enemies.Any(e => !result.HealthById.ContainsKey(e.Id))) return false;
            bool victory = result.Outcome == BattleOutcome.Victory;
            if (victory && ticket.Kind == NodeKind.Boss)
            {
                var spoils = BossSpoils.Roll(ticket.Enemies[0].Character.Id, bossCatalogue, random);
                Current.Rewards = spoils.Choices;
                ticket.BonusEquipment = spoils.Equipment;
            }
            ticket.Settled = true;
            gold = Current.Event == AdventureEventKind.Ambush ? (victory ? random.Next(12, 19) : 0) :
                RollKillGold(result, ticket.Enemies, random, ticket.EliteIds);
            if (victory)
            {
                var fallen = result.Events.Where(e => e.Kind == BattleEventKind.Defeated).Select(e => e.TargetId).ToHashSet();
                int fees = Party.Members.Sum(m => result.HealthById.TryGetValue(m.Id, out int hp) &&
                    ticket.Kind == NodeKind.NormalBattle && Current.Event == null && hp > 0 && m.Trait == PlayerTrait.AppearanceFee ? 1 :
                    fallen.Contains(m.Id) && m.Trait == PlayerTrait.Compensation ? 2 : 0);
                gold = Math.Max(0, gold - fees);
            }
            Current.Message = $"战斗获得 {gold} 金币。";
            if (!victory)
            {
                Current.Resolved = true;
                if (!IsInspection) Failed = true;
                Current.Message += "讨伐暂时告一段落。"; return true;
            }
            if (ticket.Kind == NodeKind.Boss)
            {
                Party.RecoverAfterBoss();
                Current.Message += "全队每人恢复了相当于最大生命值一半的生命。";
            }
            if (Current.Event == AdventureEventKind.Ambush)
                foreach (var witness in Party.Members.Where(m => result.HealthById.ContainsKey(m.Id)))
                    Party.RememberPersonalEvent(witness.Id, EmployeeThought.Ambush);
            if (QuestActive) QuestProgress = (int)Math.Min(Commission.Target, QuestProgress + Commission.Credit(result, ticket));
            if (Current.Event == AdventureEventKind.AscensionTrial)
            {
                Current.NeedsEvolution = EvolutionCandidates.Count > 0;
                if (!Current.NeedsEvolution) Current.Rewards = new[] { PickItem(1) };
            }
            else if (ticket.Kind != NodeKind.Boss && Current.Event != AdventureEventKind.Ambush)
            {
                bool elite = ticket.Kind != NodeKind.NormalBattle;
                var first = RollBattleReward(elite);
                Current.Rewards = elite ? new[] { first, RollBattleReward(true, new[] { first.Id }) } : new[] { first };
            }
            Current.Resolved = Current.Rewards.Count == 0 && !Current.NeedsEvolution;
            return true;
        }

        public static int RollKillGold(BattleResult result, IEnumerable<Combatant> enemies, Random random,
            ISet<string> elites = null)
        {
            var escaped = new HashSet<string>(result.Events.Where(entry => entry.Kind == BattleEventKind.Retreated)
                .Select(entry => entry.TargetId));
            return enemies.Where(enemy => result.HealthById[enemy.Id] == 0 && !escaped.Contains(enemy.Id))
                .Sum(enemy => elites?.Contains(enemy.Id) == true ? random.Next(14, 21) : random.Next(6, 10));
        }

        private EquipmentDefinition RollBattleReward(bool elite, IEnumerable<string> excluded = null)
        {
            double roll = random.NextDouble();
            double rareChance = elite ? .04 : .02;
            if (roll < rareChance && battleRewardTickets.Any(item => item.Tier == 0))
                return PickItem(battleRewardTickets.Where(item => item.Tier == 0), excluded);
            int tier = roll < rareChance + (elite ? .5 : .3) ? 1 : random.Next(2, 4);
            return PickItem(battleRewardTickets.Where(item => item.Tier == tier), excluded);
        }

        public BattleRewards RecordBattleRewards(int gold, IEnumerable<EquipmentDefinition> items)
        {
            if (Current?.Battle is not { Settled: true } ticket || ticket.Reward != null)
                throw new InvalidOperationException("Rewards need a newly settled battle.");
            return ticket.Reward = new BattleRewards(gold, ticket.BonusEquipment == null ? items : items.Append(ticket.BonusEquipment));
        }

        public bool OpenTreasureChest()
        {
            if (Current is not { Kind: NodeKind.TreasureChest, Resolved: false, TreasureOpened: false })
                return false;
            Current.TreasureOpened = true;
            return true;
        }

        public bool ClaimReward(int index)
        {
            if (Current == null || Current.Resolved || index < 0 || index >= Current.Rewards.Count ||
                Current.Kind == NodeKind.TreasureChest && !Current.TreasureOpened)
                return false;
            var reward = Current.Rewards[index]; Party.AddEquipment(reward);
            Finish(Current.Message + $"你选择了{reward.Name}，已放入背包。"); return true;
        }

        public IReadOnlyList<MemberView> EvolutionCandidates => Party.Members.Where(m => m.Health > 0 && !m.IsAway &&
            EvolutionForms(m.Id).Count > 0).ToArray();

        public IReadOnlyList<CharacterDefinition> EvolutionForms(string memberId)
        {
            var member = Party.Members.FirstOrDefault(m => m.Id == memberId);
            return member == null || member.IsAway || member.Health <= 0 ? Array.Empty<CharacterDefinition>() : characters.Where(c =>
                member.Character.EvolutionIds.Contains(c.Id) && !Party.Members.Any(other => other.Character.Id == c.Id))
                .Select(c => c.WithPersonality(member.Personality)).ToArray();
        }

        public bool ChooseEvolutionMember(string memberId)
        {
            if (Current == null || Current.Resolved || !Current.NeedsEvolution ||
                memberId != null && EvolutionForms(memberId).Count == 0)
                return false;
            Current.EvolutionMemberId = memberId;
            return true;
        }

        public bool Evolve(string memberId, string formId)
        {
            if (Current == null || Current.Resolved || !Current.NeedsEvolution ||
                Current.EvolutionMemberId != null && Current.EvolutionMemberId != memberId) return false;
            var form = TryEvolve(memberId, formId);
            if (form == null) return false;
            Current.EvolutionMemberId = null;
            Current.NeedsEvolution = false; Finish($"试炼完成，队员进阶为{form.Name}。原有装备保留。"); return true;
        }

        private CharacterDefinition TryEvolve(string memberId, string formId)
        {
            var form = EvolutionForms(memberId).FirstOrDefault(c => c.Id == formId);
            return form != null && Party.TryChangeForm(memberId, form) ? form : null;
        }

        public bool CanUseCamp => Current is { Kind: NodeKind.Camp, Resolved: false } || CanUseTown(TownService.Camp);

        public IReadOnlyList<MemberView> TrainingCandidates => Party.Members
            .Where(member => !member.IsAway && member.Health > 0 && member.Training.Sessions < CampTraining.Limit).ToArray();

        public bool TrainAtCamp(string memberId)
        {
            if (!CanUseCamp || !Party.TryTrain(memberId, IsInspection ? random : null, out var gains, out int remaining))
                return false;
            string name = Party.Members.First(member => member.Id == memberId).Character.Name;
            FinishCamp($"{name}完成训练。{gains.Describe()}。剩余训练次数 {remaining}。");
            return true;
        }

        public bool EvolveAtCamp(string memberId, string formId)
        {
            if (!CanUseCamp) return false;
            var form = TryEvolve(memberId, formId);
            if (form == null) return false;
            FinishCamp($"篝火旁，队员升阶为{form.Name}。原有装备保留。");
            return true;
        }

        public bool Rest()
        {
            if (!CanUseCamp || Current.Rested) return false;
            int healed = Party.Rest(0.3);
            Current.Rested = true;
            FinishCamp($"队伍休息完毕，合计恢复 {healed} 点生命。");
            return true;
        }

        private void FinishCamp(string message)
        {
            if (Current.Kind == NodeKind.Town)
            {
                Current.UsedServices.Add(TownService.Camp);
                Current.Message = message;
            }
            else
            {
                Finish(message);
            }
        }

        public bool CanUseTown(TownService service) => Current?.Kind == NodeKind.Town && !Current.Resolved &&
            (service is TownService.Recruit or TownService.Shop or TownService.Quest ||
                service == TownService.Camp && !Current.Rested && !Current.UsedServices.Contains(TownService.Camp));

        public int CommissionReward(TownCommission job) => job.Gold + 2 * Party.Members.Count(m => m.Trait == PlayerTrait.Contractor && m.Health > 0);
        public int TownQuestGold => !QuestActive ? 0 : commissionGold > 0 ? commissionGold : CommissionReward(Commission);

        public bool AcceptCommission(string id)
        {
            if (QuestActive || !CanUseTown(TownService.Quest)) return false;
            var job = Current.Commissions.FirstOrDefault(offer => offer.Id == id);
            if (job == null) return false;
            Commission = job;
            QuestProgress = 0;
            commissionGold = CommissionReward(job);
            Current.Message = $"已接下「{job.Title}」。{job.Objective}后，可回任意城镇领取 {commissionGold} 金币。";
            Current.UsedServices.Add(TownService.Quest);
            return true;
        }

        public bool ClaimCommission()
        {
            if (!CanUseTown(TownService.Quest) || !QuestActive || QuestProgress < Commission.Target) return false;
            int payout = TownQuestGold;
            if (payout > int.MaxValue - Party.Gold) return false;
            Party.AddGold(payout);
            Current.Message = $"「{Commission.Title}」已完成，领取 {payout} 金币。";
            Commission = null;
            QuestProgress = commissionGold = 0;
            Current.Commissions = TownCommission.Draw(random);
            Current.UsedServices.Add(TownService.Quest);
            return true;
        }

        public bool TownQuest() => QuestActive ? ClaimCommission() :
            CanUseTown(TownService.Quest) && AcceptCommission(Current.Commissions[0].Id);

        private IReadOnlyList<CharacterDefinition> DrawTownRecruits()
        {
            var owned = Party.Members.Select(member => member.Character.Id).ToHashSet();
            return characters.Where(character => !owned.Contains(character.Id)).OrderBy(_ => random.Next()).Take(8).ToArray();
        }

        public bool DrawShopRecruit()
        {
            if (!CanUseTown(TownService.Shop) || !Current.ShopRecruitment.TryDraw(random, price => Party.TrySpendGold(price, "招募转盘"))) return false;
            shopRecruitPrice = Current.ShopRecruitment.NextPrice;
            Current.UsedServices.Add(TownService.Shop);
            return true;
        }

        public bool SignShopRecruit(string characterId, string replacedMemberId = null, int days = 12,
            PerformanceBonuses terms = default, long? signingFee = null)
        {
            if (!CanUseTown(TownService.Shop)) return false;
            var candidate = Current.ShopRecruitment.Candidates.FirstOrDefault(c => c.Id == characterId);
            var draft = Party.CandidateContract(candidate, days, terms, signingFee);
            long release = replacedMemberId == null ? 0 : Party.Members.FirstOrDefault(m => m.Id == replacedMemberId)?.Contract?.ExitCost ?? 0;
            if (draft == null || draft.DueAtSigning + release > Party.Gold || !Current.ShopRecruitment.TrySign(characterId, replacedMemberId)) return false;
            Party.PaySigningWage(candidate.Name, draft);
            Party.SignNewHire(characterId, draft);
            return true;
        }

        public bool Buy(int index)
        {
            if (!CanUseTown(TownService.Shop) || index < 0 || index >= Current.Stock.Count)
                return false;
            var offer = Current.Stock[index];
            if (offer.Sold || !Party.TrySpendGold(offer.Price, "购买 · " + offer.Item.Name)) return false;
            if (offer.Item is not SkillBookDefinition && !Current.Stock.Any(o => o.Sold && o.Item is not SkillBookDefinition))
                Party.AddGold(2 * Party.Members.Count(m => m.Trait == PlayerTrait.Rebate));
            Party.AddEquipment(offer.Item); offer.Sold = true;
            Current.UsedServices.Add(TownService.Shop);
            return true;
        }

        private IReadOnlyList<AdventureShopOffer> CreateStock()
        {
            bool discount = Party.Members.Any(m => !m.IsAway && m.Health > 0 && m.Character.Trait == CharacterTrait.ShopDiscount);
            return AdventureShopOffer.Draw(shopCatalogue, random, discount);
        }

        public bool Leave()
        {
            if (Current == null || Current.Battle != null && !Current.Battle.Settled ||
                Current.Kind == NodeKind.TreasureChest && !Current.Resolved && !IsInspection)
                return false;
            if (!Current.Resolved) Finish("你整顿行囊，继续前进。");
            return CloseVisit();
        }

        public bool CloseVisit()
        {
            if (Current == null || !Current.Resolved) return false;
            if (Current.Kind is NodeKind.Event or NodeKind.TreasureChest) LastClosedEventDay = Party.Day;
            Current = null;
            AdvanceCompletedVisit();
            return true;
        }

        private void AdvanceCompletedVisit()
        {
            CompletedNodes++;
            ProcessReturns();
            if (RouteDestination is RouteStop destination)
            {
                RoutePrevious = RouteCurrent;
                RouteCurrent = destination;
                RouteDestination = null;
                if (Stage == DockRoute.Stages) FloorCleared = !Failed;
                else if (destination.Kind != NodeKind.Town || !PactAhead) Stage++;
                else followingOptions = null; // The pact takes a day at the wharf before the next numbered stop.
            }
            nodeCheckpoint = null;
            routeOptions = followingOptions;
            followingOptions = null;
            EnterClearedChapter();
        }

        private void EnterClearedChapter()
        {
            if (Floor <= 2 && FloorCleared && !IsInspection && !Failed && Current == null)
                NextFloor();
        }

        public void AdvanceInspection()
        {
            if (!IsInspection || Current?.Battle != null && !Current.Battle.Settled) return;
            if (Current != null) { Leave(); return; }
            CompletedNodes++; Stage = Stage == DockRoute.Stages ? 1 : Stage + 1; ProcessReturns();
        }

        public bool NextFloor()
        {
            if (CampaignWon || !IsInspection && !FloorCleared || Current != null) return false;
            if (Party.HasBorrowedName) return false;
            seenEvents.RemoveWhere(kind => GraveStories.Contains(kind) || CapitalStories.Contains(kind));
            Floor++; Stage = 1; FloorCleared = Failed = false; eventsTaken = elitesTaken = 0;
            RouteCurrent = default;
            RoutePrevious = RouteDestination = null;
            routeOptions = null; Route = new DockRoute(random); return true;
        }

        private void ProcessReturns()
        {
            ReturnMessage = Party.CollectPressCorrection();
            foreach (var companion in away.Where(a => a.ReturnAt <= CompletedNodes).ToArray())
            {
                if (!Party.TryReturnMember(companion.MemberId, companion.Position)) continue;
                away.Remove(companion);
                string name = Party.Members.First(m => m.Id == companion.MemberId).Character.Name;
                double roll = random.NextDouble();
                if (roll < 0.7)
                {
                    var item = PickItem(roll < 0.6 ? 2 : 1); Party.AddEquipment(item);
                    Party.RememberPersonalEvent(companion.MemberId, EmployeeThought.ChestReturn, item.Name);
                    ReturnMessage += $"{name}带着{item.Name}归队。 ";
                }
                else
                {
                    int gold = random.Next(15, 21); Party.AddGold(gold);
                    Party.RememberPersonalEvent(companion.MemberId, EmployeeThought.ChestReturn, $"{gold} 金币");
                    ReturnMessage += $"{name}带着 {gold} 金币归队。 ";
                }
            }
        }

        public void AbortBattle()
        {
            if (Current?.Battle == null || Current.Battle.Settled) return;
            Party.CancelBattle(); Current.Battle.Settled = true;
            Finish("挑战中止，没有获得战利品。");
            if (!IsInspection) Failed = true;
        }

        private bool At(AdventureEventKind kind) => Current?.Kind == NodeKind.Event && Current.Event == kind && !Current.Resolved && Current.Battle == null;
        private void Finish(string message) { Current.Message = message; Current.Resolved = true; }
        private IEnumerable<CharacterDefinition> RecruitCandidates() => characters.Where(c => c.Stage == 1 &&
            !Party.Members.Any(m => m.Character.Id == c.Id));
        private EquipmentDefinition PickItem(int tier, IEnumerable<string> excluded = null) =>
            PickItem(equipment.Where(item => item.Tier == tier), excluded);

        private EquipmentDefinition PickItem(IEnumerable<EquipmentDefinition> tierItems, IEnumerable<string> excluded)
        {
            var forbidden = new HashSet<string>(excluded ?? Array.Empty<string>());
            var pool = tierItems.ToArray();
            var choices = pool.Where(item => !forbidden.Contains(item.Id)).ToArray();
            if (choices.Length == 0) choices = pool;
            if (choices.Length == 0) throw new InvalidOperationException("Missing equipment reward pool.");
            return choices[random.Next(choices.Length)];
        }
    }
}
