using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class DockShop : MonoBehaviour
    {
        private sealed class Offer
        {
            public EquipmentAsset Item;
            public int Price;
            public VisualElement View;
            public bool Sold;
        }

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private EquipmentAsset[] stockPool = Array.Empty<EquipmentAsset>();
        [SerializeField] private Texture2D backdrop;
        [SerializeField] private Texture2D merchantSheet;
        [SerializeField] private Texture2D shelfArtwork;
        [SerializeField] private Texture2D counterArtwork;

        private readonly System.Random random = new System.Random();
        private readonly List<Offer> offers = new List<Offer>();
        private VisualElement root;
        private VisualElement merchant;
        private Image merchantImage;
        private VisualElement checkout;
        private RunTopBar topBar;
        private PartyRun party;
        private Label feedback;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private int coins;
        private int expression = -1;
        private Offer hovered;
        private Offer dragging;
        private int pointerId = -1;
        private Vector2 pointer;
        private EquipmentIcon dragPreview;
        private EquipmentTooltip tooltip;
        private int purchaseExpression;
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
            expression = -1;
            hovered = null;
            celebrationUntil = 0;
            offers.Clear();
            var stock = stockPool.Where(item => item != null && item.ShopTier >= 0 && item.ShopPrice > 0).Distinct().ToArray();
            var choices = adventure != null ? adventure.Current.Stock.Select(offer => stock.First(item => item.ToDefinition().Id == offer.Item.Id)).ToArray() :
                stock.Where(item => item.ShopTier == 1).OrderBy(_ => random.Next()).Take(2)
                .Concat(stock.Where(item => item.ShopTier >= 2).OrderBy(_ => random.Next()).Take(2))
                .OrderBy(_ => random.Next()).ToArray();
            if (choices.Length != 4 || choices.Count(item => item.ShopTier == 1) != 2)
                throw new InvalidOperationException("The shop needs two T1 items and two T2/T3 items.");
            foreach (var item in choices)
                offers.Add(new Offer
                {
                    Item = item,
                    Price = adventure != null ? adventure.Current.Stock[offers.Count].Price :
                        (int)Math.Ceiling(item.ShopPrice * (0.7 + random.NextDouble() * 0.6)),
                    Sold = adventure != null && adventure.Current.Stock[offers.Count].Sold
                });
            Render();
        }

        private void OnDisable()
        {
            CancelDrag();
            HideTooltip();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            root.Clear();
            root.style.overflow = Overflow.Hidden;
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.backgroundImage = new StyleBackground(backdrop);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.color = PrototypeStyle.Ink;
            root.focusable = true;

            merchant = new VisualElement { name = "shop-merchant-stage", pickingMode = PickingMode.Ignore };
            Place(merchant, 1, 7, 47, 80);
            merchantImage = Picture(merchantSheet, "shop-merchant");
            PrototypeStyle.Fill(merchantImage);
            merchant.Add(merchantImage);
            root.Add(merchant);

            var cabinet = new VisualElement { name = "shop-shelf", pickingMode = PickingMode.Ignore };
            Place(cabinet, 55, 14, 41, 79);
            var cabinetImage = Picture(shelfArtwork, "shop-shelf-art");
            PrototypeStyle.Fill(cabinetImage);
            cabinet.Add(cabinetImage);
            root.Add(cabinet);
            var grid = new VisualElement { name = "shop-stock" };
            grid.style.position = Position.Absolute;
            grid.style.left = grid.style.right = Length.Percent(11);
            grid.style.top = Length.Percent(11);
            grid.style.bottom = Length.Percent(12);
            cabinet.Add(grid);
            for (int row = 0; row < 2; row++)
            {
                var line = PrototypeStyle.Row("shop-stock-row");
                line.style.flexGrow = 1;
                line.style.flexBasis = 0;
                line.style.minHeight = 0;
                for (int column = 0; column < 2; column++)
                    line.Add(BuildOffer(offers[row * 2 + column], row * 2 + column));
                grid.Add(line);
            }

            var counter = new VisualElement { name = "shop-counter", pickingMode = PickingMode.Ignore };
            Place(counter, 1, 64, 48, 32);
            var counterImage = Picture(counterArtwork, "shop-counter-art");
            PrototypeStyle.Fill(counterImage);
            counter.Add(counterImage);
            root.Add(counter);
            checkout = new VisualElement { name = "shop-checkout" };
            checkout.style.position = Position.Absolute;
            checkout.style.left = checkout.style.right = Length.Percent(6);
            checkout.style.top = Length.Percent(12);
            checkout.style.height = Length.Percent(55);
            checkout.style.justifyContent = Justify.Center;
            checkout.style.alignItems = Align.Center;
            PrototypeStyle.Outline(checkout, Color.clear, 7);
            var instruction = PrototypeStyle.Text("将商品拖到这里购买", 17, PrototypeStyle.Accent);
            instruction.pickingMode = PickingMode.Ignore;
            instruction.style.backgroundColor = new Color(0.05f, 0.06f, 0.065f, 0.88f);
            instruction.style.paddingLeft = instruction.style.paddingRight = 12;
            instruction.style.paddingTop = instruction.style.paddingBottom = 6;
            checkout.Add(instruction);
            counter.Add(checkout);

            var header = PrototypeStyle.Row("shop-header");
            header.style.position = Position.Absolute;
            header.style.top = RunTopBar.Height + 7;
            header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            if (adventure != null)
                header.Add(AdventureFlow.ReturnButton());
            var title = PrototypeStyle.Text("弥雾码头 · 商店", 23, PrototypeStyle.Accent);
            title.style.flexGrow = 1;
            title.style.marginLeft = 18;
            header.Add(title);
            root.Add(header);
            topBar = new RunTopBar(root, party, content, OpenEditor, "open-shop-editor",
                () => adventure?.Party.Gold ?? coins, "shop-gold");
            RunTopBar.PlaceAtTop(root, topBar);

            feedback = PrototypeStyle.Text("", 17, PrototypeStyle.Accent);
            feedback.name = "shop-feedback";
            feedback.style.position = Position.Absolute;
            feedback.style.bottom = 14;
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
            root.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.pointerId == pointerId)
                    CancelDrag();
            });
            root.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (dragging == null)
                    Hover(null);
            });
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                    CancelDrag();
            });
            RefreshExpression();
        }

        private VisualElement BuildOffer(Offer offer, int index)
        {
            var view = new VisualElement { name = $"shop-offer-{index}" };
            offer.View = view;
            view.style.flexGrow = 1;
            view.style.flexBasis = 0;
            view.style.minWidth = view.style.minHeight = 0;
            view.style.marginLeft = view.style.marginRight = 6;
            view.style.marginTop = view.style.marginBottom = 5;
            if (offer.Sold)
            {
                var sold = PrototypeStyle.Text("已售出", 18, PrototypeStyle.Muted);
                sold.name = "shop-sold"; sold.style.flexGrow = 1; PrototypeStyle.Middle(sold);
                view.Add(sold); view.SetEnabled(false); view.style.opacity = 0.55f;
                return view;
            }
            var icon = new EquipmentIcon(offer.Item.ToDefinition(), offer.Item.Icon);
            icon.name = "shop-item-art";
            icon.style.flexBasis = 0;
            icon.style.minHeight = 0;
            view.Add(icon);
            var caption = new VisualElement { pickingMode = PickingMode.Ignore };
            caption.style.backgroundColor = new Color(0.04f, 0.05f, 0.055f, 0.88f);
            caption.style.paddingLeft = caption.style.paddingRight = 5;
            var name = PrototypeStyle.Text(offer.Item.ToDefinition().Name, 15);
            name.name = "shop-item-name";
            name.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(name);
            caption.Add(name);
            var price = PrototypeStyle.Text($"{offer.Price} 金币", 14, PrototypeStyle.Accent);
            price.name = "shop-item-price";
            price.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(price);
            caption.Add(price);
            view.Add(caption);
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
            dragPreview = new EquipmentIcon(offer.Item.ToDefinition(), offer.Item.Icon) { name = "shop-drag-preview" };
            dragPreview.style.position = Position.Absolute;
            dragPreview.style.width = dragPreview.style.height = 100;
            offer.View.style.opacity = 0.4f;
            root.Add(dragPreview);
            root.Focus();
            root.CapturePointer(pointerId);
            MoveDragPreview();
            RefreshExpression();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!merchant.enabledInHierarchy)
                return;
            pointer = evt.position;
            if (dragging == null)
            {
                Hover(offers.FirstOrDefault(offer => !offer.Sold && offer.View.worldBound.Contains(pointer)));
                return;
            }
            if (evt.pointerId != pointerId)
                return;
            MoveDragPreview();
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
            bool atCheckout = checkout.worldBound.Contains(evt.position);
            CancelDrag();
            if (atCheckout)
                Purchase(offer);
            evt.StopPropagation();
        }

        private void Purchase(Offer offer)
        {
            if (offer.Sold)
                return;
            if ((adventure?.Party.Gold ?? coins) < offer.Price)
            {
                ShowFeedback("金币不足");
                return;
            }
            if (adventure != null)
            {
                if (!adventure.Buy(offers.IndexOf(offer))) { ShowFeedback("这次购买未能完成"); return; }
            }
            else
            {
                party.AddEquipment(offer.Item.ToDefinition());
                coins -= offer.Price;
            }
            offer.Sold = true;
            offer.View.Clear();
            offer.View.SetEnabled(false);
            offer.View.style.opacity = 0.55f;
            var sold = PrototypeStyle.Text("已售出", 18, PrototypeStyle.Muted);
            sold.name = "shop-sold";
            sold.style.flexGrow = 1;
            PrototypeStyle.Middle(sold);
            offer.View.Add(sold);
            hovered = null;
            HideTooltip();
            RefreshGold();
            purchaseExpression = ExpressionFor(offer.Item.ShopTier);
            celebrationUntil = Time.unscaledTimeAsDouble + 1.05;
            RefreshExpression();
            ShowFeedback($"已购买 {offer.Item.ToDefinition().Name}，已放入共享背包");
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
            tooltip = new EquipmentTooltip(offer.Item.ToDefinition(), offer.Item.Icon);
            tooltip.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            tooltip.ShowAbove(root, offer.View);
        }

        private static int ExpressionFor(int tier) => tier == 1 ? 2 : tier == 2 ? 1 : 0;

        private void RefreshExpression()
        {
            int next = Time.unscaledTimeAsDouble < celebrationUntil ? purchaseExpression :
                ExpressionFor(dragging?.Item.ShopTier ?? hovered?.Item.ShopTier ?? 3);
            if (next == expression || merchantSheet == null)
                return;
            expression = next;
            float width = merchantSheet.width / 3f;
            merchantImage.sourceRect = new Rect(next * width, 0, width, merchantSheet.height);
            merchantImage.MarkDirtyRepaint();
        }

        private void Update()
        {
            if (merchant == null)
                return;
            float remaining = Mathf.Clamp01((float)((celebrationUntil - Time.unscaledTimeAsDouble) / 1.05));
            float offset = Mathf.Sin((1f - remaining) * Mathf.PI * 6f) * 12f * remaining;
            merchant.style.translate = new Translate(0, offset);
            RefreshExpression();
            if (feedback != null && Time.unscaledTimeAsDouble >= feedbackUntil)
                feedback.style.display = DisplayStyle.None;
        }

        private void CancelDrag()
        {
            int captured = pointerId;
            pointerId = -1;
            if (dragging != null)
                dragging.View.style.opacity = 1;
            dragging = null;
            hovered = null;
            HideTooltip();
            dragPreview?.RemoveFromHierarchy();
            dragPreview = null;
            if (root != null && captured >= 0 && root.HasPointerCapture(captured))
                root.ReleasePointer(captured);
            if (checkout != null)
            {
                checkout.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(checkout, Color.clear, 7);
            }
            RefreshExpression();
        }

        private void MoveDragPreview()
        {
            var local = root.WorldToLocal(pointer);
            dragPreview.style.left = local.x - 50;
            dragPreview.style.top = local.y - 50;
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
            RunTopBar.EditParty(root, party, content, RefreshGold,
                adventure == null ? BattlePrototype.MarkPartyEdited : null);
        }

        private static Image Picture(Texture2D texture, string name) => new Image
        {
            name = name, image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore
        };

        private static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(left);
            element.style.top = Length.Percent(top);
            element.style.width = Length.Percent(width);
            element.style.height = Length.Percent(height);
        }
    }
}
