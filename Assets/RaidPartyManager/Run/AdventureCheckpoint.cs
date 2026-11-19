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
        public int CompletedNodes;
        public int EventsTaken;
        public int ElitesTaken;
        public int Investment;
        public bool InvestmentPaid;
        public int[] InvestorFloors;
        public bool FloorCleared;
        public bool Failed;
        public bool QuestActive;
        public int QuestWins;
        public string ReturnMessage;
        public RouteCheckpointNode[] Nodes;
        public NodeKind[] Options;
        public CheckpointStop Current;
        public CheckpointStop Previous;
        public bool HasPrevious;
        public int Choice = -1;
        public bool FinishVisit;
        public bool HasBattleReward;
        public BattleRewardCheckpoint BattleReward;
        public CheckpointCompanion[] Away;
    }

    [Serializable]
    public sealed class BattleRewardCheckpoint
    {
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

        internal RouteStop Restore() => Number == 0 ? default : new RouteStop(Number, Lane, Kind);
    }

    [Serializable]
    public struct CheckpointCompanion
    {
        public string MemberId;
        public int Position;
        public int ReturnAt;
    }

    [Serializable]
    public sealed class PartyCheckpoint
    {
        public int Gold;
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
        public string StartingCharacter;
        public int StartingPosition;
        public int StartingHealth;
        public CheckpointStatus[] InitialStatuses;
        public string Character;
        public int Position;
        public int Health;
        public int PermanentAttack;
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
    }

    [Serializable]
    public struct CheckpointStatus
    {
        public StatusKind Kind;
        public int Layers;
    }
}
