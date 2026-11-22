using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage
    {
        private const float ArrivalTime = 1.16f;
        private static readonly PoseFraming DefaultFraming = new() { sideSpan = .84f, sideInset = .072f, frontSpan = .938f, frontBodySpan = .93f, frontInset = .020f };
        private static PoseFraming[] framing;
        private readonly Dictionary<CharacterDefinition, RecruitActor> recruitCast = new();
        private Mesh recruitWalkMesh;

        [Serializable]
        private sealed class FramingTable { public PoseFraming[] actors; }

        [Serializable]
        internal struct PoseFraming
        {
            public float sideSpan, sideInset, frontSpan, frontInset;
            // Crown to sole on the resting front cel, excluding raised props such as staves.
            public float frontBodySpan;
        }

        private sealed class RecruitActor
        {
            public MeshRenderer Body, Shadow;
            public Texture2D Front, Walk, Battle;
            public Vector3 Mark, Entrance;
            public PoseFraming Framing;
            public float VisibleHeight, FrontQuadHeight, FrontWidth, Elapsed;
            public float Stride;
            public int Index;
            public bool Held;
        }

        internal static PoseFraming FramingFor(int rosterNumber)
        {
            if (framing == null)
            {
                var resource = Resources.Load<TextAsset>("RecruitmentFront/Framing");
                framing = resource == null ? Array.Empty<PoseFraming>() :
                    JsonUtility.FromJson<FramingTable>(resource.text)?.actors ?? Array.Empty<PoseFraming>();
            }
            int index = rosterNumber - 1;
            return index >= 0 && index < framing.Length ? framing[index] : DefaultFraming;
        }

        internal void ShowRecruitActor(EncounterData content, CharacterDefinition character, int index, bool enter)
        {
            if (recruitCast.ContainsKey(character)) return;
            var front = Resources.Load<Texture2D>($"RecruitmentFront/{character.RosterNumber:00}");
            var walk = content.BattleWalkFor(character.Id);
            var battle = content.BattleSpriteFor(character.Id);
            var deck = house.DeckBounds;
            var mark = RecruitMark(index);
            var pose = FramingFor(character.RosterNumber);
            float bodyHeight = CompanySpriteSize * pose.sideSpan;
            float frontHeight = front == null ? CompanySpriteSize : bodyHeight / pose.frontSpan;
            float frontWidth = front == null ? frontHeight : frontHeight * front.width / front.height;
            var actor = new RecruitActor
            {
                Body = Flat("Recruit front " + character.Id, mark, frontWidth, frontHeight, front),
                Shadow = Shadow(mark, bodyHeight * .76f, .36f, .62f), Front = front, Walk = walk, Battle = battle,
                Mark = mark, Entrance = new Vector3(7.6f, deck.max.y, deck.min.z + 1.92f), Framing = pose, Index = index,
                VisibleHeight = bodyHeight, FrontQuadHeight = frontHeight, FrontWidth = frontWidth,
                Elapsed = enter ? 0 : ArrivalTime, Stride = EntranceWalkPose.MotionFor(character.Profession).Stride
            };
            if (walk == null && battle != null)
                actor.Body.GetComponent<MeshFilter>().sharedMesh = recruitWalkMesh ??= MakeRecruitWalkMesh();
            recruitCast.Add(character, actor);
            PoseRecruit(actor, character.RosterNumber, 0);
        }

        private Vector3 RecruitMark(int index)
        {
            // Fit the staggered apron marks inside the close camera at horizontal and vertical aspect ratios.
            var deck = house.DeckBounds;
            var eye = RecruitmentEye;
            float z = deck.min.z + (index < 4 ? .76f : .39f);
            float viewportX = (index < 4 ? .115f : .181f) + (index % 4) * .13f;
            return new Vector3(eye.x + (2 * viewportX - 1) * (z - eye.z) / wideProjection.m00, deck.max.y, z);
        }

        internal static Mesh MakeRecruitWalkMesh()
        {
            var mesh = BattleStage.MakeSheet(14, 18);
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y -= .5f;
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal void UpdateRecruitCast(float delta)
        {
            foreach (var entry in recruitCast)
                PoseRecruit(entry.Value, entry.Key.RosterNumber, delta);
        }

        private void PoseRecruit(RecruitActor actor, int seed, float delta)
        {
            bool visible = RecruitmentFocused || focus > .1f;
            actor.Body.enabled = visible;
            actor.Shadow.enabled = visible && !actor.Held;
            if (!visible) return;
            actor.Mark = RecruitMark(actor.Index);
            actor.Elapsed = Mathf.Min(ArrivalTime, actor.Elapsed + delta);
            bool walking = actor.Elapsed < ArrivalTime;
            if (actor.Held) return;
            float progress = Mathf.SmoothStep(0, 1, actor.Elapsed / ArrivalTime);
            var foot = Vector3.Lerp(actor.Entrance, actor.Mark, progress);
            var texture = walking ? actor.Walk ?? actor.Battle : actor.Front ?? actor.Battle;
            if (texture != null)
            {
                bool front = !walking && actor.Front != null;
                bool synthesizedWalk = walking && actor.Walk == null;
                int frame = walking ? AnimationCels.Loop(actor.Elapsed, 6f / 13f) : front ? AnimationCels.Loop(Time.unscaledTime + seed / 5f, 1.2) : 0;
                int cel = synthesizedWalk ? 0 : frame;
                int columns = front || walking && actor.Walk != null ? 4 : AnimationCels.Count;
                const int rows = 4;
                float phase = frame * 2 * Mathf.PI / AnimationCels.Count;
                actor.Body.GetPropertyBlock(properties);
                properties.SetTexture("_MainTex", texture);
                properties.SetVector("_MainTex_ST", new Vector4((walking ? -1f : 1f) / columns, 1f / rows,
                    (cel % columns + (walking ? 1 : 0)) / (float)columns, 1 - (cel / columns + 1f) / rows));
                properties.SetVector("_Walk", synthesizedWalk ? new Vector4(Mathf.Cos(phase), Mathf.Sin(phase), -1, actor.Stride) : Vector4.zero);
                properties.SetFloat("_PlayingZ", actor.Mark.z + .65f);
                actor.Body.SetPropertyBlock(properties);
            }
            bool side = walking || actor.Front == null;
            float quadHeight = side ? CompanySpriteSize : actor.FrontQuadHeight;
            float inset = side ? actor.Framing.sideInset : actor.Framing.frontInset;
            actor.Body.transform.localScale = new Vector3(side ? CompanySpriteSize : actor.FrontWidth, quadHeight, 1);
            actor.Body.transform.position = foot + Vector3.up * (quadHeight * (.5f - inset));
            actor.Shadow.transform.position = foot + Vector3.up * .016f;
        }

        internal bool RecruitReady(CharacterDefinition character) => recruitCast.TryGetValue(character, out var actor) &&
            actor.Elapsed >= ArrivalTime;

        internal void PlaceRecruitTarget(CharacterDefinition character, VisualElement target)
        {
            if (!recruitCast.TryGetValue(character, out var actor)) return;
            float halfWidth = Mathf.Max(.16f, actor.FrontWidth * .34f);
            Pin(target, actor.Mark + new Vector3(-halfWidth, actor.VisibleHeight, 0), actor.Mark + Vector3.right * halfWidth);
        }

        internal void MoveHeldRecruit(CharacterDefinition character, Vector2 viewport)
        {
            var actor = recruitCast[character];
            actor.Held = true;
            var pointer = camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y,
                actor.Mark.z - camera.transform.position.z - .14f));
            actor.Body.transform.position = pointer;
            actor.Shadow.enabled = false;
        }

        internal void ReleaseRecruit(CharacterDefinition character)
        {
            if (recruitCast.TryGetValue(character, out var actor)) actor.Held = false;
        }
    }
}
