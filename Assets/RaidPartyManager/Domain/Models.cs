using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Domain
{
    public enum Profession { Warrior, Mage, Archer, Support, Monster }
    public enum EnemyRank { Unspecified, Normal, Elite, Boss }
    public enum AttackReach { Unspecified, Melee, Ranged }
    public enum CharacterTrait
    {
        None, DamageLimit, GainEmberWhenHit, ForestMarkOnAttack, ArcherVolley, PoisonOnHit,
        MissingHealthDamage, EscalatingAttack, PermanentAttackOnKill, IgnoreMagicDefense, LightningChant,
        Rebirth, GeneratePotion, PoisonOpponents, EvolveAlly, CopyAdvancedTrait, ChorusArmory, RevealEnemies,
        PiercingArrow, BorrowAllyAttack, HealAlly, FirstDamageToTarget, HealOnDamage, DrawAttacks,
        ExposeMeleeAttacker, GoldOnSurvival, AccessoryOnSurvival, ShopDiscount,
        EnemyWarCry, EnemyCeramicPierce, EnemyBell, EnemyRearAmbush, EnemyResolve, EnemyCrossbow, EnemyQuack,
        EnemyTideFollower, EnemyWreckScavenger, EnemyDeckSailor,
        EnemyMudEel, EnemySaltCrab, EnemyLanternMoth, EnemyFogSignalGunner, EnemyShipCaulker,
        EnemyAnchorHermit, EnemyFogAngler, EnemyMooringWarden, EnemyBlockadeAdmiral, EnemyDrownedLanternRay, SoakEnemyEachRound,
        DoubleShot, GravePollen, RottenBloom
    }
    public enum Personality
    {
        Frank, Deadpan, Optimist, Worrier, Competitive, Caring, Frugal, Stoic, Curious, Theatrical
    }
    public enum Side { Party, Enemy }
    public enum ActionKind { Attack, Heal, Shield }
    public enum DamageType { Physical, Magic, Fire, Lightning, True, Default, Fixed }
    public enum TargetRule { AnyEnemy, FrontEnemy, RearEnemy, WoundedAlly, Self }
    public enum BattleOutcome { Victory, PartyDefeated, MutualDefeat, Timeout }
    public enum DecisionReason { Default, HighestDamage = 1, SelfProtection = 2, WoundedAlly = 4, UseSkill = 10 }
    public enum BattleEventKind { Action, Defeated, Finished, Effect, FormChanged, SkillVisual, Retreated, EnemyVisual, RoundStarted, Countdown, EquipmentVisual }
    public enum BattleCountdownKind { CalamityChant, ScavengerEscape, AdmiralBroadside, GoldenBell, EnemyCharge, StoneGuard }

    public readonly struct BattleCountdown
    {
        public BattleCountdownKind Kind { get; }
        public int Remaining { get; }

        public BattleCountdown(BattleCountdownKind kind, int remaining)
        {
            Kind = kind;
            Remaining = remaining;
        }
    }

    public enum BattleVisualCue
    {
        None, WarCry, CeramicPierce, CeramicSplash, BellRing, BellWave, RearAmbush,
        ResolveGuard, CrossbowCritical, QuackHeal, QuackStrike,
        DockWarCry, TideMark, ScavengerSteal, EelFrost, SaltShell, MothAsh, CaulkerMend,
        AnchorWard, AnchorExpose, AnchorShatter, AnglerCurse, WardenBrace,
        AdmiralWhistle, AdmiralWarning, AdmiralBroadside, RayTail, RayMist, RaySurge,
        RoundEndEmber, RoundEndHeal, RoundEndPoison, RoundEndPoisonHeal, RoundEndGrowth,
        RoundEndLightning, RoundEndFormShift, RoundEndTransfer,
        RoundStartExposed, RoundStartPoison, RoundStartBlessing, RoundStartBonus, PiercingArrow,
        GraveyardSpecial, GraveyardCharge, GraveyardPhase, SkillFireImpact, SkillLightningImpact
    }

    public static class Rules
    {
        public const int Positions = 4;
        public const int EquipmentSlots = 3;
        public static int PersonalEquipmentSlots(PlayerTrait talent, bool extraSlot = false) =>
            EquipmentSlots + (talent == PlayerTrait.Warehouse ? 1 : 0) + (extraSlot ? 1 : 0);
        public static bool ValidEquipmentSlots(int count, PlayerTrait talent, bool extraSlot = false) =>
            count == PersonalEquipmentSlots(talent, extraSlot) || count == PersonalEquipmentSlots(talent, extraSlot) + 1;
        public const int DefaultEnergy = 2;
        public const int MaximumEnergy = 8;

        public static bool IsPosition(int position) => position >= 1 && position <= Positions;
        public static bool Fits(int position, int slots) => IsPosition(position) && slots >= 1 && slots <= Positions - position + 1;
        public static bool Overlaps(int start, int slots, int otherStart, int otherSlots) =>
            start < otherStart + otherSlots && otherStart < start + slots;
    }

    public readonly struct BattleEnergy
    {
        public int Capacity { get; }
        public int Remaining { get; }

        public BattleEnergy(int capacity, int remaining)
        {
            Capacity = Math.Clamp(capacity, Rules.DefaultEnergy, Rules.MaximumEnergy);
            Remaining = Math.Clamp(remaining, 0, Capacity);
        }

        public static BattleEnergy Full(IEnumerable<EquipmentDefinition> equipment, int bonusEnergy = 0)
        {
            int capacity = Rules.DefaultEnergy + bonusEnergy + 2 * equipment.Count(item => item.Effect == EquipmentEffect.EchoChalice);
            return new BattleEnergy(capacity, capacity);
        }
    }

    public readonly struct Stats
    {
        public int Attack { get; }
        public int Defense { get; }
        public int SpecialDefense { get; }
        public int Health { get; }

        public Stats(int attack, int defense, int specialDefense, int health)
        {
            Attack = attack;
            Defense = defense;
            SpecialDefense = specialDefense;
            Health = health;
        }

        public static Stats operator +(Stats first, Stats second) => new Stats(
            first.Attack + second.Attack, first.Defense + second.Defense,
            first.SpecialDefense + second.SpecialDefense, first.Health + second.Health);
    }

    public readonly struct DamageComponent
    {
        public DamageType Type { get; }
        public int Power { get; }
        public bool ScalesWithAttack { get; }

        public DamageComponent(DamageType type, int power = 0, bool scalesWithAttack = true)
        {
            if (power < 0)
                throw new ArgumentOutOfRangeException(nameof(power));
            Type = type;
            Power = power;
            ScalesWithAttack = scalesWithAttack;
        }
    }

    public sealed class ActionDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public ActionKind Kind { get; }
        public TargetRule Target { get; }
        public int Power { get; }
        public int Cooldown { get; }
        public DamageType DamageType { get; }
        public bool HealingScalesWithAttack { get; }
        public IReadOnlyList<DamageComponent> AdditionalDamage { get; }

        public ActionDefinition(string id, string name, ActionKind kind, TargetRule target, int power,
            int cooldown = 0, DamageType damageType = DamageType.Default, bool healingScalesWithAttack = false,
            IEnumerable<DamageComponent> additionalDamage = null)
        {
            if (string.IsNullOrWhiteSpace(id) || power < 0 || cooldown < 0)
                throw new ArgumentException("An action needs an ID and nonnegative power and cooldown.");
            bool validTarget = kind == ActionKind.Attack
                ? target == TargetRule.AnyEnemy || target == TargetRule.FrontEnemy || target == TargetRule.RearEnemy
                : kind == ActionKind.Heal ? target == TargetRule.WoundedAlly || target == TargetRule.Self
                : kind == ActionKind.Shield && target == TargetRule.Self;
            if (!validTarget)
                throw new ArgumentException("Target rule does not match the action kind.", nameof(target));
            Id = id;
            Name = name;
            Kind = kind;
            Target = target;
            Power = power;
            Cooldown = cooldown;
            DamageType = damageType;
            HealingScalesWithAttack = healingScalesWithAttack;
            AdditionalDamage = Array.AsReadOnly(additionalDamage?.ToArray() ?? Array.Empty<DamageComponent>());
        }
    }

    public sealed class CharacterDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Profession Profession { get; }
        public EnemyRank Rank { get; }
        public int PositionSlots { get; }
        public Personality Personality { get; }
        public Stats BaseStats { get; }
        public int ActionInterval { get; }
        public ActionDefinition BasicAttack { get; }
        public ActionDefinition Skill { get; }
        public int RosterNumber { get; }
        public int Stage { get; }
        public AttackReach Reach { get; }
        public CharacterTrait Trait { get; }
        public int TraitPower { get; }
        public string TraitText { get; }
        public string BattleFormId { get; }
        public IReadOnlyList<string> EvolutionIds { get; }
        public EnemyBehaviorDefinition EnemyBehavior { get; }

        public CharacterDefinition WithName(string name) => new(Id, name, Profession, Personality, BaseStats, ActionInterval, BasicAttack, Skill, RosterNumber, Stage, Reach, Trait, TraitPower, TraitText, BattleFormId, EvolutionIds, Rank, PositionSlots, EnemyBehavior);
        public CharacterDefinition WithPersonality(Personality personality) => personality == Personality ? this : new(Id, Name, Profession, personality, BaseStats, ActionInterval, BasicAttack, Skill, RosterNumber, Stage, Reach, Trait, TraitPower, TraitText, BattleFormId, EvolutionIds, Rank, PositionSlots, EnemyBehavior);

        public CharacterDefinition(string id, string name, Profession profession, Personality personality,
            Stats baseStats, int actionInterval, ActionDefinition basicAttack, ActionDefinition skill = null,
            int rosterNumber = 0, int stage = 1, AttackReach reach = AttackReach.Unspecified,
            CharacterTrait trait = CharacterTrait.None, int traitPower = 0, string traitText = null,
            string battleFormId = null, IEnumerable<string> evolutionIds = null, EnemyRank rank = EnemyRank.Unspecified,
            int positionSlots = 1, EnemyBehaviorDefinition enemyBehavior = null)
        {
            if (string.IsNullOrWhiteSpace(id) || baseStats.Health <= 0 || actionInterval <= 0 || !Rules.Fits(1, positionSlots))
                throw new ArgumentException("A character needs an ID, positive health and an action interval.");
            if (basicAttack == null || basicAttack.Kind != ActionKind.Attack || basicAttack.Cooldown != 0)
                throw new ArgumentException("A basic attack must be an attack with no cooldown.", nameof(basicAttack));
            if (skill != null && (skill.Cooldown == 0 || skill.Id == basicAttack.Id))
                throw new ArgumentException("A skill needs a cooldown and its own ID.", nameof(skill));
            Id = id;
            Name = name;
            Profession = profession;
            Rank = rank;
            PositionSlots = positionSlots;
            Personality = personality;
            BaseStats = baseStats;
            ActionInterval = actionInterval;
            BasicAttack = basicAttack;
            Skill = skill;
            RosterNumber = rosterNumber;
            Stage = stage;
            Reach = reach;
            Trait = trait;
            TraitPower = traitPower;
            TraitText = traitText;
            BattleFormId = battleFormId;
            EvolutionIds = Array.AsReadOnly(evolutionIds?.ToArray() ?? Array.Empty<string>());
            EnemyBehavior = enemyBehavior;
        }
    }

    public class EquipmentDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Stats Bonus { get; }
        public string Description { get; }
        public EquipmentEffect Effect { get; }
        public int Tier { get; }
        public int Price { get; }
        public bool CanDropInBattle { get; }
        public string RewardBossId { get; }
        public bool BossExclusive => !string.IsNullOrEmpty(RewardBossId);
        public bool AvailableInRegularPool => !BossExclusive && Effect is not (EquipmentEffect.CoinDevourer or EquipmentEffect.StrangeSeed);
        public bool IsPotion => Effect is EquipmentEffect.VigorPotion or EquipmentEffect.SwiftPotion or
            EquipmentEffect.ResolvePotion or EquipmentEffect.GreedPotion or EquipmentEffect.ThornsPotion or
            EquipmentEffect.PurityPotion;

        public EquipmentDefinition(string id, string name, Stats bonus, string description = null,
            EquipmentEffect effect = EquipmentEffect.None, int tier = 2, int price = 20, bool canDropInBattle = true, string rewardBossId = null)
        {
            if (string.IsNullOrWhiteSpace(id) || bonus.Attack < 0 || bonus.Defense < 0 ||
                bonus.SpecialDefense < 0 || bonus.Health < 0)
                throw new ArgumentException("Prototype equipment needs an ID and nonnegative bonuses.");
            Id = id;
            Name = name;
            Bonus = bonus;
            Description = description;
            Effect = effect;
            Tier = tier;
            Price = price;
            RewardBossId = rewardBossId;
            CanDropInBattle = canDropInBattle && AvailableInRegularPool && tier is >= 0 and <= 3;
        }
    }

    public enum SkillRequirement { None, Mage, Support, Archer, Melee, Warrior }
    public enum SkillEffect
    {
        None, WarCry, BlazingFire, AbsoluteZero, Sunshine, StoneGuard, HealingHands, SweepNextAttack,
        LightningWhip, ToxicMist, FleshFeast, Sidestep, GoldenBell, FieryEyes, CrimsonReap,
        HeroicAnthem, Intimidate, Earthquake, CloudPiercer,
        HookArrow, TwinCover, FieldMedic, ColdTide, BronzeMirror, NightWatch, SiegeBolt, Intercept, BackPay, ThunderVerdict,
        BlockadeSalvo, DeepwaterSurge, BurialEdict, LongNightPrayer, ExpressClearance, MeridianTransit, AnnealingOrder,
        VenomSurge
    }

    public sealed class SkillBookDefinition : EquipmentDefinition
    {
        public SkillRequirement Requirement { get; }
        public int Cost { get; }
        public SkillEffect SkillEffect { get; }
        public bool SpendsAllEnergy => SkillEffect == SkillEffect.VenomSurge;

        public int EnergyCost(int remaining) => SpendsAllEnergy ? Math.Max(Cost, remaining) : Cost;

        public SkillBookDefinition(string id, string name, string description, SkillRequirement requirement, int cost, int tier,
            SkillEffect skillEffect = SkillEffect.None, string rewardBossId = null)
            : base(id, name, default, description, tier: tier, price: !string.IsNullOrEmpty(rewardBossId) ? 100 : tier switch { 0 => 50, 1 => 35, 2 => 25, _ => 15 }, rewardBossId: rewardBossId)
        {
            if (cost < 1 || cost > Rules.MaximumEnergy)
                throw new ArgumentOutOfRangeException(nameof(cost));
            Requirement = requirement;
            Cost = cost;
            SkillEffect = skillEffect;
        }

        public bool CanLearn(CharacterDefinition character) => Requirement switch
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

    public sealed class Combatant
    {
        public PlayerTrait Talent { get; }
        public string Id { get; }
        public CharacterDefinition Character { get; }
        public Side Side { get; }
        public int Position { get; }
        public Stats Stats { get; }
        public int Health { get; }
        // Equipment follows the owner’s slot order, with empty slots omitted.
        public IReadOnlyList<EquipmentDefinition> Equipment { get; }
        public IReadOnlyList<string> EquipmentSlots { get; }
        public IReadOnlyDictionary<StatusKind, int> StartingStatuses { get; }
        public IReadOnlyList<SkillBookDefinition> LearnedSkills { get; }
        public int BonusEnergy { get; }
        public bool ExtraEquipmentSlot { get; }

        public Combatant(string id, CharacterDefinition character, Side side, int position, Stats stats, int health,
            IEnumerable<EquipmentDefinition> equipment = null,
            IReadOnlyDictionary<StatusKind, int> startingStatuses = null, IEnumerable<string> equipmentSlots = null,
            IEnumerable<SkillBookDefinition> learnedSkills = null, PlayerTrait talent = PlayerTrait.None, int bonusEnergy = 0, bool extraEquipmentSlot = false)
        {
            BonusEnergy = bonusEnergy;
            ExtraEquipmentSlot = extraEquipmentSlot;
            Talent = talent;
            Character = character ?? throw new ArgumentNullException(nameof(character));
            if (string.IsNullOrWhiteSpace(id) || !Rules.Fits(position, Character.PositionSlots) ||
                stats.Health <= 0 || health < 0 || health > stats.Health)
                throw new ArgumentException("Invalid combatant ID, position or health.");
            Id = id;
            Side = side;
            Position = position;
            Stats = stats;
            Health = health;
            Equipment = Array.AsReadOnly(equipment?.ToArray() ?? Array.Empty<EquipmentDefinition>());
            EquipmentSlots = Array.AsReadOnly(equipmentSlots?.ToArray() ?? Array.Empty<string>());
            LearnedSkills = Array.AsReadOnly(learnedSkills?.Where(skill => skill != null).ToArray() ?? Array.Empty<SkillBookDefinition>());
            if (EquipmentSlots.Count != 0 && (!Rules.ValidEquipmentSlots(EquipmentSlots.Count, talent, extraEquipmentSlot) ||
                EquipmentSlots.Count(slot => slot != null) != Equipment.Count))
                throw new ArgumentException("Combat equipment slots must match the supplied equipped instances.", nameof(equipmentSlots));
            var initial = startingStatuses?.Where(status => status.Value > 0).ToDictionary(status => status.Key, status => status.Value)
                ?? new Dictionary<StatusKind, int>();
            if (initial.ContainsKey(StatusKind.Wet) && initial.ContainsKey(StatusKind.Chill))
            {
                initial.Remove(StatusKind.Wet);
                initial.Remove(StatusKind.Chill);
                initial[StatusKind.Frozen] = 1;
            }
            StartingStatuses = new System.Collections.ObjectModel.ReadOnlyDictionary<StatusKind, int>(initial);
        }
    }

    public sealed class BattleRequest
    {
        public string Id { get; }
        public IReadOnlyList<Combatant> Combatants { get; }
        public int TickLimit { get; }
        public IReadOnlyList<CharacterDefinition> RecruitmentPool { get; }
        public IReadOnlyDictionary<string, CharacterDefinition> CharacterCatalog { get; }
        public int PartyGold { get; }
        public int RecruitLimit { get; }
        public int ReserveArcherCount { get; }
        public int ReserveAccessoryHunterCount { get; }
        public bool HardFight { get; }
        public bool CapitalField { get; }

        public string PallbearerId { get; }
        public int GraveBell { get; }

        public BattleRequest(string id, IEnumerable<Combatant> combatants, int tickLimit,
            IEnumerable<CharacterDefinition> recruitmentPool = null, IEnumerable<CharacterDefinition> characterCatalog = null,
            int partyGold = 0, int recruitLimit = Rules.Positions, bool hardFight = false, string pallbearerId = null, int graveBell = 0,
            bool capitalField = false, int reserveArcherCount = 0, int reserveAccessoryHunterCount = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || tickLimit <= 0 || partyGold < 0)
                throw new ArgumentException("A battle needs an ID and a positive time limit.");
            var units = combatants.ToArray();
            var positions = units.SelectMany(unit => Enumerable.Range(unit.Position, unit.Character.PositionSlots)
                .Select(position => (unit.Side, position))).ToArray();
            if (units.Select(unit => unit.Id).Distinct(StringComparer.Ordinal).Count() != units.Length ||
                positions.Distinct().Count() != positions.Length ||
                !units.Any(unit => unit.Side == Side.Party && unit.Health > 0) ||
                !units.Any(unit => unit.Side == Side.Enemy && unit.Health > 0))
                throw new ArgumentException("Both sides need a living unit, with unique IDs and positions.", nameof(combatants));
            Id = id;
            Combatants = Array.AsReadOnly(units);
            TickLimit = tickLimit;
            PartyGold = partyGold;
            RecruitLimit = recruitLimit;
            if (reserveArcherCount < 0)
                throw new ArgumentOutOfRangeException(nameof(reserveArcherCount));
            ReserveArcherCount = reserveArcherCount;
            if (reserveAccessoryHunterCount < 0 || reserveAccessoryHunterCount > int.MaxValue - Rules.Positions)
                throw new ArgumentOutOfRangeException(nameof(reserveAccessoryHunterCount));
            ReserveAccessoryHunterCount = reserveAccessoryHunterCount;
            PallbearerId = pallbearerId;
            GraveBell = graveBell;
            CapitalField = capitalField;
            HardFight = hardFight || units.Any(u => u.Side == Side.Enemy && u.Character.Rank is EnemyRank.Elite or EnemyRank.Boss);
            RecruitmentPool = Array.AsReadOnly(recruitmentPool?.ToArray() ?? Array.Empty<CharacterDefinition>());
            CharacterCatalog = new System.Collections.ObjectModel.ReadOnlyDictionary<string, CharacterDefinition>(
                (characterCatalog ?? Enumerable.Empty<CharacterDefinition>()).Concat(RecruitmentPool)
                    .Concat(units.Select(unit => unit.Character)).GroupBy(character => character.Id)
                    .ToDictionary(group => group.Key, group => group.First()));
        }
    }

    public sealed class BattleLoadout
    {
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Slots { get; }
        public IReadOnlyList<string> Consumed { get; }
        public IReadOnlyList<string> Stolen { get; }
        public IReadOnlyList<string> Disabled { get; }

        public BattleLoadout(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> slots, IEnumerable<string> consumed,
            IEnumerable<string> stolen = null, IEnumerable<string> disabled = null)
        {
            Slots = new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(
                slots.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)Array.AsReadOnly(entry.Value.ToArray())));
            Consumed = Array.AsReadOnly(consumed.ToArray());
            Stolen = Array.AsReadOnly(stolen?.ToArray() ?? Array.Empty<string>());
            Disabled = Array.AsReadOnly(disabled?.ToArray() ?? Array.Empty<string>());
        }
    }

    // Action and equipment events report target state after resolution. Defeat follows the action or effect that caused it.
    public readonly struct BattleEvent
    {
        public int Tick { get; }
        public BattleEventKind Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ActionName { get; }
        public ActionKind ActionKind { get; }
        public DecisionReason Reason { get; }
        public int Amount { get; }
        public int TargetHealth { get; }
        public int TargetShield { get; }
        public string TargetStatuses { get; }
        public string Detail { get; }
        public CharacterDefinition Form { get; }
        public int MaximumHealth { get; }
        public IReadOnlyList<StatusStack> StatusStacks { get; }
        public BattleLoadout Loadout { get; }
        public IReadOnlyList<KeyValuePair<string, Stats>> PartyStatChanges { get; }
        public AttackReach AttackReach { get; }
        public BattleVisualCue VisualCue { get; }
        public BattleEnergy? ActorEnergy { get; }
        public BattleEnergy? TargetEnergy { get; }
        public string SkillBookId { get; }
        public CharacterDefinition CopiedTraitSource { get; }
        private readonly DamageBreakdown damageTaken;
        public IReadOnlyList<(DamageType Type, int Amount)> DamageTaken => damageTaken.Parts;
        public bool PoisonDamage { get; }
        public bool CharmTriggered { get; }
        public bool Intercepted { get; }
        public EquipmentEffect Equipment { get; }
        public int ShieldAbsorbed { get; }
        public BattleCountdown? Countdown { get; }

        public BattleEvent(int tick, BattleEventKind kind, string actorId = null, string targetId = null,
            string actionName = null, ActionKind actionKind = ActionKind.Attack,
            DecisionReason reason = DecisionReason.Default, int amount = 0, int targetHealth = 0, int targetShield = 0,
            string targetStatuses = null, string detail = null, CharacterDefinition form = null, int maximumHealth = 0,
            IReadOnlyList<StatusStack> statusStacks = null, BattleLoadout loadout = null,
            IReadOnlyList<KeyValuePair<string, Stats>> partyStatChanges = null, AttackReach attackReach = AttackReach.Unspecified,
            BattleVisualCue visualCue = BattleVisualCue.None, BattleEnergy? actorEnergy = null, string skillBookId = null,
            BattleEnergy? targetEnergy = null, IReadOnlyList<(DamageType Type, int Amount)> damageTaken = null,
            bool poisonDamage = false, BattleCountdown? countdown = null, int shieldAbsorbed = 0, CharacterDefinition copiedTraitSource = null,
            bool charmTriggered = false, EquipmentEffect equipment = EquipmentEffect.None, bool intercepted = false)
        {
            Tick = tick;
            Kind = kind;
            ActorId = actorId;
            TargetId = targetId;
            ActionName = actionName;
            ActionKind = actionKind;
            Reason = reason;
            Amount = amount;
            TargetHealth = targetHealth;
            TargetShield = targetShield;
            TargetStatuses = targetStatuses;
            Detail = detail;
            Form = form;
            MaximumHealth = maximumHealth;
            StatusStacks = statusStacks;
            Loadout = loadout;
            PartyStatChanges = partyStatChanges;
            AttackReach = attackReach;
            VisualCue = visualCue;
            ActorEnergy = actorEnergy;
            SkillBookId = skillBookId;
            TargetEnergy = targetEnergy;
            this.damageTaken = new DamageBreakdown(damageTaken);
            PoisonDamage = poisonDamage;
            ShieldAbsorbed = shieldAbsorbed;
            CharmTriggered = charmTriggered;
            Intercepted = intercepted;
            Equipment = equipment;
            Countdown = countdown;
            CopiedTraitSource = copiedTraitSource;
        }

        private readonly struct DamageBreakdown : IEquatable<DamageBreakdown>
        {
            private readonly IReadOnlyList<(DamageType Type, int Amount)> parts;
            public IReadOnlyList<(DamageType Type, int Amount)> Parts => parts ?? Array.Empty<(DamageType Type, int Amount)>();

            public DamageBreakdown(IReadOnlyList<(DamageType Type, int Amount)> parts) => this.parts = parts;

            public bool Equals(DamageBreakdown other) => Parts.SequenceEqual(other.Parts);

            public override bool Equals(object obj) => obj is DamageBreakdown other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (var part in Parts)
                    hash.Add(part);
                return hash.ToHashCode();
            }
        }
    }

    public sealed class BattleResult
    {
        public string Id { get; }
        public BattleOutcome Outcome { get; }
        public IReadOnlyDictionary<string, int> HealthById { get; }
        public IReadOnlyDictionary<string, int> PartyPositionsById { get; }
        public IReadOnlyList<BattleEvent> Events { get; }
        public IReadOnlyList<Combatant> Recruits { get; }
        public IReadOnlyDictionary<string, int> PermanentAttackGains { get; }
        public IReadOnlyDictionary<string, int> PermanentHealthGains { get; }
        public int PotionsEarned { get; }
        public int GoldEarned { get; }
        public int AccessoriesEarned { get; }
        public BattleLoadout Loadout { get; }

        public BattleResult(string id, BattleOutcome outcome, IEnumerable<KeyValuePair<string, int>> health,
            IEnumerable<BattleEvent> events, IEnumerable<KeyValuePair<string, int>> partyPositions = null,
            IEnumerable<Combatant> recruits = null, IEnumerable<KeyValuePair<string, int>> permanentAttackGains = null,
            int potionsEarned = 0, BattleLoadout loadout = null, int goldEarned = 0, int accessoriesEarned = 0,
            IEnumerable<KeyValuePair<string, int>> permanentHealthGains = null)
        {
            Id = id;
            Outcome = outcome;
            HealthById = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                health.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            PartyPositionsById = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                (partyPositions ?? Enumerable.Empty<KeyValuePair<string, int>>())
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            Events = Array.AsReadOnly(events.ToArray());
            Recruits = Array.AsReadOnly(recruits?.ToArray() ?? Array.Empty<Combatant>());
            PermanentAttackGains = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                (permanentAttackGains ?? Enumerable.Empty<KeyValuePair<string, int>>())
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
            PermanentHealthGains = (permanentHealthGains ?? Enumerable.Empty<KeyValuePair<string, int>>())
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            PotionsEarned = potionsEarned;
            GoldEarned = goldEarned;
            AccessoriesEarned = accessoriesEarned;
            Loadout = loadout;
        }
    }
}
