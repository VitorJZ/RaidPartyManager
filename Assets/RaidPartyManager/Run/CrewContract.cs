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
        // Amount is read only by contracts saved with the original, fixed-payment schedule.
        public bool Fits(int duration) => !Enabled || Count >= 1 && IntervalDays >= 1 && (long)Count * IntervalDays <= duration;

        public static int PremiumPercent(Personality personality) => personality switch
        {
            Personality.Caring or Personality.Optimist => 0,
            Personality.Stoic or Personality.Curious => 5,
            Personality.Frank or Personality.Deadpan => 10,
            Personality.Theatrical or Personality.Competitive => 15,
            _ => 20
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
        public bool BasePaid;
        public int InstallmentsPaid;
        public WageInstallments Plan => Rates.Installments;
        // Old saves keep their signed fixed amounts and their first payment on the signing date.
        public bool DeferredTerms;
        public long SigningFee;
        public int InterestPercent;
        public long PrincipalInArrears, InterestInArrears, AccruedInterest, InterestRemainder;
        public int Days => EndDay - StartDay;
        public long BaseTotal => DeferredTerms && !Plan.Enabled ? SigningFee : (long)DailyWage * Days;
        public long DueAtSigning => DeferredTerms ? SigningFee : Plan.Enabled ? Plan.Amount : BaseTotal;
        private long Financed => BaseTotal - SigningFee;
        public long Arrears => PrincipalInArrears + InterestInArrears;
        public long RemainingPrincipal => !Plan.Enabled ? 0 : DeferredTerms
            ? PrincipalAfter(InstallmentsPaid) + PrincipalInArrears : (long)(Plan.Count - InstallmentsPaid) * Plan.Amount;
        public long Outstanding => RemainingPrincipal + InterestInArrears + AccruedInterest + (InterestRemainder > 0 ? 1 : 0);
        public int CurrentReleaseFee => Plan.Enabled && Outstanding > 0 ? (int)Math.Min(Outstanding, ReleaseFee) : 0;
        public long ExitCost => Outstanding + CurrentReleaseFee;
        public int? NextDueDay => Plan.Enabled && !BasePaid && InstallmentsPaid < Plan.Count
            ? StartDay + (InstallmentsPaid + (DeferredTerms ? 1 : 0)) * Plan.IntervalDays : (int?)null;
        internal CrewContract Copy() => (CrewContract)MemberwiseClone();
        internal void RecordSigningPayment() => BasePaid = Outstanding == 0;

        private long PrincipalAfter(int count) =>
            Financed - (Financed / Plan.Count * count + Math.Min(count, Financed % Plan.Count));
        public long PrincipalAt(int period) => PrincipalAfter(period - 1) - PrincipalAfter(period);

        // Sum of the opening balances for the first k periods. This also supports very long contracts
        // without constructing a payment list. Only the final interest charge rounds up.
        private long ScheduledInterestThrough(int k)
        {
            long extra = Math.Min(k, Financed % Plan.Count);
            decimal paidBalances = (decimal)(Financed / Plan.Count) * k * (k - 1) / 2
                + (decimal)extra * (extra - 1) / 2 + (decimal)(k - extra) * extra;
            decimal exposure = ((decimal)k * Financed - paidBalances) * Plan.IntervalDays;
            long divisor = (long)Days * 100;
            decimal remainder = exposure % divisor;
            decimal whole = (exposure - remainder) / divisor * InterestPercent;
            decimal fraction = remainder * InterestPercent / divisor;
            // With a nearly full deposit, the principal can be paid off before the last listed period.
            return (long)(whole + (PrincipalAfter(k) == 0 ? decimal.Ceiling(fraction) : decimal.Floor(fraction)));
        }

        public long ScheduledPayment(int period) => !DeferredTerms ? Plan.Amount :
            PrincipalAt(period) + ScheduledInterestThrough(period) - ScheduledInterestThrough(period - 1);
        public long ScheduledInterest => !Plan.Enabled ? 0 : DeferredTerms ? ScheduledInterestThrough(Plan.Count) : 0;

        internal void AccrueDay()
        {
            long divisor = (long)Days * 100;
            decimal units = (decimal)RemainingPrincipal * InterestPercent + InterestRemainder;
            AccruedInterest += (long)(units / divisor);
            InterestRemainder = (long)(units % divisor);
        }

        internal bool ValidAt(int day)
        {
            if (StartDay < 1 || EndDay <= StartDay || EndDay <= day || DailyWage < 1 || !Rates.Valid || !AgreedRates.Valid) return false;
            if (!Plan.Enabled) return !DeferredTerms || SigningFee >= Days;
            bool validSchedule = DeferredTerms ? Plan.Fits(Days) && SigningFee >= 0 && SigningFee <= BaseTotal &&
                InterestPercent is >= 0 and <= 20 && PrincipalInArrears >= 0 && InterestInArrears >= 0 && AccruedInterest >= 0 &&
                InterestRemainder >= 0 && InterestRemainder < (long)Days * 100 :
                Plan.Count >= 2 && Plan.IntervalDays > 0 && Plan.Amount > 0 && ((long)Plan.Count - 1) * Plan.IntervalDays <= Days;
            return validSchedule && InstallmentsPaid >= (DeferredTerms ? 0 : 1) && InstallmentsPaid <= Plan.Count && !(NextDueDay <= day);
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
                member.Contract = CrewContract.Offer(member.Character, Day);
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
            int days, PerformanceBonuses terms, long? fee)
        {
            var draft = CrewContract.Quote(character, Day, days, expected, terms, fee ?? (terms.Installments.Enabled ? 0 : (long)expected * days));
            if (draft == null || draft.DailyWage < CrewContract.MinimumWage(expected, terms, ratings)) return null;
            return draft;
        }

        internal void SignNewHire(string characterId, CrewContract draft)
        {
            var member = members.Single(m => m.Character.Id == characterId);
            draft.RecordSigningPayment();
            member.Contract = draft;
            RefreshEmployeeGoals(member);
        }

        public bool SetContract(string id, int days, PerformanceBonuses bonuses, long? signingFee = null)
        {
            var m = FindMember(id);
            if (m == null || activeBattle != null) return false;
            var draft = NegotiatedContract(m.Character, m.Attributes, ExpectedWage(m), days, bonuses, signingFee);
            if (draft == null || draft.DueAtSigning > Gold || (m.Contract?.ExitCost ?? 0) > Gold - draft.DueAtSigning) return false;
            Gold -= (int)(m.Contract?.ExitCost ?? 0);
            RecordExitExpenses(m);
            PaySigningWage(m.Character.Name, draft);
            draft.RecordSigningPayment();
            m.Contract = draft;
            RefreshEmployeeGoals(m);
            return true;
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
            RecordExpense(member.Character.Name + " · 违约金", member.Contract?.CurrentReleaseFee ?? 0);
            PayrollDebt -= member.Contract?.Arrears ?? 0;
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

        public bool CanAdvanceDay => members.Any(m => !m.IsAway && m.Health > 0 && m.Contract.EndDay > Day + 1);

        internal void AdvanceDay()
        {
            Day++;
            expenses.RemoveAll(e => e.Day < Day - 2);
            foreach (var member in members)
            {
                member.Career.ServiceDays++;
                var contract = member.Contract;
                if (contract.DeferredTerms && contract.Plan.Enabled)
                {
                    contract.AccrueDay();
                    bool due = contract.NextDueDay == Day;
                    long principal = due ? contract.PrincipalAt(++contract.InstallmentsPaid) : 0;
                    bool final = contract.InstallmentsPaid == contract.Plan.Count;
                    if (due || final)
                    {
                        long interest = contract.AccruedInterest;
                        if (contract.InterestRemainder > 0 && (Day == contract.EndDay || contract.RemainingPrincipal == 0 && Gold >= principal))
                        {
                            interest++;
                            contract.InterestRemainder = 0;
                        }
                        contract.AccruedInterest = 0;
                        int paid = Pay(principal + interest, $"{member.Character.Name} · 第 {contract.InstallmentsPaid} 期工资");
                        contract.PrincipalInArrears += Math.Max(0, principal - paid);
                        contract.InterestInArrears += interest - Math.Max(0, paid - principal);
                    }
                    contract.BasePaid = contract.Outstanding == 0;
                }
                else if (contract.NextDueDay == Day)
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
