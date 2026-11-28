using System.Collections.Generic;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidPartyManager.Presentation
{
    internal enum VolumeChannel { Master, Ambience, Effects }

    internal sealed class GameAudio : MonoBehaviour
    {
        private enum MusicRegion { Dock, Graveyard, Capital }
        private enum MusicSetting { Exploration, Battle, Town, Shop, TalentMarket, Camp }

        private static GameAudio instance;
        private static bool volumePrefsDirty;
        private SoundBank bank;
        private AudioSource effects, atmosphere;
        private SoundCue environment;
        private MusicRegion region;
        private MusicSetting? sceneSetting;
        private AudioClip sceneMusic, battleMusic;
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
            if (mode == LoadSceneMode.Single) battleMusic = null;
            SetScene(scene.name, AdventureFlow.ForScene(scene.name));
        }

        internal static void BeginBattleMusic(string bossId)
        {
            if (instance == null) return;
            var boss = bossId == null ? null : Resources.Load<AudioClip>("BossMusic/" + bossId);
            instance.battleMusic = boss != null ? boss : LoadRegionalMusic(instance.region, MusicSetting.Battle);
        }

        internal static void EndBattleMusic()
        {
            if (instance != null) instance.battleMusic = null;
        }

        internal static void SetScene(string scene, AdventureRun run)
        {
            if (instance == null) return;
            bool capital = run?.InCapital == true || CapitalStories.Contains(run?.Current?.Event);
            bool cemetery = run?.InGraveyard == true || GraveStories.Contains(run?.Current?.Event);
            var region = scene switch
            {
                "WhisperingGraveyard" => MusicRegion.Graveyard,
                "ClockworkCapital" => MusicRegion.Capital,
                _ => capital ? MusicRegion.Capital : cemetery ? MusicRegion.Graveyard : MusicRegion.Dock
            };
            if (scene == "DockShop" && run?.AtRecruitmentWheel == true) scene = "EventSelect";
            var setting = SettingFor(scene);
            if (instance.region != region || instance.sceneSetting != setting)
            {
                instance.region = region;
                instance.sceneSetting = setting;
                instance.sceneMusic = setting.HasValue ? LoadRegionalMusic(region, setting.Value) : null;
            }
            instance.environment = SceneCue(scene, region == MusicRegion.Capital, region == MusicRegion.Graveyard);
        }

        private static MusicSetting? SettingFor(string scene) => scene switch
        {
            "EventSelect" or "BattlePrototype" or "WhisperingGraveyard" or "ClockworkCapital" => MusicSetting.Exploration,
            "Town" => MusicSetting.Town,
            "DockShop" => MusicSetting.Shop,
            "TalentMarket" => MusicSetting.TalentMarket,
            "Camp" => MusicSetting.Camp,
            _ => null
        };

        private static AudioClip LoadRegionalMusic(MusicRegion region, MusicSetting setting) =>
            Resources.Load<AudioClip>($"RegionalMusic/{region}-{setting}");

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
            var next = battleMusic != null ? battleMusic : sceneMusic;
            if (next == null && bank != null) next = bank.Clip(environment);
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
                ambienceFade = Mathf.MoveTowards(ambienceFade, battleMusic != null ? .34f : .44f, Time.unscaledDeltaTime * .65f);
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
