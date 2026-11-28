using System;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    [Serializable]
    public sealed class AdventureCheckpoint
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public PartyCheckpoint Party;
        public int Seed;
        public int Floor;
        public int Stage;
        public int FirstRatedStage;
        public FloorBattleRating[] FloorBattles;
        public int CompletedNodes;
        public int EventsTaken;
        public int GraveSeedStage;
        public int ElitesTaken;
        public int Investment;
        public bool InvestmentPaid;
        public bool HasVisitedTown;
        public int[] InvestorFloors;
        public AdventureEventKind[] SeenEvents;
        public bool FloorCleared;
        public bool Failed;
        public bool QuestActive;
        public int QuestWins; // Progress in original patrol-only saves.
        public string CommissionId;
        public int QuestProgress, CommissionGold;
        public string ReturnMessage;
        public RouteCheckpointNode[] Nodes;
        public NodeKind[] Options;
        public NodeKind[] FollowingOptions;
        public CheckpointStop Current;
        public CheckpointStop Previous;
        public bool HasPrevious;
        public int Choice = -1;
        public bool FinishVisit;
        public bool HasBattleReward;
        // Legacy final boss saves already advanced the day before taking the snapshot.
        public bool HasFinalBossPreparation;
        public string[] PayrollDepartures;
        public BattleRewardCheckpoint BattleReward;
        public bool HasShopVisit;
        public ShopVisitCheckpoint ShopVisit;
        public bool HasWheelVisit;
        public RecruitmentCheckpoint WheelVisit;
        public bool WheelLanded;
        public int ShopRecruitPricePlusOne;
        public CheckpointCompanion[] Away;

        internal bool IncludesTownVisit()
        {
            // Older checkpoints tracked town visits indirectly through route stops, commissions and the dock pact.
            static bool IsTown(NodeKind kind) => kind is NodeKind.Town or NodeKind.Shop;
            if (HasVisitedTown || HasShopVisit || QuestActive || IsTown(Current.Kind) ||
                (HasPrevious && IsTown(Previous.Kind)) || (FinishVisit && IsTown(Options[Choice])) ||
                (SeenEvents != null && Array.IndexOf(SeenEvents, AdventureEventKind.TidePact) >= 0))
                return true;
            for (int i = 0; i < Stage - 1; i++)
                if (!Nodes[i].Fork && IsTown(Nodes[i].Kind)) return true;
            return false;
        }

        internal NodeKind RestoreKind(int stage, NodeKind kind)
        {
            if (kind == NodeKind.Shop) return NodeKind.Town;
            // Preserve a town the player already entered, and restore future camps that the old town roll replaced.
            bool unvisited = stage > Stage || stage == Stage && Choice < 0;
            if (kind == NodeKind.Town && unvisited && stage >= 1 && stage <= Nodes.Length &&
                Nodes[stage - 1].Kind == NodeKind.Town && Nodes[stage - 1].Replaces == (int)NodeKind.Camp)
                return NodeKind.Camp;
            return kind;
        }

        internal NodeKind[] RestoreOptions(int stage, NodeKind[] options) =>
            options is { Length: > 0 } ? Array.ConvertAll(options, kind => RestoreKind(stage, kind)) : null;
    }

    [Serializable]
    public class RecruitmentCheckpoint
    {
        public bool Signed;
        public string[] Slots;
        public string[] Candidates;
    }

    [Serializable]
    public sealed class ShopVisitCheckpoint : RecruitmentCheckpoint
    {
        public bool InTalentMarket;
        public string[] Commissions;
        public ShopOfferCheckpoint[] Stock;
        public TownService[] UsedServices;
        public string[] TownRecruits;
        public int[] TownRecruitPricePercents;
        public string PendingRecruit;
        public int BrokerRefusals, BrokerCounter, BrokerLastAsk;
        public string BrokerMember;
        public bool Rested;
        public string Message;
    }

    [Serializable]
    public struct ShopOfferCheckpoint
    {
        public string Item;
        public int Price;
        public bool Sold;
        public bool UsesRosterDiscount;
    }

    [Serializable]
    public sealed class BattleRewardCheckpoint
    {
        public BattleRating[] Ratings;
        public bool BloomWaitingForRoom;
        public NodeKind Kind;
        public int Event = -1;
        public bool Resolved;
        public string Message;
        public bool NeedsEvolution;
        public string EvolutionMember;
        public int Gold;
        public string[] Items;
        public string[] Choices;
    }

    [Serializable]
    public struct RouteCheckpointNode
    {
        public NodeKind Kind;
        public bool Fork;
        public int Replaces;
        public int AlternativePlusOne;
    }

    [Serializable]
    public struct CheckpointStop
    {
        public int Number;
        public int Lane;
        public NodeKind Kind;

        internal CheckpointStop(RouteStop stop)
        {
            Number = stop.Number;
            Lane = stop.Lane;
            Kind = stop.Kind.GetValueOrDefault();
        }

        internal RouteStop Restore(AdventureCheckpoint saved) => Number == 0 ? default : new RouteStop(Number, Lane, saved.RestoreKind(Number, Kind));
    }

    [Serializable]
    public struct CheckpointCompanion
    {
        public string MemberId;
        public int Position;
        public int ReturnAt;
    }

    [Serializable]
    public struct GoldExpense
    {
        public int Day;
        public string Reason;
        public long Amount;
    }

    [Serializable]
    public sealed class PartyCheckpoint
    {
        public GravePromises GravePromises;
        public int Gold;
        public int Day, TalentSeed;
        public long PayrollDebt;
        public int PayrollPausedDays;
        public GoldExpense[] Expenses;
        public int NextItemId;
        public int NextBookId;
        public int NextEditorItemId;
        public int NextBattleId;
        public CheckpointMember[] Members;
        public CheckpointItem[] Inventory;
    }

    [Serializable]
    public sealed class CheckpointMember
    {
        public string Id;
        public PlayerTrait Trait;
        public EmployeeAttributes EmployeeAttributes;
        public EmployeeCareer EmployeeCareer;
        public CrewContract Contract;
        public CrewContract[] Renewals;
        public long UnpaidWages;
        public string StartingCharacter;
        public int StartingPosition;
        public int StartingHealth;
        public CheckpointStatus[] InitialStatuses;
        public string Character;
        public int Position;
        public int Health;
        public int PermanentAttack, PermanentHealth;
        public CampTraining Training;
        public bool IsAway;
        public CheckpointStatus[] Statuses;
        public string[] Slots;
        public string[] Skills;
    }

    [Serializable]
    public struct CheckpointItem
    {
        public string Id;
        public string Definition;
        public int SeedBloomFromNode;
        // Marks a greenhouse seed in older saves.
        public int BattlesToBloom;
    }

    [Serializable]
    public struct CheckpointStatus
    {
        public StatusKind Kind;
        public int Layers;
    }
}
