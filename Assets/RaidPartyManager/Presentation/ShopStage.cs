using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class ShopStage : ServiceStage
    {
        internal enum Expression { Idle, Happy, Delighted, Laugh }

        // Addresses on the same fly bar as the moon hardware, from stage left to stage right.
        private static readonly float[] Hangers = { -4.35f, -2.06f, 2.06f, 4.35f };
        private static readonly int[] Idle = { 0, 0, 1, 2, 3, 3, 2, 1 };
        private const float ItemSize = .83f;
        private readonly MeshRenderer merchant;
        private readonly Texture2D reactions;
        private readonly Suspension[] lines = new Suspension[4];
        private readonly Vector3 merchantCentre = new(0, 1.02f, -1.65f);
        private int lastCel = -1;
        private Expression lastExpression;

        private sealed class Suspension
        {
            public Transform eye, rope, knot, bob;
            public Vector3 centre, releasedOffset, grabOffset;
            public bool held, cut;
            public float releasedAt = float.NegativeInfinity;
        }

        public ShopStage(Transform owner, VisualElement panel, ServiceStageArt art, Texture2D interior, Texture2D reactions) :
            base(owner, panel, art)
        {
            this.reactions = reactions;
            HangBackdrop("Harbour chandlery", interior);
            Deck(art.timber, DockPlayhouse.PlankTile, new Color(.76f, .77f, .79f));
            FloorProp("Cloth deliveries",art.parcel,4.62f,-.95f,1.48f,-8);
            FloorProp("Creel by the loading bay",art.creel,-3.48f,2.62f,1.23f,9);
            merchant = Flat("shop-merchant", merchantCentre, 2.04f, 2.04f, art.residents[1]);
            Dressing.Cast[merchant] = new DockSet.Player(true);
            SetMerchant(Expression.Idle, 0, 0);
            var wood = new Color(.82f, .73f, .62f);
            var brass = new Color(.76f, .52f, .26f);
            Shadow(new Vector3(0, 0, -2.58f), 3.85f, 1.55f);
            Box("Counter carcass", new Vector3(0, .45f, -2.60f), new Vector3(3.22f, .90f, .86f), art.timber, wood);
            Flat("Counter joinery", new Vector3(0, .45f, -3.038f), 3.18f, .86f, art.counterFront);
            Box("Counter plinth", new Vector3(0, .07f, -2.60f), new Vector3(3.31f, .14f, .93f), art.timber, wood);
            Box("Counter top", new Vector3(0, .96f, -2.60f), new Vector3(3.42f, .12f, 1.03f), art.timber, wood);
            Box("Counter brass inlay", new Vector3(0, .94f, -3.12f), new Vector3(3.30f, .029f, .02f), null, brass);
            Box("Change tray", new Vector3(.97f, 1.035f, -2.64f), new Vector3(.38f, .03f, .27f), null, brass);
            foreach (var renderer in stage.GetComponentsInChildren<MeshRenderer>())
                Dressing.Hinges[renderer] = new Vector2(0,-2.6f);
            for (int i = 0; i < lines.Length; i++)
            {
                var rope = Shape("Hemp line " + i, PrimitiveType.Cylinder, Vector3.zero, Vector3.one,
                    art.bark, new Color(.95f,.78f,.52f), Surface.Solid, new Vector2(2,4)).transform;
                var eye = Box("Fly bar clamp " + i, Vector3.zero, new Vector3(.15f,.14f,.20f), null, brass);
                var knot = Shape("Line end " + i, PrimitiveType.Sphere, Vector3.zero, new Vector3(.082f,.105f,.08f), null, brass, Surface.Solid);
                Dressing.Flown.Add(eye); Dressing.Flown.Add(knot); Dressing.Flown.Add(rope.GetComponent<MeshRenderer>());
                lines[i] = new Suspension { rope = rope, eye = eye.transform, knot = knot.transform };
            }
        }

        public void Hang(int i, EquipmentDefinition item, Texture2D icon, bool sold)
        {
            var line = lines[i];
            if (line.bob != null)
            {
                line.bob.gameObject.SetActive(false);
                Object.Destroy(line.bob.gameObject);
            }
            line.held = false;
            line.releasedAt = float.NegativeInfinity;
            line.releasedOffset = Vector3.zero;
            bool book = item is SkillBookDefinition;
            var artwork = book ? Resources.Load<Texture2D>("SkillBookScroll") : icon;
            line.bob = Flat("Hanging stock " + i, Vector3.zero, ItemSize, ItemSize, artwork).transform;
            Dressing.Flown.Add(line.bob.GetComponent<MeshRenderer>());
            if (book && icon != null)
            {
                var seal = Flat("Scroll emblem", Vector3.zero, ItemSize * .61f, ItemSize * .61f, icon);
                Dressing.Flown.Add(seal);
                seal.transform.SetParent(line.bob, true);
                seal.transform.localPosition = new Vector3(0, 0, -.009f);
            }
            line.cut = sold;
            line.bob.gameObject.SetActive(!sold);
            Pose(i, Time.unscaledTime);
        }

        public void SetMerchant(Expression expression, float time, float celebration)
        {
            int frame = expression == Expression.Idle ? Idle[Mathf.FloorToInt(time * 3.6f) % Idle.Length] : (int)expression - 1;
            if (lastCel != frame || lastExpression != expression)
            {
                SetFrame(merchant, expression == Expression.Idle ? art.residents[1] : reactions,
                    expression == Expression.Idle ? 4 : 3, frame);
                lastCel = frame;
                lastExpression = expression;
            }
            float hop = Mathf.Abs(Mathf.Sin((1 - celebration) * Mathf.PI * 3)) * .19f * celebration;
            merchant.transform.position = merchantCentre + Vector3.up * hop;
            merchant.transform.localRotation = Quaternion.Euler(0, 0, expression == Expression.Idle ? 0 :
                Mathf.Sin(time * (expression == Expression.Laugh ? 18 : 4)) * (expression == Expression.Laugh ? 3 : .9f));
        }

        public void Pull(int i, Vector2 viewport)
        {
            var line = lines[i];
            if (line.cut) return;
            float z = Mathf.Min(-3.6f, line.eye.position.z - .65f);
            var pointer = camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, z - camera.transform.position.z));
            if (!line.held)
            {
                // Preserve the point the player grabbed, even when they grab the price tag or a rebounding item.
                var projected = camera.WorldToViewportPoint(line.centre);
                line.grabOffset = camera.ViewportToWorldPoint(new Vector3(projected.x, projected.y,
                    z - camera.transform.position.z)) - pointer;
                line.held = true;
            }
            line.centre = pointer + line.grabOffset;
            Pose(i, Time.unscaledTime);
        }

        public void Release(int i, bool bought)
        {
            var line = lines[i];
            if (line.cut) return;
            line.held = false;
            line.cut = bought;
            line.releasedAt = Time.unscaledTime;
            line.releasedOffset = bought ? line.knot.position - CutTip(line.eye.position) : line.centre - Rest(i);
            if (bought) line.bob.gameObject.SetActive(false);
            Pose(i, line.releasedAt);
        }

        private Vector3 Rest(int i)
        {
            var anchor = lines[i].eye.position;
            return new Vector3(anchor.x, i == 0 || i == 3 ? 1.86f : 2.40f, anchor.z);
        }

        private static Vector3 CutTip(Vector3 anchor) => anchor + Vector3.down * .27f;

        public void Pose(int i, float time)
        {
            var line = lines[i];
            var eye = house.FlyBarEye(Hangers[i]);
            line.eye.position = eye;
            float elapsed = time - line.releasedAt;
            float spring = elapsed < 1.55f ? Mathf.Exp(-3.8f * elapsed) * Mathf.Cos(15f * elapsed) : 0;
            Vector3 tip;
            if (line.cut)
            {
                // The loose end recoils toward the block, with a short dangling tail for sold stock.
                float recoil = elapsed < .65f ? Mathf.Exp(-8f * elapsed) * Mathf.Cos(13f * elapsed) : 0;
                tip = CutTip(eye) + line.releasedOffset * recoil;
                tip.y = Mathf.Min(tip.y, eye.y - .06f);
            }
            else
            {
                if (!line.held)
                    line.centre = Rest(i) + line.releasedOffset * spring;
                float angle = Mathf.Clamp((line.centre.x - eye.x) * 13f, -38f, 38f);
                line.bob.SetPositionAndRotation(line.centre, Quaternion.Euler(0, 0, angle));
                tip = line.centre + line.bob.up * (ItemSize * .43f);
            }
            line.knot.position = tip;
            line.rope.position = (tip + eye) * .5f;
            line.rope.rotation = Quaternion.FromToRotation(Vector3.up, eye - tip);
            line.rope.localScale = new Vector3(.034f, Vector3.Distance(eye, tip) * .5f, .034f);
        }

        public void PlaceOffer(VisualElement view, Label price, int i)
        {
            var line = lines[i];
            // Tie the tag a short distance along the actual cord, including while it is pulled sideways.
            Vector3 knot = line.knot.position;
            var tag = knot + (line.eye.position - knot).normalized * .29f;
            var centre = line.centre;
            var a = camera.WorldToViewportPoint(centre + new Vector3(-.49f,.49f,0));
            var b = camera.WorldToViewportPoint(centre + new Vector3(.49f,-.49f,0));
            var eye = camera.WorldToViewportPoint(tag);
            var halfTag = camera.WorldToViewportPoint(tag + new Vector3(.43f,.15f,0)) - eye;
            float left = Mathf.Min(a.x, eye.x - halfTag.x), right = Mathf.Max(b.x, eye.x + halfTag.x);
            float top = Mathf.Max(a.y, eye.y + halfTag.y), bottom = Mathf.Min(b.y, eye.y - halfTag.y);
            view.style.position = Position.Absolute;
            view.style.left = Length.Percent(left * 100);
            view.style.top = Length.Percent((1 - top) * 100);
            view.style.width = Length.Percent((right - left) * 100);
            view.style.height = Length.Percent((top - bottom) * 100);
            price.style.left = Length.Percent((eye.x - left) / (right - left) * 100);
            price.style.top = Length.Percent((top - eye.y) / (top - bottom) * 100);
        }

        public void PlaceCounter(VisualElement target) =>
            Pin(target, new Vector3(-1.71f, 1.04f, -2.08f), new Vector3(1.71f, .04f, -3.13f));
    }
}
