using System.Collections;
using System.Collections.Generic;
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
    public sealed class BattlePrototypeTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private bool useAuthoredFixture;

        [SetUp]
        public void StartWithFreshPlaySession()
        {
            useAuthoredFixture = true;
            ResetRun();
            SceneManager.sceneLoaded += ConfigureLineups;
        }

        [TearDown]
        public void RemoveLineupFixture() => SceneManager.sceneLoaded -= ConfigureLineups;

        private static void ResetRun() =>
            typeof(BattlePrototype).GetMethod("ResetPlaySession", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

        private void ConfigureLineups(Scene scene, LoadSceneMode mode)
        {
            if (useAuthoredFixture && scene.name == "BattlePrototype")
                typeof(BattlePrototype).GetField("randomizeLineups", PrivateInstance)
                    .SetValue(Object.FindAnyObjectByType<BattlePrototype>(), false);
        }

        [UnityTest]
        public IEnumerator DefaultDraftsAndManualSelectionsReachTheAnimatedBattle()
        {
            useAuthoredFixture = false;
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var defaultParty = RosterNames(root, "party-positions", "combatant-name");
            Assert.That(defaultParty.Distinct().Count(), Is.EqualTo(4));

            Press(root, "open-settings");
            Press(root, "open-enemy-editor");
            var defaultEnemies = RosterNames(root, "editor-positions", "character-name");
            Assert.That(defaultEnemies.Distinct().Count(), Is.EqualTo(4));
            Press(root, "edit-member-enemy-1");
            var enemyCandidates = root.Q("replacement-candidates").Query<Button>().ToList();
            Assert.That(enemyCandidates.Count, Is.EqualTo(9));
            var enemy = enemyCandidates.First(button => button.enabledInHierarchy);
            string chosenEnemy = enemy.Query<Label>().First().text;
            Press(root, enemy.name);
            Press(root, "back-party-editor");

            Press(root, "open-party-editor");
            var front = root.Q("edit-member-ally-1");
            yield return null;
            using (var right = PointerUpEvent.GetPooled(new Event
                   { type = EventType.MouseUp, button = 1, mousePosition = front.worldBound.center }))
                Send(front, right);
            var ally = root.Q("replacement-candidates").Query<Button>().ToList().First(button => button.enabledInHierarchy);
            string chosenAlly = ally.Query<Label>().First().text;
            Press(root, ally.name);
            Press(root, "back-party-editor");

            Press(root, "start");
            Assert.That(root.Q("party-position-1").Q<Label>("combatant-name").text, Is.EqualTo(chosenAlly));
            Assert.That(root.Q("enemy-position-1").Q<Label>("combatant-name").text, Is.EqualTo(chosenEnemy));
            Assert.That(RosterNames(root, "enemy-positions", "combatant-name").Distinct().Count(), Is.EqualTo(4));
            var enemySprites = root.Q("enemy-positions").Query<Image>("battle-sprite").ToList();
            Assert.That(enemySprites.Count, Is.EqualTo(4));
            Assert.That(enemySprites.Select(image => image.image.name), Is.All.EndsWith("-Combat"));

            var models = enemySprites.Select(image => image.image.name.Replace("-Combat", "-")).ToArray();
            bool enemyPlayed = false;
            float deadline = Time.realtimeSinceStartup + 7f;
            while (!enemyPlayed && Time.realtimeSinceStartup < deadline)
            {
                foreach (var overlay in root.Q("battle-action-layer").Query<Image>().ToList())
                    if (overlay.image != null && models.Any(prefix => overlay.image.name.StartsWith(prefix)))
                        enemyPlayed = true;
                yield return null;
            }
            Assert.That(enemyPlayed, Is.True);
        }

        private static string[] RosterNames(VisualElement root, string row, string label) =>
            root.Q(row).Query<Label>(label).ToList().Select(entry => entry.text).ToArray();

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
            const string spentPotion = "edit-item-gear-45";
            var potionCell = FindBackpackCell(root, spentPotion);
            Assert.That(potionCell, Is.Not.Null);
            yield return null;
            yield return Drag(root, potionCell, root.Q("edit-slot-0"));
            Assert.That(root.Q<Label>("equipped-name-0").text, Is.EqualTo("盛气药水"));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Press(root, "start");
            float entranceDeadline = Time.realtimeSinceStartup + 2f;
            while (root.Q<Label>("battle-phase").text == "入场" && Time.realtimeSinceStartup < entranceDeadline)
                yield return null;
            Assert.That(root.Q("equipment-position-1").Q("equipment-slot-1").Q<Image>(), Is.Null);
            yield return null;
            var equippedShield = root.Q("equipment-position-1").Q("equipment-slot-2");
            var shieldImage = equippedShield.Q<Image>();
            Assert.That(shieldImage.image, Is.Not.Null);
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, equippedShield.worldBound.center)))
                Send(equippedShield, enter);
            var itemTooltip = root.Q("equipment-tooltip");
            Assert.That(itemTooltip.Q<Label>("equipment-tooltip-name").text, Is.EqualTo("精钢壁垒"));
            Assert.That(itemTooltip.Q<Label>("equipment-tooltip-description").text, Does.Contain("防御和特防"));
            Assert.That(itemTooltip.Q("equipment-tooltip-art").Q<Image>().image, Is.SameAs(shieldImage.image));
            using (var leave = PointerLeaveEvent.GetPooled(Mouse(EventType.MouseMove, Vector2.zero)))
                Send(equippedShield, leave);
            Assert.That(root.Q("equipment-tooltip"), Is.Null);
            var vigor = root.Q("status-row-guard").Q("status-badge-Vigor");
            Assert.That(vigor.Q<Label>("status-stack-count").text, Is.EqualTo("3"));
            Assert.That(vigor.Q<Image>().image, Is.Not.Null);
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, vigor.worldBound.center)))
                Send(vigor, enter);
            yield return null;
            var statusTooltip = root.Q("battle-status-tooltip");
            Assert.That(statusTooltip.Q<Label>("battle-status-tooltip-name").text, Is.EqualTo("盛气 ×3"));
            Assert.That(statusTooltip.Q<Label>("battle-status-tooltip-description").text, Does.Contain("伤害增加20%"));
            var largeIcon = statusTooltip.Q("battle-status-tooltip-art").Q<Image>();
            Assert.That(largeIcon.image, Is.SameAs(vigor.Q<Image>().image));
            Assert.That(largeIcon.worldBound.width, Is.GreaterThan(vigor.worldBound.width));
            using (var leave = PointerLeaveEvent.GetPooled(Mouse(EventType.MouseMove, Vector2.zero)))
                Send(vigor, leave);
            Assert.That(root.Q("battle-status-tooltip"), Is.Null);
            var portrait = root.Q("equipment-portrait-1");
            var details = HoverCharacter(root, portrait);
            Assert.That(details.Q<Label>("battle-detail-name").text, Does.Contain("篝火守夜人"));
            Assert.That(details.Q<Label>("battle-detail-health").text, Is.EqualTo("生命  10/10"));
            Assert.That(details.Q<Label>("battle-detail-attack").text, Is.EqualTo("攻击  5"));
            Assert.That(details.Q<Label>("battle-detail-defense").text, Is.EqualTo("防御  4"));
            Assert.That(details.Q<Label>("battle-detail-special-defense").text, Is.EqualTo("特防  4"));
            Assert.That(details.Q<Label>("battle-detail-personality").text, Does.Contain("谨慎"));
            Assert.That(details.Q<Label>("battle-detail-statuses").text, Does.Contain("盛气 ×3"));
            Assert.That(details.Q<Label>("battle-detail-slot-1").text, Is.EqualTo("1 · 空槽"));
            Assert.That(details.Q<Label>("battle-detail-slot-2").text, Is.EqualTo("2 · 精钢壁垒"));
            Assert.That(details.Q<Label>("battle-detail-slot-3").text, Is.EqualTo("3 · 空槽"));
            using (var leave = PointerLeaveEvent.GetPooled(Mouse(EventType.MouseMove, Vector2.zero)))
                Send(portrait, leave);
            Assert.That(root.Q("battle-character-details"), Is.Null);
            Press(root, "open-settings");
            Assert.That(root.Q("run-settings").Q<Button>("return-main-menu"), Is.Not.Null);
            Assert.That(root.Q("prototype").enabledInHierarchy, Is.False);
            int logCount = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList().Count;
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList().Count,
                Is.EqualTo(logCount));
            Press(root, "close-settings");
            Assert.That(root.Q("run-settings"), Is.Null);
            Assert.That(root.Q("prototype").enabledInHierarchy, Is.True);
            Press(root, "open-settings");
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");

            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
            var guardEquipment = root.Q("equipment-position-1");
            Assert.That(guardEquipment.Q("equipment-slot-2").Q<Image>().image, Is.Not.Null);
            Assert.That(guardEquipment.Q("equipment-slot-1").Q<Image>(), Is.Null);
            Assert.That(guardEquipment.Q("equipment-energy").childCount, Is.EqualTo(2));
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            Assert.That(FindBackpackCell(root, spentPotion), Is.Null);
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
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.panel, Is.Not.Null);
            var backdrop = root.Q("battle-background");
            var background = backdrop.resolvedStyle.backgroundImage.texture;
            Assert.That(background, Is.Not.Null);
            Assert.That(root.ElementAt(0), Is.SameAs(backdrop));
            Assert.That(backdrop.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(backdrop.worldBound, Is.EqualTo(root.worldBound));
            Assert.That(root.Q("battlefield").resolvedStyle.backgroundImage.texture, Is.Null);
            var footer = root.Q("battle-footer");
            Assert.That(footer.parent, Is.SameAs(root));
            var rosterBounds = footer.Q("roster-equipment").worldBound;
            var logBounds = footer.Q("battle-log-paper").worldBound;
            Assert.That(root.worldBound.yMax - rosterBounds.yMax, Is.EqualTo(10f).Within(1f));
            Assert.That(logBounds.yMax, Is.EqualTo(rosterBounds.yMax).Within(1f));
            Assert.That(logBounds.xMin - rosterBounds.xMax, Is.EqualTo(32f).Within(1f));
            Assert.That(((Texture2D[])typeof(BattlePrototype).GetField("battleBackgrounds", PrivateInstance)
                .GetValue(scene)).Distinct().Count(), Is.EqualTo(13));

            Assert.That(root.Q("party-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "party-position-4", "party-position-3", "party-position-2", "party-position-1" }));
            Assert.That(root.Q("enemy-positions"), Is.Null);
            Assert.That(root.Q("enemy-position-1"), Is.Null);
            Assert.That(root.Q("battlefield").Query<Label>().ToList().Any(label => label.text == "己方阵列"),
                Is.False);
            Assert.That(root.Q("party-position-1").Children().Select(element => element.name),
                Is.EqualTo(new[] { "combatant-caption", "combatant-portrait", "health-bar", "status-row-guard" }));
            Assert.That(root.Q("party-position-1").Q("profession-portrait-art"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("profession-frame-warrior"), Is.Null);
            Assert.That(root.Q("party-position-1").Q("combatant-portrait").resolvedStyle.borderLeftWidth, Is.Zero);
            Assert.That(root.Q("equipment-position-2").Q("profession-frame-mage"), Is.Not.Null);
            Assert.That(root.Q("equipment-positions").childCount, Is.EqualTo(4));
            var top = root.Q("run-top-bar");
            Assert.That(top.Q("open-party-editor").Q("team-icon"), Is.Not.Null);
            Assert.That(top.Q<Label>("run-gold").text, Is.EqualTo("金币  0"));
            var backpackIcon = top.Q<Image>("run-backpack-icon");
            Assert.That(backpackIcon.image, Is.Not.Null);
            var backpack = top.Q("run-backpack-items");
            Assert.That(backpack.childCount, Is.EqualTo(16));
            Assert.That(top.Q<Button>("start"), Is.Not.Null);
            Assert.That(top.Q("open-settings").Q("settings-icon"), Is.Not.Null);
            Assert.That(root.Q("return-main-menu"), Is.Null);
            Assert.That(root.Q("open-enemy-editor"), Is.Null);
            Assert.That(root.Q<Label>("battle-phase"), Is.Null);
            yield return null;
            Assert.That(backpack.Children().Take(4).Select(tile => tile.name), Is.EqualTo(
                new[] { "run-backpack-item-gear-1", "run-backpack-item-gear-2",
                    "run-backpack-item-gear-3", "run-backpack-item-gear-4" }));
            Assert.That(backpack.Children().Take(4).Select(tile => tile.Q<Image>().image).Distinct().Count(), Is.EqualTo(1));
            Assert.That(backpack.Q("run-item-count"), Is.Null);
            var teamButton = top.Q("open-party-editor");
            Assert.That(top.Q("run-wallet").worldBound.xMax, Is.LessThanOrEqualTo(teamButton.worldBound.xMin));
            Assert.That(teamButton.worldBound.xMax, Is.LessThanOrEqualTo(backpackIcon.worldBound.xMin));
            Assert.That(top.Q("battle-controls").worldBound.xMax,
                Is.LessThanOrEqualTo(top.Q("open-settings").worldBound.xMin));
            Assert.That(root.Q("battle-footer").Q("run-backpack-items"), Is.Null);
            Assert.That(backpack.worldBound.yMax, Is.LessThan(root.Q("equipment-positions").worldBound.yMin));
            Assert.That(backpackIcon.worldBound.xMax, Is.LessThan(backpack.worldBound.xMin));
            var firstBackpackItem = backpack.ElementAt(0);
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, firstBackpackItem.worldBound.center)))
                Send(firstBackpackItem, enter);
            Assert.That(root.Q<Label>("equipment-tooltip-name").text, Is.EqualTo("精钢壁垒"));
            using (var leave = PointerLeaveEvent.GetPooled(Mouse(EventType.MouseMove, Vector2.zero)))
                Send(firstBackpackItem, leave);

            var startingMageEquipment = root.Q("equipment-position-2");
            Assert.That(startingMageEquipment.Q("equipment-stats"), Is.Null);
            Assert.That(startingMageEquipment.Q("equipment-energy").childCount, Is.EqualTo(2));
            var slots = startingMageEquipment.Q("equipment-slots").Children().ToArray();
            Assert.That(slots.Select(slot => slot.name),
                Is.EqualTo(new[] { "equipment-slot-1", "equipment-slot-2", "equipment-slot-3" }));
            Assert.That(slots.Select(slot => slot.worldBound.center.y), Is.Ordered.Ascending);
            var magePortrait = startingMageEquipment.Q("equipment-portrait-2");
            var art = magePortrait.Q<Image>("profession-portrait-art");
            Assert.That(art.scaleMode, Is.EqualTo(ScaleMode.ScaleToFit));
            Assert.That(art.worldBound.width / art.worldBound.height, Is.EqualTo((float)art.image.width / art.image.height).Within(0.03f));
            Assert.That(root.Q("battle-log-paper").resolvedStyle.backgroundImage.texture.name, Is.EqualTo("Event-Parchment"));
            Assert.That(root.Query<Label>().ToList().Any(label => label.text == "讨伐队经理"), Is.False);
            Assert.That(slots[0].worldBound.xMax, Is.LessThanOrEqualTo(magePortrait.worldBound.xMin));
            Assert.That(startingMageEquipment.Q("equipment-energy").worldBound.xMin, Is.GreaterThanOrEqualTo(magePortrait.worldBound.xMax));
            AssertBattleEquipmentIsReadOnly(root);
            var guard = root.Q("party-position-1");
            var caption = guard.Q("combatant-caption");
            Assert.That(caption.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
            Assert.That(caption.resolvedStyle.backgroundColor.a, Is.Zero);
            Assert.That(caption.Q("combatant-subtitle"), Is.Null);
            Assert.That(caption.Q<Label>("combatant-name").text, Is.EqualTo("篝火守夜人"));
            var bar = guard.Q("health-bar");
            var body = guard.Q("combatant-portrait");
            Assert.That(caption.worldBound.yMax, Is.LessThanOrEqualTo(body.worldBound.yMin));
            Assert.That(bar.worldBound.yMin, Is.GreaterThanOrEqualTo(body.worldBound.yMax));
            Assert.That(bar.Q("health-fill").resolvedStyle.backgroundColor.r,
                Is.GreaterThan(bar.Q("health-fill").resolvedStyle.backgroundColor.g));
            Assert.That(guard.Q("status-badges").childCount, Is.Zero);

            Press(root, "start");
            Assert.That(root.Q<Button>("start"), Is.Null);
            Assert.That(root.Q("enemy-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "enemy-position-1", "enemy-position-2", "enemy-position-3", "enemy-position-4" }));
            Assert.That(root.Q("battlefield").Query<Label>().ToList().Any(label => label.text == "敌方阵列"),
                Is.False);
            Assert.That(root.Q("enemy-position-1").Q("combatant-subtitle"), Is.Null);
            Assert.That(root.Q("enemy-position-1").Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-2").childCount, Is.Zero);
            Assert.That(root.Q("enemy-position-4").childCount, Is.Zero);
            yield return null;
            AssertBattleEquipmentIsReadOnly(root);
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("battle-background").resolvedStyle.backgroundImage.texture, Is.SameAs(background));
            AssertBattleEquipmentIsReadOnly(root);
            var initialLog = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(initialLog.Any(label => label.text.Contains("篝火守夜人 · 普攻")), Is.True);
            Assert.That(initialLog.Any(label => label.text.Contains("篝火守夜人 · 举盾")), Is.False);
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo("5 / 8"));
            var archerDetails = HoverCharacter(root, root.Q("equipment-portrait-4"));
            Assert.That(archerDetails.Q<Label>("battle-detail-health").text, Is.EqualTo("生命  5/8"));
            var mageDetails = HoverCharacter(root, root.Q("equipment-portrait-2"));
            Assert.That(mageDetails.Q<Label>("battle-detail-attack").text, Is.EqualTo("攻击  6"));
            yield return ResolveBattle(root);
            Assert.That(root.Q("battle-background").resolvedStyle.backgroundImage.texture, Is.SameAs(background));
            var first = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(first.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("银叶射手"));
            var rewards = root.Q("victory-rewards");
            Assert.That(rewards.Q<Button>("claim-loot-0").Q<Label>().text, Is.EqualTo("狂战士之印"));
            Assert.That(rewards.Q<Button>("claim-gold").Q("reward-gold-icon"), Is.Not.Null);
            foreach (var reward in rewards.Query<Button>().ToList())
            {
                Assert.That(reward.style.backgroundColor.value, Is.EqualTo(Color.clear));
                Assert.That(reward.style.borderLeftWidth.value, Is.Zero);
            }
            Assert.That(root.Q<Button>("open-settings").enabledInHierarchy, Is.False);
            Assert.That(root.Q("return-main-menu"), Is.Null);
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            int startingGold = party.Gold;
            int startingItems = party.Inventory.Count;
            Assert.That(root.Q("status-row-brute").parent.Q<Label>("health-value").text, Does.StartWith("倒下"));
            Assert.That(root.Q("status-row-flanker").parent.Q<Label>("health-value").text, Does.StartWith("倒下"));
            AssertNoFormationButtons(root);
            Press(root, "claim-gold");
            Assert.That(party.Gold - startingGold, Is.InRange(6, 10));
            Assert.That(party.Inventory.Count, Is.EqualTo(startingItems));
            Assert.That(root.Q("claim-gold"), Is.Null);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
            Press(root, "claim-loot-0");
            Assert.That(party.Inventory.Count, Is.EqualTo(startingItems + 1));
            yield return ReenterBattle();
            scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            root = scene.GetComponent<UIDocument>().rootVisualElement;

            Press(root, "open-party-editor");
            yield return DragFormation(root, "edit-member-guard", "editor-bench");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.False);
            Assert.That(root.Q("party-position-1").childCount, Is.Zero);
            Assert.That(root.Q("equipment-position-1").childCount, Is.Zero);
            Press(root, "open-party-editor");
            yield return DragFormation(root, "edit-member-guard", "edit-position-1");
            yield return DragFormation(root, "edit-member-guard", "edit-position-4");
            Press(root, "edit-member-mage");
            yield return null;
            yield return Drag(root, root.Q("bag-cell-0"), root.Q("edit-slot-1"));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            var mageEquipment = root.Q("equipment-position-2");
            Assert.That(mageEquipment.Q("equipment-slot-1").Q<Image>(), Is.Null);
            Assert.That(mageEquipment.Q("equipment-slot-2").Q<Image>().image, Is.Not.Null);
            Assert.That(mageEquipment.Q("equipment-energy").childCount, Is.EqualTo(2));
            var equippedBackpack = root.Q("run-backpack-items");
            Assert.That(equippedBackpack.Q("run-backpack-item-gear-1"), Is.Null);
            Assert.That(equippedBackpack.ElementAt(0).name, Is.EqualTo("run-backpack-item-gear-2"));
            Assert.That(equippedBackpack.childCount, Is.EqualTo(16));

            Press(root, "start");
            yield return ResolveBattle(root);
            var second = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(second.First(label => label.text.Contains("突袭队尾")).text, Does.Contain("篝火守夜人"));
            Press(root, "claim-loot-0");
            Assert.That(root.Q("claim-loot-0"), Is.Null);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
            Press(root, "claim-gold");
            yield return ReenterBattle();
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo("8 / 8"));
            Assert.That(root.Q("equipment-position-2").Q("equipment-slot-2").Q<Image>().image, Is.Not.Null);
            Press(root, "open-party-editor");
            Press(root, "edit-member-mage");
            Assert.That(FindBackpackCell(root, "edit-item-gear-201"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator BothSidesWalkIntoTheirSlotsBeforeTheFirstRound()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            yield return null;
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var seen = new List<string>();
            float deadline = Time.realtimeSinceStartup + 2f;
            while (seen.Count < 4 && Time.realtimeSinceStartup < deadline)
            {
                var layer = root.Q("battle-entrance-layer");
                foreach (string id in new[] { "guard", "mage", "healer", "archer" })
                {
                    if (layer.Q("entrance-" + id) != null && !seen.Contains(id))
                        seen.Add(id);
                }
                yield return null;
            }
            Assert.That(seen, Is.EqualTo(new[] { "guard", "mage", "healer", "archer" }));
            Assert.That(root.Q("enemy-positions"), Is.Null);
            float frontPosition = root.Q("party-position-1").worldBound.center.x;
            Press(root, "open-settings");
            Press(root, "open-enemy-editor");
            Press(root, "edit-member-brute");
            Press(root, "replace-with-EnemyAshLanternAdept");
            Press(root, "edit-member-flanker");
            Press(root, "replace-with-EnemyRearHunter");
            Press(root, "back-party-editor");

            Press(root, "start");
            yield return null;
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("入场"));
            Assert.That(root.Q("enemy-positions"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").worldBound.center.x, Is.EqualTo(frontPosition).Within(1f));
            deadline = Time.realtimeSinceStartup + 2f;
            while (root.Q("battle-entrance-layer").Q("entrance-flanker") == null &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            var entrance = root.Q("battle-entrance-layer");
            Assert.That(entrance.Q("entrance-brute"), Is.Not.Null);
            Assert.That(entrance.Q<Image>("entrance-brute"), Is.Null, "A ranged enemy should use the animated walk pose.");
            Assert.That(entrance.Q<Image>("entrance-flanker").image.name, Is.EqualTo("RearHunter-Walk"));
            Assert.That(root.Q("battle-action"), Is.Null);
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("battle-entrance-layer").childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LearnedSkillSpendsAPurpleEnergyCellAndSettlementRefillsIt()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            Press(root, "backpack-skill-tab");
            yield return null;
            yield return Drag(root, root.Q("book-cell-0"), root.Q("skill-slot-0"));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            yield return null;

            var bar = root.Q("equipment-position-1").Q("equipment-energy");
            Color full = bar.Q("energy-cell-1").resolvedStyle.backgroundColor;
            Assert.That(full.b, Is.GreaterThan(full.g));
            Assert.That(bar.Q("energy-cell-2").resolvedStyle.backgroundColor, Is.EqualTo(full));
            Press(root, "start");
            yield return WaitForRoundBreak(root);
            yield return null;

            bar = root.Q("equipment-position-1").Q("equipment-energy");
            Assert.That(bar.Q("energy-cell-1").resolvedStyle.backgroundColor, Is.EqualTo(full));
            Assert.That(bar.Q("energy-cell-2").resolvedStyle.backgroundColor, Is.EqualTo(Color.white));
            var lines = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(lines.Any(line => line.text.Contains("战争咆哮") && line.text.Contains("消耗 1 点能量")), Is.True);
            Assert.That(lines.Any(line => line.text.Contains("篝火守夜人 · 普攻")), Is.False);
            var details = HoverCharacter(root, root.Q("equipment-portrait-1"));
            Assert.That(details.Q<Label>("battle-detail-skill-1").text, Is.EqualTo("战争咆哮 · 1 能量"));
            Assert.That(details.Q<Label>("battle-detail-skill-effect-1").text, Does.Contain("翻倍"));
            Assert.That(details.Q<Label>("battle-detail-personality").text, Does.Contain("谨慎"));

            yield return ResolveBattle(root);
            yield return null;
            bar = root.Q("equipment-position-1").Q("equipment-energy");
            Assert.That(bar.Children().Select(cell => cell.resolvedStyle.backgroundColor), Is.All.EqualTo(full));
        }

        [UnityTest]
        public IEnumerator PartyEditorOpensProfileAndSharedBackpackOutsideCombat()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Assert.That(root.Q("editor-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "edit-position-4", "edit-position-3", "edit-position-2", "edit-position-1" }));
            Assert.That(root.Q("edit-position-4").Q("profession-frame-archer"), Is.Not.Null);
            Assert.That(root.Q("edit-position-3").Q("profession-frame-support"), Is.Not.Null);
            Assert.That(root.Q("edit-position-2").Q("profession-frame-mage"), Is.Not.Null);
            var guardCard = root.Q("edit-position-1");
            Assert.That(guardCard.Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(guardCard.Q("profession-portrait-art"), Is.Not.Null);
            Assert.That(guardCard.Q<Label>("character-name").text, Is.EqualTo("篝火守夜人"));
            Assert.That(guardCard.Q("character-health").Q<Label>().text, Does.Contain("10/10"));

            Press(root, "edit-member-mage");
            yield return null;
            var characterName = root.Q<Label>("details-character-name");
            Assert.That(characterName.text, Is.EqualTo("星辉术师"));
            Assert.That(root.Q("details-personality").worldBound.yMin, Is.GreaterThanOrEqualTo(characterName.parent.worldBound.yMax));
            Assert.That(root.Q("character-profile"), Is.Not.Null);
            Assert.That(root.Q("character-details").Q("details-health"), Is.Null);
            Assert.That(root.Q("details-attributes").worldBound.yMax, Is.LessThan(root.Q("details-fixed-trait").worldBound.yMin));
            var backpack = root.Q("character-backpack").worldBound;
            Assert.That(root.Q("bag-cell-0").worldBound.xMin - backpack.xMin, Is.LessThan(16f));
            Assert.That(backpack.xMax - root.Q("bag-cell-3").worldBound.xMax, Is.LessThan(16f));
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

            Assert.That(root.Q("equipment-position-1").Q("equipment-slot-2").Q<Image>().image, Is.Not.Null);
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
            Press(root, "claim-loot-0");
            Press(root, "claim-gold");
            yield return ReenterBattle();
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            var rewardCell = FindBackpackCell(root, "edit-item-gear-201");
            Assert.That(rewardCell, Is.Not.Null);
            yield return null;
            string rewardCellName = rewardCell.name;
            Down(rewardCell);
            yield return WaitForDrag(root);
            Up(root.Q("character-details-layer").ElementAt(0), Vector2.one);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Assert.That(root.Q(rewardCellName).Q("edit-item-gear-201"), Is.Not.Null);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));

            yield return Drag(root, root.Q(rewardCellName), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("狂战士之印"));
            Assert.That(root.Q(rewardCellName).Q("edit-item-gear-1"), Is.Not.Null);
            yield return null;
            var emptyCell = root.Q("bag-cell-201");
            Assert.That(emptyCell, Is.Not.Null);
            yield return Drag(root, root.Q("edit-slot-1"), emptyCell);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("装备槽 2"));
            Assert.That(root.Q(emptyCell.name).Q("edit-item-gear-201"), Is.Not.Null);
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator LongPressSwapIsAvailableOnlyBetweenRoundsAndStartsTheNextRound()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            AssertNoFormationButtons(root);
            var guard = root.Q("party-position-1");
            Down(guard);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(root.Q("formation-drag-preview"), Is.Null);
            Up(root.Q("party-positions"), root.Q("party-position-4").worldBound.center);
            Assert.That(root.Q("party-position-1").Q("status-row-guard"), Is.Not.Null);

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
            Assert.That(root.Q("party-position-1").Q("status-row-guard"), Is.Not.Null);

            Down(guard);
            yield return WaitForFormationDrag(root);
            Up(party, archer.worldBound.center);
            Assert.That(root.Q<Button>("next-round"), Is.Null, "A successful swap should start the next round.");
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("第 2 回合"));
            Assert.That(root.Q("party-position-4").Q("status-row-guard"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("status-row-archer"), Is.Not.Null);

            yield return WaitForRoundBreak(root);
            var log = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(log.Any(line => line.text.Contains("绕后猎手 · 普攻 → 篝火守夜人")), Is.True);
            Press(root, "next-round");
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("第 3 回合"));
        }

        [UnityTest]
        public IEnumerator IntelligenceOfficerRevealsEnemiesAndCelestialFormsUpdateTheBattleSprite()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
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
            ResetRun();
            encounterField.SetValue(scene, encounter);
            scene.enabled = true;

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var enemy = root.Q("enemy-position-1");
            Assert.That(enemy.Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite").image.name, Is.EqualTo("TianGong-Combat"));
            Assert.That(root.Q("party-position-2").Q<Image>("battle-sprite").image.name, Is.EqualTo("IntelligenceOfficer-Combat"));
            Press(root, "start");
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite").image.name, Is.EqualTo("DiSha-Combat"));
            Assert.That(root.Q("party-position-1").Query<Label>().ToList().Any(label => label.text == "地煞"), Is.True);

            Press(root, "next-round");
            var meleeSheets = new HashSet<string>();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (root.Q<Button>("next-round") == null && Time.realtimeSinceStartup < deadline)
            {
                var moving = root.Q<Image>("battle-moving-character");
                if (moving?.image != null)
                    meleeSheets.Add(moving.image.name);
                yield return null;
            }
            Assert.That(meleeSheets, Does.Contain("DiSha-Walk"));
            Assert.That(meleeSheets, Does.Contain("DiSha-Attack"));
            yield return WaitForRoundBreak(root);
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite").image.name, Is.EqualTo("TianGong-Combat"));
            Assert.That(root.Q("party-position-1").Query<Label>().ToList().Any(label => label.text == "天弓"), Is.True);
            Object.Destroy(encounter);
        }

        [UnityTest]
        public IEnumerator StarterSpritesLoopIdleAndPlayTheirActionAndHitPoses()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            SetAttackAnimationTimeScale(scene, 0.23f);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var sprites = Enumerable.Range(1, 4)
                .Select(position => root.Q($"party-position-{position}").Q<Image>("battle-sprite")).ToArray();
            Assert.That(sprites.All(sprite => sprite?.image != null), Is.True);
            Assert.That(sprites.Select(sprite => sprite.image.name),
                Is.EqualTo(new[] { "CampGuard-Expanded", "StarMage-Expanded", "DawnHealer-Expanded", "SilverArcher-Expanded" }));
            var initialFrames = sprites.Select(sprite => sprite.sourceRect).ToArray();
            foreach (var pose in initialFrames)
            {
                Assert.That(pose.y, Is.EqualTo(0f).Within(0.01f), $"Expected the idle row at startup, got {pose}.");
                Assert.That(pose.width, Is.EqualTo(224f).Within(0.01f));
                Assert.That(pose.height, Is.EqualTo(224f).Within(0.01f));
            }

            var idleFrames = sprites.Select(_ => new HashSet<int>()).ToArray();
            float deadline = Time.realtimeSinceStartup + 2.2f;
            while (idleFrames.Any(frames => frames.Count < 8) && Time.realtimeSinceStartup < deadline)
            {
                for (int slot = 0; slot < sprites.Length; slot++)
                    idleFrames[slot].Add(FrameColumn(sprites[slot]));
                yield return null;
            }
            Assert.That(idleFrames.Select(frames => frames.Count), Is.All.EqualTo(8));

            Press(root, "start");
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite"), Is.SameAs(sprites[0]));
            Assert.That(root.Q("enemy-position-1").Q<Image>("battle-sprite"), Is.Null);
            var actionFrames = new bool[4];
            bool enemyHasActed = false;
            bool archerRecoiled = false;
            deadline = Time.realtimeSinceStartup + 7f;
            while ((!actionFrames.All(played => played) || !archerRecoiled) && Time.realtimeSinceStartup < deadline)
            {
                enemyHasActed |= root.Query<Label>("battle-action").ToList()
                    .Any(label => label.text.Contains("] 拦路蛮兵 ·") || label.text.Contains("] 绕后猎手 ·"));
                for (int slot = 0; slot < sprites.Length; slot++)
                {
                    int row = FrameRow(sprites[slot]);
                    if (!enemyHasActed)
                        actionFrames[slot] |= sprites[slot].image.name.EndsWith("-Attack") || (slot == 0 && row == 2);
                    else if (slot == 3)
                        archerRecoiled |= row == 2;
                }
                yield return null;
            }
            Assert.That(actionFrames, Is.All.True, "All four roles should use their action poses.");
            Assert.That(archerRecoiled, Is.True, "The enemy's rear attack should animate the archer's hit reaction.");
            yield return WaitForRoundBreak(root);
            yield return new WaitForSecondsRealtime(0.45f);
            Assert.That(sprites.Select(FrameRow), Is.All.EqualTo(0), "Survivors resume idle after action and hit.");
        }

        [UnityTest]
        public IEnumerator RightClickReplacesACharacterAndTheNewCharacterPlaysAllDeathFrames()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);

            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var encounterField = typeof(BattlePrototype).GetField("encounter", fields);
            var encounter = Object.Instantiate((ScriptableObject)encounterField.GetValue(scene));
            var enemies = (object[])encounter.GetType().GetField("enemies", fields).GetValue(encounter);
            var hunterCharacter = enemies[1].GetType().GetField("character", fields);
            var hunter = Object.Instantiate((ScriptableObject)hunterCharacter.GetValue(enemies[1]));
            hunter.GetType().GetField("attack", fields).SetValue(hunter, 50);
            hunter.GetType().GetField("health", fields).SetValue(hunter, 100);
            hunterCharacter.SetValue(enemies[1], hunter);
            scene.enabled = false;
            ResetRun();
            encounterField.SetValue(scene, encounter);
            scene.enabled = true;

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-archer");
            yield return null;
            yield return Drag(root, root.Q("bag-cell-0"), root.Q("edit-slot-0"));
            Press(root, "close-character-details");

            yield return null;
            var outgoing = root.Q("edit-member-archer");
            using (var right = PointerUpEvent.GetPooled(new Event
                   { type = EventType.MouseUp, button = 1, mousePosition = outgoing.worldBound.center }))
                Send(outgoing, right);
            var popup = root.Q("party-replacement");
            Assert.That(popup, Is.Not.Null);
            var candidates = popup.Q<ScrollView>("replacement-candidates");
            Assert.That(candidates.Query<Button>().ToList().Count, Is.EqualTo(35));
            yield return null;
            Assert.That(candidates.verticalScroller.highValue, Is.GreaterThan(0));
            Assert.That(popup.Q<Button>("replace-with-CampGuard").enabledInHierarchy, Is.False);
            Assert.That(popup.Q<Button>("replace-with-SilverArcher").enabledInHierarchy, Is.False);
            Press(root, "replace-with-RelicHunter");
            Assert.That(root.Q("party-replacement"), Is.Null);
            Assert.That(root.Q("edit-position-4").Q<Label>("character-name").text, Is.EqualTo("秘宝猎手"));
            Assert.That(root.Q("edit-position-4").Q("character-health").Q<Label>().text, Does.Contain("11/11"));
            Press(root, "edit-member-archer");
            Assert.That(root.Q("edit-item-gear-1"), Is.Not.Null);
            yield return null;
            var slots = root.Q("character-slots").Children().SelectMany(group => group.Children()).ToArray();
            Assert.That(slots.Select(slot => slot.name),
                Is.EqualTo(new[] { "edit-slot-0", "edit-slot-1", "edit-slot-2", "skill-slot-0", "skill-slot-1" }));
            foreach (var slot in slots)
                Assert.That(slot.worldBound.xMin, Is.EqualTo(slots[0].worldBound.xMin).Within(1f));
            Assert.That(slots[3].worldBound.yMin, Is.GreaterThan(slots[2].worldBound.yMax));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");

            var replacement = root.Q("party-position-4").Q<Image>("battle-sprite");
            Assert.That(replacement.image.name, Is.EqualTo("RelicHunter-Combat"));
            Press(root, "start");
            var deathFrames = new HashSet<int>();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (deathFrames.Count < 8 && Time.realtimeSinceStartup < deadline)
            {
                if (FrameRow(replacement) == 3)
                    deathFrames.Add(FrameColumn(replacement));
                yield return null;
            }
            Assert.That(deathFrames, Is.EquivalentTo(Enumerable.Range(0, 8)));
            Assert.That(root.Q("party-position-4").Q<Label>("health-value").text, Does.StartWith("倒下"));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(FrameRow(replacement), Is.EqualTo(3));
            Assert.That(FrameColumn(replacement), Is.EqualTo(7));
            yield return ResolveBattle(root);
            yield return null;
            var revived = root.Q("party-positions").Children().Single(card =>
                card.Q<Label>("combatant-name")?.text == "秘宝猎手");
            Assert.That(revived.Q<Label>("health-value").text, Is.EqualTo("1 / 11"));
            Assert.That(FrameRow(revived.Q<Image>("battle-sprite")), Is.Zero);
            Object.Destroy(hunter);
            Object.Destroy(encounter);
        }

        [UnityTest]
        public IEnumerator BookmarksSwitchPagesAndOnlyEmptySkillSlotsAcceptBooks()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            yield return null;
            var attributes = root.Q("details-attributes");
            Assert.That(attributes.Children().Select(child => child.name),
                Is.EqualTo(new[] { "details-attack", "details-defense", "details-special-defense" }));
            Assert.That(root.Q("skill-slot-1"), Is.Null);
            Assert.That(root.Q("bag-cell-0"), Is.Not.Null);

            Press(root, "backpack-skill-tab");
            yield return null;
            Assert.That(root.Q("bag-cell-0"), Is.Null);
            var first = root.Q("book-cell-0");
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, first.worldBound.center)))
                Send(first, enter);
            Assert.That(root.Q<Label>("equipment-tooltip-name").text, Is.EqualTo("战争咆哮"));
            yield return Drag(root, first, root.Q("edit-slot-0"));
            Assert.That(first.Q<Image>(), Is.Not.Null);
            yield return Drag(root, first, root.Q("skill-slot-0"));
            var learned = root.Q("skill-slot-0");
            Assert.That(learned.Q<Image>().image.name, Is.EqualTo("01-WarCry"));
            yield return null;
            yield return Drag(root, root.Q("book-cell-0"), learned);
            Assert.That(learned.Q<Image>().image.name, Is.EqualTo("01-WarCry"));
            Down(learned);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Up(learned, learned.worldBound.center);

            Press(root, "backpack-equipment-tab");
            Assert.That(root.Q("bag-cell-0"), Is.Not.Null);
            Assert.That(root.Q("book-cell-0"), Is.Null);
            Press(root, "close-character-details");
            Press(root, "edit-member-guard");
            Assert.That(root.Q("skill-slot-0").Q<Image>().image.name, Is.EqualTo("01-WarCry"));
        }

        private static int FrameRow(Image sprite) => Mathf.RoundToInt(sprite.sourceRect.y / sprite.sourceRect.height);
        private static int FrameColumn(Image sprite) => Mathf.RoundToInt(sprite.sourceRect.x / sprite.sourceRect.width);

        [UnityTest]
        public IEnumerator AttackWindupRepaintsTheRenderedCharacterBeforeTheProjectile()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Rendered-pixel verification requires a graphics device.");

            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            SetAttackAnimationTimeScale(scene, 0.70f);
            var document = scene.GetComponent<UIDocument>();
            var root = document.rootVisualElement;
            var originalTarget = document.panelSettings.targetTexture;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            target.Create();
            try
            {
                document.panelSettings.targetTexture = target;
                yield return null;
                yield return null;
                Press(root, "start");

                Color32[] firstPose = null;
                Color32[] raisedStaff = null;
                float deadline = Time.realtimeSinceStartup + 8f;
                while (raisedStaff == null && Time.realtimeSinceStartup < deadline)
                {
                    var pose = root.Q<Image>("battle-attack-pose");
                    if (pose?.image != null && pose.image.name == "StarMage-Attack" && FrameRow(pose) == 0)
                    {
                        int column = FrameColumn(pose);
                        if ((column == 0 && firstPose == null) || (column == 3 && firstPose != null))
                        {
                            yield return null; // Let the changed atlas region reach the rendered panel.
                            if (pose.panel != null && FrameRow(pose) == 0 && FrameColumn(pose) == column)
                            {
                                Assert.That(root.Q("battle-projectile"), Is.Null);
                                Rect body = root.Q("party-position-2").Q<Image>("battle-sprite").worldBound;
                                var pixels = ReadRenderedRegion(target, root.worldBound, body);
                                if (column == 0)
                                    firstPose = pixels;
                                else
                                    raisedStaff = pixels;
                            }
                        }
                    }
                    yield return null;
                }

                Assert.That(firstPose, Is.Not.Null);
                Assert.That(raisedStaff, Is.Not.Null);
                int changedPixels = firstPose.Zip(raisedStaff, (a, b) =>
                    Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b))
                    .Count(difference => difference > 48);
                Assert.That(changedPixels, Is.GreaterThan(500), "The rendered mage must visibly raise the staff before launch.");
            }
            finally
            {
                document.panelSettings.targetTexture = originalTarget;
                target.Release();
                Object.Destroy(target);
            }
        }

        private static Color32[] ReadRenderedRegion(RenderTexture target, Rect panel, Rect region)
        {
            float scaleX = target.width / panel.width;
            float scaleY = target.height / panel.height;
            int width = Mathf.RoundToInt(region.width * scaleX);
            int height = Mathf.RoundToInt(region.height * scaleY);
            var rect = new Rect(Mathf.RoundToInt((region.xMin - panel.xMin) * scaleX),
                Mathf.RoundToInt((panel.yMax - region.yMax) * scaleY), width, height);
            var previous = RenderTexture.active;
            var sample = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                sample.ReadPixels(rect, 0, 0, false);
                return sample.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(sample);
            }
        }

        [UnityTest]
        public IEnumerator WeaponAttacksTravelToTheirTargetBeforeDamageAndMeleeReturns()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            SetAttackAnimationTimeScale(scene, 0.67f);
            var encounterField = typeof(BattlePrototype).GetField("encounter", fields);
            var encounter = Object.Instantiate((ScriptableObject)encounterField.GetValue(scene));
            var party = (object[])encounter.GetType().GetField("party", fields).GetValue(encounter);
            var characterField = party[0].GetType().GetField("character", fields);
            var guard = Object.Instantiate((ScriptableObject)characterField.GetValue(party[0]));
            guard.GetType().GetField("hasSkill", fields).SetValue(guard, false);
            characterField.SetValue(party[0], guard);
            var enemiesField = encounter.GetType().GetField("enemies", fields);
            var enemies = (object[])enemiesField.GetValue(encounter);
            var brute = Object.Instantiate((ScriptableObject)characterField.GetValue(enemies[0]));
            brute.GetType().GetField("health", fields).SetValue(brute, 120);
            characterField.SetValue(enemies[0], brute);
            var singleEnemy = System.Array.CreateInstance(enemies.GetType().GetElementType(), 1);
            singleEnemy.SetValue(enemies[0], 0);
            enemiesField.SetValue(encounter, singleEnemy);

            scene.enabled = false;
            ResetRun();
            encounterField.SetValue(scene, encounter);
            scene.enabled = true;
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            var guardBody = root.Q("party-position-1").Q<Image>("battle-sprite");
            float home = guardBody.worldBound.center.x;
            var seenProjectiles = new HashSet<string>();
            var visibleWindups = new HashSet<string>();
            var walkOut = new HashSet<int>();
            var walkBack = new HashSet<int>();
            string flying = null;
            int healthBeforeFlight = 0;
            float flightStart = 0;
            float flightEnd = 0;
            double flightStartedAt = 0;
            int? healthBeforeMelee = null;
            float attackPosition = 0;
            bool returned = false;
            Press(root, "start");
            float deadline = Time.realtimeSinceStartup + 9f;
            while ((seenProjectiles.Count < 2 || !returned) && Time.realtimeSinceStartup < deadline)
            {
                string healthText = root.Q("enemy-position-1").Q<Label>("health-value").text;
                int enemyHealth = int.Parse(healthText.Split('/')[0].Trim());
                var projectile = root.Q<Image>("battle-projectile");
                var pose = root.Q<Image>("battle-attack-pose");
                if (projectile == null && pose?.image != null &&
                    (pose.image.name == "StarMage-Attack" || pose.image.name == "SilverArcher-Attack"))
                {
                    int slot = pose.image.name == "StarMage-Attack" ? 2 : 4;
                    var body = root.Q($"party-position-{slot}").Q<Image>("battle-sprite");
                    Assert.That(pose.style.width.value.value, Is.GreaterThan(body.layout.width));
                    visibleWindups.Add(pose.image.name);
                }
                if (projectile != null)
                {
                    flightEnd = projectile.worldBound.center.x;
                    if (flying == null)
                    {
                        flying = projectile.image.name;
                        healthBeforeFlight = enemyHealth;
                        flightStart = flightEnd;
                        flightStartedAt = Time.unscaledTimeAsDouble;
                    }
                    Assert.That(enemyHealth, Is.EqualTo(healthBeforeFlight), "Damage must wait for contact.");
                }
                else if (flying != null)
                {
                    Assert.That(flightEnd, Is.GreaterThan(flightStart + 30f));
                    Assert.That(enemyHealth, Is.LessThan(healthBeforeFlight));
                    double minimumFlight = flying == "Arrow" ? 0.36d : 0.46d;
                    Assert.That(Time.unscaledTimeAsDouble - flightStartedAt, Is.GreaterThan(minimumFlight));
                    seenProjectiles.Add(flying);
                    flying = null;
                }

                var moving = root.Q<Image>("battle-moving-character");
                if (moving != null)
                {
                    healthBeforeMelee ??= enemyHealth;
                    if (moving.image.name == "CampGuardWalk")
                    {
                        int frame = FrameRow(moving) * 3 + FrameColumn(moving);
                        if (moving.style.scale.value.value.x > 0)
                        {
                            walkOut.Add(frame);
                            Assert.That(enemyHealth, Is.EqualTo(healthBeforeMelee.Value));
                        }
                        else
                        {
                            walkBack.Add(frame);
                            Assert.That(enemyHealth, Is.LessThan(healthBeforeMelee.Value));
                        }
                    }
                    else
                    {
                        Assert.That(moving.image.name, Is.EqualTo("CampGuard-Attack"));
                        attackPosition = moving.worldBound.center.x;
                    }
                }
                else if (healthBeforeMelee.HasValue)
                {
                    returned = true;
                    Assert.That(guardBody.visible, Is.True);
                    Assert.That(guardBody.worldBound.center.x, Is.EqualTo(home).Within(2f));
                }
                yield return null;
            }
            Assert.That(seenProjectiles, Is.EquivalentTo(new[] { "Arrow", "MagicOrb" }));
            Assert.That(visibleWindups, Is.EquivalentTo(new[] { "StarMage-Attack", "SilverArcher-Attack" }));
            Assert.That(walkOut, Is.EquivalentTo(Enumerable.Range(0, 6)));
            Assert.That(walkBack, Is.EquivalentTo(Enumerable.Range(0, 6)));
            Assert.That(attackPosition, Is.GreaterThan(home + 28f));
            Assert.That(returned, Is.True);
            Object.Destroy(guard);
            Object.Destroy(brute);
            Object.Destroy(encounter);
        }

        [UnityTest]
        public IEnumerator NextCharacterActsAsSoonAsThePreviousAnimationFinishes()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");
            float entranceDeadline = Time.realtimeSinceStartup + 2f;
            while (root.Q("battle-action") == null && Time.realtimeSinceStartup < entranceDeadline)
                yield return null;
            Assert.That(root.Query<Label>("battle-action").ToList(), Has.Count.EqualTo(1));
            var first = typeof(BattlePrototype).GetField("actionPlayback", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(scene);
            Assert.That(first, Is.Not.Null);
            double finishAt = (double)first.GetType().GetProperty("FinishAt").GetValue(first);
            while (root.Query<Label>("battle-action").ToList().Count == 1 && Time.unscaledTimeAsDouble < finishAt + 1d)
                yield return null;

            Assert.That(root.Query<Label>("battle-action").ToList(), Has.Count.EqualTo(2));
            Assert.That(Time.unscaledTimeAsDouble - finishAt, Is.InRange(0d, 0.18d));
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
            Assert.That(root.Q<Label>("equipment-page").text, Is.EqualTo("1/13"));
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
            Assert.That(root.Q("party-position-1").Q("status-row-guard"), Is.Not.Null);
            Assert.That(root.Q("party-position-4").Q("status-row-archer"), Is.Not.Null);
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

        private static void UseFastAttacks(BattlePrototype scene) => SetAttackAnimationTimeScale(scene, 0.10f);

        private static void SetAttackAnimationTimeScale(BattlePrototype scene, float scale) =>
            typeof(BattlePrototype).GetField("attackAnimationTimeScale", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(scene, scale);

        private static void AssertNoFormationButtons(VisualElement root)
        {
            Assert.That(root.Q("party-positions").Query<Button>().ToList(), Is.Empty);
            Assert.That(root.Q<Button>("unplace"), Is.Null);
        }

        private static void AssertBattleEquipmentIsReadOnly(VisualElement root)
        {
            Assert.That(root.Q("roster-equipment").Query<Button>().ToList(), Is.Empty);
            var portrait = root.Q("equipment-portrait-1");
            var target = root.panel.Pick(portrait.worldBound.center);
            Assert.That(target == portrait || portrait.Contains(target), Is.True);
            using (var down = PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, portrait.worldBound.center)))
                Send(target, down);
            Up(target, portrait.worldBound.center);
            Assert.That(root.Q("quick-equipment"), Is.Null);
            Assert.That(root.Q("character-details"), Is.Null);
        }

        private static VisualElement FindBackpackCell(VisualElement root, string itemName)
        {
            while (true)
            {
                var item = root.Q(itemName);
                if (item != null)
                    return item.parent;
                if (root.Q<Button>("bag-next") is not Button next || !next.enabledInHierarchy)
                    return null;
                Press(root, "bag-next");
            }
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

        private static IEnumerator ReenterBattle()
        {
            yield return WaitForScene("MainMenu");
            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
        }

        private static IEnumerator ResolveBattle(VisualElement root)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (root.Q<Label>("battle-phase").text != "战斗结算" && Time.realtimeSinceStartup < deadline)
            {
                if (root.Q<Button>("next-round") != null)
                    Press(root, "next-round");
                yield return null;
            }
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("战斗结算"));
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

        private static VisualElement HoverCharacter(VisualElement root, VisualElement portrait)
        {
            using var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, portrait.worldBound.center));
            Send(portrait, enter);
            var details = root.Q("battle-character-details");
            Assert.That(details, Is.Not.Null);
            return details;
        }

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
