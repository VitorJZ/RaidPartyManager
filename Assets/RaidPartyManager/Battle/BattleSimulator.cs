using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Battle
{
    public sealed class BattleSimulator
    {
        private readonly IDecisionPolicy policy;
        private readonly Random random;

        public BattleSimulator(IDecisionPolicy policy = null, Random random = null)
        {
            this.policy = policy ?? new DefaultBattlePolicy();
            this.random = random ?? new Random();
        }

        public BattleSession Start(BattleRequest request) => new BattleSession(request, policy, random);

        public BattleResult Simulate(BattleRequest request)
        {
            var battle = Start(request);
            while (battle.Result == null)
                battle.AdvanceRound();
            return battle.Result;
        }

        public static BattleOutcome? ResolveOutcome(bool partyAlive, bool enemiesAlive)
        {
            if (!partyAlive)
                return enemiesAlive ? BattleOutcome.PartyDefeated : BattleOutcome.MutualDefeat;
            return enemiesAlive ? (BattleOutcome?)null : BattleOutcome.Victory;
        }
    }

    public sealed partial class BattleSession
    {
        private sealed class EquippedItem
        {
            public string Id;
            public EquipmentDefinition Definition;
            public int Transfers;
            public bool Disabled;
        }

        private sealed class Unit
        {
            public Combatant Input { get; private set; }
            public readonly Combatant Original;
            public BattleRating Rating;
            public readonly LayerSources PoisonSources = new();
            public readonly LayerSources ShieldSources = new();
            public readonly Dictionary<StatusKind, int> Statuses;
            public HashSet<StatusKind> DelayStatusDecay;
            public CharacterDefinition CopiedTraitSource;
            public int AttackGrowth;
            public int GourdAttack;
            public int PermanentAttack;
            public int GrowthTriggers;
            public int BorrowedAttack;
            public int BlockedRound;
            public int AttackBlockedRound;
            public int LastAttackRound;
            public bool CrownTriggered, ReliquaryWard;
            public int LastSkillRound;
            public readonly Dictionary<string, int> LastBookCast = new Dictionary<string, int>();
            public int ProtectedRound;
            public Unit BellCaster;
            public int BonusMaximumHealth;
            public bool DoubleNextAttack;
            public SkillBookDefinition SweepSkill;
            public bool StoneGuard;
            public bool CorpseConsumed;
            public BattleEnergy Energy;
            private int spentEnergy;
            private int chargeGained;
            public PlayerTrait Talent => Original.Talent;
            public bool TalentUsed;
            public int WatchRound, MirrorShield, Intercepts, InterceptUntil, ThunderRound;
            public bool FirstTrigger(PlayerTrait trait)
            {
                if (Talent != trait || TalentUsed) return false;
                return TalentUsed = true;
            }

            // Passive capacity is available immediately. Round charging only bridges the gap to a learned skill.
            public int ChargeTarget => Math.Max(Energy.Capacity,
                Original.LearnedSkills.Select(book => book.Cost).DefaultIfEmpty(0).Max());

            public bool Charge()
            {
                if (Energy.Capacity >= ChargeTarget) return false;
                chargeGained++;
                Energy = new BattleEnergy(Energy.Capacity + 1, Energy.Remaining + 1);
                return true;
            }
            public EquippedItem[] Slots;
            public readonly HashSet<string> DamagedTargets = new HashSet<string>();
            public string LastHitTarget;
            public bool Revived;
            public bool ResolvingDamageHeal;
            public bool ResolvingCrystalHeal;
            public int BellRings;
            public bool EnemyTriggered;

            public CharacterDefinition TraitSource => CopiedTraitSource ?? Input.Character;
            public CharacterTrait Trait => TraitSource.Trait;
            public int TraitPower => TraitSource.TraitPower;
            public int Defense => StatAfterRebirth(Stats.Defense);
            public int SpecialDefense => StatAfterRebirth(Stats.SpecialDefense);
            public int StatAfterRebirth(long value) =>
                (int)Math.Clamp(Revived && Trait == CharacterTrait.Rebirth ? 2 * value : value, int.MinValue, int.MaxValue);
            public bool CanBeTargeted => Trait != CharacterTrait.AbyssWard;
            public bool CanReceiveHealing => CanBeTargeted && Layers(StatusKind.Cursed) == 0;
            public EquipmentDefinition[] Gear;
            public Unit LinkedFront;
            public Stats Stats;
            public int Health;
            public int Shield;
            public bool HasAttacked;
            public long SkillReadyAt;

            public Unit(Combatant input)
            {
                Input = Original = input;
                Health = input.Health;
                Stats = input.Stats;
                Gear = input.Equipment.ToArray();
                Energy = BattleEnergy.Full(Gear, input.BonusEnergy);
                Slots = new EquippedItem[Math.Max(Math.Max(Rules.PersonalEquipmentSlots(input.Talent, input.ExtraEquipmentSlot), input.EquipmentSlots.Count), Gear.Length)];
                if (input.EquipmentSlots.Count == 0)
                {
                    for (int index = 0; index < Gear.Length; index++)
                        Slots[index] = new EquippedItem { Definition = Gear[index] };
                }
                else
                {
                    int item = 0;
                    for (int slot = 0; slot < input.EquipmentSlots.Count; slot++)
                    {
                        string id = input.EquipmentSlots[slot];
                        if (id != null)
                            Slots[slot] = new EquippedItem { Id = id, Definition = Gear[item++] };
                    }
                }
                Statuses = input.StartingStatuses.ToDictionary(status => status.Key, status => status.Value);
                PoisonSources.Add(null, Layers(StatusKind.Poison));
            }

            public void ChangeForm(CharacterDefinition form)
            {
                var before = Input.Character.BaseStats;
                var next = form.BaseStats;
                var stats = Input.Stats + new Stats(next.Attack - before.Attack, next.Defense - before.Defense,
                    next.SpecialDefense - before.SpecialDefense, next.Health - before.Health);
                Health = Math.Min(Health, stats.Health + BonusMaximumHealth);
                Input = new Combatant(Input.Id, form, Input.Side, Input.Position, stats, Math.Min(Health, stats.Health), Input.Equipment,
                    bonusEnergy: Original.BonusEnergy, extraEquipmentSlot: Original.ExtraEquipmentSlot);
                GrowthTriggers = 0;
                SkillReadyAt = 0;
            }

            public IEnumerable<EquippedItem> Equipped => Slots.Where(item => item != null && !item.Disabled);

            public void ApplySlotChanges()
            {
                var oldBonus = Input.Equipment.Aggregate(default(Stats), (total, item) => total + item.Bonus);
                var equipment = Equipped.Select(item => item.Definition).ToArray();
                var newBonus = equipment.Aggregate(default(Stats), (total, item) => total + item.Bonus);
                var previous = Input.Stats;
                var stats = new Stats(previous.Attack - oldBonus.Attack + newBonus.Attack,
                    previous.Defense - oldBonus.Defense + newBonus.Defense,
                    previous.SpecialDefense - oldBonus.SpecialDefense + newBonus.SpecialDefense,
                    previous.Health - oldBonus.Health + newBonus.Health);
                Health = Math.Min(Health, stats.Health + BonusMaximumHealth);
                Input = new Combatant(Input.Id, Input.Character, Input.Side, Input.Position, stats, Math.Min(Health, stats.Health), equipment,
                    bonusEnergy: Original.BonusEnergy, extraEquipmentSlot: Original.ExtraEquipmentSlot);
                int capacity = Math.Min(Rules.MaximumEnergy, chargeGained + BattleEnergy.Full(equipment, Original.BonusEnergy).Capacity);
                Energy = new BattleEnergy(capacity, capacity - spentEnergy);
            }

            public void SpendEnergy(int cost)
            {
                spentEnergy = Math.Clamp(spentEnergy + cost, 0, Energy.Capacity);
                Energy = new BattleEnergy(Energy.Capacity, Energy.Capacity - spentEnergy);
            }

            public Combatant EquipmentInput() =>
                new Combatant(Input.Id, Input.Character, Input.Side, Input.Position,
                    PlayerMarket.GearAndPosition(Talent, Slots.Count(item => item != null), Input.Position, Input.Stats) + new Stats(AttackGrowth + GourdAttack + PermanentAttack + BorrowedAttack,
                        Trait == CharacterTrait.EnemyAnchorHermit && EnemyTriggered ? -2 : 0, 0, BonusMaximumHealth), Health, Input.Equipment);

            public Combatant RecruitmentResult() =>
                new Combatant(Original.Id, Original.Character, Original.Side, Input.Position, Original.Stats + new Stats(0, 0, 0, BonusMaximumHealth),
                    Math.Min(Health, Original.Stats.Health + BonusMaximumHealth), Original.Equipment);

            public int Count(EquipmentEffect effect) => Gear.Count(item => item.Effect == effect);
            public bool Has(EquipmentEffect effect) => Gear.Any(item => item.Effect == effect);
            public int Layers(StatusKind status) => Statuses.GetValueOrDefault(status);

            public Combatant Snapshot() =>
                new Combatant(Input.Id, Input.Character, Input.Side, Input.Position, Stats, Health, Input.Equipment, bonusEnergy: Original.BonusEnergy, extraEquipmentSlot: Original.ExtraEquipmentSlot);
        }

        private readonly BattleRequest request;
        private readonly IDecisionPolicy policy;
        private readonly Random random;
        private readonly Dictionary<string, Unit> unitsById;
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private readonly Dictionary<(string Unit, BattleCountdownKind Kind), int> countdowns = new();
        private readonly List<Unit> recruits = new List<Unit>();
        private readonly List<Unit> units;
        private readonly Dictionary<string, Stats> reportedPartyStats = new Dictionary<string, Stats>();
        private int equipmentGold;
        private readonly HashSet<string> consumedEquipment = new HashSet<string>();
        private BattleLoadout currentLoadout;
        public BattleLoadout OpeningLoadout { get; private set; }

        public string Id => request.Id;
        public int Round { get; private set; }
        public int Tick { get; private set; }
        public BattleResult Result { get; private set; }
        public IReadOnlyList<Combatant> Formation { get; private set; }

        internal BattleSession(BattleRequest request, IDecisionPolicy policy, Random random)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.policy = policy;
            this.random = random;
            units = request.Combatants.Select(input => new Unit(input)).ToList();
            unitsById = units.ToDictionary(unit => unit.Input.Id);
            GrantChorusSlots();
            UpdateFormation();
            foreach (var entry in ReadPartyStats())
                reportedPartyStats.Add(entry.Key, entry.Value);
        }

        public IEnumerable<KeyValuePair<string, BattleEnergy>> ReadPartyEnergy() => units
            .Where(unit => unit.Input.Side == Side.Party)
            .Select(unit => new KeyValuePair<string, BattleEnergy>(unit.Input.Id, unit.Energy));

        public IEnumerable<(string Id, int Health, int MaximumHealth)> ReadHealth() =>
            units.Select(unit => (unit.Input.Id, unit.Health, unit.Stats.Health));

        public IEnumerable<KeyValuePair<string, Stats>> ReadPartyStats()
        {
            // Reports can precede the next equipment refresh. Read the derived stats without changing combat state.
            var living = units.Where(unit => unit.Health > 0).Select(unit => unit.EquipmentInput()).ToArray();
            var formationStats = EquipmentFormation.Resolve(living);
            foreach (var unit in units.Where(unit => unit.Input.Side == Side.Party))
            {
                string id = unit.Input.Id;
                var stats = formationStats.TryGetValue(id, out var current) ? current : unit.Stats;
                int attack = EffectiveAttack(unit, stats.Attack);
                yield return new KeyValuePair<string, Stats>(id,
                    new Stats(attack, unit.StatAfterRebirth(stats.Defense), unit.StatAfterRebirth(stats.SpecialDefense), stats.Health));
            }
        }

        private IReadOnlyList<KeyValuePair<string, Stats>> RecordPartyStatChanges()
        {
            List<KeyValuePair<string, Stats>> changes = null;
            foreach (var entry in ReadPartyStats())
            {
                if (reportedPartyStats.TryGetValue(entry.Key, out var previous) && previous.Equals(entry.Value))
                    continue;
                reportedPartyStats[entry.Key] = entry.Value;
                (changes ??= new List<KeyValuePair<string, Stats>>()).Add(entry);
            }
            return changes?.AsReadOnly();
        }

        public IReadOnlyList<BattleEvent> AdvanceRound()
        {
            if (Result != null)
                throw new InvalidOperationException("The battle has already ended.");

            int firstEvent = events.Count;
            if (Round++ == 0)
                StartBattle();

            if (!FinishIfDecided() && AdvanceClock())
            {
                events.Add(new BattleEvent(Tick, BattleEventKind.RoundStarted));
                if (Round == 1)
                    foreach (var unit in units.Where(unit => unit.Health > 0 && unit.Trait == CharacterTrait.LightningChant))
                        ReportCountdown(unit, BattleCountdownKind.CalamityChant, 3);
                StartRound();
                if (FinishIfDecided())
                    return events.GetRange(firstEvent, events.Count - firstEvent);
                var frozenTurns = new List<Unit>();
                foreach (var actor in RoundOrder())
                {
                    if (CanActThisRound(actor, ignoreFreeze: true))
                    {
                        if (actor.Layers(StatusKind.Frozen) > 0)
                            frozenTurns.Add(actor);
                        else
                            Act(actor);
                    }
                    PlayThawedTurns(frozenTurns);
                    if (FinishIfDecided())
                        return events.GetRange(firstEvent, events.Count - firstEvent);
                }

                EndRound(frozenTurns);
                foreach (var unit in units.Where(unit => unit.Health > 0))
                {
                    if (unit.Trait == CharacterTrait.LightningChant)
                        ReportCountdown(unit, BattleCountdownKind.CalamityChant, 3 - Round % 3);
                    if (unit.ProtectedRound == Round)
                        ReportCountdown(unit, BattleCountdownKind.GoldenBell, 0);
                }
                foreach (var (scavenger, stash) in scavengedItems)
                    ReportCountdown(scavenger, BattleCountdownKind.ScavengerEscape, stash.EscapeRound - Round);

                if (!FinishIfDecided() && (long)Tick + RoundInterval() > request.TickLimit)
                    Finish(BattleOutcome.Timeout, request.TickLimit);
            }

            return events.GetRange(firstEvent, events.Count - firstEvent);
        }

        private bool AdvanceClock()
        {
            long nextTick = (long)Tick + RoundInterval();
            if (nextTick > request.TickLimit)
            {
                Finish(BattleOutcome.Timeout, request.TickLimit);
                return false;
            }

            Tick = (int)nextTick;
            return true;
        }

        private int RoundInterval() => units.Where(unit => unit.Health > 0)
            .Max(unit => unit.Input.Character.ActionInterval);

        private bool CanActThisRound(Unit unit, bool ignoreFreeze = false) =>
            unit.Health > 0 && !(Round == 1 && unit.Input.Id == request.PallbearerId) && unit.BlockedRound != Round &&
            unit.Layers(StatusKind.Stunned) == 0 && (ignoreFreeze || unit.Layers(StatusKind.Frozen) == 0);

        private bool CanAttackThisRound(Unit unit) => unit.AttackBlockedRound != Round;

        private void PlayThawedTurns(List<Unit> frozenTurns)
        {
            // Check again after each recovered turn, which can thaw someone else waiting in line.
            int ready;
            while ((ready = frozenTurns.FindIndex(unit => CanActThisRound(unit))) >= 0)
            {
                var actor = frozenTurns[ready];
                if (!Opponents(actor).Any())
                    return;
                frozenTurns.RemoveAt(ready);
                Act(actor);
            }
        }

        private IEnumerable<Unit> RoundOrder()
        {
            var living = units.Where(unit => unit.Health > 0).ToArray();
            var party = living.Where(unit => unit.Input.Side == Side.Party && CanActThisRound(unit)).ToArray();
            var opener = party.Length > 1 ? party[random.Next(party.Length)] : party.FirstOrDefault();
            // Fix only the opening slot. Shuffle the rest together, regardless of side.
            var remaining = living.Where(unit => unit != opener).ToArray();
            Shuffle(remaining);
            return opener == null ? remaining : remaining.Prepend(opener);
        }

        private void Shuffle(Unit[] order)
        {
            for (int last = order.Length - 1; last > 0; last--)
            {
                int index = random.Next(last + 1);
                (order[last], order[index]) = (order[index], order[last]);
            }
        }

        private static IEnumerable<Unit> InEquipmentOrder(IEnumerable<Unit> candidates) =>
            candidates.OrderBy(unit => unit.Input.Position).ThenBy(unit => unit.Input.Side);

        private IEnumerable<Unit> Opponents(Unit actor) =>
            units.Where(unit => unit.Input.Side != actor.Input.Side && unit.Health > 0);

        private IEnumerable<Unit> DirectOpponents(Unit actor) =>
            Opponents(actor).Where(unit => unit.CanBeTargeted);

        private IEnumerable<Unit> TargetableOpponents(Unit actor)
        {
            var enemies = DirectOpponents(actor).ToArray();
            var guards = enemies.Where(unit => unit.Trait == CharacterTrait.DrawAttacks && unit.Health >= unit.TraitPower).ToArray();
            if (guards.Length > 0)
                return guards;
            var visible = enemies.Where(unit => !unit.Has(EquipmentEffect.StealthCloak)).ToArray();
            return visible.Length > 0 ? visible : enemies;
        }

        private bool IsMelee(Unit actor) => UsesShortbow(actor) || actor.Input.Character.Reach == AttackReach.Melee;

        private static bool IsRanged(Unit actor) => actor.Input.Character.Reach == AttackReach.Ranged && !UsesShortbow(actor);

        private Unit RandomTarget(IEnumerable<Unit> candidates)
        {
            var targets = candidates as Unit[] ?? candidates.ToArray();
            return targets.Length > 1 ? targets[random.Next(targets.Length)] : targets.FirstOrDefault();
        }

        private IEnumerable<Unit> FrontAttackTargets(Unit actor)
        {
            var targets = TargetableOpponents(actor).OrderBy(unit => unit.Input.Position).ToArray();
            var front = targets.FirstOrDefault();
            if (IsMelee(actor) && front?.Input.Position == 1 && 2L * front.Health <= front.Stats.Health)
                return targets.Skip(1).Append(front);
            return targets.Take(1);
        }

        // Keep granted slots until settlement so equipped copies survive the singer falling.
        private void GrantChorusSlots()
        {
            foreach (var side in units.GroupBy(unit => unit.Input.Side))
            {
                if (!side.Any(unit => unit.Health > 0 && unit.Trait == CharacterTrait.ChorusArmory)) continue;
                foreach (var ally in side)
                {
                    int count = Rules.PersonalEquipmentSlots(ally.Talent, ally.Original.ExtraEquipmentSlot) + 1;
                    if (ally.Slots.Length < count) Array.Resize(ref ally.Slots, count);
                }
            }
        }

        private BattleLoadout CaptureLoadout()
        {
            var tracked = units.Where(unit => unit.Input.Side == Side.Party &&
                (unit.Original.EquipmentSlots.Count > 0 || recruits.Contains(unit)));
            return new BattleLoadout(tracked.Select(unit => new KeyValuePair<string, IReadOnlyList<string>>(
                unit.Input.Id, unit.Slots.Select(item => item?.Id).ToArray())), consumedEquipment,
                escapedWithEquipment.Concat(scavengedItems.Values.Select(stash => stash.Item.Id).Where(id => id != null)),
                tracked.SelectMany(unit => unit.Slots).Where(item => item?.Disabled == true && item.Id != null).Select(item => item.Id));
        }

        private void UpdateFormation()
        {
            units.Sort((a, b) => a.Input.Side == b.Input.Side
                ? a.Input.Position.CompareTo(b.Input.Position) : a.Input.Side.CompareTo(b.Input.Side));
            RefreshFormationEquipment();
        }

        private void RefreshFormationEquipment()
        {
            Formation = Array.AsReadOnly(units.Select(unit => unit.Input).ToArray());
            UpdateEquipment();
        }

        private void UpdateEquipment()
        {
            var living = units.Where(unit => unit.Health > 0).ToArray();
            var current = living.Select(unit => unit.EquipmentInput()).ToArray();
            var stats = EquipmentFormation.Resolve(current);
            foreach (var unit in living)
            {
                var front = unit.Input.Equipment.Any(item => item.Effect == EquipmentEffect.RedCord)
                    ? EquipmentFormation.AdjacentFrontOf(current, unit.Input) : null;
                unit.LinkedFront = front == null ? null : unitsById[front.Id];
                unit.Stats = stats[unit.Input.Id];
            }
            foreach (var unit in living)
                unit.Gear = GearEntries(unit).Select(entry => entry.Item.Definition).ToArray();
        }

        private IEnumerable<(Unit Owner, EquippedItem Item)> GearEntries(Unit unit) =>
            unit.Equipped.Select(item => (unit, item)).Concat(units
                .Where(owner => owner.Health > 0 && owner.LinkedFront == unit)
                .SelectMany(owner => owner.Equipped.Where(item => item.Definition.Effect != EquipmentEffect.RedCord)
                    .Select(item => (owner, item))));

        private static bool IsActiveGear(Unit recipient, Unit owner, EquippedItem item) =>
            owner.Health > 0 && (owner == recipient || owner.LinkedFront == recipient) &&
            !item.Disabled && owner.Slots.Contains(item);

        private static bool UsesShortbow(Unit actor) =>
            actor.Input.Character.Reach == AttackReach.Ranged && actor.Has(EquipmentEffect.Shortbow);

        private void Act(Unit actor)
        {
            if (actor.Input.Side == Side.Enemy && TryEnemyTurn(actor))
                return;
            bool canCast = Round != 1 || actor.Talent != PlayerTrait.Warmup;
            if (canCast && actor.Trait == CharacterTrait.AstralCasting && actor.Energy.Remaining > 0 && TryCastAstralSpell(actor))
                return;
            var options = new List<ActionOption>();
            if (canCast && actor.Input.Side == Side.Party && actor.Original.LearnedSkills.Count > 0)
                AddSkillOptions(actor, options, actor.Original.LearnedSkills);
            var skill = actor.Input.Character.Skill;
            if (canCast && skill != null && Tick >= actor.SkillReadyAt)
                AddLegalOptions(actor, skill, options);
            // The two healing traits use healing in place of a basic attack while there is a valid target.
            int skillOptions = options.Count;
            if (actor.Trait == CharacterTrait.HealAlly)
                AddLegalOptions(actor, HealAllyAction, options);
            if (options.Count == skillOptions)
                AddLegalOptions(actor, actor.Input.Character.BasicAttack, options);
            if (options.Count == 0)
                return;

            var context = new DecisionContext(actor.Snapshot(), Tick, actor.Health, actor.Shield, IsRanged(actor));
            var decision = policy.Choose(context, options.AsReadOnly());
            if (decision.OptionIndex < 0 || decision.OptionIndex >= options.Count)
                throw new InvalidOperationException("The decision policy must choose a legal option.");

            var choice = options[decision.OptionIndex];
            bool randomShot = policy is DefaultBattlePolicy && IsRanged(actor) && choice.Book == null &&
                choice.Action == actor.Input.Character.BasicAttack && choice.Kind == ActionKind.Attack &&
                choice.Action.Target == TargetRule.AnyEnemy;
            var target = randomShot ? RandomTarget(AttackTargets(actor, choice.Action)) : unitsById[choice.Target.Id];
            if (choice.Book != null)
            {
                CastLearnedSkill(actor, target, choice.Book, decision.Reason, choice.EnergyCost);
                return;
            }
            if (choice.Action == skill || skill != null && choice.Action.Id == skill.Id)
                { actor.SkillReadyAt = (long)Tick + skill.Cooldown; RefundHardFight(actor); }

            switch (choice.Action.Kind)
            {
                case ActionKind.Attack:
                    Attack(actor, target, choice.Action, randomShot ? DecisionReason.Default : decision.Reason);
                    break;
                case ActionKind.Heal:
                    Heal(actor, target, BaseHealing(actor, choice.Action), choice.Action.Name, BattleEventKind.Action, decision.Reason);
                    break;
                case ActionKind.Shield:
                    int gain = choice.Action.Power - actor.Shield;
                    gain = GiveShield(actor, gain);
                    Report(BattleEventKind.Action, actor, actor, choice.Action.Name, ActionKind.Shield, gain, decision.Reason);
                    break;
            }
        }

        private IEnumerable<Unit> AttackTargets(Unit actor, ActionDefinition action)
        {
            var enemies = TargetableOpponents(actor);
            if (actor.Trait == CharacterTrait.PiercingArrow && !UsesShortbow(actor))
                return enemies.Take(1);
            if (UsesShortbow(actor) || action.Target == TargetRule.FrontEnemy ||
                IsMelee(actor) && action == actor.Input.Character.BasicAttack && action.Target == TargetRule.AnyEnemy)
                return FrontAttackTargets(actor);
            return action.Target == TargetRule.RearEnemy ? enemies.Reverse().Take(1) : enemies;
        }

        private void AddLegalOptions(Unit actor, ActionDefinition action, List<ActionOption> options)
        {
            if (action.Kind == ActionKind.Attack)
            {
                if (!CanAttackThisRound(actor)) return;
                foreach (var target in AttackTargets(actor, action))
                    options.Add(new ActionOption(action, target.Snapshot(), target.Health,
                        ProjectedDamage(actor, target, AttackComponents(actor, target, action), UsesShortbow(actor),
                            weaponAttack: true, multiplier: actor.DoubleNextAttack ? 2 : 1)));
                return;
            }

            if (action.Kind == ActionKind.Shield)
            {
                int gain = action.Power - actor.Shield;
                if (gain > 0)
                    options.Add(new ActionOption(action, actor.Snapshot(), actor.Health, gain));
                return;
            }

            int healing = BaseHealing(actor, action);
            if (healing <= 0)
                return;
            if (actor.Has(EquipmentEffect.RebelEarring))
            {
                if (!CanAttackThisRound(actor)) return;
                int power = healing;
                foreach (var enemy in TargetableOpponents(actor))
                    options.Add(new ActionOption(action, enemy.Snapshot(), enemy.Health,
                        ProjectedDamage(actor, enemy, new[] { (DamageType.Magic, power) }), ActionKind.Attack));
                return;
            }

            foreach (var ally in units.Where(unit => unit.Input.Side == actor.Input.Side && unit.Health > 0))
            {
                if (action.Target == TargetRule.Self && ally != actor || !ally.CanReceiveHealing)
                    continue;
                int gain = Math.Min(HealingPower(ally, healing), ally.Stats.Health - ally.Health);
                if (gain > 0)
                    options.Add(new ActionOption(action, ally.Snapshot(), ally.Health, gain));
            }
        }

        private bool FinishIfDecided()
        {
            var outcome = BattleSimulator.ResolveOutcome(
                units.Any(unit => unit.Input.Side == Side.Party && unit.Health > 0),
                units.Any(unit => unit.Input.Side == Side.Enemy && unit.Health > 0));
            if (!outcome.HasValue)
                return false;
            Finish(outcome.Value, Tick);
            return true;
        }

        private void Finish(BattleOutcome outcome, int tick)
        {
            Tick = tick;
            foreach (var scavenger in scavengedItems.Keys.ToArray())
                RecoverScavengedItem(scavenger);
            events.Add(new BattleEvent(tick, BattleEventKind.Finished));
            var health = units.Select(unit =>
                new KeyValuePair<string, int>(unit.Input.Id, Math.Min(unit.Health, unit.Original.Stats.Health + (unit.Input.Side == Side.Party ? unit.BonusMaximumHealth : 0))));
            var partyPositions = units.Where(unit => unit.Input.Side == Side.Party)
                .Select(unit => new KeyValuePair<string, int>(unit.Input.Id, unit.Input.Position));
            var party = units.Where(unit => unit.Input.Side == Side.Party).ToArray();
            foreach (var actor in party)
                actor.Energy = new BattleEnergy(actor.Energy.Capacity, actor.Energy.Capacity);
            var attackGains = party.Where(unit => unit.PermanentAttack > 0)
                .Select(unit => new KeyValuePair<string, int>(unit.Input.Id, unit.PermanentAttack));
            var potions = party.Count(unit => unit.Health > 0 && unit.Trait == CharacterTrait.GeneratePotion);
            int gold = (int)Math.Min((long)int.MaxValue - request.PartyGold, equipmentGold + request.RosterGoldReward);
            int accessories = request.ReserveAccessoryHunterCount +
                party.Count(unit => unit.Health > 0 && unit.Trait == CharacterTrait.AccessoryOnSurvival);
            Result = new BattleResult(request.Id, outcome, health, events, partyPositions,
                recruits.Select(unit => unit.RecruitmentResult()), attackGains, potions, currentLoadout, gold, accessories,
                party.Where(unit => unit.BonusMaximumHealth > 0).Select(unit =>
                    new KeyValuePair<string, int>(unit.Input.Id, unit.BonusMaximumHealth)), BuildRatings());
        }

        private void ReportCountdown(Unit unit, BattleCountdownKind kind, int remaining)
        {
            var key = (unit.Input.Id, kind);
            if (countdowns.GetValueOrDefault(key) == remaining)
                return;
            if (remaining == 0)
                countdowns.Remove(key);
            else
                countdowns[key] = remaining;
            events.Add(new BattleEvent(Tick, BattleEventKind.Countdown, targetId: unit.Input.Id,
                countdown: new BattleCountdown(kind, remaining)));
        }

        private void ApplyGravePollen(Unit attacker, Unit target)
        {
            if (attacker.Input.Side == target.Input.Side) return;
            foreach (var gardener in units.Where(unit => unit.Input.Side != attacker.Input.Side &&
                (unit.Health > 0 || unit == target) &&
                unit.Trait is CharacterTrait.GravePollen or CharacterTrait.RottenBloom).OrderBy(unit => unit.Input.Position))
                GrantStatus(gardener, attacker, StatusKind.Poison, 2);
        }

        private void SpreadRottenBloom(Unit fallen)
        {
            int layers = fallen.Layers(StatusKind.Poison);
            if (layers == 0) return;
            var witch = units.Where(unit => unit.Health > 0 && unit.Trait == CharacterTrait.RottenBloom)
                .OrderBy(unit => unit.Input.Position).FirstOrDefault();
            if (witch == null) return;

            var enemies = DirectOpponents(witch).ToLookup(unit =>
            {
                if (fallen.Input.Side == witch.Input.Side)
                    return fallen.Input.Position + unit.Input.Position;
                if (unit.Input.Position < fallen.Input.Position)
                    return fallen.Input.Position - (unit.Input.Position + unit.Input.Character.PositionSlots - 1);
                return unit.Input.Position - (fallen.Input.Position + fallen.Input.Character.PositionSlots - 1);
            });
            if (enemies.Count == 0) return;
            var nearest = enemies[enemies.Min(group => group.Key)].ToArray();
            var recipient = nearest.Length == 1 ? nearest[0] : nearest[random.Next(nearest.Length)];
            if (GrantStatus(witch, recipient, StatusKind.Poison, layers, "腐花传毒",
                kind: BattleEventKind.Effect, transfer: true))
                RemoveLayers(fallen, StatusKind.Poison, layers);
        }

        private void Report(BattleEventKind kind, Unit actor, Unit target, string name, ActionKind action,
            int amount = 0, DecisionReason reason = DecisionReason.Default, string detail = null,
            CharacterDefinition form = null, BattleLoadout loadout = null, bool weaponAttack = false,
            BattleVisualCue visual = BattleVisualCue.None, BattleEnergy? actorEnergy = null, string skillBookId = null,
            BattleEnergy? targetEnergy = null, IReadOnlyList<(DamageType Type, int Amount)> damageTaken = null,
            bool poisonDamage = false, int shieldAbsorbed = 0, CharacterDefinition copiedTraitSource = null,
            bool charmTriggered = false, EquipmentEffect equipment = EquipmentEffect.None, bool intercepted = false)
        {
            if (amount > 0 && actor != null && kind is (BattleEventKind.Action or BattleEventKind.Effect))
            {
                if (action == ActionKind.Heal && actor.Input.Side == target.Input.Side)
                    actor.Rating.Healing += amount;
                if (action == ActionKind.Shield)
                    target.ShieldSources.Add(actor, amount);
            }
            if (kind == BattleEventKind.Retreated) target.Rating.Retreated = true;
            var statuses = target.Health > 0 && target.Statuses.Count > 0
                ? Array.AsReadOnly(target.Statuses.OrderBy(entry => entry.Key)
                    .Select(entry => new StatusStack(entry.Key, entry.Value)).ToArray()) : null;
            var reach = actor != null && action == ActionKind.Attack && (weaponAttack || kind == BattleEventKind.Action)
                ? UsesShortbow(actor) ? AttackReach.Melee : actor.Input.Character.Reach : AttackReach.Unspecified;
            events.Add(new BattleEvent(Tick, kind, actor?.Input.Id, target.Input.Id, name, action, reason, amount,
                target.Health, target.Shield, StatusText(target), detail, form, target.Stats.Health, statuses,
                loadout, RecordPartyStatChanges(), reach, visual, actorEnergy, skillBookId, targetEnergy, damageTaken, poisonDamage,
                shieldAbsorbed: shieldAbsorbed, copiedTraitSource: copiedTraitSource, charmTriggered: charmTriggered,
                equipment: equipment, intercepted: intercepted));
            if (detail == RebirthProtection)
                Report(BattleEventKind.Effect, target, target, "死而复生", ActionKind.Heal, visual: BattleVisualCue.Rebirth);
            if (kind == BattleEventKind.Action && action == ActionKind.Attack && actor != null)
                ApplyGravePollen(actor, target);
            if (kind == BattleEventKind.Defeated)
            {
                SpreadRottenBloom(target);
                FallenTraits(target);
            }
        }
    }
}
