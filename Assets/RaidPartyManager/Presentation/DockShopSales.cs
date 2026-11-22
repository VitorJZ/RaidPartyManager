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
        private Button sellEntry;
        private VisualElement saleWindow;

        private void OpenSale()
        {
            if (saleWindow != null || !stage.CounterReady || recruitmentWindow != null) return;
            CancelPurchaseHold();
            Hover(null);
            saleWindow = new VisualElement { name = "shop-sale-window" };
            PrototypeStyle.Fill(saleWindow);
            saleWindow.style.backgroundColor = new Color(.06f, .05f, .045f, .72f);
            saleWindow.style.alignItems = Align.Center;
            saleWindow.style.justifyContent = Justify.Center;
            saleWindow.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.target == saleWindow) { CloseSale(); evt.StopPropagation(); }
            });

            var panel = PrototypeStyle.Panel();
            panel.style.width = Length.Percent(85);
            panel.style.maxWidth = 560;
            panel.style.height = Length.Percent(74);
            panel.style.maxHeight = 520;
            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            var title = PrototypeStyle.Text("出售背包物品", 22, PrototypeStyle.Accent);
            title.style.flexGrow = 1;
            header.Add(title);
            var balance = PrototypeStyle.Text("", 15, PrototypeStyle.Accent);
            header.Add(balance);
            panel.Add(header);
            panel.Add(PrototypeStyle.Text($"普通装备和技能书按原价的 {party.ResalePercent}% 回收，Boss 专属物品每件 100 金币。", 13, PrototypeStyle.Muted));
            var stock = new ScrollView(ScrollViewMode.Vertical) { name = "shop-sale-list" };
            stock.style.flexGrow = 1;
            stock.style.minHeight = 0;
            stock.style.marginTop = stock.style.marginBottom = 8;
            panel.Add(stock);
            var status = PrototypeStyle.Text("", 14, PrototypeStyle.Ink);
            status.name = "shop-sale-status";
            panel.Add(status);
            var actions = PrototypeStyle.Row();
            var basket = new Dictionary<string, int>();
            var confirm = PrototypeStyle.Button("确认出售", "shop-confirm-sale", null, true);
            confirm.clicked += Sell;
            confirm.style.flexGrow = 1;
            actions.Add(confirm);
            actions.Add(PrototypeStyle.Button("返回柜台", "shop-close-sale", CloseSale));
            panel.Add(actions);
            saleWindow.Add(panel);
            root.Add(saleWindow);
            RefreshStock("");
            panel.Q<Button>()?.Focus();

            void RefreshStock(string message)
            {
                basket.Clear();
                confirm.text = "确认出售";
                confirm.SetEnabled(false);
                balance.text = $"持有 {Gold} 金币";
                stock.Clear();
                foreach (var group in party.SaleableItems.GroupBy(copy => copy.Definition.Id))
                {
                    var copies = group.ToArray();
                    var item = copies[0];
                    var definition = item.Definition;
                    int price = party.ResalePrice(definition);
                    var row = PrototypeStyle.Button("", "sell-item-" + definition.Id, null);
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.minHeight = 43;
                    var icon = new EquipmentIcon(definition, content.IconFor(definition.Id));
                    icon.style.flexGrow = 0;
                    icon.style.width = icon.style.height = 34;
                    icon.style.marginRight = 8;
                    row.Add(icon);
                    string name = definition is SkillBookDefinition ? "技能书 · " + definition.Name : definition.Name;
                    var label = PrototypeStyle.Text("", 15, PrototypeStyle.Ink);
                    label.style.flexGrow = 1;
                    row.Add(label);
                    row.Add(PrototypeStyle.Text($"{price} 金币", 15, PrototypeStyle.Accent));
                    var line = PrototypeStyle.Row();
                    row.style.flexGrow = 1;
                    row.style.flexBasis = 0;
                    line.Add(row);
                    var minus = PrototypeStyle.Button("−", "sell-less-" + definition.Id, null);
                    var plus = PrototypeStyle.Button("+", "sell-more-" + definition.Id, null);
                    if (copies.Length > 1) { line.Add(minus); line.Add(plus); }
                    int quantity = 0;
                    row.clicked += () => Select(quantity == 0 ? 1 : 0);
                    minus.clicked += () => Select(quantity - 1);
                    plus.clicked += () => Select(quantity + 1);
                    Select(0);
                    stock.Add(line);

                    void Select(int next)
                    {
                        quantity = Mathf.Clamp(next, 0, copies.Length);
                        for (int i = 0; i < copies.Length; i++)
                            if (i < quantity) basket[copies[i].Id] = price;
                            else basket.Remove(copies[i].Id);
                        label.text = quantity == 0 ? $"{name}  ×{copies.Length}" : $"✓ {name}　{quantity}/{copies.Length}";
                        PrototypeStyle.Outline(row, quantity > 0 ? PrototypeStyle.Accent : PrototypeStyle.Edge, 3);
                        minus.SetEnabled(quantity > 0);
                        plus.SetEnabled(quantity < copies.Length);
                        confirm.SetEnabled(basket.Count > 0);
                        confirm.text = basket.Count == 0 ? "确认出售" : $"出售 {basket.Count} 件 · {basket.Values.Sum(v => (long)v)} 金币";
                    }
                }
                status.text = stock.contentContainer.childCount == 0
                    ? message + "　背包里没有可出售的物品。" : message;
            }

            void Sell()
            {
                var ids = basket.Keys.ToArray();
                int price;
                bool sold = adventure != null ? party.TrySellItems(ids, out price) :
                    party.TryTakeItemsForSale(ids, int.MaxValue - coins, out price);
                if (!sold) { RefreshStock("出售未完成，请重新选择物品。"); return; }
                if (adventure == null) coins += price;
                GameAudio.Play(SoundCue.ShopCheckout);
                celebrationUntil = Time.unscaledTimeAsDouble + CelebrationDuration;
                RefreshGold();
                RefreshStock($"已出售 {ids.Length} 件物品，成交 {price} 金币。");
            }
        }

        private void CloseSale()
        {
            saleWindow?.RemoveFromHierarchy();
            saleWindow = null;
        }
    }
}
