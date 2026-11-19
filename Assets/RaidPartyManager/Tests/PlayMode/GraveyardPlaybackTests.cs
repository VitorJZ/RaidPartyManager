using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RaidPartyManager.Tests
{
    public sealed class GraveyardPlaybackTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private bool useAuthoredLineups;

        [SetUp]
        public void ResetSessions()
        {
            useAuthoredLineups = false;
            typeof(BattlePrototype).GetMethod("ResetPlaySession",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            SceneManager.sceneLoaded += ConfigureLineups;
        }

        [TearDown]
        public void RemoveSceneHook() => SceneManager.sceneLoaded -= ConfigureLineups;

        private void ConfigureLineups(Scene loaded, LoadSceneMode mode)
        {
            if (useAuthoredLineups && loaded.name == "WhisperingGraveyard")
                typeof(BattlePrototype).GetField("randomizeLineups", Private)
                    .SetValue(Object.FindAnyObjectByType<BattlePrototype>(), false);
        }

        [UnityTest]
        public IEnumerator MainMenuOpensGraveyardAndSwitchingRegionsKeepsTheirEnemyPoolsSeparate()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(Object.FindAnyObjectByType<UIDocument>().rootVisualElement, "open-graveyard");
            yield return WaitForScene("WhisperingGraveyard");
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var content = (EncounterData)typeof(BattlePrototype).GetField("content", Private).GetValue(scene);
            var expected = content.EnemyRoster.Where(enemy => enemy.Rank == EnemyRank.Normal).Select(enemy => enemy.Name).ToArray();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            var chosen = ReadPreparedEnemies(root);
            Assert.That(chosen.Length, Is.EqualTo(4));
            Assert.That(chosen.All(expected.Contains), Is.True);

            yield return SceneManager.LoadSceneAsync("BattlePrototype");
            yield return null;
            var dock = Object.FindAnyObjectByType<BattlePrototype>();
            var dockContent = (EncounterData)typeof(BattlePrototype).GetField("content", Private).GetValue(dock);
            var dockNames = dockContent.EnemyRoster.Select(enemy => enemy.Name).ToArray();
            var dockRoot = dock.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(ReadPreparedEnemies(dockRoot).All(dockNames.Contains), Is.True);
            yield return SceneManager.LoadSceneAsync("WhisperingGraveyard");
            yield return null;
            var again = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(ReadPreparedEnemies(again), Is.EqualTo(chosen));
        }

        [UnityTest]
        public IEnumerator GraveyardUsesItsBackgroundAndActualPoseFramesDuringTheFirstRound()
        {
            useAuthoredLineups = true;
            yield return SceneManager.LoadSceneAsync("WhisperingGraveyard");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            typeof(BattlePrototype).GetField("attackAnimationTimeScale", Private).SetValue(scene, 0.20f);
            var background = (Texture2D)typeof(BattlePrototype).GetField("battleBackground", Private).GetValue(scene);
            Assert.That(background.name, Is.EqualTo("04-WhisperingGraveyard"));
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "start");
            Press(root, "battle-pause");
            var enemies = root.Q("enemy-positions").Query<Image>("battle-sprite").ToList();
            Assert.That(enemies.Count, Is.EqualTo(4));
            Assert.That(enemies.All(image => image.image.name.EndsWith("-Combat")), Is.True);
            Assert.That(root.Q("enemy-positions").Query("health-bar").ToList().Count, Is.EqualTo(4));

            bool sawGraveyardAction = false;
            var displayedFrames = new HashSet<string>();
            var sheets = enemies.Select(image => image.image.name.Replace("-Combat", "")).ToArray();
            float deadline = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var pose in root.Q("battle-action-layer").Query<Image>().ToList())
                {
                    if (pose.image is not Texture2D atlas || !sheets.Any(prefix => atlas.name.StartsWith(prefix)) ||
                        !(atlas.name.EndsWith("-Attack") || atlas.name.EndsWith("-Special"))) continue;
                    sawGraveyardAction = true;
                    Assert.That(pose.sourceRect.width, Is.EqualTo(256));
                    Assert.That(pose.sourceRect.height, Is.EqualTo(192));
                    displayedFrames.Add($"{atlas.name}:{pose.sourceRect}");
                }
                if (root.Q<Button>("battle-pause")?.text == "继续" || root.Q<Label>("battle-phase")?.text == "战斗结算")
                    break;
                yield return null;
            }
            Assert.That(sawGraveyardAction, Is.True, "An actual authored attack or special atlas must reach the action layer.");
            Assert.That(displayedFrames.Count, Is.GreaterThan(1), "Playback must advance beyond a single pose frame.");
            Assert.That(root.Q<ScrollView>("battle-log").contentContainer.Query<Label>().ToList().Count, Is.GreaterThan(0));
        }

        private static string[] ReadPreparedEnemies(VisualElement root)
        {
            // The current preparation screen reveals the enemy formation through its editor.
            Press(root, "open-settings");
            Press(root, "open-enemy-editor");
            var names = root.Q("editor-positions").Query<Label>("character-name").ToList()
                .Select(label => label.text).ToArray();
            Press(root, "back-party-editor");
            return names;
        }

        private static IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
            yield return null;
        }

        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
