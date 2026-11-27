using System;
using System.Collections.Generic;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// The town's commission board: a wall-mounted oak board with one pinned notice per difficulty. Clicking a
    /// notice takes the job; once taken, the board shows only that notice, stamped, with its progress and the payout ribbon.
    /// </summary>
    internal sealed class QuestBoard : VisualElement
    {
        private const string ArtRoot = "QuestBoard/";
        private const float DesignWidth = 960f;
        // Plank area of the board art, as fractions of the board rectangle.
        private const float FaceLeft = .07f, FaceRight = .07f, FaceTop = .235f, FaceBottom = .1f;

        private static readonly Color Ink = new Color32(52, 33, 19, 255);
        private static readonly Color InkSoft = new Color32(52, 33, 19, 150);
        private static readonly Color Cinnabar = new Color32(158, 34, 28, 255);
        private static readonly Color Carving = new Color32(255, 226, 168, 255);
        private static readonly string[] NoteArt = { "NoteEasy", "NoteNormal", "NoteHard" };
        private static readonly string[] SealArt = { "SealEasy", "SealNormal", "SealHard" };
        private static readonly Color[] RankInk = { new Color32(74, 96, 36, 255), new Color32(42, 66, 118, 255), new Color32(146, 30, 24, 255) };
        private static readonly float[] Tilt = { -2.6f, 1.4f, -1.2f };

        private readonly ScrollView board;
        private readonly VisualElement pins;
        private readonly float aspect;
        private readonly List<Action<float>> scalers = new();
        private float lastWidth = -1;

        internal ScrollView Board => board;

        internal QuestBoard(string title, Action close)
        {
            name = "town-quest-board";
            PrototypeStyle.Fill(this);
            style.backgroundColor = new Color(.035f, .028f, .02f, 0);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            Animate(this, 260, "background-color");

            var art = Art("QuestBoard");
            aspect = art != null ? (float)art.width / art.height : 1.5f;
            board = new ScrollView(ScrollViewMode.Vertical) { name = "contract-paper" };
            board.verticalScrollerVisibility = board.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            board.style.flexShrink = 0;
            board.style.overflow = Overflow.Visible;
            board.contentViewport.style.overflow = Overflow.Visible;
            if (art != null)
            {
                board.style.backgroundImage = art;
                board.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else board.style.backgroundColor = (Color)new Color32(74, 49, 31, 255);
            board.style.opacity = 0;
            board.style.translate = new Translate(0, -26);
            Animate(board, 320, "opacity", "translate");
            Add(board);

            var face = Layer("quest-board-face");
            board.hierarchy.Add(face);

            var plaque = Layer("quest-board-plaque");
            plaque.style.top = Length.Percent(3.5f);
            plaque.style.bottom = StyleKeyword.Auto;
            plaque.style.height = Length.Percent(16);
            plaque.style.left = Length.Percent(31);
            plaque.style.right = Length.Percent(31);
            plaque.style.justifyContent = Justify.Center;
            plaque.style.alignItems = Align.Center;
            var heading = Lettering(title, 27, Carving, bold: true);
            heading.name = "quest-board-title";
            heading.style.letterSpacing = 3;
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            heading.style.whiteSpace = WhiteSpace.NoWrap;
            heading.style.textShadow = Engraved;
            plaque.Add(heading);
            face.Add(plaque);

            pins = new VisualElement { name = "quest-board-pins", pickingMode = PickingMode.Ignore };
            pins.style.position = Position.Absolute;
            pins.style.left = Length.Percent(FaceLeft * 100);
            pins.style.right = Length.Percent(FaceRight * 100);
            pins.style.top = Length.Percent(FaceTop * 100);
            pins.style.bottom = Length.Percent(FaceBottom * 100);
            pins.style.flexDirection = FlexDirection.Row;
            pins.style.justifyContent = Justify.SpaceAround;
            pins.style.alignItems = Align.FlexStart;
            face.Add(pins);

            face.Add(Hint("战斗胜利时计入进度 · 任意城镇均可交付"));
            face.Add(CloseRing(close));

            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            schedule.Execute(() =>
            {
                style.backgroundColor = new Color(.035f, .028f, .02f, .72f);
                board.style.opacity = 1;
                board.style.translate = new Translate(0, 0);
            }).ExecuteLater(16);
        }

        /// <summary>Pins an open commission; pressing it takes the job.</summary>
        internal Button Offer(TownCommission job, int reward, int order, Action accept)
        {
            var note = Note("town-quest-offer-" + job.Difficulty, job.Difficulty, order, accept);
            var body = note.Q("quest-note-body");
            Header(body, job);
            body.Add(Objective(job));
            body.Add(Spacer());
            body.Add(Reward(reward, "quest-reward"));
            var cue = Lettering("揭 榜", 13, Cinnabar, bold: true);
            cue.name = "quest-note-take";
            cue.style.alignSelf = Align.Center;
            cue.style.letterSpacing = 6;
            cue.style.opacity = 0;
            Animate(cue, 160, "opacity");
            body.Add(cue);
            note.RegisterCallback<PointerEnterEvent>(_ => cue.style.opacity = 1);
            note.RegisterCallback<PointerLeaveEvent>(_ => cue.style.opacity = 0);
            note.RegisterCallback<FocusInEvent>(_ => cue.style.opacity = 1);
            note.RegisterCallback<FocusOutEvent>(_ => cue.style.opacity = 0);
            return note;
        }

        /// <summary>Pins the job in hand with its stamp, progress and the payout ribbon <paramref name="claim"/> presses.</summary>
        internal Button Accepted(TownCommission job, int progress, int reward, Action claim)
        {
            bool done = progress >= job.Target;
            var note = Note("town-quest-current", job.Difficulty, 1, null, large: true);
            note.pickingMode = PickingMode.Ignore;
            note.focusable = false;
            var body = note.Q("quest-note-body");
            Header(body, job);
            body.Add(Objective(job, Stamp(done ? "可交付" : "已接取")));
            body.Add(Spacer());
            body.Add(Progress(progress, job.Target));

            pins.style.justifyContent = Justify.SpaceEvenly;
            pins.Add(note);

            // The payout sits beside the notice: what the job pays, then the ribbon that collects it.
            var payout = new VisualElement { name = "quest-payout", pickingMode = PickingMode.Ignore };
            payout.style.alignItems = Align.Center;
            payout.style.alignSelf = Align.Center;
            payout.style.marginBottom = Length.Percent(4);
            var caption = Lettering("委 托 酬 金", 15, new Color(Carving.r, Carving.g, Carving.b, .8f), bold: true);
            caption.style.textShadow = Engraved;
            payout.Add(caption);
            var sum = PrototypeStyle.Row();
            sum.pickingMode = PickingMode.Ignore;
            sum.style.alignItems = Align.Center;
            sum.style.marginTop = 4;
            var coin = Coin(44);
            coin.name = "reward-gold-icon";
            sum.Add(coin);
            var amount = Lettering(reward.ToString(), 46, Carving, bold: true);
            amount.name = "quest-payout-amount";
            amount.style.marginLeft = 8;
            amount.style.textShadow = Engraved;
            sum.Add(amount);
            payout.Add(sum);
            var status = Lettering(done ? "委托已完成，可领取报酬" : $"还差 {job.Target - progress} · 战斗胜利时计入进度",
                13, new Color(Carving.r, Carving.g, Carving.b, .72f));
            status.style.textShadow = Engraved;
            status.style.marginTop = 2;
            payout.Add(status);
            var ribbon = Ribbon(done, claim);
            payout.Add(ribbon);
            pins.Add(payout);
            return ribbon;
        }

        private static readonly TextShadow Engraved = new() { offset = new Vector2(0, 2), blurRadius = 2, color = new Color(.12f, .06f, .02f, .95f) };

        private Button Note(string id, int difficulty, int order, Action press, bool large = false)
        {
            float widthShare = large ? .3f : .27f;
            var art = Art(NoteArt[Mathf.Clamp(difficulty, 0, 2)]);
            float noteAspect = art != null ? (float)art.height / art.width : 1.33f;
            float tilt = large ? -1f : Tilt[order % Tilt.Length];

            var note = new Button(press) { name = id };
            Bare(note);
            note.AddToClassList("quest-note");
            note.style.flexShrink = 0;
            note.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(6));
            note.style.rotate = Deg(tilt - 5);
            note.style.scale = new Scale(Vector2.one * .9f);
            note.style.opacity = 0;
            Animate(note, 240, "rotate", "scale", "opacity", "translate");

            var shadow = Layer("quest-note-shadow");
            shadow.style.translate = new Translate(5, 9);
            shadow.style.opacity = .5f;
            var paper = Layer("quest-note-paper");
            if (art != null)
            {
                shadow.style.backgroundImage = paper.style.backgroundImage = art;
                shadow.style.unityBackgroundImageTintColor = Color.black;
            }
            else paper.style.backgroundColor = (Color)new Color32(234, 214, 170, 255);
            note.Add(shadow);
            note.Add(paper);

            var body = new VisualElement { name = "quest-note-body", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(body);
            // The hard notice is held by a dagger, which reaches further down the paper than a nail.
            body.style.paddingTop = Length.Percent(difficulty >= 2 ? 34 : 15);
            body.style.paddingBottom = Length.Percent(difficulty >= 2 ? 14 : difficulty == 1 ? 13 : 9);
            body.style.paddingLeft = body.style.paddingRight = Length.Percent(difficulty >= 2 ? 11 : 8);
            body.style.alignItems = Align.Stretch;
            note.Add(body);

            var seal = Layer("quest-note-seal");
            seal.style.left = StyleKeyword.Auto;
            seal.style.bottom = StyleKeyword.Auto;
            seal.style.right = Length.Percent(-6);
            seal.style.top = Length.Percent(4);
            seal.style.width = Length.Percent(large ? 24 : 28);
            seal.style.height = StyleKeyword.Auto;
            seal.style.rotate = Deg(12);
            var wax = Art(SealArt[Mathf.Clamp(difficulty, 0, 2)]);
            if (wax != null) seal.style.backgroundImage = wax;
            float sealAspect = wax != null ? (float)wax.height / wax.width : 1;
            note.Add(seal);

            scalers.Add(w =>
            {
                float noteWidth = w * widthShare;
                note.style.width = noteWidth;
                note.style.height = noteWidth * noteAspect;
                seal.style.height = noteWidth * (large ? .24f : .28f) * sealAspect;
            });
            if (lastWidth > 0) scalers[^1](lastWidth);

            if (press != null)
            {
                void Lift(bool lit)
                {
                    note.style.rotate = Deg(lit ? tilt * .25f : tilt);
                    note.style.scale = new Scale(Vector2.one * (lit ? 1.045f : 1));
                    note.style.translate = new Translate(0, lit ? -6 : 0);
                    shadow.style.translate = new Translate(lit ? 9 : 5, lit ? 15 : 9);
                }
                note.RegisterCallback<PointerEnterEvent>(_ => Lift(true));
                note.RegisterCallback<PointerLeaveEvent>(_ => Lift(focusController?.focusedElement == note));
                note.RegisterCallback<FocusInEvent>(_ => Lift(true));
                note.RegisterCallback<FocusOutEvent>(_ => Lift(false));
                pins.Add(note);
            }

            // Pinned one after another, each settling on its nail.
            note.schedule.Execute(() =>
            {
                note.style.opacity = 1;
                note.style.rotate = Deg(tilt);
                note.style.scale = new Scale(Vector2.one);
            }).ExecuteLater(120 + order * 70);
            return note;
        }

        private void Header(VisualElement body, TownCommission job)
        {
            var title = Lettering(job.Title, 25, Ink, bold: true);
            title.name = "quest-note-title";
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.Add(title);
            // The rank sits under the title, clear of the nail, tacks or dagger at the top of the paper.
            var rank = Lettering($"— {job.Rank}委托 —", 12.5f, RankInk[Mathf.Clamp(job.Difficulty, 0, 2)], bold: true);
            rank.name = "quest-note-rank";
            rank.style.unityTextAlign = TextAnchor.MiddleCenter;
            rank.style.letterSpacing = 2;
            rank.style.marginTop = 2;
            body.Add(rank);
            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.style.height = 2;
            rule.style.marginTop = 6;
            rule.style.marginBottom = 4;
            rule.style.marginLeft = rule.style.marginRight = Length.Percent(9);
            rule.style.backgroundColor = new Color(Ink.r, Ink.g, Ink.b, .3f);
            body.Add(rule);
        }

        private VisualElement Objective(TownCommission job, VisualElement emblem = null)
        {
            var block = new VisualElement { pickingMode = PickingMode.Ignore };
            block.style.alignItems = Align.Center;
            block.style.marginTop = 4;
            if (emblem != null) block.Add(emblem);
            var icon = new VisualElement { name = "quest-note-icon", pickingMode = PickingMode.Ignore };
            if (emblem != null) icon.style.display = DisplayStyle.None;
            // One woodcut per kind of objective, e.g. IconSwift for every "win within N rounds" job.
            var art = Art("Icon" + job.Kind) ?? Art("IconWins");
            if (art != null)
            {
                icon.style.backgroundImage = art;
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                icon.style.unityBackgroundImageTintColor = new Color(1, 1, 1, .88f);
            }
            icon.style.flexShrink = 0;
            float iconSize = job.Difficulty >= 2 ? 60 : 72;
            scalers.Add(w => icon.style.width = icon.style.height = iconSize * w / DesignWidth);
            if (lastWidth > 0) scalers[^1](lastWidth);
            block.Add(icon);
            var words = Lettering(job.Objective, 13.5f, Ink);
            words.name = "quest-note-objective";
            words.style.unityTextAlign = TextAnchor.MiddleCenter;
            words.style.marginTop = 6;
            block.Add(words);
            return block;
        }

        private VisualElement Reward(int gold, string id)
        {
            var row = PrototypeStyle.Row(id);
            row.pickingMode = PickingMode.Ignore;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            var label = Lettering("酬金", 14, InkSoft, bold: true);
            label.style.marginRight = 6;
            row.Add(label);
            row.Add(Coin(26));
            var amount = Lettering(gold.ToString(), 26, Cinnabar, bold: true);
            amount.style.marginLeft = 4;
            row.Add(amount);
            return row;
        }

        private VisualElement Progress(int progress, int target)
        {
            var block = new VisualElement { name = "quest-progress", pickingMode = PickingMode.Ignore };
            var line = PrototypeStyle.Row();
            line.pickingMode = PickingMode.Ignore;
            line.style.justifyContent = Justify.SpaceBetween;
            line.style.alignItems = Align.FlexEnd;
            line.Add(Lettering("进度", 14, InkSoft, bold: true));
            var count = Lettering($"{Math.Min(progress, target)} / {target}", 20, progress >= target ? Cinnabar : Ink, bold: true);
            count.name = "quest-progress-count";
            line.Add(count);
            block.Add(line);

            // A quill-drawn gauge: an inked outline washed in from the left.
            float share = target <= 0 ? 1 : Mathf.Clamp01((float)progress / target);
            var gauge = new VisualElement { name = "quest-progress-gauge", pickingMode = PickingMode.Ignore };
            gauge.style.height = 12;
            gauge.style.marginTop = 4;
            gauge.generateVisualContent += context =>
            {
                var r = gauge.contentRect;
                if (r.width <= 0) return;
                var pen = context.painter2D;
                pen.fillColor = new Color(Ink.r, Ink.g, Ink.b, .1f);
                Capsule(pen, new Rect(r.x, r.y, r.width, r.height));
                pen.Fill();
                if (share > 0)
                {
                    pen.fillColor = new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, .78f);
                    Capsule(pen, new Rect(r.x + 1.5f, r.y + 1.5f, Mathf.Max(r.height - 3, (r.width - 3) * share), r.height - 3));
                    pen.Fill();
                }
                pen.strokeColor = new Color(Ink.r, Ink.g, Ink.b, .8f);
                pen.lineWidth = 1.6f;
                Capsule(pen, new Rect(r.x, r.y, r.width, r.height));
                pen.Stroke();
                pen.lineWidth = 1f;
                pen.strokeColor = new Color(Ink.r, Ink.g, Ink.b, .35f);
                pen.BeginPath();
                for (int i = 1; i < target && target <= 20; i++)
                {
                    float x = r.x + r.width * i / target;
                    pen.MoveTo(new Vector2(x, r.y + 3)); pen.LineTo(new Vector2(x, r.yMax - 3));
                }
                pen.Stroke();
            };
            block.Add(gauge);
            return block;
        }

        private VisualElement Stamp(string words)
        {
            var stamp = new VisualElement { name = "quest-stamp", pickingMode = PickingMode.Ignore };
            stamp.style.flexShrink = 0;
            stamp.style.marginTop = 2;
            stamp.style.alignItems = Align.Center;
            stamp.style.justifyContent = Justify.Center;
            var ring = Art("StampRing");
            if (ring != null)
            {
                stamp.style.backgroundImage = ring;
                stamp.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            var label = Lettering(words, 21, new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, .9f), bold: true);
            label.name = "quest-stamp-words";
            label.style.letterSpacing = 2;
            stamp.Add(label);
            stamp.style.rotate = Deg(-14);
            stamp.style.scale = new Scale(Vector2.one * 1.8f);
            stamp.style.opacity = 0;
            Animate(stamp, 170, "scale", "opacity");
            scalers.Add(w => stamp.style.width = stamp.style.height = w * .3f * .37f);
            if (lastWidth > 0) scalers[^1](lastWidth);
            stamp.schedule.Execute(() =>
            {
                stamp.style.scale = new Scale(Vector2.one);
                stamp.style.opacity = .92f;
            }).ExecuteLater(420);
            return stamp;
        }

        private Button Ribbon(bool ready, Action claim)
        {
            var ribbon = new Button(claim) { name = "town-quest-act" };
            Bare(ribbon);
            ribbon.style.alignItems = Align.Center;
            ribbon.style.justifyContent = Justify.Center;
            ribbon.style.marginTop = 12;
            var art = Art("ClaimRibbon");
            if (art != null)
            {
                ribbon.style.backgroundImage = art;
                ribbon.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else
            {
                ribbon.style.backgroundColor = (Color)new Color32(122, 32, 26, 255);
                PrototypeStyle.Outline(ribbon, new Color32(201, 158, 82, 255), 4);
            }
            var words = Lettering(ready ? "领 取 报 酬" : "尚 未 完 成", 19, new Color32(255, 236, 196, 255), bold: true);
            words.style.textShadow = new TextShadow { offset = new Vector2(0, 1.5f), blurRadius = 1.5f, color = new Color(.18f, .02f, .02f, .9f) };
            ribbon.Add(words);
            float ribbonAspect = art != null ? (float)art.width / art.height : 5.2f;
            scalers.Add(w =>
            {
                float width = w * .3f;
                ribbon.style.width = width;
                ribbon.style.height = width / ribbonAspect;
            });
            if (lastWidth > 0) scalers[^1](lastWidth);
            ribbon.SetEnabled(ready);
            if (!ready) ribbon.style.unityBackgroundImageTintColor = new Color(.55f, .5f, .48f);
            ribbon.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            Animate(ribbon, 140, "scale");
            ribbon.RegisterCallback<PointerEnterEvent>(_ => { if (ribbon.enabledSelf) ribbon.style.scale = new Scale(Vector2.one * 1.06f); });
            ribbon.RegisterCallback<PointerLeaveEvent>(_ => ribbon.style.scale = new Scale(Vector2.one));
            return ribbon;
        }

        private VisualElement Hint(string words)
        {
            var slip = new VisualElement { name = "quest-board-hint", pickingMode = PickingMode.Ignore };
            slip.style.position = Position.Absolute;
            slip.style.right = Length.Percent(7);
            slip.style.bottom = Length.Percent(6.5f);
            slip.style.width = Length.Percent(34);
            slip.style.justifyContent = Justify.Center;
            slip.style.paddingLeft = Length.Percent(3.5f);
            slip.style.paddingRight = Length.Percent(1.5f);
            slip.style.rotate = Deg(-1.2f);
            var art = Art("HintSlip");
            if (art != null)
            {
                slip.style.backgroundImage = art;
                slip.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            else slip.style.backgroundColor = (Color)new Color32(226, 204, 160, 255);
            float slipAspect = art != null ? (float)art.width / art.height : 5.2f;
            scalers.Add(w => slip.style.height = w * .34f / slipAspect);
            var text = Lettering(words, 13.5f, new Color(Ink.r, Ink.g, Ink.b, .85f), bold: true);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            slip.Add(text);
            return slip;
        }

        private Button CloseRing(Action close)
        {
            var ring = new Button(close) { name = "dismiss-paper", tooltip = "关闭并返回城镇" };
            Bare(ring);
            ring.style.position = Position.Absolute;
            ring.style.right = Length.Percent(1.6f);
            ring.style.top = Length.Percent(12f);
            ring.style.width = Length.Percent(6f);
            var art = Art("CloseNail");
            if (art != null)
            {
                ring.style.backgroundImage = art;
                ring.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            ring.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(12));
            Animate(ring, 160, "rotate", "-unity-background-image-tint-color");
            float ringAspect = art != null ? (float)art.height / art.width : 1.3f;
            scalers.Add(w => ring.style.height = w * .06f * ringAspect);
            ring.RegisterCallback<PointerEnterEvent>(_ =>
            {
                ring.style.rotate = Deg(-12);
                ring.style.unityBackgroundImageTintColor = new Color(1, .86f, .7f);
            });
            ring.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                ring.style.rotate = Deg(0);
                ring.style.unityBackgroundImageTintColor = Color.white;
            });
            // The ring is the close mark; a drawn cross sits in its hole for readability.
            ring.generateVisualContent += context =>
            {
                var r = ring.contentRect;
                var centre = new Vector2(r.center.x, r.y + r.height * .59f);
                float arm = r.width * .13f;
                var pen = context.painter2D;
                pen.strokeColor = Carving;
                pen.lineWidth = Mathf.Max(1.6f, r.width * .04f);
                pen.lineCap = LineCap.Round;
                pen.BeginPath();
                pen.MoveTo(centre + new Vector2(-arm, -arm)); pen.LineTo(centre + new Vector2(arm, arm));
                pen.MoveTo(centre + new Vector2(arm, -arm)); pen.LineTo(centre + new Vector2(-arm, arm));
                pen.Stroke();
            };
            return ring;
        }

        private void Fit()
        {
            var area = contentRect;
            if (float.IsNaN(area.width) || area.width <= 0 || area.height <= 0) return;
            float width = Mathf.Min(area.width * .96f, area.height * .97f * aspect, 1080);
            if (Mathf.Abs(width - lastWidth) < .5f) return;
            lastWidth = width;
            board.style.width = width;
            board.style.height = width / aspect;
            foreach (var scale in scalers) scale(width);
        }

        private Label Lettering(string words, float size, Color color, bool bold = false)
        {
            var label = PrototypeStyle.Text(words, Mathf.RoundToInt(size), color);
            label.pickingMode = PickingMode.Ignore;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            scalers.Add(w => label.style.fontSize = Mathf.Max(10, size * w / DesignWidth));
            if (lastWidth > 0) scalers[^1](lastWidth);
            return label;
        }

        private VisualElement Coin(float size)
        {
            var coin = new VisualElement { pickingMode = PickingMode.Ignore };
            coin.style.flexShrink = 0;
            var art = Resources.Load<Texture2D>("Settings/Coin");
            if (art != null)
            {
                coin.style.backgroundImage = art;
                coin.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else coin.style.backgroundColor = PrototypeStyle.SkillGold;
            scalers.Add(w => coin.style.width = coin.style.height = size * w / DesignWidth);
            if (lastWidth > 0) scalers[^1](lastWidth);
            return coin;
        }

        private static VisualElement Spacer()
        {
            var gap = new VisualElement { pickingMode = PickingMode.Ignore };
            gap.style.flexGrow = 1;
            return gap;
        }

        private static void Capsule(Painter2D pen, Rect r)
        {
            float radius = r.height / 2;
            pen.BeginPath();
            pen.MoveTo(new Vector2(r.x + radius, r.y));
            pen.LineTo(new Vector2(r.xMax - radius, r.y));
            pen.Arc(new Vector2(r.xMax - radius, r.center.y), radius, -90, 90);
            pen.LineTo(new Vector2(r.x + radius, r.yMax));
            pen.Arc(new Vector2(r.x + radius, r.center.y), radius, 90, 270);
            pen.ClosePath();
        }

        private static VisualElement Layer(string id)
        {
            var layer = new VisualElement { name = id, pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(layer);
            layer.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            return layer;
        }

        private static void Bare(Button button)
        {
            var s = button.style;
            s.backgroundColor = Color.clear;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 0;
            s.marginTop = s.marginBottom = s.marginLeft = s.marginRight = 0;
            s.paddingTop = s.paddingBottom = s.paddingLeft = s.paddingRight = 0;
            s.overflow = Overflow.Visible;
        }

        private static Rotate Deg(float degrees) => new(new Angle(degrees, AngleUnit.Degree));

        private static Texture2D Art(string file) => Resources.Load<Texture2D>(ArtRoot + file);

        private static void Animate(VisualElement element, int milliseconds, params string[] properties)
        {
            var names = new List<StylePropertyName>();
            foreach (string property in properties) names.Add(new StylePropertyName(property));
            element.style.transitionProperty = names;
            element.style.transitionDuration = new List<TimeValue> { new(milliseconds, TimeUnit.Millisecond) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
        }
    }
}
