using System;
using HarmonyLib;
using UnityEngine;

namespace ErenshorPvP
{
    // Current Assembly-CSharp SpellVessel.FixedUpdate gives native SimPlayer casters a narrower
    // interruption path than ordinary non-Sim NPCs. Temporary PvP proxies intentionally remain
    // non-persistent/non-Sim, so this bridge reproduces ONLY that one live-combat interruption
    // distinction for a vessel whose CastSpell owner is an active temporary PvP proxy.
    //
    // It never changes NPC.SimPlayer/ThisSim, never changes spell magnitude/resources/cooldowns,
    // never calls ResolveSpell/HealMe directly, and always restores the original field in Finalizer.
    internal static class PvpNativeSpellEffectBridge
    {
        internal struct InterruptState
        {
            internal bool Changed;
            internal bool OriginalInterruptable;
        }

        internal static void BeginVesselFixedUpdate(CastSpell spellSource, Stats target, ref bool interruptable,
            ref InterruptState state)
        {
            state = new InterruptState();
            if (!PvpCombatContainment.LethalFightActive || spellSource == null) return;

            Character caster = null;
            Character targetActor = null;
            try { caster = spellSource.MyChar; } catch { }
            try { targetActor = target == null ? null : target.Myself; } catch { }
            if (!PvpTemporaryCloneFactory.IsTemporaryActor(caster)) return;

            bool casterAlive = false;
            bool targetAlive = false;
            bool targetInvulnerable = false;
            try { casterAlive = caster != null && caster.MyStats != null && caster.MyStats.CurrentHP > 0; } catch { }
            try { targetAlive = targetActor != null && targetActor.MyStats != null && targetActor.MyStats.CurrentHP > 0; } catch { }
            try { targetInvulnerable = targetActor != null && targetActor.Invulnerable; } catch { }

            if (!PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(
                true, true, casterAlive, targetActor != null, targetAlive, targetInvulnerable, interruptable)) return;

            state.Changed = true;
            state.OriginalInterruptable = interruptable;
            interruptable = false;
            PvpTemporaryCloneFactory.ObserveVesselInterruptBridge(caster, targetActor);
        }

        internal static void RestoreVesselFixedUpdate(ref bool interruptable, InterruptState state)
        {
            if (state.Changed) interruptable = state.OriginalInterruptable;
        }
    }

    [HarmonyPatch(typeof(SpellVessel), "FixedUpdate")]
    internal static class PvpTemporaryProxySpellVesselInterruptPatch
    {
        [HarmonyPrefix]
        private static void Prefix(CastSpell ___SpellSource, Stats ___targ, ref bool ___interruptable,
            ref PvpNativeSpellEffectBridge.InterruptState __state)
        {
            PvpNativeSpellEffectBridge.BeginVesselFixedUpdate(___SpellSource, ___targ, ref ___interruptable, ref __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, ref bool ___interruptable,
            PvpNativeSpellEffectBridge.InterruptState __state)
        {
            PvpNativeSpellEffectBridge.RestoreVesselFixedUpdate(ref ___interruptable, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SpellVessel), "CreateSpellChargeEffect")]
    internal static class PvpSpellVesselCreatedTelemetryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CastSpell ___SpellSource, Spell ___spell, Stats ___targ)
        {
            PvpTemporaryCloneFactory.ObserveSpellVesselCreated(___SpellSource, ___spell, ___targ);
        }
    }

    [HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]
    internal static class PvpSpellVesselResolveTelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(CastSpell ___SpellSource, Spell ___spell, Stats ___targ)
        {
            PvpTemporaryCloneFactory.ObserveSpellVesselResolve(___SpellSource, ___spell, ___targ);
        }
    }
}
