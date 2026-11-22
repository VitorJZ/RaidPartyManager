using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TalentMarketStage : ServiceStage
    {
        private const float Spacing = 1.05f;
        private const float Depth = -6.55f;
        private const float StandingHeight = 1.44f;
        private const float PlacardFallTime = .34f;
        private const float WalkDelay = .22f;
        private const float StepIntoAisleTime = .29f;
        private const float WalkSpeed = 4.2f;
        private readonly MeshRenderer[] actors;
        private readonly MeshRenderer[] shadows;
        private readonly MeshRenderer[] placards;
        private readonly bool[] animated;
        private readonly float[] strides;
        private readonly Departure[] departures;
        private static readonly Vector3 BrokerFoot = new(-4.25f, .02f, -3.35f);
        private readonly MeshRenderer broker, brokerShadow;
        internal Vector2 BrokerHead => camera.WorldToViewportPoint(BrokerFoot + Vector3.up * 2.12f);
        private Mesh walkMesh;
        private int lastFrame = -1;

        private sealed class Departure
        {
            internal float StartedAt;
            internal Vector3 StartingPosition, PlacardFoot;
            internal DockSet.Player Player;
            internal int WalkCel = -1;
        }

        internal bool HasDepartures => Array.Exists(departures, departure => departure != null);

        internal TalentMarketStage(Transform owner, VisualElement panel, ServiceStageArt art,
            IReadOnlyList<CharacterDefinition> candidates, EncounterData content, bool graveyard, bool capital = false) : base(owner, panel, art, capital ? StageLighting.Clockwork(StageVenue.Interior) : StageLighting.For(graveyard, StageVenue.Interior))
        {
            if (capital) DressCapital(2);
            else if (graveyard)
            {
                HangBackdrop("Cemetery vestry", art.vestryHall);
                GraveyardFloor();
            }
            else
            {
                HangBackdrop("Hiring guild hall", art.marketHall, horizontalRepeats: 2f, verticalOffset: .13f);
                Deck(art.guildParquet, new Vector2(2.3f, 2.3f), new Color(.66f, .64f, .60f));
            }
            if (!capital) FloorProp(graveyard ? "Vestry vestments" : "Guild trophies", graveyard ? art.vestmentStand : art.guildTrophies,
                -4.85f, .36f, 2.1f, -8);
            if (!capital) FloorProp(graveyard ? "Parish records" : "Porters' baggage", graveyard ? art.recordPigeonholes : art.porterDolly,
                4.96f, .64f, graveyard ? 1.85f : 1.48f, 7);
            FloorProp("Registry desk", art.registryDesk, -3.75f, -1.3f, 3.25f, -7);
            FloorProp("Contract board", art.contractBoard, 3.75f, -.9f, 1.9f, 7);
            var idle = Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle");
            broker = Flat("Market storyteller", BrokerFoot + Vector3.up * 1.05f, 1.68f, 2.10f, idle);
            brokerShadow = Shadow(BrokerFoot, .88f, .40f);
            var placardArt = Resources.Load<Texture2D>("MarketPricePlacard");
            const float placardWidth = .88f;
            float placardHeight = placardWidth * placardArt.height / placardArt.width;
            actors = new MeshRenderer[candidates.Count];
            shadows = new MeshRenderer[candidates.Count];
            placards = new MeshRenderer[candidates.Count];
            animated = new bool[candidates.Count];
            strides = new float[candidates.Count];
            departures = new Departure[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var front = Resources.Load<Texture2D>($"RecruitmentFront/{c.RosterNumber:00}");
                var texture = front ?? content.PortraitFor(c.Id);
                var pose = ShopStage.FramingFor(c.RosterNumber);
                float height = front == null ? StandingHeight : StandingHeight / pose.frontBodySpan;
                float width = height * texture.width / texture.height;
                var foot = new Vector3((i - (candidates.Count - 1) * .5f) * Spacing, .01f, Depth);
                float lift = height * (.5f - (front == null ? 0 : pose.frontInset));
                var actor = Flat("Market performer " + i, foot + Vector3.up * lift, width, height, texture);
                actors[i] = actor;
                animated[i] = front != null;
                float walkHeight = StandingHeight / pose.sideSpan;
                Dressing.Cast[actor] = new DockSet.Player(i >= candidates.Count / 2, content.BattleWalkFor(c.Id), content.BattleSpriteFor(c.Id),
                    walkHeight: walkHeight, walkLift: walkHeight * (.5f - pose.sideInset) - lift);
                strides[i] = EntranceWalkPose.MotionFor(c.Profession).Stride;
                shadows[i] = Shadow(foot, .8f, .36f);
                var stand = foot + new Vector3(0, placardHeight * .5f, -.28f);
                placards[i] = Flat("Price placard " + i, stand, placardWidth, placardHeight, placardArt, Surface.ScenicFlat);
            }
            Animate(0);
        }

        internal void Animate(float time)
        {
            int frame = AnimationCels.Loop(time, 1.2);
            if (frame != lastFrame)
            {
                // The painted cels are centred on the boots, so the performer stays planted.
                PoseBroker(broker, frame);
            }
            for (int i = 0; i < actors.Length; i++)
            {
                if (departures[i] is { } departure)
                {
                    AnimateDeparture(i, departure, time);
                    continue;
                }
                if (frame == lastFrame || !animated[i] || !actors[i].enabled) continue;
                int cel = (frame + 2*i) % AnimationCels.Count;
                actors[i].GetPropertyBlock(properties);
                properties.SetVector("_MainTex_ST", AnimationCels.UV(cel));
                actors[i].SetPropertyBlock(properties);
            }
            lastFrame = frame;
        }

        internal void ShowBroker(bool visible)
        {
            broker.enabled = brokerShadow.enabled = visible;
        }

        internal void BeginDeparture(int index, float time)
        {
            var actor = actors[index];
            var placard = placards[index].bounds;
            departures[index] = new Departure {
                StartedAt = time, StartingPosition = actor.transform.position,
                PlacardFoot = placard.center - Vector3.up * placard.extents.y, Player = Dressing.Cast[actor]
            };
            Dressing.Cast.Remove(actor);
        }

        private void AnimateDeparture(int index, Departure departure, float time)
        {
            float elapsed = time - departure.StartedAt;
            var board = placards[index].transform;
            float fall = Mathf.Clamp01(elapsed / PlacardFallTime);
            var tilt = Quaternion.Euler(-87f * fall * fall, 0, 0);
            board.SetPositionAndRotation(departure.PlacardFoot + tilt * (Vector3.up * (board.localScale.y * .5f)), tilt);

            float walking = elapsed - WalkDelay;
            if (walking < 0) return;
            var actor = actors[index];
            bool synthesized = departure.Player.Walk == null;
            var walk = departure.Player.Walk ?? departure.Player.Idle;
            if (walk != null)
            {
                int cel = AnimationCels.Loop(walking, 6f / 13f);
                if (cel != departure.WalkCel)
                {
                    if (departure.WalkCel < 0)
                    {
                        float height = departure.Player.WalkHeight;
                        float ratio = (float)walk.width / walk.height * (synthesized ? 4f / AnimationCels.Count : 1f);
                        actor.transform.localScale = new Vector3(height * ratio, height, 1);
                        if (synthesized)
                            actor.GetComponent<MeshFilter>().sharedMesh = walkMesh ??= ShopStage.MakeRecruitWalkMesh();
                    }
                    departure.WalkCel = cel;
                    actor.GetPropertyBlock(properties);
                    properties.SetTexture("_MainTex", walk);
                    // The travel cels face right on the sheet.
                    properties.SetVector("_MainTex_ST", synthesized ? AnimationCels.UV(0, AnimationCels.Count, 4, true) :
                        AnimationCels.UV(cel, mirror: true));
                    if (synthesized)
                    {
                        float phase = cel * (2f * Mathf.PI / AnimationCels.Count);
                        properties.SetVector("_Walk", new Vector4(Mathf.Cos(phase), Mathf.Sin(phase), -1, strides[index]));
                    }
                    actor.SetPropertyBlock(properties);
                }
            }

            // Step past the row of placards before crossing in front of the other candidates.
            float step = Mathf.Clamp01(walking / StepIntoAisleTime);
            var offset = new Vector3(-.27f * step - WalkSpeed * Mathf.Max(0, walking - StepIntoAisleTime), 0, -.86f * step);
            var position = departure.StartingPosition + offset;
            position.y += walk != null ? departure.Player.WalkLift : .04f * Mathf.Abs(Mathf.Sin(walking * 20f));
            actor.transform.position = position;
            var shadow = shadows[index].transform;
            shadow.position = new Vector3(position.x, shadow.position.y, position.z);

            if (camera.WorldToViewportPoint(actor.bounds.max).x > 0) return;
            actor.enabled = false;
            shadows[index].enabled = false;
            placards[index].enabled = false;
            departures[index] = null;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (walkMesh != null) UnityEngine.Object.Destroy(walkMesh);
        }

        internal void PlaceCandidate(VisualElement button, Label price, int index)
        {
            var bounds = actors[index].bounds;
            float halfWidth = Spacing * .48f;
            Pin(button, new Vector3(bounds.center.x - halfWidth, bounds.max.y, bounds.center.z),
                new Vector3(bounds.center.x + halfWidth, bounds.min.y - .25f, bounds.center.z));

            var placard = placards[index].bounds;
            // Keep the price inside the parchment face and above the wooden feet.
            float faceHalfWidth = placard.size.x * .37f;
            Pin(price,
                new Vector3(placard.center.x - faceHalfWidth, placard.max.y - placard.size.y * .2f, placard.center.z),
                new Vector3(placard.center.x + faceHalfWidth, placard.min.y + placard.size.y * .25f, placard.center.z));
        }
    }
}
