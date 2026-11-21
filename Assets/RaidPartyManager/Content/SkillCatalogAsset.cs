using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [Serializable]
    public sealed class SkillEntry
    {
        [SerializeField, Min(1)] private int number;
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D icon;
        [SerializeField] private Texture2D battleEffect;
        [SerializeField] private Texture2D projectile;
        [SerializeField] private Texture2D projectileFragments;
        [SerializeField] private SkillRequirement requirement;
        [SerializeField, Min(1)] private int cost;
        [SerializeField] private SkillEffect skillEffect;
        [SerializeField] private string rewardBossId;
        [SerializeField, Range(0, 3)] private int tier = 2;
        [SerializeField, TextArea] private string description;

        public int Number => number;
        public string DisplayName => displayName;
        public Texture2D Icon => icon;
        public Texture2D BattleEffect => battleEffect;
        public Texture2D Projectile => projectile;
        public Texture2D ProjectileFragments => projectileFragments;
        public SkillRequirement Requirement => requirement;
        public int Cost => cost;
        public string Description => description;

        public SkillBookDefinition ToBook() => new SkillBookDefinition($"skill-book-{number:00}", displayName,
            description, requirement, cost, tier, skillEffect, rewardBossId);

        public bool CanLearn(CharacterDefinition character) => ToBook().CanLearn(character);
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Skill Catalog")]
    public sealed class SkillCatalogAsset : ScriptableObject
    {
        [SerializeField] private SkillEntry[] skills = Array.Empty<SkillEntry>();

        public IReadOnlyList<SkillEntry> Skills => skills;
    }
}
