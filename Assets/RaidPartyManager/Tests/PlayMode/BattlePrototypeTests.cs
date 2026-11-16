using System.Collections;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RaidPartyManager.Tests
{
    public sealed class BattlePrototypeTests
    {
        [UnityTest]
        public IEnumerator BuiltSceneSupportsPreparationPlaybackLootAndReset()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            Assert.That(scene, Is.Not.Null);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.panel, Is.Not.Null);

            Assert.That(root.Q("party-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "party-position-4", "party-position-3", "party-position-2", "party-position-1" }));
            Assert.That(root.Q("enemy-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "enemy-position-1", "enemy-position-2", "enemy-position-3", "enemy-position-4" }));
            Assert.That(root.Q("party-position-1").Children().Select(element => element.name),
                Is.EqualTo(new[] { "health-bar", "combatant-portrait", "status-guard" }));
            Assert.That(root.Q("party-position-1").Q<Image>().image, Is.Not.Null);
            Assert.That(root.Q("equipment-positions").childCount, Is.EqualTo(4));
            Assert.That(root.Q<Label>("status-guard").text, Is.EqualTo("正常"));

            Press(root, "start");
            Assert.That(root.Q<Button>("start"), Is.Null);
            float shieldDeadline = Time.realtimeSinceStartup + 5f;
            while (root.Q<Label>("status-guard").text == "正常" && Time.realtimeSinceStartup < shieldDeadline)
                yield return null;
            Assert.That(root.Q<Label>("status-guard").text, Is.EqualTo("护盾 4"));
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo("6 / 9"));
            while (root.Q<Label>("status-guard").text == "护盾 4" && Time.realtimeSinceStartup < shieldDeadline)
                yield return null;
            Assert.That(root.Q<Label>("status-guard").text, Is.EqualTo("护盾 3"));
            Press(root, "speed");
            Press(root, "speed");
            Assert.That(root.Q<Button>("speed").text, Does.EndWith("×4"));
            float deadline = Time.realtimeSinceStartup + 8f;
            while (root.Q<Button>("reset") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q<Button>("reset"), Is.Not.Null, "Timed playback should reach settlement.");
            var first = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(first.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("银叶射手"));
            Assert.That(root.Q<Label>("reward").text, Does.Contain("狂战士之印"));
            Assert.That(root.Q<Label>("status-brute").text, Is.EqualTo("倒下"));
            Assert.That(root.Q<Label>("status-flanker").text, Is.EqualTo("倒下"));
            Press(root, "reset");

            Press(root, "unplace");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.False);
            Press(root, "position-1");
            Press(root, "position-4");
            Press(root, "slot-0");
            Press(root, "item-gear-1");
            Assert.That(root.Q<Button>("item-gear-1").text, Does.Contain("篝火守夜人"));
            Press(root, "member-mage");
            Press(root, "slot-0");
            Press(root, "item-gear-1");
            Assert.That(root.Q<Button>("item-gear-1").text, Does.Contain("星辉术师"));
            Press(root, "slot-1");
            Press(root, "item-gear-1");
            Assert.That(root.Q<Button>("slot-0").text, Does.Contain("空槽"));
            Assert.That(root.Q<Button>("slot-1").text, Does.Contain("精钢壁垒"));

            Press(root, "start");
            Press(root, "skip");
            var second = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(second.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("篝火守夜人"));
            Press(root, "reset");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
            Press(root, "slot-0");
            Assert.That(root.Q<Button>("item-gear-1").text, Does.EndWith("背包"));
            Assert.That(root.Q<Button>("item-gear-2"), Is.Null);
        }

        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
