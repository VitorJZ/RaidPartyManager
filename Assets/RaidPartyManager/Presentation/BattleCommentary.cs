using System;
using System.Collections.Generic;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Presentation
{
    internal enum TellerMood { Watch, Explain, Cheer, Surprise, Warn, Wince, Relief, Rally }

    // The narrator only sees events that have reached the stage. Her own random stream never affects a battle.
    internal sealed class BattleCommentary
    {
        private readonly Random random = new();
        private readonly Dictionary<string, int> lastVariant = new();
        private readonly Dictionary<string, double> lastSpoken = new();
        private (string key, string text, TellerMood mood, int priority, double at) pending;
        private double started, until;
        private int priority;
        public string Text { get; private set; }
        public TellerMood Mood { get; private set; }
        public double PoseStart { get; private set; }

        public void Opening(string boss, double now)
        {
            if (boss == null)
                Offer("opening", TellerMood.Rally, 4, now, "讨伐队，上场！让我看看谁先拿下第一击。", "我站这儿看得清！讨伐队，打出个漂亮的开场！", "讨伐队就位！前排一接上，后面可就热闹了。", "好戏开场！我的掌声可都给讨伐队留着呢。");
            else
                Offer("boss", TellerMood.Rally, 5, now, $"是{boss}！讨伐队，盯紧它的起手。", $"{boss}上场了。今天的主角，我押讨伐队！", $"认准了，{boss}！讨伐队，稳住头一阵！");
            Advance(now);
        }

        private void Offer(string key, TellerMood mood, int rank, double now, params string[] lines)
        {
            if (lastSpoken.TryGetValue(key, out var last) && now - last < (rank >= 7 ? 2.4 : 4.6)) return;
            if (pending.text != null && now - pending.at < 1.65 && pending.priority > rank) return;
            int index = random.Next(lines.Length);
            if (lines.Length > 1 && lastVariant.TryGetValue(key, out int prior) && index == prior)
                index = (index + 1 + random.Next(lines.Length - 1)) % lines.Length;
            pending = (key, lines[index], mood, rank, now);
            // Store the variant only once the line actually gets stage time.
            pendingVariant = index;
        }

        private int pendingVariant;
        public void Advance(double now)
        {
            if (pending.text != null && now - pending.at > 1.65) pending = default;
            bool interrupt = pending.priority > priority && now - started >= 1.35;
            if (pending.text != null && (Text == null || now >= until || interrupt))
            {
                Text = pending.text;
                Mood = pending.mood;
                priority = pending.priority;
                lastSpoken[pending.key] = now;
                lastVariant[pending.key] = pendingVariant;
                PoseStart = started = now;
                until = now + Math.Clamp(1.0 + Text.Length * .073, 2.35, 4.8);
                pending = default;
            }
            else if (Text != null && now >= until)
            {
                Text = null;
                Mood = TellerMood.Watch;
                PoseStart = now;
                priority = 0;
            }
        }

        public void Result(BattleOutcome outcome, double now)
        {
            pending = default;
            Text = null;
            switch (outcome)
            {
                case BattleOutcome.Victory:
                    Offer("win", TellerMood.Cheer, 10, now, "赢了！这一场，掌声给讨伐队！", "台上还站着的是谁？讨伐队！漂亮！", "最后一个敌人也倒了。讨伐队，收工！");
                    break;
                case BattleOutcome.Timeout:
                    Offer("time", TellerMood.Wince, 10, now, "收场铃响了。可惜，讨伐队还差那么一口气。", "时间到了。这一场没拿下，我替讨伐队记着。");
                    break;
                default:
                    Offer("loss", TellerMood.Wince, 10, now, "讨伐队最后一位也倒下了……这一场，我陪大家谢幕。", "先收起掌声吧。讨伐队撑到最后了。");
                    break;
            }
            Advance(now);
        }

        public void Observe(BattleEvent e, string actor, string target, bool ally, int beforeHealth, int beforeShield,
            IReadOnlyList<StatusStack> previous, double now, SkillEffect skill = SkillEffect.None)
        {
            void Say(string key, TellerMood mood, int rank, params string[] choices) =>
                Offer(key + (ally ? "/party" : "/enemy"), mood, rank, now, choices);
            var name = e.ActionName;
            if (e.VisualCue == BattleVisualCue.Rebirth)
            {
                Say("rebirth", ally ? TellerMood.Cheer : TellerMood.Warn, 8,
                    $"{target}又站起来了！生命全满，攻击、防御和特防都翻倍了！");
                return;
            }
            if (e.Countdown is { } clock)
            {
                if (clock.Remaining <= 0) return;
                switch (clock.Kind)
                {
                    case BattleCountdownKind.AdmiralBroadside:
                        Say("broadside", TellerMood.Warn, 8, "看炮口！提督下一次行动要轰讨伐队最后两人。", "提督在校炮，讨伐队后排被瞄上了！"); break;
                    case BattleCountdownKind.ScavengerEscape:
                        Say("escape", TellerMood.Warn, 7, $"那贼再过{clock.Remaining}回合就溜！讨伐队得在它逃前夺回装备。", $"盯住拾荒者！还剩{clock.Remaining}回合，打倒它才能拿回赃物。"); break;
                    case BattleCountdownKind.EnemyCharge:
                        Say("charge", TellerMood.Warn, 7, $"{target}在蓄力！下次能行动就会出招。", $"注意{target}的起手！要是能把它击晕，重击就得往后拖。"); break;
                    case BattleCountdownKind.CalamityChant when clock.Remaining == 1:
                        Say("chant", ally ? TellerMood.Cheer : TellerMood.Warn, 7, ally ? $"{target}快唱完了！讨伐队的雷要落到对面了。" : $"{target}的吟唱快结束了，讨伐队要吃一轮雷！"); break;
                }
                return;
            }
            if (e.Kind == BattleEventKind.Retreated)
            {
                Say("retreat", TellerMood.Wince, 8, $"哎！{target}带着赃物跑了，这笔账我记下了。", $"慢了一步！{target}溜了，讨伐队的装备也被带走了。"); return;
            }
            if (e.Kind == BattleEventKind.Defeated)
            {
                if (ally) Say("fallen", TellerMood.Wince, 9, $"{target}倒下了！讨伐队少了一人。", $"我看见了……{target}没能站住。剩下的人，撑住！", $"{target}倒了。我还在给场上的队员打气！", $"讨伐队折了{target}。这一下真叫人揪心。");
                else Say("fallen", TellerMood.Cheer, 9, $"{target}倒下了！给讨伐队记上一功！", $"漂亮！{target}退场，对面少了一份火力。", $"拿下{target}！这一声喝彩，我可省不了。", $"看清了吗？{target}已经倒了。讨伐队，好样的！");
                return;
            }
            if (e.Kind == BattleEventKind.FormChanged && e.Form != null)
            {
                Say("form", TellerMood.Surprise, 8, ally ? $"{target}变成了{e.Form.Name}！讨伐队添了新打法。" : $"当心！{target}换成了{e.Form.Name}的形态。", ally ? $"看那边！台上的{target}现在是{e.Form.Name}了。" : $"先别庆祝！对面换了形态，现在是{e.Form.Name}。"); return;
            }
            if (e.Kind == BattleEventKind.Action && e.SkillBookId != null && e.ActorEnergy.HasValue)
            {
                string tip = SkillTip(skill);
                string prefix = ally ? $"看{actor}的「{name}」！" : $"当心{actor}的「{name}」！";
                if (tip != null)
                    Say("skill/"+name, ally ? TellerMood.Explain : TellerMood.Warn, 6,
                        prefix + tip, (ally ? $"讨伐队这手「{name}」有讲究。" : $"对面亮了「{name}」。") + tip);
                else
                    Say("skill/"+name, ally ? TellerMood.Explain : TellerMood.Warn, 5, prefix + e.Detail + "。",
                        ally ? $"好，{actor}把能量用在「{name}」上了！" : $"对面动真格了！{actor}使的是「{name}」。");
                return;
            }
            if (e.VisualCue == BattleVisualCue.GraveyardCharge && !string.IsNullOrEmpty(e.Detail))
            {
                string call = name switch
                {
                    "葬王点名" => $"{target}被葬王点名了！下次审判前，有盾或血过半就能获赦。",
                    "星轨定位" => $"星光瞄准了{target}！命中时身上还有盾，就能把星光弹回去。",
                    "汽笛鸣响" => "汽笛响了！皇车冲锋前打掉它6点盾或血，就能保住讨伐队前排！",
                    _ => $"留意「{name}」！{e.Detail}"
                };
                Say("boss/"+name, TellerMood.Warn, 8, call); return;
            }
            if (e.VisualCue == BattleVisualCue.ScavengerSteal)
            {
                Say("stolen", TellerMood.Surprise, 8, $"{actor}{e.Detail}！快拦住它，打倒了还能拿回来。", $"盯住{actor}，它{e.Detail}！可别让它跑了。"); return;
            }
            switch (name)
            {
                case "颂歌落幕":
                    Say("chorus", TellerMood.Wince, 8, $"颂歌倒下，{target}的{e.Detail}。这场要少件装备了。"); return;
                case "拿回装备":
                    Say("recovered", TellerMood.Relief, 8, $"抓到了！{target}{e.Detail}，讨伐队的东西回来了。"); return;
                case "摇钱树枝":
                    Say("coins", TellerMood.Cheer, 2, $"听见钱响了吗？{target}的树枝给讨伐队挣了金币。", "树枝一摇，金币落袋。打这一场还有进账！"); return;
                case "赦免": case "制动熄火": case "冷却泄压": case "星轨落空":
                    Say("reprieve/"+name, TellerMood.Relief, 7, $"好！{e.Detail}", $"给讨伐队喘口气了！{e.Detail}"); return;
                case "炉温上升":
                    Say("furnace", TellerMood.Warn, 6, $"盯着炉温！{e.Detail}寒冷能帮讨伐队泄压。"); return;
            }
            if (e.CharmTriggered)
                Say("charm", ally ? TellerMood.Relief : TellerMood.Wince, 7, ally ? $"{target}的精灵护符亮了！这回真得谢它。" : $"对面的{target}触发了精灵护符，真会挑时候！");

            if (e.Detail == "未命中")
            {
                Say("miss", ally ? TellerMood.Relief : TellerMood.Wince, 4,
                    ally ? $"呼，打偏了！{target}躲过一劫。" : $"哎呀，讨伐队这一下没打中{target}！",
                    ally ? $"险！瞄着{target}的这招落了空。" : $"{target}还真难缠，这一击落空了。",
                    ally ? $"我都缩脖子了！幸好{target}没挨到这一下。" : $"扑了个空！再盯紧点，{target}还在那儿。"); return;
            }
            if (e.ActionKind == ActionKind.Heal && e.TargetHealth > beforeHealth)
            {
                int n = e.TargetHealth - beforeHealth;
                if (beforeHealth == 0)
                    Say("revive", ally ? TellerMood.Cheer : TellerMood.Surprise, 9, ally ? $"{target}站起来了！讨伐队又多一人能打！" : $"等等，{target}又站起来了！讨伐队还得再收拾它一次。");
                else if (ally)
                    Say("heal", TellerMood.Relief, 4, $"{target}回了{n}点血！我这口气总算顺了。", $"这口治疗接得好！{target}实实在在补了{n}点。", $"看血条！{target}拿回{n}点生命，又能多撑一会儿。", $"{target}回了{n}点。讨伐队的救场，我得夸一句！");
                else Say("heal", TellerMood.Wince, 4, $"可惜，{target}又回了{n}点血。", $"对面也有人兜底，{target}补回了{n}点。", $"到嘴的优势又少一点！{target}回了{n}点生命。");
            }
            if (e.TargetHealth > 0 && e.TargetShield > beforeShield)
            {
                int n = e.TargetShield - beforeShield;
                Say("guard", ally ? TellerMood.Relief : TellerMood.Explain, 3,
                    ally ? $"{target}多了{n}点护盾！让盾先替讨伐队挨几下。" : $"{target}多了{n}点护盾，讨伐队得先敲掉它。",
                    ally ? $"盾撑起来了！{target}新添{n}点缓冲。" : $"对面在护{target}，刚添了{n}点盾。");
            }
            if (e.TargetHealth > 0 && e.StatusStacks != null)
                foreach (var status in e.StatusStacks)
                {
                    int old = 0;
                    if (previous != null)
                        foreach (var stack in previous)
                            if (stack.Kind == status.Kind) { old = stack.Layers; break; }
                    if (status.Layers > old) ExplainStatus(status, target, ally, e.Detail, now);
                }
            if (e.ActionKind != ActionKind.Attack) return;
            int lost = beforeHealth - e.TargetHealth;
            if (beforeShield > 0 && e.TargetShield == 0 && e.ShieldAbsorbed > 0)
                Say("break", ally ? TellerMood.Warn : TellerMood.Cheer, 6,
                    ally ? $"{target}的盾碎了！再来一击就得靠血量扛。" : $"{target}的盾碎了！讨伐队，机会来了！",
                    ally ? $"当心{target}！挡在身前的护盾已经耗光了。" : $"听这声脆响！{target}的护盾被打穿了。");
            else if (lost == 0 && e.ShieldAbsorbed > 0)
                Say("block", ally ? TellerMood.Relief : TellerMood.Wince, 4,
                    ally ? $"盾把{e.ShieldAbsorbed}点伤害全接住了！{target}没掉血。" : $"可惜，{target}的盾把这下挡严实了。",
                    ally ? $"{target}血条没动！刚才是护盾扛下了这一击。" : $"打在盾上了，{target}的血量没动。");
            if (lost <= 0) return;
            if (e.TargetHealth > 0 && e.MaximumHealth > 0 && e.TargetHealth * 3L <= e.MaximumHealth && beforeHealth * 3L > e.MaximumHealth)
                Say("low", ally ? TellerMood.Warn : TellerMood.Cheer, 8,
                    ally ? $"{target}只剩{e.TargetHealth}点生命！我都不敢眨眼了。" : $"盯住{target}，只剩{e.TargetHealth}点生命了！",
                    ally ? $"{target}撑住！血量已经见底，还剩{e.TargetHealth}点。" : $"{target}的血线压下去了，只剩{e.TargetHealth}点！");
            if (previous != null && e.DamageTaken.Count > 0)
            {
                bool wet = false, frozen = false;
                foreach (var status in previous)
                {
                    wet |= status.Kind == StatusKind.Wet;
                    frozen |= status.Kind == StatusKind.Frozen;
                }
                foreach (var damage in e.DamageTaken)
                {
                    if (wet && damage.Type == DamageType.Lightning)
                        Say("conduct", ally ? TellerMood.Warn : TellerMood.Cheer, 7,
                            ally ? $"{target}还湿着！雷打上去多算2点，难怪血条掉得快。" : $"好一个借水引雷！{target}身上有潮湿，这道雷多算2点。",
                            ally ? $"看{target}身上的水！刚才那道雷吃到了潮湿加成。" : $"讨伐队赶上好时候，雷正落在湿透的{target}身上！");
                    if (frozen && damage.Type == DamageType.Fire)
                        Say("shatter", ally ? TellerMood.Wince : TellerMood.Cheer, 7,
                            ally ? $"糟了，{target}冻着还挨了火，冰冻把这记火伤放大了！" : $"讨伐队这一下漂亮！{target}还冻着，火打上去格外疼。");
                }
            }
            if (e.PoisonDamage)
            {
                Say("poison-tick", ally ? TellerMood.Wince : TellerMood.Cheer, 5,
                    ally ? $"毒发了！{target}又掉{lost}点血。" : $"毒发得正是时候！{target}被毒掉了{lost}点血。",
                    ally ? $"{target}这{lost}点血，是毒在回合末扣的。" : $"看{target}的血条！毒结算，又少{lost}点。"); return;
            }
            if (e.Kind == BattleEventKind.Action || e.DamageTaken.Count > 0)
            {
                // Many passive effects name the victim as the actor. Attribute those to the effect itself.
                string source = actor == null || e.ActorId == e.TargetId ? name : actor;
                if (string.IsNullOrEmpty(source)) return;
                if (ally) Say("hit", TellerMood.Wince, 2, $"{source}这下打掉{target}{lost}点血。挺住！", $"{target}挨了{source}一下，少了{lost}点生命。", $"这一下可不轻松。{target}掉了{lost}点血。", $"我替{target}捏把汗！这一击扣了{lost}点。");
                else Say("hit", TellerMood.Cheer, 2, $"打中了！{target}少了{lost}点生命。", $"漂亮，{source}打掉{target}{lost}点血！", $"这一下我看清了！{target}实打实掉了{lost}点血。", $"好！{target}的血条又短了{lost}点。");
            }
        }

        private static string SkillTip(SkillEffect skill) => skill switch
        {
            SkillEffect.WarCry => "这一声是在攒劲，下一次攻击的伤害翻倍！",
            SkillEffect.BlazingFire => "这一把火烧的是对面全队！",
            SkillEffect.AbsoluteZero => "寒气能冻住最多两个目标。盯紧接下来的那一击！",
            SkillEffect.Sunshine => "这道光通常能给全队回血，戴反叛者耳坠时会变成伤害。",
            SkillEffect.StoneGuard => "下一次超过7点的重伤，就靠它挡了。",
            SkillEffect.HealingHands => "这是一次单人急救，看看血条实际补上多少。",
            SkillEffect.SweepNextAttack => "先看起手！等下一次攻击，打的就是整个敌阵。",
            SkillEffect.LightningWhip => "敌人站着的越多，这束雷在同一目标上抽得越多！",
            SkillEffect.ToxicMist => "攻击力越高，这一口毒叠得越厚。带巫医玩偶的人可巴不得分一口。",
            SkillEffect.VenomSurge => "把剩下的能量一口气用光！敌阵都沾了毒，施法的人也吞下了一份。",
            SkillEffect.FleshFeast => "吃掉倒下的敌人，既能回血，生命上限还永久多1点。",
            SkillEffect.Sidestep => "这一转身，既添闪避的机会，又能打得更疼。",
            SkillEffect.GoldenBell => "钟声护住全队，这回合的敌方攻击伤害进不来！",
            SkillEffect.FieryEyes => "这是在找全场的破绽，好给下一击加力。",
            SkillEffect.CrimsonReap => "这一击穿过双防，打掉的血还能吸回来。",
            SkillEffect.HeroicAnthem => "这首歌给全队挂纯净，身上的其他状态也会清掉。",
            SkillEffect.Intimidate => "这是要震晕整个敌阵，中了的人就没法行动。",
            SkillEffect.Earthquake => "别只看落点，紧挨着目标的单位也在震区里。",
            SkillEffect.CloudPiercer => "这一箭穿过双防；目标现在血越多，打得越疼。",
            SkillEffect.HookArrow => "箭绕去找最后一人，挨住了也可能露出破绽。",
            SkillEffect.TwinCover => "护盾连着身后那位；身后队友半血或更低时，拿到的盾更厚。",
            SkillEffect.FieldMedic => "先拔毒、解诅咒，再把血补回来。",
            SkillEffect.ColdTide => "这一波先打魔法伤害，再给敌阵挂寒冷。",
            SkillEffect.BronzeMirror => "这层盾会反震，削掉多少盾就弹回多少伤害。",
            SkillEffect.NightWatch => "灯亮先回一口血，接下来两个回合开头还有。",
            SkillEffect.SiegeBolt => "箭先拆盾，旁边站着的也躲不开波及。",
            SkillEffect.Intercept => "这是在替队友接单体攻击，最多能替三次。",
            SkillEffect.BackPay => "欠的工钱还有人惦记！倒下的队友能带盾回来。",
            SkillEffect.ThunderVerdict => "雷还没落！施法者得清醒地撑到下回合开头。",
            SkillEffect.BlockadeSalvo => "炮口瞄的是最后两人，挨住炮火也会露破绽。",
            SkillEffect.DeepwaterSurge => "被浪头打中的人吃的寒冷最多，其余人也会沾上。",
            SkillEffect.BurialEdict => "这道敕令先打最前面的人，诅咒却要落在整个敌阵。",
            SkillEffect.LongNightPrayer => "先清负面状态，再给站着的人补血、上盾。",
            SkillEffect.ExpressClearance => "最前两人都在轨道上，挨过这一击还得提防破绽。",
            SkillEffect.MeridianTransit => "这道术式扫过整个敌阵，后排也在里面！",
            SkillEffect.AnnealingOrder => "还站着的人一起回血上盾，一下就能稳住阵脚。",
            _ => null
        };

        private void ExplainStatus(StatusStack stack, string target, bool ally, string detail, double now)
        {
            var rule = StatusRules.For(stack.Kind);
            bool good = ally != rule.Negative;
            var mood = good ? TellerMood.Explain : TellerMood.Warn;
            var effect = stack.Kind switch
            {
                StatusKind.Frozen when detail == "潮湿遇寒冷" => "潮湿碰上寒冷，直接冻住了！解冻前动不了，下一击打上去伤害翻倍。",
                StatusKind.Frozen => "冻住了！解冻前动不了，挨的下一击会翻倍，火焰更疼。",
                StatusKind.ForestMark => $"已经挂上{stack.Layers}层森之印，森语巡猎下一击还会更疼。",
                StatusKind.Wet => "浑身湿透了，再吃雷要多挨2点；沾上寒冷就会结冰。",
                StatusKind.Evaporation => "身上冒起了蒸汽，下一次攻击伤害减半。",
                StatusKind.Chill => "沾上寒冷了，凑足三层或碰到潮湿都会结冰。",
                StatusKind.Poison => $"已经叠到{stack.Layers}层毒，回合末还得挨毒伤。",
                StatusKind.Cursed => "中了诅咒，治疗暂时补不进去。",
                StatusKind.Exposed => "露了破绽，下一次挨打会多吃一半伤害。",
                StatusKind.Stunned => "被震晕了，一时出不了手。",
                StatusKind.Thorns => "披上了荆棘，出手攻击的人也会吃反伤。",
                StatusKind.Purity => "身上只留纯净，别的状态都挂不住了。",
                StatusKind.Undying => "有不灭护身，生命最低还能留1点。",
                StatusKind.Resolve => "有坚毅护身，现在挨打只受一半伤害。",
                StatusKind.Weakened => "陷入萎靡，攻击力少了2点。",
                StatusKind.Ember => "攒起了余烬，下次攻击会带上火伤。",
                StatusKind.Injured => "带伤出手，现在有可能打空。",
                StatusKind.Swift => "有迅捷在身，有机会避开来招。",
                StatusKind.Greed => "添了贪婪，此后再挂状态，层数也会跟着涨。",
                _ => $"叠到了{stack.Layers}层盛气，每层加1点攻击力。"
            };
            Offer("status/" + stack.Kind + ally, mood, stack.Kind is StatusKind.Frozen or StatusKind.Stunned ? 7 : 5, now,
                (good ? "机会！" : "当心！") + target + effect, (good ? "这可帮了讨伐队！" : "讨伐队得留个心眼。") + target + effect);
        }
    }
}
