using System;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [Serializable]
    internal sealed class ActionSpec
    {
        [SerializeField] private string id = "basic";
        [SerializeField] private string displayName = "普攻";
        [SerializeField] private ActionKind kind;
        [SerializeField] private TargetRule target = TargetRule.FrontEnemy;
        [SerializeField, Min(0)] private int power;
        [SerializeField, Min(0)] private int cooldown;
        [SerializeField] private DamageType damageType;

        public ActionDefinition ToDefinition() =>
            new ActionDefinition(id, displayName, kind, target, power, cooldown, damageType);
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Character")]
    public sealed class CharacterAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D portrait;
        [SerializeField] private Profession profession;
        [SerializeField] private Personality personality;
        [SerializeField, Min(0)] private int attack = 5;
        [SerializeField, Min(0)] private int defense = 2;
        [SerializeField, Min(0)] private int specialDefense = 2;
        [SerializeField, Min(1)] private int health = 10;
        [SerializeField, Min(1)] private int actionInterval = 10;
        [SerializeField] private ActionSpec basicAttack = new ActionSpec();
        [SerializeField] private bool hasSkill;
        [SerializeField] private ActionSpec skill = new ActionSpec();

        internal Texture2D Portrait => portrait;

        public CharacterDefinition ToDefinition() =>
            new CharacterDefinition(id, displayName, profession, personality,
                new Stats(attack, defense, specialDefense, health), actionInterval,
                basicAttack.ToDefinition(), hasSkill ? skill.ToDefinition() : null);
    }
}
