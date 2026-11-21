using System.Collections.Generic;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidPartyManager.Presentation
{
    internal sealed class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;
        private SoundBank bank;
        private AudioSource effects, atmosphere;
        private SoundCue environment;
        private readonly Dictionary<SoundCue, double> lastPlayed = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var player = new GameObject("Game audio");
            DontDestroyOnLoad(player);
            instance = player.AddComponent<GameAudio>();
            instance.bank = Resources.Load<SoundBank>("MistboundSounds");
            instance.effects = instance.Source(false);
            instance.atmosphere = instance.Source(true);
            SceneManager.sceneLoaded += instance.SceneLoaded;
        }

        private AudioSource Source(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.priority = loop ? 160 : 64;
            return source;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (instance == this) instance = null;
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => SetScene(scene.name, AdventureFlow.Active);

        internal static void SetScene(string scene, AdventureRun run)
        {
            if (instance == null) return;
            bool cemetery = run?.InGraveyard == true || GraveStories.Contains(run?.Current?.Event);
            instance.environment = scene switch
            {
                "Camp" => SoundCue.AmbCampfire,
                "EventSelect" or "BattlePrototype" when !cemetery => SoundCue.AmbMistboundDocks,
                "Town" or "TalentMarket" when !cemetery => SoundCue.AmbDocksideTown,
                "DockShop" when !cemetery => SoundCue.AmbShopInterior,
                _ => SoundCue.None
            };
        }

        private void Update()
        {
            var next = bank.Clip(environment);
            if (atmosphere.clip != next)
            {
                atmosphere.volume = Mathf.MoveTowards(atmosphere.volume, 0, Time.unscaledDeltaTime * .9f);
                if (atmosphere.volume > 0 && atmosphere.isPlaying) return;
                atmosphere.Stop();
                atmosphere.volume = 0;
                atmosphere.clip = next;
                if (next != null) atmosphere.Play();
            }
            if (next != null)
                atmosphere.volume = Mathf.MoveTowards(atmosphere.volume, .44f, Time.unscaledDeltaTime * .65f);
        }

        internal static void Play(SoundCue cue, float gain = .64f)
        {
            if (instance == null || cue == SoundCue.None) return;
            double now = Time.unscaledTimeAsDouble;
            // Simultaneous hits, group statuses and overlapping UI events share one transient.
            double interval = cue == SoundCue.UiHover ? .12 : .065;
            if (instance.lastPlayed.TryGetValue(cue, out double previous) && now - previous < interval) return;
            instance.lastPlayed[cue] = now;
            instance.effects.PlayOneShot(instance.bank.Clip(cue), gain);
        }
    }
}
