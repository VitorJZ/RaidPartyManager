using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public enum NodeKind { Event, Camp, Shop, Town, NormalBattle, EliteBattle, Boss, TreasureChest }
    // Checkpoints store these numbers. Retired events leave gaps.
    public enum AdventureEventKind
    {
        Investor = 0, Ambush = 1, LostTraveler = 2, FoundCoins = 3, HatTrick = 4, Forge = 5,
        LockedChest = 6, AscensionTrial = 7, Alchemist = 8,
        Obituary = 9, Pallbearer = 10, ClaimDesk = 11, Auction = 12, HoundWalk = 14, DeadLetters = 15,
        Ravens = 17, BellRope = 18, SoulWard = 19, SoupQueue = 20, WhisperWall = 21, Epitaph = 22,
        FareGate = 24, DeadMail = 25, TimeOffice = 26, WaterCharter = 27, ClinicQueue = 29, WrongSwitch = 30,
        ElectionPress = 32, BoilerUnion = 33, SeedArchive = 35, SpareHands = 36, GoldStandard = 37, LastMeridian = 38,
        TidePact = 39, GraveSeed = 40, RecruitmentWheel = 41
    }
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
        public const int EquipmentSlots = 5;
        public const int BookSlots = 3;
        private readonly Func<bool> shouldDiscount;
        public EquipmentDefinition Item { get; }
        internal int ListedPrice { get; }
        internal bool UsesRosterDiscount => shouldDiscount != null;
        public int Price => shouldDiscount?.Invoke() == true ? (int)((4L * ListedPrice + 4) / 5) : ListedPrice;
        public bool Sold { get; internal set; }
        internal AdventureShopOffer(EquipmentDefinition item, int price, Func<bool> shouldDiscount = null)
        {
            Item = item;
            ListedPrice = price;
            this.shouldDiscount = shouldDiscount;
        }

        public static int BasePrice(EquipmentDefinition item) => item.Price;

        public static int ResalePrice(EquipmentDefinition item, int recoveryPercent = 50) =>
            item.BossExclusive ? 100 : Math.Max(1, (int)((long)BasePrice(item) * recoveryPercent / 100));

        public static IReadOnlyList<AdventureShopOffer> Draw(IEnumerable<EquipmentDefinition> catalogue, Random random, bool discount) =>
            Draw(catalogue, random, discount ? () => true : null);

        public static IReadOnlyList<AdventureShopOffer> Draw(IEnumerable<EquipmentDefinition> catalogue, Random random, Func<bool> shouldDiscount) =>
            FillStock(Array.Empty<AdventureShopOffer>(), catalogue, random, shouldDiscount);

        internal static IReadOnlyList<AdventureShopOffer> FillStock(IEnumerable<AdventureShopOffer> existing,
            IEnumerable<EquipmentDefinition> catalogue, Random random, Func<bool> shouldDiscount)
        {
            var offers = existing.ToList();
            int books = offers.Count(o => o.Item is SkillBookDefinition);
            int gear = offers.Count - books;
            if (gear >= EquipmentSlots && books >= BookSlots) return offers.AsReadOnly();
            var ids = offers.Select(offer => offer.Item.Id).ToHashSet();
            var stock = catalogue.Where(e => e.AvailableInRegularPool && BasePrice(e) > 0 && e.Tier is >= 1 and <= 3)
                .GroupBy(e => e.Id).Select(g => g.First()).Where(e => !ids.Contains(e.Id)).ToArray();
            int premium = offers.Count(o => o.Item is not SkillBookDefinition && o.Item.Tier == 1);
            var items = stock.Where(e => e is not SkillBookDefinition).OrderBy(_ => random.Next()).ToArray();
            var highTier = items.Where(e => e.Tier == 1).Take(Math.Clamp(2 - premium, 0, Math.Max(0, EquipmentSlots - gear)));
            foreach (var item in highTier.Concat(items.Where(e => e.Tier != 1)).Concat(items).Distinct()
                         .Take(Math.Max(0, EquipmentSlots - gear)).Concat(
                             stock.OfType<SkillBookDefinition>().OrderBy(_ => random.Next()).Take(Math.Max(0, BookSlots - books))))
            {
                int basePrice = BasePrice(item);
                int minimum = (int)((9L * basePrice + 9) / 10);
                int maximum = (int)Math.Min(int.MaxValue, 6L * basePrice / 5);
                int price = minimum + (int)((maximum - minimum + 1L) * random.NextDouble());
                offers.Add(new AdventureShopOffer(item, price, shouldDiscount));
            }
            return offers.AsReadOnly();
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
        public IReadOnlyList<BattleRating> Ratings { get; internal set; } = Array.Empty<BattleRating>();
        internal EquipmentDefinition BonusEquipment { get; set; }
        public string BloomWearer { get; internal set; }
        public bool BloomWaitingForRoom { get; internal set; }
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
        internal Dictionary<string, int> RecruitSpecials { get; } = new();
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
        public bool WheelLanded { get; internal set; }
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
        private const int CampaignFloors = 3;
        private Random random;
        public const string TideSingerId = "DeepSeaWhisper";
        private readonly CharacterDefinition tideSinger;
        public const string GraveGardenerId = "GraveGardener";
        internal const string RotBloomWitchId = "RotBloomWitch";
        public static bool IsGardenerForm(string id) => id is GraveGardenerId or RotBloomWitchId;
        private readonly CharacterDefinition gardener;
        private readonly EquipmentDefinition graveSeed;
        private int graveSeedStage;
        private readonly CharacterDefinition[] characters;
        private readonly CharacterDefinition[] roster;
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
        private readonly List<FloorBattleRating> floorBattles = new();
        private int shopRecruitPrice = ShopRecruitment.StartingPrice;

        public PartyRun Party { get; }
        public bool IsInspection { get; }
        public int Floor { get; private set; } = 1;
        public int FirstRatedStage { get; private set; } = 1;
        public IReadOnlyList<FloorBattleRating> FloorBattles => floorBattles;
        public bool InGraveyard => (Floor - 1) % 3 == 1;
        public bool InCapital => (Floor - 1) % 3 == 2;
        public string RegionName => InCapital ? "机巧遗都" : InGraveyard ? "低语墓园" : "弥雾码头";
        public bool CrossingChapter => !IsInspection && Floor <= 2 && Stage == DockRoute.Stages &&
            !Failed && Current is { Kind: NodeKind.Boss, Resolved: true, Battle: { Settled: true } };
        private bool AtCampaignFinalStage => !IsInspection && InCapital && !Failed && Stage == DockRoute.Stages;
        private bool FinalBossDayAlreadyCounted => SuspendedChoice.HasValue && nodeCheckpoint.HasFinalBossPreparation;
        public bool PreparingFinalBoss => AtCampaignFinalStage &&
            Current is { Kind: NodeKind.Boss, Battle: { Settled: false, RequestId: null } };
        public bool NeedsFinalPayroll => Party.ContractsEnabled && PreparingFinalBoss &&
            Party.Members.Any(member => Party.WageBalanceFor(member.Id) > 0);
        public int? DaysUntilFinalPayroll
        {
            get
            {
                if (IsInspection || Failed || Floor > CampaignFloors ||
                    AtCampaignFinalStage && (FloorCleared || Current?.Battle is { RequestId: not null } or { Settled: true }))
                    return null;

                // The tide pact uses a day without advancing the numbered route stage.
                bool enteredStage = FinalBossDayAlreadyCounted || (RouteDestination ?? RouteCurrent).Number == Stage;
                return (CampaignFloors - Floor) * DockRoute.Stages + DockRoute.Stages - Stage +
                    (enteredStage ? 0 : 1) + (PactAhead ? 1 : 0);
            }
        }
        public IReadOnlyList<string> PayrollDepartures { get; private set; } = Array.Empty<string>();
        public bool CampaignWon => AtCampaignFinalStage && (Current == null ? FloorCleared :
                Current is { Kind: NodeKind.Boss, Resolved: true, Battle: { Settled: true } } && !HasBattleRewardScreen);
        public int Stage { get; private set; } = 1;
        public int CompletedNodes { get; private set; }
        public int LastClosedEventDay { get; private set; } = -1;
        public int Investment { get; private set; }
        public bool InvestmentPaid { get; private set; }
        public bool HasVisitedTown { get; private set; }
        public bool InvestorVisitedThisFloor => investorFloors.Contains(Floor);
        public bool FloorCleared { get; private set; }
        public bool Failed { get; private set; }
        public DockRoute Route { get; private set; }
        public RouteStop RouteCurrent { get; private set; }
        public RouteStop? RoutePrevious { get; private set; }
        public RouteStop? RouteDestination { get; private set; }
        public int? SuspendedChoice { get; private set; }
        public AdventureVisit Current { get; private set; }
        public bool AtRecruitmentWheel => Current?.Event == AdventureEventKind.RecruitmentWheel;
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
            roster = characters.Where(c => c.RosterNumber > 0).ToArray();
            tideSinger = roster.FirstOrDefault(c => c.Id == TideSingerId);
            gardener = roster.FirstOrDefault(c => c.Id == GraveGardenerId);
            this.characters = roster.Where(c => c.Id != TideSingerId && !IsGardenerForm(c.Id)).ToArray();
            var items = equipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
            bossCatalogue = items;
            graveSeed = items.FirstOrDefault(item => item.Effect == EquipmentEffect.StrangeSeed);
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
                    Ratings = Current.Battle.Ratings.ToArray(),
                    Kind = Current.Battle.Kind, BloomWaitingForRoom = Current.Battle.BloomWaitingForRoom,
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
                if (AtRecruitmentWheel)
                {
                    saved.HasWheelVisit = true;
                    saved.WheelLanded = Current.WheelLanded;
                    saved.WheelVisit = new RecruitmentCheckpoint();
                    recruitment.Capture(saved.WheelVisit);
                    return saved;
                }
                saved.HasShopVisit = true;
                saved.ShopVisit = new ShopVisitCheckpoint
                {
                    Commissions = Current.Commissions.Select(job => job.Id).ToArray(),
                    Stock = Current.Stock.Select(offer => new ShopOfferCheckpoint
                        { Item = offer.Item.Id, Price = offer.ListedPrice, Sold = offer.Sold, UsesRosterDiscount = offer.UsesRosterDiscount }).ToArray(),
                    UsedServices = Current.UsedServices.ToArray(),
                    TownRecruits = Current.Recruits.Select(candidate => candidate.Id).ToArray(),
                    TownRecruitPricePercents = Current.Recruits.Select(TownRecruitPricePercent).ToArray(),
                    BrokerRefusals = Current.BrokerRefusals, BrokerCounter = Current.BrokerCounter, BrokerMember = Current.BrokerMember,
                    BrokerLastAsk = Current.BrokerLastAsk,
                    PendingRecruit = Current.PendingRecruit?.Id, Rested = Current.Rested, Message = Current.Message
                };
                recruitment.Capture(saved.ShopVisit);
                return saved;
            }

            if (Current is { Resolved: false })
                return nodeCheckpoint;

            var checkpoint = CaptureCheckpoint(SuspendedChoice.HasValue ? nodeCheckpoint.Seed : random.Next(),
                SuspendedChoice ?? (Current != null ? nodeCheckpoint.Choice : -1), Current?.Resolved == true);
            checkpoint.HasFinalBossPreparation = FinalBossDayAlreadyCounted;
            return checkpoint;
        }

        private AdventureCheckpoint CaptureCheckpoint(int seed, int choice, bool finishVisit = false) =>
            new AdventureCheckpoint
            {
                Party = Party.CaptureCheckpoint(), Seed = seed, Floor = Floor, Stage = Stage, CompletedNodes = CompletedNodes,
                FirstRatedStage = FirstRatedStage, FloorBattles = floorBattles.ToArray(),
                EventsTaken = eventsTaken, GraveSeedStage = graveSeedStage, ElitesTaken = elitesTaken, Investment = Investment, InvestmentPaid = InvestmentPaid,
                HasVisitedTown = HasVisitedTown,
                InvestorFloors = investorFloors.ToArray(), SeenEvents = seenEvents.OrderBy(kind => kind).ToArray(),
                FloorCleared = FloorCleared, Failed = Failed, PayrollDepartures = PayrollDepartures.ToArray(),
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
            IEnumerable<EquipmentDefinition> equipment, bool contractsEnabled = true)
        {
            if (saved == null || saved.Version != AdventureCheckpoint.CurrentVersion || saved.Nodes?.Length != DockRoute.Stages ||
                saved.Stage is < 1 or > DockRoute.Stages || saved.Floor < 1 || saved.CompletedNodes < 0 ||
                (saved.ShopRecruitPricePlusOne != 0 &&
                    (saved.ShopRecruitPricePlusOne is < 1 or > 96 || (saved.ShopRecruitPricePlusOne - 1) % 5 != 0)) ||
                saved.Choice < -1 || (saved.Choice >= 0 && (saved.Options == null || saved.Choice >= saved.Options.Length)) ||
                ((saved.FinishVisit || saved.HasBattleReward || saved.HasShopVisit || saved.HasWheelVisit || saved.HasFinalBossPreparation) && saved.Choice < 0))
                throw new ArgumentException("Invalid adventure checkpoint.");

            var catalog = characters.ToArray();
            var items = equipment.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
            var party = PartyRun.RestoreCheckpoint(saved.Party, catalog.ToDictionary(character => character.Id), items, contractsEnabled);
            bool insideNode = saved.HasShopVisit || saved.HasWheelVisit || saved.HasBattleReward || saved.FinishVisit || saved.HasFinalBossPreparation;
            party.StartContracts(saved.Seed, saved.CompletedNodes + (insideNode ? 2 : 1));
            var run = new AdventureRun(party, catalog, items.Values, false, new Random(saved.Seed))
            {
                Floor = saved.Floor, Stage = saved.Stage, CompletedNodes = saved.CompletedNodes, eventsTaken = saved.EventsTaken,
                FirstRatedStage = saved.FirstRatedStage > 0 ? saved.FirstRatedStage : saved.Stage + (saved.FinishVisit ? 1 : 0),
                graveSeedStage = saved.GraveSeedStage, elitesTaken = saved.ElitesTaken, Investment = saved.Investment, InvestmentPaid = saved.InvestmentPaid,
                HasVisitedTown = saved.IncludesTownVisit(),
                FloorCleared = saved.FloorCleared, Failed = saved.Failed,
                PayrollDepartures = contractsEnabled ? saved.PayrollDepartures ?? Array.Empty<string>() : Array.Empty<string>(),
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
            if (saved.FloorBattles != null)
                run.floorBattles.AddRange(saved.FloorBattles.Where(fight => fight?.Members != null));
            run.investorFloors.UnionWith(saved.InvestorFloors ?? Array.Empty<int>());
            if (saved.SeenEvents != null)
                run.seenEvents.UnionWith(saved.SeenEvents.Where(kind => Enum.IsDefined(typeof(AdventureEventKind), kind)));
            if (run.InGraveyard && run.graveSeedStage == 0)
                run.PlanGraveSeed(saved.Stage + (saved.Choice >= 0 ? 1 : 0));
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
                        Ratings = Array.AsReadOnly(BattleRating.ScoreBattle(reward.Ratings ?? Array.Empty<BattleRating>())),
                        Settled = true, BloomWaitingForRoom = reward.BloomWaitingForRoom,
                        Reward = new BattleRewards(reward.Gold,
                            reward.Items.Select(id => string.IsNullOrEmpty(id) ? null : items[id]))
                    }
                };
                // Reward saves made before floor history still contain this fight's raw contributions.
                if (saved.FirstRatedStage == 0)
                    run.RecordFloorBattle();
            }
            else if (saved.HasWheelVisit)
            {
                var destination = run.RouteChoice(saved.Choice);
                if (destination.Kind != NodeKind.Event || saved.HasShopVisit)
                    throw new ArgumentException("Invalid recruitment wheel visit.");
                run.RouteDestination = destination;
                run.nodeCheckpoint = saved;
                run.RecordEvent(AdventureEventKind.RecruitmentWheel);
                run.Current = new AdventureVisit(NodeKind.Event, AdventureEventKind.RecruitmentWheel)
                {
                    ShopRecruitment = ShopRecruitment.Restore(party, catalog.ToDictionary(c => c.Id), saved.WheelVisit, run.shopRecruitPrice),
                    WheelLanded = saved.WheelLanded
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
                var recruitment = ShopRecruitment.Restore(party, byId, shop, run.shopRecruitPrice);
                run.RouteDestination = destination;
                run.nodeCheckpoint = saved;
                run.Current = new AdventureVisit(destination.Kind.Value, null)
                {
                    BrokerRefusals = Math.Clamp(shop.BrokerRefusals, 0, BrokerPatience),
                    BrokerCounter = Math.Max(0, shop.BrokerCounter), BrokerMember = shop.BrokerMember,
                    BrokerLastAsk = Math.Max(0, shop.BrokerLastAsk),
                    Commissions = TownCommission.RestoreBoard(shop.Commissions, run.random),
                    ShopRecruitment = recruitment, Rested = shop.Rested, Message = shop.Message ?? "",
                    Stock = AdventureShopOffer.FillStock(shop.Stock.Select(offer => new AdventureShopOffer(items[offer.Item],
                        offer.Price == 0 ? items[offer.Item].Price : offer.Price,
                        offer.UsesRosterDiscount ? () => party.HasShopDiscount : null)
                        { Sold = offer.Sold }), run.shopCatalogue, run.random, () => party.HasShopDiscount),
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
                if (saved.Options[saved.Choice] != NodeKind.Shop &&
                    shop.TownRecruitPricePercents is { Length: > 0 } percentages &&
                    percentages.Length == shop.TownRecruits.Length)
                {
                    for (int i = 0; i < percentages.Length; i++)
                        if (percentages[i] is 10 or 50)
                            run.Current.RecruitSpecials[shop.TownRecruits[i]] = percentages[i];
                }
                else
                    RollTownRecruitSpecials(run.Current, new Random(saved.Seed));
                run.Current.UsedServices.UnionWith(shop.UsedServices);
            }
            else if (saved.FinishVisit)
            {
                run.RouteDestination = run.RouteChoice(saved.Choice);
                run.AdvanceCompletedVisit();
            }
            else if (saved.Choice >= 0)
            {
                if (saved.HasFinalBossPreparation &&
                    (!run.AtCampaignFinalStage || run.FloorCleared || run.RouteChoice(saved.Choice).Kind != NodeKind.Boss))
                    throw new ArgumentException("Invalid final boss visit.");
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

        private void PlanGraveSeed(int firstStage)
        {
            if (gardener == null || graveSeed == null || seenEvents.Contains(AdventureEventKind.GraveSeed)) return;
            var forks = Route.Nodes.Where(node => node.Number >= firstStage && node.IsFork).ToArray();
            var fork = forks.FirstOrDefault(node => node.Alternative == NodeKind.Event) ?? forks.FirstOrDefault();
            if (fork == null) return;
            graveSeedStage = fork.Number;
            fork.Alternative = NodeKind.Event;
            if (Stage == graveSeedStage) routeOptions = null;
            if (Stage + 1 == graveSeedStage) followingOptions = null;
        }

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

        public bool EnterFollowing(int index, Action afterClosing = null)
        {
            var options = FollowingOptions;
            if (!Party.CanAdvanceDay || index < 0 || index >= options.Count ||
                options[index] is (NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !Party.CanStartBattle)
                return false;
            if (!Leave()) return false;
            afterClosing?.Invoke();
            return EnterRoute(index);
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
            bool dayAlreadyCounted = FinalBossDayAlreadyCounted;
            if ((!dayAlreadyCounted && !Party.CanAdvanceDay) || IsInspection || Current != null || Failed || FloorCleared || choice < 0 || choice >= RouteOptions.Count ||
                (SuspendedChoice.HasValue && choice != SuspendedChoice.Value))
                return false;
            var destination = RouteChoice(choice);
            var kind = destination.Kind.Value;
            if ((kind is NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !Party.CanStartBattle)
                return false;

            // Keep the generated node stable when a checkpoint is reentered.
            int seed = SuspendedChoice.HasValue ? nodeCheckpoint.Seed : random.Next();
            nodeCheckpoint = CaptureCheckpoint(seed, choice);
            nodeCheckpoint.HasFinalBossPreparation = dayAlreadyCounted;
            if (!dayAlreadyCounted) Party.AdvanceDay();
            away.RemoveAll(a => !Party.Members.Any(m => m.Id == a.MemberId));
            random = new Random(seed);
            SuspendedChoice = null;
            RouteDestination = destination;
            bool tidePact = kind == NodeKind.Event && LeavingForPact;
            if (kind == NodeKind.Event) eventsTaken++;
            if (kind == NodeKind.EliteBattle) elitesTaken++;
            AdventureEventKind? story = null;
            if (kind == NodeKind.Event)
                story = tidePact ? AdventureEventKind.TidePact :
                    InGraveyard && Stage == graveSeedStage ? AdventureEventKind.GraveSeed : RollEvent();
            Open(kind, story);
            return true;
        }

        public bool Inspect(NodeKind kind, AdventureEventKind? eventKind = null)
        {
            if (!IsInspection || (kind == NodeKind.Event && (!eventKind.HasValue || !Enum.IsDefined(typeof(AdventureEventKind), eventKind.Value))) ||
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
                HasVisitedTown = true;
                visit.Commissions = TownCommission.Draw(random);
                visit.Message = Party.DeliverWreath();
                visit.Stock = CreateStock();
                visit.Recruits = DrawTownRecruits();
            }
            if (kind == NodeKind.Town || eventKind == AdventureEventKind.RecruitmentWheel)
                visit.ShopRecruitment = new ShopRecruitment(Party, characters, random, shopRecruitPrice);
            if (kind == NodeKind.Town)
                RollTownRecruitSpecials(visit, random);
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
            if (InCapital || InGraveyard)
            {
                var eligible = InCapital ? CapitalStories.Kinds : GraveStories.Kinds.Where(GraveEligible).ToArray();
                var fresh = eligible.Append(AdventureEventKind.RecruitmentWheel).Where(kind => !seenEvents.Contains(kind)).ToArray();
                return fresh.Length > 0 ? fresh : eligible;
            }
            var list = new List<AdventureEventKind> { AdventureEventKind.Ambush };
            if (!InvestmentPaid && !InvestorVisitedThisFloor && (Floor > 1 || Stage >= 4))
                list.Add(AdventureEventKind.Investor);
            if (Stage <= 3) list.AddRange(new[] { AdventureEventKind.LostTraveler, AdventureEventKind.FoundCoins, AdventureEventKind.HatTrick });
            else if (Stage <= 6) list.AddRange(new[] { AdventureEventKind.Forge, AdventureEventKind.LockedChest });
            else if (Stage <= 9) list.AddRange(new[] { AdventureEventKind.AscensionTrial, AdventureEventKind.Alchemist });
            return list.Append(AdventureEventKind.RecruitmentWheel).Where(kind => !seenEvents.Contains(kind)).ToArray();
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
            if (!CanUseTown(TownService.Recruit) ||
                Party.ContractsEnabled && (days < 1 || days > int.MaxValue - Party.ContractDay || !terms.Valid)) return false;
            var candidate = Current.Recruits.FirstOrDefault(c => c.Id == characterId);
            if (candidate == null) return false;
            var training = Party.TownCandidateTraining(candidate);
            var draft = Party.ContractsEnabled ? Party.CandidateContract(candidate, days, terms, signingFee, training) : null;
            if (Party.ContractsEnabled && draft == null) return false;
            int price = TownRecruitPrice(candidate);
            long total = price + (draft?.DueAtSigning ?? 0);
            if (total > Party.Gold || !Party.TryRecruit(candidate, training)) return false;
            Party.TrySpendGold(price, candidate.Name + " · 人才市场转会费");
            if (draft != null)
            {
                Party.PaySigningWage(candidate.Name, draft);
                Party.SignNewHire(characterId, draft);
            }
            Current.Recruits = Current.Recruits.Where(c => c.Id != characterId).ToArray();
            Current.RecruitSpecials.Remove(characterId);
            Current.UsedServices.Add(TownService.Recruit);
            Current.Message = draft == null ? $"{candidate.Name}加入讨伐队。转会费 {price} 金币。" :
                $"{candidate.Name}加入讨伐队。转会费 {price} 金币，签字费 {draft.DueAtSigning} 金币，折算日薪 {draft.BaseTotal / (decimal)days:0.##} 金币。";
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
                Current.RecruitSpecials.Remove(recruit.Id);
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

        public bool TakeGraveSeedReward(bool seed)
        {
            if (!At(AdventureEventKind.GraveSeed) || (seed && graveSeed == null)) return false;
            if (seed)
            {
                Party.AddEquipment(graveSeed).BattlesToBloom = random.Next(3, 6);
                Finish("你把奇怪的种子收进背包。把它装备在出战队员身上，看看它会不会发芽。");
            }
            else
            {
                Party.AddGold(Math.Min(50, int.MaxValue - Party.Gold));
                Finish("你拿走了 50 金币，把种子留在温室里。");
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
            Finish("深海轻语收下装备和技能书，乘浪跟上了队伍。" +
                (Party.ContractsEnabled ? "她已签下 99 天的零薪合同。" : ""));
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

        public bool ConfirmFinalPayroll()
        {
            if (!PreparingFinalBoss)
                return false;
            if (!NeedsFinalPayroll)
                return Party.CanStartBattle;

            PayrollDepartures = Party.RemoveUnpaidEmployees();
            away.RemoveAll(companion => Party.Members.All(member => member.Id != companion.MemberId));
            if (Party.CanStartBattle) return true;

            AbortBattle();
            Current.Message = "欠薪队员已经离队，队伍已无人能够迎战。";
            return false;
        }

        public void BindBattle(string requestId)
        {
            if (Current?.Battle is { Settled: false } battle)
                battle.RequestId = requestId;
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
            bool fullFormation = HasVisitedTown && Current.Event != AdventureEventKind.AscensionTrial;
            int count = Rules.Positions;
            if (!fullFormation)
            {
                int allies = Party.CaptureFormation(Side.Party).Count;
                count = rank == EnemyRank.Normal ? Math.Max(1, allies) :
                    Current.Event == AdventureEventKind.AscensionTrial ? 1 : Math.Clamp(allies - 1, 1, 3);
            }
            var groups = formations?.Where(group => group.Rank == rank &&
                (fullFormation ? group.Members.Sum(member => member.PositionSlots) == Rules.Positions :
                    rank != EnemyRank.Normal || group.Members.Count >= count)).ToArray();
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
            ticket.Ratings = result.Ratings;
            bool victory = result.Outcome == BattleOutcome.Victory;
            if (victory && ticket.Kind == NodeKind.Boss)
            {
                var spoils = BossSpoils.Roll(ticket.Enemies[0].Character.Id, bossCatalogue, random);
                Current.Rewards = spoils.Choices;
                ticket.BonusEquipment = spoils.Equipment;
            }
            ticket.Settled = true;
            RecordFloorBattle();
            gold = RollBattleGold(result.Outcome, ticket.Kind, random, Floor);
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
            var bloom = Party.FinishSeedBattle(result, gardener);
            ticket.BloomWearer = bloom.Wearer;
            ticket.BloomWaitingForRoom = bloom.WaitingForRoom;
            if (bloom.Wearer != null)
            {
                Party.RememberRoadEvent(EmployeeThought.Traveler, gardener.Name, exceptCharacter: gardener.Id);
                Current.Message += "种子开花了，墓园花匠加入了讨伐队。";
            }
            if (bloom.WaitingForRoom)
                Current.Message += "种子已经成熟。讨伐队有空位后，再装备它赢下一场战斗即可招募花匠。";
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

        public static int RollBattleGold(BattleOutcome outcome, NodeKind kind, Random random, int floor)
        {
            if (outcome != BattleOutcome.Victory) return 0;
            int region = (Math.Max(1, floor) - 1) % CampaignFloors + 1;
            var (minimum, maximum) = (region, kind) switch
            {
                (1, NodeKind.NormalBattle) => (4, 6),
                (1, NodeKind.EliteBattle) => (6, 9),
                (1, NodeKind.Boss) => (10, 15),
                (2, NodeKind.NormalBattle) => (5, 7),
                (2, NodeKind.EliteBattle) => (7, 10),
                (2, NodeKind.Boss) => (12, 17),
                (3, NodeKind.NormalBattle) => (7, 10),
                (3, NodeKind.EliteBattle) => (10, 15),
                (3, NodeKind.Boss) => (15, 20),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected a battle kind.")
            };
            return random.Next(minimum, maximum + 1);
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

        private void RecordFloorBattle() =>
            floorBattles.Add(new FloorBattleRating(Stage, Current.Kind, Current.Battle.Ratings));

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
            GrantChestBonus();
            return true;
        }

        private int GrantChestBonus()
        {
            int gold = Math.Min(random.Next(20, 26), int.MaxValue - Party.Gold);
            Party.AddGold(gold);
            return gold;
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
            return member == null || member.IsAway || member.Health <= 0 ? Array.Empty<CharacterDefinition>() : roster.Where(c =>
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
            Current.NeedsEvolution = false;
            Finish($"试炼完成，队员升阶为{form.Name}，生命值已回满。");
            return true;
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
            FinishCamp($"篝火旁，队员升阶为{form.Name}，生命值已回满。");
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

        private static void RollTownRecruitSpecials(AdventureVisit visit, Random random)
        {
            if (visit.Recruits.Count == 0) return;
            int halfPriceIndex = random.Next(visit.Recruits.Count);
            for (int i = 0; i < visit.Recruits.Count; i++)
            {
                string id = visit.Recruits[i].Id;
                if (i == halfPriceIndex) visit.RecruitSpecials[id] = 50;
                else if (random.Next(100) < 3) visit.RecruitSpecials[id] = 10;
            }
        }

        public int TownRecruitPricePercent(CharacterDefinition candidate) =>
            Current != null && Current.RecruitSpecials.TryGetValue(candidate.Id, out int percent) ? percent : 100;

        public int TownRecruitPrice(CharacterDefinition candidate) =>
            (int)(((long)Party.SigningPrice(candidate) * TownRecruitPricePercent(candidate) + 99) / 100);

        public void LandRecruitmentWheel()
        {
            if (AtRecruitmentWheel) Current.WheelLanded = true;
        }

        private bool CanUseRecruitmentWheel => CanUseTown(TownService.Shop) ||
            (At(AdventureEventKind.RecruitmentWheel) && Current.WheelLanded);

        public bool DrawShopRecruit()
        {
            if (!CanUseRecruitmentWheel || !Current.ShopRecruitment.TryDraw(random, price => Party.TrySpendGold(price, "招募转盘"))) return false;
            shopRecruitPrice = Current.ShopRecruitment.NextPrice;
            if (!AtRecruitmentWheel) Current.UsedServices.Add(TownService.Shop);
            return true;
        }

        public bool SignShopRecruit(string characterId, string replacedMemberId = null, int days = 12,
            PerformanceBonuses terms = default, long? signingFee = null)
        {
            if (!CanUseRecruitmentWheel) return false;
            var candidate = Current.ShopRecruitment.Candidates.FirstOrDefault(c => c.Id == characterId);
            var draft = Party.ContractsEnabled ? Party.CandidateContract(candidate, days, terms, signingFee) : null;
            long release = replacedMemberId == null ? 0 : Party.TransferExitCostFor(replacedMemberId);
            if ((Party.ContractsEnabled && draft == null) || (draft?.DueAtSigning ?? 0) + release > Party.Gold ||
                !Current.ShopRecruitment.TrySign(characterId, replacedMemberId)) return false;
            if (draft != null)
            {
                Party.PaySigningWage(candidate.Name, draft);
                Party.SignNewHire(characterId, draft);
            }
            if (AtRecruitmentWheel) Finish($"{candidate.Name}加入讨伐队。");
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

        private IReadOnlyList<AdventureShopOffer> CreateStock() =>
            AdventureShopOffer.Draw(shopCatalogue, random, () => Party.HasShopDiscount);

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
            if (Current == null || !Current.Resolved ||
                AtCampaignFinalStage && Current.Kind == NodeKind.Boss && !CampaignWon)
                return false;
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
            seenEvents.RemoveWhere(kind => GraveStories.Contains(kind) || CapitalStories.Contains(kind));
            Floor++; Stage = 1; FloorCleared = Failed = false; eventsTaken = elitesTaken = 0;
            floorBattles.Clear();
            FirstRatedStage = 1;
            RouteCurrent = default;
            RoutePrevious = RouteDestination = null;
            routeOptions = null;
            Route = new DockRoute(random);
            if (InGraveyard && !IsInspection) PlanGraveSeed(firstStage: 2);
            return true;
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
                    int gold = Math.Min(random.Next(15, 21), int.MaxValue - Party.Gold);
                    Party.AddGold(gold);
                    Party.RememberPersonalEvent(companion.MemberId, EmployeeThought.ChestReturn, $"{gold} 金币");
                    ReturnMessage += $"{name}带着 {gold} 金币归队。 ";
                }
                int bonus = GrantChestBonus();
                if (bonus > 0) ReturnMessage += $"开箱额外获得 {bonus} 金币。 ";
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
