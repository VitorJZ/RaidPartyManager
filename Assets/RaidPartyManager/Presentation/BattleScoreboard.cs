using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class BattleScoreboard : VisualElement
    {
        public BattleScoreboard(IReadOnlyList<BattleRating> ratings, EncounterData content)
        {
            name = "battle-scoreboard";
            style.flexShrink = 0;
            var title = PrototypeStyle.Row();
            title.style.alignItems = Align.Center;
            title.style.marginBottom = 4;
            var heading = PrototypeStyle.Text("全场评分", 19);
            heading.style.flexGrow = 1;
            title.Add(heading);
            title.Add(PrototypeStyle.Text("10 分制 · 点选单位查看明细", 12, PrototypeStyle.Muted));
            Add(title);
            if (ratings == null || ratings.Count == 0)
            {
                Add(PrototypeStyle.Text("这场战斗的存档中没有评分记录。", 14, PrototypeStyle.Muted));
                return;
            }

            var header = PrototypeStyle.Row();
            header.style.backgroundColor = PrototypeStyle.Inset;
            header.style.paddingLeft = header.style.paddingRight = 5;
            header.style.minHeight = 23;
            var unit = Cell("单位", 2.8f, PrototypeStyle.Muted);
            unit.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(unit);
            foreach (string caption in new[] { "伤害", "治疗", "承伤", "挡伤", "异常", "击败", "评分" })
                header.Add(Cell(caption, 1, PrototypeStyle.Muted));
            Add(header);

            foreach (var side in new[] { Side.Party, Side.Enemy })
            {
                var color = side == Side.Party ? PrototypeStyle.Ally : PrototypeStyle.Enemy;
                var units = ratings.Where(r => r.Side == side).OrderByDescending(r => r.ScoreTenths).ThenBy(r => r.Position).ToArray();
                var mean = BattleRating.AverageTenths(units.Select(r => (int?)r.ScoreTenths));
                string caption = side == Side.Party ? "我方" : "敌方";
                var group = PrototypeStyle.Text(mean is int value ? $"{caption} · 均分 {value / 10m:0.0}" : caption, 13, color);
                group.style.marginTop = 4;
                Add(group);
                foreach (var row in units)
                    AddUnit(row, content, color);
            }

            var rules = new Foldout { text = "评分规则", value = false, name = "battle-rating-rules" };
            rules.style.fontSize = 13;
            rules.style.color = PrototypeStyle.Muted;
            rules.style.marginTop = 5;
            rules.Add(PrototypeStyle.Text("每名单位从 0 分起，按实际表现累加，满 10 分封顶。小数加分先累积，结算时显示一位小数。", 13));
            rules.Add(PrototypeStyle.Text("每点生命伤害 +0.20，护盾伤害 +0.10，有效治疗 +0.25，承受生命伤害 +0.08，自身护盾挡伤 +0.16，给队友的护盾挡伤 +0.20。", 13));
            rules.Add(PrototypeStyle.Text("每层敌方异常 +0.40，队友增益或主动净化每层 +0.20，拦截命中或技能格挡每次 +0.40，击败或救援每次 +0.60。", 13));
            Add(rules);
        }

        private static Label Cell(string value, float width, Color color)
        {
            var label = PrototypeStyle.Text(value, 13, color);
            label.style.flexBasis = 0;
            label.style.flexGrow = width;
            label.style.minWidth = 0;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private void AddUnit(BattleRating row, EncounterData content, Color sideColor)
        {
            string state = row.Retreated ? "撤离" : row.Fallen ? "倒下" : "存活";
            var detail = PrototypeStyle.Text(
                $"{row.Name}（{state}）　生命伤害 {row.HealthDamage} · 护盾伤害 {row.ShieldDamage} · 其中毒伤 {row.PoisonDamage}\n" +
                $"自身护盾挡伤 {row.ShieldAbsorbed} · 为队友挡伤 {row.AllyShielding} · 拦截 {row.Interceptions} 次 · 技能格挡 {row.SkillBlocks} 次\n" +
                $"施加异常 {row.DebuffLayers} 层 · 给予队友增益 {row.BuffLayers} 层 · 净化异常 {row.CleansedLayers} 层 · 救援 {row.Saves} 次\n" +
                $"加分明细　输出 +{row.DamagePoints:0.##} · 治疗 +{row.HealingPoints:0.##} · 防护 +{row.DefensePoints:0.##} · 状态 +{row.StatusPoints:0.##} · 击败与救援 +{row.KillAndSavePoints:0.##}",
                13, PrototypeStyle.Ink);
            detail.name = "battle-rating-detail-" + row.UnitId;
            detail.style.display = DisplayStyle.None;
            detail.style.paddingLeft = 12;
            detail.style.paddingTop = detail.style.paddingBottom = 4;
            detail.style.backgroundColor = PrototypeStyle.Inset;

            var button = PrototypeStyle.Button("", "battle-rating-" + row.UnitId, null);
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginTop = button.style.marginBottom = 1;
            button.style.paddingLeft = button.style.paddingRight = 4;
            button.style.paddingTop = button.style.paddingBottom = 0;
            button.style.height = 30;
            button.style.flexShrink = 0;
            button.tooltip = $"{row.Name} · {state} · 查看贡献明细";
            var identity = PrototypeStyle.Row();
            identity.style.alignItems = Align.Center;
            identity.style.flexBasis = 0;
            identity.style.flexGrow = 2.8f;
            identity.style.minWidth = 0;
            identity.pickingMode = PickingMode.Ignore;
            var marker = PrototypeStyle.Text("›", 14, sideColor);
            marker.style.width = 11;
            identity.Add(marker);
            if (content.PortraitFor(row.CharacterId) is { } texture)
            {
                var portrait = new Image { image = texture, scaleMode = ScaleMode.ScaleAndCrop };
                portrait.style.width = portrait.style.height = 22;
                portrait.style.marginRight = 4;
                portrait.pickingMode = PickingMode.Ignore;
                identity.Add(portrait);
            }
            var nameLabel = Cell(row.Name, 1, row.Fallen || row.Retreated ? PrototypeStyle.Muted : PrototypeStyle.Ink);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            identity.Add(nameLabel);
            button.Add(identity);
            foreach (long amount in new[] { row.Damage, row.Healing, row.DamageTaken, row.ShieldAbsorbed + row.AllyShielding, row.DebuffLayers, row.Kills })
                button.Add(Cell(amount.ToString(), 1, amount > 0 ? PrototypeStyle.Ink : PrototypeStyle.Muted));
            var score = Cell((row.ScoreTenths / 10m).ToString("0.0"), 1, PrototypeStyle.Accent);
            score.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.Add(score);
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
