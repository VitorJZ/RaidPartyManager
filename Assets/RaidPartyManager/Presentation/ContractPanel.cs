using System;
using System.Collections.Generic;
using System.Linq;
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

        public static VisualElement Sheet(string title, out ScrollView paper, Button back = null)
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
            paper.style.maxHeight = Length.Percent(78);
            paper.style.backgroundColor = (Color)new Color32(242, 211, 141, 255);
            paper.style.paddingLeft = paper.style.paddingRight = 25;
            paper.style.paddingTop = paper.style.paddingBottom = 17;
            PrototypeStyle.Outline(paper, new Color32(155, 111, 55, 255), 2);
            var heading = PrototypeStyle.Text(title, 23, Seal);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.borderBottomWidth = 1;
            heading.style.borderBottomColor = Seal;
            heading.style.paddingBottom = heading.style.marginBottom = 8;
            if (back == null) paper.Add(heading);
            else
            {
                var header = PrototypeStyle.Row();
                header.style.alignItems = Align.Center;
                heading.style.flexGrow = 1;
                header.Add(back);
                header.Add(heading);
                paper.Add(header);
            }
            shade.Add(paper);
            return shade;
        }

        public static string Summary(CharacterDefinition c, CrewContract contract)
        {
            string text = $"{c.Name}的讨伐队成员合同。第 {contract.StartDay} 天生效，第 {contract.EndDay} 天到期，共 {contract.Days} 天。折算日薪 {contract.BaseTotal / (decimal)contract.Days:0.##} 金币。";
            if (contract.DeferredTerms) text += $" 签字费 {contract.SigningFee} 金币。";
            if (!contract.Plan.Enabled) return text + (contract.BasePaid ? "合同底薪已付清。" : "");
            text += $" 分 {contract.Plan.Count} 期，间隔 {contract.Plan.IntervalDays} 天。尚待结清 {contract.Outstanding} 金币。";
            if (contract.Arrears > 0) text += $"其中已逾期 {contract.Arrears} 金币。";
            if (contract.NextDueDay is int due)
                text += $" 下期付款日为第 {due} 天，按期付款金额 {contract.ScheduledPayment(contract.InstallmentsPaid + 1)} 金币。";
            return text;
        }

        public static VisualElement Negotiate(CharacterDefinition c, int day, PlayerTrait talent, int tradeQuote,
            CrewContract current, int expectedWage, EmployeeAttributes ratings, Func<int, PerformanceBonuses, long, bool> sign, Action close, IReadOnlyList<CrewContract> renewals = null)
        {
            int startDay = (renewals?.LastOrDefault() ?? current)?.EndDay ?? day;
            var view = Sheet(c.Name + (current == null ? " · 讨伐队合约" : " · 续约合同"), out var paper);
            paper.Add(Text(PlayerMarket.Describe(talent), 15));
            if (current != null)
            {
                paper.Add(Text(Summary(c, current) + $" 剩余 {current.EndDay - day} 天。", 16));
                paper.Add(Text(Terms(current), 14));
                if (renewals != null)
                    foreach (var agreement in renewals)
                        paper.Add(Text("已签续约合同 · " + Summary(c, agreement) + " " + Terms(agreement, pending: true), 14));
            }
            else paper.Add(Text($"人才市场转会费　{tradeQuote} 金币", 17));

            var edits = new VisualElement();
            var length = new IntegerField("合同天数") { name = "contract-duration", value = 12 };
            edits.Add(length);
            var fee = new LongField("签字费（金币）") { name = "contract-signing-fee", value = (long)expectedWage * length.value };
            edits.Add(fee);
            edits.Add(Text($"期望日薪 {expectedWage} 金币。不分期时，签字费是整份合同的报价。", 14));
            edits.Add(Text("额外薪资", 16));
            var rates = (renewals?.LastOrDefault() ?? current)?.AgreedRates ?? default;
            var appearance = Bonus("出场费", "每次出场", "appearance", rates.Appearance);
            var kill = Bonus("击杀奖", "每击杀 1 名敌人", "kill", rates.Kill);
            var damage = Bonus("承伤奖", "每场每损失 5 点生命", "damage", rates.DamageTaken);
            var shield = Bonus("抗伤奖", "每场护盾每挡下 5 点敌方伤害", "shield", rates.Shield);
            var plan = (renewals?.LastOrDefault() ?? current)?.Plan ?? default;
            int premium = WageInstallments.PremiumPercent(c.Personality);
            var installments = new Toggle { name = "installment-enabled", text = "分期支付底薪", value = false };
            edits.Add(installments);
            var schedule = new VisualElement();
            var count = new IntegerField("期数") { name = "installment-count", value = plan.Enabled ? plan.Count : 2 };
            var interval = new IntegerField("每期间隔（天）") { name = "installment-days", value = plan.Enabled ? plan.IntervalDays : 5 };
            schedule.Add(interval); schedule.Add(count);
            schedule.Add(Text($"按期望日薪计算底薪，签字费抵本金。分期利率 {premium}%（按整个合同期限折算），每天只对尚未还清的本金计息。", 14));
            var payments = Text("", 14);
            payments.name = "contract-payment-schedule";
            schedule.Add(payments);
            int firstPeriod = 1;
            const int pageSize = 5;
            var pages = PrototypeStyle.Row();
            var prev = PrototypeStyle.Button("上一页", "previous-payments", null);
            var next = PrototypeStyle.Button("下一页", "next-payments", null);
            pages.Add(prev); pages.Add(next); schedule.Add(pages);
            edits.Add(schedule);
            var quote = PrototypeStyle.Text("", 15, Seal);
            quote.name = "contract-preview";
            var submit = PrototypeStyle.Button(current == null ? "签订新合同" : "签署续约合同", "sign-crew-contract", () => {
                var terms = ReadRates();
                var draft = CrewContract.Quote(c, startDay, length.value, expectedWage, terms, fee.value);
                if (draft == null) return;
                if (draft.DailyWage < CrewContract.MinimumWage(expectedWage, terms, ratings))
                {
                    GameAudio.Play(SoundCue.UiRestricted);
                    quote.text = $"{c.Name}拒绝了这份报价：“照这个价干满 {length.value} 天，不划算。”";
                    return;
                }
                if (sign(length.value, terms, fee.value)) close();
                else quote.text = "签约失败，请检查余额、人数及合同期限。";
            }, true);
            prev.clicked += () => { firstPeriod -= pageSize; Refresh(); };
            next.clicked += () => { firstPeriod += pageSize; Refresh(); };
            edits.RegisterCallback<ChangeEvent<bool>>(_ => Refresh());
            edits.RegisterCallback<ChangeEvent<int>>(_ => { firstPeriod = 1; Refresh(); });
            fee.RegisterValueChangedCallback(_ => Refresh());
            installments.RegisterValueChangedCallback(e =>
            {
                long basis = (long)expectedWage * Math.Max(1, length.value);
                if (e.newValue && fee.value == basis) fee.SetValueWithoutNotify(0);
                else if (!e.newValue && fee.value == 0) fee.SetValueWithoutNotify(basis);
                firstPeriod = 1;
                Refresh();
            });
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
                Installments = installments.value ? new WageInstallments { Enabled = true, Count = count.value, IntervalDays = interval.value } : default
            };

            void Refresh()
            {
                var terms = ReadRates();
                int days = length.value;
                bool financed = installments.value;
                schedule.style.display = financed ? DisplayStyle.Flex : DisplayStyle.None;
                var draft = CrewContract.Quote(c, startDay, days, expectedWage, terms, fee.value);
                submit.SetEnabled(draft != null && (financed || fee.value > 0));
                payments.text = "";
                pages.style.display = DisplayStyle.None;
                if (draft == null)
                {
                    quote.text = days < 1 || days > int.MaxValue - startDay ? "请输入至少 1 天的有效期限。" :
                        !terms.Valid ? "已勾选的奖励至少为 1 金币。" :
                        !terms.Installments.Fits(days) ? "间隔和期数至少为 1，最后一期须在合同期内。" :
                        "请检查签字费。分期首付可为零，最高为整份合同的底薪。";
                    return;
                }
                if (financed)
                {
                    int last = (int)Math.Min(count.value, (long)firstPeriod + pageSize - 1);
                    var lines = new List<string>();
                    for (long k = firstPeriod; k <= last; k++)
                    {
                        int i = (int)k;
                        long principal = draft.PrincipalAt(i);
                        long amount = draft.ScheduledPayment(i);
                        lines.Add($"第 {i} 期　 第 {startDay + i * interval.value} 天　 {amount} 金币（本金 {principal}，利息 {amount - principal}）");
                    }
                    payments.text = string.Join("\n", lines);
                    pages.style.display = count.value > pageSize ? DisplayStyle.Flex : DisplayStyle.None;
                    prev.SetEnabled(firstPeriod > 1); next.SetEnabled(last < count.value);
                }
                string breakdown = financed ? $"底薪 {draft.BaseTotal}，签字费抵扣 {fee.value}，按期付清共计利息 {draft.ScheduledInterest} 金币。" :
                    $"合同总价 {fee.value} 金币，折算日薪 {fee.value / (decimal)days:0.##} 金币。";
                quote.text = $"第 {startDay} 天生效，第 {draft.EndDay} 天到期。{breakdown} 生效时违约金 {draft.CurrentReleaseFee} 金币。";
                decimal due = (decimal)draft.DueAtSigning + tradeQuote;
                quote.text += $"\n签约当天合计支付 {due} 金币。";
                if (current == null) quote.text = $"让{c.Name}成为玩家的讨伐队成员。\n" + quote.text;
            }
        }

        private static string Terms(CrewContract c, bool pending = false)
        {
            var rates = c.AgreedRates;
            var parts = new List<string>();
            if (rates.Appearance > 0) parts.Add($"每次出场 {rates.Appearance} 金币");
            if (rates.Kill > 0) parts.Add($"每击杀 1 名敌人 {rates.Kill} 金币");
            if (rates.DamageTaken > 0) parts.Add($"每场每损失 5 点生命 {rates.DamageTaken} 金币");
            if (rates.Shield > 0) parts.Add($"每场护盾每挡下 5 点敌方伤害 {rates.Shield} 金币");
            string bonuses = parts.Count == 0 ? "无" : string.Join("，", parts);
            return (pending ? "到期接续后履行。" : $"当前违约金 {c.CurrentReleaseFee} 金币。") + $"额外薪资　{bonuses}。";
        }
    }
}
