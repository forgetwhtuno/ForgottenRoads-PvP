namespace ErenshorPvP
{
    // Pure pre-GO semantics: preparation is intentionally inert, so structural readiness does not
    // require a coroutine, UpdateNav entry, path, target, or displacement.
    internal static class PvpPreparationNavReadinessPolicy
    {
        internal static bool IsStructurallyReady(bool agentPresent, bool enabled, bool onNavMesh,
            bool heldStopped, bool hasPath, bool nativeNavFaulted, float speed, float acceleration,
            float angularSpeed, float radius, float height, float stoppingDistance)
        {
            return agentPresent && enabled && onNavMesh && heldStopped && !hasPath && !nativeNavFaulted &&
                FinitePositive(speed) && FinitePositive(acceleration) && FiniteNonNegative(angularSpeed) &&
                FinitePositive(radius) && FinitePositive(height) && FiniteNonNegative(stoppingDistance);
        }

        internal static string FirstFailure(bool agentPresent, bool enabled, bool onNavMesh,
            bool heldStopped, bool hasPath, bool nativeNavFaulted, float speed, float acceleration,
            float angularSpeed, float radius, float height, float stoppingDistance)
        {
            if (!agentPresent) return "agent_missing";
            if (!enabled) return "agent_disabled";
            if (!onNavMesh) return "agent_off_mesh";
            if (!heldStopped) return "pre_go_not_stopped";
            if (hasPath) return "stale_pre_go_path";
            if (nativeNavFaulted) return "native_nav_faulted";
            if (!FinitePositive(speed)) return "invalid_speed";
            if (!FinitePositive(acceleration)) return "invalid_acceleration";
            if (!FiniteNonNegative(angularSpeed)) return "invalid_angular_speed";
            if (!FinitePositive(radius)) return "invalid_radius";
            if (!FinitePositive(height)) return "invalid_height";
            if (!FiniteNonNegative(stoppingDistance)) return "invalid_stopping_distance";
            return "ready";
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool FinitePositive(float value) { return Finite(value) && value > 0f; }
        private static bool FiniteNonNegative(float value) { return Finite(value) && value >= 0f; }

        internal static string RunSelfTests()
        {
            if (!IsStructurallyReady(true, true, true, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL stopped on-mesh structural-ready case";
            if (IsStructurallyReady(true, false, true, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL disabled agent ready";
            if (IsStructurallyReady(true, true, false, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL off-mesh agent ready";
            if (IsStructurallyReady(true, true, true, false, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL prematurely released agent ready";
            if (IsStructurallyReady(true, true, true, true, true, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL stale-path agent ready";
            if (IsStructurallyReady(true, true, true, true, false, true, 3.5f, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL faulted native nav ready";
            if (IsStructurallyReady(true, true, true, true, false, false, float.NaN, 8f, 120f, 0.5f, 2f, 0f)) return "FAIL invalid movement parameter ready";
            return "PASS pvp pre-GO structural nav readiness policy";
        }
    }
}
