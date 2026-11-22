using System;
using System.Linq;
using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    [Flags]
    public enum BonusTerms { None = 0, Appearance = 1, Kill = 2, DamageTaken = 4, Shield = 8 }

    [Serializable]
    public struct WageInstallments
    {
        public bool Enabled;
        public int Count, IntervalDays, Amount;
        public long Total => (long)Count * Amount;
        public bool Fits(int duration) => !Enabled || Count >= 2 && IntervalDays >= 1 && Amount > 0 &&
            ((long)Count - 1) * IntervalDays <= duration;

        public static int PremiumPercent(Personality personality) => personality switch
        {
            Personality.Caring or Personality.Optimist => 0,
            Personality.Stoic or Personality.Curious => 5,
            Personality.Frank or Personality.Deadpan => 10,
            Personality.Theatrical or Personality.Competitive => 15,
            _ => 20
        };

        public static long MinimumTotal(Personality personality, int wage, int days)
        {
            long basis = (long)wage * days;
            int percent = PremiumPercent(personality);
            return basis + basis / 100 * percent + (basis % 100 * percent + 99) / 100;
        }
    }

    [Serializable]
    public struct PerformanceBonuses
    {
        public int Appearance, Kill, DamageTaken, Shield;
        public WageInstallments Installments;

        public bool Valid => Appearance >= 0 && Kill >= 0 && DamageTaken >= 0 && Shield >= 0;
        public BonusTerms Terms => (Appearance > 0 ? BonusTerms.Appearance : 0) | (Kill > 0 ? BonusTerms.Kill : 0)
            | (DamageTaken > 0 ? BonusTerms.DamageTaken : 0) | (Shield > 0 ? BonusTerms.Shield : 0);

        public static implicit operator PerformanceBonuses(BonusTerms legacy)
        {
            if (((int)legacy & ~15) != 0) return new PerformanceBonuses { Appearance = -1 };
            return new PerformanceBonuses {
                Appearance = (legacy & BonusTerms.Appearance) != 0 ? 2 : 0,
                Kill = (legacy & BonusTerms.Kill) != 0 ? 3 : 0,
                DamageTaken = (legacy & BonusTerms.DamageTaken) != 0 ? 2 : 0,
                Shield = (legacy & BonusTerms.Shield) != 0 ? 2 : 0
            };
        }
    }

    [Serializable]
    public sealed class CrewContract
    {
        public int StartDay, EndDay, DailyWage, ReleaseFee;
        public bool BasePaid;
        public int InstallmentsPaid;
        public WageInstallments Plan => Rates.Installments;
        public long DueAtSigning => Plan.Enabled ? Plan.Amount : BaseTotal;
        public long Outstanding => Plan.Enabled ? (long)(Plan.Count - InstallmentsPaid) * Plan.Amount : 0;
        public long ExitCost => Outstanding + ReleaseFee;
        public int? NextDueDay => Plan.Enabled && InstallmentsPaid < Plan.Count
            ? StartDay + InstallmentsPaid * Plan.IntervalDays : (int?)null;
        internal CrewContract Copy() => (CrewContract)MemberwiseClone();
        internal void RecordSigningPayment()
        {
            InstallmentsPaid = Plan.Enabled ? 1 : 0;
            BasePaid = !Plan.Enabled;
        }
        public long BaseTotal => (long)DailyWage * (EndDay - StartDay);
        public BonusTerms Bonuses;
        public PerformanceBonuses Rates;

        // Earlier checkpoints stored only which clauses were signed.
        public PerformanceBonuses AgreedRates => Rates.Terms == BonusTerms.None && Bonuses != BonusTerms.None
            ? (PerformanceBonuses)Bonuses : Rates;

        public static int ExpectedWage(CharacterDefinition character, PlayerTrait trait, IEnumerable<SkillBookDefinition> learned = null)
        {
            int basis = Math.Max(1, PlayerMarket.BaseValue(character));
            int value = PlayerMarket.Value(character, trait, learned);
            return Math.Max(1, (int)Math.Round((2 + basis / 18) * Math.Sqrt(value / (double)basis), MidpointRounding.AwayFromZero));
        }

        private static long WageCredit(PerformanceBonuses rates, bool releaseClause) =>
            rates.Appearance / 2L + rates.Kill / 3L + rates.DamageTaken / 2L + rates.Shield / 2L + (releaseClause ? 1 : 0);

        public static int MinimumWage(int expected, PerformanceBonuses bonuses, bool releaseClause)
        {
            long credit = WageCredit(bonuses, releaseClause);
            int fixedFloor = (int)Math.Ceiling(expected * .60);
            return (int)Math.Max(fixedFloor, Math.Ceiling(expected * .85) - credit);
        }

        public static CrewContract Offer(CharacterDefinition character, int day, int duration = 12,
            PerformanceBonuses bonuses = default, bool releaseClause = false)
        {
            if (duration < 10 || duration > int.MaxValue - day) throw new ArgumentOutOfRangeException(nameof(duration));
            if (!bonuses.Valid) throw new ArgumentOutOfRangeException(nameof(bonuses));
            long concession = WageCredit(bonuses, releaseClause);
            int baseValue = PlayerMarket.BaseValue(character);
            return new CrewContract { StartDay = day, EndDay = day + duration,
                DailyWage = (int)Math.Max(1, 2 + baseValue / 18 - concession),
                Bonuses = bonuses.Terms, Rates = bonuses, ReleaseFee = releaseClause ? Math.Max(2, baseValue / 4) : 0 };
        }
    }

    public sealed partial class PartyRun
    {
        public const int RosterLimit = 8;
        private int talentSeed;
        public int Day { get; private set; }
        public long PayrollDebt { get; private set; }
        public bool NeedsRenewal => members.Any(m => m.Contract != null && m.Contract.EndDay - Day <= 3);

        internal void StartContracts(int seed, int day = 1)
        {
            if (Day != 0) return;
            talentSeed = seed;
            Day = day;
            foreach (var member in members) PrepareHire(member);
        }

        // The founding grant covers only the original agreements of a newly created company.
        internal void FundFoundingContracts()
        {
            foreach (var member in members) member.Contract.BasePaid = true;
        }

        public PlayerTrait TalentFor(CharacterDefinition character) =>
            (PlayerTrait)new Random(unchecked(talentSeed + Day * 7919 + character.RosterNumber * 104729)).Next(1, PlayerMarket.TraitCount + 1);

        private void PrepareHire(Member member)
        {
            if (!member.Attributes.IsValid)
                member.Attributes = EmployeeAttributes.Roll(employeeRandom);
            if (Day > 0 && member.Contract == null)
            {
                member.Trait = TalentFor(member.Character);
                member.Contract = CrewContract.Offer(member.Character, Day);
                if (member.Trait == PlayerTrait.Warehouse) Array.Resize(ref member.Slots, Rules.EquipmentSlots + 1);
                if (member.Trait == PlayerTrait.Sturdy && member.Health > 0) member.Health += 2;
            }
            RefreshEmployeeGoals(member);
        }

        private static Stats TraitBonus(Member m) => new Stats(m.Trait == PlayerTrait.Brawn ? 1 : 0,
            m.Trait == PlayerTrait.Tough ? 1 : 0, m.Trait == PlayerTrait.Warded ? 1 : 0, m.Trait == PlayerTrait.Sturdy ? 2 : 0);

        internal CrewContract CandidateContract(CharacterDefinition candidate, int days, PerformanceBonuses terms, bool clause, int? wage)
        {
            if (candidate == null || Day < 1 || days < 10 || days > int.MaxValue - Day || !terms.Valid) return null;
            return NegotiatedContract(candidate, TalentFor(candidate), null, days, terms, clause, wage);
        }

        private CrewContract NegotiatedContract(CharacterDefinition character, PlayerTrait trait, IEnumerable<SkillBookDefinition> skills,
            int days, PerformanceBonuses terms, bool clause, int? wage, int? memberExpectation = null)
        {
            int expected = memberExpectation ?? CrewContract.ExpectedWage(character, trait, skills);
            int minimum = CrewContract.MinimumWage(expected, terms, clause);
            var draft = CrewContract.Offer(character, Day, days, terms, clause);
            draft.DailyWage = wage ?? Math.Max(minimum, draft.DailyWage);
            if (draft.DailyWage < minimum || !terms.Installments.Fits(days)) return null;
            if (terms.Installments.Enabled && terms.Installments.Total <
                WageInstallments.MinimumTotal(character.Personality, draft.DailyWage, days)) return null;
            return draft;
        }

        internal void SignNewHire(string characterId, CrewContract draft)
        {
            var member = members.Single(m => m.Character.Id == characterId);
            draft.RecordSigningPayment();
            member.Contract = draft;
            RefreshEmployeeGoals(member);
        }

        public bool SetContract(string id, int days, PerformanceBonuses bonuses, bool releaseClause, int? dailyWage = null)
        {
            var m = FindMember(id);
            if (m == null || activeBattle != null || Day < 1 || days < 10 || days > int.MaxValue - Day ||
                !bonuses.Valid || dailyWage is <= 0) return false;
            var draft = NegotiatedContract(m.Character, m.Trait, m.LearnedSkills, days, bonuses, releaseClause, dailyWage, ExpectedWage(m));
            if (draft == null) return false;
            long previous = m.Contract?.Outstanding ?? 0;
            int fee = !releaseClause ? m.Contract?.ReleaseFee ?? 0 : 0;
            if (draft.DueAtSigning + previous + fee > Gold) return false;
            TrySpendGold((int)previous, m.Character.Name + " · 结清旧合同分期");
            TrySpendGold(fee, m.Character.Name + " · 违约金");
            PaySigningWage(m.Character.Name, draft);
            draft.RecordSigningPayment();
            m.Contract = draft;
            RefreshEmployeeGoals(m);
            return true;
        }

        internal void PaySigningWage(string name, CrewContract draft) =>
            TrySpendGold((int)draft.DueAtSigning, name + (draft.Plan.Enabled ? " · 第 1 期工资" : " · 整期底薪"));

        private int QuotePercent(CharacterDefinition c, int salt, int minimum, int maximum)
        {
            var random = new Random(unchecked(talentSeed ^ (Day * 7919 + c.RosterNumber * 104729 + salt)));
            return random.Next(minimum, maximum + 1);
        }

        public int SigningPrice(CharacterDefinition c) =>
            (PlayerMarket.Value(c, TalentFor(c)) * QuotePercent(c, 431, 101, 110) + 99) / 100;

        public int TransferOffer(string memberId)
        {
            var m = FindMember(memberId);
            return m == null ? 0 : Math.Max(1, PlayerMarket.Value(m.Character, m.Trait, m.LearnedSkills)
                * QuotePercent(m.Character, 173, 90, 99) / 100);
        }

        public bool CanSellMember(string id)
        {
            var m = FindMember(id);
            return activeBattle == null && m?.Contract != null && !m.IsAway && members.Any(x => x != m && !x.IsAway && x.Health > 0);
        }

        public bool SellMember(string id) => SellMember(id, TransferOffer(id));

        internal bool SellMember(string id, int price)
        {
            if (price < 1 || !CanSellMember(id)) return false;
            var m = FindMember(id);
            long proceeds = price - m.Contract.ExitCost;
            if (proceeds > int.MaxValue - Gold || proceeds < -Gold) return false;
            if (proceeds < 0) Gold += (int)proceeds;
            RecordExitExpenses(m);
            members.Remove(m);
            if (proceeds > 0) AddGold((int)proceeds);
            DeployReserveIfNeeded();
            return true;
        }

        private void RecordExitExpenses(Member member)
        {
            RecordExpense(member.Character.Name + " · 结清旧合同分期", member.Contract?.Outstanding ?? 0);
            RecordExpense(member.Character.Name + " · 违约金", member.Contract?.ReleaseFee ?? 0);
        }

        private void DeployReserveIfNeeded()
        {
            if (members.Any(IsDeployed)) return;
            var reserve = members.FirstOrDefault(m => !m.IsAway && m.Health > 0);
            if (reserve != null) reserve.Position = 1;
        }

        private void Pay(long amount, string reason)
        {
            int paid = (int)Math.Min(Gold, amount);
            Gold -= paid;
            RecordExpense(reason, paid);
            PayrollDebt += amount - paid;
        }

        public bool CanAdvanceDay => members.Any(m => !m.IsAway && m.Health > 0 && m.Contract.EndDay > Day + 1);

        internal void AdvanceDay()
        {
            Day++;
            expenses.RemoveAll(e => e.Day < Day - 2);
            foreach (var member in members)
            {
                member.Career.ServiceDays++;
                var contract = member.Contract;
                if (contract.NextDueDay == Day)
                {
                    contract.InstallmentsPaid++;
                    Pay(contract.Plan.Amount, $"{member.Character.Name} · 第 {contract.InstallmentsPaid} 期工资");
                    contract.BasePaid = contract.InstallmentsPaid == contract.Plan.Count;
                }
                else if (!contract.BasePaid && !contract.Plan.Enabled)
                    Pay(contract.DailyWage, member.Character.Name + " · 每日工资");
            }
            members.RemoveAll(m => m.Contract.EndDay <= Day);
            DeployReserveIfNeeded();
            foreach (var member in members) RefreshEmployeeGoals(member);
        }

        private void SettlePerformance(BattleResult result, HashSet<string> deployed)
        {
            var enemies = activeBattle.Combatants.Where(u => u.Side == Side.Enemy).Select(u => u.Id).ToHashSet();
            foreach (var m in members.Where(m => m.Contract != null && deployed.Contains(m.Id)))
            {
                var rates = m.Contract.AgreedRates;
                Pay(rates.Appearance, m.Character.Name + " · 出场费");
                if (rates.Kill > 0)
                {
                    int kills = result.Events.Count(e => e.Kind == BattleEventKind.Defeated && e.ActorId == m.Id && enemies.Contains(e.TargetId));
                    Pay((long)rates.Kill * kills, m.Character.Name + " · 击杀奖");
                }
                if (rates.DamageTaken > 0)
                {
                    long damage = result.Events.Where(e => e.TargetId == m.Id).Sum(e => e.DamageTaken.Sum(d => (long)d.Amount));
                    Pay(rates.DamageTaken * (damage / 5), m.Character.Name + " · 承伤奖");
                }
                if (rates.Shield > 0)
                {
                    long blocked = result.Events.Where(e => e.TargetId == m.Id && enemies.Contains(e.ActorId)).Sum(e => (long)e.ShieldAbsorbed);
                    Pay(rates.Shield * (blocked / 5), m.Character.Name + " · 抗伤奖");
                }
            }
        }
    }
}
