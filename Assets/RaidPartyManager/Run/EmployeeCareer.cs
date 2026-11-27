using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public enum EmployeeGoalKind
    {
        None, Appearances, Damage, Blocking, Healing, Kills, Victories, Tenure,
        Equipment, Training, Raise, Renewal, SeparateLineups
    }

    public enum EmployeeRating
    {
        None, Loyalty, Obedience, PayPreference, PlayingTime, Satisfaction, Hardiness
    }

    [Serializable]
    public struct EmployeeGoal
    {
        public EmployeeGoalKind Kind;
        public long Baseline;
        public int Target;
        public int Progress;
        public string SubjectId, SubjectName;
        public EmployeeRating Awarded;
        public int AwardedLevel;
    }

    [Serializable]
    public struct EmployeeMemory
    {
        public EmployeeThought Topic;
        public string Detail;
        public int Day;
    }

    [Serializable]
    public struct EmployeeCareer
    {
        public bool Initialized;
        public bool LastBattleAttended;
        public int LastStartingPosition;
        public long LastBattleBlocked, LastBattleContribution;
        public string RecordedEpitaph;
        public EmployeeGoal Inscription;
        public EmployeeMemory LatestMemory, PreviousMemory;
        public bool HasRescuedAlly;
        public int ServiceDays;
        public long Damage, Blocked, DamageTaken, Healed, Kills, Appearances, Victories;
        public int MissedBattles, QuietBattles, WorkGoalsCompleted, GoalsCompleted, HighestWage;
        public bool EquipmentWishFulfilled, TrainingWishFulfilled;
        public string RivalId, RivalCharacterId;
        public int RivalIncidents;
        public EmployeeGoal Work, Development, Raise, Renewal, Relationship, LastCompleted;
    }

    public sealed class EmployeeProfile
    {
        public EmployeeCareer Career { get; }
        public IReadOnlyList<string> Thoughts { get; }
        public IReadOnlyList<EmployeeGoal> Goals { get; }

        internal EmployeeProfile(EmployeeCareer career, IReadOnlyList<string> thoughts, IReadOnlyList<EmployeeGoal> goals)
        {
            Career = career;
            Thoughts = thoughts;
            Goals = goals;
        }
    }

    public sealed partial class PartyRun
    {
        public EmployeeProfile EmployeeProfileFor(string memberId)
        {
            var member = FindMember(memberId);
            if (member == null) return null;
            var career = member.Career;
            var goals = new[] { career.Inscription, career.Work, career.Development, career.Renewal, career.Raise, career.Relationship }
                .Where(goal => goal.Kind != EmployeeGoalKind.None).ToArray();
            for (int i = 0; i < goals.Length; i++)
            {
                var goal = goals[i];
                goal.Progress = GoalProgress(member, goal);
                goals[i] = goal;
            }
            return new EmployeeProfile(career, EmployeeThoughts(member), goals);
        }

        private string[] EmployeeThoughts(Member member)
        {
            var career = member.Career;
            var voice = EmployeeVoice.For(member.Start.Character.Personality);
            var thoughts = new List<(EmployeeThought Topic, string Text)>();
            if (ContractsEnabled && PayrollDebt > 0 && member.Contract != null)
                Add(EmployeeThought.Arrears);
            if (ContractsEnabled && member.Contract is { } contract)
            {
                int remaining = member.LastContract.EndDay - ContractDay;
                if (remaining <= 3) Add(EmployeeThought.Renewal, remaining);
                int expected = ExpectedWage(member);
                if (Underpaid(member, expected)) Add(EmployeeThought.LowPay, contract.DailyWage, expected);
            }
            int urgent = thoughts.Count;
            Recall(career.LatestMemory);
            Recall(career.PreviousMemory);
            if (member.IsAway) Add(EmployeeThought.Away);
            else if (career.MissedBattles >= (member.Attributes.PlayingTime >= 4 ? 2 : 3))
                Add(EmployeeThought.Benched, career.MissedBattles);
            if (career.QuietBattles >= 2) Add(EmployeeThought.SelfDoubt);
            var rival = FindCurrentRival(member);
            if (rival != null && career.RivalIncidents >= 2) Add(EmployeeThought.Rivalry, rival.Character.Name);
            if (career.Development.Kind == EmployeeGoalKind.Equipment) Add(EmployeeThought.Equipment, career.Development.SubjectName, career.Development.Target - career.Development.Progress);
            if (career.Development.Kind == EmployeeGoalKind.Training) Add(EmployeeThought.Training, career.Development.SubjectName);
            if (career.GoalsCompleted > 0 && member.Attributes.Satisfaction >= 4) Add(EmployeeThought.Appreciation);
            if (thoughts.Count == 0) Add(career.Appearances == 0 ? EmployeeThought.Debut : EmployeeThought.Steady);

            // Urgent contract facts stay visible; within each group lead with the speaker's own concern.
            return thoughts.Take(urgent).OrderByDescending(t => t.Topic == voice.Favorite)
                .Concat(thoughts.Skip(urgent).OrderByDescending(t => t.Topic == voice.Favorite))
                .Select(t => t.Text).ToArray();

            void Add(EmployeeThought topic, params object[] details) => thoughts.Add((topic, voice.Say(topic, details)));
            void Recall(EmployeeMemory memory)
            {
                if (!ContractsEnabled && memory.Topic is (EmployeeThought.Arrears or EmployeeThought.Renewal or EmployeeThought.LowPay)) return;
                if (memory.Topic != EmployeeThought.None && memory.Day <= Day && Day - memory.Day < 3)
                    Add(memory.Topic, memory.Detail);
            }
        }

        private void Remember(Member member, EmployeeThought topic, string detail = "")
        {
            ref var career = ref member.Career;
            if (career.LatestMemory.Topic != topic) career.PreviousMemory = career.LatestMemory;
            career.LatestMemory = new EmployeeMemory { Topic = topic, Detail = detail, Day = Day };
        }

        internal void RememberRoadEvent(EmployeeThought topic, string detail, string exceptCharacter = null)
        {
            foreach (var witness in members.Where(m => IsDeployed(m) && m.Character.Id != exceptCharacter))
                Remember(witness, topic, detail);
        }

        internal void RememberPersonalEvent(string memberId, EmployeeThought topic, string detail = "")
        {
            if (FindMember(memberId) is { } member) Remember(member, topic, detail);
        }

        public int ExpectedDailyWage(string memberId) => FindMember(memberId) is { } member ? ExpectedWage(member) : 0;

        private static bool Underpaid(Member member, int expected)
        {
            var contract = member.LastContract;
            if (contract.TidePact) return false;
            if (member.Attributes.Loyalty < 4 && member.Attributes.Obedience < 4)
                return contract.DailyWage < expected;
            return contract.DailyWage < CrewContract.MinimumWage(expected, contract.AgreedRates, member.Attributes);
        }

        private static int ExpectedWage(Member member)
        {
            int basis = CrewContract.ExpectedWage(member.Character, member.Trait, member.LearnedSkills, member.Training.Sessions);
            var career = member.Career;
            double work = career.Damage + (double)career.DamageTaken + career.Blocked * .5 +
                career.Healed * 1.5 + career.Kills * 5.0;
            double ceiling = .20 + .03 * member.Attributes.PayPreference;
            double premium = ceiling * work / (work + 100.0);
            int increase = (int)Math.Round(basis * premium, MidpointRounding.AwayFromZero);
            return basis + Math.Min(basis * 35 / 100, increase);
        }

        private Member FindCurrentRival(Member member)
        {
            var rival = FindMember(member.Career.RivalId);
            return rival != null && rival.Start.Character.Id == member.Career.RivalCharacterId ? rival : null;
        }

        private void SettleEmployeeBattle(BattleResult result, HashSet<string> participants)
        {
            var employees = members.Where(member => participants.Contains(member.Id)).ToDictionary(member => member.Id);
            var starters = activeBattle.Combatants.Where(unit => unit.Side == Side.Party).ToDictionary(unit => unit.Id);
            var enemies = activeBattle.Combatants.Where(unit => unit.Side == Side.Enemy).Select(unit => unit.Id).ToHashSet();
            foreach (var member in members) { member.Career.LastBattleBlocked = 0; member.Career.LastBattleAttended = participants.Contains(member.Id); }
            var lastAttacker = new Dictionary<string, Member>();
            var contributions = employees.Keys.ToDictionary(id => id, _ => 0L);
            var defeated = new HashSet<string>();
            foreach (var entry in result.Events)
            {
                if (entry.TargetId == null) continue;
                bool enemyTarget = enemies.Contains(entry.TargetId);
                var actor = entry.ActorId == null ? null : employees.GetValueOrDefault(entry.ActorId);
                if (entry.Kind == BattleEventKind.Defeated && enemyTarget && actor != null && defeated.Add(entry.TargetId))
                {
                    actor.Career.Kills++;
                    continue;
                }
                if (entry.Kind is not (BattleEventKind.Action or BattleEventKind.Effect)) continue;

                if (actor != null && enemyTarget && entry.ActionKind == ActionKind.Attack)
                {
                    long damage = Math.Max(0, entry.Amount);
                    actor.Career.Damage += damage;
                    contributions[actor.Id] += damage;
                    if (damage > 0)
                    {
                        if (entry.TargetHealth == 0 && lastAttacker.TryGetValue(entry.TargetId, out var rival) && rival != actor &&
                            (rival.Career.Relationship.Kind == EmployeeGoalKind.None || FindCurrentRival(rival) == actor))
                        {
                            if (FindCurrentRival(rival) != actor)
                            {
                                rival.Career.RivalId = actor.Id;
                                rival.Career.RivalCharacterId = actor.Start.Character.Id;
                                rival.Career.RivalIncidents = 0;
                            }
                            rival.Career.RivalIncidents++;
                        }
                        lastAttacker[entry.TargetId] = actor;
                    }
                }
                if (actor != null && employees.ContainsKey(entry.TargetId) && entry.ActionKind == ActionKind.Heal)
                {
                    long healed = Math.Max(0, entry.Amount);
                    var patient = employees[entry.TargetId];
                    int danger = Math.Max(1, PersonalStats(patient).Health / 4);
                    if (actor != patient && !actor.Career.HasRescuedAlly && healed > 0 &&
                        entry.TargetHealth > danger && entry.TargetHealth - healed <= danger)
                    {
                        actor.Career.HasRescuedAlly = true;
                        Remember(actor, EmployeeThought.Rescue, patient.Character.Name);
                    }
                    actor.Career.Healed += healed;
                    contributions[actor.Id] += healed;
                }
                if (employees.TryGetValue(entry.TargetId, out var injured))
                {
                    long lost = entry.DamageTaken.Sum(part => (long)part.Amount);
                    injured.Career.DamageTaken += lost;
                    contributions[injured.Id] += lost;
                }
                if (entry.ActorId != null && enemies.Contains(entry.ActorId) &&
                    employees.TryGetValue(entry.TargetId, out var defender))
                {
                    long blocked = Math.Max(0, entry.ShieldAbsorbed);
                    defender.Career.Blocked += blocked;
                    defender.Career.LastBattleBlocked += blocked;
                    contributions[defender.Id] += blocked;
                }
            }
            foreach (var member in members)
            {
                if (employees.ContainsKey(member.Id))
                {
                    member.Career.LastBattleContribution = contributions[member.Id];
                    member.Career.Appearances++;
                    if (starters.TryGetValue(member.Id, out var starter)) member.Career.LastStartingPosition = starter.Position;
                    var wish = member.Career.Development;
                    if (wish.Kind == EmployeeGoalKind.Equipment && starter != null &&
                        starter.Equipment.Any(item => item.Id == wish.SubjectId))
                        member.Career.Development.Progress++;
                    member.Career.MissedBattles = 0;
                    member.Career.QuietBattles = contributions[member.Id] < 4 ? member.Career.QuietBattles + 1 : 0;
                    if (result.Outcome == BattleOutcome.Victory)
                    {
                        member.Career.Victories++;
                        if (result.HealthById.TryGetValue(member.Id, out int hp) && hp > 0 && (long)hp * 4 <= PersonalStats(member).Health)
                            Remember(member, EmployeeThought.NarrowVictory);
                    }
                    if (member.Career.Relationship.Kind == EmployeeGoalKind.SeparateLineups)
                    {
                        var rival = FindCurrentRival(member);
                        if (rival == null || !employees.ContainsKey(rival.Id))
                            member.Career.Relationship.Progress++;
                    }
                }
                else if (!member.IsAway && member.Health > 0) member.Career.MissedBattles++;
                RefreshEmployeeGoals(member);
            }
        }

        private void RefreshEmployeeGoals(Member member)
        {
            ref var career = ref member.Career;
            career.Initialized = true;
            if (!ContractsEnabled)
            {
                career.Raise = default;
                career.Renewal = default;
            }
            LimitActiveGoals(ref career); // Older checkpoints can contain five simultaneous goals.
            FinishGoal(member, ref career.Inscription);
            FinishGoal(member, ref career.Work);
            FinishGoal(member, ref career.Development);
            if (career.Raise.Kind == EmployeeGoalKind.Raise && member.Contract != null)
            {
                int expected = ExpectedWage(member);
                if (member.LastContract.DailyWage < career.Raise.Target && !Underpaid(member, expected))
                    career.Raise = default;
                else if (career.Raise.Target > expected)
                    career.Raise = Goal(EmployeeGoalKind.Raise, expected);
            }
            FinishGoal(member, ref career.Raise);
            FinishGoal(member, ref career.Renewal);
            FinishGoal(member, ref career.Relationship);
            int remaining = LimitActiveGoals(ref career);
            if (ContractsEnabled && member.Contract != null)
            {
                if (remaining > 0 && career.Renewal.Kind == EmployeeGoalKind.None &&
                    member.LastContract.EndDay - ContractDay <= 3 && ContractDay <= int.MaxValue - 10)
                {
                    career.Renewal = Goal(EmployeeGoalKind.Renewal, ContractDay + 10);
                    remaining--;
                }
                int expected = ExpectedWage(member);
                if (remaining > 0 && career.Raise.Kind == EmployeeGoalKind.None && career.ServiceDays >= 2 &&
                    Underpaid(member, expected) && expected > career.HighestWage)
                {
                    career.Raise = Goal(EmployeeGoalKind.Raise, expected);
                    remaining--;
                }
                career.HighestWage = Math.Max(career.HighestWage, member.LastContract.DailyWage);
            }
            if (remaining > 0 && career.Relationship.Kind == EmployeeGoalKind.None && career.RivalIncidents >= 2 &&
                FindCurrentRival(member) is { } rival)
            {
                career.Relationship = Goal(EmployeeGoalKind.SeparateLineups, 2, subjectId: rival.Id, subjectName: rival.Character.Name);
                remaining--;
            }
            if (remaining > 0 && career.Work.Kind == EmployeeGoalKind.None)
            {
                career.Work = NextWorkGoal(member);
                remaining--;
            }
            if (remaining > 0 && career.Development.Kind == EmployeeGoalKind.None)
                career.Development = NextDevelopmentGoal(member);
        }

        private static int LimitActiveGoals(ref EmployeeCareer career)
        {
            int remaining = 2;
            Keep(ref career.Inscription);
            Keep(ref career.Renewal);
            Keep(ref career.Raise);
            Keep(ref career.Relationship);
            Keep(ref career.Work);
            Keep(ref career.Development);
            return remaining;

            void Keep(ref EmployeeGoal goal)
            {
                if (goal.Kind == EmployeeGoalKind.None) return;
                if (remaining == 0) goal = default;
                else remaining--;
            }
        }

        private static EmployeeGoal NextWorkGoal(Member member)
        {
            var career = member.Career;
            return (career.WorkGoalsCompleted % 4) switch
            {
                1 when career.Healed > 0 => Goal(EmployeeGoalKind.Healing, 10, career.Healed),
                1 when career.Blocked > 0 => Goal(EmployeeGoalKind.Blocking, 10, career.Blocked),
                1 => Goal(EmployeeGoalKind.Damage, 20, career.Damage),
                2 when career.Kills > 0 => Goal(EmployeeGoalKind.Kills, 2, career.Kills),
                2 or 3 => Goal(EmployeeGoalKind.Victories, 2, career.Victories),
                _ => Goal(EmployeeGoalKind.Appearances, 2, career.Appearances)
            };
        }

        private EmployeeGoal NextDevelopmentGoal(Member member)
        {
            if (!member.Career.TrainingWishFulfilled && member.LearnedSkills.Take(
                    MemberView.SkillSlotsFor(member.Character) + (member.Trait == PlayerTrait.Scholar ? 1 : 0)).Any(skill => skill == null))
            {
                var book = inventory.Values.Select(item => item.Definition).OfType<SkillBookDefinition>()
                    .FirstOrDefault(item => item.CanLearn(member.Character) && member.LearnedSkills.All(known => known?.Id != item.Id));
                if (book != null) return Goal(EmployeeGoalKind.Training, 1, subjectId: book.Id, subjectName: book.Name);
            }
            if (!member.Career.EquipmentWishFulfilled)
            {
                var worn = EquipmentOf(member).Select(item => item.Id).ToHashSet();
                var item = inventory.Values.Select(entry => entry.Definition)
                    .Where(gear => gear is not SkillBookDefinition && !gear.IsPotion &&
                        gear.Effect is not (EquipmentEffect.TreeRoot or EquipmentEffect.RecruitmentNotice) && !worn.Contains(gear.Id))
                    .OrderByDescending(gear => EquipmentInterest(member.Character.Profession, gear.Bonus))
                    .FirstOrDefault();
                if (item != null)
                    return Goal(EmployeeGoalKind.Equipment, employeeRandom.Next(1, 4), subjectId: item.Id, subjectName: item.Name);
            }
            return Goal(EmployeeGoalKind.Tenure, 5, member.Career.ServiceDays);
        }

        private static long EquipmentInterest(Profession role, Stats bonus) => role switch
        {
            Profession.Warrior => 3L * bonus.Defense + 2L * bonus.Health + bonus.Attack + bonus.SpecialDefense,
            Profession.Support => 3L * bonus.Attack + 2L * bonus.SpecialDefense + bonus.Health + bonus.Defense,
            _ => 3L * bonus.Attack + bonus.SpecialDefense + bonus.Health + bonus.Defense
        };

        private static EmployeeGoal Goal(EmployeeGoalKind kind, int target, long baseline = 0,
            string subjectId = null, string subjectName = null) => new EmployeeGoal
            { Kind = kind, Target = target, Baseline = baseline, SubjectId = subjectId, SubjectName = subjectName };

        private int GoalProgress(Member member, EmployeeGoal goal)
        {
            var career = member.Career;
            long progress = goal.Kind switch
            {
                EmployeeGoalKind.Appearances => career.Appearances - goal.Baseline,
                EmployeeGoalKind.Damage => career.Damage - goal.Baseline,
                EmployeeGoalKind.Blocking => career.Blocked - goal.Baseline,
                EmployeeGoalKind.Healing => career.Healed - goal.Baseline,
                EmployeeGoalKind.Kills => career.Kills - goal.Baseline,
                EmployeeGoalKind.Victories => career.Victories - goal.Baseline,
                EmployeeGoalKind.Tenure => career.ServiceDays - goal.Baseline,
                EmployeeGoalKind.Equipment => goal.Progress,
                EmployeeGoalKind.Training => member.LearnedSkills.Any(skill => skill?.Id == goal.SubjectId) ? 1 : 0,
                EmployeeGoalKind.Raise => member.LastContract?.DailyWage ?? 0,
                EmployeeGoalKind.Renewal => member.LastContract?.EndDay ?? 0,
                EmployeeGoalKind.SeparateLineups => FindCurrentRival(member) == null ? goal.Target : goal.Progress,
                _ => 0
            };
            return (int)Math.Max(0, Math.Min(goal.Target, progress));
        }

        private void FinishGoal(Member member, ref EmployeeGoal goal)
        {
            if (goal.Kind == EmployeeGoalKind.None || GoalProgress(member, goal) < goal.Target) return;
            if (goal.Kind is EmployeeGoalKind.Appearances or EmployeeGoalKind.Damage or EmployeeGoalKind.Healing or
                EmployeeGoalKind.Blocking or EmployeeGoalKind.Kills or EmployeeGoalKind.Victories)
                member.Career.WorkGoalsCompleted++;
            if (goal.Kind == EmployeeGoalKind.Equipment) member.Career.EquipmentWishFulfilled = true;
            if (goal.Kind == EmployeeGoalKind.Training) member.Career.TrainingWishFulfilled = true;
            if (goal.Kind == EmployeeGoalKind.SeparateLineups) member.Career.RivalIncidents = 0;

            if (goal.SubjectId == "grave-epitaph") member.Career.RecordedEpitaph = goal.SubjectName;
            goal.Progress = goal.Target;
            var eligible = Ratings.Where(r => member.Attributes.Level(r) < 5).ToArray();
            if (eligible.Length > 0)
            {
                var rating = eligible[employeeRandom.Next(eligible.Length)];
                member.Attributes.TryRaise(rating, out int level);
                goal.Awarded = rating;
                goal.AwardedLevel = level;
                if (level == 5 && rating == EmployeeRating.Loyalty && member.Health > 0) member.Health += 3;
                if (level == 5 && rating == EmployeeRating.PlayingTime && activeBattle == null) RefreshEquipmentSlots();
            }
            member.Career.GoalsCompleted++;
            member.Career.LastCompleted = goal;
            goal = default;
        }

        private static readonly EmployeeRating[] Ratings = { EmployeeRating.Loyalty, EmployeeRating.Obedience,
            EmployeeRating.PayPreference, EmployeeRating.PlayingTime, EmployeeRating.Satisfaction, EmployeeRating.Hardiness };
    }
}
