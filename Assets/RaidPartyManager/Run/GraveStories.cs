using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPartyManager.Run
{
    public static class GraveStories
    {
        // Lead-in, action and hold are measured from the player's choice, in seconds.
        private static readonly (string title, string intro, float anticipation, float action, float hold)[] scripts =
        {
            ("明日讣告", "明天的讣告竟登了咱们队员的名字。花 3 金币能撤稿，也能答应记者跟访。让这名队员下场出战并活着回来，队伍就收 12 金币。", .32f,.42f,.42f),
            ("棺材差一个角", "棺材缺个抬角的人。选一名队员搭把手，队伍马上拿 10 金币。不过他要歇口气，下场战斗第一回合不能行动。", .42f,.48f,.34f),
            ("墓园工伤理赔处", "办事员只认上场的护盾记录。先选一名用盾挡过伤害的队员，每挡 5 点赔 2 金币，最多 10 枚。再选钱归队伍还是归本人。", .28f,.28f,.46f),
            ("遗产拍卖，第零号拍品", "拍卖桌上三只匣子，只能免费查验一只。第一次点匣子看内容，验完再点任意一只就按标价买下。点空白处可以退后。", .4f,.46f,.38f),
            ("墓碑接受冠名", "花商出 15 金币，想在墓碑上刻一位活人的名字。先挑位队员冠名，再选谁收钱。给本人，他会更满意。给队伍，就进公账。", .31f,.47f,.42f),
            ("守柩獒最后一次散步", "老狗叼着项圈，要去找主人。先选一名队员牵它走。留意它在哪块碑前趴下，再选把项圈挂到哪块碑上。", .46f,.54f,.36f),
            ("三封投错的遗书", "三封信让雨淋花了地址。读每封信里的线索，从钟匠、摆渡人和裁缝里挑出收信人。三封都送对，邮差有回礼。", .29f,.42f,.48f),
            ("哭墙今天休息", "哭墙今天请病假，想雇个人替它哭一场。挑一位队员代哭就能赚 8 金币。至于怎么哭，且听他自己发挥。", .43f,.51f,.37f),
            ("渡鸦只说最后一句", "三只渡鸦都记着一句遗言。先读挂牌，再选一句跟过去。一句指向补给，一句关系到下场敌阵，还有一句在找失主的东西。", .24f,.36f,.45f),
            ("墓钟少响了一声", "墓钟的声音两边都听得见。下场开战，敲一声让双方最前排各得 3 点护盾。敲三声让双方最后排各得 1 层盛气。", .30f,.57f,.50f),
            ("缝魂医师的双人床位", "医师能把两个人现有的生命重新分配。先后选两名队员，看清各自会变成多少。只有同意缝魂，生命值才会改变。", .46f,.50f,.44f),
            ("排在你前面的人", "汤只够一碗。给上场贡献最少的队员留座，他会更满意。也可以让伤势最重的队员喝，最多恢复 5 点生命。", .38f,.44f,.36f),
            ("生前没说完的那句话", "两块碑替闹别扭的队员捎了话。先选哪两个人谈，再约好下场怎么排。要么两人一起上阵并活下来，要么只派其中一人。", .45f,.44f,.48f),
            ("给自己写墓志铭", "石匠肯替活人刻碑。先挑队员，再选他想做到的事，写进目标栏。两个栏都满了，就得选一个旧目标来替换。", .34f,.46f,.49f),
            ("无名葬王征用姓名", "葬王向队伍租一个名字，租金是这名队员两天的日薪。选谁，谁在本层就暂叫「某位员工」。记住真名，到了出口还要认人。", .50f,.50f,.50f)
        };
        public static IReadOnlyList<AdventureEventKind> Kinds { get; } =
            Enumerable.Range((int)AdventureEventKind.Obituary, scripts.Length).Select(i => (AdventureEventKind)i).ToArray();
        public static bool Contains(AdventureEventKind? kind) => kind.HasValue &&
            (int)kind.Value >= (int)AdventureEventKind.Obituary && (int)kind.Value <= (int)AdventureEventKind.BorrowedName;
        private static int Index(AdventureEventKind kind) => (int)kind - (int)AdventureEventKind.Obituary;
        public static string Title(AdventureEventKind kind) => scripts[Index(kind)].title;
        public static string Intro(AdventureEventKind kind) => scripts[Index(kind)].intro;
        public static (float lead, float action, float hold) Timing(AdventureEventKind kind)
        {
            var cue = scripts[Index(kind)];
            return (cue.anticipation, cue.action, cue.hold);
        }
    }
}
