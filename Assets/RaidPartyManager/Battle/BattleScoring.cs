using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        // Poison and shields can outlive their source. Retain the grant order so decay,
        // absorption and dispels remove exactly the layers that are no longer in play.
        private sealed class LayerSources
        {
            private readonly List<(Unit Source, int Amount)> grants = new();
            public void Clear() => grants.Clear();

            public void Add(Unit source, int amount)
            {
                if (amount <= 0) return;
                if (grants.Count > 0 && grants[^1].Source == source)
                    grants[^1] = (source, grants[^1].Amount + amount);
                else
                    grants.Add((source, amount));
            }

            public void Spend(int amount, Unit protectedAlly = null)
            {
                int i = 0;
                while (amount > 0 && i < grants.Count)
                {
                    var (source, available) = grants[i];
                    int used = Math.Min(amount, available);
                    if (protectedAlly != null && source != null && source != protectedAlly &&
                        source.Input.Side == protectedAlly.Input.Side)
                        source.Rating.AllyShielding += used;
                    amount -= used;
                    if (used == available) i++;
                    else grants[i] = (source, available - used);
                }
                if (i > 0) grants.RemoveRange(0, i);
            }

            // Include unattributed starting poison in the denominator. Fix ownership before
            // defeat reactions can move stacks to a new target or red cord shares the hit.
            public (Unit Source, int Amount)[] PoisonShares() => grants.GroupBy(grant => grant.Source)
                .Select(group => (group.Key, group.Sum(grant => grant.Amount))).ToArray();
        }

        private static void ScoreHit(Unit actor, Unit target, int lost, int absorbed, (Unit Source, int Amount)[] poison)
        {
            if (lost == 0 && absorbed == 0) return;
            Unit killer = actor;
            if (poison == null || poison.Length == 0)
                RecordHitShare(actor, target, lost, absorbed);
            else if (poison.Length == 1)
            {
                killer = poison[0].Source;
                RecordHitShare(killer, target, lost, absorbed, poison: true);
            }
            else
            {
                int layers = poison.Sum(share => share.Amount);
                var hp = SplitPoisonDamage(lost, poison, layers);
                var shields = SplitPoisonDamage(absorbed, poison, layers);
                for (int i = 0; i < poison.Length; i++)
                    RecordHitShare(poison[i].Source, target, hp[i], shields[i], poison: true);
                if (target.Health == 0) killer = poison.OrderByDescending(share => share.Amount).First().Source;
            }
            if (target.Health == 0 && lost > 0 && killer != null && killer.Input.Side != target.Input.Side)
                killer.Rating.Kills++;
        }

        private static void RecordHitShare(Unit source, Unit target, int lost, int absorbed, bool poison = false)
        {
            bool hostile = source == null || source.Input.Side != target.Input.Side;
            target.ShieldSources.Spend(absorbed, hostile ? target : null);
            if (!hostile) return;
            target.Rating.DamageTaken += lost;
            target.Rating.ShieldAbsorbed += absorbed;
            if (source == null) return;
            source.Rating.HealthDamage += lost;
            source.Rating.ShieldDamage += absorbed;
            if (poison) source.Rating.PoisonDamage += (long)lost + absorbed;
        }

        // Award indivisible points to the largest remainders, in grant order for ties.
        private static int[] SplitPoisonDamage(int damage, (Unit Source, int Amount)[] shares, int layers)
        {
            var awarded = new int[shares.Length];
            if (damage == 0) return awarded;
            var remainders = new long[shares.Length];
            int remaining = damage;
            for (int i = 0; i < shares.Length; i++)
            {
                long weighted = (long)damage * shares[i].Amount;
                awarded[i] = (int)(weighted / layers);
                remainders[i] = weighted % layers;
                remaining -= awarded[i];
            }
            if (remaining > 0)
                foreach (int i in Enumerable.Range(0, shares.Length).OrderByDescending(i => remainders[i]).Take(remaining))
                    awarded[i]++;
            return awarded;
        }

        private static void ScoreStatus(Unit actor, Unit target, StatusKind status, int layers)
        {
            if (status == StatusKind.Poison) target.PoisonSources.Add(actor, layers);
            if (actor == null || layers <= 0) return;
            bool negative = StatusRules.For(status).Negative;
            if (negative && actor.Input.Side != target.Input.Side)
                actor.Rating.DebuffLayers += layers;
            else if (!negative && actor != target && actor.Input.Side == target.Input.Side)
                actor.Rating.BuffLayers += layers;
        }

        private BattleRating[] BuildRatings() => BattleRating.ScoreBattle(units.Select(unit =>
        {
            var row = unit.Rating;
            row.UnitId = unit.Input.Id;
            row.CharacterId = unit.Input.Character.Id;
            row.Name = unit.Input.Character.Name;
            row.Side = unit.Input.Side;
            row.Position = unit.Input.Position;
            row.Fallen = unit.Health <= 0 && !row.Retreated;
            return row;
        }));
    }
}
