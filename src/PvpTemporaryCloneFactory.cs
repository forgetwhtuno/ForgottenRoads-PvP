using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorPvP
{
    // Temporary encounter actors never enter SimPlayerMngr's persistent collections. They are
    // inert until PvpCombatContainment explicitly begins an approved lethal encounter.
    internal static class PvpTemporaryCloneFactory
    {
        private static readonly HashSet<int> TemporaryActorIds = new HashSet<int>();
        private static bool _suppressPersistentLoad;
        private static GameObject _clone;
        private static PvpOpponentProfile _activeProfile;
        private static readonly List<GameObject> TeamClones = new List<GameObject>();
        private static readonly List<PvpOpponentProfile> TeamProfiles = new List<PvpOpponentProfile>();
        private static readonly List<Spell> TemporarySpells = new List<Spell>();
        private static readonly Dictionary<int, Animator> VisualAnimators = new Dictionary<int, Animator>();
        private static readonly Dictionary<int, Transform> VisualShellRoots = new Dictionary<int, Transform>();
        private static readonly HashSet<int> EquipmentVisualsApplied = new HashSet<int>();
        private static readonly HashSet<int> ClassLoadoutsApplied = new HashSet<int>();
        private static readonly Dictionary<int, int> EligibleCombatSpellCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> EligibleHealSpellCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackDecisionCounts = new Dictionary<int, int>();
        // Split, per-proxy versions of AttackDecisionCounts (0.5.11). AttackDecisionCounts stays
        // as-is for the existing combined-evidence consumer (HasAnyNativeCombatEvidence); these two
        // exist so the terminal summary can report DoAttackSkill vs DoAttackSpell decisions
        // separately instead of the combined total being mislabeled as spell-only.
        private static readonly Dictionary<int, int> AttackSkillDecisionCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackSpellDecisionCounts = new Dictionary<int, int>();
        private static readonly HashSet<int> AttackSkillDecisionLogged = new HashSet<int>();
        private static readonly HashSet<int> AttackSpellDecisionLogged = new HashSet<int>();
        private static readonly Dictionary<int, int> HealCheckCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealRaidCheckCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellStartCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> NativeSpellRequestCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackSpellRequestCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealNativeRequestCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellContainmentRejectCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellAcceptedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackSpellAcceptedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealNativeAcceptedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellRejectedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellCompletedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackSpellCompletedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealCompletedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellEffectCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> AttackSpellEffectCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealEffectCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealTargetResolvedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealLegalAllyCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealSpellSelectionCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealBridgeRequestCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealBridgeAcceptedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, string> LatestAttackSpellAssessment = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> LatestHealAssessment = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> LastSpellStartFrame = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> LastSpellAcceptedFrame = new Dictionary<int, int>();
        // Per-proxy effective outcome telemetry (0.5.11). Populated from PvpCombatContainment's
        // existing damage/heal telemetry hooks when the source is identifiable as a temporary
        // attacker, so "did this proxy's cast actually land" can be answered per proxy instead of
        // only as a whole-team total.
        private static readonly Dictionary<int, long> DamageDealtCounts = new Dictionary<int, long>();
        private static readonly Dictionary<int, long> HealingDoneCounts = new Dictionary<int, long>();
        private static readonly HashSet<int> NativeStartCompleted = new HashSet<int>();
        private static readonly HashSet<int> NativeCharacterStartCompleted = new HashSet<int>();
        private sealed class RewardSuppressionProof
        {
            internal int ActorInstanceId;
            internal int LootInstanceId;
            internal bool Verified;
            internal string Source = string.Empty;
            internal string Reason = string.Empty;
        }
        private static readonly Dictionary<int, RewardSuppressionProof> RewardSuppressionProofs = new Dictionary<int, RewardSuppressionProof>();
        private static readonly HashSet<int> RewardSuppressionStateLogged = new HashSet<int>();
        private static readonly HashSet<int> RewardHoldCheckLogged = new HashSet<int>();
        private static readonly HashSet<string> RewardStateSnapshotLogged = new HashSet<string>();
        private static readonly HashSet<int> EquipmentForensicsLogged = new HashSet<int>();
        // Cleanup contract: NativeCharacterStartCompleted.Clear(); RewardSuppressionProofs.Clear(); RewardSuppressionStateLogged.Clear();
        // Retirement contract: RewardSuppressionProofs.Remove(id); RewardSuppressionStateLogged.Remove(id);
        private static readonly HashSet<int> StartupSpellCooldownNormalized = new HashSet<int>();
        private sealed class ProxyPreparationState
        {
            internal bool NativeStartCompleted;
            internal bool RuntimeInvariantPassed;
            internal bool StatsReady;
            internal bool CharacterReady;
            internal bool CasterReady;
            internal bool SpellsReady;
            internal bool NavAgentReady;
            internal bool OnNavMesh;
            internal string NavReadinessDetail = string.Empty;
            internal bool VisualShellReady;
            internal bool RewardSuppressionReady;
            internal string RewardSuppressionDetail = string.Empty;
            internal bool StartupNpcSpellCooldownNormalized;
            internal bool AttackStartupMaintenanceReady;
            internal bool InitialTargetCanBeResolved;
            internal bool NativeNavLoopReady;
            internal bool NativeBehaviorLoopReady;
            internal bool CombatLoopInfrastructureReady;
            internal bool PreGoLoopBoundaryReady;
            internal bool Ready;
            internal string LastReason = "not_evaluated";
            internal bool ReadyLogged;
        }
        private static readonly Dictionary<int, ProxyPreparationState> PreparationStates = new Dictionary<int, ProxyPreparationState>();
        private static float _goReleasedAt;
        private static readonly Dictionary<string, HashSet<int>> FirstActionStages = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> VesselCreatedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> VesselResolveCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealMeEnteredCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> StatusAppliedCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> VesselDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> EffectDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> LastVesselCreatedAt = new Dictionary<int, float>();
        private sealed class NativeNavProbe
        {
            internal bool CoroutineObserved;
            internal bool UpdateNavReached;
            internal bool FirstUpdateNavCompleted;
            internal bool Faulted;
            internal string FaultType = string.Empty;
            internal bool DestinationAttempted;
            internal bool MovementObserved;
            internal Vector3 StartPosition;
            internal Vector3 LastPosition;
            internal Vector3 PursuitStartPosition;
            internal Vector3 ShellStartPosition;
            internal Vector3 ShellStartLocalPosition;
            internal Vector3 Destination;
            internal float PursuitSampleAt;
            internal bool PursuitSamplePending;
            internal bool PursuitResultLogged;
            internal bool PursuitNeedsMovement;
            internal int PursuitTargetId;
        }
        internal struct AttackSpellEntryState
        {
            internal bool Active;
            internal int ProxyId;
            internal int RequestsBefore;
            internal string Assessment;
        }
        private sealed class CastExecutionProbe
        {
            internal Spell Spell;
            internal Stats Target;
            internal PvpSpellSemanticCategory Category;
            internal bool Beneficial;
            internal bool CountsAsHealing;
            internal bool VesselCreated;
            internal bool ResolveReached;
            internal bool HealMeEntered;
            internal bool StatusApplied;
            internal float AcceptedAt;
            internal long DamageBefore;
            internal long HealingBefore;
            internal int TargetHpBefore;
            internal int AnimatorStateAtAccept;
            internal bool EffectObserved;
        }
        private static readonly Dictionary<int, CastExecutionProbe> CastExecutionProbes = new Dictionary<int, CastExecutionProbe>();
        private static readonly Dictionary<int, int> AttackEntryDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellRequestDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> SpellResultDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> HealDetailLogCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> CastFinishDetailLogCounts = new Dictionary<int, int>();
        private static int _activeMeleeProxyId;
        private static int _activeMeleeDepth;
        private static readonly Dictionary<int, long> MeleeDamageCounts = new Dictionary<int, long>();
        private static readonly Dictionary<int, NativeNavProbe> NativeNavProbes = new Dictionary<int, NativeNavProbe>();
        private static readonly Dictionary<int, System.Collections.IEnumerator> ModOwnedNavLoops = new Dictionary<int, System.Collections.IEnumerator>();
        private static readonly Dictionary<int, System.Collections.IEnumerator> ModOwnedBehaviorLoops = new Dictionary<int, System.Collections.IEnumerator>();
        private static readonly Dictionary<int, int> ModOwnedNavLoopStarts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> ModOwnedBehaviorLoopStarts = new Dictionary<int, int>();
        private static readonly HashSet<int> CombatLoopRecoveryLogged = new HashSet<int>();
        private static readonly HashSet<int> CombatBranchTraceLogged = new HashSet<int>();
        private static readonly HashSet<int> NonRaidBehaviorReached = new HashSet<int>();
        private static readonly HashSet<int> PreGoBehaviorBlockedLogged = new HashSet<int>();
        private static readonly HashSet<int> PreGoNavBlockedLogged = new HashSet<int>();
        private static readonly HashSet<int> NativeUpdateReached = new HashSet<int>();
        private static readonly HashSet<int> CombatSectionReached = new HashSet<int>();
        private static readonly HashSet<int> LegalTargetAcquired = new HashSet<int>();
        private static readonly HashSet<int> NavPursuitRequested = new HashSet<int>();
        private static readonly Dictionary<int, int> MeleeAttemptCounts = new Dictionary<int, int>();
        private static readonly HashSet<int> NativeUpdateExceptionLogged = new HashSet<int>();
        private static bool _nativeSimComparisonLogged;
        private static bool _healthyNativeCastLogged;
        private static bool _runtimeInvalidCleanupQueued;
        private static string _runtimeInvalidReason = string.Empty;
        private static float _despawnAt;
        private static string _activeMatchId;
        private static PvpEncounterMode _activeMode = PvpEncounterMode.Arranged;
        private static string _activeMotive = string.Empty;
        private static string _templateSource = "none";
        private const float PlayerProtectedClearance = 10f;
        private const float SpawnProtectedClearance = 8f;
        private const float SpawnActorCollisionClearance = 1.5f;
        private const float FormationDistance = 11f;

        internal static bool SuppressPersistentLoad { get { return _suppressPersistentLoad; } }
        internal static bool HasActiveTeam { get { return TeamClones.Any(x => x != null); } }

        internal static void Tick()
        {
            if (_runtimeInvalidCleanupQueued)
            {
                _runtimeInvalidCleanupQueued = false;
                Despawn("runtime_invalid");
                return;
            }
            TickNativeNavHealth();
            TickCastExecutionHealth();
            PvpCombatContainment.Tick();
            if (TeamClones.Count > 0 && Time.unscaledTime >= _despawnAt) Despawn("timer");
        }

        internal static string SpawnVisualClone() { return SpawnVisualClone("PvP Proxy"); }

        internal static string SpawnVisualClone(PvpOpponentProfile profile)
        {
            return SpawnVisualClone(profile == null ? "PvP Proxy" : profile.Name, profile);
        }

        internal static string SpawnVisualClone(string opponentName) { return SpawnVisualClone(opponentName, null); }

        private static string SpawnVisualClone(string opponentName, PvpOpponentProfile profile)
        {
            if (TeamClones.Count > 0) return "[Erenshor PvP] A temporary PvP team is already active. Use /epvp despawn.";
            List<Vector3> positions; string reason;
            if (!TryFindClearFormation(1, out positions, out reason)) return "[Erenshor PvP] Clone spawn blocked: " + reason;
            return SpawnMember(opponentName, profile, 0, positions[0]);
        }

        internal static string SpawnTeam(PvpTeamPlan plan, string matchId)
        { return SpawnTeam(plan, matchId, PvpEncounterMode.Arranged, "party_match"); }

        internal static string SpawnTeam(PvpTeamPlan plan, string matchId, PvpEncounterMode mode, string motive)
        {
            if (plan == null || plan.Members.Count == 0) return "[Erenshor PvP] Team spawn blocked: empty plan.";
            if (TeamClones.Count > 0) return "[Erenshor PvP] A temporary PvP team is already active. Use /epvp despawn.";
            List<Vector3> positions; string clearanceReason;
            if (!TryFindClearFormation(plan.Members.Count, out positions, out clearanceReason))
                return "[Erenshor PvP] Team spawn blocked: " + clearanceReason;
            _activeMatchId = matchId ?? string.Empty;
            _activeMode = mode; _activeMotive = motive ?? string.Empty;
            for (int i = 0; i < plan.Members.Count; i++)
            {
                PvpOpponentProfile profile = plan.Members[i].Profile;
                string result = SpawnMember(profile.Name, profile, i, positions[i]);
                if (result.IndexOf("spawned", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Despawn("team_spawn_failed");
                    return result;
                }
            }
            _despawnAt = Time.unscaledTime + 30f;
            return "[Erenshor PvP] PvP team spawned: " + plan.Members.Count + " off-map profiles, average level " + plan.AverageLevel + ".";
        }

        private static string SpawnMember(string opponentName, PvpOpponentProfile profile, int memberIndex, Vector3 position)
        {
            try
            {
                if (GameData.Zoning || GameData.PlayerControl == null || GameData.PlayerControl.Myself == null)
                    return "[Erenshor PvP] Clone test blocked: game state is not ready.";
                GameObject template = FindNativeMobTemplate();
                if (template == null) return "[Erenshor PvP] PvP needs a nearby native mob template in this zone; no proxy was spawned.";

                Character player = GameData.PlayerControl.Myself;
                GameObject clone;
                Vector3 towardPlayer = player.transform.position - position; towardPlayer.y = 0f;
                Quaternion rotation = towardPlayer.sqrMagnitude > .01f ? Quaternion.LookRotation(towardPlayer.normalized, Vector3.up) : player.transform.rotation;
                clone = UnityEngine.Object.Instantiate(template, position, rotation);
                if (clone == null) return "[Erenshor PvP] Clone test failed safely: instantiate returned null.";

                // The source mob is already running. Freeze the newly instantiated root in the
                // construction frame so none of its copied MonoBehaviours can receive a Start
                // callback before PvP has established its disposable identity and inert hold.
                // The root is reactivated for structural preparation; NPC itself remains disabled
                // until GO, where Unity owns the fresh natural Start transition.
                clone.SetActive(false);

                clone.name = "PvP_TemporaryClone";
                string proxyName = string.IsNullOrWhiteSpace(opponentName) ? "PvP Proxy" : opponentName + " (PvP)";
                clone.name = "PvP_TemporaryClone_" + proxyName;
                SimPlayer sim = clone.GetComponent<SimPlayer>();
                if (sim != null) { sim.myIndex = -1; sim.MySimTracking = null; sim.enabled = false; }
                NPC npc = clone.GetComponent<NPC>();
                Character actor = clone.GetComponent<Character>();
                CastSpell spells = clone.GetComponent<CastSpell>();
                NavMeshAgent nav = clone.GetComponent<NavMeshAgent>();
                SimPlayerLanguage language = clone.GetComponent<SimPlayerLanguage>();
                LootTable loot = clone.GetComponent<LootTable>();
                if (npc != null)
                {
                    npc.NPCName = proxyName;
                    npc.ThisSim = null;
                    npc.SimPlayer = false;
                    npc.InGroup = false;
                    npc.NeverAggro = true;
                    ApplyProfileLoadout(npc, profile);
                    ConfigureProfileWeaponRuntime(npc, profile, false);
                    npc.enabled = false;
                }
                if (actor != null)
                {
                    TrySetCharacterXp(actor, 0);
                    TrySetField(actor, "BossXp", 0f);
                    TrySetField(actor, "QuestCompleteOnDeath", null);
                    TrySetField(actor, "factionMods", new ModifyFaction[0]);
                    actor.NeverTrack = true;
                    actor.enabled = false;
                    ConfigureCombatStats(player, actor, profile);
                }
                if (loot != null) { loot.MinGold = 0; loot.MaxGold = 0; loot.MyGold = 0; loot.enabled = false; }
                if (language != null) language.enabled = false;
                if (spells != null) spells.enabled = false;
                if (nav != null) nav.enabled = false;

                ConfigureNativeMaintenanceState(npc, actor, spells, nav);
                if (npc != null)
                {
                    // IEnumerator fields copied from a source component are never proof of a
                    // scheduled coroutine on the clone. The proxy's natural pre-GO Start owns normal
                    // initialization; 0.5.15 recovers only whichever nav/behavior loop Start skips
                    // under the intentional NeverAggro preparation hold.
                    TrySetField(npc, "navDo", null);
                    TrySetField(npc, "behDo", null);
                }

                if (profile != null) AttachSimVisualShell(clone, profile);
                try
                {
                    if (npc != null)
                    {
                        npc.UpdateNamePlate();
                        if (npc.NamePlate != null) foreach (Renderer renderer in npc.NamePlate.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                    }
                }
                catch { }

                TemporaryActorIds.Add(clone.GetInstanceID());
                TeamClones.Add(clone);
                TeamProfiles.Add(profile);
                if (_clone == null) { _clone = clone; _activeProfile = profile; }
                string runtimeState;
                bool runtimeReady = ValidateNativeMaintenanceState(npc, actor, actor == null ? null : actor.MyStats, spells, nav, out runtimeState);
                PvpDiagnostics.Log("proxy_runtime_state proxy=" + proxyName + "; requiredRuntimeState=" +
                    (runtimeReady ? "PASS" : "FAIL") + "; missing=" + runtimeState + "; template=" + _templateSource);
                if (!runtimeReady)
                {
                    Despawn("runtime_state_missing");
                    return "[Erenshor PvP] Clone test blocked: temporary opponent native runtime is incomplete.";
                }
                _despawnAt = Time.unscaledTime + 20f;
                ErenshorPvpEvents.Publish(new PvpSemanticEvent("pvp_proxy_spawned", _activeMatchId, proxyName,
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "visual_test", "inert_temporary_actor"));
                string hp = actor == null || actor.MyStats == null ? "unknown" : actor.MyStats.CurrentHP + "/" + actor.MyStats.CurrentMaxHP;
                PvpDiagnostics.Log("proxy_spawn hp=" + hp + "; level=" + (actor == null || actor.MyStats == null ? "unknown" : actor.MyStats.Level.ToString()) + "; profile=" + (profile == null ? "test" : profile.Describe()) + "; template=" + _templateSource + "; persistent_sim=false");
                return "[Erenshor PvP] Temporary native-mob PvP proxy spawned for 20 seconds. HP=" + hp + "; named after an off-map Sim; no Sim roster, loot, XP, quest, or faction identity.";
            }
            catch (Exception ex)
            {
                _suppressPersistentLoad = false;
                Despawn("failure");
                return "[Erenshor PvP] Clone test failed safely (" + ex.GetType().Name + ").";
            }
        }

        internal static bool CanSpawnClearTeam(int memberCount, out string reason)
        {
            List<Vector3> ignored;
            return TryFindClearFormation(memberCount, out ignored, out reason);
        }

        private static bool TryFindClearFormation(int memberCount, out List<Vector3> positions, out string reason)
        {
            positions = new List<Vector3>(); reason = "no clear navigable formation was found";
            Character player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself;
            if (player == null) { reason = "player state is unavailable"; return false; }
            memberCount = Math.Max(1, Math.Min(5, memberCount));
            List<Character> worldActors = OutsideNpcActors(player);
            List<Character> protectedActors = worldActors.Where(PvpCombatContainment.IsProtectedWorldActor).ToList();
            Character nearest = protectedActors.OrderBy(x => (x.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (nearest != null)
            {
                float nearestDistance = Vector3.Distance(nearest.transform.position, player.transform.position);
                if (!MeetsClearance(nearestDistance, PlayerProtectedClearance))
                {
                    reason = "move away from protected world actor " + ActorName(nearest) + " (" + nearestDistance.ToString("0.0") + "m; need " + PlayerProtectedClearance.ToString("0") + "m clearance)";
                    PvpDiagnostics.Log("spawn_clearance blocked=protected_near_player; npc=" + ActorName(nearest) + "; distance=" + nearestDistance.ToString("0.0"));
                    return false;
                }
            }

            Vector3 forward = player.transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward; else forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3[] directions =
            {
                forward, -forward, right, -right,
                (forward + right).normalized, (forward - right).normalized,
                (-forward + right).normalized, (-forward - right).normalized
            };
            NavMeshHit playerHit;
            if (!NavMesh.SamplePosition(player.transform.position, out playerHit, 3f, NavMesh.AllAreas))
            { reason = "the player is not near a navigable combat surface"; return false; }

            for (int d = 0; d < directions.Length; d++)
            {
                List<Vector3> candidate = new List<Vector3>(); bool valid = true;
                for (int i = 0; i < memberCount; i++)
                {
                    float lateral = (i - ((memberCount - 1) * .5f)) * 1.8f;
                    Vector3 intended = player.transform.position + directions[d] * FormationDistance + right * lateral;
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(intended, out hit, 3f, NavMesh.AllAreas) ||
                        protectedActors.Any(x => !MeetsClearance(Vector3.Distance(x.transform.position, hit.position), SpawnProtectedClearance)) ||
                        worldActors.Any(x => !MeetsClearance(Vector3.Distance(x.transform.position, hit.position), SpawnActorCollisionClearance)))
                    { valid = false; break; }
                    NavMeshPath path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    { valid = false; break; }
                    candidate.Add(hit.position);
                }
                if (!valid || candidate.Count != memberCount) continue;
                positions = candidate; reason = string.Empty;
                PvpDiagnostics.Log("spawn_clearance pass members=" + memberCount + "; player_protected_clearance=" + PlayerProtectedClearance +
                    "; spawn_protected_clearance=" + SpawnProtectedClearance + "; spawn_actor_collision_clearance=" + SpawnActorCollisionClearance +
                    "; world_actors=" + worldActors.Count + "; protected_actors=" + protectedActors.Count + "; formation_distance=" + FormationDistance);
                return true;
            }
            PvpDiagnostics.Log("spawn_clearance blocked=no_formation; world_actors=" + worldActors.Count + "; protected_actors=" + protectedActors.Count + "; members=" + memberCount);
            return false;
        }

        private static List<Character> OutsideNpcActors(Character player)
        {
            HashSet<Character> principals = new HashSet<Character> { player };
            try
            {
                if (GameData.GroupMembers != null)
                    foreach (SimPlayerTracking tracking in GameData.GroupMembers)
                    {
                        Character actor = tracking == null || tracking.MyAvatar == null || tracking.MyAvatar.MyStats == null
                            ? null : tracking.MyAvatar.MyStats.Myself;
                        if (actor != null) principals.Add(actor);
                    }
            }
            catch { }
            List<Character> result = new List<Character>();
            try
            {
                foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
                {
                    if (npc == null || !npc.gameObject.activeInHierarchy) continue;
                    Character actor = npc.GetComponent<Character>() ?? npc.GetComponentInParent<Character>();
                    if (actor == null || actor.MyStats == null || !actor.Alive || IsOwnedBy(actor, principals)) continue;
                    if (!result.Contains(actor)) result.Add(actor);
                }
            }
            catch { }
            return result;
        }

        private static bool IsOwnedBy(Character actor, HashSet<Character> principals)
        {
            if (actor == null) return false;
            if (principals.Contains(actor)) return true;
            Character owner = null;
            try { owner = actor.Master; } catch { }
            for (int depth = 0; owner != null && depth < 4; depth++)
            {
                if (principals.Contains(owner)) return true;
                try { owner = owner.Master; } catch { return false; }
            }
            return false;
        }

        private static string ActorName(Character actor)
        {
            try
            {
                if (actor != null && actor.MyStats != null && !string.IsNullOrWhiteSpace(actor.MyStats.MyName)) return actor.MyStats.MyName;
                if (actor != null && actor.MyNPC != null && !string.IsNullOrWhiteSpace(actor.MyNPC.NPCName)) return actor.MyNPC.NPCName;
                return actor == null ? "an NPC" : actor.name;
            }
            catch { return "an NPC"; }
        }

        private static bool MeetsClearance(float distance, float required)
        {
            return distance >= required;
        }

        internal static string RunSpawnPolicySelfTests()
        {
            if (MeetsClearance(9.99f, PlayerProtectedClearance)) return "FAIL protected player clearance";
            if (!MeetsClearance(PlayerProtectedClearance, PlayerProtectedClearance)) return "FAIL protected player boundary";
            if (MeetsClearance(7.99f, SpawnProtectedClearance)) return "FAIL protected spawn clearance";
            if (!MeetsClearance(SpawnProtectedClearance, SpawnProtectedClearance)) return "FAIL protected spawn boundary";
            if (MeetsClearance(1.49f, SpawnActorCollisionClearance)) return "FAIL world actor collision clearance";
            if (!MeetsClearance(SpawnActorCollisionClearance, SpawnActorCollisionClearance)) return "FAIL world actor collision boundary";
            // RewardSuppressionProofPasses(true, false, ...) is a required negative lifecycle case.
            return "PASS pvp spawn/reward readiness";
        }

        internal static string Despawn(string reason)
        {
            PvpCombatContainment.End(reason);
            GameObject clone = _clone;
            // A match that ends without a fight verdict still needs a terminal record, otherwise a
            // consumer such as Nemesis waits forever for a result that will never arrive. It is
            // reported as cancelled/invalid, never as an escape.
            string cancelledMatchId = _activeMatchId;
            string cancelledOpponent = _activeProfile != null ? _activeProfile.Name
                : (TeamProfiles.Count > 0 && TeamProfiles[0] != null ? TeamProfiles[0].Name : "PvP Proxy");
            PvpEncounterMode cancelledMode = _activeMode;
            _clone = null; _activeProfile = null; _despawnAt = 0f; _activeMatchId = string.Empty; _activeMode = PvpEncounterMode.Arranged; _activeMotive = string.Empty;
            string cancelledClassification = ErenshorPvpApi.ClassifyOutcome(reason);
            string cancelledModeToken = cancelledMode.ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(cancelledMatchId))
            {
                if (ErenshorPvpApi.TryRecordResult(cancelledMatchId, cancelledOpponent, reason ?? "manual", cancelledModeToken, cancelledClassification))
                {
                    // Social consumers hear the first terminal result exactly once. The housekeeping
                    // despawn event below carries no match and is not part of their allow lists.
                    ErenshorPvpEvents.Publish(new PvpSemanticEvent("pvp_cancelled", cancelledMatchId, cancelledOpponent,
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, cancelledModeToken, reason ?? "manual", cancelledClassification));
                }
            }
            if (TeamClones.Count == 0 && clone == null) { PreparationStates.Clear(); return "[Erenshor PvP] No temporary clone is active."; }
            foreach (GameObject member in TeamClones)
            {
                if (member == null) continue;
                StopOwnedNativeCombatLoops(member.GetComponent<NPC>());
                TemporaryActorIds.Remove(member.GetInstanceID());
                VisualAnimators.Remove(member.GetInstanceID());
                VisualShellRoots.Remove(member.GetInstanceID());
                EquipmentVisualsApplied.Remove(member.GetInstanceID());
                ClassLoadoutsApplied.Remove(member.GetInstanceID());
                EligibleCombatSpellCounts.Remove(member.GetInstanceID());
                EligibleHealSpellCounts.Remove(member.GetInstanceID());
                AttackDecisionCounts.Remove(member.GetInstanceID());
                AttackSkillDecisionCounts.Remove(member.GetInstanceID());
                AttackSpellDecisionCounts.Remove(member.GetInstanceID());
                AttackSkillDecisionLogged.Remove(member.GetInstanceID());
                AttackSpellDecisionLogged.Remove(member.GetInstanceID());
                HealCheckCounts.Remove(member.GetInstanceID());
                HealRaidCheckCounts.Remove(member.GetInstanceID());
                SpellStartCounts.Remove(member.GetInstanceID());
                SpellAcceptedCounts.Remove(member.GetInstanceID());
                SpellCompletedCounts.Remove(member.GetInstanceID());
                SpellEffectCounts.Remove(member.GetInstanceID());
                HealTargetResolvedCounts.Remove(member.GetInstanceID());
                CastExecutionProbes.Remove(member.GetInstanceID());
                AttackEntryDetailLogCounts.Remove(member.GetInstanceID());
                SpellRequestDetailLogCounts.Remove(member.GetInstanceID());
                SpellResultDetailLogCounts.Remove(member.GetInstanceID());
                HealDetailLogCounts.Remove(member.GetInstanceID());
                CastFinishDetailLogCounts.Remove(member.GetInstanceID());
                MeleeDamageCounts.Remove(member.GetInstanceID());
                DamageDealtCounts.Remove(member.GetInstanceID());
                HealingDoneCounts.Remove(member.GetInstanceID());
                NativeStartCompleted.Remove(member.GetInstanceID());
                NativeCharacterStartCompleted.Remove(member.GetInstanceID());
                RewardSuppressionProofs.Remove(member.GetInstanceID());
                RewardSuppressionStateLogged.Remove(member.GetInstanceID());
                RewardHoldCheckLogged.Remove(member.GetInstanceID());
                RewardStateSnapshotLogged.RemoveWhere(key => key.EndsWith(":" + member.GetInstanceID()));
                StartupSpellCooldownNormalized.Remove(member.GetInstanceID());
                PreparationStates.Remove(member.GetInstanceID());
                VesselCreatedCounts.Remove(member.GetInstanceID());
                VesselResolveCounts.Remove(member.GetInstanceID());
                HealMeEnteredCounts.Remove(member.GetInstanceID());
                StatusAppliedCounts.Remove(member.GetInstanceID());
                VesselDetailLogCounts.Remove(member.GetInstanceID());
                EffectDetailLogCounts.Remove(member.GetInstanceID());
                NativeNavProbes.Remove(member.GetInstanceID());
                NativeUpdateReached.Remove(member.GetInstanceID());
                CombatSectionReached.Remove(member.GetInstanceID());
                LegalTargetAcquired.Remove(member.GetInstanceID());
                NavPursuitRequested.Remove(member.GetInstanceID());
                MeleeAttemptCounts.Remove(member.GetInstanceID());
                NativeUpdateExceptionLogged.Remove(member.GetInstanceID());
                try { UnityEngine.Object.Destroy(member); } catch { }
            }
            TeamClones.Clear(); TeamProfiles.Clear(); VisualAnimators.Clear(); VisualShellRoots.Clear(); EquipmentVisualsApplied.Clear(); ClassLoadoutsApplied.Clear(); EligibleCombatSpellCounts.Clear(); EligibleHealSpellCounts.Clear(); AttackDecisionCounts.Clear(); AttackSkillDecisionCounts.Clear(); AttackSpellDecisionCounts.Clear(); AttackSkillDecisionLogged.Clear(); AttackSpellDecisionLogged.Clear(); HealCheckCounts.Clear(); HealRaidCheckCounts.Clear(); SpellStartCounts.Clear(); NativeSpellRequestCounts.Clear(); AttackSpellRequestCounts.Clear(); HealNativeRequestCounts.Clear(); SpellContainmentRejectCounts.Clear(); SpellAcceptedCounts.Clear(); AttackSpellAcceptedCounts.Clear(); HealNativeAcceptedCounts.Clear(); SpellRejectedCounts.Clear(); SpellCompletedCounts.Clear(); AttackSpellCompletedCounts.Clear(); HealCompletedCounts.Clear(); SpellEffectCounts.Clear(); AttackSpellEffectCounts.Clear(); HealEffectCounts.Clear(); HealTargetResolvedCounts.Clear(); HealLegalAllyCounts.Clear(); HealSpellSelectionCounts.Clear(); HealBridgeRequestCounts.Clear(); HealBridgeAcceptedCounts.Clear(); LatestAttackSpellAssessment.Clear(); LatestHealAssessment.Clear(); CastExecutionProbes.Clear(); AttackEntryDetailLogCounts.Clear(); SpellRequestDetailLogCounts.Clear(); SpellResultDetailLogCounts.Clear(); HealDetailLogCounts.Clear(); CastFinishDetailLogCounts.Clear(); MeleeDamageCounts.Clear(); DamageDealtCounts.Clear(); HealingDoneCounts.Clear(); LastSpellStartFrame.Clear(); LastSpellAcceptedFrame.Clear(); NativeStartCompleted.Clear(); NativeCharacterStartCompleted.Clear(); RewardSuppressionProofs.Clear(); RewardSuppressionStateLogged.Clear(); RewardHoldCheckLogged.Clear(); RewardStateSnapshotLogged.Clear(); StartupSpellCooldownNormalized.Clear(); PreparationStates.Clear(); VesselCreatedCounts.Clear(); VesselResolveCounts.Clear(); HealMeEnteredCounts.Clear(); StatusAppliedCounts.Clear(); VesselDetailLogCounts.Clear(); EffectDetailLogCounts.Clear(); LastVesselCreatedAt.Clear(); FirstActionStages.Clear(); _goReleasedAt = 0f; NativeNavProbes.Clear(); ModOwnedNavLoops.Clear(); ModOwnedBehaviorLoops.Clear(); ModOwnedNavLoopStarts.Clear(); ModOwnedBehaviorLoopStarts.Clear(); CombatLoopRecoveryLogged.Clear(); CombatBranchTraceLogged.Clear(); NonRaidBehaviorReached.Clear(); PreGoBehaviorBlockedLogged.Clear(); PreGoNavBlockedLogged.Clear(); NativeUpdateReached.Clear(); CombatSectionReached.Clear(); LegalTargetAcquired.Clear(); NavPursuitRequested.Clear(); MeleeAttemptCounts.Clear(); NativeUpdateExceptionLogged.Clear(); _runtimeInvalidCleanupQueued = false; _runtimeInvalidReason = string.Empty; _nativeSimComparisonLogged = false; _healthyNativeCastLogged = false; _activeMeleeProxyId = 0; _activeMeleeDepth = 0; DestroyTemporarySpells();
            ErenshorPvpEvents.Publish(new PvpSemanticEvent("pvp_proxy_despawned", string.Empty, "PvP Proxy",
                string.Empty, "cleanup", reason ?? "manual"));
            PvpController.EncounterCleaned();
            return "[Erenshor PvP] Temporary clone removed (" + (reason ?? "manual") + ").";
        }

        internal static void Shutdown() { Despawn("shutdown"); }

        internal static bool IsTemporaryActor(Character actor)
        {
            try { return actor != null && TeamClones.Contains(actor.gameObject); } catch { return false; }
        }

        internal static bool IsTemporaryNpc(NPC npc)
        {
            try { return npc != null && TeamClones.Contains(npc.gameObject); } catch { return false; }
        }

        // Character/NPC Start can rebuild native XP values after the proxy is cloned. Reapply the
        // no-borrowed-rewards boundary immediately before DoDeath so only PvpRewardService pays
        // out for the completed team encounter.
        internal static bool SuppressBorrowedDeathRewards(Character actor)
        {
            if (!IsTemporaryActor(actor)) return false;
            try
            {
                TrySetCharacterXp(actor, 0);
                actor.BossXp = 0f;
                actor.BonusRangeXP = Vector2.zero;
                actor.QuestCompleteOnDeath = null;
                actor.factionMods = new ModifyFaction[0];
                actor.NeverTrack = true;
                NPC npc = actor.GetComponent<NPC>();
                if (npc != null) TrySetField(npc, "SetAchievementOnDefeat", string.Empty);
                LootTable loot = actor.GetComponent<LootTable>();
                if (loot != null)
                {
                    loot.MinGold = 0;
                    loot.MaxGold = 0;
                    loot.MyGold = 0;
                    ClearBorrowedLootCollection(loot, "ActualDrops");
                    ClearBorrowedLootCollection(loot, "ActualDropsQual");
                    loot.enabled = false;
                }
                string reason;
                bool verified = RecordAndVerifyRewardSuppression(actor.gameObject, "apply", out reason);
                LogRewardStateSnapshot(actor.gameObject, "post_apply", verified);
                if (RewardSuppressionStateLogged.Add(actor.gameObject.GetInstanceID()))
                    PvpDiagnostics.Log("reward_suppression_state proxy=" + actor.name + "; applied=true; verified=" + verified +
                        "; actor_id=" + actor.GetInstanceID() + "; loot_id=" + (loot == null ? 0 : loot.GetInstanceID()));
                if (verified) PvpDiagnostics.Log("borrowed_death_rewards_suppressed actor=" + actor.name);
                return verified;
            }
            catch (Exception ex) { Debug.LogWarning("[Erenshor PvP] reward_suppression_failed=" + ex.GetType().Name); return false; }
        }

        private static void ClearBorrowedLootCollection(LootTable loot, string fieldName)
        {
            object value;
            if (loot == null || !TryReadField(loot, fieldName, out value) || value == null) return;
            try { System.Collections.IList list = value as System.Collections.IList; if (list != null) { list.Clear(); return; } } catch { }
            try { System.Collections.IDictionary map = value as System.Collections.IDictionary; if (map != null) map.Clear(); } catch { }
        }

        private static bool BorrowedLootCollectionsEmpty(LootTable loot)
        {
            if (loot == null) return true;
            foreach (string name in new[] { "ActualDrops", "ActualDropsQual" })
            {
                object value;
                if (!TryReadField(loot, name, out value)) return false;
                try { System.Collections.ICollection c = value as System.Collections.ICollection; if (c != null && c.Count != 0) return false; } catch { return false; }
            }
            return true;
        }

        internal static void ObserveNativeCharacterStartCompleted(Character actor)
        {
            if (!IsTemporaryActor(actor) || actor.gameObject == null) return;
            NativeCharacterStartCompleted.Add(actor.gameObject.GetInstanceID());
            // Character.Start is the final pre-GO native writer for xp and factionMods. Reassert
            // exactly once in this completion postfix, after its hydration and before countdown.
            FinalizePostStartRewardSuppression(actor.gameObject, "character_start");
        }

        // NPC.Start is deliberately deferred until GO. Character.Start is therefore the final
        // pre-GO reward-hydration boundary; never make readiness depend on deferred NPC.Start.

        private static bool FinalizePostStartRewardSuppression(GameObject go, string source)
        {
            if (go == null || !TeamClones.Contains(go)) return false;
            int id = go.GetInstanceID();
            if (!NativeCharacterStartCompleted.Contains(id)) return false;
            Character actor = go.GetComponent<Character>();
            string reason = string.Empty;
            bool applied = SuppressBorrowedDeathRewards(actor);
            bool verified = applied && VerifyRewardSuppression(go, out reason);
            return verified;
        }

        private static bool ValidatePostStartRewardSuppression(GameObject go, out string reason)
        {
            return VerifyRewardSuppression(go, out reason);
        }

        // The one authoritative reward proof for preparation, countdown hold, GO, and post-Start
        // reassertion. The stored IDs prove this is the same current proxy surface; the boundary is
        // re-read so a real mutation still fails closed.
        private static bool VerifyRewardSuppression(GameObject go, out string reason)
        {
            reason = string.Empty;
            if (go == null || !TeamClones.Contains(go)) { reason = "unregistered_proxy"; return false; }
            int id = go.GetInstanceID(); Character actor = go.GetComponent<Character>(); LootTable loot = go.GetComponent<LootTable>();
            RewardSuppressionProof proof;
            bool has = RewardSuppressionProofs.TryGetValue(id, out proof) && proof != null;
            bool boundary = ValidateProxyRewardBoundary(go, out reason);
            bool actorMatches = has && actor != null && proof.ActorInstanceId == actor.GetInstanceID();
            bool lootMatches = has && proof.LootInstanceId == (loot == null ? 0 : loot.GetInstanceID());
            bool pass = PvpProxyStartupPolicy.RewardSuppressionProofPasses(boundary, has && proof.Verified, actorMatches, lootMatches);
            if (!pass && string.IsNullOrEmpty(reason)) reason = "reward_suppression_pending:current_proof_failed";
            return pass;
        }

        private static bool RecordAndVerifyRewardSuppression(GameObject go, string source, out string reason)
        {
            reason = string.Empty;
            if (go == null || !TeamClones.Contains(go)) { reason = "unregistered_proxy"; return false; }
            Character actor = go.GetComponent<Character>(); LootTable loot = go.GetComponent<LootTable>();
            bool boundary = ValidateProxyRewardBoundary(go, out reason);
            RewardSuppressionProofs[go.GetInstanceID()] = new RewardSuppressionProof
            {
                ActorInstanceId = actor == null ? 0 : actor.GetInstanceID(), LootInstanceId = loot == null ? 0 : loot.GetInstanceID(),
                Verified = boundary, Source = source ?? "unknown", Reason = boundary ? "ready" : (reason ?? "failed")
            };
            return VerifyRewardSuppression(go, out reason);
        }

        internal static bool ValidateProxyStartupInvariant(GameObject go, out string reason)
        {
            reason = string.Empty;
            if (go == null) { reason = "missing_root"; return false; }
            NPC npc = go.GetComponent<NPC>();
            Character actor = go.GetComponent<Character>();
            Stats stats = actor == null ? null : actor.MyStats;
            CastSpell caster = go.GetComponent<CastSpell>();
            NavMeshAgent nav = go.GetComponent<NavMeshAgent>();
            SimPlayer sim = go.GetComponent<SimPlayer>();
            bool actorLinksNpc = false, statsLinksActor = false;
            try { actorLinksNpc = actor != null && actor.MyNPC == npc; } catch { }
            try { statsLinksActor = stats != null && stats.Myself == actor; } catch { }
            bool persistent = false;
            try { persistent = (sim != null && (sim.enabled || sim.MySimTracking != null || sim.myIndex >= 0)) || (npc != null && (npc.SimPlayer || npc.ThisSim != null)); } catch { persistent = true; }
            string maintenanceReason;
            bool maintenance = ValidateNativeMaintenanceState(npc, actor, stats, caster, nav, out maintenanceReason);
            bool pass = PvpProxyStartupPolicy.InvariantPasses(TeamClones.Contains(go), npc != null, actor != null, stats != null,
                actorLinksNpc, statsLinksActor, caster != null, nav != null, persistent);
            pass = pass && maintenance;
            if (!pass)
            {
                reason = "registered=" + TeamClones.Contains(go) + ",npc=" + (npc != null) + ",character=" + (actor != null) +
                    ",stats=" + (stats != null) + ",actorNpc=" + actorLinksNpc + ",statsActor=" + statsLinksActor +
                    ",caster=" + (caster != null) + ",nav=" + (nav != null) + ",persistent=" + persistent +
                    ",maintenance=" + maintenanceReason;
            }
            return pass;
        }

        private static void ConfigureNativeMaintenanceState(NPC npc, Character actor, CastSpell caster, NavMeshAgent nav)
        {
            if (npc == null) return;
            try
            {
                // These are the Start-owned references that must still point at the converted proxy after
                // native Start completes. The same routine also establishes a safe pre-Start graph for
                // countdown validation.
                TrySetField(npc, "Myself", actor);
                TrySetField(npc, "MyStats", actor == null ? null : actor.MyStats);
                TrySetField(npc, "MySpells", caster);
                TrySetField(npc, "MyNav", nav);
                TrySetField(npc, "MyCharControl", npc.GetComponent<CharacterController>());
                TrySetField(npc, "MyRaidSlot", null);
                EnsureNamePlatePresentation(npc, actor);
                object currentFlash;
                if (!TryReadField(npc, "NameFlash", out currentFlash) || currentFlash == null)
                {
                    FlashUIColors flash = null;
                    object namePlateObject;
                    if (TryReadField(npc, "NamePlateObject", out namePlateObject) && namePlateObject is Component)
                        flash = ((Component)namePlateObject).GetComponent<FlashUIColors>();
                    object namePlate;
                    if (flash == null && TryReadField(npc, "NamePlate", out namePlate) && namePlate is Component)
                        flash = ((Component)namePlate).GetComponent<FlashUIColors>();
                    if (flash == null) flash = npc.GetComponentInChildren<FlashUIColors>(true);
                    TrySetField(npc, "NameFlash", flash);
                }
            }
            catch { }
        }

        // NPC.Update calls HandleNameTag() as its THIRD statement - before the NeverAggro early-out and
        // before every combat call - and HandleNameTag dereferences NPC.NamePlateTxt (TMPro.TextMeshPro)
        // via callvirt Behaviour.get_enabled() in every branch. NPC.Update has no exception handler, so a
        // null NamePlateTxt throws every frame and the whole combat/aggro/nav half of Update never runs.
        //
        // Verified against the installed Assembly-CSharp.dll: NamePlateTxt and NamePlateObject are written
        // by NPC.Start and by nothing else, and read by HandleNameTag (NamePlateObject also by Start).
        // The 0.5.5 repair originally reconstructed these because 0.5.4/0.5.5 bypassed native Start.
        // 0.5.9 preserves the proxy-owned nameplate invariant even though native Start is restored, so a
        // template/source nameplate is never shared across actors.
        //
        // Native Start's recipe (IL_0475-0682) is reproduced here in the same order:
        //   NamePlate       = Instantiate(GameData.GM.GetComponent<Misc>().NamePlate, pos, rot).transform
        //   NamePlateTxt    = NamePlate.GetComponent<TextMeshPro>()
        //   NamePlateObject = NamePlate.GetComponent<NamePlate>()
        //   NamePlateObject.MyStats / .Myself = this NPC's Stats / Character
        //   NamePlate text  = NPCName
        //   NamePlate.SetParent(transform)
        // Preference order is reuse-then-rebuild so a clone that already carries a valid nameplate keeps it.
        private static void EnsureNamePlatePresentation(NPC npc, Character actor)
        {
            if (npc == null) return;
            try
            {
                Transform plate = npc.NamePlate;

                // (a)/(b) Reuse the clone's OWN nameplate when it survived cloning. A component that is not
                // under this proxy's transform belongs to the source/template NPC: binding it would make two
                // NPCs share one nameplate and mutate the live original, so it is rejected here.
                if (plate != null && !IsUnderProxyRoot(npc, plate)) plate = null;
                if (plate == null)
                {
                    TextMeshPro owned = FindOwnNamePlateText(npc);
                    if (owned != null) plate = owned.transform;
                }

                // (c) The clone genuinely has no nameplate presentation. Build the native equivalent from the
                // same prefab NPC.Start uses, so HandleNameTag sees exactly the shape it expects.
                if (plate == null) plate = InstantiateNativeNamePlate(npc);
                if (plate == null) return; // validation below fails the proxy; preparation must not continue.

                TrySetField(npc, "NamePlate", plate);
                TextMeshPro text = plate.GetComponent<TextMeshPro>();
                if (text == null) text = plate.GetComponentInChildren<TextMeshPro>(true);
                TrySetField(npc, "NamePlateTxt", text);

                NamePlate plateComponent = plate.GetComponent<NamePlate>();
                if (plateComponent == null) plateComponent = plate.GetComponentInChildren<NamePlate>(true);
                if (plateComponent != null)
                {
                    TrySetField(npc, "NamePlateObject", plateComponent);
                    // Start binds the nameplate back to its owner's live stats/character. Without this the
                    // plate would report the template creature instead of the proxy.
                    TrySetField(plateComponent, "MyStats", actor == null ? null : actor.MyStats);
                    TrySetField(plateComponent, "Myself", actor);
                }

                if (text != null && !string.IsNullOrEmpty(npc.NPCName)) text = ApplyNamePlateText(text, npc.NPCName);
                if (plate.parent != npc.transform) plate.SetParent(npc.transform);
            }
            catch { }
        }

        // 0.5.15 combat-loop recovery. Current Assembly-CSharp gates BOTH native loop launches in
        // NPC.Start behind !NeverAggro. Since 0.5.13, PvP intentionally runs natural Start before GO
        // while NeverAggro=true, so a successful Start can legally complete with navDo/behDo null.
        // Natural Start still owns all normal initialization; PvP only starts a missing iterator after
        // Start completes, tracks exactly what it started, and keeps those loops inert until GO.
        private static bool EnsureNativeCombatLoops(NPC npc, string phase, out string detail)
        {
            detail = string.Empty;
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null)
            { detail = "not_temporary"; return false; }
            int id = npc.gameObject.GetInstanceID();
            if (!NativeStartCompleted.Contains(id))
            { detail = "native_start_pending"; return false; }

            try
            {
                object navHandle; bool navPresent = TryReadField(npc, "navDo", out navHandle) && navHandle != null;
                object behaviorHandle; bool behaviorPresent = TryReadField(npc, "behDo", out behaviorHandle) && behaviorHandle != null;

                int navStartsBefore = CounterValue(ModOwnedNavLoopStarts, id);
                if (PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, navPresent,
                    ModOwnedNavLoops.ContainsKey(id), navStartsBefore))
                {
                    System.Collections.IEnumerator navLoop = InvokeNativeNpcLoop(npc, "NavUpdate", 0.3f);
                    if (navLoop != null && TryStartOwnedNativeCombatLoop(npc, "navDo", navLoop,
                        ModOwnedNavLoops, ModOwnedNavLoopStarts, id))
                        navPresent = true;
                }

                int behaviorStartsBefore = CounterValue(ModOwnedBehaviorLoopStarts, id);
                if (PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, behaviorPresent,
                    ModOwnedBehaviorLoops.ContainsKey(id), behaviorStartsBefore))
                {
                    System.Collections.IEnumerator behaviorLoop = InvokeNativeNpcLoop(npc, "BehaviorUpdate", 0.1f);
                    if (behaviorLoop != null && TryStartOwnedNativeCombatLoop(npc, "behDo", behaviorLoop,
                        ModOwnedBehaviorLoops, ModOwnedBehaviorLoopStarts, id))
                        behaviorPresent = true;
                }

                navPresent = TryReadField(npc, "navDo", out navHandle) && navHandle != null;
                behaviorPresent = TryReadField(npc, "behDo", out behaviorHandle) && behaviorHandle != null;
                int navStarts = CounterValue(ModOwnedNavLoopStarts, id);
                int behaviorStarts = CounterValue(ModOwnedBehaviorLoopStarts, id);
                bool ready = PvpNativeCombatLoopPolicy.InfrastructureReady(true, navPresent, behaviorPresent, navStarts, behaviorStarts);

                bool preGo = !PvpCombatContainment.LethalFightActive;
                bool navHeld = true;
                if (preGo)
                {
                    npc.NeverAggro = true;
                    npc.CurrentAggroTarget = null;
                    NormalizeBorrowedPreGoAiState(npc);
                    NavMeshAgent nav = npc.GetComponent<NavMeshAgent>();
                    string holdReason;
                    navHeld = nav != null && PvpPreparationNavRuntime.TryNormalizeHeldAgent(nav, out holdReason);
                }

                string navOwner = ModOwnedNavLoops.ContainsKey(id) ? "pvp" : (navPresent ? "native" : "none");
                string behaviorOwner = ModOwnedBehaviorLoops.ContainsKey(id) ? "pvp" : (behaviorPresent ? "native" : "none");
                detail = "nav=" + navPresent + "/" + navOwner + "/starts=" + navStarts +
                    ",behavior=" + behaviorPresent + "/" + behaviorOwner + "/starts=" + behaviorStarts +
                    ",preGoHeld=" + (!preGo || navHeld);
                if (CombatLoopRecoveryLogged.Add(id))
                    PvpDiagnostics.Log("combat_loop_recovery proxy=" + SafeProxyName(npc) + "; phase=" + (phase ?? "unknown") +
                        "; " + detail + "; neverAggro=" + npc.NeverAggro + "; target_cleared=" + (npc.CurrentAggroTarget == null));

                NativeNavProbe probe;
                if (navPresent && NativeNavProbes.TryGetValue(id, out probe)) probe.CoroutineObserved = true;
                return ready && (!preGo || PvpNativeCombatLoopPolicy.PreGoBoundaryReady(
                    npc.NeverAggro, npc.CurrentAggroTarget == null, navHeld, true, true));
            }
            catch (Exception ex)
            {
                detail = "loop_recovery_threw:" + ex.GetType().Name;
                PvpDiagnostics.Log("combat_loop_recovery proxy=" + SafeProxyName(npc) + "; phase=" + (phase ?? "unknown") +
                    "; result=failed; error=" + ex.GetType().Name);
                return false;
            }
        }

        private static bool TryStartOwnedNativeCombatLoop(NPC npc, string fieldName,
            System.Collections.IEnumerator loop,
            Dictionary<int, System.Collections.IEnumerator> ownedLoops,
            Dictionary<int, int> ownedStartCounts, int id)
        {
            if (npc == null || loop == null || ownedLoops == null || ownedStartCounts == null || id == 0) return false;
            if (ownedLoops.ContainsKey(id) || CounterValue(ownedStartCounts, id) != 0) return false;

            object existing;
            if (TryReadField(npc, fieldName, out existing) && existing != null) return false;

            bool fieldAssigned = false;
            try
            {
                TrySetField(npc, fieldName, loop);
                object assigned;
                fieldAssigned = TryReadField(npc, fieldName, out assigned) && object.ReferenceEquals(assigned, loop);
                if (!fieldAssigned) return false;

                npc.StartCoroutine(loop);
                ownedLoops[id] = loop;
                Increment(ownedStartCounts, id);
                return true;
            }
            catch
            {
                if (fieldAssigned)
                {
                    object current;
                    if (TryReadField(npc, fieldName, out current) && object.ReferenceEquals(current, loop))
                        TrySetField(npc, fieldName, null);
                }
                ownedLoops.Remove(id);
                return false;
            }
        }

        private static System.Collections.IEnumerator InvokeNativeNpcLoop(NPC npc, string methodName, float interval)
        {
            try
            {
                MethodInfo method = AccessTools.Method(typeof(NPC), methodName, new Type[] { typeof(float) });
                if (method == null) return null;
                return method.Invoke(npc, new object[] { interval }) as System.Collections.IEnumerator;
            }
            catch { return null; }
        }

        private static void NormalizeBorrowedPreGoAiState(NPC npc)
        {
            if (npc == null) return;
            // These are transient native fields verified in the current Assembly-CSharp loop bodies.
            // A live mob template can carry an in-progress leash/off-nav/environment state into the clone;
            // none belongs to the disposable PvP identity during preparation.
            TrySetField(npc, "Leashing", false);
            TrySetField(npc, "offNav", false);
            TrySetField(npc, "TakingEnvironmentalDamage", false);
        }

        internal static bool AllowNativeBehaviorStep(NPC npc)
        {
            if (!IsTemporaryNpc(npc)) return true;
            if (PvpCombatContainment.LethalFightActive) return true;
            int id = npc == null || npc.gameObject == null ? 0 : npc.gameObject.GetInstanceID();
            if (id != 0 && PreGoBehaviorBlockedLogged.Add(id))
                PvpDiagnostics.Log("pre_go_behavior_hold proxy=" + SafeProxyName(npc) + "; loop_alive=true; decision_body=blocked");
            return false;
        }

        internal static bool AllowNativeNavStep(NPC npc)
        {
            if (!IsTemporaryNpc(npc)) return true;
            if (PvpCombatContainment.LethalFightActive) return true;
            int id = npc == null || npc.gameObject == null ? 0 : npc.gameObject.GetInstanceID();
            if (id != 0 && PreGoNavBlockedLogged.Add(id))
                PvpDiagnostics.Log("pre_go_nav_hold proxy=" + SafeProxyName(npc) + "; loop_alive=true; updateNav=blocked");
            return false;
        }

        internal static void ObserveNonRaidBehavior(NPC npc)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc == null || npc.gameObject == null) return;
            int id = npc.gameObject.GetInstanceID();
            if (NonRaidBehaviorReached.Add(id))
            {
                PvpDiagnostics.Log("behavior_section_reached proxy=" + SafeProxyName(npc) + "; " + CombatGateSnapshot(npc));
                ObserveFirstActionLatency(id, "first_behavior_section");
            }
        }

        private static void StopOwnedNativeCombatLoops(NPC npc)
        {
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return;
            int id = npc.gameObject.GetInstanceID();
            System.Collections.IEnumerator owned;
            if (ModOwnedNavLoops.TryGetValue(id, out owned) && PvpNativeCombatLoopPolicy.CanStopOwnedLoop(true, true))
            {
                try { npc.StopCoroutine(owned); } catch { }
                object current; if (TryReadField(npc, "navDo", out current) && object.ReferenceEquals(current, owned)) TrySetField(npc, "navDo", null);
                ModOwnedNavLoops.Remove(id);
            }
            if (ModOwnedBehaviorLoops.TryGetValue(id, out owned) && PvpNativeCombatLoopPolicy.CanStopOwnedLoop(true, true))
            {
                try { npc.StopCoroutine(owned); } catch { }
                object current; if (TryReadField(npc, "behDo", out current) && object.ReferenceEquals(current, owned)) TrySetField(npc, "behDo", null);
                ModOwnedBehaviorLoops.Remove(id);
            }
        }

        // The Start probe deliberately clears copied iterator fields. A live template's IEnumerator is
        // never proof of a scheduled coroutine on the clone; missing loops are recovered only after the
        // clone's own natural Start has completed.
        internal static void PrepareNativeStartProbe(NPC npc)
        {
            // Fresh probe clears only this proxy: NativeCharacterStartCompleted.Remove(id); RewardSuppressionProofs.Remove(id);
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return;
            int id = npc.gameObject.GetInstanceID();
            if (NativeStartCompleted.Contains(id))
            {
                string existingDetail;
                EnsureNativeCombatLoops(npc, "prepare_reentry", out existingDetail);
                return;
            }
            StopOwnedNativeCombatLoops(npc);
            ModOwnedNavLoopStarts.Remove(id);
            ModOwnedBehaviorLoopStarts.Remove(id);
            CombatLoopRecoveryLogged.Remove(id);
            PreGoBehaviorBlockedLogged.Remove(id);
            PreGoNavBlockedLogged.Remove(id);
            NativeStartCompleted.Remove(id);
            NativeNavProbe probe = new NativeNavProbe();
            try
            {
                probe.StartPosition = npc.transform.position;
                probe.LastPosition = probe.StartPosition;
            }
            catch { }
            NativeNavProbes[id] = probe;
            // A cloned component may carry IEnumerator fields copied from the already-started source.
            // They are not running on this MonoBehaviour and must never be mistaken for native health.
            TrySetField(npc, "navDo", null);
            TrySetField(npc, "behDo", null);
        }

        internal static void ObserveNativeNavEntered(NPC npc)
        {
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return;
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(npc.gameObject.GetInstanceID(), out probe)) return;
            probe.UpdateNavReached = true;
            object navDo;
            if (TryReadField(npc, "navDo", out navDo) && navDo != null) probe.CoroutineObserved = true;
        }

        internal static void ObserveNativeNavCompleted(NPC npc)
        {
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return;
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(npc.gameObject.GetInstanceID(), out probe)) return;
            probe.UpdateNavReached = true;
            probe.FirstUpdateNavCompleted = true;
            object navDo;
            if (TryReadField(npc, "navDo", out navDo) && navDo != null) probe.CoroutineObserved = true;
        }

        internal static Exception ObserveNativeNavException(NPC npc, Exception exception)
        {
            if (exception == null || npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return exception;
            int id = npc.gameObject.GetInstanceID();
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(id, out probe))
            {
                probe = new NativeNavProbe();
                NativeNavProbes[id] = probe;
            }
            bool first = !probe.Faulted;
            probe.Faulted = true;
            probe.FaultType = exception.GetType().Name;
            if (first)
                PvpDiagnostics.Log("proxy_nav_faulted proxy=" + SafeProxyName(npc) +
                    "; fault=" + probe.FaultType + "; updateNavReached=" + probe.UpdateNavReached +
                    "; firstUpdateNavCompleted=" + probe.FirstUpdateNavCompleted);
            return exception; // diagnostics only; never hide a native navigation failure.
        }

        internal static bool NativeNavHealthy(NPC npc)
        {
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) return false;
            int id = npc.gameObject.GetInstanceID();
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(id, out probe)) return false;
            return PvpNativeNavHealthPolicy.IsHealthy(NativeStartCompleted.Contains(id),
                probe.CoroutineObserved, probe.UpdateNavReached, probe.FirstUpdateNavCompleted, probe.Faulted);
        }

        internal static bool CompleteNativeNavFailure(IList<NPC> npcs)
        {
            if (npcs == null || npcs.Count == 0) return false;
            int faulted = 0, relevant = 0;
            for (int i = 0; i < npcs.Count; i++)
            {
                NPC npc = npcs[i];
                if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null) continue;
                relevant++;
                NativeNavProbe probe;
                if (NativeNavProbes.TryGetValue(npc.gameObject.GetInstanceID(), out probe) && probe.Faulted) faulted++;
            }
            return PvpNativeNavHealthPolicy.CompleteNavFailure(relevant, faulted);
        }

        internal static string NativeNavHealthSummary()
        {
            int starts = 0, coroutine = 0, entered = 0, completed = 0, destination = 0, movement = 0, faulted = 0;
            int agentPresent = 0, agentEnabled = 0, onMesh = 0;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue; int id = go.GetInstanceID();
                if (NativeStartCompleted.Contains(id)) starts++;
                NativeNavProbe probe;
                if (NativeNavProbes.TryGetValue(id, out probe))
                {
                    if (probe.CoroutineObserved) coroutine++;
                    if (probe.UpdateNavReached) entered++;
                    if (probe.FirstUpdateNavCompleted) completed++;
                    if (probe.DestinationAttempted) destination++;
                    if (probe.MovementObserved) movement++;
                    if (probe.Faulted) faulted++;
                }
                try
                {
                    NavMeshAgent nav = go.GetComponent<NavMeshAgent>();
                    if (nav != null)
                    {
                        agentPresent++;
                        if (nav.enabled) agentEnabled++;
                        if (nav.enabled && nav.isOnNavMesh) onMesh++;
                    }
                }
                catch { }
            }
            return "nav_start_completed=" + starts + "/" + TeamClones.Count +
                "; nav_coroutine_observed=" + coroutine + "; updateNav_reached=" + entered +
                "; nav_first_step_completed=" + completed + "; nav_destination=" + destination +
                "; nav_movement=" + movement + "; nav_faulted=" + faulted +
                "; nav_agents=" + agentPresent + "; nav_enabled=" + agentEnabled + "; nav_on_mesh=" + onMesh;
        }

        private static void TickNativeNavHealth()
        {
            if (!PvpCombatContainment.LethalFightActive) return;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue;
                NativeNavProbe probe; int id = go.GetInstanceID();
                if (!NativeNavProbes.TryGetValue(id, out probe)) continue;
                try
                {
                    NPC npc = go.GetComponent<NPC>();
                    NavMeshAgent nav = go.GetComponent<NavMeshAgent>();
                    object navDo;
                    if (npc != null && TryReadField(npc, "navDo", out navDo) && navDo != null) probe.CoroutineObserved = true;
                    if (nav != null && nav.enabled && nav.isOnNavMesh && nav.hasPath) probe.DestinationAttempted = true;
                    Vector3 position = go.transform.position;
                    if (!probe.MovementObserved && (position - probe.StartPosition).sqrMagnitude > 0.04f)
                    {
                        probe.MovementObserved = true;
                        ObserveFirstActionLatency(id, "first_movement");
                    }
                    probe.LastPosition = position;
                    if (probe.PursuitSamplePending && !probe.PursuitResultLogged && Time.unscaledTime >= probe.PursuitSampleAt)
                    {
                        Transform shell = null; VisualShellRoots.TryGetValue(id, out shell);
                        Vector3 shellPosition = shell == null ? position : shell.position;
                        Vector3 shellLocal = shell == null ? Vector3.zero : shell.localPosition;
                        float rootDisplacement = Vector3.Distance(probe.PursuitStartPosition, position);
                        float shellDisplacement = shell == null ? -1f : Vector3.Distance(probe.ShellStartPosition, shellPosition);
                        float localOffsetDelta = shell == null ? -1f : Vector3.Distance(probe.ShellStartLocalPosition, shellLocal);
                        bool agentEnabled = nav != null && nav.enabled;
                        bool liveOnMesh = agentEnabled && nav.isOnNavMesh;
                        bool stopped = liveOnMesh && nav.isStopped;
                        bool hasPath = liveOnMesh && nav.hasPath;
                        float velocity = liveOnMesh ? nav.velocity.magnitude : 0f;
                        bool pathComplete = hasPath && nav.pathStatus == NavMeshPathStatus.PathComplete;
                        string pathStatus = hasPath ? nav.pathStatus.ToString() : "unavailable";
                        float remaining = liveOnMesh ? nav.remainingDistance : -1f;
                        bool pursuit = PvpCombatExecutionPolicy.PursuitProven(probe.PursuitNeedsMovement, nav != null,
                            agentEnabled, liveOnMesh, stopped, hasPath,
                            pathComplete, rootDisplacement, velocity);
                        bool shellFollow = shell != null && PvpCombatExecutionPolicy.ShellFollowProven(rootDisplacement,
                            Math.Max(0f, shellDisplacement), Math.Max(0f, localOffsetDelta));
                        Character currentTarget = npc == null ? null : npc.CurrentAggroTarget;
                        bool targetStable = currentTarget != null && currentTarget.gameObject != null &&
                            currentTarget.gameObject.GetInstanceID() == probe.PursuitTargetId;
                        string assessment = PvpCombatExecutionPolicy.PursuitAssessment(probe.PursuitNeedsMovement,
                            nav != null, agentEnabled, liveOnMesh, stopped, hasPath,
                            pathComplete, rootDisplacement, velocity);
                        Character actor = npc == null ? null : npc.GetComponent<Character>();
                        int animatorState = AnimatorStateHash(actor);
                        PvpDiagnostics.Log("pursuit_probe_result proxy=" + (npc == null ? go.name : SafeProxyName(npc)) +
                            "; assessment=" + assessment + "; root_displacement=" + rootDisplacement.ToString("0.00") +
                            "; shell_displacement=" + shellDisplacement.ToString("0.00") + "; shell_local_delta=" + localOffsetDelta.ToString("0.00") +
                            "; shell_follow=" + shellFollow + "; velocity=" + velocity.ToString("0.00") +
                            "; isStopped=" + stopped + "; hasPath=" + hasPath +
                            "; pathStatus=" + pathStatus + "; remaining=" + remaining.ToString("0.00") +
                            "; target_stable=" + targetStable + "; animator_state=" + animatorState +
                            "; pursuit_proven=" + pursuit);
                        probe.PursuitResultLogged = true;
                        probe.PursuitSamplePending = false;
                    }
                }
                catch { }
            }
        }

        private static TextMeshPro ApplyNamePlateText(TextMeshPro text, string value)
        {
            try { text.text = value; } catch { }
            return text;
        }

        private static bool IsUnderProxyRoot(NPC npc, Transform candidate)
        {
            if (npc == null || candidate == null) return false;
            Transform root = npc.transform;
            for (Transform t = candidate; t != null; t = t.parent) if (t == root) return true;
            return false;
        }

        // Only ever returns a TextMeshPro that lives inside this proxy's own hierarchy.
        private static TextMeshPro FindOwnNamePlateText(NPC npc)
        {
            if (npc == null) return null;
            TextMeshPro[] candidates = npc.GetComponentsInChildren<TextMeshPro>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                TextMeshPro candidate = candidates[i];
                if (candidate != null && IsUnderProxyRoot(npc, candidate.transform)) return candidate;
            }
            return null;
        }

        private static Transform InstantiateNativeNamePlate(NPC npc)
        {
            try
            {
                if (GameData.GM == null) return null;
                Misc misc = GameData.GM.GetComponent<Misc>();
                if (misc == null || misc.NamePlate == null) return null;
                GameObject spawned = UnityEngine.Object.Instantiate(misc.NamePlate,
                    npc.transform.position, npc.transform.rotation);
                if (spawned == null) return null;
                spawned.name = "PvP_NamePlate_" + (string.IsNullOrEmpty(npc.NPCName) ? "proxy" : npc.NPCName);
                return spawned.transform;
            }
            catch { return null; }
        }

        private static bool ValidateNativeMaintenanceState(NPC npc, Character actor, Stats stats,
            CastSpell caster, NavMeshAgent nav, out string reason)
        {
            bool self = false, boundStats = false, boundNav = false, boundCaster = false, flash = false, raidClear = false;
            bool plateText = false, plateObject = false;
            try
            {
                object value;
                self = npc != null && TryReadField(npc, "Myself", out value) && object.ReferenceEquals(value, actor);
                boundStats = npc != null && TryReadField(npc, "MyStats", out value) && object.ReferenceEquals(value, stats);
                boundNav = npc != null && TryReadField(npc, "MyNav", out value) && object.ReferenceEquals(value, nav);
                boundCaster = npc != null && TryReadField(npc, "MySpells", out value) && object.ReferenceEquals(value, caster);
                flash = npc != null && TryReadField(npc, "NameFlash", out value) && value != null;
                raidClear = npc != null && TryReadField(npc, "MyRaidSlot", out value) && value == null;
                // The exact two references NPC.HandleNameTag dereferences on its first Update. Compared
                // against Unity's null (not just a C# reference check) so a destroyed nameplate also fails.
                plateText = npc != null && TryReadField(npc, "NamePlateTxt", out value) &&
                            value is TextMeshPro && (TextMeshPro)value != null;
                plateObject = npc != null && TryReadField(npc, "NamePlateObject", out value) &&
                              value is NamePlate && (NamePlate)value != null;
            }
            catch { }
            bool pass = PvpProxyStartupPolicy.MaintenanceStatePasses(npc != null && TeamClones.Contains(npc.gameObject),
                self, boundStats, boundNav, boundCaster, flash, raidClear, plateText, plateObject);
            reason = pass ? "pass" : "self=" + self + ",stats=" + boundStats + ",nav=" + boundNav +
                ",caster=" + boundCaster + ",nameFlash=" + flash + ",raidSlotClear=" + raidClear +
                ",namePlateTxt=" + plateText + ",namePlateObject=" + plateObject;
            return pass;
        }

        internal static bool AllowNativeMaintenance(NPC npc)
        {
            if (!IsTemporaryNpc(npc)) return true;
            Character actor = npc == null ? null : npc.GetComponent<Character>();
            Stats stats = actor == null ? null : actor.MyStats;
            CastSpell caster = npc == null ? null : npc.GetComponent<CastSpell>();
            NavMeshAgent nav = npc == null ? null : npc.GetComponent<NavMeshAgent>();
            string reason;
            bool valid = ValidateNativeMaintenanceState(npc, actor, stats, caster, nav, out reason);
            if (!PvpProxyStartupPolicy.ShouldInterceptMaintenance(true, valid)) return true;
            if (!_runtimeInvalidCleanupQueued)
            {
                _runtimeInvalidCleanupQueued = true;
                _runtimeInvalidReason = reason;
                PvpDiagnostics.Log("proxy_runtime_invalid proxy=" + SafeProxyName(npc) + "; missing=" + reason +
                    "; action=cancel_and_cleanup; template=" + _templateSource);
                try { PvpCombatContainment.End("runtime_invalid"); } catch { }
            }
            return false;
        }

        internal static bool AllowNativeNpcStart(NPC npc, out bool temporaryNativeStartExpected)
        {
            temporaryNativeStartExpected = false;
            if (!IsTemporaryNpc(npc)) return true;

            string reason;
            bool ready = ValidateProxyStartupInvariant(npc == null ? null : npc.gameObject, out reason);
            if (!ready)
            {
                if (!_runtimeInvalidCleanupQueued)
                {
                    _runtimeInvalidCleanupQueued = true;
                    _runtimeInvalidReason = "pre_start/" + reason;
                }
                PvpDiagnostics.Log("proxy_native_start action=blocked_invalid; proxy=" + SafeProxyName(npc) +
                    "; pre_start_invariant=fail:" + reason + "; template=" + _templateSource);
                return false;
            }

            bool runNative = PvpProxyStartupPolicy.ShouldRunNativeNpcStart(true, true);
            temporaryNativeStartExpected = runNative;
            PvpDiagnostics.Log("proxy_native_start action=native_pending; proxy=" + SafeProxyName(npc) +
                "; pre_start_invariant=pass; template=" + _templateSource);
            return runNative;
        }

        internal static void ObserveNativeNpcStartCompleted(NPC npc, bool temporaryNativeStartExpected)
        {
            if (!temporaryNativeStartExpected || !IsTemporaryNpc(npc) || npc.gameObject == null) return;
            int id = npc.gameObject.GetInstanceID();
            NativeStartCompleted.Add(id);
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(id, out probe))
            {
                probe = new NativeNavProbe();
                try { probe.StartPosition = probe.LastPosition = npc.transform.position; } catch { }
                NativeNavProbes[id] = probe;
            }
            object navDo;
            if (TryReadField(npc, "navDo", out navDo) && navDo != null) probe.CoroutineObserved = true;

            Character actor = npc.GetComponent<Character>();
            CastSpell caster = npc.GetComponent<CastSpell>();
            NavMeshAgent nav = npc.GetComponent<NavMeshAgent>();
            int teamIndex = TeamClones.IndexOf(npc.gameObject);
            PvpOpponentProfile profile = teamIndex >= 0 && teamIndex < TeamProfiles.Count ? TeamProfiles[teamIndex] : null;
            try
            {
                // Native Start is authoritative for the lifecycle graph. Immediately reassert only
                // PvP-owned identity/reward and profile weapon-mode constraints that Start may derive
                // from the borrowed body. DO NOT rebuild spell lists here: native Start has just derived
                // MemmedHealSpells and other runtime state from the pre-Start profile lists.
                npc.ThisSim = null;
                npc.SimPlayer = false;
                npc.InGroup = false;
                npc.NeverAggro = !PvpCombatContainment.LethalFightActive;
                npc.CurrentAggroTarget = null;
                npc.GuildName = profile == null ? string.Empty : profile.GuildId;
                ConfigureProfileWeaponRuntime(npc, profile, true);
                ConfigureNativeMaintenanceState(npc, actor, caster, nav);
                ReassertVisualAnimatorBinding(npc, actor);
                SuppressBorrowedDeathRewards(actor);
                LogPostStartAbilityRuntime(npc, profile);
                float startupSpellCooldown; string startupSpellReason;
                bool startupSpellReady = PvpNativeSpellExecutionBridge.TryClearSyntheticStartupCooldown(npc, true,
                    out startupSpellCooldown, out startupSpellReason);
                PvpDiagnostics.Log("proxy_spell_startup_gate proxy=" + SafeProxyName(npc) +
                    "; before=" + startupSpellCooldown.ToString("0.00") + "; result=" + startupSpellReason +
                    "; surface=" + (startupSpellReady ? "ready" : "unavailable"));
                if (startupSpellReady) StartupSpellCooldownNormalized.Add(id);
            }
            catch { }

            string loopDetail;
            bool loopsReady = EnsureNativeCombatLoops(npc, "native_start_completed", out loopDetail);
            string invariant, reward;
            bool runtimeValid = ValidateProxyStartupInvariant(npc.gameObject, out invariant);
            bool rewardValid = VerifyRewardSuppression(npc.gameObject, out reward);
            PvpDiagnostics.Log("proxy_native_start action=native; completion=completed; proxy=" + SafeProxyName(npc) +
                "; runtime=" + (runtimeValid ? "pass" : "fail:" + invariant) +
                "; reward=" + (rewardValid ? "pass" : "fail:" + reward) +
                "; combat_loops=" + (loopsReady ? "ready" : "fail:" + loopDetail) +
                "; nav_coroutine_observed=" + probe.CoroutineObserved + "; template=" + _templateSource);
            if (!runtimeValid || !rewardValid || !loopsReady)
            {
                _runtimeInvalidCleanupQueued = true;
                _runtimeInvalidReason = !runtimeValid ? invariant : (!rewardValid ? reward : "combat_loops/" + loopDetail);
                return;
            }
            if (PvpCombatContainment.LethalFightActive) PvpCombatContainment.ObserveProxyNativeStartCompleted(npc);
            else PvpDiagnostics.Log("proxy_native_start_completed_pre_go proxy=" + SafeProxyName(npc) + "; held=true");
        }

        // A native Start fault is per-proxy evidence, not proof that the encounter is unsound.
        //
        // Verified against the installed Assembly-CSharp, NPC.Start's FINAL branch is:
        //   IL_0b43  if (MyStats.CharacterClass == GameData.ClassDB.Stormcaller)
        //   IL_0b5a      foreach (SimPlayerSkillSlot slot in ThisSim.Skillbook)   <- no null guard
        //   IL_0b91          if (slot.skill.Id == "58018670") myImbued = slot;
        //   IL_0bb1  ret
        // A PvP proxy deliberately carries no persistent Sim identity (ThisSim is nulled at spawn so
        // the clone can never touch SimPlayerMngr's roster), so a Stormcaller-class proxy throws
        // there every time. That is the very last statement of Start: MyNav/MyStats/nameplate/skills/
        // spells are already established. On the CURRENT pre-GO path NeverAggro=true intentionally
        // skipped both native loop launches earlier in Start, so recovery must establish those missing
        // loops separately; the only actual Start-tail casualty is the optional myImbued slot.
        //
        // So do not assume, and do not tear down five attackers because one faulted. Reassert the
        // PvP-owned state the postfix would have applied and validate the result. A proxy that still
        // proves out keeps fighting; one that does not is dropped alone.
        internal static void ObserveNativeNpcStartFailed(NPC npc, Exception exception)
        {
            if (npc == null || !IsTemporaryNpc(npc) || npc.gameObject == null || exception == null) return;
            int id = npc.gameObject.GetInstanceID();
            NativeNavProbe probe;
            if (!NativeNavProbes.TryGetValue(id, out probe))
            {
                probe = new NativeNavProbe();
                NativeNavProbes[id] = probe;
            }
            probe.Faulted = true;
            probe.FaultType = "NPC.Start/" + exception.GetType().Name;

            string recoveryDetail;
            if (TryRecoverFaultedProxyStart(npc, out recoveryDetail))
            {
                string recoveredFault = probe.FaultType;
                // A successfully recovered Start-tail fault is not a NavUpdate fault. Leaving the
                // shared probe fault bit set would make the 0.5.14 structural nav readiness gate fail
                // forever even though the nav loop has now been recovered and is held on-mesh.
                probe.Faulted = false;
                probe.FaultType = string.Empty;
                PvpDiagnostics.Log("proxy_start_recovered proxy=" + SafeProxyName(npc) +
                    "; fault=" + recoveredFault + "; nav_fault=false; " + recoveryDetail);
                return;
            }

            int remaining = PvpCombatContainment.RemoveAttacker(npc);
            PvpDiagnostics.Log("proxy_start_dropped proxy=" + SafeProxyName(npc) +
                "; fault=" + probe.FaultType + "; reason=" + recoveryDetail + "; attackers_left=" + remaining);
            RetireProxy(npc);
            if (remaining <= 0)
            {
                _runtimeInvalidCleanupQueued = true;
                _runtimeInvalidReason = "all_proxies_failed_start/" + probe.FaultType;
            }
        }

        // The recovery path reasserts exactly what the Start postfix reasserts, then validates the
        // same runtime/reward graph and recovers only missing native loops. Since pre-GO Start now
        // intentionally runs with NeverAggro=true, navDo/behDo absence is expected on the current
        // assembly and is no longer evidence that Start faulted too early.
        private static bool TryRecoverFaultedProxyStart(NPC npc, out string detail)
        {
            detail = string.Empty;
            try
            {
                Character actor = npc.GetComponent<Character>();
                CastSpell caster = npc.GetComponent<CastSpell>();
                NavMeshAgent nav = npc.GetComponent<NavMeshAgent>();
                int teamIndex = TeamClones.IndexOf(npc.gameObject);
                PvpOpponentProfile profile = teamIndex >= 0 && teamIndex < TeamProfiles.Count ? TeamProfiles[teamIndex] : null;

                npc.ThisSim = null;
                npc.SimPlayer = false;
                npc.InGroup = false;
                npc.NeverAggro = !PvpCombatContainment.LethalFightActive;
                npc.CurrentAggroTarget = null;
                npc.GuildName = profile == null ? string.Empty : profile.GuildId;
                ConfigureProfileWeaponRuntime(npc, profile, true);
                ConfigureNativeMaintenanceState(npc, actor, caster, nav);
                ReassertVisualAnimatorBinding(npc, actor);
                SuppressBorrowedDeathRewards(actor);
                LogPostStartAbilityRuntime(npc, profile);
                float startupSpellCooldown; string startupSpellReason;
                bool startupSpellReady = PvpNativeSpellExecutionBridge.TryClearSyntheticStartupCooldown(npc, true,
                    out startupSpellCooldown, out startupSpellReason);
                PvpDiagnostics.Log("proxy_spell_startup_gate proxy=" + SafeProxyName(npc) +
                    "; before=" + startupSpellCooldown.ToString("0.00") + "; result=" + startupSpellReason +
                    "; surface=" + (startupSpellReady ? "ready" : "unavailable") + "; path=start_recovery");
                if (startupSpellReady) StartupSpellCooldownNormalized.Add(npc.gameObject.GetInstanceID());

                string invariant, reward;
                if (!ValidateProxyStartupInvariant(npc.gameObject, out invariant)) { detail = "runtime:" + invariant; return false; }
                if (!VerifyRewardSuppression(npc.gameObject, out reward)) { detail = "reward:" + reward; return false; }

                int id = npc.gameObject.GetInstanceID();
                NativeStartCompleted.Add(id);
                string loopDetail;
                if (!EnsureNativeCombatLoops(npc, "native_start_recovery", out loopDetail))
                {
                    NativeStartCompleted.Remove(id);
                    detail = "combat_loops:" + loopDetail;
                    return false;
                }
                detail = "runtime=pass; reward=pass; combat_loops=" + loopDetail;
                if (PvpCombatContainment.LethalFightActive) PvpCombatContainment.ObserveProxyNativeStartCompleted(npc);
                else PvpDiagnostics.Log("proxy_native_start_completed_pre_go proxy=" + SafeProxyName(npc) + "; held=true; path=recovery");
                return true;
            }
            catch (Exception ex)
            {
                detail = "recovery_threw:" + ex.GetType().Name;
                return false;
            }
        }

        // Take a single unusable proxy out of play without disturbing the rest of the encounter.
        private static void RetireProxy(NPC npc)
        {
            try
            {
                GameObject go = npc.gameObject;
                int id = go.GetInstanceID();
                npc.NeverAggro = true;
                StopOwnedNativeCombatLoops(npc);
                ModOwnedNavLoopStarts.Remove(id); ModOwnedBehaviorLoopStarts.Remove(id);
                CombatLoopRecoveryLogged.Remove(id); CombatBranchTraceLogged.Remove(id); NonRaidBehaviorReached.Remove(id);
                PreGoBehaviorBlockedLogged.Remove(id); PreGoNavBlockedLogged.Remove(id);
                npc.enabled = false;
                Character actor = go.GetComponent<Character>();
                if (actor != null) actor.enabled = false;
                CastSpell caster = go.GetComponent<CastSpell>();
                if (caster != null) caster.enabled = false;
                NavMeshAgent nav = go.GetComponent<NavMeshAgent>();
                if (nav != null) nav.enabled = false;
                int index = TeamClones.IndexOf(go);
                if (index >= 0)
                {
                    TeamClones.RemoveAt(index);
                    if (index < TeamProfiles.Count) TeamProfiles.RemoveAt(index);
                }
                if (_clone == go) _clone = TeamClones.Count > 0 ? TeamClones[0] : null;
                UnityEngine.Object.Destroy(go);
            }
            catch { }
        }

        internal static bool ValidateProxyRewardBoundary(GameObject go, out string reason)
        {
            reason = string.Empty;
            Character actor = go == null ? null : go.GetComponent<Character>();
            if (actor == null) { reason = "missing_character"; return false; }
            int xp;
            bool xpReadable = TryReadCharacterXp(actor, out xp);
            bool xpReadableAndZero = xpReadable && xp == 0;
            bool neverTrack = false, questNull = false, factionEmpty = false;
            try
            {
                neverTrack = actor.NeverTrack;
                questNull = actor.QuestCompleteOnDeath == null;
                factionEmpty = actor.factionMods == null || actor.factionMods.Length == 0;
            }
            catch { }
            LootTable loot = go.GetComponent<LootTable>();
            bool lootGoldZero = loot == null || (loot.MinGold == 0 && loot.MaxGold == 0 && loot.MyGold == 0);
            bool lootDropsEmpty = BorrowedLootCollectionsEmpty(loot);
            bool lootDisabled = loot == null || !loot.enabled;
            bool achievementCreditClear = true;
            NPC rewardNpc = go.GetComponent<NPC>();
            object achievement;
            achievementCreditClear = rewardNpc != null && TryReadField(rewardNpc, "SetAchievementOnDefeat", out achievement) &&
                (achievement == null || string.IsNullOrEmpty(achievement as string));
            bool pass = PvpProxyStartupPolicy.RewardBoundaryPasses(xpReadableAndZero, neverTrack,
                questNull, factionEmpty, lootGoldZero, lootDropsEmpty, lootDisabled, achievementCreditClear);
            if (!pass)
                reason = "xp=" + (xpReadable ? xp.ToString() : "unreadable") + ",neverTrack=" + neverTrack +
                    ",questNull=" + questNull + ",factionEmpty=" + factionEmpty +
                    ",lootGoldZero=" + lootGoldZero + ",lootDisabled=" + lootDisabled;
            return pass;
        }

        private static void LogRewardStateSnapshot(GameObject go, string phase, bool result)
        {
            if (go == null || string.IsNullOrEmpty(phase)) return;
            int proxyId = go.GetInstanceID();
            string key = phase + ":" + proxyId;
            if (!RewardStateSnapshotLogged.Add(key)) return;
            Character actor = go.GetComponent<Character>();
            LootTable loot = go.GetComponent<LootTable>();
            int xp = 0;
            bool xpReadable = TryReadCharacterXp(actor, out xp);
            bool questNull = actor != null && actor.QuestCompleteOnDeath == null;
            bool factionEmpty = actor != null && (actor.factionMods == null || actor.factionMods.Length == 0);
            string bossXp = actor == null ? "unavailable" : actor.BossXp.ToString();
            string bonusXp = actor == null ? "unavailable" : actor.BonusRangeXP.ToString();
            string lootGold = loot == null ? "none" : loot.MinGold + "/" + loot.MaxGold + "/" + loot.MyGold;
            bool lootDisabled = loot == null || !loot.enabled;
            PvpDiagnostics.Log("reward_state_snapshot phase=" + phase + "; proxyId=" + proxyId +
                "; actorId=" + (actor == null ? 0 : actor.GetInstanceID()) + "; lootId=" + (loot == null ? 0 : loot.GetInstanceID()) +
                "; xpReadable=" + xpReadable + "; xpValue=" + (xpReadable ? xp.ToString() : "unreadable") +
                "; bossXp=" + bossXp + "; bonusXp=" + bonusXp + "; questNull=" + questNull +
                "; factionEmpty=" + factionEmpty + "; lootGold=" + lootGold + "; lootDisabled=" + lootDisabled + "; result=" + result);
        }

        internal static void ObserveNativeUpdate(NPC npc)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            if (NativeUpdateReached.Add(id))
                PvpDiagnostics.Log("native_update_reached proxy=" + SafeProxyName(npc) + "; neverAggro=false");
        }

        internal static void ObserveNativeUpdateCompleted(NPC npc)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            Character target = null;
            try { target = npc.CurrentAggroTarget; } catch { }
            if (target != null && PvpCombatContainment.IsProtectedWorldActor(target))
            {
                PvpDiagnostics.Log("protected_target_cleared proxy=" + SafeProxyName(npc) + "; target=" + PvpCombatContainment.DescribeCombatTarget(target));
                try { npc.ForceAggroOn(null); } catch { }
                return;
            }
            if (target != null && PvpCombatContainment.IsPermittedProxyTarget(target) && LegalTargetAcquired.Add(id))
            {
                PvpDiagnostics.Log("legal_target_acquired proxy=" + SafeProxyName(npc) + "; target=" + PvpCombatContainment.DescribeCombatTarget(target));
                ObserveFirstActionLatency(id, "first_target_acquired");
            }
            if (!_nativeSimComparisonLogged && target != null) TryLogNativeSimComparison(npc);
            if (target != null && _goReleasedAt > 0f && Time.unscaledTime - _goReleasedAt >= 0.35f &&
                !CombatSectionReached.Contains(id) && CombatBranchTraceLogged.Add(id))
            {
                PvpDiagnostics.Log("combat_branch_trace proxy=" + SafeProxyName(npc) +
                    "; first_divergence=" + ClassifyCombatDivergence(npc) + "; " + CombatGateSnapshot(npc));
            }

            try
            {
                NavMeshAgent nav = npc.GetComponent<NavMeshAgent>();
                if (nav != null && nav.enabled && nav.isOnNavMesh && nav.hasPath && NavPursuitRequested.Add(id))
                {
                    NativeNavProbe probe;
                    if (!NativeNavProbes.TryGetValue(id, out probe))
                    {
                        probe = new NativeNavProbe(); NativeNavProbes[id] = probe;
                    }
                    Character actor = npc.GetComponent<Character>();
                    float distance = target == null || actor == null ? 0f : Vector3.Distance(actor.transform.position, target.transform.position);
                    float attackRange = 0f; object rawRange;
                    if (actor != null && TryReadField(actor, "AttackRange", out rawRange) && rawRange is float) attackRange = (float)rawRange;
                    Stats movementStats = actor == null ? null : actor.MyStats;
                    probe.PursuitNeedsMovement = target != null && PvpNativeNavHealthPolicy.NeedsPursuit(true, distance, attackRange);
                    probe.PursuitTargetId = target == null || target.gameObject == null ? 0 : target.gameObject.GetInstanceID();
                    probe.PursuitStartPosition = npc.transform.position;
                    Transform shell; VisualShellRoots.TryGetValue(id, out shell);
                    probe.ShellStartPosition = shell == null ? probe.PursuitStartPosition : shell.position;
                    probe.ShellStartLocalPosition = shell == null ? Vector3.zero : shell.localPosition;
                    probe.Destination = nav.destination;
                    probe.PursuitSampleAt = Time.unscaledTime + 0.75f;
                    probe.PursuitSamplePending = true;
                    PvpDiagnostics.Log("pursuit_probe_start proxy=" + SafeProxyName(npc) + "; target=" +
                        PvpCombatContainment.DescribeCombatTarget(target) + "; distance=" + distance.ToString("0.00") +
                        "; attackRange=" + attackRange.ToString("0.00") + "; needs_pursuit=" + probe.PursuitNeedsMovement +
                        "; enabled=" + nav.enabled + "; onMesh=" + nav.isOnNavMesh + "; isStopped=" + nav.isStopped +
                        "; hasPath=" + nav.hasPath + "; pathStatus=" + nav.pathStatus + "; destination=" + nav.destination +
                        "; remaining=" + nav.remainingDistance.ToString("0.00") + "; velocity=" + nav.velocity.magnitude.ToString("0.00") +
                        "; agent_speed=" + nav.speed.ToString("0.00") + "; run_speed=" + (movementStats == null ? -1f : movementStats.RunSpeed).ToString("0.00") +
                        "; actual_run_speed=" + (movementStats == null ? -1f : movementStats.actualRunSpeed).ToString("0.00"));
                }
            }
            catch { }
        }

        private static void TryLogNativeSimComparison(NPC proxy)
        {
            if (_nativeSimComparisonLogged || proxy == null) return;
            _nativeSimComparisonLogged = true; // exactly one bounded scan per match
            try
            {
                NPC healthy = UnityEngine.Object.FindObjectsOfType<NPC>()
                    .Where(x => x != null && !IsTemporaryNpc(x) && x.enabled &&
                        (x.SimPlayer || x.ThisSim != null) && x.CurrentAggroTarget != null)
                    .FirstOrDefault();
                PvpDiagnostics.Log("proxy_capability_snapshot " + CapabilitySnapshot(proxy));
                if (healthy != null) PvpDiagnostics.Log("native_sim_capability_snapshot " + CapabilitySnapshot(healthy));
                else PvpDiagnostics.Log("native_sim_capability_snapshot unavailable=no_loaded_fighting_native_sim");
            }
            catch (Exception ex)
            {
                PvpDiagnostics.Log("native_sim_capability_snapshot unavailable=" + ex.GetType().Name);
            }
        }

        private static string CapabilitySnapshot(NPC npc)
        {
            if (npc == null || npc.gameObject == null) return "npc=null";
            int id = npc.gameObject.GetInstanceID();
            Character actor = npc.GetComponent<Character>(); Stats stats = actor == null ? null : actor.MyStats;
            CastSpell caster = npc.GetComponent<CastSpell>(); NavMeshAgent nav = npc.GetComponent<NavMeshAgent>();
            Animator animator = null; try { animator = actor == null ? null : actor.GetMyAnim(); } catch { }
            object navDo, behDo, mhBow, mhWand, npcMyself, npcMyStats, npcMySpells, npcMyNav;
            bool navLoop = TryReadField(npc, "navDo", out navDo) && navDo != null;
            bool behaviorLoop = TryReadField(npc, "behDo", out behDo) && behDo != null;
            TryReadField(npc, "MHBow", out mhBow); TryReadField(npc, "MHWand", out mhWand);
            TryReadField(npc, "Myself", out npcMyself); TryReadField(npc, "MyStats", out npcMyStats);
            TryReadField(npc, "MySpells", out npcMySpells); TryReadField(npc, "MyNav", out npcMyNav);
            return "name=" + SafeProxyName(npc) + "; sim=" + npc.SimPlayer + "; thisSim=" + (npc.ThisSim != null) +
                "; npc_enabled=" + npc.enabled + "; myself=" + (actor != null) + "; myStats=" + (stats != null) +
                "; npc_myself_bound=" + object.ReferenceEquals(npcMyself, actor) + "; npc_mystats_bound=" + object.ReferenceEquals(npcMyStats, stats) +
                "; npc_myspells_bound=" + object.ReferenceEquals(npcMySpells, caster) + "; npc_mynav_bound=" + object.ReferenceEquals(npcMyNav, nav) +
                "; caster_enabled=" + (caster != null && caster.enabled) + "; neverAggro=" + npc.NeverAggro +
                "; target=" + (npc.CurrentAggroTarget != null) + "; alive=" + (actor != null && actor.Alive) +
                "; nav=" + (nav != null) + "; nav_enabled=" + (nav != null && nav.enabled) +
                "; on_mesh=" + (nav != null && nav.enabled && nav.isOnNavMesh) + "; isStopped=" + (nav != null && nav.enabled && nav.isStopped) +
                "; hasPath=" + (nav != null && nav.enabled && nav.hasPath) +
                "; pathStatus=" + (nav != null && nav.enabled && nav.isOnNavMesh ? nav.pathStatus.ToString() : "unavailable") +
                "; destination=" + (nav != null && nav.enabled && nav.isOnNavMesh ? nav.destination.ToString() : "unavailable") +
                "; remaining=" + (nav != null && nav.enabled && nav.isOnNavMesh ? nav.remainingDistance.ToString("0.00") : "unavailable") +
                "; nav_loop=" + navLoop + "; nav_owner=" + (ModOwnedNavLoops.ContainsKey(id) ? "pvp" : (navLoop ? "native" : "none")) +
                "; behavior_loop=" + behaviorLoop + "; behavior_owner=" + (ModOwnedBehaviorLoops.ContainsKey(id) ? "pvp" : (behaviorLoop ? "native" : "none")) +
                "; leashing=" + SnapshotField(npc, "Leashing") + "; offNav=" + SnapshotField(npc, "offNav") +
                "; spawnCD=" + SnapshotField(npc, "spawnCD") + "; behaviorDelay=" + SnapshotField(npc, "SpawnWithBehaviorDelay") +
                "; inMeleeRange=" + SnapshotField(npc, "inMeleeRange") + "; casting=" + IsNativeSpellRuntimeCasting(npc) +
                "; atkSpellDelay=" + SnapshotField(npc, "atkSpellDelay") + "; healCD=" + SnapshotField(npc, "healCD") +
                "; NPCSpellCooldown=" + SnapshotField(npc, "NPCSpellCooldown") + "; forceSpellCD=" + SnapshotField(npc, "forceSpellCD") +
                "; stunned=" + SnapshotField(stats, "Stunned") + "; feared=" + SnapshotField(stats, "Feared") +
                "; rooted=" + SnapshotField(stats, "Rooted") + "; charmed=" + SnapshotField(stats, "Charmed") +
                "; attackRange=" + SnapshotField(actor, "AttackRange") + "; animator=" + (animator != null) +
                "; controller=" + (animator != null && animator.runtimeAnimatorController != null) +
                "; class=" + (stats == null || stats.CharacterClass == null ? "unknown" : stats.CharacterClass.ClassName) +
                "; runSpeed=" + (stats == null ? -1f : stats.RunSpeed).ToString("0.00") + "; actualRunSpeed=" + (stats == null ? -1f : stats.actualRunSpeed).ToString("0.00") +
                "; attackSpells=" + (npc.MyAttackSpells == null ? 0 : npc.MyAttackSpells.Count) +
                "; heal=" + (npc.MyHealSpells == null ? 0 : npc.MyHealSpells.Count) + "; memmedHeal=" + (npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count) +
                "; MHBow=" + mhBow + "; MHWand=" + mhWand;
        }

        private static string CombatGateSnapshot(NPC npc)
        {
            if (npc == null || npc.gameObject == null) return "npc=null";
            int id = npc.gameObject.GetInstanceID();
            object navDo, behDo;
            bool navLoop = TryReadField(npc, "navDo", out navDo) && navDo != null;
            bool behaviorLoop = TryReadField(npc, "behDo", out behDo) && behDo != null;
            Character actor = npc.GetComponent<Character>(); Stats stats = actor == null ? null : actor.MyStats;
            return "neverAggro=" + npc.NeverAggro + "; alive=" + (actor != null && actor.Alive) +
                "; target=" + (npc.CurrentAggroTarget != null) + "; behavior_loop=" + behaviorLoop +
                "; behavior_owner=" + (ModOwnedBehaviorLoops.ContainsKey(id) ? "pvp" : (behaviorLoop ? "native" : "none")) +
                "; nav_loop=" + navLoop + "; nav_owner=" + (ModOwnedNavLoops.ContainsKey(id) ? "pvp" : (navLoop ? "native" : "none")) +
                "; nonraid_reached=" + NonRaidBehaviorReached.Contains(id) + "; combat_reached=" + CombatSectionReached.Contains(id) +
                "; leashing=" + SnapshotField(npc, "Leashing") + "; spawnCD=" + SnapshotField(npc, "spawnCD") +
                "; behaviorDelay=" + SnapshotField(npc, "SpawnWithBehaviorDelay") + "; casting=" + IsNativeSpellRuntimeCasting(npc) +
                "; stunned=" + SnapshotField(stats, "Stunned") + "; feared=" + SnapshotField(stats, "Feared") +
                "; rooted=" + SnapshotField(stats, "Rooted") + "; healCD=" + SnapshotField(npc, "healCD") +
                "; atkSpellDelay=" + SnapshotField(npc, "atkSpellDelay");
        }

        private static string SnapshotField(object instance, string name)
        {
            object value;
            return TryReadField(instance, name, out value) ? (value == null ? "null" : value.ToString()) : "unavailable";
        }

        private static string ClassifyCombatDivergence(NPC npc)
        {
            if (npc == null || npc.gameObject == null) return "npc_missing";
            int id = npc.gameObject.GetInstanceID();
            object value;
            if (!TryReadField(npc, "behDo", out value) || value == null) return "behavior_loop_missing";
            if (!TryReadField(npc, "navDo", out value) || value == null) return "nav_loop_missing";
            if (npc.NeverAggro) return "never_aggro";
            if (npc.CurrentAggroTarget == null) return "no_target";
            Character actor = npc.GetComponent<Character>();
            if (actor == null || !actor.Alive) return "dead";
            if (!NonRaidBehaviorReached.Contains(id)) return "behavior_coroutine_not_progressing";
            bool flag; float gate;
            if (TryReadBoolField(npc, "Leashing", out flag) && flag) return "leashing";
            if (TryReadFloatField(npc, "spawnCD", out gate) && gate > 0f) return "spawn_cd";
            if (TryReadFloatField(npc, "SpawnWithBehaviorDelay", out gate) && gate > 0f) return "spawn_behavior_delay";
            if (IsNativeSpellRuntimeCasting(npc)) return "casting";
            return "combat_not_reached_after_behavior_entry";
        }

        private static bool TryReadBoolField(object instance, string name, out bool value)
        {
            value = false; object raw;
            if (!TryReadField(instance, name, out raw) || !(raw is bool)) return false;
            value = (bool)raw; return true;
        }

        private static bool TryReadFloatField(object instance, string name, out float value)
        {
            value = 0f; object raw;
            if (!TryReadField(instance, name, out raw)) return false;
            if (raw is float) { value = (float)raw; return true; }
            return false;
        }

        internal static Exception ObserveNativeUpdateException(NPC npc, Exception exception)
        {
            if (exception == null || !IsTemporaryNpc(npc)) return exception;
            int id = npc.gameObject.GetInstanceID();
            if (NativeUpdateExceptionLogged.Add(id))
                PvpDiagnostics.Log("native_npc_exception method=NPC.Update; proxy=" + SafeProxyName(npc) +
                    "; type=" + exception.GetType().Name + "; message=" + (exception.Message ?? string.Empty));
            return exception; // diagnostics never suppress native failures.
        }

        internal static void ObserveCombatSection(NPC npc)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            if (CombatSectionReached.Add(id))
                PvpDiagnostics.Log("combat_section_reached proxy=" + SafeProxyName(npc));
        }

        internal static void ObserveMeleeAttempt(NPC npc)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            if (_activeMeleeDepth == 0) _activeMeleeProxyId = id;
            _activeMeleeDepth++;
            int before; MeleeAttemptCounts.TryGetValue(id, out before);
            Increment(MeleeAttemptCounts, id);
            if (before == 0)
            {
                PvpDiagnostics.Log("melee_decision proxy=" + SafeProxyName(npc));
                PvpDiagnostics.Log("melee_attempt proxy=" + SafeProxyName(npc));
                ObserveFirstActionLatency(id, "first_melee_decision");
            }
        }

        internal static Exception FinishMeleeAttempt(NPC npc, Exception exception)
        {
            if (npc != null && IsTemporaryNpc(npc) && _activeMeleeDepth > 0)
            {
                _activeMeleeDepth--;
                if (_activeMeleeDepth == 0) _activeMeleeProxyId = 0;
            }
            return exception;
        }

        internal static void ObserveAttackDecision(NPC npc, bool spellDecision)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            Increment(AttackDecisionCounts, id);
            Increment(spellDecision ? AttackSpellDecisionCounts : AttackSkillDecisionCounts, id);
            HashSet<int> logged = spellDecision ? AttackSpellDecisionLogged : AttackSkillDecisionLogged;
            if (logged.Add(id))
                PvpDiagnostics.Log((spellDecision ? "attack_spell_ai_entry" : "attack_skill_decision") +
                    " proxy=" + SafeProxyName(npc));
        }

        internal static AttackSpellEntryState BeginAttackSpellEntry(NPC npc)
        {
            AttackSpellEntryState state = new AttackSpellEntryState();
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return state;

            ObserveAttackDecision(npc, true);
            int id = npc.gameObject.GetInstanceID();
            ObserveFirstActionLatency(id, "first_attack_spell_candidate");
            state.Active = true;
            state.ProxyId = id;
            state.RequestsBefore = CounterValue(AttackSpellRequestCounts, id);

            float previousNpcCooldown, attackDelay, forceCooldown;
            string compatReason;
            bool reflectionReady = PvpNativeSpellExecutionBridge.TryReadAttackSpellState(npc,
                out previousNpcCooldown, out attackDelay, out forceCooldown, out compatReason);

            Character target = null;
            try { target = npc.CurrentAggroTarget; } catch { }
            bool targetInvulnerable = false;
            try { targetInvulnerable = target != null && target.Invulnerable; } catch { }
            bool casting = IsNativeSpellRuntimeCasting(npc);
            int spellCount = npc.MyAttackSpells == null ? 0 : npc.MyAttackSpells.Count;
            Stats npcStats = ReadField<Stats>(npc, "MyStats");
            int mana = npcStats == null ? 0 : npcStats.CurrentMana;
            int minimumMana = -1;
            try
            {
                if (npc.MyAttackSpells != null && npc.MyAttackSpells.Count > 0)
                    minimumMana = npc.MyAttackSpells.Where(x => x != null).Select(x => x.ManaCost).DefaultIfEmpty(-1).Min();
            }
            catch { minimumMana = -1; }

            if (!reflectionReady)
                state.Assessment = "reflection_surface_missing";
            else
                state.Assessment = PvpSpellExecutionPolicy.AttackEntryAssessment(target != null, targetInvulnerable, casting,
                    spellCount, previousNpcCooldown, attackDelay, mana, minimumMana);
            LatestAttackSpellAssessment[id] = state.Assessment;

            int logs = CounterValue(AttackEntryDetailLogCounts, id);
            if (logs < 6)
            {
                AttackEntryDetailLogCounts[id] = logs + 1;
                float distance = -1f;
                try { if (target != null) distance = Vector3.Distance(npc.transform.position, target.transform.position); } catch { }
                PvpDiagnostics.Log("spell_ai_entry proxy=" + SafeProxyName(npc) +
                    "; selected=native_pending; target=" + PvpCombatContainment.DescribeCombatTarget(target) +
                    "; candidates=" + spellCount + "; distance=" + (distance < 0f ? "unknown" : distance.ToString("0.00")) +
                    "; mana=" + mana + "; min_mana_cost=" + minimumMana +
                    "; npc_spell_cooldown_before=" + previousNpcCooldown.ToString("0.00") +
                    "; attack_spell_delay=" + attackDelay.ToString("0.00") +
                    "; force_spell_cooldown=" + forceCooldown.ToString("0.00") +
                    "; compatibility=" + compatReason + "; assessment=" + state.Assessment +
                    "; los=native_path_no_explicit_DoAttackSpell_los_gate");
            }
            return state;
        }

        internal static void FinishAttackSpellEntry(NPC npc, AttackSpellEntryState state)
        {
            if (!state.Active || npc == null || !IsTemporaryNpc(npc)) return;
            int requestsAfter = CounterValue(AttackSpellRequestCounts, state.ProxyId);
            if (requestsAfter > state.RequestsBefore) return;
            int logs = CounterValue(AttackEntryDetailLogCounts, state.ProxyId);
            if (logs >= 8) return;
            AttackEntryDetailLogCounts[state.ProxyId] = logs + 1;
            PvpDiagnostics.Log("spell_no_request proxy=" + SafeProxyName(npc) +
                "; assessment=" + (string.IsNullOrEmpty(state.Assessment) ? "unknown" : state.Assessment) +
                "; native_request_invoked=false");
        }

        // Called from PvpCombatContainment's existing damage/heal telemetry once the source is
        // identified as a temporary attacker (see DamageTelemetryState/HpTelemetryState threading).
        // Amounts are already clamped non-negative upstream; the <= 0 guard here is defensive only.
        internal static void RecordDamageDealt(int proxyId, int amount)
        {
            if (amount <= 0) return;
            long before; DamageDealtCounts.TryGetValue(proxyId, out before);
            DamageDealtCounts[proxyId] = before + amount;
            if (_activeMeleeDepth > 0 && _activeMeleeProxyId == proxyId)
            {
                long meleeBefore; MeleeDamageCounts.TryGetValue(proxyId, out meleeBefore);
                MeleeDamageCounts[proxyId] = meleeBefore + amount;
            }
            else if (CastExecutionProbes.ContainsKey(proxyId)) Increment(SpellEffectCounts, proxyId);
        }

        internal static void RecordHealingDone(int proxyId, int amount)
        {
            if (amount <= 0) return;
            long before; HealingDoneCounts.TryGetValue(proxyId, out before);
            HealingDoneCounts[proxyId] = before + amount;
            if (CastExecutionProbes.ContainsKey(proxyId)) Increment(SpellEffectCounts, proxyId);
        }

        internal static void ObserveHealCheck(NPC npc, bool raidPath)
        {
            if (!IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive) return;
            int id = npc.gameObject.GetInstanceID();
            Dictionary<int, int> counter = raidPath ? HealRaidCheckCounts : HealCheckCounts;
            int before; counter.TryGetValue(id, out before);
            Increment(counter, id);
            if (before == 0)
            {
                try
                {
                    Character actor = npc.GetComponent<Character>();
                    int friends = actor == null || actor.NearbyFriends == null ? 0 : actor.NearbyFriends.Count;
                    int injured = actor == null || actor.NearbyFriends == null ? 0 : actor.NearbyFriends.Count(x =>
                        x != null && x.MyStats != null && x.MyStats.CurrentMaxHP > 0 && x.MyStats.CurrentHP < x.MyStats.CurrentMaxHP);
                    int memmed = npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count;
                    int configured = npc.MyHealSpells == null ? 0 : npc.MyHealSpells.Count;
                    bool casting = IsNativeSpellRuntimeCasting(npc);
                    PvpDiagnostics.Log((raidPath ? "heal_raid_check" : "heal_check") + " proxy=" + SafeProxyName(npc) + "; configured=" + configured +
                        "; memmed=" + memmed + "; nearby_friends=" + friends + "; injured_friends=" + injured +
                        "; casting=" + casting);
                }
                catch { PvpDiagnostics.Log((raidPath ? "heal_raid_check" : "heal_check") + " proxy=" + SafeProxyName(npc) + "; detail=unavailable"); }
            }
        }

        internal static int BeginHealCheck(NPC npc, bool raidPath)
        {
            ObserveHealCheck(npc, raidPath);
            if (!IsTemporaryNpc(npc)) return 0;
            return CounterValue(NativeSpellRequestCounts, npc.gameObject.GetInstanceID());
        }

        internal static void FinishHealCheck(NPC npc, bool raidPath, int requestsBefore)
        {
            if (raidPath || !IsTemporaryNpc(npc) || !PvpCombatContainment.LethalFightActive || npc.NeverAggro) return;
            int id = npc.gameObject.GetInstanceID();
            if (CounterValue(NativeSpellRequestCounts, id) > requestsBefore) return; // native self/group path already acted.
            TryAssistProxyAllyHeal(npc);
        }

        private static void TryAssistProxyAllyHeal(NPC npc)
        {
            int id = npc.gameObject.GetInstanceID();
            CastSpell caster = ReadField<CastSpell>(npc, "MySpells");
            if (caster == null || IsNativeSpellRuntimeCasting(npc)) { LatestHealAssessment[id] = "already_casting"; return; }
            if (npc.MemmedHealSpells == null || npc.MemmedHealSpells.Count == 0)
            { LatestHealAssessment[id] = "no_memmed_heal_spell"; return; }

            float healCooldown;
            if (!PvpNativeSpellExecutionBridge.TryReadHealCooldown(npc, out healCooldown))
            { LatestHealAssessment[id] = "reflection_surface_missing"; return; }
            if (healCooldown > 0f) { LatestHealAssessment[id] = "native_heal_cooldown"; return; }

            Character healer = null;
            try { healer = ReadField<Character>(npc, "Myself") ?? npc.GetComponent<Character>(); } catch { healer = npc.GetComponent<Character>(); }
            Character ally;
            float ratio;
            if (!PvpCombatContainment.TryFindInjuredAttackerAlly(healer, out ally, out ratio))
            { LatestHealAssessment[id] = "no_injured_legal_ally"; return; }
            Increment(HealLegalAllyCounts, id);

            Spell selected = null;
            string blocker = "no_usable_heal_spell";
            float distance = -1f;
            try { distance = Vector3.Distance(healer.transform.position, ally.transform.position); } catch { }
            foreach (Spell spell in npc.MemmedHealSpells)
            {
                if (spell == null) continue;
                PvpSpellSemanticSnapshot healSemantic = PvpSpellSemantics.Inspect(spell);
                if (!healSemantic.DirectHpHeal && !healSemantic.HealOverTime) continue;
                Stats currentStats = ReadField<Stats>(npc, "MyStats");
                if (currentStats == null || currentStats.CurrentMana <= spell.ManaCost) { blocker = "resources"; continue; }
                if (distance >= 0f && spell.SpellRange <= distance) { blocker = "range"; continue; }
                selected = spell;
                break;
            }
            if (selected == null) { LatestHealAssessment[id] = blocker; return; }
            Increment(HealSpellSelectionCounts, id);
            LatestHealAssessment[id] = "native_request_pending";

            int logs = CounterValue(HealDetailLogCounts, id);
            if (logs < 8)
            {
                HealDetailLogCounts[id] = logs + 1;
                PvpDiagnostics.Log("heal_spell_selected proxy=" + SafeProxyName(npc) + "; spell=" + selected.SpellName +
                    "; target=" + PvpCombatContainment.DescribeCombatTarget(ally) + "; target_hp_ratio=" + ratio.ToString("0.00") +
                    "; range=" + selected.SpellRange.ToString("0.00") + "; distance=" + (distance < 0f ? "unknown" : distance.ToString("0.00")) +
                    "; mana=" + (ReadField<Stats>(npc, "MyStats") == null ? 0 : ReadField<Stats>(npc, "MyStats").CurrentMana) + "; mana_cost=" + selected.ManaCost +
                    "; native_method=CastSpell.StartSpell(Spell,Stats); source=pvp_team_bridge");
            }

            try
            {
                NavMeshAgent nav = ReadField<NavMeshAgent>(npc, "MyNav");
                if (nav != null && nav.enabled && nav.isOnNavMesh) nav.isStopped = true;
                Character actor = ReadField<Character>(npc, "Myself");
                Animator animator = actor == null ? null : actor.GetMyAnim();
                if (animator != null && animator.enabled)
                {
                    animator.SetBool("Walking", false);
                    animator.SetBool("Patrol", false);
                }
                Increment(HealBridgeRequestCounts, id);
                bool accepted = caster.StartSpell(selected, ally.MyStats);
                if (accepted)
                {
                    Increment(HealBridgeAcceptedCounts, id);
                    PvpNativeSpellExecutionBridge.TrySetNativeHealCooldown(npc, selected);
                }
            }
            catch (Exception ex)
            {
                LatestHealAssessment[id] = "native_request_exception:" + ex.GetType().Name;
                PvpDiagnostics.Log("heal_native_request_exception proxy=" + SafeProxyName(npc) + "; error=" + ex.GetType().Name);
            }
        }

        internal static bool ObserveAndAllowSpellStart(CastSpell caster, Spell spell, ref Stats target, bool directCastEntry)
        {
            Character actor = null;
            try { actor = caster == null ? null : caster.MyChar; } catch { }
            bool temporary = IsTemporaryActor(actor);
            int id = temporary && actor != null ? actor.gameObject.GetInstanceID() : 0;

            bool adaptedToSelf;
            if (!PvpCombatContainment.PrepareSpellStart(caster, spell, ref target, directCastEntry, out adaptedToSelf))
            {
                if (temporary)
                {
                    Increment(SpellContainmentRejectCounts, id);
                    PvpDiagnostics.Log("spell_request_blocked_by_pvp_boundary proxy=" + SafeActorName(actor) +
                        "; spell=" + (spell == null ? "unknown" : spell.SpellName));
                }
                return false;
            }

            PvpSpellSemanticSnapshot semantic = PvpSpellSemantics.Inspect(spell);
            if (temporary)
            {
                // This point is the actual native request boundary: returning true from this Prefix
                // means the current bool-returning StartSpell-family method will execute.
                Increment(NativeSpellRequestCounts, id);
                bool countsAsHealing = semantic.DirectHpHeal || semantic.HealOverTime;
                if (countsAsHealing)
                {
                    int priorHealRequests = CounterValue(HealNativeRequestCounts, id);
                    Increment(HealNativeRequestCounts, id);
                    if (CounterValue(HealSpellSelectionCounts, id) <= priorHealRequests) Increment(HealSpellSelectionCounts, id);
                }
                else if (semantic.Harmful) Increment(AttackSpellRequestCounts, id);

                int logs = CounterValue(SpellRequestDetailLogCounts, id);
                if (logs < 8)
                {
                    SpellRequestDetailLogCounts[id] = logs + 1;
                    Character targetActor = null;
                    try { targetActor = target == null ? null : target.Myself; } catch { }
                    float distance = -1f;
                    try { if (targetActor != null) distance = Vector3.Distance(actor.transform.position, targetActor.transform.position); } catch { }
                    int mana = actor.MyStats == null ? 0 : actor.MyStats.CurrentMana;
                    PvpDiagnostics.Log("spell_native_request proxy=" + SafeActorName(actor) +
                        "; spell=" + (spell == null ? "unknown" : spell.SpellName) +
                        "; spell_id=" + StableSpellId(spell) + "; category=" + semantic.Token +
                        "; beneficial=" + semantic.Beneficial + "; hp_heal=" + countsAsHealing +
                        "; target=" + PvpCombatContainment.DescribeCombatTarget(targetActor) +
                        "; target_adapted_to_self=" + adaptedToSelf +
                        "; range=" + (spell == null ? "unknown" : spell.SpellRange.ToString("0.00")) +
                        "; distance=" + (distance < 0f ? "unknown" : distance.ToString("0.00")) +
                        "; mana=" + mana + "; mana_cost=" + (spell == null ? -1 : spell.ManaCost) +
                        "; method=CastSpell.StartSpell_family; native_request_invoked=true");
                }
                ObserveFirstActionLatency(id, countsAsHealing ? "first_heal_request" : "first_native_spell_request");
            }

            ObserveSpellStart(caster, spell);
            return true;
        }

        internal static void ObserveSpellStart(CastSpell caster, Spell spell)
        {
            Character actor = null;
            try { actor = caster == null ? null : caster.MyChar; } catch { }
            if (!IsTemporaryActor(actor)) return;
            int actorId = actor.gameObject.GetInstanceID();
            int spellId = spell == null ? 0 : spell.GetHashCode();
            int key = unchecked((actorId * 397) ^ spellId);
            int frame = Time.frameCount, previous;
            if (LastSpellStartFrame.TryGetValue(key, out previous) && previous == frame) return;
            LastSpellStartFrame[key] = frame;
            int before; SpellStartCounts.TryGetValue(actorId, out before);
            Increment(SpellStartCounts, actorId);
            if (before == 0) PvpDiagnostics.Log("spell_start proxy=" + (actor.MyStats == null ? actor.name : actor.MyStats.MyName) +
                "; spell=" + (spell == null ? "unknown" : spell.SpellName));
        }

        internal static void ObserveSpellStartResult(CastSpell caster, Spell spell, Stats target, bool nativeAccepted)
        {
            Character actor = null;
            try { actor = caster == null ? null : caster.MyChar; } catch { }
            ObserveHealthyNativeSimCastResult(caster, actor, spell, target, nativeAccepted);
            if (!IsTemporaryActor(actor) || caster == null || actor.gameObject == null) return;
            int id = actor.gameObject.GetInstanceID();
            PvpSpellSemanticSnapshot semantic = PvpSpellSemantics.Inspect(spell);
            bool countsAsHealing = semantic.DirectHpHeal || semantic.HealOverTime;
            bool castingObserved = false;
            try { castingObserved = spell != null && caster.GetCurrentCast() == spell; } catch { }
            if (!nativeAccepted)
            {
                Increment(SpellRejectedCounts, id);
                if (countsAsHealing) LatestHealAssessment[id] = "native_rejected";
                int logs; SpellResultDetailLogCounts.TryGetValue(id, out logs);
                if (logs < 8)
                {
                    SpellResultDetailLogCounts[id] = logs + 1;
                    PvpDiagnostics.Log("spell_native_result proxy=" + SafeActorName(actor) + "; spell=" +
                        (spell == null ? "unknown" : spell.SpellName) + "; category=" + semantic.Token +
                        "; accepted=false; casting=" + castingObserved + "; classification=native_request_rejected");
                }
                return;
            }

            int spellId = spell == null ? 0 : spell.GetHashCode();
            int acceptedKey = unchecked((id * 397) ^ spellId);
            int acceptedFrame;
            if (LastSpellAcceptedFrame.TryGetValue(acceptedKey, out acceptedFrame) && acceptedFrame == Time.frameCount) return;
            LastSpellAcceptedFrame[acceptedKey] = Time.frameCount;

            Increment(SpellAcceptedCounts, id);
            if (countsAsHealing)
            {
                Increment(HealNativeAcceptedCounts, id);
                LatestHealAssessment[id] = "native_accepted";
            }
            else if (semantic.Harmful) Increment(AttackSpellAcceptedCounts, id);
            Character targetActor = null;
            try { targetActor = target == null ? null : target.Myself; } catch { }
            bool targetResolved = targetActor != null || semantic.DeclaresSelf;
            if (countsAsHealing && targetResolved) Increment(HealTargetResolvedCounts, id);
            long damageBefore, healingBefore;
            DamageDealtCounts.TryGetValue(id, out damageBefore); HealingDoneCounts.TryGetValue(id, out healingBefore);
            float vesselCreatedAt;
            bool vesselAlreadyCreated = LastVesselCreatedAt.TryGetValue(SpellProbeKey(id, spell), out vesselCreatedAt) &&
                Time.unscaledTime - vesselCreatedAt < 0.5f;
            CastExecutionProbes[id] = new CastExecutionProbe
            {
                Spell = spell,
                Target = target,
                Category = semantic.Category,
                Beneficial = semantic.Beneficial,
                CountsAsHealing = countsAsHealing,
                VesselCreated = vesselAlreadyCreated,
                AcceptedAt = Time.unscaledTime,
                DamageBefore = damageBefore,
                HealingBefore = healingBefore,
                TargetHpBefore = target == null ? -1 : target.CurrentHP,
                AnimatorStateAtAccept = AnimatorStateHash(actor)
            };
            int detail; SpellResultDetailLogCounts.TryGetValue(id, out detail);
            if (detail < 8)
            {
                SpellResultDetailLogCounts[id] = detail + 1;
                PvpDiagnostics.Log("spell_native_result proxy=" + SafeActorName(actor) + "; spell=" +
                    (spell == null ? "unknown" : spell.SpellName) + "; category=" + semantic.Token +
                    "; accepted=true; casting=" + castingObserved + "; beneficial=" + semantic.Beneficial +
                    "; hp_heal=" + countsAsHealing + "; target=" + PvpCombatContainment.DescribeCombatTarget(targetActor) +
                    "; heal_target_resolved=" + targetResolved + "; classification=native_accepted");
            }
        }

        internal static void ObserveVesselInterruptBridge(Character caster, Character target)
        {
            if (!PvpCombatContainment.LethalFightActive || !IsTemporaryActor(caster)) return;
            int id = caster.gameObject.GetInstanceID();
            int logs = CounterValue(VesselDetailLogCounts, id);
            if (logs >= 4) return;
            VesselDetailLogCounts[id] = logs + 1;
            PvpDiagnostics.Log("spell_vessel_interrupt_bridge proxy=" + SafeActorName(caster) +
                "; target=" + PvpCombatContainment.DescribeCombatTarget(target) +
                "; scope=active_temporary_proxy_only; persistent_sim=false; interruptable_temporarily=false");
        }

        private static int SpellProbeKey(int actorId, Spell spell)
        {
            return unchecked((actorId * 397) ^ (spell == null ? 0 : spell.GetHashCode()));
        }

        internal static void ObserveSpellVesselCreated(CastSpell source, Spell spell, Stats target)
        {
            Character actor = null;
            try { actor = source == null ? null : source.MyChar; } catch { }
            if (!PvpCombatContainment.LethalFightActive || !IsTemporaryActor(actor)) return;
            int id = actor.gameObject.GetInstanceID();
            Increment(VesselCreatedCounts, id);
            LastVesselCreatedAt[SpellProbeKey(id, spell)] = Time.unscaledTime;
            CastExecutionProbe probe;
            if (CastExecutionProbes.TryGetValue(id, out probe) && probe != null && probe.Spell == spell) probe.VesselCreated = true;
            int logs = CounterValue(VesselDetailLogCounts, id);
            if (logs < 8)
            {
                VesselDetailLogCounts[id] = logs + 1;
                Character targetActor = null; try { targetActor = target == null ? null : target.Myself; } catch { }
                PvpDiagnostics.Log("spell_vessel_created proxy=" + SafeActorName(actor) + "; spell=" +
                    (spell == null ? "unknown" : spell.SpellName) + "; target=" +
                    PvpCombatContainment.DescribeCombatTarget(targetActor));
            }
        }

        internal static void ObserveSpellVesselResolve(CastSpell source, Spell spell, Stats target)
        {
            Character actor = null;
            try { actor = source == null ? null : source.MyChar; } catch { }
            if (!PvpCombatContainment.LethalFightActive || !IsTemporaryActor(actor)) return;
            int id = actor.gameObject.GetInstanceID();
            Increment(VesselResolveCounts, id);
            CastExecutionProbe probe;
            if (CastExecutionProbes.TryGetValue(id, out probe) && probe != null && probe.Spell == spell) probe.ResolveReached = true;
            int logs = CounterValue(VesselDetailLogCounts, id);
            if (logs < 10)
            {
                VesselDetailLogCounts[id] = logs + 1;
                Character targetActor = null; try { targetActor = target == null ? null : target.Myself; } catch { }
                PvpDiagnostics.Log("spell_vessel_resolve proxy=" + SafeActorName(actor) + "; spell=" +
                    (spell == null ? "unknown" : spell.SpellName) + "; target=" +
                    PvpCombatContainment.DescribeCombatTarget(targetActor) + "; native_resolve_entered=true");
            }
        }

        internal static Character ResolveActiveSpellSource(Spell spell, Stats target, Character supplied)
        {
            if (supplied != null) return supplied;
            if (!PvpCombatContainment.LethalFightActive || spell == null) return supplied;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue;
                CastSpell caster = go.GetComponent<CastSpell>();
                if (caster == null) continue;
                try { if (caster.GetCurrentCast() == spell) return caster.MyChar; } catch { }
            }
            try
            {
                Character targetActor = target == null ? null : target.Myself;
                if (targetActor != null && IsTemporaryActor(targetActor) &&
                    (spell.SelfOnly || spell.ApplyToCaster || spell.InflictOnSelf)) return targetActor;
            }
            catch { }
            return supplied;
        }

        internal static void ObserveHealMeEntered(int sourceProxyId, Stats target, int requested)
        {
            if (!PvpCombatContainment.LethalFightActive || sourceProxyId == 0) return;
            Increment(HealMeEnteredCounts, sourceProxyId);
            CastExecutionProbe probe;
            if (CastExecutionProbes.TryGetValue(sourceProxyId, out probe) && probe != null) probe.HealMeEntered = true;
            int logs = CounterValue(EffectDetailLogCounts, sourceProxyId);
            if (logs < 8)
            {
                EffectDetailLogCounts[sourceProxyId] = logs + 1;
                Character targetActor = null; try { targetActor = target == null ? null : target.Myself; } catch { }
                PvpDiagnostics.Log("healme_entered proxy_id=" + sourceProxyId + "; target=" +
                    PvpCombatContainment.DescribeCombatTarget(targetActor) + "; raw_requested=" + requested);
            }
        }

        internal static void RecordHealMeObserved(int sourceProxyId, Stats target, int before, int after, int requested)
        {
            if (!PvpCombatContainment.LethalFightActive || sourceProxyId == 0) return;
            CastExecutionProbe probe;
            bool effective = after > before;
            if (CastExecutionProbes.TryGetValue(sourceProxyId, out probe) && probe != null)
            {
                probe.HealMeEntered = true;
                if (effective) probe.EffectObserved = true;
            }
            int effectiveAmount = Math.Max(0, after - before);
            int overheal = requested > 0 ? Math.Max(0, requested - effectiveAmount) : -1;
            int logs = CounterValue(EffectDetailLogCounts, sourceProxyId);
            if (logs < 12)
            {
                EffectDetailLogCounts[sourceProxyId] = logs + 1;
                PvpDiagnostics.Log("heal_effect_applied proxy_id=" + sourceProxyId + "; hp_before=" + before +
                    "; hp_after=" + after + "; raw_requested=" + requested + "; effective=" + effectiveAmount +
                    "; overheal=" + (overheal < 0 ? "unknown" : overheal.ToString()) + "; proven=" + effective);
            }
        }

        internal static void RecordStatusEffectApplied(Character source, Stats target, Spell spell, bool applied, string outcome,
            int beforeMatches, int afterMatches, float beforeDuration, float afterDuration)
        {
            source = ResolveActiveSpellSource(spell, target, source);
            if (!PvpCombatContainment.LethalFightActive || !IsTemporaryActor(source)) return;
            int id = source.gameObject.GetInstanceID();
            CastExecutionProbe probe;
            if (CastExecutionProbes.TryGetValue(id, out probe) && probe != null && probe.Spell == spell)
            {
                probe.StatusApplied = applied;
                if (applied) probe.EffectObserved = true;
            }
            if (applied) Increment(StatusAppliedCounts, id);
            int logs = CounterValue(EffectDetailLogCounts, id);
            if (logs < 12)
            {
                EffectDetailLogCounts[id] = logs + 1;
                Character targetActor = null; try { targetActor = target == null ? null : target.Myself; } catch { }
                PvpDiagnostics.Log("status_effect_result proxy=" + SafeActorName(source) + "; spell=" +
                    (spell == null ? "unknown" : spell.SpellName) + "; target=" +
                    PvpCombatContainment.DescribeCombatTarget(targetActor) + "; outcome=" + (outcome ?? "unknown") +
                    "; proven_applied=" + applied + "; matching_before=" + beforeMatches + "; matching_after=" + afterMatches +
                    "; duration_before=" + beforeDuration.ToString("0.00") + "; duration_after=" + afterDuration.ToString("0.00"));
            }
        }

        private static void ObserveHealthyNativeSimCastResult(CastSpell caster, Character actor, Spell spell, Stats target, bool nativeAccepted)
        {
            if (_healthyNativeCastLogged || !nativeAccepted || !PvpCombatContainment.LethalFightActive || caster == null || actor == null) return;
            NPC npc = null;
            try { npc = actor.MyNPC; } catch { }
            if (npc == null || IsTemporaryNpc(npc) || !npc.SimPlayer) return;
            _healthyNativeCastLogged = true; // one proven accepted native Sim cast per match; observation only.
            try
            {
                Character targetActor = target == null ? null : target.Myself;
                float distance = -1f;
                try { if (targetActor != null) distance = Vector3.Distance(actor.transform.position, targetActor.transform.position); } catch { }
                object attackDelay, healCd, npcSpellCooldown, forceCooldown, mhBow, mhWand, bowEquipped;
                TryReadField(npc, "atkSpellDelay", out attackDelay);
                TryReadField(npc, "healCD", out healCd);
                TryReadField(npc, "NPCSpellCooldown", out npcSpellCooldown);
                TryReadField(npc, "forceSpellCD", out forceCooldown);
                TryReadField(npc, "MHBow", out mhBow);
                TryReadField(npc, "MHWand", out mhWand);
                TryReadField(npc, "BowEquipped", out bowEquipped);
                Animator animator = null; try { animator = actor.GetMyAnim(); } catch { }
                bool casting = IsNativeSpellRuntimeCasting(npc);
                PvpDiagnostics.Log("healthy_native_sim_cast accepted=true; name=" + SafeActorName(actor) +
                    "; spell=" + (spell == null ? "unknown" : spell.SpellName) + "; spell_id=" + StableSpellId(spell) +
                    "; target=" + PvpCombatContainment.DescribeCombatTarget(targetActor) +
                    "; distance=" + (distance < 0f ? "unknown" : distance.ToString("0.00")) +
                    "; range=" + (spell == null ? "unknown" : spell.SpellRange.ToString("0.00")) +
                    "; caster_enabled=" + caster.enabled + "; caster_owner_match=" + (caster.MyChar == actor) +
                    "; npc_myspells_match=" + (ReadField<CastSpell>(npc, "MySpells") == caster) + "; attack_spells=" + (npc.MyAttackSpells == null ? 0 : npc.MyAttackSpells.Count) +
                    "; heal_spells=" + (npc.MyHealSpells == null ? 0 : npc.MyHealSpells.Count) +
                    "; memmed_heals=" + (npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count) +
                    "; mana=" + (actor.MyStats == null ? -1 : actor.MyStats.CurrentMana) +
                    "; mana_cost=" + (spell == null ? -1 : spell.ManaCost) + "; casting=" + casting +
                    "; animator=" + (animator != null && animator.enabled) + "; MHBow=" + mhBow + "; MHWand=" + mhWand +
                    "; BowEquipped=" + bowEquipped + "; atkSpellDelay=" + attackDelay + "; healCD=" + healCd +
                    "; NPCSpellCooldown=" + npcSpellCooldown + "; forceSpellCD=" + forceCooldown +
                    "; los=native_StartSpell_acceptance_proves_native_request_prerequisites_passed");
            }
            catch (Exception ex)
            {
                PvpDiagnostics.Log("healthy_native_sim_cast accepted=true; detail=unavailable; error=" + ex.GetType().Name);
            }
        }

        private static string StableSpellId(Spell spell)
        {
            if (spell == null) return "unknown";
            try
            {
                return string.IsNullOrEmpty(spell.Id) ? (spell.SpellName ?? "unknown") : spell.Id;
            }
            catch { return "unknown"; }
        }

        private static void TickCastExecutionHealth()
        {
            if (!PvpCombatContainment.LethalFightActive || CastExecutionProbes.Count == 0) return;
            foreach (int id in CastExecutionProbes.Keys.ToArray())
            {
                try
                {
                    CastExecutionProbe probe; if (!CastExecutionProbes.TryGetValue(id, out probe) || probe == null) continue;
                    GameObject go = TeamClones.FirstOrDefault(x => x != null && x.GetInstanceID() == id);
                    CastSpell caster = go == null ? null : go.GetComponent<CastSpell>();
                    if (caster == null) { CastExecutionProbes.Remove(id); continue; }
                    long damage, healing; DamageDealtCounts.TryGetValue(id, out damage); HealingDoneCounts.TryGetValue(id, out healing);
                    if (damage > probe.DamageBefore || healing > probe.HealingBefore) probe.EffectObserved = true;
                    if (probe.Beneficial && probe.Target != null && probe.TargetHpBefore >= 0 && probe.Target.CurrentHP > probe.TargetHpBefore)
                        probe.EffectObserved = true;
                    Spell current = null; try { current = caster.GetCurrentCast(); } catch { }
                    if (current == probe.Spell || Time.unscaledTime - probe.AcceptedAt < 0.02f) continue;
                    Increment(SpellCompletedCounts, id);
                    if (probe.CountsAsHealing) Increment(HealCompletedCounts, id);
                    else if (probe.Category == PvpSpellSemanticCategory.HarmfulDamage ||
                        probe.Category == PvpSpellSemanticCategory.OffensiveDebuff ||
                        probe.Category == PvpSpellSemanticCategory.CrowdControl ||
                        probe.Category == PvpSpellSemanticCategory.Area) Increment(AttackSpellCompletedCounts, id);
                    if (probe.EffectObserved)
                    {
                        if (probe.CountsAsHealing) Increment(HealEffectCounts, id);
                        else Increment(AttackSpellEffectCounts, id);
                        int effects; SpellEffectCounts.TryGetValue(id, out effects);
                        if (effects == 0) SpellEffectCounts[id] = 1;
                        if (probe.CountsAsHealing) LatestHealAssessment[id] = "effect_observed";
                    }
                    else if (probe.CountsAsHealing) LatestHealAssessment[id] = "completed_no_effect";
                    int logs; CastFinishDetailLogCounts.TryGetValue(id, out logs);
                    if (logs < 3)
                    {
                        CastFinishDetailLogCounts[id] = logs + 1;
                        NPC npc = go.GetComponent<NPC>(); Character actor = go.GetComponent<Character>();
                        int animatorState = AnimatorStateHash(actor);
                        PvpDiagnostics.Log("cast_finished proxy=" + SafeProxyName(npc) + "; spell=" +
                            (probe.Spell == null ? "unknown" : probe.Spell.SpellName) + "; effect_observed=" + probe.EffectObserved +
                            "; animator_state=" + animatorState + "; animator_state_changed=" + (animatorState != probe.AnimatorStateAtAccept) +
                            "; elapsed=" + Math.Max(0f, Time.unscaledTime - probe.AcceptedAt).ToString("0.00"));
                    }
                    CastExecutionProbes.Remove(id);
                }
                catch (Exception ex)
                {
                    CastExecutionProbes.Remove(id);
                    PvpDiagnostics.Log("cast_probe_failed proxy_id=" + id + "; error=" + ex.GetType().Name);
                }
            }
        }

        private static bool IsBeneficialSpellForTelemetry(Spell spell)
        {
            return PvpSpellSemantics.Inspect(spell).Beneficial;
        }

        private static int AnimatorStateHash(Character actor)
        {
            try
            {
                Animator animator = actor == null ? null : actor.GetMyAnim();
                return animator != null && animator.enabled && animator.layerCount > 0
                    ? animator.GetCurrentAnimatorStateInfo(0).fullPathHash : 0;
            }
            catch { return 0; }
        }

        private static string SafeActorName(Character actor)
        {
            try { return actor == null ? "null" : (actor.MyStats == null || string.IsNullOrWhiteSpace(actor.MyStats.MyName) ? actor.name : actor.MyStats.MyName); }
            catch { return "unknown"; }
        }

        internal static bool HasAnyNativeCombatEvidence()
        {
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue;
                int id = go.GetInstanceID();
                int melee = 0, completedCasts = 0;
                long damage = 0, healing = 0;
                MeleeAttemptCounts.TryGetValue(id, out melee);
                SpellCompletedCounts.TryGetValue(id, out completedCasts);
                DamageDealtCounts.TryGetValue(id, out damage);
                HealingDoneCounts.TryGetValue(id, out healing);
                NativeNavProbe navProbe; bool moved = NativeNavProbes.TryGetValue(id, out navProbe) && navProbe.MovementObserved;
                // High-level decisions, heal checks, destination requests and StartSpell Prefixes are
                // intentionally excluded. They were all present in the failed live 5v5 and therefore
                // cannot prove a synthetic actor crossed into executable combat behavior.
                if (PvpCombatExecutionPolicy.AnyExecutionEvidence(moved, melee > 0, completedCasts > 0,
                    damage > 0, healing > 0)) return true;
            }
            return false;
        }

        internal static string NativeCombatEvidenceSummary()
        {
            return "native_updates=" + NativeUpdateReached.Count + "; combat_sections=" + CombatSectionReached.Count +
                "; legal_targets=" + LegalTargetAcquired.Count + "; nav_pursuit=" + NavPursuitRequested.Count +
                "; melee_attempts=" + MeleeAttemptCounts.Values.Sum() + "; " + BalanceRuntimeSummary();
        }

        private static void Increment(Dictionary<int, int> map, int id)
        {
            int value; map.TryGetValue(id, out value); map[id] = value + 1;
        }

        private static int CounterValue(Dictionary<int, int> map, int id)
        {
            int value; return map != null && map.TryGetValue(id, out value) ? value : 0;
        }

        private static string SafeProxyName(NPC npc)
        {
            try { return npc == null ? "null" : (string.IsNullOrWhiteSpace(npc.NPCName) ? npc.gameObject.name : npc.NPCName); }
            catch { return "unknown"; }
        }

        internal static string BalanceRuntimeSummary()
        {
            int healCapable = 0, healChecks = 0, healRaidChecks = 0, skillDecisions = 0, spellEntries = 0;
            int nativeRequests = 0, attackRequests = 0, healRequests = 0, containmentRejects = 0, nativeRejected = 0;
            int spellAccepted = 0, spellCompleted = 0, spellEffects = 0, healLegal = 0, healBridgeRequests = 0, healBridgeAccepted = 0;
            int vesselCreated = 0, vesselResolved = 0, healMeEntered = 0, statusApplied = 0;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue; int id = go.GetInstanceID();
                int value;
                if (EligibleHealSpellCounts.TryGetValue(id, out value) && value > 0) healCapable++;
                if (HealCheckCounts.TryGetValue(id, out value)) healChecks += value;
                if (HealRaidCheckCounts.TryGetValue(id, out value)) healRaidChecks += value;
                if (AttackSkillDecisionCounts.TryGetValue(id, out value)) skillDecisions += value;
                if (AttackSpellDecisionCounts.TryGetValue(id, out value)) spellEntries += value;
                if (NativeSpellRequestCounts.TryGetValue(id, out value)) nativeRequests += value;
                if (AttackSpellRequestCounts.TryGetValue(id, out value)) attackRequests += value;
                if (HealNativeRequestCounts.TryGetValue(id, out value)) healRequests += value;
                if (SpellContainmentRejectCounts.TryGetValue(id, out value)) containmentRejects += value;
                if (SpellRejectedCounts.TryGetValue(id, out value)) nativeRejected += value;
                if (SpellAcceptedCounts.TryGetValue(id, out value)) spellAccepted += value;
                if (SpellCompletedCounts.TryGetValue(id, out value)) spellCompleted += value;
                if (SpellEffectCounts.TryGetValue(id, out value)) spellEffects += value;
                if (HealLegalAllyCounts.TryGetValue(id, out value)) healLegal += value;
                if (HealBridgeRequestCounts.TryGetValue(id, out value)) healBridgeRequests += value;
                if (HealBridgeAcceptedCounts.TryGetValue(id, out value)) healBridgeAccepted += value;
                if (VesselCreatedCounts.TryGetValue(id, out value)) vesselCreated += value;
                if (VesselResolveCounts.TryGetValue(id, out value)) vesselResolved += value;
                if (HealMeEnteredCounts.TryGetValue(id, out value)) healMeEntered += value;
                if (StatusAppliedCounts.TryGetValue(id, out value)) statusApplied += value;
            }
            return "heal_capable_attackers=" + healCapable + "; heal_checks=" + healChecks + "; heal_raid_checks=" + healRaidChecks +
                "; attack_skill_decisions=" + skillDecisions + "; attack_spell_ai_entries=" + spellEntries +
                "; spell_native_requests=" + nativeRequests + "; attack_spell_requests=" + attackRequests +
                "; heal_native_requests=" + healRequests + "; spell_boundary_rejected=" + containmentRejects +
                "; spell_native_rejected=" + nativeRejected + "; spell_native_accepted=" + spellAccepted +
                "; spell_casts_finished=" + spellCompleted + "; spell_effect_events=" + spellEffects +
                "; heal_legal_allies=" + healLegal + "; heal_bridge_requests=" + healBridgeRequests +
                "; heal_bridge_accepted=" + healBridgeAccepted + "; vessels_created=" + vesselCreated +
                "; vessels_resolved=" + vesselResolved + "; healme_entered=" + healMeEntered +
                "; statuses_proven_applied=" + statusApplied;
        }

        // Bounded per-proxy terminal diagnostic (0.5.11): one line per temporary attacker, logged
        // exactly once when the fight ends (see PvpCombatContainment.LogBalanceSummary). This is the
        // "did this proxy actually evaluate and use its abilities" answer the live/aggregate summary
        // could not give on its own - it never runs per frame and never dumps a spellbook.
        internal static void LogPerProxyAbilitySummary()
        {
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i];
                if (go == null) continue;
                int id = go.GetInstanceID();
                PvpOpponentProfile profile = i < TeamProfiles.Count ? TeamProfiles[i] : null;
                int offensiveSpells = CounterValue(EligibleCombatSpellCounts, id);
                int healSpells = CounterValue(EligibleHealSpellCounts, id);
                int healChecks = CounterValue(HealCheckCounts, id);
                int healRaidChecks = CounterValue(HealRaidCheckCounts, id);
                int skillDecisions = CounterValue(AttackSkillDecisionCounts, id);
                int spellEntries = CounterValue(AttackSpellDecisionCounts, id);
                int spellStarts = CounterValue(SpellStartCounts, id);
                int nativeRequests = CounterValue(NativeSpellRequestCounts, id);
                int attackRequests = CounterValue(AttackSpellRequestCounts, id);
                int attackAccepted = CounterValue(AttackSpellAcceptedCounts, id);
                int attackCompleted = CounterValue(AttackSpellCompletedCounts, id);
                int attackEffects = CounterValue(AttackSpellEffectCounts, id);
                int healRequests = CounterValue(HealNativeRequestCounts, id);
                int healAccepted = CounterValue(HealNativeAcceptedCounts, id);
                int healCompleted = CounterValue(HealCompletedCounts, id);
                int healEffects = CounterValue(HealEffectCounts, id);
                int spellAccepted = CounterValue(SpellAcceptedCounts, id);
                int spellCompleted = CounterValue(SpellCompletedCounts, id);
                int spellEffects = CounterValue(SpellEffectCounts, id);
                int healTargets = CounterValue(HealTargetResolvedCounts, id);
                int healLegal = CounterValue(HealLegalAllyCounts, id);
                int healSelections = CounterValue(HealSpellSelectionCounts, id);
                int healBridgeRequests = CounterValue(HealBridgeRequestCounts, id);
                int healBridgeAccepted = CounterValue(HealBridgeAcceptedCounts, id);
                int vesselsCreated = CounterValue(VesselCreatedCounts, id);
                int vesselsResolved = CounterValue(VesselResolveCounts, id);
                int healMeEntered = CounterValue(HealMeEnteredCounts, id);
                int statusesApplied = CounterValue(StatusAppliedCounts, id);
                long damageDealt, healingDone, meleeDamage;
                DamageDealtCounts.TryGetValue(id, out damageDealt);
                HealingDoneCounts.TryGetValue(id, out healingDone);
                MeleeDamageCounts.TryGetValue(id, out meleeDamage);
                string abilityUse = PvpProxyStartupPolicy.ProxyAbilityUseAssessment(offensiveSpells, healSpells,
                    healChecks, skillDecisions, spellEntries, spellStarts, damageDealt, healingDone);
                string healLegacyAssessment = PvpProxyStartupPolicy.ZeroHealingAssessment(healSpells > 0 ? 1 : 0,
                    healChecks, spellStarts, healingDone);
                NPC npc = go.GetComponent<NPC>();
                int memmedHeals = npc == null || npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count;
                int meleeAttempts = CounterValue(MeleeAttemptCounts, id);
                string latestAttack; LatestAttackSpellAssessment.TryGetValue(id, out latestAttack);
                string latestHeal; LatestHealAssessment.TryGetValue(id, out latestHeal);
                string attackPipeline = PvpSpellExecutionPolicy.CastPipelineAssessment(spellEntries, attackRequests,
                    attackAccepted, attackCompleted, attackEffects, latestAttack);
                string healPipeline = PvpSpellExecutionPolicy.HealAssessment(healChecks, healLegal, healSelections,
                    healRequests, healAccepted, healEffects, latestHeal);
                PvpDiagnostics.Log("proxy_ability_summary proxy=" + SafeProxyName(npc) +
                    "; profile=" + (profile == null || string.IsNullOrEmpty(profile.ClassName) ? "unknown" : profile.ClassName) +
                    "; offensive_spells=" + offensiveSpells + "; heal_spells=" + healSpells + "; memmed_heals=" + memmedHeals +
                    "; heal_checks=" + healChecks + "; heal_raid_checks=" + healRaidChecks + "; attack_skill_decisions=" + skillDecisions +
                    "; attack_spell_ai_entries=" + spellEntries + "; spell_native_requests=" + nativeRequests +
                    "; attack_spell_requests=" + attackRequests + "; attack_spell_accepted=" + attackAccepted +
                    "; attack_spell_finished=" + attackCompleted + "; attack_spell_effects=" + attackEffects +
                    "; heal_native_requests=" + healRequests + "; heal_native_accepted=" + healAccepted +
                    "; heal_finished=" + healCompleted + "; heal_effects=" + healEffects +
                    "; spell_starts=" + spellStarts + "; spell_native_accepted=" + spellAccepted +
                    "; spell_casts_finished=" + spellCompleted + "; spell_effect_events=" + spellEffects +
                    "; heal_targets_resolved=" + healTargets + "; heal_legal_allies=" + healLegal +
                    "; heal_bridge_requests=" + healBridgeRequests + "; heal_bridge_accepted=" + healBridgeAccepted +
                    "; vessels_created=" + vesselsCreated + "; vessels_resolved=" + vesselsResolved +
                    "; healme_entered=" + healMeEntered + "; statuses_proven_applied=" + statusesApplied +
                    "; melee_attempts=" + meleeAttempts + "; melee_damage=" + meleeDamage +
                    "; damage_dealt=" + damageDealt + "; healing_done=" + healingDone +
                    "; attack_pipeline=" + attackPipeline + "; heal_pipeline=" + healPipeline +
                    "; legacy_ability_use=" + abilityUse + "; legacy_heal_assessment=" + healLegacyAssessment);
            }
        }

        internal static string BalanceHealingAssessment(long healingDone)
        {
            int healCapable = 0, healChecks = 0, spellStarts = 0;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; if (go == null) continue; int id = go.GetInstanceID(); int value;
                if (EligibleHealSpellCounts.TryGetValue(id, out value) && value > 0) healCapable++;
                if (HealCheckCounts.TryGetValue(id, out value)) healChecks += value;
                if (SpellStartCounts.TryGetValue(id, out value)) spellStarts += value;
            }
            return PvpProxyStartupPolicy.ZeroHealingAssessment(healCapable, healChecks, spellStarts, healingDone);
        }

        internal static string BeginTargetingTest()
        {
            if (_clone == null) return "[Erenshor PvP] Spawn a temporary clone first: /epvp spawnclone";
            return PvpCombatContainment.BeginTargetingTest(_clone.GetComponent<NPC>(), _clone.GetComponent<Character>());
        }

        internal static bool PrepareForCountdown(out string reason)
        {
            reason = string.Empty;
            if (_clone == null || TeamClones.Count == 0) { reason = "no_active_team"; return false; }
            PreparationStates.Clear();
            StartupSpellCooldownNormalized.Clear();
            _goReleasedAt = 0f;
            FirstActionStages.Clear();
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject member = TeamClones[i];
                string invariant;
                if (!ValidateProxyStartupInvariant(member, out invariant))
                {
                    reason = "proxy" + (i + 1) + "_startup:" + invariant;
                    return false;
                }
                Character actor = member == null ? null : member.GetComponent<Character>();
                NPC npc = member == null ? null : member.GetComponent<NPC>();
                CastSpell caster = member == null ? null : member.GetComponent<CastSpell>();
                NavMeshAgent nav = member == null ? null : member.GetComponent<NavMeshAgent>();
                SuppressBorrowedDeathRewards(actor);
                string rewardInvariant;
                if (!VerifyRewardSuppression(member, out rewardInvariant))
                {
                    reason = "proxy" + (i + 1) + "_reward:" + rewardInvariant;
                    return false;
                }

                int id = member.GetInstanceID();
                PreparationStates[id] = new ProxyPreparationState();
                PrepareNativeStartProbe(npc);
                try
                {
                    if (npc != null)
                    {
                        npc.NeverAggro = true;
                        npc.CurrentAggroTarget = null;
                        NormalizeBorrowedPreGoAiState(npc);
                    }
                    if (actor != null) actor.enabled = true;
                    if (caster != null) caster.enabled = true;
                    if (nav != null)
                    {
                        nav.enabled = true;
                        if (nav.isOnNavMesh)
                        {
                            string navPrepareReason;
                            PvpPreparationNavRuntime.TryNormalizeHeldAgent(nav, out navPrepareReason);
                        }
                    }
                    if (!member.activeSelf) member.SetActive(true);
                    // NPC.Start belongs to the released combat lifecycle. Keeping this component
                    // disabled here prevents an impossible pre-GO callback from becoming the
                    // countdown gate; GO enables it and Unity invokes Start naturally.
                    if (npc != null) npc.enabled = false;
                }
                catch (Exception ex)
                {
                    reason = "proxy" + (i + 1) + "_prepare_enable:" + ex.GetType().Name;
                    return false;
                }
                PvpDiagnostics.Log("proxy_prepare_begin proxy=" + (i + 1) + "; neverAggro=true; npc_enabled=" +
                    (npc != null && npc.enabled) + "; spells_enabled=" + (caster != null && caster.enabled) +
                    "; nav_enabled=" + (nav != null && nav.enabled) + "; natural_start=deferred_to_go");
            }
            return MaintainPreparationHold(out reason);
        }

        internal static bool MaintainPreparationHold(out string reason)
        {
            return MaintainInertPreparation(false, out reason);
        }

        internal static bool MaintainCountdownHold(out string reason)
        {
            if (!MaintainInertPreparation(false, out reason)) return false;
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject member = TeamClones[i];
                int id = member == null ? 0 : member.GetInstanceID();
                Character actor = member == null ? null : member.GetComponent<Character>();
                LootTable loot = member == null ? null : member.GetComponent<LootTable>();
                RewardSuppressionProof proof = null; bool has = id != 0 && RewardSuppressionProofs.TryGetValue(id, out proof) && proof != null;
                string rewardReason; bool verified = VerifyRewardSuppression(member, out rewardReason);
                LogRewardStateSnapshot(member, "countdown_hold", verified);
                if (RewardHoldCheckLogged.Add(id) || !verified)
                    PvpDiagnostics.Log("reward_hold_check proxyId=" + id + "; actorId=" + (actor == null ? 0 : actor.GetInstanceID()) +
                        "; lootId=" + (loot == null ? 0 : loot.GetInstanceID()) + "; applied=" + (has && proof.Verified) +
                        "; verified=" + verified + "; storedProofId=" + (has ? id : 0) + "; result=" + verified +
                        "; reason=" + (verified ? "ready" : rewardReason));
                if (!verified) { reason = "proxy" + (i + 1) + ":" + rewardReason; return false; }
            }
            string readiness;
            bool allReady = AreAllProxiesReady(out readiness);
            reason = allReady ? "ready" : readiness;
            return allReady;
        }

        private static bool MaintainInertPreparation(bool requireReady, out string reason)
        {
            reason = string.Empty;
            if (TeamClones.Count == 0) { reason = "no_active_team"; return false; }
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject member = TeamClones[i];
                NPC npc = member == null ? null : member.GetComponent<NPC>();
                CastSpell caster = member == null ? null : member.GetComponent<CastSpell>();
                NavMeshAgent nav = member == null ? null : member.GetComponent<NavMeshAgent>();
                if (npc == null) { reason = "proxy" + (i + 1) + "_npc_missing"; return false; }
                try
                {
                    npc.NeverAggro = true;
                    npc.CurrentAggroTarget = null;
                    NormalizeBorrowedPreGoAiState(npc);
                    // Maintain the inert structural surface, but never enable NPC before GO.
                    npc.enabled = false;
                    if (caster != null) caster.enabled = true;
                    if (nav != null)
                    {
                        nav.enabled = true;
                        if (nav.isOnNavMesh)
                        {
                            string navPrepareReason;
                            PvpPreparationNavRuntime.TryNormalizeHeldAgent(nav, out navPrepareReason);
                        }
                    }
                }
                catch (Exception ex)
                {
                    reason = "proxy" + (i + 1) + "_hold_exception:" + ex.GetType().Name;
                    return false;
                }
            }
            string readiness;
            bool allReady = AreAllProxiesReady(out readiness);
            if (requireReady && !allReady) { reason = readiness; return false; }
            reason = allReady ? "ready" : readiness;
            return true;
        }

        internal static bool AreAllProxiesReady(out string reason)
        {
            return EvaluateAllProxiesReady(true, out reason);
        }

        // GO is validation/release only. Missing loop infrastructure may be recovered during the
        // preparation barrier, but a loop lost during countdown must fail closed rather than being
        // created as part of GO.
        internal static bool AreAllProxiesReadyForGo(out string reason)
        {
            return EvaluateAllProxiesReady(false, out reason);
        }

        private static bool EvaluateAllProxiesReady(bool allowLoopRecovery, out string reason)
        {
            reason = string.Empty;
            if (TeamClones.Count == 0) { reason = "no_active_team"; return false; }
            List<string> pending = new List<string>();
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject member = TeamClones[i];
                if (member == null) { pending.Add("proxy" + (i + 1) + ":destroyed"); continue; }
                int id = member.GetInstanceID();
                ProxyPreparationState state;
                if (!PreparationStates.TryGetValue(id, out state))
                {
                    state = new ProxyPreparationState(); PreparationStates[id] = state;
                }
                EvaluatePreparationState(member, state, allowLoopRecovery);
                if (!state.Ready) pending.Add("proxy" + (i + 1) + ":" + state.LastReason);
                else if (!state.ReadyLogged)
                {
                    state.ReadyLogged = true;
                    PvpDiagnostics.Log("proxy_ready proxy=" + (i + 1) + "; " + PreparationFlags(state));
                }
            }
            if (pending.Count == 0)
            {
                reason = "all_proxies_ready";
                return true;
            }
            reason = string.Join("|", pending.ToArray());
            return false;
        }

        internal static string PreparationReadinessSummary()
        {
            List<string> items = new List<string>();
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject member = TeamClones[i];
                if (member == null) { items.Add("proxy" + (i + 1) + "=destroyed"); continue; }
                ProxyPreparationState state;
                if (!PreparationStates.TryGetValue(member.GetInstanceID(), out state))
                { items.Add("proxy" + (i + 1) + "=untracked"); continue; }
                EvaluatePreparationState(member, state, false);
                items.Add("proxy" + (i + 1) + "=" + state.LastReason + "[" + PreparationFlags(state) + "]");
            }
            return string.Join("; ", items.ToArray());
        }

        private static void EvaluatePreparationState(GameObject member, ProxyPreparationState state, bool allowLoopRecovery)
        {
            int id = member.GetInstanceID();
            NPC npc = member.GetComponent<NPC>();
            Character actor = member.GetComponent<Character>();
            Stats stats = actor == null ? null : actor.MyStats;
            CastSpell caster = member.GetComponent<CastSpell>();
            NavMeshAgent nav = member.GetComponent<NavMeshAgent>();
            NativeNavProbe probe; NativeNavProbes.TryGetValue(id, out probe);
            string invariant, reward = string.Empty;
            state.NativeStartCompleted = NativeStartCompleted.Contains(id);
            state.RuntimeInvariantPassed = ValidateProxyStartupInvariant(member, out invariant);
            state.StatsReady = stats != null && stats.Myself == actor && stats.CurrentMaxHP > 0 && stats.CurrentHP > 0;
            state.CharacterReady = actor != null && actor.enabled && actor.MyNPC == npc;
            state.CasterReady = caster != null && caster.enabled && caster.MyChar == actor;
            state.SpellsReady = ClassLoadoutsApplied.Contains(id) && caster != null && caster.KnownSpells != null &&
                npc != null && npc.MyAttackSpells != null && npc.MyHealSpells != null && npc.MyBuffSpells != null && npc.MyCCSpells != null;
            bool nativeNavFaulted = probe != null && probe.Faulted;
            state.NavAgentReady = PvpPreparationNavRuntime.EvaluateAgent(nav, nativeNavFaulted, out state.NavReadinessDetail);
            state.OnNavMesh = nav != null && nav.enabled && nav.isOnNavMesh;
            Transform shell; state.VisualShellReady = VisualShellRoots.TryGetValue(id, out shell) && shell != null;
            // Character.Start can run on the frame after the initial safe snapshot and hydrates
            // native xp/factionMods. It must complete and its one-shot postfix reassertion must
            // be proven before this proxy can enter countdown; NPC.Start remains deferred to GO.
            bool characterStartCompleted = NativeCharacterStartCompleted.Contains(id);
            state.RewardSuppressionReady = characterStartCompleted && VerifyRewardSuppression(member, out reward);
            if (!characterStartCompleted) reward = "character_start_pending:reward_hydration_not_final";
            LogRewardStateSnapshot(member, "ready", state.RewardSuppressionReady);
            state.RewardSuppressionDetail = reward ?? string.Empty;
            state.StartupNpcSpellCooldownNormalized = StartupSpellCooldownNormalized.Contains(id);
            int combatSpellCount; EligibleCombatSpellCounts.TryGetValue(id, out combatSpellCount);
            float npcCd = 0f, attackDelay = 0f, forceCd = 0f; string cooldownReason = string.Empty;
            bool attackSurface = npc != null && PvpNativeSpellExecutionBridge.TryReadAttackSpellState(npc,
                out npcCd, out attackDelay, out forceCd, out cooldownReason);
            state.AttackStartupMaintenanceReady = combatSpellCount <= 0 ||
                (attackSurface && PvpSpellTargetPolicy.StartupAttackDelayReady(attackDelay));
            Character player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself;
            state.InitialTargetCanBeResolved = player != null && player.Alive && player.MyStats != null &&
                player.MyStats.CurrentHP > 0 && !IsTemporaryActor(player);

            string loopDetail = string.Empty;
            if (allowLoopRecovery && state.NativeStartCompleted && npc != null)
                EnsureNativeCombatLoops(npc, "readiness", out loopDetail);
            object navLoop, behaviorLoop;
            state.NativeNavLoopReady = npc != null && TryReadField(npc, "navDo", out navLoop) && navLoop != null;
            state.NativeBehaviorLoopReady = npc != null && TryReadField(npc, "behDo", out behaviorLoop) && behaviorLoop != null;
            int navStarts = CounterValue(ModOwnedNavLoopStarts, id);
            int behaviorStarts = CounterValue(ModOwnedBehaviorLoopStarts, id);
            state.CombatLoopInfrastructureReady = PvpNativeCombatLoopPolicy.InfrastructureReady(state.NativeStartCompleted,
                state.NativeNavLoopReady, state.NativeBehaviorLoopReady, navStarts, behaviorStarts);
            bool navHeld = nav != null && nav.enabled && nav.isOnNavMesh && nav.isStopped && !nav.hasPath;
            state.PreGoLoopBoundaryReady = npc != null && !npc.enabled && npc.NeverAggro &&
                npc.CurrentAggroTarget == null && navHeld;

            state.Ready = state.PreGoLoopBoundaryReady &&
                PvpSpellTargetPolicy.PreGoReadinessComplete(state.RuntimeInvariantPassed,
                    state.StatsReady, state.CharacterReady, state.CasterReady, state.SpellsReady,
                    state.NavAgentReady, state.OnNavMesh, state.VisualShellReady,
                    state.RewardSuppressionReady, state.InitialTargetCanBeResolved);
            state.LastReason = state.Ready ? "ready" : FirstReadinessFailure(state);
        }

        private static string FirstReadinessFailure(ProxyPreparationState state)
        {
            if (!state.PreGoLoopBoundaryReady) return "pre_go_loop_boundary_pending";
            if (!state.RuntimeInvariantPassed) return "runtime_invariant_pending";
            if (!state.StatsReady) return "stats_pending";
            if (!state.CharacterReady) return "character_pending";
            if (!state.CasterReady) return "caster_pending";
            if (!state.SpellsReady) return "spells_pending";
            if (!state.NavAgentReady) return "nav_structural_pending:" + (state.NavReadinessDetail ?? "unknown");
            if (!state.OnNavMesh) return "nav_mesh_pending";
            if (!state.VisualShellReady) return "visual_shell_pending";
            if (!state.RewardSuppressionReady) return "reward_suppression_pending";
            if (!state.InitialTargetCanBeResolved) return "initial_target_pending";
            return "unknown_pending";
        }

        private static string PreparationFlags(ProxyPreparationState state)
        {
            return "nativeStartCompleted=" + state.NativeStartCompleted +
                ",runtimeInvariantPassed=" + state.RuntimeInvariantPassed +
                ",statsReady=" + state.StatsReady + ",characterReady=" + state.CharacterReady +
                ",casterReady=" + state.CasterReady + ",spellsReady=" + state.SpellsReady +
                ",navAgentReady=" + state.NavAgentReady + ",navReadiness={" + (state.NavReadinessDetail ?? string.Empty) + "},onNavMesh=" + state.OnNavMesh +
                ",visualShellReady=" + state.VisualShellReady + ",rewardSuppressionReady=" + state.RewardSuppressionReady +
                ",startupNpcSpellCooldownNormalized=" + state.StartupNpcSpellCooldownNormalized +
                ",attackStartupMaintenanceReady=" + state.AttackStartupMaintenanceReady +
                ",initialTargetCanBeResolved=" + state.InitialTargetCanBeResolved +
                ",nativeNavLoopReady=" + state.NativeNavLoopReady +
                ",nativeBehaviorLoopReady=" + state.NativeBehaviorLoopReady +
                ",combatLoopInfrastructureReady=" + state.CombatLoopInfrastructureReady +
                ",preGoLoopBoundaryReady=" + state.PreGoLoopBoundaryReady;
        }

        internal static string BeginLethalFight()
        {
            if (_clone == null) return "[Erenshor PvP] Spawn a temporary proxy first: /epvp spawnclone";
            string readiness;
            if (!AreAllProxiesReadyForGo(out readiness))
            {
                PvpDiagnostics.Log("combat_start_blocked readiness=" + readiness);
                return "[Erenshor PvP] Lethal fight blocked: opponents are not fully prepared.";
            }
            List<NPC> npcs = new List<NPC>(); List<Character> actors = new List<Character>(); List<CastSpell> spells = new List<CastSpell>();
            foreach (GameObject member in TeamClones)
            {
                if (member == null) continue;
                npcs.Add(member.GetComponent<NPC>()); actors.Add(member.GetComponent<Character>()); spells.Add(member.GetComponent<CastSpell>());
            }
            string result = PvpCombatContainment.BeginLethalFight(npcs, actors, spells);
            if (PvpCombatContainment.LethalFightActive) _despawnAt = float.PositiveInfinity;
            return result;
        }

        internal static void NotifyGoReleased()
        {
            _goReleasedAt = Time.unscaledTime;
            FirstActionStages.Clear();
            PvpDiagnostics.Log("go_release_timestamp t=" + _goReleasedAt.ToString("0.000") + "; prepared=true");
        }

        private static void ObserveFirstActionLatency(int proxyId, string stage)
        {
            if (_goReleasedAt <= 0f || proxyId == 0 || string.IsNullOrEmpty(stage)) return;
            HashSet<int> ids;
            if (!FirstActionStages.TryGetValue(stage, out ids))
            { ids = new HashSet<int>(); FirstActionStages[stage] = ids; }
            if (!ids.Add(proxyId)) return;
            PvpDiagnostics.Log("first_action_latency proxy_id=" + proxyId + "; stage=" + stage +
                "; ms=" + Math.Max(0f, (Time.unscaledTime - _goReleasedAt) * 1000f).ToString("0"));
        }

        // PvP remains lethal if the player stays in the encounter, but opting out by fleeing is
        // a valid MMO-style outcome. It never grants a victory reward and is recorded separately
        // from a defeat so a player does not need to use the debug despawn command to leave.
        internal static string Flee()
        {
            if (!PvpCombatContainment.LethalFightActive)
                return "[Erenshor PvP] You can only flee an active lethal PvP encounter.";
            PvpCombatContainment.End("player_fled");
            DespawnAfterFight("player_fled", null);
            return "[Erenshor PvP] You disengage and escape. No PvP reward was granted.";
        }

        internal static string CloneStatus()
        {
            if (_clone == null) return "[Erenshor PvP] No temporary proxy is active.";
            List<string> health = new List<string>();
            foreach (GameObject member in TeamClones)
            {
                Character actor = member == null ? null : member.GetComponent<Character>();
                health.Add(actor == null || actor.MyStats == null ? "unknown" : actor.MyStats.CurrentHP + "/" + actor.MyStats.CurrentMaxHP);
            }
            Character player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself;
            string playerHp = player == null || player.MyStats == null ? "unknown" : player.MyStats.CurrentHP + "/" + player.MyStats.CurrentMaxHP;
            return "[Erenshor PvP] attackers=" + TeamClones.Count + "; hp=" + string.Join(",", health.ToArray()) + "; player=" + playerHp + "; lethal=" + PvpCombatContainment.LethalFightActive + ".";
        }

        internal static string DiagnosticStatus()
        {
            if (!HasActiveTeam) FindNativeMobTemplate();
            return "template=" + _templateSource + "; active_match=" + (string.IsNullOrEmpty(_activeMatchId) ? "none" : _activeMatchId.Substring(0, Math.Min(8, _activeMatchId.Length))) +
                "; mode=" + _activeMode.ToString().ToLowerInvariant() + "; motive=" + (string.IsNullOrEmpty(_activeMotive) ? "none" : _activeMotive) +
                "; spell_bridge={" + PvpNativeSpellExecutionBridge.SurfaceStatus() + "}; " + CloneStatus().Replace("[Erenshor PvP] ", string.Empty);
        }

        internal static PvpEncounterMode ActiveMode { get { return _activeMode; } }
        internal static string ActiveMotive { get { return _activeMotive ?? string.Empty; } }

        // Structured view of the live proxies for the panel roster. Kept separate from the
        // chat-facing status strings so UI formatting never dictates log wording.
        internal static List<PvpRosterEntry> Roster()
        {
            List<PvpRosterEntry> entries = new List<PvpRosterEntry>();
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i];
                PvpOpponentProfile profile = i < TeamProfiles.Count ? TeamProfiles[i] : null;
                if (go == null && profile == null) continue;
                Character actor = go == null ? null : go.GetComponent<Character>();
                CastSpell caster = go == null ? null : go.GetComponent<CastSpell>();
                int hp = -1, maxHp = -1;
                try { if (actor != null && actor.MyStats != null) { hp = actor.MyStats.CurrentHP; maxHp = actor.MyStats.CurrentMaxHP; } }
                catch { }
                bool alive = false;
                try { alive = actor != null && actor.Alive; } catch { }
                entries.Add(new PvpRosterEntry(
                    profile == null ? "Proxy" : profile.Name,
                    profile == null ? 0 : profile.Level,
                    profile == null ? string.Empty : profile.ClassName,
                    profile == null ? string.Empty : profile.GuildId,
                    PvpTeamMember.RoleFor(profile == null ? null : profile.ClassName),
                    hp, maxHp,
                    caster == null || caster.KnownSpells == null ? 0 : caster.KnownSpells.Count,
                    alive));
            }
            return entries;
        }

        internal static string TeamStatus()
        {
            if (!HasActiveTeam) return "[Erenshor PvP] No active PvP team.";
            List<string> members = new List<string>();
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i]; PvpOpponentProfile profile = i < TeamProfiles.Count ? TeamProfiles[i] : null;
                Character actor = go == null ? null : go.GetComponent<Character>(); CastSpell caster = go == null ? null : go.GetComponent<CastSpell>();
                string hp = actor == null || actor.MyStats == null ? "?" : actor.MyStats.CurrentHP + "/" + actor.MyStats.CurrentMaxHP;
                members.Add((profile == null ? "Proxy" : profile.Name + " L" + profile.Level + " " + profile.ClassName) +
                    " hp=" + hp + " spells=" + (caster == null || caster.KnownSpells == null ? 0 : caster.KnownSpells.Count));
            }
            return "[Erenshor PvP] Active " + _activeMode.ToString().ToLowerInvariant() + " (" + _activeMotive + "): " + string.Join("; ", members.ToArray());
        }

        internal static string VerifyRuntime()
        {
            if (!HasActiveTeam) return "[Erenshor PvP] VERIFY FAIL no active team.";
            List<string> failures = new List<string>();
            if (TeamProfiles.Count != TeamClones.Count) failures.Add("profile_count");
            for (int i = 0; i < TeamClones.Count; i++)
            {
                GameObject go = TeamClones[i];
                if (go == null) { failures.Add("proxy" + (i + 1) + "_missing"); continue; }
                Character actor = go.GetComponent<Character>(); NPC npc = go.GetComponent<NPC>();
                if (actor == null || actor.MyStats == null || actor.MyStats.CurrentMaxHP <= 0) failures.Add("proxy" + (i + 1) + "_hp");
                if (npc == null || npc.SimPlayer || npc.ThisSim != null) failures.Add("proxy" + (i + 1) + "_identity");
                else if (npc.NoSelfHeal) failures.Add("proxy" + (i + 1) + "_self_heal_blocked");
                Transform visual = null;
                foreach (Transform child in go.transform) if (child != null && child.name.StartsWith("PvP_SimVisual_", StringComparison.Ordinal)) { visual = child; break; }
                if (visual == null) failures.Add("proxy" + (i + 1) + "_visual");
                else
                {
                    if (!visual.GetComponentsInChildren<Renderer>(true).Any(x => x != null && x.enabled)) failures.Add("proxy" + (i + 1) + "_visual_hidden");
                    if (!visual.gameObject.activeInHierarchy) failures.Add("proxy" + (i + 1) + "_visual_inactive");
                    if (visual.GetComponentsInChildren<Stats>(true).Any(x => x != null && x.enabled)) failures.Add("proxy" + (i + 1) + "_visual_stats_active");
                    Animator shellAnimator = visual.GetComponentInChildren<Animator>(true);
                    if (shellAnimator == null || !shellAnimator.enabled) failures.Add("proxy" + (i + 1) + "_animator");
                    else if (shellAnimator.runtimeAnimatorController == null) failures.Add("proxy" + (i + 1) + "_animator_controller");
                    else if (shellAnimator.applyRootMotion) failures.Add("proxy" + (i + 1) + "_visual_root_motion");
                    Animator boundAnimator;
                    if (!VisualAnimators.TryGetValue(go.GetInstanceID(), out boundAnimator) || boundAnimator != shellAnimator)
                        failures.Add("proxy" + (i + 1) + "_animator_unbound");
                    else
                    {
                        try { if (actor == null || actor.GetMyAnim() != shellAnimator) failures.Add("proxy" + (i + 1) + "_character_animator_unbound"); }
                        catch { failures.Add("proxy" + (i + 1) + "_character_animator_unreadable"); }
                    }
                    if (visual.localPosition.sqrMagnitude > .01f) failures.Add("proxy" + (i + 1) + "_visual_root_offset");
                }
                PvpOpponentProfile profile = i < TeamProfiles.Count ? TeamProfiles[i] : null;
                if (profile != null && !EquipmentVisualsApplied.Contains(go.GetInstanceID()))
                    failures.Add("proxy" + (i + 1) + "_equipment");
                if (profile != null && !ClassLoadoutsApplied.Contains(go.GetInstanceID())) failures.Add("proxy" + (i + 1) + "_loadout");
                CastSpell caster = go.GetComponent<CastSpell>();
                int expectedSpells;
                if (EligibleCombatSpellCounts.TryGetValue(go.GetInstanceID(), out expectedSpells) && expectedSpells > 0 &&
                    (caster == null || caster.KnownSpells == null || caster.KnownSpells.Count == 0))
                    failures.Add("proxy" + (i + 1) + "_spells");
                int expectedHeals;
                if (npc != null && NativeStartCompleted.Contains(go.GetInstanceID()) &&
                    EligibleHealSpellCounts.TryGetValue(go.GetInstanceID(), out expectedHeals) && expectedHeals > 0 &&
                    !PvpCombatExecutionPolicy.HealingRuntimeConnected(expectedHeals, npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count))
                    failures.Add("proxy" + (i + 1) + "_heal_runtime_disconnected");
            }
            return failures.Count == 0 ? "[Erenshor PvP] VERIFY PASS proxies=" + TeamClones.Count + "; visuals=visible; profiles=matched." :
                "[Erenshor PvP] VERIFY FAIL " + string.Join(",", failures.ToArray()) + ".";
        }

        internal static void DespawnAfterFight(string reason, Character winner)
        {
            // Containment can observe several terminal native callbacks in the same frame. Once
            // the owned proxy collections are empty and the primary clone is gone, cleanup has
            // already completed and must not publish/log a second terminal cleanup.
            if (TeamClones.Count == 0 && _clone == null) return;
            // Keep this terminal path independently safe for callers outside containment.Tick.
            // End is idempotent and clears native aggro, defender-pet targets, and navigation.
            PvpCombatContainment.End(reason);
            GameObject clone = _clone;
            string opponent = _activeProfile == null ? "PvP Proxy" : _activeProfile.Name;
            List<PvpOpponentProfile> completedProfiles = new List<PvpOpponentProfile>(TeamProfiles.Where(x => x != null));
            string completedMatchId = _activeMatchId;
            PvpEncounterMode completedMode = _activeMode; string completedMotive = _activeMotive;
            int proxyCount = TeamClones.Count;
            int animated = VisualAnimators.Count(x => x.Value != null);
            int equipped = EquipmentVisualsApplied.Count;
            int loadouts = ClassLoadoutsApplied.Count;
            int spellTotal = EligibleCombatSpellCounts.Values.Sum();
            PvpDiagnostics.Log("validation_summary match=" + ShortMatch(completedMatchId) + "; outcome=" + (reason ?? "unknown") +
                "; profiles=" + completedProfiles.Count + "; proxies=" + proxyCount + "; animated=" + animated +
                "; equipped=" + equipped + "; loadouts=" + loadouts + "; spells=" + spellTotal);
            _clone = null; _activeProfile = null; _despawnAt = 0f; _activeMatchId = string.Empty; _activeMode = PvpEncounterMode.Arranged; _activeMotive = string.Empty;
            if (TeamClones.Count == 0 && clone == null) return;
            foreach (GameObject member in TeamClones)
            {
                if (member == null) continue;
                StopOwnedNativeCombatLoops(member.GetComponent<NPC>());
                TemporaryActorIds.Remove(member.GetInstanceID());
                VisualAnimators.Remove(member.GetInstanceID());
                VisualShellRoots.Remove(member.GetInstanceID());
                EquipmentVisualsApplied.Remove(member.GetInstanceID());
                ClassLoadoutsApplied.Remove(member.GetInstanceID());
                EligibleCombatSpellCounts.Remove(member.GetInstanceID());
                EligibleHealSpellCounts.Remove(member.GetInstanceID());
                AttackDecisionCounts.Remove(member.GetInstanceID());
                AttackSkillDecisionCounts.Remove(member.GetInstanceID());
                AttackSpellDecisionCounts.Remove(member.GetInstanceID());
                AttackSkillDecisionLogged.Remove(member.GetInstanceID());
                AttackSpellDecisionLogged.Remove(member.GetInstanceID());
                HealCheckCounts.Remove(member.GetInstanceID());
                HealRaidCheckCounts.Remove(member.GetInstanceID());
                SpellStartCounts.Remove(member.GetInstanceID());
                SpellAcceptedCounts.Remove(member.GetInstanceID());
                SpellCompletedCounts.Remove(member.GetInstanceID());
                SpellEffectCounts.Remove(member.GetInstanceID());
                HealTargetResolvedCounts.Remove(member.GetInstanceID());
                CastExecutionProbes.Remove(member.GetInstanceID());
                AttackEntryDetailLogCounts.Remove(member.GetInstanceID());
                SpellRequestDetailLogCounts.Remove(member.GetInstanceID());
                SpellResultDetailLogCounts.Remove(member.GetInstanceID());
                HealDetailLogCounts.Remove(member.GetInstanceID());
                CastFinishDetailLogCounts.Remove(member.GetInstanceID());
                MeleeDamageCounts.Remove(member.GetInstanceID());
                DamageDealtCounts.Remove(member.GetInstanceID());
                HealingDoneCounts.Remove(member.GetInstanceID());
                NativeStartCompleted.Remove(member.GetInstanceID());
                NativeCharacterStartCompleted.Remove(member.GetInstanceID());
                RewardSuppressionProofs.Remove(member.GetInstanceID());
                RewardSuppressionStateLogged.Remove(member.GetInstanceID());
                RewardHoldCheckLogged.Remove(member.GetInstanceID());
                StartupSpellCooldownNormalized.Remove(member.GetInstanceID());
                PreparationStates.Remove(member.GetInstanceID());
                VesselCreatedCounts.Remove(member.GetInstanceID());
                VesselResolveCounts.Remove(member.GetInstanceID());
                HealMeEnteredCounts.Remove(member.GetInstanceID());
                StatusAppliedCounts.Remove(member.GetInstanceID());
                VesselDetailLogCounts.Remove(member.GetInstanceID());
                EffectDetailLogCounts.Remove(member.GetInstanceID());
                NativeNavProbes.Remove(member.GetInstanceID());
                NativeUpdateReached.Remove(member.GetInstanceID());
                CombatSectionReached.Remove(member.GetInstanceID());
                LegalTargetAcquired.Remove(member.GetInstanceID());
                NavPursuitRequested.Remove(member.GetInstanceID());
                MeleeAttemptCounts.Remove(member.GetInstanceID());
                NativeUpdateExceptionLogged.Remove(member.GetInstanceID());
                try { UnityEngine.Object.Destroy(member); } catch { }
            }
            TeamClones.Clear(); TeamProfiles.Clear(); VisualAnimators.Clear(); VisualShellRoots.Clear(); EquipmentVisualsApplied.Clear(); ClassLoadoutsApplied.Clear(); EligibleCombatSpellCounts.Clear(); EligibleHealSpellCounts.Clear(); AttackDecisionCounts.Clear(); AttackSkillDecisionCounts.Clear(); AttackSpellDecisionCounts.Clear(); AttackSkillDecisionLogged.Clear(); AttackSpellDecisionLogged.Clear(); HealCheckCounts.Clear(); HealRaidCheckCounts.Clear(); SpellStartCounts.Clear(); NativeSpellRequestCounts.Clear(); AttackSpellRequestCounts.Clear(); HealNativeRequestCounts.Clear(); SpellContainmentRejectCounts.Clear(); SpellAcceptedCounts.Clear(); AttackSpellAcceptedCounts.Clear(); HealNativeAcceptedCounts.Clear(); SpellRejectedCounts.Clear(); SpellCompletedCounts.Clear(); AttackSpellCompletedCounts.Clear(); HealCompletedCounts.Clear(); SpellEffectCounts.Clear(); AttackSpellEffectCounts.Clear(); HealEffectCounts.Clear(); HealTargetResolvedCounts.Clear(); HealLegalAllyCounts.Clear(); HealSpellSelectionCounts.Clear(); HealBridgeRequestCounts.Clear(); HealBridgeAcceptedCounts.Clear(); LatestAttackSpellAssessment.Clear(); LatestHealAssessment.Clear(); CastExecutionProbes.Clear(); AttackEntryDetailLogCounts.Clear(); SpellRequestDetailLogCounts.Clear(); SpellResultDetailLogCounts.Clear(); HealDetailLogCounts.Clear(); CastFinishDetailLogCounts.Clear(); MeleeDamageCounts.Clear(); DamageDealtCounts.Clear(); HealingDoneCounts.Clear(); LastSpellStartFrame.Clear(); LastSpellAcceptedFrame.Clear(); NativeStartCompleted.Clear(); NativeCharacterStartCompleted.Clear(); RewardSuppressionProofs.Clear(); RewardSuppressionStateLogged.Clear(); RewardHoldCheckLogged.Clear(); StartupSpellCooldownNormalized.Clear(); PreparationStates.Clear(); VesselCreatedCounts.Clear(); VesselResolveCounts.Clear(); HealMeEnteredCounts.Clear(); StatusAppliedCounts.Clear(); VesselDetailLogCounts.Clear(); EffectDetailLogCounts.Clear(); LastVesselCreatedAt.Clear(); FirstActionStages.Clear(); _goReleasedAt = 0f; NativeNavProbes.Clear(); ModOwnedNavLoops.Clear(); ModOwnedBehaviorLoops.Clear(); ModOwnedNavLoopStarts.Clear(); ModOwnedBehaviorLoopStarts.Clear(); CombatLoopRecoveryLogged.Clear(); CombatBranchTraceLogged.Clear(); NonRaidBehaviorReached.Clear(); PreGoBehaviorBlockedLogged.Clear(); PreGoNavBlockedLogged.Clear(); NativeUpdateReached.Clear(); CombatSectionReached.Clear(); LegalTargetAcquired.Clear(); NavPursuitRequested.Clear(); MeleeAttemptCounts.Clear(); NativeUpdateExceptionLogged.Clear(); _nativeSimComparisonLogged = false; _healthyNativeCastLogged = false; _activeMeleeProxyId = 0; _activeMeleeDepth = 0; DestroyTemporarySpells();
            PvpDiagnostics.Log("validation_cleanup match=" + ShortMatch(completedMatchId) +
                "; proxy_collections=" + TeamClones.Count + "; profile_collections=" + TeamProfiles.Count +
                "; spell_collections=" + TemporarySpells.Count + "; destroy_scheduled=" + proxyCount);
            string completedClassification = ErenshorPvpApi.ClassifyOutcome(reason);
            bool competitiveResult = PvpCombatStartupPolicy.ShouldRecordCompetitiveResult(reason);
            if (competitiveResult)
            {
                if (ErenshorPvpApi.TryRecordResult(completedMatchId, opponent, reason ?? "unknown", completedMode.ToString().ToLowerInvariant(), completedClassification))
                    ErenshorPvpEvents.Publish(new PvpSemanticEvent("pvp_match_completed", completedMatchId, opponent,
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, completedMode.ToString().ToLowerInvariant(), reason ?? "unknown", completedClassification));
                PvpRecordService.Complete(opponent, reason ?? "unknown", completedMode);
            }
            else
            {
                // Technical startup failure is operational evidence, not a PvP result. Publish a
                // transient cancellation event for integrations but deliberately do not append it to
                // RecentResults/PvpRecordService or grant any reward/stat/history credit.
                ErenshorPvpEvents.Publish(new PvpSemanticEvent("pvp_cancelled", completedMatchId, opponent,
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, completedMode.ToString().ToLowerInvariant(),
                    reason ?? "unknown", "invalid"));
                PvpDiagnostics.Log("technical_failure_result match=" + ShortMatch(completedMatchId) +
                    "; winner=none; xp=0; gold=0; win_credit=false; history_credit=false");
                winner = null;
            }
            Debug.Log("[Erenshor PvP] lethal_result=" + (reason ?? "unknown") + "; mode=" + completedMode.ToString().ToLowerInvariant() + "; motive=" + completedMotive + "; winner=" + (winner == null ? "none" : "player"));
            try
            {
                if (string.Equals(reason, "proxy_death", StringComparison.Ordinal)) UpdateSocialLog.LogAdd("[PvP] " + opponent + (completedMode == PvpEncounterMode.Ambush ? ": you held your ground. We're done here." : ": gf, well played."), "lightblue");
                else if (string.Equals(reason, "player_death", StringComparison.Ordinal)) UpdateSocialLog.LogAdd("[PvP] " + opponent + (completedMode == PvpEncounterMode.Ambush && completedMotive == "camp_claim" ? ": camp's ours now." : ": gf. See you out there."), "lightblue");
                else if (string.Equals(reason, "retreat", StringComparison.Ordinal)) UpdateSocialLog.LogAdd("[PvP] " + opponent + " disengages and escapes.", "lightblue");
                else if (string.Equals(reason, "player_fled", StringComparison.Ordinal)) UpdateSocialLog.LogAdd("[PvP] You disengage from " + opponent + " and escape.", "lightblue");
            }
            catch { }
            if (PvpCombatStartupPolicy.CanGrantVictoryReward(reason, winner != null))
            {
                string result = PvpRewardService.GrantVictory(completedMatchId, winner, completedProfiles);
                PvpDiagnostics.Log("reward_result match=" + ShortMatch(completedMatchId) + "; " + result.Replace("[Erenshor PvP] ", string.Empty));
                try { UpdateSocialLog.LogAdd(result, "lightblue"); } catch { }
            }
            PvpController.EncounterCleaned();
        }

        private static string ShortMatch(string matchId)
        {
            return string.IsNullOrEmpty(matchId) ? "none" : matchId.Substring(0, Math.Min(8, matchId.Length));
        }

        private static bool TryReadIntField(object instance, string name, out int value)
        {
            value = 0;
            try
            {
                FieldInfo field = instance == null ? null : instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) return false;
                object raw = field.GetValue(instance);
                if (raw is int) { value = (int)raw; return true; }
                return false;
            }
            catch { return false; }
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static bool TryReadCharacterXp(Character actor, out int value)
        {
            value = 0;
            try
            {
                FieldInfo field = actor == null ? null : (AccessTools.Field(typeof(Character), "xp") ?? FindInstanceField(actor.GetType(), "xp"));
                object raw = field == null ? null : field.GetValue(actor);
                if (raw is int) { value = (int)raw; return true; }
            }
            catch { }
            return false;
        }

        private static bool TrySetCharacterXp(Character actor, int value)
        {
            try
            {
                FieldInfo field = actor == null ? null : (AccessTools.Field(typeof(Character), "xp") ?? FindInstanceField(actor.GetType(), "xp"));
                if (field == null || field.FieldType != typeof(int)) return false;
                field.SetValue(actor, value);
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadField(object instance, string name, out object value)
        {
            value = null;
            try
            {
                FieldInfo field = instance == null ? null : instance.GetType().GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) return false;
                value = field.GetValue(instance);
                return true;
            }
            catch { return false; }
        }

        private static T ReadField<T>(object instance, string name) where T : class
        {
            object value;
            return TryReadField(instance, name, out value) ? value as T : null;
        }

        private static bool IsNativeSpellRuntimeCasting(NPC npc)
        {
            object spells;
            if (!TryReadField(npc, "MySpells", out spells) || spells == null) return false;
            try
            {
                MethodInfo method = spells.GetType().GetMethod("isCasting",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) return false;
                object result = method.Invoke(spells, null);
                return result is bool && (bool)result;
            }
            catch { return false; }
        }

        private static int NativeKnownSpellCount(NPC npc)
        {
            object spells;
            object known;
            if (!TryReadField(npc, "MySpells", out spells) || spells == null ||
                !TryReadField(spells, "KnownSpells", out known) || known == null) return 0;
            try
            {
                System.Collections.ICollection collection = known as System.Collections.ICollection;
                return collection == null ? 0 : collection.Count;
            }
            catch { return 0; }
        }

        private static void TrySetField(object instance, string name, object value)
        {
            try
            {
                FieldInfo field = instance == null ? null : instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) field.SetValue(instance, value);
            }
            catch { }
        }

        private static GameObject FindNativeMobTemplate()
        {
            try
            {
                foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
                {
                    if (npc == null || npc.gameObject == null || !npc.gameObject.activeInHierarchy) continue;
                    if (!IsEligibleCombatTemplate(npc)) continue;
                    Character actor = npc.GetComponent<Character>();
                    if (actor != null && actor.MyStats != null && actor.Alive)
                    {
                        _templateSource = "live:" + npc.gameObject.name;
                        PvpDiagnostics.Log("template_runtime_state template=" + _templateSource + "; " + DescribeNativeMaintenanceState(npc));
                        return npc.gameObject;
                    }
                }
            }
            catch { }
            // A zone does not need to contain a currently spawned creature. Unity keeps loaded
            // prefab assets available through Resources; cloning one avoids mutating any scene NPC.
            try
            {
                foreach (NPC npc in Resources.FindObjectsOfTypeAll<NPC>())
                {
                    if (npc == null || npc.gameObject == null || !IsEligibleCombatTemplate(npc)) continue;
                    if (npc.gameObject.scene.IsValid()) continue;
                    Character actor = npc.GetComponent<Character>();
                    if (actor == null || actor.MyStats == null || npc.GetComponent<NavMeshAgent>() == null) continue;
                    string name = npc.gameObject.name ?? string.Empty;
                    if (name.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    _templateSource = "resource:" + name;
                    return npc.gameObject;
                }
            }
            catch { }
            _templateSource = "unavailable";
            return null;
        }

        private static string DescribeNativeMaintenanceState(NPC npc)
        {
            object value;
            bool self = TryReadField(npc, "Myself", out value) && value != null;
            bool stats = TryReadField(npc, "MyStats", out value) && value != null;
            bool nav = TryReadField(npc, "MyNav", out value) && value != null;
            bool caster = TryReadField(npc, "MySpells", out value) && value != null;
            bool flash = TryReadField(npc, "NameFlash", out value) && value != null;
            bool raid = TryReadField(npc, "MyRaidSlot", out value) && value != null;
            // namePlateTxt is the field NPC.HandleNameTag actually dereferences. It is reported
            // separately from nameFlash because a template can satisfy one and not the other, which is
            // exactly how the broken 5v5 logged nameFlash=True while every proxy still threw.
            bool plateText = TryReadField(npc, "NamePlateTxt", out value) && value != null;
            bool plateObject = TryReadField(npc, "NamePlateObject", out value) && value != null;
            return "npcMyself=" + self + "; npcMyStats=" + stats + "; npcMyNav=" + nav +
                "; npcMySpells=" + caster + "; nameFlash=" + flash + "; raidSlot=" + raid +
                "; namePlateTxt=" + plateText + "; namePlateObject=" + plateObject;
        }

        // Companion bodies have very different scale, animation, and stat lifecycles from normal
        // hostile NPCs. In particular, borrowing a player's pet produced oversized inactive PvP
        // shells. Template selection must only use ordinary native combat NPCs.
        private static bool IsEligibleCombatTemplate(NPC npc)
        {
            if (npc == null || npc.SimPlayer || npc.ThisSim != null || IsTemporaryNpc(npc)) return false;
            Character actor = npc.GetComponent<Character>();
            // Current Character.DamageMe/MagicDamageMe/BleedDamageMe all reject protected
            // actors. A temporary lethal proxy must never inherit a protected donor surface.
            if (actor == null || actor.Invulnerable || actor.isVendor) return false;
            if (npc.SummonedByPlayer || npc.MiningNode || npc.TreasureChest || npc.NeverAggro) return false;
            string name = (npc.NPCName ?? string.Empty) + " " + (npc.gameObject == null ? string.Empty : npc.gameObject.name ?? string.Empty);
            return name.IndexOf("pet", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("companion", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("familiar", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("minion", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("summon", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static void ApplyProfileLoadout(NPC npc, PvpOpponentProfile profile)
        {
            if (npc == null) return;
            // A borrowed body must never retain its source creature's spell/skill identity.
            npc.MyBuffSpells = npc.MyBuffSpells ?? new List<Spell>(); npc.MyBuffSpells.Clear();
            npc.MyAttackSpells = npc.MyAttackSpells ?? new List<Spell>(); npc.MyAttackSpells.Clear();
            npc.MyHealSpells = npc.MyHealSpells ?? new List<Spell>(); npc.MyHealSpells.Clear();
            npc.MyCCSpells = npc.MyCCSpells ?? new List<Spell>(); npc.MyCCSpells.Clear();
            npc.MyTauntSpell = npc.MyTauntSpell ?? new List<Spell>(); npc.MyTauntSpell.Clear();
            npc.GroupHeals = npc.GroupHeals ?? new List<Spell>(); npc.GroupHeals.Clear();
            npc.MyAttackSkills = npc.MyAttackSkills ?? new List<Skill>(); npc.MyAttackSkills.Clear();
            npc.MyPetSpell = null; npc.MyHOTSpell = null; npc.GroupHOTSpell = null;
            npc.MyEmitVitaeSpell = null; npc.AETaunt = null; npc.NPCProcOnHit = null;
            npc.NoSelfHeal = false;
            npc.GuildName = profile == null ? string.Empty : profile.GuildId;
            int level = profile == null ? 1 : Math.Max(1, profile.Level);
            // The previous level..2x-level range became trivial chip damage after player armor.
            // An equal-level opponent should remain dangerous even when its spell AI pauses.
            npc.BaseAtkDmg = Math.Max(3, level * 3);
            npc.MinAtkDmg = Math.Max(2, level * 2);
            npc.DamageRange = new Vector2(Math.Max(2, level * 2), Math.Max(4, level * 4));
            ApplyClassSpells(npc, profile);
        }

        private static List<Item> ResolveEffectiveEquipment(PvpOpponentProfile profile, out bool fallback)
        {
            fallback = false;
            List<Item> items = new List<Item>();
            if (profile == null || GameData.ItemDB == null) return items;
            try
            {
                for (int i = 0; i < profile.EquippedItemIds.Count; i++)
                {
                    string id = profile.EquippedItemIds[i];
                    Item item = string.IsNullOrWhiteSpace(id) ? null : GameData.ItemDB.GetItemByID(id);
                    if (item != null) items.Add(item);
                }
                if (items.Count == 0)
                {
                    fallback = true;
                    items.AddRange(PvpFallbackEquipment.Build(profile, ClassFor(profile.ClassName)).Where(x => x != null));
                }
            }
            catch { }
            return items;
        }

        // The visible Sim shell and the synthetic combat root previously had two unrelated weapon
        // identities: ModularParts rendered the profile's bow/wand while NPC kept MHBow/MHWand from
        // the borrowed creature. Current Assembly-CSharp uses those private booleans in Combat,
        // UpdateNav, CheckAttackRange and PerformMeleeHit, so presentation and native attack mode must
        // be derived from the SAME profile equipment. This never installs items into a persistent
        // inventory; it only sets the encounter-local NPC mode flags already used by native combat.
        private static void ConfigureProfileWeaponRuntime(NPC npc, PvpOpponentProfile profile, bool refreshAttackRanges)
        {
            if (npc == null || profile == null) return;
            try
            {
                bool fallback;
                List<Item> items = ResolveEffectiveEquipment(profile, out fallback);
                Item main = SelectNativeMainWeapon(items);
                bool mainBow = main != null && main.IsBow;
                bool mainWand = main != null && main.IsWand;
                bool anyBow = items.Any(x => x != null && x.IsBow);
                bool anyWand = items.Any(x => x != null && x.IsWand);
                TrySetField(npc, "MHBow", mainBow);
                TrySetField(npc, "MHWand", mainWand);
                TrySetField(npc, "BowEquipped", anyBow);
                TrySetField(npc, "WandEquipped", anyWand);
                if (refreshAttackRanges)
                {
                    MethodInfo setAttackRanges = AccessTools.Method(typeof(NPC), "SetAttackRanges");
                    if (setAttackRanges != null) setAttackRanges.Invoke(npc, null);
                }
                object mhBow, mhWand;
                TryReadField(npc, "MHBow", out mhBow); TryReadField(npc, "MHWand", out mhWand);
                PvpDiagnostics.Log("weapon_runtime profile=" + profile.Name + "; source=" +
                    (fallback ? "level_class_fallback" : "saved_profile") + "; main_bow=" + mhBow +
                    "; main_wand=" + mhWand + "; attack_ranges_refreshed=" + refreshAttackRanges);
            }
            catch (Exception ex)
            {
                PvpDiagnostics.Warning("weapon_runtime_failed profile=" + profile.Name + "; error=" + ex.GetType().Name);
            }
        }

        private static Item SelectNativeMainWeapon(List<Item> items)
        {
            if (items == null) return null;
            // Native presentation treats a two-handed item as the main-hand identity even
            // when a profile record or fallback classifier exposed it through a flexible slot.
            Item twoHanded = items.FirstOrDefault(IsTwoHandedWeapon);
            if (twoHanded != null) return twoHanded;
            Item primary = items.FirstOrDefault(x => x != null && x.RequiredSlot == Item.SlotType.Primary);
            if (primary != null) return primary;
            return items.FirstOrDefault(x => x != null && x.RequiredSlot == Item.SlotType.PrimaryOrSecondary);
        }

        private static bool IsTwoHandedWeapon(Item item)
        {
            if (item == null) return false;
            return item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                item.ThisWeaponType == Item.WeaponType.TwoHandBow;
        }

        private static void LogPostStartAbilityRuntime(NPC npc, PvpOpponentProfile profile)
        {
            if (npc == null || npc.gameObject == null) return;
            try
            {
                int configured = npc.MyHealSpells == null ? 0 : npc.MyHealSpells.Count;
                int memmed = npc.MemmedHealSpells == null ? 0 : npc.MemmedHealSpells.Count;
                int known = NativeKnownSpellCount(npc);
                bool connected = PvpCombatExecutionPolicy.HealingRuntimeConnected(configured, memmed);
                PvpDiagnostics.Log("post_start_ability_runtime proxy=" + SafeProxyName(npc) + "; profile=" +
                    (profile == null ? "none" : profile.Name) + "; heal_configured=" + configured +
                    "; heal_memmed=" + memmed + "; known_spells=" + known + "; heal_runtime_connected=" + connected);
            }
            catch { }
        }

        private static void ReassertVisualAnimatorBinding(NPC npc, Character actor)
        {
            if (npc == null || actor == null || npc.gameObject == null) return;
            Animator shell;
            if (!VisualAnimators.TryGetValue(npc.gameObject.GetInstanceID(), out shell) || shell == null) return;
            try
            {
                Animator before = actor.GetMyAnim();
                bool changed = before != shell;
                shell.enabled = true;
                shell.applyRootMotion = false;
                if (changed) actor.AssignAnim(shell);
                PvpDiagnostics.Log("visual_animator_post_start proxy=" + SafeProxyName(npc) + "; binding=" +
                    (actor.GetMyAnim() == shell ? "shell" : "other") + "; rebound=" + changed +
                    "; controller=" + (shell.runtimeAnimatorController != null) + "; root_motion=" + shell.applyRootMotion);
            }
            catch { }
        }

        private static void AttachSimVisualShell(GameObject combatRoot, PvpOpponentProfile profile)
        {
            if (combatRoot == null || profile == null || GameData.SimMngr == null) return;
            try
            {
                // Hide the borrowed creature body before adding the Sim visual child.
                foreach (Renderer renderer in combatRoot.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;

                _suppressPersistentLoad = true;
                GameObject visual;
                GameObject stagingRoot = new GameObject("PvP_VisualStaging_" + profile.Name);
                stagingRoot.transform.position = combatRoot.transform.position;
                stagingRoot.transform.rotation = combatRoot.transform.rotation;
                GameObject visualTemplate = FindSimVisualTemplate(profile);
                if (visualTemplate == null) { UnityEngine.Object.Destroy(stagingRoot); _suppressPersistentLoad = false; return; }
                bool namedTemplate = visualTemplate != GameData.SimMngr.BlankSPTemplate;
                try { visual = UnityEngine.Object.Instantiate(visualTemplate, combatRoot.transform.position, combatRoot.transform.rotation); }
                finally { _suppressPersistentLoad = false; }
                if (visual == null) return;
                visual.name = "PvP_SimVisual_" + profile.Name;
                // Materialize the native Sim presentation while active in hierarchy. The combat
                // root remains inactive so its NPC Start/combat lifecycle stays deferred to GO.
                visual.transform.SetParent(stagingRoot.transform, true);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (NPC component in visual.GetComponentsInChildren<NPC>(true)) component.enabled = false;
                foreach (Character component in visual.GetComponentsInChildren<Character>(true)) component.enabled = false;
                // The visual clone is not a real actor. Leaving its Stats MonoBehaviour running
                // calls CheckAuras against missing Sim state every frame and destabilizes combat.
                foreach (Stats component in visual.GetComponentsInChildren<Stats>(true)) component.enabled = false;
                foreach (SimPlayer component in visual.GetComponentsInChildren<SimPlayer>(true)) component.enabled = false;
                foreach (NavMeshAgent component in visual.GetComponentsInChildren<NavMeshAgent>(true)) component.enabled = false;
                foreach (CastSpell component in visual.GetComponentsInChildren<CastSpell>(true)) component.enabled = false;
                foreach (LootTable component in visual.GetComponentsInChildren<LootTable>(true)) component.enabled = false;
                foreach (SimPlayerLanguage component in visual.GetComponentsInChildren<SimPlayerLanguage>(true)) component.enabled = false;
                // Off-map and blank Sim templates may be pooled inactive. All gameplay-bearing
                // components are disabled above, so activating the render shell is now safe and
                // is required for its Animator to evaluate.
                visual.SetActive(true);
                // A named template may have had its Animator disabled while it was pooled or
                // inactive. The shell is render-only, so its Animator is safe to restore.
                foreach (Animator component in visual.GetComponentsInChildren<Animator>(true)) component.enabled = true;

                // Erenshor's NPC movement, attack, casting, hit, and death paths all write to
                // Character.MyAnim. Redirect those native animation commands from the hidden
                // borrowed creature to the visible Sim shell.
                Animator visualAnimator = visual.GetComponentInChildren<Animator>(true);
                Character combatActor = combatRoot.GetComponent<Character>();
                if (visualAnimator != null && combatActor != null)
                {
                    visualAnimator.enabled = true;
                    // This object is presentation only. Native PvP navigation moves the combat root;
                    // allowing the child Animator to apply root motion creates a second transform owner
                    // and can leave the visible shell behind (or drift it away) while the invisible root
                    // continues to path. The child follows its parent and receives animation state only.
                    visualAnimator.applyRootMotion = false;
                    combatActor.AssignAnim(visualAnimator);
                    VisualAnimators[combatRoot.GetInstanceID()] = visualAnimator;
                    PvpDiagnostics.Log("visual_animator_bound profile=" + profile.Name + "; animator=" + visualAnimator.name +
                        "; root_motion=false");
                }
                else Debug.LogWarning("[Erenshor PvP] visual_animator_missing profile=" + profile.Name);

                ModularParts parts = FindPrimaryModularParts(visual);
                if (parts != null)
                {
                    InitializeVisualSim(parts, profile);
                    parts.Gender = profile.Gender;
                    parts.HairName = profile.HairName;
                    parts.HairCol = profile.HairColor;
                    parts.SkinCol = profile.SkinColor;
                    try { parts.DoSkinColor(); } catch { }
                    try { parts.UpdateHair(profile.HairName, profile.HairColor); } catch { }
                    PvpDiagnostics.Log("visual_lifecycle_checkpoint phase=before_native_visuals; combatRootActiveSelf=" + combatRoot.activeSelf +
                        "; combatRootActiveInHierarchy=" + combatRoot.activeInHierarchy + "; shellActiveSelf=" + visual.activeSelf +
                        "; shellActiveInHierarchy=" + visual.activeInHierarchy + "; partsActiveInHierarchy=" + parts.gameObject.activeInHierarchy +
                        "; stagingRoot=true; npcEnabled=" + (combatRoot.GetComponent<NPC>() != null && combatRoot.GetComponent<NPC>().enabled) +
                        "; neverAggro=" + (combatRoot.GetComponent<NPC>() != null && combatRoot.GetComponent<NPC>().NeverAggro));
                    if (ApplyEquipmentVisuals(parts, profile)) EquipmentVisualsApplied.Add(combatRoot.GetInstanceID());
                    PvpDiagnostics.Log("visual_lifecycle_checkpoint phase=after_native_visuals; shellActiveInHierarchy=" + visual.activeInHierarchy +
                        "; equipmentSucceeded=" + EquipmentVisualsApplied.Contains(combatRoot.GetInstanceID()));
                }
                // Native UpdateSlot is a coroutine and yields between non-player modular
                // transforms. Keep the shell under its active staging root through that bounded
                // native activation window; immediate reparenting under the inactive combat root
                // paused the coroutine before armor nodes could be enabled.
                VisualShellRoots[combatRoot.GetInstanceID()] = visual.transform;
                parts.StartCoroutine(FinalizeVisualStaging(parts, visual, combatRoot, stagingRoot, profile));
                PvpDiagnostics.Log("visual_shell profile=" + profile.Describe() + "; source=" + (namedTemplate ? "named_sim_template" : "blank_sim_template") + "; equipment_ids=" + profile.EquippedItemIds.Count);
            }
            catch (Exception ex)
            {
                _suppressPersistentLoad = false;
                Debug.LogWarning("[Erenshor PvP] visual_shell_failed=" + ex.GetType().Name);
            }
        }

        private static ModularParts FindPrimaryModularParts(GameObject visual)
        {
            if (visual == null) return null;
            // Prefer the native Sim's serialized renderer reference over a hierarchy guess.
            SimPlayer visualSim = visual.GetComponentInChildren<SimPlayer>(true);
            if (visualSim != null && visualSim.Mods != null) return visualSim.Mods;
            ModularParts fallback = null;
            foreach (ModularParts candidate in visual.GetComponentsInChildren<ModularParts>(true))
            {
                if (candidate == null) continue;
                if (fallback == null) fallback = candidate;
                Transform parent = candidate.transform.parent;
                if (parent != null && parent.GetComponent<SimPlayer>() != null) return candidate;
            }
            return fallback;
        }

        // UpdateSimPlayerVisuals reads appearance/cosmetic fields from the ModularParts object's
        // immediate SimPlayer parent. Persistent Sim loading is intentionally suppressed for PvP,
        // so initialize only this temporary visual copy with safe non-null values.
        private static void InitializeVisualSim(ModularParts parts, PvpOpponentProfile profile)
        {
            if (parts == null || profile == null || parts.transform.parent == null) return;
            SimPlayer sim = parts.transform.parent.GetComponent<SimPlayer>();
            if (sim == null) return;
            int count = GameData.SimMngr == null || GameData.SimMngr.Sims == null ? 0 : GameData.SimMngr.Sims.Count;
            sim.myIndex = count <= 0 ? 0 : Math.Max(0, Math.Min(count - 1, profile.SimIndex));
            sim.HairName = profile.HairName;
            sim.HairColor = profile.HairColorIndex;
            sim.SkinColor = profile.SkinColorIndex;
            if (sim.SimCosHead == null) sim.SimCosHead = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosChest == null) sim.SimCosChest = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosBack == null) sim.SimCosBack = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosArm == null) sim.SimCosArm = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosFoot == null) sim.SimCosFoot = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosWrist == null) sim.SimCosWrist = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosLeg == null) sim.SimCosLeg = new ItemSaveData(string.Empty, 1);
            if (sim.SimCosHand == null) sim.SimCosHand = new ItemSaveData(string.Empty, 1);
        }

        private static bool ApplyEquipmentVisuals(ModularParts parts, PvpOpponentProfile profile)
        {
            if (parts == null || profile == null || GameData.ItemDB == null) return false;
            try
            {
                // Current ModularParts.UpdateSimPlayerVisuals delegates weapons to SpawnWeapons,
                // which writes cosmetic weapon damage through parts.transform.parent.GetComponent<NPC>().
                // Our player-like render shell is owned by SimPlayer (never the borrowed combat NPC),
                // so provide a disabled shell-local compatibility host before invoking that native
                // renderer. It is not registered as a temporary combat actor and never owns stats,
                // inventory, loot, rewards, targeting, or native update loops.
                NPC visualWeaponHost = EnsureVisualWeaponHost(parts);
                Inventory visualInventory = EnsureVisualPresentationInventory(parts, profile);
                LogEquipmentVisualContext(parts, profile, visualWeaponHost, effectiveCount: profile.EquippedItemIds.Count);
                PvpDiagnostics.Log("transform_names_preconditions profile=" + profile.Name + "; inventoryOnParent=" + (visualInventory != null) + "; modularParOnParts=" + (parts.GetComponent<ModularPar>() != null) + "; playerModularParent=" + (GameData.PlayerModularParent != null) + "; result=" + (visualInventory == null ? "missing:Inventory" : "ready"));
                if (visualInventory == null) return false;
                if (visualWeaponHost == null)
                {
                    PvpDiagnostics.Warning("equipment_slot_failed profile=" + profile.Name + "; slot=weapons; itemId=multiple; stage=weapon_host; reason=missing_shell_parent");
                    return false;
                }
                // UpdateSimPlayerVisuals consumes ModularParts' native gender-scoped transform
                // cache. PvP shells disable the cloned SimPlayer, so the normal Sim lifecycle
                // does not rebuild that cache after the profile gender is applied. Rebuild it
                // at the same native boundary before asking UpdateSimPlayerVisuals to materialize
                // armor or weapons; this preserves the native Male/Female branch selection.
                PvpDiagnostics.Log("native_visual_stage profile=" + profile.Name + "; entered=GetTransformNames; branch=" +
                    (visualInventory.isMale ? "Male" : "Female"));
                MethodInfo getTransformNames = AccessTools.Method(typeof(ModularParts), "GetTransformNames");
                if (getTransformNames == null) throw new MissingMethodException("ModularParts.GetTransformNames");
                getTransformNames.Invoke(parts, null);
                List<SimInvSlot> armor = new List<SimInvSlot>();
                // SpawnWeapons dereferences both hands. Native callers supply explicit Empty
                // slots for unused hands rather than null.
                SimInvSlot main = new SimInvSlot(Item.SlotType.Primary) { MyItem = GameData.PlayerInv.Empty, Quant = 1 };
                SimInvSlot off = new SimInvSlot(Item.SlotType.Secondary) { MyItem = GameData.PlayerInv.Empty, Quant = 1 };
                bool fallback;
                List<Item> effective = ResolveEffectiveEquipment(profile, out fallback);
                LogEquipmentInput(profile, effective);
                Item observedLeg = effective.FirstOrDefault(x => x != null && x.Id == "8454409");
                int valid = 0, armorCount = 0, weaponCount = 0, failed = 0;
                foreach (Item item in effective)
                {
                    if (item == null) continue;
                    valid++;
                    SimInvSlot slot = new SimInvSlot(item.RequiredSlot) { MyItem = item, Quant = 1 };
                    if (item.RequiredSlot == Item.SlotType.Primary) { main = slot; weaponCount++; }
                    else if (item.RequiredSlot == Item.SlotType.Secondary) { off = slot; weaponCount++; }
                    else if (item.RequiredSlot == Item.SlotType.PrimaryOrSecondary)
                    {
                        if (main.MyItem == GameData.PlayerInv.Empty) main = slot;
                        else off = slot;
                        weaponCount++;
                    }
                    else { armor.Add(slot); if (IsVisibleBodySlot(item.RequiredSlot)) armorCount++; }
                }
                Item nativeMain = SelectNativeMainWeapon(effective);
                if (IsTwoHandedWeapon(nativeMain))
                {
                    // SpawnWeapons receives explicit native Empty slots. A two-handed item must
                    // occupy only the primary presentation slot; leaving a secondary item in the
                    // pair produces the observed one-handed/off-hand attachment semantics.
                    main = new SimInvSlot(Item.SlotType.Primary) { MyItem = nativeMain, Quant = 1 };
                    off = new SimInvSlot(Item.SlotType.Secondary) { MyItem = GameData.PlayerInv.Empty, Quant = 1 };
                    PvpDiagnostics.Log("weapon_presentation_normalized profile=" + profile.Name +
                        "; itemId=" + nativeMain.Id + "; weaponType=" + nativeMain.ThisWeaponType +
                        "; main=primary; off=empty; nativeTwoHanded=true");
                }
                PvpDiagnostics.Log("native_visual_stage profile=" + profile.Name + "; entered=UpdateSimPlayerVisuals; lastStage=armor_input_complete");
                if (observedLeg != null) PvpItemDbVisualObserver.LogShellBeforeMaterialization(parts, profile, visualInventory, observedLeg);
                parts.UpdateSimPlayerVisuals(armor, main, off);
                if (observedLeg != null) PvpItemDbVisualObserver.LogShellAfterMaterialization(parts, profile, visualInventory, observedLeg);
                PvpDiagnostics.Log("transform_names_result profile=" + profile.Name + "; completed=true");
                int bodyVisible = LogBodySlotVisuals(parts, armor);
                string mainState = EquipmentVisualState(parts.transform.parent, main.MyItem);
                string offState = EquipmentVisualState(parts.transform.parent, off.MyItem);
                PvpDiagnostics.Log("armor_visual_checkpoint profile=" + profile.Name + "; requested=" + armorCount + "; resolved=" + armor.Count + "; activeRenderers=" + ActiveRendererCount(parts.gameObject) + "; visibleArmorProof=" + (bodyVisible > 0));
                PvpDiagnostics.Log("weapon_visual_result profile=" + profile.Name + "; hand=main; " + mainState);
                PvpDiagnostics.Log("weapon_visual_result profile=" + profile.Name + "; hand=off; " + offState);
                PvpDiagnostics.Log("equipment_visual_result profile=" + profile.Name + "; requested=" + profile.EquippedItemIds.Count +
                    "; resolved=" + valid + "; bodySlotsRequested=" + armorCount + "; bodySlotsVisible=" + bodyVisible +
                    "; bodySlotsMissing=" + (armorCount - bodyVisible) + "; mainWeaponVisible=" + VisualStateVisible(mainState) +
                    "; offWeaponVisible=" + VisualStateVisible(offState) + "; failedCount=" + failed + "; source=" +
                    (fallback ? "level_class_fallback" : "saved_profile"));
                return valid > 0;
            }
            catch (Exception ex) { LogEquipmentVisualException(parts, profile, ex); return false; }
        }

        private static void LogEquipmentVisualContext(ModularParts parts, PvpOpponentProfile profile, NPC expected, int effectiveCount)
        {
            if (parts == null || profile == null || !EquipmentForensicsLogged.Add(parts.GetInstanceID())) return;
            Transform parent = parts.transform.parent; GameObject shell = parts.transform.root == null ? null : parts.transform.root.gameObject;
            NPC actual = parent == null ? null : parent.GetComponent<NPC>();
            PvpDiagnostics.Log("equipment_visual_context profile=" + profile.Name + "; shellId=" + Id(shell) + "; shellName=" + Name(shell) +
                "; modularPartsId=" + parts.GetInstanceID() + "; modularPartsObject=" + Name(parts.gameObject) +
                "; modularPartsParentId=" + Id(parent == null ? null : parent.gameObject) + "; modularPartsParentName=" + Name(parent == null ? null : parent.gameObject) +
                "; shellParentId=" + Id(shell == null || shell.transform.parent == null ? null : shell.transform.parent.gameObject) + "; shellParentName=" + Name(shell == null || shell.transform.parent == null ? null : shell.transform.parent.gameObject) +
                "; compatibilityNpcId=" + Id(expected == null ? null : expected.gameObject) + "; compatibilityNpcEnabled=" + (expected != null && expected.enabled) +
                "; npcOnPartsObject=" + (parts.GetComponent<NPC>() != null) + "; npcOnPartsParent=" + (actual != null) + "; npcOnShell=" + (shell != null && shell.GetComponent<NPC>() != null) +
                "; simPlayerOnPartsObject=" + (parts.GetComponent<SimPlayer>() != null) + "; simPlayerOnPartsParent=" + (parent != null && parent.GetComponent<SimPlayer>() != null) +
                "; animatorId=" + Id(parts.GetComponentInChildren<Animator>(true) == null ? null : parts.GetComponentInChildren<Animator>(true).gameObject) + "; itemCount=" + effectiveCount);
            PvpDiagnostics.Log("weapon_host_check partsParent=" + Name(parent == null ? null : parent.gameObject) + "; expectedNpc=" + Id(expected == null ? null : expected.gameObject) + "; actualNpc=" + Id(actual == null ? null : actual.gameObject) + "; sameReference=" + object.ReferenceEquals(expected, actual) + "; result=" + (actual == null ? "missing" : (object.ReferenceEquals(expected, actual) ? "pass" : "different")));
        }

        private static void LogEquipmentInput(PvpOpponentProfile profile, List<Item> items)
        {
            if (profile == null) return;
            string slots = string.Join(",", items.Where(x => x != null).Select(x => x.RequiredSlot + ":" + x.Id + ":resolved"));
            PvpDiagnostics.Log("equipment_input profile=" + profile.Name + "; slots=" + slots);
        }

        private static void LogEquipmentVisualException(ModularParts parts, PvpOpponentProfile profile, Exception ex)
        {
            string stack = ex == null || string.IsNullOrEmpty(ex.StackTrace) ? "unavailable" : ex.StackTrace.Replace("\\", "/");
            int path = stack.IndexOf(":/"); if (path >= 0) stack = stack.Substring(0, path) + "[path-stripped]";
            PvpDiagnostics.Warning("equipment_visual_exception profile=" + (profile == null ? "unknown" : profile.Name) + "; type=" + (ex == null ? "unknown" : ex.GetType().Name) + "; method=ModularParts.UpdateSimPlayerVisuals; armorBeforeFailure=" + ActiveRendererCount(parts == null ? null : parts.gameObject) + "; stack=" + stack);
        }

        private static int ActiveRendererCount(GameObject go) { return go == null ? 0 : go.GetComponentsInChildren<Renderer>(true).Count(x => x != null && x.enabled && x.gameObject.activeInHierarchy); }
        private static int Id(GameObject go) { return go == null ? 0 : go.GetInstanceID(); }
        private static string Name(GameObject go) { return go == null ? "null" : go.name; }

        private static NPC EnsureVisualWeaponHost(ModularParts parts)
        {
            try
            {
                Transform parent = parts == null ? null : parts.transform.parent;
                if (parent == null || parent.GetComponent<SimPlayer>() == null) return null;
                NPC host = parent.GetComponent<NPC>();
                if (host == null) host = parent.gameObject.AddComponent<NPC>();
                host.enabled = false;
                return host;
            }
            catch { return null; }
        }

        private static Inventory EnsureVisualPresentationInventory(ModularParts parts, PvpOpponentProfile profile)
        {
            try
            {
                Transform parent = parts == null ? null : parts.transform.parent;
                if (parent == null || parent.GetComponent<SimPlayer>() == null) return null;
                Inventory inventory = parent.GetComponent<Inventory>();
                if (inventory == null) inventory = parent.gameObject.AddComponent<Inventory>();
                inventory.enabled = false;
                // GetTransformNames uses only this presentation flag to select the matching
                // native modular source; this shell-local component never participates in player
                // inventory, combat stat calculation, or persistence.
                inventory.isMale = profile != null && string.Equals(profile.Gender, "Male", StringComparison.OrdinalIgnoreCase);
                return inventory;
            }
            catch { return null; }
        }

        private static System.Collections.IEnumerator FinalizeVisualStaging(ModularParts parts, GameObject visual,
            GameObject combatRoot, GameObject stagingRoot, PvpOpponentProfile profile)
        {
            bool ignoredFallback;
            List<Item> finalItems = ResolveEffectiveEquipment(profile, out ignoredFallback);
            List<Item> pending = finalItems.Where(x => x != null && IsVisibleBodySlot(x.RequiredSlot)).ToList();
            const int maxVisualFrames = 120;
            int elapsedFrames = 0;
            // Current native UpdateSlot yields once per candidate transform, so completion varies
            // by slot/hierarchy. Keep staging active until each requested native visual has an
            // active selected node, never a guessed fixed frame count.
            while (pending.Count > 0 && elapsedFrames < maxVisualFrames && visual != null && visual.activeInHierarchy)
            {
                pending.RemoveAll(item => NativeVisualNodeActive(parts, item));
                if (pending.Count == 0) break;
                elapsedFrames++;
                yield return null;
            }
            if (visual != null && combatRoot != null)
            {
                if (pending.Count > 0)
                    PvpDiagnostics.Warning("armor_materialization_timeout profile=" + (profile == null ? "unknown" : profile.Name) +
                        "; completed=" + (finalItems.Count - pending.Count) + "/" + finalItems.Count + "; pending=" + string.Join("|", pending.Select(x => x.RequiredSlot + ":" + x.Id)) + "; elapsedFrames=" + elapsedFrames);
                LogFinalArmorSlots(parts, profile, finalItems, "before_reparent");
                PvpDiagnostics.Log("armor_activation_trace profile=" + (profile == null ? "unknown" : profile.Name) +
                    "; phase=end_of_frame; activeRenderers=" + ActiveRendererCount(visual));
                visual.transform.SetParent(combatRoot.transform, true);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                PvpDiagnostics.Log("armor_activation_trace profile=" + (profile == null ? "unknown" : profile.Name) +
                    "; phase=after_reparent; activeInHierarchy=" + visual.activeInHierarchy + "; activeRenderers=" + ActiveRendererCount(visual));
                int afterVisible = LogFinalArmorSlots(parts, profile, finalItems, "after_reparent");
                int requested = finalItems.Count(x => x != null && IsVisibleBodySlot(x.RequiredSlot));
                PvpDiagnostics.Log("armor_visual_final profile=" + (profile == null ? "unknown" : profile.Name) +
                    "; requestedVisibleSlots=" + requested + "; visibleSlots=" + afterVisible + "; missingSlots=" + (requested - afterVisible));
            }
            if (stagingRoot != null) UnityEngine.Object.Destroy(stagingRoot);
        }

        private static bool NativeVisualNodeActive(ModularParts parts, Item item)
        {
            if (parts == null || item == null || string.IsNullOrEmpty(item.EquipmentToActivate)) return true;
            Transform node = FindNativePresentationNode(parts, item.EquipmentToActivate, true);
            return node != null;
        }

        private static Transform FindNativePresentationNode(ModularParts parts, string activation, bool requireVisibleRenderer)
        {
            if (parts == null || string.IsNullOrEmpty(activation)) return null;
            Transform scope = parts.transform.parent == null ? parts.transform : parts.transform.parent;
            Inventory inventory = scope.GetComponent<Inventory>();
            string branchName = inventory != null && inventory.isMale ? "Male_Parts" : "Female_Parts";
            Transform branch = scope.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x != null && string.Equals(x.name, branchName, StringComparison.Ordinal));
            IEnumerable<Transform> candidates = (branch == null ? scope : branch).GetComponentsInChildren<Transform>(true)
                .Where(x => x != null && string.Equals(x.name, activation, StringComparison.Ordinal));
            foreach (Transform candidate in candidates)
            {
                if (!candidate.gameObject.activeInHierarchy) continue;
                if (!requireVisibleRenderer) return candidate;
                if (candidate.GetComponentsInChildren<Renderer>(true).Any(x => x != null && x.enabled && x.gameObject.activeInHierarchy)) return candidate;
            }
            return null;
        }

        private static string EquipmentPresentationSummary(ModularParts parts, SimInvSlot main, SimInvSlot off)
        {
            try
            {
                Transform scope = parts == null ? null : (parts.transform.parent == null ? parts.transform : parts.transform.parent);
                Item mainItem = main == null ? null : main.MyItem;
                Item offItem = off == null ? null : off.MyItem;
                string mainState = EquipmentVisualState(scope, mainItem);
                string offState = EquipmentVisualState(scope, offItem);
                return "main_visual=" + mainState + "; off_visual=" + offState;
            }
            catch { return "weapon_visual_state=unavailable"; }
        }

        private static string EquipmentVisualState(Transform scope, Item item)
        {
            if (item == null || item == GameData.PlayerInv.Empty) return "empty";
            string activation = item.EquipmentToActivate ?? string.Empty;
            if (scope == null || string.IsNullOrEmpty(activation)) return "item:" + item.Id + ",activation=unknown";
            ModularParts parts = scope.GetComponentInChildren<ModularParts>(true);
            Transform found = FindNativePresentationNode(parts, activation, false);
            int renderers = found == null ? 0 : found.GetComponentsInChildren<Renderer>(true).Count(x => x != null);
            int enabled = found == null ? 0 : found.GetComponentsInChildren<Renderer>(true).Count(x => x != null && x.enabled && x.gameObject.activeInHierarchy);
            bool visible = found != null && found.gameObject.activeInHierarchy && enabled > 0;
            return "itemId=" + item.Id + "; nodeFound=" + (found != null) + "; parentBone=" + (found == null || found.parent == null ? "none" : found.parent.name) +
                "; activeSelf=" + (found != null && found.gameObject.activeSelf) + "; activeInHierarchy=" + (found != null && found.gameObject.activeInHierarchy) +
                "; rendererCount=" + renderers + "; enabledRenderers=" + enabled + "; visible=" + visible + "; reason=" + (visible ? "ready" : "native_node_not_visible");
        }

        private static bool IsVisibleBodySlot(Item.SlotType slot)
        {
            string name = slot.ToString();
            return name == "Head" || name == "Chest" || name == "Leg" || name == "Foot" || name == "Hand" || name == "Arm" || name == "Back" || name == "Bracer" || name == "Waist";
        }

        private static int LogBodySlotVisuals(ModularParts parts, List<SimInvSlot> armor)
        {
            int visible = 0; Transform scope = parts == null ? null : parts.transform.parent;
            foreach (SimInvSlot slot in armor.Where(x => x != null && x.MyItem != null && IsVisibleBodySlot(x.MyItem.RequiredSlot)))
            {
                string state = EquipmentVisualState(scope, slot.MyItem);
                if (VisualStateVisible(state)) visible++;
                PvpDiagnostics.Log("equipment_slot_visual slot=" + slot.MyItem.RequiredSlot + "; requested=True; " + state);
            }
            return visible;
        }

        private static bool VisualStateVisible(string state) { return !string.IsNullOrEmpty(state) && state.IndexOf("visible=True", StringComparison.Ordinal) >= 0; }

        private static int LogFinalArmorSlots(ModularParts parts, PvpOpponentProfile profile, List<Item> items, string phase)
        {
            int visible = 0; Transform scope = parts == null ? null : parts.transform.parent;
            LogActiveVisualRenderers(scope, profile, phase);
            foreach (Item item in items.Where(x => x != null && IsVisibleBodySlot(x.RequiredSlot)))
            {
                string activation = item.EquipmentToActivate ?? string.Empty;
                Transform[] candidates = scope == null ? new Transform[0] : scope.GetComponentsInChildren<Transform>(true).Where(x => x != null && x.name == activation).ToArray();
                for (int index = 0; index < candidates.Length; index++)
                {
                    Transform candidate = candidates[index];
                    Renderer[] candidateRenderers = candidate.GetComponentsInChildren<Renderer>(true);
                    string meshes = string.Join(",", candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x != null && x.sharedMesh != null).Select(x => x.sharedMesh.name));
                    PvpDiagnostics.Log("equipment_transform_candidate slot=" + item.RequiredSlot + "; itemId=" + item.Id + "; expectedNode=" + activation +
                        "; candidateIndex=" + index + "; instanceId=" + candidate.GetInstanceID() + "; fullHierarchyPath=" + TransformPath(candidate) +
                        "; activeSelf=" + candidate.gameObject.activeSelf + "; activeInHierarchy=" + candidate.gameObject.activeInHierarchy +
                        "; rendererCount=" + candidateRenderers.Length + "; enabledRenderers=" + candidateRenderers.Count(x => x != null && x.enabled && x.gameObject.activeInHierarchy) +
                        "; meshNames=" + meshes + "; parentName=" + (candidate.parent == null ? "none" : candidate.parent.name));
                }
                Transform node = candidates.FirstOrDefault(x => x.GetComponentsInChildren<Renderer>(true).Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) ?? candidates.FirstOrDefault();
                int renderers = node == null ? 0 : node.GetComponentsInChildren<Renderer>(true).Count(x => x != null);
                int enabled = node == null ? 0 : node.GetComponentsInChildren<Renderer>(true).Count(x => x != null && x.enabled && x.gameObject.activeInHierarchy);
                bool mesh = node != null && node.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(x => x != null && x.sharedMesh != null);
                bool isVisible = node != null && node.gameObject.activeInHierarchy && enabled > 0 && mesh;
                if (isVisible) visible++;
                PvpDiagnostics.Log("equipment_slot_post_activation profile=" + (profile == null ? "unknown" : profile.Name) +
                    "; phase=" + phase + "; slot=" + item.RequiredSlot + "; itemId=" + item.Id + "; nodeFound=" + (node != null) +
                    "; nodeName=" + (node == null ? "none" : node.name) + "; activeSelf=" + (node != null && node.gameObject.activeSelf) +
                    "; activeInHierarchy=" + (node != null && node.gameObject.activeInHierarchy) + "; rendererCount=" + renderers +
                    "; enabledRenderers=" + enabled + "; meshPresent=" + mesh + "; visible=" + isVisible);
            }
            return visible;
        }

        private static void LogActiveVisualRenderers(Transform scope, PvpOpponentProfile profile, string phase)
        {
            if (scope == null) return;
            foreach (Renderer renderer in scope.GetComponentsInChildren<Renderer>(true).Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy))
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                string mesh = skinned == null || skinned.sharedMesh == null ? "none" : skinned.sharedMesh.name;
                PvpDiagnostics.Log("active_visual_renderer profile=" + (profile == null ? "unknown" : profile.Name) + "; phase=" + phase +
                    "; instanceId=" + renderer.GetInstanceID() + "; objectName=" + renderer.gameObject.name + "; hierarchyPath=" + TransformPath(renderer.transform) +
                    "; rendererType=" + renderer.GetType().Name + "; enabled=" + renderer.enabled + "; mesh=" + mesh + "; materialCount=" + renderer.sharedMaterials.Length);
            }
        }

        private static string TransformPath(Transform transform)
        {
            if (transform == null) return "none";
            List<string> names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse(); return string.Join("/", names.ToArray());
        }

        private static void ApplyClassSpells(NPC npc, PvpOpponentProfile profile)
        {
            if (npc == null || profile == null || GameData.SpellDatabase == null || GameData.SpellDatabase.SpellDatabase == null) return;
            try
            {
                Class profileClass = ClassFor(profile.ClassName);
                if (profileClass == null) return;
                HashSet<string> acquired = new HashSet<string>(profile.AcquiredSpellIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                HashSet<string> admitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int categorized = 0;
                foreach (Spell source in GameData.SpellDatabase.SpellDatabase)
                {
                    if (source == null || !source.SimUsable || source.RequiredLevel > profile.Level || !PvpSpellSemantics.IsSafeTemporaryCombatAbility(source)) continue;
                    if (source.UsedBy == null || !source.UsedBy.Contains(profileClass)) continue;
                    if (source.SimsNeedHelpToLearn && !acquired.Contains(source.Id) && !acquired.Contains(source.SpellName)) continue;
                    string key = string.IsNullOrEmpty(source.Id) ? source.SpellName : source.Id;
                    if (string.IsNullOrEmpty(key) || !admitted.Add(key)) continue;
                    Spell spell = UnityEngine.Object.Instantiate(source); spell.CanHitPlayers = true; TemporarySpells.Add(spell);
                    PvpSpellSemanticSnapshot semantic = PvpSpellSemantics.Inspect(spell);
                    if (semantic.DirectHpHeal || semantic.HealOverTime)
                    {
                        // Only actual HP-heal/HoT payloads belong in native heal decisions. Generic
                        // beneficial utilities remain buffs and never inflate healing telemetry.
                        npc.MyHealSpells.Add(spell); categorized++;
                    }
                    else if (semantic.Category == PvpSpellSemanticCategory.CrowdControl)
                    { npc.MyCCSpells.Add(spell); categorized++; }
                    else if (semantic.Category == PvpSpellSemanticCategory.BeneficialBuff ||
                        semantic.Category == PvpSpellSemanticCategory.SelfUtility)
                    { npc.MyBuffSpells.Add(spell); categorized++; }
                    else if (semantic.Harmful || semantic.Category == PvpSpellSemanticCategory.Area)
                    { npc.MyAttackSpells.Add(spell); categorized++; }
                }
                EligibleCombatSpellCounts[npc.gameObject.GetInstanceID()] = categorized;
                EligibleHealSpellCounts[npc.gameObject.GetInstanceID()] = npc.MyHealSpells.Count;
                CastSpell caster = npc.GetComponent<CastSpell>();
                if (caster != null)
                {
                    if (caster.KnownSpells == null) caster.KnownSpells = new List<Spell>();
                    caster.KnownSpells.Clear();
                    caster.KnownSpells.AddRange(npc.MyAttackSpells);
                    caster.KnownSpells.AddRange(npc.MyHealSpells);
                    caster.KnownSpells.AddRange(npc.MyCCSpells);
                    caster.KnownSpells.AddRange(npc.MyBuffSpells);
                }
                if (caster == null) throw new InvalidOperationException("Proxy template has no CastSpell component.");
                ClassLoadoutsApplied.Add(npc.gameObject.GetInstanceID());
                PvpDiagnostics.Log("class_loadout profile=" + profile.Name + "; attack=" + npc.MyAttackSpells.Count +
                    "; heal=" + npc.MyHealSpells.Count + "; cc=" + npc.MyCCSpells.Count + "; buff=" + npc.MyBuffSpells.Count);
            }
            catch (Exception ex) { Debug.LogWarning("[Erenshor PvP] class_loadout_failed=" + ex.GetType().Name); }
        }

        private static Spell FindSpell(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName) || GameData.SpellDatabase == null) return null;
            try
            {
                Spell exact = GameData.SpellDatabase.GetSpellByID(idOrName);
                if (exact != null) return exact;
            }
            catch { }
            try
            {
                if (GameData.SpellDatabase.SpellDatabase == null) return null;
                return GameData.SpellDatabase.SpellDatabase.FirstOrDefault(x => x != null &&
                    (string.Equals(x.Id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.SpellName, idOrName, StringComparison.OrdinalIgnoreCase)));
            }
            catch { return null; }
        }

        private static void DestroyTemporarySpells()
        {
            foreach (Spell spell in TemporarySpells) try { if (spell != null) UnityEngine.Object.Destroy(spell); } catch { }
            TemporarySpells.Clear();
        }

        private static GameObject FindSimVisualTemplate(PvpOpponentProfile profile)
        {
            try
            {
                if (GameData.SimMngr.ActualSims != null)
                {
                    foreach (GameObject candidate in GameData.SimMngr.ActualSims)
                    {
                        if (candidate != null && string.Equals(candidate.name, profile.Name, StringComparison.OrdinalIgnoreCase)) return candidate;
                    }
                }
            }
            catch { }
            return GameData.SimMngr.BlankSPTemplate;
        }

        // BlankSPTemplate is a visual/template object, not an encounter-ready character and
        // arrives with 1/1 HP. Copy only live numeric combat values from the local player; no
        // inventory, spells, persistent Sim state, or save-backed identity crosses this boundary.
        private static void ConfigureCombatStats(Character player, Character proxy, PvpOpponentProfile profile)
        {
            try
            {
                if (player == null || proxy == null || player.MyStats == null || proxy.MyStats == null) return;
                Stats source = player.MyStats;
                Stats target = proxy.MyStats;
                target.Level = profile == null ? Math.Max(1, source.Level) : profile.Level;
                target.CharacterClass = ClassFor(profile == null ? null : profile.ClassName) ?? source.CharacterClass;
                float levelScale = profile == null || source.Level <= 0 ? 1f : Mathf.Clamp((float)profile.Level / source.Level, .65f, 1.5f);
                PvpCombatRole role = PvpTeamMember.RoleFor(profile == null ? null : profile.ClassName);
                float healthRole = role == PvpCombatRole.Vanguard ? 1.20f : role == PvpCombatRole.Support ? 1.05f : .92f;
                float armorRole = role == PvpCombatRole.Vanguard ? 1.18f : role == PvpCombatRole.Striker ? .92f : .85f;
                target.BaseHP = Math.Max(1, Mathf.RoundToInt(source.BaseHP * levelScale * healthRole));
                target.BaseAC = Math.Max(0, Mathf.RoundToInt(source.BaseAC * levelScale * armorRole));
                target.BaseMana = Math.Max(0, source.BaseMana);
                target.BaseStr = Math.Max(1, source.BaseStr); target.BaseEnd = Math.Max(1, source.BaseEnd);
                target.BaseDex = Math.Max(1, source.BaseDex); target.BaseAgi = Math.Max(1, source.BaseAgi);
                target.BaseInt = Math.Max(1, source.BaseInt); target.BaseWis = Math.Max(1, source.BaseWis); target.BaseCha = Math.Max(1, source.BaseCha);
                target.CurrentMaxHP = Math.Max(1, Mathf.RoundToInt(source.CurrentMaxHP * levelScale * healthRole)); target.CurrentHP = target.CurrentMaxHP;
                target.CurrentAC = Math.Max(0, Mathf.RoundToInt(source.CurrentAC * levelScale * armorRole));
                target.CurrentMana = Math.Max(0, Mathf.RoundToInt(source.CurrentMana * levelScale));
                target.StopAllRegen = true;
                target.BaseMHAtkDelay = Math.Max(.1f, source.BaseMHAtkDelay);
                target.CurrentMHAtkDelay = Math.Max(.1f, source.CurrentMHAtkDelay);
                target.BaseOHAtkDelay = Math.Max(.1f, source.BaseOHAtkDelay);
                target.CurrentOHAtkDelay = Math.Max(.1f, source.CurrentOHAtkDelay);
            }
            catch { }
        }

        private static Class ClassFor(string className)
        {
            try
            {
                if (GameData.ClassDB == null) return null;
                string value = (className ?? string.Empty).ToLowerInvariant();
                if (value.Contains("arcan")) return GameData.ClassDB.Arcanist;
                if (value.Contains("paladin")) return GameData.ClassDB.Paladin;
                if (value.Contains("duel") || value.Contains("windblade")) return GameData.ClassDB.Duelist;
                if (value.Contains("druid")) return GameData.ClassDB.Druid;
                if (value.Contains("storm")) return GameData.ClassDB.Stormcaller;
                if (value.Contains("reaver")) return GameData.ClassDB.Reaver;
            }
            catch { }
            return null;
        }
    }

    // SimPlayer.Awake calls LoadAllSimData immediately. During the one-frame clone construction
    // window, suppress that method so it cannot read/write a persistent roster entry by myIndex.
    [HarmonyPatch(typeof(SimPlayer), "LoadAllSimData")]
    internal static class PvpTemporaryCloneLoadPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() { return !PvpTemporaryCloneFactory.SuppressPersistentLoad; }
    }

    // 0.5.9 forensic recovery: the cloned NPC must receive its own native Start lifecycle. The
    // Prefix scopes diagnostics to registered PvP roots; the Postfix immediately reasserts PvP-owned
    // identity/loadout/reward state. Vanilla NPCs are untouched.
    [HarmonyPatch(typeof(Character), "Start")]
    internal static class PvpTemporaryCharacterStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            PvpTemporaryCloneFactory.ObserveNativeCharacterStartCompleted(__instance);
        }
    }

    [HarmonyPatch(typeof(NPC), "Start")]
    internal static class PvpTemporaryNpcStartPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, out bool __state)
        {
            return PvpTemporaryCloneFactory.AllowNativeNpcStart(__instance, out __state);
        }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance, bool __state)
        {
            PvpTemporaryCloneFactory.ObserveNativeNpcStartCompleted(__instance, __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(NPC __instance, bool __state, Exception __exception)
        {
            if (__state && __exception != null && PvpTemporaryCloneFactory.IsTemporaryNpc(__instance))
            {
                PvpDiagnostics.Log("proxy_native_start action=native; completion=failed; proxy=" +
                    (__instance == null ? "null" : __instance.gameObject.name) + "; error=" + __exception.GetType().Name);
                PvpTemporaryCloneFactory.ObserveNativeNpcStartFailed(__instance, __exception);
            }
            // Diagnostics only: return the original exception unchanged. Never suppress a native
            // NPC.Start failure for a proxy or ordinary game NPC.
            return __exception;
        }
    }

    // The native maintenance method is never changed for vanilla NPCs. It only prevents an
    // already-identified invalid temporary proxy from throwing every frame while the factory
    // closes and destroys that match on its next Tick.
    [HarmonyPatch(typeof(NPC), "HandleMaintenaceAndCounters")]
    internal static class PvpTemporaryNpcMaintenancePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            return PvpTemporaryCloneFactory.AllowNativeMaintenance(__instance);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(NPC __instance, Exception __exception)
        {
            if (__exception != null && PvpTemporaryCloneFactory.IsTemporaryNpc(__instance))
                PvpDiagnostics.Warning("native_npc_exception method=NPC.HandleMaintenaceAndCounters; proxy=" +
                    (__instance == null || __instance.gameObject == null ? "null" : __instance.gameObject.name) +
                    "; error=" + __exception.GetType().Name);
            // Diagnostic only. Never suppress an exception that escaped the proven temporary-proxy
            // maintenance precondition; live acceptance must prove the current native path stays clean.
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "HighPriorityNavUpdate")]
    internal static class PvpNativeHighPriorityNavGatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        { return PvpTemporaryCloneFactory.AllowNativeNavStep(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "UpdateNav")]
    internal static class PvpNativeUpdateNavHealthPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, out bool __state)
        {
            __state = PvpTemporaryCloneFactory.AllowNativeNavStep(__instance);
            if (__state) PvpTemporaryCloneFactory.ObserveNativeNavEntered(__instance);
            return __state;
        }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance, bool __state)
        { if (__state) PvpTemporaryCloneFactory.ObserveNativeNavCompleted(__instance); }

        [HarmonyFinalizer]
        private static Exception Finalizer(NPC __instance, bool __state, Exception __exception)
        { return __state ? PvpTemporaryCloneFactory.ObserveNativeNavException(__instance, __exception) : __exception; }
    }

    [HarmonyPatch(typeof(NPC), "DoStances")]
    internal static class PvpNativeStanceBehaviorGatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        { return PvpTemporaryCloneFactory.AllowNativeBehaviorStep(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "DoNonRaidBehavior")]
    internal static class PvpNativeNonRaidBehaviorGatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            bool allow = PvpTemporaryCloneFactory.AllowNativeBehaviorStep(__instance);
            if (allow) PvpTemporaryCloneFactory.ObserveNonRaidBehavior(__instance);
            return allow;
        }
    }

    [HarmonyPatch(typeof(NPC), "DoRaidBehavior")]
    internal static class PvpNativeRaidBehaviorGatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        { return PvpTemporaryCloneFactory.AllowNativeBehaviorStep(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "Update")]
    internal static class PvpNativeUpdateTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance)
        { PvpTemporaryCloneFactory.ObserveNativeUpdate(__instance); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance)
        { PvpTemporaryCloneFactory.ObserveNativeUpdateCompleted(__instance); }

        [HarmonyFinalizer]
        private static Exception Finalizer(NPC __instance, Exception __exception)
        { return PvpTemporaryCloneFactory.ObserveNativeUpdateException(__instance, __exception); }
    }

    [HarmonyPatch(typeof(NPC), "Combat")]
    internal static class PvpCombatSectionTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance) { PvpTemporaryCloneFactory.ObserveCombatSection(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
    internal static class PvpMeleeAttemptTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance) { PvpTemporaryCloneFactory.ObserveMeleeAttempt(__instance); }

        [HarmonyFinalizer]
        private static Exception Finalizer(NPC __instance, Exception __exception)
        { return PvpTemporaryCloneFactory.FinishMeleeAttempt(__instance, __exception); }
    }

    [HarmonyPatch(typeof(NPC), "DoAttackSkill")]
    internal static class PvpAttackSkillDecisionTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance) { PvpTemporaryCloneFactory.ObserveAttackDecision(__instance, false); }
    }

    [HarmonyPatch(typeof(NPC), "DoAttackSpell")]
    internal static class PvpAttackDecisionTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, out PvpTemporaryCloneFactory.AttackSpellEntryState __state)
        { __state = PvpTemporaryCloneFactory.BeginAttackSpellEntry(__instance); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance, PvpTemporaryCloneFactory.AttackSpellEntryState __state)
        { PvpTemporaryCloneFactory.FinishAttackSpellEntry(__instance, __state); }
    }

    [HarmonyPatch(typeof(NPC), "CheckHeals")]
    internal static class PvpHealCheckTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, out int __state)
        { __state = PvpTemporaryCloneFactory.BeginHealCheck(__instance, false); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance, int __state)
        { PvpTemporaryCloneFactory.FinishHealCheck(__instance, false, __state); }
    }

    [HarmonyPatch(typeof(NPC), "CheckHealsRaid")]
    internal static class PvpHealRaidCheckTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, out int __state)
        { __state = PvpTemporaryCloneFactory.BeginHealCheck(__instance, true); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance, int __state)
        { PvpTemporaryCloneFactory.FinishHealCheck(__instance, true, __state); }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats) })]
    internal static class PvpSpellStartTelemetry2Patch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, true); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }
    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    internal static class PvpSpellStartTelemetry3Patch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, true); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }
    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool) })]
    internal static class PvpSpellStartTelemetry4Patch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, true); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }
    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool), typeof(float) })]
    internal static class PvpSpellStartTelemetry5Patch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, true); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpellFromProc", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool), typeof(float) })]
    internal static class PvpSpellProcTelemetryPatch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, false); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpellNoAnim", new Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    internal static class PvpSpellNoAnimTelemetryPatch
    {
        [HarmonyPrefix] private static bool Prefix(CastSpell __instance, Spell __0, ref Stats __1, out bool __state)
        { __state = PvpTemporaryCloneFactory.ObserveAndAllowSpellStart(__instance, __0, ref __1, false); return __state; }
        [HarmonyPostfix] private static void Postfix(CastSpell __instance, Spell __0, Stats __1, bool __state, bool __result)
        { if (__state) PvpTemporaryCloneFactory.ObserveSpellStartResult(__instance, __0, __1, __result); }
    }

    [HarmonyPatch(typeof(Character), "DoWorldEventCredit")]
    internal static class PvpTemporaryCloneWorldEventCreditPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance)
        {
            return !PvpTemporaryCloneFactory.IsTemporaryActor(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "DoDeath")]
    internal static class PvpTemporaryCloneDeathRewardPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            PvpTemporaryCloneFactory.SuppressBorrowedDeathRewards(__instance);
        }
    }
}
