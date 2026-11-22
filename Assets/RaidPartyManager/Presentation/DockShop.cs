using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class DockShop : MonoBehaviour
    {
        private const float CelebrationDuration = 1.05f;
        private const float PurchaseHoldSeconds = .55f;
        internal static bool RecruitmentWheelVisible { get; set; } = false;
        private static PartyRun standalonePriceOwner;
        private static int standaloneRecruitPrice = ShopRecruitment.StartingPrice;

        private sealed class Offer
        {
            public EquipmentDefinition Item;
            public Texture2D Icon;
            public int Index;
            public int Price;
            public VisualElement View;
            public Label PriceTag;
            public VisualElement HoldFill;
            public bool Sold;
        }

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private EquipmentAsset[] stockPool = Array.Empty<EquipmentAsset>();
        [SerializeField] private Texture2D backdrop;
        [SerializeField] private Texture2D merchantReactions;
        [SerializeField] private ServiceStageArt stageArt;

        private readonly System.Random random = new System.Random();
        private readonly List<Offer> offers = new List<Offer>();
        private VisualElement root;
        private ShopStage stage;
        private VisualElement stockLayer;
        private VisualElement checkout;
        private VisualElement counterBackdrop;
        private Button counterVisit;
        private Label closeUpHint;
        private RunTopBar topBar;
        private PartyRun party;
        private Label feedback;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private int coins;
        private readonly List<GoldExpense> standaloneExpenses = new();
        private Offer hovered;
        private Offer heldOffer;
        private double holdStarted;
        private Vector2 holdOrigin;
        private int pointerId = -1;
        private EquipmentTooltip tooltip;
        private double celebrationUntil;
        private double feedbackUntil;
        private AdventureRun adventure;
        private ShopRecruitment recruitment;
        private Button recruitmentButton;


        private int Gold => adventure?.Party.Gold ?? coins;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            content = encounter.ToData();
            adventure = AdventureFlow.ForScene("DockShop");
            party = adventure?.Party ?? BattlePrototype.GetPlaySession(content);
            coins = 999;
            standaloneExpenses.Clear();
            hovered = null;
            celebrationUntil = 0;
            var catalogue = stockPool.Where(item => item != null).Select(item => item.ToDefinition()).Concat(content.SkillBooks);
            var stock = adventure?.Current.Stock ?? AdventureShopOffer.Draw(catalogue, random, false);
            stage = new ShopStage(transform, root, stageArt, backdrop, merchantReactions, adventure?.InGraveyard == true, adventure?.InCapital == true);
            stage.ShowCompany(party, content);
            if (RecruitmentWheelVisible) PrepareRecruitment();
            Render();
            DisplayStock(stock);
        }

        private void DisplayStock(IReadOnlyList<AdventureShopOffer> stock)
        {
            CancelPurchaseHold();
            HideTooltip();
            hovered = null;
            offers.Clear();
            stockLayer.Clear();
            for (int i = 0; i < stock.Count; i++)
            {
                var record = stock[i];
                var offer = new Offer { Index = i, Item = record.Item, Icon = content.IconFor(record.Item.Id),
                    Price = record.Price, Sold = record.Sold };
                offers.Add(offer);
                stage.SetTableItem(i, offer.Item, offer.Icon, offer.Sold);
                var view = BuildOffer(offer);
                stockLayer.Add(view);
                if (!offer.Sold) stage.PlaceOffer(view, offer.PriceTag, i);
            }
        }

        private void OnDisable()
        {
            CloseSale();
            CloseRecruitment();
            recruitment = null;
            CancelPurchaseHold();
            HideTooltip();
            root.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            root.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            root.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            root.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            stage?.Dispose();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            root.Clear();
            root.style.overflow = Overflow.Hidden;
            root.style.backgroundColor = Color.clear;
            root.style.color = PrototypeStyle.Ink;
            root.focusable = true;

            counterBackdrop = new VisualElement { name = "shop-counter-background" };
            PrototypeStyle.Fill(counterBackdrop);
            counterBackdrop.style.display = DisplayStyle.None;
            counterBackdrop.RegisterCallback<PointerDownEvent>(OnBackgroundDown);
            root.Add(counterBackdrop);

            stockLayer = new VisualElement { name = "shop-stock", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(stockLayer);
            root.Add(stockLayer);

            counterVisit = PrototypeStyle.Button("查看商品", "shop-counter", OpenCounter);
            counterVisit.style.justifyContent = Justify.FlexEnd;
            counterVisit.style.paddingBottom = 8;
            counterVisit.style.marginTop = counterVisit.style.marginBottom = 0;
            counterVisit.style.backgroundColor = Color.clear;
            counterVisit.style.unityFontStyleAndWeight = FontStyle.Bold;
            counterVisit.style.unityTextOutlineColor = (Color)new Color32(35, 23, 20, 255);
            counterVisit.style.unityTextOutlineWidth = 1f;
            PrototypeStyle.Outline(counterVisit, Color.clear, 0);
            stage.PlaceCounterVisit(counterVisit);
            root.Add(counterVisit);

            checkout = new VisualElement { name = "shop-checkout", pickingMode = PickingMode.Ignore };
            checkout.style.justifyContent = Justify.Center;
            checkout.style.alignItems = Align.Center;
            PrototypeStyle.Outline(checkout, Color.clear, 7);
            var instruction = PrototypeStyle.Text("将候选人拖到这里签约", 17, PrototypeStyle.Accent);
            instruction.pickingMode = PickingMode.Ignore;
            instruction.style.display = DisplayStyle.None;
            instruction.style.backgroundColor = new Color(0.05f, 0.06f, 0.065f, 0.88f);
            instruction.style.paddingLeft = instruction.style.paddingRight = 12;
            instruction.style.paddingTop = instruction.style.paddingBottom = 6;
            checkout.Add(instruction);
            root.Add(checkout);
            stage.PlaceCounter(checkout);

            topBar = new RunTopBar(root, party, content, OpenEditor, "open-shop-editor",
                () => Gold, "shop-gold", expenseOverride: adventure == null ? standaloneExpenses : null);
            if (adventure != null) AdventureFlow.AddServiceNavigation(root, stageArt.graveyardStone, topBar, () => stage.WideView);
            else root.Add(new StageSignMask(HangingSign.Exit("leave-shop", () => PrototypeSceneMenu.OpenStandalone("MainMenu"))));
            RunTopBar.PlaceAtTop(root, topBar);
            stage.MountShopSigns(root);

            if (recruitment != null) RenderRecruitmentEntry();

            closeUpHint = PrototypeStyle.Text("长按购买 · 点击空白返回", 15, PrototypeStyle.Accent);
            closeUpHint.name = "shop-counter-hint";
            closeUpHint.style.position = Position.Absolute;
            closeUpHint.style.left = Length.Percent(30);
            closeUpHint.style.right = Length.Percent(30);
            closeUpHint.style.top = RunTopBar.Height + 10;
            closeUpHint.style.backgroundColor = PrototypeStyle.PanelColor;
            closeUpHint.style.paddingTop = closeUpHint.style.paddingBottom = 6;
            closeUpHint.style.display = DisplayStyle.None;
            PrototypeStyle.Middle(closeUpHint);
            closeUpHint.pickingMode = PickingMode.Ignore;
            root.Add(closeUpHint);
            sellEntry = PrototypeStyle.Button("出售背包物品", "shop-sell", OpenSale);
            sellEntry.style.position = Position.Absolute;
            sellEntry.style.right = 14;
            sellEntry.style.top = RunTopBar.Height + 10;
            sellEntry.style.display = DisplayStyle.None;
            root.Add(sellEntry);

            feedback = PrototypeStyle.Text("", 17, PrototypeStyle.Accent);
            feedback.name = "shop-feedback";
            feedback.style.position = Position.Absolute;
            feedback.style.bottom = Length.Percent(AudienceForeground.ClearancePercent);
            feedback.style.left = Length.Percent(15);
            feedback.style.right = Length.Percent(15);
            feedback.style.display = DisplayStyle.None;
            feedback.style.backgroundColor = new Color(0.05f, 0.06f, 0.065f, 0.92f);
            feedback.style.paddingTop = feedback.style.paddingBottom = 8;
            PrototypeStyle.Middle(feedback);
            feedback.pickingMode = PickingMode.Ignore;
            root.Add(feedback);

            root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            root.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            RefreshExpression();
        }

        private VisualElement BuildOffer(Offer offer)
        {
            var view = new VisualElement { name = $"shop-offer-{offer.Index}", pickingMode = PickingMode.Ignore };
            offer.View = view;
            view.style.alignItems = Align.Center;
            if (offer.Sold)
            {
                view.style.display = DisplayStyle.None;
                return view;
            }
            var price = PrototypeStyle.Text($"{offer.Price} 金币", 13, new Color32(58,36,19,255));
            offer.PriceTag = price;
            price.name = "shop-item-price";
            price.pickingMode = PickingMode.Ignore;
            price.style.position = Position.Absolute;
            price.style.whiteSpace = WhiteSpace.NoWrap;
            price.style.paddingLeft = price.style.paddingRight = 0;
            price.style.paddingTop = price.style.paddingBottom = 0;
            price.style.unityTextAlign = TextAnchor.MiddleCenter;
            price.style.unityFontStyleAndWeight = FontStyle.Bold;
            view.Add(price);
            var track = new VisualElement { name = "shop-hold-progress", pickingMode = PickingMode.Ignore };
            track.style.position = Position.Absolute;
            track.style.left = track.style.right = 0;
            track.style.bottom = -5;
            track.style.height = 4;
            track.style.backgroundColor = PrototypeStyle.Inset;
            track.style.display = DisplayStyle.None;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.backgroundColor = PrototypeStyle.Accent;
            track.Add(fill);
            price.Add(track);
            offer.HoldFill = fill;
            view.RegisterCallback<PointerEnterEvent>(_ => { if (stage.CounterReady && heldOffer == null) Hover(offer); });
            view.RegisterCallback<PointerLeaveEvent>(_ => { if (heldOffer == null && hovered == offer) Hover(null); });
            view.RegisterCallback<PointerDownEvent>(evt => BeginPurchaseHold(evt, offer));
            return view;
        }

        private void OpenCounter()
        {
            if (!stage.WideView || recruitmentWindow != null) return;
            stage.CounterFocused = true;
            counterBackdrop.style.display = DisplayStyle.Flex;
            counterVisit.style.display = DisplayStyle.None;
            root.Focus();
        }

        private void CloseCounter()
        {
            CancelPurchaseHold();
            Hover(null);
            stage.CounterFocused = false;
            counterBackdrop.style.display = DisplayStyle.None;
            closeUpHint.style.display = DisplayStyle.None;
        }

        private void OnBackgroundDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || !stage.CounterFocused || recruitmentWindow != null) return;
            CloseCounter();
            evt.StopPropagation();
        }

        private void BeginPurchaseHold(PointerDownEvent evt, Offer offer)
        {
            if (evt.button != 0 || !stage.CounterReady || !offer.View.enabledInHierarchy ||
                offer.Sold || heldOffer != null || !offer.View.worldBound.Contains(evt.position))
                return;

            hovered = offer;
            heldOffer = offer;
            pointerId = evt.pointerId;
            holdOrigin = evt.position;
            holdStarted = Time.unscaledTimeAsDouble;
            offer.HoldFill.style.width = Length.Percent(0);
            offer.HoldFill.parent.style.display = DisplayStyle.Flex;
            HideTooltip();
            root.Focus();
            root.CapturePointer(pointerId);
            RefreshExpression();
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == pointerId) CancelPurchaseHold();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == pointerId) CancelPurchaseHold();
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (heldOffer == null) Hover(null);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            if (saleWindow != null) { CloseSale(); evt.StopPropagation(); }
            else if (recruitmentWindow != null) CloseRecruitment();
            else if (stage.CounterFocused) CloseCounter();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (heldOffer != null)
            {
                if (evt.pointerId != pointerId) return;
                var position = (Vector2)evt.position;
                if ((position - holdOrigin).sqrMagnitude > 18 * 18 || !heldOffer.View.worldBound.Contains(position))
                    CancelPurchaseHold();
                evt.StopPropagation();
                return;
            }
            if (saleWindow == null && stage.CounterReady && stockLayer.enabledInHierarchy)
                Hover(offers.FirstOrDefault(offer => !offer.Sold && offer.View.worldBound.Contains(evt.position)));
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0 || evt.pointerId != pointerId) return;
            CancelPurchaseHold();
            evt.StopPropagation();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelPurchaseHold();
        }

        private void Purchase(Offer offer)
        {
            if (offer.Sold)
                return;
            if (Gold < offer.Price)
            {
                GameAudio.Play(SoundCue.UiRestricted);
                ShowFeedback("金币不足");
                return;
            }
            if (adventure != null)
            {
                if (!adventure.Buy(offer.Index)) { ShowFeedback("这次购买未能完成"); return; }
            }
            else
            {
                party.AddEquipment(offer.Item);
                SpendTestCoins(offer.Price, "购买 · " + offer.Item.Name);
            }
            offer.Sold = true;
            GameAudio.Play(SoundCue.ShopCheckout);
            GameAudio.Play(SoundCue.ShopkeeperChuckle, .39f);
            stage.SellTableItem(offer.Index);
            offer.View.style.display = DisplayStyle.None;
            hovered = null;
            HideTooltip();
            RefreshGold();
            celebrationUntil = Time.unscaledTimeAsDouble + CelebrationDuration;
            RefreshExpression();
            ShowFeedback("已放入共享背包");
        }

        private void Hover(Offer offer)
        {
            if (hovered == offer)
                return;
            hovered = offer;
            HideTooltip();
            RefreshExpression();
            if (offer == null || offer.Sold || heldOffer != null)
                return;
            tooltip = new EquipmentTooltip(offer.Item, offer.Icon);
            tooltip.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            tooltip.ShowAbove(root, offer.View);
        }

        private void RefreshExpression()
        {
            var offer = heldOffer ?? hovered;
            var remaining = Mathf.Clamp01((float)((celebrationUntil - Time.unscaledTimeAsDouble) / CelebrationDuration));
            var face = remaining > 0 ? ShopStage.Expression.Laugh : offer == null ? ShopStage.Expression.Idle :
                offer.Item.Tier == 1 ? ShopStage.Expression.Delighted : ShopStage.Expression.Happy;
            stage.SetMerchant(face, Time.unscaledTime, remaining);
        }

        private void Update()
        {
            stage.FrameCamera();
            stage.UpdateCamera(Time.unscaledDeltaTime);
            if (recruitment != null) UpdateRecruitment(Time.unscaledDeltaTime);
            stage.PoseShopSigns(root);
            stage.PlaceCounter(checkout);
            if (stage.WideView)
            {
                stage.PlaceCounterVisit(counterVisit);
                counterVisit.style.display = DisplayStyle.Flex;
            }
            else counterVisit.style.display = DisplayStyle.None;

            bool ready = stage.CounterReady;
            closeUpHint.style.display = ready && saleWindow == null ? DisplayStyle.Flex : DisplayStyle.None;
            sellEntry.style.display = ready && saleWindow == null ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var offer in offers)
            {
                if (offer.Sold) continue;
                stage.PlaceOffer(offer.View, offer.PriceTag, offer.Index);
                offer.View.pickingMode = ready ? PickingMode.Position : PickingMode.Ignore;
            }
            if (heldOffer != null)
            {
                if (!ready || !heldOffer.View.enabledInHierarchy) CancelPurchaseHold();
                else
                {
                    float progress = (float)(Time.unscaledTimeAsDouble - holdStarted) / PurchaseHoldSeconds;
                    heldOffer.HoldFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
                    if (progress >= 1)
                    {
                        var offer = heldOffer;
                        CancelPurchaseHold();
                        Purchase(offer);
                    }
                }
            }
            RefreshExpression();
            if (feedback != null && Time.unscaledTimeAsDouble >= feedbackUntil)
                feedback.style.display = DisplayStyle.None;
        }

        private void CancelPurchaseHold()
        {
            if (heldOffer == null) return;
            heldOffer.HoldFill.parent.style.display = DisplayStyle.None;
            heldOffer = null;
            hovered = null;
            int captured = pointerId;
            pointerId = -1;
            if (root.HasPointerCapture(captured)) root.ReleasePointer(captured);
            RefreshExpression();
        }

        private void HideTooltip()
        {
            tooltip?.RemoveFromHierarchy();
            tooltip = null;
        }

        private void ShowFeedback(string message)
        {
            feedback.text = message;
            feedback.style.display = DisplayStyle.Flex;
            feedbackUntil = Time.unscaledTimeAsDouble + 3;
        }

        private void RefreshGold() => topBar.Refresh();

        private void RefreshRecruitmentButton()
        {
            recruitmentButton.text = recruitment.Signed ? "本次招募已完成" : "角色招募";
            recruitmentButton.SetEnabled(!recruitment.Signed && !stage.RecruitmentSpinning);
            recruitmentButton.style.display = recruitmentWindow == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool SpendTestCoins(int price) => SpendTestCoins(price, "招募转盘");

        private bool SpendTestCoins(int price, string reason)
        {
            if (coins < price) return false;
            coins -= price;
            if (price > 0) standaloneExpenses.Add(new GoldExpense { Day = Math.Max(1, party.Day), Reason = reason, Amount = price });
            return true;
        }

        private bool SignRecruit(CharacterDefinition candidate, string replacedMemberId = null, int days = 12,
            PerformanceBonuses terms = default, long? signingFee = null)
        {
            bool signed = adventure != null ? adventure.SignShopRecruit(candidate.Id, replacedMemberId, days, terms, signingFee) :
                recruitment.TrySign(candidate.Id, replacedMemberId);
            if (!signed) return false;
            GameAudio.Play(SoundCue.CompanionJoin);
            if (adventure == null) BattlePrototype.MarkPartyEdited();
            CloseRecruitment();
            RefreshRecruitmentButton();
            RefreshGold();
            ShowFeedback($"{candidate.Name} 加入了讨伐队");
            return true;
        }

        private void OpenEditor()
        {
            CloseSale();
            CloseRecruitment();
            CancelPurchaseHold();
            topBar.EditParty(() => stage.ShowCompany(party, content), edited: adventure == null ? BattlePrototype.MarkPartyEdited : null);
        }

    }
}
