using UnityEngine;

namespace RaidPartyManager.Presentation
{
    internal sealed class BossEntranceCue
    {
        private enum Role { Admiral, Ray, King, Matron, Engine, Governor, Regent }
        private readonly Role role;
        internal readonly float Seconds, Beat, FocusHeight;
        internal readonly SoundCue Sound;

        private BossEntranceCue(Role role, float seconds, float beat, float focusHeight, SoundCue sound)
        {
            this.role = role;
            Seconds = seconds;
            Beat = beat;
            FocusHeight = focusHeight;
            Sound = sound;
        }

        internal static BossEntranceCue For(string id) => id switch
        {
            "EnemyBlockadeAdmiral" => new(Role.Admiral, 2.10f, .51f, .41f, SoundCue.BossAdmiralWarning),
            "EnemyDrownedLanternRay" => new(Role.Ray, 2.25f, .50f, .50f, SoundCue.BossMantarayColdMist),
            "EnemyNamelessKing" => new(Role.King, 2.30f, .56f, .38f, SoundCue.SwingHeavy),
            "EnemySepulchralMatron" => new(Role.Matron, 2.25f, .54f, .43f, SoundCue.EnemyJudgeBell),
            "EnemyRoyalEngine" => new(Role.Engine, 2.16f, .49f, .53f, SoundCue.EnemyAnchorArmorBreak),
            "EnemyAstralGovernor" => new(Role.Governor, 2.30f, .55f, .42f, SoundCue.OrbCast),
            "EnemyCrucibleRegent" => new(Role.Regent, 2.18f, .55f, .47f, SoundCue.SkillEarthquake),
            _ => null
        };

        // Offsets are fractions of the figure, shared by different viewport and stage sizes.
        internal Vector2 Offset(float t)
        {
            float landing = Mathf.Clamp01(t / Beat);
            float arc = Mathf.Sin(Mathf.PI * landing);
            float brake = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.32f, .66f, t));
            return role switch
            {
                Role.Admiral => new Vector2(0, -.035f * Mathf.Abs(Mathf.Sin(3 * Mathf.PI * landing))),
                Role.Ray => new Vector2(-.12f * arc, -.51f * arc + .11f * brake),
                Role.King => new Vector2(0, .26f * (1 - Mathf.SmoothStep(0, 1, landing))),
                Role.Matron => new Vector2(.08f * arc, -.17f * Mathf.Sin(Mathf.PI * t)),
                Role.Engine => new Vector2(-.32f * brake, 0),
                Role.Governor => new Vector2(.19f * arc, -.38f * (1 - landing) - .12f * arc),
                Role.Regent => new Vector2(0, -.29f * arc + .07f * brake),
                _ => Vector2.zero
            };
        }

        internal float ShotWeight(float t) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.16f, Beat - .04f, t)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Beat + .15f, .98f, t)));
    }
}
