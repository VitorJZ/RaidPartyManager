using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using static RaidPartyManager.Presentation.SoundCue;

namespace RaidPartyManager.Presentation
{
    internal static class BattleSounds
    {
        internal static SoundCue Fall(CharacterDefinition c) =>
            c.PositionSlots > 1 || c.Rank == EnemyRank.Boss ? FallLargeMonster : FallHumanoid;

        internal static SoundCue Step(CharacterDefinition c)
        {
            if (c.Trait is CharacterTrait.EnemyBlockadeAdmiral or CharacterTrait.EnemyMooringWarden) return StepHeavyArmor;
            if (c.Trait is CharacterTrait.EnemyMudEel or CharacterTrait.EnemySaltCrab or CharacterTrait.EnemyAnchorHermit
                or CharacterTrait.EnemyDrownedLanternRay or CharacterTrait.EnemyFogAngler) return StepWetMonster;
            return c.Profession == Profession.Warrior || c.PositionSlots > 1 ? StepHeavyArmor : StepDampWood;
        }

        // The first twenty numbered books have dedicated recordings in the audio catalogue.
        private static SoundCue Book(string id)
        {
            const string prefix = "skill-book-";
            if (id?.StartsWith(prefix, StringComparison.Ordinal) != true ||
                !int.TryParse(id.Substring(prefix.Length), out int number) || number < 1 || number > 20) return None;
            return (SoundCue)((int)SkillWarCry + number - 1);
        }

        internal static SoundCue Release(BattleEvent action, CharacterDefinition actor, bool projectile, bool arrow)
        {
            var book = Book(action.SkillBookId);
            if (action.ActorEnergy.HasValue) return None;
            if (book == SkillMeteor || action.VisualCue == BattleVisualCue.AdmiralBroadside) return None;
            if (book is SkillArrowVolley or SkillBroadSweep) return book;
            if (action.ActionKind != ActionKind.Attack || action.ActorId == action.TargetId) return None;
            if (projectile) return arrow ? BowRelease : OrbCast;
            return action.AttackReach == AttackReach.Melee
                ? Step(actor) == StepHeavyArmor ? SwingHeavy : SwingLight : None;
        }

        internal static void Impact(BattleEvent entry, CharacterDefinition target, bool arrow,
            IReadOnlyList<StatusStack> previous, int previousHealth)
        {
            if (entry.Kind == BattleEventKind.Action && entry.ActorEnergy.HasValue)
            {
                var cast = Book(entry.SkillBookId);
                GameAudio.Play(cast is SkillArrowVolley or SkillMeteor or SkillBroadSweep ? BuffApply : cast);
            }
            if (entry.Detail == "未命中") { GameAudio.Play(AttackMiss); return; }
            if (entry.Kind == BattleEventKind.Defeated) return;

            var special = entry.VisualCue switch
            {
                BattleVisualCue.BellRing => EnemyJudgeBell,
                BattleVisualCue.BellWave => EnemyBellShockwave,
                BattleVisualCue.ScavengerSteal => EnemyScavengerSteal,
                BattleVisualCue.AdmiralWhistle => BossAdmiralWarning,
                BattleVisualCue.AdmiralBroadside when entry.Kind == BattleEventKind.Action => BossAdmiralCannon,
                BattleVisualCue.AnchorShatter => EnemyAnchorArmorBreak,
                BattleVisualCue.RayMist => BossMantarayColdMist,
                BattleVisualCue.RaySurge => BossMantarayPhaseShift,
                BattleVisualCue.RoundEndTransfer => MegaphoneHandoff,
                BattleVisualCue.RoundEndLightning when entry.ActionName == "灾厄吟唱" => BossCalamity,
                _ => None
            };
            if (entry.Kind == BattleEventKind.Retreated && target.Trait == CharacterTrait.EnemyWreckScavenger)
                special = EnemyScavengerEscape;
            if (entry.ActionName == "消耗药水" || entry.ActionName == "万能药瓶")
                special = PotionTrigger;
            if (entry.Kind == BattleEventKind.Action && entry.ActionKind == ActionKind.Attack && Book(entry.SkillBookId) == SkillMeteor)
                special = SkillMeteor;
            GameAudio.Play(special);

            if (entry.ShieldAbsorbed > 0 || entry.Detail == "坚如磐石抵挡了伤害") GameAudio.Play(HitShield);
            if (entry.ActionKind == ActionKind.Heal && entry.TargetHealth > previousHealth)
                GameAudio.Play(Heal, .52f);
            if (entry.TargetHealth < previousHealth && entry.ActionKind == ActionKind.Attack)
            {
                var damage = entry.PoisonDamage ? TickPoison : arrow ? ArrowImpact : HitPhysical;
                if (!entry.PoisonDamage)
                    foreach (var part in entry.DamageTaken)
                    {
                        if (part.Amount <= 0) continue;
                        damage = part.Type switch
                        {
                            DamageType.Fire => HitFire,
                            DamageType.Lightning => HitLightning,
                            DamageType.Magic => HitMagic,
                            DamageType.True or DamageType.Fixed => HitFixedDamage,
                            _ => damage
                        };
                        break;
                    }
                GameAudio.Play(damage, special == None ? .57f : .32f);
            }

            if (previous != null && Layers(previous, StatusKind.Frozen) > 0 &&
                Layers(entry.StatusStacks, StatusKind.Frozen) == 0 && entry.TargetHealth < previousHealth)
                GameAudio.Play(IceShatter);
            if (entry.Detail == "状态未生效" || entry.StatusStacks == null) return;
            foreach (var status in entry.StatusStacks)
            {
                if (status.Layers <= Layers(previous, status.Kind)) continue;
                var cue = status.Kind switch
                {
                    StatusKind.Chill or StatusKind.Frozen => FreezeForm,
                    StatusKind.Exposed or StatusKind.Weakened or StatusKind.Poison or StatusKind.Cursed or
                        StatusKind.Stunned or StatusKind.Injured => DebuffApply,
                    _ => BuffApply
                };
                GameAudio.Play(cue, .31f);
            }
        }

        private static int Layers(IReadOnlyList<StatusStack> stacks, StatusKind kind)
        {
            if (stacks != null)
                foreach (var stack in stacks) if (stack.Kind == kind) return stack.Layers;
            return 0;
        }
    }
}
