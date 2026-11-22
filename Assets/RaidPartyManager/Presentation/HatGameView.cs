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
        private readonly AdventureVisit originalVisit;
        private readonly DockEventStage stage;
        private readonly EncounterData content;
        private readonly Action<VisualElement, Func<bool>> collect;
        private readonly Button[] hats = new Button[3];
        private readonly VisualElement[] prizes = new VisualElement[3];
        private readonly Label instruction;
        private readonly HangingSign start;
        private EquipmentTooltip tooltip;
        private bool moving;
        private double began, revealedAt;
        private const double SwapSeconds = .78;

        internal HatGameView(AdventureRun run, DockEventStage stage, EncounterData content,
            Action<VisualElement, Func<bool>> collect, EventChoiceRack rack)
        {
            originalVisit = run.Current;
            this.run = run; this.stage = stage; this.content = content; this.collect = collect;
            name = "hat-game";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            instruction = PrototypeStyle.Text("记住掀起的帽子下有什么。看清后，下拉「开始交换」。", 15, AdventureEventWindow.PaperInk);
            instruction.name = "hat-instruction";
            rack.Note(instruction);
            for (int i = 0; i < hats.Length; i++)
            {
                int id = i;
                // These are hit targets for the three physical props on the table.
                var hit = new Button(() => Pick(id)) { name = "hat-" + id };
                hit.style.position = Position.Absolute;
                hit.style.backgroundColor = Color.clear;
                hit.style.marginTop = hit.style.marginLeft = hit.style.marginBottom = hit.style.marginRight = 0;
                PrototypeStyle.Outline(hit, Color.clear, 0);
                hats[i] = hit;
                Add(hit);
            }
            Settle();
            var visit = run.Current;
            stage.PresentHatRound(visit.Resolved);
            if (visit.Resolved)
            {
                instruction.text = (visit.HatWon ? "好眼力！" : "原来藏在这儿。") + "悬停查看桌上的三件奖品。";
                revealedAt = Time.unscaledTimeAsDouble;
                for (int i = 0; i < hats.Length; i++) ShowPrize(i);
            }
            else if (visit.HatStep == visit.HatSwaps.Count)
                Ready();
            else
            {
                bool fresh = visit.HatStep == 0;
                if (fresh) ShowPrize(0);
                else instruction.text = "这局还没结束。下拉「继续交换」看完余下的几手。";
                start = rack.Choice(fresh ? "开始交换" : "继续交换", "hat-start", Begin, false);
            }
            schedule.Execute(Tick).Every(16);
            RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
        }

        private void Begin()
        {
            if (!stage.AtTable || moving || run.Current.Resolved) return;
            moving = true;
            start.style.visibility = Visibility.Hidden;
            instruction.text = "盯住那顶帽子……";
            prizes[0]?.RemoveFromHierarchy();
            prizes[0] = null;
            Settle();
            began = Time.unscaledTimeAsDouble;
        }

        private void Tick()
        {
            if (panel == null || run.Current != originalVisit) return;
            if (start != null && !moving && !run.Current.Resolved && run.Current.HatStep < run.Current.HatSwaps.Count)
                start.SetEnabled(stage.AtTable);
            if (moving)
            {
                var visit = run.Current;
                var swap = visit.HatSwaps[visit.HatStep];
                float t = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - began) / SwapSeconds));
                float eased = Mathf.SmoothStep(0, 1, t);
                float arc = .31f * Mathf.Sin(t * Mathf.PI);
                stage.MoveHat(visit.HatPositions[swap.First], Mathf.Lerp(swap.First,swap.Second,eased), arc, 0);
                stage.MoveHat(visit.HatPositions[swap.Second], Mathf.Lerp(swap.Second,swap.First,eased), -arc, 0);
                if (t >= 1)
                {
                    if (run.StepHat(visit.HatStep)) GameAudio.Play(SoundCue.HatSwap, .45f);
                    Settle();
                    began = Time.unscaledTimeAsDouble;
                    if (visit.HatStep == visit.HatSwaps.Count) { moving = false; Ready(); }
                }
            }
            if (run.Current.Resolved)
            {
                float reveal = Mathf.SmoothStep(0, 1, (float)(Time.unscaledTimeAsDouble - revealedAt) / .32f);
                for (int slot = 0; slot < hats.Length; slot++)
                {
                    int id = run.Current.HatPositions[slot];
                    stage.MoveHat(id,slot,0,.56f * reveal);
                    prizes[id].style.opacity = reveal;
                }
            }
            for (int i = 0; i < hats.Length; i++)
            {
                Pin(hats[i], stage.TableTarget(i,false));
                if (prizes[i] != null) Pin(prizes[i], stage.TableTarget(i,true));
            }
        }

        private void Pin(VisualElement element, Rect uv)
        {
            var screen = panel.visualTree.worldBound;
            var topLeft = this.WorldToLocal(screen.min + Vector2.Scale(uv.min,screen.size));
            element.style.left = topLeft.x;
            element.style.top = topLeft.y;
            element.style.width = screen.width * uv.width;
            element.style.height = screen.height * uv.height;
        }

        private void Settle()
        {
            var visit = run.Current;
            for (int slot = 0; slot < hats.Length; slot++)
            {
                int id = visit.HatPositions[slot];
                bool raised = visit.Resolved || !moving && visit.HatStep == 0 && id == 0;
                stage.MoveHat(id,slot,0,raised ? .56f : 0);
            }
        }

        private void Ready() => instruction.text = "帽子停了。点选桌上的一顶，看看眼力如何。";

        private void Pick(int id)
        {
            var visit = run.Current;
            if (!stage.AtTable || moving || visit.Resolved || visit.HatStep != visit.HatSwaps.Count) return;
            collect(hats[id], () => run.PickHat(Array.IndexOf(visit.HatPositions, id)));
        }

        private void ShowPrize(int slot)
        {
            var visit = run.Current;
            int id = visit.HatPositions[slot];
            var reward = visit.HatRewards[id];
            var item = EquipmentIcon.CreateArtwork(reward, content.IconFor(reward.Id));
            item.name = "hat-revealed-item";
            item.style.position = Position.Absolute;
            item.pickingMode = visit.Resolved ? PickingMode.Position : PickingMode.Ignore;
            if (visit.Resolved)
            {
                item.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    HideTooltip();
                    tooltip = new EquipmentTooltip(reward, content.IconFor(reward.Id));
                    tooltip.ShowAbove(GetFirstAncestorOfType<AdventureEventWindow>(),item);
                });
                item.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
            }
            prizes[id] = item;
            Add(item);
        }

        private void HideTooltip() { tooltip?.RemoveFromHierarchy(); tooltip = null; }
    }
}
