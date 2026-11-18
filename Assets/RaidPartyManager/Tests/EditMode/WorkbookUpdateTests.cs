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
                skill.Icon != null && skill.Icon.width == skill.Icon.height), Is.True);
            Assert.That(catalog.Skills.Single(skill => skill.DisplayName == "熊熊烈火")
                .CanLearn(Fighter("guard", Side.Party, 1, Personality.Stubborn).Character), Is.False);
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

        private static Combatant Fighter(string id, Side side, int position, Personality personality, int health = 30)
        {
            var basic = new ActionDefinition("basic", "普攻", ActionKind.Attack, TargetRule.FrontEnemy, 0);
            var definition = new CharacterDefinition(id, id, Profession.Warrior, personality, new Stats(2, 0, 0, 30), 1, basic);
            return new Combatant(id, definition, side, position, definition.BaseStats, health);
        }
    }
}
