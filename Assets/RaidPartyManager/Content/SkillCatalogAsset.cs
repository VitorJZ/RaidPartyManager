using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    public enum SkillRequirement { None, Mage, Support, Archer, Melee, Warrior }

    [Serializable]
    public sealed class SkillEntry
    {
        [SerializeField, Min(1)] private int number;
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D icon;
        [SerializeField] private SkillRequirement requirement;
        [SerializeField, Min(1)] private int cost;
        [SerializeField, TextArea] private string description;

        public int Number => number;
        public string DisplayName => displayName;
        public Texture2D Icon => icon;
        public SkillRequirement Requirement => requirement;
        public int Cost => cost;
        public string Description => description;

        public bool CanLearn(CharacterDefinition character) => requirement switch
        {
            SkillRequirement.None => true,
            SkillRequirement.Mage => character.Profession == Profession.Mage,
            SkillRequirement.Support => character.Profession == Profession.Support,
            SkillRequirement.Archer => character.Profession == Profession.Archer,
            SkillRequirement.Melee => character.Reach == AttackReach.Melee,
            SkillRequirement.Warrior => character.Profession == Profession.Warrior,
            _ => false
        };
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Skill Catalog")]
    public sealed class SkillCatalogAsset : ScriptableObject
    {
        [SerializeField] private SkillEntry[] skills = Array.Empty<SkillEntry>();

        public IReadOnlyList<SkillEntry> Skills => skills;
    }
}
