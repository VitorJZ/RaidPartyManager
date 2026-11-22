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
            ("明日讣告", "卖报的把明天的报纸塞过铁栅栏。墨还湿着，讣告栏里竟印着咱们队员的名字。改版要三枚钱；肯让他们跟访，活着回来倒有稿费。", .32f,.42f,.42f),
            ("棺材差一个角", "三个抬棺人站在坡下，第四根肩带空着。棺里的人敲了两下盖板：“快迟到了，十枚，谁来？”", .42f,.48f,.34f),
            ("墓园工伤理赔处", "白骨办事员伸手要上场战报。每挡下五点伤害赔两枚，上限十枚。领款人一栏，它特意留了两行。", .28f,.28f,.46f),
            ("遗产拍卖，第零号拍品", "三只封存匣摆上绒布。一只叮当响，一只透出药香，一只压弯了桌腿。验货只许掀一次盖，买哪只倒随您。", .4f,.46f,.38f),
            ("墓碑接受冠名", "碑上空着一行金字。经纪人拍胸脯：“活人可以冠名！只借名字，十五枚现结，花圈我们负责送。”", .31f,.47f,.42f),
            ("守柩獒最后一次散步", "老狗叼着一副项圈，爪子轻轻扒门。三块碑，它只在其中一块前卧过一辈子。派个人牵它再走一趟吧。", .46f,.54f,.36f),
            ("三封投错的遗书", "邮差的袋底破了。三封信，收件人的墨都被雨冲掉了。可有人还在墓里等一句迟了许多年的话。", .29f,.42f,.48f),
            ("哭墙今天休息", "墙上钉着一张病假条。守门人递过手绢：“替哭一次，八枚。怎么哭您定，能让墙安心歇会儿就成。”", .43f,.51f,.37f),
            ("渡鸦只说最后一句", "三只渡鸦各守着一盏灯。它们记不得谁说过话，只记得最后一句。您想跟哪只走？", .24f,.36f,.45f),
            ("墓钟少响了一声", "钟绳垂到指尖。轻轻补一声，前排守夜人便举盾；连敲三响，两边后排都要壮起胆子。", .30f,.57f,.50f),
            ("缝魂医师的双人床位", "两张床共用一根银线。医师量了量：“把两个人现有的命分匀些。签字以前，可以先看看账。”", .46f,.50f,.44f),
            ("排在你前面的人", "汤锅前插着昨日战报。掌勺人说：“干活最没被瞧见的那个，往前站。真有伤员，也可以把这碗让给他。”", .38f,.44f,.36f),
            ("生前没说完的那句话", "两块相邻的墓碑，替两个闹别扭的活人开了口。第一块刚说完“我其实……”，第二块就问：“你肯听我讲完？”", .45f,.44f,.48f),
            ("给自己写墓志铭", "石匠收起凿子：“死人没法改稿，活人可以。挑一句想配得上的话吧。做到了，我替你刻。”", .34f,.46f,.49f),
            ("无名葬王征用姓名", "王冠压在一本空名册上。一只手伸过帷幔，比出两根指头。借一位自愿者的名字，用到离开本层，照付两天工资。", .50f,.50f,.50f)
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
