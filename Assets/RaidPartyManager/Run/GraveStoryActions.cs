using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;
using static RaidPartyManager.Run.AdventureEventKind;

namespace RaidPartyManager.Run
{
    public sealed partial class AdventureRun
    {
        private sealed class GraveVisit
        {
            public string Member, Second, PrintedMember;
            public int Step, Number, Correct, Used, Inspected = -1;
            public int[] Order;
            public EquipmentDefinition[] Boxes;
        }

        private GraveVisit graveVisit;
        private static readonly string[] AuctionNames = { "叮当匣", "药香匣", "沉重匣" };
        private static readonly int[] AuctionPrices = { 6, 8, 12 };
        public int AuctionInspected => graveVisit?.Inspected ?? -1;
        public int AuctionKindAt(int slot) => graveVisit.Order[slot];
        public string AuctionNameAt(int slot) => AuctionNames[AuctionKindAt(slot)];
        public int AuctionPriceAt(int slot) => AuctionPrices[AuctionKindAt(slot)];
        private IEnumerable<MemberView> Present => Party.Members.Where(m => m.Health > 0 && !m.IsAway);

        private EmployeeCareer Career(MemberView m) => Party.EmployeeProfileFor(m.Id).Career;
        private bool HasRival(MemberView m) => Career(m).RivalIncidents > 0 && Present.Any(r => r.Id == Career(m).RivalId && r.Id != m.Id);
        private bool GraveEligible(AdventureEventKind kind) => kind switch
        {
            ClaimDesk => Present.Any(m => Career(m).LastBattleBlocked >= 5),
            WhisperWall => Present.Any(HasRival),
            SoupQueue => Present.Any(m => Career(m).LastBattleAttended),
            SoulWard => Present.Count() >= 2,
            BorrowedName => !Party.HasBorrowedName && Present.Any(m => m.Contract != null),
            Epitaph => Present.Any(m => Career(m).Inscription.Kind == EmployeeGoalKind.None),
            _ => true
        };
        private void OpenGraveStory(AdventureEventKind kind)
        {
            graveVisit = new GraveVisit
            {
                Order = Enumerable.Range(0, 3).OrderBy(_ => random.Next()).ToArray()
            };
            if (kind == Obituary)
            {
                var candidates = Present.ToArray();
                if (candidates.Length > 0)
                {
                    var subject = candidates[random.Next(candidates.Length)];
                    graveVisit.PrintedMember = subject.Id;
                    Current.Message = $"墨迹未干，铅字排着「{subject.Character.Name}」。";
                }
            }

            if (kind == Auction)
            {
                var pool = equipment.Where(i => i is not SkillBookDefinition).ToArray();
                var boxes = new List<EquipmentDefinition>();
                foreach (var group in new[]
                {
                    pool.Where(i => i.Tier == 3 && !i.IsPotion),
                    pool.Where(i => i.IsPotion),
                    pool.Where(i => i.Tier == 2 && !i.IsPotion)
                }

                )
                {
                    var choices = group.DefaultIfEmpty().ToArray();
                    boxes.Add(choices[random.Next(choices.Length)] ?? pool.OrderBy(_ => random.Next()).FirstOrDefault());
                }

                graveVisit.Boxes = boxes.ToArray();
            }

            if (kind == DeadLetters)
                Current.Message = "〔信中写着：" + LetterClues[graveVisit.Order[0]] + "〕";
            if (!GraveEligible(kind))
                Current.Message = kind switch
                {
                    ClaimDesk => "办事员翻了战报：上场无人累计用护盾挡下五点伤害。",
                    WhisperWall => "碑下安安静静。同行的人里还没有结下梁子的两位。",
                    SoupQueue => "掌勺人找不到上一场的战报，把汤留给了送葬人。",
                    SoulWard => "医师数了数床位：至少要有两位在场的活人。",
                    _ => "守门人查了一遍名册，今天没有合适的志愿者。"
                };
        }

        // Actions are rebuilt when a sign is pressed, so a stale sign cannot pay twice or use an absent employee.
        private readonly struct GraveOption
        {
            public readonly string Key, Text;
            public readonly Action Apply;
            public GraveOption(string key, string text, Action apply)
            {
                Key = key;
                Text = text;
                Apply = apply;
            }
        }

        public IReadOnlyList<(string key, string text)> GraveChoices() => BuildGraveChoices().Select(o => (o.Key, o.Text)).ToArray();
        public bool ChooseGrave(string key)
        {
            var option = BuildGraveChoices().FirstOrDefault(o => o.Key == key);
            if (option.Apply == null)
                return false;
            option.Apply();
            return true;
        }

        private List<GraveOption> BuildGraveChoices()
        {
            var options = new List<GraveOption>();
            if (Current?.Resolved != false || !GraveStories.Contains(Current.Event) || graveVisit == null)
                return options;
            var scene = graveVisit;
            var kind = Current.Event.Value;
            var present = Present.ToArray();
            var member = present.FirstOrDefault(m => m.Id == scene.Member);
            var promises = Party.Promises;
            if (scene.Member != null && member == null)
            {
                scene.Member = scene.Second = null;
                scene.Step = 0;
            }

            if (!GraveEligible(kind))
            {
                Add("pass", "告辞", () => Finish(Current.Message));
                return options;
            }

            switch (kind)
            {
                case Obituary:
                    OfferObituary();
                    break;
                case Pallbearer:
                    OfferPallbearer();
                    break;
                case ClaimDesk:
                case Sponsorship:
                    OfferPayment();
                    break;
                case Auction:
                    OfferAuction();
                    break;
                case HoundWalk:
                    OfferHoundWalk();
                    break;
                case DeadLetters:
                    OfferDeadLetters();
                    break;
                case WeepingWall:
                    OfferWeepingWall();
                    break;
                case Ravens:
                    OfferRavens();
                    break;
                case BellRope:
                    OfferBellRope();
                    break;
                case SoulWard:
                    OfferSoulWard();
                    break;
                case SoupQueue:
                    OfferSoupQueue();
                    break;
                case WhisperWall:
                    OfferWhisperWall();
                    break;
                case Epitaph:
                    OfferEpitaph();
                    break;
                case BorrowedName:
                    OfferBorrowedName();
                    break;
            }

            Add("pass", "就此告辞", () => Finish("守门人合起名册，给队伍让出了路。"));
            return options;
            void OfferObituary()
            {
                if (Party.Gold >= 3)
                    Add("retract", "花 3 金币撤稿", () =>
                    {
                        if (Party.TrySpendGold(3, "撤回讣告"))
                            Finish("排字工抽掉了姓名铅字。明天的版面留给天气。");
                    });
                People(present.Where(m => m.Id == scene.PrintedMember), m => $"跟访{m.Character.Name}", m =>
                {
                    promises.Interview = m.Id;
                    Party.GraveMemory(m.Id, "我刚读了自己的讣告。下场回来，我亲自给他们改。");
                    Finish($"记者记下{m.Character.Name}的名字。下场出战并存活，可领更正稿费 12 金币。");
                });
            }

            void OfferPallbearer()
            {
                People(present, m => m.Character.Name + "抬棺", m =>
                {
                    promises.Porter = m.Id;
                    Party.AddGold(10);
                    Party.GraveMemory(m.Id, "我肩上还压着那口棺材的分量。下场第一轮，容我缓口气。");
                    Finish($"棺材四角齐了。队伍收入 10 金币，{m.Character.Name}下场战斗第一轮无法行动。");
                });
            }

            void OfferPayment()
            {
                if (member == null)
                {
                    People(present.Where(m => kind == Sponsorship || Career(m).LastBattleBlocked >= 5), m => m.Character.Name, Select);
                    return;
                }

                int gold = kind == Sponsorship ? 15 : (int)Math.Min(10, Career(member).LastBattleBlocked / 5 * 2);
                Add("team", $"{gold} 金币归队", () => Pay(false));
                Add("employee", $"{gold} 金币归本人", () => Pay(true));
                void Pay(bool personal)
                {
                    if (!personal)
                        Party.AddGold(gold);
                    if (kind == Sponsorship)
                        promises.Wreath = member.Character.Name;
                    Party.GraveMemory(member.Id, personal ? "我亲手接过了那只钱袋，队长没抽成。" : "我在领款栏签了名，钱入了公账。", personal);
                    Finish($"{member.Character.Name}签了字，{gold} 金币{(personal ? "由本人领取，满意度提高" : "计入队伍公账")}。" + (kind == Sponsorship ? "花商问清了下座城镇的地址。" : "办事员盖上了结案章。"));
                }
            }

            void OfferAuction()
            {
                for (int i = 0; i < 3; i++)
                {
                    int n = i, box = AuctionKindAt(i), price = AuctionPriceAt(i);
                    var item = scene.Boxes[box];
                    if (item == null) continue;
                    string clue = AuctionNameAt(i);
                    if (scene.Inspected < 0)
                        Add("peek" + i, "查验" + clue, () =>
                        {
                            scene.Inspected = n;
                            Current.Message = $"{clue}里是「{item.Name}」。您已经验过货了，下一次点击任意匣子，就会按标价买下它。";
                        });
                    else if (Party.Gold >= price)
                        Add("buy" + i, $"{clue} · {price} 金币", () =>
                        {
                            if (!Party.TrySpendGold(price, $"第 0 号拍卖品 · {clue} · {item.Name}"))
                                return;
                            Party.AddEquipment(item);
                            Finish($"木槌落下。你用 {price} 金币买下了{item.Name}。");
                        });
                }
            }

            void OfferHoundWalk()
            {
                if (member == null)
                {
                    People(present, m => "让" + m.Character.Name + "牵绳", m =>
                    {
                        Select(m);
                        Current.Message = "走到三块碑前，老狗在" + Headstones[scene.Order[0]] + "旁伏下，把下巴搁在爪上。";
                    });
                    return;
                }

                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    Add("collar" + i, Headstones[i], () =>
                    {
                        bool right = n == scene.Order[0];
                        Party.GraveMemory(member.Id, right ? "我把项圈挂回去了。老狗这回肯松嘴了。" : "我走错了碑。那条狗又叼起项圈，领我走了一遍。", right);
                        Finish(right ? "项圈轻碰石碑。老狗在碑脚蜷起，你们等它睡熟才走。" : "老狗没有责怪谁。它把项圈叼到隔壁碑前，安安稳稳卧下了。");
                    });
                }
            }

            void OfferDeadLetters()
            {
                int letter = scene.Order[scene.Step];
                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    if ((scene.Used & 1 << i) != 0)
                        continue;
                    Add("letter" + i, "投给" + Recipients[i], () =>
                    {
                        scene.Used |= 1 << n;
                        scene.Step++;
                        bool right = n == letter;
                        if (right)
                            scene.Correct++;
                        Current.Message = right ? LetterReplies[n] : "信纸从墓缝里悄悄滑了出来。";
                        if (scene.Step < 3)
                            Current.Message += " 〔下一封写着：" + LetterClues[scene.Order[scene.Step]] + "〕";
                        else
                            Finish(Current.Message + $" 三封信投对了 {scene.Correct} 封。" + (scene.Correct == 3 ? Gift("邮差打开了回礼包裹。") : "邮差默默记下了退件。"));
                    });
                }
            }

            void OfferWeepingWall()
            {
                People(present, m => m.Character.Name + "代哭", m =>
                {
                    string line = CryLine(m.Personality);
                    Party.AddGold(8);
                    Party.GraveMemory(m.Id, line);
                    Finish($"{m.Character.Name}攥住手绢：“{line}” 墙那头传来轻轻一声谢谢。演出费 8 金币。");
                });
            }

            void OfferRavens()
            {
                Add("lamp", "先打灯", () => Finish(Gift("渡鸦啄破了灯罩底的补给袋。")));
                Add("watch", "别碰灯", () =>
                {
                    promises.Scout = true;
                    Finish("灯没熄。渡鸦答应在下一场开战前，把对面每一个站位指给你看。");
                });
                Add("skull", "那是我的头", () =>
                {
                    var hat = equipment.FirstOrDefault(i => i.Name.Contains("帽") || i.Name.Contains("盔"));
                    if (hat != null)
                        Party.AddEquipment(hat);
                    Finish("提灯下滚出一颗头骨：“我说的是帽子。头还给我！”" + (hat == null ? "你把帽子扣回它头上。" : "背包收到" + hat.Name + "。"));
                });
            }

            void OfferBellRope()
            {
                Add("once", "轻拉一声", () =>
                {
                    promises.Bell = 1;
                    Finish("绳尾摆了一下。下场开战，双方最前方单位各获 3 点护盾。");
                });
                Add("three", "连拉三响", () =>
                {
                    promises.Bell = 3;
                    Finish("第三道回声越过围墙。下场开战，双方最后方单位各获 1 层盛气。");
                });
            }

            void OfferSoulWard()
            {
                if (member == null)
                {
                    People(present, m => "第一张床 · " + m.Character.Name, Select);
                    return;
                }

                var partner = present.FirstOrDefault(m => m.Id == scene.Second);
                if (partner == null)
                {
                    People(present.Where(m => m.Id != member.Id), m => "第二张床 · " + m.Character.Name, m =>
                    {
                        scene.Second = m.Id;
                        var hp = Party.SharedHealth(member.Id, m.Id);
                        Current.Message = $"{member.Character.Name} {member.Health} → {hp.first}，{m.Character.Name} {m.Health} → {hp.second}。";
                    });
                    return;
                }

                Add("stitch", "同意缝魂", () =>
                {
                    Party.SharedHealth(member.Id, partner.Id, true);
                    foreach (var m in new[]
                    {
                        member,
                        partner
                    }

                    )
                        Party.GraveMemory(m.Id, "我听见过另一个人的心跳，就在我的胸口里。");
                    Finish(Current.Message + " 银线松开，两人同时睁开眼。");
                });
            }

            void OfferSoupQueue()
            {
                var quiet = present.Where(m => Career(m).LastBattleAttended).OrderBy(m => Career(m).LastBattleContribution).First();
                var hurt = present.OrderByDescending(m => m.EffectiveStats.Health - m.Health).First();
                Add("quiet", "给" + quiet.Character.Name + "留座", () => Soup(quiet, true));
                Add("hurt", "给" + hurt.Character.Name + "疗伤", () => Soup(hurt, false));
                void Soup(MemberView m, bool comfort)
                {
                    if (!comfort)
                        Party.GiveSoup(m.Id);
                    Party.GraveMemory(m.Id, comfort ? "我以为昨天谁也没看见我出力。有人给我留了座。" : "我捧着热汤，身上的伤总算缓过来了。", comfort);
                    bool bread = quiet.Id == hurt.Id;
                    if (bread)
                        Party.GiveSoup(m.Id);
                    Finish($"{m.Character.Name}喝下热汤。" + (comfort ? "满意度提高。" : "恢复至多 5 点生命。") + (bread ? "掌勺人又塞来一块面包，额外恢复至多 5 点生命。" : ""));
                }
            }

            void OfferWhisperWall()
            {
                if (member == null)
                {
                    People(present.Where(HasRival), m => m.Character.Name + "和" + present.First(r => r.Id == Career(m).RivalId).Character.Name, Select);
                    return;
                }

                if (!HasRival(member))
                    return;
                Add("together", "下场并肩", () => Promise(false));
                Add("apart", "下场错班", () => Promise(true));
                void Promise(bool apart)
                {
                    var other = present.First(r => r.Id == Career(member).RivalId);
                    promises.PartnerA = member.Id;
                    promises.PartnerB = other.Id;
                    promises.Separate = apart;
                    Finish($"{member.Character.Name}：“我想把那句话说完。” {other.Character.Name}：“我在听。” " + (apart ? "下场只派一位出战，就算兑现分班承诺。" : "下场两人一同出战并存活，便能解开心结。"));
                }
            }

            void OfferEpitaph()
            {
                if (member == null)
                {
                    People(present.Where(m => Career(m).Inscription.Kind == EmployeeGoalKind.None), m => m.Character.Name, Select);
                    return;
                }

                if (scene.Step == 0)
                {
                    for (int i = 0; i < Epitaphs.Length; i++)
                    {
                        int n = i;
                        Add("vow" + i, Epitaphs[i].task, () =>
                        {
                            scene.Number = n;
                            scene.Step = 1;
                            Current.Message = "拟刻「" + Epitaphs[n].words + "」。选择目标栏位。";
                        });
                    }

                    return;
                }

                var goals = Party.EmployeeProfileFor(member.Id).Goals;
                if (goals.Count < 2)
                    Add("free", "写入空位", () => Inscribe(EmployeeGoalKind.None));
                else
                    foreach (var goal in goals)
                        Add("replace" + goal.Kind, "替换：" + GoalName(goal.Kind), () => Inscribe(goal.Kind));
                void Inscribe(EmployeeGoalKind old)
                {
                    var vow = Epitaphs[scene.Number];
                    if (Party.WriteEpitaph(member.Id, vow.kind, old, vow.words))
                        Finish($"石匠收好草稿。{vow.task}后，「{vow.words}」将刻入员工档案。");
                }
            }

            void OfferBorrowedName()
            {
                People(present.Where(m => m.Contract != null), m => m.Character.Name + "自愿借名", m =>
                {
                    promises.RealName = m.Character.Name;
                    promises.Alias = m.Id;
                    int fee = m.Contract.DailyWage * 2;
                    Party.AddGold(fee);
                    Party.GraveMemory(m.Id, "我把名字借出去了。到了出口，可要记得叫我一声。");
                    Finish($"王冠点了点头。队伍收到 {fee} 金币，本层余下的点名册上只写「某位员工」。");
                });
            }

            void Add(string key, string text, Action act) => options.Add(new GraveOption(key, text, act));
            void People(IEnumerable<MemberView> who, Func<MemberView, string> label, Action<MemberView> act)
            {
                foreach (var m in who)
                    Add("member-" + m.Id, label(m), () => act(m));
            }

            void Select(MemberView m)
            {
                scene.Member = m.Id;
                Current.Message = m.Character.Name + "走到了台前。";
            }

            string Gift(string words)
            {
                var item = equipment.Where(i => i.IsPotion || i.Tier == 3).OrderBy(_ => random.Next()).FirstOrDefault();
                if (item == null)
                {
                    Party.AddGold(5);
                    return words + " 袋里有 5 金币。";
                }

                Party.AddEquipment(item);
                return words + " 获得" + item.Name + "。";
            }
        }

        private static readonly string[] Headstones =
        {
            "缺角的石碑",
            "刻狗爪的石碑",
            "长青苔的石碑"
        };
        private static readonly string[] Recipients =
        {
            "钟匠",
            "摆渡人",
            "裁缝"
        };
        private static readonly string[] LetterClues =
        {
            "你的小齿轮，我每天都上油。",
            "旧船桨还靠在门背后。",
            "你缝的袖口，我又放宽了一寸。"
        };
        private static readonly string[] LetterReplies =
        {
            "地底响起一声准点的钟。",
            "碑后响起木桨拨水的声音。",
            "一圈红线从土下穿过信角。"
        };
        private static readonly (EmployeeGoalKind kind, string task, string words)[] Epitaphs =
        {
            (EmployeeGoalKind.Blocking, "累计抵挡 10 点伤害", "我站过的地方，后来的人都走过去了。"),
            (EmployeeGoalKind.Healing, "累计治疗 10 点生命", "我曾把一只发冷的手重新捂热。"),
            (EmployeeGoalKind.Appearances, "再出战 2 次", "点到我的名字时，我总是在场。")
        };
        private static string GoalName(EmployeeGoalKind kind) => kind switch
        {
            EmployeeGoalKind.Appearances => "出场",
            EmployeeGoalKind.Damage => "输出",
            EmployeeGoalKind.Blocking => "抵挡伤害",
            EmployeeGoalKind.Healing => "治疗",
            EmployeeGoalKind.Kills => "击杀",
            EmployeeGoalKind.Victories => "胜利",
            EmployeeGoalKind.Tenure => "工龄",
            EmployeeGoalKind.Equipment => "获得装备",
            EmployeeGoalKind.Training => "学习技能",
            EmployeeGoalKind.Raise => "加薪",
            EmployeeGoalKind.Renewal => "续约",
            _ => "分开编队"
        };
        private static string CryLine(Personality p) => p switch
        {
            Personality.Frank => "我嗓门大，您放心歇。",
            Personality.Deadpan => "我头一回替墙上班。它的假比我的好请。",
            Personality.Optimist => "我先哭一阵，明天说不定就有人来这儿报喜。",
            Personality.Worrier => "我要是哭跑了调，他们会不会以为墙病重了？",
            Personality.Competitive => "我哭到对面那堵都得给我递手绢。",
            Personality.Caring => "我替您哭。您歇一会儿，轮到谁都会累。",
            Personality.Frugal => "我只领一份工钱，两行眼泪算我送的。",
            Personality.Stoic => "我来。手绢给我。",
            Personality.Curious => "我想试试，这面墙记得哪一种回声。",
            _ => "我这一滴泪，献给今天终于获准休息的墙！"
        };
    }
}
