namespace RaidPartyManager.Domain
{
    public readonly struct StatusStack
    {
        public StatusKind Kind { get; }
        public int Layers { get; }

        public StatusStack(StatusKind kind, int layers)
        {
            Kind = kind;
            Layers = layers;
        }
    }

    public static class StatusRules
    {
        private static readonly (string Name, string Description, bool Negative)[] Entries =
        {
            ("盛气", "每有1层，造成的伤害增加20%", false),
            ("迅捷", "有20%概率躲避敌人的攻击", false),
            ("坚毅", "受到的伤害-50%", false),
            ("不灭", "生命值最低为1", false),
            ("贪婪", "每有1层，获得的状态效果+1", false),
            ("纯净", "清除且无法获得其他状态效果", false),
            ("余烬", "攻击时触发，每有1层，对目标造成额外3点火焰伤害", false),
            ("中毒", "回合结束时，受到等同于中毒层数的伤害", true),
            ("破绽", "受到的下1次伤害+50%，随后减少1层破绽", true),
            ("萎靡", "攻击力减少2点", true),
            ("荆棘", "受到攻击时，对攻击方造成此次伤害50%的物理伤害", false),
            ("眩晕", "无法行动", true),
            ("受伤", "攻击时有20%概率无法命中目标", true),
            ("诅咒", "无法恢复生命值", true),
            ("寒冷", "受到火焰伤害时，每层寒冷使火焰伤害增加50%，随后清空寒冷。累计3层，或与潮湿同时存在时，立即冰冻。", true),
            ("冰冻", "进入冰冻的目标不再会获得寒冷。受到的下一次伤害为2倍，若是火焰伤害则为3倍", true),
            ("潮湿", "受到的雷电伤害+2。每次获得中毒时额外获得1层中毒。受到火焰伤害时，潮湿转为1层蒸发。只要同时带有潮湿和寒冷，两种状态立即转为1层冰冻。", true),
            ("蒸发", "下一次攻击造成的伤害减半，随后消耗1层。", true),
        };

        public static (string Name, string Description, bool Negative) For(StatusKind kind) => Entries[(int)kind];
    }
}
