using System;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class HatGameView : VisualElement
    {
        private readonly AdventureRun run;
        private readonly EncounterData content;
        private readonly Action changed;
        private readonly Button[] hats = new Button[3];
        private readonly VisualElement track;
        private readonly Label instruction;
        private readonly Button start;
        private IVisualElementScheduledItem animation;
        private EquipmentTooltip rewardTooltip;
        private double began;
        private int firstHat, secondHat;
        private bool moving;
        private const double SwapSeconds = 0.78;

        internal HatGameView(AdventureRun run, Texture2D artwork, EncounterData content, Action changed)
        {
            this.run = run; this.content = content; this.changed = changed;
            name = "hat-game";
            style.minHeight = 300;
            instruction = PrototypeStyle.Text("记住左侧帽子里的物品，再开始交换。", 15, AdventureEventWindow.PaperAccent);
            instruction.name = "hat-instruction"; Add(instruction);
            track = new VisualElement { name = "hat-track" };
            track.style.height = 160; track.style.marginTop = 18; Add(track);
            track.style.overflow = Overflow.Hidden;
            for (int i = 0; i < 3; i++)
            {
                int hatId = i;
                var button = new Button(() => Pick(hatId)) { name = "hat-" + i };
                button.style.position = Position.Absolute; button.style.width = Length.Percent(33.333f);
                button.style.height = 100; button.style.top = 24;
                button.style.marginLeft = button.style.marginRight = 0;
                button.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(button, Color.clear, 3);
                var image = new Image { image = artwork, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(image); button.Add(image); hats[i] = button; track.Add(button);
            }
            SetPositions();
            start = AdventureWidgets.Action(this, "看好了，开始交换", "hat-start", Begin);
            AdventureEventWindow.StylePaperButton(start);
            RegisterCallback<DetachFromPanelEvent>(_ => { animation?.Pause(); HideRewardTooltip(); });
            RegisterCallback<WheelEvent>(_ => HideRewardTooltip(), TrickleDown.TrickleDown);
            if (run.Current.Resolved) Reveal();
            else if (run.Current.HatStep == run.Current.HatSwaps.Count) Ready();
            else
            {
                ShowItem(0, 0);
                hats[0].style.top = -20;
            }
        }

        private void Begin()
        {
            if (moving || run.Current.Resolved) return;
            moving = true; start.SetEnabled(false);
            instruction.text = "盯紧装着物品的那顶帽子……";
            foreach (var item in track.Query<Image>("hat-revealed-item").ToList()) item.RemoveFromHierarchy();
            foreach (var hat in hats) hat.style.top = 24;
            BeginStep();
            animation = schedule.Execute(Animate).Every(16);
        }

        private void BeginStep()
        {
            var swap = run.Current.HatSwaps[run.Current.HatStep];
            firstHat = run.Current.HatPositions[swap.First]; secondHat = run.Current.HatPositions[swap.Second];
            began = Time.unscaledTimeAsDouble;
        }

        private void Animate()
        {
            var visit = run.Current;
            if (!moving || visit == null || visit.Resolved) { animation?.Pause(); return; }
            var pair = visit.HatSwaps[visit.HatStep];
            float t = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - began) / SwapSeconds));
            float eased = Mathf.SmoothStep(0, 1, t);
            hats[firstHat].style.left = Length.Percent(Mathf.Lerp(pair.First, pair.Second, eased) * 33.333f);
            hats[secondHat].style.left = Length.Percent(Mathf.Lerp(pair.Second, pair.First, eased) * 33.333f);
            hats[firstHat].style.top = 24 - Mathf.Sin(t * Mathf.PI) * 26;
            hats[secondHat].style.top = 24 + Mathf.Sin(t * Mathf.PI) * 18;
            if (t < 1) return;
            run.StepHat(visit.HatStep); SetPositions();
            if (visit.HatStep == visit.HatSwaps.Count) { animation.Pause(); moving = false; Ready(); }
            else BeginStep();
        }

        private void SetPositions()
        {
            for (int slot = 0; slot < 3; slot++)
            {
                hats[run.Current.HatPositions[slot]].style.left = Length.Percent(slot * 33.333f);
                hats[run.Current.HatPositions[slot]].style.top = 24;
            }
        }

        private void Ready()
        {
            instruction.text = "交换结束，请点选装着展示物品的那顶帽子。";
            start.style.display = DisplayStyle.None;
        }

        private void Pick(int hatId)
        {
            if (moving || run.Current.HatStep != run.Current.HatSwaps.Count || run.Current.Resolved) return;
            int slot = Array.IndexOf(run.Current.HatPositions, hatId);
            if (run.PickHat(slot)) changed();
        }

        private void ShowItem(int slot, int hatId)
        {
            var reward = run.Current.HatRewards[hatId];
            var item = new Image { name = "hat-revealed-item", image = content.IconFor(reward.Id), scaleMode = ScaleMode.ScaleToFit };
            item.style.position = Position.Absolute; item.style.left = Length.Percent(slot * 33.333f + 3);
            item.style.width = Length.Percent(27); item.style.height = 62; item.style.top = 70;
            if (run.Current.Resolved)
            {
                item.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    HideRewardTooltip();
                    var window = this.GetFirstAncestorOfType<AdventureEventWindow>();
                    if (window == null) return;
                    rewardTooltip = new EquipmentTooltip(reward, content.IconFor(reward.Id));
                    rewardTooltip.ShowAbove(window, item);
                });
                item.RegisterCallback<PointerLeaveEvent>(_ => HideRewardTooltip());
            }
            track.Add(item);
        }

        private void HideRewardTooltip()
        {
            rewardTooltip?.RemoveFromHierarchy();
            rewardTooltip = null;
        }

        private void Reveal()
        {
            instruction.text = (run.Current.HatWon ? "找到了！所有帽子都揭开了。" : "原来物品藏在这里。所有帽子都揭开了。") +
                "悬停物品可查看详情。";
            start.style.display = DisplayStyle.None;
            for (int slot = 0; slot < 3; slot++)
            {
                int id = run.Current.HatPositions[slot]; hats[id].style.top = -16;
                hats[id].SetEnabled(false);
                hats[id].style.opacity = 1f;
                ShowItem(slot, id);
                if (slot == run.Current.PickedHat) PrototypeStyle.Outline(hats[id], AdventureEventWindow.PaperAccent, 4);
            }
        }
    }
}
