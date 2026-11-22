using System;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class AuctionTableView : VisualElement
    {
        private readonly AdventureRun run;
        private readonly AdventureVisit visit;
        private readonly DockEventStage stage;
        private readonly RunTopBar topBar;
        private readonly Label speech;
        private readonly Button[] targets = new Button[3];
        private readonly Label[] prices = new Label[3];
        private float nextClick;
        private bool pullingBack;

        internal AuctionTableView(AdventureRun run, DockEventStage stage, RunTopBar topBar, Action leave, Label speech)
        {
            this.run = run;
            this.visit = run.Current;
            this.stage = stage;
            this.topBar = topBar;
            this.speech = speech;
            name = "auction-table";
            PrototypeStyle.Fill(this);
            var blank = new Button(() =>
            {
                if (pullingBack && stage.RevealFinished) { leave(); return; }
                pullingBack = true;
                stage.FocusCases(false);
                speech.text = visit.Resolved ? visit.Message + " 点空白处离场。"
                    : "还想看货就点匣子回到桌前。要离开，现在再点一次空白处。";
            }) { name = "auction-floor" };
            StyleTarget(blank);
            PrototypeStyle.Fill(blank);
            Add(blank);
            for (int i = 0; i < targets.Length; i++)
            {
                int slot = i;
                var button = new CaseButton(stage, slot, () => Select(slot)) { name = "auction-case-" + slot };
                StyleTarget(button);
                targets[i] = button;
                button.SetEnabled(false);
                button.tooltip = run.AuctionNameAt(i) + " · " + run.AuctionPriceAt(i) + " 金币";
                var price = PrototypeStyle.Text(button.tooltip, 14, new Color32(249,222,168,255));
                price.pickingMode = PickingMode.Ignore;
                price.style.backgroundColor = (Color)new Color32(61,40,32,245);
                price.style.unityTextAlign = TextAnchor.MiddleCenter;
                price.style.position = Position.Absolute;
                price.style.left = price.style.right = 0;
                price.style.top = Length.Percent(96);
                prices[i] = price;
                button.Add(price);
                Add(button);
            }
            speech.text = Instructions();
            schedule.Execute(PositionCases).Every(16);
        }

        private string Instructions() => run.AuctionInspected >= 0
            ? visit.Message : "先点一只匣子，免费验货。只能验一次，下一次点哪只，就按标价买哪只。点空白处退回远景。";

        private void Select(int slot)
        {
            if (run.Current != visit || visit.Resolved || Time.unscaledTime < nextClick) return;
            if (pullingBack)
            {
                pullingBack = false;
                stage.FocusCases(true);
                speech.text = Instructions();
                return;
            }
            if (!stage.AtTable) return;
            if (run.AuctionInspected < 0)
            {
                if (!run.ChooseGrave("peek" + slot)) return;
                GameAudio.Play(SoundCue.ChestLatchRelease);
                stage.OpenCase(slot);
                nextClick = Time.unscaledTime + .45f;
                speech.text = visit.Message;
                return;
            }
            if (!run.ChooseGrave("buy" + slot))
            {
                GameAudio.Play(SoundCue.UiRestricted);
                speech.text = $"这只要 {run.AuctionPriceAt(slot)} 金币，您手上的钱还差一些。可以看看别的匣子，或点空白处退后。";
                return;
            }
            GameAudio.Play(SoundCue.ShopCheckout);
            stage.TakeCase(slot);
            targets[slot].style.display = DisplayStyle.None;
            speech.text = visit.Message + " 点空白处退后。";
            topBar.Refresh();
        }

        private sealed class CaseButton : Button
        {
            private readonly DockEventStage stage;
            private readonly int slot;

            internal CaseButton(DockEventStage stage, int slot, Action click) : base(click)
            {
                this.stage = stage;
                this.slot = slot;
            }

            public override bool ContainsPoint(Vector2 localPoint)
            {
                if (!base.ContainsPoint(localPoint) || panel == null) return false;
                var screen = panel.visualTree.worldBound;
                var p = this.LocalToWorld(localPoint) - screen.min;
                return stage.HitCase(slot, new Vector2(p.x / screen.width, 1f - p.y / screen.height));
            }
        }

        private static void StyleTarget(Button button)
        {
            button.style.position = Position.Absolute;
            button.style.backgroundColor = Color.clear;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
            button.style.overflow = Overflow.Visible;
            PrototypeStyle.Outline(button, Color.clear, 0);
        }

        private void PositionCases()
        {
            if (panel == null || run.Current != visit) return;
            var screen = panel.visualTree.worldBound;
            for (int i = 0; i < targets.Length; i++)
            {
                Rect uv = stage.AuctionTarget(i);
                var pos = this.WorldToLocal(screen.min + Vector2.Scale(uv.min, screen.size));
                var button = targets[i];
                button.style.left = pos.x;
                button.style.top = pos.y;
                button.style.width = uv.width * screen.width;
                button.style.height = uv.height * screen.height;
                button.SetEnabled(!visit.Resolved && (stage.AtTable || pullingBack && stage.RevealFinished));
                button.pickingMode = visit.Resolved ? PickingMode.Ignore : PickingMode.Position;
                prices[i].style.display = stage.AtTable && !visit.Resolved ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
