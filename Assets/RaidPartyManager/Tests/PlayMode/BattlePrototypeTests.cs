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
    public sealed class BattlePrototypeTests
    {
        [SetUp]
        public void StartWithFreshPlaySession() =>
            typeof(BattlePrototype).GetMethod("ResetPlaySession", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

        [UnityTest]
        public IEnumerator EquipmentPersistsAcrossMainMenuTemplateAndLeavingABattle()
        {
            Assert.That(SceneUtility.GetScenePathByBuildIndex(0), Is.EqualTo("Assets/Scenes/MainMenu.unity"));
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;

            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
            var root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            yield return null;
            yield return Drag(root, root.Q("bag-cell-0"), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));
            Press(root, "close-character-details");
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");

            PressMenu("open-template");
            yield return WaitForScene("SampleScene");
            PressMenu("return-main-menu");
            yield return WaitForScene("MainMenu");

            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));
            Assert.That(root.Q("edit-item-gear-1"), Is.Null);
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Press(root, "slot-0");
            var potionButton = root.Q("quick-equipment").Query<Button>().ToList()
                .First(button => button.text.StartsWith("盛气药水"));
            string spentPotion = potionButton.name;
            Press(root, spentPotion);
            Press(root, "start");
            Assert.That(root.Q("equipment-position-1").Query<Label>().ToList()
                .Any(label => label.text == "1 · 空槽"), Is.True);
            yield return null;
            var vigor = root.Q("status-row-guard").Q("status-badge-Vigor");
            Assert.That(vigor.Q<Label>("status-stack-count").text, Is.EqualTo("3"));
            Assert.That(vigor.Q<Image>().image, Is.Not.Null);
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");

            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
            Assert.That(root.Q<Button>("slot-1").text, Does.Contain("精钢壁垒"));
            Assert.That(root.Q<Button>("slot-0").text, Does.Contain("空槽"));
            Press(root, "slot-0");
            Assert.That(root.Q<Button>(spentPotion), Is.Null);
        }

        private static void PressMenu(string button) =>
            Press(Object.FindAnyObjectByType<UIDocument>().rootVisualElement, button);

        private static IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BuiltSceneSupportsPreparationPlaybackLootAndReset()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            Assert.That(scene, Is.Not.Null);
            UseShortDelays(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.panel, Is.Not.Null);

            Assert.That(root.Q("party-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "party-position-4", "party-position-3", "party-position-2", "party-position-1" }));
            Assert.That(root.Q("enemy-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "enemy-position-1", "enemy-position-2", "enemy-position-3", "enemy-position-4" }));
            Assert.That(root.Q("party-position-1").Children().Select(element => element.name),
                Is.EqualTo(new[] { "combatant-portrait", "health-bar", "status-row-guard" }));
            Assert.That(root.Q("party-position-1").Q("profession-portrait-art"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-1").Q("profession-frame-warrior"), Is.Null);
            Assert.That(root.Q("enemy-position-2").Q("profession-frame-warrior"), Is.Null);
            Assert.That(root.Q("equipment-position-2").Q("profession-frame-mage"), Is.Not.Null);
            Assert.That(root.Q("equipment-positions").childCount, Is.EqualTo(4));
            Assert.That(root.Q<Label>("status-guard").text, Is.EqualTo("正常"));

            Press(root, "start");
            Assert.That(root.Q<Button>("start"), Is.Null);
            Assert.That(root.Q("enemy-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            yield return WaitForRoundBreak(root);
            var initialLog = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(initialLog.Any(label => label.text.Contains("获得 4 护盾")), Is.True);
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo("5 / 8"));
            yield return ResolveBattle(root);
            var first = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(first.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("银叶射手"));
            Assert.That(root.Q<Label>("reward").text, Does.Contain("狂战士之印"));
            Assert.That(root.Q<Label>("status-brute").text, Is.EqualTo("倒下"));
            Assert.That(root.Q<Label>("status-flanker").text, Is.EqualTo("倒下"));
            AssertNoFormationButtons(root);
            Press(root, "reset");

            Press(root, "open-party-editor");
            yield return DragFormation(root, "edit-member-guard", "editor-bench");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.False);
            Press(root, "open-party-editor");
            yield return DragFormation(root, "edit-member-guard", "edit-position-1");
            yield return DragFormation(root, "edit-member-guard", "edit-position-4");
            Press(root, "back-party-editor");
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
            yield return ResolveBattle(root);
            var second = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(second.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("篝火守夜人"));
            Press(root, "reset");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
            Press(root, "slot-0");
            Assert.That(root.Q<Button>("item-gear-1").text, Does.EndWith("星辉术师"));
            Assert.That(root.Q<Button>("item-gear-177").text, Does.EndWith("背包"));
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo("8 / 8"));
        }

        [UnityTest]
        public IEnumerator PartyEditorOpensProfileAndSharedBackpackOutsideCombat()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseShortDelays(scene);
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
            Assert.That(guardCard.Q("character-health").Q<Label>().text, Does.Contain("10/10"));

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
            Press(root, "open-party-editor");
            yield return DragFormation(root, "edit-member-guard", "editor-bench");
            Assert.That(root.Q("edit-position-1").Q<Button>("edit-member-guard"), Is.Null);
            Press(root, "edit-member-guard");
            Press(root, "close-character-details");
            yield return DragFormation(root, "edit-member-guard", "edit-position-1");
            Press(root, "back-party-editor");

            Press(root, "start");
            Assert.That(root.Q<Button>("open-party-editor"), Is.Null);
            yield return ResolveBattle(root);
            var battleHealth = root.Q("party-position-4").Q("health-bar").Q<Label>().text.Replace(" ", "");
            Press(root, "open-party-editor");
            var archerHealth = root.Q("edit-position-4").Q("character-health").Q<Label>().text;
            Assert.That(archerHealth, Does.Contain(battleHealth));
            Press(root, "edit-member-guard");
            yield return null;
            while (root.Q<Button>("bag-next") is Button next && next.enabledInHierarchy)
                Press(root, "bag-next");
            yield return null;
            var rewardCell = root.Q("edit-item-gear-177")?.parent;
            Assert.That(rewardCell, Is.Not.Null);
            string rewardCellName = rewardCell.name;
            Down(rewardCell);
            yield return WaitForDrag(root);
            Up(root.Q("character-details-layer").ElementAt(0), Vector2.one);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Assert.That(root.Q(rewardCellName).Q("edit-item-gear-177"), Is.Not.Null);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));

            yield return Drag(root, root.Q(rewardCellName), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("狂战士之印"));
            Assert.That(root.Q(rewardCellName).Q("edit-item-gear-1"), Is.Not.Null);
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Label>("reward").text, Does.Contain("狂战士之印"));
        }

        [UnityTest]
        public IEnumerator LongPressSwapIsAvailableOnlyBetweenRoundsAndStartsTheNextRound()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseShortDelays(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            AssertNoFormationButtons(root);
            var guard = root.Q("party-position-1");
            Down(guard);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(root.Q("formation-drag-preview"), Is.Null);
            Up(root.Q("party-positions"), root.Q("party-position-4").worldBound.center);
            Assert.That(root.Q("party-position-1").Q("status-guard"), Is.Not.Null);

            Press(root, "start");
            yield return null;
            Assert.That(root.Q<Button>("next-round"), Is.Null);
            AssertNoFormationButtons(root);
            guard = root.Q("party-position-1");
            Down(guard);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(root.Q("formation-drag-preview"), Is.Null);
            Up(guard, guard.worldBound.center);

            yield return WaitForRoundBreak(root);
            AssertNoFormationButtons(root);
            var party = root.Q("party-positions");
            guard = root.Q("party-position-1");
            var archer = root.Q("party-position-4");

            Down(guard);
            Up(party, archer.worldBound.center);
            Assert.That(root.Q<Button>("next-round"), Is.Not.Null, "A short press should not swap.");

            Down(guard);
            yield return WaitForFormationDrag(root);
            Up(party, root.Q("enemy-position-1").worldBound.center);
            Assert.That(root.Q<Button>("next-round"), Is.Not.Null, "Dropping on an enemy should not advance.");
            Assert.That(root.Q("party-position-1").Q("status-guard"), Is.Not.Null);

            Down(guard);
            yield return WaitForFormationDrag(root);
            Up(party, archer.worldBound.center);
            Assert.That(root.Q<Button>("next-round"), Is.Null, "A successful swap should start the next round.");
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("第 2 回合"));
            Assert.That(root.Q("party-position-4").Q("status-guard"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("status-archer"), Is.Not.Null);

            yield return WaitForRoundBreak(root);
            var log = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(log.Any(line => line.text.Contains("绕后猎手 · 普攻 → 篝火守夜人")), Is.True);
            Press(root, "next-round");
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("第 3 回合"));
        }

        [UnityTest]
        public IEnumerator IntelligenceOfficerRevealsEnemiesAndCelestialFormsUpdateThePortrait()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseShortDelays(scene);
            // Use the real authored characters in a controlled starting formation.
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var encounterField = typeof(BattlePrototype).GetField("encounter", fields);
            var encounter = Object.Instantiate((ScriptableObject)encounterField.GetValue(scene));
            var type = encounter.GetType();
            var recruits = (Object[])type.GetField("additionalRecruits", fields).GetValue(encounter);
            var starters = (object[])type.GetField("party", fields).GetValue(encounter);
            var character = starters[0].GetType().GetField("character", fields);
            character.SetValue(starters[0], recruits.Single(asset => asset.name == "TianGong"));
            character.SetValue(starters[1], recruits.Single(asset => asset.name == "IntelligenceOfficer"));
            scene.enabled = false;
            StartWithFreshPlaySession();
            encounterField.SetValue(scene, encounter);
            scene.enabled = true;

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var enemy = root.Q("enemy-position-1");
            Assert.That(enemy.Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("profession-frame-archer"), Is.Not.Null);
            Press(root, "start");
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("party-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("profession-portrait-art"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Query<Label>().ToList().Any(label => label.text == "地煞"), Is.True);

            Press(root, "next-round");
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("party-position-1").Q("profession-frame-archer"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Query<Label>().ToList().Any(label => label.text == "天弓"), Is.True);
            Object.Destroy(encounter);
        }

        [UnityTest]
        public IEnumerator CharactersActThreeSecondsApartByDefault()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");
            yield return null;
            Assert.That(root.Query<Label>("battle-action").ToList(), Has.Count.EqualTo(1));
            double first = Time.unscaledTimeAsDouble;
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(root.Query<Label>("battle-action").ToList(), Has.Count.EqualTo(1));
            while (root.Query<Label>("battle-action").ToList().Count == 1 && Time.unscaledTimeAsDouble < first + 4d)
                yield return null;

            Assert.That(root.Query<Label>("battle-action").ToList(), Has.Count.EqualTo(2));
            Assert.That(Time.unscaledTimeAsDouble - first, Is.InRange(2.95d, 4d));
        }

        [UnityTest]
        public IEnumerator PartyEditorDragMovesOrSwapsMembersAndTapStillOpensGear()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            yield return null;
            var guard = root.Q("edit-member-guard");
            Down(guard);
            Up(root.Q("editor-roster"), guard.worldBound.center);
            Assert.That(root.Q("character-details"), Is.Not.Null);
            Assert.That(root.Q<Label>("equipment-page").text, Does.Contain("11"));
            Press(root, "close-character-details");

            yield return DragFormation(root, "edit-member-guard", "edit-position-4");
            Assert.That(root.Q("edit-position-4").Q("edit-member-guard"), Is.Not.Null);
            Assert.That(root.Q("edit-position-1").Q("edit-member-archer"), Is.Not.Null);
            Assert.That(root.Q("character-details"), Is.Null);
            yield return DragFormation(root, "edit-member-archer", "editor-bench");
            Assert.That(root.Q("edit-position-1").Q("edit-member-archer"), Is.Null);
            yield return DragFormation(root, "edit-member-guard", "edit-position-1");
            yield return DragFormation(root, "edit-member-archer", "edit-position-4");
            Press(root, "back-party-editor");
            Assert.That(root.Q("party-position-1").Q("status-guard"), Is.Not.Null);
            Assert.That(root.Q("party-position-4").Q("status-archer"), Is.Not.Null);
        }

        private static IEnumerator DragFormation(VisualElement root, string from, string to)
        {
            yield return null;
            Down(root.Q(from));
            yield return WaitForFormationDrag(root);
            var center = root.Q(to).worldBound.center;
            var roster = root.Q("editor-roster");
            using (var move = PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, center)))
                Send(roster, move);
            Up(roster, center);
            Assert.That(root.Q("formation-drag-preview"), Is.Null);
        }

        private static void UseShortDelays(BattlePrototype scene) =>
            typeof(BattlePrototype).GetField("secondsBetweenActions", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(scene, 0.15f);

        private static void AssertNoFormationButtons(VisualElement root)
        {
            Assert.That(root.Q("party-positions").Query<Button>().ToList(), Is.Empty);
            Assert.That(root.Q<Button>("unplace"), Is.Null);
        }

        private static IEnumerator WaitForRoundBreak(VisualElement root)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (root.Q<Button>("next-round") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q<Button>("next-round"), Is.Not.Null, "Playback should stop at the round break.");
            yield return null;
        }

        private static IEnumerator WaitForFormationDrag(VisualElement root)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (root.Q("formation-drag-preview") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q("formation-drag-preview"), Is.Not.Null, "Holding the portrait should begin a drag.");
        }

        private static IEnumerator ResolveBattle(VisualElement root)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (root.Q<Button>("reset") == null && Time.realtimeSinceStartup < deadline)
            {
                if (root.Q<Button>("next-round") != null)
                    Press(root, "next-round");
                yield return null;
            }
            Assert.That(root.Q<Button>("reset"), Is.Not.Null, "Continuing each round should reach settlement.");
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
