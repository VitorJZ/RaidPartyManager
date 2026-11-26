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

        // Book numbers 1–20 and 21–38 occupy consecutive blocks in the bank.
        private static SoundCue Book(string id)
        {
            const string prefix = "skill-book-";
            if (id?.StartsWith(prefix, StringComparison.Ordinal) != true ||
                !int.TryParse(id.Substring(prefix.Length), out int number)) return None;
            return number switch
            {
                >= 1 and <= 20 => (SoundCue)((int)SkillWarCry + number - 1),
                >= 21 and <= 38 => (SoundCue)((int)SkillHookMoonArrow + number - 21),
                _ => None
            };
        }

        internal static SoundCue Release(BattleEvent action, CharacterDefinition actor, bool projectile, bool arrow)
        {
            var book = Book(action.SkillBookId);
            if (action.ActorEnergy.HasValue) return None;
            if (book == SkillMeteor || action.VisualCue == BattleVisualCue.AdmiralBroadside) return None;
            if (book is SkillArrowVolley or SkillBroadSweep) return book;
            if (action.ActionKind != ActionKind.Attack || action.ActorId == action.TargetId) return None;
            if (actor.Id == "DeepSeaWhisper" && action.SkillBookId == null) return ActorDeepSeaWhisperWaterBlade;
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
                GameAudio.Play(cast is SkillArrowVolley or SkillMeteor or SkillBroadSweep or SkillThunderVerdictRelease or SkillIronWallIntercept
                    ? BuffApply : cast);
            }
            GameAudio.Play(BossMechanic(entry));
            if (entry.Intercepted) GameAudio.Play(SkillIronWallIntercept, .54f);
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
            var equipment = entry.Equipment == EquipmentEffect.WindGlove || entry.VisualCue == BattleVisualCue.RoundEndTransfer
                ? None : Equipment(entry.Equipment);
            if (equipment != None && entry.Detail != "状态未生效")
                special = equipment;
            if (special == None && entry.ActionName == "消耗药水")
                special = PotionTrigger;
            if (entry.CharmTriggered) GameAudio.Play(GearElvenCharmSave);
            if (entry.ActionName == "腐花传毒" && entry.Detail != "状态未生效") special = ActorRotBloomPoisonTransfer;
            if (entry.Kind == BattleEventKind.Action && entry.ActionKind == ActionKind.Attack && Book(entry.SkillBookId) == SkillMeteor)
                special = SkillMeteor;
            GameAudio.Play(special);

            if (entry.ShieldAbsorbed > 0 || entry.Detail is global::RaidPartyManager.Battle.BattleSession.StoneGuardBlocked or global::RaidPartyManager.Battle.BattleSession.GoldenBellBlocked) GameAudio.Play(HitShield);
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
                    StatusKind.Wet when Layers(previous, StatusKind.Wet) == 0 => StatusWetApply,
                    StatusKind.Evaporation => StatusEvaporationApply,
                    StatusKind.ForestMark => StatusForestMarkApply,
                    StatusKind.Exposed or StatusKind.Weakened or StatusKind.Poison or StatusKind.Cursed or
                        StatusKind.Stunned or StatusKind.Injured or StatusKind.Wet => DebuffApply,
                    _ => BuffApply
                };
                GameAudio.Play(cue, .31f);
            }
        }

        private static SoundCue BossMechanic(BattleEvent entry)
        {
            if (entry.VisualCue is not (BattleVisualCue.GraveyardCharge or BattleVisualCue.GraveyardSpecial)) return None;
            return entry.ActionName switch
            {
                "葬王点名" => BossKingSentenceMark,
                "王命葬列" when entry.Kind == BattleEventKind.Action => BossKingBurialSentence,
                "赦免" => BossKingSentenceCancel,
                "守柩祈祷" => BossMatronGuardPrayer,
                "空柩哀悼" => BossMatronEmptyBier,
                "汽笛鸣响" => BossEngineChargeWarning,
                "终点清场" when entry.Kind == BattleEventKind.Action => BossEngineCharge,
                "制动熄火" => BossEngineStall,
                "星轨定位" => BossGovernorTargetLock,
                "群星合相" when entry.Kind == BattleEventKind.Action => BossGovernorAlignment,
                "星光折返" => BossGovernorRefract,
                "炉温上升" => BossRegentHeatBuildup,
                "全城开炉" when entry.Kind == BattleEventKind.Action => BossRegentFurnaceBurst,
                "冷却泄压" => BossRegentPressureRelease,
                _ => None
            };
        }

        internal static SoundCue Equipment(EquipmentEffect effect) => effect switch
        {
            EquipmentEffect.RitualMask => GearRitualMaskEcho,
            EquipmentEffect.WindGlove => GearWindGloveFollowup,
            EquipmentEffect.VigorPotion => GearVigorPotion,
            EquipmentEffect.SwiftPotion => GearSwiftPotion,
            EquipmentEffect.ResolvePotion => GearResolvePotion,
            EquipmentEffect.GreedPotion => GearGreedPotion,
            EquipmentEffect.ThornsPotion => GearThornsPotion,
            EquipmentEffect.PurityPotion => GearPurityPotion,
            EquipmentEffect.MurkyWater => GearMurkyWaterRepeat,
            EquipmentEffect.PanaceaBottle => GearPanaceaPreserve,
            EquipmentEffect.ZuixianGourd => GearZuixianGourdMight,
            EquipmentEffect.WitchDoctorDoll => GearWitchDoctorDoll,
            EquipmentEffect.PoisonRecipe => GearPoisonRecipeAmplify,
            EquipmentEffect.PressureGovernor or EquipmentEffect.CelestialGyro or EquipmentEffect.InsulatingOrb => GearMetalWard,
            EquipmentEffect.LifePendant or EquipmentEffect.RubyPendant or EquipmentEffect.HealingRing or EquipmentEffect.CrucibleHeart => GearVitalityRestore,
            EquipmentEffect.StoneArmor => GearStoneArmor,
            EquipmentEffect.FacelessCrown => GearFacelessCrown,
            EquipmentEffect.ColdSoulReliquary or EquipmentEffect.SunkenFrostChain => GearFrostRelic,
            EquipmentEffect.FireRing or EquipmentEffect.FirstFire or EquipmentEffect.EternalFire => GearFireEmber,
            EquipmentEffect.LightningRing or EquipmentEffect.HarborWhistle or EquipmentEffect.StaticPrism or EquipmentEffect.TreeRoot => GearLightningArc,
            EquipmentEffect.IceRing => GearIceRingSpread,
            EquipmentEffect.ExecutionMask or EquipmentEffect.RebelEarring or EquipmentEffect.Bludgeon => GearRendingMark,
            EquipmentEffect.CounterHorn => GearCounterHorn,
            EquipmentEffect.InsightTotem => GearInsightTotem,
            EquipmentEffect.MoneyTreeBranch => GearMoneyTreeCoins,
            EquipmentEffect.Megaphone => GearMegaphoneBuff,
            EquipmentEffect.RedCord => GearRedCordLink,
            EquipmentEffect.RecruitmentNotice => GearRecruitmentNotice,
            EquipmentEffect.ArcaneCrystal or EquipmentEffect.ElementalQuartz => GearArcaneCrystal,
            EquipmentEffect.Bagua => GearBaguaTurn,
            _ => None
        };

        private static int Layers(IReadOnlyList<StatusStack> stacks, StatusKind kind)
        {
            if (stacks != null)
                foreach (var stack in stacks) if (stack.Kind == kind) return stack.Layers;
            return 0;
        }
    }
}
