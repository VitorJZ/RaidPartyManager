using System.Collections;
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

namespace RaidPartyManager.Tests
{
    public sealed class AdventureSceneTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly System.Type Flow = typeof(AdventureEventHub).Assembly.GetType("RaidPartyManager.Presentation.AdventureFlow");
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
        }

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
            yield return CheckEditorTopBar(Root<CampPrototype>(), "open-camp-editor", "run-gold", party.Gold);
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
            yield return CheckEditorTopBar(shopRoot, "open-shop-editor", "shop-gold", party.Gold);
            AssertNoVisibleTiers(shopRoot);
            Press(shopRoot, "return-event-select"); yield return WaitScene("EventSelect");

            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            yield return CheckEditorTopBar(Root<DockTown>(), "open-town-editor", "run-gold", party.Gold);
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

            typeof(AdventureRun).GetProperty(nameof(AdventureRun.QuestWins)).SetValue(run, 2);
            Press(Root<AdventureEventHub>(), "inspect-node-Town"); yield return WaitScene("Town");
            var town = Root<DockTown>();
            int beforeReward = party.Gold;
            Press(town, "town-service-Quest");
            Assert.That(party.Gold, Is.EqualTo(beforeReward + 15));
            Assert.That(town.Q("reward-flight").Q("reward-gold-icon"), Is.Not.Null);
            Assert.That(town.Q<Label>("run-gold").text, Is.EqualTo($"金币  {beforeReward}"));
            Press(town, "return-event-select");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Town"));
            yield return WaitScene("EventSelect");
            Assert.That(Root<AdventureEventHub>().Q<Label>("adventure-gold").text, Is.EqualTo($"金币  {beforeReward + 15}"));
        }

        [UnityTest]
        public IEnumerator CampOffersOneActionAndAllowsChoosingARecruitOrEvolutionBranch()
        {
            yield return OpenMenu();
            Press(Root<AdventureEventHub>(), "inspect-node-Camp");
            yield return WaitScene("Camp");
            var root = Root<CampPrototype>();
            var actions = new[] { "camp-recruit", "camp-evolve", "camp-rest" };
            Assert.That(actions.All(id => root.Q<Button>(id).enabledInHierarchy), Is.True);
            Press(root, "camp-recruit");
            Press(root, "close-camp-choice");
            Assert.That(actions.All(id => root.Q<Button>(id).enabledInHierarchy), Is.True);
            var recruit = run.CampRecruits.First();
            var oldMember = run.Party.Members.Last();
            Press(root, "camp-recruit");
            yield return null;
            AssertCharacterPreview(root, "camp-recruit-" + recruit.Id, recruit);
            Press(root, "camp-recruit-" + recruit.Id);
            Assert.That(root.Q("character-choice-preview"), Is.Null);
            Assert.That(run.Current.Resolved, Is.False);
            yield return null;
            AssertCharacterPreview(root, "replace-recruit-" + oldMember.Id, oldMember.Character);
            Press(root, "replace-recruit-" + oldMember.Id);
            Assert.That(run.Party.Members.Single(member => member.Id == oldMember.Id).Character.Id, Is.EqualTo(recruit.Id));
            Assert.That(run.Current.Resolved, Is.True);
            Assert.That(actions.All(id => !root.Q<Button>(id).enabledInHierarchy), Is.True);

            Press(root, "return-event-select");
            yield return WaitScene("EventSelect");
            Press(Root<AdventureEventHub>(), "inspect-node-Camp");
            yield return WaitScene("Camp");
            root = Root<CampPrototype>();
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
            Assert.That(preview.Q<Label>("character-preview-trait")?.text, Is.EqualTo(expected.TraitText));
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
            using (var enter = PointerEnterEvent.GetPooled(new Event
                { type = EventType.MouseMove, mousePosition = revealed[0].worldBound.center }))
                Send(revealed[0], enter);
            Assert.That(root.Q("equipment-tooltip"), Is.Not.Null);
            AssertNoVisibleTiers(root);
            Press(root, "finish-event");
            float finishDeadline = Time.realtimeSinceStartup + 2f;
            while (root.Q("event-window") != null && Time.realtimeSinceStartup < finishDeadline)
                yield return null;
            Assert.That(root.Q("event-window"), Is.Null);
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
        public IEnumerator NewRouteDraftsTwoOfFourAndUsesOnlyTheRolledGear()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");

            var root = Root<AdventureEventHub>();
            Assert.That(root.Q("dock-route"), Is.Null);
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

            var party = CurrentRoute().Party;
            var members = party.Members.OrderBy(member => member.Position).ToArray();
            Assert.That(members.Select(member => member.Character.Id), Is.EqualTo(new[] { definitions[1].Id, definitions[2].Id }));
            Assert.That(members.Select(member => member.Position.Value), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(members.All(member => member.EquipmentIds.All(item => item == null)), Is.True);
            Assert.That(party.Gold, Is.Zero);
            Assert.That(root.Q<Label>("adventure-gold").text, Is.EqualTo("金币  0"));
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
            Press(root, "back-party-editor");
            SaveAndExit(root);
            yield return WaitScene("MainMenu");

            ResetAdventureFlow();
            Press(Root<PrototypeSceneMenu>(), "open-route");
            yield return WaitScene("EventSelect");
            var restored = CurrentRoute().Party;
            Assert.That(restored.Gold, Is.Zero);
            Assert.That(Root<AdventureEventHub>().Q("starter-draft"), Is.Null);
            Assert.That(restored.Members.Select(member => member.Character.Id),
                Is.EqualTo(members.Select(member => member.Character.Id)));
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
            var map = Root<AdventureEventHub>().Q("dock-route");
            Assert.That(run.CompletedNodes, Is.EqualTo(completed));
            Assert.That(run.Current, Is.Null);
            Assert.That(run.Party.Gold, Is.EqualTo(gold));
            Assert.That(run.Party.Members.First().EquipmentIds[0], Is.EqualTo(potion.Id));
            Assert.That(run.Party.Inventory.Any(item => item.Id == potion.Id), Is.True);
            Assert.That(((RouteStop)map.Q<Button>("route-node-current").userData).Number, Is.EqualTo(target.Number));
            Assert.That(map.Q("route-choice-0"), Is.Null);
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
            var weak = new CharacterDefinition(original.Id, original.Name, Profession.Warrior, Personality.Cautious,
                new Stats(1, 0, 0, 1), 1, new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            run.Party.ResetRoster(new[] { new PartyMemberStart("last-ally", weak, 1, 1) });
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
            Assert.That(abandoned.Party.Gold, Is.EqualTo(47));
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
            Assert.That(fresh.Party.Gold, Is.Zero);
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
                var enemies = seeded.PrepareEnemies(content.EnemyRoster);
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
            Press(root, "route-node-current");
            Assert.That(run.Current.Event, Is.EqualTo(AdventureEventKind.FoundCoins));
            Press(root, "event-collect-coins");
            Assert.That(run.Current.Resolved, Is.True);
            Assert.That(root.Q<Button>("finish-event"), Is.Not.Null);
            int rewardGold = run.Party.Gold;
            Assert.That(rewardGold, Is.GreaterThan(30));
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
            bool tookRightBranch = false;
            for (int stage = 1; stage <= 10; stage++)
            {
                Assert.That(run.Stage, Is.EqualTo(stage));
                var root = Root<AdventureEventHub>();
                var map = root.Q("dock-route");
                Assert.That(map.parent.Children().Select(section => section.name),
                    Is.EqualTo(new[] { "dock-route", "adventure-party" }));
                Assert.That(root.Q("reset-adventure"), Is.Null);
                var firstChoice = map.Q<Button>("route-choice-0");
                float deadline = Time.realtimeSinceStartup + 2f;
                while ((!firstChoice.visible || firstChoice.pickingMode != PickingMode.Position || !(firstChoice.worldBound.height > 0))
                    && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(firstChoice.visible && firstChoice.pickingMode == PickingMode.Position, Is.True);
                Assert.That(root.Q("run-top-bar").worldBound.yMax, Is.LessThan(map.worldBound.yMin));

                var current = map.Q("route-node-current");
                var previous = map.Q("route-node-previous");
                Assert.That(((RouteStop)current.userData).Number, Is.EqualTo(stage - 1));
                Assert.That(map.Query<Label>().ToList(), Is.Empty);
                Assert.That(current.Q<Image>("route-node-icon").image, Is.Not.Null);
                Assert.That(map.Query("route-current-circle").ToList(), Has.Count.EqualTo(1));
                Assert.That(current.Q("route-current-circle").pickingMode, Is.EqualTo(PickingMode.Ignore));
                if (stage == 1)
                    Assert.That(previous, Is.Null);
                else
                {
                    Assert.That(((RouteStop)previous.userData).Number, Is.EqualTo(stage - 2));
                    Assert.That(previous.worldBound.center.y, Is.GreaterThan(current.worldBound.center.y));
                }
                var choices = map.Query<Button>().ToList();
                Assert.That(choices, Has.Count.EqualTo(run.RouteOptions.Count));
                for (int i = 0; i < choices.Count; i++)
                {
                    var stop = (RouteStop)choices[i].userData;
                    Assert.That(stop.Number, Is.EqualTo(stage));
                    Assert.That(stop.Kind, Is.EqualTo(run.RouteOptions[i]));
                    Assert.That(choices[i].Q<Image>("route-node-icon").image, Is.SameAs(mapArt.NodeIcon(stop.Kind.Value)));
                    Assert.That(choices[i].worldBound.center.y, Is.LessThan(current.worldBound.center.y));
                }
                if (choices.Count == 2)
                {
                    Assert.That(choices[0].worldBound.center.x, Is.LessThan(current.worldBound.center.x));
                    Assert.That(choices[1].worldBound.center.x, Is.GreaterThan(current.worldBound.center.x));
                }
                int choiceIndex = choices.Count == 2 && !tookRightBranch ? 1 : 0;
                tookRightBranch |= choiceIndex == 1;
                var selected = (RouteStop)choices[choiceIndex].userData;
                Press(root, "route-choice-" + choiceIndex);
                var kind = run.Current.Kind;
                Assert.That(kind, Is.EqualTo(selected.Kind));
                if (kind == NodeKind.Event)
                {
                    var activeMap = root.Q("dock-route");
                    Assert.That(((RouteStop)activeMap.Q("route-node-current").userData).Lane, Is.EqualTo(selected.Lane));
                    Assert.That(activeMap.Q("route-choice-0"), Is.Null);
                    Assert.That(root.Q("event-window"), Is.Not.Null);
                    Press(root, "close-event");
                }
                else if (kind == NodeKind.TreasureChest)
                {
                    Assert.That(choices, Has.Count.EqualTo(1));
                    Assert.That(stage, Is.InRange(3, 8));
                    var window = root.Q("event-window");
                    Assert.That(window.Q("chest-closed").style.opacity.value, Is.EqualTo(1));
                    Assert.That(window.Q("chest-reward-0"), Is.Null);
                    Press(window, "open-treasure-chest");
                    Assert.That(window.Q("chest-reward-0"), Is.Null);
                    float openingDeadline = Time.realtimeSinceStartup + 3f;
                    while (window.Q("chest-reward-0") == null && Time.realtimeSinceStartup < openingDeadline)
                        yield return null;
                    Assert.That(window.Q("chest-open").style.opacity.value, Is.EqualTo(1));
                    Assert.That(window.Query<Button>().ToList().Count(button => button.name?.StartsWith("chest-reward-") == true),
                        Is.EqualTo(3));
                    yield return null;
                    int items = run.Party.Inventory.Count + run.Party.SkillBooks.Count;
                    var origin = window.Q("chest-reward-1").Q<Image>().worldBound.center;
                    Press(window, "chest-reward-1");
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
                    Press(window, "finish-event");
                }
                else if (kind == NodeKind.Camp)
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
                yield return WaitScene("EventSelect");
                Assert.That(run.RouteCurrent.Lane, Is.EqualTo(selected.Lane));
            }
            Assert.That(tookRightBranch, Is.True);
            Assert.That(run.CompletedNodes, Is.EqualTo(10)); Assert.That(run.FloorCleared, Is.True);
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
            Assert.That(run.FloorCleared, Is.True);
            Assert.That(run.Party.Gold, Is.EqualTo(finalGold));
            Assert.That(run.Party.Inventory.Concat(run.Party.SkillBooks).Select(item => item.Id), Is.EqualTo(rewardIds));
            var finalMap = Root<AdventureEventHub>().Q("dock-route");
            Assert.That(((RouteStop)finalMap.Q("route-node-current").userData).Kind, Is.EqualTo(NodeKind.Boss));
            Assert.That(finalMap.Q<Button>("route-node-current"), Is.Null);
            Assert.That(finalMap.Q("route-choice-0"), Is.Null);
            Press(finalMap, "route-next-floor");
            var newFloor = Root<AdventureEventHub>().Q("dock-route");
            Assert.That(((RouteStop)newFloor.Q("route-node-current").userData).Number, Is.Zero);
            Assert.That(newFloor.Q("route-node-previous"), Is.Null);

            var routeRoot = Root<AdventureEventHub>();
            Press(routeRoot, "open-settings");
            Press(routeRoot.Q("run-settings"), "reset-adventure");
            ConfirmFirstPair(routeRoot);
            var resetRun = (AdventureRun)typeof(AdventureEventHub).GetField("run", Private)
                .GetValue(Object.FindAnyObjectByType<AdventureEventHub>());
            Assert.That(resetRun.Floor, Is.EqualTo(1));
            Assert.That(resetRun.CompletedNodes, Is.Zero);
            Assert.That(routeRoot.Q("run-settings"), Is.Null);
            Assert.That(routeRoot.Q("dock-route").Q("route-choice-0"), Is.Not.Null);
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
                yield return null;
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
