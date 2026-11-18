using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidPartyManager.Presentation;
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
        public IEnumerator CampMirrorsTheCurrentPartyAndKeepsReplacementsWhenReturningToBattle()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>().rootVisualElement,
                "open-camp");
            yield return WaitForScene("Camp");
            var root = Object.FindAnyObjectByType<CampPrototype>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Query<Image>().ToList().Count(image => image.name.StartsWith("camp-seat-")), Is.EqualTo(4));
            for (int position = 1; position <= 4; position++)
            {
                var seated = root.Q<Image>($"camp-seat-{position}");
                Assert.That(seated.image, Is.Not.Null);
                Assert.That(seated.resolvedStyle.scale.value.x, Is.EqualTo(position <= 2 ? -1f : 1f));
            }
            var stage = root.Q("camp-stage");
            var leftRear = root.Q("camp-seat-3");
            var rightRear = root.Q("camp-seat-2");
            var leftFront = root.Q("camp-seat-4");
            var rightFront = root.Q("camp-seat-1");
            Assert.That(leftRear.worldBound.center.x, Is.LessThan(stage.worldBound.center.x));
            Assert.That(rightRear.worldBound.center.x, Is.GreaterThan(stage.worldBound.center.x));
            Assert.That(leftFront.worldBound.center.y, Is.GreaterThan(leftRear.worldBound.center.y));
            Assert.That(rightFront.worldBound.center.y, Is.GreaterThan(rightRear.worldBound.center.y));
            Assert.That(root.Q("camp-fire").worldBound.center.y, Is.GreaterThan(leftFront.worldBound.center.y));

            var before = root.Q<Image>("camp-seat-1").image;
            Press(root, "open-camp-editor");
            yield return null;
            var outgoing = root.Q("edit-position-1");
            Assert.That(outgoing, Is.Not.Null);
            using (var right = PointerUpEvent.GetPooled(new Event
                { type = EventType.MouseUp, button = 1, mousePosition = outgoing.worldBound.center }))
                Send(outgoing, right);
            var candidates = root.Q("replacement-candidates").Query<Button>().ToList();
            Assert.That(candidates.Count, Is.EqualTo(35));
            var selected = candidates.First(button => button.enabledInHierarchy);
            string characterId = selected.name.Substring("replace-with-".Length);
            Press(root, selected.name);
            Press(root, "back-party-editor");
            yield return null;
            var replacement = root.Q<Image>("camp-seat-1").image;
            Assert.That(replacement, Is.Not.SameAs(before));
            Assert.That(replacement.name, Does.Contain(characterId));
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
            Assert.That(battleRoot.Q<Button>($"replace-with-{characterId}").enabledInHierarchy, Is.False,
                "The seated party and the battle party must share the same run.");
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
