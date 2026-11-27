using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class ShopStage
    {
        private const float PlankDepth = .105f, RopeThickness = .021f;
        // The way back to town is the same square sign the talent market and camp hang, at the same size on screen.
        private const float TownSignWorldHeight = .47f;
        private static readonly float TownSignWorldWidth = TownSignWorldHeight * HangingSign.TownSignWidth / HangingSign.TownSignHeight;
        // Only sets the depth of the flown board; its place and size on screen follow the panel sign. It hangs just
        // downstage of the proscenium so its rings show over the header, as the panel sign's do in the other rooms.
        private static readonly Vector3 TownSignRest = new(0, 3.2f, -10.1f);
        private readonly List<FlownSign> townSigns = new();
        private readonly List<HangingSign> panelSigns = new();

        /// <summary>
        /// The return-to-town sign hangs over the playing area, so it is built into the scenery and leaves with the
        /// camera. Other signs, such as the standalone exit arrow, stay the panel's own hanging signs as in every other scene.
        /// </summary>
        internal void MountShopSigns(VisualElement root)
        {
            foreach (var target in root.Query<HangingSign>().ToList())
            {
                var artwork = target.Q<Image>("route-node-icon")?.image as Texture2D;
                if (artwork == null)
                {
                    panelSigns.Add(target);
                    continue;
                }
                var sign = HangingBoard("Shop route sign " + target.name, "Carved plank", SignFace.Square,
                    TownSignWorldWidth, TownSignWorldHeight, PlankDepth, "Shop sign braided line", RopeThickness);
                // The emblem keeps the panel sign's proportion: 44 of its 58 pixels, just proud of the carved panel.
                float emblem = TownSignWorldHeight * 44f / HangingSign.TownSignHeight;
                sign.Paint(Part(Flat("Route emblem", Vector3.zero, emblem, emblem, artwork), sign.Board,
                    new Vector3(0, 0, -PlankDepth / 2 - .009f)), Color.white);
                foreach (var (part, _) in sign.Painted) Dressing.Flown.Add(part);
                foreach (var rope in sign.Ropes) Dressing.Flown.Add(rope);
                sign.Target = target;
                target.MountInScenery();
                target.RetractInScenery = chosen => sign.Retract(chosen);
                townSigns.Add(sign);
            }
            PoseShopSigns(root);
        }

        internal void PoseShopSigns(VisualElement root)
        {
            var frame = root.worldBound;
            if (!(frame.height > 0 && frame.width > 0)) return;
            bool wide = RecruitmentFocus < .44f && counterFocus < .44f;
            foreach (var sign in panelSigns)
                sign.style.visibility = wide ? Visibility.Visible : Visibility.Hidden;
            foreach (var sign in townSigns)
            {
                var target = sign.Target;
                if (target.parent == null) continue;
                sign.Board.gameObject.SetActive(wide);
                target.style.visibility = wide ? Visibility.Visible : Visibility.Hidden;
                float depth = camera.WorldToViewportPoint(TownSignRest).z;
                float centreY = RunTopBar.Height + HangingSign.ExitTopGap + HangingSign.TownSignHeight * .5f;
                var rest = camera.ViewportToWorldPoint(new Vector3(.5f, 1 - centreY / frame.height, depth));
                // Size the board so it covers exactly the panel sign's pixels.
                float perMetre = (camera.WorldToViewportPoint(rest + Vector3.up).y - camera.WorldToViewportPoint(rest).y) * frame.height;
                float scale = HangingSign.TownSignHeight / (TownSignWorldHeight * perMetre);
                sign.Board.localScale = Vector3.one * scale;
                // The rigging descends from above the shot, leaving the overhead scenery clear.
                var aboveFrame = camera.WorldToViewportPoint(rest);
                aboveFrame.y = 1.03f;
                float hangerY = Mathf.Max(rest.y + .5f, camera.ViewportToWorldPoint(aboveFrame).y);
                sign.RopeThickness = RopeThickness * scale;
                sign.Pose(this, rest, Quaternion.identity, hangerY);

                Vector2 a = Project(rest + scale * new Vector3(-TownSignWorldWidth * .5f, TownSignWorldHeight * .5f, -.07f));
                Vector2 b = Project(rest + scale * new Vector3(TownSignWorldWidth * .5f, -TownSignWorldHeight * .5f, -.07f));
                Vector2 local = target.parent.WorldToLocal(a);
                target.style.left = local.x;
                target.style.right = StyleKeyword.Auto;
                target.style.top = local.y;
                target.style.width = Mathf.Max(0, b.x - a.x);
                target.style.height = Mathf.Max(0, b.y - a.y);
            }

            Vector2 Project(Vector3 point)
            {
                var view = camera.WorldToViewportPoint(point);
                return frame.min + Vector2.Scale(frame.size, new Vector2(view.x, 1 - view.y));
            }
        }
    }
}
