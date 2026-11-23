using System;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [Flags]
    internal enum GuideStep { Started = 1, Equipped = 2, Learned = 4, ReadContract = 8, Departed = 16, Town = 32, Camp = 64 }

    internal static class FirstRunGuide
    {
        private const GuideStep Preparation = GuideStep.Equipped | GuideStep.Learned | GuideStep.ReadContract;
        private static GuideStep Progress => (GuideStep)PlayerPrefs.GetInt(AdventureFlow.GuideKey);
        private static bool Has(GuideStep step) => (Progress & step) == step;
        internal static bool Prepared => Has(Preparation);

        internal static void Remember(GuideStep step)
        {
            if (Has(step)) return;
            PlayerPrefs.SetInt(AdventureFlow.GuideKey, (int)(Progress | step));
            PlayerPrefs.Save();
        }

        internal static bool AtDeparture(PartyRun party) => AdventureFlow.OnRoute &&
            AdventureFlow.Active is { Floor: 1, Stage: 1, CompletedNodes: 0, Current: null } run &&
            run.Party == party && Has(GuideStep.Started) && !Has(GuideStep.Departed);

        internal static void Practiced(PartyRun party, GuideStep step)
        {
            if (AtDeparture(party)) Remember(step);
        }

        internal static void SupplyBook(PartyRun party)
        {
            if (!AtDeparture(party) || Has(GuideStep.Learned) || party.SkillBooks.Count > 0) return;
            var book = AdventureFlow.PracticeBook;
            if (book != null) party.AddEquipment(book);
        }

        internal static void ShowDeparture(VisualElement parent, Action skip)
        {
            var message = Prepared ? "队伍准备好了。点击舞台前沿落下的石路牌，进入第一个节点。"
                : "点击左上角金币旁的队伍图标，打开编队。出发前，给队员装备一件物品、学习一个技能，再查看合同。";
            var card = Card("departure-guide", message);
            if (!Prepared)
                card.Add(PrototypeStyle.Button("跳过教学", "skip-departure-guide", () => { Remember(GuideStep.Departed); skip(); }));
            parent.Add(card);
        }

        internal static Label EditorHint(VisualElement editor)
        {
            var hint = PrototypeStyle.Text("", 14, PrototypeStyle.Accent);
            hint.name = "party-guide-hint";
            hint.pickingMode = PickingMode.Ignore;
            hint.style.flexGrow = 1;
            hint.style.flexBasis = 0;
            hint.style.whiteSpace = WhiteSpace.Normal;
            void UpdateHint()
            {
                string text;
                if (editor.Q("crew-contract") != null)
                    text = "合同里可以查看工资和到期日。看完点右上角关闭。";
                else if (Prepared)
                    text = "准备完成。关闭队员详情，点编队页的「返回」，回到舞台选择路牌。";
                else if (!Has(GuideStep.Equipped) || !Has(GuideStep.Learned))
                {
                    if (editor.Q("character-details") == null) text = "点击一名队员的立绘，打开「装备与技能」。";
                    else if (editor.Q("employee-dossier") != null) text = "点击队员姓名旁的「装备与技能」。";
                    else if (!Has(GuideStep.Equipped)) text = "点右侧「装备」，按住一件背包物品约 0.15 秒，拖入银框装备槽后松开。";
                    else if (editor.Q("book-cell-0") == null) text = "点击右侧「技能书」，领取一本入门技能书。";
                    else text = "按住背包里的技能书，拖入金框技能槽后松开，这名队员就学会了它。";
                }
                else text = editor.Q("character-details") == null
                    ? "右击任一队员的立绘，查看合同里的工资和到期日。"
                    : "点「员工档案」里的「查看合约」，也可以关闭详情后右击队员。";
                if (hint.text != text) hint.text = text;
            }
            UpdateHint();
            hint.schedule.Execute(UpdateHint).Every(180);
            return hint;
        }

        internal static void ShowFacility(VisualElement parent, AdventureRun run, GuideStep facility)
        {
            if (run.IsInspection || Has(facility)) return;
            Remember(facility);
            string text = facility == GuideStep.Town
                ? "点击挂牌进入设施。\n人才市场里，点队员谈签约，点说书人谈出售。\n商店里，点柜台靠近，按住商品购买，也可出售背包物品。\n委托可三选一，达标后回城交付。营地每次到访城镇可行动一次。\n点城镇图标返回主页，再点石路牌出发。"
                : "本次营地可行动一次。点「休息」为全队恢复生命，或点「训练」「升阶」再选队员。查看人选后可以返回，确认行动才会用掉机会。";
            var card = Card(facility == GuideStep.Town ? "town-guide" : "camp-guide", text);
            card.Add(PrototypeStyle.Button("知道了", "dismiss-facility-guide", card.RemoveFromHierarchy));
            parent.Add(card);
        }

        private static VisualElement Card(string name, string text)
        {
            var card = PrototypeStyle.Panel();
            card.name = name;
            card.pickingMode = PickingMode.Ignore;
            card.style.position = Position.Absolute;
            card.style.left = 12;
            card.style.top = name == "departure-guide" ? 8 : RunTopBar.Height + 46;
            card.style.width = Length.Percent(42);
            card.style.maxWidth = 420;
            var label = PrototypeStyle.Text(text, 15);
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);
            return card;
        }
    }
}
