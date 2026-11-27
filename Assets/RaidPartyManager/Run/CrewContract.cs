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
        public const int MaximumPremiumPercent = 22;

        public bool Enabled;
        public int Count, IntervalDays, Amount;
        // Amount is read only by contracts saved with the original, fixed-payment schedule.
        public bool Fits(int duration) => !Enabled || Count >= 1 && IntervalDays >= 1 && (long)Count * IntervalDays <= duration;

        public static int PremiumPercent(Personality personality) => personality switch
        {
            Personality.Caring or Personality.Optimist => 0,
            Personality.Stoic or Personality.Curious => 7,
            Personality.Frank or Personality.Deadpan => 12,
            Personality.Theatrical or Personality.Competitive => 17,
            _ => MaximumPremiumPercent
        };

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
        public bool BasePaid, TidePact;
        public int InstallmentsPaid;
        public WageInstallments Plan => Rates.Installments;
        // Old saves keep their signed fixed amounts and their first payment on the signing date.
        public bool DeferredTerms;
        public long SigningFee;
        public int InterestPercent;
        public long PrincipalInArrears, InterestInArrears, AccruedInterest, InterestRemainder;
        public long PrincipalPaidEarly;
        public int InterestThroughDay;
        public int Days => EndDay - StartDay;
        public long BaseTotal => DeferredTerms && !Plan.Enabled ? SigningFee : (long)DailyWage * Days;
        public long DueAtSigning => DeferredTerms ? SigningFee : Plan.Enabled ? Plan.Amount : BaseTotal;
        private long Financed => DeferredTerms ? BaseTotal - SigningFee : (long)Plan.Count * Plan.Amount;
        private long InterestDivisor => (long)Days * 100;
        public long Arrears => PrincipalInArrears + InterestInArrears;
        public long RemainingPrincipal => !Plan.Enabled ? 0 : PrincipalAfter(InstallmentsPaid) + PrincipalInArrears;
        public long Outstanding => RemainingPrincipal + InterestInArrears + AccruedInterest + (InterestRemainder > 0 ? 1 : 0);
        public int CurrentReleaseFee => Plan.Enabled && Outstanding > 0 ? (int)Math.Min(Outstanding, ReleaseFee) : 0;
        public long ExitCost => Outstanding + CurrentReleaseFee;
        public int? NextDueDay => Plan.Enabled && !BasePaid && InstallmentsPaid < Plan.Count
            ? StartDay + (InstallmentsPaid + (DeferredTerms ? 1 : 0)) * Plan.IntervalDays : (int?)null;
        internal CrewContract Copy() => (CrewContract)MemberwiseClone();
        internal void RecordSigningPayment()
        {
            BasePaid = Outstanding == 0;
            if (BasePaid) InterestThroughDay = 0;
        }

        private long PrincipalAfter(int count) => Math.Max(0,
            Financed - Financed / Plan.Count * count - Math.Min(count, Financed % Plan.Count) - PrincipalPaidEarly);
        public long PrincipalAt(int period) => PrincipalAfter(period - 1) - PrincipalAfter(period);

        private static decimal SumBalances(long opening, long reduction, int count)
        {
            if (opening <= 0 || count <= 0) return 0;
            if (reduction > 0)
                count = (int)Math.Min(count, (opening - 1) / reduction + 1);
            return (decimal)opening * count - (decimal)reduction * count * (count - 1) / 2;
        }

        // The principal schedule has at most two rates because its first installments carry the remainder.
        private decimal ScheduledBalanceSum(int count)
        {
            int extra = (int)Math.Min(count, Financed % Plan.Count);
            long regular = Financed / Plan.Count;
            return SumBalances(Financed - PrincipalPaidEarly, regular + 1, extra)
                + SumBalances(PrincipalAfter(extra), regular, count - extra);
        }

        private long ScheduledInterestThrough(int period)
        {
            if (period <= InstallmentsPaid || !DeferredTerms) return 0;
            int projectedFrom = InstallmentsPaid + (InterestThroughDay > 0 ? 1 : 0);
            decimal exposure = (ScheduledBalanceSum(period) - ScheduledBalanceSum(projectedFrom)) * Plan.IntervalDays;
            decimal fraction = exposure % InterestDivisor;
            decimal whole = (exposure - fraction) / InterestDivisor * InterestPercent;
            decimal extra = (fraction * InterestPercent + InterestRemainder) / InterestDivisor;
            return AccruedInterest + (long)(whole + (PrincipalAfter(period) == 0 ? decimal.Ceiling(extra) : decimal.Floor(extra)));
        }

        public long ScheduledPayment(int period) => PrincipalAt(period)
            + ScheduledInterestThrough(period) - ScheduledInterestThrough(period - 1);
        public long ScheduledInterest => Plan.Enabled ? ScheduledInterestThrough(Plan.Count) : 0;

        private decimal PeriodInterest(long principal, int days) => (decimal)principal * InterestPercent * days;

        private void AdjustInterest(decimal change)
        {
            decimal units = change + InterestRemainder;
            decimal remainder = units % InterestDivisor;
            if (remainder < 0) remainder += InterestDivisor;
            AccruedInterest += (long)((units - remainder) / InterestDivisor);
            InterestRemainder = (long)remainder;
        }

        internal void OpenInterestPeriod(int day)
        {
            if (!DeferredTerms || day < StartDay || InterestThroughDay != 0 || NextDueDay is not int due)
                return;
            InterestThroughDay = due;
            AdjustInterest(PeriodInterest(RemainingPrincipal, Math.Max(0, due - day)));
        }

        private void CreditEarlyInterest(long principal, int day)
        {
            if (DeferredTerms && InterestThroughDay > day && principal > 0)
                AdjustInterest(-PeriodInterest(principal, InterestThroughDay - day));
        }

        internal long PayoffToday(int day)
        {
            if (InterestThroughDay <= day || RemainingPrincipal == 0) return Outstanding;
            var closing = Copy();
            closing.CreditEarlyInterest(closing.RemainingPrincipal, day);
            return closing.Outstanding;
        }

        internal long RepayArrears(long amount, int day)
        {
            long principal = Math.Min(amount, PrincipalInArrears);
            PrincipalInArrears -= principal;
            CreditEarlyInterest(principal, day);
            long interest = Math.Min(amount - principal, InterestInArrears);
            InterestInArrears -= interest;
            if (Plan.Enabled) RecordSigningPayment();
            return principal + interest;
        }

        internal void PayEarly(long amount, int day)
        {
            if (amount == 0) return;
            long principal = Math.Min(amount, RemainingPrincipal - PrincipalInArrears);
            PrincipalPaidEarly += principal;
            CreditEarlyInterest(principal, day);
            long interest = amount - principal;
            if (interest > AccruedInterest)
                AccruedInterest = InterestRemainder = 0;
            else
                AccruedInterest -= interest;
            RecordSigningPayment();
        }

        internal long CloseInterestPeriod(bool roundUp)
        {
            long interest = AccruedInterest;
            if (roundUp && InterestRemainder > 0)
            {
                interest++;
                InterestRemainder = 0;
            }
            AccruedInterest = 0;
            InterestThroughDay = 0;
            return interest;
        }

        internal bool ValidAt(int day)
        {
            if (TidePact) return StartDay >= 1 && Days == 99 && EndDay >= day && DailyWage == 0 &&
                BasePaid && SigningFee == 0 && !Plan.Enabled && Bonuses == BonusTerms.None && Rates.Terms == BonusTerms.None;
            if (StartDay < 1 || EndDay <= StartDay || EndDay < day || DailyWage < 1 || !Rates.Valid || !AgreedRates.Valid) return false;
            if (!Plan.Enabled) return !DeferredTerms || SigningFee >= Days;
            bool validSchedule = DeferredTerms ? Plan.Fits(Days) && SigningFee >= 0 && SigningFee <= BaseTotal &&
                InterestPercent is >= 0 and <= WageInstallments.MaximumPremiumPercent && PrincipalInArrears >= 0 && InterestInArrears >= 0 && AccruedInterest >= 0 &&
                InterestRemainder >= 0 && InterestRemainder < (long)Days * 100 :
                Plan.Count >= 2 && Plan.IntervalDays > 0 && Plan.Amount > 0 && ((long)Plan.Count - 1) * Plan.IntervalDays <= Days;
            return validSchedule && InstallmentsPaid >= (DeferredTerms ? 0 : 1) && InstallmentsPaid <= Plan.Count &&
                PrincipalPaidEarly >= 0 && PrincipalPaidEarly <= Financed &&
                (InterestThroughDay == 0 || DeferredTerms && InterestThroughDay > day && InterestThroughDay <= EndDay) &&
                !(NextDueDay <= day);
        }

        public BonusTerms Bonuses;
        public PerformanceBonuses Rates;

        // Earlier checkpoints stored only which clauses were signed.
        public PerformanceBonuses AgreedRates => Rates.Terms == BonusTerms.None && Bonuses != BonusTerms.None
            ? (PerformanceBonuses)Bonuses : Rates;

        public static int ExpectedWage(CharacterDefinition character, PlayerTrait trait, IEnumerable<SkillBookDefinition> learned = null,
            int trainingSessions = 0)
        {
            int basis = Math.Max(1, PlayerMarket.BaseValue(character));
            int value = PlayerMarket.Value(character, trait, learned, trainingSessions);
            return Math.Max(1, (int)Math.Round((2 + basis / 18) * Math.Sqrt(value / (double)basis), MidpointRounding.AwayFromZero));
        }

        private static long WageCredit(PerformanceBonuses rates) =>
            rates.Appearance / 2L + rates.Kill / 3L + rates.DamageTaken / 2L + rates.Shield / 2L;

        public static int MinimumWage(int expected, PerformanceBonuses bonuses, EmployeeAttributes ratings)
        {
            int discount = 10 * (Math.Max(0, ratings.Loyalty - 3) + Math.Max(0, ratings.Obedience - 3));
            long Percent(int percent) => ((long)expected * percent + 99) / 100;
            long baseFloor = Percent(Math.Max(40, 60 - discount / 2));
            long askingFloor = Percent(Math.Max(50, 85 - discount)) - WageCredit(bonuses);
            return (int)Math.Max(1, Math.Max(baseFloor, askingFloor));
        }

        public static CrewContract Offer(CharacterDefinition character, int day, int duration = 12,
            PerformanceBonuses bonuses = default)
        {
            if (duration < 1 || duration > int.MaxValue - day) throw new ArgumentOutOfRangeException(nameof(duration));
            if (!bonuses.Valid) throw new ArgumentOutOfRangeException(nameof(bonuses));
            long concession = WageCredit(bonuses);
            int baseValue = PlayerMarket.BaseValue(character);
            return new CrewContract { StartDay = day, EndDay = day + duration,
                DailyWage = (int)Math.Max(1, 2 + baseValue / 18 - concession),
                Bonuses = bonuses.Terms, Rates = bonuses };
        }
        // Used by both the interactive quote and the signing transaction.
        public static CrewContract Quote(CharacterDefinition character, int day, int days, int expected,
            PerformanceBonuses terms, long signingFee)
        {
            if (day < 1 || days < 1 || days > int.MaxValue - day || expected < 1 || !terms.Valid || signingFee < 0 ||
                !terms.Installments.Fits(days)) return null;
            bool financed = terms.Installments.Enabled;
            long basis = (long)days * expected;
            if (financed && signingFee > basis || !financed && signingFee / days > int.MaxValue) return null;
            return new CrewContract {
                DeferredTerms = true, StartDay = day, EndDay = day + days, SigningFee = signingFee,
                DailyWage = financed ? expected : (int)(signingFee / days),
                Bonuses = terms.Terms, Rates = terms, InterestPercent = WageInstallments.PremiumPercent(character.Personality),
                ReleaseFee = financed ? Math.Max(2, PlayerMarket.BaseValue(character) / 4) : 0
            };
        }
    }

    public sealed partial class PartyRun
    {
        public const int RosterLimit = 6;
        private int talentSeed;
        private int payrollPausedDays;
        public int Day { get; private set; }
        public int ContractDay => Day - payrollPausedDays;
        public bool ContractsEnabled { get; }
        public long PayrollDebt { get; private set; }
        public bool NeedsRenewal => ContractsEnabled && members.Any(m => m.Contract != null && m.LastContract.EndDay - ContractDay <= 3);

        internal void StartContracts(int seed, int day = 1)
        {
            if (Day != 0) return;
            talentSeed = seed;
            Day = day;
            foreach (var member in members) PrepareHire(member);
            RefreshEquipmentSlots();
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
                member.Attributes = EmployeeAttributes.Starting;
            if (Day > 0 && member.Contract == null)
            {
                member.Trait = TalentFor(member.Character);
                member.Contract = CrewContract.Offer(member.Character, ContractDay);
                if (member.Trait == PlayerTrait.Sturdy && member.Health > 0) member.Health += 2;
            }
            RefreshEmployeeGoals(member);
        }

        private static Stats TraitBonus(Member m) => new Stats(m.Trait == PlayerTrait.Brawn ? 1 : 0,
            m.Trait == PlayerTrait.Tough ? 1 : 0, m.Trait == PlayerTrait.Warded ? 1 : 0, m.Trait == PlayerTrait.Sturdy ? 2 : 0);

        internal CrewContract CandidateContract(CharacterDefinition candidate, int days, PerformanceBonuses terms, long? fee,
            CampTraining training = default)
        {
            if (candidate == null) return null;
            int expected = CrewContract.ExpectedWage(candidate, TalentFor(candidate), trainingSessions: training.Sessions);
            return NegotiatedContract(candidate, EmployeeAttributes.Starting, expected, days, terms, fee);
        }

        private CrewContract NegotiatedContract(CharacterDefinition character, EmployeeAttributes ratings, int expected,
            int days, PerformanceBonuses terms, long? fee, int? start = null)
        {
            var draft = CrewContract.Quote(character, start ?? ContractDay, days, expected, terms, fee ?? (terms.Installments.Enabled ? 0 : (long)expected * days));
            if (draft == null || draft.DailyWage < CrewContract.MinimumWage(expected, terms, ratings)) return null;
            return draft;
        }

        internal void SignNewHire(string characterId, CrewContract draft)
        {
            var member = members.Single(m => m.Character.Id == characterId);
            draft.RecordSigningPayment();
            draft.OpenInterestPeriod(ContractDay);
            member.Contract = draft;
            RefreshEmployeeGoals(member);
        }

        public bool SetContract(string id, int days, PerformanceBonuses bonuses, long? signingFee = null)
        {
            var m = FindMember(id);
            if (!ContractsEnabled || m == null || activeBattle != null) return false;
            var draft = NegotiatedContract(m.Character, m.Attributes, ExpectedWage(m), days, bonuses, signingFee, m.LastContract?.EndDay);
            if (draft == null || draft.DueAtSigning > Gold) return false;
            PaySigningWage(m.Character.Name, draft);
            draft.RecordSigningPayment();
            if (draft.StartDay == ContractDay)
                ActivateContract(m, draft);
            else m.Renewals.Add(draft);
            RefreshEmployeeGoals(m);
            return true;
        }

        private void ActivateContract(Member member, CrewContract draft)
        {
            member.UnpaidWages += member.Contract?.Arrears ?? 0;
            member.Contract = draft;
            draft.OpenInterestPeriod(ContractDay);
        }

        internal void PaySigningWage(string name, CrewContract draft) =>
            TrySpendGold((int)draft.DueAtSigning, name + " · 签字费");

        private Random CandidateRandom(CharacterDefinition c, int salt) =>
            new Random(unchecked(talentSeed ^ (Day * 7919 + c.RosterNumber * 104729 + salt)));

        private int QuotePercent(CharacterDefinition c, int salt, int minimum, int maximum) =>
            CandidateRandom(c, salt).Next(minimum, maximum + 1);

        public CampTraining TownCandidateTraining(CharacterDefinition c)
        {
            var random = CandidateRandom(c, 9199);
            int roll = random.Next(100);
            if (roll >= 25) return default;
            var training = CampTraining.Roll(random);
            return roll < 5 ? training.With(CampTraining.Roll(random)) : training;
        }

        public int SigningPrice(CharacterDefinition c) =>
            (PlayerMarket.Value(c, TalentFor(c), trainingSessions: TownCandidateTraining(c).Sessions)
                * QuotePercent(c, 431, 101, 110) + 99) / 100;

        public int TransferOffer(string memberId)
        {
            var m = FindMember(memberId);
            return m == null ? 0 : Math.Max(1, PlayerMarket.Value(m.Character, m.Trait, m.LearnedSkills, m.Training.Sessions)
                * QuotePercent(m.Character, 173, 90, 99) / 100);
        }

        internal int BrokerBudget(string id, int opening)
        {
            int premium = QuotePercent(FindMember(id).Character, 283, 32, 54);
            long room = Math.Max(12, (long)opening * premium / 100);
            return (int)Math.Min(int.MaxValue, opening + room);
        }

        public bool CanSellMember(string id)
        {
            var m = FindMember(id);
            return activeBattle == null && m != null && (!ContractsEnabled || m.Contract != null) &&
                !m.IsAway && members.Any(x => x != m && !x.IsAway && x.Health > 0);
        }

        public bool SellMember(string id) => SellMember(id, TransferOffer(id));

        internal bool SellMember(string id, int price)
        {
            if (price < 1 || !CanSellMember(id)) return false;
            var m = FindMember(id);
            long proceeds = price - TransferExitCostFor(id);
            if (proceeds > int.MaxValue - Gold || proceeds < -Gold) return false;
            if (proceeds < 0) Gold += (int)proceeds;
            if (ContractsEnabled) RecordExitExpenses(m);
            members.Remove(m);
            if (proceeds > 0) AddGold((int)proceeds);
            DeployReserveIfNeeded();
            return true;
        }

        private void RecordExitExpenses(Member member)
        {
            RecordExpense(member.Character.Name + " · 结清旧合同分期", member.Contract?.Outstanding ?? 0);
            RecordExpense(member.Character.Name + " · 违约金", member.Contract?.CurrentReleaseFee ?? 0);
            PayrollDebt -= member.Contract?.Arrears ?? 0;
        }

        public long TransferExitCostFor(string id) =>
            ContractsEnabled ? FindMember(id)?.Contract?.ExitCost ?? 0 : 0;

        private static bool IsPayrollExpense(string reason)
        {
            if (reason == "补付拖欠薪资") return true;
            int separator = reason?.LastIndexOf(" · ", StringComparison.Ordinal) ?? -1;
            if (separator < 0) return false;
            string category = reason.Substring(separator + 3);
            return category is "结清旧合同分期" or "违约金" or "签字费" or "偿还薪资" or "每日工资"
                or "出场费" or "击杀奖" or "承伤奖" or "抗伤奖" ||
                category.StartsWith("第 ", StringComparison.Ordinal) && category.EndsWith(" 期工资", StringComparison.Ordinal);
        }

        private void DeployReserveIfNeeded()
        {
            if (!members.Any(IsDeployed))
            {
                var reserve = members.FirstOrDefault(m => !m.IsAway && m.Health > 0);
                if (reserve != null) reserve.Position = 1;
            }
            RefreshEquipmentSlots();
        }

        private int Pay(long amount, string reason)
        {
            int paid = (int)Math.Min(Gold, amount);
            Gold -= paid;
            RecordExpense(reason, paid);
            PayrollDebt += amount - paid;
            return paid;
        }

        private void PayWages(Member member, long amount, string reason) =>
            member.UnpaidWages += amount - Pay(amount, member.Character.Name + " · " + reason);

        private long WageBalance(Member member) => member.UnpaidWages + (member.Contract?.PayoffToday(ContractDay) ?? 0);

        public long WageBalanceFor(string memberId) =>
            ContractsEnabled && FindMember(memberId) is { } member ? WageBalance(member) : 0;

        public bool TryRepayWages(string memberId, int amount)
        {
            var member = FindMember(memberId);
            if (!ContractsEnabled || member == null || amount <= 0 || amount > WageBalance(member) ||
                !TrySpendGold(amount, member.Character.Name + " · 偿还薪资")) return false;

            long repaid = RepayArrears(member, amount);
            PayrollDebt -= repaid;
            member.Contract?.PayEarly(amount - repaid, ContractDay);
            return true;
        }

        private long RepayArrears(Member member, long amount)
        {
            long contract = member.Contract?.RepayArrears(amount, ContractDay) ?? 0;
            long wages = Math.Min(amount - contract, member.UnpaidWages);
            member.UnpaidWages -= wages;
            return contract + wages;
        }

        internal string[] RemoveUnpaidEmployees()
        {
            var leaving = members.Where(member => WageBalance(member) > 0).ToArray();
            foreach (var member in leaving)
            {
                // Their earned and contracted wages stay in the company ledger after departure.
                PayrollDebt += WageBalance(member) - member.UnpaidWages - (member.Contract?.Arrears ?? 0);
                members.Remove(member);
            }
            DeployReserveIfNeeded();
            return leaving.Select(member => member.Character.Name).ToArray();
        }

        public bool CanAdvanceDay => members.Any(m => !m.IsAway && m.Health > 0 &&
            (!ContractsEnabled || m.LastContract?.EndDay > ContractDay));

        internal void AdvanceDay()
        {
            Day++;
            expenses.RemoveAll(e => e.Day < Day - 2);
            if (ContractsEnabled) members.RemoveAll(m => m.Contract.EndDay < ContractDay);
            else payrollPausedDays++;
            foreach (var member in members)
            {
                member.Career.ServiceDays++;
                if (!ContractsEnabled) continue;
                var contract = member.Contract;
                if (contract.NextDueDay == ContractDay)
                {
                    long principal = contract.PrincipalAt(++contract.InstallmentsPaid);
                    long interest = contract.CloseInterestPeriod(contract.InstallmentsPaid == contract.Plan.Count ||
                        contract.RemainingPrincipal == 0 && Gold >= principal);
                    int paid = Pay(principal + interest, $"{member.Character.Name} · 第 {contract.InstallmentsPaid} 期工资");
                    contract.PrincipalInArrears += Math.Max(0, principal - paid);
                    contract.InterestInArrears += interest - Math.Max(0, paid - principal);
                    contract.RecordSigningPayment();
                    contract.OpenInterestPeriod(ContractDay);
                }
                else if (!contract.BasePaid && !contract.Plan.Enabled)
                    PayWages(member, contract.DailyWage, "每日工资");
                if (contract.EndDay == ContractDay && member.Renewals.Count > 0)
                {
                    ActivateContract(member, member.Renewals[0]);
                    member.Renewals.RemoveAt(0);
                }
            }
            DeployReserveIfNeeded();
            foreach (var member in members) RefreshEmployeeGoals(member);
        }

        private void SettlePerformance(BattleResult result, HashSet<string> deployed)
        {
            if (!ContractsEnabled) return;
            var enemies = activeBattle.Combatants.Where(u => u.Side == Side.Enemy).Select(u => u.Id).ToHashSet();
            foreach (var m in members.Where(m => m.Contract != null && deployed.Contains(m.Id)))
            {
                var rates = m.Contract.AgreedRates;
                PayWages(m, rates.Appearance, "出场费");
                if (rates.Kill > 0)
                {
                    int kills = result.Events.Count(e => e.Kind == BattleEventKind.Defeated && e.ActorId == m.Id && enemies.Contains(e.TargetId));
                    PayWages(m, (long)rates.Kill * kills, "击杀奖");
                }
                if (rates.DamageTaken > 0)
                {
                    long damage = result.Events.Where(e => e.TargetId == m.Id).Sum(e => e.DamageTaken.Sum(d => (long)d.Amount));
                    PayWages(m, rates.DamageTaken * (damage / 5), "承伤奖");
                }
                if (rates.Shield > 0)
                {
                    long blocked = result.Events.Where(e => e.TargetId == m.Id && enemies.Contains(e.ActorId)).Sum(e => (long)e.ShieldAbsorbed);
                    PayWages(m, rates.Shield * (blocked / 5), "抗伤奖");
                }
            }
        }
    }
}
