using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using UnityEngine;

namespace RaidPartyManager.Content
{
    public sealed class EncounterData
    {
        public string Name { get; }
        public IReadOnlyList<Combatant> Party { get; }
        public IReadOnlyList<Combatant> Enemies { get; }
        public IReadOnlyList<EquipmentDefinition> StartingEquipment { get; }
        public IReadOnlyList<SkillBookDefinition> SkillBooks { get; }
        public EquipmentDefinition VictoryReward { get; }
        public int TickLimit { get; }
        public Texture2D BackpackIcon { get; }
        public IReadOnlyList<CharacterDefinition> RecruitmentPool { get; }
        public IReadOnlyList<CharacterDefinition> CharacterCatalog { get; }
        public IReadOnlyList<CharacterDefinition> EnemyRoster { get; }
        public IReadOnlyList<EnemyFormation> EnemyFormations { get; }
        private readonly CharacterDefinition[] partyRoster;
        private readonly IReadOnlyDictionary<string, Texture2D> portraits;
        private readonly HashSet<string> battlePortraitIds;
        private readonly IReadOnlyDictionary<string, Texture2D> battleSpriteSheets;
        private readonly IReadOnlyDictionary<string, Texture2D> battleAttackSheets;
        private readonly IReadOnlyDictionary<string, Texture2D> battleWalkSheets;
        private readonly IReadOnlyDictionary<string, (Texture2D Body, Texture2D Effect, Texture2D Finale, Texture2D Strike)> battleExtras;
        private readonly IReadOnlyDictionary<string, Texture2D> battleProjectiles;
        private readonly IReadOnlyDictionary<string, Texture2D> campSprites;
        private readonly IReadOnlyDictionary<string, Texture2D> equipmentIcons;
        private readonly IReadOnlyDictionary<string, Texture2D> skillEffects;
        private readonly IReadOnlyDictionary<string, (Texture2D Flight, Texture2D Fragments)> skillProjectiles;
        private readonly Texture2D[] statusIcons;
        private readonly Texture2D arrowProjectile;
        private readonly Texture2D magicProjectile;

        internal EncounterData(string name, Combatant[] party, Combatant[] enemies,
            EquipmentDefinition[] startingEquipment, EquipmentDefinition victoryReward, int tickLimit,
            IReadOnlyDictionary<string, Texture2D> portraits, IReadOnlyDictionary<string, Texture2D> equipmentIcons,
            CharacterDefinition[] recruitmentPool, CharacterDefinition[] characterCatalog, Texture2D[] statusIcons,
            IReadOnlyDictionary<string, Texture2D> battleSpriteSheets, IReadOnlyDictionary<string, Texture2D> battleAttackSheets,
            IReadOnlyDictionary<string, Texture2D> battleWalkSheets, Texture2D arrowProjectile, Texture2D magicProjectile,
            CharacterDefinition[] enemyRoster,
            IReadOnlyDictionary<string, (Texture2D Body, Texture2D Effect, Texture2D Finale, Texture2D Strike)> battleExtras,
            IReadOnlyDictionary<string, Texture2D> battleProjectiles,
            IReadOnlyDictionary<string, Texture2D> campSprites, SkillBookDefinition[] skillBooks, Texture2D backpackIcon,
            IReadOnlyDictionary<string, Texture2D> skillEffects, IReadOnlyDictionary<string, (Texture2D Flight, Texture2D Fragments)> skillProjectiles,
            IEnumerable<string> battlePortraitIds, EnemyFormation[] enemyFormations)
        {
            Name = name;
            Party = Array.AsReadOnly(party);
            Enemies = Array.AsReadOnly(enemies);
            StartingEquipment = Array.AsReadOnly(startingEquipment);
            SkillBooks = Array.AsReadOnly(skillBooks);
            BackpackIcon = backpackIcon;
            VictoryReward = victoryReward;
            TickLimit = tickLimit;
            this.portraits = portraits;
            this.battlePortraitIds = new HashSet<string>(battlePortraitIds, StringComparer.Ordinal);
            this.battleSpriteSheets = battleSpriteSheets;
            this.battleAttackSheets = battleAttackSheets;
            this.battleWalkSheets = battleWalkSheets;
            this.battleExtras = battleExtras;
            this.battleProjectiles = battleProjectiles;
            this.campSprites = campSprites;
            this.equipmentIcons = equipmentIcons;
            this.skillEffects = skillEffects;
            this.skillProjectiles = skillProjectiles;
            this.statusIcons = statusIcons;
            this.arrowProjectile = arrowProjectile;
            this.magicProjectile = magicProjectile;
            RecruitmentPool = Array.AsReadOnly(recruitmentPool);
            CharacterCatalog = Array.AsReadOnly(characterCatalog);
            EnemyRoster = Array.AsReadOnly(enemyRoster);
            EnemyFormations = Array.AsReadOnly(enemyFormations);
            partyRoster = characterCatalog.Where(character => character.RosterNumber > 0).ToArray();
        }

        public IReadOnlyList<Combatant> Draft(Side side, System.Random random)
        {
            if (side == Side.Enemy)
            {
                var groups = EnemyFormations.Where(group => group.Rank == EnemyRank.Normal).ToArray();
                if (groups.Length > 0)
                    return groups[random.Next(groups.Length)].CreateLineup("enemy");
            }

            var candidates = (side == Side.Party ? partyRoster :
                EnemyRoster.Where(character => character.Rank != EnemyRank.Boss && character.PositionSlots == 1)).ToArray();
            if (candidates.Length < Rules.Positions)
                return side == Side.Party ? Party : Enemies;

            var lineup = new Combatant[Rules.Positions];
            for (int i = 0; i < lineup.Length; i++)
            {
                int pick = random.Next(i, candidates.Length);
                (candidates[i], candidates[pick]) = (candidates[pick], candidates[i]);
                var character = candidates[i];
                string id = $"{(side == Side.Party ? "ally" : "enemy")}-{i + 1}";
                lineup[i] = new Combatant(id, character, side, i + 1, character.BaseStats, character.BaseStats.Health);
            }
            return lineup;
        }

        public Texture2D IconFor(string itemId) =>
            itemId != null && equipmentIcons.TryGetValue(itemId, out var icon) ? icon : null;

        public Texture2D SkillEffectFor(string skillBookId, BattleVisualCue visual = BattleVisualCue.None)
        {
            var impact = visual switch
            {
                BattleVisualCue.SkillFireImpact => SkillEffect.BlazingFire,
                BattleVisualCue.SkillLightningImpact => SkillEffect.LightningWhip,
                _ => SkillEffect.None
            };
            string artworkId = impact == SkillEffect.None ? skillBookId :
                SkillBooks.FirstOrDefault(book => book.SkillEffect == impact)?.Id;
            return artworkId != null && skillEffects.TryGetValue(artworkId, out var artwork) ? artwork : null;
        }

        public (Texture2D Flight, Texture2D Fragments) SkillProjectileFor(string skillBookId) =>
            skillBookId != null && skillProjectiles.TryGetValue(skillBookId, out var artwork) ? artwork : default;

        public Texture2D StatusIcon(StatusKind kind) =>
            (int)kind >= 0 && (int)kind < statusIcons.Length ? statusIcons[(int)kind] : null;

        public Texture2D PortraitFor(string definitionId) =>
            definitionId != null && portraits.TryGetValue(definitionId, out var portrait) ? portrait : null;

        public Texture2D CampSpriteFor(string definitionId) =>
            definitionId != null && campSprites.TryGetValue(definitionId, out var sprite) ? sprite : null;

        public Texture2D BattleSpriteFor(string definitionId) =>
            definitionId != null && battleSpriteSheets.TryGetValue(definitionId, out var sheet) ? sheet : null;

        public Texture2D BattlePortraitFor(string definitionId) =>
            battlePortraitIds.Contains(definitionId) ? PortraitFor(definitionId) : null;

        public Texture2D BattleAttackFor(string definitionId) =>
            definitionId != null && battleAttackSheets.TryGetValue(definitionId, out var sheet) ? sheet : null;

        public Texture2D BattleWalkFor(string definitionId) =>
            definitionId != null && battleWalkSheets.TryGetValue(definitionId, out var sheet) ? sheet : null;

        public Texture2D BattleSpecialFor(string definitionId, BattleVisualCue cue)
        {
            if (!battleExtras.TryGetValue(definitionId, out var art))
                return null;
            return cue switch
            {
                BattleVisualCue.WarCry or BattleVisualCue.CeramicPierce or BattleVisualCue.BellRing or
                BattleVisualCue.RearAmbush or BattleVisualCue.ResolveGuard or BattleVisualCue.CrossbowCritical or
                BattleVisualCue.QuackHeal or BattleVisualCue.AnchorExpose or BattleVisualCue.AnglerCurse or
                BattleVisualCue.WardenBrace or BattleVisualCue.AdmiralWhistle or BattleVisualCue.RayMist or
                BattleVisualCue.RaySurge or BattleVisualCue.GraveyardSpecial or BattleVisualCue.GraveyardCharge => art.Body,
                _ => null
            };
        }

        public Texture2D BattleEffectFor(string definitionId, BattleVisualCue cue)
        {
            if (!battleExtras.TryGetValue(definitionId, out var art))
                return null;
            return cue switch
            {
                BattleVisualCue.None or BattleVisualCue.CeramicPierce => null,
                BattleVisualCue.BellWave => art.Finale,
                BattleVisualCue.QuackStrike => art.Strike,
                _ => art.Effect
            };
        }

        public Texture2D ProjectileFor(CharacterDefinition character)
        {
            if (battleProjectiles.TryGetValue(character.Id, out var projectile))
                return projectile;
            return character.Profession switch
            {
                Profession.Archer => arrowProjectile,
                Profession.Mage => magicProjectile,
                Profession.Monster when character.BasicAttack.DamageType == DamageType.Magic => magicProjectile,
                _ => null
            };
        }
    }

    [CreateAssetMenu(menuName = "Raid Party Manager/Encounter")]
    public sealed class EncounterAsset : ScriptableObject
    {
        [Serializable]
        private sealed class StartingStatus
        {
            public StatusKind status;
            [Min(1)] public int layers = 1;
        }

        [Serializable]
        private sealed class UnitEntry
        {
            [SerializeField] private string instanceId;
            [SerializeField] private CharacterAsset character;
            [SerializeField, Range(1, Rules.Positions)] private int position = 1;
            [Tooltip("-1 uses the character's maximum health.")]
            [SerializeField, Min(-1)] private int startingHealth = -1;
            [SerializeField] private StartingStatus[] startingStatuses = Array.Empty<StartingStatus>();

            public CharacterAsset Character => character;

            public Combatant ToCombatant(Side side)
            {
                if (character == null)
                    throw new InvalidOperationException($"Encounter unit '{instanceId}' needs a character asset.");
                var definition = character.ToDefinition();
                int health = startingHealth == -1 ? definition.BaseStats.Health : startingHealth;
                var statuses = startingStatuses.GroupBy(entry => entry.status)
                    .ToDictionary(group => group.Key, group => group.Sum(entry => entry.layers));
                return new Combatant(instanceId, definition, side, position, definition.BaseStats, health,
                    startingStatuses: statuses);
            }
        }

        [Serializable]
        private sealed class FormationEntry
        {
            public string name;
            public string[] enemyIds = Array.Empty<string>();

            public EnemyFormation ToData(IReadOnlyDictionary<string, CharacterDefinition> roster) =>
                new EnemyFormation(name, enemyIds.Select(id => roster.TryGetValue(id, out var enemy) ? enemy :
                    throw new InvalidOperationException($"Enemy formation '{name}' references '{id}' outside the encounter roster.")));
        }

        [SerializeField] private string displayName;
        [SerializeField] private UnitEntry[] party = Array.Empty<UnitEntry>();
        [SerializeField] private UnitEntry[] enemies = Array.Empty<UnitEntry>();
        [SerializeField] private CharacterAsset[] additionalRecruits = Array.Empty<CharacterAsset>();
        [SerializeField] private CharacterAsset[] eventRecruits = Array.Empty<CharacterAsset>();
        [SerializeField] private CharacterAsset[] enemyRoster = Array.Empty<CharacterAsset>();
        [SerializeField] private FormationEntry[] enemyFormations = Array.Empty<FormationEntry>();
        [SerializeField] private Texture2D[] statusIcons = Array.Empty<Texture2D>();
        [SerializeField] private Texture2D arrowProjectile;
        [SerializeField] private Texture2D magicProjectile;
        [SerializeField] private Texture2D backpackIcon;
        [SerializeField] private EquipmentAsset[] startingEquipment = Array.Empty<EquipmentAsset>();
        [SerializeField, Min(1)] private int startingCopies = 1;
        [SerializeField] private SkillCatalogAsset skillCatalog;
        [SerializeField] private EquipmentAsset victoryReward;
        [SerializeField, Min(1)] private int tickLimit = 200;

        public EncounterData ToData()
        {
            var partyInputs = party.Select(entry => entry.ToCombatant(Side.Party)).ToArray();
            var enemyInputs = enemies.Select(entry => entry.ToCombatant(Side.Enemy)).ToArray();
            // Fail at startup if a content author has created an invalid formation or duplicate ID.
            _ = new BattleRequest("encounter-validation", partyInputs.Concat(enemyInputs), tickLimit);
            var recruits = party.Select(entry => entry.Character).Concat(additionalRecruits)
                .Where(character => character != null).Distinct().ToArray();
            var enemyChoices = enemyRoster.Where(character => character != null).Distinct().ToArray();
            var catalog = new Dictionary<string, CharacterAsset>(StringComparer.Ordinal);
            var pending = new Queue<CharacterAsset>(recruits.Concat(eventRecruits.Where(c => c != null)).Concat(enemies.Select(entry => entry.Character)).Concat(enemyChoices));
            while (pending.Count > 0)
            {
                var character = pending.Dequeue();
                if (!catalog.TryAdd(character.Id, character))
                    continue;
                foreach (var form in character.RelatedForms)
                    pending.Enqueue(form);
            }

            var portraits = catalog.Values.Where(character => character.Portrait != null)
                .ToDictionary(character => character.Id, character => character.Portrait, StringComparer.Ordinal);
            var battleSpriteSheets = catalog.Values.Where(character => character.BattleSpriteSheet != null)
                .ToDictionary(character => character.Id, character => character.BattleSpriteSheet, StringComparer.Ordinal);
            var battleAttackSheets = catalog.Values.Where(character => character.BattleAttackSheet != null)
                .ToDictionary(character => character.Id, character => character.BattleAttackSheet, StringComparer.Ordinal);
            var battleWalkSheets = catalog.Values.Where(character => character.BattleWalkSheet != null)
                .ToDictionary(character => character.Id, character => character.BattleWalkSheet, StringComparer.Ordinal);
            var equipmentIcons = startingEquipment.Append(victoryReward)
                .Where(item => item != null && item.Icon != null)
                .GroupBy(item => item.ToDefinition().Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Icon, StringComparer.Ordinal);
            var skills = skillCatalog?.Skills ?? Array.Empty<SkillEntry>();
            var books = skills.Select(skill => skill.ToBook()).ToArray();
            var skillEffects = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var skillProjectiles = new Dictionary<string, (Texture2D Flight, Texture2D Fragments)>(StringComparer.Ordinal);
            for (int i = 0; i < books.Length; i++)
            {
                equipmentIcons.Add(books[i].Id, skills[i].Icon);
                if (skills[i].BattleEffect != null)
                    skillEffects.Add(books[i].Id, skills[i].BattleEffect);
                if (skills[i].Projectile != null)
                    skillProjectiles.Add(books[i].Id, (skills[i].Projectile, skills[i].ProjectileFragments));
            }
            var enemyDefinitions = enemyChoices.Select(character => character.ToDefinition()).ToArray();
            var enemyById = enemyDefinitions.ToDictionary(enemy => enemy.Id, StringComparer.Ordinal);
            var formations = enemyFormations.Select(group => group.ToData(enemyById)).ToArray();
            if (formations.Select(group => group.Name).Distinct(StringComparer.Ordinal).Count() != formations.Length)
                throw new InvalidOperationException($"Encounter '{displayName}' has duplicate enemy formation names.");

            return new EncounterData(displayName, partyInputs, enemyInputs,
                startingEquipment.SelectMany(item => Enumerable.Repeat(item.ToDefinition(), startingCopies)).ToArray(),
                victoryReward != null ? victoryReward.ToDefinition() : null, tickLimit, portraits, equipmentIcons,
                recruits.Select(character => character.ToDefinition()).ToArray(),
                catalog.Values.Select(character => character.ToDefinition()).ToArray(), statusIcons, battleSpriteSheets,
                battleAttackSheets, battleWalkSheets, arrowProjectile, magicProjectile,
                enemyDefinitions,
                catalog.Values.ToDictionary(character => character.Id, character => character.BattleExtras, StringComparer.Ordinal),
                catalog.Values.Where(character => character.BattleProjectile != null)
                    .ToDictionary(character => character.Id, character => character.BattleProjectile, StringComparer.Ordinal),
                catalog.Values.Where(character => character.CampSeatedSprite != null)
                    .ToDictionary(character => character.Id, character => character.CampSeatedSprite, StringComparer.Ordinal),
                books, backpackIcon, skillEffects, skillProjectiles,
                catalog.Values.Where(character => character.UsePortraitInBattle).Select(character => character.Id), formations);
        }
    }
}
