using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RaidPartyManager.Domain;
using RaidPartyManager.Battle;
using RaidPartyManager.Presentation;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RaidPartyManager.Tests
{
    public sealed class AdventureSceneTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private AdventureRun run;

        [SetUp]
        public void Reset()
        {
            typeof(AdventureEventHub).Assembly.GetType("RaidPartyManager.Presentation.AdventureFlow")
                .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            typeof(BattlePrototype).GetMethod("ResetPlaySession", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        }

        [TearDown]
        public void ClearSceneContext() => typeof(AdventureEventHub).Assembly.GetType("RaidPartyManager.Presentation.AdventureFlow")
            .GetMethod("ClearSceneContext", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

        private IEnumerator OpenMenu()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-events"); yield return WaitScene("EventSelect");
            run = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private).GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
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
                var illustration = root.Q<Image>("event-illustration");
                var description = root.Q<Label>("event-description");
                var options = root.Q("event-options");
                Assert.That(illustration.image, Is.TypeOf<RenderTexture>(), kind.ToString());
                Assert.That(description.text.Length, Is.GreaterThan(10));
                Assert.That(illustration.worldBound.xMax, Is.LessThan(description.worldBound.xMin));
                Assert.That(options.worldBound.yMin, Is.GreaterThan(root.Q("event-story").worldBound.yMax));
                AssertNoVisibleTiers(root);
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
            }
            var party = run.Party;
            Press(Root<AdventureEventHub>(), "inspect-node-Camp"); yield return WaitScene("Camp");
            Assert.That(typeof(CampPrototype).GetField("run", Private).GetValue(Object.FindAnyObjectByType<CampPrototype>()), Is.SameAs(party));
            Press(Root<CampPrototype>(), "camp-rest");
            Assert.That(Root<CampPrototype>().Q<Button>("camp-rest").enabledInHierarchy, Is.False);
            Press(Root<CampPrototype>(), "return-event-select"); yield return WaitScene("EventSelect");

            Press(Root<AdventureEventHub>(), "inspect-node-Shop"); yield return WaitScene("DockShop");
            var stock = run.Current.Stock;
            int chosen = Enumerable.Range(0, stock.Count).OrderBy(i => stock[i].Price).First();
            int gold = party.Gold, items = party.Inventory.Count, price = stock[chosen].Price;
            Assert.That(Root<DockShop>().Q<Label>("shop-gold").text, Is.EqualTo($"金币  {gold}"));
            var shopRoot = Root<DockShop>();
            Down(shopRoot.Q("shop-offer-" + chosen));
            Up(shopRoot, shopRoot.Q("shop-checkout").worldBound.center);
            Assert.That(stock[chosen].Sold, Is.True);
            Assert.That(party.Gold, Is.EqualTo(gold - price)); Assert.That(party.Inventory.Count, Is.EqualTo(items + 1));
            AssertNoVisibleTiers(shopRoot);
            Press(shopRoot, "return-event-select"); yield return WaitScene("EventSelect");

            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            Press(Root<DockTown>(), "town-service-Camp"); yield return WaitScene("Camp");
            Press(Root<CampPrototype>(), "camp-rest");
            Press(Root<CampPrototype>(), "return-event-select"); yield return WaitScene("Town");
            Assert.That(run.Current.UsedServices.Count, Is.EqualTo(1));
            Press(Root<DockTown>(), "town-service-Quest");
            Assert.That(run.Current.UsedServices.Count, Is.EqualTo(2));
            Assert.That(Root<DockTown>().Q<Button>("town-service-Recruit").enabledInHierarchy, Is.False);
            Assert.That(Root<DockTown>().Q<Button>("town-service-Shop").enabledInHierarchy, Is.False);
            Press(Root<DockTown>(), "return-event-select"); yield return WaitScene("EventSelect");
            Assert.That(run.Current, Is.Null);
        }

        [UnityTest]
        public IEnumerator HatGameActuallyMovesOnScreenThenAwardsTheTrackedItem()
        {
            yield return OpenMenu(); var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-HatTrick"); yield return null;
            Assert.That(root.Q("hat-target-item"), Is.Null);
            Assert.That(root.Q("hat-game").Query<Label>().ToList().Any(label => label.text == run.Current.HatRewards[0].Name), Is.False);
            int items = run.Party.Inventory.Count;
            Press(root, "hat-0"); Assert.That(run.Current.Resolved, Is.False);
            var first = run.Current.HatSwaps[0];
            var movingHat = root.Q<Button>("hat-" + first.First);
            float before = movingHat.worldBound.center.x;
            Press(root, "hat-start");
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(Mathf.Abs(movingHat.worldBound.center.x - before), Is.GreaterThan(10), "The hat must visibly animate, not just change its hidden result.");
            float deadline = Time.realtimeSinceStartup + 8;
            while (run.Current.HatStep < run.Current.HatSwaps.Count && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(run.Current.HatStep, Is.EqualTo(7));
            Press(root, "hat-0");
            Assert.That(run.Current.HatWon, Is.True);
            Assert.That(run.Party.Inventory.Count, Is.EqualTo(items + 1));
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
                Assert.That(tooltip.Q("equipment-tooltip-art").Q<Image>().image, Is.SameAs(item.image));
                using (var leave = PointerLeaveEvent.GetPooled(new Event
                    { type = EventType.MouseMove, mousePosition = item.worldBound.center }))
                    Send(item, leave);
                Assert.That(root.Q("equipment-tooltip"), Is.Null);
            }
            using (var enter = PointerEnterEvent.GetPooled(new Event
                { type = EventType.MouseMove, mousePosition = revealed[0].worldBound.center }))
                Send(revealed[0], enter);
            Assert.That(root.Q("equipment-tooltip"), Is.Not.Null);
            AssertNoVisibleTiers(root);
            Press(root, "finish-event");
            Assert.That(root.Q("equipment-tooltip"), Is.Null, "Closing the event must remove its hover panel.");
        }

        [UnityTest]
        public IEnumerator ChestCompanionIsLockedInTheEditorAndReturnsThroughActualNodeAdvancement()
        {
            yield return OpenMenu(); var root = Root<AdventureEventHub>();
            string id = run.Party.Members.First().Id;
            Press(root, "inspect-event-LockedChest");
            Assert.That(root.Q("leave-companion-" + id), Is.Null);
            Press(root, "chest-skip");
            Assert.That(root.Q("event-window"), Is.Null);
            Assert.That(run.Party.Members.Any(m => m.IsAway), Is.False);

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
            Assert.That(supplies.Length, Is.EqualTo(50));
            Assert.That(books.Length, Is.EqualTo(20));
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
            Press(root, "finish-event");
            Assert.That(run.Party.Members.Single(m => m.Id == id).IsAway, Is.True);
            Assert.That(run.Party.CanStartBattle, Is.True);
            Press(root, "event-party-editor");
            Assert.That(root.Q<Button>("edit-member-" + id).enabledInHierarchy, Is.False);
            Press(root, "back-party-editor");
            for (int i = 0; i < 3; i++)
            {
                Press(root, "inspection-advance");
                Assert.That(run.Party.Members.Single(m => m.Id == id).IsAway, Is.EqualTo(i < 2));
            }
            Assert.That(root.Q("adventure-party").Query<Label>().ToList().Any(l => l.text.Contains("守候宝箱")), Is.False);
        }

        [UnityTest]
        public IEnumerator TrialBattleLetsThePlayerChooseAnEvolutionThenShowsItInCamp()
        {
            yield return OpenMenu(); AddTestWeapons();
            var root = Root<AdventureEventHub>();
            Press(root, "inspect-event-AscensionTrial");
            Assert.That(run.EvolutionCandidates.Count, Is.GreaterThan(0));
            Assert.That(root.Q<Button>("event-trial-gift").Q<Image>(), Is.Null, "The gift stays wrapped until chosen.");
            Press(root, "event-trial-battle"); yield return WaitScene("BattlePrototype");
            FastBattle(); Assert.That(run.Current.Battle.Enemies.Count, Is.EqualTo(1));
            Press(Root<BattlePrototype>(), "start"); yield return WaitSettlement();
            var battle = Root<BattlePrototype>();
            Assert.That(battle.Q("evolution-choices"), Is.Null);
            ClaimBonusLoot(battle);
            Press(battle, "claim-gold");
            yield return WaitScene("EventSelect");
            root = Root<AdventureEventHub>();
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
            Press(root, "finish-event");
            Assert.That(run.Current, Is.Null);
            Press(Root<AdventureEventHub>(), "inspect-node-Camp"); yield return WaitScene("Camp");
            var sprite = Root<CampPrototype>().Q<Image>("camp-seat-" + member.Position.Value);
            Assert.That(sprite.image.name, Does.Contain(evolved));
        }

        [UnityTest]
        public IEnumerator TenNodeRouteCanCompleteIncludingLootAndTheFinalBoss()
        {
            yield return OpenMenu(); Press(Root<AdventureEventHub>(), "event-tab-route");
            run = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private).GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
            AddTestWeapons();
            for (int stage = 1; stage <= 10; stage++)
            {
                Assert.That(run.Stage, Is.EqualTo(stage));
                Press(Root<AdventureEventHub>(), "route-choice-0");
                var kind = run.Current.Kind;
                if (kind == NodeKind.Camp)
                {
                    yield return WaitScene("Camp"); Press(Root<CampPrototype>(), "camp-rest");
                    Press(Root<CampPrototype>(), "return-event-select");
                }
                else if (kind == NodeKind.Shop)
                {
                    yield return WaitScene("DockShop"); Press(Root<DockShop>(), "return-event-select");
                }
                else if (kind == NodeKind.Town)
                {
                    yield return WaitScene("Town"); Press(Root<DockTown>(), "return-event-select");
                }
                else
                {
                    yield return WaitScene("BattlePrototype"); FastBattle();
                    Press(Root<BattlePrototype>(), "start"); yield return WaitSettlement();
                    Assert.That(run.Failed, Is.False);
                    var battle = Root<BattlePrototype>();
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
                    Press(battle, "claim-gold");
                    Assert.That(run.Party.Gold, Is.GreaterThan(gold));
                }
                yield return WaitScene("EventSelect");
            }
            Assert.That(run.CompletedNodes, Is.EqualTo(10)); Assert.That(run.FloorCleared, Is.True);
            Assert.That(Root<AdventureEventHub>().Q<Button>("route-next-floor"), Is.Not.Null);
        }

        private static void ClaimBonusLoot(VisualElement battle)
        {
            foreach (string button in battle.Q("victory-rewards").Query<Button>().ToList()
                .Where(view => view.name?.StartsWith("claim-loot-") == true).Select(view => view.name).ToArray())
                Press(battle, button);
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
            var enemyDefinition = new CharacterDefinition("durable", "test", Profession.Warrior, Personality.Calm,
                new Stats(1, 999, 999, 9999), 10,
                new ActionDefinition("basic", "test", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            var enemy = new Combatant("durable", enemyDefinition, Side.Enemy, 1, enemyDefinition.BaseStats, 9999);
            int ticks = System.Math.Max(10, run.Party.Members.Max(m => m.Character.ActionInterval));
            var request = run.Party.BeginBattle(new[] { enemy }, ticks, null);
            var result = new BattleSimulator().Simulate(request);
            Assert.That(run.Party.TrySettle(result, out _), Is.True);
            var wounded = run.Party.Members.First(m => m.Health < m.EffectiveStats.Health);
            int health = wounded.Health;
            Press(Root<AdventureEventHub>(), "inspect-node-NormalBattle"); yield return WaitScene("BattlePrototype");
            Assert.That(run.Party.Members.Single(m => m.Id == wounded.Id).Health, Is.EqualTo(health),
                "Entering a route battle must not use the rehearsal's full-health reset.");
            Press(Root<BattlePrototype>(), "open-settings");
            Press(Root<BattlePrototype>(), "return-main-menu"); yield return WaitScene("MainMenu");
            Press(Root<PrototypeSceneMenu>(), "open-events"); yield return WaitScene("EventSelect");
            Press(Root<AdventureEventHub>(), "finish-event");
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
            {
                var next = Root<BattlePrototype>().Q<Button>("next-round");
                if (next != null) Press(Root<BattlePrototype>(), "next-round");
                yield return null;
            }
            Assert.That(Root<BattlePrototype>().Q<Label>("battle-phase").text, Is.EqualTo("战斗结算"));
        }

        private static VisualElement Root<T>() where T : MonoBehaviour =>
            Object.FindAnyObjectByType<T>().GetComponent<UIDocument>().rootVisualElement;

        private static IEnumerator WaitScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
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
