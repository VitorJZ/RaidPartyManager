using System;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class EmployeeDossier : ScrollView
    {
        internal EmployeeDossier(MemberView member, EmployeeProfile profile, Action openContract) : base(ScrollViewMode.Vertical)
        {
            name = "employee-dossier";
            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minHeight = 0;
            contentContainer.style.paddingRight = 8;

            var chart = new EmployeeAttributeChart(member.EmployeeAttributes);
            chart.style.width = Length.Percent(100);
            chart.style.maxWidth = 340;
            chart.style.alignSelf = Align.Center;
            Add(chart);

            var career = profile.Career;
            var performance = Section("业绩", "employee-performance");
            var values = PrototypeStyle.Row();
            values.style.flexWrap = Wrap.Wrap;
            Metric("damage", "造成的伤害", career.Damage);
            Metric("damage-taken", "承受伤害", career.DamageTaken, "实际损失的生命，扣除护盾吸收及过量伤害");
            Metric("healing", "治疗量", career.Healed, "普攻、技能和装备实际恢复的生命，溢出治疗不计入");
            Metric("kills", "杀敌数", career.Kills);
            Metric("tenure", "工龄", career.ServiceDays, "每进入一个新节点增加一天", " 天");
            performance.Add(values);
            Add(performance);

            var thoughts = Section("想法", "employee-thoughts");
            var voice = EmployeeVoice.For(member.Personality);
            var personality = PrototypeStyle.Text("性格 · " + voice.Name, 13, PrototypeStyle.Accent);
            personality.name = "employee-personality";
            personality.tooltip = voice.Description;
            thoughts.Add(personality);
            foreach (string text in profile.Thoughts)
            {
                var thought = PrototypeStyle.Text("“" + text + "”", 14, PrototypeStyle.Ink);
                thought.style.marginBottom = 5;
                thoughts.Add(thought);
            }
            Add(thoughts);

            var goals = Section("目标", "employee-goals");
            if (!string.IsNullOrEmpty(career.RecordedEpitaph)) goals.Add(PrototypeStyle.Text("墓志铭 · " + career.RecordedEpitaph, 14, PrototypeStyle.Ally));
            foreach (var goal in profile.Goals)
                goals.Add(GoalCard(goal));
            if (career.GoalsCompleted > 0)
            {
                var last = career.LastCompleted;
                string growth = last.Awarded == EmployeeRating.None ? "" :
                    $" · {RatingName(last.Awarded)}提高至 {last.AwardedLevel} 级";
                var completed = PrototypeStyle.Text($"已完成 {career.GoalsCompleted} 项 · 最近完成 {GoalText(last)}{growth}",
                    13, PrototypeStyle.Ally);
                completed.name = "employee-last-goal";
                completed.style.marginTop = 7;
                goals.Add(completed);
            }
            if (member.Contract != null)
            {
                var contract = PrototypeStyle.Button("查看合约", "employee-open-contract", openContract);
                contract.style.alignSelf = Align.FlexStart;
                contract.style.marginTop = 8;
                goals.Add(contract);
            }
            Add(goals);

            void Metric(string key, string title, long amount, string tooltip = null, string suffix = "")
            {
                var tile = new VisualElement { tooltip = tooltip };
                tile.style.flexGrow = 1;
                tile.style.flexBasis = 96;
                tile.style.minWidth = 96;
                tile.style.paddingLeft = tile.style.paddingRight = 8;
                tile.style.paddingTop = tile.style.paddingBottom = 5;
                tile.Add(PrototypeStyle.Text(title, 12, PrototypeStyle.Muted));
                var number = PrototypeStyle.Text(amount.ToString("N0") + suffix, 20, PrototypeStyle.Ink);
                number.name = "employee-stat-" + key;
                tile.Add(number);
                values.Add(tile);
            }
        }

        private static VisualElement Section(string title, string name)
        {
            var section = new VisualElement { name = name };
            section.style.backgroundColor = PrototypeStyle.Inset;
            section.style.marginTop = 9;
            section.style.paddingLeft = section.style.paddingRight = 12;
            section.style.paddingTop = section.style.paddingBottom = 8;
            PrototypeStyle.Outline(section, PrototypeStyle.Edge, 5);
            var heading = PrototypeStyle.Text(title, 16, PrototypeStyle.Accent);
            heading.style.marginBottom = 5;
            section.Add(heading);
            return section;
        }

        private static VisualElement GoalCard(EmployeeGoal goal)
        {
            var row = PrototypeStyle.Row();
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            row.style.paddingBottom = 5;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = PrototypeStyle.Edge;
            var description = PrototypeStyle.Text(goal.SubjectId == "grave-epitaph" ? $"拟刻「{goal.SubjectName}」 · {GoalText(goal)}" : GoalText(goal), 14);
            description.style.flexGrow = 1;
            description.style.flexBasis = 0;
            description.style.minWidth = 0;
            row.Add(description);
            var progress = PrototypeStyle.Text($"{goal.Progress} / {goal.Target}", 13, PrototypeStyle.Accent);
            progress.style.flexShrink = 0;
            progress.style.marginLeft = 10;
            row.Add(progress);
            return row;
        }

        private static string GoalText(EmployeeGoal goal) => goal.Kind switch
        {
            EmployeeGoalKind.Appearances => $"接下来上场 {goal.Target} 次",
            EmployeeGoalKind.Damage => $"累计再造成 {goal.Target} 点伤害",
            EmployeeGoalKind.Blocking => $"累计再抵挡 {goal.Target} 点伤害",
            EmployeeGoalKind.Healing => $"累计再治疗 {goal.Target} 点生命",
            EmployeeGoalKind.Kills => $"再击败 {goal.Target} 名敌人",
            EmployeeGoalKind.Victories => $"随队再获胜 {goal.Target} 场",
            EmployeeGoalKind.Tenure => $"再为讨伐队效力 {goal.Target} 天",
            EmployeeGoalKind.Equipment => $"装备「{goal.SubjectName}」",
            EmployeeGoalKind.Training => $"学会「{goal.SubjectName}」",
            EmployeeGoalKind.Raise => $"把日薪提高到 {goal.Target} 金币",
            EmployeeGoalKind.Renewal => $"把合约续签到第 {goal.Target} 天或之后",
            EmployeeGoalKind.SeparateLineups => $"在没有{goal.SubjectName}同场的情况下出战 {goal.Target} 次",
            _ => ""
        };

        private static string RatingName(EmployeeRating rating) => rating switch
        {
            EmployeeRating.Loyalty => "忠诚程度",
            EmployeeRating.Obedience => "战术服从",
            EmployeeRating.PayPreference => "薪酬偏好",
            EmployeeRating.PlayingTime => "上场需求",
            EmployeeRating.Satisfaction => "满意程度",
            EmployeeRating.Hardiness => "吃苦耐劳",
            _ => ""
        };
    }
}
