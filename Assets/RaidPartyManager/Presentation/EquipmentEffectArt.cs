using RaidPartyManager.Domain;
using UnityEngine;
using static RaidPartyManager.Domain.EquipmentEffect;

namespace RaidPartyManager.Presentation
{
    internal static class EquipmentEffectArt
    {
        internal static Texture2D For(EquipmentEffect effect)
        {
            string artwork = effect switch
            {
                RitualMask => "RitualEcho",
                WindGlove => "WindFollowUp",
                VigorPotion => "VigorTonic",
                SwiftPotion => "SwiftTonic",
                ResolvePotion => "ResolveTonic",
                GreedPotion => "GreedTonic",
                ThornsPotion => "ThornsTonic",
                PurityPotion => "PurityTonic",
                MurkyWater => "SecondDose",
                PanaceaBottle => "SavedBottle",
                ZuixianGourd => "GourdMight",
                WitchDoctorDoll => "PoisonRemedy",
                PoisonRecipe => "ThornsTonic",
                PressureGovernor or CelestialGyro or InsulatingOrb => "MetalWard",
                LifePendant or RubyPendant or HealingRing or CrucibleHeart => "Vitality",
                StoneArmor => "StoneWard",
                FacelessCrown => "CrownWard",
                ColdSoulReliquary or SunkenFrostChain => "FrostRelic",
                FireRing or FirstFire or EternalFire => "FireBurst",
                LightningRing or HarborWhistle or StaticPrism or TreeRoot => "LightningSurge",
                IceRing => "IceBurst",
                ExecutionMask or RebelEarring or Bludgeon => "RendingMark",
                CounterHorn => "CounterPulse",
                InsightTotem => "Exposure",
                MoneyTreeBranch => "CoinBloom",
                Megaphone => "Resonance",
                RedCord => "LinkedDamage",
                RecruitmentNotice => "UnfurlingNotice",
                ArcaneCrystal or ElementalQuartz => "ArcaneRecovery",
                Bagua => "Trigram",
                _ => null
            };
            return artwork == null ? null : Resources.Load<Texture2D>("EquipmentEffects/Triggers/" + artwork);
        }
    }
}
