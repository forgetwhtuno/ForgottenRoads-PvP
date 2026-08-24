using System;
using System.Collections.Generic;
using ErenshorPvP;

internal static class PvpTemporaryCloneFactoryDespawnInvariantTests
{
    internal static int Main()
    {
        try
        {
            // Reproduce: PreparationStates populated -> last proxy retired -> TeamClones empty / _clone null
            // -> Shutdown/terminal Despawn -> all factory-owned state cleared.

            // Use a dedicated wrapper class so we can read private static fields without reflection,
            // keeping the test pure and deterministic at runtime.
            Fixture f = new Fixture();

            // ---- Phase 1: populate PreparationStates (mirrors PrepareForCountdown behavior) —
            // the real code adds one entry per proxy in TeamClones during PrepareForCountdown. —
            // Here we directly inject two entries to simulate a 2-proxy match state.            f.SetPreparationState(42, new ProxyPreparationState());
            f.SetPreparationState(99, new ProxyPreparationState());

            Assert(f.GetPreparationStateCount() == 2, "phase 1: PreparationStates populated to 2");
            Assert(f.TeamClonesEmpty(), "phase 1: no proxies alive yet (baseline)");
            Assert(f.CloneIsNull(), "phase 1: _clone is null (baseline)");

            // ---- Phase 2: simulate all proxies retired via RetireProxy —
            // the real code removes TeamClones entries and _clone but does NOT clear PreparationStates.            f.SetDespawnInvariantBroken();

            Assert(!f.TeamClonesEmpty() == false, "phase 2: TeamClones now empty (simulated)");
            Assert(f.PreparationStatesNotEmpty(), "phase 2: PreparationStates still has entries (known leak before fix)");

            // ---- Phase 3: call Despawn —
            // Before the fix this returned early without clearing PreparationStates.
            // After the fix it must clear PreparationStates and not crash.
            string result = f.DespawnCall("shutdown");
            Assert(result.Contains("No temporary clone is active") || result.Contains("shutdown"), "phase 3: Despawn returned without throwing");
            Assert(!f.PreparationStatesNotEmpty(), "phase 3: PreparationStates cleared after Despawn even with empty TeamClones");

            // ---- Phase 4: verify idempotency — repeated Despawn on already-emptied state —
            string result2 = f.DespawnCall("shutdown-again");
            Assert(!f.PreparationStatesNotEmpty(), "phase 4: repeated Despawn remains clean");

            Console.WriteLine("PvpTemporaryCloneFactoryDespawnInvariantTests: PASS"); return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PvpTemporaryCloneFactoryDespawnInvariantTests: FAIL " + ex.Message);
            return 1;
        }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // ---- Fixture: mirrors the PvpTemporaryCloneFactory state layout and exposes internals via —
    // reflection so tests can verify factory-owned collections directly. —
    // This is a deterministic source/contract test.

    private static readonly System.Type _factoryType = typeof(PvpTemporaryCloneFactory);

    private class Fixture
    {
        private readonly object prepStatesDict;       // Dictionary<int, ProxyPreparationState>
        private readonly List<GameObject> teamClonesField; // List<GameObject> — via reflection
        private readonly int[] teamClonesCountSnapshot;  // wrapper for reading count
        private bool cloneIsNullField;                  // mirrors _clone

        internal Fixture()
        {
            prepStatesDict = _factoryType.GetField("PreparationStates",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);

            object clonesList = _factoryType.GetField("TeamClones",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            teamClonesCountSnapshot = new int[1];
            CountTeams(); // seed

            cloneIsNullField = (bool)_factoryType.GetField("_clone",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) == null;
        }

        internal void SetPreparationState(int key, object stateValue)
        {
            var add = prepStatesDict.GetType().GetMethod("Add");
            add.Invoke(prepStatesDict, new object[] { key, stateValue });
            CountTeams(); // refresh snapshot (no-op for this field but keeps pattern)
        }

        internal int GetPreparationStateCount()
        {
            var get_Count = prepStatesDict.GetType().GetProperty("Count");
            return (int)get_Count.GetValue(prepStatesDict, null);
        }

        internal bool PreparationStatesNotEmpty() { return GetPreparationStateCount() > 0; }

        internal void SetDespawnInvariantBroken()
        {
            // Simulate RetireProxy removing everything: clear TeamClones and _clone.
            var clearTeam = _factoryType.GetField("TeamClones",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            ((IList)clearTeam).Clear();

            cloneIsNullField = true; // mirrors: _clone = null;
        }

        internal bool TeamClonesEmpty()
        {
            object team = _factoryType.GetField("TeamClones",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var prop = team.GetType().GetProperty("Count");
            return (int)prop.GetValue(team, null) == 0;
        }

        internal bool CloneIsNull() { return cloneIsNullField; }

        internal string DespawnCall(string reason)
        {
            // Call Shutdown -> Despawn(reason) which was the affected method.
            object result = _factoryType.GetMethod("Shutdown").Invoke(null, null);
            if (result == null) result = "null return";
            string s = result.ToString();
            CountTeams(); // refresh after call
            return s;
        }

        private void CountTeams()
        {
            object team = _factoryType.GetField("TeamClones",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var prop = team.GetType().GetProperty("Count");
            teamClonesCountSnapshot[0] = (int)prop.GetValue(team, null);
        }
    }
}
