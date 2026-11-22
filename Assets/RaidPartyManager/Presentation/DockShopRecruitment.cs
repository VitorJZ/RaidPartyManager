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
        private VisualElement recruitmentWindow, recruitmentControls, replacementPanel;
        private readonly Dictionary<CharacterDefinition, VisualElement> recruitTargets = new();
        private Button spinButton;
        private ShopPriceCounter priceCounter;
        private CharacterDefinition recruitHeld;
        private IVisualElementScheduledItem recruitHold;
        private int recruitPointer = -1;
        private bool recruitDragging;
        private bool restoredCompany;
        private Vector2 recruitPointerPosition;

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
            stage.BuildRecruitmentWheel(recruitment, content);
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
            RefreshRecruitmentButton();
        }

        private void ShowRecruitment()
        {
            if (recruitment.Signed || recruitmentWindow != null) return;
            CloseCounter();
            stockLayer.SetEnabled(false);
            stage.RecruitmentFocused = true;
            stage.HideCompany();
            restoredCompany = false;
            stage.RefreshWheel(recruitment);

            recruitmentWindow = new VisualElement { name = "shop-recruit-window" };
            PrototypeStyle.Fill(recruitmentWindow);
            recruitmentWindow.style.opacity = 0;
            recruitmentWindow.RegisterCallback<PointerDownEvent>(OnRecruitmentBackground);
            recruitmentWindow.RegisterCallback<PointerMoveEvent>(MoveRecruitPointer);
            recruitmentWindow.RegisterCallback<PointerUpEvent>(EndRecruitPointer);
            recruitmentWindow.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == recruitPointer) ReleaseHeldRecruit(); });
            recruitmentWindow.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == recruitPointer) ReleaseHeldRecruit(); });
            root.Insert(root.IndexOf(topBar), recruitmentWindow);
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
            RefreshSpinControl();
            SyncRecruitCast();
            spinButton.Focus();
        }

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
                stage.ShowRecruitActor(content, candidate, i, candidate == arriving);
                if (recruitTargets.ContainsKey(candidate)) continue;
                var hit = new VisualElement { name = "shop-recruit-actor-" + candidate.Id, focusable = true };
                hit.RegisterCallback<PointerDownEvent>(evt => BeginRecruitPointer(evt, candidate));
                CharacterChoicePreview.Attach(hit, candidate, content.PortraitFor(candidate.Id));
                hit.style.display = DisplayStyle.None;
                recruitmentWindow.Insert(0, hit);
                recruitTargets.Add(candidate, hit);
            }
        }

        private void BeginRecruitPointer(PointerDownEvent evt, CharacterDefinition candidate)
        {
            if (evt.button != 0 || recruitHeld != null || stage.RecruitmentSpinning || replacementPanel != null ||
                !recruitment.CanSign(candidate.Id) || !stage.RecruitReady(candidate)) return;

            recruitHeld = candidate;
            recruitPointer = evt.pointerId;
            recruitPointerPosition = evt.position;
            recruitmentWindow.CapturePointer(recruitPointer);
            recruitHold = recruitmentWindow.schedule.Execute(() =>
            {
                recruitDragging = true;
                checkout[0].style.display = DisplayStyle.Flex;
                MoveHeldRecruit();
            }).StartingIn(290);
            evt.StopPropagation();
        }

        private void MoveRecruitPointer(PointerMoveEvent evt)
        {
            if (recruitHeld == null || evt.pointerId != recruitPointer) return;
            recruitPointerPosition = evt.position;
            if (recruitDragging) MoveHeldRecruit();
            evt.StopPropagation();
        }

        private void EndRecruitPointer(PointerUpEvent evt)
        {
            if (evt.button != 0 || recruitHeld == null || evt.pointerId != recruitPointer) return;
            var candidate = recruitHeld;
            bool atCounter = recruitDragging && checkout.worldBound.Contains(evt.position);
            ReleaseHeldRecruit();
            evt.StopPropagation();
            if (!atCounter || !recruitment.CanSign(candidate.Id)) return;
            if (party.Members.Count >= PartyRun.RosterLimit) ShowRecruitReplacement(candidate);
            else OpenRecruitContract(candidate);
        }

        private void MoveHeldRecruit()
        {
            var bounds = root.worldBound;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            stage.MoveHeldRecruit(recruitHeld, new Vector2((recruitPointerPosition.x - bounds.xMin) / bounds.width,
                1 - (recruitPointerPosition.y - bounds.yMin) / bounds.height));
            bool atCounter = checkout.worldBound.Contains(recruitPointerPosition);
            checkout.style.backgroundColor = atCounter ? new Color(.65f, .46f, .16f, .22f) : Color.clear;
            PrototypeStyle.Outline(checkout, atCounter ? PrototypeStyle.Accent : Color.clear, 7);
        }

        private void ReleaseHeldRecruit()
        {
            if (recruitHeld == null) return;
            recruitHold?.Pause();
            recruitHold = null;
            stage.ReleaseRecruit(recruitHeld);
            int captured = recruitPointer;
            recruitPointer = -1;
            recruitHeld = null;
            recruitDragging = false;
            if (recruitmentWindow.HasPointerCapture(captured)) recruitmentWindow.ReleasePointer(captured);
            checkout[0].style.display = DisplayStyle.None;
            checkout.style.backgroundColor = Color.clear;
            PrototypeStyle.Outline(checkout, Color.clear, 7);
        }

        private void OpenRecruitContract(CharacterDefinition candidate, string replace = null)
        {
            replacementPanel?.RemoveFromHierarchy();
            if (adventure == null) { SignRecruit(candidate, replace); return; }
            var talent = party.TalentFor(candidate);
            replacementPanel = ContractPanel.Negotiate(candidate, party.Day, talent, 0, null, CrewContract.ExpectedWage(candidate, talent),
                (days, terms, clause, wage) => SignRecruit(candidate, replace, days, terms, clause, wage),
                () => { replacementPanel?.RemoveFromHierarchy(); replacementPanel = null; if (recruitmentWindow != null) RefreshSpinControl(); });
            recruitmentWindow.Add(replacementPanel);
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
            AdventureWidgets.Action(choices, "取消换人", "shop-recruit-back", () =>
            {
                replacementPanel.RemoveFromHierarchy();
                replacementPanel = null;
                RefreshSpinControl();
            });
            recruitmentWindow.Add(replacementPanel);
            RefreshSpinControl();
        }

        private void RefreshSpinControl()
        {
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
            RefreshGold();
            stage.SpinRecruitmentWheel(recruitment.Candidates.Last());
            RefreshRecruitmentButton();
            spinButton.SetEnabled(false);
        }

        private void UpdateRecruitment(float delta)
        {
            if (stage.UpdateRecruitment(delta))
            {
                stage.RefreshWheel(recruitment);
                RefreshRecruitmentButton();
                if (recruitmentWindow != null)
                {
                    SyncRecruitCast(recruitment.Candidates.Last());
                    RefreshSpinControl();
                }
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
                return;
            }
            recruitmentWindow.style.opacity = Mathf.InverseLerp(.24f, .93f, stage.RecruitmentFocus);
            stage.PlaceRecruitmentControls(recruitmentControls);
            priceCounter.Tick(delta);
            foreach (var pair in recruitTargets)
            {
                bool ready = stage.RecruitReady(pair.Key) && replacementPanel == null;
                pair.Value.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
                if (ready) stage.PlaceRecruitTarget(pair.Key, pair.Value);
            }
        }

        private void CloseRecruitment()
        {
            if (recruitmentWindow == null) return;
            ReleaseHeldRecruit();
            recruitmentWindow.RemoveFromHierarchy();
            recruitmentWindow = recruitmentControls = replacementPanel = null;
            recruitTargets.Clear();
            priceCounter = null;
            stage.RecruitmentFocused = false;
            stockLayer.SetEnabled(true);
            RefreshRecruitmentButton();
        }
    }
}
