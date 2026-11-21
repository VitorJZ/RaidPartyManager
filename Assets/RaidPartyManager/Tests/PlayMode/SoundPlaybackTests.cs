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
            var button = Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q<Button>("open-shop");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            Assert.That(effects.isPlaying, Is.True, "The accepted menu input should start a one-shot.");
            yield return WaitForLoop("074_amb_shop_interior");
            yield return SceneManager.LoadSceneAsync("Camp");
            yield return WaitForLoop("075_amb_campfire");
            Assert.That(GameObject.Find("Game audio"), Is.SameAs(player));
            yield return SceneManager.LoadSceneAsync("MainMenu");
            float end = Time.realtimeSinceStartup + 3;
            while (ambience.clip != null && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(ambience.isPlaying, Is.False);
            Assert.That(ambience.clip, Is.Null);

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
