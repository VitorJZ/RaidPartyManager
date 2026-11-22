using System;
using System.Linq;
using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private sealed class BossCycle
        {
            internal int Turn, Position, Pressure;
            internal Unit Mark;
        }

        private readonly Dictionary<Unit, BossCycle> cycles = new();

        private bool TryBossTurn(Unit actor, EnemyBehaviorDefinition profile)
        {
            string id = actor.Input.Character.Id;
            if (id is not ("EnemyNamelessKing" or "EnemySepulchralMatron" or "EnemyRoyalEngine" or "EnemyAstralGovernor" or "EnemyCrucibleRegent"))
                return false;
            if (!cycles.TryGetValue(actor, out var cycle)) cycles[actor] = cycle = new BossCycle();
            int turn = cycle.Turn++;
            var basic = profile.Actions[0];
            var special = profile.Actions[1];
            const BattleVisualCue effect = BattleVisualCue.GraveyardSpecial;
            var opponents = TargetableOpponents(actor).OrderBy(u => u.Input.Position).ToArray();
            if (opponents.Length == 0) return true;
            switch (id)
            {
                case "EnemyNamelessKing":
                    if (turn % 3 == 1)
                    {
                        cycle.Mark = opponents.OrderBy(u => (double)u.Health / u.Stats.Health).First();
                        Warn(cycle.Mark, "葬王点名", "下一次行动审判此人。生命过半或持有护盾可获赦免。");
                    }
                    else if (turn % 3 == 2)
                    {
                        var marked = cycle.Mark;
                        ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 0);
                        if (marked.Health > 0 && marked.Health * 2L <= marked.Stats.Health && marked.Shield == 0)
                            ExecuteConfiguredEnemyAction(actor, special, effect, new[] { marked });
                        else Rest("赦免", "判词落空，葬王收剑。");
                    }
                    else Basic();
                    break;
                case "EnemySepulchralMatron":
                    if (turn % 2 == 0) { Basic(); break; }
                    var ward = units.FirstOrDefault(u => u != actor && u.Input.Side == actor.Input.Side && u.Health > 0);
                    if (ward == null)
                        GrantStatus(actor, actor, StatusKind.Exposed, 1, "空柩哀悼", effect);
                    else
                    {
                        ward.Shield += 4;
                        Report(BattleEventKind.Action, actor, ward, "守柩祈祷", ActionKind.Shield, 4, visual: effect);
                        Heal(actor, actor, 4, "护柩回生", BattleEventKind.Effect, visual: effect);
                    }
                    break;
                case "EnemyRoyalEngine":
                    if (turn % 3 == 1)
                    {
                        cycle.Pressure = actor.Health + actor.Shield;
                        Warn(actor, "汽笛鸣响", "下一次行动冲击前两人。在此之前削去至少 6 点护盾或生命可令皇车熄火。");
                    }
                    else if (turn % 3 == 2)
                    {
                        ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 0);
                        if (cycle.Pressure - actor.Health - actor.Shield >= 6) Rest("制动熄火", "冲锋被打断。");
                        else ExecuteConfiguredEnemyAction(actor, special, effect, opponents.Take(2).ToArray());
                    }
                    else Basic();
                    break;
                case "EnemyAstralGovernor":
                    if (turn % 2 == 0)
                    {
                        cycle.Mark = opponents.FirstOrDefault(u => u.Input.Position > cycle.Position) ?? opponents[0];
                        cycle.Position = cycle.Mark.Input.Position;
                        Warn(cycle.Mark, "星轨定位", "下一次行动攻击此人。若命中时仍有护盾，星光将被折返。");
                    }
                    else
                    {
                        ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 0);
                        var mark = cycle.Mark;
                        if (mark.Health == 0) Rest("星轨落空", "本轮星位已空。");
                        else if (mark.Shield > 0)
                            GrantStatus(actor, actor, StatusKind.Weakened, 2, "星光折返", effect, delayDecay: true);
                        else ExecuteConfiguredEnemyAction(actor, special, effect, new[] { mark });
                    }
                    break;
                case "EnemyCrucibleRegent":
                    if (actor.Layers(StatusKind.Chill) > 0)
                    {
                        RemoveLayers(actor, StatusKind.Chill, 1);
                        cycle.Pressure = 0;
                        Rest("冷却泄压", "寒冷使炉火熄灭，炉温归零。");
                    }
                    else if (++cycle.Pressure >= 3)
                    {
                        cycle.Pressure = 0;
                        ExecuteConfiguredEnemyAction(actor, special, effect);
                    }
                    else
                    {
                        Basic();
                        if (actor.Health > 0)
                            Report(BattleEventKind.Effect, actor, actor, "炉温上升", ActionKind.Shield,
                                detail: $"炉温 {cycle.Pressure}/3，升满后灼烧前两人。", visual: effect);
                    }
                    break;
            }
            return true;

            void Basic() => ExecuteConfiguredEnemyAction(actor, basic, BattleVisualCue.None);
            void Rest(string name, string detail) =>
                Report(BattleEventKind.Action, actor, actor, name, ActionKind.Shield, detail: detail, visual: effect);
            void Warn(Unit target, string name, string detail)
            {
                Report(BattleEventKind.Action, actor, target, name, ActionKind.Shield, detail: detail, visual: BattleVisualCue.GraveyardCharge);
                ReportCountdown(actor, BattleCountdownKind.EnemyCharge, 1);
            }
        }
    }
}
