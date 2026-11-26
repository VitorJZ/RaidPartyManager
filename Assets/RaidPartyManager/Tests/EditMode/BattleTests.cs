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
        public void DefaultPolicyChoosesHighestDamageAcrossAttackTypesAndTargets()
        {
            var magic = Attack("magic", TargetRule.AnyEnemy, 2, 3, DamageType.Magic);
            var result = Fight(Unit("actor", Side.Party, 1, Personality.Competitive, skill: magic),
                Unit("front", Side.Enemy, 1, defense: 1, specialDefense: 8),
                Unit("back", Side.Enemy, 4, defense: 8, specialDefense: 0));

            var action = FirstAction(result, "actor");
            Assert.That(action.ActionName, Is.EqualTo("magic"));
            Assert.That(action.TargetId, Is.EqualTo("back"));
            Assert.That(action.Amount, Is.EqualTo(7));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.HighestDamage));
        }

        [Test]
        public void ReadyShieldPrecedesBasicAttack()
        {
            var action = FirstAction(Fight(Unit("actor", Side.Party, 1, Personality.Worrier, skill: Guard),
                Unit("enemy", Side.Enemy, 1)), "actor");
            Assert.That(action.ActionKind, Is.EqualTo(ActionKind.Shield));
            Assert.That(action.TargetId, Is.EqualTo("actor"));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.SelfProtection));
        }

        [Test]
        public void HealingTargetsLowestHealthRatioAndSkipsDefeatedAllies()
        {
            var action = FirstAction(Fight(Unit("actor", Side.Party, 1, Personality.Caring, skill: Heal, hp: 7),
                Unit("hurt", Side.Party, 2, hp: 6, maxHealth: 30),
                Unit("down", Side.Party, 3, hp: 0),
                Unit("enemy", Side.Enemy, 1)), "actor");
            Assert.That(action.TargetId, Is.EqualTo("hurt"));
            Assert.That(action.TargetHealth, Is.EqualTo(10));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.WoundedAlly));
        }

        [Test]
        public void AllEmployeePersonalitiesMakeTheSameBattleDecision()
        {
            foreach (Personality personality in System.Enum.GetValues(typeof(Personality)))
            {
                var action = FirstAction(Fight(Unit("actor", Side.Party, 1, personality, skill: Heal, hp: 3),
                    Unit("enemy", Side.Enemy, 1)), "actor");
                Assert.That(action.ActionKind, Is.EqualTo(ActionKind.Heal), personality.ToString());
            }
        }

        [Test]
        public void FixedRearTargetSkipsEmptyAndDefeatedSlotsAndChangesAfterSwap()
        {
            var enemy = Unit("enemy", Side.Enemy, 1, Personality.Stoic,
                basic: Attack("rear", TargetRule.RearEnemy), interval: 1);
            var first = FirstAction(Fight(new[] { enemy, Unit("guard", Side.Party, 1, interval: 2),
                Unit("archer", Side.Party, 3, interval: 2), Unit("down", Side.Party, 4, hp: 0) }, 2), "enemy");
            var second = FirstAction(Fight(new[] { enemy, Unit("archer", Side.Party, 1, interval: 2),
                Unit("guard", Side.Party, 3, interval: 2), Unit("down", Side.Party, 4, hp: 0) }, 2), "enemy");
            Assert.That(first.TargetId, Is.EqualTo("archer"));
            Assert.That(second.TargetId, Is.EqualTo("guard"));
        }

        [Test]
        public void EachLivingUnitActsOnceAndTheRearTargetKeepsItsSlot()
        {
            var enemy = Unit("enemy", Side.Enemy, 1, basic: Attack("rear", TargetRule.RearEnemy), attack: 1);
            var guard = Unit("guard", Side.Party, 1, interval: 2, attack: 1);
            var archer = Unit("archer", Side.Party, 3, interval: 4, attack: 1);
            var down = Unit("down", Side.Party, 4, hp: 0);
            var session = new BattleSimulator().Start(new BattleRequest("rounds", new[] { enemy, guard, archer, down }, 12));

            var first = session.AdvanceRound().Where(entry => entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(first.Select(entry => entry.ActorId), Is.EquivalentTo(new[] { "enemy", "guard", "archer" }));
            Assert.That(first.Single(entry => entry.ActorId == "enemy").TargetId, Is.EqualTo("archer"));

            var second = session.AdvanceRound().Where(entry => entry.Kind == BattleEventKind.Action).ToArray();
            Assert.That(second.Select(entry => entry.ActorId), Is.EquivalentTo(new[] { "enemy", "guard", "archer" }));
            Assert.That(second.Single(entry => entry.ActorId == "enemy").TargetId, Is.EqualTo("archer"));
            session.AdvanceRound();
            Assert.That(session.Result.PartyPositionsById["guard"], Is.EqualTo(1));
        }

        [Test]
        public void SkillWaitsForLegalTargetAndCooldownWhileBasicAttacksContinue()
        {
            var healer = Unit("healer", Side.Party, 1, Personality.Caring, basic: Front, skill: Heal, attack: 1);
            var enemy = Unit("enemy", Side.Enemy, 1, attack: 3, maxHealth: 30, interval: 2);
            var actions = Fight(new[] { enemy, healer }, 8).Events
                .Where(entry => entry.Kind == BattleEventKind.Action && entry.ActorId == healer.Id)
                .Select(entry => entry.ActionName).ToArray();
            Assert.That(actions, Is.EqualTo(new[] { "front", "heal", "front", "heal" }));
        }

        [Test]
        public void SeededRoundsReplayWithAnAlliedOpenerAndMixedTurns()
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
            Assert.That(rounds.All(round => round[0] is "a" or "b"), Is.True);
            Assert.That(rounds.Any(round => round[1] is "c" or "d"), Is.True);
            Assert.That(rounds.Select(round => round[0]).Distinct().Count(), Is.EqualTo(2));
            foreach (var round in rounds)
                Assert.That(round, Is.EquivalentTo(new[] { "a", "b", "c", "d" }));
            Assert.That(first.Outcome, Is.EqualTo(BattleOutcome.Timeout));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AStunnedAllyDoesNotTakeTheOpeningTurn(bool bothAlliesStunned)
        {
            var a = Unit("a", Side.Party, 1);
            var b = Unit("b", Side.Party, 2);
            var c = Unit("c", Side.Enemy, 1);
            var round = Fight(Stunned(a), bothAlliesStunned ? Stunned(b) : b, c)
                .Events.Where(entry => entry.Kind == BattleEventKind.Action).Select(entry => entry.ActorId);
            Assert.That(round, Is.EqualTo(bothAlliesStunned ? new[] { "c" } : new[] { "b", "c" }));

            static Combatant Stunned(Combatant unit) => new(unit.Id, unit.Character, unit.Side, unit.Position,
                unit.Stats, unit.Health, startingStatuses: new System.Collections.Generic.Dictionary<StatusKind, int>
                { [StatusKind.Stunned] = 1 });
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
                Is.EqualTo(new[] { BattleEventKind.RoundStarted, BattleEventKind.Action, BattleEventKind.Defeated, BattleEventKind.Finished }));

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

        [Test]
        public void ProtectiveSkillsReportBadgesUntilTheyAreSpent()
        {
            var bell = new SkillBookDefinition("bell", "金钟罩", "", SkillRequirement.None, 2, 1, SkillEffect.GoldenBell);
            var stone = new SkillBookDefinition("stone", "坚如磐石", "", SkillRequirement.None, 1, 3, SkillEffect.StoneGuard);
            Combatant Learner(string id, int position, SkillBookDefinition book)
            {
                // No attack of their own, so the decision policy always prefers casting the protective skill.
                var unit = Unit(id, Side.Party, position, maxHealth: 40, attack: 0);
                return new Combatant(unit.Id, unit.Character, unit.Side, position, unit.Stats, unit.Health, learnedSkills: new[] { book });
            }
            var enemy = Unit("enemy", Side.Enemy, 1, maxHealth: 200, attack: 9);
            var battle = new BattleSimulator().Start(new BattleRequest("protect", new[] { Learner("bell", 1, bell), Learner("stone", 2, stone), enemy }, 6));
            var events = battle.AdvanceRound().Concat(battle.AdvanceRound()).ToList();

            int Index(System.Func<BattleEvent, bool> match) => events.FindIndex(entry => match(entry));
            Assert.That(Index(e => e.Countdown is { Kind: BattleCountdownKind.StoneGuard, Remaining: 1 } && e.TargetId == "stone"), Is.GreaterThanOrEqualTo(0));
            Assert.That(Index(e => e.Countdown is { Kind: BattleCountdownKind.GoldenBell, Remaining: 1 } && e.TargetId == "bell"), Is.GreaterThanOrEqualTo(0));
            Assert.That(events.Any(e => e.ActorId == "enemy" && e.Detail == BattleSession.GoldenBellBlocked), Is.True);

            var alone = new BattleSimulator().Start(new BattleRequest("stone", new[] { Learner("stone", 1, stone), enemy }, 6));
            events = alone.AdvanceRound().Concat(alone.AdvanceRound()).ToList();
            int broken = Index(e => e.ActorId == "enemy" && e.TargetId == "stone" && e.Detail == BattleSession.StoneGuardBlocked);
            Assert.That(broken, Is.GreaterThanOrEqualTo(0));
            Assert.That(events[broken + 1].Countdown, Is.EqualTo(new BattleCountdown(BattleCountdownKind.StoneGuard, 0)),
                "The stone guard badge comes off right after the hit it absorbed.");
        }

        [Test]
        public void ChaliceCapacityDoesNotChargeAgainForAnAlreadyAffordableSkill()
        {
            var baseUnit = Unit("party", Side.Party, 1, maxHealth: 100);
            var enemy = Unit("enemy", Side.Enemy, 1, maxHealth: 200, attack: 1);
            var book = new SkillBookDefinition("warcry", "战争咆哮", "", SkillRequirement.None, 4, 2, SkillEffect.WarCry);
            var cup = new EquipmentDefinition("cup", "余响圣杯", default, effect: EquipmentEffect.EchoChalice);
            var hero = new Combatant(baseUnit.Id, baseUnit.Character, baseUnit.Side, 1, baseUnit.Stats, baseUnit.Health,
                new[] { cup }, learnedSkills: new[] { book });
            var battle = new BattleSimulator().Start(new BattleRequest("capacity", new[] { hero, enemy }, 6));
            for (int round = 1; round <= 4; round++)
            {
                battle.AdvanceRound();
                Assert.That(battle.ReadPartyEnergy().Single().Value.Capacity, Is.EqualTo(4));
            }
        }

        [TestCase(false, 2)]
        [TestCase(true, 4)]
        public void SkillEnergyLastsTheWholeBattleAndCastsReplaceAttacksAndCounters(bool hasChalice, int capacity)
        {
            var baseUnit = Unit("party", Side.Party, 1, maxHealth: 100);
            var enemy = Unit("enemy", Side.Enemy, 1, maxHealth: 200, attack: 1);
            var book = new SkillBookDefinition("warcry", "战争咆哮", "", SkillRequirement.None, 1, 2, SkillEffect.WarCry);
            var horn = new EquipmentDefinition("horn", "反击的号角", default, effect: EquipmentEffect.CounterHorn);
            var chalice = new EquipmentDefinition("chalice", "余响圣杯", default, effect: EquipmentEffect.EchoChalice);
            var party = new Combatant(baseUnit.Id, baseUnit.Character, baseUnit.Side, baseUnit.Position, baseUnit.Stats,
                baseUnit.Health, hasChalice ? new[] { horn, chalice } : new[] { horn }, learnedSkills: new[] { book });
            var battle = new BattleSimulator(new FixedOptionPolicy(0)).Start(new BattleRequest("energy", new[] { party, enemy }, 9));

            Assert.That(battle.ReadPartyEnergy().Single().Value.Remaining, Is.EqualTo(capacity));
            var casts = new System.Collections.Generic.List<int>();
            while (battle.Result == null)
            {
                var round = battle.AdvanceRound();
                foreach (var cast in round.Where(entry => entry.ActorEnergy.HasValue))
                {
                    var energy = cast.ActorEnergy.Value;
                    Assert.That(energy.Capacity, Is.EqualTo(capacity));
                    casts.Add(energy.Remaining);
                    Assert.That(round.Any(entry => entry.ActorId == party.Id && entry.ActionKind == ActionKind.Attack), Is.False);
                }
            }
            Assert.That(casts, Is.EqualTo(Enumerable.Range(0, capacity).Reverse()));
            Assert.That(battle.Result.Events.Any(entry => entry.ActorId == party.Id && entry.ActionKind == ActionKind.Attack &&
                entry.Kind == BattleEventKind.Action && entry.Amount == 10), Is.True);
            Assert.That(battle.ReadPartyEnergy().Single().Value.Remaining, Is.EqualTo(capacity));
        }

        [TestCase(SkillEffect.LightningWhip, 2)]
        [TestCase(SkillEffect.WarCry, 1)]
        public void DefaultPolicyFinishesAWeakenedEnemyWithABasicAttackAndSavesEnergy(SkillEffect effect, int cost)
        {
            var actor = Unit("caster", Side.Party, 1);
            var book = new SkillBookDefinition("book", "book", "", SkillRequirement.None, cost, 1, effect);
            var caster = new Combatant(actor.Id, actor.Character, actor.Side, 1, actor.Stats, actor.Health,
                learnedSkills: new[] { book });
            var enemy = Unit("enemy", Side.Enemy, 1, hp: 3);

            var battle = new BattleSimulator().Start(new BattleRequest("save-energy", new[] { caster, enemy }, 1));
            var round = battle.AdvanceRound();

            Assert.That(round.Single(e => e.ActorId == caster.Id && e.Kind == BattleEventKind.Action).ActionName, Is.EqualTo("basic"));
            Assert.That(round.Any(e => e.ActorEnergy.HasValue), Is.False);
            Assert.That(battle.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
        }

        [TestCase(19, false)]
        [TestCase(3, true)]
        public void DefaultPolicyUsesHealingForASeverelyWoundedAllyButSkipsMinorInjuries(int allyHealth, bool shouldHeal)
        {
            var actor = Unit("caster", Side.Party, 2);
            var sunshine = new SkillBookDefinition("sunshine", "阳光普照", "", SkillRequirement.None, 1, 2, SkillEffect.Sunshine);
            var caster = new Combatant(actor.Id, actor.Character, actor.Side, 2, actor.Stats, actor.Health,
                learnedSkills: new[] { sunshine });
            var ally = Unit("ally", Side.Party, 1, hp: allyHealth);
            var enemy = Unit("enemy", Side.Enemy, 1, attack: 0, maxHealth: 40);
            var result = Fight(caster, ally, enemy);

            Assert.That(FirstAction(result, caster.Id).SkillBookId, Is.EqualTo(shouldHeal ? sunshine.Id : null));
            if (shouldHeal)
                Assert.That(result.Events.Single(e => e.SkillBookId == sunshine.Id && e.ActionKind == ActionKind.Heal).Amount, Is.EqualTo(3));
        }

        [Test]
        public void TwoEnergySkillUsesFireDefenseForEveryTargetAndFallsBackToBasicAttack()
        {
            var actor = Unit("caster", Side.Party, 1, maxHealth: 50);
            var book = new SkillBookDefinition("fire", "熊熊烈火", "", SkillRequirement.Mage, 2, 1, SkillEffect.BlazingFire);
            var mage = new CharacterDefinition("caster", "caster", Profession.Mage, Personality.Theatrical,
                actor.Stats, 1, actor.Character.BasicAttack);
            var caster = new Combatant(actor.Id, mage, actor.Side, 1, actor.Stats, actor.Health, learnedSkills: new[] { book });
            var front = Unit("front", Side.Enemy, 1, defense: 2, specialDefense: 2);
            var back = Unit("back", Side.Enemy, 4, defense: 2, specialDefense: 4);

            var result = Fight(new[] { caster, front, back }, 2);
            var cast = result.Events.Single(entry => entry.ActorEnergy.HasValue);
            Assert.That(cast.ActorEnergy.Value.Remaining, Is.Zero);
            var targets = result.Events.Where(entry => entry.ActionName == book.Name && entry.ActionKind == ActionKind.Attack);
            Assert.That(targets.Select(entry => (entry.TargetId, entry.Amount)), Is.EqualTo(new[] { (front.Id, 4), (back.Id, 2) }));
            Assert.That(result.Events.Where(entry => entry.ActorId == actor.Id && entry.Kind == BattleEventKind.Action)
                .Select(entry => (entry.Tick, entry.ActionName)), Is.EqualTo(new[] { (1, book.Name), (2, "basic") }));
        }

        [Test]
        public void OrdinaryEnemyTargetingIgnoresTheAuthoredPersonality()
        {
            var enemy = Unit("enemy", Side.Enemy, 1, Personality.Competitive);
            var action = FirstAction(Fight(enemy, Unit("front", Side.Party, 1, defense: 20),
                Unit("back", Side.Party, 4)), enemy.Id);
            Assert.That(action.TargetId, Is.EqualTo("front"));
            Assert.That(action.Reason, Is.EqualTo(DecisionReason.Default));
        }

        [TestCase(Side.Party, false)]
        [TestCase(Side.Enemy, false)]
        [TestCase(Side.Enemy, true)]
        public void RangedBasicAttacksCanHitAnyLivingOpponent(Side side, bool configured)
        {
            var foe = side == Side.Party ? Side.Enemy : Side.Party;
            var behavior = configured ? new EnemyBehaviorDefinition(new[] {
                new EnemyActionDefinition("shot", 100, EnemyActionKind.Attack, EnemyTargetRule.Front)
            }) : null;
            var archer = Unit("archer", side, 1, reach: AttackReach.Ranged, enemyBehavior: behavior);
            var front = Unit("front", foe, 1, defense: 15);
            var rear = Unit("rear", foe, 3);
            var defeated = Unit("defeated", foe, 4, hp: 0);
            var request = new BattleRequest("ranged-targeting", new[] { archer, front, rear, defeated }, 1);

            var targets = Enumerable.Range(0, 32).Select(seed => FirstAction(
                new BattleSimulator(random: new System.Random(seed)).Simulate(request), archer.Id).TargetId).Distinct();

            Assert.That(targets, Is.EquivalentTo(new[] { front.Id, rear.Id }));
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

        private static Combatant Unit(string id, Side side, int position, Personality personality = Personality.Frank,
            ActionDefinition basic = null, ActionDefinition skill = null, int? hp = null, int maxHealth = 20,
            int defense = 0, int specialDefense = 0, int interval = 1, int attack = 5,
            AttackReach reach = AttackReach.Unspecified, EnemyBehaviorDefinition enemyBehavior = null)
        {
            var stats = new Stats(attack, defense, specialDefense, maxHealth);
            var definition = new CharacterDefinition(id, id, Profession.Warrior, personality,
                stats, interval, basic ?? Basic, skill, reach: reach, enemyBehavior: enemyBehavior);
            return new Combatant(id, definition, side, position, stats, hp ?? maxHealth);
        }

        private static ActionDefinition Attack(string id, TargetRule target, int power = 0, int cooldown = 0,
            DamageType type = DamageType.Physical) => new ActionDefinition(id, id, ActionKind.Attack, target, power, cooldown, type);
    }
}
