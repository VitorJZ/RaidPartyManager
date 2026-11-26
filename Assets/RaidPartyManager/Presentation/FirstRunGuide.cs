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
            var message = Prepared ? "准备好了。点舞台前沿的石路牌出发。"
                : "先点左上角的「队伍」。\n给队员穿件装备、学个技能，再看一眼合同。";
            var card = Card("departure-guide", message);
            if (!Prepared)
                card.Add(NoteButton("跳过教学", "skip-departure-guide", () => { Remember(GuideStep.Departed); skip(); }));
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

        /// <summary>
        /// Shows the first-visit hint. Facility details hang as one-line slips under their own signs,
        /// so the corner note stays short enough to read at a glance.
        /// </summary>
        internal static void ShowFacility(VisualElement parent, AdventureRun run, GuideStep facility,
            params (VisualElement sign, string tip)[] tips)
        {
            if (run.IsInspection || Has(facility)) return;
            Remember(facility);
            string text = facility == GuideStep.Town
                ? "点挂牌进店，店里点城镇牌回来。\n逛完了，点石路牌出发。"
                : "营地每次进城只能做一件事。\n休息回血，或者训练、升阶。确认后才算数。";
            var card = Card(facility == GuideStep.Town ? "town-guide" : "camp-guide", text);
            var slips = new VisualElement[tips.Length];
            for (int i = 0; i < tips.Length; i++)
                tips[i].sign.Add(slips[i] = Slip(tips[i].tip, i));
            card.Add(NoteButton("知道了", "dismiss-facility-guide", () =>
            {
                card.RemoveFromHierarchy();
                foreach (var slip in slips) slip.RemoveFromHierarchy();
            }));
            parent.Add(card);
        }

        private static readonly Color NoteInk = new Color32(58, 38, 22, 255);

        private static void Paper(VisualElement element, int pinSide)
        {
            element.style.backgroundImage = MainMenuBoard.Load("MenuNoteTorn");
            element.style.unitySliceLeft = pinSide;
            element.style.unitySliceRight = element.style.unitySliceTop = element.style.unitySliceBottom = 34;
            element.style.unitySliceScale = .5f;
        }

        private static VisualElement Card(string name, string text)
        {
            var card = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Paper(card, 96);
            card.style.position = Position.Absolute;
            card.style.left = 14;
            card.style.top = name == "departure-guide" ? 8 : RunTopBar.Height + 46;
            card.style.maxWidth = Length.Percent(46);
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 44;
            card.style.paddingRight = 16;
            card.style.paddingTop = card.style.paddingBottom = 13;
            card.style.rotate = new Rotate(-1.2f);
            var label = PrototypeStyle.Text(text, 17, NoteInk);
            label.pickingMode = PickingMode.Ignore;
            label.style.flexShrink = 1;
            label.style.marginRight = 12;
            card.Add(label);
            return card;
        }

        private static Button NoteButton(string text, string name, Action click)
        {
            var button = PrototypeStyle.Button(text, name, click);
            button.style.flexShrink = 0;
            button.style.fontSize = 15;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = NoteInk;
            button.style.backgroundColor = new Color(NoteInk.r, NoteInk.g, NoteInk.b, .12f);
            PrototypeStyle.Outline(button, new Color(NoteInk.r, NoteInk.g, NoteInk.b, .55f), 4);
            button.style.paddingLeft = button.style.paddingRight = 10;
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = new Color(NoteInk.r, NoteInk.g, NoteInk.b, .24f));
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = new Color(NoteInk.r, NoteInk.g, NoteInk.b, .12f));
            return button;
        }

        private static VisualElement Slip(string text, int index)
        {
            var slip = new Label(text) { name = "facility-guide-tip", pickingMode = PickingMode.Ignore };
            Paper(slip, 34);
            slip.style.position = Position.Absolute;
            slip.style.top = Length.Percent(100);
            slip.style.marginTop = 6;
            slip.style.left = Length.Percent(50);
            slip.style.translate = new Translate(Length.Percent(-50), 0);
            slip.style.rotate = new Rotate(index % 2 == 0 ? -1.2f : 1f);
            slip.style.paddingLeft = slip.style.paddingRight = 12;
            slip.style.paddingTop = slip.style.paddingBottom = 5;
            slip.style.fontSize = 15;
            slip.style.unityFontStyleAndWeight = FontStyle.Bold;
            slip.style.color = NoteInk;
            slip.style.whiteSpace = WhiteSpace.NoWrap;
            slip.style.unityTextAlign = TextAnchor.MiddleCenter;
            return slip;
        }
    }
}
