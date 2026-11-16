using System.Linq;
using NUnit.Framework;
using RaidPartyManager.Battle;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Tests
{
    public sealed class BattleTests
    {
        private static readonly ActionDefinition Basic = Attack("basic", TargetRule.AnyEnemy);
        private static readonly ActionDefinition Front = Attack("front", TargetRule.FrontEnemy);
        private static readonly ActionDefinition Heal = new ActionDefinition("heal", "heal", ActionKind.Heal, TargetRule.WoundedAlly, 4, 3);
        private static readonly ActionDefinition Guard = new ActionDefinition("guard", "guard", ActionKind.Shield, TargetRule.Self, 5, 3);

        [Test]
        public void AggressiveChoosesHighestDamageAcrossAttackTypesAndTargets()
        {
            var magic = Attack("magic", TargetRule.AnyEnemy, 2, 3, DamageType.Magic);
            var result = Fight(Unit("actor", Side.Party, 1, Personality.Aggressive, skill: magic),
                Unit("front", Side.Enemy, 1, defense: 1, specialDefense: 8),
                Unit("back", Side.Enemy, 4, defense: 8, specialDefense: 0));

            var action = FirstAction(result, "actor");
            Assert.That(action.ActionName, Is.EqualTo("magic"));
            Assert.That(action.TargetId, Is.EqualTo("back"));
            Assert.That(action.Amount, Is.EqualTo(7));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.HighestDamage));
        }

        [Test]
        public void CautiousShieldsInPreferenceToAttacking()
        {
            var action = FirstAction(Fight(Unit("actor", Side.Party, 1, Personality.Cautious, skill: Guard),
                Unit("enemy", Side.Enemy, 1)), "actor");
            Assert.That(action.ActionKind, Is.EqualTo(ActionKind.Shield));
            Assert.That(action.TargetId, Is.EqualTo("actor"));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.SelfProtection));
        }

        [Test]
        public void RecklessAttacksEvenWhenSelfHealingIsLegal()
        {
            var action = FirstAction(Fight(Unit("actor", Side.Party, 1, Personality.Reckless, skill: Heal, hp: 3),
                Unit("enemy", Side.Enemy, 1)), "actor");
            Assert.That(action.ActionKind, Is.EqualTo(ActionKind.Attack));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.FavorAttack));
        }

        [Test]
        public void GentleHealsLowestHealthRatioAndSkipsDefeatedAllies()
        {
            var action = FirstAction(Fight(Unit("actor", Side.Party, 1, Personality.Gentle, skill: Heal, hp: 7),
                Unit("hurt", Side.Party, 2, hp: 6, maxHealth: 30),
                Unit("down", Side.Party, 3, hp: 0),
                Unit("enemy", Side.Enemy, 1)), "actor");
            Assert.That(action.TargetId, Is.EqualTo("hurt"));
            Assert.That(action.TargetHealth, Is.EqualTo(10));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.WoundedAlly));
        }

        [Test]
        public void CooperativeUsesLivingTargetPreviouslyAttackedByTeammate()
        {
            var result = Fight(Unit("leader", Side.Party, 1, basic: Attack("rear", TargetRule.RearEnemy)),
                Unit("support", Side.Party, 2, Personality.Cooperative),
                Unit("front", Side.Enemy, 1), Unit("back", Side.Enemy, 4));
            var action = FirstAction(result, "support");
            Assert.That(action.TargetId, Is.EqualTo("back"));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.FollowAlly));
        }

        [Test]
        public void CalmPicksLowestHealthRatioEvenWhenInFrontOfWeakerRawHealth()
        {
            var result = Fight(Unit("actor", Side.Party, 1, Personality.Calm),
                Unit("front", Side.Enemy, 1, hp: 5, maxHealth: 8),
                Unit("back", Side.Enemy, 4, hp: 7, maxHealth: 20));
            var action = FirstAction(result, "actor");
            Assert.That(action.TargetId, Is.EqualTo("back"));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.LowestHealth));
        }

        [Test]
        public void FixedRearTargetSkipsEmptyAndDefeatedSlotsAndChangesAfterSwap()
        {
            var enemy = Unit("enemy", Side.Enemy, 1, Personality.Calm,
                basic: Attack("rear", TargetRule.RearEnemy), interval: 1);
            var first = FirstAction(Fight(new[] { enemy, Unit("guard", Side.Party, 1, interval: 2),
                Unit("archer", Side.Party, 3, interval: 2), Unit("down", Side.Party, 4, hp: 0) }, 2), "enemy");
            var second = FirstAction(Fight(new[] { enemy, Unit("archer", Side.Party, 1, interval: 2),
                Unit("guard", Side.Party, 3, interval: 2), Unit("down", Side.Party, 4, hp: 0) }, 2), "enemy");
            Assert.That(first.TargetId, Is.EqualTo("archer"));
            Assert.That(second.TargetId, Is.EqualTo("guard"));
        }

        [Test]
        public void EachLivingUnitActsOnceAndOneRoundBreakSwapChangesTheRearTarget()
        {
            var enemy = Unit("enemy", Side.Enemy, 1, basic: Attack("rear", TargetRule.RearEnemy), attack: 1);
            var guard = Unit("guard", Side.Party, 1, interval: 2, attack: 1);
            var archer = Unit("archer", Side.Party, 3, interval: 4, attack: 1);
            var down = Unit("down", Side.Party, 4, hp: 0);
            var session = new BattleSimulator().Start(new BattleRequest("rounds", new[] { enemy, guard, archer, down }, 12));

            Assert.That(session.TrySwapPartyPositions(1, 3), Is.False);
            var first = session.AdvanceRound().Where(entry => entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(first.Select(entry => entry.ActorId), Is.EquivalentTo(new[] { "enemy", "guard", "archer" }));
            Assert.That(first.Last().ActorId, Is.EqualTo("enemy"));
            Assert.That(first.Last().TargetId, Is.EqualTo("archer"));
            Assert.That(session.TrySwapPartyPositions(1, 2), Is.False);
            Assert.That(session.TrySwapPartyPositions(1, 4), Is.False);
            Assert.That(session.TrySwapPartyPositions(1, 3), Is.True);
            Assert.That(session.TrySwapPartyPositions(1, 3), Is.False);

            var second = session.AdvanceRound().Where(entry => entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(second.Select(entry => entry.ActorId), Is.EquivalentTo(new[] { "enemy", "guard", "archer" }));
            Assert.That(second.Last().ActorId, Is.EqualTo("enemy"));
            Assert.That(second.Last().TargetId, Is.EqualTo("guard"));
            session.AdvanceRound();
            Assert.That(session.Result.PartyPositionsById["guard"], Is.EqualTo(3));
            Assert.That(session.TrySwapPartyPositions(1, 3), Is.False);
        }

        [Test]
        public void SkillWaitsForLegalTargetAndCooldownWhileBasicAttacksContinue()
        {
            var healer = Unit("healer", Side.Party, 1, Personality.Gentle, basic: Front, skill: Heal, attack: 1);
            var enemy = Unit("enemy", Side.Enemy, 1, attack: 3, maxHealth: 30, interval: 2);
            var actions = Fight(new[] { enemy, healer }, 8).Events
                .Where(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == healer.Id)
                .Select(entry => entry.ActionName).ToArray();
            Assert.That(actions, Is.EqualTo(new[] { "front", "heal", "front", "heal" }));
        }

        [Test]
        public void SeededRoundsCanBeReplayedAndShuffleEachSideIndependently()
        {
            var a = Unit("a", Side.Party, 1, maxHealth: 100);
            var b = Unit("b", Side.Party, 4, maxHealth: 100);
            var c = Unit("c", Side.Enemy, 1, maxHealth: 100);
            var d = Unit("d", Side.Enemy, 4, maxHealth: 100);
            var first = Fight(new[] { a, b, c, d }, 9);
            var second = Fight(new[] { d, c, b, a }, 9);
            Assert.That(first.Events, Is.EqualTo(second.Events));

            var rounds = first.Events.Where(entry => entry.Kind == BattleEventKind.Action).GroupBy(entry => entry.Tick)
                .Select(round => round.Select(entry => entry.ActorId).ToArray()).ToArray();
            Assert.That(rounds.All(round => round.Take(2).All(id => id == "a" || id == "b")), Is.True);
            Assert.That(rounds.All(round => round.Skip(2).All(id => id == "c" || id == "d")), Is.True);
            Assert.That(rounds.Select(round => round[0]).Distinct().Count(), Is.EqualTo(2));
            Assert.That(rounds.Select(round => round[2]).Distinct().Count(), Is.EqualTo(2));
            Assert.That(first.Outcome, Is.EqualTo(BattleOutcome.Timeout));
        }

        [Test]
        public void BattleStopsOnVictoryOrPartyDefeat()
        {
            var party = Unit("party", Side.Party, 1);
            var enemy = Unit("enemy", Side.Enemy, 1, hp: 1);
            var victory = Fight(party, enemy);
            Assert.That(victory.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(victory.HealthById["party"], Is.EqualTo(20));
            Assert.That(victory.Events.Select(entry => entry.Kind),
                Is.EqualTo(new[] { BattleEventKind.Action, BattleEventKind.Defeated, BattleEventKind.Finished }));

            var defeat = Fight(Unit("weak", Side.Party, 1, hp: 1), Unit("strong", Side.Enemy, 1));
            Assert.That(defeat.Outcome, Is.EqualTo(BattleOutcome.PartyDefeated));
            Assert.That(BattleSimulator.ResolveOutcome(false, false), Is.EqualTo(BattleOutcome.MutualDefeat));
        }

        [Test]
        public void InjectedDecisionCanPickFromLegalActionsOnly()
        {
            var actor = Unit("a", Side.Party, 1);
            var front = Unit("front", Side.Enemy, 1);
            var rear = Unit("rear", Side.Enemy, 4);
            var request = new BattleRequest("test", new[] { actor, front, rear }, 1);
            var result = new BattleSimulator(new FixedOptionPolicy(1)).Simulate(request);
            Assert.That(FirstAction(result, actor.Id).TargetId, Is.EqualTo(rear.Id));
            Assert.Throws<System.InvalidOperationException>(() => new BattleSimulator(new FixedOptionPolicy(9)).Simulate(request));
        }

        private sealed class FixedOptionPolicy : IDecisionPolicy
        {
            private readonly int index;
            public FixedOptionPolicy(int index) => this.index = index;
            public Decision Choose(DecisionContext context, System.Collections.Generic.IReadOnlyList<ActionOption> options)
                => new Decision(context.Actor.Side == Side.Party ? index : 0, DecisionReason.Default);
        }

        private static BattleEvent FirstAction(BattleResult result, string id) =>
            result.Events.First(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == id);

        private static BattleResult Fight(params Combatant[] units) => Fight(units, 1);

        private static BattleResult Fight(Combatant[] units, int ticks) =>
            new BattleSimulator(random: new System.Random(0)).Simulate(new BattleRequest("test", units, ticks));

        private static Combatant Unit(string id, Side side, int position, Personality personality = Personality.Reckless,
            ActionDefinition basic = null, ActionDefinition skill = null, int? hp = null, int maxHealth = 20,
            int defense = 0, int specialDefense = 0, int interval = 1, int attack = 5)
        {
            var stats = new Stats(attack, defense, specialDefense, maxHealth);
            var definition = new CharacterDefinition(id, id, Profession.Warrior, personality,
                stats, interval, basic ?? Basic, skill);
            return new Combatant(id, definition, side, position, stats, hp ?? maxHealth);
        }

        private static ActionDefinition Attack(string id, TargetRule target, int power = 0, int cooldown = 0,
            DamageType type = DamageType.Physical) => new ActionDefinition(id, id, ActionKind.Attack, target, power, cooldown, type);
    }
}
