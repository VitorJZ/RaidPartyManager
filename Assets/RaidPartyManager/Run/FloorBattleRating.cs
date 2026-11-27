using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    [Serializable]
    public struct FloorMemberRating
    {
        public string UnitId, Name;
        public int ScoreTenths;
    }

    [Serializable]
    public sealed class FloorBattleRating
    {
        public int Stage;
        public NodeKind Kind;
        public FloorMemberRating[] Members;

        internal FloorBattleRating(int stage, NodeKind kind, IEnumerable<BattleRating> ratings)
        {
            Stage = stage;
            Kind = kind;
            Members = ratings.Where(r => r.Side == Side.Party).Select(r => new FloorMemberRating
                { UnitId = r.UnitId, Name = r.Name, ScoreTenths = r.ScoreTenths }).ToArray();
        }

        // A missing score (an absent member or an older save) is distinct from a deployed unit earning zero.
        public int? ScoreFor(string unitId)
        {
            if (unitId == null) return BattleRating.AverageTenths(Members.Select(member => (int?)member.ScoreTenths));
            foreach (var member in Members)
                if (member.UnitId == unitId) return member.ScoreTenths;
            return null;
        }

    }
}
