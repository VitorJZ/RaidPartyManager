using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
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
        public IEnumerator SellingEquipmentAndBooksAtTheCounterPaysTheDisplayedPriceOnce()
        {
            yield return SceneManager.LoadSceneAsync("DockShop");
            yield return null;
            var shop = Object.FindAnyObjectByType<DockShop>();
            var root = shop.GetComponent<UIDocument>().rootVisualElement;
            var party = (PartyRun)typeof(DockShop).GetField("party", Private).GetValue(shop);
            var content = (EncounterData)typeof(DockShop).GetField("content", Private).GetValue(shop);
            var crown = party.AddEquipment(content.StartingEquipment.First(item => item.Effect == EquipmentEffect.FacelessCrown));
            party.AddEquipment(crown.Definition);
            var book = party.AddEquipment(content.SkillBooks.First(item => item.BossExclusive));
            int count = party.BackpackItems.Count;
            Press(root, "shop-counter");
            yield return WaitForCounter(root);
            Press(root, "shop-sell");
            string crownId = crown.Definition.Id;
            string bookId = book.Definition.Id;
            Press(root, "sell-item-" + crownId);
            Press(root, "sell-more-" + crownId);
            Press(root, "sell-item-" + bookId);
            var checkout = root.Q<Button>("shop-confirm-sale");
            Assert.That(checkout.text, Does.Contain("3 件").And.Contain("300 金币"));
            Press(root, "sell-less-" + crownId);
            Press(root, "sell-item-" + bookId);
            Assert.That(checkout.text, Does.Contain("1 件").And.Contain("100 金币"));
            Assert.That(party.BackpackItems.Count, Is.EqualTo(count));
            Press(root, "sell-more-" + crownId);
            Press(root, "sell-item-" + bookId);
            Press(root, "shop-confirm-sale");
            Assert.That(root.Q("sell-item-" + bookId), Is.Null);
            Assert.That(root.Q("sell-item-" + crownId), Is.Null);
            Assert.That(checkout.enabledSelf, Is.False);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  1299"));
            Assert.That(party.BackpackItems.Count, Is.EqualTo(count - 3));
            Press(root, "shop-close-sale");
            yield return WaitForCounter(root);
            Assert.That(root.Q("shop-sale-window"), Is.Null);
        }

        [UnityTest]
        public IEnumerator ShopSupportsExpressionsCancellationPurchaseAndSharedInventory()
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
            yield return SceneManager.LoadSceneAsync("DockShop");
            yield return null;

            var shop = Object.FindAnyObjectByType<DockShop>();
            var root = shop.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q("shop-recruit-open"), Is.Null);
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
                int basePrice = AdventureShopOffer.BasePrice(item);
                Assert.That(Price(view), Is.InRange((int)System.Math.Ceiling(basePrice * 0.7),
                    (int)System.Math.Ceiling(basePrice * 1.3)));
            }
            Assert.That(root.Q("shop-item-name"), Is.Null);
            Assert.That(offers.Select(view => OfferItem(shop, view).Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(offers.Select(v => v.worldBound.center.x), Is.Ordered);
            var stage = shop.transform.Find("Service theatre");
            Assert.That(stage.Find("Recruitment wheel fixture"), Is.Null);
            Assert.That(stage.Find("shop-merchant").position.x, Is.Zero.Within(.01f));
            Assert.That(stage.Find("Cabinet back"), Is.Null);
            Assert.That(stage.Find("Shop roof beam"), Is.Null);
            Assert.That(stage.Find("Shop roof beam lower edge"), Is.Null);
            Assert.That(stage.Find("Roof knee brace"), Is.Null);
            var rope = stage.Find("Shop sign braided line");
            Assert.That(rope, Is.Not.Null);
            Assert.That(Camera.main.WorldToViewportPoint(rope.TransformPoint(Vector3.up)).y, Is.GreaterThan(1f));

            ConfigureLowerTierStock(shop);
            root = shop.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            var merchant = shop.transform.Find("Service theatre/shop-merchant").GetComponent<MeshRenderer>();
            float restY = merchant.transform.position.y;
            var wideEye = Camera.main.transform.position;
            AssertPriceSigns(root, stage);
            Press(root, "shop-counter");
            yield return WaitForCounter(root);
            Assert.That(Camera.main.transform.position.z, Is.GreaterThan(wideEye.z + 3));
            AssertPriceSigns(root, stage);
            var sheet = Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle");
            var faces = (Texture2D)typeof(DockShop).GetField("merchantReactions", Private).GetValue(shop);
            Assert.That(sheet, Is.Not.Null);
            Assert.That(faces, Is.Not.Null);
            Assert.That(faces, Is.Not.SameAs(sheet));
            Move(root, root.worldBound.min + Vector2.one * 2);
            AssertFace(merchant, sheet);
            var high = OfferViews(root).First(view => OfferTier(shop, view) == 1);
            var medium = OfferViews(root).Single(view => OfferTier(shop, view) == 2);
            var low = OfferViews(root).Single(view => OfferTier(shop, view) == 3);
            int price = Price(high);
            Enter(high);
            AssertFace(merchant, faces, 1);
            Enter(medium);
            AssertFace(merchant, faces, 0);
            Enter(low);
            AssertFace(merchant, faces, 0);

            var display = stage.Find("Table stock " + high.name.Split('-').Last());
            var counterTop = stage.Find("Counter top").GetComponent<MeshRenderer>();
            Assert.That(display.position.y, Is.EqualTo(counterTop.bounds.max.y).Within(.01f));
            Assert.That(counterTop.bounds.min.x, Is.LessThan(display.position.x));
            Assert.That(counterTop.bounds.max.x, Is.GreaterThan(display.position.x));
            Assert.That(stage.Find("Hemp line 0"), Is.Null);
            var centre = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, display.Find("Stock artwork").position, Camera.main);
            Assert.That(root.panel.Pick(centre), Is.SameAs(high));

            Down(high);
            Assert.That(high.Q("shop-hold-progress").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Up(root, high.worldBound.center);
            yield return new WaitForSecondsRealtime(.62f);
            Assert.That(display.gameObject.activeSelf, Is.True);

            Down(high);
            Vector2 blank = root.worldBound.min + Vector2.Scale(root.worldBound.size, new Vector2(.87f, .81f));
            Move(root, blank);
            yield return new WaitForSecondsRealtime(.62f);
            Up(root, blank);
            Assert.That(display.gameObject.activeSelf, Is.True);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  999"));
            Assert.That(Price(high), Is.EqualTo(price));

            yield return HoldToBuy(root, high);
            Assert.That(display.gameObject.activeSelf, Is.False);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo($"金币  {999 - price}"));
            Assert.That(InventoryCount(), Is.EqualTo(startingItems + 1));
            Assert.That(root.Q<Label>("shop-feedback").text, Does.Contain("共享背包"));
            AssertFace(merchant, faces, 0);
            Enter(medium);
            AssertFace(merchant, faces, 0);

            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(Mathf.Abs(merchant.transform.position.y-restY), Is.GreaterThan(0.004f));
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(merchant.transform.position.y, Is.EqualTo(restY).Within(0.01f));
            AssertFace(merchant, faces, 0);

            var background = root.Q("shop-counter-background");
            Assert.That(root.panel.Pick(blank), Is.SameAs(background));
            using (var click = PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, blank)))
                Send(background, click);
            Up(root, blank);
            yield return new WaitForSecondsRealtime(.54f);
            Assert.That(Vector3.Distance(Camera.main.transform.position, wideEye), Is.LessThan(.01f));
            Assert.That(root.Q("shop-counter-hint").style.display.value, Is.EqualTo(DisplayStyle.None));
            AssertPriceSigns(root, stage);

            Press(root, "shop-counter");
            yield return WaitForCounter(root);
            yield return HoldToBuy(root, low);
            AssertFace(merchant, faces, 0);
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
            ConfigureLowerTierStock(shop, "MurkyWater", "Bludgeon");
            yield return null;
            var root = shop.GetComponent<UIDocument>().rootVisualElement;
            float until = Time.realtimeSinceStartup + 4;
            while (root.Q("curtain-input-shield") != null && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(root.Q("curtain-input-shield"), Is.Null);
            Press(root, "shop-counter");
            yield return WaitForCounter(root);
            var merchant = shop.transform.Find("Service theatre/shop-merchant").GetComponent<MeshRenderer>();
            var camera = Camera.main;
            var target = new RenderTexture(1280,720,24);
            target.Create();
            var sheet = Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle");
            var faces = (Texture2D)typeof(DockShop).GetField("merchantReactions", Private).GetValue(shop);
            // Compare the actual rendered face from a fixed camera position, independent of pointer parallax.
            var originalTarget = camera.targetTexture;
            camera.targetTexture = target;
            yield return null;
            var pose = (camera.transform.position,camera.transform.rotation);
            Color32[] FacePixels()
            {
                var pos = camera.transform.position;
                var rot = camera.transform.rotation;
                try
                {
                    camera.transform.SetPositionAndRotation(pose.position,pose.rotation);
                    camera.Render();
                    var uv = camera.WorldToViewportPoint(merchant.bounds.center+Vector3.up*.66f);
                    return ReadFace(target,new Rect(uv.x*target.width-65,(1-uv.y)*target.height-55,130,110));
                }
                finally
                {
                    camera.transform.SetPositionAndRotation(pos,rot);
                }
            }
            try
            {
                Move(root, root.worldBound.min + Vector2.one * 2);
                AssertFace(merchant, sheet);
                var idle = FacePixels();
                var high = OfferViews(root).First(view => OfferTier(shop, view) == 1);
                Color32[] happy = null;
                foreach (string item in new[] { "浑浊的水", "打狗棒" })
                {
                    var offer = OfferViews(root).Single(view => OfferItem(shop, view).Name == item);
                    Move(root, offer.worldBound.center);
                    Assert.That(root.Q<Label>("equipment-tooltip-name").text, Does.Contain(item));
                    AssertFace(merchant, faces, 0);
                    happy = FacePixels();
                    Assert.That(ChangedPixels(idle, happy), Is.GreaterThan(idle.Length / 50), item);
                    Move(root, root.worldBound.min + Vector2.one*2);
                    Assert.That(ChangedPixels(idle, FacePixels()), Is.LessThan(idle.Length / 200), item);
                }
                Enter(high);
                AssertFace(merchant, faces, 1);
                Assert.That(ChangedPixels(happy, FacePixels()), Is.GreaterThan(idle.Length / 50));
            }
            finally
            {
                camera.targetTexture = originalTarget;
                target.Release();
                Object.Destroy(target);
            }
        }

        private static void AssertFace(MeshRenderer merchant, Texture sheet, int frame = -1)
        {
            var paint = new MaterialPropertyBlock();
            merchant.GetPropertyBlock(paint);
            Assert.That(paint.GetTexture("_MainTex"), Is.SameAs(sheet));
            var uv = paint.GetVector("_MainTex_ST");
            if (frame < 0)
            {
                Assert.That(sheet.name, Is.EqualTo("BrokerIdle"));
                Assert.That(uv.x, Is.EqualTo(.25f).Within(.001f));
                Assert.That(uv.y, Is.EqualTo(330f / 2048f).Within(.001f));
            }
            else
            {
                Assert.That(sheet.name, Is.EqualTo("BrokerPortraits"));
                Assert.That(uv.x, Is.EqualTo(.125f));
                Assert.That(uv.y, Is.EqualTo(.125f));
                int row = 7 - Mathf.RoundToInt(uv.w * 8);
                Assert.That(row / 2, Is.EqualTo(frame));
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
            Down(offer);
            yield return new WaitForSecondsRealtime(.62f);
            Up(root, offer.worldBound.center);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  999"));
            Press(root, "shop-counter");
            yield return WaitForCounter(root);
            var funds = typeof(DockShop).GetField("coins", Private);
            funds.SetValue(shop, 0);
            typeof(DockShop).GetMethod("RefreshGold", Private).Invoke(shop, null);
            yield return HoldToBuy(root, offer);
            Assert.That(shop.transform.Find("Service theatre/Table stock 0").gameObject.activeSelf, Is.True);
            Assert.That(root.Q<Label>("shop-feedback").text, Is.EqualTo("金币不足"));
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo("金币  0"));
            AssertFace(shop.transform.Find("Service theatre/shop-merchant").GetComponent<MeshRenderer>(),
                Resources.Load<Texture2D>("StoryEvents/Cast/BrokerIdle"));

            funds.SetValue(shop, 999);
            typeof(DockShop).GetMethod("RefreshGold", Private).Invoke(shop, null);
            yield return HoldToBuy(root, offer);
            int purchasedItems = InventoryCount();
            yield return HoldToBuy(root, offer);
            Assert.That(root.Q<Label>("shop-gold").text, Is.EqualTo($"金币  {999 - price}"));
            Assert.That(InventoryCount(), Is.EqualTo(purchasedItems));
            Assert.That(shop.transform.Find("Service theatre/Table stock 0").gameObject.activeSelf, Is.False);
        }

        private static void AssertPriceSigns(VisualElement root, Transform stage)
        {
            var camera = Camera.main;
            for (int i = 0; i < 4; i++)
            {
                var display = stage.Find("Table stock " + i);
                if (!display.gameObject.activeSelf) continue;
                var paper = display.Find("Counter price paper");
                var board = display.Find("Counter price tablet");
                Assert.That(paper.localScale.x, Is.EqualTo(board.localScale.x));
                Assert.That(paper.localScale.y, Is.EqualTo(board.localScale.y));
                var price = root.Q($"shop-offer-{i}").Q<Label>("shop-item-price");
                Vector2 Project(Vector3 point) =>
                    RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, paper.TransformPoint(point), camera);
                var centre = Project(Vector3.zero);
                Assert.That(Vector2.Distance(price.worldBound.center, centre), Is.LessThan(3f));
                Assert.That(price.worldBound.width, Is.EqualTo(Project(Vector3.right * .5f).x -
                    Project(Vector3.left * .5f).x).Within(3f));
                Assert.That(price.worldBound.height, Is.EqualTo(Project(Vector3.down * .5f).y -
                    Project(Vector3.up * .5f).y).Within(3f));
                Assert.That(price.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleCenter));
            }
        }

        private static IEnumerator WaitForCounter(VisualElement root)
        {
            var hint = root.Q("shop-counter-hint");
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((hint.resolvedStyle.display == DisplayStyle.None || root.Q("curtain-input-shield") != null) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(hint.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q("curtain-input-shield"), Is.Null);
            yield return null;
        }

        private static IEnumerator HoldToBuy(VisualElement root, VisualElement offer)
        {
            Vector2 start = offer.worldBound.center;
            Down(offer);
            yield return new WaitForSecondsRealtime(.65f);
            Up(root, start);
        }

        private static VisualElement[] OfferViews(VisualElement root) =>
            Enumerable.Range(0, 4).Select(index => root.Q($"shop-offer-{index}")).ToArray();

        private static EquipmentDefinition OfferItem(DockShop shop, VisualElement view)
        {
            var offers = (IEnumerable)typeof(DockShop).GetField("offers", Private).GetValue(shop);
            var offer = offers.Cast<object>().Single(entry =>
                ReferenceEquals(entry.GetType().GetField("View").GetValue(entry), view));
            return (EquipmentDefinition)offer.GetType().GetField("Item").GetValue(offer);
        }

        private static int OfferTier(DockShop shop, VisualElement view)
        {
            var item = OfferItem(shop, view);
            return item.Tier;
        }

        private static int Price(VisualElement view) =>
            int.Parse(Regex.Match(view.Q<Label>("shop-item-price").text, @"\d+").Value);

        private static void ConfigureLowerTierStock(DockShop shop, params string[] assetNames)
        {
            var assets = (EquipmentAsset[])typeof(DockShop).GetField("stockPool", Private).GetValue(shop);
            var gear = assets.Where(a => a != null && a.ShopPrice > 0).ToArray();
            var lower = assetNames.Length == 0 ?
                new[] { gear.First(a => a.ShopTier == 2), gear.First(a => a.ShopTier == 3) } :
                assetNames.Select(name => gear.Single(a => a.name == name)).ToArray();
            var stock = AdventureShopOffer.Draw(gear.Where(a => a.ShopTier == 1).Take(2).Concat(lower)
                .Select(a => a.ToDefinition()), new System.Random(9), false);
            typeof(DockShop).GetMethod("DisplayStock", Private).Invoke(shop, new object[] { stock });
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
