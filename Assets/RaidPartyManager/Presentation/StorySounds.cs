using System;
using RaidPartyManager.Run;
using static RaidPartyManager.Presentation.SoundCue;

namespace RaidPartyManager.Presentation
{
    internal static class StorySounds
    {
        internal static SoundCue Opening(AdventureEventKind kind) => kind switch
        {
            AdventureEventKind.Obituary => EventGraveObituary,
            AdventureEventKind.ClinicQueue => EventCapitalClinicZero,
            _ => None
        };

        internal static SoundCue Choice(AdventureEventKind kind, string key, bool resolved)
        {
            if (key == "pass") return None;
            return kind switch
            {
                AdventureEventKind.Pallbearer when resolved => EventGraveCoffinLift,
                AdventureEventKind.ClaimDesk when resolved => EventGraveCompStamp,
                AdventureEventKind.Sponsorship when key.StartsWith("member-", StringComparison.Ordinal) => EventGraveSponsorEngrave,
                AdventureEventKind.HoundWalk when resolved => EventGraveMastiffCollar,
                AdventureEventKind.DeadLetters => EventGraveMisdeliveredLetters,
                AdventureEventKind.WeepingWall when resolved => EventGraveWeepingWall,
                AdventureEventKind.Ravens when resolved => EventGraveRavenWing,
                AdventureEventKind.BellRope when resolved => EventGraveBellStrike,
                AdventureEventKind.SoulWard when resolved && key == "stitch" => EventGraveTwinBedsTransfer,
                AdventureEventKind.SoupQueue when resolved => EventGraveSoupHandoff,
                AdventureEventKind.WhisperWall when resolved => EventGraveGraveMarkerReply,
                AdventureEventKind.Epitaph when resolved => EventGraveEpitaphChisel,
                AdventureEventKind.BorrowedName when resolved => EventGraveSealedName,
                AdventureEventKind.FareGate => EventCapitalTurnstile,
                AdventureEventKind.DeadMail => EventCapitalPneumaticMail,
                AdventureEventKind.TimeOffice => EventCapitalTimeClock,
                AdventureEventKind.WaterCharter => EventCapitalWaterworks,
                AdventureEventKind.LostFreight => EventCapitalLuggageScale,
                AdventureEventKind.WrongSwitch => EventCapitalTrackSwitch,
                AdventureEventKind.StreetCurrent when key == "0" => EventCapitalStreetLamp,
                AdventureEventKind.ElectionPress => EventCapitalBallotPress,
                AdventureEventKind.BoilerUnion => EventCapitalBoilerVent,
                AdventureEventKind.SilentOrchestra when key == "0" => EventCapitalOrchestraWindup,
                AdventureEventKind.SeedArchive => EventCapitalSeedVault,
                AdventureEventKind.SpareHands => EventCapitalMechanicalHands,
                AdventureEventKind.GoldStandard => EventCapitalHalfGram,
                AdventureEventKind.LastMeridian when key == "0" => EventCapitalLastNoon,
                _ => None
            };
        }
    }
}
