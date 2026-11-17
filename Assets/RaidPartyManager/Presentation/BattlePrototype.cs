using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattlePrototype : MonoBehaviour
    {
        private enum Phase { Preparation, Playback, RoundBreak, Settlement }
        private const int VisibleBackpackItems = 16;

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private Texture2D backpackIcon;
        [SerializeField] private bool randomizeLineups = true;
        [SerializeField, Min(0.05f)] private float attackAnimationTimeScale = 1f;

        private static PartyRun playModeRun;
        private static PartyRun playModeEnemies;
        private static bool partyEdited;
        private static bool enemiesEdited;
        private readonly System.Random draftRandom = new System.Random();
        private bool initialized;

        private readonly Dictionary<string, int> health = new Dictionary<string, int>();
        private readonly Dictionary<string, int> shields = new Dictionary<string, int>();
        private readonly Dictionary<string, IReadOnlyList<StatusStack>> statuses = new Dictionary<string, IReadOnlyList<StatusStack>>();
        private readonly Dictionary<string, Stats> partyStats = new Dictionary<string, Stats>();
        private readonly Dictionary<string, (CharacterDefinition Character, int MaximumHealth)> appearances =
            new Dictionary<string, (CharacterDefinition, int)>();
        private readonly Dictionary<string, BattleUnitCard> unitCards = new Dictionary<string, BattleUnitCard>();
        private readonly Dictionary<string, (Label Attack, Label Defense, Label SpecialDefense)> equipmentStats =
            new Dictionary<string, (Label Attack, Label Defense, Label SpecialDefense)>();
        private readonly Dictionary<string, BattleSpriteView> battleSprites = new Dictionary<string, BattleSpriteView>();
        private readonly List<string> battleLog = new List<string>();
        private readonly List<BattleEffectPlayback> impactEffects = new List<BattleEffectPlayback>();

        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private PartyRun run;
        private PartyRun enemyRun;
        private IReadOnlyList<Combatant> preparedEnemies;
        private Phase phase;
        private BattleSession battle;
        private BattleLoadout battleLoadout;
        private IReadOnlyList<BattleEvent> roundEvents;
        private BattleResult result;
        private EquipmentInstance reward;
        private int eventIndex;
        private BattleActionPlayback actionPlayback;
        private BattleEvent visualAction;
        private bool waitingForImpact;
        private VisualElement actionLayer;
        private ScrollView logView;
        private BattleCharacterDetails characterDetails;
        private BattleStatusTooltip statusTooltip;
        private EquipmentTooltip equipmentTooltip;
        private VisualElement equipmentTooltipAnchor;
        private VisualElement inspectedPortrait;
        private string inspectedMember;
        private Vector2 inspectionPointer;

        // A new Play session must start fresh even when Unity skips domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            playModeRun = playModeEnemies = null;
            partyEdited = enemiesEdited = false;
        }

        private void OnEnable()
        {
            if (initialized)
                Initialize();
        }

        private void Start()
        {
            initialized = true;
            Initialize();
        }

        private void Initialize()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            if (encounter == null)
            {
                root.Add(new Label("Assign the prototype encounter asset to BattlePrototype."));
                return;
            }

            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            statusTooltip = new BattleStatusTooltip(root);

            content = encounter.ToData();
            run = playModeRun ??= new PartyRun(RosterStart(Side.Party), content.StartingEquipment);
            enemyRun = playModeEnemies ??= new PartyRun(RosterStart(Side.Enemy), Array.Empty<EquipmentDefinition>());
            ResetDemo();
        }

        private IEnumerable<PartyMemberStart> RosterStart(Side side)
        {
            var formation = randomizeLineups ? content.Draft(side, draftRandom) : side == Side.Party ? content.Party : content.Enemies;
            return formation.Select(unit => new PartyMemberStart(
                unit.Id, unit.Character, unit.Position, unit.Health, unit.StartingStatuses));
        }

        private void OnDisable()
        {
            ClearAction();
            ClearImpactEffects();
            statusTooltip?.Hide();
            HideEquipmentTooltip();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void ResetDemo(bool reroll = false)
        {
            ClearAction();
            ClearImpactEffects();
            if (reroll && randomizeLineups && !partyEdited)
                run.ResetRoster(RosterStart(Side.Party));
            else
                run.ResetEncounter();
            if (reroll && randomizeLineups && !enemiesEdited)
                enemyRun.ResetRoster(RosterStart(Side.Enemy));
            preparedEnemies = enemyRun.CaptureFormation(Side.Enemy);
            phase = Phase.Preparation;
            battle = null;
            battleLoadout = null;
            roundEvents = null;
            result = null;
            reward = null;
            health.Clear();
            shields.Clear();
            statuses.Clear();
            partyStats.Clear();
            appearances.Clear();
            battleSprites.Clear();
            battleLog.Clear();
            Render();
        }

        private void StartBattle()
        {
            if (phase != Phase.Preparation || !run.CanStartBattle)
                return;
            var request = run.BeginBattle(preparedEnemies, content.TickLimit, content.VictoryReward,
                content.RecruitmentPool, content.CharacterCatalog);
            battle = new BattleSimulator().Start(request);
            ReadPartyStats();
            foreach (var unit in battle.Formation)
            {
                health[unit.Id] = unit.Health;
                appearances[unit.Id] = (unit.Character, unit.Stats.Health);
            }

            BeginNextRound();
        }

        private void BeginNextRound()
        {
            roundEvents = battle.AdvanceRound();
            if (battle.Round == 1 && battle.OpeningLoadout != null)
                ApplyLoadout(battle.OpeningLoadout);
            foreach (var unit in battle.Formation)
            {
                health.TryAdd(unit.Id, unit.Health);
                appearances.TryAdd(unit.Id, (unit.Character, unit.Stats.Health));
            }
            eventIndex = 0;
            phase = Phase.Playback;
            battleLog.Add($"第 {battle.Round} 回合");
            Render();
        }

        private void ContinueRound()
        {
            if (phase == Phase.RoundBreak)
                BeginNextRound();
        }

        private void SwapBetweenRounds(int from, int to)
        {
            if (phase == Phase.RoundBreak && battle.TrySwapPartyPositions(from, to))
            {
                ReadPartyStats();
                BeginNextRound();
            }
        }

        private void ReadPartyStats()
        {
            foreach (var entry in battle.ReadPartyStats())
                partyStats[entry.Key] = entry.Value;
        }

        private void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            if (phase == Phase.Playback)
                AdvancePlayback(now);
            foreach (var sprite in battleSprites.Values)
                if (sprite.panel != null)
                    sprite.UpdateFrame(now);
            if (actionPlayback != null)
                actionPlayback.Draw(now, actionLayer, unitCards.GetValueOrDefault(visualAction.ActorId),
                    unitCards.GetValueOrDefault(visualAction.TargetId));
            for (int i = impactEffects.Count - 1; i >= 0; i--)
            {
                var effect = impactEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    impactEffects.RemoveAt(i);
                }
            }
        }

        private void AdvancePlayback(double now)
        {
            if (actionPlayback != null && !waitingForImpact && now >= actionPlayback.FinishAt)
                ClearAction();

            while (eventIndex < roundEvents.Count)
            {
                var entry = roundEvents[eventIndex];
                bool actionLogged = false;
                if (entry.Kind == BattleEventKind.Action || entry.AttackReach != AttackReach.Unspecified)
                {
                    if (waitingForImpact)
                    {
                        if (now < actionPlayback.ImpactAt)
                            return;
                        actionPlayback.HandleImpact();
                        waitingForImpact = false;
                        actionLogged = true;
                        if (now >= actionPlayback.FinishAt)
                            ClearAction();
                    }
                    else
                    {
                        if (HasActiveAnimation(now))
                            return;
                        if (BeginAction(entry, now))
                        {
                            RecordBattleLine(Describe(entry), entry.Kind);
                            return;
                        }
                    }
                }
                if (entry.Kind == BattleEventKind.Finished && HasActiveAnimation(now))
                    return;
                eventIndex++;

                if (entry.Kind == BattleEventKind.Finished)
                {
                    result = battle.Result;
                    if (!run.TrySettle(result, out reward))
                        throw new InvalidOperationException("The active replay could not be settled.");
                    phase = Phase.Settlement;
                    Render();
                    return;
                }

                string line = actionLogged ? null : Describe(entry);
                if (entry.PartyStatChanges != null)
                    foreach (var change in entry.PartyStatChanges)
                    {
                        partyStats[change.Key] = change.Value;
                        RefreshEquipmentStats(change.Key, change.Value);
                    }
                if (entry.Loadout != null)
                    ApplyLoadout(entry.Loadout);
                if (entry.Kind != BattleEventKind.Defeated)
                {
                    if (entry.ActionKind == ActionKind.Attack &&
                        (entry.Amount > 0 || entry.TargetShield < shields.GetValueOrDefault(entry.TargetId)) &&
                        battleSprites.TryGetValue(entry.TargetId, out var targetSprite) && targetSprite.panel != null)
                        targetSprite.PlayHit(now);

                    health[entry.TargetId] = entry.TargetHealth;
                    shields[entry.TargetId] = entry.TargetShield;
                    statuses[entry.TargetId] = entry.StatusStacks;
                    if (entry.Form != null)
                    {
                        appearances[entry.TargetId] = (entry.Form, entry.MaximumHealth);
                        Render();
                    }
                    else if (entry.Loadout != null)
                        Render();
                    else
                        unitCards[entry.TargetId].Refresh(entry.TargetHealth, entry.TargetShield, entry.StatusStacks);
                    if (entry.Detail != "未命中" && EffectArt(entry) is Texture2D effect)
                        impactEffects.Add(new BattleEffectPlayback(entry.TargetId, effect, now, AnimationScale));
                }

                if (!actionLogged)
                    RecordBattleLine(line, entry.Kind);
                if (entry.TargetId == inspectedMember || entry.PartyStatChanges != null)
                    RefreshCharacterDetails();
            }

            if (HasActiveAnimation(now))
                return;

            ReadPartyStats();
            phase = Phase.RoundBreak;
            Render();
        }

        private bool HasActiveAnimation(double now) => actionPlayback != null ||
            impactEffects.Any(effect => effect.ActiveAt(now)) ||
            battleSprites.Values.Any(sprite => sprite.panel != null && sprite.IsReactingAt(now));

        private float AnimationScale => Mathf.Max(0.05f, attackAnimationTimeScale);

        private Texture2D EffectArt(BattleEvent entry) =>
            entry.VisualCue != BattleVisualCue.None && entry.ActorId != null && appearances.TryGetValue(entry.ActorId, out var actor)
                ? content.BattleEffectFor(actor.Character.Id, entry.VisualCue) : null;

        private bool BeginAction(BattleEvent entry, double now)
        {
            if (entry.ActorId == null || !appearances.TryGetValue(entry.ActorId, out var appearance))
                return false;

            var character = appearance.Character;
            var sprite = battleSprites.GetValueOrDefault(entry.ActorId);
            if (sprite?.panel == null)
                sprite = null;
            Texture2D walking = null;
            Texture2D projectile = null;
            if (entry.ActionKind == ActionKind.Attack && entry.ActorId != entry.TargetId)
            {
                if (entry.AttackReach == AttackReach.Melee)
                    walking = sprite != null ? content.BattleWalkFor(character.Id) : null;
                else
                    projectile = content.ProjectileFor(character);
            }
            if (sprite == null && projectile == null)
                return false;

            actionPlayback = new BattleActionPlayback(sprite, entry.ActionKind, walking, projectile,
                character.Profession == Profession.Archer, entry.Detail == "未命中", now, AnimationScale,
                content.BattleSpecialFor(character.Id, entry.VisualCue), EffectArt(entry) != null,
                entry.VisualCue == BattleVisualCue.CrossbowCritical);
            visualAction = entry;
            waitingForImpact = true;
            return true;
        }

        private void ClearAction()
        {
            actionPlayback?.Clear();
            actionPlayback = null;
            waitingForImpact = false;
        }

        private void ClearImpactEffects()
        {
            foreach (var effect in impactEffects)
                effect.Clear();
            impactEffects.Clear();
        }

        private void RecordBattleLine(string line, BattleEventKind kind)
        {
            battleLog.Add(line);
            AppendLog(line, kind);
        }

        private void ApplyLoadout(BattleLoadout loadout)
        {
            if (!run.TryApplyBattleLoadout(battle.Id, loadout))
                throw new InvalidOperationException("Battle equipment no longer matches the party inventory.");
            battleLoadout = loadout;
        }

        private string Describe(BattleEvent entry)
        {
            string target = appearances[entry.TargetId].Character.Name;
            if (entry.Kind == BattleEventKind.FormChanged)
                return $"[{entry.Tick:000}] {target} · {entry.ActionName} → {entry.Form.Name}";
            if (entry.Kind == BattleEventKind.Defeated)
                return $"[{entry.Tick:000}] {target} 倒下";
            string actor = entry.ActorId != null ? appearances[entry.ActorId].Character.Name : null;
            if (entry.Form != null)
                target = entry.Form.Name;
            string source = actor != null ? $"{actor} · " : "";
            string amount = entry.ActionKind == ActionKind.Heal ? $"恢复 {entry.Amount} 生命" :
                entry.ActionKind == ActionKind.Shield ? $"获得 {entry.Amount} 护盾" : $"损失 {entry.Amount} 生命";
            string detail = entry.Detail != null
                ? entry.Amount > 0 ? $"{amount} · {entry.Detail}" : entry.Detail : amount;
            return $"[{entry.Tick:000}] {source}{entry.ActionName} → {target} · {detail}  {ReasonText(entry.Reason)}";
        }

        private void Render()
        {
            Vector2? previousInspection = inspectedPortrait != null ? inspectionPointer : null;
            HideCharacterDetails();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Clear();
            unitCards.Clear();
            equipmentStats.Clear();
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.color = PrototypeStyle.Ink;

            var page = new ScrollView { name = "prototype" };
            page.style.flexGrow = 1;
            page.verticalScroller.valueChanged += _ => HideEquipmentTooltip();
            page.horizontalScroller.valueChanged += _ => HideEquipmentTooltip();
            page.contentContainer.style.paddingTop = 16;
            page.contentContainer.style.paddingBottom = 16;
            page.contentContainer.style.paddingLeft = 22;
            page.contentContainer.style.paddingRight = 22;
            root.Add(page);

            var header = PrototypeStyle.Row();
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            var heading = new VisualElement();
            heading.Add(PrototypeStyle.Text("讨伐队经理", 29));
            heading.Add(PrototypeStyle.Text(content.Name, 13, PrototypeStyle.Muted));
            header.Add(heading);
            var tools = PrototypeStyle.Row();
            tools.style.alignItems = Align.Center;
            var phaseLabel = PrototypeStyle.Text(phase switch
            {
                Phase.Preparation => "战前准备",
                Phase.Playback => $"第 {battle.Round} 回合",
                Phase.RoundBreak => $"第 {battle.Round} 回合结束",
                _ => "战斗结算"
            }, 14, PrototypeStyle.Accent);
            phaseLabel.name = "battle-phase";
            phaseLabel.style.marginRight = 8;
            tools.Add(phaseLabel);
            if (phase == Phase.Preparation || phase == Phase.Settlement)
            {
                tools.Add(PrototypeStyle.Button("角色编辑", "open-party-editor", OpenPartyEditor));
                if (content.EnemyRoster.Count > 0)
                    tools.Add(PrototypeStyle.Button("敌方编辑", "open-enemy-editor", OpenEnemyEditor));
            }
            if (phase == Phase.Preparation)
            {
                var start = PrototypeStyle.Button("开始自动战斗", "start", StartBattle, true);
                start.SetEnabled(run.CanStartBattle);
                tools.Add(start);
            }
            if (phase == Phase.RoundBreak)
                tools.Add(PrototypeStyle.Button("下回合", "next-round", ContinueRound, true));
            if (phase == Phase.Settlement)
                tools.Add(PrototypeStyle.Button("重置演练", "reset", () => ResetDemo(reroll: true), true));
            tools.Add(PrototypeSceneMenu.ReturnButton());
            header.Add(tools);
            page.Add(header);

            var arena = PrototypeStyle.Panel();
            arena.name = "battlefield";
            if (phase == Phase.RoundBreak)
            {
                var hint = PrototypeStyle.Text("长按一名队员，拖到另一名存活队员上交换站位。", 13, PrototypeStyle.Muted);
                hint.style.marginBottom = 8;
                arena.Add(hint);
            }
            var opposingLines = PrototypeStyle.Row();
            opposingLines.style.alignItems = Align.Stretch;
            opposingLines.Add(BuildLine(Side.Party));
            var divider = PrototypeStyle.Text("◆", 11, PrototypeStyle.Accent);
            divider.style.width = 24;
            PrototypeStyle.Middle(divider);
            opposingLines.Add(divider);
            opposingLines.Add(BuildLine(Side.Enemy));
            arena.Add(opposingLines);
            actionLayer = new VisualElement { name = "battle-action-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(actionLayer);
            arena.Add(actionLayer);
            page.Add(arena);

            RenderFooter(page, previousInspection);
            if (phase == Phase.Settlement)
                RenderSettlement(page);
        }

        private void OpenPartyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Clear();
            root.Add(new PartyEditorView(run, content.CharacterCatalog, EditorPosition, content.PortraitFor, content.IconFor,
                Render, edited: () => partyEdited = true));
        }

        private void OpenEnemyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Clear();
            root.Add(new PartyEditorView(enemyRun, content.EnemyRoster, member => member.Position, content.PortraitFor,
                content.IconFor, () =>
                {
                    preparedEnemies = enemyRun.CaptureFormation(Side.Enemy);
                    Render();
                }, Side.Enemy, () => enemiesEdited = true));
        }

        private int? EditorPosition(MemberView member)
        {
            if (member.Position.HasValue || phase != Phase.Settlement)
                return member.Position;

            // Settlement removes defeated members from the formation. Keep their last battle slots in the editor.
            return member.Health == 0 ? battle.Formation.FirstOrDefault(unit => unit.Side == Side.Party && unit.Id == member.Id)?.Position : null;
        }

        private VisualElement BuildLine(Side side)
        {
            bool isParty = side == Side.Party;
            var line = new VisualElement();
            line.style.flexGrow = 1;
            line.style.flexBasis = 0;
            line.style.minWidth = 0;
            bool hideEnemies = !isParty && phase == Phase.Preparation &&
                !run.Members.Any(member => member.Health > 0 && member.Character.Trait == CharacterTrait.RevealEnemies);
            var title = PrototypeStyle.Text(isParty ? "己方阵列" : hideEnemies ? "敌方阵列 · 待侦察" : "敌方阵列", 13,
                isParty ? PrototypeStyle.Ally : PrototypeStyle.Enemy);
            title.style.unityTextAlign = isParty ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            title.style.marginBottom = 7;
            line.Add(title);

            var positions = PrototypeStyle.Row(isParty ? "party-positions" : "enemy-positions");
            var drag = isParty && phase == Phase.RoundBreak
                ? new FormationSwap(positions, (from, to) => SwapBetweenRounds(from.Position.Value, to.Position.Value))
                : null;
            for (int index = 0; index < Rules.Positions; index++)
            {
                int slot = isParty ? Rules.Positions - index : index + 1;
                var card = hideEnemies
                    ? new BattleUnitCard(side, slot, null, null, null, 0, 0, 0, null, hidden: true)
                    : PositionCard(side, slot);
                positions.Add(card);
                if (drag != null && card.CanSwap && !battle.IsPositionLocked(card.Slot))
                    drag.Register(new FormationSwap.Slot
                    {
                        View = card, Border = card.Q("combatant-portrait"), Position = card.Slot,
                        Artwork = card.Artwork, Name = card.CharacterName, CanDrag = true, CanDrop = true,
                        Borderless = card.HasBattleSprite
                    });
            }
            line.Add(positions);
            return line;
        }

        private BattleUnitCard PositionCard(Side side, int position)
        {
            bool preparing = phase == Phase.Preparation;
            string id;
            CharacterDefinition character;
            int currentHealth;
            int maximumHealth;
            if (preparing && side == Side.Party)
            {
                var member = run.Members.FirstOrDefault(candidate => candidate.Position == position);
                id = member?.Id;
                character = member?.Character;
                currentHealth = member?.Health ?? 0;
                maximumHealth = member?.EffectiveStats.Health ?? 0;
            }
            else
            {
                var units = preparing ? preparedEnemies : battle.Formation;
                var unit = units.FirstOrDefault(candidate => candidate.Side == side && candidate.Position == position);
                id = unit?.Id;
                var appearance = id != null && !preparing ? appearances[id] : (unit?.Character, unit?.Stats.Health ?? 0);
                character = appearance.Item1;
                currentHealth = unit == null ? 0 : preparing ? unit.Health : health[id];
                maximumHealth = appearance.Item2;
            }

            string subtitle = character == null ? null : preparing && side == Side.Enemy && character.Skill != null
                ? character.Skill.Name : PersonalityText(character.Personality);
            int shield = !preparing && phase != Phase.Settlement && id != null ? shields.GetValueOrDefault(id) : 0;
            BattleSpriteView sprite = null;
            var sheet = content.BattleSpriteFor(character?.Id);
            if (sheet != null)
            {
                if (!battleSprites.TryGetValue(id, out sprite) || sprite.Sheet != sheet)
                    battleSprites[id] = sprite = new BattleSpriteView(sheet, content.BattleAttackFor(character.Id),
                        character.Profession, position, side == Side.Enemy);
                sprite.RemoveFromHierarchy();
            }
            var card = new BattleUnitCard(side, position, id, character, content.PortraitFor(character?.Id),
                maximumHealth, currentHealth, shield, subtitle,
                statusIcon: content.StatusIcon, battleSprite: sprite, statusTooltip: statusTooltip);
            if (id != null)
            {
                card.Refresh(currentHealth, shield, statuses.GetValueOrDefault(id));
                unitCards[id] = card;
            }
            return card;
        }

        private void RenderFooter(VisualElement page, Vector2? previousInspection)
        {
            var bottom = PrototypeStyle.Row("battle-footer");
            bottom.style.alignItems = Align.Stretch;
            var roster = PrototypeStyle.Panel();
            roster.name = "roster-equipment";
            roster.style.flexGrow = 7;
            roster.style.flexBasis = 0;
            roster.style.minWidth = 0;
            roster.style.marginRight = 10;

            var members = run.Members;
            var inventory = run.Inventory;
            var equipped = new HashSet<string>(members.SelectMany(member => member.EquipmentIds));
            roster.Add(BuildBackpackStrip(inventory.Where(item => !equipped.Contains(item.Id)).Take(VisibleBackpackItems)));
            var cards = PrototypeStyle.Row("equipment-positions");
            for (int position = Rules.Positions; position >= 1; position--)
            {
                string id = phase == Phase.Preparation
                    ? members.FirstOrDefault(member => member.Position == position)?.Id
                    : battle.Formation.FirstOrDefault(unit => unit.Side == Side.Party && unit.Position == position)?.Id;
                var member = members.FirstOrDefault(entry => entry.Id == id);
                var character = phase != Phase.Preparation && id != null && appearances.TryGetValue(id, out var appearance)
                    ? appearance.Character : member?.Character;
                var equipment = id != null && battleLoadout != null && battleLoadout.Slots.TryGetValue(id, out var slots)
                    ? slots : member?.EquipmentIds;
                Stats? stats = phase != Phase.Preparation && id != null && partyStats.TryGetValue(id, out var current)
                    ? current : member?.EffectiveStats;
                cards.Add(EquipmentCard(position, id, character, stats, equipment, inventory, previousInspection));
            }
            roster.Add(cards);
            if (phase == Phase.Preparation)
            {
                foreach (var member in members.Where(member => !member.Position.HasValue))
                    roster.Add(PrototypeStyle.Text($"未布阵 · {member.Character.Name}", 12, PrototypeStyle.Muted));
                if (run.HasRecruitmentVacancy && members.Count == Rules.Positions)
                    roster.Add(PrototypeStyle.Text("寻人启事将填补空位。召唤队友存活时会替换一名倒下或未布阵的队员。", 12, PrototypeStyle.Muted));
            }
            bottom.Add(roster);
            bottom.Add(BuildLog());
            page.Add(bottom);
        }

        private VisualElement BuildBackpackStrip(IEnumerable<EquipmentInstance> items)
        {
            var strip = PrototypeStyle.Row("battle-backpack");
            strip.style.alignItems = Align.Center;
            strip.style.marginBottom = 7;
            var icon = new Image { name = "battle-backpack-icon", image = backpackIcon,
                scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = 34;
            icon.style.flexShrink = 0;
            icon.style.marginRight = 8;
            strip.Add(icon);

            var tiles = PrototypeStyle.Row("battle-backpack-items");
            tiles.style.flexGrow = 1;
            tiles.style.minWidth = 0;
            foreach (var item in items)
            {
                var tile = EquipmentTile($"backpack-item-{item.Id}", item.Definition);
                tile.style.marginRight = 3;
                tiles.Add(tile);
            }
            strip.Add(tiles);
            return strip;
        }

        private VisualElement EquipmentCard(int position, string id, CharacterDefinition character, Stats? stats,
            IReadOnlyList<string> equipment, IReadOnlyList<EquipmentInstance> inventory, Vector2? previousInspection)
        {
            var card = PrototypeStyle.Row($"equipment-position-{position}");
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.minWidth = 0;
            card.style.height = 132;
            card.style.marginRight = position == 1 ? 0 : 5;
            card.style.backgroundColor = PrototypeStyle.Inset;
            card.style.overflow = Overflow.Hidden;
            PrototypeStyle.Outline(card, PrototypeStyle.Edge, 3);

            var slots = new VisualElement { name = "equipment-slots" };
            slots.style.width = Length.Percent(24);
            slots.style.maxWidth = 42;
            slots.style.alignItems = Align.Center;
            slots.style.justifyContent = Justify.SpaceBetween;
            slots.style.flexShrink = 0;
            slots.style.paddingTop = slots.style.paddingBottom = 7;
            slots.style.paddingLeft = slots.style.paddingRight = 3;
            var equippedItems = new EquipmentDefinition[Rules.EquipmentSlots];
            for (int slot = 0; slot < Rules.EquipmentSlots; slot++)
            {
                var held = equipment != null ? inventory.FirstOrDefault(item => item.Id == equipment[slot])?.Definition : null;
                equippedItems[slot] = held;
                slots.Add(EquipmentTile($"equipment-slot-{slot + 1}", held, slot + 1));
            }
            card.Add(slots);

            var portrait = new VisualElement { name = $"equipment-portrait-{position}" };
            portrait.style.flexGrow = 1;
            portrait.style.flexBasis = 0;
            portrait.style.minWidth = 0;
            portrait.style.overflow = Overflow.Hidden;
            var art = content.PortraitFor(character?.Id);
            ProfessionFrame.AddTo(portrait, character?.Profession, art);

            var caption = PrototypeStyle.Text(character?.Name ?? $"{position} 号位", 10);
            caption.style.position = Position.Absolute;
            caption.style.left = caption.style.right = caption.style.bottom = 3;
            caption.style.backgroundColor = new Color(0.064f, 0.069f, 0.086f, 0.90f);
            caption.style.paddingBottom = caption.style.paddingTop = 2;
            PrototypeStyle.Middle(caption);
            portrait.Add(caption);
            card.Add(portrait);

            var attributes = new VisualElement { name = "equipment-stats" };
            attributes.style.width = Length.Percent(35);
            attributes.style.flexShrink = 0;
            attributes.style.minWidth = 0;
            attributes.style.justifyContent = Justify.SpaceBetween;
            attributes.style.paddingLeft = attributes.style.paddingRight = 4;
            attributes.style.paddingTop = attributes.style.paddingBottom = 13;
            var attack = AddAttribute("攻击", "equipment-attack");
            var defense = AddAttribute("防御", "equipment-defense");
            var specialDefense = AddAttribute("特防", "equipment-special-defense");
            card.Add(attributes);
            if (id != null)
            {
                equipmentStats[id] = (attack, defense, specialDefense);
                if (stats.HasValue)
                    RefreshEquipmentStats(id, stats.Value);
            }

            if (character != null && (phase == Phase.Playback || phase == Phase.RoundBreak))
                RegisterCharacterInspection(portrait, position, id, character, equippedItems, previousInspection);
            return card;

            Label AddAttribute(string title, string name)
            {
                var row = PrototypeStyle.Row();
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                var label = PrototypeStyle.Text(title, 11, PrototypeStyle.Muted);
                label.style.whiteSpace = WhiteSpace.NoWrap;
                row.Add(label);
                var value = PrototypeStyle.Text("—", 13);
                value.name = name;
                value.style.whiteSpace = WhiteSpace.NoWrap;
                row.Add(value);
                attributes.Add(row);
                return value;
            }
        }

        private void RefreshEquipmentStats(string id, Stats stats)
        {
            if (!equipmentStats.TryGetValue(id, out var values))
                return;
            values.Attack.text = stats.Attack.ToString();
            values.Defense.text = stats.Defense.ToString();
            values.SpecialDefense.text = stats.SpecialDefense.ToString();
        }

        private VisualElement EquipmentTile(string name, EquipmentDefinition item, int? slot = null)
        {
            var view = new VisualElement { name = name };
            view.style.width = view.style.height = 30;
            view.style.minWidth = 0;
            view.style.maxWidth = Length.Percent(100);
            view.style.backgroundColor = item == null ? PrototypeStyle.Inset : PrototypeStyle.PanelColor;
            PrototypeStyle.Outline(view, PrototypeStyle.Edge, 4);
            if (item == null)
            {
                var empty = PrototypeStyle.Text(slot?.ToString() ?? "", 11, PrototypeStyle.Muted);
                empty.pickingMode = PickingMode.Ignore;
                empty.style.flexGrow = 1;
                PrototypeStyle.Middle(empty);
                view.Add(empty);
                return view;
            }

            var artwork = content.IconFor(item.Id);
            view.Add(new EquipmentIcon(item, artwork));
            view.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                    return;
                HideEquipmentTooltip();
                equipmentTooltipAnchor = view;
                equipmentTooltip = new EquipmentTooltip(item, artwork);
                equipmentTooltip.ShowAbove(root, view);
            });
            view.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (equipmentTooltipAnchor == view)
                    HideEquipmentTooltip();
            });
            return view;
        }

        private void HideEquipmentTooltip()
        {
            equipmentTooltip?.RemoveFromHierarchy();
            equipmentTooltip = null;
            equipmentTooltipAnchor = null;
        }

        private void RegisterCharacterInspection(VisualElement portrait, int position, string id, CharacterDefinition character,
            IReadOnlyList<EquipmentDefinition> equipment, Vector2? previousInspection)
        {
            portrait.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                    Show((Vector2)evt.position);
            });
            portrait.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (inspectedPortrait == portrait)
                    inspectionPointer = (Vector2)evt.position;
            });
            portrait.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (inspectedPortrait == portrait)
                    HideCharacterDetails();
            });
            if (previousInspection.HasValue)
                portrait.RegisterCallback<GeometryChangedEvent>(RestoreInspection);

            void RestoreInspection(GeometryChangedEvent evt)
            {
                portrait.UnregisterCallback<GeometryChangedEvent>(RestoreInspection);
                if (inspectedPortrait != null || !portrait.worldBound.Contains(previousInspection.Value))
                    return;
                var target = root.panel?.Pick(previousInspection.Value);
                if (target != null && (target == portrait || portrait.Contains(target)))
                    Show(previousInspection.Value);
            }

            void Show(Vector2 pointer)
            {
                HideCharacterDetails();
                inspectedPortrait = portrait;
                inspectedMember = id;
                inspectionPointer = pointer;
                var details = new BattleCharacterDetails(position, character, equipment);
                characterDetails = details;
                details.RegisterCallback<GeometryChangedEvent>(_ => details.PositionAt(root, portrait));
                root.Add(details);
                RefreshCharacterDetails();
                details.PositionAt(root, portrait);
            }
        }

        private void RefreshCharacterDetails()
        {
            if (characterDetails == null || !partyStats.TryGetValue(inspectedMember, out var stats))
                return;
            characterDetails.Refresh(stats, health[inspectedMember], shields.GetValueOrDefault(inspectedMember),
                statuses.GetValueOrDefault(inspectedMember));
        }

        private void HideCharacterDetails()
        {
            characterDetails?.RemoveFromHierarchy();
            characterDetails = null;
            inspectedPortrait = null;
            inspectedMember = null;
        }

        private void RenderSettlement(VisualElement page)
        {
            var summary = PrototypeStyle.Panel();
            summary.Add(PrototypeStyle.Text(OutcomeText(result.Outcome), 20, PrototypeStyle.Accent));
            summary.Add(PrototypeStyle.Text("剩余生命  " + string.Join("  ·  ", run.Members.Select(member =>
                $"{member.Character.Name} {HealthText(member.Health, member.EffectiveStats.Health)}")), 14));
            var rewardLabel = PrototypeStyle.Text(reward == null ? "本场无装备掉落" :
                $"战利品：{reward.Definition.Name}（{BonusText(reward.Definition.Bonus)}），已放入共享背包", 14);
            rewardLabel.name = "reward";
            summary.Add(rewardLabel);
            if (result.GoldEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.GoldEarned} 金币 · 当前 {run.Gold} 金币", 13, PrototypeStyle.Accent));
            if (result.AccessoriesEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.AccessoriesEarned} 件饰品，已放入共享背包", 13, PrototypeStyle.Accent));
            page.Add(summary);
        }

        private VisualElement BuildLog()
        {
            var panel = PrototypeStyle.Panel();
            panel.style.flexGrow = 3;
            panel.style.flexBasis = 0;
            panel.style.minWidth = 0;
            panel.Add(PrototypeStyle.Text("战斗日志", 17));
            logView = new ScrollView { name = "battle-log" };
            logView.style.height = 150;
            logView.style.marginTop = 6;
            var scroller = logView.verticalScroller;
            scroller.style.width = 11;
            scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
            var track = scroller.slider.Q("unity-tracker");
            var thumb = scroller.slider.Q("unity-dragger");
            if (track != null)
                track.style.backgroundColor = PrototypeStyle.Inset;
            if (thumb != null)
                thumb.style.backgroundColor = PrototypeStyle.Edge;
            panel.Add(logView);
            if (battleLog.Count == 0 && phase == Phase.Preparation)
                logView.Add(PrototypeStyle.Text("战斗开始后显示行动记录。", 12, PrototypeStyle.Muted));
            foreach (string line in battleLog)
                logView.Add(PrototypeStyle.Text(line, 12));
            if (battleLog.Count > 0)
                ScrollLogTo(logView, logView.contentContainer.ElementAt(logView.contentContainer.childCount - 1));
            return panel;
        }

        private void AppendLog(string line, BattleEventKind kind)
        {
            var label = PrototypeStyle.Text(line, 13);
            if (kind == BattleEventKind.Action)
                label.name = "battle-action";
            logView.Add(label);
            ScrollLogTo(logView, label);
        }

        private static void ScrollLogTo(ScrollView log, VisualElement label)
        {
            label.schedule.Execute(() => log.ScrollTo(label));
        }

        private static string ReasonText(DecisionReason reason) => reason switch
        {
            DecisionReason.HighestDamage => "（激进：伤害优先）", DecisionReason.SelfProtection => "（慎重：自保）",
            DecisionReason.FavorAttack => "（鲁莽：先攻）", DecisionReason.WoundedAlly => "（温柔：救援）",
            DecisionReason.FollowAlly => "（配合：追击）", DecisionReason.LowestHealth => "（冷静：低血量目标）",
            _ => ""
        };

        private static string OutcomeText(BattleOutcome outcome) => outcome switch
        {
            BattleOutcome.Victory => "胜利 · 敌方全灭", BattleOutcome.PartyDefeated => "失败 · 讨伐队全灭",
            BattleOutcome.MutualDefeat => "失败 · 双方全灭", BattleOutcome.Timeout => "失败 · 达到战斗时限",
            _ => outcome.ToString()
        };
    }
}
