using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public enum NodeKind { Event, Camp, Shop, Town, NormalBattle, EliteBattle, Boss, TreasureChest }
    public enum AdventureEventKind { Investor, Ambush, LostTraveler, FoundCoins, HatTrick, Forge, LockedChest, AscensionTrial, Alchemist }
    public enum TownService { Recruit, Shop, Camp, Quest }

    public sealed class RouteNode
    {
        public int Number { get; }
        public NodeKind Kind { get; internal set; }
        public NodeKind? Replaces { get; internal set; }
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
            int shop = Pick(2, 9, chest, earlyCamp, lateCamp, elite);
            nodes[shop - 1].Kind = NodeKind.Shop;
            int secondNormal = Pick(3, 9, chest, earlyCamp, lateCamp, elite, shop);

            int[] services = { earlyCamp, lateCamp, shop };
            var townSites = services.Where(number => number >= 6).ToArray();
            if (townSites.Length > 0 && random.NextDouble() < 0.35)
            {
                var town = nodes[townSites[random.Next(townSites.Length)] - 1];
                town.Replaces = town.Kind;
                town.Kind = NodeKind.Town;
            }

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

        internal DockRoute(RouteCheckpointNode[] saved)
        {
            Nodes = Array.AsReadOnly(saved.Select((node, index) => new RouteNode(index + 1, node.Kind)
            {
                IsFork = node.Fork,
                Alternative = node.AlternativePlusOne == 0 ? (NodeKind?)null : (NodeKind)(node.AlternativePlusOne - 1),
                Replaces = node.Replaces < 0 ? (NodeKind?)null : (NodeKind)node.Replaces
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
        public HashSet<TownService> UsedServices { get; } = new HashSet<TownService>();
        public IReadOnlyList<AdventureShopOffer> Stock { get; internal set; } = Array.Empty<AdventureShopOffer>();
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

    public sealed class AdventureRun
    {
        private Random random;
        private readonly CharacterDefinition[] characters;
        private readonly EquipmentDefinition[] equipment;
        private readonly EquipmentDefinition[] battleLoot;
        private readonly EquipmentDefinition[] treasureLoot;
        private readonly HashSet<int> investorFloors = new HashSet<int>();
        private readonly List<AwayCompanion> away = new List<AwayCompanion>();
        private int eventsTaken, elitesTaken;
        private IReadOnlyList<NodeKind> routeOptions;
        private AdventureCheckpoint nodeCheckpoint;

        public PartyRun Party { get; }
        public bool IsInspection { get; }
        public int Floor { get; private set; } = 1;
        public int Stage { get; private set; } = 1;
        public int CompletedNodes { get; private set; }
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
        public bool QuestActive { get; private set; }
        public int QuestWins { get; private set; }

        public AdventureRun(PartyRun party, IEnumerable<CharacterDefinition> characters,
            IEnumerable<EquipmentDefinition> equipment, bool inspection, Random random = null)
        {
            Party = party ?? throw new ArgumentNullException(nameof(party));
            this.characters = characters.Where(c => c.RosterNumber > 0).ToArray();
            var items = equipment.GroupBy(e => e.Id).Select(g => g.First()).ToArray();
            this.equipment = items.Where(e => e.Tier >= 0 && e.Price > 0).ToArray();
            battleLoot = items.Where(e => e.CanDropInBattle).ToArray();
            treasureLoot = items.Where(e => !e.IsPotion && e.Tier is >= 1 and <= 3).ToArray();
            Party.RegisterRewardCatalog(this.equipment);
            this.random = random ?? new Random();
            IsInspection = inspection;
            Route = new DockRoute(this.random);
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
                InvestorFloors = investorFloors.ToArray(), FloorCleared = FloorCleared, Failed = Failed,
                QuestActive = QuestActive, QuestWins = QuestWins, ReturnMessage = ReturnMessage,
                Nodes = Route.Nodes.Select(node => new RouteCheckpointNode
                {
                    Kind = node.Kind, Fork = node.IsFork, Replaces = node.Replaces.HasValue ? (int)node.Replaces.Value : -1,
                    AlternativePlusOne = node.Alternative.HasValue ? (int)node.Alternative.Value + 1 : 0
                }).ToArray(),
                Options = routeOptions?.ToArray(), Current = new CheckpointStop(RouteCurrent),
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
                saved.Choice < -1 || (saved.Choice >= 0 && (saved.Options == null || saved.Choice >= saved.Options.Length)) ||
                ((saved.FinishVisit || saved.HasBattleReward) && saved.Choice < 0))
                throw new ArgumentException("Invalid adventure checkpoint.");

            var catalog = characters.ToArray();
            var items = equipment.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
            var party = PartyRun.RestoreCheckpoint(saved.Party, catalog.ToDictionary(character => character.Id), items);
            var run = new AdventureRun(party, catalog, items.Values, false, new Random(saved.Seed))
            {
                Floor = saved.Floor, Stage = saved.Stage, CompletedNodes = saved.CompletedNodes, eventsTaken = saved.EventsTaken,
                elitesTaken = saved.ElitesTaken, Investment = saved.Investment, InvestmentPaid = saved.InvestmentPaid,
                FloorCleared = saved.FloorCleared, Failed = saved.Failed, QuestActive = saved.QuestActive, QuestWins = saved.QuestWins,
                ReturnMessage = saved.ReturnMessage ?? "", Route = new DockRoute(saved.Nodes),
                RouteCurrent = saved.Current.Restore(), RoutePrevious = saved.HasPrevious ? saved.Previous.Restore() : (RouteStop?)null,
                routeOptions = saved.Options is { Length: > 0 } ? saved.Options : null,
                random = new Random(saved.Seed)
            };
            run.investorFloors.UnionWith(saved.InvestorFloors ?? Array.Empty<int>());
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
                run.Current = new AdventureVisit(run.RouteDestination.Value.Kind.Value,
                    reward.Event < 0 ? (AdventureEventKind?)null : (AdventureEventKind)reward.Event)
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
            return run;
        }

        public IReadOnlyList<NodeKind> RouteOptions => routeOptions ??=
            Route.Options(Stage, eventsTaken, elitesTaken, random);

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
            if (IsInspection || Current != null || Failed || FloorCleared || choice < 0 || choice >= RouteOptions.Count ||
                (SuspendedChoice.HasValue && choice != SuspendedChoice.Value))
                return false;
            var destination = RouteChoice(choice);
            var kind = destination.Kind.Value;
            if ((kind is NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss) && !Party.CanStartBattle)
                return false;

            // Keep the generated node stable when a checkpoint is reentered.
            int seed = SuspendedChoice.HasValue ? nodeCheckpoint.Seed : random.Next();
            nodeCheckpoint = CaptureCheckpoint(seed, choice);
            random = new Random(seed);
            SuspendedChoice = null;
            RouteDestination = destination;
            if (kind == NodeKind.Event) eventsTaken++;
            if (kind == NodeKind.EliteBattle) elitesTaken++;
            Open(kind, kind == NodeKind.Event ? RollEvent() : (AdventureEventKind?)null);
            return true;
        }

        public bool Inspect(NodeKind kind, AdventureEventKind? eventKind = null)
        {
            if (!IsInspection || (kind == NodeKind.Event && !eventKind.HasValue) ||
                Current?.Battle != null && !Current.Battle.Settled)
                return false;
            if (Current?.Resolved == true) CloseVisit();
            Current = null;
            Open(kind, eventKind);
            return true;
        }

        private void Open(NodeKind kind, AdventureEventKind? eventKind)
        {
            var visit = Current = new AdventureVisit(kind, eventKind);
            if (eventKind == AdventureEventKind.Investor)
            {
                visit.InvestorAvailable = !InvestmentPaid && !InvestorVisitedThisFloor;
                investorFloors.Add(Floor);
            }
            if (kind == NodeKind.TreasureChest)
                visit.Rewards = treasureLoot.OrderBy(_ => random.Next()).Take(3).ToArray();
            if (kind == NodeKind.Shop || kind == NodeKind.Town)
                visit.Stock = CreateStock();
            if (kind is NodeKind.Camp or NodeKind.Town)
                visit.Recruits = RecruitCandidates().OrderBy(_ => random.Next()).Take(2).ToArray();
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
            var list = new List<AdventureEventKind> { AdventureEventKind.Ambush };
            if (!InvestmentPaid && !InvestorVisitedThisFloor && (Floor > 1 || Stage >= 4))
                list.Add(AdventureEventKind.Investor);
            if (Stage <= 3) list.AddRange(new[] { AdventureEventKind.LostTraveler, AdventureEventKind.FoundCoins, AdventureEventKind.HatTrick });
            else if (Stage <= 6) list.AddRange(new[] { AdventureEventKind.Forge, AdventureEventKind.LockedChest });
            else if (Stage <= 9) list.AddRange(new[] { AdventureEventKind.AscensionTrial, AdventureEventKind.Alchemist });
            return list;
        }

        private AdventureEventKind RollEvent()
        {
            var choices = AvailableEvents();
            return choices[random.Next(choices.Count)];
        }

        public bool Donate(int amount)
        {
            if (!At(AdventureEventKind.Investor) || InvestmentPaid || !Current.InvestorAvailable || amount < 1 ||
                !Party.TrySpendGold(amount)) return false;
            investorFloors.Add(Floor);
            Investment += amount;
            if (Investment >= 50)
            {
                var first = PickItem(1);
                var second = PickItem(1, new[] { first.Id });
                Party.AddEquipment(first); Party.AddEquipment(second);
                InvestmentPaid = true;
                Finish($"创业者在账本上划下最后一笔。临走前，他送给你{first.Name}和{second.Name}。");
            }
            else Finish($"创业者接过 {amount} 枚金币，把你的名字记在账本上。“改日再来看看船吧。”");
            return true;
        }

        public bool CollectCoins()
        {
            if (!At(AdventureEventKind.FoundCoins)) return false;
            int gold = random.Next(10, 21); Party.AddGold(gold);
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

        public bool ChooseTownRecruit(string characterId)
        {
            if (!CanUseTown(TownService.Recruit) || Current.PendingRecruit != null) return false;
            Current.PendingRecruit = Current.Recruits.FirstOrDefault(c => c.Id == characterId);
            if (Current.PendingRecruit == null) return false;
            return CompleteRecruit(null);
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
                Current.UsedServices.Add(TownService.Recruit);
                Current.Message = $"{recruit.Name}加入了讨伐队，原队员的装备留在背包中。";
            }
            else Finish($"{recruit.Name}拍了拍旅途的尘土，加入了讨伐队。");
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
            Party.AddEquipment(output); Finish($"两件旧物在炉火中化为一体。你得到了{output.Name}，已放入背包。"); return true;
        }

        public bool LeaveCompanion(string memberId)
        {
            if (!At(AdventureEventKind.LockedChest)) return false;
            var member = Party.Members.FirstOrDefault(m => m.Id == memberId);
            if (member?.Position == null || !Party.TrySendAway(memberId)) return false;
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
                Finish($"你找到了装着{reward.Name}的帽子！物品已放入背包。");
            }
            else Finish("你盯错了帽子。戏法师掀开所有帽子，让你看清了每顶帽子下的物品。");
            return true;
        }

        public bool ChoosePotion(int index)
        {
            if (!At(AdventureEventKind.Alchemist) || index < 0 || index >= Current.Potions.Count) return false;
            var potion = Current.Potions[index]; Party.AddEquipment(potion);
            Finish($"炼药师这才给瓶子贴上标签。你把{potion.Name}收进了背包。"); return true;
        }

        public bool TakeTrialGift()
        {
            if (!At(AdventureEventKind.AscensionTrial) || Current.TrialGift == null) return false;
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

        public IReadOnlyList<Combatant> PrepareEnemies(IReadOnlyList<CharacterDefinition> roster)
        {
            var ticket = Current?.Battle ?? throw new InvalidOperationException("No encounter selected.");
            if (ticket.Enemies != null) return ticket.Enemies;
            EnemyRank RankOf(CharacterDefinition character) => character.Rank != EnemyRank.Unspecified
                ? character.Rank
                : character.Trait is CharacterTrait.EnemyBell or CharacterTrait.EnemyRearAmbush or CharacterTrait.EnemyResolve
                    ? EnemyRank.Elite : EnemyRank.Normal;
            var normal = roster.Where(c => RankOf(c) == EnemyRank.Normal).OrderBy(_ => random.Next()).ToArray();
            var selected = new List<(CharacterDefinition Character, bool Elite)>();
            if (ticket.Kind != NodeKind.NormalBattle)
            {
                var rank = ticket.Kind == NodeKind.Boss ? EnemyRank.Boss : EnemyRank.Elite;
                var candidates = roster.Where(c => RankOf(c) == rank).ToArray();
                var primary = candidates.Length > 0 ? candidates[random.Next(candidates.Length)] :
                    roster.FirstOrDefault(c => c.Trait == CharacterTrait.EnemyBell) ?? roster.First();
                selected.Add((primary, true));
            }
            int allies = Party.CaptureFormation(Side.Party).Count;
            int count = ticket.Kind == NodeKind.NormalBattle ? Math.Max(1, allies) :
                Current.Event == AdventureEventKind.AscensionTrial ? 1 : Math.Min(3, Math.Max(1, allies - 1));
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
            return true;
        }

        public bool SettleBattle(BattleResult result, out int gold)
        {
            gold = 0;
            var ticket = Current?.Battle;
            if (ticket == null || ticket.Settled || result == null || ticket.RequestId != result.Id || ticket.Enemies == null ||
                ticket.Enemies.Any(e => !result.HealthById.ContainsKey(e.Id))) return false;
            ticket.Settled = true;
            bool victory = result.Outcome == BattleOutcome.Victory;
            gold = Current.Event == AdventureEventKind.Ambush ? (victory ? random.Next(5, 11) : 0) :
                RollKillGold(result, ticket.Enemies, random, ticket.EliteIds);
            Current.Message = $"战斗获得 {gold} 金币。";
            if (!victory)
            {
                Current.Resolved = true;
                if (!IsInspection) Failed = true;
                Current.Message += "讨伐暂时告一段落。"; return true;
            }
            if (QuestActive) QuestWins++;
            if (Current.Event == AdventureEventKind.AscensionTrial)
            {
                Current.NeedsEvolution = EvolutionCandidates.Count > 0;
                if (!Current.NeedsEvolution) Current.Rewards = new[] { PickItem(1) };
            }
            else if (Current.Event != AdventureEventKind.Ambush)
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
                .Sum(enemy => elites?.Contains(enemy.Id) == true ? random.Next(8, 13) : random.Next(3, 6));
        }

        private EquipmentDefinition RollBattleReward(bool elite, IEnumerable<string> excluded = null)
        {
            double roll = random.NextDouble();
            int tier = roll < (elite ? 0.5 : 0.3) ? 1 : random.Next(2, 4);
            return PickItem(battleLoot.Where(item => item.Tier == tier), excluded);
        }

        public BattleRewards RecordBattleRewards(int gold, IEnumerable<EquipmentDefinition> items)
        {
            if (Current?.Battle is not { Settled: true } ticket || ticket.Reward != null)
                throw new InvalidOperationException("Rewards need a newly settled battle.");
            return ticket.Reward = new BattleRewards(gold, items);
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
                member.Character.EvolutionIds.Contains(c.Id) && !Party.Members.Any(other => other.Character.Id == c.Id)).ToArray();
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

        public IReadOnlyList<CharacterDefinition> CampRecruits => CanUseCamp
            ? Current.Recruits.Intersect(RecruitCandidates()).ToArray() : Array.Empty<CharacterDefinition>();

        public bool RecruitAtCamp(string characterId, string replaceMemberId = null)
        {
            var recruit = CampRecruits.FirstOrDefault(candidate => candidate.Id == characterId);
            if (recruit == null) return false;
            bool recruited = replaceMemberId == null ? Party.TryRecruit(recruit) : Party.TryReplaceMember(replaceMemberId, recruit);
            if (!recruited) return false;
            FinishCamp($"{recruit.Name}加入了讨伐队。");
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
            Current.UsedServices.Count < 2 && !Current.UsedServices.Contains(service);

        public bool TownQuest()
        {
            if (!CanUseTown(TownService.Quest)) return false;
            if (!QuestActive)
            {
                QuestActive = true; QuestWins = 0; Current.Message = "接下码头委托：赢得 2 场战斗，回来领取 15 金币。";
            }
            else
            {
                if (QuestWins < 2) return false;
                Party.AddGold(15); QuestActive = false; QuestWins = 0; Current.Message = "委托完成，你领取了 15 金币。";
            }
            Current.UsedServices.Add(TownService.Quest); return true;
        }

        public bool Buy(int index)
        {
            if (Current == null || Current.Resolved || index < 0 || index >= Current.Stock.Count ||
                Current.Kind != NodeKind.Shop && !(CanUseTown(TownService.Shop) || Current.Kind == NodeKind.Town && Current.UsedServices.Contains(TownService.Shop)))
                return false;
            var offer = Current.Stock[index];
            if (offer.Sold || !Party.TrySpendGold(offer.Price)) return false;
            Party.AddEquipment(offer.Item); offer.Sold = true;
            if (Current.Kind == NodeKind.Town) Current.UsedServices.Add(TownService.Shop);
            return true;
        }

        private IReadOnlyList<AdventureShopOffer> CreateStock()
        {
            var choices = equipment.Where(e => e.Tier == 1).OrderBy(_ => random.Next()).Take(2)
                .Concat(equipment.Where(e => e.Tier is 2 or 3).OrderBy(_ => random.Next()).Take(2)).OrderBy(_ => random.Next());
            bool discount = Party.Members.Any(m => !m.IsAway && m.Health > 0 && m.Character.Trait == CharacterTrait.ShopDiscount);
            return choices.Select(e => new AdventureShopOffer(e, (int)Math.Ceiling(
                Math.Ceiling(e.Price * (0.7 + random.NextDouble() * 0.6)) * (discount ? 0.5 : 1)))).ToArray();
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
                else Stage++;
            }
            nodeCheckpoint = null;
            routeOptions = null;
        }

        public void AdvanceInspection()
        {
            if (!IsInspection || Current?.Battle != null && !Current.Battle.Settled) return;
            if (Current != null) { Leave(); return; }
            CompletedNodes++; Stage = Stage == DockRoute.Stages ? 1 : Stage + 1; ProcessReturns();
        }

        public bool NextFloor()
        {
            if (!IsInspection && !FloorCleared || Current != null) return false;
            Floor++; Stage = 1; FloorCleared = Failed = false; eventsTaken = elitesTaken = 0;
            RouteCurrent = default;
            RoutePrevious = RouteDestination = null;
            routeOptions = null; Route = new DockRoute(random); return true;
        }

        private void ProcessReturns()
        {
            ReturnMessage = "";
            foreach (var companion in away.Where(a => a.ReturnAt <= CompletedNodes).ToArray())
            {
                if (!Party.TryReturnMember(companion.MemberId, companion.Position)) continue;
                away.Remove(companion);
                string name = Party.Members.First(m => m.Id == companion.MemberId).Character.Name;
                double roll = random.NextDouble();
                if (roll < 0.7)
                {
                    var item = PickItem(roll < 0.6 ? 2 : 1); Party.AddEquipment(item);
                    ReturnMessage += $"{name}带着{item.Name}归队。 ";
                }
                else
                {
                    int gold = random.Next(15, 21); Party.AddGold(gold);
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
