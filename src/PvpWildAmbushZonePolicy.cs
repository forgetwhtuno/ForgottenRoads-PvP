using System;
using System.Collections.Generic;

namespace ErenshorPvP
{
    internal sealed class PvpWildAmbushZoneDecision
    {
        internal bool Playable;
        internal bool Protected;
        internal bool Allowed;
        internal string Source;
    }

    // Pure zone-level policy. Physical actor, collision, and NavMesh clearance remain owned by
    // PvpTemporaryCloneFactory after this admission decision succeeds.
    internal static class PvpWildAmbushZonePolicy
    {
        private static readonly HashSet<string> HardNonGameplayScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "menu", "loadscene", "characterselect"
        };

        // Current Erenshor has no whole-zone safe/sanctuary flag. These exact internal scene
        // identities and display aliases are the small conservative hard-safe registry.
        private static readonly HashSet<string> HardProtectedScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "tutorial", "islandtomb", "stowaway", "stowawaysstep", "azure", "portazure"
        };

        internal static PvpWildAmbushZoneDecision Evaluate(string scene, bool gameplayReady, bool zoning,
            bool pvpEnabled, bool ambushEnabled, bool coopBlocked, string configuredProtected,
            string explicitlyDisabled, string legacyExplicitlyEnabled)
        {
            PvpWildAmbushZoneDecision result = new PvpWildAmbushZoneDecision();
            string normalized = Normalize(scene);
            result.Playable = gameplayReady && !zoning && normalized.Length > 0 && !HardNonGameplayScenes.Contains(normalized);
            result.Protected = IsProtected(scene, configuredProtected);

            if (!result.Playable) result.Source = "non_gameplay_scene";
            else if (!pvpEnabled) result.Source = "pvp_disabled";
            else if (!ambushEnabled) result.Source = "ambush_disabled";
            else if (coopBlocked) result.Source = "coop_blocked";
            else if (result.Protected) result.Source = "protected_zone";
            else if (Contains(explicitlyDisabled, scene)) result.Source = "explicit_zone_disabled";
            else
            {
                result.Allowed = true;
                result.Source = Contains(legacyExplicitlyEnabled, scene) ? "explicit_zone_enabled" : "ordinary_adventure_zone";
            }
            return result;
        }

        internal static bool IsProtected(string scene, string configuredProtected)
        {
            string normalized = Normalize(scene);
            return normalized.Length == 0 || HardProtectedScenes.Contains(normalized) || Contains(configuredProtected, scene);
        }

        internal static bool IsHardNonGameplay(string scene)
        {
            string normalized = Normalize(scene);
            return normalized.Length == 0 || HardNonGameplayScenes.Contains(normalized);
        }

        internal static bool Contains(string csv, string scene)
        {
            string normalized = Normalize(scene);
            if (normalized.Length == 0) return false;
            foreach (string item in (csv ?? string.Empty).Split(','))
                if (Normalize(item) == normalized) return true;
            return false;
        }

        internal static string WithEntry(string csv, string scene, bool include)
        {
            string normalized = Normalize(scene);
            List<string> entries = new List<string>();
            foreach (string item in (csv ?? string.Empty).Split(','))
                if (!string.IsNullOrWhiteSpace(item) && Normalize(item) != normalized) entries.Add(item.Trim());
            if (include && normalized.Length > 0) entries.Add((scene ?? string.Empty).Trim());
            return string.Join(", ", entries.ToArray());
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            char[] result = new char[value.Length]; int count = 0;
            foreach (char c in value) if (char.IsLetterOrDigit(c)) result[count++] = char.ToLowerInvariant(c);
            return new string(result, 0, count);
        }
    }
}
