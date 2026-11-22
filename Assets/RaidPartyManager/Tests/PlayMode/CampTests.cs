using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
        [SetUp]
        public void ResetSession() => typeof(BattlePrototype)
            .GetMethod("ResetPlaySession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        [UnityTest]
        public IEnumerator CampMirrorsTheCurrentPartyAndKeepsRenewalsWhenReturningToBattle()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>().rootVisualElement,
                "open-camp");
            yield return WaitForScene("Camp");
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
            Assert.That(root.Q<Button>("sign-crew-contract").text, Is.EqualTo("签订新合同"));
            var party = (PartyRun)typeof(CampPrototype).GetField("run", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Object.FindAnyObjectByType<CampPrototype>());
            int salary = 18 * root.Q<IntegerField>("contract-daily-wage").value;
            party.AddGold(salary);
            int initialBalance = party.Gold;
            root.Q<IntegerField>("contract-duration").value = 18;
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
            Assert.That(terms.Any(label => label.text.Contains("剩余 18 天")), Is.True);
            Assert.That(terms.Any(label => label.text.Contains("每挡下 5 点敌方伤害 6 金币")), Is.True);
            Assert.That(battleRoot.Q<Button>("sign-crew-contract").text, Is.EqualTo("签订新合同"));
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
