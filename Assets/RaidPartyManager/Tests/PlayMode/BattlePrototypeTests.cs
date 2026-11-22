using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator ApocalypseShowsTheCopiedCharactersActualTraitOnHover()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            var front = root.Q("edit-member-guard");
            yield return null;
            using (var right = PointerUpEvent.GetPooled(new Event
                { type = EventType.MouseUp, button = 1, mousePosition = front.worldBound.center }))
                Send(front, right);
            var candidate = root.Q("replacement-candidates").Query<Button>().ToList()
                .Single(button => button.Query<Label>().First().text == "天启");
            Press(root, candidate.name);
            Press(root, "back-party-editor");
            Press(root, "start");
            var copies = (Dictionary<string, CharacterDefinition>)typeof(BattlePrototype)
                .GetField("copiedTraits", PrivateInstance).GetValue(scene);
            float deadline = Time.realtimeSinceStartup + 12;
            while (!copies.ContainsKey("guard") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(copies.ContainsKey("guard"), Is.True);
            var details = HoverCharacter(root, root.Q("equipment-portrait-1"));
            Assert.That(details.Q<Label>("battle-detail-trait").text, Does.Contain(copies["guard"].TraitText));
            Assert.That(details.Q<Label>("battle-detail-name").text, Does.Contain("天启"));
        }

        [UnityTest]
        public IEnumerator ChantsFallenGearStaysInItsSlotAndTurnsGrayDuringPlayback()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            var content = (EncounterData)typeof(BattlePrototype).GetField("content", PrivateInstance).GetValue(scene);
            var singer = content.CharacterCatalog.Single(c => c.Id == "Chant");
            var ally = party.Members.First(m => m.Position == 2);
            party.ResetRoster(new[] {
                new PartyMemberStart("singer", singer, 1, 1, new Dictionary<StatusKind, int> { [StatusKind.Poison] = 8 }),
                new PartyMemberStart(ally.Id, ally.Character, 2, ally.Character.BaseStats.Health)
            });
            var cup = party.AddEquipment(content.StartingEquipment.First(item => item.Effect == EquipmentEffect.EchoChalice));
            var singersCup = party.AddEquipment(cup.Definition);
            Assert.That(party.TryEquip("singer", 3, singersCup.Id), Is.True);
            Assert.That(party.TryEquip(ally.Id, 3, cup.Id), Is.True);
            Press(root, "start");
            VisualElement GrayIcon() => root.Q("equipment-position-2")?.Q("equipment-slot-4")?.Q("equipment-icon");
            float deadline = Time.realtimeSinceStartup + 20;
            while (GrayIcon()?.style.filter.value?.Count != 1 && Time.realtimeSinceStartup < deadline) yield return null;
            var icon = GrayIcon();
            Assert.That(icon.style.filter.value?.Single().type, Is.EqualTo(FilterFunctionType.Grayscale));
            Assert.That(icon.Q<Image>().image, Is.SameAs(content.IconFor(cup.Definition.Id)));
            var own = root.Q("equipment-position-1").Q("equipment-slot-4").Q("equipment-icon");
            Assert.That(own.Q<Image>().image, Is.SameAs(content.IconFor(cup.Definition.Id)));
            Assert.That(own.style.filter.value == null || own.style.filter.value.Count == 0, Is.True);
            var details = HoverCharacter(root, root.Q("equipment-portrait-2"));
            Assert.That(details.Q<Label>("battle-detail-slot-4").text, Does.Contain("本场失效"));
        }

        [UnityTest]
        public IEnumerator AConsumedElvenCharmPlaysOnTheProtectedCombatant()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            var front = party.Members.First(m => m.Position == 1);
            party.ResetRoster(new[] { new PartyMemberStart(front.Id, front.Character, 1, 1,
                new Dictionary<StatusKind, int> { [StatusKind.Poison] = 8 }) });
            var charm = party.AddEquipment(new EquipmentDefinition("test-charm", "精灵护符", default,
                effect: EquipmentEffect.ElvenCharm));
            Assert.That(party.TryEquip(front.Id, 0, charm.Id), Is.True);
            Press(root, "start");
            var frames = new HashSet<Rect>();
            float limit = Time.realtimeSinceStartup + 18;
            while (Time.realtimeSinceStartup < limit)
            {
                var fx = root.Q<Image>("battle-elven-charm-effect");
                if (fx == null && frames.Count > 0) break;
                if (fx != null && fx.worldBound.width > 0)
                {
                    var protectedBody = root.Q("party-position-1").Q<Image>("battle-sprite");
                    Assert.That(fx.image.name, Is.EqualTo("ElvenCharm"));
                    Assert.That(fx.worldBound.center.x, Is.EqualTo(protectedBody.worldBound.center.x).Within(2f));
                    frames.Add(fx.sourceRect);
                }
                yield return null;
            }
            Assert.That(frames.Count, Is.GreaterThan(2));
            Assert.That(party.Inventory.Any(e => e.Id == charm.Id), Is.False);
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
            Assert.That(enemyCandidates.Count, Is.EqualTo(23));
            var enemy = enemyCandidates.Single(button => button.Query<Label>().First().text == "沉灯巨魟");
            string chosenEnemy = enemy.Query<Label>().First().text;
            Press(root, enemy.name);
            Assert.That(root.Q("edit-position-1").Q<Label>("position-tag").text, Is.EqualTo("1 号位"));
            Assert.That(root.Q("edit-position-2"), Is.Null);
            Assert.That(root.Q("edit-position-3"), Is.Null);
            Assert.That(root.Q("editor-bench"), Is.Not.Null);
            Press(root, "back-party-editor");

            bool bossEntered = false;
            if (root.Q("enemy-position-1") != null)
            {
                float entranceDeadline = Time.realtimeSinceStartup + 1f;
                while (!bossEntered && Time.realtimeSinceStartup < entranceDeadline)
                {
                    bossEntered = BossEntranceIsVisible(root);
                    yield return null;
                }
            }

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
            Assert.That(RosterNames(root, "enemy-positions", "combatant-name").Distinct().Count(), Is.EqualTo(2));
            Assert.That(root.Q("enemy-position-2"), Is.Null);
            Assert.That(root.Q("enemy-position-3"), Is.Null);
            Assert.That(root.Q("enemy-positions").Q("position-tag"), Is.Null);
            Assert.That(root.Q("party-positions").Q("position-tag"), Is.Null);
            var enemySprites = root.Q("enemy-positions").Query<Image>("battle-sprite").ToList();
            Assert.That(enemySprites.Count, Is.EqualTo(2));
            Assert.That(root.Q("enemy-position-1").Q<Image>("battle-sprite").image.name,
                Is.EqualTo("DrownedLanternRay-Combat"));
            Assert.That(enemySprites.All(image => image.image.name.EndsWith("-Combat")), Is.True);

            bool bossPlayed = false;
            bool bossEffectPlayed = false;
            float deadline = Time.realtimeSinceStartup + 7f;
            while ((!bossPlayed || !bossEffectPlayed) && Time.realtimeSinceStartup < deadline)
            {
                bossEntered |= BossEntranceIsVisible(root);
                bossPlayed |= root.Q("battle-action-layer").Query<Image>().ToList().Any(overlay =>
                    overlay.image != null && overlay.image.name.StartsWith("DrownedLanternRay-"));
                bossEffectPlayed |= root.Q("battle-action-layer").Query<VisualElement>("dock-enemy-effect").ToList()
                    .Any(effect => effect.ClassListContains("dock-fx-RayMist") || effect.ClassListContains("dock-fx-RayTail") ||
                        effect.ClassListContains("dock-fx-RaySurge"));
                yield return null;
            }
            Assert.That(bossEntered, Is.True);
            Assert.That(bossPlayed, Is.True);
            Assert.That(bossEffectPlayed, Is.True);
            Assert.That(root.Q("enemy-position-1").worldBound.width, Is.GreaterThan(root.Q("enemy-position-4").worldBound.width * 2));
            Assert.That(root.Q("enemy-position-1").Q<Image>("battle-sprite").resolvedStyle.height,
                Is.GreaterThan(root.Q("enemy-position-4").Q<Image>("battle-sprite").resolvedStyle.height * 1.2f));
        }

        private static bool BossEntranceIsVisible(VisualElement root) =>
            root.Q<Image>("entrance-enemy-1")?.image?.name == "EnemyDrownedLanternRay";

        private static string[] RosterNames(VisualElement root, string row, string label) =>
            root.Q(row).Query<Label>(label).ToList().Select(entry => entry.text).ToArray();

        [UnityTest]
        public IEnumerator EquipmentPersistsAcrossMainMenuAndLeavingABattle()
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
            var header = root.Q("party-editor-header");
            Assert.That(header.Q("return-main-menu"), Is.Null);
            Assert.That(header.Children().Last().name, Is.EqualTo("back-party-editor"));
            Press(root, "back-party-editor");
            Assert.That(root.Q("party-editor"), Is.Null);
            Press(root, "open-settings");
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");

            Assert.That(Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>()
                .rootVisualElement.Q("open-template"), Is.Null);

            PressMenu("open-battle");
            yield return WaitForScene("BattlePrototype");
            root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-party-editor");
            Press(root, "edit-member-guard");
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));
            Assert.That(root.Q("edit-item-gear-1").parent.Q("equipped-item-border"), Is.Not.Null);
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
            Assert.That(details.Q<Label>("battle-detail-personality").text, Does.Contain("冷幽默"));
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
            if (name == "BattlePrototype")
            {
                var root = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
                while (root.Q("curtain-input-shield") != null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(root.Q("curtain-input-shield"), Is.Null);
            }
        }

        [UnityTest]
        public IEnumerator BuiltSceneSupportsPreparationPlaybackLootAndReset()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return WaitForScene("BattlePrototype");

            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            Assert.That(scene, Is.Not.Null);
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.panel, Is.Not.Null);
            var stage = scene.transform.Find("Paper Theatre");
            Assert.That(Camera.main.orthographic, Is.False);
            Assert.That(root.Q("battle-background"), Is.Null);
            Assert.That(root.resolvedStyle.backgroundColor.a, Is.Zero);
            var backcloth = stage.Find("Cyclorama").GetComponent<MeshRenderer>();
            Assert.That(backcloth.enabled, Is.True);
            Assert.That(backcloth.bounds.max.z, Is.GreaterThan(stage.Find("Left wing").position.z));
            var deckBounds = stage.Find("Stage floor").GetComponent<Renderer>().bounds;
            Assert.That(stage.Find("Left wing").position.z, Is.InRange(deckBounds.min.z, deckBounds.max.z));
            var house = Object.FindAnyObjectByType<DockPlayhouse>();
            var drape = house != null ? house.transform.Find("Curtain L").GetComponent<Renderer>() : stage.Find("Left drape").GetComponent<Renderer>();
            Assert.That(drape.bounds.center.z, Is.LessThan(0));
            Assert.That(Vector3.Dot(stage.Find("Stage floor").up, Vector3.forward), Is.GreaterThan(.99f));
            var footer = root.Q("battle-footer");
            Assert.That(footer.parent, Is.SameAs(root.Q("battle-screen")));
            Assert.That(footer.parent.worldBound, Is.EqualTo(root.worldBound));
            var rosterBounds = footer.Q("roster-equipment").worldBound;
            var logBounds = footer.Q("battle-log-paper").worldBound;
            var clearance = root.Q("audience-clearance").worldBound;
            Assert.That(clearance.height / root.worldBound.height, Is.InRange(.061f, .067f));
            Assert.That(clearance.yMin - rosterBounds.yMax, Is.EqualTo(10f).Within(1f));
            var seats = house != null ? house.transform.Find("Stalls/Front row/Seat L1").GetComponent<MeshRenderer>() :
                stage.Find("Audience/Front stalls").GetComponent<MeshRenderer>();
            float crest = Camera.main.WorldToViewportPoint(seats.bounds.max).y;
            Assert.That(crest, Is.InRange(.035f, .11f));
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
            var available = ((PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene)).BackpackItems;
            Assert.That(backpack.Children().Select(tile => tile.name), Is.EqualTo(
                available.Skip(available.Count - 16).Select(item => "run-backpack-item-" + item.Id)));
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
            Assert.That(root.Q<Label>("equipment-tooltip-name").text, Is.EqualTo(available[available.Count - 16].Definition.Name));
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
            yield return WaitForRound(root, 1);
            var round = (IReadOnlyList<BattleEvent>)typeof(BattlePrototype).GetField("roundEvents", PrivateInstance).GetValue(scene);
            yield return WaitForRound(root, 2);
            yield return null; // Let the rebuilt round UI complete layout before pointer hit-testing.
            Assert.That(scene.transform.Find("Paper Theatre/Cyclorama").GetComponent<MeshRenderer>(), Is.SameAs(backcloth));
            AssertBattleEquipmentIsReadOnly(root);
            var initialLog = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(initialLog.Any(label => label.text.Contains("篝火守夜人 · 普攻")), Is.True);
            Assert.That(initialLog.Any(label => label.text.Contains("篝火守夜人 · 举盾")), Is.False);
            var archer = ((PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene)).Members.Single(m => m.Position == 4);
            int healthAfterRound = round.Where(e => e.TargetId == archer.Id && e.Kind is BattleEventKind.Action or BattleEventKind.Effect)
                .Select(e => e.TargetHealth).DefaultIfEmpty(archer.Health).Last();
            Assert.That(root.Q("party-position-4").Q("health-bar").Q<Label>().text, Is.EqualTo($"{healthAfterRound} / 8"));
            var archerDetails = HoverCharacter(root, root.Q("equipment-portrait-4"));
            Assert.That(archerDetails.Q<Label>("battle-detail-health").text, Is.EqualTo($"生命  {healthAfterRound}/8"));
            var mageDetails = HoverCharacter(root, root.Q("equipment-portrait-2"));
            Assert.That(mageDetails.Q<Label>("battle-detail-attack").text, Is.EqualTo("攻击  6"));
            yield return ResolveBattle(root);
            Assert.That(scene.transform.Find("Paper Theatre/Cyclorama").GetComponent<MeshRenderer>(), Is.SameAs(backcloth));
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
            Assert.That(root.Q<Button>("open-settings").enabledInHierarchy, Is.True);
            Press(root, "open-settings");
            Assert.That(rewards.enabledInHierarchy, Is.False);
            Assert.That(root.Q("run-settings").Q("return-main-menu"), Is.Not.Null);
            Press(root, "close-settings");
            Assert.That(rewards.enabledInHierarchy, Is.True);
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            int startingGold = party.Gold;
            int startingItems = party.Inventory.Count;
            Assert.That(root.Q("status-row-brute").parent.Q<Label>("health-value").text, Does.StartWith("倒下"));
            Assert.That(root.Q("status-row-flanker").parent.Q<Label>("health-value").text, Does.StartWith("倒下"));
            AssertNoFormationButtons(root);
            Press(root, "claim-gold");
            Assert.That(party.Gold - startingGold, Is.InRange(12, 18));
            Assert.That(party.Inventory.Count, Is.EqualTo(startingItems));
            Assert.That(root.Q("claim-gold"), Is.Null);
            Assert.That(root.Q("reward-flight").Q("reward-gold-icon"), Is.Not.Null);
            Assert.That(root.Q<Label>("run-gold").text, Is.EqualTo($"金币  {startingGold}"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
            yield return null;
            Press(root, "claim-loot-0");
            Assert.That(party.Inventory.Count, Is.EqualTo(startingItems + 1));
            Assert.That(root.Q("run-backpack-items").Children().Last().name,
                Is.EqualTo("run-backpack-item-" + party.BackpackItems.Last().Id));
            Assert.That(root.Q("reward-flight-layer").Q("equipment-icon"), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BattlePrototype"));
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
            Assert.That(equippedBackpack.Children().Last().name, Is.EqualTo("run-backpack-item-" + party.BackpackItems.Last().Id));
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
            Assert.That(FindBackpackCell(root, VictoryRewardItemName()), Is.Not.Null);
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
            Assert.That(entrance.Q<Image>("entrance-brute").image.name, Is.EqualTo("AshLanternAdept-Walk"));
            Assert.That(entrance.Q<Image>("entrance-flanker").image.name, Is.EqualTo("RearHunter-Walk"));
            Assert.That(root.Q("battle-action"), Is.Null);
            yield return WaitForRound(root, 2);
            Assert.That(root.Q("battle-entrance-layer").childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RayPhaseDuringAPiercingShotKeepsHealthAndLivingSpritesInTheirSlots()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var type = typeof(BattlePrototype);
            var content = (EncounterData)type.GetField("content", PrivateInstance).GetValue(scene);
            var template = content.CharacterCatalog.Single(c => c.Id == "TianGong");
            var archer = new CharacterDefinition(template.Id, template.Name, template.Profession, template.Personality,
                new Stats(32, 2, 0, 50), 1, template.BasicAttack, reach: template.Reach, trait: template.Trait);
            var ray = content.EnemyRoster.Single(c => c.Id == "EnemyDrownedLanternRay");
            var crab = content.EnemyRoster.Single(c => c.Id == "EnemySaltCrab");
            var party = new PartyRun(new[] { new PartyMemberStart("archer", archer, 1, 50) }, System.Array.Empty<EquipmentDefinition>());
            var enemies = new PartyRun(new[] { new PartyMemberStart("ray", ray, 1, ray.BaseStats.Health),
                new PartyMemberStart("crab", crab, 4, crab.BaseStats.Health) }, System.Array.Empty<EquipmentDefinition>());
            type.GetField("run", PrivateInstance).SetValue(scene, party);
            type.GetField("enemyRun", PrivateInstance).SetValue(scene, enemies);
            type.GetMethod("ResetDemo", PrivateInstance).Invoke(scene, new object[] { false });
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");

            yield return WaitForRound(root, 1);
            var round = (IReadOnlyList<BattleEvent>)type.GetField("roundEvents", PrivateInstance).GetValue(scene);
            yield return WaitForRound(root, 2);
            Assert.That(round.Count(e => e.ActionName == "沉灯翻涌"), Is.EqualTo(1));
            foreach (var slot in new[] { ("party-position-1", "archer"), ("enemy-position-1", "ray"), ("enemy-position-4", "crab") })
            {
                var state = round.Last(e => e.TargetId == slot.Item2 &&
                    e.Kind is BattleEventKind.Action or BattleEventKind.Effect or BattleEventKind.Defeated);
                var card = root.Q(slot.Item1);
                Assert.That(card.Q<Label>("health-value").text, Does.EndWith($"{state.TargetHealth} / {state.MaximumHealth}"));
                if (state.TargetHealth > 0)
                    Assert.That(card.Q<Image>("battle-sprite").visible, Is.True, slot.Item1);
            }
            Press(root, "run-wallet");
            Assert.That(root.Q("expense-ledger"), Is.Not.Null);
            Press(root, "close-ledger");
            Assert.That(root.Q("battle-pause"), Is.Null);
        }

        [UnityTest]
        public IEnumerator TianGongFiresOneArrowWithOrbitingParticlesAndStaggeredImpacts()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            SetAttackAnimationTimeScale(scene, 1.1f);
            var type = typeof(BattlePrototype);
            var content = (EncounterData)type.GetField("content", PrivateInstance).GetValue(scene);
            var archer = content.CharacterCatalog.Single(character => character.Id == "TianGong");
            Assert.That(content.BattleEffectFor(archer.Id, BattleVisualCue.PiercingArrow) == null, Is.True,
                "The piercing projectile does not require a separate impact atlas.");

            // Force a Unity-null texture whose managed wrapper still exists, even in batch mode.
            var missingAtlas = new Texture2D(8, 1);
            Object.Destroy(missingAtlas);
            yield return null;
            Assert.That(missingAtlas == null && !ReferenceEquals(missingAtlas, null), Is.True);
            var extras = (IDictionary<string, (Texture2D Body, Texture2D Effect, Texture2D Finale, Texture2D Strike)>)
                typeof(EncounterData).GetField("battleExtras", PrivateInstance).GetValue(content);
            var sprites = extras[archer.Id];
            extras[archer.Id] = (sprites.Body, missingAtlas, sprites.Finale, sprites.Strike);

            var party = new PartyRun(new[] { new PartyMemberStart("archer", archer, 2, archer.BaseStats.Health) },
                System.Array.Empty<EquipmentDefinition>());
            var foe = content.Enemies[0].Character;
            var practice = new CharacterDefinition(foe.Id, foe.Name, foe.Profession, Personality.Frank,
                new Stats(0, 0, 0, 50), 1, foe.BasicAttack, reach: AttackReach.Melee);
            var enemies = new PartyRun(new[] { 1, 2, 4 }.Select(position =>
                new PartyMemberStart("target-" + position, practice, position, 50)), System.Array.Empty<EquipmentDefinition>());
            type.GetField("run", PrivateInstance).SetValue(scene, party);
            type.GetField("enemyRun", PrivateInstance).SetValue(scene, enemies);
            type.GetMethod("ResetDemo", PrivateInstance).Invoke(scene, new object[] { false });

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (root.Q("battle-piercing-flight") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            var arrow = root.Q<Image>("battle-piercing-arrow");
            Assert.That(arrow, Is.Not.Null);
            Assert.That(arrow.image.name, Is.EqualTo("TianGong-PiercingArrow"));
            Assert.That(root.Query<Image>("battle-piercing-arrow").ToList(), Has.Count.EqualTo(1));
            Assert.That(root.Query<Image>("battle-projectile").ToList(), Is.Empty);
            var particles = root.Query<Image>("battle-arrow-particle").ToList();
            Assert.That(particles.Count, Is.GreaterThan(2));
            Assert.That(particles.Select(particle => particle.image.name), Is.All.EqualTo("TianGong-ArrowParticle"));
            var launchCel = arrow.sourceRect;
            Assert.That(launchCel.width, Is.EqualTo(arrow.image.width / 4f));
            Assert.That(particles[0].sourceRect.height, Is.EqualTo(particles[0].image.height / 4f));
            float startX = arrow.style.left.value.value;
            float particleY = particles[0].style.top.value.value;
            Assert.That(HealthAt(1), Is.EqualTo("50 / 50"));

            while (HealthAt(1) == "50 / 50" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(HealthAt(1), Is.EqualTo($"{50 - archer.BaseStats.Attack} / 50"));
            Assert.That(HealthAt(2), Is.EqualTo("50 / 50"));
            Assert.That(HealthAt(4), Is.EqualTo("50 / 50"));
            Assert.That(root.Q<Image>("battle-piercing-arrow"), Is.SameAs(arrow));
            Assert.That(arrow.style.left.value.value, Is.GreaterThan(startX));
            Assert.That(arrow.sourceRect, Is.Not.EqualTo(launchCel));
            Assert.That(particles[0].style.top.value.value, Is.Not.EqualTo(particleY).Within(0.01f));

            while (HealthAt(4) == "50 / 50" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(HealthAt(2), Is.EqualTo($"{51 - archer.BaseStats.Attack} / 50"));
            Assert.That(HealthAt(4), Is.EqualTo($"{52 - archer.BaseStats.Attack} / 50"));
            Assert.That(root.Q<Image>("battle-piercing-arrow"), Is.SameAs(arrow));

            while (root.Q("battle-piercing-flight") != null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q("battle-piercing-flight"), Is.Null);
            Assert.That(root.Query<Image>("battle-arrow-particle").ToList(), Is.Empty);
            Assert.That(root.Query<Image>("battle-impact-effect").ToList(), Is.Empty);
            LogAssert.NoUnexpectedReceived();

            string HealthAt(int position) => root.Q("enemy-position-" + position).Q<Label>("health-value").text;
        }

        [UnityTest]
        public IEnumerator ArrowBarrageLaunchesOnceAndHitsEveryEnemyTogether()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var (archer, book) = PrepareGroupSkillBattle(scene, "archer", "skill-book-07");
            UseFastAttacks(scene);
            Press(root, "start");

            yield return WaitForRound(root, 2);
            Assert.That(root.Q<ScrollView>("battle-log").Query<Label>().ToList()
                .Any(line => line.text.Contains("万箭齐发") && line.text.Contains("消耗")), Is.True);

            SetAttackAnimationTimeScale(scene, 0.55f);


            float deadline = Time.realtimeSinceStartup + 6f;
            while (root.Query<Image>("battle-projectile", "skill-volley-projectile").ToList().Count != 3 &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            var arrows = root.Query<Image>("battle-projectile", "skill-volley-projectile").ToList();
            Assert.That(arrows, Has.Count.EqualTo(3));
            Assert.That(arrows.Select(arrow => arrow.image.name), Is.All.EqualTo("07-VolleyProjectile"));
            while (root.Query<Image>("battle-skill-effect", book.Id).ToList().Count < 3 &&
                Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(root.Query<Image>("battle-projectile", "skill-volley-projectile").ToList(), Is.Empty);
            Assert.That(root.Query<Image>("battle-skill-effect", book.Id).ToList(), Has.Count.EqualTo(3));
            for (int position = 1; position <= 3; position++)
                Assert.That(root.Q("enemy-position-" + position).Q<Label>("health-value").text,
                    Is.EqualTo($"{50 - archer.BaseStats.Attack} / 50"));
        }

        [UnityTest]
        public IEnumerator StarfallDropsOneMeteorFromAboveAndBreaksItOnImpact()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var (caster, book) = PrepareGroupSkillBattle(scene, "mage", "skill-book-08");
            UseFastAttacks(scene);
            Press(root, "start");

            yield return WaitForRound(root, 2);

            SetAttackAnimationTimeScale(scene, 0.9f);


            float deadline = Time.realtimeSinceStartup + 6f;
            while (root.Q<Image>("battle-meteor") == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            var meteor = root.Q<Image>("battle-meteor");
            Assert.That(meteor, Is.Not.Null);
            Assert.That(meteor.image.name, Is.EqualTo("08-MeteorBoulder"));
            Assert.That(root.Query<Image>("battle-meteor").ToList(), Has.Count.EqualTo(1));
            var fallingCel = meteor.sourceRect;
            Assert.That(fallingCel.width, Is.EqualTo(meteor.image.width / 4f));
            float top = meteor.style.top.value.value;
            float left = meteor.style.left.value.value;
            float center = left + meteor.style.width.value.value * 0.5f;
            var layer = root.Q("battle-meteor-layer");
            var middle = root.Q("enemy-position-2");
            var body = (VisualElement)middle.Q<Image>("battle-sprite") ?? middle.Q("combatant-portrait");
            Assert.That(center, Is.EqualTo(layer.WorldToLocal(body.worldBound).center.x).Within(2f));
            Assert.That(top, Is.LessThan(layer.WorldToLocal(body.worldBound).yMin));
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(meteor.panel, Is.Not.Null);
            Assert.That(meteor.style.left.value.value, Is.EqualTo(left).Within(1f));
            Assert.That(meteor.style.top.value.value, Is.GreaterThan(top));
            Assert.That(meteor.sourceRect, Is.Not.EqualTo(fallingCel));
            Assert.That(root.Query<Image>("battle-meteor-fragment").ToList(), Is.Empty);
            for (int position = 1; position <= 3; position++)
                Assert.That(root.Q("enemy-position-" + position).Q<Label>("health-value").text, Is.EqualTo("50 / 50"));

            while (root.Q<Image>("battle-meteor-fragment") == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(root.Q("battle-meteor"), Is.Null);
            var fragments = root.Query<Image>("battle-meteor-fragment").ToList();
            Assert.That(fragments, Has.Count.EqualTo(4));
            Assert.That(fragments.Select(piece => piece.image.name), Is.All.EqualTo("08-MeteorFragments"));
            Assert.That(fragments.Select(piece => piece.sourceRect).Distinct().Count(), Is.EqualTo(4));
            Assert.That(root.Query<Image>("battle-skill-effect", book.Id).ToList(), Has.Count.EqualTo(3));
            for (int position = 1; position <= 3; position++)
                Assert.That(root.Q("enemy-position-" + position).Q<Label>("health-value").text,
                    Is.EqualTo($"{50 - caster.BaseStats.Attack} / 50"));
            var brokenCel = fragments[0].sourceRect;
            Assert.That(brokenCel.width, Is.EqualTo(fragments[0].image.width / 8f));
            float fragmentX = fragments[0].style.left.value.value;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(fragments[0].style.left.value.value, Is.LessThan(fragmentX));
            Assert.That(fragments[0].sourceRect, Is.Not.EqualTo(brokenCel));
            yield return WaitForRound(root, 3);
            Assert.That(root.Q("battle-meteor-effect"), Is.Null);
        }

        private static (CharacterDefinition Caster, SkillBookDefinition Book) PrepareGroupSkillBattle(
            BattlePrototype scene, string memberId, string bookId)
        {
            var type = typeof(BattlePrototype);
            var content = (EncounterData)type.GetField("content", PrivateInstance).GetValue(scene);
            var original = content.Party.Single(unit => unit.Id == memberId).Character;
            var caster = new CharacterDefinition(original.Id, original.Name, original.Profession, original.Personality,
                original.BaseStats, original.ActionInterval, original.BasicAttack, reach: original.Reach);
            var book = content.SkillBooks.Single(skill => skill.Id == bookId);
            var party = new PartyRun(new[] { new PartyMemberStart(memberId, caster, 4, caster.BaseStats.Health) },
                new EquipmentDefinition[] { book });
            Assert.That(party.TryLearnSkill(memberId, 0, party.SkillBooks[0].Id), Is.True);

            var foe = content.EnemyRoster.First(character => content.BattleSpriteFor(character.Id) != null);
            var practice = new CharacterDefinition(foe.Id, foe.Name, foe.Profession, Personality.Frank,
                new Stats(0, 0, 0, 50), 1, new ActionDefinition("practice", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0),
                reach: AttackReach.Melee);
            var enemies = new PartyRun(Enumerable.Range(1, 3).Select(position =>
                new PartyMemberStart("target-" + position, practice, position, 50)), System.Array.Empty<EquipmentDefinition>());
            type.GetField("run", PrivateInstance).SetValue(scene, party);
            type.GetField("enemyRun", PrivateInstance).SetValue(scene, enemies);
            type.GetMethod("ResetDemo", PrivateInstance).Invoke(scene, new object[] { false });
            return (caster, book);
        }

        [UnityTest]
        public IEnumerator RoundOpeningEffectPlaysWithTheFirstAttackAndCountdownFollowsEachRound()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            SetAttackAnimationTimeScale(scene, 0.28f);
            var type = typeof(BattlePrototype);
            var content = (EncounterData)type.GetField("content", PrivateInstance).GetValue(scene);
            var chanter = content.CharacterCatalog.Single(character => character.Id == "CalamityChanter");
            var totem = content.StartingEquipment.First(item => item.Effect == EquipmentEffect.InsightTotem);
            var party = new PartyRun(new[] { new PartyMemberStart("chanter", chanter, 1, chanter.BaseStats.Health) },
                new[] { totem });
            Assert.That(party.TryEquip("chanter", 0, party.Inventory.Single().Id), Is.True);

            var original = content.Enemies.First().Character;
            var target = new CharacterDefinition(original.Id, original.Name, Profession.Warrior, Personality.Worrier,
                new Stats(1, 0, 0, 150), 1, original.BasicAttack, reach: AttackReach.Melee);
            var enemies = new PartyRun(new[] { new PartyMemberStart("target", target, 1, 150) }, System.Array.Empty<EquipmentDefinition>());
            type.GetField("run", PrivateInstance).SetValue(scene, party);
            type.GetField("enemyRun", PrivateInstance).SetValue(scene, enemies);
            type.GetMethod("ResetDemo", PrivateInstance).Invoke(scene, new object[] { false });

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");

            var activeAction = type.GetField("actionPlayback", PrivateInstance);
            float deadline = Time.realtimeSinceStartup + 6;
            while (root.Q("round-start-effect") == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(root.Q<Label>("round-start-effect-label").text, Does.Contain("洞察图腾"));
            Assert.That(activeAction.GetValue(scene), Is.Not.Null);
            Assert.That(root.Q("status-row-target").Q("status-badge-Exposed"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-1").Q<Label>("health-value").text, Is.EqualTo("150 / 150"));
            Assert.That(ChantBadge().Q<Label>("status-stack-count").text, Is.EqualTo("3"));

            for (int round = 2; round <= 4; round++)
            {
                yield return WaitForRound(root, round);
                Assert.That(ChantBadge().Q<Label>("status-stack-count").text, Is.EqualTo((3 - (round - 1) % 3).ToString()));
            }

            var badge = ChantBadge();
            yield return null;
            using (var hover = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, badge.worldBound.center)))
                Send(badge, hover);
            Assert.That(root.Q<Label>("battle-status-tooltip-name").text, Does.Contain("剩余 3 回合"));

            VisualElement ChantBadge() => root.Q("status-badge-countdown-CalamityChant");
        }

        [UnityTest]
        public IEnumerator RoundEndAnimationsShowActualHealthAndEmbersBeforeContinuing()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            SetAttackAnimationTimeScale(scene, 0.28f);
            var type = typeof(BattlePrototype);
            var content = (EncounterData)type.GetField("content", PrivateInstance).GetValue(scene);
            var original = content.Party.Single(unit => unit.Id == "guard").Character;
            var attack = new ActionDefinition("mixed", "混合伤害", ActionKind.Attack, TargetRule.FrontEnemy, 0,
                damageType: DamageType.Physical, additionalDamage: new[] { new DamageComponent(DamageType.Magic),
                    new DamageComponent(DamageType.Fire), new DamageComponent(DamageType.Lightning) });
            var guard = new CharacterDefinition(original.Id, original.Name, original.Profession, Personality.Worrier,
                new Stats(2, 1, 1, 24), 1, attack, reach: original.Reach);
            var effects = new[] { EquipmentEffect.FirstFire, EquipmentEffect.LifePendant, EquipmentEffect.RitualMask };
            var gear = effects.Select(effect => content.StartingEquipment.First(item => item.Effect == effect)).ToArray();
            var party = new PartyRun(new[] { new PartyMemberStart("guard", guard, 1, 10,
                new Dictionary<StatusKind, int> { { StatusKind.Poison, 2 } }) }, gear);
            for (int index = 0; index < effects.Length; index++)
                Assert.That(party.TryEquip("guard", index, party.Inventory.Single(item => item.Definition.Effect == effects[index]).Id),
                    Is.True);
            var foe = content.EnemyRoster.First();
            var target = new CharacterDefinition(foe.Id, foe.Name, foe.Profession, Personality.Worrier, new Stats(1, 0, 0, 50),
                1, foe.BasicAttack, reach: foe.Reach);
            var enemies = new PartyRun(new[] { new PartyMemberStart("practice", target, 1, 50) },
                System.Array.Empty<EquipmentDefinition>());
            type.GetField("run", PrivateInstance).SetValue(scene, party);
            type.GetField("enemyRun", PrivateInstance).SetValue(scene, enemies);
            type.GetMethod("ResetDemo", PrivateInstance).Invoke(scene, new object[] { false });

            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");

            string PoisonLayers() => root.Q("status-badge-Poison")?.Q<Label>("status-stack-count").text;
            Assert.That(PoisonLayers(), Is.EqualTo("2"));
            var seen = new HashSet<Label>();
            var triggers = new List<string>();
            bool decayShownDuringEffect = false;
            var colors = new Dictionary<string, Color>();
            var kinds = new[] { "physical", "magic", "fire", "lightning", "healing", "poison" };
            Label firstDamage = null;
            float firstHeight = 0;
            float firstSeen = 0;
            float firstGone = 0;
            bool rose = false;
            float deadline = Time.realtimeSinceStartup + 7f;
            while (root.Q<Label>("battle-phase")?.text != "第 2 回合" && Time.realtimeSinceStartup < deadline)
            {
                foreach (var caption in root.Query<Label>("round-end-effect-label").ToList())
                    if (seen.Add(caption))
                        triggers.Add(caption.text);
                decayShownDuringEffect |= root.Q("round-end-effect") != null && PoisonLayers() == "1";
                foreach (var number in root.Query<Label>("combat-number").ToList())
                    foreach (var kind in kinds.Where(kind => number.ClassListContains("combat-number-" + kind)))
                        colors[kind] = number.style.color.value;

                if (firstDamage == null && root.Q<Label>(className: "combat-number-fire") is { } fire &&
                    fire.worldBound.height > 0 && !float.IsNaN(fire.worldBound.y))
                {
                    firstDamage = fire;
                    firstHeight = fire.worldBound.y;
                    firstSeen = Time.realtimeSinceStartup;
                    Assert.That(fire.text, Is.EqualTo("−2"));
                }
                if (firstDamage?.panel != null)
                    rose |= firstDamage.worldBound.y < firstHeight - 8f;
                else if (firstDamage != null && firstGone == 0)
                    firstGone = Time.realtimeSinceStartup;
                yield return null;
            }

            Assert.That(colors.Keys, Is.EquivalentTo(kinds));
            Assert.That(colors.Values.Distinct().Count(), Is.EqualTo(6));
            Assert.That(rose, Is.True);
            Assert.That(firstDamage.panel, Is.Null);
            Assert.That(firstGone - firstSeen, Is.InRange(0.37f, 0.67f));
            Assert.That(decayShownDuringEffect, Is.True);
            Assert.That(triggers, Is.EqualTo(new[]
            {
                "初始之火  余烬 ×1", "生命吊坠  +3 生命", "中毒  −2 生命",
                "初始之火  余烬 ×2", "生命吊坠  +3 生命", "中毒  −2 生命"
            }));
            Assert.That(root.Q("battle-pause"), Is.Null);
            Assert.That(root.Q("round-end-effect"), Is.Null);
            Assert.That(root.Q("party-position-1").Q<Label>("health-value").text, Is.EqualTo("11 / 24"));
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
            SetAttackAnimationTimeScale(scene, 0.30f);
            Press(root, "start");

            Image warCry = null;
            float effectDeadline = Time.realtimeSinceStartup + 12f;
            while (warCry == null && Time.realtimeSinceStartup < effectDeadline)
            {
                yield return null;
                warCry = root.Q<Image>("battle-skill-effect", "skill-book-01");
            }
            Assert.That(warCry, Is.Not.Null, "War Cry should display its own effect on the battlefield.");
            Assert.That(warCry.sourceRect.width, Is.EqualTo(96f));
            float firstFrame = warCry.sourceRect.x;
            while (warCry.panel != null && warCry.sourceRect.x == firstFrame &&
                Time.realtimeSinceStartup < effectDeadline)
                yield return null;
            Assert.That(warCry.sourceRect.x, Is.GreaterThan(firstFrame));
            UseFastAttacks(scene);
            yield return WaitForRound(root, 2);
            yield return null;
            Assert.That(root.Q("battle-skill-effect"), Is.Null);

            bar = root.Q("equipment-position-1").Q("equipment-energy");
            Assert.That(bar.Q("energy-cell-1").resolvedStyle.backgroundColor, Is.EqualTo(full));
            Assert.That(bar.Q("energy-cell-2").resolvedStyle.backgroundColor, Is.EqualTo(Color.white));
            var lines = root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList();
            Assert.That(lines.Any(line => line.text.Contains("战争咆哮") && line.text.Contains("消耗 1 点能量")), Is.True);
            var firstRound = lines.TakeWhile(line => line.text != "第 2 回合");
            Assert.That(firstRound.Any(line => line.text.Contains("篝火守夜人 · 普攻")), Is.False);
            var details = HoverCharacter(root, root.Q("equipment-portrait-1"));
            Assert.That(details.Q<Label>("battle-detail-skill-1").text, Is.EqualTo("战争咆哮 · 1 能量"));
            Assert.That(details.Q<Label>("battle-detail-skill-effect-1").text, Does.Contain("翻倍"));
            Assert.That(details.Q<Label>("battle-detail-personality").text, Does.Contain("冷幽默"));

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
            var topBar = root.Q("run-top-bar");
            Press(root, "open-party-editor");
            yield return null;
            Assert.That(root.Q("run-top-bar"), Is.SameAs(topBar));
            Assert.That(topBar.enabledInHierarchy, Is.True);
            Assert.That(topBar.worldBound.yMax, Is.EqualTo(root.Q("party-editor").worldBound.yMin).Within(1f));
            Assert.That(root.Q("battle-controls").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.False);
            Assert.That(root.Q("editor-positions").Children().Select(card => card.name),
                Is.EqualTo(new[] { "edit-position-4", "edit-position-3", "edit-position-2", "edit-position-1" }));
            Assert.That(root.Q("edit-position-4").Q("profession-frame-archer"), Is.Not.Null);
            Assert.That(root.Q("edit-position-3").Q("profession-frame-support"), Is.Not.Null);
            Assert.That(root.Q("edit-position-2").Q("profession-frame-mage"), Is.Not.Null);
            var guardCard = root.Q("edit-position-1");
            Assert.That(guardCard.Q("profession-frame-warrior"), Is.Not.Null);
            Assert.That(guardCard.Q("profession-portrait-art"), Is.Not.Null);
            foreach (var portrait in root.Query<VisualElement>("character-portrait").ToList())
            {
                var artwork = portrait.Q<Image>("profession-portrait-art");
                float ratio = artwork == null ? 2f / 3f : (float)artwork.image.width / artwork.image.height;
                Assert.That(portrait.worldBound.width, Is.EqualTo(portrait.worldBound.height * ratio).Within(2f));
                if (artwork != null)
                {
                    Assert.That(artwork.scaleMode, Is.EqualTo(ScaleMode.ScaleToFit));
                    Assert.That(artwork.worldBound.width, Is.EqualTo(artwork.worldBound.height * ratio).Within(2f));
                }
            }
            Assert.That(guardCard.Q<Label>("character-name").text, Is.EqualTo("篝火守夜人"));
            Assert.That(guardCard.Q("character-health").Q<Label>().text, Does.Contain("10/10"));

            Press(root, "edit-member-mage");
            yield return null;
            var characterName = root.Q<Label>("details-character-name");
            Assert.That(characterName.text, Is.EqualTo("星辉术师"));
            var loadoutTab = root.Q<Button>("open-character-loadout").worldBound;
            var dossierTab = root.Q<Button>("open-employee-dossier").worldBound;
            Assert.That(loadoutTab.center.y, Is.EqualTo(characterName.worldBound.center.y).Within(2));
            Assert.That(dossierTab.center.y, Is.EqualTo(loadoutTab.center.y).Within(2));
            Assert.That(loadoutTab.xMin, Is.GreaterThanOrEqualTo(characterName.worldBound.xMax));
            Assert.That(dossierTab.xMin, Is.GreaterThanOrEqualTo(loadoutTab.xMax));
            Press(root, "open-employee-dossier");
            yield return null;
            Assert.That(root.Q("employee-dossier").Children().Count(), Is.GreaterThan(0));
            Assert.That(root.Q<Label>("employee-personality").text, Does.Contain("戏精"));
            var chart = root.Q("employee-attribute-radar");
            Assert.That(chart, Is.Not.Null);
            var captions = chart.Children().ToArray();
            Assert.That(captions.Select(caption => caption.Q<Label>().text), Is.EqualTo(new[]
                { "忠诚程度", "战术服从", "薪酬偏好", "上场需求", "满意程度", "吃苦耐劳" }));
            Assert.That(captions.Select(caption => int.Parse(caption.Children().OfType<Label>().Last().text)), Is.All.InRange(1, 5));
            Assert.That(captions.All(caption => chart.worldBound.Contains(caption.worldBound.center)), Is.True);
            foreach (var stat in new[] { "damage", "damage-taken", "healing", "kills" })
                Assert.That(root.Q<Label>("employee-stat-" + stat).text, Is.EqualTo("0"));
            Assert.That(root.Q<Label>("employee-stat-tenure").text, Is.EqualTo("0 天"));
            Assert.That(root.Q("employee-thoughts").Query<Label>().ToList().Count, Is.GreaterThan(1));
            Assert.That(root.Q("employee-goals").Query<Label>().ToList().Any(label => label.text.Contains("上场 2 次")), Is.True);
            Press(root, "open-character-loadout");
            yield return null;
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
            Assert.That(root.Q("edit-item-gear-1").parent.Q("equipped-item-border"), Is.Not.Null);
            Assert.That(topBar.Q("run-backpack-item-gear-1"), Is.Null);
            yield return null;
            var wornCell = root.Q("bag-cell-0");
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, wornCell.worldBound.center)))
                Send(wornCell, enter);
            string wearer = root.Q<Label>("details-character-name").text;
            Assert.That(root.Q<Label>("equipped-item-wearer").text, Does.Contain($"{wearer} · 装备槽 1"));
            yield return Drag(root, root.Q("edit-slot-0"), wornCell);
            Assert.That(root.Q<Label>("equipped-name-0").text, Does.Contain("装备槽"));
            Assert.That(root.Q("bag-cell-0").Q("edit-item-gear-1"), Is.Not.Null);
            Assert.That(root.Q("bag-cell-0").Q("equipped-item-border"), Is.Null);
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
            string rewardName = VictoryRewardItemName();
            var rewardCell = FindBackpackCell(root, rewardName);
            Assert.That(rewardCell, Is.Not.Null);
            yield return null;
            string rewardCellName = rewardCell.name;
            Down(rewardCell);
            yield return WaitForDrag(root);
            Up(root.Q("character-details-layer").ElementAt(0), Vector2.one);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Assert.That(root.Q(rewardCellName).Q(rewardName), Is.Not.Null);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("精钢壁垒"));

            yield return Drag(root, root.Q(rewardCellName), root.Q("edit-slot-1"));
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("狂战士之印"));
            Assert.That(root.Q(rewardCellName).Q(rewardName), Is.Not.Null);
            Assert.That(root.Q(rewardCellName).Q("equipped-item-border"), Is.Not.Null);
            yield return null;
            var emptyCell = root.Q("bag-cell-" + (int.Parse(rewardCellName.Substring("bag-cell-".Length)) + 1));
            Assert.That(emptyCell, Is.Not.Null);
            yield return Drag(root, root.Q("edit-slot-1"), emptyCell);
            Assert.That(root.Q<Label>("equipped-name-1").text, Is.EqualTo("装备槽 2"));
            Assert.That(root.Q(emptyCell.name).Q(rewardName), Is.Not.Null);
            Assert.That(root.Q(emptyCell.name).Q("equipped-item-border"), Is.Null);
            Assert.That(root.Query(rewardName).ToList().Count, Is.EqualTo(1));
            Press(root, "close-character-details");
            Press(root, "back-party-editor");
            Assert.That(root.Q<Button>("start").enabledInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator BattleAdvancesAtTheSelectedSpeedEvenWhileSettingsAreOpen()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            UseFastAttacks(scene);
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");
            Assert.That(root.Q("battle-pause"), Is.Null);
            Assert.That(root.Q<Button>("battle-speed").text, Is.EqualTo("倍速 1×"));
            yield return WaitForRound(root, 1);
            var clock = typeof(BattlePrototype).GetProperty("PlaybackNow", PrivateInstance);
            double Now() => (double)clock.GetValue(scene);
            double before = Now();
            Press(root, "battle-speed");
            Assert.That(Now(), Is.EqualTo(before).Within(.01), "Switching speed must not skip ahead.");
            var guard = root.Q("party-position-1");
            var party = root.Q("party-positions");
            Down(guard);
            double start = Now(), real = Time.unscaledTimeAsDouble;
            yield return new WaitForSecondsRealtime(.36f);
            Assert.That((Now() - start) / (Time.unscaledTimeAsDouble - real), Is.EqualTo(2).Within(.05));
            Assert.That(root.Q("formation-drag-preview"), Is.Null);
            Up(party, root.Q("party-position-4").worldBound.center);
            Press(root, "open-settings");
            yield return WaitForRound(root, 2);
            Assert.That(root.Q("run-settings"), Is.Not.Null);
            Assert.That(root.Q("party-position-1").Q("status-row-guard"), Is.Not.Null);
            Press(root, "close-settings");
            Assert.That(root.Q<Button>("battle-speed").text, Is.EqualTo("倍速 2×"));
            Press(root, "battle-speed");
            start = Now(); real = Time.unscaledTimeAsDouble;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That((Now() - start) / (Time.unscaledTimeAsDouble - real), Is.EqualTo(1).Within(.05));
            yield return ResolveBattle(root);
            Assert.That(root.Q("battle-speed"), Is.Null);
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

            yield return WaitForRound(root, 2);
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite").image.name, Does.StartWith("DiSha-"));
            Assert.That(root.Q("party-position-1").Query<Label>().ToList().Any(label => label.text == "地煞"), Is.True);


            var meleeSheets = new HashSet<string>();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (root.Q<Label>("battle-phase")?.text != "第 3 回合" && Time.realtimeSinceStartup < deadline)
            {
                var moving = root.Q<Image>("battle-moving-character");
                if (moving?.image != null)
                    meleeSheets.Add(moving.image.name);
                yield return null;
            }
            Assert.That(meleeSheets, Does.Contain("DiSha-Walk"));
            Assert.That(meleeSheets, Does.Contain("DiSha-Attack"));
            yield return WaitForRound(root, 3);
            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite").image.name, Does.StartWith("TianGong-"));
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

            // The combat playback clock waits for the opening curtain before advancing the idle cycle.
            float openBy = Time.realtimeSinceStartup + 4;
            while (root.Q("curtain-input-shield") != null && Time.realtimeSinceStartup < openBy) yield return null;
            var idleFrames = sprites.Select(_ => new HashSet<int>()).ToArray();
            float deadline = Time.realtimeSinceStartup + 2.2f;
            while (idleFrames.Any(frames => frames.Count < 16) && Time.realtimeSinceStartup < deadline)
            {
                for (int slot = 0; slot < sprites.Length; slot++)
                    idleFrames[slot].Add(FrameColumn(sprites[slot]));
                yield return null;
            }
            Assert.That(idleFrames.Select(frames => frames.Count), Is.All.EqualTo(16));

            Press(root, "start");

            Assert.That(root.Q("party-position-1").Q<Image>("battle-sprite"), Is.SameAs(sprites[0]));
            Assert.That(root.Q("enemy-position-1").Q<Image>("battle-sprite"), Is.Null);
            var actionFrames = new bool[4];
            var playbackField = typeof(BattlePrototype).GetField("actionPlayback", PrivateInstance);
            var actorSpriteField = playbackField.FieldType.GetField("sprite", PrivateInstance);
            bool archerRecoiled = false;
            deadline = Time.realtimeSinceStartup + 7f;
            while ((!actionFrames.All(played => played) || !archerRecoiled) && Time.realtimeSinceStartup < deadline)
            {
                var playback = playbackField.GetValue(scene);
                var activeFigure = playback == null ? null :
                    (actorSpriteField.GetValue(playback) as VisualElement)?.Q<Image>("battle-sprite");
                for (int slot = 0; slot < sprites.Length; slot++)
                {
                    int row = FrameRow(sprites[slot]);
                    if (ReferenceEquals(sprites[slot], activeFigure))
                        actionFrames[slot] |= sprites[slot].image.name.EndsWith("-Attack") || (slot == 0 && row == 2);
                    else if (slot == 3)
                        archerRecoiled |= row == 2;
                }
                yield return null;
            }
            Assert.That(actionFrames, Is.All.True, "All four roles should use their action poses.");
            Assert.That(archerRecoiled, Is.True, "The enemy's rear attack should animate the archer's hit reaction.");
            yield return WaitForRound(root, 2);
            deadline = Time.realtimeSinceStartup + 2f;
            while (sprites.Any(sprite => FrameRow(sprite) != 0) && Time.realtimeSinceStartup < deadline)
                yield return null;
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
            // Leave a survivor so this exercise can reach the one-HP revival at settlement.
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", fields).GetValue(scene);
            var trainingGear = party.AddEquipment(new EquipmentDefinition("training-guard", "训练护甲", new Stats(20, 0, 0, 300)));
            Assert.That(party.TryEquip("guard", 0, trainingGear.Id), Is.True);
            party.Rest(1);
            Press(root, "start");
            var deathFrames = new HashSet<int>();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (deathFrames.Count < 16 && Time.realtimeSinceStartup < deadline)
            {
                if (FrameRow(replacement) == 3)
                    deathFrames.Add(FrameColumn(replacement));
                yield return null;
            }
            Assert.That(deathFrames, Is.EquivalentTo(Enumerable.Range(0, 16)));
            Assert.That(root.Q("party-position-4").Q<Label>("health-value").text, Does.StartWith("倒下"));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(FrameRow(replacement), Is.EqualTo(3));
            Assert.That(FrameColumn(replacement), Is.EqualTo(15));
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
        public IEnumerator BookmarksSwitchPagesAndCompatibleBooksCanReplaceLearnedSkills()
        {
            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return WaitForScene("BattlePrototype");
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
            var scroll = first.Q<Image>();
            var symbol = scroll.Q<Image>("skill-scroll-symbol");
            Assert.That(scroll.image.name, Is.EqualTo("SkillBookScroll"));
            Assert.That(symbol.image.name, Is.EqualTo("01-WarCry"));
            Assert.That(symbol.worldBound.width, Is.LessThan(scroll.worldBound.width));
            using (var enter = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, first.worldBound.center)))
                Send(first, enter);
            Assert.That(root.Q<Label>("equipment-tooltip-name").text, Is.EqualTo("战争咆哮"));
            var bookTooltip = root.Q("equipment-tooltip");
            Assert.That(bookTooltip.Q<Label>("skill-book-requirements").text, Does.Contain("消耗"));
            Assert.That(bookTooltip.Query<Label>().ToList().Any(label => label.text.Contains("拆卸")), Is.False);
            var tooltipArtwork = bookTooltip.Q("equipment-tooltip-art").Q<Image>();
            Assert.That(tooltipArtwork.image, Is.SameAs(scroll.image));
            Assert.That(tooltipArtwork.Q<Image>("skill-scroll-symbol").image, Is.SameAs(symbol.image));
            yield return Drag(root, first, root.Q("edit-slot-0"));
            Assert.That(first.Q<Image>(), Is.Not.Null);
            yield return Drag(root, first, root.Q("skill-slot-0"));
            var learned = root.Q("skill-slot-0");
            Assert.That(learned.Q<Image>().image.name, Is.EqualTo("01-WarCry"));
            yield return null;
            yield return Drag(root, root.Q("book-cell-0"), learned); // Another War Cry is still rejected.
            Assert.That(root.Q("skill-slot-0").Q<Image>().image.name, Is.EqualTo("01-WarCry"));
            var content = (EncounterData)typeof(BattlePrototype).GetField("content", PrivateInstance).GetValue(scene);
            var party = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            var replacement = content.SkillBooks.Single(book => book.SkillEffect == SkillEffect.StoneGuard);
            var replacementItem = party.SkillBooks.First(book => book.Definition.Id == replacement.Id);
            var booksBefore = party.SkillBooks.Select(book => book.Id).ToArray();
            var replacementCell = root.Query<VisualElement>().ToList()
                .FirstOrDefault(view => view.name?.StartsWith("book-cell-") == true && view.Q($"edit-book-{replacementItem.Id}") != null);
            while (replacementCell == null)
            {
                var next = root.Q<Button>("bag-next");
                Assert.That(next?.enabledInHierarchy, Is.True, "The replacement book must be on a backpack page.");
                Press(root, "bag-next");
                yield return null;
                replacementCell = root.Query<VisualElement>().ToList().FirstOrDefault(view =>
                    view.name?.StartsWith("book-cell-") == true && view.Q($"edit-book-{replacementItem.Id}") != null);
            }
            yield return Drag(root, replacementCell, root.Q("skill-slot-0"));
            Assert.That(root.Q<Label>("skill-name-0").text, Is.EqualTo(replacement.Name));
            Assert.That(party.SkillBooks.Select(book => book.Id), Is.EquivalentTo(booksBefore.Where(id => id != replacementItem.Id)));
            learned = root.Q("skill-slot-0");
            Down(learned);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(root.Q("equipment-drag-preview"), Is.Null);
            Up(learned, learned.worldBound.center);

            Press(root, "backpack-equipment-tab");
            Assert.That(root.Q("bag-cell-0"), Is.Not.Null);
            Assert.That(root.Q("book-cell-0"), Is.Null);
            Press(root, "close-character-details");
            var rose = party.Inventory.First(item => item.Definition.Effect == EquipmentEffect.ScarletRose);
            Assert.That(party.TryEquip("guard", 0, rose.Id), Is.True);
            Press(root, "edit-member-guard");
            Assert.That(root.Q<Label>("details-defense").text, Is.EqualTo("防御 0 (-3)"));
            Assert.That(root.Q<Label>("details-special-defense").text, Is.EqualTo("特防 0 (-3)"));
            Assert.That(root.Q<Label>("skill-name-0").text, Is.EqualTo(replacement.Name));
        }

        private static int FrameRow(Image sprite) => Mathf.RoundToInt(sprite.sourceRect.y / sprite.sourceRect.height);
        private static int FrameColumn(Image sprite) => Mathf.RoundToInt(sprite.sourceRect.x / sprite.sourceRect.width);

        private static IEnumerator LoadDockBattle()
        {
            void ChooseDock(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.name != "BattlePrototype") return;
                var battle = Object.FindAnyObjectByType<BattlePrototype>();
                var field = typeof(BattlePrototype).GetField("battleBackgrounds", PrivateInstance);
                var choices = (Texture2D[])field.GetValue(battle);
                field.SetValue(battle, choices.Where(texture => texture.name.StartsWith("01-")).ToArray());
            }
            SceneManager.sceneLoaded += ChooseDock;
            try { yield return SceneManager.LoadSceneAsync("BattlePrototype"); }
            finally { SceneManager.sceneLoaded -= ChooseDock; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DockFloorMeetsTheApronAndRestsOnItsJoistsAtEveryFraming()
        {
            yield return LoadDockBattle();
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var house = Object.FindAnyObjectByType<DockPlayhouse>().transform;
            var port = house.Find("Half-round stage/Port gallery").GetComponent<MeshFilter>().sharedMesh.vertices;
            var starboard = house.Find("Half-round stage/Starboard gallery").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(port.Length, Is.EqualTo(starboard.Length));
            float mirrorError = port.Zip(starboard, (p, q) => (p - new Vector3(-q.x, q.y, q.z)).magnitude).Max();
            Assert.That(mirrorError, Is.LessThan(.0001f));
            var loft = house.Find("Half-round stage/Fly loft").GetComponent<MeshFilter>().sharedMesh;
            var crown = loft.vertices.Where(v => Mathf.Abs(v.y - loft.bounds.max.y) < .001f).ToArray();
            Assert.That(crown.Max(v => v.z) - crown.Min(v => v.z), Is.GreaterThan(11.9f),
                "The ceiling must stay level from the front of the house to the rear.");
            var aft = house.Find("Half-round stage/Backcloth opening");
            Assert.That((aft.localPosition.z - 7.165f) / aft.localScale.x, Is.EqualTo(.5f).Within(.001f));
            var apron = house.Find("Apron").GetComponent<MeshFilter>();
            var timber = house.Find("Half-round stage/Deck and joists").GetComponent<Renderer>();
            var floor = scene.transform.Find("Paper Theatre/Stage floor").GetComponent<Renderer>();
            var stage = typeof(BattlePrototype).GetField("stage", PrivateInstance).GetValue(scene);
            var frame = stage.GetType().GetMethod("Update");
            var camera = Camera.main;
            float initialAspect = camera.aspect;
            // Only back-edge plank vertices are shared with the playing floor. Lamps are higher, further downstage.
            float rearZ = apron.sharedMesh.bounds.max.z;
            Vector3 joint = apron.sharedMesh.vertices.Where(v => Mathf.Abs(v.z - rearZ) < .001f)
                .OrderByDescending(v => v.y).First();
            try
            {
                foreach (float aspect in new[] { 16f / 9, 4f / 3, 21f / 9 })
                foreach (float baseline in new[] { .24f, .30f, .40f })
                {
                    camera.aspect = aspect;
                    frame.Invoke(stage, new object[] { Time.unscaledTimeAsDouble, baseline, .8f, .1f });
                    var edge = apron.transform.TransformPoint(joint);
                    Bounds ceiling = house.Find("Half-round stage/Fly loft").GetComponent<Renderer>().bounds;
                    Bounds deckBounds = timber.bounds;
                    foreach (var masking in new[] { "Portal and traveller rail", "Curtain L", "Curtain R" })
                        Assert.That(house.Find(masking).GetComponent<Renderer>().bounds.max.z,
                            Is.LessThan(deckBounds.min.z - .05f), masking + " must stay downstage of the rotating deck.");
                    float sweptRadius = new Vector2(deckBounds.extents.x, deckBounds.extents.y).magnitude;
                    Assert.That(ceiling.min.y - (deckBounds.center.y + sweptRadius), Is.GreaterThan(1),
                        "The loft must clear the entire semicircle, including the joists, during its half-turn.");
                    var bottomRear = new Vector3(ceiling.center.x, ceiling.min.y, ceiling.max.z);
                    Assert.That(camera.WorldToViewportPoint(bottomRear).y, Is.GreaterThan(1.02f),
                        "Even the farthest underside of the ceiling must be above the audience's view.");
                    Assert.That(timber.bounds.size.x, Is.EqualTo(2 * timber.bounds.size.z).Within(.002f));
                    var cloth = scene.transform.Find("Paper Theatre/Cyclorama").GetComponent<MeshFilter>();
                    var pattern = cloth.sharedMesh;
                    Assert.That(pattern.subMeshCount, Is.EqualTo(1));
                    var rim = pattern.vertices.Where(v => Mathf.Abs(v.y) < .001f).ToArray();
                    Assert.That(rim.Min(v => v.x), Is.EqualTo(-1).Within(.001f));
                    Assert.That(rim.Max(v => v.x), Is.EqualTo(1).Within(.001f));
                    Assert.That(rim.All(v => v.z >= -.001f && Mathf.Abs(v.x*v.x + v.z*v.z - 1) < .001f), Is.True);
                    var uv = pattern.uv.Where(t => t.y < .001f).Select(t => t.x).ToArray();
                    Assert.That(uv[0], Is.EqualTo(0));
                    Assert.That(uv[uv.Length-1], Is.EqualTo(1));
                    Assert.That(uv.Zip(uv.Skip(1), (a, b) => b > a).All(increases => increases), Is.True);
                    Assert.That(cloth.GetComponent<Renderer>().bounds.max.z, Is.EqualTo(timber.bounds.max.z).Within(.002f));
                    Assert.That(timber.bounds.min.z, Is.EqualTo(edge.z).Within(.0002f));
                    Assert.That(timber.bounds.max.y, Is.EqualTo(edge.y).Within(.0002f));
                    Assert.That(floor.bounds.min.z, Is.EqualTo(edge.z).Within(.0002f));
                    Assert.That(floor.bounds.center.y - timber.bounds.max.y, Is.EqualTo(.001f).Within(.0002f));
                    Assert.That(floor.bounds.center.y, Is.EqualTo(.001f).Within(.0002f));
                    Assert.That(floor.bounds.max.z, Is.EqualTo(timber.bounds.max.z).Within(.0002f));
                    Assert.That(floor.bounds.min.x, Is.EqualTo(timber.bounds.min.x).Within(.0002f));
                    Assert.That(floor.bounds.max.x, Is.EqualTo(timber.bounds.max.x).Within(.0002f));
                }
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    AssertPlanksReachTheJoint(camera, floor);
            }
            finally { camera.aspect = initialAspect; }
        }

        private static void AssertPlanksReachTheJoint(Camera camera, Renderer floor)
        {
            var target = RenderTexture.GetTemporary(672, 288, 24);
            var originalTarget = camera.targetTexture;
            var clear = camera.backgroundColor;
            int mask = camera.cullingMask;
            int layer = floor.gameObject.layer;
            try
            {
                floor.gameObject.layer = 30;
                camera.cullingMask = 1 << 30;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                // The old view-space clip discarded the front planks at this framing.
                var point = new Vector3(floor.bounds.center.x, floor.bounds.center.y, floor.bounds.min.z + .12f);
                var view = camera.WorldToViewportPoint(point);
                var region = new Rect(view.x - .004f, 1 - view.y - .004f, .008f, .008f);
                var pixels = ReadRenderedRegion(target, new Rect(0, 0, 1, 1), region);
                Assert.That(pixels.All(pixel => pixel.r < 245 || pixel.b < 245), Is.True);
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.backgroundColor = clear;
                camera.cullingMask = mask;
                floor.gameObject.layer = layer;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        [UnityTest]
        public IEnumerator BothPortalPostsMaskTheMovingCast()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Stage occlusion requires a graphics device.");
            yield return LoadDockBattle();
            var stage = Object.FindAnyObjectByType<BattlePrototype>().transform.Find("Paper Theatre");
            var house = Object.FindAnyObjectByType<DockPlayhouse>().transform;
            var portal = house.Find("Portal and traveller rail");
            var camera = Camera.main;
            var target = RenderTexture.GetTemporary(960, 540, 24);
            var previous = camera.targetTexture;
            int oldMask = camera.cullingMask, oldLayer = portal.gameObject.layer;
            // Bright ink at entrance sorting order, crossing both jambs. The window must still show the ink.
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var actor = card.GetComponent<MeshRenderer>();
            actor.sharedMaterial = stage.Find("Right wing").GetComponent<Renderer>().sharedMaterial;
            actor.sortingOrder = 5;
            var ink = new MaterialPropertyBlock();
            ink.SetColor("_Color", Color.magenta);
            ink.SetTexture("_MainTex", Texture2D.whiteTexture);
            actor.SetPropertyBlock(ink);
            try
            {
                card.layer = portal.gameObject.layer = 30;
                camera.cullingMask = 1 << 30;
                camera.targetTexture = target;
                card.transform.position = house.TransformPoint(new Vector3(0, -.24f, 7.53f));
                card.transform.localScale = new Vector3(24, 3, 1);
                camera.Render();
                foreach (int side in new[] { -1, 1 })
                {
                    bool InkAt(float x)
                    {
                        var uv = camera.WorldToViewportPoint(house.TransformPoint(new Vector3(side * x, -.24f, 7.81f)));
                        var region = new Rect(uv.x - .002f, 1 - uv.y - .004f, .004f, .008f);
                        return ReadRenderedRegion(target, new Rect(0, 0, 1, 1), region)
                            .All(p => p.r > 150 && p.b > 150 && p.g < 50);
                    }
                    Assert.That(InkAt(3.70f), Is.True, "Incoming ink should be visible inside the portal.");
                    Assert.That(InkAt(4.12f), Is.False, "The front post must occlude an incoming actor.");
                }
            }
            finally
            {
                camera.targetTexture = previous;
                camera.cullingMask = oldMask;
                portal.gameObject.layer = oldLayer;
                Object.Destroy(card);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        [UnityTest]
        public IEnumerator DockMoonHangsFromTheLoftAndIlluminatesThePlayingBoards()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Moonlight verification requires a graphics device.");

            yield return LoadDockBattle();
            var stage = Object.FindAnyObjectByType<BattlePrototype>().transform.Find("Paper Theatre");
            var sky = stage.Find("Cyclorama");
            var floor = stage.Find("Stage floor");
            var house = Object.FindAnyObjectByType<DockPlayhouse>().transform;
            var hook = house.Find("Half-round stage/Fly loft/Moon shackle");
            var lantern = hook.Find("Flown moon lantern");
            Assert.That(lantern.gameObject.activeInHierarchy, Is.True);
            var lift = lantern.Find("Hemp lift line");
            Assert.That(Vector3.Distance(lift.TransformPoint(Vector3.up), hook.position), Is.LessThan(.001f));
            Assert.That(hook.position.z, Is.LessThan(house.Find("Half-round stage/Backcloth opening").position.z - 2f));
            var lens = lantern.Find("Illuminated silk").gameObject;
            var lamp = lantern.GetComponentInChildren<Light>();
            Assert.That(lamp.type, Is.EqualTo(LightType.Spot));
            int lensLayer = lens.layer;
            var cargoShadow = stage.Find("Left wing/Moon shadow").gameObject;
            int shadowLayer = cargoShadow.layer;
            var material = sky.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.IsKeywordEnabled("RPM_DOCK_MOON"), Is.True);

            var camera = Camera.main;
            var originalTarget = camera.targetTexture;
            int originalMask = camera.cullingMask;
            int skyLayer = sky.gameObject.layer, floorLayer = floor.gameObject.layer;
            var target = new RenderTexture(960, 540, 24);
            target.Create();
            try
            {
                sky.gameObject.layer = floor.gameObject.layer = lens.layer = 30;
                camera.cullingMask = 1 << 30;
                camera.targetTexture = target;
                yield return null;
                yield return null;
                Vector3 moon = material.GetVector("_MoonWorld");
                Vector3 landing = material.GetVector("_MoonLanding");
                var paint = new MaterialPropertyBlock();
                sky.GetComponent<Renderer>().GetPropertyBlock(paint);
                var crop = paint.GetVector("_MainTex_ST");
                Assert.That(crop.x, Is.EqualTo(1), "A single uninterrupted image must span both wings.");
                Assert.That(crop.z, Is.EqualTo(0));
                Assert.That(paint.GetTexture("_MainTex").name, Is.EqualTo("Harbour"));
                Assert.That(Vector3.Distance(moon, lantern.position), Is.LessThan(.001f));
                Assert.That(moon.z, Is.LessThan(sky.GetComponent<Renderer>().bounds.max.z - 2));
                Assert.That(moon.y, Is.LessThan(hook.position.y));
                Assert.That(landing.z, Is.EqualTo(0).Within(1f));

                float BrightnessAround(Vector3 point)
                {
                    Vector3 v = camera.WorldToViewportPoint(point);
                    var region = new Rect(v.x - .008f, 1 - v.y - .008f, .016f, .016f);
                    Assert.That(new Rect(0, 0, 1, 1).Contains(region.min), Is.True);
                    var pixels = ReadRenderedRegion(target, new Rect(0, 0, 1, 1), region);
                    return (float)pixels.Average(c => (c.r + c.g + c.b) / 3f);
                }
                camera.Render();
                float litDisc = BrightnessAround(moon), litDeck = BrightnessAround(landing);
                var boardArea = new Rect(.02f, .52f, .96f, .46f);
                var screen = new Rect(0, 0, 1, 1);
                var bareBoards = ReadRenderedRegion(target, screen, boardArea);
                cargoShadow.layer = 30;
                camera.Render();
                var withShadow = ReadRenderedRegion(target, screen, boardArea);
                int shadedPixels = bareBoards.Zip(withShadow, (a, b) => a.r - b.r).Count(drop => drop > 4);
                Assert.That(shadedPixels, Is.GreaterThan(25), "Cargo must cast a visible shadow on the dock.");
                cargoShadow.layer = shadowLayer;
                camera.Render();
                var boards = floor.GetComponent<Renderer>().bounds;
                var apron = new Vector3(0, boards.center.y, boards.min.z + .3f);
                var upstage = new Vector3(0, boards.center.y, boards.max.z - 1.0f);
                float clearApron = BrightnessAround(apron), mistyDistance = BrightnessAround(upstage);
                var mist = material.GetVector("_StageMist");
                var clearAir = mist;
                clearAir.w = 0;
                material.SetVector("_StageMist", clearAir);
                camera.Render();
                Assert.That(BrightnessAround(apron), Is.EqualTo(clearApron).Within(1));
                Assert.That(Mathf.Abs(BrightnessAround(upstage) - mistyDistance), Is.GreaterThan(2));
                material.SetVector("_StageMist", mist);
                material.DisableKeyword("RPM_DOCK_MOON");
                lens.layer = lensLayer;
                camera.Render();
                Assert.That(litDisc, Is.GreaterThan(BrightnessAround(moon) + 8));
                Assert.That(litDeck, Is.GreaterThan(BrightnessAround(landing) + 8));
            }
            finally
            {
                material.EnableKeyword("RPM_DOCK_MOON");
                lens.layer = lensLayer;
                sky.gameObject.layer = skyLayer;
                floor.gameObject.layer = floorLayer;
                cargoShadow.layer = shadowLayer;
                camera.cullingMask = originalMask;
                camera.targetTexture = originalTarget;
                target.Release();
                Object.Destroy(target);
            }
        }

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
            var camera = Camera.main;
            var originalTarget = camera.targetTexture;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            target.Create();
            try
            {
                camera.targetTexture = target;
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
                            yield return null; // Let the pose reach the stage renderer.
                            if (pose.panel != null && FrameRow(pose) == 0 && FrameColumn(pose) == column)
                            {
                                Assert.That(root.Q("battle-projectile"), Is.Null);
                                Rect body = root.Q("party-position-2").Q<Image>("battle-sprite").worldBound;
                                var paper = scene.transform.Find("Paper Theatre").GetComponentsInChildren<MeshRenderer>()
                                    .Single(view => view.enabled && view.name == "battle-attack-pose");
                                // Isolate the actor so moving scenery cannot count towards the pixel difference.
                                int layer = paper.gameObject.layer;
                                int mask = camera.cullingMask;
                                try
                                {
                                    paper.gameObject.layer = 30;
                                    camera.cullingMask = 1 << 30;
                                    camera.Render();
                                }
                                finally
                                {
                                    camera.cullingMask = mask;
                                    paper.gameObject.layer = layer;
                                }
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
                camera.targetTexture = originalTarget;
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
                        int frame = FrameRow(moving) * 4 + FrameColumn(moving);
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
                else if (healthBeforeMelee.HasValue && !returned)
                {
                    returned = true;
                    Assert.That(guardBody.visible, Is.True);
                    Assert.That(guardBody.worldBound.center.x, Is.EqualTo(home).Within(3.1f), "The returning figure may play its three-pixel hit recoil.");
                }
                yield return null;
            }
            Assert.That(seenProjectiles, Is.EquivalentTo(new[] { "Arrow", "MagicOrb" }));
            Assert.That(visibleWindups, Is.EquivalentTo(new[] { "StarMage-Attack", "SilverArcher-Attack" }));
            Assert.That(walkOut, Is.EquivalentTo(Enumerable.Range(0, 16)));
            Assert.That(walkBack, Is.EquivalentTo(Enumerable.Range(0, 16)));
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
            var roster = root.Q("editor-roster");
            var target = root.Q(to);
            // The editor can still be scrolling the previously focused portrait into view.
            target.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(target);
            float deadline = Time.realtimeSinceStartup + 3f;
            Vector2 center;
            VisualElement picked;
            do
            {
                yield return null;
                center = target.worldBound.center;
                picked = target.panel?.Pick(center);
            } while ((picked == null || picked != target && !target.Contains(picked)) &&
                Time.realtimeSinceStartup < deadline);
            Assert.That(picked == target || picked != null && target.Contains(picked), Is.True,
                $"The centre of {to} should pick that slot before pointer release (picked {picked?.name}).");
            using (var move = PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, center)))
                Send(roster, move);
            yield return null;
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

        private static string VictoryRewardItemName()
        {
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var run = (PartyRun)typeof(BattlePrototype).GetField("run", PrivateInstance).GetValue(scene);
            var content = (EncounterData)typeof(BattlePrototype).GetField("content", PrivateInstance).GetValue(scene);
            return "edit-item-" + run.Inventory.Last(item => item.Definition.Id == content.VictoryReward.Id).Id;
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

        private static IEnumerator WaitForRound(VisualElement root, int round)
        {
            string expected = $"第 {round} 回合";
            float deadline = Time.realtimeSinceStartup + 12f;
            while (root.Q<Label>("battle-phase")?.text != expected && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q<Label>("battle-phase")?.text, Is.EqualTo(expected));
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
                yield return null;
            }
            Assert.That(root.Q<Label>("battle-phase").text, Is.EqualTo("战斗结算"));
        }

        private static IEnumerator Drag(VisualElement root, VisualElement source, VisualElement target)
        {
            Down(source);
            yield return WaitForDrag(root);
            yield return null;
            Assert.That(root.Q("equipment-tooltip"), Is.Null);
            Vector2 destination = target.worldBound.center;
            using (var move = PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, destination)))
                Send(target, move);
            yield return null;
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
