using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEditor;

namespace RaidPartyManager.Tests
{
    public sealed class WorkbookUpdateTests
    {
        [Test]
        public void SkillCatalogLoadsTheExcelRowsAndTransparentSquareArt()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillCatalogAsset>(
                "Assets/RaidPartyManager/PrototypeData/SkillCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Skills.Select(skill => skill.Number), Is.EqualTo(Enumerable.Range(1, 20)));
            Assert.That(catalog.Skills.Select(skill => skill.DisplayName).Distinct().Count(), Is.EqualTo(20));
            Assert.That(catalog.Skills.All(skill => skill.Cost is 1 or 2 && skill.Description.Length > 0 &&
                skill.ToBook().SkillEffect != SkillEffect.None && skill.Icon != null && skill.Icon.width == skill.Icon.height), Is.True);
            Assert.That(catalog.Skills.Single(skill => skill.DisplayName == "熊熊烈火")
                .CanLearn(Fighter("guard", Side.Party, 1, Personality.Stubborn).Character), Is.False);
        }

        [Test]
        public void EveryLearnedSkillHasAUniqueImportedEffectStrip()
        {
            var effects = LoadSkills().Skills.Select(skill => skill.BattleEffect).ToArray();
            Assert.That(effects, Has.Length.EqualTo(20));
            Assert.That(effects.Distinct().Count(), Is.EqualTo(20));
            foreach (var sheet in effects)
            {
                Assert.That(sheet, Is.Not.Null);
                Assert.That(sheet.height, Is.EqualTo(96));
                Assert.That(sheet.width, Is.EqualTo(16 * sheet.height));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sheet));
                Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True);
                Assert.That(importer.mipmapEnabled, Is.False);
            }
        }

        [Test]
        public void GroupDamageMarksOnlyResolvedEnemyTargetsWithItsSkillId()
        {
            var book = LoadSkills().Skills.Single(skill => skill.Number == 2).ToBook();
            var caster = Fighter("caster", Side.Party, 1, Personality.Stubborn, book: book);
            var ally = Fighter("ally", Side.Party, 2, Personality.Stubborn);
            var front = Fighter("front", Side.Enemy, 1, Personality.Reckless);
            var rear = Fighter("rear", Side.Enemy, 4, Personality.Reckless);
            var session = new BattleSimulator(new PreferBooks()).Start(new BattleRequest(
                "group-skill", new[] { caster, ally, front, rear }, 200));

            var events = session.AdvanceRound();

            Assert.That(events.Where(entry => entry.SkillBookId == book.Id && !entry.ActorEnergy.HasValue).Select(entry => entry.TargetId),
                Is.EqualTo(new[] { front.Id, rear.Id }));
        }

        [TestCase(0, DamageType.Fire, 3, BattleVisualCue.None)]
        [TestCase(1, DamageType.Lightning, 9, BattleVisualCue.SkillLightningImpact)]
        [TestCase(2, DamageType.Fire, 3, BattleVisualCue.None)]
        public void BlazingFireUsesTheElementalQuartzDamageRulesAndImpactArt(
            int quartzCount, DamageType damageType, int damage, BattleVisualCue visual)
        {
            const string folder = "Assets/RaidPartyManager/PrototypeData/";
            var content = AssetDatabase.LoadAssetAtPath<EncounterAsset>(folder + "RoadsideEncounter.asset").ToData();
            var fire = content.SkillBooks.Single(book => book.SkillEffect == SkillEffect.BlazingFire);
            var whip = content.SkillBooks.Single(book => book.SkillEffect == SkillEffect.LightningWhip);
            var quartz = content.StartingEquipment.First(item => item.Id == "elemental-quartz");
            var thunderEye = content.StartingEquipment.First(item => item.Effect == EquipmentEffect.ThunderEye);
            var stats = new Stats(6, 0, 0, 40);
            var mage = new CharacterDefinition("mage", "mage", Profession.Mage, Personality.Impatient, stats, 1,
                new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            var caster = new Combatant("caster", mage, Side.Party, 1, stats, stats.Health,
                Enumerable.Repeat(quartz, quartzCount).Append(thunderEye), learnedSkills: new[] { fire });
            var enemies = new[] { Fighter("front", Side.Enemy, 1, Personality.Reckless),
                Fighter("rear", Side.Enemy, 4, Personality.Reckless) }.Select(unit =>
                new Combatant(unit.Id, unit.Character, unit.Side, unit.Position, new Stats(2, 0, 4, 30), 30)).ToArray();
            var round = new BattleSimulator(new PreferBooks()).Start(
                new BattleRequest("quartz", enemies.Prepend(caster), 1)).AdvanceRound();
            var hits = round.Where(entry => entry.SkillBookId == fire.Id && entry.ActionKind == ActionKind.Attack).ToArray();
            Assert.That(hits.Select(entry => entry.TargetId), Is.EqualTo(enemies.Select(enemy => enemy.Id)));
            foreach (var hit in hits)
            {
                Assert.That(hit.DamageTaken, Is.EqualTo(new[] { (damageType, damage) }));
                Assert.That(hit.VisualCue, Is.EqualTo(visual));
                Assert.That(content.SkillEffectFor(fire.Id, hit.VisualCue),
                    Is.SameAs(content.SkillEffectFor(damageType == DamageType.Lightning ? whip.Id : fire.Id)));
            }
        }

        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        public void SweepVariantsRetainTheBookIdentityUntilTheNextAttack(int number)
        {
            var book = LoadSkills().Skills.Single(skill => skill.Number == number).ToBook();
            var caster = Fighter("caster", Side.Party, 1, Personality.Stubborn, book: book);
            var front = Fighter("front", Side.Enemy, 1, Personality.Reckless, gear: new[]
            {
                new EquipmentDefinition("horn", "反击的号角", default, effect: EquipmentEffect.CounterHorn)
            });
            var rear = Fighter("rear", Side.Enemy, 4, Personality.Reckless);
            var session = new BattleSimulator(new PreferBooks()).Start(new BattleRequest(
                "sweep-skill", new[] { caster, front, rear }, 200));

            var firstRound = session.AdvanceRound();
            Assert.That(firstRound.Any(entry => entry.ActorId == caster.Id && entry.ActionName == book.Name), Is.True);
            Assert.That(firstRound.Any(entry => entry.SkillBookId == book.Id && !entry.ActorEnergy.HasValue), Is.False);
            var secondRound = session.AdvanceRound();

            var sweep = secondRound.Where(entry => entry.SkillBookId == book.Id).ToArray();
            Assert.That(sweep.Select(entry => entry.TargetId), Is.EqualTo(new[] { front.Id, rear.Id }));
            Assert.That(sweep.Select(entry => entry.Kind), Is.EqualTo(new[] { BattleEventKind.Action, BattleEventKind.Effect }));
            Assert.That(sweep.Select(entry => entry.Amount), Is.All.EqualTo(2));
            Assert.That(sweep.Select(entry => entry.ActionName), Is.All.EqualTo(book.Name));
            var log = secondRound.ToList();
            Assert.That(log.FindIndex(entry => entry.ActionName == "反击的号角"),
                Is.GreaterThan(log.FindLastIndex(entry => entry.SkillBookId == book.Id)));
        }

        [Test]
        public void TimidRetreatFindsTheOpenPositionBeforeAStubbornAlly()
        {
            var timid = Fighter("timid", Side.Party, 1, Personality.Timid, 10);
            var stubborn = Fighter("stubborn", Side.Party, 4, Personality.Stubborn);
            var enemy = Fighter("enemy", Side.Enemy, 1, Personality.Reckless);
            var session = new BattleSimulator().Start(new BattleRequest("retreat", new[] { timid, stubborn, enemy }, 2));

            session.AdvanceRound();

            Assert.That(session.Formation.Single(unit => unit.Id == timid.Id).Position, Is.EqualTo(3));
            Assert.That(session.IsPositionLocked(4), Is.True);
            Assert.That(session.PositionLockReason(4), Is.EqualTo("固执"));
            Assert.That(session.TrySwapPartyPositions(3, 4), Is.False);
        }

        [Test]
        public void NewTargetPreferencesRespectLegalOptionsAndDistinguishDamageFromOverkill()
        {
            var first = Fighter("first", Side.Enemy, 1, Personality.Reckless, 2);
            var second = Fighter("second", Side.Enemy, 2, Personality.Reckless, 8);
            var options = new[]
            {
                new ActionOption(first.Character.BasicAttack, first, first.Health, 15),
                new ActionOption(second.Character.BasicAttack, second, second.Health, 7)
            };
            foreach (var (personality, expected) in new[]
            {
                (Personality.Greedy, 1), (Personality.Careful, 0),
                (Personality.Vengeful, 1), (Personality.Focused, 0)
            })
            {
                var actor = Fighter("actor", Side.Party, 1, personality);
                var context = new DecisionContext(actor, 1, actor.Health, 0, null, second.Id, first.Id);
                Assert.That(new PersonalityPolicy().Choose(context, options).OptionIndex, Is.EqualTo(expected), personality.ToString());
            }
        }

        private static SkillCatalogAsset LoadSkills() => AssetDatabase.LoadAssetAtPath<SkillCatalogAsset>(
            "Assets/RaidPartyManager/PrototypeData/SkillCatalog.asset");

        private static Combatant Fighter(string id, Side side, int position, Personality personality,
            int health = 30, SkillBookDefinition book = null, IEnumerable<EquipmentDefinition> gear = null)
        {
            var basic = new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var definition = new CharacterDefinition(id, id, Profession.Warrior, personality, new Stats(2, 0, 0, 30), 1, basic);
            return new Combatant(id, definition, side, position, definition.BaseStats, health, gear,
                learnedSkills: book == null ? null : new[] { book });
        }

        private sealed class PreferBooks : IDecisionPolicy
        {
            public Decision Choose(DecisionContext context, IReadOnlyList<ActionOption> options)
            {
                for (int i = 0; i < options.Count; i++)
                    if (options[i].Book != null)
                        return new Decision(i, DecisionReason.UseSkill);
                return new Decision(0, DecisionReason.Default);
            }
        }
    }
}
