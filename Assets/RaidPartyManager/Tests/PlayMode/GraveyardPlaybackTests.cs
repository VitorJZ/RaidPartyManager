using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
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
        public IEnumerator MenuHasNoFacilityOrGraveyardShortcutsAndRegionEnemyPoolsStaySeparate()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            var menu = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            foreach (string shortcut in new[] { "open-graveyard", "open-shop", "open-camp" })
                Assert.That(menu.Q<Button>(shortcut), Is.Null);
            yield return SceneManager.LoadSceneAsync("WhisperingGraveyard");
            yield return null;
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
        public IEnumerator LoadingAGraveyardPresetUsesTheWholeFormationInBattle()
        {
            yield return SceneManager.LoadSceneAsync("WhisperingGraveyard");
            yield return null;
            var scene = Object.FindAnyObjectByType<BattlePrototype>();
            var root = scene.GetComponent<UIDocument>().rootVisualElement;
            Press(root, "open-settings");
            Press(root, "open-enemy-editor");
            Assert.That(root.Q<Button>("apply-enemy-formation").enabledInHierarchy, Is.False);
            root.Q<DropdownField>("enemy-formation").value = "首领 · 无名葬王";
            Assert.That(root.Q<Button>("apply-enemy-formation").enabledInHierarchy, Is.True);
            Press(root, "apply-enemy-formation");
            Assert.That(root.Q("editor-positions").Query<Label>("character-name").ToList().Select(label => label.text),
                Is.EqualTo(new[] { "无名葬王", "盗墓苦役" }));
            Press(root, "back-party-editor");
            var prepared = (IReadOnlyList<Combatant>)typeof(BattlePrototype).GetField("preparedEnemies", Private).GetValue(scene);
            Assert.That(prepared.Select(enemy => (enemy.Character.Id, enemy.Position)),
                Is.EqualTo(new[] { ("EnemyNamelessKing", 1), ("EnemyGraveDigger", 4) }));
            Press(root, "start");
            Assert.That(root.Q("enemy-position-1"), Is.Not.Null);
            Assert.That(root.Q("enemy-position-2"), Is.Null);
            Assert.That(root.Q("enemy-position-4"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator EveryBossGetsACenteredCloseUpBeforeRoundOne()
        {
            var seen = new HashSet<string>();
            var roomFloors = new HashSet<Texture>();
            foreach (var region in new[] { "BattlePrototype", "WhisperingGraveyard", "ClockworkCapital" })
            {
                EnemyFormation[] groups = null;
                for (int i = 0; groups == null || i < groups.Length; i++)
                {
                    yield return SceneManager.LoadSceneAsync(region);
                    yield return null;
                    var scene = Object.FindAnyObjectByType<BattlePrototype>();
                    var data = (EncounterData)typeof(BattlePrototype).GetField("content", Private).GetValue(scene);
                    groups ??= data.EnemyFormations.Where(g => g.Rank == EnemyRank.Boss)
                        .GroupBy(g => g.Members[0].Id).Select(g => g.First()).ToArray();
                    var boss = groups[i].Members[0];
                    var atlas = Resources.Load<Texture2D>("BossEntrances/" + boss.Id);
                    Assert.That(atlas, Is.Not.Null, boss.Name);
                    var ui = scene.GetComponent<UIDocument>().rootVisualElement;
                    Press(ui, "open-settings");
                    Press(ui, "open-enemy-editor");
                    ui.Q<DropdownField>("enemy-formation").value = "首领 · " + groups[i].Name;
                    Press(ui, "apply-enemy-formation");
                    Press(ui, "back-party-editor");
                    yield return null;
                    var room = Resources.Load<ScriptableObject>("BossRooms/" + boss.Id);
                    Assert.That(room, Is.Not.Null, boss.Name);
                    var roomType = room.GetType();
                    var cels = (Texture2D[])roomType.GetField("backdrop").GetValue(room);
                    Assert.That(cels.Where(texture => texture != null).Distinct().Count(), Is.EqualTo(16), boss.Name);
                    var stage = scene.transform.Find("Paper Theatre");
                    var block = new MaterialPropertyBlock();
                    Texture OnStage(string piece)
                    {
                        stage.Find(piece).GetComponent<MeshRenderer>().GetPropertyBlock(block);
                        return block.GetTexture("_MainTex");
                    }
                    Assert.That(cels, Does.Contain(OnStage("Cyclorama")), boss.Name);
                    Assert.That(OnStage("Stage floor"), Is.EqualTo(roomType.GetField("floor").GetValue(room)));
                    Assert.That(roomFloors.Add(OnStage("Stage floor")), Is.True, boss.Name);
                    Assert.That(OnStage("Left wing"), Is.EqualTo(roomType.GetField("leftWing").GetValue(room)));
                    Assert.That(OnStage("Right wing"), Is.EqualTo(roomType.GetField("rightWing").GetValue(room)));
                    Assert.That(Mathf.Abs(stage.Find("Left wing").position.z - stage.Find("Right wing").position.z),
                        Is.GreaterThan(.4f), boss.Name);
                    var camera = Camera.main;
                    float wide = camera.projectionMatrix.m00, peak = 1, last = 1;
                    Vector2 framed = Vector2.zero;
                    var frames = new HashSet<Rect>();
                    void Measure(Camera cam)
                    {
                        if (cam != camera) return;
                        last = cam.projectionMatrix.m00 / wide;
                        var pose = ui.Q("battle-entrance-layer")?.Query<Image>().ToList().Find(v => v.image == atlas);
                        if (pose == null) return;
                        frames.Add(pose.sourceRect);
                        if (last <= peak) return;
                        var mesh = scene.transform.Find("Paper Theatre/" + pose.name)?.GetComponent<MeshRenderer>();
                        if (mesh == null || !mesh.enabled) return;
                        peak = last;
                        framed = cam.WorldToViewportPoint(mesh.bounds.center);
                    }
                    void Begin(ScriptableRenderContext _, Camera cam) => Measure(cam);
                    RenderPipelineManager.beginCameraRendering += Begin;
                    Camera.onPreRender += Measure;
                    var target = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight, 24);
                    var originalTarget = camera.targetTexture;
                    try
                    {
                        Press(ui, "start");
                        if (i % 2 == 1) Press(ui, "battle-speed");
                        float limit = Time.realtimeSinceStartup + 12;
                        do
                        {
                            yield return null;
                            camera.targetTexture = target;
                            camera.Render();
                            camera.targetTexture = originalTarget;
                        }
                        while (ui.Q<Label>("battle-phase")?.text == "入场" && Time.realtimeSinceStartup < limit);
                        yield return null;
                        Assert.That(ui.Q<Label>("battle-phase")?.text, Is.EqualTo("第 1 回合"), boss.Name);
                        Assert.That(peak, Is.GreaterThan(1.2f), boss.Name);
                        Assert.That(framed.x, Is.InRange(.26f,.74f), boss.Name);
                        Assert.That(framed.y, Is.InRange(.22f,.78f), boss.Name);
                        Assert.That(frames.Count, Is.GreaterThan(3), boss.Name);
                        Assert.That(last, Is.EqualTo(1).Within(.01f), boss.Name);
                        Assert.That(ui.Q("enemy-position-1").Q("health-bar").resolvedStyle.opacity, Is.EqualTo(1));
                        seen.Add(boss.Id);
                    }
                    finally
                    {
                        RenderPipelineManager.beginCameraRendering -= Begin;
                        Camera.onPreRender -= Measure;
                        camera.targetTexture = originalTarget;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
            }
            Assert.That(seen.Count, Is.EqualTo(7));
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
            var enemies = root.Q("enemy-positions").Query<Image>("battle-sprite").ToList();
            Assert.That(enemies.Count, Is.EqualTo(4));
            Assert.That(enemies.All(image => image.image.name.EndsWith("-Combat")), Is.True);
            Assert.That(root.Q("enemy-positions").Query("health-bar").ToList().Count, Is.EqualTo(4));

            var stage = scene.transform.Find("Paper Theatre");
            var lighting = stage.Find("Cyclorama").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(lighting.IsKeywordEnabled("RPM_DOCK_MOON"), Is.False);
            Assert.That(lighting.GetVector("_StageMist").w, Is.GreaterThan(0));
            Assert.That(lighting.GetVector("_StageKey").z, Is.GreaterThan(lighting.GetVector("_StageKey").x));
            Assert.That(stage.Find("Left wing/Overhead shadow"), Is.Not.Null);
            Assert.That(stage.Find("Moonlit harbour mist"), Is.Null);
            Assert.That(stage.Find("Upstage").position.z, Is.GreaterThan(stage.Find("Left wing").position.z));
            var block = new MaterialPropertyBlock();
            var worldPoses = new HashSet<(Texture, Vector4)>();
            var renderers = new List<MeshRenderer>();
            bool sawGraveyardAction = false;
            var displayedFrames = new HashSet<string>();
            var sheets = enemies.Select(image => image.image.name.Replace("-Combat", "")).ToArray();
            float deadline = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < deadline)
            {
                stage.GetComponentsInChildren(renderers);
                foreach (var body in renderers)
                {
                    if (!body.enabled || body.name is not ("battle-attack-pose" or "battle-moving-character"))
                        continue;
                    body.GetPropertyBlock(block);
                    var atlas = block.GetTexture("_MainTex");
                    if (atlas != null && sheets.Any(prefix => atlas.name.StartsWith(prefix)) &&
                        (atlas.name.EndsWith("-Attack") || atlas.name.EndsWith("-Special")))
                        worldPoses.Add((atlas, block.GetVector("_MainTex_ST")));
                }
                foreach (var pose in root.Q("battle-action-layer").Query<Image>().ToList())
                {
                    if (pose.image is not Texture2D atlas || !sheets.Any(prefix => atlas.name.StartsWith(prefix)) ||
                        !(atlas.name.EndsWith("-Attack") || atlas.name.EndsWith("-Special"))) continue;
                    sawGraveyardAction = true;
                    Assert.That(pose.sourceRect.width, Is.EqualTo(256));
                    Assert.That(pose.sourceRect.height, Is.EqualTo(192));
                    displayedFrames.Add($"{atlas.name}:{pose.sourceRect}");
                }
                if (root.Q<Label>("battle-phase")?.text == "第 2 回合" || root.Q<Label>("battle-phase")?.text == "战斗结算")
                    break;
                yield return null;
            }
            Assert.That(sawGraveyardAction, Is.True, "An actual authored attack or special atlas must reach the action layer.");
            Assert.That(worldPoses.Count, Is.GreaterThan(1), "The camera must receive different printed attack frames.");
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
