using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RaidPartyManager.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RaidPartyManager.Tests
{
    public sealed class DockShopTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void ResetSession() => typeof(BattlePrototype)
            .GetMethod("ResetPlaySession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        [UnityTest]
        public IEnumerator MenuShopSupportsExpressionsCancellationPurchaseAndSharedInventory()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Press(MenuRoot(), "open-battle");
            yield return WaitForScene("BattlePrototype");
            int startingItems = InventoryCount();
            var battleRoot = Object.FindAnyObjectByType<BattlePrototype>().GetComponent<UIDocument>().rootVisualElement;
            Press(battleRoot, "open-settings");
            Press(battleRoot, "return-main-menu");
            yield return WaitForScene("MainMenu");
            Press(MenuRoot(), "open-shop");
            yield return WaitForScene("DockShop");

            var shop = Object.FindAnyObjectByType<DockShop>();
            var root = shop.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  999"));
            var prices = root.Query<Label>("shop-item-price").ToList();
            Assert.That(prices.Count, Is.EqualTo(4));
            var offers = OfferViews(root);
            Assert.That(offers.Count(view => OfferTier(shop, view) == 1), Is.EqualTo(2));
            Assert.That(offers.Count(view => OfferTier(shop, view) >= 2), Is.EqualTo(2));
            Assert.That(root.Query<Label>().ToList().Any(label => Regex.IsMatch(label.text, @"T[0-3]")), Is.False);
            foreach (var view in offers)
            {
                var item = OfferItem(shop, view);
                int basePrice = (int)item.GetType().GetProperty("ShopPrice").GetValue(item);
                Assert.That(Price(view), Is.InRange((int)System.Math.Ceiling(basePrice * 0.7),
                    (int)System.Math.Ceiling(basePrice * 1.3)));
            }
            Assert.That(root.Query<Label>("shop-item-name").ToList().Select(label => label.text).Distinct().Count(),
                Is.EqualTo(4));
            Assert.That(root.Q("shop-offer-0").worldBound.center.y,
                Is.EqualTo(root.Q("shop-offer-1").worldBound.center.y).Within(1f));
            Assert.That(root.Q("shop-offer-2").worldBound.center.y,
                Is.GreaterThan(root.Q("shop-offer-0").worldBound.center.y));

            ConfigureOneOfEachLowerTier(shop);
            root = shop.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            var merchant = root.Q<Image>("shop-merchant");
            Assert.That(merchant.image, Is.Not.Null);
            Assert.That(merchant.sourceRect.x, Is.Zero);
            var high = OfferViews(root).First(view => OfferTier(shop, view) == 1);
            var medium = OfferViews(root).Single(view => OfferTier(shop, view) == 2);
            var low = OfferViews(root).Single(view => OfferTier(shop, view) == 3);
            int price = Price(high);
            Enter(high);
            Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width * 2).Within(0.1f));
            Enter(medium);
            Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width).Within(0.1f));
            Enter(low);
            Assert.That(merchant.sourceRect.x, Is.Zero);

            Down(high);
            Assert.That(root.Q("shop-drag-preview"), Is.Not.Null);
            Vector2 blank = root.worldBound.min + Vector2.Scale(root.worldBound.size, new Vector2(0.51f, 0.94f));
            Move(root, blank);
            Up(root, blank);
            Assert.That(root.Q("shop-drag-preview"), Is.Null);
            Assert.That(high.Q("shop-sold"), Is.Null);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  999"));
            Assert.That(merchant.sourceRect.x, Is.Zero);
            Assert.That(Price(high), Is.EqualTo(price), "Hovering and cancelling a drag must keep the displayed price.");

            Down(high);
            Vector2 counter = root.Q("shop-checkout").worldBound.center;
            Move(root, counter);
            Up(root, counter);
            Assert.That(high.Q("shop-sold"), Is.Not.Null);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo($"金币  {999 - price}"));
            Assert.That(InventoryCount(), Is.EqualTo(startingItems + 1));
            Assert.That(root.Q<Label>("shop-feedback").text, Does.Contain("共享背包"));
            Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width * 2).Within(0.1f));

            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(Mathf.Abs(root.Q("shop-merchant-stage").resolvedStyle.translate.y), Is.GreaterThan(0.05f));
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(root.Q("shop-merchant-stage").resolvedStyle.translate.y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(merchant.sourceRect.x, Is.Zero);

            Down(medium);
            Move(root, counter);
            Up(root, counter);
            Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width).Within(0.1f));
            Assert.That(InventoryCount(), Is.EqualTo(startingItems + 2));
            Press(root, "open-settings");
            Press(root, "return-main-menu");
            yield return WaitForScene("MainMenu");
            Press(MenuRoot(), "open-battle");
            yield return WaitForScene("BattlePrototype");
            Assert.That(InventoryCount(), Is.EqualTo(startingItems + 2));
        }

        [UnityTest]
        public IEnumerator HoverRepaintsMerchantFaceAndRestoresIdle()
        {
            yield return SceneManager.LoadSceneAsync("DockShop");
            yield return null;
            var shop = Object.FindAnyObjectByType<DockShop>();
            ConfigureOneOfEachLowerTier(shop);
            var document = shop.GetComponent<UIDocument>();
            var originalSettings = document.panelSettings;
            var settings = Object.Instantiate(originalSettings);
            var target = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            target.Create();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            document.panelSettings = settings;
            try
            {
                yield return new WaitForSecondsRealtime(0.15f);
                var root = document.rootVisualElement;
                var merchant = root.Q<Image>("shop-merchant");
                var bounds = merchant.worldBound;
                var face = new Rect(bounds.x + bounds.width * 0.25f,
                    bounds.y + bounds.height * 0.08f, bounds.width * 0.5f, bounds.height * 0.42f);
                var idle = ReadFace(target, face);
                var medium = OfferViews(root).Single(view => OfferTier(shop, view) == 2);
                var high = OfferViews(root).First(view => OfferTier(shop, view) == 1);
                Enter(medium);
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width).Within(0.1f));
                var happy = ReadFace(target, face);
                Assert.That(ChangedPixels(idle, happy), Is.GreaterThan(idle.Length / 50),
                    "Hover must repaint the merchant's face on screen.");
                Enter(high);
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(merchant.sourceRect.x, Is.EqualTo(merchant.sourceRect.width * 2).Within(0.1f));
                var delighted = ReadFace(target, face);
                Assert.That(ChangedPixels(happy, delighted), Is.GreaterThan(idle.Length / 50));
                Move(root, new Vector2(650, 690));
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(merchant.sourceRect.x, Is.Zero);
                Assert.That(ChangedPixels(idle, ReadFace(target, face)), Is.LessThan(idle.Length / 200));
            }
            finally
            {
                document.panelSettings = originalSettings;
                Object.Destroy(settings);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static Color32[] ReadFace(RenderTexture target, Rect face)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            int width = Mathf.RoundToInt(face.width);
            int height = Mathf.RoundToInt(face.height);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(Mathf.RoundToInt(face.x), Mathf.RoundToInt(target.height - face.yMax),
                width, height), 0, 0);
            image.Apply();
            var pixels = image.GetPixels32();
            RenderTexture.active = previous;
            Object.Destroy(image);
            return pixels;
        }

        private static int ChangedPixels(Color32[] first, Color32[] second) =>
            first.Where((pixel, index) => !pixel.Equals(second[index])).Count();

        [UnityTest]
        public IEnumerator ShopRejectsUnaffordableAndRepeatedPurchases()
        {
            yield return SceneManager.LoadSceneAsync("DockShop");
            yield return null;
            var shop = Object.FindAnyObjectByType<DockShop>();
            var root = shop.GetComponent<UIDocument>().rootVisualElement;
            var offer = root.Q("shop-offer-0");
            int price = Price(offer);
            Vector2 counter = root.Q("shop-checkout").worldBound.center;
            var funds = typeof(DockShop).GetField("coins", Private);
            funds.SetValue(shop, 0);
            typeof(DockShop).GetMethod("RefreshGold", Private).Invoke(shop, null);
            Down(offer);
            Move(root, counter);
            Up(root, counter);
            Assert.That(offer.Q("shop-sold"), Is.Null);
            Assert.That(root.Q<Label>("shop-feedback").text, Is.EqualTo("金币不足"));
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  0"));

            funds.SetValue(shop, 999);
            typeof(DockShop).GetMethod("RefreshGold", Private).Invoke(shop, null);
            Down(offer);
            Move(root, counter);
            Up(root, counter);
            int purchasedItems = InventoryCount();
            Down(offer);
            Up(root, counter);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo($"金币  {999 - price}"));
            Assert.That(InventoryCount(), Is.EqualTo(purchasedItems));
            Assert.That(root.Q("shop-drag-preview"), Is.Null);
        }

        private static VisualElement[] OfferViews(VisualElement root) =>
            Enumerable.Range(0, 4).Select(index => root.Q($"shop-offer-{index}")).ToArray();

        private static object OfferItem(DockShop shop, VisualElement view)
        {
            var offers = (IEnumerable)typeof(DockShop).GetField("offers", Private).GetValue(shop);
            var offer = offers.Cast<object>().Single(entry =>
                ReferenceEquals(entry.GetType().GetField("View").GetValue(entry), view));
            return offer.GetType().GetField("Item").GetValue(offer);
        }

        private static int OfferTier(DockShop shop, VisualElement view)
        {
            var item = OfferItem(shop, view);
            return (int)item.GetType().GetProperty("ShopTier").GetValue(item);
        }

        private static int Price(VisualElement view) =>
            int.Parse(Regex.Match(view.Q<Label>("shop-item-price").text, @"\d+").Value);

        private static void ConfigureOneOfEachLowerTier(DockShop shop)
        {
            var field = typeof(DockShop).GetField("stockPool", Private);
            var pool = (System.Array)field.GetValue(shop);
            var assets = pool.Cast<Object>().ToArray();
            int Tier(Object asset) => (int)asset.GetType().GetProperty("ShopTier").GetValue(asset);
            var selected = assets.Where(asset => Tier(asset) == 1).Take(2)
                .Concat(assets.Where(asset => Tier(asset) == 2).Take(1))
                .Concat(assets.Where(asset => Tier(asset) == 3).Take(1)).ToArray();
            var fixture = System.Array.CreateInstance(pool.GetType().GetElementType(), selected.Length);
            for (int i = 0; i < selected.Length; i++)
                fixture.SetValue(selected[i], i);
            shop.enabled = false;
            field.SetValue(shop, fixture);
            shop.enabled = true;
        }

        private static int InventoryCount()
        {
            var run = typeof(BattlePrototype).GetField("playModeRun", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var inventory = (IEnumerable)run.GetType().GetProperty("Inventory").GetValue(run);
            return inventory.Cast<object>().Count();
        }

        private static VisualElement MenuRoot() =>
            Object.FindAnyObjectByType<PrototypeSceneMenu>().GetComponent<UIDocument>().rootVisualElement;

        private static IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
        }

        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            using var evt = NavigationSubmitEvent.GetPooled();
            Send(button, evt);
        }

        private static Event Mouse(EventType type, Vector2 point) =>
            new Event { type = type, mousePosition = point, button = 0, clickCount = 1 };

        private static void Enter(VisualElement element)
        {
            using var evt = PointerEnterEvent.GetPooled(Mouse(EventType.MouseMove, element.worldBound.center));
            Send(element, evt);
        }

        private static void Down(VisualElement element)
        {
            using var evt = PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, element.worldBound.center));
            Send(element, evt);
        }

        private static void Move(VisualElement root, Vector2 point)
        {
            using var evt = PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, point));
            Send(root, evt);
        }

        private static void Up(VisualElement root, Vector2 point)
        {
            using var evt = PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, point));
            Send(root, evt);
        }

        private static void Send(VisualElement element, EventBase evt)
        {
            evt.target = element;
            element.SendEvent(evt);
        }
    }
}
