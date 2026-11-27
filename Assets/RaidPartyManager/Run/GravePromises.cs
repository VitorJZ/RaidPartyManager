using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    [Serializable]
    public sealed class GravePromises
    {
        public string PressCorrection;
        public string Porter, Interview, Wreath, Alias, RealName, PartnerA, PartnerB;
        public int Bell;
        public bool Scout, Separate;
        public GravePromises Copy() => (GravePromises)MemberwiseClone();
    }

    public sealed partial class PartyRun
    {
        private GravePromises grave = new();
        internal GravePromises Promises => grave;
        public bool GraveScout => grave.Scout;
        public bool HasBorrowedName => !string.IsNullOrEmpty(grave.RealName);
        public string BorrowedRealName => grave.RealName;
        private CharacterDefinition DisplayCharacter(Member member) => member.Id == grave.Alias
            ? member.Character.WithName("某位员工") : member.Character;

        public bool ReclaimName(string name)
        {
            if (!HasBorrowedName || name != grave.RealName) return false;
            GraveMemory(grave.Alias, "我在出园登记簿上认出了自己的名字。");
            grave.Alias = grave.RealName = null;
            return true;
        }

        internal string CollectPressCorrection()
        {
            string notice = grave.PressCorrection;
            grave.PressCorrection = null;
            return notice ?? "";
        }

        internal string DeliverWreath()
        {
            if (string.IsNullOrEmpty(grave.Wreath)) return "";
            string recipient = grave.Wreath;
            grave.Wreath = null;
            return $"花商举着一只白花圈来接{recipient}。缎带上写着「合作愉快，明年续签」。";
        }

        internal void GraveMemory(string id, string words, bool happier = false)
        {
            var employee = FindMember(id);
            if (employee == null) return;
            if (happier) employee.Attributes.TryRaise(EmployeeRating.Satisfaction, out _);
            Remember(employee, EmployeeThought.GraveMemory, words);
        }

        internal void GiveSoup(string id)
        {
            var employee = FindMember(id);
            if (employee?.CanRecoverHealth == true) employee.Health = Math.Min(PersonalStats(employee).Health, employee.Health + 5);
        }

        internal (int first, int second) SharedHealth(string first, string second, bool settle = false)
        {
            var a = FindMember(first);
            var b = FindMember(second);
            int total = a.Health + b.Health;
            int hp = Math.Max(total - PersonalStats(b).Health, Math.Min(PersonalStats(a).Health, (total + 1) / 2));
            if ((!a.CanRecoverHealth && hp > a.Health) || (!b.CanRecoverHealth && total - hp > b.Health))
                hp = a.Health;
            if (settle) { a.Health = hp; b.Health = total - hp; }
            return (hp, total - hp);
        }

        internal bool WriteEpitaph(string id, EmployeeGoalKind kind, EmployeeGoalKind replace, string words)
        {
            var employee = FindMember(id);
            if (employee == null) return false;
            ref var career = ref employee.Career;
            if (career.Inscription.Kind != EmployeeGoalKind.None) return false;
            var active = EmployeeProfileFor(id).Goals;
            if (active.Count >= 2 && !active.Any(goal => goal.Kind == replace)) return false;
            if (replace != EmployeeGoalKind.None)
            {
                if (career.Work.Kind == replace) career.Work = default;
                else if (career.Development.Kind == replace) career.Development = default;
                else if (career.Raise.Kind == replace) career.Raise = default;
                else if (career.Renewal.Kind == replace) career.Renewal = default;
                else if (career.Relationship.Kind == replace) career.Relationship = default;
            }
            long baseline = kind == EmployeeGoalKind.Healing ? career.Healed :
                kind == EmployeeGoalKind.Blocking ? career.Blocked : career.Appearances;
            career.Inscription = Goal(kind, kind == EmployeeGoalKind.Appearances ? 2 : 10, baseline, "grave-epitaph", words);
            return true;
        }

        private void SettleGravePromises(BattleResult result, HashSet<string> participants)
        {
            if (Survived(grave.Interview))
            {
                AddGold(12);
                grave.PressCorrection = $"《墓园晚报》更正：{FindMember(grave.Interview)?.Character.Name}亲自出战后平安返回。稿费 12 金币已入账。 ";
                GraveMemory(grave.Interview, "我亲自拿到了讣告更正样报。稿费十二枚，我数过。", true);
            }
            if (grave.PartnerA != null && (grave.Separate
                    ? participants.Contains(grave.PartnerA) != participants.Contains(grave.PartnerB)
                    : Survived(grave.PartnerA) && Survived(grave.PartnerB)))
            {
                foreach (string id in new[] { grave.PartnerA, grave.PartnerB })
                {
                    var member = FindMember(id);
                    if (member == null) continue;
                    string other = id == grave.PartnerA ? grave.PartnerB : grave.PartnerA;
                    if (member.Career.RivalId == other)
                    {
                        member.Career.RivalId = member.Career.RivalCharacterId = null;
                        member.Career.RivalIncidents = 0;
                        member.Career.Relationship = default;
                    }
                    GraveMemory(id, grave.Separate ? "我想分开值班。队长真的记住了。" : "我愿意再和那家伙搭一回班。", true);
                }
            }
            grave.Interview = grave.Porter = grave.PartnerA = grave.PartnerB = null;
            grave.Bell = 0;
            grave.Separate = grave.Scout = false;
            bool Survived(string id) => id != null && participants.Contains(id) && result.HealthById.TryGetValue(id, out int hp) && hp > 0;
        }
    }
}
