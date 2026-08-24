using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorPvP
{
    // Owns temporary-proxy combat admission. Before GO the proxy boundary is held symmetrically;
    // after GO native world combat stays open and only proven protected neutral/noncombat actors are
    // filtered per interaction.
    internal static class PvpCombatContainment
    {
        private static NPC _cloneNpc;
        private static Character _cloneActor;
        private static Character _player;
        private static NavMeshAgent _cloneNav;
        private static float _targetTestEnds;
        private static bool _lethalFight;
        private static bool _retreatRolled;
        private static float _fightStartedAt;
        private static float _combatStartupDeadline;
        private static long _damageToAttackers;
        private static long _petDamageToAttackers;
        private static long _damageToDefenders;
        private static long _healingToAttackers;
        private static long _healingToDefenders;
        private static bool _damageContextPet;
        // Instance id of the temporary attacker currently dealing damage, 0 = none/unattributed.
        // Threaded across the DamageMe/MagicDamageMe/BleedDamageMe -> Stats.ReduceHP call boundary
        // the exact same way _damageContextPet already is (save/restore around the nested call), so
        // per-proxy "effective damage dealt" can be attributed at the point the real HP delta is
        // known (0.5.11 observability; see PvpTemporaryCloneFactory.RecordDamageDealt).
        private static int _damageSourceProxyId;
        private static int _healTelemetryDepth;
        private static bool _loggedDamageToDefender;
        private static bool _loggedHealToAttacker;
        private static readonly List<NPC> EnemyNpcs = new List<NPC>();
        private static readonly List<Character> EnemyActors = new List<Character>();
        private static readonly List<Character> Defenders = new List<Character>();
        private static readonly HashSet<Character> DefenderPets = new HashSet<Character>();

        internal struct DamageTelemetryState
        {
            internal bool ContextSet;
            internal bool PreviousPetContext;
            internal int PreviousDamageSourceProxyId;
        }

        internal struct HpTelemetryState
        {
            internal bool Track;
            internal bool TargetIsEnemy;
            internal int StartingHp;
            internal int StartingMaxHp;
            internal int RequestedHeal;
            internal bool OwnsHealScope;
            // Instance id of the temporary attacker that cast this heal, 0 = unattributed (e.g. the
            // flat HealMe(int) overload, which carries no source Character at all).
            internal int SourceProxyId;
        }

        internal sealed class StatusEffectTelemetryState
        {
            internal Stats Target;
            internal Spell Spell;
            internal Character Source;
            internal int BeforeMatching;
            internal float BeforeMaxDuration;
            internal StatusEffect[] BeforeSlots;
            internal bool Track;
            internal bool Completed;
        }

        internal static bool TargetTestActive { get { return _cloneNpc != null && Time.unscaledTime < _targetTestEnds; } }
        internal static bool LethalFightActive { get { return _lethalFight && EnemyActors.Count > 0 && _player != null; } }

        internal static string VerifyRuntime()
        {
            if (!LethalFightActive) return "COMBAT VERIFY FAIL lethal=false.";
            List<string> failures = new List<string>();
            if (EnemyNpcs.Count == 0 || EnemyNpcs.Count != EnemyActors.Count) failures.Add("enemy_count");
            if (Defenders.Count == 0 || _player == null || !Defenders.Contains(_player)) failures.Add("defenders");
            for (int i = 0; i < EnemyNpcs.Count; i++)
            {
                NPC npc = EnemyNpcs[i]; Character actor = i < EnemyActors.Count ? EnemyActors[i] : null;
                if (npc == null || actor == null || actor.MyStats == null) { failures.Add("enemy" + (i + 1) + "_actor"); continue; }
                if (npc.NeverAggro) failures.Add("enemy" + (i + 1) + "_neverAggro");
                if (npc.CurrentAggroTarget == null || !IsPermittedProxyTarget(npc.CurrentAggroTarget)) failures.Add("enemy" + (i + 1) + "_target");
            }
            return failures.Count == 0 ? "COMBAT VERIFY PASS attackers=" + EnemyActors.Count + "; defenders=" + Defenders.Count + "; defender_pets=" + DefenderPets.Count + "; damage_to_attackers=" + _damageToAttackers + "; pet_damage=" + _petDamageToAttackers + "; healing_to_attackers=" + _healingToAttackers + "; damage_to_defenders=" + _damageToDefenders + "; healing_to_defenders=" + _healingToDefenders + "; " + PvpTemporaryCloneFactory.NativeCombatEvidenceSummary() + "; " +
                PvpTemporaryCloneFactory.NativeNavHealthSummary() + "; targets=world_native." :
                "COMBAT VERIFY FAIL " + string.Join(",", failures.ToArray()) + "; " +
                PvpTemporaryCloneFactory.NativeNavHealthSummary() + ".";
        }

        internal static string BeginTargetingTest(NPC cloneNpc, Character cloneActor)
        {
            Character player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself;
            if (cloneNpc == null || cloneActor == null || player == null) return "[Erenshor PvP] Target test blocked: clone or player is unavailable.";
            _cloneNpc = cloneNpc; _cloneActor = cloneActor; _player = player;
            _cloneNav = cloneActor.GetComponent<NavMeshAgent>();
            if (_cloneNav != null)
            {
                _cloneNav.enabled = true;
                if (_cloneNav.isOnNavMesh) _cloneNav.isStopped = false;
                else _cloneNav.enabled = false;
            }
            _targetTestEnds = Time.unscaledTime + 10f;
            cloneNpc.ForceAggroOn(player);
            if (cloneNpc.CurrentAggroTarget != player) { End("target_rejected"); return "[Erenshor PvP] Target test blocked: native target was not accepted."; }
            return "[Erenshor PvP] Clone target/chase test active for 10 seconds. It will face and path toward you; AI and all damage remain disabled.";
        }

        internal static void Tick()
        {
            if (_cloneNpc == null) return;
            if (LethalFightActive)
            {
                try
                {
                    if (!_player.Alive) { End("player_death"); PvpTemporaryCloneFactory.DespawnAfterFight("player_death", null); return; }
                    if (PvpTemporaryCloneFactory.CompleteNativeNavFailure(EnemyNpcs))
                    {
                        PvpDiagnostics.Log("technical_failure_ai_inactive reason=complete_nav_failure; " +
                            PvpTemporaryCloneFactory.NativeNavHealthSummary() + "; " +
                            PvpTemporaryCloneFactory.NativeCombatEvidenceSummary());
                        End(PvpCombatStartupPolicy.TechnicalFailureAiInactive);
                        PvpTemporaryCloneFactory.DespawnAfterFight(PvpCombatStartupPolicy.TechnicalFailureAiInactive, null);
                        return;
                    }
                    if (_combatStartupDeadline > 0f && Time.unscaledTime >= _combatStartupDeadline)
                    {
                        // Only proxy-owned native AI evidence satisfies the startup watchdog. World
                        // mobs/Sims may now damage or heal participants, so aggregate HP movement is no
                        // longer valid proof that a PvP attacker itself became combat-active.
                        bool evidence = PvpTemporaryCloneFactory.HasAnyNativeCombatEvidence();
                        float secondsSinceGo = _fightStartedAt <= 0f ? 0f : Mathf.Max(0f, Time.unscaledTime - _fightStartedAt);
                        if (PvpCombatStartupPolicy.ShouldFailInactive(true, evidence, secondsSinceGo, PvpCombatStartupPolicy.DefaultStartupWindowSeconds))
                        {
                            PvpDiagnostics.Log("technical_failure_ai_inactive seconds=" + secondsSinceGo.ToString("0.0") +
                                "; " + PvpTemporaryCloneFactory.NativeCombatEvidenceSummary() +
                                "; " + PvpTemporaryCloneFactory.NativeNavHealthSummary() +
                                "; damage_to_defenders=" + _damageToDefenders + "; healing_to_attackers=" + _healingToAttackers);
                            End(PvpCombatStartupPolicy.TechnicalFailureAiInactive);
                            PvpTemporaryCloneFactory.DespawnAfterFight(PvpCombatStartupPolicy.TechnicalFailureAiInactive, null);
                            return;
                        }
                        _combatStartupDeadline = 0f;
                    }
                    List<Character> living = EnemyActors.Where(x => x != null && x.Alive && x.MyStats != null).ToList();
                    if (!_retreatRolled && living.Count == 1 && living[0].MyStats.CurrentMaxHP > 0 &&
                        living[0].MyStats.CurrentHP <= living[0].MyStats.CurrentMaxHP * .12f)
                    {
                        _retreatRolled = true;
                        if (UnityEngine.Random.Range(0, 100) < 20)
                        {
                            End("retreat"); PvpTemporaryCloneFactory.DespawnAfterFight("retreat", null); return;
                        }
                    }
                    if (EnemyActors.All(x => x == null || !x.Alive))
                    {
                        bool engagementObserved = PvpTemporaryCloneFactory.HasAnyNativeCombatEvidence();
                        if (!engagementObserved)
                        {
                            PvpDiagnostics.Log("technical_failure_ai_inactive terminal=attackers_defeated_before_engagement; " +
                                PvpTemporaryCloneFactory.NativeCombatEvidenceSummary());
                            End(PvpCombatStartupPolicy.TechnicalFailureAiInactive);
                            PvpTemporaryCloneFactory.DespawnAfterFight(PvpCombatStartupPolicy.TechnicalFailureAiInactive, null);
                            return;
                        }
                        Character winner = _player;
                        End("proxy_death");
                        PvpTemporaryCloneFactory.DespawnAfterFight("proxy_death", winner);
                        return;
                    }
                }
                catch { End("fight_state_failed"); PvpTemporaryCloneFactory.DespawnAfterFight("fight_state_failed", null); }
                return;
            }
            if (Time.unscaledTime >= _targetTestEnds) { End("timer"); return; }
            try
            {
                if (_cloneActor != null && _player != null)
                {
                    Vector3 destination = _player.transform.position;
                    Vector3 flat = destination - _cloneActor.transform.position; flat.y = 0f;
                    if (flat.sqrMagnitude > 0.04f) _cloneActor.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    if (_cloneNav != null && _cloneNav.enabled && _cloneNav.isOnNavMesh) _cloneNav.SetDestination(destination);
                }
            }
            catch { End("target_update_failed"); }
        }

        internal static void End(string reason)
        {
            bool wasLethal = _lethalFight;
            if (wasLethal) LogBalanceSummary(reason);
            NPC npc = _cloneNpc;
            NavMeshAgent nav = _cloneNav;
            _cloneNpc = null; _cloneActor = null; _player = null; _cloneNav = null; _targetTestEnds = 0f; _lethalFight = false; _retreatRolled = false;
            foreach (NPC enemy in EnemyNpcs) { try { if (enemy != null) enemy.ForceAggroOn(null); } catch { } }
            // Defender pets are native world actors, not PvP-owned proxies. Release only a stale
            // PvP-attacker target; do not erase a legitimate world-mob target acquired during the match.
            foreach (Character pet in DefenderPets)
            {
                try
                {
                    if (pet == null || pet.MyNPC == null) continue;
                    Character current = pet.MyNPC.CurrentAggroTarget;
                    if (current != null && EnemyActors.Contains(current)) pet.MyNPC.ForceAggroOn(null);
                }
                catch { }
            }
            EnemyNpcs.Clear(); EnemyActors.Clear(); Defenders.Clear(); DefenderPets.Clear();
            _fightStartedAt = 0f; _combatStartupDeadline = 0f; _damageToAttackers = 0; _petDamageToAttackers = 0; _damageToDefenders = 0;
            _healingToAttackers = 0; _healingToDefenders = 0; _damageContextPet = false; _damageSourceProxyId = 0; _healTelemetryDepth = 0;
            _loggedDamageToDefender = false; _loggedHealToAttacker = false;
            try { if (npc != null) npc.ForceAggroOn(null); } catch { }
            try { if (nav != null) { nav.ResetPath(); nav.enabled = false; } } catch { }
        }

        internal static bool AllowAggro(NPC npc, Character target)
        {
            if (target == null) return true; // cleanup must always be able to clear target state.
            if (TargetTestActive && npc == _cloneNpc) return target == _player;
            Character actor = NpcCharacter(npc);
            if (!LethalFightActive)
            {
                // Preparation/countdown is symmetric only across the temporary-proxy boundary.
                // Existing world combat among ordinary actors continues natively before GO.
                return !PvpTemporaryCloneFactory.IsTemporaryNpc(npc) &&
                    !PvpTemporaryCloneFactory.IsTemporaryActor(target);
            }

            bool sourceAttacker = EnemyNpcs.Contains(npc) || EnemyActors.Contains(actor);
            bool sourceDefender = Defenders.Contains(actor) || DefenderPets.Contains(actor) || RegisterDefenderPet(actor);
            bool targetAttacker = EnemyActors.Contains(target);
            bool targetDefender = Defenders.Contains(target) || DefenderPets.Contains(target) || RegisterDefenderPet(target);
            bool sourceProtected = IsProtectedWorldActor(actor);
            bool targetProtected = IsProtectedWorldActor(target);
            PvpInteractionDecision decision = PvpWorldCombatPolicy.DecideAggro(sourceAttacker, sourceDefender,
                targetAttacker, targetDefender, sourceProtected, targetProtected);
            if (decision != PvpInteractionDecision.Block) return true;

            // Protected neutral/noncombat actors are excluded narrowly. Ordinary Sims, pets, hostile
            // mobs and other combat-capable/unknown world actors stay in the native combat graph.
            PvpDiagnostics.Log("protected_aggro_rejected source=" + DescribeCombatTarget(actor) +
                "; target=" + DescribeCombatTarget(target));
            try { if (sourceAttacker && npc != null && npc.CurrentAggroTarget == target) npc.ForceAggroOn(null); } catch { }
            return false;
        }

        internal static bool PrepareSpellStart(CastSpell caster, Spell spell, ref Stats targetStats,
            bool directCastEntry, out bool adaptedToSelf)
        {
            adaptedToSelf = false;
            Character source = null, passedTarget = null;
            try { source = caster == null ? null : caster.MyChar; } catch { }
            try { passedTarget = targetStats == null ? null : targetStats.Myself; } catch { }

            PvpSpellSemanticSnapshot semantic = PvpSpellSemantics.Inspect(spell);
            Character policyTarget = passedTarget;

            if (LethalFightActive && source != null && source == _player && passedTarget != null &&
                EnemyActors.Contains(passedTarget) && directCastEntry && spell != null)
            {
                bool healType = false, group = false, ae = false, pbae = false, pet = false, charm = false, proc = false;
                int targetHealing = 0, targetDamage = 0;
                try { healType = spell.Type == Spell.SpellType.Heal; } catch { }
                try { targetHealing = spell.TargetHealing; } catch { }
                try { targetDamage = spell.TargetDamage; } catch { }
                try { group = spell.GroupEffect; } catch { }
                try { ae = spell.Type == Spell.SpellType.AE; } catch { }
                try { pbae = spell.Type == Spell.SpellType.PBAE; } catch { }
                try { pet = spell.PetToSummon != null || spell.Type == Spell.SpellType.Pet; } catch { }
                try { charm = spell.CharmTarget; } catch { }
                try { proc = spell.AddProc != null; } catch { }
                if (PvpSpellTargetPolicy.CanAdaptOpponentHealToSelf(true, true, true, true,
                    healType, targetHealing, targetDamage, group, ae, pbae, pet, charm, proc))
                {
                    Stats selfStats = source.MyStats;
                    if (selfStats != null && selfStats.Myself == source)
                    {
                        targetStats = selfStats;
                        policyTarget = source;
                        adaptedToSelf = true;
                        PvpDiagnostics.Log("spell_target_adaptation source=defender_player; spell=" +
                            (spell == null ? "unknown" : spell.SpellName) + "; original_target=" +
                            DescribeCombatTarget(passedTarget) + "; resolved_target=defender:self; current_target_preserved=true; mode=single_target_heal_to_self");
                    }
                }
            }

            // SelfOnly / ApplyToCaster / InflictOnSelf are properties of the Spell asset, not proof
            // that the caller-supplied Stats is the eventual affected actor. Preserve the native target
            // argument, but authorize the cast using the caster as its effective self edge.
            if (!adaptedToSelf && semantic.DeclaresSelf && source != null) policyTarget = source;
            return AllowSpellStartResolved(caster, spell, source, policyTarget, semantic);
        }

        internal static bool AllowSpellStart(CastSpell caster, Spell spell, Stats targetStats)
        {
            bool ignored;
            return PrepareSpellStart(caster, spell, ref targetStats, false, out ignored);
        }

        private static bool AllowSpellStartResolved(CastSpell caster, Spell spell, Character source,
            Character target, PvpSpellSemanticSnapshot semantic)
        {
            if (!LethalFightActive)
            {
                // Before GO, block every spell initiation that crosses the temporary-proxy boundary.
                // Ordinary world casting, including combat already in progress, remains native.
                return !PvpTemporaryCloneFactory.IsTemporaryActor(source) &&
                    !PvpTemporaryCloneFactory.IsTemporaryActor(target);
            }

            bool sourceAttacker = EnemyActors.Contains(source);
            bool sourceDefender = Defenders.Contains(source) || DefenderPets.Contains(source) || RegisterDefenderPet(source);
            bool targetAttacker = EnemyActors.Contains(target);
            bool targetDefender = Defenders.Contains(target) || DefenderPets.Contains(target) || RegisterDefenderPet(target);
            bool beneficial = semantic != null && semantic.Beneficial;
            bool sourceProtected = IsProtectedWorldActor(source);
            bool targetProtected = IsProtectedWorldActor(target);
            PvpInteractionDecision decision = PvpWorldCombatPolicy.DecideSpellStart(sourceDefender, sourceAttacker,
                targetDefender, targetAttacker, target == null, beneficial, sourceProtected, targetProtected);
            if (decision != PvpInteractionDecision.Block) return true;

            // Targeted protected/cross-team beneficial interactions are rejected individually. AE/PBAE
            // starts are never proximity-blocked here; actual affected targets are filtered per edge.
            PvpDiagnostics.Log("protected_or_team_spell_rejected source=" + DescribeCombatTarget(source) +
                "; target=" + DescribeCombatTarget(target) + "; category=" +
                (semantic == null ? "unknown" : semantic.Token) + "; beneficial=" + beneficial);
            return false;
        }

        internal static bool PrepareDamage(Character target, Character attacker, bool fromPlayer, ref int result, ref DamageTelemetryState state)
        {
            state = new DamageTelemetryState();
            if (LethalFightActive)
            {
                bool targetDefender = Defenders.Contains(target) || DefenderPets.Contains(target) || RegisterDefenderPet(target);
                bool targetAttacker = EnemyActors.Contains(target);
                bool sourceAttacker = EnemyActors.Contains(attacker);
                bool sourceDefender = Defenders.Contains(attacker) || DefenderPets.Contains(attacker) || RegisterDefenderPet(attacker);
                bool unknownPlayerProjectile = attacker == null && fromPlayer;
                bool sourceProtected = IsProtectedWorldActor(attacker);
                bool targetProtected = IsProtectedWorldActor(target);
                PvpInteractionDecision decision = PvpWorldCombatPolicy.DecideDamage(targetDefender, targetAttacker,
                    sourceAttacker, sourceDefender, unknownPlayerProjectile, sourceProtected, targetProtected);
                if (decision != PvpInteractionDecision.Block)
                {
                    if (decision == PvpInteractionDecision.AllowMatch)
                    {
                        state.ContextSet = true;
                        state.PreviousPetContext = _damageContextPet;
                        state.PreviousDamageSourceProxyId = _damageSourceProxyId;
                        _damageContextPet = targetAttacker && DefenderPets.Contains(attacker);
                        _damageSourceProxyId = (sourceAttacker && PvpTemporaryCloneFactory.IsTemporaryActor(attacker)) ?
                            attacker.gameObject.GetInstanceID() : 0;
                    }
                    return true;
                }

                PvpDiagnostics.Log("protected_or_team_damage_rejected target=" + DescribeCombatTarget(target) +
                    "; attacker=" + (attacker == null ? (fromPlayer ? "player_projectile" : "none") : DescribeCombatTarget(attacker)));
                result = 0;
                return false;
            }
            if (!PvpTemporaryCloneFactory.IsTemporaryActor(target) && !PvpTemporaryCloneFactory.IsTemporaryActor(attacker)) return true;
            result = 0;
            return false;
        }

        internal static void FinishDamage(DamageTelemetryState state)
        {
            if (state.ContextSet) { _damageContextPet = state.PreviousPetContext; _damageSourceProxyId = state.PreviousDamageSourceProxyId; }
        }

        internal static void BeginHpTelemetry(Stats stats, ref HpTelemetryState state)
        {
            state = new HpTelemetryState();
            CaptureHpTelemetry(stats, ref state);
        }

        internal static void BeginHealTelemetry(Stats stats, int requested, ref HpTelemetryState state)
        {
            BeginHealTelemetry(stats, null, requested, ref state);
        }

        // Overload used where the caster is known (Stats.HealMe(Spell, int, bool, bool, Character)).
        // The flat HealMe(int) overload has no source Character and therefore remains unattributed
        // per proxy, but effective team healing is still measured from the real native HP delta.
        internal static void BeginHealTelemetry(Stats stats, Character healer, int requested, ref HpTelemetryState state)
        {
            state = new HpTelemetryState { OwnsHealScope = true, RequestedHeal = requested };
            _healTelemetryDepth++;
            if (_healTelemetryDepth == 1)
            {
                CaptureHpTelemetry(stats, ref state);
                if (healer != null && EnemyActors.Contains(healer)) state.SourceProxyId = healer.gameObject.GetInstanceID();
                if (state.SourceProxyId != 0) PvpTemporaryCloneFactory.ObserveHealMeEntered(state.SourceProxyId, stats, requested);
            }
        }

        private static void CaptureHpTelemetry(Stats stats, ref HpTelemetryState state)
        {
            if (!LethalFightActive || stats == null) return;
            Character target = null;
            try { target = stats.Myself; } catch { }
            bool enemy = EnemyActors.Contains(target);
            bool defender = Defenders.Contains(target) || DefenderPets.Contains(target) || RegisterDefenderPet(target);
            if (!enemy && !defender) return;
            state.Track = true; state.TargetIsEnemy = enemy; state.StartingHp = stats.CurrentHP; state.StartingMaxHp = stats.CurrentMaxHP;
        }

        internal static void FinishHpReduction(Stats stats, HpTelemetryState state)
        {
            if (!state.Track || stats == null) return;
            int applied = Mathf.Max(0, state.StartingHp - Mathf.Max(0, stats.CurrentHP));
            if (applied <= 0) return;
            if (state.TargetIsEnemy)
            {
                _damageToAttackers += applied;
                if (_damageContextPet) _petDamageToAttackers += applied;
            }
            else
            {
                _damageToDefenders += applied;
                if (_damageSourceProxyId != 0) PvpTemporaryCloneFactory.RecordDamageDealt(_damageSourceProxyId, applied);
                if (!_loggedDamageToDefender)
                {
                    _loggedDamageToDefender = true;
                    PvpDiagnostics.Log("damage_to_defender amount=" + applied);
                }
            }
        }

        internal static void FinishHpHealing(Stats stats, HpTelemetryState state)
        {
            try
            {
                if (!state.Track || stats == null) return;
                int after = stats.CurrentHP;
                int applied = Mathf.Max(0, after - state.StartingHp);
                if (state.SourceProxyId != 0)
                    PvpTemporaryCloneFactory.RecordHealMeObserved(state.SourceProxyId, stats, state.StartingHp, after, state.RequestedHeal);
                if (applied <= 0) return; // accepted/full-health/overheal casts do not inflate FIGHT healing.
                if (state.TargetIsEnemy)
                {
                    _healingToAttackers += applied;
                    if (state.SourceProxyId != 0) PvpTemporaryCloneFactory.RecordHealingDone(state.SourceProxyId, applied);
                    if (!_loggedHealToAttacker)
                    {
                        _loggedHealToAttacker = true;
                        PvpDiagnostics.Log("heal_to_attacker amount=" + applied);
                    }
                }
                else _healingToDefenders += applied;
            }
            finally
            {
                if (state.OwnsHealScope) _healTelemetryDepth = Math.Max(0, _healTelemetryDepth - 1);
            }
        }

        internal static bool PrepareStatusEffect(Stats targetStats, Spell spell, Character suppliedSource,
            ref int result, ref StatusEffectTelemetryState state)
        {
            state = new StatusEffectTelemetryState();
            Character source = PvpTemporaryCloneFactory.ResolveActiveSpellSource(spell, targetStats, suppliedSource);
            Character target = null; try { target = targetStats == null ? null : targetStats.Myself; } catch { }
            PvpSpellSemanticSnapshot semantic = PvpSpellSemantics.Inspect(spell);
            Character policyTarget = semantic.DeclaresSelf && source != null ? source : target;
            if (!AllowSpellStartResolved(null, spell, source, policyTarget, semantic))
            {
                result = 0;
                return false;
            }

            if (!LethalFightActive || targetStats == null || spell == null) return true;
            bool relevant = PvpTemporaryCloneFactory.IsTemporaryActor(source) || PvpTemporaryCloneFactory.IsTemporaryActor(target) ||
                Defenders.Contains(source) || Defenders.Contains(target) || DefenderPets.Contains(source) || DefenderPets.Contains(target);
            if (!relevant) return true;

            state.Target = targetStats;
            state.Spell = spell;
            state.Source = source;
            state.Track = true;
            SnapshotStatusEffectState(targetStats, spell, out state.BeforeMatching, out state.BeforeMaxDuration, out state.BeforeSlots);
            return true;
        }

        internal static void FinishStatusEffect(StatusEffectTelemetryState state)
        {
            if (state == null || state.Completed || !state.Track || state.Target == null || state.Spell == null) return;
            state.Completed = true;
            int afterMatching;
            float afterDuration;
            StatusEffect[] afterSlots;
            SnapshotStatusEffectState(state.Target, state.Spell, out afterMatching, out afterDuration, out afterSlots);
            bool newSlot = false;
            if (afterSlots != null)
            {
                for (int i = 0; i < afterSlots.Length && !newSlot; i++)
                {
                    StatusEffect current = afterSlots[i];
                    if (current == null) continue;
                    bool existed = false;
                    if (state.BeforeSlots != null)
                        for (int j = 0; j < state.BeforeSlots.Length; j++)
                            if (object.ReferenceEquals(state.BeforeSlots[j], current)) { existed = true; break; }
                    if (!existed) newSlot = true;
                }
            }
            string outcome = PvpSpellTargetPolicy.ClassifyStatusApplication(state.BeforeMatching, afterMatching, newSlot,
                state.BeforeMaxDuration, afterDuration);
            bool applied = outcome != "no_observable_state_change";
            PvpTemporaryCloneFactory.RecordStatusEffectApplied(state.Source, state.Target, state.Spell, applied, outcome,
                state.BeforeMatching, afterMatching, state.BeforeMaxDuration, afterDuration);
        }

        private static void SnapshotStatusEffectState(Stats stats, Spell spell, out int matching, out float maxDuration,
            out StatusEffect[] matchingSlots)
        {
            matching = 0; maxDuration = 0f; matchingSlots = new StatusEffect[0];
            if (stats == null || spell == null || stats.StatusEffects == null) return;
            List<StatusEffect> slots = new List<StatusEffect>();
            StatusEffect[] effects = stats.StatusEffects;
            for (int i = 0; i < effects.Length; i++)
            {
                StatusEffect slot = effects[i];
                if (slot == null || slot.Effect != spell) continue;
                matching++;
                if (slot.Duration > maxDuration) maxDuration = slot.Duration;
                slots.Add(slot);
            }
            matchingSlots = slots.ToArray();
        }

        internal static bool AllowHeal(Stats targetStats, Character healer)
        {
            Character target = null;
            try { target = targetStats == null ? null : targetStats.Myself; } catch { }
            if (!LethalFightActive)
            {
                // Unknown-source vanilla self/HoT ticks on ordinary actors remain game-owned.
                // Any heal path involving a temporary attacker is held until GO.
                return !PvpTemporaryCloneFactory.IsTemporaryActor(target) &&
                    !PvpTemporaryCloneFactory.IsTemporaryActor(healer);
            }

            bool targetAttacker = EnemyActors.Contains(target);
            bool targetDefender = Defenders.Contains(target) || DefenderPets.Contains(target) || RegisterDefenderPet(target);
            bool sourceAttacker = EnemyActors.Contains(healer);
            bool sourceDefender = Defenders.Contains(healer) || DefenderPets.Contains(healer) || RegisterDefenderPet(healer);

            // Native HoTs/lifesteal/self-heals can arrive without a source Character. Preserve them
            // for a participant rather than inventing outside assistance.
            if (healer == null && (targetAttacker || targetDefender)) return true;

            PvpInteractionDecision decision = PvpWorldCombatPolicy.DecideHeal(targetDefender, targetAttacker,
                sourceDefender, sourceAttacker, IsProtectedWorldActor(healer), IsProtectedWorldActor(target));
            if (decision != PvpInteractionDecision.Block) return true;
            PvpDiagnostics.Log("protected_or_cross_team_heal_rejected target=" + DescribeCombatTarget(target) +
                "; healer=" + DescribeCombatTarget(healer));
            return false;
        }

        internal static bool IsLegalDefender(Character actor)
        {
            return actor != null && (Defenders.Contains(actor) || DefenderPets.Contains(actor) || RegisterDefenderPet(actor));
        }

        // PvP-only semantic team query used by the allied-heal bridge. Native NPC.CheckHeals can
        // self-heal temporary non-Sim proxies, but its ordinary ally paths require native InGroup /
        // SimPlayerTracking membership that these disposable actors deliberately never enter.
        // Restricting this query to EnemyActors prevents unrelated world actors or defenders from
        // becoming synthetic heal targets while leaving all ordinary world-combat heals native.
        internal static bool TryFindInjuredAttackerAlly(Character healer, out Character ally, out float healthRatio)
        {
            ally = null; healthRatio = 1f;
            if (!LethalFightActive || healer == null || !EnemyActors.Contains(healer)) return false;
            float best = 1f;
            foreach (Character candidate in EnemyActors)
            {
                try
                {
                    if (candidate == null || candidate == healer || !candidate.Alive || candidate.MyStats == null ||
                        candidate.MyStats.CurrentMaxHP <= 0) continue;
                    int hp = candidate.MyStats.CurrentHP, maxHp = candidate.MyStats.CurrentMaxHP;
                    if (!PvpSpellExecutionPolicy.LegalProxyAllyHealTarget(true, true, false, hp, maxHp)) continue;
                    float ratio = Mathf.Clamp01((float)Math.Max(0, hp) / Math.Max(1, maxHp));
                    if (ally == null || ratio < best) { ally = candidate; best = ratio; }
                }
                catch { /* destroyed/transitioning match actors fail closed as heal candidates */ }
            }
            if (ally == null) return false;
            healthRatio = best;
            return true;
        }

        internal static string LiveCombatPresentationSummary()
        {
            if (!LethalFightActive) return string.Empty;
            int livingAttackers = EnemyActors.Count(x => x != null && x.Alive && x.MyStats != null && x.MyStats.CurrentHP > 0);
            int attackerHp = EnemyActors.Where(x => x != null && x.MyStats != null).Sum(x => Math.Max(0, x.MyStats.CurrentHP));
            int attackerMax = EnemyActors.Where(x => x != null && x.MyStats != null).Sum(x => Math.Max(0, x.MyStats.CurrentMaxHP));
            int playerHp = _player == null || _player.MyStats == null ? 0 : Math.Max(0, _player.MyStats.CurrentHP);
            int playerMax = _player == null || _player.MyStats == null ? 0 : Math.Max(0, _player.MyStats.CurrentMaxHP);
            return "Your HP " + playerHp + "/" + playerMax + "   Opponents " + attackerHp + "/" + attackerMax +
                " (" + livingAttackers + " alive)\n" +
                "Damage dealt to attackers " + _damageToAttackers + "   Damage taken by defenders " + _damageToDefenders +
                "\nHealing to attackers " + _healingToAttackers + "   Healing to defenders " + _healingToDefenders;
        }

        // Native AI may expand from the initially seeded defender target into ordinary world combat.
        // A proxy target is permitted unless it is its own PvP team or current native state positively
        // proves the actor is protected neutral/noncombat. Native hostility/faction logic remains primary.
        internal static bool IsPermittedProxyTarget(Character actor)
        {
            return actor != null && !EnemyActors.Contains(actor) && !IsProtectedWorldActor(actor);
        }

        internal static bool IsProtectedWorldActor(Character actor)
        {
            if (actor == null || EnemyActors.Contains(actor)) return false;
            // Authority outranks the defender snapshot. If COOP attaches network ownership after a
            // match started, the actor stops being locally mutable even if it was previously local.
            if (PvpCompatibility.IsNetworkOwnedActor(actor)) return true;
            if (Defenders.Contains(actor) || DefenderPets.Contains(actor)) return false;
            // Ordinary local Sims remain world combatants.
            NPC npc = CharacterNpc(actor);
            try
            {
                bool simPlayer = npc != null && (npc.SimPlayer || npc.ThisSim != null);
                bool ownedOrSummoned = actor.Master != null || (npc != null && npc.SummonedByPlayer);
                bool resourceObject = npc != null && (npc.MiningNode || npc.TreasureChest);
                bool neverAggro = npc != null && npc.NeverAggro;
                bool knownFriendlyFaction = actor.MyFaction == Character.Faction.Player || actor.MyFaction == Character.Faction.PC ||
                    actor.MyFaction == Character.Faction.Villager || actor.MyFaction == Character.Faction.DEBUG;
                return PvpWorldCombatPolicy.IsProtectedNonCombat(simPlayer, ownedOrSummoned, actor.isVendor,
                    actor.Invulnerable, neverAggro, resourceObject, knownFriendlyFaction);
            }
            catch { return false; } // unknown is not proof of neutral/noncombat; defer to native.
        }

        internal static string DescribeParticipant(Character actor)
        {
            if (actor == null) return "none";
            if (EnemyActors.Contains(actor)) return "attacker:" + Describe(actor, null);
            if (Defenders.Contains(actor)) return "defender:" + Describe(actor, null);
            if (DefenderPets.Contains(actor) || RegisterDefenderPet(actor)) return "defender_pet:" + Describe(actor, null);
            return DescribeCombatTarget(actor);
        }

        internal static string DescribeCombatTarget(Character actor)
        {
            if (actor == null) return "none";
            if (EnemyActors.Contains(actor)) return "attacker:" + Describe(actor, null);
            if (Defenders.Contains(actor)) return "defender:" + Describe(actor, null);
            if (DefenderPets.Contains(actor) || RegisterDefenderPet(actor)) return "defender_pet:" + Describe(actor, null);
            NPC npc = CharacterNpc(actor);
            try
            {
                if (PvpCompatibility.IsNetworkOwnedActor(actor)) return "protected_network_actor:" + Describe(actor, npc);
                if (npc != null && (npc.SimPlayer || npc.ThisSim != null)) return "world_sim:" + Describe(actor, npc);
                if (actor.Master != null || (npc != null && npc.SummonedByPlayer)) return "world_pet:" + Describe(actor, npc);
            }
            catch { }
            return (IsProtectedWorldActor(actor) ? "protected_world:" : "native_world:") + Describe(actor, npc);
        }

        private static Character NpcCharacter(NPC npc)
        {
            try
            {
                if (npc == null) return null;
                Character actor = npc.GetComponent<Character>();
                return actor != null ? actor : npc.GetComponentInParent<Character>();
            }
            catch { return null; }
        }

        private static NPC CharacterNpc(Character actor)
        {
            if (actor == null) return null;
            try
            {
                if (actor.MyNPC != null) return actor.MyNPC;
                NPC npc = actor.GetComponent<NPC>();
                return npc != null ? npc : actor.GetComponentInParent<NPC>();
            }
            catch { return null; }
        }

        internal static string RunSelfTests()
        {
            string world = PvpWorldCombatPolicy.RunSelfTests();
            if (!world.StartsWith("PASS", StringComparison.Ordinal)) return world;
            return "PASS pvp combat containment/world-combat policy";
        }

        internal static string BeginLethalFight(NPC cloneNpc, Character cloneActor, CastSpell cloneSpells)
        {
            return BeginLethalFight(new List<NPC> { cloneNpc }, new List<Character> { cloneActor }, new List<CastSpell> { cloneSpells });
        }

        internal static string BeginLethalFight(IList<NPC> cloneNpcs, IList<Character> cloneActors, IList<CastSpell> cloneSpells)
        {
            Character player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself;
            if (cloneNpcs == null || cloneActors == null || cloneNpcs.Count == 0 || cloneNpcs.Count != cloneActors.Count || player == null || player.MyStats == null)
                return "[Erenshor PvP] Lethal fight blocked: combat actors are not ready.";
            for (int i = 0; i < cloneActors.Count; i++)
                if (cloneNpcs[i] == null || cloneActors[i] == null || cloneActors[i].MyStats == null || cloneActors[i].MyStats.CurrentHP <= 0 || cloneActors[i].MyStats.CurrentMaxHP <= 0)
                    return "[Erenshor PvP] Lethal fight blocked: proxy " + (i + 1) + " has no valid HP.";
            if (player.MyStats.CurrentHP <= 0 || player.MyStats.CurrentMaxHP <= 0)
                return "[Erenshor PvP] Lethal fight blocked: player has no valid HP.";
            _cloneNpc = cloneNpcs[0]; _cloneActor = cloneActors[0]; _player = player; _targetTestEnds = 0f;
            EnemyNpcs.Clear(); EnemyActors.Clear(); Defenders.Clear();
            EnemyNpcs.AddRange(cloneNpcs.Where(x => x != null)); EnemyActors.AddRange(cloneActors.Where(x => x != null));
            Defenders.Add(player); AddPartyDefenders(); SnapshotDefenderPets();
            _fightStartedAt = Time.unscaledTime; _combatStartupDeadline = _fightStartedAt + PvpCombatStartupPolicy.DefaultStartupWindowSeconds;
            _damageToAttackers = 0; _petDamageToAttackers = 0; _damageToDefenders = 0;
            _healingToAttackers = 0; _healingToDefenders = 0; _damageContextPet = false; _damageSourceProxyId = 0; _healTelemetryDepth = 0;
            _loggedDamageToDefender = false; _loggedHealToAttacker = false;
            _lethalFight = true; _retreatRolled = false;
            try
            {
                // GO releases the inert structural proxy. NPC.Start is intentionally enabled here,
                // after the countdown, so Unity owns the proxy's natural Start callback and its
                // native loop creation. The bounded startup watchdog classifies a post-GO failure.
                PvpTemporaryCloneFactory.NotifyGoReleased();
                int seeded = 0;
                for (int i = 0; i < EnemyNpcs.Count; i++)
                {
                    NPC npc = EnemyNpcs[i]; Character actor = EnemyActors[i];
                    CastSpell caster = cloneSpells != null && i < cloneSpells.Count ? cloneSpells[i] : null;
                    if (caster != null) caster.enabled = true;
                    actor.enabled = true;
                    npc.enabled = true;
                    npc.NeverAggro = false;
                    try
                    {
                        NavMeshAgent nav = actor.GetComponent<NavMeshAgent>();
                        if (nav != null)
                        {
                            nav.enabled = true;
                            if (nav.isOnNavMesh) nav.isStopped = false;
                        }
                    }
                    catch { }
                    Character target = Defenders[i % Defenders.Count];
                    npc.ForceAggroOn(target);
                    bool accepted = npc.CurrentAggroTarget == target;
                    if (accepted) seeded++;
                    PvpDiagnostics.Log("go_target_seed proxy=" + (i + 1) + "; target=" +
                        DescribeCombatTarget(target) + "; accepted=" + accepted);
                }
                PvpDiagnostics.Log("go_release attackers=" + EnemyNpcs.Count +
                    "; neverAggro=false; defenders_released=true; natural_start=enabled_after_go; target_seeded=" + seeded);
                PvpDiagnostics.Log("lethal_started attackers=" + EnemyActors.Count + "; defenders=" + Defenders.Count +
                    "; defender_pets=" + DefenderPets.Count + "; player_hp=" + player.MyStats.CurrentHP + "/" +
                    player.MyStats.CurrentMaxHP + "; nav_health=prewarmed");
                return "[Erenshor PvP] Lethal team PvP started: " + EnemyActors.Count +
                    " attacker(s) vs " + Defenders.Count + " defender(s).";
            }
            catch
            {
                End("start_failed");
                return "[Erenshor PvP] Lethal fight failed safely before combat began.";
            }
        }

        internal static void ObserveProxyNativeStartCompleted(NPC npc)
        {
            if (!LethalFightActive || npc == null) return;
            int index = EnemyNpcs.IndexOf(npc);
            if (index < 0 || Defenders.Count == 0) return;
            try
            {
                npc.NeverAggro = false;
                Character target = Defenders[index % Defenders.Count];
                npc.ForceAggroOn(target);
                bool accepted = npc.CurrentAggroTarget == target;
                PvpDiagnostics.Log("native_start_target_seed proxy=" + (index + 1) +
                    "; target=" + DescribeCombatTarget(target) + "; accepted=" + accepted);
                // Do not fabricate a target if native aggro rejects it. The bounded startup watchdog
                // will fail the match with no rewards if native combat cannot become active.
            }
            catch (Exception ex)
            {
                PvpDiagnostics.Log("native_start_target_seed proxy=" + (index + 1) +
                    "; accepted=false; error=" + ex.GetType().Name);
            }
        }

        // One proxy losing its native Start must not destroy the encounter. Remove just that
        // attacker and report how many remain; the caller ends the match only when none are left.
        internal static int RemoveAttacker(NPC npc)
        {
            if (npc == null) return EnemyNpcs.Count;
            int index = EnemyNpcs.IndexOf(npc);
            if (index >= 0)
            {
                EnemyNpcs.RemoveAt(index);
                if (index < EnemyActors.Count) EnemyActors.RemoveAt(index);
            }
            if (EnemyNpcs.Count > 0 && _cloneNpc == npc)
            {
                _cloneNpc = EnemyNpcs[0];
                _cloneActor = EnemyActors.Count > 0 ? EnemyActors[0] : null;
            }
            return EnemyNpcs.Count;
        }

        internal static bool LethalAttackersRemain { get { return _lethalFight && EnemyNpcs.Count > 0; } }

        private static void AddPartyDefenders()
        {
            try
            {
                if (GameData.GroupMembers == null) return;
                foreach (SimPlayerTracking tracking in GameData.GroupMembers)
                {
                    Character actor = tracking == null || tracking.MyAvatar == null || tracking.MyAvatar.MyStats == null ? null : tracking.MyAvatar.MyStats.Myself;
                    if (actor != null && actor.Alive && !Defenders.Contains(actor)) Defenders.Add(actor);
                }
            }
            catch { }
        }

        private static void SnapshotDefenderPets()
        {
            DefenderPets.Clear();
            try
            {
                foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
                {
                    Character actor = npc == null ? null : npc.GetComponent<Character>();
                    if (actor == null && npc != null) actor = npc.GetComponentInParent<Character>();
                    RegisterDefenderPet(actor);
                }
            }
            catch { }
        }

        // Character.Master is the verified native ownership link used for summoned pets. Walk a
        // short bounded chain so a pet or nested summon is admitted only when it resolves to the
        // local player or a living party defender already captured for this encounter.
        private static bool RegisterDefenderPet(Character actor)
        {
            if (actor == null || Defenders.Contains(actor) || EnemyActors.Contains(actor)) return false;
            Character owner = null;
            try { owner = actor.Master; } catch { return false; }
            for (int depth = 0; owner != null && depth < 4; depth++)
            {
                if (Defenders.Contains(owner))
                {
                    DefenderPets.Add(actor);
                    return true;
                }
                try { owner = owner.Master; } catch { return false; }
            }
            return false;
        }

        private static void LogBalanceSummary(string reason)
        {
            try
            {
                int defeated = EnemyActors.Count(x => x == null || !x.Alive || x.MyStats == null || x.MyStats.CurrentHP <= 0);
                List<int> levels = EnemyActors.Where(x => x != null && x.MyStats != null && x.MyStats.Level > 0).Select(x => x.MyStats.Level).ToList();
                int averageLevel = levels.Count == 0 ? 0 : Mathf.RoundToInt((float)levels.Average());
                float seconds = _fightStartedAt <= 0f ? 0f : Mathf.Max(0f, Time.unscaledTime - _fightStartedAt);
                PvpDiagnostics.Log("balance_summary reason=" + (reason ?? "unknown") +
                    "; seconds=" + seconds.ToString("0.0") + "; attackers=" + EnemyActors.Count +
                    "; attackers_defeated=" + defeated + "; enemy_avg_level=" + averageLevel +
                    "; defenders=" + Defenders.Count + "; defender_pets=" + DefenderPets.Count +
                    "; damage_to_attackers=" + _damageToAttackers + "; pet_damage=" + _petDamageToAttackers +
                    "; healing_to_attackers=" + _healingToAttackers +
                    "; damage_to_defenders=" + _damageToDefenders + "; healing_to_defenders=" + _healingToDefenders +
                    "; " + PvpTemporaryCloneFactory.BalanceRuntimeSummary() +
                    "; healing_assessment=" + PvpTemporaryCloneFactory.BalanceHealingAssessment(_healingToAttackers));
                // One bounded per-proxy line per fight, logged here only - never per frame. See
                // PvpTemporaryCloneFactory.LogPerProxyAbilitySummary.
                PvpTemporaryCloneFactory.LogPerProxyAbilitySummary();
            }
            catch { }
        }

        private static string Describe(Character actor, NPC npc)
        {
            try
            {
                if (actor != null && actor.MyStats != null && !string.IsNullOrWhiteSpace(actor.MyStats.MyName)) return actor.MyStats.MyName;
                if (npc != null && !string.IsNullOrWhiteSpace(npc.NPCName)) return npc.NPCName;
                if (actor != null) return actor.name;
                if (npc != null) return npc.name;
            }
            catch { }
            return "unknown";
        }

    }

    [HarmonyPatch(typeof(NPC), "AggroOn")]
    internal static class PvpCloneAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __0) { return PvpCombatContainment.AllowAggro(__instance, __0); }
    }

    [HarmonyPatch(typeof(NPC), "ForceAggroOn")]
    internal static class PvpCloneForceAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __0) { return PvpCombatContainment.AllowAggro(__instance, __0); }
    }

    [HarmonyPatch(typeof(NPC), "ManageAggro")]
    internal static class PvpCloneManageAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __1) { return PvpCombatContainment.AllowAggro(__instance, __1); }
    }

    [HarmonyPatch(typeof(Character), "DamageMe")]
    internal static class PvpCloneDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, bool __1, Character __3, ref int __result, ref PvpCombatContainment.DamageTelemetryState __state)
        { return PvpCombatContainment.PrepareDamage(__instance, __3, __1, ref __result, ref __state); }
        [HarmonyPostfix]
        private static void Postfix(Character __instance, PvpCombatContainment.DamageTelemetryState __state)
        { PvpCombatContainment.FinishDamage(__state); }
    }

    [HarmonyPatch(typeof(Character), "MagicDamageMe")]
    internal static class PvpCloneMagicDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, bool __1, Character __3, ref int __result, ref PvpCombatContainment.DamageTelemetryState __state)
        { return PvpCombatContainment.PrepareDamage(__instance, __3, __1, ref __result, ref __state); }
        [HarmonyPostfix]
        private static void Postfix(Character __instance, PvpCombatContainment.DamageTelemetryState __state)
        { PvpCombatContainment.FinishDamage(__state); }
    }

    [HarmonyPatch(typeof(Character), "BleedDamageMe")]
    internal static class PvpCloneBleedDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, bool __1, Character __2, ref int __result, ref PvpCombatContainment.DamageTelemetryState __state)
        { return PvpCombatContainment.PrepareDamage(__instance, __2, __1, ref __result, ref __state); }
        [HarmonyPostfix]
        private static void Postfix(Character __instance, PvpCombatContainment.DamageTelemetryState __state)
        { PvpCombatContainment.FinishDamage(__state); }
    }

    [HarmonyPatch(typeof(Stats), "ReduceHP")]
    internal static class PvpReduceHpTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Stats __instance, ref PvpCombatContainment.HpTelemetryState __state)
        { PvpCombatContainment.BeginHpTelemetry(__instance, ref __state); }
        [HarmonyPostfix]
        private static void Postfix(Stats __instance, PvpCombatContainment.HpTelemetryState __state)
        { PvpCombatContainment.FinishHpReduction(__instance, __state); }
    }

    [HarmonyPatch(typeof(Stats), "HealMe", new Type[] { typeof(Spell), typeof(int), typeof(bool), typeof(bool), typeof(Character) })]
    internal static class PvpSpellHealTelemetryPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, int __1, Character __4, ref PvpCombatContainment.HpTelemetryState __state)
        {
            __state = new PvpCombatContainment.HpTelemetryState();
            if (!PvpCombatContainment.AllowHeal(__instance, __4)) return false;
            PvpCombatContainment.BeginHealTelemetry(__instance, __4, __1, ref __state);
            return true;
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Stats __instance, PvpCombatContainment.HpTelemetryState __state)
        { PvpCombatContainment.FinishHpHealing(__instance, __state); return __exception; }
    }

    [HarmonyPatch(typeof(Stats), "HealMe", new Type[] { typeof(int) })]
    internal static class PvpFlatHealTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Stats __instance, int __0, ref PvpCombatContainment.HpTelemetryState __state)
        { PvpCombatContainment.BeginHealTelemetry(__instance, __0, ref __state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Stats __instance, PvpCombatContainment.HpTelemetryState __state)
        { PvpCombatContainment.FinishHpHealing(__instance, __state); return __exception; }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int) })]
    internal static class PvpStatusEffect3Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, ref int __result, ref PvpCombatContainment.StatusEffectTelemetryState __state)
        { return PvpCombatContainment.PrepareStatusEffect(__instance, __0, null, ref __result, ref __state); }
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, PvpCombatContainment.StatusEffectTelemetryState __state)
        { PvpCombatContainment.FinishStatusEffect(__state); return __exception; }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class PvpStatusEffect4Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref int __result, ref PvpCombatContainment.StatusEffectTelemetryState __state)
        { return PvpCombatContainment.PrepareStatusEffect(__instance, __0, __3, ref __result, ref __state); }
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, PvpCombatContainment.StatusEffectTelemetryState __state)
        { PvpCombatContainment.FinishStatusEffect(__state); return __exception; }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character), typeof(float) })]
    internal static class PvpStatusEffect5Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref int __result, ref PvpCombatContainment.StatusEffectTelemetryState __state)
        { return PvpCombatContainment.PrepareStatusEffect(__instance, __0, __3, ref __result, ref __state); }
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, PvpCombatContainment.StatusEffectTelemetryState __state)
        { PvpCombatContainment.FinishStatusEffect(__state); return __exception; }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffectNoChecks", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class PvpStatusEffectNoChecksPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref PvpCombatContainment.StatusEffectTelemetryState __state)
        { int ignored = 0; return PvpCombatContainment.PrepareStatusEffect(__instance, __0, __3, ref ignored, ref __state); }
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, PvpCombatContainment.StatusEffectTelemetryState __state)
        { PvpCombatContainment.FinishStatusEffect(__state); return __exception; }
    }
}
