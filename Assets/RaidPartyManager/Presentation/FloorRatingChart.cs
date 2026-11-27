using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class FloorRatingChart : VisualElement
    {
        private readonly IReadOnlyList<FloorBattleRating> battles;
        private readonly VisualElement plot;
        private readonly Label averageLabel, legend;
        private readonly Label[] ticks = new Label[3];
        private readonly Label[] values, stages;
        private readonly int?[] scores;
        private int? average;

        internal FloorRatingChart(AdventureRun run)
        {
            name = "floor-rating-chart";
            battles = run.FloorBattles.ToArray();
            style.flexShrink = 0;
            style.backgroundColor = PrototypeStyle.Inset;
            style.paddingLeft = style.paddingRight = 9;
            style.paddingTop = style.paddingBottom = 7;
            style.marginBottom = 8;
            PrototypeStyle.Outline(this, PrototypeStyle.Edge, 4);

            var header = PrototypeStyle.Row();
            header.style.alignItems = Align.Center;
            var title = PrototypeStyle.Text($"第 {run.Floor} 层 · 战斗评分", 18);
            title.style.flexGrow = 1;
            header.Add(title);
            Add(header);
            var members = battles.SelectMany(b => b.Members).Where(m => !string.IsNullOrEmpty(m.UnitId))
                .GroupBy(m => m.UnitId).Select(group => group.Last()).ToArray();
            if (members.Length == 0)
            {
                Add(PrototypeStyle.Text("这份存档没有可显示的本层战斗评分。", 13, PrototypeStyle.Muted));
                return;
            }

            var names = new Dictionary<string, string> { [string.Empty] = "全队" };
            foreach (var group in members.GroupBy(member => member.Name))
            {
                int number = 0;
                foreach (var member in group)
                    names.Add(member.UnitId, group.Count() == 1 ? member.Name : $"{member.Name}（{++number}）");
            }
            var series = new DropdownField(new[] { string.Empty }.Concat(members.Select(m => m.UnitId)).ToList(),
                0, id => names[id], id => names[id]) { name = "floor-rating-series", tooltip = "切换全队或成员的评分走势" };
            series.style.width = 122;
            series.style.marginRight = 10;
            if (series.Q<TextElement>(className: "unity-base-popup-field__text") is { } selection)
                selection.style.color = PrototypeStyle.Background;
            header.Add(series);
            averageLabel = PrototypeStyle.Text("", 17, PrototypeStyle.Accent);
            averageLabel.name = "floor-rating-average";
            header.Add(averageLabel);

            plot = new VisualElement { name = "floor-rating-plot" };
            plot.style.height = 154;
            plot.generateVisualContent += Draw;
            if (battles.Count > DockRoute.Stages)
            {
                // Inspection can run any number of encounters before advancing to the next floor.
                var timeline = new ScrollView(ScrollViewMode.Horizontal);
                plot.style.width = 65 * battles.Count + 32;
                timeline.Add(plot);
                Add(timeline);
            }
            else Add(plot);
            for (int i = 0; i < ticks.Length; i++)
                ticks[i] = Caption((i * 5).ToString(), 11, PrototypeStyle.Muted);

            scores = new int?[battles.Count];
            values = new Label[battles.Count];
            stages = new Label[battles.Count];
            for (int i = 0; i < battles.Count; i++)
            {
                var battle = battles[i];
                string kind = battle.Kind switch
                {
                    NodeKind.Boss => "首领战", NodeKind.Event => "事件战", _ => AdventureFlow.NodeName(battle.Kind)
                };
                string stop = run.IsInspection ? $"第{i + 1}战" : $"第{battle.Stage}站";
                stages[i] = Caption(stop + "\n" + kind, 11, battle.Kind == NodeKind.Boss ? PrototypeStyle.Accent : PrototypeStyle.Muted);
                values[i] = Caption("", 12, PrototypeStyle.Ink);
                values[i].name = "floor-rating-point-" + i;
            }
            legend = PrototypeStyle.Text("", 12, PrototypeStyle.Muted);
            Add(legend);
            if (run.FirstRatedStage > 1)
                Add(PrototypeStyle.Text($"本层评分记录从第 {run.FirstRatedStage} 站开始，均分按已记录场次计算。", 12, PrototypeStyle.Muted));
            plot.RegisterCallback<GeometryChangedEvent>(_ => PlaceCaptions());
            series.RegisterValueChangedCallback(evt => SelectSeries(evt.newValue));
            SelectSeries(string.Empty);
        }

        private Label Caption(string text, int size, Color color)
        {
            var label = PrototypeStyle.Text(text, size, color);
            label.style.position = Position.Absolute;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginLeft = label.style.marginRight = 0;
            PrototypeStyle.Middle(label);
            plot.Add(label);
            return label;
        }

        private void SelectSeries(string id)
        {
            bool team = string.IsNullOrEmpty(id);
            for (int i = 0; i < battles.Count; i++)
            {
                scores[i] = battles[i].ScoreFor(team ? null : id);
                values[i].text = scores[i] is int score ? Format(score) : "—";
                values[i].tooltip = scores[i].HasValue ? stages[i].text.Replace('\n', ' ') + " · " + values[i].text + " 分" :
                    battles[i].Members.Length == 0 ? "缺少本场评分记录" : "本场未上阵";
            }
            average = BattleRating.AverageTenths(scores);
            averageLabel.text = "本层均分  " + (average is int mean ? Format(mean) : "—");
            legend.text = team ? "实线为每场上阵队员的平均分，虚线为各场均分的平均值。" :
                "实线为成员的单场评分，虚线为参战场次的平均值。未上阵的场次留空。";
            PlaceCaptions();
            plot.MarkDirtyRepaint();
        }

        private static string Format(int tenths) => (tenths / 10m).ToString("0.0");

        private Rect Graph => Rect.MinMaxRect(27, 22, plot.contentRect.width - 4, plot.contentRect.height - 34);
        private float X(Rect graph, int i) => graph.xMin + (i + .5f) * graph.width / battles.Count;
        private static float Y(Rect graph, int tenths) => graph.yMax - graph.height * tenths / 100f;

        private void PlaceCaptions()
        {
            var graph = Graph;
            if (!(graph.width > 0 && graph.height > 0)) return;
            for (int i = 0; i < ticks.Length; i++)
                Place(ticks[i], 2, Y(graph, i * 50) - 8, 20, 16);
            float width = graph.width / battles.Count;
            for (int i = 0; i < battles.Count; i++)
            {
                float left = X(graph, i) - width * .5f;
                Place(stages[i], left, graph.yMax + 2, width, 32);
                Place(values[i], left, Y(graph, scores[i] ?? 0) - 21, width, 17);
            }
        }

        private static void Place(Label label, float x, float y, float width, float height)
        {
            label.style.left = x;
            label.style.top = y;
            label.style.width = width;
            label.style.height = height;
        }

        private void Draw(MeshGenerationContext context)
        {
            var graph = Graph;
            if (!(graph.width > 0 && graph.height > 0)) return;
            var pen = context.painter2D;
            pen.lineWidth = 1;
            pen.strokeColor = PrototypeStyle.Edge;
            pen.BeginPath();
            for (int level = 0; level <= 100; level += 50)
            {
                float y = Y(graph, level);
                pen.MoveTo(new Vector2(graph.xMin, y));
                pen.LineTo(new Vector2(graph.xMax, y));
            }
            pen.Stroke();
            if (average is int mean)
            {
                float y = Y(graph, mean);
                pen.strokeColor = PrototypeStyle.Accent;
                pen.BeginPath();
                for (float x = graph.xMin; x < graph.xMax; x += 10)
                {
                    pen.MoveTo(new Vector2(x, y));
                    pen.LineTo(new Vector2(Mathf.Min(x + 5, graph.xMax), y));
                }
                pen.Stroke();
            }

            pen.strokeColor = PrototypeStyle.Ally;
            pen.lineWidth = 2.5f;
            pen.BeginPath();
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] is not int score) continue;
                var point = new Vector2(X(graph, i), Y(graph, score));
                if (i == 0 || !scores[i - 1].HasValue) pen.MoveTo(point);
                else pen.LineTo(point);
            }
            pen.Stroke();
            pen.fillColor = PrototypeStyle.Ally;
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] is not int score) continue;
                pen.BeginPath();
                pen.Arc(new Vector2(X(graph, i), Y(graph, score)), 3.6f, 0, 360, ArcDirection.Clockwise);
                pen.Fill();
            }
        }
    }
}
