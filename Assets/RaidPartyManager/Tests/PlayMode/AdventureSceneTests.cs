using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Battle;
using RaidPartyManager.Presentation;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

namespace RaidPartyManager.Tests
{
    public sealed class AdventureSceneTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly System.Type Flow = typeof(AdventureEventHub).Assembly.GetType("RaidPartyManager.Presentation.AdventureFlow");
        private static readonly PropertyInfo RecruitmentWheelVisibility = typeof(DockShop).GetProperty("RecruitmentWheelVisible", PrivateStatic);
        private AdventureRun run;
        private string savePath;

        [SetUp]
        public void Reset()
        {
            ResetAdventureFlow();
            savePath = Path.Combine(Application.temporaryCachePath, $"raid-test-{System.Guid.NewGuid():N}.json");
            Flow.GetField("checkpointPathOverride", PrivateStatic).SetValue(null, savePath);
            typeof(BattlePrototype).GetMethod("ResetPlaySession", PrivateStatic).Invoke(null, null);
        }

        [TearDown]
        public void ClearSceneContext()
        {
            RecruitmentWheelVisibility.SetValue(null, false);
            Flow.GetMethod("ClearSceneContext", PrivateStatic).Invoke(null, null);
            Flow.GetField("checkpointPathOverride", PrivateStatic).SetValue(null, null);
            File.Delete(savePath);
            File.Delete(savePath + ".tmp");
        }

        private static void ResetAdventureFlow() => Flow.GetMethod("Reset", PrivateStatic).Invoke(null, null);

        private static AdventureRun CurrentRoute() => (AdventureRun)typeof(AdventureEventHub).GetField("run", Private)
            .GetValue(Object.FindAnyObjectByType<AdventureEventHub>());

        private static void SaveAndExit(VisualElement root)
        {
            Press(root, "open-settings");
            Press(root.Q("run-settings"), "save-and-exit");
        }

        private static void ConfirmFirstPair(VisualElement root)
        {
            var candidates = root.Q("starter-candidates").Query<Button>().ToList();
            Press(root, candidates[0].name);
            Press(root, candidates[1].name);
            Press(root, "confirm-starters");
            Assert.That(root.Q("crew-contract"), Is.Not.Null);
            Press(root, "sign-starters");
        }

        private IEnumerator OpenMenu()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-events"); yield return WaitScene("EventSelect");
            run = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private).GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
        }

        [UnityTest]
        public IEnumerator TownFlyBeamStaysAboveTheFrameAsTheAspectChanges()
        {
            yield return OpenMenu();
            Press(Root<AdventureEventHub>(), "inspect-node-Town");
            yield return WaitScene("Town");
            yield return WaitForShow();
            var town = Object.FindAnyObjectByType<DockTown>();
            var set = town.transform.Find("Service theatre");
            var beam = set.Find("Town sign fly beam").GetComponent<MeshRenderer>();
            var ropes = set.GetComponentsInChildren<MeshRenderer>().Where(m => m.name == "Sign cord").ToArray();
            Assert.That(ropes.Length, Is.EqualTo(8));
            var camera = Camera.main;
            try
            {
                foreach (float aspect in new[] { 4f / 3, 16f / 9, 21f / 9, 9f / 16 })
                {
                    camera.aspect = aspect;
                    yield return null;
                    var bounds = beam.bounds;
                    Assert.That(camera.WorldToViewportPoint(new Vector3(0, bounds.min.y, bounds.max.z)).y,
                        Is.GreaterThan(1.04f), "The underside must be above the top of the frame.");
                    foreach (var rope in ropes)
                    {
                        Assert.That(rope.bounds.max.y, Is.EqualTo(bounds.min.y).Within(.01f));
                        Assert.That(camera.WorldToViewportPoint(rope.bounds.min).y, Is.InRange(.5f, .92f));
                    }
                }
            }
            finally { camera.ResetAspect(); }
        }

        [UnityTest]
        public IEnumerator BrokerDialogueSwapsBustsAndClosesAfterThreeRejectedOffers()
        {
            yield return OpenMenu();
            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            Press(Root<DockTown>(), "town-service-Recruit"); yield return WaitScene("TalentMarket");
            var root = Root<TalentMarket>();
            Assert.That(root.Q<Button>("market-broker").text, Is.EqualTo("要做比交易吗？"));
            Press(root, "market-broker");
            var portrait = root.Q<Image>("broker-portrait");
            yield return null;
            Assert.That(portrait.image.name, Is.EqualTo("BrokerPortraits"));
            Assert.That(portrait.worldBound.center.x, Is.GreaterThan(root.worldBound.center.x));
            Assert.That(root.Q("broker-options").worldBound.center.x, Is.LessThan(root.worldBound.center.x));
            Assert.That(portrait.worldBound.height, Is.GreaterThan(root.worldBound.height * .6f));
            Press(root, "broker-member-" + run.Party.Members[0].Id);
            var asking = root.Q<IntegerField>("broker-asking");
            int opening = asking.value;
            Press(root, "broker-raise-15");
            Assert.That(asking.value, Is.GreaterThan(opening));
            int gentle = asking.value;
            Press(root, "broker-raise-45");
            Assert.That(asking.value, Is.GreaterThan(gentle));
            Assert.That(run.Current.BrokerRefusals, Is.Zero);
            for (int i = 0; i < 3; i++)
            {
                var previous = portrait.sourceRect;
                root.Q<IntegerField>("broker-asking").value = 100000;
                Press(root, "broker-offer");
                Assert.That(portrait.sourceRect, Is.Not.EqualTo(previous));
            }
            Assert.That(root.Q("broker-asking"), Is.Null);
            Press(root, "broker-leave");
            Assert.That(root.Q("market-dialogue"), Is.Null);
            Assert.That(root.Q<Button>("market-broker").enabledInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator MarketShowsEightPerformersAndSignsMultipleCandidatesBeforeReturning()
        {
            yield return OpenMenu();
            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            var candidates = run.Current.Recruits.Select(c => c.Id).ToArray();
            int firstPrice = run.Party.SigningPrice(run.Current.Recruits[0]);
            int secondPrice = run.Party.SigningPrice(run.Current.Recruits[1]);
            run.Party.AddGold(firstPrice + secondPrice + 250);
            int day = run.Party.Day, balance = run.Party.Gold;
            int members = run.Party.Members.Count;
            Press(Root<DockTown>(), "town-service-Recruit");
            yield return CheckMarketTurn(false);
            var root = Root<TalentMarket>();
            Press(root, "close-town-recruit");
            yield return CheckMarketTurn(true);
            Assert.That(run.CanUseTown(TownService.Recruit), Is.True);
            Press(Root<DockTown>(), "town-service-Recruit");
            yield return CheckMarketTurn(false);
            root = Root<TalentMarket>();
            Assert.That(run.Current.Recruits.Select(c => c.Id), Is.EqualTo(candidates));
            Assert.That(run.Party.Day, Is.EqualTo(day));
            Assert.That(run.Party.Gold, Is.EqualTo(balance));
            var entries = root.Query<Button>().ToList().Where(b => b.name?.StartsWith("town-recruit-") == true).ToArray();
            Assert.That(entries.Length, Is.EqualTo(8));
            var theatre = Object.FindAnyObjectByType<TalentMarket>().transform.Find("Service theatre");
            Assert.That(theatre.Find("Registry desk"), Is.Not.Null);
            Assert.That(theatre.Find("Contract board"), Is.Not.Null);
            var bodies = Enumerable.Range(0, 8).Select(i => theatre.Find("Market performer " + i).GetComponent<MeshRenderer>()).ToArray();
            var placards = Enumerable.Range(0, 8).Select(i => theatre.Find("Price placard " + i).GetComponent<MeshRenderer>()).ToArray();
            Assert.That(bodies.All(b => b.enabled), Is.True);
            var depth = bodies[0].bounds.center.z;
            Assert.That(depth, Is.LessThan(-4.65f));
            Assert.That(bodies.All(b => Mathf.Approximately(b.bounds.center.z, depth)), Is.True);
            Assert.That(bodies.All(b => Mathf.Abs(b.bounds.min.y) < .06f), Is.True);
            Assert.That(bodies.Select(b => b.bounds.center.x), Is.Ordered.Ascending);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (root.Q("curtain-input-shield") != null && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            for (int i = 0; i < entries.Length; i++)
            {
                var bounds = bodies[i].bounds;
                var price = root.Q<Label>("town-price-" + candidates[i]);
                Assert.That(price.text, Is.EqualTo($"价格 {run.Party.SigningPrice(run.Current.Recruits[i])}"));
                Assert.That(placards[i].bounds.center.z, Is.LessThan(bounds.center.z));
                Assert.That(placards[i].bounds.center.x, Is.EqualTo(bounds.center.x));
                Assert.That(Camera.main.WorldToViewportPoint(bounds.min).x, Is.GreaterThan(0f));
                Assert.That(Camera.main.WorldToViewportPoint(bounds.max).x, Is.LessThan(1f));
                var centre = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, bounds.center, Camera.main);
                var target = root.panel.Pick(centre);
                Assert.That(target == entries[i] || entries[i].Contains(target), Is.True, entries[i].name);
                if (i > 0) Assert.That(entries[i - 1].worldBound.xMax, Is.LessThanOrEqualTo(entries[i].worldBound.xMin));
            }
            var candidate = run.Current.Recruits[0];
            var profile = root.Q<Label>("market-candidate-profile");
            yield return null;
            foreach (var entry in entries)
            {
                using var departure = PointerLeaveEvent.GetPooled(new Event { type = EventType.MouseMove });
                Send(entry, departure);
            }
            Assert.That(profile.style.display.value, Is.EqualTo(DisplayStyle.None));
            using (var enter = PointerEnterEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = entries[0].worldBound.center }))
                Send(entries[0], enter);
            Assert.That(profile.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(profile.text, Does.Contain(candidate.Name).And.Contain("生命"));
            Assert.That(profile.text, Does.Not.Contain("身价"));
            using (var leave = PointerLeaveEvent.GetPooled(new Event { type = EventType.MouseMove }))
                Send(entries[0], leave);
            Assert.That(profile.style.display.value, Is.EqualTo(DisplayStyle.None));
            Press(root, entries[0].name);
            Assert.That(root.Q("contract-paper").Query<Label>().ToList().Any(l => l.text.Contains($"人才市场转会费　{firstPrice} 金币")), Is.True);
            root.Q<IntegerField>().value = 0;
            Assert.That(root.Q<Button>("sign-crew-contract").enabledInHierarchy, Is.False);
            root.Q<IntegerField>("contract-duration").value = 15;
            yield return null;
            Assert.That(root.Q("contract-paper").resolvedStyle.backgroundImage.texture, Is.Not.Null);
            var kill = root.Q<Toggle>("bonus-kill");
            var amount = root.Q<IntegerField>("bonus-kill-amount");
            Assert.That(amount.enabledInHierarchy, Is.False);
            kill.value = true;
            Assert.That(root.Q<Button>("sign-crew-contract").enabledInHierarchy, Is.False);
            amount.value = 9;
            root.Q<Toggle>("bonus-appearance").value = true;
            root.Q<IntegerField>("bonus-appearance-amount").value = 4;
            root.Q<Toggle>("bonus-appearance").value = false;
            var feeField = root.Q<LongField>("contract-signing-fee");
            int agreedWage = CrewContract.ExpectedWage(candidate, run.Party.TalentFor(candidate),
                trainingSessions: run.Party.TownCandidateTraining(candidate).Sessions);
            int minimum = CrewContract.MinimumWage(agreedWage, new PerformanceBonuses { Kill = 9 },
                EmployeeAttributes.Starting);
            feeField.value = (minimum - 1) * 15;
            if (minimum > 1)
            {
                Press(root, "sign-crew-contract");
                Assert.That(root.Q<Label>("contract-preview").text, Does.Contain("拒绝"));
            }
            else Assert.That(root.Q<Button>("sign-crew-contract").enabledInHierarchy, Is.False);
            Assert.That(run.Current.Recruits.Any(c => c.Id == candidate.Id), Is.True);
            feeField.value = agreedWage * 15;
            Vector3 firstMark = bodies[0].transform.position;
            Press(root, "sign-crew-contract");
            var signed = run.Party.Members.Single(m => m.Character.Id == candidate.Id).Contract;
            Assert.That(signed.DailyWage, Is.EqualTo(agreedWage));
            Assert.That(signed.AgreedRates.Kill, Is.EqualTo(9));
            Assert.That(signed.AgreedRates.Appearance, Is.Zero);
            Assert.That(run.Party.Members.Single(m => m.Character.Id == candidate.Id).Contract.EndDay, Is.EqualTo(run.Party.Day + 15));
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("TalentMarket"));
            Assert.That(root.Q("crew-contract"), Is.Null);
            Assert.That(root.Q<Button>("town-recruit-" + candidate.Id), Is.Null);
            Assert.That(root.Q<Label>("town-price-" + candidate.Id), Is.Null);
            Assert.That(bodies[0].enabled, Is.True);
            Assert.That(placards[0].enabled, Is.True);
            Assert.That(root.Q<Label>("run-gold").text, Does.Contain((balance - firstPrice - signed.BaseTotal).ToString()));

            Press(root, "town-recruit-" + candidates[1]);
            Press(root, "close-contract");
            Assert.That(root.Q("crew-contract"), Is.Null);
            Assert.That(bodies[1].enabled, Is.True);
            Assert.That(run.Party.Members.Count, Is.EqualTo(members + 1));
            Press(root, "town-recruit-" + candidates[1]);
            Assert.That(root.Q<IntegerField>("contract-duration").value, Is.EqualTo(12));
            root.Q<Toggle>("installment-enabled").value = true;
            root.Q<IntegerField>("installment-count").value = 3;
            root.Q<IntegerField>("installment-days").value = 7;
            Assert.That(root.Q<Button>("sign-crew-contract").enabledInHierarchy, Is.False);
            root.Q<IntegerField>("installment-days").value = 4;
            Assert.That(root.Q("installment-amount"), Is.Null);
            Assert.That(root.Q<Label>("contract-payment-schedule").text, Does.Contain($"第 {day + 4} 天"));
            var second = run.Current.Recruits.First(c => c.Id == candidates[1]);
            const int deposit = 2;
            root.Q<LongField>("contract-signing-fee").value = deposit;
            Press(root, "sign-crew-contract");
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("TalentMarket"));
            Assert.That(root.Q("crew-contract"), Is.Null);
            Assert.That(root.Q<Button>("town-recruit-" + candidates[1]), Is.Null);
            Assert.That(root.Q<Button>("town-recruit-" + candidates[2]), Is.Not.Null);
            Assert.That(bodies[1].enabled, Is.True);
            Assert.That(run.Current.Recruits.Select(c => c.Id), Is.EqualTo(candidates.Skip(2)));
            Assert.That(run.Party.Members.Count, Is.EqualTo(members + 2));
            Assert.That(run.Party.Gold, Is.EqualTo(balance - firstPrice - secondPrice - signed.BaseTotal -
                deposit));
            Assert.That(run.Party.Members.Single(m => m.Character.Id == candidates[1]).Contract.NextDueDay, Is.EqualTo(day + 4));
            Press(root, "run-wallet");
            var ledger = root.Q("expense-ledger").Query<Label>().ToList();
            Assert.That(ledger.Any(label => label.text.Contains(second.Name + " · 签字费") && label.text.Contains("−" + deposit)), Is.True);
            Press(root, "close-ledger");
            Assert.That(run.Party.Day, Is.EqualTo(day));
            var market = Object.FindAnyObjectByType<TalentMarket>();
            var encounter = ((AdventureContentAsset)typeof(TalentMarket).GetField("assets", Private).GetValue(market)).Encounter;
            bool synthesized = encounter.BattleWalkFor(candidate.Id) == null;
            var travel = encounter.BattleWalkFor(candidate.Id) ?? encounter.BattleSpriteFor(candidate.Id);
            var ink = new MaterialPropertyBlock();
            float exitDeadline = Time.realtimeSinceStartup + 6f;
            bool sawFall = false, sawWalk = false, sawNextCel = false;
            Vector4 firstCel = default;
            while (bodies[0].enabled && Time.realtimeSinceStartup < exitDeadline)
            {
                float angle = Vector3.Angle(placards[0].transform.up, Vector3.up);
                sawFall |= angle > 0 && angle < 87f;
                bodies[0].GetPropertyBlock(ink);
                var uv = ink.GetVector("_MainTex_ST");
                var cel = synthesized ? ink.GetVector("_Walk") : uv;
                if (travel != null && ink.GetTexture("_MainTex") == travel && uv.x < 0 &&
                    bodies[0].transform.position.x < firstMark.x && bodies[0].transform.position.z < firstMark.z)
                {
                    if (!sawWalk) firstCel = cel;
                    sawWalk = true;
                    sawNextCel |= cel != firstCel;
                }
                yield return null;
            }
            Assert.That(sawFall, Is.True, "The signed candidate's placard should visibly tip.");
            Assert.That(sawWalk && sawNextCel, Is.True, "The signed candidate should take multiple steps facing left.");
            Assert.That(bodies[0].enabled, Is.False);
            Assert.That(Camera.main.WorldToViewportPoint(bodies[0].bounds.max).x, Is.LessThanOrEqualTo(0));
            Assert.That(placards[0].enabled, Is.False);
            Assert.That(bodies[2].enabled && placards[2].enabled, Is.True);
            Press(root, "close-town-recruit");
            yield return CheckMarketTurn(true);
            root = Root<DockTown>();
            Assert.That(root.Q<Button>("town-service-Recruit").enabledInHierarchy, Is.True);
            var guard = run.Party.Members.First(m => m.Position.HasValue);
            Assert.That(run.Party.TrySendAway(guard.Id), Is.True);
            typeof(PartyRun).GetProperty(nameof(PartyRun.Day)).SetValue(run.Party, guard.Contract.EndDay - 3);
            Press(root, "open-town-editor");
            Assert.That(root.Q("editor-positions").childCount, Is.EqualTo(4));
            Assert.That(root.Q("reserve-positions").childCount, Is.EqualTo(4));
            Assert.That(root.Q("contract-remaining"), Is.Null);
            Press(root, "roster-display");
            var reminders = root.Query<Label>("contract-remaining").ToList();
            Assert.That(reminders.Count, Is.EqualTo(8));
            var expiring = reminders.First(row => row.text == "合约剩余 3 天");
            Assert.That((Color32)expiring.style.color.value, Is.EqualTo(new Color32(255, 115, 103, 255)));
            Assert.That(reminders.Any(row => row.text.StartsWith("合约剩余") && row.text != expiring.text &&
                row.style.color.value != expiring.style.color.value), Is.True);
            Press(root, "roster-display");
            Assert.That(root.Q("contract-remaining"), Is.Null);
            var teamButton = root.Q<Button>("open-town-editor");
            Assert.That(teamButton.tooltip, Does.Contain(guard.Character.Name));
            Assert.That(teamButton.style.backgroundColor.value.r, Is.GreaterThan(teamButton.style.backgroundColor.value.g));
            var reserve = root.Q<Button>("edit-member-" + guard.Id);
            using (var right = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = reserve.worldBound.center }))
                Send(reserve, right);
            Assert.That(root.Q<Button>("sign-crew-contract").text, Is.EqualTo("签署续约合同"));
            root.Q<LongField>("contract-signing-fee").value = 12 * CrewContract.ExpectedWage(guard.Character, guard.Trait, guard.LearnedSkills);
            Press(root, "sign-crew-contract");
            Assert.That(teamButton.tooltip, Does.Not.Contain(guard.Character.Name));
            Assert.That(run.Party.Members.Single(m => m.Id == guard.Id).ContractEndDay, Is.EqualTo(guard.Contract.EndDay + 12));
        }

        private static IEnumerator CheckMarketTurn(bool returning)
        {
            float until = Time.realtimeSinceStartup + 10f;
            float roll = 0;
            while (Mathf.Abs(roll) < .25f && Time.realtimeSinceStartup < until)
            {
                yield return null;
                var cloth = GameObject.Find("Travelling backcloth");
                if (cloth != null) roll = cloth.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Roll");
            }
            Assert.That(returning ? -roll : roll, Is.GreaterThanOrEqualTo(.25f));
            var shaft = GameObject.Find("Semicircular deck shaft").transform;
            Assert.That(returning ? -shaft.right.y : shaft.right.y, Is.GreaterThan(0));
            yield return WaitForShow();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(returning ? "Town" : "TalentMarket"));
            if (!returning) yield return WaitForRoadstone(Root<TalentMarket>(), "town-return-sign");
        }

        [UnityTest]
        public IEnumerator ClockworkCapitalOpensTheCityEventAndBattleStages()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-FareGate");
            yield return null;
            Assert.That(GameObject.Find("Service theatre/Terminus"), Is.Not.Null);
            Assert.That(root.Q<Label>("event-description").text, Does.Contain("9 金币"));
            var visit = CurrentRoute();
            int before = visit.Party.Gold;
            Press(root, "capital-1");
            Assert.That(visit.Party.Gold, Is.EqualTo(before + 9));
            Assert.That(visit.Current.Resolved, Is.True);
            ResetAdventureFlow();
            yield return SceneManager.LoadSceneAsync("ClockworkCapital");
            yield return null;
            Assert.That(Object.FindAnyObjectByType<BattlePrototype>(), Is.Not.Null);
            Assert.That(GameObject.Find("Cyclorama"), Is.Not.Null);
            var floor = Resources.Load<Texture2D>("Clockwork/IronDeck");
            Assert.That(floor.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(floor.wrapModeV, Is.EqualTo(TextureWrapMode.Repeat));
            Press(Root<BattlePrototype>(), "start");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BothWardPatientsWalkToTheirOwnBedBeforeTheHealthPreview()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-SoulWard");
            yield return WaitForShow();
            var run = CurrentRoute();
            var members = run.Party.Members.Take(2).ToArray();
            Assert.That(GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(r => r.name == "Linen mattress"), Is.EqualTo(2));
            foreach (var member in members)
            {
                Press(root, "grave-member-" + member.Id);
                var patient = GameObject.Find("Service theatre/Ward patient " + member.Id).GetComponent<MeshRenderer>();
                var start = patient.transform.position;
                var paint = new MaterialPropertyBlock();
                var lying = Resources.Load<Texture2D>("WardPatients/" + member.Character.RosterNumber);
                Assert.That(lying, Is.Not.Null);
                float timeout = Time.realtimeSinceStartup + 6;
                do
                {
                    yield return null;
                    patient.GetPropertyBlock(paint);
                } while ((paint.GetTexture("_MainTex") != lying || root.Q<Button>("grave-pass") == null) && Time.realtimeSinceStartup < timeout);
                Assert.That(paint.GetTexture("_MainTex"), Is.SameAs(lying));
                Assert.That(Vector3.Distance(start, patient.transform.position), Is.GreaterThan(1));
                Assert.That(run.Party.Members.First(m => m.Id == member.Id).Health, Is.EqualTo(member.Health));
            }
            Assert.That(root.Q<Button>("grave-stitch"), Is.Not.Null);
            Press(root, "grave-stitch");
            Assert.That(run.Current.Resolved, Is.True);
        }

        [UnityTest]
        public IEnumerator GravePressAndFemaleStorytellerRevealTheSelectedOutcome()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-Obituary");
            yield return null;
            var theatre = Object.FindAnyObjectByType<AdventureEventHub>().transform.Find("Service theatre");
            var actor = theatre.Find("Dock storyteller").GetComponent<MeshRenderer>();
            var press = theatre.Find("Event prop Obituary").GetComponent<MeshRenderer>();
            var costume = new MaterialPropertyBlock();
            actor.GetPropertyBlock(costume);
            Assert.That(costume.GetTexture("_MainTex").name, Is.EqualTo("GraveChronicler"));
            Assert.That(Resources.Load<Texture2D>("StoryEvents/Storyteller"), Is.Null);
            Press(root, "grave-retract");
            var frames = new HashSet<int>();
            var gestures = new HashSet<int>();
            var props = new MaterialPropertyBlock();
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && root.Q<Button>("finish-event") == null)
            {
                press.GetPropertyBlock(props);
                actor.GetPropertyBlock(costume);
                frames.Add(Cel(props));
                gestures.Add(Cel(costume));
                yield return null;
            }
            int Cel(MaterialPropertyBlock paint)
            {
                Vector4 uv = paint.GetVector("_MainTex_ST");
                return (3 - Mathf.RoundToInt(4 * uv.w)) * 4 + Mathf.RoundToInt(4 * uv.z);
            }
            Assert.That(frames, Does.Contain(15));
            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(gestures.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(CurrentRoute().Current.Message, Does.Contain("抽掉了姓名"));
            Assert.That(root.Q<Button>("finish-event"), Is.Not.Null);
            Press(root, "finish-event");
            yield return WaitForEventExit(root);
        }

        [UnityTest]
        public IEnumerator AllEventWindowsAndIndependentTownCampShopScenesWorkFromTheMenu()
        {
            yield return OpenMenu();
            foreach (AdventureEventKind kind in System.Enum.GetValues(typeof(AdventureEventKind)))
            {
                var root = Root<AdventureEventHub>();
                Press(root, "inspect-event-" + kind);
                yield return null;
                Assert.That(root.Q("event-window"), Is.Not.Null, kind.ToString());
                var speech = root.Q("storyteller-speech");
                var description = speech.Q<Label>("event-description");
                var options = root.Q("event-options");
                Assert.That(root.Q("event-illustration"), Is.Null);
                Assert.That(root.Q("event-panel"), Is.Null);
                Assert.That(description.text.Length, Is.GreaterThan(10));
                var theatre = Object.FindAnyObjectByType<AdventureEventHub>().transform.Find("Service theatre");
                var storyteller = theatre.Find("Dock storyteller").GetComponent<MeshRenderer>();
                var costume = new MaterialPropertyBlock();
                storyteller.GetPropertyBlock(costume);
                string voice = CapitalStories.Contains(kind) ? CapitalStories.For(kind).District switch
                    { 0 => "LostTraveler", 1 => "Forge", _ => "Investor" } : GraveStories.Contains(kind) ? "GraveChronicler" : kind.ToString();
                Assert.That(costume.GetTexture("_MainTex"), Is.SameAs(Resources.Load<Texture2D>("StoryEvents/Cast/" + voice)));
                if (kind == AdventureEventKind.Auction)
                {
                    Assert.That(options, Is.Null);
                    Assert.That(root.Q<Button>("close-event"), Is.Null);
                    var chestArt = new System.Collections.Generic.HashSet<Texture>();
                    var ink = new MaterialPropertyBlock();
                    for (int slot = 0; slot < 3; slot++)
                    {
                        theatre.Find("Auction case " + slot).GetComponent<MeshRenderer>().GetPropertyBlock(ink);
                        chestArt.Add(ink.GetTexture("_MainTex"));
                    }
                    Assert.That(chestArt.Count, Is.EqualTo(3));
                    float closeUpBy = Time.realtimeSinceStartup + 9f;
                    while (!root.Q<Button>("auction-case-0").enabledInHierarchy && Time.realtimeSinceStartup < closeUpBy) yield return null;
                    var first = root.Q<Button>("auction-case-0");
                    Assert.That(root.panel.Pick(first.worldBound.center), Is.SameAs(first));
                    Assert.That(root.panel.Pick(first.worldBound.min + Vector2.one * 2), Is.SameAs(root.Q("auction-floor")));
                    int purse = run.Party.Gold;
                    Assert.That(run.ChooseGrave("buy0"), Is.False);
                    Press(root, "auction-case-0");
                    Assert.That(run.AuctionInspected, Is.Zero);
                    Assert.That(run.Party.Gold, Is.EqualTo(purse));
                    Assert.That(description.text, Does.Contain("下一次"));
                    Press(root, "auction-floor");
                    yield return new WaitForSecondsRealtime(1f);
                    Press(root, "auction-case-1");
                    yield return new WaitForSecondsRealtime(1f);
                    int cost = run.AuctionPriceAt(1);
                    Press(root, "auction-case-1");
                    Assert.That(run.Current.Resolved, Is.True);
                    Assert.That(run.Party.Gold, Is.EqualTo(purse - cost));
                    Assert.That(run.ChooseGrave("buy2"), Is.False);
                    Press(root, "auction-floor");
                    yield return new WaitForSecondsRealtime(1f);
                    Press(root, "auction-floor");
                    yield return WaitForEventExit(root);
                    run = CurrentRoute();
                    continue;
                }
                if (kind == AdventureEventKind.LostTraveler)
                {
                    foreach (var traveler in run.Current.Recruits)
                    {
                        var option = root.Q<Button>("event-profession-" + traveler.Profession);
                        using var hover = PointerEnterEvent.GetPooled(new Event { type = EventType.MouseMove,
                            mousePosition = option.worldBound.center });
                        Send(option, hover);
                        Assert.That(root.Q("character-choice-preview"), Is.Null);
                        Assert.That(speech.Query<Label>().ToList().Any(label => label.text.Contains(traveler.Name)), Is.False);
                    }
                }
                if (kind == AdventureEventKind.SoulWard)
                    Assert.That(theatre.Find("Ward cot frame"), Is.Not.Null);
                else
                {
                    var prop = theatre.Find("Event prop " + kind).GetComponent<MeshRenderer>();
                    var block = new MaterialPropertyBlock();
                    prop.GetPropertyBlock(block);
                    Assert.That(block.GetTexture("_MainTex").name, Is.EqualTo(kind.ToString()));
                }
                if (kind == AdventureEventKind.HatTrick) Assert.That(root.Q("hat-game").Q("hat-2"), Is.Not.Null);
                Assert.That(options.Query<Button>().ToList().All(button => button.GetType().Name == "HangingSign"), Is.True);
                Assert.That(root.Q<Button>("close-event").text, Is.Empty);
                AssertNoVisibleTiers(root);
                if (kind == AdventureEventKind.DeadLetters)
                {
                    Assert.That(description.text, Does.Contain("第一封"));
                    Press(root, "grave-letter0");
                    float deadline = Time.realtimeSinceStartup + 5f;
                    while (root.Q("grave-letter1") == null && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(description.text, Is.EqualTo(run.Current.Message));
                    Assert.That(description.text, Does.Contain("下一封"));
                }
                if (kind == AdventureEventKind.Alchemist)
                {
                    var labels = root.Q("event-options").Query<Label>().ToList().Select(label => label.text).ToArray();
                    foreach (var potion in run.Current.Potions)
                    {
                        Assert.That(labels.Contains(potion.Name), Is.False);
                        Assert.That(labels.Contains(potion.Description), Is.False);
                    }
                }
                Press(root, "close-event");
                yield return WaitForEventExit(root);
                run = CurrentRoute();
            }
            var party = run.Party;
            Press(Root<AdventureEventHub>(), "inspect-node-Camp"); yield return WaitScene("Camp");
            Assert.That(typeof(CampPrototype).GetField("run", Private).GetValue(Object.FindAnyObjectByType<CampPrototype>()), Is.SameAs(party));
            yield return CheckEditorTopBar(Root<CampPrototype>(), "open-camp-editor", "run-gold", party.Gold);
            Press(Root<CampPrototype>(), "camp-rest");
            Assert.That(Root<CampPrototype>().Q<Button>("camp-rest").enabledInHierarchy, Is.False);
            Press(Root<CampPrototype>(), "return-event-select"); yield return WaitScene("EventSelect");

            yield return OpenShopFromInspection();
            var stock = run.Current.Stock;
            int chosen = Enumerable.Range(0, stock.Count).OrderBy(i => stock[i].Price).First();
            int gold = party.Gold, price = stock[chosen].Price;
            int copiesBefore = party.Inventory.Concat(party.SkillBooks).Count(item => item.Definition.Id == stock[chosen].Item.Id);
            Assert.That(Root<DockShop>().Q<Label>("shop-gold").text, Is.EqualTo($"金币  {gold}"));
            var shopRoot = Root<DockShop>();
            Assert.That(shopRoot.Q("shop-recruit-open"), Is.Null);
            Press(shopRoot, "shop-counter");
            yield return new WaitForSecondsRealtime(.56f);
            var offerView = shopRoot.Q("shop-offer-" + chosen);
            Vector2 offerPosition = offerView.worldBound.center;
            Down(offerView);
            yield return new WaitForSecondsRealtime(.65f);
            Up(shopRoot, offerPosition);
            var counterBackdrop = shopRoot.Q("shop-counter-background");
            Down(counterBackdrop);
            Up(shopRoot, offerPosition);
            yield return new WaitForSecondsRealtime(.56f);
            Assert.That(stock[chosen].Sold, Is.True);
            Assert.That(party.Gold, Is.EqualTo(gold - price + (stock[chosen].Item is SkillBookDefinition ? 0 : 2 * party.Members.Count(m => m.Trait == PlayerTrait.Rebate))));
            Assert.That(party.Inventory.Concat(party.SkillBooks).Count(item => item.Definition.Id == stock[chosen].Item.Id), Is.EqualTo(copiesBefore + 1));
            yield return CheckEditorTopBar(shopRoot, "open-shop-editor", "shop-gold", party.Gold);
            AssertNoVisibleTiers(shopRoot);
            Press(shopRoot, "return-event-select"); yield return WaitScene("Town");
            Press(Root<DockTown>(), "return-event-select"); yield return WaitScene("EventSelect");

            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            yield return CheckEditorTopBar(Root<DockTown>(), "open-town-editor", "run-gold", party.Gold);
            yield return WaitForShow(); // The curtain intercepts input until the town entrance finishes.
            var flats = Object.FindAnyObjectByType<DockTown>().transform.Find("Service theatre");
            var trader = new MaterialPropertyBlock();
            flats.Find("Resident 1").GetComponent<MeshRenderer>().GetPropertyBlock(trader);
            Assert.That(trader.GetTexture("_MainTex"), Is.SameAs(Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle")));
            var beforeMotion = new float[4];
            float Cel(int i)
            {
                var block = new MaterialPropertyBlock();
                flats.Find("Resident " + i).GetComponent<MeshRenderer>().GetPropertyBlock(block);
                return block.GetVector("_MainTex_ST").z;
            }
            for (int i=0;i<4;i++)
            {
                var building = flats.Find("Facility " + i);
                var placard = flats.Find("Hanging placard " + i);
                Assert.That(placard.position.z, Is.LessThan(building.position.z));
                Assert.That(placard.position.y, Is.GreaterThan(3.5f));
                var service = Root<DockTown>().Q<Button>("town-service-" + (TownService)i);
                Assert.That(service.text, Is.EqualTo(new[] { "人才市场", "商店", "营地", "委托" }[i]));
                Assert.That(service.panel.Pick(service.worldBound.center), Is.SameAs(service));
                Assert.That(flats.Find("Resident " + i).position.z, Is.LessThan(building.position.z));
                beforeMotion[i] = Cel(i);
            }
            var moved = new bool[4];
            float until = Time.realtimeSinceStartup + 1.6f;
            while (Time.realtimeSinceStartup < until && moved.Any(v => !v))
            {
                yield return null;
                for (int i=0;i<4;i++) moved[i] |= Cel(i) != beforeMotion[i];
            }
            Assert.That(moved.All(v => v), Is.True);
            Press(Root<DockTown>(), "town-service-Camp"); yield return WaitScene("Camp");
            Press(Root<CampPrototype>(), "return-event-select"); yield return WaitScene("Town");
            Assert.That(run.CanUseCamp, Is.True);
            Press(Root<DockTown>(), "town-service-Camp"); yield return WaitScene("Camp");
            var trainee = run.TrainingCandidates.First();
            Press(Root<CampPrototype>(), "camp-train");
            Press(Root<CampPrototype>(), "train-" + trainee.Id);
            Assert.That(run.Party.Members.Single(m => m.Id == trainee.Id).Training.Sessions,
                Is.EqualTo(trainee.Training.Sessions + 1));
            Assert.That(Root<CampPrototype>().Q<Button>("camp-rest").enabledInHierarchy, Is.False);
            yield return WaitForRoadstone(Root<CampPrototype>(), "town-return-sign");
            Press(Root<CampPrototype>(), "return-event-select"); yield return WaitScene("Town");
            Assert.That(run.Current.UsedServices.Count, Is.EqualTo(1));
            yield return WaitForShow();
            var facilities = Root<DockTown>();
            Assert.That(facilities.Q<Button>("town-service-Camp").enabledInHierarchy, Is.False);
            Press(facilities, "town-service-Quest");
            yield return null;
            var parchment = facilities.Q<ScrollView>("contract-paper");
            var back = facilities.Q<Button>("close-town-quest");
            Assert.That(parchment.resolvedStyle.backgroundImage.texture, Is.Not.Null);
            Assert.That(back.worldBound.center.x, Is.LessThan(parchment.worldBound.center.x));
            Assert.That(back.worldBound.center.y, Is.LessThan(parchment.worldBound.center.y));
            Assert.That(back.panel.Pick(back.worldBound.center), Is.SameAs(back));
            Press(facilities, "close-town-quest");
            Assert.That(run.QuestActive, Is.False);
            Press(facilities, "town-service-Quest");
            Assert.That(facilities.Q("town-quest-board").Query<Button>().ToList().Count(button => button.name?.StartsWith("town-quest-offer-") == true), Is.EqualTo(3));
            Press(facilities, "town-quest-offer-1");
            Assert.That(run.QuestActive, Is.True);
            Assert.That(facilities.Q<Button>("town-quest-act").enabledInHierarchy, Is.False);
            yield return null;
            Press(facilities, "close-town-quest");
            Assert.That(facilities.Q<Button>("town-service-Recruit").enabledInHierarchy, Is.True);
            var townStock = run.Current.Stock;
            for (int visit = 0; visit < 2; visit++)
            {
                Press(Root<DockTown>(), "town-service-Shop"); yield return WaitScene("DockShop");
                Assert.That(run.Current.Stock, Is.SameAs(townStock));
                Press(Root<DockShop>(), "return-event-select"); yield return WaitScene("Town");
            }
            Press(Root<DockTown>(), "return-event-select"); yield return WaitScene("EventSelect");
            Assert.That(run.Current, Is.Null);

            int contractReward = run.TownQuestGold;
            typeof(AdventureRun).GetProperty(nameof(AdventureRun.QuestProgress)).SetValue(run, run.Commission.Target);
            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            var town = Root<DockTown>();
            int beforeReward = party.Gold;
            Press(town, "town-service-Quest");
            Press(town, "town-quest-act");
            Assert.That(party.Gold, Is.EqualTo(beforeReward + contractReward));
            Assert.That(town.Q("reward-flight").Q("reward-gold-icon"), Is.Not.Null);
            Assert.That(town.Q<Label>("run-gold").text, Is.EqualTo($"金币  {beforeReward}"));
            Press(town, "close-town-quest");
            yield return new WaitUntil(() => town.Q<Button>("return-event-select") != null);
            Press(town, "return-event-select");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Town"));
            yield return WaitScene("EventSelect");
            Assert.That(Root<AdventureEventHub>().Q<Label>("adventure-gold").text, Is.EqualTo($"金币  {beforeReward + contractReward}"));
        }

        [UnityTest]
        public IEnumerator CampOffersEvolutionRestAndTrainingAsExclusiveActions()
        {
            yield return OpenMenu();
            Press(Root<AdventureEventHub>(), "inspect-node-Camp");
            yield return WaitScene("Camp");
            var root = Root<CampPrototype>();
            var actions = new[] { "camp-evolve", "camp-rest", "camp-train" };
            Assert.That(root.Q("camp-recruit"), Is.Null);
            Press(root, "camp-evolve");
            Press(root, "close-camp-choice");
            Assert.That(actions.All(id => root.Q<Button>(id).enabledInHierarchy), Is.True);
            var candidate = run.EvolutionCandidates.First();
            var form = run.EvolutionForms(candidate.Id).Last();
            Press(root, "camp-evolve");
            yield return null;
            AssertCharacterPreview(root, "pick-evolution-member-" + candidate.Id, candidate.Character);
            Press(root, "pick-evolution-member-" + candidate.Id);
            Assert.That(run.Current.Resolved, Is.False);
            yield return null;
            string branch = "evolve-" + candidate.Id + "-" + form.Id;
            AssertCharacterPreview(root, branch, form);
            Press(root, branch);
            Assert.That(root.Q("character-choice-preview"), Is.Null);
            var evolved = run.Party.Members.Single(member => member.Id == candidate.Id);
            Assert.That(evolved.Character.Id, Is.EqualTo(form.Id));
            Assert.That(evolved.Health, Is.EqualTo(Mathf.Min(candidate.Health, evolved.EffectiveStats.Health)));
            Assert.That(run.Current.Resolved, Is.True);
            Assert.That(actions.All(id => !root.Q<Button>(id).enabledInHierarchy), Is.True);
        }

        [UnityTest]
        public IEnumerator ShopStagesDrawnActorsAndSignsOneByDraggingToTheCounter()
        {
            RecruitmentWheelVisibility.SetValue(null, true);
            yield return OpenMenu();
            yield return OpenShopFromInspection();
            var root = Root<DockShop>();
            run.Party.AddGold(120);
            int startingGold = run.Party.Gold;
            var member = run.Party.Members.Last();
            var camera = Camera.main;
            var wide = camera.transform.position;
            Press(root, "shop-recruit-open");
            yield return new WaitForSecondsRealtime(.52f);
            Assert.That(camera.transform.position.z, Is.GreaterThan(wide.z + 1.7f));
            Assert.That(root.Q("shop-recruit-candidates"), Is.Null);
            Assert.That(root.Q("shop-recruit-close"), Is.Null);
            Assert.That(root.Q<Label>("shop-price-digit-0").text + root.Q<Label>("shop-price-digit-1").text, Is.EqualTo("15"));
            Press(root, "shop-recruit-draw");
            yield return new WaitForSecondsRealtime(1.55f);
            var recruit = run.Current.ShopRecruitment.Candidates.Single();
            int wedge = run.Current.ShopRecruitment.Slots.ToList().IndexOf(recruit);
            var rotor = Object.FindAnyObjectByType<DockShop>().transform.Find("Service theatre/Recruitment wheel fixture/Recruitment wheel rotor");
            Assert.That(rotor, Is.Not.Null);
            var portrait = rotor.Find($"Recruitment wedge {wedge}/Recruitment portrait {wedge}").GetComponent<MeshRenderer>();
            var material = new MaterialPropertyBlock();
            portrait.GetPropertyBlock(material);
            Assert.That(material.GetFloat("_Desaturation"), Is.EqualTo(1));
            Assert.That(portrait.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(4));
            var stage = Object.FindAnyObjectByType<DockShop>().transform.Find("Service theatre");
            Assert.That(stage.Find("Recruitment wheel fixture/Price housing"), Is.Null);
            Assert.That(stage.Find("Recruitment wheel fixture/Wheel foot"), Is.Null);
            var body = stage.Find("Recruit front " + recruit.Id).GetComponent<MeshRenderer>();
            float enteringX = body.transform.position.x;
            float walkQuadHeight = body.transform.localScale.y;
            yield return new WaitForSecondsRealtime(1.22f);
            Assert.That(body.transform.position.x, Is.LessThan(enteringX - .5f));
            var deck = GameObject.Find("Mist Dock Playhouse/Half-round stage/Deck and joists").GetComponent<MeshRenderer>().bounds;
            Assert.That(body.transform.position.z - deck.min.z, Is.InRange(.38f, .8f));
            Assert.That(camera.WorldToViewportPoint(body.bounds.center).x, Is.InRange(.08f, .64f));
            Assert.That(body.transform.localScale.y, Is.LessThan(walkQuadHeight));
            var atlas = Resources.Load<Texture2D>($"RecruitmentFront/{recruit.RosterNumber:00}");
            Assert.That(atlas, Is.Not.Null);
            body.GetPropertyBlock(material);
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(atlas));
            Assert.That(material.GetVector("_MainTex_ST").x, Is.EqualTo(.25f).Within(.001f));
            Vector4 firstPose = material.GetVector("_MainTex_ST");
            yield return new WaitForSecondsRealtime(.23f);
            body.GetPropertyBlock(material);
            Assert.That(material.GetVector("_MainTex_ST"), Is.Not.EqualTo(firstPose));
            Vector2 empty = root.worldBound.min + Vector2.Scale(root.worldBound.size, new Vector2(.05f, .25f));
            using (var blank = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = empty }))
                Send(root.Q("shop-recruit-window"), blank);
            yield return new WaitForSecondsRealtime(.52f);
            Assert.That(Vector3.Distance(camera.transform.position, wide), Is.LessThan(.1f));
            Press(root, "shop-recruit-open");
            Press(root, "shop-recruit-draw");
            yield return new WaitForSecondsRealtime(1.55f);
            Assert.That(run.Current.ShopRecruitment.Candidates.Select(candidate => candidate.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo($"金币  {startingGold - 35}"));
            Assert.That(root.Q<Label>("shop-price-digit-0").text + root.Q<Label>("shop-price-digit-1").text, Is.EqualTo("25"));
            yield return null;
            string actorId = "shop-recruit-actor-" + recruit.Id;
            AssertCharacterPreview(root, actorId, recruit);
            var target = root.Q(actorId);
            var counter = root.Q("shop-checkout").worldBound.center;
            var window = root.Q("shop-recruit-window");
            Down(target);
            Up(window, counter);
            Assert.That(root.Q("shop-recruit-replacement"), Is.Null);
            yield return DragToCounter();
            Press(root, "close-contract");
            Assert.That(run.Current.ShopRecruitment.Signed, Is.False);
            yield return DragToCounter();
            Press(root, "sign-crew-contract");
            Assert.That(run.Party.Members.Single(entry => entry.Character.Id == recruit.Id).Contract, Is.Not.Null);
            Assert.That(run.Party.Members.Single(entry => entry.Character.Id == recruit.Id).Position, Is.Null);
            Assert.That(root.Q<Button>("shop-recruit-open").enabledInHierarchy, Is.False);
            Assert.That(root.Q("shop-recruit-window"), Is.Null);
            Assert.That(run.Party.Gold, Is.EqualTo(startingGold - 35 -
                run.Party.Members.Single(m => m.Character.Id == recruit.Id).Contract.BaseTotal));

            IEnumerator DragToCounter()
            {
                Down(target);
                yield return new WaitForSecondsRealtime(.34f);
                using var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, button = 0, mousePosition = counter });
                Send(window, move);
                Up(window, counter);
                Assert.That(root.Q("crew-contract"), Is.Not.Null);
            }
        }

        private static void AssertCharacterPreview(VisualElement root, string option, CharacterDefinition expected)
        {
            var anchor = root.Q(option);
            using var hover = PointerEnterEvent.GetPooled(new Event { type = EventType.MouseMove,
                mousePosition = anchor.worldBound.center });
            Send(anchor, hover);
            var preview = root.Q("character-choice-preview");
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.Q<Label>("character-preview-name").text, Is.EqualTo(expected.Name));
            Assert.That(preview.Q<Label>("character-preview-stats").text, Does.Contain("特防"));
            Assert.That(preview.Q<Label>("character-preview-trait")?.text, Is.EqualTo("被动 · " + expected.TraitText));
        }

        private static IEnumerator CheckEditorTopBar(VisualElement root, string openEditor, string goldLabel, int gold)
        {
            var topBar = root.Q("run-top-bar");
            Assert.That(topBar.Q<Label>(goldLabel).text, Is.EqualTo($"金币  {gold}"));
            Press(root, openEditor);
            yield return null;
            var editor = root.Q("party-editor");
            Assert.That(root.Q("run-top-bar"), Is.SameAs(topBar));
            Assert.That(topBar.enabledInHierarchy, Is.True);
            Assert.That(topBar.worldBound.yMax, Is.EqualTo(editor.worldBound.yMin).Within(1f));
            Press(topBar, "open-settings");
            Assert.That(editor.enabledInHierarchy, Is.False);
            Press(root, "close-settings");
            Assert.That(editor.enabledInHierarchy, Is.True);
            Press(editor, "back-party-editor");
            Assert.That(root.Q("party-editor"), Is.Null);
        }

        [UnityTest]
        public IEnumerator ForgeSignsKeepTheirPageWhenChoosingTwoItems()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            var hub = Object.FindAnyObjectByType<AdventureEventHub>();
            var content = ((AdventureContentAsset)typeof(AdventureEventHub).GetField("assets",Private).GetValue(hub)).Encounter;
            var input = content.StartingEquipment.Last(e => e.Tier == 3);
            for (int i = 0; i < 14; i++) run.Party.AddEquipment(input);
            Press(root, "inspect-event-Forge");
            yield return WaitForShow();
            IEnumerator PullBoard(Button board)
            {
                var at = board.worldBound.center;
                Down(board); Up(board, at + Vector2.up * 80);
                yield return new WaitForSecondsRealtime(.8f);
            }
            yield return PullBoard(root.Q<Button>("later-event-options"));
            yield return null;
            var choices = root.Q("event-options").Query<Button>().ToList()
                .Where(b => b.name.StartsWith("forge-item-") && b.resolvedStyle.display == DisplayStyle.Flex).Take(2).ToArray();
            Assert.That(choices.Length, Is.EqualTo(2));
            string second = choices[1].name;
            yield return PullBoard(choices[0]);
            yield return null;
            Assert.That(root.Q<Button>(second).resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            yield return PullBoard(root.Q<Button>(second));
            yield return null;
            yield return PullBoard(root.Q<Button>("earlier-event-options"));
            yield return null;
            var confirm = root.Q<Button>("event-forge-confirm");
            Assert.That(confirm.enabledInHierarchy, Is.True);
            Assert.That(confirm.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator EventSignsRequireADownwardPullForChoicesAndExit()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-FoundCoins");
            yield return WaitForShow();
            var choice = root.Q<Button>("event-collect-coins");
            int gold = run.Party.Gold;
            var origin = choice.worldBound.center;
            Down(choice); Up(choice, origin);
            Assert.That(run.Party.Gold, Is.EqualTo(gold));
            var frame = root.panel.visualTree.worldBound;
            var beam = GameObject.Find("Mist Dock Playhouse/Portal and traveller rail/Header lower edge").transform;
            float beamY = frame.yMin + frame.height * (1 - Camera.main.WorldToViewportPoint(beam.position).y);
            var rope = choice.Q("sign-rope");
            Assert.That(rope.worldBound.yMin, Is.EqualTo(beamY).Within(1));
            float originalLength = rope.worldBound.height;
            float originalBottom = rope.worldBound.yMax;
            Down(choice);
            using (var drag = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = origin + Vector2.up * 18 }))
                Send(choice, drag);
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(rope.worldBound.yMin, Is.EqualTo(beamY).Within(1));
            Assert.That(rope.worldBound.yMax - originalBottom, Is.EqualTo(18).Within(2));
            Assert.That(rope.worldBound.height - originalLength, Is.EqualTo(rope.worldBound.yMax - originalBottom).Within(2));
            Up(choice, origin + Vector2.up * 18);
            Assert.That(run.Current.Resolved, Is.False);
            Down(choice); Up(choice, origin + Vector2.up * 78);
            Assert.That(run.Current.Resolved, Is.False);
            Assert.That(root.panel.visualTree.Q("retracting-sign"), Is.Not.Null);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(run.Party.Gold, Is.GreaterThan(gold));
            yield return WaitForEventExit(root);
            Assert.That(CurrentRoute().Current, Is.Null);
            Assert.That(CurrentRoute().Party.Gold, Is.EqualTo(gold));
            Assert.That(File.Exists(savePath), Is.False);
        }

        [UnityTest]
        public IEnumerator ApothecaryTableOffersThreePhysicalBottlesAndReturnsToTheWideShot()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root,"inspect-event-Alchemist");
            var eye = Camera.main.transform.position;
            var visit = run.Current;
            var stage = Object.FindAnyObjectByType<AdventureEventHub>().transform.Find("Service theatre");
            var choices = root.Q("potion-table").Query<Button>().ToList();
            Assert.That(choices,Has.Count.EqualTo(3));
            float deadline = Time.realtimeSinceStartup + 6;
            while (!choices[1].enabledInHierarchy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Camera.main.transform.position.z,Is.GreaterThan(eye.z+4));
            var target = stage.Find("Apothecary bottle 1").GetComponent<Renderer>();
            var screen = root.panel.visualTree.worldBound;
            var projected = Camera.main.WorldToViewportPoint(target.bounds.center);
            Assert.That(choices[1].worldBound.Contains(screen.min + new Vector2(projected.x*screen.width,(1-projected.y)*screen.height)),Is.True);
            int before = run.Party.Inventory.Count(i => i.Definition.Id == visit.Potions[1].Id);
            Press(root,"event-potion-1");
            Assert.That(run.Party.Inventory.Count(i => i.Definition.Id == visit.Potions[1].Id),Is.EqualTo(before+1));
            Assert.That(target.enabled,Is.False);
            Assert.That(visit.Resolved,Is.True);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Camera.main.transform.position.z,Is.GreaterThan(eye.z+.05f));
            yield return WaitForEventExit(root);
            Assert.That(CurrentRoute().Current,Is.Null);
        }

        [UnityTest]
        public IEnumerator LeavingTheHatTableRestoresTheWideShotBeforeTheNextEvent()
        {
            yield return OpenMenu();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-HatTrick");
            Vector3 wide = Camera.main.transform.position;
            float until = Time.realtimeSinceStartup + 6;
            while (!root.Q<Button>("hat-start").enabledInHierarchy && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.That(Camera.main.transform.position.z, Is.GreaterThan(wide.z + 4));
            var arrow = root.Q<Button>("close-event");
            Down(arrow); Up(arrow, arrow.worldBound.center + Vector2.up * 80);
            Assert.That(root.Q("hat-game"), Is.Not.Null);
            yield return WaitForEventExit(root);
            Press(root, "inspect-event-FoundCoins");
            yield return WaitForShow();
            Assert.That(Vector3.Distance(Camera.main.transform.position,wide), Is.LessThan(.02f));
            Assert.That(root.Q("hat-game"), Is.Null);
        }

        [UnityTest]
        public IEnumerator HatGameActuallyMovesOnScreenThenAwardsTheTrackedItem()
        {
            yield return OpenMenu(); var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-HatTrick"); yield return null;
            Assert.That(root.Q("hat-target-item"), Is.Null);
            Assert.That(root.Q("hat-game").Query<Label>().ToList().Any(label => label.text == run.Current.HatRewards[0].Name), Is.False);
            int items = run.Party.Inventory.Count + run.Party.SkillBooks.Count;
            Press(root, "hat-0"); Assert.That(run.Current.Resolved, Is.False);
            var first = run.Current.HatSwaps[0];
            var movingHat = root.Q<Button>("hat-" + first.First);
            var table = Object.FindAnyObjectByType<AdventureEventHub>().transform.Find("Service theatre/Hat table top");
            Assert.That(table, Is.Not.Null);
            var camera = Camera.main;
            Vector3 wide = camera.transform.position;
            float arrivalDeadline = Time.realtimeSinceStartup + 5;
            while (!root.Q<Button>("hat-start").enabledInHierarchy && Time.realtimeSinceStartup < arrivalDeadline)
                yield return null;
            Assert.That(camera.transform.position.z, Is.GreaterThan(wide.z + 4));
            Assert.That(camera.WorldToViewportPoint(table.position).x, Is.InRange(.43f,.57f));
            float before = movingHat.worldBound.center.x;
            Press(root, "hat-start");
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(Mathf.Abs(movingHat.worldBound.center.x - before), Is.GreaterThan(10), "The hat must visibly animate, not just change its hidden result.");
            float deadline = Time.realtimeSinceStartup + 8;
            while (run.Current.HatStep < run.Current.HatSwaps.Count && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(run.Current.HatStep, Is.EqualTo(7));
            Press(root, "hat-0");
            Assert.That(run.Current.HatWon, Is.True);
            Assert.That((run.Party.Inventory.Count + run.Party.SkillBooks.Count), Is.EqualTo(items + 1));
            Assert.That(root.Query<Image>("hat-revealed-item").ToList().Count, Is.EqualTo(3));
            yield return null;
            var revealed = root.Query<Image>("hat-revealed-item").ToList();
            for (int slot = 0; slot < revealed.Count; slot++)
            {
                var item = revealed[slot];
                var reward = run.Current.HatRewards[run.Current.HatPositions[slot]];
                using (var enter = PointerEnterEvent.GetPooled(new Event
                    { type = EventType.MouseMove, mousePosition = item.worldBound.center }))
                    Send(item, enter);
                yield return null;
                var tooltip = root.Q("equipment-tooltip");
                Assert.That(tooltip, Is.Not.Null, $"Missing details for hat position {slot}.");
                Assert.That(tooltip.Q<Label>("equipment-tooltip-name").text, Is.EqualTo(reward.Name));
                Assert.That(tooltip.Q<Label>("equipment-tooltip-description").text, Is.EqualTo(reward.Description));
                var tooltipArtwork = tooltip.Q("equipment-tooltip-art").Q<Image>();
                Assert.That(tooltipArtwork.image, Is.SameAs(item.image));
                if (reward is SkillBookDefinition)
                {
                    var symbol = item.Q<Image>("skill-scroll-symbol");
                    Assert.That(symbol, Is.Not.Null);
                    Assert.That(tooltipArtwork.Q<Image>("skill-scroll-symbol").image, Is.SameAs(symbol.image));
                }
                using (var leave = PointerLeaveEvent.GetPooled(new Event
                    { type = EventType.MouseMove, mousePosition = item.worldBound.center }))
                    Send(item, leave);
                Assert.That(root.Q("equipment-tooltip"), Is.Null);
            }
            float retreatDeadline = Time.realtimeSinceStartup + 5;
            while (Vector3.Distance(wide,camera.transform.position) > .02f && Time.realtimeSinceStartup < retreatDeadline)
                yield return null;
            Assert.That(Vector3.Distance(wide,camera.transform.position), Is.LessThan(.02f));
            yield return WaitForEventExit(root);
            Assert.That(root.Q("equipment-tooltip"), Is.Null, "Closing the event must remove its hover panel.");
        }

        [UnityTest]
        public IEnumerator InspectingALockedChestDiscardsTheAbsentCompanionAfterExit()
        {
            yield return OpenMenu(); var root = Root<AdventureEventHub>();
            string id = run.Party.Members.First().Id;
            Press(root, "inspect-event-LockedChest");
            Assert.That(root.Q("leave-companion-" + id), Is.Null);
            Press(root, "chest-skip");
            yield return WaitForEventExit(root);
            Assert.That(root.Q("event-window"), Is.Null);
            Assert.That(run.Party.Members.Any(m => m.IsAway), Is.False);

            run = CurrentRoute();
            Press(root, "inspect-event-LockedChest");
            Press(root, "chest-choose-companion");
            var activeEvent = root.Q("event-window");
            var topBar = root.Q("run-top-bar");
            Assert.That(topBar.Q<Label>("adventure-gold").text, Is.EqualTo($"金币  {run.Party.Gold}"));
            Assert.That(topBar.Q<Button>("event-party-editor").Q("team-icon"), Is.Not.Null);
            var beforeSupply = run.Party.Inventory.Select(item => item.Id).ToArray();
            var beforeBooks = run.Party.SkillBooks.Select(item => item.Id).ToArray();
            Press(root, "event-party-editor");
            Assert.That(root.Q("party-editor"), Is.Not.Null);
            Assert.That(root.Q<Button>("leave-companion-" + id).enabledInHierarchy, Is.False);
            var supplies = run.Party.Inventory.GroupBy(item => item.Definition.Id).ToArray();
            var books = run.Party.SkillBooks.GroupBy(item => item.Definition.Id).ToArray();
            Assert.That(supplies.Length, Is.EqualTo(51));
            Assert.That(books.Length, Is.EqualTo(30));
            Assert.That(supplies.Select(group => group.Count()), Is.All.EqualTo(4));
            Assert.That(books.Select(group => group.Count()), Is.All.EqualTo(4));
            Press(root, "back-party-editor");
            Assert.That(run.Party.Inventory.Select(item => item.Id), Is.EqualTo(beforeSupply));
            Assert.That(run.Party.SkillBooks.Select(item => item.Id), Is.EqualTo(beforeBooks));
            Assert.That(root.Q("event-window"), Is.SameAs(activeEvent));
            Assert.That(root.Q<Button>("leave-companion-" + id).enabledInHierarchy, Is.True);
            Assert.That(run.Party.Members.Any(m => m.IsAway), Is.False);
            Press(root, "chest-back");
            Assert.That(root.Q("leave-companion-" + id), Is.Null);
            Press(root, "chest-choose-companion");
            Press(root, "leave-companion-" + id);
            Assert.That(run.Party.Members.Single(m => m.Id == id).IsAway, Is.True);
            yield return WaitForEventExit(root);
            Assert.That(CurrentRoute().Party.Members.Any(m => m.IsAway), Is.False);
            Assert.That(CurrentRoute().CompletedNodes, Is.Zero);
            Assert.That(root.Q("adventure-party").Query<Label>().ToList().Any(l => l.text.Contains("守候宝箱")), Is.False);
        }

        [UnityTest]
        public IEnumerator TrialRehearsalAllowsChoosingAnEvolutionBeforeDiscardingTheResult()
        {
            yield return OpenMenu(); AddTestWeapons();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-AscensionTrial");
            Assert.That(run.EvolutionCandidates.Count, Is.GreaterThan(0));
            Assert.That(root.Q<Button>("event-trial-gift").Q<Image>(), Is.Null, "The gift stays wrapped until chosen.");
            yield return WaitForShow();
            var scenery = GameObject.Find("Event backcloth");
            var deck = GameObject.Find("Service theatre/Stage floor");
            var eventScene = SceneManager.GetActiveScene();
            Press(root, "event-trial-battle");
            float timeout = Time.realtimeSinceStartup + 5;
            while (root.Q("battle-phase") == null && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene(),Is.EqualTo(eventScene));
            Assert.That(GameObject.Find("Event backcloth"),Is.SameAs(scenery));
            Assert.That(GameObject.Find("Service theatre/Stage floor"),Is.SameAs(deck));
            Assert.That(GameObject.Find("Dock storyteller").GetComponent<MeshRenderer>().enabled,Is.False);
            Assert.That(root.Q<Button>("start"),Is.Null);
            FastBattle(); Assert.That(run.Current.Battle.Enemies.Count, Is.EqualTo(1));
            yield return WaitSettlement();
            var battle = Root<BattlePrototype>();
            Assert.That(battle.Q("evolution-choices"), Is.Null);
            ClaimBonusLoot(battle);
            Press(battle, "claim-gold");
            timeout = Time.realtimeSinceStartup + 5;
            while (root.Q("evolution-choices") == null && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene(),Is.EqualTo(eventScene));
            var candidates = run.EvolutionCandidates;
            Assert.That(root.Q("evolution-choices").Query<Button>().ToList().Select(button => button.name),
                Is.EquivalentTo(candidates.Select(member => $"pick-evolution-member-{member.Id}")));
            var member = candidates.First();
            var forms = run.EvolutionForms(member.Id);
            string old = member.Character.Id;

            Press(root, $"pick-evolution-member-{member.Id}");
            Assert.That(run.Current.Resolved, Is.False);
            Assert.That(run.Party.Members.Single(m => m.Id == member.Id).Character.Id, Is.EqualTo(old));
            Assert.That(root.Q("evolution-choices").Query<Button>().ToList().Select(button => button.name),
                Is.EquivalentTo(forms.Select(form => $"evolve-{member.Id}-{form.Id}").Append("back-to-evolution-members")));
            Press(root, "back-to-evolution-members");
            Assert.That(run.Current.EvolutionMemberId, Is.Null);
            Press(root, $"pick-evolution-member-{member.Id}");
            Press(root, $"evolve-{member.Id}-{forms[0].Id}");
            string evolved = run.Party.Members.Single(m => m.Id == member.Id).Character.Id;
            Assert.That(evolved, Is.EqualTo(forms[0].Id));
            Assert.That(run.Current.Resolved, Is.True);
            yield return WaitForEventExit(root);
            Assert.That(CurrentRoute(), Is.Not.SameAs(run));
            Assert.That(CurrentRoute().CompletedNodes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NewRouteDraftsTwoOfFourAndUsesOnlyTheRolledGear()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");

            var root = Root<AdventureEventHub>();
            Assert.That(root.Q("dock-route-signs"), Is.Null);
            var candidates = root.Q("starter-candidates").Query<Button>().ToList();
            Assert.That(candidates.Count, Is.EqualTo(4));
            var definitions = candidates.Select(button => (CharacterDefinition)button.userData).ToArray();
            Assert.That(definitions.All(character => character.Stage == 1), Is.True);
            Assert.That(definitions.Select(character => character.Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(definitions.Any(character => character.Profession == Profession.Warrior), Is.True);

            var hub = Object.FindAnyObjectByType<AdventureEventHub>();
            var content = ((AdventureContentAsset)typeof(AdventureEventHub).GetField("assets", Private).GetValue(hub)).Encounter;
            var random = new System.Random(917);
            var draw = Flow.GetMethod("DrawStarters", PrivateStatic);
            var drafts = Enumerable.Range(0, 64)
                .Select(_ => (CharacterDefinition[])draw.Invoke(null, new object[] { content, random })).ToArray();
            Assert.That(drafts.All(draft => draft.Any(character => character.Profession == Profession.Warrior)), Is.True);
            Assert.That(drafts.Any(draft => draft.Count(character => character.Profession == Profession.Warrior) > 1), Is.True);
            Assert.That(drafts.SelectMany(draft => draft).Any(character => character.EvolutionIds.Count == 0), Is.True);
            Assert.That(root.Q<Button>("confirm-starters").enabledInHierarchy, Is.False);

            Press(root, candidates[0].name);
            Assert.That(root.Q<Button>("confirm-starters").enabledInHierarchy, Is.False);
            Press(root, candidates[1].name);
            Assert.That(candidates[2].enabledInHierarchy, Is.False);
            Press(root, candidates[0].name);
            Assert.That(root.Q<Button>("confirm-starters").enabledInHierarchy, Is.False);
            Press(root, candidates[2].name);
            Press(root, "confirm-starters");
            Assert.That(root.Q("crew-contract"), Is.Not.Null);
            Press(root, "sign-starters");

            var party = CurrentRoute().Party;
            var members = party.Members.OrderBy(member => member.Position).ToArray();
            Assert.That(members.Select(member => member.Character.Id), Is.EqualTo(new[] { definitions[1].Id, definitions[2].Id }));
            Assert.That(members.Select(member => member.Position.Value), Is.EqualTo(new[] { 1, 2 }));
            foreach (var member in members)
            {
                var ratings = member.EmployeeAttributes;
                Assert.That(new[] { ratings.Loyalty, ratings.Obedience, ratings.PayPreference, ratings.PlayingTime,
                    ratings.Hardiness }, Is.All.InRange(3, 5));
                Assert.That(ratings.Satisfaction, Is.EqualTo(3));
            }
            Assert.That(members.All(member => member.EquipmentIds.All(item => item == null)), Is.True);
            Assert.That(party.Gold, Is.EqualTo(50));
            Assert.That(members.All(m => m.Contract.BasePaid), Is.True);
            Assert.That(root.Q<Label>("adventure-gold").text, Is.EqualTo("金币  50"));
            Assert.That(party.Inventory.Count, Is.EqualTo(2));
            Assert.That(party.Inventory.All(item => !item.Definition.IsPotion && item.Definition.Tier is 1 or 2), Is.True);
            Assert.That(party.SkillBooks, Is.Empty);
            var gear = party.Inventory.Select(item => item.Definition.Id).ToArray();
            Assert.That(gear.Distinct().Count(), Is.EqualTo(2));
            Assert.That(root.Q("run-backpack-items").childCount, Is.EqualTo(2));
            AssertNoVisibleTiers(root);

            Press(root, "event-party-editor");
            Assert.That(party.Inventory.Count, Is.EqualTo(2));
            Assert.That(party.SkillBooks, Is.Empty);
            Press(root, "edit-member-" + members[0].Id);
            Press(root, "open-employee-dossier");
            Assert.That(root.Q("employee-dossier"), Is.Not.Null);
            Press(root, "employee-open-contract");
            Assert.That(root.Q<Button>("sign-crew-contract").text, Is.EqualTo("签署续约合同"));
            Assert.That(root.Q("sell-member"), Is.Null);
            Assert.That(root.Q("renew-contract"), Is.Null);
            Assert.That(root.Q<LongField>("contract-signing-fee").enabledInHierarchy, Is.True);
            int newWage = party.ExpectedDailyWage(members[0].Id) + 1;
            party.AddGold(12 * newWage);
            root.Q<LongField>("contract-signing-fee").value = 12 * newWage;
            Press(root, "sign-crew-contract");
            Assert.That(root.Q("employee-dossier"), Is.Not.Null);
            Assert.That(party.Members.Single(member => member.Id == members[0].Id).Renewals.Single().DailyWage, Is.EqualTo(newWage));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            SaveAndExit(root);
            yield return WaitScene("MainMenu");

            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            var restored = CurrentRoute().Party;
            Assert.That(restored.Gold, Is.EqualTo(50));
            Assert.That(Root<AdventureEventHub>().Q("starter-draft"), Is.Null);
            Assert.That(restored.Members.Select(member => member.Character.Id),
                Is.EqualTo(members.Select(member => member.Character.Id)));
            Assert.That(restored.Members.Select(member => member.EmployeeAttributes),
                Is.EqualTo(members.Select(member => member.EmployeeAttributes)));
            Assert.That(restored.Members.Single(member => member.Id == members[0].Id).Renewals.Single().DailyWage, Is.EqualTo(newWage));
            Assert.That(restored.Inventory.Select(item => item.Definition.Id), Is.EqualTo(gear));
        }

        [UnityTest]
        public IEnumerator SaveDuringBattleReopensTheSameNodeWithoutKeepingSpentPotions()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            ConfirmFirstPair(Root<AdventureEventHub>());
            run = CurrentRoute();
            Assert.That(run.RouteOptions, Is.EqualTo(new[] { NodeKind.NormalBattle }));

            var assets = (AdventureContentAsset)typeof(AdventureEventHub).GetField("assets", Private)
                .GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
            var holder = run.Party.Members.First();
            var potion = run.Party.AddEquipment(assets.Encounter.StartingEquipment.First(item => item.IsPotion));
            Assert.That(run.Party.TryEquip(holder.Id, 0, potion.Id), Is.True);
            var target = run.RouteChoice(0);
            int completed = run.CompletedNodes;
            int gold = run.Party.Gold;
            yield return WaitForRoadstone(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "route-choice-0");
            yield return WaitScene("BattlePrototype");
            var battle = Root<BattlePrototype>();
            Assert.That(battle.Q("start"), Is.Null);
            var enemies = run.Current.Battle.Enemies.Select(enemy => enemy.Character.Id).ToArray();
            float deadline = Time.realtimeSinceStartup + 8;
            while (run.Party.Inventory.Any(item => item.Id == potion.Id) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(run.Party.Inventory.Any(item => item.Id == potion.Id), Is.False);

            SaveAndExit(battle);
            yield return WaitScene("MainMenu");
            Assert.That(File.Exists(savePath), Is.True);
            Assert.That(Root<PrototypeSceneMenu>().Q<Button>("open-route").text, Is.EqualTo("继续讨伐"));
            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            run = CurrentRoute();
            var map = Root<AdventureEventHub>().Q("dock-route-signs");
            Assert.That(run.CompletedNodes, Is.EqualTo(completed));
            Assert.That(run.Current, Is.Null);
            Assert.That(run.Party.Gold, Is.EqualTo(gold));
            Assert.That(run.Party.Members.First().EquipmentIds[0], Is.EqualTo(potion.Id));
            Assert.That(run.Party.Inventory.Any(item => item.Id == potion.Id), Is.True);
            Assert.That(((RouteStop)map.Q<Button>("route-node-current").userData).Number, Is.EqualTo(target.Number));
            Assert.That(map.Q("route-choice-0"), Is.Null);
            yield return WaitForRoadstone(map);
            Press(map, "route-node-current");
            yield return WaitScene("BattlePrototype");
            Assert.That(Root<BattlePrototype>().Q("start"), Is.Null);
            Assert.That(run.Current.Battle.Enemies.Select(enemy => enemy.Character.Id), Is.EqualTo(enemies));
        }

        [UnityTest]
        public IEnumerator APartyWipeReturnsToTheMenuAndClearsTheCheckpoint()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            ConfirmFirstPair(Root<AdventureEventHub>());
            SaveAndExit(Root<AdventureEventHub>());
            yield return WaitScene("MainMenu");
            Assert.That(File.Exists(savePath), Is.True);
            File.WriteAllText(savePath + ".tmp", "interrupted save");
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");

            run = CurrentRoute();
            var original = run.Party.Members.First().Character;
            var weak = new CharacterDefinition(original.Id, original.Name, Profession.Warrior, Personality.Worrier,
                new Stats(1, 0, 0, 1), 1, new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            run.Party.ResetRoster(new[] { new PartyMemberStart("last-ally", weak, 1, 1) });
            yield return WaitForRoadstone(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "route-choice-0");
            yield return WaitScene("BattlePrototype");
            FastBattle();

            float deadline = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name == "BattlePrototype" && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
            Assert.That(run.Failed, Is.True);
            Assert.That(File.Exists(savePath), Is.False);
            Assert.That(File.Exists(savePath + ".tmp"), Is.False);
            Assert.That(Root<PrototypeSceneMenu>().Q<Button>("open-route").text, Is.EqualTo("开始讨伐"));
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            Assert.That(Root<AdventureEventHub>().Q("starter-draft"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator AbandoningDuringBattleClearsSavedAndActiveProgress()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");

            ConfirmFirstPair(Root<AdventureEventHub>());
            CurrentRoute().Party.AddGold(47);
            SaveAndExit(Root<AdventureEventHub>());
            yield return WaitScene("MainMenu");
            Assert.That(Root<PrototypeSceneMenu>().Q<Button>("open-route").text, Is.EqualTo("继续讨伐"));
            Assert.That(File.Exists(savePath), Is.True);
            File.WriteAllText(savePath + ".tmp", "interrupted save");

            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            var abandoned = CurrentRoute();
            Assert.That(abandoned.Party.Gold, Is.EqualTo(97));
            yield return WaitForRoadstone(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "route-choice-0");
            yield return WaitScene("BattlePrototype");
            var battle = Root<BattlePrototype>();
            Press(battle, "open-settings");
            Press(battle.Q("run-settings"), "abandon-run");
            yield return WaitScene("MainMenu");

            Assert.That(File.Exists(savePath), Is.False);
            Assert.That(File.Exists(savePath + ".tmp"), Is.False);
            Assert.That(abandoned.Failed, Is.True);
            var menu = Root<PrototypeSceneMenu>();
            Assert.That(menu.Q<Button>("open-route").text, Is.EqualTo("开始讨伐"));
            Press(menu, "open-route");
            yield return WaitScene("EventSelect");

            ConfirmFirstPair(Root<AdventureEventHub>());
            var fresh = CurrentRoute();
            Assert.That(fresh, Is.Not.SameAs(abandoned));
            Assert.That(fresh.Floor, Is.EqualTo(1));
            Assert.That(fresh.CompletedNodes, Is.Zero);
            Assert.That(fresh.Current, Is.Null);
            Assert.That(fresh.Party.Gold, Is.EqualTo(50));
        }

        [UnityTest]
        public IEnumerator SavingAfterAnEventRewardKeepsTheRewardAndOnlyOffersTheNextNode()
        {
            yield return OpenMenu();
            var hub = Object.FindAnyObjectByType<AdventureEventHub>();
            var assets = (AdventureContentAsset)typeof(AdventureEventHub).GetField("assets", Private).GetValue(hub);
            var content = assets.Encounter;
            var gear = content.StartingEquipment.Concat(content.SkillBooks).ToArray();
            AdventureCheckpoint beforeEvent = null;
            for (int seed = 0; seed < 256 && beforeEvent == null; seed++)
            {
                var party = new PartyRun(content.Party.Select(unit => new PartyMemberStart(unit.Id,
                    unit.Character, unit.Position, unit.Health)), gear.Take(1));
                party.AddGold(30);
                var seeded = new AdventureRun(party, content.CharacterCatalog, gear, false, new System.Random(seed));
                seeded.EnterRoute(0);
                var enemies = seeded.PrepareEnemies(content.EnemyRoster, content.EnemyFormations);
                seeded.BindBattle("fixture");
                Assert.That(seeded.SettleBattle(new BattleResult("fixture", BattleOutcome.Victory,
                    enemies.ToDictionary(enemy => enemy.Id, _ => 0), System.Array.Empty<BattleEvent>()), out _), Is.True);
                Assert.That(seeded.ClaimReward(0) && seeded.CloseVisit(), Is.True);
                int eventOption = seeded.RouteOptions.ToList().IndexOf(NodeKind.Event);
                if (eventOption >= 0 && seeded.EnterRoute(eventOption) && seeded.Current.Event == AdventureEventKind.FoundCoins)
                    beforeEvent = seeded.CheckpointForExit();
            }
            Assert.That(beforeEvent, Is.Not.Null);
            File.WriteAllText(savePath, JsonUtility.ToJson(beforeEvent));
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");

            var root = Root<AdventureEventHub>();
            run = CurrentRoute();
            var node = (RouteStop)root.Q<Button>("route-node-current").userData;
            yield return WaitForRoadstone(root);
            Press(root, "route-node-current");
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.FoundCoins));
            yield return WaitForShow();
            root = Root<AdventureEventHub>();
            int beforeCoins = run.Party.Gold;
            Press(root, "event-collect-coins");
            Assert.That(run.Current.Resolved, Is.True);
            Assert.That(root.Q<Button>("finish-event"), Is.Null);
            int rewardGold = run.Party.Gold;
            Assert.That(rewardGold - beforeCoins, Is.InRange(10, 20));
            Assert.That(root.Q("reward-flight").Q("reward-gold-icon"), Is.Not.Null);
            SaveAndExit(root);
            yield return WaitScene("MainMenu");

            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            run = CurrentRoute();
            root = Root<AdventureEventHub>();
            Assert.That(run.Current, Is.Null);
            Assert.That(run.CompletedNodes, Is.EqualTo(node.Number));
            Assert.That(run.Stage, Is.EqualTo(node.Number + 1));
            Assert.That(run.Party.Gold, Is.EqualTo(rewardGold));
            Assert.That(run.RouteCurrent.Lane, Is.EqualTo(node.Lane));
            Assert.That(root.Q<Button>("route-node-current"), Is.Null);
            Assert.That(root.Q("event-window"), Is.Null);
            Assert.That(root.Q<Button>("route-choice-0"), Is.Not.Null);

            SaveAndExit(root);
            yield return WaitScene("MainMenu");
            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            Assert.That(CurrentRoute().CompletedNodes, Is.EqualTo(node.Number));
            Assert.That(CurrentRoute().Party.Gold, Is.EqualTo(rewardGold));
        }

        [UnityTest]
        public IEnumerator FirstVictoryKeepsThePaintedFloorAndPropsBehindTheRouteSigns()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Checking the scenery requires a graphics device.");
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            ConfirmFirstPair(Root<AdventureEventHub>());
            run = CurrentRoute();
            AddTestWeapons();
            yield return WaitForRoadstone(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "route-choice-0");
            yield return WaitScene("BattlePrototype");
            FastBattle();
            yield return WaitSettlement();
            var live = Object.FindAnyObjectByType<BattlePrototype>().transform.Find("Paper Theatre");
            var floor = live.Find("Stage floor").GetComponent<MeshRenderer>();
            var crane = live.Find("Right wing").GetComponent<MeshRenderer>();
            var before = RenderFloorAndCrane(floor, crane);

            var rewards = Root<BattlePrototype>();
            Press(rewards, "claim-route-loot-0");
            ClaimBonusLoot(rewards);
            Press(rewards, "claim-gold");
            yield return WaitScene("EventSelect");
            Assert.That(Root<AdventureEventHub>().Q("route-choice-0"), Is.Not.Null);
            var snapshot = GameObject.Find("Travelling dock set").GetComponentsInChildren<MeshRenderer>();
            var after = RenderFloorAndCrane(snapshot.Single(r => r.name == "Stage floor"),
                snapshot.Single(r => r.name == "Right wing"));
            int changed = before.Zip(after, (a, b) => Mathf.Abs(a.r - b.r) > 24 ||
                Mathf.Abs(a.g - b.g) > 24 || Mathf.Abs(a.b - b.b) > 24).Count(different => different);
            Assert.That(before.Count(c => c.g > 15 && c.r < 240), Is.GreaterThan(before.Length / 10),
                "The reference must contain painted scenery.");
            Assert.That(changed, Is.LessThan(before.Length / 50),
                "Unloading the battle must preserve the timber grain, crane artwork and its transparent outline.");
        }

        private static Color32[] RenderFloorAndCrane(MeshRenderer floor, MeshRenderer crane)
        {
            var camera = Camera.main;
            var target = RenderTexture.GetTemporary(384, 216, 24);
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var activeTarget = RenderTexture.active;
            int floorLayer = floor.gameObject.layer, craneLayer = crane.gameObject.layer, mask = camera.cullingMask;
            var background = camera.backgroundColor;
            try
            {
                floor.gameObject.layer = crane.gameObject.layer = 30;
                camera.cullingMask = 1 << 30;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                return readback.GetPixels32();
            }
            finally
            {
                floor.gameObject.layer = floorLayer;
                crane.gameObject.layer = craneLayer;
                camera.cullingMask = mask;
                camera.backgroundColor = background;
                camera.targetTexture = previousTarget;
                RenderTexture.active = activeTarget;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(readback);
            }
        }

        [UnityTest]
        public IEnumerator GraveyardTownServicesAndTreasureUseTheSameDistrict()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            ConfirmFirstPair(Root<AdventureEventHub>());
            SaveAndExit(Root<AdventureEventHub>());
            yield return WaitScene("MainMenu");

            // Resume at a town followed by a chest to exercise the nested service and route exits together.
            var saved = JsonUtility.FromJson<AdventureCheckpoint>(File.ReadAllText(savePath));
            saved.Floor = 2;
            saved.Stage = 3;
            saved.CompletedNodes = 12;
            saved.Options = null;
            saved.Nodes[2] = new RouteCheckpointNode { Kind = NodeKind.Town, Replaces = -1 };
            saved.Nodes[3] = new RouteCheckpointNode { Kind = NodeKind.TreasureChest, Replaces = -1 };
            File.WriteAllText(savePath, JsonUtility.ToJson(saved));
            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            yield return WaitForRoadstone(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "route-choice-0");
            yield return WaitScene("Town");
            AssertTexture("Cemetery square", "MourningSquare");
            var backdrop = GameObject.Find("Service theatre/Cemetery square").GetComponent<MeshRenderer>();
            var animation = new System.Collections.Generic.HashSet<Texture>();
            var paint = new MaterialPropertyBlock();
            float until = Time.realtimeSinceStartup + 3f;
            while (animation.Count < 6 && Time.realtimeSinceStartup < until)
            {
                backdrop.GetPropertyBlock(paint);
                animation.Add(paint.GetTexture("_MainTex"));
                yield return null;
            }
            Assert.That(animation.Count, Is.EqualTo(6));
            string[] facades = { "HiringOffice", "Chandlery", "KeepersRest", "SextonOffice" };
            for (int i = 0; i < facades.Length; i++) AssertTexture("Facility " + i, facades[i]);

            var route = (AdventureRun)Flow.GetProperty("Active", PrivateStatic).GetValue(null);
            var visit = route.Current;
            int townDay = route.Party.Day;
            var art = (AdventureContentAsset)Flow.GetField("routeArt", PrivateStatic).GetValue(null);
            Press(Root<DockTown>(), "town-service-Shop");
            yield return WaitScene("DockShop");
            AssertTexture("Cemetery chandlery", "WakeShop");
            ClickTownSign(Root<DockShop>());
            yield return WaitScene("Town");

            Press(Root<DockTown>(), "town-service-Recruit");
            yield return WaitScene("TalentMarket");
            AssertTexture("Cemetery vestry", "VestryHall");
            ClickTownSign(Root<TalentMarket>());
            yield return WaitScene("Town");

            Press(Root<DockTown>(), "town-service-Camp");
            yield return WaitScene("Camp");
            AssertTexture("Cemetery vigil", "WatchCloister");
            ClickTownSign(Root<CampPrototype>());
            yield return WaitScene("Town");
            var townRoot = Root<DockTown>();
            Press(townRoot, "town-service-Quest");
            yield return null;
            Press(townRoot, "close-town-quest");
            Assert.That(route.Current, Is.SameAs(visit));
            Assert.That(route.Party.Day, Is.EqualTo(townDay));
            yield return WaitForRoadstone(townRoot);
            var onward = townRoot.Q<Button>("route-choice-0");
            Assert.That(townRoot.panel.Pick(onward.worldBound.center), Is.SameAs(onward));
            Down(onward);
            Up(onward, onward.worldBound.center);
            yield return WaitScene("EventSelect");
            AssertTexture("Stage floor", "StoneDeck");
            Assert.That(GameObject.Find("Service theatre/Graveyard treasure chest"), Is.Not.Null);
            Assert.That(route.Current.Kind, Is.EqualTo(NodeKind.TreasureChest));
            Assert.That(route.Party.Day, Is.EqualTo(townDay + 1));

            void ClickTownSign(VisualElement root)
            {
                Assert.That(root.Q("dock-route-signs"), Is.Null);
                var buttons = root.Q("town-return-sign").Query<Button>().ToList();
                Assert.That(buttons, Has.Count.EqualTo(1));
                var button = buttons[0];
                Assert.That(button.tooltip, Is.EqualTo("下拉返回城镇"));
                Assert.That(root.panel.Pick(button.worldBound.center), Is.SameAs(button), button.name);
                Down(button);
                Up(button, button.worldBound.center + new Vector2(0,75));
            }

            void AssertTexture(string mesh, string expected)
            {
                var obj = GameObject.Find("Service theatre/" + mesh);
                Assert.That(obj, Is.Not.Null, mesh);
                var ink = new MaterialPropertyBlock();
                obj.GetComponent<MeshRenderer>().GetPropertyBlock(ink);
                Assert.That(ink.GetTexture("_MainTex").name, Is.EqualTo(expected), mesh);
            }
        }

        [UnityTest]
        public IEnumerator RoadstoneFallsBeforeSelectionAndShakesTheRenderedViewOnImpact()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            var root = Root<AdventureEventHub>();
            ConfirmFirstPair(root);
            float deadline = Time.realtimeSinceStartup + 8;
            while (GameObject.Find("Right stone signpost/Foot") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            var foot = GameObject.Find("Right stone signpost/Foot").GetComponent<Renderer>();
            var deck = Object.FindAnyObjectByType<DockPlayhouse>().transform.Find("Half-round stage/Deck and joists").GetComponent<Renderer>();
            Assert.That(foot.bounds.min.y, Is.GreaterThan(deck.bounds.max.y + 1));
            Assert.That(root.Q<Button>("route-choice-0").enabledInHierarchy, Is.False);
            yield return WaitForRoadstone(root);
            Assert.That(foot.bounds.min.y, Is.EqualTo(deck.bounds.max.y).Within(.005f));

            var eye = Camera.main;
            var resting = eye.projectionMatrix;
            Matrix4x4 duringRender = resting;
            void Observe(Camera camera) { if (camera == eye) duringRender = eye.projectionMatrix; }
            void ObservePipeline(ScriptableRenderContext context, Camera camera) => Observe(camera);
            var originalTarget = eye.targetTexture;
            var target = RenderTexture.GetTemporary(eye.pixelWidth, eye.pixelHeight, 24);
            RenderPipelineManager.beginCameraRendering += ObservePipeline;
            Camera.onPreRender += Observe;
            try
            {
                eye.targetTexture = target;
                eye.Render();
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= ObservePipeline;
                Camera.onPreRender -= Observe;
                eye.targetTexture = originalTarget;
                RenderTexture.ReleaseTemporary(target);
            }
            Assert.That(duringRender, Is.Not.EqualTo(resting));
            Assert.That(eye.projectionMatrix, Is.EqualTo(resting));
        }

        [UnityTest]
        public IEnumerator HoveringTopBarEquipmentAcrossABattleNodeRebuildKeepsRoundOnePlayable()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            var map = Root<AdventureEventHub>();
            ConfirmFirstPair(map);
            run = CurrentRoute();
            var item = run.Party.AddEquipment(new EquipmentDefinition("hover-shield", "行军盾", new Stats(0, 1, 0, 0)));
            Press(map, "event-party-editor");
            Press(map, "back-party-editor");
            string tileName = "run-backpack-item-" + item.Id;

            VisualElement Hover(VisualElement root)
            {
                var tile = root.Q(tileName);
                Assert.That(tile, Is.Not.Null);
                using var enter = PointerEnterEvent.GetPooled(new Event
                    { type = EventType.MouseMove, mousePosition = tile.worldBound.center });
                Send(tile, enter);
                var tip = root.Q("equipment-tooltip");
                Assert.That(tip?.Q<Label>("equipment-tooltip-name")?.text, Is.EqualTo("行军盾"));
                return tip;
            }

            yield return WaitForRoadstone(map);
            var mapTip = Hover(map);
            Press(map, "route-choice-0");
            float limit = Time.realtimeSinceStartup + 12;
            while (SceneManager.GetActiveScene().name != "BattlePrototype" && Time.realtimeSinceStartup < limit)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
            var battle = Root<BattlePrototype>();
            while (!(battle.Q(tileName)?.worldBound.width > 0) && Time.realtimeSinceStartup < limit)
                yield return null;
            Assert.That(mapTip.panel, Is.Null);
            Assert.That(battle.Q<Label>("battle-phase")?.text, Is.EqualTo("入场"));

            var entranceTip = Hover(battle);
            while (battle.Q<Label>("battle-phase")?.text == "入场" && Time.realtimeSinceStartup < limit)
                yield return null;
            Assert.That(battle.Q<Label>("battle-phase")?.text, Is.EqualTo("第 1 回合"));
            Assert.That(entranceTip.panel, Is.Null);
            var roundTip = Hover(battle);
            var currentTile = battle.Q(tileName);
            using var leave = PointerLeaveEvent.GetPooled(new Event
                { type = EventType.MouseMove, mousePosition = currentTile.worldBound.max + Vector2.one });
            Send(currentTile, leave);
            Assert.That(roundTip.panel, Is.Null);
            Assert.That(battle.Q("equipment-tooltip"), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TenNodeRouteCanCompleteIncludingLootAndTheFinalBoss()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            ConfirmFirstPair(Root<AdventureEventHub>());
            var hub = Object.FindAnyObjectByType<AdventureEventHub>();
            run = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private).GetValue(hub);
            var mapArt = (AdventureContentAsset)typeof(AdventureEventHub).GetField("assets", Private).GetValue(hub);
            Assert.That(run.IsInspection, Is.False);
            AddTestWeapons();
            bool tookRightBranch = false, checkedLeftTurn = false;
            for (int stage = 1; stage <= 10; stage++)
            {
                var previousVisit = run.Current;
                Assert.That(run.Stage + (previousVisit != null ? 1 : 0), Is.EqualTo(stage));
                var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                var map = root.Q("dock-route-signs");
                Assert.That(root.Q("dock-route"), Is.Null);
                Assert.That(map, Is.Not.Null);
                yield return WaitForShow();
                yield return null;
                var choices = map.Query<Button>().ToList();
                var kinds = previousVisit == null ? run.RouteOptions : run.FollowingOptions;
                Assert.That(choices, Has.Count.EqualTo(kinds.Count));
                for (int i = 0; i < choices.Count; i++)
                {
                    var stop = (RouteStop)choices[i].userData;
                    Assert.That(choices[i].text, Is.Empty);
                    Assert.That(choices[i].Q<Label>(), Is.Null);
                    Assert.That(choices[i].tooltip, Is.Null.Or.Empty);
                    Assert.That(stop.Number, Is.EqualTo(stage));
                    Assert.That(stop.Kind, Is.EqualTo(kinds[i]));
                    string direction = choices.Count > 1 && i == 0 ? "Left arrow" : "Right arrow";
                    string arrowPath = (direction == "Left arrow" ? "Left" : "Right") + " stone signpost/" + direction;
                    float visibleBy = Time.realtimeSinceStartup + 3f;
                    while ((GameObject.Find(arrowPath) == null || !choices[i].enabledSelf) && Time.realtimeSinceStartup < visibleBy) yield return null;
                    var arrow = GameObject.Find(arrowPath);
                    Assert.That(arrow, Is.Not.Null);
                    var foot = arrow.transform.parent.Find("Foot").GetComponent<Renderer>().bounds;
                    var deck = Object.FindAnyObjectByType<DockPlayhouse>().transform
                        .Find("Half-round stage/Deck and joists").GetComponent<Renderer>().bounds;
                    Assert.That(foot.min.y, Is.EqualTo(deck.max.y).Within(.005f));
                    Assert.That(foot.min.z - deck.min.z, Is.InRange(0f, foot.size.z));
                    var ink = new MaterialPropertyBlock();
                    arrow.transform.Find("Route emblem").GetComponent<MeshRenderer>().GetPropertyBlock(ink);
                    Assert.That(ink.GetTexture("_MainTex"), Is.SameAs(mapArt.NodeIcon(stop.Kind.Value)));
                    Assert.That(choices[i].Q("sign-rope"), Is.Null);
                    Assert.That(root.panel.Pick(choices[i].worldBound.center), Is.SameAs(choices[i]));
                }
                if (choices.Count == 2)
                {
                    Assert.That(choices[0].worldBound.center.x, Is.LessThan(choices[1].worldBound.center.x));
                }
                int choiceIndex = choices.Count == 2 && !tookRightBranch ? 1 : 0;
                tookRightBranch |= choiceIndex == 1;
                var selected = (RouteStop)choices[choiceIndex].userData;
                var seat = Camera.main.transform.position;
                var sign = choices[choiceIndex];
                Assert.That(root.panel.Pick(sign.worldBound.center), Is.SameAs(sign));
                Down(sign);
                Up(sign, sign.worldBound.center);
                float selectionDeadline = Time.realtimeSinceStartup + 3f;
                while (run.Current == previousVisit && Time.realtimeSinceStartup < selectionDeadline) yield return null;
                var kind = run.Current.Kind;
                Assert.That(kind, Is.EqualTo(selected.Kind));
                bool left = choices.Count == 2 && choiceIndex == 0;
                if (stage == 1 || left && !checkedLeftTurn)
                {
                    checkedLeftTurn |= left;
                    float deadline = Time.realtimeSinceStartup + 6;
                    var turning = default(GameObject);
                    while (turning == null && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                        turning = GameObject.Find("Travelling backcloth");
                        if (turning != null && Mathf.Abs(turning.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Roll")) < .1f)
                            turning = null;
                    }
                    Assert.That(turning, Is.Not.Null, "The first battle should arrive on the same stage.");
                    var cloth = turning.GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.That(cloth.GetTexture("_MainTex"), Is.Not.Null);
                    Assert.That(cloth.GetTexture("_NextTex"), Is.Not.Null);
                    Assert.That(Vector3.Distance(Camera.main.transform.position, seat), Is.LessThan(.001f));
                    var shaft = GameObject.Find("Semicircular deck shaft").transform;
                    Assert.That(shaft.forward, Is.EqualTo(Vector3.forward));
                    Assert.That(left ? -shaft.right.y : shaft.right.y, Is.GreaterThan(0), "The chosen side should rise.");
                    var foldingSign = GameObject.Find((left ? "Left" : "Right") + " stone signpost");
                    Assert.That(foldingSign, Is.Not.Null);
                    Assert.That(foldingSign.transform.IsChildOf(shaft), Is.True);
                    Assert.That(foldingSign.transform.parent.name, Is.EqualTo("Roadstone hinge"));
                    Assert.That(Vector3.Angle(foldingSign.transform.parent.up, shaft.up), Is.GreaterThan(85));
                    Assert.That(Vector3.Dot(shaft.Find("Reverse face").up, shaft.up), Is.LessThan(-.999f));
                    var floorBounds = Object.FindAnyObjectByType<DockPlayhouse>().transform.Find(
                        "Half-round stage/Deck and joists").GetComponent<Renderer>().bounds;
                    Assert.That(shaft.position.x, Is.EqualTo(floorBounds.center.x).Within(.001f));
                    Assert.That(shaft.position.z, Is.EqualTo(floorBounds.min.z).Within(.001f));
                }
                if (kind == NodeKind.Event)
                {
                    yield return WaitForShow();
                    root = Root<AdventureEventHub>();
                    Assert.That(root.Q("dock-route-signs"), Is.Null);
                    Assert.That(root.Q("event-window"), Is.Not.Null);
                    Assert.That(root.Q("close-event"), Is.Null);
                    Press(root, run.Current.Event == AdventureEventKind.LockedChest ? "chest-skip" : "decline-event");
                }
                else if (kind == NodeKind.TreasureChest)
                {
                    Assert.That(choices, Has.Count.EqualTo(1));
                    Assert.That(stage, Is.InRange(3, 8));
                    yield return WaitForShow();
                    root = Root<AdventureEventHub>();
                    var window = root.Q("event-window");
                    Assert.That(window.Q("event-panel"), Is.Null);
                    var chest = GameObject.Find("Harbour treasure chest").GetComponent<MeshRenderer>();
                    var ink = new MaterialPropertyBlock();
                    chest.GetPropertyBlock(ink);
                    Assert.That(ink.GetTexture("_MainTex").name,Is.EqualTo("HarbourChest-Closed"));
                    Assert.That(window.Q("chest-reward-0"), Is.Null);
                    var latch = window.Q<Button>("open-treasure-chest");
                    yield return null;
                    Down(latch); Up(latch,latch.worldBound.center);
                    Assert.That(window.Q("chest-reward-0"), Is.Null);
                    float openingDeadline = Time.realtimeSinceStartup + 3f;
                    while (window.Q("chest-reward-0")?.enabledInHierarchy != true && Time.realtimeSinceStartup < openingDeadline)
                        yield return null;
                    chest.GetPropertyBlock(ink);
                    Assert.That(ink.GetTexture("_MainTex").name, Is.EqualTo("HarbourChest-Open"));
                    Assert.That(window.Q("dock-route-signs"), Is.Null);
                    Assert.That(window.Query<Button>().ToList().Count(button => button.name?.StartsWith("chest-reward-") == true),
                        Is.EqualTo(3));
                    yield return null;
                    int items = run.Party.Inventory.Count + run.Party.SkillBooks.Count;
                    var origin = window.Q("chest-reward-1").Q<Image>().worldBound.center;
                    var prize = window.Q<Button>("chest-reward-1");
                    Assert.That(prize.Q("sign-rope"),Is.Null);
                    Down(prize); Up(prize,prize.worldBound.center);
                    Assert.That(run.Party.Inventory.Count + run.Party.SkillBooks.Count, Is.EqualTo(items + 1));
                    Assert.That(window.Q("chest-reward-0"), Is.Null);
                    var rewardTile = root.Q("run-backpack-items").Children().Last();
                    Assert.That(rewardTile.name, Is.EqualTo("run-backpack-item-" + run.Party.BackpackItems.Last().Id));
                    Assert.That(rewardTile.style.opacity.value, Is.Zero);
                    var flight = root.Q("reward-flight");
                    bool movedTowardBackpack = false;
                    float flightDeadline = Time.realtimeSinceStartup + 2f;
                    while (flight.panel != null && Time.realtimeSinceStartup < flightDeadline)
                    {
                        yield return null;
                        if (flight.panel != null && flight.resolvedStyle.opacity > 0.3f)
                        {
                            var destination = rewardTile.worldBound.center;
                            Assert.That(flight.worldBound.center.y, Is.GreaterThanOrEqualTo(destination.y - 1f));
                            movedTowardBackpack |= Vector2.Distance(flight.worldBound.center, destination) <
                                Vector2.Distance(origin, destination) * 0.8f;
                        }
                    }
                    Assert.That(movedTowardBackpack, Is.True);
                    Assert.That(rewardTile.style.opacity.value, Is.EqualTo(1));
                    Assert.That(window.Q("finish-event"), Is.Null);
                }
                else if (kind == NodeKind.Camp)
                {
                    yield return WaitScene("Camp");
                    Assert.That(Root<CampPrototype>().Q("return-event-select"), Is.Null);
                }
                else if (kind == NodeKind.Town)
                {
                    yield return WaitScene("Town");
                    Assert.That(Root<DockTown>().Q("return-event-select"), Is.Null);
                }
                else
                {
                    yield return WaitScene("BattlePrototype"); FastBattle();
                    var battleScene = Object.FindAnyObjectByType<BattlePrototype>();
                    var room = (ScriptableObject)typeof(BattlePrototype).GetField("bossRoom", Private).GetValue(battleScene);
                    if (kind == NodeKind.Boss)
                        Assert.That(room?.name, Is.EqualTo(run.Current.Battle.Enemies.First(e => e.Character.Rank == EnemyRank.Boss).Character.Id));
                    else
                        Assert.That(room, Is.Null);
                    Assert.That(Root<BattlePrototype>().Q("start"), Is.Null);
                    yield return WaitSettlement();
                    Assert.That(run.Failed, Is.False);
                    var battle = Root<BattlePrototype>();
                    if (stage == 1)
                    {
                        yield return null;
                        var gear = battle.Q<Button>("open-settings");
                        Assert.That(gear.enabledInHierarchy, Is.True);
                        Assert.That(gear.worldBound.yMax, Is.LessThanOrEqualTo(battle.Q("victory-rewards").worldBound.yMin));
                        Press(battle, "open-settings");
                        Assert.That(battle.Q<Button>("claim-route-loot-0").enabledInHierarchy, Is.False);
                        Press(battle, "close-settings");

                        RemoveTestWeapons();
                        int pendingGold = run.Current.Battle.Reward.Gold;
                        var pendingChoices = run.Current.Rewards.Select(item => item.Id).ToArray();
                        SaveAndExit(battle);
                        yield return WaitScene("MainMenu");
                        ResetAdventureFlow();
                        Press(Root<PrototypeSceneMenu>(), "open-route");
                        yield return WaitScene("BattlePrototype");
                        battle = Root<BattlePrototype>();
                        run = CurrentBattleRoute();
                        Assert.That(battle.Q("victory-rewards"), Is.Not.Null);
                        Assert.That(run.Current.Battle.Reward.Gold, Is.EqualTo(pendingGold));
                        Assert.That(run.Current.Rewards.Select(item => item.Id), Is.EqualTo(pendingChoices));
                    }
                    int gold = run.Party.Gold;
                    int itemCount = run.Party.Inventory.Count + run.Party.SkillBooks.Count;
                    if (kind is NodeKind.EliteBattle or NodeKind.Boss)
                        Assert.That(battle.Q<Button>("claim-route-loot-1"), Is.Not.Null);
                    Press(battle, "claim-route-loot-0");
                    Assert.That(battle.Q("claim-route-loot-1"), Is.Null);
                    Assert.That(run.Party.Inventory.Count + run.Party.SkillBooks.Count, Is.EqualTo(itemCount + 1));
                    Assert.That(run.Party.Gold, Is.EqualTo(gold));
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
                    ClaimBonusLoot(battle);
                    if (stage == 1)
                    {
                        int unclaimedGold = run.Current.Battle.Reward.Gold;
                        int itemsAfterClaim = run.Party.Inventory.Count + run.Party.SkillBooks.Count;
                        SaveAndExit(battle);
                        yield return WaitScene("MainMenu");
                        ResetAdventureFlow();
                        Press(Root<PrototypeSceneMenu>(), "open-route");
                        yield return WaitScene("BattlePrototype");
                        battle = Root<BattlePrototype>();
                        run = CurrentBattleRoute();
                        Assert.That(run.Party.Gold, Is.EqualTo(gold));
                        Assert.That(run.Party.Inventory.Count + run.Party.SkillBooks.Count, Is.EqualTo(itemsAfterClaim));
                        Assert.That(run.Current.Battle.Reward.Gold, Is.EqualTo(unclaimedGold));
                        Assert.That(battle.Q("victory-rewards"), Is.Not.Null);
                        Assert.That(battle.Q("claim-route-loot-0"), Is.Null);
                        Assert.That(battle.Q("claim-gold"), Is.Not.Null);
                        AddTestWeapons();
                    }
                    Press(battle, "claim-gold");
                    Assert.That(run.Party.Gold, Is.GreaterThan(gold));
                }
                if (kind is NodeKind.NormalBattle or NodeKind.EliteBattle or NodeKind.Boss)
                    yield return WaitScene("EventSelect");
                else
                {
                    var currentPanel = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                    float untilSigns = Time.realtimeSinceStartup + 6;
                    while (currentPanel.Q("dock-route-signs") == null && Time.realtimeSinceStartup < untilSigns)
                        yield return null;
                    Assert.That(currentPanel.Q("dock-route-signs"), Is.Not.Null, kind.ToString());
                }
                if (stage < DockRoute.Stages)
                    Assert.That((run.RouteDestination ?? run.RouteCurrent).Lane, Is.EqualTo(selected.Lane));
            }
            Assert.That(tookRightBranch && checkedLeftTurn, Is.True, "Both choices should be exercised on the route.");
            Assert.That(run.CompletedNodes, Is.EqualTo(10));
            Assert.That(run.InGraveyard && run.Floor == 2, Is.True);
            Assert.That(run.Stage, Is.EqualTo(1));
            int dayAtGate = run.Party.Day;
            RemoveTestWeapons();
            int finalGold = run.Party.Gold;
            var rewardIds = run.Party.Inventory.Concat(run.Party.SkillBooks).Select(item => item.Id).ToArray();
            SaveAndExit(Root<AdventureEventHub>());
            yield return WaitScene("MainMenu");
            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            run = CurrentRoute();
            Assert.That(run.CompletedNodes, Is.EqualTo(10));
            Assert.That(run.Floor, Is.EqualTo(2));
            Assert.That(run.Party.Day, Is.EqualTo(dayAtGate));
            Assert.That(run.Party.Gold, Is.EqualTo(finalGold));
            Assert.That(run.Party.Inventory.Concat(run.Party.SkillBooks).Select(item => item.Id), Is.EqualTo(rewardIds));
            var newFloor = Root<AdventureEventHub>().Q("dock-route-signs");
            Assert.That(newFloor.Q("route-next-floor"), Is.Null);
            Assert.That(newFloor.Q<Label>("route-region").text, Does.Contain("低语墓园"));
            Assert.That(GameObject.Find("Cemetery gate"), Is.Not.Null);
            Assert.That(run.RouteCurrent.Number, Is.Zero);
            AddTestWeapons();
            yield return WaitForRoadstone(newFloor);
            Press(newFloor, "route-choice-0");
            yield return WaitScene("WhisperingGraveyard");
            Assert.That(run.Party.Day, Is.EqualTo(dayAtGate + 1));
            Assert.That(run.Current.Battle.Enemies.All(e => e.Character.EnemyBehavior != null), Is.True);
            FastBattle();
            yield return WaitSettlement();
            RemoveTestWeapons();
            SaveAndExit(Root<BattlePrototype>());
            yield return WaitScene("MainMenu");
            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("WhisperingGraveyard");
            var graveBattle = Root<BattlePrototype>();
            run = CurrentBattleRoute();
            Assert.That(run.Floor, Is.EqualTo(2));
            Assert.That(run.Party.Day, Is.EqualTo(dayAtGate + 1));
            Assert.That(graveBattle.Q("victory-rewards"), Is.Not.Null);
            Press(graveBattle, "claim-route-loot-0");
            ClaimBonusLoot(graveBattle);
            Press(graveBattle, "claim-gold");
            yield return WaitScene("EventSelect");
            Assert.That(run.Stage, Is.EqualTo(2));

            var routeRoot = Root<AdventureEventHub>();
            Press(routeRoot, "open-settings");
            Press(routeRoot.Q("run-settings"), "reset-adventure");
            ConfirmFirstPair(routeRoot);
            var resetRun = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private)
                .GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
            Assert.That(resetRun.Floor, Is.EqualTo(1));
            Assert.That(resetRun.CompletedNodes, Is.Zero);
            Assert.That(routeRoot.Q("run-settings"), Is.Null);
            Assert.That(routeRoot.Q("dock-route-signs").Q("route-choice-0"), Is.Not.Null);
        }

        private static void ClaimBonusLoot(VisualElement battle)
        {
            foreach (string button in battle.Q("victory-rewards").Query<Button>().ToList()
                .Where(view => view.name?.StartsWith("claim-loot-") == true).Select(view => view.name).ToArray())
                Press(battle, button);
        }

        private static AdventureRun CurrentBattleRoute() => (AdventureRun)typeof(BattlePrototype).GetField("adventure", Private)
            .GetValue(Object.FindAnyObjectByType<BattlePrototype>());

        private void RemoveTestWeapons()
        {
            foreach (var member in run.Party.Members)
                Assert.That(run.Party.TryEquip(member.Id, 0, null), Is.True);
            var testWeapons = run.Party.Inventory.Where(item => item.Definition.Id == "test-power")
                .Select(item => item.Id).ToArray();
            Assert.That(run.Party.TryConsumeItems(testWeapons), Is.True);
        }

        private void AddTestWeapons()
        {
            foreach (var member in run.Party.Members)
                run.Party.TryEquip(member.Id, 0, run.Party.AddEquipment(new EquipmentDefinition("test-power", "测试武器", new Stats(100, 0, 0, 0))).Id);
        }

        [UnityTest]
        public IEnumerator LoadingAnAdventureBattlePreservesWoundsAndCampRestHealsOnlyOnce()
        {
            yield return OpenMenu();
            var enemyDefinition = new CharacterDefinition("durable", "test", Profession.Warrior, Personality.Stoic,
                new Stats(1, 999, 999, 9999), 10,
                new ActionDefinition("basic", "test", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            var enemy = new Combatant("durable", enemyDefinition, Side.Enemy, 1, enemyDefinition.BaseStats, 9999);
            var request = run.Party.BeginBattle(new[] { enemy }, 10, null);
            var wounded = request.Combatants.First(u => u.Side == Side.Party && u.Health > 1);
            int health = wounded.Health - 1;
            var finalHealth = request.Combatants.ToDictionary(u => u.Id, u => u.Id == wounded.Id ? health : u.Health);
            var result = new BattleResult(request.Id, BattleOutcome.Timeout, finalHealth, System.Array.Empty<BattleEvent>());
            Assert.That(run.Party.TrySettle(result, out _), Is.True);
            Press(Root<AdventureEventHub>(), "inspect-node-NormalBattle"); yield return WaitScene("BattlePrototype");
            Assert.That(run.Party.Members.Single(m => m.Id == wounded.Id).Health, Is.EqualTo(health),
                "Entering a route battle must not use the rehearsal's full-health reset.");
            Press(Root<BattlePrototype>(), "open-settings");
            Press(Root<BattlePrototype>(), "return-main-menu"); yield return WaitScene("MainMenu");
            Press(Root<PrototypeSceneMenu>(), "open-events"); yield return WaitScene("EventSelect");
            Press(Root<AdventureEventHub>(), "finish-event");
            yield return WaitForEventExit(Root<AdventureEventHub>());
            Press(Root<AdventureEventHub>(), "inspect-node-Camp"); yield return WaitScene("Camp");
            Press(Root<CampPrototype>(), "camp-rest");
            Assert.That(run.Party.Members.Single(m => m.Id == wounded.Id).Health, Is.GreaterThan(health));
            Assert.That(Root<CampPrototype>().Q<Button>("camp-rest").enabledInHierarchy, Is.False);
        }

        private static void FastBattle() => typeof(BattlePrototype).GetField("attackAnimationTimeScale", Private)
            .SetValue(Object.FindAnyObjectByType<BattlePrototype>(), 0.05f);

        private static IEnumerator WaitSettlement()
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (Root<BattlePrototype>().Q<Label>("battle-phase")?.text != "战斗结算" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(Root<BattlePrototype>().Q<Label>("battle-phase").text, Is.EqualTo("战斗结算"));
        }

        private static VisualElement Root<T>() where T : MonoBehaviour =>
            Object.FindAnyObjectByType<T>().GetComponent<UIDocument>().rootVisualElement;

        private static IEnumerator WaitForEventExit(VisualElement root)
        {
            float until = Time.realtimeSinceStartup + 4;
            while (root.panel != null && root.Q("event-window") != null && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(root.panel == null || root.Q("event-window") == null);
        }

        [UnityTest]
        public IEnumerator DockChangesSceneryBehindOnePairOfTravellers()
        {
            yield return OpenMenu();
            var map = Root<AdventureEventHub>();
            Press(map, "inspect-event-FoundCoins");
            var house = Object.FindAnyObjectByType<DockPlayhouse>();
            Assert.That(house, Is.Not.Null);
            var camera = Camera.main;
            var seat = camera.transform.position;
            var lens = camera.projectionMatrix;
            var opening = house.transform.Find("Half-round stage/Backcloth opening");
            var rear = camera.WorldToViewportPoint(opening.position);
            foreach (string part in new[] { "Port gallery", "Starboard gallery", "Fly loft" })
            {
                var bounds = house.transform.Find("Half-round stage/" + part).GetComponent<MeshRenderer>().bounds;
                Assert.That(bounds.size.z, Is.GreaterThan(8), part);
                Assert.That(bounds.min.z, Is.LessThan(0), part);
                Assert.That(bounds.max.z, Is.EqualTo(opening.position.z).Within(.21f), part);
            }
            void AssertSightline(Transform service)
            {
                camera = Camera.main;
                Assert.That(Vector3.Distance(camera.transform.position, seat), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.That(camera.projectionMatrix, Is.EqualTo(lens));
                Assert.That(Vector3.Distance(camera.WorldToViewportPoint(opening.position), rear), Is.LessThan(.001f));
                var floor = service.Find("Stage floor").GetComponent<Renderer>().bounds;
                var boards = house.transform.Find("Half-round stage/Deck and joists").GetComponent<Renderer>().bounds;
                Assert.That(floor.min.z, Is.EqualTo(boards.min.z).Within(.0002f));
                Assert.That(floor.max.z, Is.EqualTo(opening.position.z).Within(.0002f));
                Assert.That(floor.center.y - boards.max.y, Is.EqualTo(.001f).Within(.0002f));
            }
            AssertSightline(Object.FindAnyObjectByType<AdventureEventHub>().transform.Find("Service theatre"));
            var right = house.transform.Find("Curtain R").GetComponent<MeshRenderer>();
            var left = house.transform.Find("Curtain L").GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            float Travel(MeshRenderer r) { r.GetPropertyBlock(block); return block.GetFloat("_Travel"); }
            Assert.That(Travel(right), Is.Zero);
            var stalls = house.transform.Find("Stalls");
            Assert.That(stalls.childCount, Is.EqualTo(2));
            var chair = stalls.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(chair.bounds.size.z, Is.GreaterThan(.4f));
            bool intermediate = false;
            float expiry = Time.realtimeSinceStartup + 4;
            while (Travel(right) < 1 && Time.realtimeSinceStartup < expiry)
            {
                intermediate |= Travel(right) > .05f && Travel(right) < .95f;
                Assert.That(Travel(left), Is.EqualTo(Travel(right)));
                yield return null;
            }
            Assert.That(intermediate, Is.True);
            Assert.That(Travel(right), Is.EqualTo(1));
            Press(map, "close-event");
            Assert.That(map.Q("event-window"), Is.Not.Null);
            bool closing = false;
            while (house.gameObject.activeSelf && Time.realtimeSinceStartup < expiry)
            {
                closing |= Travel(right) > .05f && Travel(right) < .95f;
                Assert.That(map.Q("adventure-screen").style.display.value, Is.EqualTo(DisplayStyle.None));
                yield return null;
            }
            Assert.That(closing, Is.True);
            Assert.That(Travel(right), Is.Zero);
            yield return WaitForEventExit(map);
            yield return OpenShopFromInspection();
            Assert.That(Object.FindAnyObjectByType<DockPlayhouse>(), Is.SameAs(house));
            Assert.That(GameObject.Find("Shop route sign return-event-select/Carved plank"), Is.Not.Null);
            AssertSightline(Object.FindAnyObjectByType<DockShop>().transform.Find("Service theatre"));
        }

        private static IEnumerator OpenShopFromInspection()
        {
            var hub = Root<AdventureEventHub>();
            Assert.That(hub.Q("inspect-node-Shop"), Is.Null);
            Press(hub, "inspect-node-Town");
            yield return WaitScene("Town");
            Press(Root<DockTown>(), "town-service-Shop");
            yield return WaitScene("DockShop");
        }

        private static IEnumerator WaitForRoadstone(VisualElement root, string container = "dock-route-signs")
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (root.Q(container)?.Q<Button>()?.enabledInHierarchy != true && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q(container)?.Q<Button>()?.enabledInHierarchy, Is.True);
        }

        private static IEnumerator WaitForShow()
        {
            var property = typeof(DockPlayhouse).GetProperty("Moving", PrivateStatic);
            float until = Time.realtimeSinceStartup + 9;
            yield return null;
            while ((bool)property.GetValue(null) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That((bool)property.GetValue(null), Is.False, "The change of scenery must release the stage.");
        }

        private static IEnumerator WaitScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
            if (Flow.GetProperty("OnRoute", PrivateStatic).GetValue(null) is true) yield return WaitForShow();
            if (name is "TalentMarket" or "DockShop" or "Camp")
            {
                var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                if (root.Q("town-return-sign") != null) yield return WaitForRoadstone(root, "town-return-sign");
            }
        }

        private static void AssertNoVisibleTiers(VisualElement root) => Assert.That(root.Query<Label>().ToList()
            .Any(l => Regex.IsMatch(l.text ?? "", @"\bT[0-3]\b")), Is.False);

        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name); Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            using var evt = NavigationSubmitEvent.GetPooled(); Send(button, evt);
        }

        private static void Down(VisualElement element)
        {
            using var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = element.worldBound.center });
            Send(element, evt);
        }

        private static void Up(VisualElement root, Vector2 point)
        {
            using var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = point }); Send(root, evt);
        }

        private static void Send(VisualElement element, EventBase evt) { evt.target = element; element.SendEvent(evt); }
    }
}
