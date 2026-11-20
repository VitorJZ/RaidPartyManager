using System;
using System.Collections.Generic;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class TreasureChestView : VisualElement
    {
        private readonly DockEventStage stage;
        private readonly AdventureRun run;
        private readonly EncounterData content;
        private readonly Action<VisualElement,Func<bool>> collect;
        private readonly Button latch;
        private readonly List<Button> prizes = new();
        private float revealAt;
        private bool ready;
        private EquipmentTooltip tooltip;

        internal TreasureChestView(DockEventStage stage, AdventureRun run, EncounterData content,
            Action<VisualElement,Func<bool>> collect)
        {
            this.stage = stage;
            this.run = run;
            this.content = content;
            this.collect = collect;
            name = "treasure-chest-art";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            latch = Target("open-treasure-chest");
            latch.tooltip = "打开宝箱";
            latch.clicked += () =>
            {
                if (DockPlayhouse.Moving || !run.OpenTreasureChest()) return;
                latch.style.display = DisplayStyle.None;
                stage.UnlockChest();
            };
            schedule.Execute(Pose).Every(16);
            RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
        }

        private Button Target(string id)
        {
            var target = new Button { name = id };
            target.style.position = Position.Absolute;
            target.style.marginLeft = target.style.marginTop = target.style.marginRight = target.style.marginBottom = 0;
            target.style.paddingLeft = target.style.paddingRight = target.style.paddingTop = target.style.paddingBottom = 0;
            target.style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(target,Color.clear,0);
            Add(target);
            return target;
        }

        internal void Refresh()
        {
            latch.style.display = run.Current.TreasureOpened ? DisplayStyle.None : DisplayStyle.Flex;
            if (!run.Current.Resolved) return;
            HideTooltip();
            foreach (var choice in prizes) choice.RemoveFromHierarchy();
            prizes.Clear();
        }

        private void HideTooltip()
        {
            tooltip?.RemoveFromHierarchy();
            tooltip = null;
        }

        private void Spill()
        {
            revealAt = Time.unscaledTime;
            for (int i = 0; i < run.Current.Rewards.Count; i++)
            {
                int index = i;
                var item = run.Current.Rewards[i];
                var art = content.IconFor(item.Id);
                var button = Target("chest-reward-" + i);
                button.Add(new EquipmentIcon(item,art));
                button.SetEnabled(false);
                button.clicked += () =>
                {
                    if (ready && !run.Current.Resolved) collect(button,() => run.ClaimReward(index));
                };
                button.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (!ready) return;
                    HideTooltip();
                    tooltip = new EquipmentTooltip(item,art);
                    tooltip.ShowAbove(this,button);
                });
                button.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
                prizes.Add(button);
            }
        }

        private void Pose()
        {
            if (!(layout.width > 0) || DockPlayhouse.Moving || run.Current == null || run.Current.Resolved) return;
            Rect uv = stage.ChestBounds;
            var screen = panel.visualTree.worldBound;
            Vector2 corner = this.WorldToLocal(screen.min + Vector2.Scale(uv.min,screen.size));
            Vector2 size = Vector2.Scale(uv.size,screen.size);
            latch.style.left = corner.x + size.x*.05f;
            latch.style.top = corner.y + size.y*.23f;
            latch.style.width = size.x*.90f;
            latch.style.height = size.y*.77f;
            if (!stage.ChestOpen) return;
            if (prizes.Count == 0) Spill();
            float age = Time.unscaledTime - revealAt;
            var mouth = corner + Vector2.Scale(size,new Vector2(.5f,.53f));
            float icon = Mathf.Min(Mathf.Clamp(size.x*.59f,48,94),layout.width*.17f);
            float gap = icon * 1.44f;
            float centre = Mathf.Clamp(mouth.x,gap+icon*.5f,layout.width-gap-icon*.5f);
            float landing = Mathf.Min(mouth.y+size.y*.69f,layout.height-icon*.6f);
            for (int i = 0; i < prizes.Count; i++)
            {
                float t = Mathf.Clamp01((age-i*.085f)/.58f);
                float ease = 1-(1-t)*(1-t);
                var end = new Vector2(centre+(i-(prizes.Count-1)*.5f)*gap,landing);
                Vector2 pos = Vector2.Lerp(mouth,end,ease);
                pos.y -= 4*t*(1-t)*icon*1.35f;
                float width = Mathf.Lerp(icon*.22f,icon,ease);
                var choice = prizes[i];
                choice.style.left = pos.x-width*.5f;
                choice.style.top = pos.y-width*.5f;
                choice.style.width = choice.style.height = width;
                choice.style.opacity = Mathf.Clamp01(t*6);
            }
            if (!ready && age >= .58f+(prizes.Count-1)*.085f)
            {
                ready = true;
                foreach (var prize in prizes) prize.SetEnabled(true);
            }
        }
    }
}
