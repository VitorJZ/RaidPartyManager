using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidPartyManager.Tests
{
    public sealed class SoundBankTests
    {
        private const string AudioFolder = "Assets/RaidPartyManager/Audio/";
        private const int LegacyCues = 91;

        [Serializable]
        private sealed class Manifest
        {
            public Recording[] effects = Array.Empty<Recording>();
        }

        [Serializable]
        private sealed class Recording
        {
            public string filename = "";
            public float duration_seconds = 0;
            public bool loop = false;
        }

        [Test]
        public void ImportedClipsMatchManifestAndKeepLegacyBankSlots()
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Docs", "ElevenLabsSoundEffects.json");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            var bank = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AudioFolder + "Resources/MistboundSounds.asset");
            Assert.That(bank, Is.Not.Null);
            using var serializedBank = new SerializedObject(bank);
            var clips = serializedBank.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(LegacyCues + manifest.effects.Length));
            Assert.That(clips.GetArrayElementAtIndex(0).objectReferenceValue, Is.Null);
            for (int i = 1; i < LegacyCues; i++)
                Assert.That(clips.GetArrayElementAtIndex(i).objectReferenceValue, Is.TypeOf<AudioClip>(), $"Legacy slot {i}");

            for (int i = 0; i < manifest.effects.Length; i++)
            {
                var recording = manifest.effects[i];
                string path = AudioFolder + "ElevenLabs/" + recording.filename;
                var clip = clips.GetArrayElementAtIndex(LegacyCues + i).objectReferenceValue as AudioClip;
                Assert.That(clip, Is.Not.Null, path);
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(path));
                Assert.That(clip.length, Is.EqualTo(recording.duration_seconds)
                    .Within(Math.Max(.10f, recording.duration_seconds * .02f)), path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(recording.loop
                    ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad), path);
            }
        }
    }
}
