$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
function Find-Csc {
  foreach ($p in @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe")) { if (Test-Path $p) { return $p } }
  throw "csc.exe not found."
}
$csc=Find-Csc; $out=Join-Path $env:TEMP "ErenshorPvP.UiPolicyTests.exe"
& $csc /nologo /target:exe ("/out:{0}" -f $out) `
  (Join-Path $Root "src\PvpUiGeometry.cs") `
  (Join-Path $Root "src\SuiteLauncherPolicy.cs") `
  (Join-Path $Root "src\PvpHubPresentation.cs") `
  (Join-Path $Root "src\PvpUiPresentation.cs") `
  (Join-Path $Root "src\PvpWindowChromePolicy.cs") `
  (Join-Path $Root "src\PvpUiStatePolicy.cs") `
  (Join-Path $Root "src\PvpProxyStartupPolicy.cs") `
  (Join-Path $Root "src\PvpPointerOwnershipState.cs") `
  (Join-Path $Root "src\PvpMatchLifecyclePolicy.cs") `
  (Join-Path $Root "src\PvpCombatStartupPolicy.cs") `
  (Join-Path $Root "src\PvpNativeNavHealthPolicy.cs") `
  (Join-Path $Root "src\PvpNativeCombatLoopPolicy.cs") `
  (Join-Path $Root "src\PvpPreparationNavReadinessPolicy.cs") `
  (Join-Path $Root "src\PvpCombatExecutionPolicy.cs") `
  (Join-Path $Root "src\PvpSpellExecutionPolicy.cs") `
  (Join-Path $Root "src\PvpNativeHealThresholdPolicy.cs") `
  (Join-Path $Root "src\PvpWorldCombatPolicy.cs") `
  (Join-Path $Root "src\PvpSpellTargetPolicy.cs") `
  (Join-Path $Root "src\PvpPluginIdentityPolicy.cs") `
  (Join-Path $Root "src\PvpWildAmbushZonePolicy.cs") `
  (Join-Path $Root "src\PvpPreOpportunityPolicy.cs") `
  (Join-Path $Root "src\PvpMatchmakingPolicy.cs") `
  (Join-Path $Root "tests\PvpUiPolicyTests.cs")
if ($LASTEXITCODE -ne 0) { throw "PvP UI policy test compilation failed." }
try { & $out; if ($LASTEXITCODE -ne 0) { throw "PvP UI policy tests failed." } } finally { Remove-Item $out -Force -ErrorAction SilentlyContinue }

# Runtime-wiring source guards supplement the pure pointer-state tests without requiring Unity execution.
$panelSource = Get-Content (Join-Path $Root "src\PvpPanel.cs") -Raw
$dragSource = Get-Content (Join-Path $Root "src\PvpDragGuard.cs") -Raw
$controllerSource = Get-Content (Join-Path $Root "src\PvpController.cs") -Raw
$zonePolicySource = Get-Content (Join-Path $Root "src\PvpWildAmbushZonePolicy.cs") -Raw
$itemObserverSource = Get-Content (Join-Path $Root "src\PvpItemDbVisualObserver.cs") -Raw
$auraSource = Get-Content (Join-Path $Root "src\PvpSuiteAuraProvider.cs") -Raw
if ($dragSource -notmatch 'OnPointerDown[\s\S]*Acquire\(\)') { throw "PvP drag guard failed: pointer-down ownership missing." }
if ($dragSource -notmatch 'InputButton\.Left' -or $dragSource -notmatch 'UsingUI') { throw "PvP drag guard failed: left-only modern camera containment missing." }
if ($dragSource -notmatch 'OnDisable\(\).*EndDrag' -or $dragSource -notmatch 'OnDestroy\(\).*EndDrag') { throw "PvP drag guard failed: disable/destroy cleanup missing." }
if ($panelSource -notmatch 'private\s+static\s+void\s+HideAll\(\)[\s\S]*PvpDragGuard\.ForceReleaseIfOwned\(\)') { throw "PvP drag guard failed: HideAll does not release ownership." }
if ($panelSource -notmatch 'internal\s+static\s+void\s+ResetPosition\(\)[\s\S]*PvpDragGuard\.ForceReleaseIfOwned\(\)' -or $panelSource -notmatch 'internal\s+static\s+void\s+ResetLauncherPosition\(\)[\s\S]*PvpDragGuard\.ForceReleaseIfOwned\(\)') { throw "PvP drag guard failed: reset paths do not release ownership." }
if ($controllerSource -notmatch 'SceneTransition\(\)[\s\S]*PvpPanel\.ReleaseDrag\(\)' -or $controllerSource -notmatch 'Shutdown\(\)[\s\S]*PvpPanel\.Dispose\(\)') { throw "PvP drag guard failed: zone/unload cleanup wiring missing." }
if ($controllerSource -notmatch 'pvp_disabled' -or $controllerSource -notmatch 'game_not_ready' -or $controllerSource -notmatch 'EncounterCleaned\(\)') { throw "PvP lifecycle guard failed: interrupted cleanup ownership missing." }
if ($controllerSource -notmatch '_nextOffer < Time\.unscaledTime \+ 300f') { throw "PvP lifecycle guard failed: zoning restart delay missing." }
if ($controllerSource -notmatch 'Random\.Range\(minimum \* 60f, maximum \* 60f \+ 1f\)' -or
    $controllerSource -notmatch 'Random\.Range\(0, 100\) < Math\.Max\(5, Math\.Min\(100, _ambushChancePercent\.Value\)\)') { throw "PvP wild-ambush scheduler/chance contract changed." }
if ($controllerSource -notmatch 'minimum == null \? 15' -and $controllerSource -notmatch '_ambushMinimumMinutes == null \? 15') { throw "PvP wild-ambush 15-minute default changed." }
if ($controllerSource -notmatch '_ambushMaximumMinutes == null \? 35') { throw "PvP wild-ambush 35-minute default changed." }
if ($controllerSource -notmatch 'eligibility_source=' -or $controllerSource -notmatch 'ordinary ready gameplay scenes are eligible by default') { throw "PvP wild-ambush diagnostics/policy text missing." }
if ($zonePolicySource -notmatch 'ordinary_adventure_zone' -or $zonePolicySource -notmatch 'explicit_zone_disabled' -or
    $zonePolicySource -notmatch 'non_gameplay_scene' -or $zonePolicySource -notmatch 'protected_zone') { throw "PvP wild-ambush decision sources incomplete." }
if ($controllerSource -notmatch 'CanSpawnClearTeam' -or $controllerSource -notmatch 'PvpEncounterMode\.Ambush') { throw "PvP wild-ambush local spawn clearance wiring missing." }
if ($controllerSource -match 'DeepSim|Campmaster|PracticeDuel') { throw "PvP zone policy introduced a sibling-mod source dependency." }
if ($auraSource -notmatch 'Prefix \+ "ui\.state"' -or $auraSource -notmatch 'PvpUiStatePolicy\.Build') { throw "PvP Suite guard failed: ui.state provider missing." }
if ($controllerSource -notmatch 'PvpItemDbVisualObserver\.Inspect\(option\)') { throw "PvP ItemDB observer command wiring missing." }
foreach ($required in @('GetItemByID', 'EquipmentToActivate', 'RequiredSlot', 'ItemLevel', 'Male_Parts', 'Female_Parts', 'All_Gender_Parts', 'FindExactNamedDescendant', 'FullPath', 'FindObjectsOfType<SimPlayer>', 'MaxIds', 'MaxCandidatesPerBranch', 'MaxOutputChars')) {
  if ($itemObserverSource -notmatch [regex]::Escape($required)) { throw "PvP ItemDB observer contract missing: $required" }
}
foreach ($forbidden in @('SetActive(', '.enabled =', 'SetField(', '.isMale =', 'MyItem =', 'Quant =')) {
  if ($itemObserverSource -match [regex]::Escape($forbidden)) { throw "PvP ItemDB observer must be read-only: $forbidden" }
}
Write-Host "PvP drag/Suite release source guards: PASS" -ForegroundColor Green

# Native-runtime regression guard: registered proxies retain the safe nameplate/maintenance
# invariant, but native NPC.Start is restored as owner of the complete navigation lifecycle.
$factorySource = Get-Content (Join-Path $Root "src\PvpTemporaryCloneFactory.cs") -Raw
$startupSource = Get-Content (Join-Path $Root "src\PvpProxyStartupPolicy.cs") -Raw
$rewardSource = Get-Content (Join-Path $Root "src\PvpRewardService.cs") -Raw
foreach ($token in @('TrySetField(npc, "Myself", actor)', 'TrySetField(npc, "MyStats"', 'TrySetField(npc, "MyNav", nav)', 'TrySetField(npc, "MySpells", caster)', 'NameFlash', 'HandleMaintenaceAndCounters', 'AllowNativeMaintenance', 'runtime_invalid')) {
  if ($factorySource -notmatch [regex]::Escape($token)) { throw "PvP native-runtime guard failed: missing $token" }
}
if ($startupSource -notmatch 'MaintenanceStatePasses' -or $startupSource -notmatch 'ShouldInterceptMaintenance') { throw "PvP native-runtime guard failed: state discriminator missing." }
if ($factorySource -notmatch 'if \(!IsTemporaryNpc\(npc\)\) return true;') { throw "PvP native-runtime guard failed: vanilla NPC fail-open missing." }
if ($factorySource -notmatch 'native_npc_exception method=NPC\.HandleMaintenaceAndCounters' -or $factorySource -notmatch '\[HarmonyFinalizer\]') { throw "PvP native-runtime guard failed: maintenance exception diagnostics missing." }
if ($rewardSource -notmatch 'GameData\.AddExperience\(xp, false\)' -or $rewardSource -notmatch 'GameData\.PlayerInv\.Gold \+= gold' -or $rewardSource -notmatch 'UpdatePlayerInventory\(\)') { throw "PvP reward guard failed: working reward path changed." }
Write-Host "PvP native NPC runtime/reward source guards: PASS" -ForegroundColor Green
$launcherVisual = Get-Content (Join-Path $Root "src\StandaloneLauncherVisual.cs") -Raw
if ($launcherVisual -notmatch 'Width\s*=\s*154f' -or $launcherVisual -notmatch 'Height\s*=\s*32f' -or
    $launcherVisual -notmatch 'GripWidth\s*=\s*20f' -or $launcherVisual -notmatch '"GripDot"' -or
    $panelSource -notmatch 'StyleGrip\(grip\)' -or $panelSource -notmatch 'PVP \[ON\]') {
    throw "PvP Forgotten Roads launcher visual contract failed."
}
Write-Host "PvP Forgotten Roads launcher visual contract: PASS" -ForegroundColor Green
$chromeSource = Get-Content (Join-Path $Root "src\PvpWindowChromePolicy.cs") -Raw
if ($panelSource -notmatch 'AddVerticalChevron\(_collapseChevron, true\)' -or
    $panelSource -notmatch 'private\s+static\s+void\s+SetCollapsed' -or
    $panelSource -notmatch 'ApplyCollapsedVisibility' -or
    $panelSource -notmatch 'PvpWindowChromePolicy\.PreserveTopBottomY' -or
    $chromeSource -notmatch 'CollapsedHeight\s*=\s*HeaderHeight') {
    throw "PvP Forgotten Roads header collapse contract failed."
}
Write-Host "PvP Forgotten Roads header collapse contract: PASS" -ForegroundColor Green


# Arranged match-start/native-AI source contracts. These are deterministic wiring guards for
# Unity/Harmony paths that cannot be executed by the standalone policy test executable.
$containmentSource = Get-Content (Join-Path $Root "src\PvpCombatContainment.cs") -Raw
$lifecycleSource = Get-Content (Join-Path $Root "src\PvpMatchLifecyclePolicy.cs") -Raw
$startupPolicySource = Get-Content (Join-Path $Root "src\PvpCombatStartupPolicy.cs") -Raw

foreach ($token in @(
  'PvpMatchLifecycleState.Countdown',
  'BeginMatchCountdown',
  'Say("[PvP] 3")',
  'Say("[PvP] GO")',
  '_lifecycle.Go()',
  'PrepareForCountdown',
  'npc.NeverAggro = true',
  'AreAllProxiesReady',
  'proxy_prepare_begin',
  'natural_start=deferred_to_go',
  'countdown_ready_barrier',
  'natural_start=enabled_after_go',
  'go_release',
  'TrySetField(npc, "navDo", null)',
  'TrySetField(npc, "behDo", null)',
  'PrepareNativeStartProbe',
  'ObserveNativeNavEntered',
  'ObserveNativeNavCompleted',
  'proxy_nav_faulted',
  'NativeNavHealthSummary',
  'native_update_reached',
  'combat_section_reached',
  'legal_target_acquired',
  'pursuit_probe_start',
  'melee_decision',
  'melee_attempt',
  'attack_spell_ai_entry',
  'heal_check',
  'spell_start',
  'damage_to_defender',
  'heal_to_attacker',
  'technical_failure_ai_inactive',
  'HasAnyNativeCombatEvidence',
  'CanGrantVictoryReward'
)) {
  if (($controllerSource + $factorySource + $containmentSource + $lifecycleSource + $startupPolicySource) -notmatch [regex]::Escape($token)) {
    throw "PvP match-start/native-AI guard failed: missing $token"
  }
}
if (($controllerSource + $factorySource + $containmentSource + $lifecycleSource + $startupPolicySource) -notmatch 'NeverAggro\s*=\s*(false|!PvpCombatContainment\.LethalFightActive)') {
  throw "PvP match-start/native-AI guard failed: no explicit GO aggression release found."
}
if ($factorySource -notmatch 'PvpNativeCombatLoopPolicy\.ShouldStartMissingLoop' -or
    $factorySource -notmatch 'InvokeNativeNpcLoop\(npc, "NavUpdate"' -or
    $factorySource -notmatch 'InvokeNativeNpcLoop\(npc, "BehaviorUpdate"' -or
    $factorySource -notmatch 'ModOwnedNavLoops' -or $factorySource -notmatch 'ModOwnedBehaviorLoops') {
  throw "PvP native lifecycle guard failed: missing bounded startup/ownership for a genuinely absent native loop."
}
if ($factorySource -notmatch '\[HarmonyPatch\(typeof\(NPC\), "UpdateNav"\)\]' -or
    $factorySource -notmatch 'ObserveNativeNavException' -or
    $containmentSource -notmatch 'CompleteNativeNavFailure') {
  throw "PvP nav health guard failed: actual UpdateNav progression/fault probe missing."
}
if ($factorySource -notmatch 'npc\.enabled = false' -or $factorySource -notmatch 'natural_start=deferred_to_go' -or
    $factorySource -notmatch 'PreGoReadinessComplete' -or $containmentSource -notmatch 'natural_start=enabled_after_go') {
  throw "PvP native Start lifecycle guard failed: deferred natural post-GO Start wiring missing."
}
if ($factorySource -match 'npc\.Start\(' -or $factorySource -match 'AccessTools\.Method\(typeof\(NPC\), "Start"') {
  throw "PvP native Start lifecycle guard failed: manual NPC.Start invocation/discovery returned."
}
if ($factorySource -notmatch 'npc\.NoSelfHeal = false') { throw "PvP healing guard failed: native self-heal remains disabled." }
if ($factorySource -notmatch 'if \(npc\.NoSelfHeal\) failures\.Add') { throw "PvP healing guard failed: runtime verifier still expects self-heal suppression." }
if ($containmentSource -notmatch 'return !PvpTemporaryCloneFactory\.IsTemporaryNpc\(npc\)[\s\S]*!PvpTemporaryCloneFactory\.IsTemporaryActor\(target\)') { throw "PvP pre-GO guard failed: defender-side proxy acquisition is not held." }
if ($containmentSource -notmatch 'Defenders\.Add\(player\)' -or $containmentSource -notmatch 'AddPartyDefenders\(\)' -or
    $containmentSource -notmatch 'RegisterDefenderPet\(actor\)' -or $containmentSource -notmatch 'actor\.Master') {
  throw "PvP participant guard failed: player/current party/current owned-pet defender set is incomplete."
}
if ($factorySource -notmatch 'ShouldRecordCompetitiveResult\(reason\)' -or
    $factorySource -notmatch 'history_credit=false' -or
    $factorySource -notmatch 'winner = null') {
  throw "PvP technical-failure guard failed: no-credit terminal path missing."
}
if ($containmentSource -notmatch 'AllowSpellStart' -or $factorySource -notmatch 'ObserveAndAllowSpellStart') {
  throw "PvP pre-GO guard failed: temporary-proxy spell initiation is not held."
}
$worldPolicySource = Get-Content (Join-Path $Root "src\PvpWorldCombatPolicy.cs") -Raw
if ($containmentSource -notmatch 'AllowHeal' -or $worldPolicySource -notmatch 'targetDefender && sourceDefender' -or
    $worldPolicySource -notmatch 'targetAttacker && sourceAttacker') {
  throw "PvP healing guard failed: same-team native healing policy missing."
}
foreach ($token in @(
  'AllowWorld',
  'IsProtectedNonCombat',
  'simPlayer || ownedOrSummoned',
  'sourceParticipant && noTarget',
  'Do not proximity-block AE/PBAE starts',
  'IsProtectedWorldActor',
  'protected_target_cleared',
  'SpawnActorCollisionClearance',
  'protectedActors',
  'IsPermittedProxyTarget'
)) {
  if (($worldPolicySource + $containmentSource + $factorySource) -notmatch [regex]::Escape($token)) {
    throw "PvP world-combat guard failed: missing $token"
  }
}
if ($containmentSource -match 'third_party_aggro' -or $containmentSource -match '_thirdPartyInterference' -or
    $controllerSource -match 'WorldCombatBusy\(\)' -or $controllerSource -match 'player_in_combat') {
  throw "PvP world-combat guard failed: isolation-era third-party/player-in-combat abort remains wired."
}
if ($worldPolicySource -notmatch 'DecideAggro[\s\S]*AllowWorld' -or
    $worldPolicySource -notmatch 'DecideDamage[\s\S]*AllowWorld' -or
    $worldPolicySource -notmatch 'DecideHeal[\s\S]*AllowWorld') {
  throw "PvP world-combat guard failed: native outside aggro/damage/heal is not admitted."
}
Write-Host "PvP MMO world-combat policy source guards: PASS" -ForegroundColor Green

$compatSource = Get-Content (Join-Path $Root "src\PvpCompatibility.cs") -Raw
if ($compatSource -notmatch 'ErenshorCoop\.NetworkedPlayer' -or $compatSource -notmatch 'ErenshorCoop\.NetworkedSim' -or
    $compatSource -notmatch 'IsNetworkOwnedActor') {
  throw "PvP COOP authority guard failed: namespaced current COOP actor detection missing."
}
if ($containmentSource -notmatch 'PvpCompatibility\.IsNetworkOwnedActor\(actor\)') { throw "PvP COOP authority guard failed: network-owned actors are not protected from local PvP ownership." }
if ($controllerSource -notmatch 'StartEncounter[\s\S]*IsCoopSession\(\)') { throw "PvP COOP authority guard failed: encounter-start authority recheck missing." }
if ($controllerSource -notmatch 'ReleaseMatchAtGo[\s\S]*IsCoopSession\(\)' -or $controllerSource -notmatch '_nextAuthorityCheck') { throw "PvP COOP authority guard failed: countdown/active authority recheck missing." }
if ($worldPolicySource -notmatch 'targetDefender && unknownPlayerProjectile') { throw "PvP world-combat guard failed: unattributed player projectile friendly-fire block missing." }
if ($startupPolicySource -notmatch 'proxy_death' -or $startupPolicySource -notmatch 'player_death' -or
    $startupPolicySource -notmatch 'player_fled' -or $startupPolicySource -notmatch 'retreat') {
  throw "PvP result guard failed: competitive result allow-list missing."
}
Write-Host "PvP COOP authority/result classification source guards: PASS" -ForegroundColor Green

if ($rewardSource -notmatch '_lastClaimedMatchId' -or $rewardSource -notmatch 'already claimed' -or
    $rewardSource -notmatch 'TryPersistSettings\(\)') {
  throw "PvP reward guard failed: legitimate match exact-once claim barrier missing."
}
if ($factorySource -notmatch 'TeamClones\.Count == 0 && _clone == null\) return' -or
    $controllerSource -notmatch 'EncounterCleaned\(\)') {
  throw "PvP cleanup guard failed: duplicate-safe terminal cleanup missing."
}
Write-Host "PvP arranged match-start/native-AI source guards: PASS" -ForegroundColor Green

# Final forensic combat-recovery matrix (task cases 24-49). These are source/pure-policy
# contracts; current-assembly build and live two-match acceptance remain separate gates.
$navPolicySource = Get-Content (Join-Path $Root "src\PvpNativeNavHealthPolicy.cs") -Raw
$identityPolicySource = Get-Content (Join-Path $Root "src\PvpPluginIdentityPolicy.cs") -Raw
$finalRecoveryCases = 0
function Assert-Recovery([bool]$condition, [string]$name) {
  if (-not $condition) { throw ("PvP final recovery matrix failed: " + $name) }
  $script:finalRecoveryCases++
}

Assert-Recovery ($identityPolicySource -match 'ExactlyOneExpectedIdentity') '24 exactly one effective ErenshorPvP identity policy'
Assert-Recovery ($startupSource -match 'hasNamePlateText' -and $startupSource -match 'hasNamePlateObject') '25 runtime invariant includes nameplate dependencies'
Assert-Recovery ($factorySource -match 'NamePlateTxt' -and $factorySource -match 'HandleNameTag') '26 HandleNameTag/nameplate regression guard retained'
Assert-Recovery ($factorySource -match 'npc\.NeverAggro = true' -and $factorySource -match 'npc\.CurrentAggroTarget = null' -and $containmentSource -match 'IsTemporaryActor\(target\)') '27 preparation/countdown holds attackers and defenders while native Start remains enabled'
Assert-Recovery ($controllerSource -match 'go_count=' -and $containmentSource -match 'go_release' -and $lifecycleSource -match 'GoTransitions') '28 GO releases once'
Assert-Recovery ($factorySource -match 'ValidateProxyStartupInvariant' -and $factorySource -match 'action=blocked_invalid') '29 native dependencies validated before Start/Active'
Assert-Recovery ($navPolicySource -match 'LaunchAloneIsHealthy' -and $navPolicySource -match 'return false') '30 coroutine launch alone is not healthy'
Assert-Recovery ($navPolicySource -match 'faulted' -and $navPolicySource -match '!faulted') '31 first MoveNext/UpdateNav fault is unhealthy'
Assert-Recovery ($factorySource -match 'ObserveNativeNavEntered' -and $factorySource -match 'ObserveNativeNavCompleted') '32 UpdateNav progression is health evidence'
Assert-Recovery ($navPolicySource -match 'NeedsPursuit' -and $factorySource -match 'pursuit_probe_start') '33 out-of-range melee pursuit evidence'
Assert-Recovery ($navPolicySource -match 'distance > attackRange' -or $navPolicySource -match 'distance > Mathf') '34 ranged in-range does not require artificial movement'
Assert-Recovery ($factorySource -match 'legal_target_acquired') '35 native target acquisition evidence'
Assert-Recovery ($factorySource -match 'combat_section_reached' -and $factorySource -match 'native_update_reached') '36 native BehaviorUpdate/combat progression evidence'
Assert-Recovery ($factorySource -match 'attack_spell_ai_entry') '37 attack spell decision evidence'
Assert-Recovery ($factorySource -match 'npc\.NoSelfHeal = false' -and $containmentSource -match 'AllowHeal') '38 self/ally healing admitted'
Assert-Recovery ($worldPolicySource -match 'outside world actor' -or $worldPolicySource -match 'AllowWorld') '39 ordinary hostile world actor may join'
Assert-Recovery ($worldPolicySource -match 'simPlayer \|\| ownedOrSummoned') '40 Sim participation is not protected-NPC interference'
Assert-Recovery ($worldPolicySource -match 'ownedOrSummoned') '41 pet/summon participation is not protected-NPC interference'
Assert-Recovery ($containmentSource -notmatch 'third_party_aggro' -and $controllerSource -notmatch 'third_party_aggro') '42 third_party_aggro is not a runtime terminal path'
Assert-Recovery ($factorySource -match 'protected_target_cleared') '43 protected neutral target is rejected narrowly'
Assert-Recovery ($factorySource -match 'ForceAggroOn\(null\)' -and $factorySource -match 'protected_target_cleared') '44 protected target rejection clears target without match cancel'
Assert-Recovery ($containmentSource -match 'CompleteNativeNavFailure' -and $containmentSource -match 'technical_failure_ai_inactive') '45 complete nav failure becomes technical failure'
Assert-Recovery ($factorySource -match 'winner=none' -and $factorySource -match 'winner = null') '46 technical failure winner none'
Assert-Recovery ($factorySource -match 'xp=0; gold=0' -and $factorySource -match 'history_credit=false') '47 technical failure zero rewards/history'
Assert-Recovery ($rewardSource -match '_lastClaimedMatchId' -and $rewardSource -match 'already claimed') '48 legitimate victory reward exact-once'
Assert-Recovery ($controllerSource -match 'EncounterCleaned' -and $factorySource -match 'TeamClones\.Clear\(\)') '49 repeated-match cleanup permits second match'

if ($finalRecoveryCases -ne 26) { throw "PvP final recovery matrix count mismatch: $finalRecoveryCases / 26" }
Write-Host ("PvP final forensic recovery source matrix: PASS (" + $finalRecoveryCases + "/26)") -ForegroundColor Green

# 0.5.11 timer-inventory guard. Investigation found no timer that terminates an ACTIVE fight at 30
# seconds: _pendingExpires is the arranged-challenge OFFER expiration (before the fight starts),
# and _despawnAt is a pre-GO setup safety despawn that BeginLethalFight neutralizes to
# float.PositiveInfinity the moment the fight actually goes live. Both must stay exactly as found;
# no active-combat timeout was invented, and none of the explicitly-unrelated timers moved.
$controllerSource = Get-Content (Join-Path $Root "src\PvpController.cs") -Raw
$settingsSource = Get-Content (Join-Path $Root "src\PvpSettings.cs") -Raw
if ($controllerSource -notmatch '_pendingExpires = now \+ 30f;') { throw "PvP timer guard failed: challenge offer expiration (_pendingExpires) changed unexpectedly." }
if ($factorySource -notmatch '_despawnAt = Time\.unscaledTime \+ 30f;' -or $factorySource -notmatch '_despawnAt = Time\.unscaledTime \+ 20f;') { throw "PvP timer guard failed: pre-GO setup despawn timers changed unexpectedly." }
if ($factorySource -notmatch 'if \(PvpCombatContainment\.LethalFightActive\) _despawnAt = float\.PositiveInfinity;') { throw "PvP timer guard failed: active-fight despawn neutralization was removed - this would reintroduce a timer that ends an active fight." }
if ($settingsSource -notmatch 'VictoryCooldownMinutes = 30;') { throw "PvP timer guard failed: reward cooldown (VictoryCooldownMinutes) changed unexpectedly." }
if ($controllerSource -notmatch '_nextScan = Time\.unscaledTime \+ 300f;' -or $controllerSource -notmatch '_nextOffer < Time\.unscaledTime \+ 300f') { throw "PvP timer guard failed: scan/offer/ambush cooldown changed unexpectedly." }
Write-Host "PvP timer-inventory guard: PASS (no active-fight-terminating timer exists; offer/despawn/reward/scan timers unchanged)" -ForegroundColor Green

# 0.5.11 per-proxy ability-use observability guards. The terminal summary must stay bounded (one
# line per proxy, logged exactly once per fight end) and must never be reachable from a per-frame
# path such as Tick()/Update().
if ($factorySource -notmatch 'internal static void LogPerProxyAbilitySummary\(\)') { throw "PvP diagnostics guard failed: per-proxy terminal summary is missing." }
$perProxyCallSites = [regex]::Matches($containmentSource + $factorySource, 'LogPerProxyAbilitySummary\(\)').Count
if ($perProxyCallSites -ne 2) { throw "PvP diagnostics guard failed: per-proxy terminal summary must be defined once and called exactly once (found $perProxyCallSites references, expected 2: the definition and the single call site)." }
$balanceSummaryBody = [regex]::Match($containmentSource, 'private\s+static\s+void\s+LogBalanceSummary\(string reason\)[\s\S]*?\n        \}')
if (-not $balanceSummaryBody.Success -or $balanceSummaryBody.Value -notmatch 'LogPerProxyAbilitySummary\(\)') { throw "PvP diagnostics guard failed: per-proxy summary is not called from the terminal balance summary." }
$tickBody = [regex]::Match($containmentSource, 'internal\s+static\s+void\s+Tick\(\)[\s\S]*?\n        \}')
if ($tickBody.Success -and $tickBody.Value -match 'LogPerProxyAbilitySummary') { throw "PvP diagnostics guard failed: per-proxy summary must never be reachable from the per-frame Tick() path." }
if ($factorySource -notmatch 'AttackSkillDecisionCounts' -or $factorySource -notmatch 'AttackSpellDecisionCounts' -or
    $factorySource -notmatch 'DamageDealtCounts' -or $factorySource -notmatch 'HealingDoneCounts') {
    throw "PvP diagnostics guard failed: split decision / per-proxy outcome counters are missing."
}
if ($startupSource -notmatch 'internal static string ProxyAbilityUseAssessment') { throw "PvP diagnostics guard failed: ProxyAbilityUseAssessment classification is missing." }
Write-Host "PvP per-proxy ability-use observability guard: PASS (bounded, not per-frame)" -ForegroundColor Green

# Version guard.
$pluginSource = Get-Content (Join-Path $Root "src\ErenshorPvPPlugin.cs") -Raw
if ($pluginSource -notmatch 'LunarisPlugin\("forgetwhtuno\.erenshor\.pvp", "0\.5\.34", "forgetwhtuno"') { throw "PvP version guard failed: plugin version is not 0.5.34." }
Write-Host "PvP version guard: PASS (0.5.34)" -ForegroundColor Green
if ($pluginSource -notmatch 'revision=pvp-0\.5\.34-preop-world-context-r1') { throw "PvP revision guard failed: pre-opportunity world-context marker missing." }
$preopPolicySource = Get-Content (Join-Path $Root "src\PvpPreOpportunityPolicy.cs") -Raw
if ($preopPolicySource -notmatch 'MinimumPlayerLevel = 3' -or $preopPolicySource -notmatch 'RecentNativeCombatGraceSeconds = 20f' -or $preopPolicySource -notmatch 'NearbyWorldActorRadius = 12f') {
  throw "PvP pre-opportunity policy guard failed: approved constants are missing."
}
if ($preopPolicySource -notmatch 'BelowMinimumLevel' -or $preopPolicySource -notmatch 'NativeCombatActive' -or $preopPolicySource -notmatch 'NearbyWorldActor') {
  throw "PvP pre-opportunity policy guard failed: required stable decisions are missing."
}
$tryOffer = [regex]::Match($controllerSource, 'private\s+static\s+void\s+TryOffer\([\s\S]*?\n        \}')
$namedAmbush = [regex]::Match($controllerSource, 'internal\s+static\s+string\s+RequestNamedAmbush\([\s\S]*?\n        \}')
$accept = [regex]::Match($controllerSource, 'internal\s+static\s+void\s+Accept\([\s\S]*?\n        \}')
if (!$tryOffer.Success -or $tryOffer.Value.IndexOf('EvaluatePreOpportunityContext') -gt $tryOffer.Value.IndexOf('TrySelectOffMap') -or
    !$namedAmbush.Success -or $namedAmbush.Value.IndexOf('EvaluatePreOpportunityContext') -gt $namedAmbush.Value.IndexOf('TrySelectOffMap') -or
    !$accept.Success -or $accept.Value -notmatch 'EvaluatePreOpportunityContext') {
  throw "PvP pre-opportunity wiring guard failed: gate must run before selection and at arranged acceptance."
}
if ($controllerSource -notmatch 'CurrentAggroTarget' -or $controllerSource -notmatch 'FindObjectsOfType<NPC>' -or $controllerSource -notmatch 'PvpTemporaryCloneFactory\.IsTemporaryNpc') {
  throw "PvP pre-opportunity wiring guard failed: native combat/world actor scan is incomplete."
}
Write-Host "PvP 0.5.34 pre-opportunity policy/wiring guards: PASS" -ForegroundColor Green
$loopPolicySource = Get-Content (Join-Path $Root "src\PvpNativeCombatLoopPolicy.cs") -Raw
foreach ($token in @(
  'ShouldStartMissingLoop', 'ownedStartCount == 0', 'InfrastructureReady', 'PreGoBoundaryReady', 'CanStopOwnedLoop',
  'ModOwnedNavLoops', 'ModOwnedBehaviorLoops', 'ModOwnedNavLoopStarts', 'ModOwnedBehaviorLoopStarts',
  'TryStartOwnedNativeCombatLoop', 'object.ReferenceEquals(current, loop)', 'object.ReferenceEquals(current, owned)',
  'StopOwnedNativeCombatLoops', 'behavior_section_reached', 'pre_go_behavior_hold', 'pre_go_nav_hold'
)) {
  if (($loopPolicySource + $factorySource) -notmatch [regex]::Escape($token)) { throw "PvP 0.5.16 combat-loop ownership guard failed: missing $token" }
}
if ($factorySource -match 'StopAllCoroutines\(') { throw "PvP 0.5.16 combat-loop ownership guard failed: broad coroutine cleanup is forbidden." }
if ([regex]::Matches($factorySource, 'StartCoroutine\(loop\)').Count -ne 1) { throw "PvP 0.5.16 combat-loop ownership guard failed: owned loop startup is not centralized." }
if ([regex]::Matches($factorySource, 'StopCoroutine\(owned\)').Count -ne 2) { throw "PvP 0.5.16 combat-loop ownership guard failed: cleanup must stop only nav/behavior owned iterators." }
if ($factorySource -match 'AccessTools\.Method\(typeof\(NPC\),\s*"Start"' -or $factorySource -match '\.Start\(\)') { throw "PvP 0.5.16 combat-loop ownership guard failed: NPC.Start must never be manually invoked." }
if ($controllerSource -notmatch 'PvpNativeCombatLoopPolicy\.RunSelfTests\(\)') { throw "PvP 0.5.16 combat-loop ownership guard failed: /epvp selftest omits loop policy." }
if ($factorySource -notmatch 'AreAllProxiesReadyForGo' -or $factorySource -notmatch 'PreGoReadinessComplete' -or $containmentSource -notmatch 'natural_start=enabled_after_go') { throw "PvP 0.5.17 lifecycle guard failed: GO must enable natural Start after structural preparation." }
Write-Host "PvP 0.5.16 combat-loop ownership/pre-GO/cleanup guards: PASS" -ForegroundColor Green

$rewardPolicySource = Get-Content (Join-Path $Root "src\PvpProxyStartupPolicy.cs") -Raw
foreach ($token in @(
  'NativeCharacterStartCompleted',
  'FinalizePostStartRewardSuppression',
  'VerifyRewardSuppression',
  'RecordAndVerifyRewardSuppression',
  'reward_hold_check proxyId=',
  'RewardSuppressionProofPasses',
  'reward_suppression_state proxy=',
  '[HarmonyPatch(typeof(Character), "Start")]',
  '[HarmonyPatch(typeof(Character), "DoWorldEventCredit")]',
  'ClearBorrowedLootCollection(loot, "ActualDrops")',
  'TrySetField(npc, "SetAchievementOnDefeat", string.Empty)'
)) {
  if (($factorySource + $rewardPolicySource) -notmatch [regex]::Escape($token)) { throw "PvP 0.5.16 reward-readiness guard failed: missing $token" }
}
if ($factorySource -match 'RewardSuppressionReady\s*=\s*true') { throw "PvP 0.5.16 reward-readiness guard failed: readiness forced true." }
if ($rewardPolicySource -notmatch 'CountdownHoldAccepts' -or $factorySource -notmatch 'characterStartCompleted && VerifyRewardSuppression' -or $factorySource -notmatch 'bool verified = VerifyRewardSuppression\(member, out rewardReason\)' -or $factorySource -notmatch 'LogRewardStateSnapshot\(member, "ready"' -or $factorySource -notmatch 'LogRewardStateSnapshot\(member, "countdown_hold"') { throw "PvP countdown-hold consistency guard failed: readiness and hold do not share authoritative proof/snapshots." }
if ($rewardPolicySource -match 'npcStartCompleted && characterStartCompleted') { throw "PvP countdown-hold consistency guard failed: deferred native Start still gates pre-GO reward proof." }
if ($controllerSource -notmatch 'countdown safety check failed\. Normal control restored' -or $controllerSource -notmatch 'PvpTemporaryCloneFactory\.Despawn\("countdown_hold_failed"\)') { throw "PvP countdown-hold cleanup guard failed: terminal cancellation is incomplete." }
Write-Host "PvP 0.5.18 reward/countdown consistency source guards: PASS" -ForegroundColor Green

# Equipment presentation fidelity guards. The render shell must refresh the native gendered
# transform cache before UpdateSimPlayerVisuals, and two-handed Item metadata must own the
# primary/off-hand normalization instead of allowing a flexible slot to create an off-hand
# presentation attachment.
foreach ($token in @(
  'entered=GetTransformNames',
  'getTransformNames\.Invoke\(parts, null\)',
  'ThisWeaponType',
  'TwoHandMelee',
  'TwoHandStaff',
  'TwoHandBow',
  'SelectNativeMainWeapon',
  'weapon_presentation_normalized',
  'nativeTwoHanded=true',
  'FindNativePresentationNode',
  'requireVisibleRenderer'
)) {
  if ($factorySource -notmatch $token) { throw "PvP equipment presentation fidelity guard failed: missing $token" }
}
if ($factorySource -notmatch 'getTransformNames\.Invoke\(parts, null\)[\s\S]*parts\.UpdateSimPlayerVisuals\(') {
  throw "PvP equipment presentation fidelity guard failed: native transform cache refresh must precede visual materialization."
}
if ($factorySource -notmatch 'off=empty[\s\S]*nativeTwoHanded=true') {
  throw "PvP equipment presentation fidelity guard failed: two-handed presentation must clear the native secondary slot."
}
Write-Host "PvP equipment presentation fidelity source guards: PASS" -ForegroundColor Green



$preGoNavPolicySource = Get-Content (Join-Path $Root "src\PvpPreparationNavReadinessPolicy.cs") -Raw
$preGoNavRuntimeSource = Get-Content (Join-Path $Root "src\PvpPreparationNavRuntime.cs") -Raw
$healThresholdProbeSource = Get-Content (Join-Path $Root "src\PvpNativeHealThresholdProbe.cs") -Raw
foreach ($token in @('IsStructurallyReady', 'heldStopped', '!hasPath', 'nativeNavFaulted', 'TryNormalizeHeldAgent', 'ResetPath()', 'nav.isStopped = true', 'PvpPreparationNavRuntime.EvaluateAgent', 'nav_structural_pending')) {
  if (($preGoNavPolicySource + $preGoNavRuntimeSource + $factorySource) -notmatch [regex]::Escape($token)) { throw "PvP 0.5.16 pre-GO nav readiness guard failed: missing $token" }
}
if ($factorySource -match 'state\.NavAgentReady\s*=.*FirstUpdateNavCompleted') { throw "PvP 0.5.16 pre-GO nav readiness still depends on operational UpdateNav proof." }
if ($preGoNavRuntimeSource -match 'SetDestination\(' -or $preGoNavRuntimeSource -match 'Warp\(') { throw "PvP 0.5.16 pre-GO nav readiness probe may move/teleport an attacker." }
if ($healThresholdProbeSource -notmatch 'typeof\(NPC\).*CheckHeals' -or $healThresholdProbeSource -notmatch 'OpCodes\.Ldc_R4' -or $healThresholdProbeSource -notmatch 'OperandSize' -or $healThresholdProbeSource -notmatch 'GetILAsByteArray') { throw "PvP 0.5.16 native heal threshold IL proof missing." }
if ($controllerSource -notmatch 'PvpNativeHealThresholdProbe\.RunSelfTest\(\)') { throw "PvP 0.5.16 /epvp selftest does not include current-native heal threshold proof." }
Write-Host "PvP 0.5.16 pre-GO structural nav/native heal proof guards: PASS" -ForegroundColor Green

# 0.5.14 native-combat convergence guards (retains the 0.5.12 request/acceptance repair). These supplement the pure policy cases above;
# they verify that current runtime wiring distinguishes AI entry, native request, actual bool result,
# completion/effect, and the PvP-only allied-heal bridge without restoring sealed-arena behavior.
$spellPolicySource = Get-Content (Join-Path $Root "src\PvpSpellExecutionPolicy.cs") -Raw
$spellBridgeSource = Get-Content (Join-Path $Root "src\PvpNativeSpellExecutionBridge.cs") -Raw
foreach ($token in @(
  'NPCSpellCooldown',
  'atkSpellDelay',
  'healCD',
  'cleared_synthetic_startup_npc_spell_cooldown',
  'AttackEntryAssessment',
  'CastPipelineAssessment',
  'PvpNativeHealThresholdPolicy.ExpectedThreshold',
  'TryFindInjuredAttackerAlly',
  'caster.StartSpell(selected, ally.MyStats)',
  'spell_native_request',
  'spell_native_result',
  'cast_finished',
  'healthy_native_sim_cast accepted=true',
  'LiveCombatPresentationSummary'
)) {
  if (($factorySource + $containmentSource + $spellPolicySource + $spellBridgeSource) -notmatch [regex]::Escape($token)) {
    throw "PvP 0.5.16 native-combat convergence guard failed: missing $token"
  }
}
if ([regex]::Matches($factorySource, 'bool __state, bool __result').Count -ne 6) { throw "PvP 0.5.16 native-combat convergence guard failed: every StartSpell-family bool result must be observed." }
if ($spellBridgeSource -match 'AttackSpellDelayField\.SetValue' -or $spellBridgeSource -match 'ForceSpellCooldownField\.SetValue') { throw "PvP 0.5.16 native-combat convergence guard failed: native attack cadence was overridden." }
if ($containmentSource -match 'third_party_aggro') { throw "PvP 0.5.16 native-combat convergence guard failed: isolated-arena cancellation returned." }
Write-Host "PvP 0.5.16 native-combat convergence source guards: PASS" -ForegroundColor Green


# 0.5.14 Duel-parity/effect/readiness source guards.
$spellTargetSource = Get-Content (Join-Path $Root "src\PvpSpellTargetPolicy.cs") -Raw
$spellSemanticSource = Get-Content (Join-Path $Root "src\PvpSpellSemantics.cs") -Raw
$spellEffectSource = Get-Content (Join-Path $Root "src\PvpNativeSpellEffectBridge.cs") -Raw
foreach ($token in @(
  'CanAdaptOpponentHealToSelf', 'current_target_preserved=true', 'DirectHpHeal', 'HealOverTime',
  'BeneficialBuff', 'SelfUtility', 'PvpTemporaryProxySpellVesselInterruptPatch',
  'RestoreVesselFixedUpdate', 'spell_vessel_created', 'spell_vessel_resolve',
  'healme_entered', 'heal_effect_applied', 'SnapshotStatusEffectState', 'proven_applied=',
  'PreparationTimeoutSeconds', 'first_action_latency'
)) {
  if (($factorySource + $containmentSource + $controllerSource + $spellTargetSource + $spellSemanticSource + $spellEffectSource) -notmatch [regex]::Escape($token)) {
    throw "PvP 0.5.16 convergence guard failed: missing $token"
  }
}
if ($spellEffectSource -match 'SimPlayer\s*=\s*true' -or $spellEffectSource -match 'HealMe\(' -or $spellEffectSource -match 'CurrentHP\s*\+=') {
  throw "PvP 0.5.16 convergence guard failed: SpellVessel bridge bypasses native identity/effect ownership."
}
if ($factorySource -match 'AttackSpellDelayField\.SetValue' -or $factorySource -match 'atkSpellDelay\s*=\s*0') {
  throw "PvP 0.5.16 convergence guard failed: atkSpellDelay is forced instead of draining natively."
}
if ($spellTargetSource -notmatch 'application_proven' -or $spellTargetSource -notmatch 'refresh_proven' -or $spellTargetSource -notmatch 'no_observable_state_change') {
  throw "PvP 0.5.16 convergence guard failed: status effect outcome proof is incomplete."
}
if ([regex]::Matches($containmentSource, 'FinishHpHealing\(__instance, __state\); return __exception;').Count -ne 2) {
  throw "PvP 0.5.16 convergence guard failed: HealMe telemetry must close through exactly two exception-safe finalizers."
}
if ([regex]::Matches($containmentSource, 'FinishStatusEffect\(__state\); return __exception;').Count -ne 4) {
  throw "PvP 0.5.16 convergence guard failed: status telemetry must close through exactly four exception-safe finalizers."
}
if ($containmentSource -match '\[HarmonyPostfix\] private static void Postfix\(PvpCombatContainment\.StatusEffectTelemetryState __state\)') {
  throw "PvP 0.5.16 convergence guard failed: status telemetry still double-finishes through Postfix and Finalizer."
}
Write-Host "PvP 0.5.16 Duel parity/effect/readiness source guards: PASS" -ForegroundColor Green
