using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidPartyManager.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Tests
{
    public sealed class SoundPlaybackTests
    {
        [UnityTest]
        public IEnumerator VolumeSlidersMixIndependentlyAcrossScenesAndAudioStartup()
        {
            string[] keys = { "Audio.Volume.Master", "Audio.Volume.Ambience", "Audio.Volume.Effects" };
            var previous = keys.Select(key => PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null).ToArray();
            var audioType = typeof(BattlePrototype).Assembly.GetType("RaidPartyManager.Presentation.GameAudio");
            try
            {
                foreach (var key in keys) PlayerPrefs.DeleteKey(key);
                yield return SceneManager.LoadSceneAsync("MainMenu");
                var menu = Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>().rootVisualElement;
                Press(menu, "open-settings");
                var panel = menu.Q("run-settings");
                Assert.That(panel.Query<SliderInt>().ToList().Select(s => s.value), Is.EqualTo(new[] { 100, 100, 100 }));
                panel.Q<SliderInt>("master-volume").value = 50;
                panel.Q<SliderInt>("ambience-volume").value = 25;
                panel.Q<SliderInt>("effects-volume").value = 80;
                Assert.That(panel.Q<Label>("effects-volume-value").text, Is.EqualTo("80%"));
                Press(panel, "close-settings");
                Assert.That(menu.Q("main-menu").enabledInHierarchy, Is.True);
                Assert.That(keys.Select(key => PlayerPrefs.GetInt(key)), Is.EqualTo(new[] { 50, 25, 80 }));

                // Recreate the persistent player just as BeforeSceneLoad does on the next launch.
                Object.Destroy(GameObject.Find("Game audio"));
                yield return null;
                audioType.GetMethod("Boot", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                yield return SceneManager.LoadSceneAsync("Camp");
                yield return new WaitForSecondsRealtime(1f);
                var sources = GameObject.Find("Game audio").GetComponents<AudioSource>();
                var ambience = sources.Single(s => s.loop);
                var effects = sources.Single(s => !s.loop);
                Assert.That(ambience.volume, Is.EqualTo(.44f * .5f * .25f).Within(.001f));
                Assert.That(effects.volume, Is.EqualTo(.40f).Within(.001f));

                var camp = Object.FindAnyObjectByType<CampPrototype>().GetComponent<UIDocument>().rootVisualElement;
                Press(camp, "open-settings");
                panel = camp.Q("run-settings");
                Assert.That(panel.Query<SliderInt>().ToList().Select(s => s.value), Is.EqualTo(new[] { 50, 25, 80 }));
                panel.Q<SliderInt>("ambience-volume").value = 0;
                Assert.That(ambience.volume, Is.Zero);
                Assert.That(effects.volume, Is.EqualTo(.40f).Within(.001f));
                panel.Q<SliderInt>("ambience-volume").value = 100;
                panel.Q<SliderInt>("effects-volume").value = 0;
                Assert.That(effects.volume, Is.Zero);
                Assert.That(ambience.volume, Is.EqualTo(.22f).Within(.001f));
                panel.Q<SliderInt>("effects-volume").value = 80;
                panel.Q<SliderInt>("master-volume").value = 0;
                Assert.That(effects.volume, Is.Zero);
                Assert.That(ambience.volume, Is.Zero);

                yield return SceneManager.LoadSceneAsync("DockShop");
                yield return new WaitForSecondsRealtime(1.8f);
                Assert.That(ambience.clip?.name, Is.EqualTo("Dock-Shop"));
                Assert.That(ambience.volume, Is.Zero);
                var shop = Object.FindAnyObjectByType<DockShop>().GetComponent<UIDocument>().rootVisualElement;
                Press(shop, "open-settings");
                panel = shop.Q("run-settings");
                panel.Q<SliderInt>("master-volume").value = 100;
                Assert.That(ambience.volume, Is.EqualTo(.44f).Within(.001f));
                Assert.That(effects.volume, Is.EqualTo(.80f).Within(.001f));
                Press(panel, "close-settings");
                yield return SceneManager.LoadSceneAsync("MainMenu");
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    if (previous[i].HasValue) PlayerPrefs.SetInt(keys[i], previous[i].Value);
                    else PlayerPrefs.DeleteKey(keys[i]);
                }
                PlayerPrefs.Save();
                var audio = GameObject.Find("Game audio")?.GetComponent(audioType);
                if (audio != null) audioType.GetMethod("ApplyVolumes", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audio, null);
            }

            void Press(VisualElement root, string id)
            {
                var button = root.Q<Button>(id);
                Assert.That(button, Is.Not.Null, id);
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [Test]
        public void CatalogueLoadsAllNinetyRecordingsAndMapsEveryOriginalSkill()
        {
            var bank = Resources.Load<ScriptableObject>("MistboundSounds");
            Assert.That(bank, Is.Not.Null);
            var clips = (AudioClip[])bank.GetType().GetField("clips", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bank);
            Assert.That(clips.Length, Is.EqualTo(91));
            for (int i = 1; i < clips.Length; i++)
            {
                var clip = clips[i];
                Assert.That(clip, Is.Not.Null, "Cue " + i);
                Assert.That(clip.name, Does.StartWith(i.ToString("000") + "_"));
                Assert.That(clip.samples, Is.GreaterThan(0));
            }
            var book = typeof(BattlePrototype).Assembly.GetType("RaidPartyManager.Presentation.BattleSounds")
                .GetMethod("Book", BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 1; i <= 20; i++)
                Assert.That(Convert.ToInt32(book.Invoke(null, new object[] { $"skill-book-{i:00}" })), Is.EqualTo(i + 51));
        }

        [UnityTest]
        public IEnumerator StartingABattlePlaysTheEntranceAndAnActualHit()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            var menu = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var start = menu.Q<Button>("open-battle");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = start;
                start.SendEvent(submit);
            }
            float deadline = Time.realtimeSinceStartup + 10;
            while (Object.FindAnyObjectByType<BattlePrototype>() == null && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var battle = Object.FindAnyObjectByType<BattlePrototype>();
            Assert.That(battle, Is.Not.Null);
            var root = battle.GetComponent<UIDocument>().rootVisualElement;
            var audio = GameObject.Find("Game audio").GetComponents<MonoBehaviour>().Single();
            var played = (IDictionary)audio.GetType().GetField("lastPlayed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(audio);
            double started = Time.unscaledTimeAsDouble;
            var button = root.Q<Button>("start");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            Assert.That(Heard("UiBattleStart"), Is.True);
            deadline = Time.realtimeSinceStartup + 22;
            while (!HeardHit() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(HeardHit(), Is.True, "Combat playback should emit a hit when the first damaging action resolves.");
            yield return SceneManager.LoadSceneAsync("MainMenu");

            bool Heard(string name)
            {
                foreach (DictionaryEntry entry in played)
                    if (entry.Key.ToString() == name && (double)entry.Value >= started) return true;
                return false;
            }

            bool HeardHit() => Heard("HitPhysical") || Heard("HitMagic") || Heard("ArrowImpact") || Heard("HitShield")
                || Heard("HitFire") || Heard("HitLightning") || Heard("HitFixedDamage");
        }

        [UnityTest]
        public IEnumerator MenuInputAndSceneTransitionsUseOneMixerAndTheCorrectLoop()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            var player = GameObject.Find("Game audio");
            Assert.That(player, Is.Not.Null);
            var sources = player.GetComponents<AudioSource>();
            var effects = sources.Single(source => !source.loop);
            var ambience = sources.Single(source => source.loop);
            var button = Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q<Button>("open-battle");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            Assert.That(effects.isPlaying, Is.True, "The accepted menu input should start a one-shot.");
            yield return WaitForLoop("Dock-Exploration");
            yield return SceneManager.LoadSceneAsync("DockShop");
            yield return WaitForLoop("Dock-Shop");
            yield return SceneManager.LoadSceneAsync("Camp");
            yield return WaitForLoop("Dock-Camp");
            Assert.That(GameObject.Find("Game audio"), Is.SameAs(player));
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return WaitForLoop("amb_main_menu_march");

            IEnumerator WaitForLoop(string name)
            {
                float deadline = Time.realtimeSinceStartup + 6;
                while ((ambience.clip == null || ambience.clip.name != name || !ambience.isPlaying) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(ambience.clip?.name, Is.EqualTo(name));
                Assert.That(ambience.isPlaying, Is.True);
            }
        }
    }
}
