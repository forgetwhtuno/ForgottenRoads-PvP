using System;
using System.Reflection;

namespace ErenshorPvP
{
    // Current-assembly compatibility shim for native NPC cast cadence. Reflection discovery is
    // performed exactly once at type initialization. No lookup occurs from the combat hot path.
    internal static class PvpNativeSpellExecutionBridge
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo NpcSpellCooldownField = typeof(NPC).GetField("NPCSpellCooldown", InstanceFlags);
        private static readonly FieldInfo AttackSpellDelayField = typeof(NPC).GetField("atkSpellDelay", InstanceFlags);
        private static readonly FieldInfo HealCooldownField = typeof(NPC).GetField("healCD", InstanceFlags);
        private static readonly FieldInfo ForceSpellCooldownField = typeof(NPC).GetField("forceSpellCD", InstanceFlags);

        internal static bool AttackSurfaceReady { get { return NpcSpellCooldownField != null && AttackSpellDelayField != null; } }
        internal static bool HealSurfaceReady { get { return HealCooldownField != null; } }

        internal static string SurfaceStatus()
        {
            return "npc_spell_cooldown=" + (NpcSpellCooldownField != null ? "bound" : "missing") +
                "; attack_spell_delay=" + (AttackSpellDelayField != null ? "bound" : "missing") +
                "; heal_cooldown=" + (HealCooldownField != null ? "bound" : "missing") +
                "; force_spell_cooldown=" + (ForceSpellCooldownField != null ? "bound" : "missing");
        }

        // Current NPC.Start assigns ordinary NPCs a random initial NPCSpellCooldown (20..360).
        // A persistent SimPlayer bypasses that gate in NPC.DoAttackSpell, but the temporary PvP
        // actor must remain SimPlayer=false/ThisSim=null. Clear that *initial* non-Sim mismatch once
        // immediately after native Start/recovery. SpellVessel can set NPCSpellCooldown again after
        // a real cast; those later values are deliberately left native and SetAttackRanges decrements
        // them normally.
        internal static bool TryClearSyntheticStartupCooldown(NPC npc, bool temporaryProxy,
            out float previousNpcSpellCooldown, out string reason)
        {
            previousNpcSpellCooldown = 0f;
            reason = string.Empty;
            if (npc == null || !temporaryProxy) { reason = "not_temporary_proxy"; return false; }
            if (NpcSpellCooldownField == null) { reason = "reflection_surface_missing"; return false; }
            try
            {
                previousNpcSpellCooldown = Convert.ToSingle(NpcSpellCooldownField.GetValue(npc));
                if (PvpSpellExecutionPolicy.ShouldClearSyntheticNpcSpellCooldown(true, npc.SimPlayer, previousNpcSpellCooldown))
                {
                    NpcSpellCooldownField.SetValue(npc, 0f);
                    reason = "cleared_synthetic_startup_npc_spell_cooldown";
                }
                else reason = "native_startup_gate_unchanged";
                return true;
            }
            catch (Exception ex)
            {
                reason = "reflection_failed:" + ex.GetType().Name;
                return false;
            }
        }

        internal static bool TryReadAttackSpellState(NPC npc, out float npcSpellCooldown,
            out float attackSpellDelay, out float forceSpellCooldown, out string reason)
        {
            npcSpellCooldown = 0f;
            attackSpellDelay = 0f;
            forceSpellCooldown = 0f;
            reason = string.Empty;
            if (npc == null) { reason = "npc_missing"; return false; }
            if (!AttackSurfaceReady) { reason = "reflection_surface_missing"; return false; }
            try
            {
                npcSpellCooldown = Convert.ToSingle(NpcSpellCooldownField.GetValue(npc));
                attackSpellDelay = Convert.ToSingle(AttackSpellDelayField.GetValue(npc));
                if (ForceSpellCooldownField != null) forceSpellCooldown = Convert.ToSingle(ForceSpellCooldownField.GetValue(npc));
                reason = "native_state_observed";
                return true;
            }
            catch (Exception ex)
            {
                reason = "reflection_failed:" + ex.GetType().Name;
                return false;
            }
        }

        internal static bool TryReadHealCooldown(NPC npc, out float healCooldown)
        {
            healCooldown = 0f;
            if (npc == null || HealCooldownField == null) return false;
            try { healCooldown = Convert.ToSingle(HealCooldownField.GetValue(npc)); return true; }
            catch { return false; }
        }

        internal static bool TrySetNativeHealCooldown(NPC npc, Spell spell)
        {
            if (npc == null || spell == null || HealCooldownField == null) return false;
            try
            {
                // Current NPC.CheckHeals uses spell.Cooldown * 60 for the ordinary non-Druid path.
                // We use that conservative native cadence for the PvP-only allied-proxy bridge rather
                // than creating a separate rapid-cast timer. Self-heals continue through native AI.
                float cooldown = Math.Max(1f, spell.Cooldown * 60f);
                HealCooldownField.SetValue(npc, cooldown);
                return true;
            }
            catch { return false; }
        }
    }
}
