using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattlePrototype : MonoBehaviour
    {
        private enum Phase { Preparation, Entering, Playback, RoundBreak, Settlement }

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private Texture2D battleLogParchment;
        [SerializeField] private Texture2D piercingArrowParticle;
        [SerializeField] private Texture2D[] battleBackgrounds = Array.Empty<Texture2D>();
        [SerializeField] private bool randomizeLineups = true;
        [SerializeField, Min(0.05f)] private float attackAnimationTimeScale = 1f;

        private static PartyRun playModeRun;
        private static readonly Dictionary<EncounterAsset, PartyRun> enemySessions = new();
        private static readonly HashSet<EncounterAsset> editedEnemyEncounters = new();
        private static bool partyEdited;
        private readonly System.Random draftRandom = new System.Random();
        private bool initialized;

        private readonly Dictionary<string, int> health = new Dictionary<string, int>();
        private readonly Dictionary<string, int> shields = new Dictionary<string, int>();
        private readonly Dictionary<string, IReadOnlyList<StatusStack>> statuses = new Dictionary<string, IReadOnlyList<StatusStack>>();
        private readonly Dictionary<string, Dictionary<BattleCountdownKind, int>> countdowns = new();
        private readonly Dictionary<string, Stats> partyStats = new Dictionary<string, Stats>();
        private readonly Dictionary<string, (CharacterDefinition Character, int MaximumHealth)> appearances =
            new Dictionary<string, (CharacterDefinition, int)>();
        private readonly Dictionary<string, BattleUnitCard> unitCards = new Dictionary<string, BattleUnitCard>();
        private static readonly Color AvailableEnergy = new Color32(194, 105, 255, 255);
        private readonly Dictionary<string, BattleEnergy> partyEnergy = new Dictionary<string, BattleEnergy>();
        private readonly Dictionary<string, VisualElement> energyBars = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, BattleSpriteView> battleSprites = new Dictionary<string, BattleSpriteView>();
        private readonly BattleEntrance entrance = new BattleEntrance();
        private readonly List<string> battleLog = new List<string>();
        private readonly List<BattleEffectPlayback> impactEffects = new List<BattleEffectPlayback>();
        private readonly List<DockEnemyEffectPlayback> dockEffects = new List<DockEnemyEffectPlayback>();
        private readonly List<RoundEffectPlayback> roundEffects = new List<RoundEffectPlayback>();
        private readonly List<FloatingCombatNumbers> floatingNumbers = new List<FloatingCombatNumbers>();

        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private Texture2D battleBackground;
        private PartyRun run;
        private PartyRun enemyRun;
        private AdventureRun adventure;
        private IReadOnlyList<Combatant> preparedEnemies;
        private Phase phase;
        private bool pauseRequested;
        private BattleSession battle;
        private BattleLoadout battleLoadout;
        private IReadOnlyList<BattleEvent> roundEvents;
        private BattleResult result;
        private BattleRewards victoryRewards;
        private RunTopBar topBar;
        private bool leavingVictory;
        private bool endingTeamWipe;
        private int eventIndex;
        private BattleActionPlayback actionPlayback;
        private BattleEvent visualAction;
        private bool waitingForImpact;
        private VisualElement actionLayer;
        private VisualElement numberLayer;
        private VisualElement meteorLayer;
        private VisualElement entranceLayer;
        private bool enemyEntranceScheduled;
        private ScrollView logView;
        private double? settingsOpenedAt;
        private double pausedTime;

        private double PlaybackNow => (settingsOpenedAt ?? Time.unscaledTimeAsDouble) - pausedTime;
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
            playModeRun = null;
            enemySessions.Clear();
            editedEnemyEncounters.Clear();
            partyEdited = false;
        }

        internal static PartyRun GetPlaySession(EncounterData content)
        {
            if (playModeRun == null)
            {
                var formation = content.Draft(Side.Party, new System.Random());
                playModeRun = new PartyRun(formation.Select(unit => new PartyMemberStart(
                    unit.Id, unit.Character, unit.Position, unit.Health, unit.StartingStatuses)),
                    content.StartingEquipment.Concat(content.SkillBooks));
            }
            return playModeRun;
        }

        internal static void MarkPartyEdited() => partyEdited = true;

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
            adventure = AdventureFlow.ForScene(SceneManager.GetActiveScene().name);
            run = adventure?.Party ?? (playModeRun ??= new PartyRun(RosterStart(Side.Party),
                content.StartingEquipment.Concat(content.SkillBooks)));
            if (!enemySessions.TryGetValue(encounter, out enemyRun))
                enemySessions.Add(encounter, enemyRun = new PartyRun(RosterStart(Side.Enemy), Array.Empty<EquipmentDefinition>()));
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
            if (adventure == AdventureFlow.Active)
                adventure?.AbortBattle();
            entrance.Clear(unitCards);
            ClearAction();
            ClearImpactEffects();
            statusTooltip?.Hide();
            HideEquipmentTooltip();
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void ResetDemo(bool reroll = false)
        {
            settingsOpenedAt = null;
            pausedTime = 0;
            entrance.Clear(unitCards);
            ClearAction();
            ClearImpactEffects();
            if (adventure != null)
                run.CancelBattle();
            else if (reroll && randomizeLineups && !partyEdited)
                run.ResetRoster(RosterStart(Side.Party));
            else
                run.ResetEncounter();
            if (reroll && randomizeLineups && !editedEnemyEncounters.Contains(encounter))
                enemyRun.ResetRoster(RosterStart(Side.Enemy));
            victoryRewards = adventure?.Current?.Battle?.Reward;
            preparedEnemies = victoryRewards != null ? Array.Empty<Combatant>() :
                adventure != null ? adventure.PrepareEnemies(content.EnemyRoster) : enemyRun.CaptureFormation(Side.Enemy);
            phase = Phase.Preparation;
            pauseRequested = false;
            battle = null;
            battleLoadout = null;
            roundEvents = null;
            result = null;
            leavingVictory = false;
            endingTeamWipe = false;
            health.Clear();
            shields.Clear();
            statuses.Clear();
            countdowns.Clear();
            partyStats.Clear();
            partyEnergy.Clear();
            appearances.Clear();
            battleSprites.Clear();
            battleLog.Clear();
            var backgrounds = battleBackgrounds.Where(texture => texture != null).ToArray();
            battleBackground = backgrounds.Length > 0 ? backgrounds[draftRandom.Next(backgrounds.Length)] : null;
            if (adventure != null)
                battleBackground = backgrounds.FirstOrDefault(texture => texture.name.StartsWith("01-")) ?? battleBackground;
            if (victoryRewards != null)
            {
                phase = Phase.Settlement;
                Render();
                return;
            }
            entrance.Add(run.CaptureFormation(Side.Party), content);
            enemyEntranceScheduled = false;
            RenderPreparation();
        }

        private bool AutoStarts => adventure is { IsInspection: false };

        private void RenderPreparation()
        {
            if (phase == Phase.Preparation && AutoStarts && run.CanStartBattle)
            {
                StartBattle();
                return;
            }
            if (phase == Phase.Preparation && !enemyEntranceScheduled && CanPreviewEnemies())
            {
                entrance.Add(preparedEnemies, content);
                enemyEntranceScheduled = true;
            }
            Render();
        }

        private void StartBattle()
        {
            if (phase != Phase.Preparation || !run.CanStartBattle)
                return;
            var request = run.BeginBattle(preparedEnemies, content.TickLimit, adventure != null ? null : content.VictoryReward,
                content.RecruitmentPool, content.CharacterCatalog);
            adventure?.BindBattle(request.Id);
            battle = new BattleSimulator().Start(request);
            ReadPartyStats();
            ReadPartyEnergy();
            foreach (var unit in battle.Formation)
            {
                health[unit.Id] = unit.Health;
                appearances[unit.Id] = (unit.Character, unit.Stats.Health);
                if (unit.StartingStatuses.Count > 0)
                    statuses[unit.Id] = unit.StartingStatuses.OrderBy(status => status.Key)
                        .Select(status => new StatusStack(status.Key, status.Value)).ToArray();
            }

            if (!enemyEntranceScheduled)
            {
                entrance.Add(battle.Formation.Where(unit => unit.Side == Side.Enemy), content);
                enemyEntranceScheduled = true;
            }

            if (entrance.Active)
            {
                phase = Phase.Entering;
                Render();
            }
            else
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
                if (unit.Side == Side.Party)
                    partyEnergy.TryAdd(unit.Id, BattleEnergy.Full(unit.Equipment));
                appearances.TryAdd(unit.Id, (unit.Character, unit.Stats.Health));
            }
            eventIndex = 0;
            phase = Phase.Playback;
            battleLog.Add($"第 {battle.Round} 回合");
            Render();
        }

        private void ToggleRoundPause()
        {
            if (phase == Phase.RoundBreak)
            {
                ResumeBattle();
                return;
            }

            if (phase is Phase.Entering or Phase.Playback)
            {
                pauseRequested = !pauseRequested;
                UpdatePauseButton(root.Q<Button>("battle-pause"));
            }
        }

        private void ResumeBattle()
        {
            pauseRequested = false;
            BeginNextRound();
        }

        private void SwapBetweenRounds(int from, int to)
        {
            if (phase == Phase.RoundBreak && battle.TrySwapPartyPositions(from, to))
            {
                ReadPartyStats();
                ResumeBattle();
            }
        }

        private void ReadPartyEnergy()
        {
            foreach (var entry in battle.ReadPartyEnergy())
                partyEnergy[entry.Key] = entry.Value;
        }

        private void ReadPartyStats()
        {
            foreach (var entry in battle.ReadPartyStats())
                partyStats[entry.Key] = entry.Value;
        }

        private void Update()
        {
            if (settingsOpenedAt.HasValue)
                return;

            double now = PlaybackNow;
            if (entrance.Active)
                entrance.Show(now, entranceLayer, unitCards);
            if (phase == Phase.Entering && !entrance.Active)
                BeginNextRound();
            else if (phase == Phase.Playback)
                AdvancePlayback(now);
            foreach (var sprite in battleSprites.Values)
                if (sprite.panel != null)
                    sprite.UpdateFrame(now);
            if (actionPlayback != null)
                actionPlayback.Draw(now, actionLayer, meteorLayer, unitCards.GetValueOrDefault(visualAction.ActorId),
                    unitCards);
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
            for (int i = dockEffects.Count - 1; i >= 0; i--)
            {
                var effect = dockEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, unitCards.GetValueOrDefault(effect.ActorId),
                        unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    dockEffects.RemoveAt(i);
                }
            }
            for (int i = roundEffects.Count - 1; i >= 0; i--)
            {
                var effect = roundEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, effect.ActorId != null ? unitCards.GetValueOrDefault(effect.ActorId) : null,
                        unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    roundEffects.RemoveAt(i);
                }
            }
            for (int i = floatingNumbers.Count - 1; i >= 0; i--)
            {
                var numbers = floatingNumbers[i];
                if (numbers.ActiveAt(now))
                    numbers.Draw(numberLayer, unitCards.GetValueOrDefault(numbers.TargetId)?.BodyBounds ?? default, now);
                else
                {
                    numbers.RemoveFromHierarchy();
                    floatingNumbers.RemoveAt(i);
                }
            }
            if (endingTeamWipe && floatingNumbers.Count == 0)
            {
                endingTeamWipe = false;
                PrototypeSceneMenu.OpenStandalone("MainMenu");
            }
        }

        private void AdvancePlayback(double now)
        {
            if (actionPlayback != null && !waitingForImpact && now >= actionPlayback.FinishAt)
                ClearAction();

            while (eventIndex < roundEvents.Count)
            {
                var entry = roundEvents[eventIndex];
                if (entry.Kind == BattleEventKind.RoundStarted)
                {
                    if (HasActiveAnimation(now))
                        return;
                    eventIndex++;
                    continue;
                }
                if (entry.Countdown is { } timer)
                {
                    UpdateCountdown(entry.TargetId, timer, now);
                    eventIndex++;
                    continue;
                }

                bool isRoundEndEffect = RoundEffectPlayback.IsRoundEnd(entry.VisualCue);
                bool isRoundStartEffect = RoundEffectPlayback.IsRoundStart(entry.VisualCue);
                bool showRoundEndEffect = isRoundEndEffect && entry.Detail != "状态未生效" && entry.Detail != "未命中";
                // Status-only changes can settle while an earlier round-end effect is still fading out.
                bool statusChanged = ChangesStatusWithoutAnimation(entry);
                bool waitingForRoundEndEffect = showRoundEndEffect
                    ? actionPlayback != null || roundEffects.Any(effect => !effect.AtRoundStart &&
                        effect.TargetId == entry.TargetId && effect.HoldsTargetAt(now))
                    : !isRoundEndEffect && !statusChanged && entry.Kind != BattleEventKind.Defeated &&
                        roundEffects.Any(effect => !effect.AtRoundStart && effect.ActiveAt(now));
                if (waitingForRoundEndEffect)
                    return;

                bool actionLogged = false;
                bool piercingInFlight = waitingForImpact && actionPlayback?.IsPiercing == true;
                if (piercingInFlight && entry.VisualCue == BattleVisualCue.PiercingArrow && entry.ActorId == visualAction.ActorId)
                {
                    if (now < actionPlayback.PiercingImpactAt(entry.TargetId))
                        return;
                    waitingForImpact = actionPlayback.HasMorePiercingHits(entry.TargetId);
                }
                else if (entry.Kind == BattleEventKind.Action || entry.VisualCue == BattleVisualCue.RaySurge &&
                    entry.Kind != BattleEventKind.EnemyVisual && !piercingInFlight ||
                    entry.SkillBookId == null && entry.AttackReach != AttackReach.Unspecified)
                {
                    if (waitingForImpact)
                    {
                        if (now < actionPlayback.ImpactAt)
                            return;
                        actionPlayback.HandleImpact();
                        waitingForImpact = false;
                        actionLogged = !actionPlayback.LogsAtImpact;
                        if (now >= actionPlayback.FinishAt)
                            ClearAction();
                    }
                    else
                    {
                        if (HasActiveAnimation(now))
                            return;
                        if (BeginAction(entry, now))
                        {
                            if (!actionPlayback.LogsAtImpact)
                                RecordBattleLine(Describe(entry), entry.Kind);
                            return;
                        }
                    }
                }
                if (entry.Kind == BattleEventKind.Finished &&
                    (HasActiveAnimation(now) || !TeamWiped && floatingNumbers.Any(numbers => numbers.ActiveAt(now))))
                    return;
                eventIndex++;

                if (entry.Kind == BattleEventKind.SkillVisual)
                {
                    QueueSkillEffect(entry, now);
                    continue;
                }
                if (entry.Kind == BattleEventKind.EnemyVisual)
                {
                    dockEffects.Add(new DockEnemyEffectPlayback(entry, now, AnimationScale));
                    continue;
                }

                if (entry.Kind == BattleEventKind.Finished)
                {
                    if (TeamWiped)
                    {
                        FinishTeamWipe();
                        return;
                    }

                    SettleBattle();
                    foreach (var member in run.Members)
                    {
                        if (member.Health <= 0 || !result.HealthById.TryGetValue(member.Id, out int finalHealth) || finalHealth != 0)
                            continue;
                        health[member.Id] = member.Health;
                        shields.Remove(member.Id);
                        statuses.Remove(member.Id);
                    }
                    ReadPartyEnergy();
                    phase = Phase.Settlement;
                    if (result.Outcome == BattleOutcome.Victory)
                        ContinueVictory();
                    else
                        Render();
                    return;
                }

                string line = actionLogged ? null : Describe(entry);
                if (entry.ActorEnergy is { } energy)
                {
                    partyEnergy[entry.ActorId] = energy;
                    if (energyBars.TryGetValue(entry.ActorId, out var bar))
                        PaintEnergy(bar, energy);
                }
                if (entry.TargetEnergy is { } targetEnergy)
                {
                    partyEnergy[entry.TargetId] = targetEnergy;
                    if (energyBars.TryGetValue(entry.TargetId, out var bar))
                        PaintEnergy(bar, targetEnergy);
                }
                if (entry.PartyStatChanges != null)
                    foreach (var change in entry.PartyStatChanges)
                        partyStats[change.Key] = change.Value;
                if (entry.Loadout != null)
                    ApplyLoadout(entry.Loadout);
                if (entry.Kind != BattleEventKind.Defeated)
                {
                    int absorbed = Math.Max(0, shields.GetValueOrDefault(entry.TargetId) - entry.TargetShield);
                    if (entry.Kind == BattleEventKind.Retreated && battleSprites.TryGetValue(entry.TargetId, out var departing))
                        departing.PlayRetreat(now, content.BattleWalkFor(appearances[entry.TargetId].Character.Id));
                    if (entry.ActionKind == ActionKind.Attack &&
                        (entry.Amount > 0 || absorbed > 0) &&
                        battleSprites.TryGetValue(entry.TargetId, out var targetSprite) && targetSprite.panel != null)
                        targetSprite.PlayHit(now);

                    int healed = entry.ActionKind == ActionKind.Heal ? entry.Amount :
                        entry.TargetHealth - health.GetValueOrDefault(entry.TargetId, entry.TargetHealth);
                    if (healed > 0 || entry.DamageTaken.Count > 0)
                    {
                        int lane = floatingNumbers.Count(numbers => numbers.TargetId == entry.TargetId && numbers.RecentlyStarted(now));
                        Rect body = unitCards.GetValueOrDefault(entry.TargetId)?.BodyBounds ?? default;
                        floatingNumbers.Add(new FloatingCombatNumbers(entry, healed, body, now, lane));
                    }
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
                        unitCards[entry.TargetId].Refresh(entry.TargetHealth, entry.TargetShield, now, entry.StatusStacks,
                            countdowns.GetValueOrDefault(entry.TargetId));
                    if (entry.Detail != "未命中")
                    {
                        if (showRoundEndEffect || isRoundStartEffect && entry.Detail != "状态未生效")
                        {
                            int lane = isRoundStartEffect
                                ? roundEffects.Count(effect => effect.AtRoundStart && effect.TargetId == entry.TargetId && effect.ActiveAt(now))
                                : 0;
                            roundEffects.Add(new RoundEffectPlayback(entry, absorbed, now, AnimationScale,
                                entry.VisualCue == BattleVisualCue.RoundEndTransfer ? content.IconFor("megaphone") : null, lane));
                        }
                        else if (DockEnemyEffectPlayback.Supports(entry.VisualCue))
                            dockEffects.Add(new DockEnemyEffectPlayback(entry, now, AnimationScale));
                        else if (!isRoundEndEffect && !isRoundStartEffect && EffectArt(entry) is Texture2D effect)
                            impactEffects.Add(new BattleEffectPlayback(entry.TargetId, effect, now, AnimationScale));
                    }
                    QueueSkillEffect(entry, now);
                }

                if (!actionLogged)
                    RecordBattleLine(line, entry.Kind);
                if (entry.TargetId == inspectedMember || entry.PartyStatChanges != null)
                    RefreshCharacterDetails();

                if (entry.TargetHealth == 0 && TeamWiped && battle.Formation
                    .Where(unit => unit.Side == Side.Party).All(unit => health[unit.Id] == 0))
                {
                    FinishTeamWipe();
                    return;
                }
            }

            if (HasActiveAnimation(now))
                return;

            ReadPartyStats();
            if (!pauseRequested)
            {
                BeginNextRound();
                return;
            }

            phase = Phase.RoundBreak;
            Render();
        }

        private void UpdateCountdown(string id, BattleCountdown timer, double now)
        {
            if (!countdowns.TryGetValue(id, out var timers))
                countdowns.Add(id, timers = new Dictionary<BattleCountdownKind, int>());
            if (timer.Remaining == 0)
                timers.Remove(timer.Kind);
            else
                timers[timer.Kind] = timer.Remaining;

            if (unitCards.TryGetValue(id, out var card))
                card.Refresh(health.GetValueOrDefault(id), shields.GetValueOrDefault(id), now, statuses.GetValueOrDefault(id), timers);
            if (inspectedMember == id)
                RefreshCharacterDetails();
        }

        private bool ChangesStatusWithoutAnimation(BattleEvent entry)
        {
            if (entry.Kind != BattleEventKind.Effect || entry.ActionKind != ActionKind.Shield ||
                entry.VisualCue != BattleVisualCue.None || entry.Form != null || entry.Loadout != null)
                return false;

            var previous = statuses.GetValueOrDefault(entry.TargetId) ?? Array.Empty<StatusStack>();
            var current = entry.StatusStacks ?? Array.Empty<StatusStack>();
            return entry.TargetShield != shields.GetValueOrDefault(entry.TargetId) || !previous.SequenceEqual(current);
        }

        private bool TeamWiped => battle.Result?.Outcome is BattleOutcome.PartyDefeated or BattleOutcome.MutualDefeat;

        private void FinishTeamWipe()
        {
            SettleBattle();
            phase = Phase.Settlement;
            if (adventure is { IsInspection: false })
                AdventureFlow.EndWipedRoute();
            topBar.SetEnabled(false);
            endingTeamWipe = true;
        }

        private void SettleBattle()
        {
            result = battle.Result;
            if (result.Outcome != BattleOutcome.Victory)
            {
                if (!run.TrySettle(result, out _) || adventure != null && !adventure.SettleBattle(result))
                    throw new InvalidOperationException("The battle loss could not be settled.");
                return;
            }

            if (!run.TryFinishBattle(result, out var loot))
                throw new InvalidOperationException("The active replay could not be settled.");
            int gold;
            if (adventure == null)
                gold = AdventureRun.RollKillGold(result, battle.Formation.Where(unit => unit.Side == Side.Enemy), draftRandom);
            else if (!adventure.SettleBattle(result, out gold))
                throw new InvalidOperationException("The route encounter could not be settled.");

            int totalGold = (int)Math.Min(int.MaxValue - run.Gold, (long)result.GoldEarned + gold);
            victoryRewards = adventure != null ? adventure.RecordBattleRewards(totalGold, loot) : new BattleRewards(totalGold, loot);
        }

        private bool HasActiveAnimation(double now) => actionPlayback != null ||
            impactEffects.Any(effect => effect.ActiveAt(now)) ||
            dockEffects.Any(effect => effect.ActiveAt(now)) ||
            roundEffects.Any(effect => !effect.AtRoundStart && effect.ActiveAt(now)) ||
            battleSprites.Values.Any(sprite => sprite.panel != null && sprite.IsReactingAt(now));

        private float AnimationScale => Mathf.Max(0.05f, attackAnimationTimeScale);

        private Texture2D EffectArt(BattleEvent entry) =>
            entry.VisualCue != BattleVisualCue.None && entry.ActorId != null && appearances.TryGetValue(entry.ActorId, out var actor)
                ? content.BattleEffectFor(actor.Character.Id, entry.VisualCue) : null;

        private void QueueSkillEffect(BattleEvent entry, double now)
        {
            if (entry.SkillBookId == null || entry.TargetId == null || entry.ActorEnergy.HasValue || entry.Detail == "未命中" ||
                content.SkillEffectFor(entry.SkillBookId) is not Texture2D atlas)
                return;
            if (impactEffects.Any(effect => effect.SkillBookId == entry.SkillBookId &&
                effect.TargetId == entry.TargetId && effect.ActiveAt(now)))
                return;
            impactEffects.Add(new BattleEffectPlayback(entry.TargetId, atlas, now, AnimationScale, entry.SkillBookId));
        }

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
            Texture2D fragments = null;
            IReadOnlyList<BattleEvent> projectileHits = null;
            bool isArrow = character.Profession == Profession.Archer || character.Id == "EnemyForkFisher" ||
                character.Trait == CharacterTrait.EnemyDeckSailor;
            bool isSweep = entry.Kind == BattleEventKind.Action && entry.SkillBookId != null && entry.ActionKind == ActionKind.Attack;
            if (entry.ActionKind == ActionKind.Attack && entry.ActorId != entry.TargetId)
            {
                if (isSweep)
                    (projectile, fragments) = content.SkillProjectileFor(entry.SkillBookId);
                if (projectile != null)
                {
                    isArrow = fragments == null;
                    projectileHits = roundEvents.Skip(eventIndex)
                        .TakeWhile((hit, offset) => offset == 0 || hit.Kind != BattleEventKind.Action)
                        .Where(hit => hit.ActorId == entry.ActorId && hit.SkillBookId == entry.SkillBookId &&
                            hit.ActionKind == ActionKind.Attack)
                        .ToArray();
                }
                else if (entry.AttackReach == AttackReach.Melee)
                    walking = content.BattleWalkFor(character.Id) ?? (sprite?.UsesPortrait == true ? sprite.Sheet : null);
                else if (!isSweep)
                {
                    projectile = content.ProjectileFor(character);
                    if (projectile != null && entry.VisualCue is BattleVisualCue.AdmiralBroadside or BattleVisualCue.PiercingArrow)
                        projectileHits = roundEvents.Skip(eventIndex)
                            .TakeWhile((hit, offset) => offset == 0 || hit.Kind != BattleEventKind.Action)
                            .Where(hit => hit.ActorId == entry.ActorId && hit.VisualCue == entry.VisualCue)
                            .ToArray();
                }
            }
            if (sprite == null && projectile == null)
                return false;

            var book = entry.ActorEnergy.HasValue ? content.SkillBooks.FirstOrDefault(skill => skill.Id == entry.SkillBookId) : null;
            var pose = book?.SkillEffect switch
            {
                SkillEffect.WarCry or SkillEffect.SweepNextAttack or SkillEffect.StoneGuard or SkillEffect.GoldenBell => ActionKind.Shield,
                not null => ActionKind.Heal,
                _ => entry.ActionKind
            };
            actionPlayback = new BattleActionPlayback(sprite, pose, walking, projectile, fragments, isArrow, entry, projectileHits,
                now, AnimationScale, content.BattleSpecialFor(character.Id, entry.VisualCue),
                DockEnemyEffectPlayback.Supports(entry.VisualCue) || EffectArt(entry) != null ||
                entry.SkillBookId != null && book?.SkillEffect != SkillEffect.SweepNextAttack,
                entry.VisualCue == BattleVisualCue.CrossbowCritical, cannon: character.Trait == CharacterTrait.EnemyBlockadeAdmiral,
                piercingParticle: piercingArrowParticle);
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
            foreach (var effect in dockEffects)
                effect.Clear();
            dockEffects.Clear();
            foreach (var effect in roundEffects)
                effect.Clear();
            roundEffects.Clear();
            foreach (var numbers in floatingNumbers)
                numbers.RemoveFromHierarchy();
            floatingNumbers.Clear();
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
            energyBars.Clear();
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.color = PrototypeStyle.Ink;

            var backdrop = new VisualElement { name = "battle-background", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(backdrop);
            if (battleBackground != null)
            {
                backdrop.style.backgroundImage = new StyleBackground(battleBackground);
                backdrop.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                backdrop.style.unityBackgroundImageTintColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            }
            root.Add(backdrop);

            bool defeat = phase == Phase.Settlement && victoryRewards == null;
            bool setup = phase == Phase.Preparation || defeat;
            topBar = new RunTopBar(root, run, content, setup ? OpenPartyEditor : null, actions: BattleControls(defeat),
                settingsChanged: SetSettingsOpen,
                openEnemyEditor: setup && content.EnemyRoster.Count > 0 && adventure == null ? OpenEnemyEditor : null);
            root.Add(topBar);
            if (phase is Phase.Entering or Phase.Playback or Phase.RoundBreak)
            {
                var pause = PrototypeStyle.Button("", "battle-pause", ToggleRoundPause, true);
                pause.style.alignSelf = Align.Center;
                pause.style.flexShrink = 0;
                pause.style.minWidth = 100;
                pause.style.marginTop = 7;
                UpdatePauseButton(pause);
                root.Add(pause);
            }

            var page = new ScrollView { name = "prototype" };
            page.style.flexGrow = 1;
            page.style.minHeight = 0;
            page.verticalScroller.valueChanged += _ => HideEquipmentTooltip();
            page.contentContainer.style.flexGrow = 1;
            page.contentContainer.style.paddingTop = 8;
            page.contentContainer.style.paddingLeft = page.contentContainer.style.paddingRight = 22;
            root.Add(page);

            var arena = new VisualElement { name = "battlefield" };
            arena.style.marginTop = StyleKeyword.Auto;
            arena.style.flexShrink = 0;
            if (phase == Phase.RoundBreak)
            {
                var hint = PrototypeStyle.Text("长按一名队员，拖到另一名存活队员上交换站位。", 13, PrototypeStyle.Muted);
                hint.style.marginBottom = 8;
                arena.Add(hint);
                var locked = battle.Formation.Where(unit => unit.Side == Side.Party)
                    .Select(unit => (unit.Character.Name, Reason: battle.PositionLockReason(unit.Position)))
                    .Where(unit => unit.Reason != null)
                    .Select(unit => $"{unit.Name}（{unit.Reason}）").ToArray();
                if (locked.Length > 0)
                {
                    var notice = PrototypeStyle.Text($"锁定站位  {string.Join("、", locked)}", 13, PrototypeStyle.Accent);
                    notice.name = "formation-lock-reasons";
                    notice.style.marginBottom = 6;
                    arena.Add(notice);
                }
            }
            bool showEnemies = battle != null || phase == Phase.Preparation && CanPreviewEnemies();
            int largestEnemy = showEnemies ? (battle?.Formation ?? preparedEnemies)
                .Where(unit => unit.Side == Side.Enemy).Select(unit => unit.Character.PositionSlots).DefaultIfEmpty(1).Max() : 1;
            float portraitHeight = PortraitHeight(largestEnemy);
            var opposingLines = PrototypeStyle.Row();
            opposingLines.style.alignItems = Align.Stretch;
            opposingLines.Add(BuildLine(Side.Party, portraitHeight));
            var divider = PrototypeStyle.Text("◆", 11, PrototypeStyle.Accent);
            divider.style.width = 24;
            divider.visible = showEnemies;
            PrototypeStyle.Middle(divider);
            opposingLines.Add(divider);
            opposingLines.Add(BuildLine(Side.Enemy, portraitHeight, showEnemies));
            arena.Add(opposingLines);
            actionLayer = new VisualElement { name = "battle-action-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(actionLayer);
            arena.Add(actionLayer);
            entranceLayer = new VisualElement { name = "battle-entrance-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(entranceLayer);
            entranceLayer.style.overflow = Overflow.Hidden;
            arena.Add(entranceLayer);
            page.Add(arena);
            if (entrance.Active)
                entrance.Show(PlaybackNow, entranceLayer, unitCards);

            // A falling meteor can extend above the arena without changing the scrollable area.
            meteorLayer = new VisualElement { name = "battle-meteor-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(meteorLayer);
            meteorLayer.style.top = RunTopBar.Height;
            meteorLayer.style.overflow = Overflow.Hidden;
            root.Add(meteorLayer);

            var footer = BuildFooter(previousInspection);
            root.Add(footer);
            numberLayer = new VisualElement { name = "battle-number-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(numberLayer);
            numberLayer.style.top = RunTopBar.Height;
            root.Add(numberLayer);
            if (defeat)
                RenderDefeat(page);
            else if (phase == Phase.Settlement)
            {
                page.SetEnabled(false);
                footer.SetEnabled(false);
                RenderVictory();
            }
        }

        private void UpdatePauseButton(Button button)
        {
            button.text = phase == Phase.RoundBreak ? "继续" : pauseRequested ? "取消暂停" : "暂停";
            button.tooltip = phase == Phase.RoundBreak ? "继续自动战斗" : pauseRequested ? "已预约回合结束时暂停" : "在当前回合结束时暂停";
        }

        private VisualElement BattleControls(bool defeat)
        {
            var controls = PrototypeStyle.Row("battle-controls");
            controls.style.alignItems = Align.Center;
            controls.style.flexShrink = 0;
            controls.style.marginLeft = 7;
            if (phase == Phase.Preparation)
            {
                if (AutoStarts)
                    controls.Add(PrototypeStyle.Text("请先为在场队员安排站位", 13, PrototypeStyle.Accent));
                else
                {
                    var start = PrototypeStyle.Button("开始战斗", "start", StartBattle, true);
                    start.SetEnabled(run.CanStartBattle);
                    controls.Add(start);
                }
                return controls;
            }

            var label = PrototypeStyle.Text(phase switch
            {
                Phase.Entering => "入场",
                Phase.Playback => $"第 {battle.Round} 回合",
                Phase.RoundBreak => $"第 {battle.Round} 回合结束",
                _ => "战斗结算"
            }, 13, PrototypeStyle.Accent);
            label.name = "battle-phase";
            label.style.marginRight = 6;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            controls.Add(label);
            if (defeat)
            {
                if (adventure == null)
                    controls.Add(PrototypeStyle.Button("重置演练", "reset", () => ResetDemo(reroll: true), true));
                else
                    controls.Add(AdventureFlow.ReturnButton());
            }
            return controls;
        }

        private void SetSettingsOpen(bool open)
        {
            double now = Time.unscaledTimeAsDouble;
            if (open)
            {
                settingsOpenedAt = now;
                HideCharacterDetails();
                statusTooltip.Hide();
                HideEquipmentTooltip();
            }
            else if (settingsOpenedAt.HasValue)
            {
                pausedTime += now - settingsOpenedAt.Value;
                settingsOpenedAt = null;
                if (phase == Phase.Settlement && victoryRewards != null && !victoryRewards.HasUnclaimed && LootChoices.Count == 0)
                    ContinueVictory();
            }
        }

        private void OpenPartyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            ClearEntranceForEditing();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Q<RunTopBar>().EditParty(RenderPreparation, () => partyEdited = true);
        }

        private void OpenEnemyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            ClearEntranceForEditing();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Q<RunTopBar>().EditParty(() =>
            {
                preparedEnemies = enemyRun.CaptureFormation(Side.Enemy);
                RenderPreparation();
            }, () =>
            {
                editedEnemyEncounters.Add(encounter);
                enemyEntranceScheduled = false;
            }, enemyRun);
        }

        private void ClearEntranceForEditing()
        {
            if (entrance.EnemyEntrancePending)
                enemyEntranceScheduled = false;
            entrance.Clear(unitCards);
        }

        private bool CanPreviewEnemies() =>
            run.Members.Any(member => member.Health > 0 && member.Character.Trait == CharacterTrait.RevealEnemies);

        private static float PortraitHeight(int slots) => 185 + (slots - 1) * 28;

        private VisualElement BuildLine(Side side, float portraitHeight, bool showUnits = true)
        {
            bool isParty = side == Side.Party;
            var line = new VisualElement();
            line.style.flexGrow = 1;
            line.style.flexBasis = 0;
            line.style.minWidth = 0;
            if (!showUnits)
                return line;

            var positions = PrototypeStyle.Row(isParty ? "party-positions" : "enemy-positions");
            var drag = isParty && phase == Phase.RoundBreak
                ? new FormationSwap(positions, (from, to) => SwapBetweenRounds(from.Position.Value, to.Position.Value))
                : null;
            for (int index = 0; index < Rules.Positions; index++)
            {
                int slot = isParty ? Rules.Positions - index : index + 1;
                var card = PositionCard(side, slot, portraitHeight);
                int width = isParty ? 1 : card?.PositionSlots ?? 1;
                var slotView = (VisualElement)card ?? new VisualElement { pickingMode = PickingMode.Ignore };
                slotView.name = $"{(isParty ? "party" : "enemy")}-position-{slot}";
                slotView.style.flexGrow = width;
                slotView.style.flexBasis = 0;
                slotView.style.minWidth = 0;
                slotView.style.marginLeft = slotView.style.marginRight = 4;
                positions.Add(slotView);
                index += width - 1;
                if (drag != null && card?.CanSwap == true && !battle.IsPositionLocked(card.Slot))
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

        private BattleUnitCard PositionCard(Side side, int position, float portraitHeight)
        {
            bool preparing = battle == null;
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

            if (id == null)
                return null;

            int shield = !preparing && phase != Phase.Settlement ? shields.GetValueOrDefault(id) : 0;
            BattleSpriteView sprite = null;
            var sheet = content.BattleSpriteFor(character?.Id);
            var illustration = sheet ?? content.BattlePortraitFor(character?.Id);
            if (illustration != null)
            {
                if (!battleSprites.TryGetValue(id, out sprite) || sprite.Sheet != illustration)
                    battleSprites[id] = sprite = new BattleSpriteView(illustration, content.BattleAttackFor(character.Id),
                        character.Profession, position, side == Side.Enemy, usesPortrait: sheet == null);
                sprite.style.top = 5 + portraitHeight - PortraitHeight(character.PositionSlots);
                sprite.RemoveFromHierarchy();
            }
            var card = new BattleUnitCard(side, position, id, character, content.PortraitFor(character?.Id),
                maximumHealth, currentHealth, shield, PlaybackNow, portraitHeight,
                statusIcon: content.StatusIcon, battleSprite: sprite, statusTooltip: statusTooltip);
            card.Refresh(currentHealth, shield, PlaybackNow, statuses.GetValueOrDefault(id),
                phase == Phase.Settlement ? null : countdowns.GetValueOrDefault(id));
            unitCards[id] = card;
            return card;
        }

        private VisualElement BuildFooter(Vector2? previousInspection)
        {
            var bottom = PrototypeStyle.Row("battle-footer");
            bottom.style.alignItems = Align.Stretch;
            bottom.style.justifyContent = Justify.Center;
            bottom.style.flexShrink = 0;
            bottom.style.paddingLeft = bottom.style.paddingRight = 22;
            bottom.style.paddingBottom = 10;
            var roster = PrototypeStyle.Panel();
            roster.name = "roster-equipment";
            roster.style.flexGrow = 7;
            roster.style.flexBasis = 0;
            roster.style.minWidth = 0;
            roster.style.maxWidth = 558;
            roster.style.marginRight = 32;
            roster.style.paddingTop = roster.style.paddingBottom = 6;
            roster.style.paddingLeft = roster.style.paddingRight = 7;

            var members = run.Members;
            var inventory = run.Inventory;
            var cards = PrototypeStyle.Row("equipment-positions");
            for (int position = Rules.Positions; position >= 1; position--)
            {
                string id = battle == null
                    ? members.FirstOrDefault(member => member.Position == position)?.Id
                    : battle.Formation.FirstOrDefault(unit => unit.Side == Side.Party && unit.Position == position)?.Id;
                var member = members.FirstOrDefault(entry => entry.Id == id);
                var character = phase != Phase.Preparation && id != null && appearances.TryGetValue(id, out var appearance)
                    ? appearance.Character : member?.Character;
                var equipment = id != null && battleLoadout != null && battleLoadout.Slots.TryGetValue(id, out var slots)
                    ? slots : member?.EquipmentIds;
                var skills = member == null ? Array.Empty<SkillBookDefinition>() :
                    member.LearnedSkills.Take(member.SkillSlotCount).ToArray();
                cards.Add(EquipmentCard(position, id, character, equipment, inventory, skills, previousInspection));
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
            return bottom;
        }

        private VisualElement EquipmentCard(int position, string id, CharacterDefinition character,
            IReadOnlyList<string> equipment, IReadOnlyList<EquipmentInstance> inventory,
            IReadOnlyList<SkillBookDefinition> skills, Vector2? previousInspection)
        {
            var card = PrototypeStyle.Row($"equipment-position-{position}");
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.minWidth = 0;
            card.style.height = 104;
            card.style.marginRight = position == 1 ? 0 : 5;
            if (id == null)
            {
                card.pickingMode = PickingMode.Ignore;
                return card;
            }

            card.style.backgroundColor = PrototypeStyle.Inset;
            card.style.overflow = Overflow.Hidden;
            PrototypeStyle.Outline(card, PrototypeStyle.Edge, 3);

            var slots = new VisualElement { name = "equipment-slots" };
            slots.style.width = 34;
            slots.style.alignItems = Align.Center;
            slots.style.justifyContent = Justify.SpaceBetween;
            slots.style.flexShrink = 0;
            slots.style.paddingTop = slots.style.paddingBottom = 3;
            slots.style.paddingLeft = slots.style.paddingRight = 2;
            var equippedItems = new EquipmentDefinition[Rules.EquipmentSlots];
            for (int slot = 0; slot < Rules.EquipmentSlots; slot++)
            {
                var held = inventory.FirstOrDefault(item => item.Id == equipment[slot])?.Definition;
                equippedItems[slot] = held;
                slots.Add(EquipmentTile($"equipment-slot-{slot + 1}", held, slot + 1));
            }
            card.Add(slots);

            var portrait = new VisualElement { name = $"equipment-portrait-{position}" };
            portrait.style.flexGrow = 1;
            portrait.style.flexBasis = 0;
            portrait.style.minWidth = 0;
            portrait.style.overflow = Overflow.Hidden;
            var illustration = new VisualElement { name = "equipment-portrait-art", pickingMode = PickingMode.Ignore };
            illustration.style.flexGrow = 1;
            illustration.style.flexBasis = 0;
            portrait.Add(illustration);
            ProfessionFrame.AddFittedTo(illustration, character.Profession, content.PortraitFor(character.Id));

            var caption = PrototypeStyle.Text(character.Name, 10);
            caption.style.paddingBottom = caption.style.paddingTop = 2;
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            caption.style.overflow = Overflow.Hidden;
            caption.style.textOverflow = TextOverflow.Ellipsis;
            caption.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(caption);
            portrait.Add(caption);
            card.Add(portrait);

            var bar = new VisualElement { name = "equipment-energy", pickingMode = PickingMode.Ignore };
            bar.style.width = 15;
            bar.style.flexShrink = 0;
            bar.style.flexDirection = FlexDirection.ColumnReverse;
            bar.style.marginTop = bar.style.marginBottom = 4;
            bar.style.marginLeft = bar.style.marginRight = 2;
            bar.style.paddingTop = bar.style.paddingBottom = 2;
            bar.style.paddingLeft = bar.style.paddingRight = 2;
            bar.style.backgroundColor = PrototypeStyle.Background;
            PrototypeStyle.Outline(bar, new Color32(103, 77, 127, 255), 7);
            var energy = phase != Phase.Preparation && partyEnergy.TryGetValue(id, out var current)
                ? current : BattleEnergy.Full(equippedItems.Where(item => item != null));
            PaintEnergy(bar, energy);
            energyBars[id] = bar;
            card.Add(bar);

            if (phase == Phase.Playback || phase == Phase.RoundBreak)
                RegisterCharacterInspection(portrait, position, id, character, equippedItems, skills, previousInspection);
            return card;
        }

        private static void PaintEnergy(VisualElement bar, BattleEnergy energy)
        {
            bar.tooltip = $"能量  {energy.Remaining}/{energy.Capacity}";
            if (bar.childCount != energy.Capacity)
            {
                bar.Clear();
                for (int i = 0; i < energy.Capacity; i++)
                {
                    var cell = new VisualElement { name = $"energy-cell-{i + 1}", pickingMode = PickingMode.Ignore };
                    cell.style.flexGrow = 1;
                    cell.style.flexBasis = 0;
                    cell.style.marginTop = cell.style.marginBottom = 1;
                    cell.style.borderTopLeftRadius = cell.style.borderTopRightRadius = 3;
                    cell.style.borderBottomLeftRadius = cell.style.borderBottomRightRadius = 3;
                    bar.Add(cell);
                }
            }
            for (int i = 0; i < energy.Capacity; i++)
                bar[i].style.backgroundColor = i < energy.Remaining ? AvailableEnergy : Color.white;
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
            RegisterEquipmentTooltip(view, item, artwork);
            return view;
        }

        private void RegisterEquipmentTooltip(VisualElement view, EquipmentDefinition item, Texture2D artwork)
        {
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
        }

        private void HideEquipmentTooltip()
        {
            equipmentTooltip?.RemoveFromHierarchy();
            equipmentTooltip = null;
            equipmentTooltipAnchor = null;
        }

        private void RegisterCharacterInspection(VisualElement portrait, int position, string id, CharacterDefinition character,
            IReadOnlyList<EquipmentDefinition> equipment, IReadOnlyList<SkillBookDefinition> skills, Vector2? previousInspection)
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
                var details = new BattleCharacterDetails(position, character, equipment, skills, content.IconFor);
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
                statuses.GetValueOrDefault(inspectedMember), phase == Phase.Settlement ? null : countdowns.GetValueOrDefault(inspectedMember));
        }

        private void HideCharacterDetails()
        {
            characterDetails?.RemoveFromHierarchy();
            characterDetails = null;
            inspectedPortrait = null;
            inspectedMember = null;
        }

        private IReadOnlyList<EquipmentDefinition> LootChoices =>
            adventure?.Current is { Resolved: false } visit ? visit.Rewards : Array.Empty<EquipmentDefinition>();

        private void ContinueVictory()
        {
            var overlay = root.Q("victory-rewards");
            if (victoryRewards.HasUnclaimed || LootChoices.Count > 0)
            {
                if (overlay == null)
                    Render();
                else
                    RenderVictory();
                return;
            }

            HideEquipmentTooltip();
            overlay?.RemoveFromHierarchy();
            if (topBar.HasRewardFlights)
            {
                topBar.WhenRewardsArrive(ContinueVictory);
                return;
            }
            if (leavingVictory || settingsOpenedAt.HasValue)
                return;

            leavingVictory = true;
            if (adventure == null)
                PrototypeSceneMenu.OpenStandalone("MainMenu");
            else
                AdventureFlow.ReturnFromNode();
        }

        private void RenderVictory()
        {
            HideEquipmentTooltip();
            root.Q("victory-rewards")?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = "victory-rewards" };
            PrototypeStyle.Fill(overlay);
            overlay.style.top = RunTopBar.Height;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0.03f, 0.026f, 0.033f, 0.76f);
            var panel = PrototypeStyle.Panel();
            panel.name = "victory-panel";
            panel.style.width = Length.Percent(90);
            panel.style.maxWidth = 550;
            panel.style.maxHeight = Length.Percent(88);
            panel.style.paddingTop = panel.style.paddingBottom = 18;
            panel.style.paddingLeft = panel.style.paddingRight = 18;
            panel.style.marginTop = 0;
            PrototypeStyle.Outline(panel, PrototypeStyle.Accent, 6);
            var heading = PrototypeStyle.Text("胜利", 26, PrototypeStyle.Accent);
            PrototypeStyle.Middle(heading);
            panel.Add(heading);

            var rewards = new ScrollView(ScrollViewMode.Vertical);
            rewards.style.flexShrink = 1;
            panel.Add(rewards);
            var row = RewardRow();
            for (int i = 0; i < victoryRewards.Items.Count; i++)
            {
                int index = i;
                if (victoryRewards.Items[index] == null)
                    continue;
                ItemButton(row, victoryRewards.Items[index], "claim-loot-" + index,
                    () => victoryRewards.ClaimItem(run, index));
            }
            if (victoryRewards.Gold > 0)
                row.Add(RewardButton($"{victoryRewards.Gold} 金币", "claim-gold", new GoldRewardIcon(),
                    button => Claim(button, () => victoryRewards.ClaimGold(run))));
            var choices = LootChoices;
            var choiceRow = choices.Count > 1 ? RewardRow() : row;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                ItemButton(choiceRow, choices[index], "claim-route-loot-" + index,
                    () => adventure.ClaimReward(index));
            }
            if (row.childCount > 0)
                rewards.Add(row);
            if (choices.Count > 1)
            {
                var label = PrototypeStyle.Text($"战利品 · {choices.Count} 选 1", 14, PrototypeStyle.Muted);
                label.name = "loot-choice-count";
                PrototypeStyle.Middle(label);
                rewards.Add(label);
                rewards.Add(choiceRow);
            }

            overlay.Add(panel);
            root.Add(overlay);
            topBar.BringRewardFlightsToFront();
            overlay.Q<Button>()?.Focus();

            void Claim(Button source, Func<bool> grant)
            {
                if (topBar.CollectReward(source, grant))
                    ContinueVictory();
            }

            void ItemButton(VisualElement parent, EquipmentDefinition item, string id, Func<bool> grant)
            {
                var texture = content.IconFor(item.Id);
                string caption = item is SkillBookDefinition ? $"技能书 · {item.Name}" : item.Name;
                var button = RewardButton(caption, id, new EquipmentIcon(item, texture), source => Claim(source, grant));
                RegisterEquipmentTooltip(button, item, texture);
                parent.Add(button);
            }
        }

        private static VisualElement RewardRow()
        {
            var row = PrototypeStyle.Row();
            row.style.justifyContent = Justify.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 8;
            return row;
        }

        private static Button RewardButton(string caption, string id, VisualElement icon, Action<Button> claim)
        {
            var button = PrototypeStyle.Button("", id, null);
            button.clicked += () => claim(button);
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.width = 110;
            button.style.minHeight = 106;
            button.style.marginLeft = button.style.marginRight = 4;
            button.style.marginBottom = 6;
            button.style.paddingLeft = button.style.paddingRight = 6;
            icon.style.alignSelf = Align.Center;
            icon.style.width = icon.style.height = 70;
            icon.style.flexGrow = 0;
            button.Add(icon);
            var label = PrototypeStyle.Text(caption, 13, PrototypeStyle.Accent);
            label.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(label);
            button.Add(label);
            return button;
        }

        private void RenderDefeat(VisualElement page)
        {
            var summary = PrototypeStyle.Panel();
            summary.name = "defeat-summary";
            summary.Add(PrototypeStyle.Text(OutcomeText(result.Outcome), 20, PrototypeStyle.Accent));
            summary.Add(PrototypeStyle.Text("剩余生命  " + string.Join("  ·  ", run.Members.Select(member =>
                $"{member.Character.Name} {HealthText(member.Health, member.EffectiveStats.Health)}")), 14));
            if (result.GoldEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.GoldEarned} 金币 · 当前 {run.Gold} 金币", 13, PrototypeStyle.Accent));
            if (result.AccessoriesEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.AccessoriesEarned} 件饰品，已放入共享背包", 13, PrototypeStyle.Accent));
            if (adventure?.Current is { } visit)
                summary.Add(PrototypeStyle.Text(visit.Message, 15, PrototypeStyle.Accent));
            page.Add(summary);
        }

        private VisualElement BuildLog()
        {
            var panel = PrototypeStyle.Panel();
            panel.style.flexGrow = 3;
            panel.style.flexBasis = 0;
            panel.style.minWidth = 0;
            panel.name = "battle-log-paper";
            panel.style.maxWidth = 426;
            panel.style.paddingTop = panel.style.paddingBottom = 5;
            panel.style.backgroundColor = (Color)new Color32(234, 208, 165, 255);
            if (battleLogParchment != null)
            {
                panel.style.backgroundImage = new StyleBackground(battleLogParchment);
                panel.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            PrototypeStyle.Outline(panel, new Color32(130, 85, 51, 255), 5);
            panel.Add(PrototypeStyle.Text("战斗日志", 14, AdventureEventWindow.PaperAccent));
            logView = new ScrollView { name = "battle-log" };
            logView.style.height = 86;
            logView.style.marginTop = 3;
            var scroller = logView.verticalScroller;
            scroller.style.width = 11;
            scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
            var track = scroller.slider.Q("unity-tracker");
            var thumb = scroller.slider.Q("unity-dragger");
            if (track != null)
                track.style.backgroundColor = (Color)new Color32(131, 91, 53, 75);
            if (thumb != null)
                thumb.style.backgroundColor = (Color)new Color32(119, 75, 41, 190);
            panel.Add(logView);
            if (battleLog.Count == 0 && phase == Phase.Preparation)
                logView.Add(PrototypeStyle.Text("战斗开始后显示行动记录。", 12, AdventureEventWindow.PaperMuted));
            foreach (string line in battleLog)
                logView.Add(PrototypeStyle.Text(line, 12, AdventureEventWindow.PaperInk));
            if (battleLog.Count > 0)
                ScrollLogTo(logView, logView.contentContainer.ElementAt(logView.contentContainer.childCount - 1));
            return panel;
        }

        private void AppendLog(string line, BattleEventKind kind)
        {
            var label = PrototypeStyle.Text(line, 12, AdventureEventWindow.PaperInk);
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
            DecisionReason.Retaliate => "（记仇：还击）", DecisionReason.FinishWounded => "（谨慎：低生命目标）",
            DecisionReason.StayOnTarget => "（专注：继续集火）", DecisionReason.UseSkill => "（技能优先）",
            DecisionReason.SaveSkill => "（隐忍：保留技能）", DecisionReason.AvoidOverkill => "（贪心：有效伤害）",
            DecisionReason.EmergencyHeal => "（贪心：紧急救援）",
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
