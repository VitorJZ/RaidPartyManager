using UnityEngine;

namespace RaidPartyManager.Presentation
{
    internal sealed class BossEntranceCue
    {
        private enum Role { Admiral, Ray, King, Matron, Engine, Governor, Regent }
        private readonly Role role;
        internal readonly float Seconds, LeadBeat, Beat;
        internal readonly SoundCue LeadSound, Sound;

        private BossEntranceCue(Role role, float seconds, float leadBeat, SoundCue leadSound, float beat, SoundCue sound)
        {
            this.role = role;
            Seconds = seconds;
            LeadBeat = leadBeat;
            LeadSound = leadSound;
            Beat = beat;
            Sound = sound;
        }

        internal static BossEntranceCue For(string id) => id switch
        {
            "EnemyBlockadeAdmiral" => new(Role.Admiral, 2.22f, .14f, SoundCue.BossAdmiralWarning, .55f, SoundCue.BossAdmiralCannon),
            "EnemyDrownedLanternRay" => new(Role.Ray, 2.30f, .12f, SoundCue.BossMantarayColdMist, .49f, SoundCue.BossMantarayPhaseShift),
            "EnemyNamelessKing" => new(Role.King, 2.45f, .16f, SoundCue.BossKingCoffinUnlock, .58f, SoundCue.BossKingCoffinRise),
            "EnemySepulchralMatron" => new(Role.Matron, 2.50f, .12f, SoundCue.BossMatronCenser, .60f, SoundCue.BossMatronAppear),
            "EnemyRoyalEngine" => new(Role.Engine, 2.05f, .13f, SoundCue.BossEngineArrivalWhistle, .48f, SoundCue.BossEngineBrakeSkid),
            "EnemyAstralGovernor" => new(Role.Governor, 2.32f, .16f, SoundCue.BossGovernorArmillarySpin, .52f, SoundCue.BossGovernorAppearLock),
            "EnemyCrucibleRegent" => new(Role.Regent, 2.34f, .24f, SoundCue.BossRegentFurnaceOpen, .63f, SoundCue.BossRegentLanding),
            _ => null
        };

        // Offsets are measured from the final foot mark, in standing actor heights.
        internal readonly struct Pose
        {
            internal readonly float X, Y, Scale, Angle, Alpha, CropBottom;
            internal Pose(float x = 0, float y = 0, float scale = 1, float angle = 0, float alpha = 1, float cropBottom = 0)
            {
                X = x; Y = y; Scale = scale; Angle = angle; Alpha = alpha; CropBottom = cropBottom;
            }
        }

        internal readonly struct Shot
        {
            internal readonly float Weight, Coverage, Height, LeadRoom, PropFocus;
            internal Shot(float weight, float coverage, float height, float leadRoom = 0, float propFocus = 0)
            {
                Weight = weight; Coverage = coverage; Height = height; LeadRoom = leadRoom; PropFocus = propFocus;
            }
        }

        private static float Ease(float from, float to, float t) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, t));

        private static float Window(float start, float crest, float release, float end, float t) =>
            Ease(start, crest, t) * (1 - Ease(release, end, t));

        internal Pose Actor(float t)
        {
            switch (role)
            {
                case Role.Admiral:
                    // Already at his gun. Brace, recoil at the report, recover the salute.
                    float recoil = Window(.54f, .59f, .60f, .78f, t);
                    return new Pose(.12f * recoil, .025f * recoil, angle: 5 * recoil,
                        alpha: Ease(0, .12f, t));
                case Role.Ray:
                    // Breach on the far left, bank above the boards, then dive onto the mark.
                    float flight = Ease(.04f, .82f, t);
                    float crest = Mathf.Sin(Mathf.PI * flight);
                    return new Pose(-1.48f * (1 - flight), -.79f * crest,
                        .48f + .52f * flight + .22f * crest, -23 * crest + 12 * Window(.50f,.64f,.66f,.85f,t),
                        Ease(.03f,.15f,t));
                case Role.King:
                    float buried = 1 - Ease(.22f, .69f, t);
                    return new Pose(y: .69f * buried, alpha: Ease(.21f,.32f,t), cropBottom: .69f * buried);
                case Role.Matron:
                    // Follow the censer out of the depth of the chapel in a slow procession.
                    float near = Ease(.12f,.86f,t);
                    return new Pose(.48f * (1 - near), -.24f * (1 - near),
                        .29f + .71f * near, alpha: Ease(.14f,.48f,t));
                case Role.Engine:
                    float run = Ease(.12f, .48f, t);
                    float skid = 1 - Ease(.48f, .75f, t);
                    return new Pose(2.85f * (1 - run) - .34f * run * skid,
                        -.045f * Window(.46f,.50f,.51f,.65f,t),
                        angle: -5 * Window(.44f,.49f,.51f,.69f,t));
                case Role.Governor:
                    // The figure materializes inside an already turning sight, then locks to its center.
                    float lockOn = Ease(.35f,.57f,t);
                    return new Pose(y: -.13f * (1 - Ease(.56f,.84f,t)),
                        scale: .70f + .30f * lockOn, alpha: Ease(.29f,.50f,t));
                default:
                    float emerge = Ease(.36f,.66f,t);
                    float strike = Window(.59f,.63f,.65f,.78f,t);
                    return new Pose(y: -.30f * Window(.34f,.53f,.54f,.64f,t) + .06f * strike,
                        scale: .56f + .44f * emerge, angle: -4 * strike, alpha: Ease(.33f,.56f,t));
            }
        }

        internal int PropOrder => role is Role.Admiral or Role.Matron ? 6 : -2;
        internal bool HasCoffinRim => role == Role.King;

        internal Pose Prop(float t)
        {
            float fade = 1 - Ease(.72f,.95f,t);
            switch (role)
            {
                case Role.Admiral:
                    return new Pose(-.60f + .08f * Window(.54f,.59f,.60f,.75f,t), .09f, .94f,
                        alpha: fade * Ease(0,.14f,t));
                case Role.Ray:
                    float wake = Ease(.07f,.62f,t);
                    return new Pose(-1.13f + .91f * wake, .20f, .65f + .96f * wake,
                        alpha: Window(.06f,.18f,.53f,.77f,t));
                case Role.King:
                    return new Pose(y: .24f + .25f * Ease(.73f,.95f,t), scale: 1.08f,
                        alpha: fade * Ease(0,.10f,t));
                case Role.Matron:
                    float swing = Mathf.Sin((t * 1.42f - .15f) * 2 * Mathf.PI);
                    return new Pose(-.42f + .14f * swing, -.23f, .63f, -10 * swing,
                        fade * Ease(0,.12f,t));
                case Role.Engine:
                    return new Pose(.45f + .32f * Ease(.44f,.71f,t), .37f, 1.42f,
                        alpha: Window(.37f,.47f,.60f,.80f,t));
                case Role.Governor:
                    float align = Ease(.04f,.52f,t);
                    return new Pose(y: -.03f, scale: 1.50f - .23f * align, angle: -64 * (1 - align),
                        alpha: fade * Ease(0,.11f,t));
                default:
                    return new Pose(y: .05f, scale: 1.36f + .15f * Ease(.17f,.57f,t),
                        alpha: fade * Ease(0,.12f,t));
            }
        }

        internal float PropProgress(float t) => role switch
        {
            Role.Admiral => Mathf.Lerp(0,.32f,Ease(.10f,.55f,t)) + .68f * Ease(.55f,.81f,t),
            Role.Engine => Mathf.InverseLerp(.37f,.77f,t),
            Role.King => Mathf.InverseLerp(.12f,.63f,t),
            Role.Governor => Mathf.InverseLerp(.02f,.52f,t),
            Role.Regent => Mathf.InverseLerp(.10f,.64f,t),
            _ => Mathf.InverseLerp(.04f,.77f,t)
        };

        internal Shot CameraAt(float t) => role switch
        {
            Role.Admiral => new(Window(.32f,.43f,.66f,.90f,t), .68f,.42f,.07f, .46f * (1 - Ease(.39f,.58f,t))),
            Role.Ray => new(Window(.18f,.60f,.66f,.93f,t), .60f,.51f,-.12f),
            Role.King => new(Window(.17f,.63f,.73f,.97f,t), .75f, .70f - .36f * Ease(.23f,.64f,t), propFocus: 1 - Ease(.24f,.58f,t)),
            Role.Matron => new(Window(.04f,.35f,.56f,.98f,t), .56f,.48f,.04f, .55f * (1 - Ease(.21f,.52f,t))),
            Role.Engine => new(Window(.44f,.51f,.56f,.80f,t), .68f,.56f,-.10f),
            Role.Governor => new(Window(.05f,.27f,.65f,.88f,t), .67f,.43f, propFocus: 1 - Ease(.32f,.53f,t)),
            _ => new(Window(.33f,.53f,.69f,.96f,t), .74f,.54f - .13f * Ease(.49f,.65f,t), propFocus: 1 - Ease(.33f,.57f,t))
        };
    }
}
