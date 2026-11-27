using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.SettlementArt;

namespace RaidPartyManager.Presentation
{
    /// <summary>
    /// The playbill's cast list: every performer inked in a ruled ledger, grouped by side, with a metal medal for the
    /// score, a laurel for the party's best, and each column's leading figure inked in its side's colour.
    /// </summary>
    internal sealed class BattleScoreboard : VisualElement
    {
        private const float NameShare = 3.6f;
        private const int GoldTenths = 60, SilverTenths = 30;
        private static readonly string[] Columns = { "伤害", "治疗", "承伤", "挡伤", "异常", "击败" };

        public BattleScoreboard(IReadOnlyList<BattleRating> ratings, EncounterData content)
        {
            name = "battle-scoreboard";
            style.flexShrink = 0;
            Add(SectionHeading("全场评分"));
            if (ratings == null || ratings.Count == 0)
            {
                var empty = Lettering("这场战斗的存档中没有评分记录。", 14, InkSoft);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 8;
                Add(empty);
                return;
            }

            var header = PrototypeStyle.Row("battle-scoreboard-header");
            header.style.minHeight = 22;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = header.style.paddingRight = 6;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = InkFaint;
            header.AddToClassList(RevealClass);
            var unit = Cell("单位", NameShare, InkSoft, 12);
            unit.style.unityTextAlign = TextAnchor.MiddleLeft;
            unit.style.paddingLeft = 14;
            header.Add(unit);
            foreach (string caption in Columns)
                header.Add(Cell(caption, 1, InkSoft, 12));
            var scoreHeading = Cell("评分", 1.15f, InkSoft, 12);
            scoreHeading.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(scoreHeading);
            Add(header);

            // The best figure in each column is inked in its side's colour.
            var leaders = new long[Columns.Length];
            foreach (var row in ratings)
            {
                var amounts = Amounts(row);
                for (int i = 0; i < amounts.Length; i++) leaders[i] = System.Math.Max(leaders[i], amounts[i]);
            }
            string star = ratings.Where(r => r.Side == Side.Party && r.ScoreTenths > 0).OrderByDescending(r => r.ScoreTenths)
                .ThenBy(r => r.Position).Select(r => r.UnitId).FirstOrDefault();

            foreach (var side in new[] { Side.Party, Side.Enemy })
            {
                var units = ratings.Where(r => r.Side == side).OrderByDescending(r => r.ScoreTenths).ThenBy(r => r.Position).ToArray();
                if (units.Length == 0) continue;
                var mean = BattleRating.AverageTenths(units.Select(r => (int?)r.ScoreTenths));
                Add(Group(side, mean));
                foreach (var row in units)
                    AddUnit(row, content, side == Side.Party ? AllyInk : EnemyInk, leaders, star != null && row.UnitId == star);
            }

            var rules = new Foldout { text = "评分规则 · 10 分制，点选单位查看明细", value = false, name = "battle-rating-rules" };
            rules.style.marginTop = 6;
            rules.AddToClassList(RevealClass);
            var toggle = rules.Q<Toggle>();
            toggle.style.marginLeft = 0;
            if (toggle.Q<Label>() is { } toggleLabel)
            {
                toggleLabel.style.color = InkSoft;
                toggleLabel.style.fontSize = 12;
            }
            if (toggle.Q(className: Toggle.checkmarkUssClassName) is { } arrow)
                arrow.style.unityBackgroundImageTintColor = InkSoft;
            rules.contentContainer.style.marginLeft = 16;
            foreach (string line in new[]
                     {
                         "每名单位从 0 分起，按实际表现累加，满 10 分封顶。小数加分先累积，结算时显示一位小数。",
                         "每点生命伤害 +0.20，护盾伤害 +0.10，有效治疗 +0.25，承受生命伤害 +0.08，自身护盾挡伤 +0.16，给队友的护盾挡伤 +0.20。",
                         "每层敌方异常 +0.40，队友增益或主动净化每层 +0.20，拦截命中或技能格挡每次 +0.40，击败或救援每次 +0.60。",
                         $"奖章：{GoldTenths / 10} 分及以上为金，{SilverTenths / 10} 分及以上为银，其余为铜；桂冠标记讨伐队本场得分最高者。"
                     })
            {
                var text = Lettering(line, 12, InkSoft);
                text.style.marginTop = 2;
                rules.Add(text);
            }
            Add(rules);
        }

        private static long[] Amounts(BattleRating row) =>
            new[] { row.Damage, row.Healing, row.DamageTaken, row.ShieldAbsorbed + row.AllyShielding, row.DebuffLayers, row.Kills };

        private static VisualElement Group(Side side, int? mean)
        {
            var color = side == Side.Party ? AllyInk : EnemyInk;
            var group = PrototypeStyle.Row("battle-scoreboard-group-" + (side == Side.Party ? "party" : "enemy"));
            group.pickingMode = PickingMode.Ignore;
            group.style.alignItems = Align.Center;
            group.style.marginTop = 8;
            group.style.marginBottom = 2;
            group.AddToClassList(RevealClass);
            var pennant = new VisualElement { pickingMode = PickingMode.Ignore };
            pennant.style.width = 15;
            pennant.style.height = 19;
            pennant.style.marginRight = 6;
            var art = Art(side == Side.Party ? "PennantAlly" : "PennantEnemy");
            if (art != null)
            {
                pennant.style.backgroundImage = art;
                pennant.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else pennant.style.backgroundColor = color;
            group.Add(pennant);
            var caption = Lettering(side == Side.Party ? "讨伐队" : "敌方", 14, color, bold: true);
            caption.style.letterSpacing = 2;
            group.Add(caption);
            if (mean is int value)
            {
                var average = Lettering($"均分 {value / 10m:0.0}", 12, InkSoft);
                average.style.marginLeft = 8;
                group.Add(average);
            }
            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.style.flexGrow = 1;
            rule.style.height = 1;
            rule.style.marginLeft = 10;
            rule.style.backgroundColor = new Color(color.r, color.g, color.b, .3f);
            group.Add(rule);
            return group;
        }

        private static Label Cell(string value, float width, Color color, int size = 14)
        {
            var label = Lettering(value, size, color);
            label.style.flexBasis = 0;
            label.style.flexGrow = width;
            label.style.minWidth = 0;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            return label;
        }

        private void AddUnit(BattleRating row, EncounterData content, Color sideColor, long[] leaders, bool star)
        {
            string state = row.Retreated ? "撤离" : row.Fallen ? "倒下" : "存活";
            bool down = row.Fallen || row.Retreated;
            var detail = Lettering(
                $"{row.Name}（{state}）　生命伤害 {row.HealthDamage} · 护盾伤害 {row.ShieldDamage} · 其中毒伤 {row.PoisonDamage}\n" +
                $"自身护盾挡伤 {row.ShieldAbsorbed} · 为队友挡伤 {row.AllyShielding} · 拦截 {row.Interceptions} 次 · 技能格挡 {row.SkillBlocks} 次\n" +
                $"施加异常 {row.DebuffLayers} 层 · 给予队友增益 {row.BuffLayers} 层 · 净化异常 {row.CleansedLayers} 层 · 救援 {row.Saves} 次\n" +
                $"加分明细　输出 +{row.DamagePoints:0.##} · 治疗 +{row.HealingPoints:0.##} · 防护 +{row.DefensePoints:0.##} · 状态 +{row.StatusPoints:0.##} · 击败与救援 +{row.KillAndSavePoints:0.##}",
                12, Ink);
            detail.name = "battle-rating-detail-" + row.UnitId;
            detail.style.display = DisplayStyle.None;
            detail.style.marginLeft = 20;
            detail.style.marginTop = 1;
            detail.style.marginBottom = 4;
            detail.style.paddingLeft = 10;
            detail.style.paddingRight = 8;
            detail.style.paddingTop = detail.style.paddingBottom = 5;
            detail.style.backgroundColor = Wash;
            detail.style.borderLeftWidth = 2;
            detail.style.borderLeftColor = sideColor;

            var button = new Button { name = "battle-rating-" + row.UnitId, text = string.Empty };
            Bare(button);
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.height = 34;
            button.style.flexShrink = 0;
            button.style.paddingLeft = button.style.paddingRight = 6;
            button.style.borderBottomWidth = 1;
            button.style.borderBottomColor = Rule;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius =
                button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 3;
            button.tooltip = $"{row.Name} · {state} · 查看贡献明细";
            Animate(button, 120, "background-color");
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = Wash);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = Color.clear);
            button.AddToClassList(RevealClass);

            var identity = PrototypeStyle.Row();
            identity.pickingMode = PickingMode.Ignore;
            identity.style.alignItems = Align.Center;
            identity.style.flexBasis = 0;
            identity.style.flexGrow = NameShare;
            identity.style.minWidth = 0;
            identity.style.alignSelf = Align.Stretch;
            var marker = Lettering("›", 15, sideColor, bold: true);
            marker.style.width = 12;
            marker.style.unityTextAlign = TextAnchor.MiddleCenter;
            identity.Add(marker);
            if (content.PortraitFor(row.CharacterId) is { } texture)
            {
                var portrait = new Image { image = texture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                portrait.style.width = portrait.style.height = 26;
                portrait.style.marginLeft = 2;
                portrait.style.marginRight = 6;
                portrait.style.flexShrink = 0;
                portrait.style.backgroundColor = new Color(sideColor.r, sideColor.g, sideColor.b, .14f);
                PrototypeStyle.Outline(portrait, new Color(sideColor.r, sideColor.g, sideColor.b, .7f), 4);
                if (down)
                {
                    var grayscale = new FilterFunction(FilterFunctionType.Grayscale);
                    grayscale.AddParameter(new FilterParameter(1f));
                    portrait.style.filter = new List<FilterFunction> { grayscale };
                    portrait.style.opacity = .6f;
                }
                identity.Add(portrait);
            }
            var nameLabel = Cell(row.Name, 0, down ? InkSoft : Ink);
            nameLabel.style.flexGrow = 0;
            nameLabel.style.flexShrink = 0;
            nameLabel.style.maxWidth = 120;
            nameLabel.style.flexBasis = StyleKeyword.Auto;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            identity.Add(nameLabel);
            if (star && Art("Laurel") is { } laurel)
            {
                var crown = new VisualElement { name = "battle-rating-star", pickingMode = PickingMode.Ignore };
                crown.style.width = crown.style.height = 20;
                crown.style.marginLeft = 4;
                crown.style.flexShrink = 0;
                crown.style.backgroundImage = laurel;
                crown.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                identity.Add(crown);
                button.tooltip = $"{row.Name} · 本场最佳 · 查看贡献明细";
            }
            if (down)
            {
                var tag = Lettering(state, 11, new Color(EnemyInk.r, EnemyInk.g, EnemyInk.b, .8f));
                tag.name = "battle-rating-state";
                tag.style.marginLeft = 5;
                tag.style.flexShrink = 1;
                tag.style.minWidth = 0;
                tag.style.overflow = Overflow.Hidden;
                tag.style.whiteSpace = WhiteSpace.NoWrap;
                identity.Add(tag);
            }

            button.Add(identity);

            var amounts = Amounts(row);
            for (int i = 0; i < amounts.Length; i++)
            {
                long amount = amounts[i];
                bool leads = amount > 0 && amount == leaders[i];
                var cell = Cell(amount.ToString(), 1, amount == 0 ? InkFaint : leads ? sideColor : Ink);
                if (leads) cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.Add(cell);
            }

            var medalSlot = new VisualElement { pickingMode = PickingMode.Ignore };
            medalSlot.style.flexBasis = 0;
            medalSlot.style.flexGrow = 1.15f;
            medalSlot.style.alignItems = Align.Center;
            medalSlot.style.justifyContent = Justify.Center;
            var medal = new VisualElement { name = "battle-rating-medal", pickingMode = PickingMode.Ignore };
            medal.style.width = medal.style.height = 32;
            medal.style.alignItems = Align.Center;
            medal.style.justifyContent = Justify.Center;
            string tier = row.ScoreTenths >= GoldTenths ? "Gold" : row.ScoreTenths >= SilverTenths ? "Silver" : "Bronze";
            if (Art("Medal" + tier) is { } medalArt)
            {
                medal.style.backgroundImage = medalArt;
                medal.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                medal.style.backgroundColor = (Color)(tier == "Gold" ? new Color32(226, 178, 74, 255) :
                    tier == "Silver" ? new Color32(196, 200, 204, 255) : new Color32(164, 104, 64, 255));
                PrototypeStyle.Outline(medal, InkSoft, 16);
            }
            medalSlot.Add(medal);
            var score = Lettering((row.ScoreTenths / 10m).ToString("0.0"), 13, tier == "Bronze" ? Gilt : Ink, bold: true);
            score.style.unityTextAlign = TextAnchor.MiddleCenter;
            score.style.textShadow = tier == "Bronze"
                ? new TextShadow { offset = new Vector2(0, 1), blurRadius = 1.5f, color = new Color(.16f, .06f, .02f, .9f) }
                : new TextShadow { offset = new Vector2(0, 1), blurRadius = 1, color = new Color(1, 1, 1, .45f) };
            medal.Add(score);
            button.Add(medalSlot);

            button.clicked += () =>
            {
                bool opening = detail.style.display.value == DisplayStyle.None;
                detail.style.display = opening ? DisplayStyle.Flex : DisplayStyle.None;
                marker.text = opening ? "⌄" : "›";
            };
            Add(button);
            Add(detail);
        }
    }
}
