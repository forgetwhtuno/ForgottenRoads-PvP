using System;

namespace ErenshorPvP
{
    // Pure, deterministic interpretation of live execution evidence. These helpers deliberately
    // distinguish "native code was entered" from "the requested combat action actually progressed".
    internal static class PvpCombatExecutionPolicy
    {
        internal static bool AnyExecutionEvidence(bool rootMoved, bool meleeHitReached, bool nativeCastCompleted,
            bool damageApplied, bool healingApplied)
        {
            return rootMoved || meleeHitReached || nativeCastCompleted || damageApplied || healingApplied;
        }

        internal static bool PursuitProven(bool needsPursuit, bool agentPresent, bool enabled, bool onMesh,
            bool isStopped, bool hasPath, bool pathComplete, float displacement, float velocity)
        {
            if (!needsPursuit) return true;
            return agentPresent && enabled && onMesh && !isStopped && hasPath && pathComplete &&
                (displacement >= 0.20f || velocity >= 0.05f);
        }

        internal static string PursuitAssessment(bool needsPursuit, bool agentPresent, bool enabled, bool onMesh,
            bool isStopped, bool hasPath, bool pathComplete, float displacement, float velocity)
        {
            if (!needsPursuit) return "not_required";
            if (!agentPresent) return "agent_missing";
            if (!enabled) return "agent_disabled";
            if (!onMesh) return "agent_off_mesh";
            if (isStopped) return "agent_stopped";
            if (!hasPath) return "path_missing";
            if (!pathComplete) return "path_incomplete";
            if (displacement < 0.20f && velocity < 0.05f) return "path_but_no_motion";
            return "moving";
        }

        internal static bool ShellFollowProven(float rootDisplacement, float shellDisplacement, float localOffsetDelta)
        {
            if (localOffsetDelta > 0.10f) return false;
            if (rootDisplacement < 0.20f) return true;
            return Math.Abs(rootDisplacement - shellDisplacement) <= 0.20f;
        }

        internal static bool HealingRuntimeConnected(int configuredHealSpells, int memmedHealSpells)
        {
            return configuredHealSpells <= 0 || memmedHealSpells > 0;
        }

        internal static string CastAssessment(int requested, int accepted, int completed, int effects)
        {
            if (requested <= 0) return "no_request";
            if (accepted <= 0) return "native_rejected";
            if (completed <= 0) return "accepted_not_finished";
            if (effects <= 0) return "finished_no_effect";
            return "effect_observed";
        }
    }
}
