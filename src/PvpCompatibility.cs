using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorPvP
{
    internal static class PvpCompatibility
    {
        private static int _networkAssemblyCount = -1;
        private static Type _networkedPlayerType;
        private static Type _networkedSimType;

        internal static bool IsCoopSession()
        {
            try
            {
                ResolveNetworkTypes(true);
                if (_networkedPlayerType != null && UnityEngine.Object.FindObjectsOfType(_networkedPlayerType).Length > 0) return true;
                if (_networkedSimType != null && UnityEngine.Object.FindObjectsOfType(_networkedSimType).Length > 0) return true;
                return false;
            }
            catch { return true; }
        }

        internal static bool IsVerifiedHuntCampActive()
        {
            try
            {
                Type api = FindType("ErenshorCampmaster.CampmasterApi");
                PropertyInfo property = api == null ? null : api.GetProperty("IsHuntCampActive", BindingFlags.Public | BindingFlags.Static);
                return property != null && property.PropertyType == typeof(bool) && (bool)property.GetValue(null, null);
            }
            catch { return false; }
        }

        internal static bool IsRemoteHuman(SimPlayer sim)
        {
            if (sim == null) return true;
            try { return HasNetworkComponent(sim, true, false); }
            catch { return true; }
        }

        // Network ownership is an authority boundary, not a world-combat hostility decision.
        // Current Erenshor COOP exposes namespaced NetworkedPlayer / NetworkedSim components; PvP
        // protects either from local proxy mutation/aggro while still allowing ordinary local Sims.
        internal static bool IsNetworkOwnedActor(Component actor)
        {
            if (actor == null) return false;
            try { return HasNetworkComponent(actor, true, true); }
            catch { return true; }
        }

        private static bool HasNetworkComponent(Component component, bool includePlayers, bool includeSims)
        {
            ResolveNetworkTypes(false);
            if (includePlayers && _networkedPlayerType != null && component.GetComponent(_networkedPlayerType) != null) return true;
            if (includeSims && _networkedSimType != null && component.GetComponent(_networkedSimType) != null) return true;
            return false;
        }

        internal static bool IsPartyMember(SimPlayer sim)
        {
            try { return sim != null && sim.InGroup && GameData.SimPlayerGrouping != null && GameData.SimPlayerGrouping.IsSimInPlayerGroup(sim); }
            catch { return true; }
        }

        internal static bool IsSameScene(UnityEngine.Object value, Character player)
        {
            try
            {
                if (value == null || player == null || player.gameObject == null) return false;
                GameObject go = value as GameObject;
                if (go == null)
                {
                    Component component = value as Component;
                    go = component == null ? null : component.gameObject;
                }
                return go != null && go.scene.IsValid() && go.scene.isLoaded &&
                    go.scene.handle == SceneManager.GetActiveScene().handle;
            }
            catch { return false; }
        }

        internal static string ReadName(SimPlayer sim)
        {
            if (sim == null) return string.Empty;
            foreach (string name in new[] { "PlayerName", "MyName", "CharacterName", "CharName", "SimName", "Name" })
            {
                object value = ReadMember(sim, name);
                if (value is string && !string.IsNullOrWhiteSpace((string)value)) return ((string)value).Trim();
            }
            try { return sim.gameObject == null ? string.Empty : sim.gameObject.name; } catch { return string.Empty; }
        }

        internal static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            try
            {
                Type type = instance.GetType();
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(instance);
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property != null && property.CanRead ? property.GetValue(instance, null) : null;
            }
            catch { return null; }
        }

        internal static bool TryInt(object instance, string name, out int value)
        {
            value = 0; object raw = ReadMember(instance, name);
            if (raw == null) return false;
            try { value = Convert.ToInt32(raw); return true; } catch { return false; }
        }

        private static void ResolveNetworkTypes(bool refreshAssemblySet)
        {
            // Damage/aggro hooks are hot paths. Once the types are resolved, do not allocate an
            // AppDomain assembly snapshot on every effect. The low-frequency session check refreshes
            // the cache so a COOP plugin loaded after PvP is still discovered within one authority poll.
            if (!refreshAssemblySet && _networkAssemblyCount >= 0) return;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (_networkAssemblyCount == assemblies.Length) return;
            _networkAssemblyCount = assemblies.Length;
            _networkedPlayerType = FindType("ErenshorCoop.NetworkedPlayer", "NetworkedPlayer", assemblies);
            _networkedSimType = FindType("ErenshorCoop.NetworkedSim", "NetworkedSim", assemblies);
        }

        private static Type FindType(string fullName, string shortName, Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    Type exact = assembly.GetType(fullName, false);
                    if (exact != null) return exact;
                    string assemblyName = assembly.GetName().Name ?? string.Empty;
                    if (assemblyName.IndexOf("ErenshorCoop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (Type type in assembly.GetTypes())
                        if (type != null && string.Equals(type.Name, shortName, StringComparison.Ordinal)) return type;
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (ex.Types == null) continue;
                    foreach (Type type in ex.Types)
                        if (type != null && string.Equals(type.Name, shortName, StringComparison.Ordinal)) return type;
                }
                catch { }
            }
            return null;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                try { Type type = assembly.GetType(name, false); if (type != null) return type; } catch { }
            return null;
        }
    }
}
