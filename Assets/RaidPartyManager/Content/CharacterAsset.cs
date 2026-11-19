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
        [SerializeField] private bool usePortraitInBattle;
        [SerializeField] private Texture2D campSeatedSprite;
        [Tooltip("Eight columns. Rows contain 8 idle, 8 action, 4 hit and 8 death poses.")]
        [SerializeField] private Texture2D battleSpriteSheet;
        [Tooltip("Optional full-width attack poses in four columns and two rows.")]
        [SerializeField] private Texture2D battleAttackSheet;
        [Tooltip("Six walking frames in three columns and two rows.")]
        [SerializeField] private Texture2D battleWalkSheet;
        [SerializeField] private Texture2D battleSpecialSheet;
        [SerializeField] private Texture2D battleSpecialEffect;
        [SerializeField] private Texture2D battleFinaleEffect;
        [SerializeField] private Texture2D battleStrikeEffect;
        [SerializeField] private Texture2D battleProjectile;
        [SerializeField] private Profession profession;
        [SerializeField] private EnemyRank enemyRank;
        [SerializeField, Range(1, Rules.Positions)] private int positionSlots = 1;
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
        internal bool UsePortraitInBattle => usePortraitInBattle;
        internal Texture2D CampSeatedSprite => campSeatedSprite;
        internal Texture2D BattleSpriteSheet => battleSpriteSheet;
        internal Texture2D BattleAttackSheet => battleAttackSheet;
        internal Texture2D BattleWalkSheet => battleWalkSheet;
        internal (Texture2D Body, Texture2D Effect, Texture2D Finale, Texture2D Strike) BattleExtras =>
            (battleSpecialSheet, battleSpecialEffect, battleFinaleEffect, battleStrikeEffect);
        internal Texture2D BattleProjectile => battleProjectile;
        internal string Id => id;
        internal System.Collections.Generic.IEnumerable<CharacterAsset> RelatedForms =>
            evolutions.Append(battleForm).Where(form => form != null);

        public CharacterDefinition ToDefinition() =>
            new CharacterDefinition(id, displayName, profession, personality,
                new Stats(attack, defense, specialDefense, health), actionInterval,
                basicAttack.ToDefinition(), hasSkill ? skill.ToDefinition() : null,
                rosterNumber, stage, reach, fixedTrait, traitPower, traitText,
                battleForm != null ? battleForm.Id : null, evolutions.Where(form => form != null).Select(form => form.Id),
                enemyRank, Math.Clamp(positionSlots, 1, Rules.Positions));
    }
}
