using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public sealed class TownCommission
    {
        private enum Goal { Wins, Kills, Casts, Healing, Damage, EliteWins, Shield }
        private readonly Goal goal;
        public string Id { get; }
        public string Title { get; }
        public int Difficulty { get; }
        public string Rank => Difficulty switch { 0 => "简单", 1 => "普通", _ => "困难" };
        public string Objective { get; }
        public int Target { get; }
        public int Gold { get; }

        private TownCommission(string id, string title, int difficulty, Goal goal, string objective, int target, int gold)
        {
            Id = id; Title = title; Difficulty = difficulty; this.goal = goal;
            Objective = objective; Target = target; Gold = gold;
        }

        private static readonly TownCommission[] jobs = {
            new("patrol", "商队护送", 0, Goal.Wins, "赢得 2 场战斗", 2, 22),
            new("vermin", "清理沿途威胁", 0, Goal.Kills, "击败 3 名敌人", 3, 18),
            new("drill", "术式试演", 0, Goal.Casts, "队员施放 3 次已学技能", 3, 20),
            new("clinic", "战地救护", 1, Goal.Healing, "为队员实际恢复 18 点生命", 18, 34),
            new("breach", "突破封锁", 1, Goal.Damage, "队员对敌方造成 48 点生命伤害", 48, 32),
            new("bulwark", "护盾验收", 1, Goal.Shield, "用护盾抵挡 15 点敌方伤害", 15, 36),
            new("hunt", "危险通缉令", 2, Goal.EliteWins, "赢得 2 场精英或首领战", 2, 64),
            new("clearance", "清剿行动", 2, Goal.Kills, "击败 14 名敌人", 14, 55),
            new("campaign", "远征护航", 2, Goal.Wins, "赢得 5 场战斗", 5, 58)
        };

        internal static TownCommission Find(string id) => Array.Find(jobs, job => job.Id == id);
        internal static TownCommission[] Draw(Random random)
        {
            var board = new TownCommission[3];
            var used = new HashSet<Goal>();
            for (int rank = 0; rank < board.Length; rank++)
            {
                var options = jobs.Where(job => job.Difficulty == rank && !used.Contains(job.goal)).ToArray();
                board[rank] = options[random.Next(options.Length)];
                used.Add(board[rank].goal);
            }
            return board;
        }

        internal static TownCommission[] RestoreBoard(string[] ids, Random random)
        {
            if (ids?.Length == 3)
            {
                var board = Array.ConvertAll(ids, Find);
                if (board.Select((job, i) => job?.Difficulty == i).All(valid => valid) &&
                    board.Select(job => job.goal).Distinct().Count() == 3) return board;
            }
            return Draw(random);
        }

        internal long Credit(BattleResult result, AdventureBattle battle)
        {
            if (goal == Goal.Wins) return 1;
            if (goal == Goal.EliteWins) return battle.Kind is NodeKind.EliteBattle or NodeKind.Boss ? 1 : 0;
            var enemies = battle.Enemies.Select(unit => unit.Id).ToHashSet();
            bool Ally(string id) => id != null && result.HealthById.ContainsKey(id) && !enemies.Contains(id);
            return goal switch {
                Goal.Kills => result.Events.Count(e => e.Kind == BattleEventKind.Defeated && enemies.Contains(e.TargetId)),
                Goal.Casts => result.Events.Count(e => Ally(e.ActorId) && e.Kind == BattleEventKind.Action && e.ActorEnergy.HasValue && e.SkillBookId != null),
                Goal.Healing => result.Events.Where(e => Ally(e.ActorId) && Ally(e.TargetId) && e.ActionKind == ActionKind.Heal).Sum(e => (long)Math.Max(0, e.Amount)),
                Goal.Damage => result.Events.Where(e => Ally(e.ActorId) && enemies.Contains(e.TargetId)).Sum(e => e.DamageTaken.Sum(hit => (long)hit.Amount)),
                Goal.Shield => result.Events.Where(e => enemies.Contains(e.ActorId) && Ally(e.TargetId)).Sum(e => (long)e.ShieldAbsorbed),
                _ => 0
            };
        }
    }
}
