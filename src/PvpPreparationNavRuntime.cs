using System;
using UnityEngine.AI;

namespace ErenshorPvP
{
    internal static class PvpPreparationNavRuntime
    {
        internal static bool TryNormalizeHeldAgent(NavMeshAgent nav, out string reason)
        {
            reason = "agent_missing";
            if (nav == null) return false;
            try
            {
                nav.enabled = true;
                if (!nav.isOnNavMesh) { reason = "agent_off_mesh"; return false; }
                nav.ResetPath();
                nav.isStopped = true;
                reason = "held_structural";
                return true;
            }
            catch (Exception ex) { reason = "normalize_" + ex.GetType().Name; return false; }
        }

        internal static bool EvaluateAgent(NavMeshAgent nav, bool nativeNavFaulted, out string reason)
        {
            if (nav == null) { reason = "agent_missing"; return false; }
            try
            {
                bool ready = PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, nav.enabled, nav.isOnNavMesh,
                    nav.isStopped, nav.hasPath, nativeNavFaulted, nav.speed, nav.acceleration, nav.angularSpeed,
                    nav.radius, nav.height, nav.stoppingDistance);
                reason = ready ? "ready" : PvpPreparationNavReadinessPolicy.FirstFailure(true, nav.enabled, nav.isOnNavMesh,
                    nav.isStopped, nav.hasPath, nativeNavFaulted, nav.speed, nav.acceleration, nav.angularSpeed,
                    nav.radius, nav.height, nav.stoppingDistance);
                return ready;
            }
            catch (Exception ex) { reason = "inspect_" + ex.GetType().Name; return false; }
        }
    }
}
