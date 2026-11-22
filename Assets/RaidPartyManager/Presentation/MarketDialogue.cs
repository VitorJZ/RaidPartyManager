using System;
using System.Linq;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    internal sealed class MarketDialogue : VisualElement
    {
        private readonly AdventureRun run;
        private readonly Action changed, close;
        private readonly Image portrait;
        private readonly Texture2D atlas;
        private readonly Label line, account;
        private readonly ScrollView choices;

        internal MarketDialogue(AdventureRun run, Action changed, Action close)
        {
            this.run = run;
            this.changed = changed;
            this.close = close;
            name = "market-dialogue";
            PrototypeStyle.Fill(this);
            styleSheets.Add(Resources.Load<StyleSheet>("MarketDialogue"));
            atlas = Resources.Load<Texture2D>("StoryEvents/Cast/BrokerPortraits");
            portrait = new Image { name = "broker-portrait", image = atlas, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            Add(portrait);
            var options = new VisualElement { name = "broker-options" };
            account = PrototypeStyle.Text("", 14, PrototypeStyle.Accent);
            options.Add(account);
            choices = new ScrollView();
            options.Add(choices);
            Add(options);
            var speech = new VisualElement { name = "broker-speech" };
            speech.Add(PrototypeStyle.Text("说书人", 21, PrototypeStyle.Accent));
            line = PrototypeStyle.Text("", 18, new Color32(255, 241, 220, 255));
            line.name = "broker-line";
            speech.Add(line);
            Add(speech);
            Pick("咳咳，从现在起请叫我董事长……胡子粘正了吗？来，让我看看你想给谁找个新东家。");
            if (run.Current.BrokerMember is string id && run.Party.CanSellMember(id)) Quote(id, true);
        }

        private void Speak(string words)
        {
            var cell = new Vector2(atlas.width * .5f, atlas.height * .5f);
            int pose = Mathf.Clamp(run.Current.BrokerRefusals, 0, 3);
            portrait.sourceRect = new Rect(pose % 2 * cell.x, pose / 2 * cell.y, cell.x, cell.y);
            line.text = words;
            account.text = $"队伍资金　{run.Party.Gold} 金币";
            choices.Clear();
        }

        private void Option(string words, string id, Action action, bool primary = false)
        {
            var button = PrototypeStyle.Button(words, id, action, primary);
            button.AddToClassList("broker-choice");
            choices.Add(button);
        }

        private void Pick(string words)
        {
            Speak(words);
            var crew = run.Party.Members.Where(m => run.Party.CanSellMember(m.Id)).ToArray();
            if (crew.Length == 0) line.text = "给队里留个能带队的人吧。等外出的伙伴回来，咱们再翻这本账。";
            foreach (var m in crew)
                Option($"谈谈{m.Character.Name}的转会", "broker-member-" + m.Id, () => Quote(m.Id));
            Option("今天先聊到这儿。", "broker-leave", close);
        }

        private void Quote(string id, bool resumed = false)
        {
            if (!run.DiscussSale(id)) { Pick("这位今天来不了，我们换个人谈？"); return; }
            var member = run.Party.Members.Single(m => m.Id == id);
            int counter = run.Current.BrokerCounter;
            var words = resumed ? "刚才那页账我可还夹着书签呢。" : "我推一推眼镜，显得专业一点。";
            Bargain(id, $"{words}{member.Character.Name}，我愿意出 {counter} 金币。你怎么说？", counter);
        }

        private void Bargain(string id, string words, int suggested)
        {
            Speak(words);
            var member = run.Party.Members.Single(m => m.Id == id);
            int fee = member.Contract.ReleaseFee;
            var field = new IntegerField { name = "broker-asking", value = suggested, isDelayed = false };
            choices.Add(PrototypeStyle.Text("我的报价（金币）", 14, PrototypeStyle.Ink));
            choices.Add(field);
            var net = PrototypeStyle.Text("", 14, PrototypeStyle.Muted);
            choices.Add(net);
            void Preview(int price) => net.text = $"违约金 {fee}　·　待付工资 {member.Contract.Outstanding}　·　成交净收入 {price - member.Contract.ExitCost} 金币";
            field.RegisterValueChangedCallback(e => Preview(e.newValue));
            Preview(field.value);
            Option("按我的报价成交", "broker-offer", () => Submit(id, field.value), true);
            int counter = run.Current.BrokerCounter;
            Option($"接受她的 {counter} 金币", "broker-accept", () => Submit(id, counter));
            Option("换一位队员谈。", "broker-back", () => Pick("名单给我，我重新翻一页。"));
            Option("暂时离席", "broker-leave", close);
        }

        private void Submit(string id, int asking)
        {
            var member = run.Party.Members.FirstOrDefault(m => m.Id == id);
            if (member == null) { Pick("这位的手续已经办好了。"); return; }
            long net = asking - member.Contract.ExitCost;
            switch (run.OfferSale(id, asking))
            {
                case MarketReply.Sold:
                    GameAudio.Play(SoundCue.RewardGold);
                    changed();
                    Speak($"成交！{member.Character.Name}的转会费是 {asking} 金币，结清合同后是 {net} 金币。你看，我盖章盖得还挺像那么回事吧？");
                    Option("再谈一名队员。", "broker-more", () => Pick("还有谁想找新东家？"));
                    Option("收好合同，离席。", "broker-leave", close);
                    break;
                case MarketReply.Counteroffer:
                    int counter = run.Current.BrokerCounter;
                    string reply = run.Current.BrokerRefusals == 1 ? "慢着，这枚金币可掰不成两枚。" : "等、等一下，眼镜滑了！这次可真是我能挪出的预算了。";
                    Bargain(id, $"{reply} 我还到 {counter} 金币，你再想想？", counter);
                    break;
                case MarketReply.Closed:
                    Speak("……哼，今天的董事会到此结束！再讲下去，我的胡子就要掉下来啦。下座城镇见。");
                    Option("目送她合上账本", "broker-leave", close);
                    break;
                default:
                    Bargain(id, "这张单子没法入账。报价至少要一枚金币，违约金也得付得起。", Math.Max(1, run.Current.BrokerCounter));
                    break;
            }
        }
    }
}
