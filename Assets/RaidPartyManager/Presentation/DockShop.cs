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
    public sealed class DockShop : MonoBehaviour
    {
        private const float CelebrationDuration = 1.05f;

        private sealed class Offer
        {
            public EquipmentDefinition Item;
            public Texture2D Icon;
            public int Index;
            public int Price;
            public VisualElement View;
            public Label PriceTag;
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
        private RunTopBar topBar;
        private PartyRun party;
        private Label feedback;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private int coins;
        private Offer hovered;
        private Offer dragging;
        private int pointerId = -1;
        private Vector2 pointer;
        private EquipmentTooltip tooltip;
        private double celebrationUntil;
        private double feedbackUntil;
        private AdventureRun adventure;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            content = encounter.ToData();
            adventure = AdventureFlow.ForScene("DockShop");
            party = adventure?.Party ?? BattlePrototype.GetPlaySession(content);
            coins = 999;
            hovered = null;
            celebrationUntil = 0;
            var catalogue = stockPool.Where(item => item != null).Select(item => item.ToDefinition()).Concat(content.SkillBooks);
            var stock = adventure?.Current.Stock ?? AdventureShopOffer.Draw(catalogue, random, false);
            stage = new ShopStage(transform, root, stageArt, backdrop, merchantReactions);
            stage.ShowCompany(party, content);
            Render();
            DisplayStock(stock);
        }

        private void DisplayStock(IReadOnlyList<AdventureShopOffer> stock)
        {
            offers.Clear();
            stockLayer.Clear();
            for (int i = 0; i < stock.Count; i++)
            {
                var record = stock[i];
                var offer = new Offer { Index = i, Item = record.Item, Icon = content.IconFor(record.Item.Id),
                    Price = record.Price, Sold = record.Sold };
                offers.Add(offer);
                stage.Hang(i, offer.Item, offer.Icon, offer.Sold);
                var view = BuildOffer(offer);
                stockLayer.Add(view);
                if (!offer.Sold) stage.PlaceOffer(view, offer.PriceTag, i);
            }
        }

        private void OnDisable()
        {
            CancelDrag();
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

            stockLayer = new VisualElement { name = "shop-stock", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(stockLayer);
            root.Add(stockLayer);

            checkout = new VisualElement { name = "shop-checkout" };
            checkout.style.justifyContent = Justify.Center;
            checkout.style.alignItems = Align.Center;
            PrototypeStyle.Outline(checkout, Color.clear, 7);
            var instruction = PrototypeStyle.Text("将商品拖到这里购买", 17, PrototypeStyle.Accent);
            instruction.pickingMode = PickingMode.Ignore;
            instruction.style.display = DisplayStyle.None;
            instruction.style.backgroundColor = new Color(0.05f, 0.06f, 0.065f, 0.88f);
            instruction.style.paddingLeft = instruction.style.paddingRight = 12;
            instruction.style.paddingTop = instruction.style.paddingBottom = 6;
            checkout.Add(instruction);
            root.Add(checkout);
            stage.PlaceCounter(checkout);

            topBar = new RunTopBar(root, party, content, OpenEditor, "open-shop-editor",
                () => adventure?.Party.Gold ?? coins, "shop-gold");
            if (adventure != null) AdventureFlow.AddServiceNavigation(root, stageArt.timber, topBar);
            else root.Add(new StageSignMask(HangingSign.Exit("leave-shop", () => PrototypeSceneMenu.OpenStandalone("MainMenu"))));
            RunTopBar.PlaceAtTop(root, topBar);

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
            var view = new VisualElement { name = $"shop-offer-{offer.Index}" };
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
            price.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            price.style.whiteSpace = WhiteSpace.NoWrap;
            price.style.paddingLeft = price.style.paddingRight = 7;
            price.style.paddingTop = price.style.paddingBottom = 2;
            price.style.unityFontStyleAndWeight = FontStyle.Bold;
            price.style.backgroundColor = (Color)new Color32(224,193,138,255);
            PrototypeStyle.Outline(price, new Color32(123,85,43,255), 2);
            view.Add(price);
            view.RegisterCallback<PointerEnterEvent>(_ => { if (dragging == null) Hover(offer); });
            view.RegisterCallback<PointerLeaveEvent>(_ => { if (dragging == null && hovered == offer) Hover(null); });
            view.RegisterCallback<PointerDownEvent>(evt => BeginDrag(evt, offer));
            return view;
        }

        private void BeginDrag(PointerDownEvent evt, Offer offer)
        {
            if (evt.button != 0 || offer.Sold || dragging != null)
                return;
            dragging = offer;
            pointerId = evt.pointerId;
            pointer = evt.position;
            HideTooltip();
            checkout[0].style.display = DisplayStyle.Flex;
            root.Focus();
            root.CapturePointer(pointerId);
            MoveHeldItem();
            RefreshExpression();
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == pointerId)
                CancelDrag();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == pointerId) CancelDrag();
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (dragging == null)
                Hover(null);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
                CancelDrag();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!stockLayer.enabledInHierarchy)
                return;
            pointer = evt.position;
            if (dragging == null)
            {
                Hover(offers.FirstOrDefault(offer => !offer.Sold && offer.View.worldBound.Contains(pointer)));
                return;
            }
            if (evt.pointerId != pointerId)
                return;
            MoveHeldItem();
            bool ready = checkout.worldBound.Contains(pointer);
            checkout.style.backgroundColor = ready ? new Color(0.65f, 0.46f, 0.16f, 0.22f) : Color.clear;
            PrototypeStyle.Outline(checkout, ready ? PrototypeStyle.Accent : Color.clear, 7);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0 || dragging == null || evt.pointerId != pointerId)
                return;
            var offer = dragging;
            pointer = evt.position;
            MoveHeldItem();
            bool atCheckout = checkout.worldBound.Contains(evt.position);
            bool bought = atCheckout && Purchase(offer);
            EndDrag(bought);
            evt.StopPropagation();
        }

        private bool Purchase(Offer offer)
        {
            if (offer.Sold)
                return false;
            if ((adventure?.Party.Gold ?? coins) < offer.Price)
            {
                ShowFeedback("金币不足");
                return false;
            }
            if (adventure != null)
            {
                if (!adventure.Buy(offer.Index)) { ShowFeedback("这次购买未能完成"); return false; }
            }
            else
            {
                party.AddEquipment(offer.Item);
                coins -= offer.Price;
            }
            offer.Sold = true;
            offer.View.style.display = DisplayStyle.None;
            hovered = null;
            HideTooltip();
            RefreshGold();
            celebrationUntil = Time.unscaledTimeAsDouble + CelebrationDuration;
            RefreshExpression();
            ShowFeedback("已放入共享背包");
            return true;
        }

        private void Hover(Offer offer)
        {
            if (hovered == offer)
                return;
            hovered = offer;
            HideTooltip();
            RefreshExpression();
            if (offer == null || offer.Sold || dragging != null)
                return;
            tooltip = new EquipmentTooltip(offer.Item, offer.Icon);
            tooltip.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            tooltip.ShowAbove(root, offer.View);
        }

        private void RefreshExpression()
        {
            var offer = dragging ?? hovered;
            var remaining = Mathf.Clamp01((float)((celebrationUntil - Time.unscaledTimeAsDouble) / CelebrationDuration));
            var face = remaining > 0 ? ShopStage.Expression.Laugh : offer == null ? ShopStage.Expression.Idle :
                offer.Item.Tier == 1 ? ShopStage.Expression.Delighted : ShopStage.Expression.Happy;
            stage.SetMerchant(face, Time.unscaledTime, remaining);
        }

        private void Update()
        {
            stage.FrameCamera();
            stage.PlaceCounter(checkout);
            foreach (var offer in offers)
            {
                stage.Pose(offer.Index, Time.unscaledTime);
                if (!offer.Sold) stage.PlaceOffer(offer.View, offer.PriceTag, offer.Index);
            }
            RefreshExpression();
            if (feedback != null && Time.unscaledTimeAsDouble >= feedbackUntil)
                feedback.style.display = DisplayStyle.None;
        }

        private void CancelDrag() => EndDrag(false);

        private void EndDrag(bool bought)
        {
            int captured = pointerId;
            pointerId = -1;
            if (dragging != null)
            {
                stage.Release(dragging.Index, bought);
            }
            dragging = null;
            hovered = null;
            HideTooltip();
            if (root != null && captured >= 0 && root.HasPointerCapture(captured))
                root.ReleasePointer(captured);
            if (checkout != null)
            {
                checkout[0].style.display = DisplayStyle.None;
                checkout.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(checkout, Color.clear, 7);
            }
            RefreshExpression();
        }

        private void MoveHeldItem()
        {
            var bounds = root.worldBound;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            stage.Pull(dragging.Index, new Vector2((pointer.x - bounds.xMin) / bounds.width,
                1 - (pointer.y - bounds.yMin) / bounds.height));
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

        private void OpenEditor()
        {
            CancelDrag();
            topBar.EditParty(() => stage.ShowCompany(party, content), edited: adventure == null ? BattlePrototype.MarkPartyEdited : null);
        }

    }
}
