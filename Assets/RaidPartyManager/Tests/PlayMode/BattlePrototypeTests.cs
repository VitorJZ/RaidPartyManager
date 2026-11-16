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
            Assert.That(root.Q("party-position-1").Q("profession-portrait-art"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-2").Q("profession-frame-warrior"), Is.Null);
            Assert.That(root.Q("equipment-position-2").Q("profession-frame-mage"), Is.Not.Null);
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

        [UnityTest]
        public IEnumerator PartyEditorOpensProfileAndSharedBackpackOutsideCombat()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Assert.That(root.Q("editor-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "edit-position-4", "edit-position-3", "edit-position-2", "edit-position-1" }));
            Assert.That(root.Q("edit-position-4").Q("profession-frame-archer"), Is.Not.Null);
            Assert.That(root.Q("edit-position-3").Q("profession-frame-support"), Is.Not.Null);
            Assert.That(root.Q("edit-position-2").Q("profession-frame-mage"), Is.Not.Null);
            var guardCard = root.Q("edit-position-1");
            Assert.That(guardCard.Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(guardCard.Q<Label>("character-name").text, Is.EqualTo("篝火守夜人"));
            Assert.That(guardCard.Q("character-health").Q<Label>().text, Does.Contain("15/15"));

            Press(root, "edit-member-mage");
            yield return null;
            Assert.That(root.Q<Label>("details-character-name").text, Is.EqualTo("星辉术师"));
            Assert.That(root.Q("character-profile"), Is.Not.Null);
            Assert.That(root.Q("backpack-grid").Children().Select(row => row.childCount),
                Is.EqualTo(new[] { 4, 4, 4, 4 }));
            Assert.That(root.Q<Button>("back-party-editor").enabledInHierarchy, Is.False);

            var bagCell = root.Q("bag-cell-0");
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, bagCell.worldBound.center)))
                Send(bagCell, enter);
            Assert.That(root.Q<Label>("equipment-tooltip-name").text, Is.EqualTo("精钢壁垒"));
            Assert.That(root.Q<Label>("equipment-tooltip-description").text, Does.Contain("防御和特防"));
            using (var leave = PointerLeaveEvent.GetPooled(Mouse(EventType.MouseMove, Vector2.one)))
                Send(bagCell, leave);
            Assert.That(root.Q("equipment-tooltip"), Is.Null);

            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, bagCell.worldBound.center)))
                Send(bagCell, enter);
            Down(bagCell);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Up(bagCell, bagCell.worldBound.center);
            Assert.That(root.Q("edit-item-gear-1"), Is.Not.Null);

            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, bagCell.worldBound.center)))
                Send(bagCell, enter);
            yield return Drag(root, bagCell, root.Q("edit-slot-0"));
            Assert.That(root.Q<Label>("equipped-name-0").text, Is.EqualTo("精钢壁垒"));
            Assert.That(root.Q("edit-item-gear-1"), Is.Null);
            yield return null;
            yield return Drag(root, root.Q("edit-slot-0"), root.Q("bag-cell-0"));
            Assert.That(root.Q<Label>("equipped-name-0").text, Does.Contain("装备槽"));
            Assert.That(root.Q("bag-cell-0").Q("edit-item-gear-1"), Is.Not.Null);
            Press(root, "close-character-details");
            Assert.That(root.Q("character-details"), Is.Null);

            Press(root, "edit-member-guard");
            yield return null;
            yield return Drag(root, root.Q("bag-cell-0"), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");

            Press(root, "slot-1");
            Assert.That(root.Q<Button>("slot-1").text, Does.Contain("精钢壁垒"));
            Press(root, "unplace");
            Press(root, "open-party-editor");
            Assert.That(root.Q("edit-position-1").Q<Button>("edit-member-guard"), Is.Null);
            Press(root, "edit-member-guard");
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Press(root, "position-1");

            Press(root, "start");
            Assert.That(root.Q<Button>("open-party-editor"), Is.Null);
            Press(root, "skip");
            Press(root, "open-party-editor");
            var archerHealth = root.Q("edit-position-4").Q("character-health").Q<Label>().text;
            Assert.That(archerHealth, Does.Not.Contain("9/9"));
            Press(root, "edit-member-guard");
            yield return null;
            Assert.That(root.Q("bag-cell-0").Q("edit-item-gear-2"), Is.Not.Null);
            var rewardCell = root.Q("bag-cell-0");
            Down(rewardCell);
            yield return WaitForDrag(root);
            Up(root.Q("character-details-layer").ElementAt(0), Vector2.one);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Assert.That(root.Q("bag-cell-0").Q("edit-item-gear-2"), Is.Not.Null);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));

            yield return Drag(root, root.Q("bag-cell-0"), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("狂战士之印"));
            Assert.That(root.Q("bag-cell-0").Q("edit-item-gear-1"), Is.Not.Null);
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Label>("reward").text, Does.Contain("狂战士之印"));
        }

        private static IEnumerator Drag(VisualElement root, VisualElement source, VisualElement target)
        {
            Down(source);
            yield return WaitForDrag(root);
            Assert.That(root.Q("equipment-tooltip"), Is.Null);
            Vector2 destination = target.worldBound.center;
            using (var move = PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, destination)))
                Send(target, move);
            Up(target, destination);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
        }

        private static IEnumerator WaitForDrag(VisualElement root)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (root.Q("equipment-drag-preview") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q("equipment-drag-preview"), Is.Not.Null, "Holding the item should begin a drag.");
        }

        private static Event Mouse(EventType type, Vector2 point) =>
            new Event { type = type, mousePosition = point, button = 0, clickCount = 1 };

        private static void Send(VisualElement target, EventBase evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }

        private static void Down(VisualElement element)
        {
            using var evt = PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, element.worldBound.center));
            Send(element, evt);
        }

        private static void Up(VisualElement element, Vector2 point)
        {
            using var evt = PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, point));
            Send(element, evt);
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
