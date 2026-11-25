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
        public void SkillCatalogLoadsAllAuthoredSkillsAndTheirSquareIcons()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillCatalogAsset>(
                "Assets/RaidPartyManager/PrototypeData/SkillCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Skills.Select(skill => skill.Number), Is.EqualTo(Enumerable.Range(1, 38)));
            Assert.That(catalog.Skills.Select(skill => skill.DisplayName).Distinct().Count(), Is.EqualTo(38));
            Assert.That(catalog.Skills.All(skill => skill.Cost is >= 1 and <= 8 && skill.Description.Length > 0 &&
                skill.ToBook().SkillEffect != SkillEffect.None && skill.Icon != null && skill.Icon.width == skill.Icon.height), Is.True);
            Assert.That(catalog.Skills.Select(s => s.Icon).Distinct().Count(), Is.EqualTo(38));
            Assert.That(catalog.Skills.Skip(20).Select(s => s.Cost), Is.EqualTo(new[] { 3, 3, 3, 4, 4, 4, 6, 6, 7, 8, 1, 1, 2, 2, 2, 3, 2, 1 }));
            Assert.That(catalog.Skills.Single(skill => skill.DisplayName == "熊熊烈火")
                .CanLearn(Fighter("guard", Side.Party, 1, Personality.Stoic).Character), Is.False);
        }

        [Test]
        public void EveryLearnedSkillHasAUniqueImportedEffectStrip()
        {
            var effects = LoadSkills().Skills.Select(skill => skill.BattleEffect).ToArray();
            Assert.That(effects, Has.Length.EqualTo(38));
            Assert.That(effects.Distinct().Count(), Is.EqualTo(38));
            foreach (var sheet in effects)
            {
                Assert.That(sheet, Is.Not.Null);
                Assert.That(sheet.height, Is.EqualTo(96).Or.EqualTo(128), "Both the original and repainted battle effects use square cels.");
                Assert.That(sheet.width, Is.EqualTo(16 * sheet.height));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sheet));
                Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True);
                Assert.That(importer.mipmapEnabled, Is.False);
            }
        }

        [TestCase(0, PlayerTrait.None, 2)]
        [TestCase(2, PlayerTrait.Distracted, 3)]
        public void VenomSurgeUsesRemainingEnergyAndCanFuelTheWearersDoll(int bonusEnergy, PlayerTrait talent, int spent)
        {
            var book = LoadSkills().Skills.Single(skill => skill.ToBook().SpendsAllEnergy).ToBook();
            var doll = new EquipmentDefinition("doll", "巫医玩偶", default, effect: EquipmentEffect.WitchDoctorDoll);
            var model = Fighter("caster", Side.Party, 1, Personality.Stoic, health: 15);
            var caster = new Combatant(model.Id, model.Character, model.Side, model.Position, model.Stats, model.Health,
                new[] { doll }, learnedSkills: new[] { book }, talent: talent, bonusEnergy: bonusEnergy);
            var front = Fighter("front", Side.Enemy, 1, Personality.Frank);
            var rear = Fighter("rear", Side.Enemy, 2, Personality.Frank);
            var battle = new BattleSimulator().Start(new BattleRequest("venom-surge", new[] { caster, front, rear }, 2));
            var round = battle.AdvanceRound();

            var cast = round.Single(entry => entry.SkillBookId == book.Id && entry.ActorEnergy.HasValue);
            Assert.That(cast.Detail, Is.EqualTo($"消耗 {spent} 点能量"));
            Assert.That(cast.ActorEnergy.Value.Remaining, Is.Zero);
            Assert.That(round.Where(entry => entry.SkillBookId == book.Id && !entry.ActorEnergy.HasValue)
                .Select(entry => (entry.TargetId, entry.TargetStatuses)), Is.EqualTo(new[]
                {
                    (front.Id, $"中毒 {3 * spent}"), (rear.Id, $"中毒 {3 * spent}"), (caster.Id, $"中毒 {spent}")
                }));
            Assert.That(round.Single(entry => entry.ActorId == caster.Id && entry.ActionName == "巫医玩偶").Amount,
                Is.EqualTo(spent));
            Assert.That(battle.AdvanceRound().Any(entry => entry.SkillBookId == book.Id), Is.False);
        }

        [TestCase(true, .29, "caster")]
        [TestCase(false, .29, "ally")]
        [TestCase(false, .30, "enemy")]
        public void ToxicMistCanRedirectToACompatibleDollWearerIncludingItself(bool wearIt, double roll, string expected)
        {
            var book = LoadSkills().Skills.Single(skill => skill.ToBook().SkillEffect == SkillEffect.ToxicMist).ToBook();
            var doll = new[] { new EquipmentDefinition("doll", "巫医玩偶", default, effect: EquipmentEffect.WitchDoctorDoll) };
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, 15, book, wearIt ? doll : null);
            var ally = Fighter("ally", Side.Party, 2, Personality.Stoic, 15, gear: wearIt ? null : doll);
            var foe = Fighter("enemy", Side.Enemy, 1, Personality.Frank);
            var result = new BattleSimulator(random: new FixedSkillRoll(roll))
                .Simulate(new BattleRequest("doll-support", new[] { caster, ally, foe }, 1));

            var poison = result.Events.Single(entry => entry.SkillBookId == book.Id && !entry.ActorEnergy.HasValue);
            Assert.That(poison.TargetId, Is.EqualTo(expected));
            Assert.That(poison.TargetStatuses, Does.Contain("中毒 2"));
            if (expected != foe.Id)
                Assert.That(result.Events.Single(entry => entry.ActionName == "巫医玩偶").TargetId, Is.EqualTo(expected));
        }

        [TestCase(5, true)]
        [TestCase(12, false)]
        public void MageRotatesComparableBooksButKeepsAClearlyBetterSpell(int attack, bool shouldRotate)
        {
            var books = LoadSkills().Skills.Select(s => s.ToBook()).ToArray();
            var fire = books.Single(s => s.SkillEffect == SkillEffect.BlazingFire);
            var whip = books.Single(s => s.SkillEffect == SkillEffect.LightningWhip);
            var basic = new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var stats = new Stats(attack, 0, 0, 30);
            var mage = new CharacterDefinition("mage", "mage", Profession.Mage, Personality.Stoic, stats, 1, basic);
            var cup = new EquipmentDefinition("cup", "余响圣杯", default, effect: EquipmentEffect.EchoChalice);
            var actor = new Combatant("mage", mage, Side.Party, 1, stats, 30, new[] { cup }, learnedSkills: new[] { fire, whip });
            var foe = Fighter("foe", Side.Enemy, 1, Personality.Frank);
            var battle = new BattleSimulator().Start(new BattleRequest("book-choice", new[] { actor, foe }, 3));
            var events = battle.AdvanceRound().ToList();
            events.AddRange(battle.AdvanceRound());
            Assert.That(events.Where(e => e.ActorId == actor.Id && e.SkillBookId != null && e.ActorEnergy.HasValue)
                .Select(e => e.SkillBookId), Is.EqualTo(new[] { fire.Id, shouldRotate ? whip.Id : fire.Id }));
        }

        [TestCase(30, 6)]
        [TestCase(19, 3)]
        [TestCase(4, 1)]
        public void CloudPiercerSpendsTwoEnergyAndDealsTrueDamageFromCurrentHealth(int health, int damage)
        {
            var book = LoadSkills().Skills.Select(skill => skill.ToBook()).Single(skill => skill.SkillEffect == SkillEffect.CloudPiercer);
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, book: book);
            var enemy = Fighter("target", Side.Enemy, 1, Personality.Frank, health);
            var round = new BattleSimulator(new PreferBooks()).Start(new BattleRequest("cloud-piercer",
                new[] { caster, enemy }, 1)).AdvanceRound();

            var cast = round.Single(e => e.SkillBookId == book.Id && e.ActorEnergy.HasValue);
            Assert.That(cast.ActorEnergy.Value.Remaining, Is.EqualTo(cast.ActorEnergy.Value.Capacity - 2));
            var hit = round.Single(e => e.SkillBookId == book.Id && e.DamageTaken.Count > 0);
            Assert.That(hit.DamageTaken, Is.EqualTo(new[] { (DamageType.True, damage) }));
            Assert.That(hit.TargetHealth, Is.EqualTo(health - damage));
        }

        [TestCase(30, 1)] [TestCase(27, 3)]
        public void FeastGrowsOncePerCorpseAndOnlyCreditsEffectiveHealing(int initial, int healing)
        {
            var book = LoadSkills().Skills.Single(s => s.Number == 12).ToBook();
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, initial, book);
            var corpse = Fighter("fallen", Side.Enemy, 1, Personality.Stoic, 0);
            var other = Fighter("foe", Side.Enemy, 2, Personality.Stoic);
            var session = new BattleSimulator(new PreferBooks(), new System.Random(1)).Start(
                new BattleRequest("feast", new[] { caster, corpse, other }, 3));
            while (session.Result == null) session.AdvanceRound();
            var result = session.Result;
            Assert.That(result.PermanentHealthGains[caster.Id], Is.EqualTo(1));
            var heal = result.Events.Single(ev => ev.SkillBookId == book.Id && ev.ActionKind == ActionKind.Heal && !ev.ActorEnergy.HasValue);
            Assert.That(heal.Amount, Is.EqualTo(healing));
        }

        [TestCase(21)] [TestCase(22)] [TestCase(23)] [TestCase(24)] [TestCase(25)]
        [TestCase(26)] [TestCase(27)] [TestCase(28)] [TestCase(29)] [TestCase(30)]
        public void HighCostSkillsChargeAndActuallyCastInBattle(int number)
        {
            var book = LoadSkills().Skills.Single(s => s.Number == number).ToBook();
            var caster = Fighter("caster", Side.Party, 2, Personality.Stoic, 22, book);
            var ally = Fighter("ally", Side.Party, 1, Personality.Stoic, number == 29 ? 1 : 20);
            var enemy = Fighter("enemy", Side.Enemy, 1, Personality.Frank);
            var boss = new Combatant(enemy.Id, enemy.Character, enemy.Side, 1, new Stats(2, 0, 0, 200), 200);
            var session = new BattleSimulator(new PreferBooks(), new System.Random(8)).Start(
                new BattleRequest("charge", new[] { caster, ally, boss }, 30));
            var events = new List<BattleEvent>();
            for (int i = 0; i < book.Cost && session.Result == null; i++) events.AddRange(session.AdvanceRound());
            var cast = events.First(e => e.ActorId == caster.Id && e.ActionName == book.Name && e.ActorEnergy.HasValue);
            Assert.That(cast.ActorEnergy.Value.Remaining, Is.Zero);
            Assert.That(cast.ActorEnergy.Value.Capacity, Is.EqualTo(book.Cost));
            if (number == 29) Assert.That(events.Any(e => e.TargetId == ally.Id && e.SkillBookId == book.Id && e.ActionKind == ActionKind.Heal && e.Amount == 12), Is.True);
            if (number == 30) Assert.That(events.Any(e => e.TargetId == boss.Id && e.SkillBookId == book.Id && e.DamageTaken.Any(p => p.Type == DamageType.Lightning)), Is.True);
        }

        [TestCase(31)] [TestCase(32)] [TestCase(33)] [TestCase(34)]
        public void BossBooksApplyTheirDocumentedEffectsToTheIntendedTargets(int number)
        {
            var book = LoadSkills().Skills.Single(skill => skill.Number == number).ToBook();
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, 20, book);
            var ally = Fighter("ally", Side.Party, 2, Personality.Stoic, 15);
            ally = new Combatant(ally.Id, ally.Character, ally.Side, ally.Position, ally.Stats, ally.Health,
                startingStatuses: new Dictionary<StatusKind, int> { [StatusKind.Cursed] = 4 });
            var front = Fighter("front", Side.Enemy, 1, Personality.Frank);
            var middle = Fighter("middle", Side.Enemy, 2, Personality.Frank);
            var rear = Fighter("rear", Side.Enemy, 3, Personality.Frank);
            var battle = new BattleSimulator(new PreferBooks(), new System.Random(19)).Start(
                new BattleRequest("boss-book", new[] { caster, ally, front, middle, rear }, 6));
            var log = new List<BattleEvent>();
            for (int i = 0; i < book.Cost; i++) log.AddRange(battle.AdvanceRound());
            var events = log.Where(e => e.SkillBookId == book.Id).ToArray();
            Assert.That(events.Count(e => e.ActorEnergy.HasValue), Is.EqualTo(1));
            var hits = events.Where(e => e.ActionKind == ActionKind.Attack && e.DamageTaken.Count > 0).ToArray();
            if (number == 31)
            {
                Assert.That(hits.Select(e => e.TargetId), Is.EquivalentTo(new[] { "middle", "rear" }));
                Assert.That(hits.All(e => e.DamageTaken.Single() == (DamageType.Physical, 5)), Is.True);
                Assert.That(events.Where(e => e.TargetStatuses?.Contains("破绽 1") == true).Select(e => e.TargetId),
                    Is.EquivalentTo(new[] { "middle", "rear" }));
            }
            else if (number == 32)
            {
                Assert.That(hits.Single().DamageTaken.Single(), Is.EqualTo((DamageType.Magic, 5)));
                Assert.That(events.Any(e => e.TargetId == hits[0].TargetId && e.TargetStatuses?.Contains("冰冻") == true), Is.True);
                Assert.That(events.Count(e => e.TargetStatuses?.Contains("寒冷 1") == true), Is.EqualTo(2));
            }
            else if (number == 33)
            {
                Assert.That(hits.Single().TargetId, Is.EqualTo("front"));
                Assert.That(hits[0].DamageTaken.Single(), Is.EqualTo((DamageType.Physical, 6)));
                Assert.That(events.Where(e => e.TargetStatuses?.Contains("诅咒 2") == true).Select(e => e.TargetId),
                    Is.EquivalentTo(new[] { "front", "middle", "rear" }));
            }
            else
            {
                Assert.That(events.Where(e => e.ActionKind == ActionKind.Heal && e.Amount == 5).Select(e => e.TargetId),
                    Is.EquivalentTo(new[] { "caster", "ally" }));
                Assert.That(events.Where(e => e.ActionKind == ActionKind.Shield && e.Amount == 5).Select(e => e.TargetId),
                    Is.EquivalentTo(new[] { "caster", "ally" }));
                Assert.That(events.Any(e => e.ActionKind == ActionKind.Heal && e.TargetStatuses?.Contains("诅咒") == true), Is.False);
            }
        }

        [Test]
        public void GroupDamageMarksOnlyResolvedEnemyTargetsWithItsSkillId()
        {
            var book = LoadSkills().Skills.Single(skill => skill.Number == 2).ToBook();
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, book: book);
            var ally = Fighter("ally", Side.Party, 2, Personality.Stoic);
            var front = Fighter("front", Side.Enemy, 1, Personality.Frank);
            var rear = Fighter("rear", Side.Enemy, 4, Personality.Frank);
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
            var mage = new CharacterDefinition("mage", "mage", Profession.Mage, Personality.Theatrical, stats, 1,
                new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0));
            var caster = new Combatant("caster", mage, Side.Party, 1, stats, stats.Health,
                Enumerable.Repeat(quartz, quartzCount).Append(thunderEye), learnedSkills: new[] { fire });
            var enemies = new[] { Fighter("front", Side.Enemy, 1, Personality.Frank),
                Fighter("rear", Side.Enemy, 4, Personality.Frank) }.Select(unit =>
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
            var caster = Fighter("caster", Side.Party, 1, Personality.Stoic, book: book);
            var front = Fighter("front", Side.Enemy, 1, Personality.Frank, gear: new[]
            {
                new EquipmentDefinition("horn", "反击的号角", default, effect: EquipmentEffect.CounterHorn)
            });
            var rear = Fighter("rear", Side.Enemy, 4, Personality.Frank);
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
        public void EmployeesKeepTheirPositionsAfterBeingWounded()
        {
            var first = Fighter("first", Side.Party, 1, Personality.Worrier, 10);
            var last = Fighter("last", Side.Party, 4, Personality.Stoic);
            var enemy = Fighter("enemy", Side.Enemy, 1, Personality.Frank);
            var session = new BattleSimulator().Start(new BattleRequest("formation", new[] { first, last, enemy }, 2));

            session.AdvanceRound();
            Assert.That(session.Formation.Single(unit => unit.Id == first.Id).Position, Is.EqualTo(1));
            Assert.That(session.Formation.Single(unit => unit.Id == last.Id).Position, Is.EqualTo(4));
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

        private sealed class FixedSkillRoll : System.Random
        {
            private readonly double roll;
            public FixedSkillRoll(double roll) => this.roll = roll;
            public override double NextDouble() => roll;
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
