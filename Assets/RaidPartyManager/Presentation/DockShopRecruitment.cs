using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    public sealed partial class DockShop
    {
        private VisualElement recruitmentWindow, recruitmentControls, recruitActorLayer, replacementPanel;
        private readonly Dictionary<CharacterDefinition, (VisualElement View, VisualElement Fill)> recruitTargets = new();
        private Button spinButton;
        private ShopPriceCounter priceCounter;
        private CharacterDefinition recruitHeld;
        private int recruitPointer = -1;
        private double recruitHoldStarted;
        private bool restoredCompany;
        private bool focusWheelOnArrival;
        private Vector2 recruitHoldOrigin;

        private void PrepareRecruitment()
        {
            if (adventure == null && standalonePriceOwner != party)
            {
                standalonePriceOwner = party;
                standaloneRecruitPrice = ShopRecruitment.StartingPrice;
            }
            recruitment = adventure?.Current.ShopRecruitment ??
                new ShopRecruitment(party, content.CharacterCatalog, random, standaloneRecruitPrice);
            restoredCompany = true;
            focusWheelOnArrival = isWheelEvent;
            stage.BuildRecruitmentWheel(recruitment, content, isWheelEvent && !adventure.Current.WheelLanded);
        }

        private void RenderRecruitmentEntry()
        {
            recruitmentButton = PrototypeStyle.Button("角色招募", "shop-recruit-open", ShowRecruitment);
            recruitmentButton.style.backgroundColor = Color.clear;
            recruitmentButton.style.borderTopWidth = recruitmentButton.style.borderBottomWidth = 0;
            recruitmentButton.style.borderLeftWidth = recruitmentButton.style.borderRightWidth = 0;
            recruitmentButton.style.unityTextAlign = TextAnchor.LowerCenter;
            recruitmentButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            recruitmentButton.style.unityTextOutlineColor = (Color)new Color32(35, 23, 20, 255);
            recruitmentButton.style.unityTextOutlineWidth = 1;
            stage.PlaceRecruitmentTarget(recruitmentButton);
            root.Add(recruitmentButton);

            recruitActorLayer = new VisualElement { name = "shop-recruit-actors", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(recruitActorLayer);
            recruitActorLayer.RegisterCallback<PointerMoveEvent>(MoveRecruitPointer);
            recruitActorLayer.RegisterCallback<PointerUpEvent>(EndRecruitPointer);
            recruitActorLayer.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == recruitPointer) ReleaseHeldRecruit(); });
            recruitActorLayer.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == recruitPointer) ReleaseHeldRecruit(); });
            root.Insert(root.IndexOf(topBar), recruitActorLayer);
            SyncRecruitCast();
            RefreshRecruitmentButton();
        }

        private void ShowRecruitment()
        {
            if (recruitment.Signed || recruitmentWindow != null || stage.RecruitmentDropping || replacementPanel != null) return;
            if (!isWheelEvent) CloseCounter();
            ReleaseHeldRecruit();
            stockLayer?.SetEnabled(false);
            stage.RecruitmentFocused = true;
            stage.HideCompany();
            restoredCompany = false;
            stage.RefreshWheel(recruitment);

            recruitmentWindow = new VisualElement { name = "shop-recruit-window" };
            PrototypeStyle.Fill(recruitmentWindow);
            recruitmentWindow.style.opacity = 0;
            recruitmentWindow.RegisterCallback<PointerDownEvent>(OnRecruitmentBackground);
            root.Insert(root.IndexOf(recruitActorLayer), recruitmentWindow);
            RefreshRecruitmentButton();

            recruitmentControls = new VisualElement { name = "shop-wheel-controls", pickingMode = PickingMode.Ignore };
            recruitmentControls.style.alignItems = Align.Center;
            recruitmentControls.style.justifyContent = Justify.Center;
            stage.PlaceRecruitmentControls(recruitmentControls);
            recruitmentWindow.Add(recruitmentControls);

            spinButton = PrototypeStyle.Button("转动转盘", "shop-recruit-draw", DrawRecruit);
            spinButton.style.minHeight = 29;
            spinButton.style.minWidth = 112;
            spinButton.style.marginBottom = 3;
            spinButton.style.backgroundColor = (Color)new Color32(119, 51, 36, 255);
            PrototypeStyle.Outline(spinButton, new Color32(208, 158, 83, 255), 2);
            recruitmentControls.Add(spinButton);
            priceCounter = new ShopPriceCounter(recruitment.NextPrice);
            recruitmentControls.Add(priceCounter);
            if (isWheelEvent)
            {
                var leave = PrototypeStyle.Button(adventure.IsInspection ? "返回事件检查" : "继续赶路", "leave-wheel-event",
                    () => topBar.WhenRewardsArrive(AdventureFlow.ReturnFromNode));
                leave.style.position = Position.Absolute;
                leave.style.top = RunTopBar.Height + 10;
                leave.style.right = 16;
                recruitmentWindow.Add(leave);
            }
            var hint = PrototypeStyle.Text(RecruitHoldHint, 15, PrototypeStyle.Accent);
            hint.name = "wheel-contract-hint";
            hint.style.position = Position.Absolute;
            hint.style.left = 18;
            hint.style.top = RunTopBar.Height + (isWheelEvent ? 41 : 10);
            hint.pickingMode = PickingMode.Ignore;
            recruitmentWindow.Add(hint);
            RefreshSpinControl();
            spinButton.Focus();
        }

        private string RecruitHoldHint => party.ContractsEnabled ? "长按候选人签约" : "长按候选人招募";

        private void OnRecruitmentBackground(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.target != recruitmentWindow || recruitHeld != null ||
                stage.OverRecruitmentWheel(evt.position, root.worldBound)) return;
            evt.StopPropagation();
            CloseRecruitment();
        }

        private void SyncRecruitCast(CharacterDefinition arriving = null)
        {
            for (int i = 0; i < recruitment.Candidates.Count; i++)
            {
                var candidate = recruitment.Candidates[i];
                if (!recruitment.CanSign(candidate.Id)) continue;
                stage.ShowRecruitActor(content, candidate, i, candidate == arriving);
                if (recruitTargets.ContainsKey(candidate)) continue;
                var hit = new VisualElement { name = "shop-recruit-actor-" + candidate.Id, focusable = true };
                hit.tooltip = RecruitHoldHint;
                hit.RegisterCallback<PointerDownEvent>(evt => BeginRecruitPointer(evt, candidate));
                CharacterChoicePreview.Attach(hit, candidate, content.PortraitFor(candidate.Id));
                hit.style.display = DisplayStyle.None;
                var fill = AddHoldProgress(hit, "shop-recruit-hold-progress");
                recruitActorLayer.Add(hit);
                recruitTargets.Add(candidate, (hit, fill));
            }
        }

        private bool CanHoldRecruit(CharacterDefinition candidate) =>
            !DockPlayhouse.Moving && !stage.RecruitmentSpinning && !stage.CounterFocused &&
            (stage.WideView || stage.RecruitmentFocused && stage.RecruitmentFocus >= .93f) &&
            replacementPanel == null && saleWindow == null && !recruitment.Signed && stage.RecruitReady(candidate);

        private void BeginRecruitPointer(PointerDownEvent evt, CharacterDefinition candidate)
        {
            var (view, fill) = recruitTargets[candidate];
            if (evt.button != 0 || recruitHeld != null || !CanHoldRecruit(candidate) ||
                !view.enabledInHierarchy || !view.worldBound.Contains(evt.position) || !recruitment.CanSign(candidate.Id)) return;

            recruitHeld = candidate;
            recruitPointer = evt.pointerId;
            recruitHoldOrigin = evt.position;
            recruitHoldStarted = Time.unscaledTimeAsDouble;
            fill.style.width = Length.Percent(0);
            fill.parent.style.display = DisplayStyle.Flex;
            recruitActorLayer.CapturePointer(recruitPointer);
            root.Focus();
            RefreshSpinControl();
            evt.StopPropagation();
        }

        private void MoveRecruitPointer(PointerMoveEvent evt)
        {
            if (recruitHeld == null || evt.pointerId != recruitPointer) return;
            Vector2 position = evt.position;
            if ((position - recruitHoldOrigin).sqrMagnitude > 18 * 18 ||
                !recruitTargets[recruitHeld].View.worldBound.Contains(position)) ReleaseHeldRecruit();
            evt.StopPropagation();
        }

        private void EndRecruitPointer(PointerUpEvent evt)
        {
            if (evt.button != 0 || recruitHeld == null || evt.pointerId != recruitPointer) return;
            ReleaseHeldRecruit();
            evt.StopPropagation();
        }

        private void UpdateRecruitHold()
        {
            if (recruitHeld == null) return;
            var candidate = recruitHeld;
            var (view, fill) = recruitTargets[candidate];
            if (!CanHoldRecruit(candidate) || !view.enabledInHierarchy)
            {
                ReleaseHeldRecruit();
                return;
            }
            float progress = (float)(Time.unscaledTimeAsDouble - recruitHoldStarted) / PurchaseHoldSeconds;
            fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
            if (progress < 1) return;

            ReleaseHeldRecruit();
            if (!recruitment.CanSign(candidate.Id)) return;
            if (party.Members.Count >= PartyRun.RosterLimit) ShowRecruitReplacement(candidate);
            else OpenRecruitContract(candidate);
        }

        private void ReleaseHeldRecruit()
        {
            if (recruitHeld == null) return;
            recruitTargets[recruitHeld].Fill.parent.style.display = DisplayStyle.None;
            int captured = recruitPointer;
            recruitPointer = -1;
            recruitHeld = null;
            if (recruitActorLayer.HasPointerCapture(captured)) recruitActorLayer.ReleasePointer(captured);
            RefreshSpinControl();
        }

        private void CloseRecruitSelection()
        {
            replacementPanel?.RemoveFromHierarchy();
            replacementPanel = null;
            RefreshSpinControl();
        }

        private void OpenRecruitContract(CharacterDefinition candidate, string replace = null)
        {
            CloseRecruitSelection();
            if (adventure == null || !party.ContractsEnabled) { SignRecruit(candidate, replace); return; }
            var talent = party.TalentFor(candidate);
            replacementPanel = ContractPanel.Negotiate(candidate, party.ContractDay, talent, 0, null, CrewContract.ExpectedWage(candidate, talent), EmployeeAttributes.Starting,
                (days, terms, fee) => SignRecruit(candidate, replace, days, terms, fee),
                CloseRecruitSelection);
            root.Add(replacementPanel);
            RefreshSpinControl();
        }

        private void ShowRecruitReplacement(CharacterDefinition candidate)
        {
            replacementPanel = PrototypeStyle.Panel();
            replacementPanel.name = "shop-recruit-replacement";
            replacementPanel.style.position = Position.Absolute;
            replacementPanel.style.left = Length.Percent(7);
            replacementPanel.style.top = Length.Percent(14);
            replacementPanel.style.width = Length.Percent(42);
            replacementPanel.style.maxHeight = Length.Percent(79);
            var choices = new ScrollView();
            replacementPanel.Add(choices);
            AdventureWidgets.RecruitReplacement(choices, candidate, party, content, id => OpenRecruitContract(candidate, id));
            AdventureWidgets.Action(choices, "取消换人", "shop-recruit-back", CloseRecruitSelection);
            root.Add(replacementPanel);
            RefreshSpinControl();
        }

        private void RefreshSpinControl()
        {
            if (recruitmentWindow == null) return;
            bool available = recruitment.CanDraw;
            bool affordable = Gold >= recruitment.NextPrice;
            spinButton.text = !available ? "候选已抽完" : !affordable ? "金币不足" : "转动转盘";
            spinButton.SetEnabled(available && affordable && !stage.RecruitmentSpinning && replacementPanel == null && recruitHeld == null);
            priceCounter.RollTo(recruitment.NextPrice);
        }

        private void DrawRecruit()
        {
            if (stage.RecruitmentSpinning || recruitHeld != null || replacementPanel != null) return;
            bool drawn = adventure != null ? adventure.DrawShopRecruit() : recruitment.TryDraw(random, SpendTestCoins);
            if (!drawn) return;
            if (adventure == null) standaloneRecruitPrice = recruitment.NextPrice;
            else if (isWheelEvent) AdventureFlow.AutoSave(adventure);
            RefreshGold();
            stage.SpinRecruitmentWheel(recruitment.Candidates.Last());
            RefreshRecruitmentButton();
            spinButton.SetEnabled(false);
        }

        private void UpdateRecruitment(float delta)
        {
            if (stage.UpdateWheelDrop(delta))
            {
                adventure.LandRecruitmentWheel();
                AdventureFlow.AutoSave(adventure);
                RefreshRecruitmentButton();
            }
            if (focusWheelOnArrival && !stage.RecruitmentDropping && !DockPlayhouse.Moving)
            {
                focusWheelOnArrival = false;
                ShowRecruitment();
            }
            if (stage.UpdateRecruitment(delta))
            {
                stage.RefreshWheel(recruitment);
                RefreshRecruitmentButton();
                SyncRecruitCast(recruitment.Candidates.Last());
                RefreshSpinControl();
            }
            stage.UpdateRecruitCast(delta);
            stage.PlaceRecruitmentTarget(recruitmentButton);
            if (recruitmentWindow == null)
            {
                if (!restoredCompany && stage.RecruitmentFocus <= 0)
                {
                    stage.ShowCompany(party, content);
                    restoredCompany = true;
                }
            }
            else
            {
                recruitmentWindow.style.opacity = Mathf.InverseLerp(.24f, .93f, stage.RecruitmentFocus);
                stage.PlaceRecruitmentControls(recruitmentControls);
                priceCounter.Tick(delta);
            }
            foreach (var pair in recruitTargets)
            {
                bool ready = CanHoldRecruit(pair.Key);
                var view = pair.Value.View;
                view.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
                if (ready) stage.PlaceRecruitTarget(pair.Key, view);
            }
            UpdateRecruitHold();
        }

        private void CloseRecruitment()
        {
            ReleaseHeldRecruit();
            CloseRecruitSelection();
            if (recruitmentWindow == null) return;
            recruitmentWindow.RemoveFromHierarchy();
            recruitmentWindow = recruitmentControls = null;
            priceCounter = null;
            stage.RecruitmentFocused = false;
            stockLayer?.SetEnabled(true);
            RefreshRecruitmentButton();
        }
    }
}
