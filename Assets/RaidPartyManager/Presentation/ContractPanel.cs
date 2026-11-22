using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal static class ContractPanel
    {
        private static readonly Color Ink = new Color32(57, 38, 23, 255);
        private static readonly Color Seal = new Color32(117, 48, 31, 255);

        public static Label Text(string words, int size) => PrototypeStyle.Text(words, size, Ink);

        public static VisualElement Sheet(string title, out ScrollView paper)
        {
            var shade = new VisualElement { name = "crew-contract" };
            PrototypeStyle.Fill(shade);
            shade.style.backgroundColor = new Color(.055f, .044f, .034f, .8f);
            shade.style.justifyContent = Justify.Center;
            shade.style.alignItems = Align.Center;
            shade.styleSheets.Add(Resources.Load<StyleSheet>("CrewContractPaper"));
            paper = new ScrollView { name = "contract-paper" };
            paper.style.width = Length.Percent(92);
            paper.style.maxWidth = 640;
            paper.style.maxHeight = Length.Percent(94);
            paper.style.backgroundColor = (Color)new Color32(242, 211, 141, 255);
            paper.style.paddingLeft = paper.style.paddingRight = 25;
            paper.style.paddingTop = paper.style.paddingBottom = 17;
            PrototypeStyle.Outline(paper, new Color32(155, 111, 55, 255), 2);
            var heading = PrototypeStyle.Text(title, 23, Seal);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.borderBottomWidth = 1;
            heading.style.borderBottomColor = Seal;
            heading.style.paddingBottom = heading.style.marginBottom = 8;
            paper.Add(heading);
            shade.Add(paper);
            return shade;
        }

        public static string Summary(CharacterDefinition c, CrewContract contract) =>
            $"让{c.Name}成为玩家的讨伐队成员。第 {contract.StartDay} 天签约，第 {contract.EndDay} 天到期，共 {contract.EndDay - contract.StartDay} 天。每日工资 {contract.DailyWage} 金币。" + (contract.Plan.Enabled ? $"分 {contract.Plan.Count} 期，每 {contract.Plan.IntervalDays} 天付 {contract.Plan.Amount} 金币，已结算 {contract.InstallmentsPaid} 期。" +
                (contract.NextDueDay is int due ? $"下期在第 {due} 天支付。" : "") : contract.BasePaid ? "整期底薪已付清。" : "");

        public static VisualElement Negotiate(CharacterDefinition c, int day, PlayerTrait talent, int tradeQuote,
            CrewContract current, int expectedWage, Func<int, PerformanceBonuses, bool, int?, bool> sign, Action close)
        {
            var view = Sheet(c.Name + " · 讨伐队合约", out var paper);
            paper.Add(Text(PlayerMarket.Describe(talent), 15));
            if (current != null)
            {
                paper.Add(Text(Summary(c, current) + $" 剩余 {current.EndDay - day} 天。", 16));
                paper.Add(Text(Terms(current), 14));
            }
            else paper.Add(Text($"签约费用　{tradeQuote} 金币", 17));

            var edits = new VisualElement();
            var length = new IntegerField("合同天数（至少 10 天）") { name = "contract-duration", value = 12 };
            edits.Add(length);
            var wage = new IntegerField("每日工资（金币）") { name = "contract-daily-wage", value = Math.Max(current?.DailyWage ?? 0, expectedWage) };
            edits.Add(wage);
            edits.Add(Text($"期望日薪 {expectedWage} 金币　·　默认签字时付清底薪，重签时已付工资不退。", 14));
            edits.Add(Text("额外薪资", 16));
            var rates = current?.AgreedRates ?? default;
            var appearance = Bonus("出场费", "每次出场", "appearance", rates.Appearance);
            var kill = Bonus("击杀奖", "每击杀 1 名敌人", "kill", rates.Kill);
            var damage = Bonus("承伤奖", "每场每损失 5 点生命", "damage", rates.DamageTaken);
            var shield = Bonus("抗伤奖", "每场护盾每挡下 5 点敌方伤害", "shield", rates.Shield);
            var plan = current?.Plan ?? default;
            int premium = WageInstallments.PremiumPercent(c.Personality);
            var installments = new Toggle { name = "installment-enabled", text = "分期支付底薪", value = plan.Enabled };
            edits.Add(installments);
            var schedule = new VisualElement();
            var count = new IntegerField("总期数（含首期）") { name = "installment-count", value = plan.Enabled ? plan.Count : 2 };
            var interval = new IntegerField("每期间隔（天）") { name = "installment-days", value = plan.Enabled ? plan.IntervalDays : 5 };
            var amount = new IntegerField("每期支付（金币）") { name = "installment-amount", value = plan.Enabled ? plan.Amount :
                (int)Math.Min(int.MaxValue, (WageInstallments.MinimumTotal(c.Personality, wage.value, length.value) + 1) / 2) };
            schedule.Add(count); schedule.Add(interval); schedule.Add(amount);
            schedule.Add(Text($"{EmployeeVoice.For(c.Personality).Name} · 分期加价 {premium}%。首期签字时支付，最后一期最迟在到期日支付。", 14));
            edits.Add(schedule);
            var release = new Toggle { text = "提前出售时由讨伐队支付违约金", value = current?.ReleaseFee > 0 };
            var quote = PrototypeStyle.Text("", 15, Seal);
            quote.name = "contract-preview";
            var submit = PrototypeStyle.Button("签订新合同", "sign-crew-contract", () => {
                var terms = ReadRates();
                if (wage.value < CrewContract.MinimumWage(expectedWage, terms, release.value))
                {
                    GameAudio.Play(SoundCue.UiRestricted);
                    quote.text = $"{c.Name}拒绝了报价：“每天 {wage.value} 金币我不签。我希望日薪有 {expectedWage} 金币，额外薪资也可以谈。”";
                    return;
                }
                if (terms.Installments.Enabled && terms.Installments.Total < WageInstallments.MinimumTotal(c.Personality, wage.value, length.value))
                {
                    GameAudio.Play(SoundCue.UiRestricted);
                    quote.text = $"{c.Name}拒绝分期报价：“分期我至少要拿到 {WageInstallments.MinimumTotal(c.Personality, wage.value, length.value)} 金币。”";
                    return;
                }
                if (sign(length.value, terms, release.value, wage.value)) close();
                else quote.text = "签约失败，请检查余额、人数及合同期限。";
            }, true);
            edits.RegisterCallback<ChangeEvent<bool>>(_ => Refresh());
            edits.RegisterCallback<ChangeEvent<int>>(_ => Refresh());
            edits.Add(release);
            edits.Add(quote);
            edits.Add(submit);
            paper.Add(edits);
            paper.Add(PrototypeStyle.Button("返回", "close-contract", close));
            Refresh();
            return view;

            (Toggle enabled, IntegerField amount) Bonus(string label, string unit, string id, int rate)
            {
                var row = PrototypeStyle.Row();
                row.AddToClassList("contract-bonus");
                var toggle = new Toggle { text = label + " · " + unit, name = "bonus-" + id, value = rate > 0 };
                var amount = new IntegerField { name = "bonus-" + id + "-amount", value = rate };
                amount.SetEnabled(toggle.value);
                toggle.RegisterValueChangedCallback(e => amount.SetEnabled(e.newValue));
                row.Add(toggle);
                row.Add(amount);
                row.Add(Text("金币", 13));
                edits.Add(row);
                return (toggle, amount);
            }

            int Agreed((Toggle enabled, IntegerField amount) row)
            {
                if (!row.enabled.value) return 0;
                return row.amount.value > 0 ? row.amount.value : -1;
            }

            PerformanceBonuses ReadRates() => new PerformanceBonuses {
                Appearance = Agreed(appearance), Kill = Agreed(kill), DamageTaken = Agreed(damage), Shield = Agreed(shield),
                Installments = installments.value ? new WageInstallments { Enabled = true, Count = count.value, IntervalDays = interval.value, Amount = amount.value } : default
            };

            void Refresh()
            {
                var terms = ReadRates();
                bool validLength = length.value >= 10 && length.value <= int.MaxValue - day;
                bool validWage = wage.value >= 1;
                schedule.style.display = installments.value ? DisplayStyle.Flex : DisplayStyle.None;
                bool fits = terms.Installments.Fits(length.value);
                submit.SetEnabled(validLength && terms.Valid && validWage && fits);
                if (!validLength) { quote.text = "合同至少 10 天，请输入有效的期限。"; return; }
                if (!validWage) { quote.text = $"{c.Name}摇头：“一天连一枚金币都拿不到，这份合同我不签。”"; return; }
                if (!terms.Valid) { quote.text = "请为每项已勾选的奖金填写至少 1 金币。"; return; }
                if (!fits) { quote.text = "至少分 2 期，间隔和金额均须大于零，末期须在合同期内。"; return; }
                var draft = CrewContract.Offer(c, day, length.value, terms, release.value);
                quote.text = $"第 {draft.EndDay} 天到期　·　每日工资 {wage.value} 金币　·　提前出售违约金 {draft.ReleaseFee} 金币。" +
                    (current?.ReleaseFee > 0 && !release.value ? $"　移除原违约金条款需结清 {current.ReleaseFee} 金币。" : "");
                long basePay = (long)wage.value * length.value;
                long outstanding = current?.Outstanding ?? 0;
                long due = (installments.value ? amount.value : basePay) + tradeQuote + outstanding + (!release.value ? current?.ReleaseFee ?? 0 : 0);
                if (outstanding > 0) quote.text += $"　旧合同剩余工资 {outstanding} 金币需结清。";
                quote.text += installments.value ? $"\n分期总额 {terms.Installments.Total} 金币（最低 {WageInstallments.MinimumTotal(c.Personality, wage.value, length.value)}），首期 {amount.value} 金币。" : $"\n整期底薪 {basePay} 金币。";
                quote.text += $"签字时合计支付 {due} 金币。";
                if (current == null) quote.text = $"让{c.Name}成为玩家的讨伐队成员。\n" + quote.text;
            }
        }

        private static string Terms(CrewContract c)
        {
            var rates = c.AgreedRates;
            var parts = new List<string>();
            if (rates.Appearance > 0) parts.Add($"每次出场 {rates.Appearance} 金币");
            if (rates.Kill > 0) parts.Add($"每击杀 1 名敌人 {rates.Kill} 金币");
            if (rates.DamageTaken > 0) parts.Add($"每场每损失 5 点生命 {rates.DamageTaken} 金币");
            if (rates.Shield > 0) parts.Add($"每场护盾每挡下 5 点敌方伤害 {rates.Shield} 金币");
            string bonuses = parts.Count == 0 ? "无" : string.Join("，", parts);
            return $"提前出售违约金 {c.ReleaseFee} 金币。额外薪资：{bonuses}。";
        }
    }
}
