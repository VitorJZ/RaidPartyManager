using System.Collections.Generic;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidPartyManager.Presentation
{
    internal enum VolumeChannel { Master, Ambience, Effects }

    internal sealed class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;
        private static bool volumePrefsDirty;
        private SoundBank bank;
        private AudioSource effects, atmosphere;
        private SoundCue environment;
        private AudioClip bossMusic;
        private float ambienceFade, ambienceGain;
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
            instance.ApplyVolumes();
            SceneManager.sceneLoaded += instance.SceneLoaded;
        }

        internal static int Volume(VolumeChannel channel) =>
            Mathf.Clamp(PlayerPrefs.GetInt("Audio.Volume." + channel, 100), 0, 100);

        internal static void SetVolume(VolumeChannel channel, int percent)
        {
            percent = Mathf.Clamp(percent, 0, 100);
            if (Volume(channel) == percent) return;
            PlayerPrefs.SetInt("Audio.Volume." + channel, percent);
            volumePrefsDirty = true;
            if (instance != null) instance.ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            float master = Volume(VolumeChannel.Master) / 100f;
            effects.volume = master * (Volume(VolumeChannel.Effects) / 100f);
            ambienceGain = master * (Volume(VolumeChannel.Ambience) / 100f);
            atmosphere.volume = ambienceFade * ambienceGain;
        }

        internal static void SaveVolumes()
        {
            if (!volumePrefsDirty) return;
            PlayerPrefs.Save();
            volumePrefsDirty = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveVolumes();
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

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) bossMusic = null;
            SetScene(scene.name, AdventureFlow.Active);
        }

        internal static void SetBossMusic(string bossId = null)
        {
            if (instance == null) return;
            instance.bossMusic = bossId == null ? null : Resources.Load<AudioClip>("BossMusic/" + bossId);
        }

        internal static void SetScene(string scene, AdventureRun run)
        {
            if (instance == null) return;
            bool capital = run?.InCapital == true || CapitalStories.Contains(run?.Current?.Event);
            bool cemetery = run?.InGraveyard == true || GraveStories.Contains(run?.Current?.Event);
            bool wheelEvent = scene == "DockShop" && run?.AtRecruitmentWheel == true;
            instance.environment = SceneCue(wheelEvent ? "EventSelect" : scene, capital, cemetery);
        }

        private static SoundCue SceneCue(string scene, bool capital, bool cemetery) => scene switch
        {
            "MainMenu" => SoundCue.AmbMainMenuMarch,
            "Camp" => SoundCue.AmbCampfire,
            "WhisperingGraveyard" => SoundCue.AmbGraveyardExterior,
            "ClockworkCapital" => SoundCue.AmbCapitalExterior,
            "EventSelect" or "BattlePrototype" => capital
                ? SoundCue.AmbCapitalExterior : cemetery ? SoundCue.AmbGraveyardExterior : SoundCue.AmbMistboundDocks,
            "Town" => capital ? SoundCue.AmbCapitalTown : cemetery ? SoundCue.AmbGraveyardTown : SoundCue.AmbDocksideTown,
            "TalentMarket" => capital ? SoundCue.AmbCapitalMarket : cemetery ? SoundCue.AmbGraveyardMarket : SoundCue.AmbDocksideTown,
            "DockShop" => capital ? SoundCue.AmbCapitalShop : cemetery ? SoundCue.AmbGraveyardShop : SoundCue.AmbShopInterior,
            _ => SoundCue.None
        };

        private void Update()
        {
            var next = bossMusic != null ? bossMusic : bank != null ? bank.Clip(environment) : null;
            if (atmosphere.clip != next)
            {
                ambienceFade = Mathf.MoveTowards(ambienceFade, 0, Time.unscaledDeltaTime * .9f);
                atmosphere.volume = ambienceFade * ambienceGain;
                if (ambienceFade > 0 && atmosphere.isPlaying) return;
                atmosphere.Stop();
                ambienceFade = 0;
                atmosphere.clip = next;
                if (next != null) atmosphere.Play();
            }
            if (next != null)
                ambienceFade = Mathf.MoveTowards(ambienceFade, bossMusic != null ? .34f : .44f, Time.unscaledDeltaTime * .65f);
            atmosphere.volume = ambienceFade * ambienceGain;
        }

        internal static void Play(SoundCue cue, float gain = .64f)
        {
            if (instance == null || instance.bank == null || cue == SoundCue.None) return;
            var clip = instance.bank.Clip(cue);
            if (clip == null) return;
            double now = Time.unscaledTimeAsDouble;
            // Simultaneous hits, group statuses and overlapping UI events share one transient.
            double interval = cue == SoundCue.UiHover ? .12 : .065;
            if (instance.lastPlayed.TryGetValue(cue, out double previous) && now - previous < interval) return;
            instance.lastPlayed[cue] = now;
            instance.effects.PlayOneShot(clip, gain);
        }
    }
}
