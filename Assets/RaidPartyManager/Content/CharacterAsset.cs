using System;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [Serializable]
    internal sealed class DamageComponentSpec
    {
        [SerializeField] private DamageType type = DamageType.Default;
        [SerializeField, Min(0)] private int power;
        [SerializeField] private bool scalesWithAttack = true;

        public DamageComponent ToDefinition() => new DamageComponent(type, power, scalesWithAttack);
    }

    [Serializable]
    internal sealed class ActionSpec
    {
        [SerializeField] private string id = "basic";
        [SerializeField] private string displayName = "普攻";
        [SerializeField] private ActionKind kind;
        [SerializeField] private TargetRule target = TargetRule.FrontEnemy;
        [SerializeField, Min(0)] private int power;
        [SerializeField, Min(0)] private int cooldown;
        [SerializeField] private DamageType damageType = DamageType.Default;
        [SerializeField] private bool healingScalesWithAttack;
        [SerializeField] private DamageComponentSpec[] additionalDamage = Array.Empty<DamageComponentSpec>();

        public ActionDefinition ToDefinition() =>
            new ActionDefinition(id, displayName, kind, target, power, cooldown, damageType,
                healingScalesWithAttack, additionalDamage.Select(component => component.ToDefinition()));
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Character")]
    public sealed class CharacterAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D portrait;
        [SerializeField] private Profession profession;
        [SerializeField, Min(0)] private int rosterNumber;
        [SerializeField, Min(1)] private int stage = 1;
        [SerializeField] private AttackReach reach;
        [SerializeField] private CharacterTrait fixedTrait;
        [SerializeField, Min(0)] private int traitPower;
        [SerializeField, TextArea] private string traitText;
        [SerializeField] private CharacterAsset battleForm;
        [SerializeField] private CharacterAsset[] evolutions = Array.Empty<CharacterAsset>();
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
        internal string Id => id;
        internal System.Collections.Generic.IEnumerable<CharacterAsset> RelatedForms =>
            evolutions.Append(battleForm).Where(form => form != null);

        public CharacterDefinition ToDefinition() =>
            new CharacterDefinition(id, displayName, profession, personality,
                new Stats(attack, defense, specialDefense, health), actionInterval,
                basicAttack.ToDefinition(), hasSkill ? skill.ToDefinition() : null,
                rosterNumber, stage, reach, fixedTrait, traitPower, traitText,
                battleForm != null ? battleForm.Id : null, evolutions.Where(form => form != null).Select(form => form.Id));
    }
}
