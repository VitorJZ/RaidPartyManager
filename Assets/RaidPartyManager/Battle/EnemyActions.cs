using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed partial class BattleSession
    {
        private bool TryEnemyTurn(Unit actor)
        {
            switch (actor.Trait)
            {
                case CharacterTrait.EnemyWarCry when random.NextDouble() < 0.5:
                    GrantStatus(actor, actor, StatusKind.Vigor, 3, "盛气咆哮", BattleVisualCue.WarCry);
                    return true;
                case CharacterTrait.EnemyResolve when random.NextDouble() < 0.5:
                    GrantStatus(actor, actor, StatusKind.Resolve, 1, "扎盾坚毅", BattleVisualCue.ResolveGuard);
                    return true;
                case CharacterTrait.EnemyCeramicPierce:
                    var target = TargetableOpponents(actor).First();
                    int defense = target.Stats.Defense;
                    var behind = Opponents(actor).FirstOrDefault(unit => unit.Input.Position > target.Input.Position);
                    if (EnemyHit(actor, target, "裂陶穿击", BattleVisualCue.CeramicPierce) && defense > 0 && behind?.Health > 0)
                        Strike(actor, behind, defense, DamageType.True, "陶片溅射", BattleEventKind.Effect,
                            visual: BattleVisualCue.CeramicSplash);
                    return true;
                case CharacterTrait.EnemyBell when actor.BellRings < 3 && random.NextDouble() < 0.5:
                    RingBell(actor);
                    return true;
                case CharacterTrait.EnemyRearAmbush when random.NextDouble() < 0.7:
                    EnemyHit(actor, TargetableOpponents(actor).Last(), "绕后突袭", BattleVisualCue.RearAmbush);
                    return true;
                case CharacterTrait.EnemyCrossbow:
                    CrossbowTurn(actor);
                    return true;
                case CharacterTrait.EnemyQuack:
                    QuackTurn(actor);
                    return true;
                default:
                    return false;
            }
        }

        private void RingBell(Unit actor)
        {
            actor.BellRings++;
            Report(BattleEventKind.Action, actor, actor, "敲钟", ActionKind.Shield,
                detail: $"第 {actor.BellRings} 声", visual: BattleVisualCue.BellRing);
            if (actor.BellRings != 3)
                return;
            int missingLife = actor.Stats.Health - actor.Health;
            foreach (var target in Opponents(actor).ToArray())
                Strike(actor, target, missingLife, DamageType.Magic, "第三声钟波", BattleEventKind.Effect,
                    visual: BattleVisualCue.BellWave);
        }

        private void CrossbowTurn(Unit actor)
        {
            if (random.NextDouble() >= 0.5)
            {
                Report(BattleEventKind.Effect, actor, actor, "待机", ActionKind.Shield, detail: "本回合未射击");
                return;
            }
            bool critical = random.NextDouble() < 0.2;
            EnemyHit(actor, TargetableOpponents(actor).First(), critical ? "重弩暴击" : "普攻",
                critical ? BattleVisualCue.CrossbowCritical : BattleVisualCue.None, critical ? 2 : 1);
        }

        private void QuackTurn(Unit actor)
        {
            if (random.NextDouble() < 0.5)
            {
                var target = units.Where(unit => unit.Input.Side == actor.Input.Side &&
                    unit.Health > 0 && unit.Health < unit.Stats.Health && unit.Layers(StatusKind.Cursed) == 0)
                    .OrderBy(unit => (double)unit.Health / unit.Stats.Health).FirstOrDefault();
                if (target != null)
                {
                    Heal(actor, target, actor.TraitPower, "摇铃治疗", BattleEventKind.Action, visual: BattleVisualCue.QuackHeal);
                    return;
                }
            }
            EnemyHit(actor, TargetableOpponents(actor).First(), "真伤刺击", BattleVisualCue.QuackStrike);
        }

        private bool EnemyHit(Unit actor, Unit target, string name, BattleVisualCue cue, int multiplier = 1) =>
            Hit(actor, target, actor.Input.Character.BasicAttack, BattleEventKind.Action, DecisionReason.Default,
                name, visual: cue, multiplier: multiplier);
    }
}
