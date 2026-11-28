using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Presentation;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RaidPartyManager.Tests
{
    public sealed class CampTests
    {
        private static readonly PropertyInfo ContractsFeature = typeof(CampPrototype).Assembly
            .GetType("RaidPartyManager.Presentation.AdventureFlow")
            .GetProperty("ContractsEnabled", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void ResetSession() => typeof(BattlePrototype)
            .GetMethod("ResetPlaySession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        [TearDown]
        public void RestoreContractsVisibility() => ContractsFeature.SetValue(null, false);

        [UnityTest]
        public IEnumerator RotBloomWitchPlaysHerSeatedAnimationInCamp()
        {
            yield return SceneManager.LoadSceneAsync("Camp");
            yield return null;
            var camp = Object.FindAnyObjectByType<CampPrototype>();
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var content = (EncounterData)typeof(CampPrototype).GetField("content", privateInstance).GetValue(camp);
            var witch = content.CharacterCatalog.Single(character => character.Id == "RotBloomWitch");
            var party = new PartyRun(new[] { new PartyMemberStart("witch", witch, 1, witch.BaseStats.Health) },
                System.Array.Empty<EquipmentDefinition>());
            var stage = typeof(CampPrototype).GetField("stage", privateInstance).GetValue(camp);
            stage.GetType().GetMethod("SeatParty").Invoke(stage, new object[] { party, content });

            var seat = camp.transform.Find("Service theatre/camp-seat-1").GetComponent<MeshRenderer>();
            var paint = new MaterialPropertyBlock();
            seat.GetPropertyBlock(paint);
            var artwork = paint.GetTexture("_MainTex");
            Assert.That(seat.enabled, Is.True);
            Assert.That(artwork, Is.SameAs(content.CampSpriteFor(witch.Id)));
            Assert.That((artwork.width, artwork.height), Is.EqualTo((1024, 1024)));
            var first = paint.GetVector("_MainTex_ST");
            float deadline = Time.realtimeSinceStartup + 2;
            while (paint.GetVector("_MainTex_ST") == first && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                seat.GetPropertyBlock(paint);
            }
            Assert.That(paint.GetVector("_MainTex_ST"), Is.Not.EqualTo(first));
        }

        [UnityTest]
        public IEnumerator CampTrainingShowsTheGainsAndDisablesTheOtherActions()
        {
            yield return SceneManager.LoadSceneAsync("Camp");
            yield return null;
            var camp = Object.FindAnyObjectByType<CampPrototype>();
            var root = camp.GetComponent<UIDocument>().rootVisualElement;
            var party = (PartyRun)typeof(CampPrototype).GetField("run", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(camp);
            var member = party.Members.First();
            Press(root, "camp-train");
            Assert.That(root.Q<Button>("train-" + member.Id).text, Does.Contain("剩余 2 次"));
            Press(root, "train-" + member.Id);
            var trained = party.Members.First();
            Assert.That(trained.Training.Sessions, Is.EqualTo(1));
            Assert.That(root.Q<Label>("camp-message").text, Does.Contain(trained.Training.Describe()).And.Contain("剩余训练次数 1"));
            Assert.That(root.Q<Button>("camp-rest").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("camp-evolve").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("camp-train").enabledInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator CampMirrorsTheCurrentPartyAndKeepsRenewalsWhenReturningToBattle()
        {
            ContractsFeature.SetValue(null, true);
            yield return SceneManager.LoadSceneAsync("Camp");
            yield return null;
            var root = Object.FindAnyObjectByType<CampPrototype>().GetComponent<UIDocument>().rootVisualElement;
            var theatre = Object.FindAnyObjectByType<CampPrototype>().transform.Find("Service theatre");
            var camera = Camera.main;
            Assert.That(camera.orthographic, Is.False);
            MeshRenderer Seat(int n) => theatre.Find($"camp-seat-{n}").GetComponent<MeshRenderer>();
            Texture Artwork(int n)
            {
                var block = new MaterialPropertyBlock();
                Seat(n).GetPropertyBlock(block);
                return block.GetTexture("_MainTex");
            }
            Vector3 ScreenAt(int n) => camera.WorldToViewportPoint(Seat(n).bounds.center);
            for (int position = 1; position <= 4; position++)
            {
                Assert.That(Seat(position).enabled, Is.True);
                Assert.That(Artwork(position), Is.Not.Null);
                Assert.That(Mathf.Sign(Seat(position).transform.localScale.x), Is.EqualTo(position <= 2 ? -1f : 1f));
            }
            Assert.That(ScreenAt(3).x, Is.LessThan(.5f));
            Assert.That(ScreenAt(2).x, Is.GreaterThan(.5f));
            Assert.That(ScreenAt(4).y, Is.LessThan(ScreenAt(3).y));
            Assert.That(ScreenAt(1).y, Is.LessThan(ScreenAt(2).y));
            var lamp = theatre.GetComponentInChildren<Light>();
            Assert.That(lamp.type, Is.EqualTo(LightType.Point));
            float initial = lamp.intensity;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(lamp.intensity, Is.Not.EqualTo(initial));
            Assert.That(Seat(1).sharedMaterial.GetVector("_FireLight").w, Is.EqualTo(lamp.intensity).Within(.001f));
            var hearth = theatre.Find("camp-fire").GetComponent<MeshRenderer>();
            var paint = new MaterialPropertyBlock();
            hearth.GetPropertyBlock(paint);
            Assert.That(paint.GetTexture("_MainTex").name, Is.EqualTo("PaperHearth-Cels"));
            float frame = paint.GetVector("_MainTex_ST").z;
            float until = Time.unscaledTime + .85f;
            while (Time.unscaledTime < until && Mathf.Approximately(frame, paint.GetVector("_MainTex_ST").z))
            {
                yield return null;
                hearth.GetPropertyBlock(paint);
            }
            Assert.That(paint.GetVector("_MainTex_ST").z, Is.Not.EqualTo(frame));
            Assert.That(Seat(1).bounds.size.y, Is.LessThan(1.85f));
            var before = Artwork(1);
            Press(root, "open-camp-editor");
            yield return null;
            var outgoing = root.Q("edit-position-1");
            Assert.That(outgoing, Is.Not.Null);
            using (var right = PointerUpEvent.GetPooled(new Event
                { type = EventType.MouseUp, button = 1, mousePosition = outgoing.worldBound.center }))
                Send(outgoing, right);
            Assert.That(root.Q<Button>("sign-crew-contract").text, Is.EqualTo("签署续约合同"));
            var party = (PartyRun)typeof(CampPrototype).GetField("run", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Object.FindAnyObjectByType<CampPrototype>());
            int salary = 18 * (int)(root.Q<LongField>("contract-signing-fee").value / 12);
            party.AddGold(salary);
            int initialBalance = party.Gold;
            root.Q<IntegerField>("contract-duration").value = 18;
            root.Q<LongField>("contract-signing-fee").value = salary;
            root.Q<Toggle>("bonus-shield").value = true;
            root.Q<IntegerField>("bonus-shield-amount").value = 6;
            Press(root, "sign-crew-contract");
            Assert.That(party.Gold, Is.EqualTo(initialBalance - salary));
            Press(root, "back-party-editor");
            yield return null;
            Assert.That(Artwork(1), Is.SameAs(before));
            Press(root, "open-settings");
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");
            Press(Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>().rootVisualElement,
                "open-battle");
            yield return WaitForScene("BattlePrototype");
            var battleRoot = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(battleRoot, "open-party-editor");
            yield return null;
            var preserved = battleRoot.Q("edit-position-1");
            using (var right = PointerUpEvent.GetPooled(new Event
                { type = EventType.MouseUp, button = 1, mousePosition = preserved.worldBound.center }))
                Send(preserved, right);
            var contract = battleRoot.Q("crew-contract");
            var terms = contract.Query<Label>().ToList();
            Assert.That(terms.Any(label => label.text.Contains("已签续约合同") && label.text.Contains("共 18 天")), Is.True);
            Assert.That(terms.Any(label => label.text.Contains("每挡下 5 点敌方伤害 6 金币")), Is.True);
            Assert.That(battleRoot.Q<Button>("sign-crew-contract").text, Is.EqualTo("签署续约合同"));
            Assert.That(contract.Q<Toggle>("bonus-shield").value, Is.True);
            Assert.That(contract.Q<IntegerField>("bonus-shield-amount").value, Is.EqualTo(6));

        }

        private static IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
        }

        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            using var evt = NavigationSubmitEvent.GetPooled();
            Send(button, evt);
        }

        private static void Send(VisualElement element, EventBase evt)
        {
            evt.target = element;
            element.SendEvent(evt);
        }
    }
}
