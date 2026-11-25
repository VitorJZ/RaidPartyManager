using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class MainMenuMarch : VisualElement
    {
        private const float RoadLevel = .824f;
        private const double MarchSpeed = .70;
        private readonly Texture2D backdrop;
        private readonly VisualElement strip = new() { name = "menu-promenade-strip", pickingMode = PickingMode.Ignore };
        private readonly List<Image> tiles = new();
        private readonly List<Walker> walkers = new();
        private readonly double started = Time.unscaledTimeAsDouble;
        private float tileWidth;

        private sealed class Walker
        {
            public VisualElement Figure;
            public VisualElement Shadow;
            public Image Sprite;
            public EntranceWalkPose Pose;
            public Texture2D WalkSheet;
            public float SoleInset;
            public int Frame = -1;
        }

        internal MainMenuMarch(Texture2D backdrop, EncounterData content, IEnumerable<string> characterIds)
        {
            this.backdrop = backdrop;
            name = "menu-march";
            pickingMode = PickingMode.Ignore;
            PrototypeStyle.Fill(this);
            style.overflow = Overflow.Hidden;
            strip.style.position = Position.Absolute;
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.height = Length.Percent(100);
            Add(strip);

            if (content != null)
                foreach (string id in characterIds) AddWalker(content, id);
            RegisterCallback<GeometryChangedEvent>(_ => LayoutMarch());
            schedule.Execute(Animate).Every(16);
        }

        private void AddWalker(EncounterData content, string id)
        {
            var character = content.CharacterCatalog.FirstOrDefault(definition => definition.Id == id);
            if (character == null) return;
            var walk = content.BattleWalkFor(id);
            var idle = content.BattleSpriteFor(id);
            Image sprite = null;
            EntranceWalkPose pose = null;
            VisualElement artwork;
            if (walk == null && idle != null)
                artwork = pose = new EntranceWalkPose(idle, character.Profession, facesLeft: false, stage: null);
            else
            {
                var texture = walk ?? content.PortraitFor(id);
                if (texture == null) return;
                sprite = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                if (walk != null)
                    sprite.sourceRect = AnimationCels.RectFor(walk, 0);
                artwork = sprite;
            }
            var figure = new VisualElement { name = "menu-walker-" + id, pickingMode = PickingMode.Ignore };
            figure.style.position = Position.Absolute;
            PrototypeStyle.Fill(artwork);
            figure.Add(artwork);
            var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.style.position = Position.Absolute;
            shadow.style.backgroundColor = new Color(.035f, .045f, .06f, .58f);
            shadow.style.borderTopLeftRadius = shadow.style.borderTopRightRadius =
                shadow.style.borderBottomLeftRadius = shadow.style.borderBottomRightRadius = Length.Percent(50);
            Add(shadow);
            Add(figure);
            walkers.Add(new Walker {
                Figure = figure, Shadow = shadow, Sprite = sprite, Pose = pose, WalkSheet = walk,
                SoleInset = ShopStage.FramingFor(character.RosterNumber).sideInset
            });
        }

        private void LayoutMarch()
        {
            float width = layout.width, height = layout.height;
            if (!(width > 0 && height > 0)) return;
            if (backdrop != null)
            {
                tileWidth = height * backdrop.width / backdrop.height;
                int visibleTiles = Mathf.CeilToInt(width / tileWidth) + 1;
                while (tiles.Count < visibleTiles)
                {
                    var tile = new Image { image = backdrop, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    tile.style.flexShrink = 0;
                    strip.Add(tile);
                    tiles.Add(tile);
                }
                for (int i = 0; i < tiles.Count; i++)
                {
                    tiles[i].style.width = tileWidth;
                    tiles[i].style.height = height;
                    tiles[i].style.display = i < visibleTiles ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            int count = walkers.Count;
            if (count == 0) { Animate(); return; }
            bool wide = width / height >= 1.25f;
            float size = Mathf.Min(height * .33f, width * (wide ? .57f : .88f) / (1 + .66f * (count - 1)));
            float centre = width * (wide ? .68f : .52f);
            for (int index = 0; index < count; index++)
            {
                var actor = walkers[index];
                float x = centre + ((count - 1) / 2f - index) * size * .66f;
                float ground = RoadLevel * height + (index % 2 == 0 ? .008f : -.008f) * height;
                actor.Figure.style.left = x - size / 2;
                actor.Figure.style.top = ground - size * (1 - actor.SoleInset);
                actor.Figure.style.width = actor.Figure.style.height = size;
                actor.Shadow.style.left = x - size * .22f;
                actor.Shadow.style.top = ground - size * .024f;
                actor.Shadow.style.width = size * .44f;
                actor.Shadow.style.height = size * .055f;
            }
            Animate();
        }

        private void Animate()
        {
            double elapsed = (Time.unscaledTimeAsDouble - started) * MarchSpeed;
            if (tileWidth > 0)
            {
                float offset = (float)((tileWidth * .10 + elapsed * layout.height * .046) % tileWidth);
                strip.style.translate = new Translate(-offset, 0);
            }
            for (int index = 0; index < walkers.Count; index++)
            {
                var actor = walkers[index];
                int frame = AnimationCels.Loop(elapsed + index * .101, .76);
                if (frame == actor.Frame) continue;
                actor.Frame = frame;
                if (actor.WalkSheet != null)
                {
                    actor.Sprite.sourceRect = AnimationCels.RectFor(actor.WalkSheet, frame);
                    actor.Sprite.MarkDirtyRepaint();
                }
                else if (actor.Pose != null)
                    actor.Pose.ShowFrame(frame);
                else
                    actor.Sprite.style.translate = new Translate(0, -(1 - Mathf.Cos(frame * Mathf.PI / 4f)) * 2f);
            }
        }
    }
}
