using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ErenshorPvP
{
    // Developer-only, read-only ItemDB observer. This code deliberately has no SetActive, field
    // write, Inventory mutation, or PvP visual pipeline dependency: it only reports native data.
    internal static class PvpItemDbVisualObserver
    {
        private const int MaxIds = 2;
        private const int MaxCandidatesPerBranch = 3;
        private const int MaxOutputChars = 3500;

        internal static string Inspect(string command)
        {
            List<string> ids = ParseIds(command);
            if (ids.Count == 0) return "[Erenshor PvP] Usage: /epvp itemdiag <itemId>|legs";
            StringBuilder output = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                Item item = null;
                try { if (GameData.ItemDB != null) item = GameData.ItemDB.GetItemByID(ids[i]); } catch { }
                if (item == null)
                {
                    Append(output, "itemId=" + ids[i] + "; result=missing-or-itemdb-unavailable");
                    continue;
                }
                string activation = item.EquipmentToActivate ?? string.Empty;
                Append(output, "itemId=" + Safe(item.Id, ids[i]) + "; name=" + Safe(item.ItemName, "(unnamed)") +
                    "; slot=" + item.RequiredSlot + "; activation=" + Safe(activation, "(empty)") + "; level=" + item.ItemLevel);
                Append(output, "trims=shoulder(" + Safe(item.ShoulderTrimL, "-") + "," + Safe(item.ShoulderTrimR, "-") +
                    "); elbow(" + Safe(item.ElbowTrimL, "-") + "," + Safe(item.ElbowTrimR, "-") + "); knee(" +
                    Safe(item.KneeTrimL, "-") + "," + Safe(item.KneeTrimR, "-") + ")");
                ReportBranches(output, Safe(item.Id, ids[i]), activation);
                ReportNormalSimOracle(output, item);
            }
            return output.ToString();
        }

        internal static void LogShellBeforeMaterialization(ModularParts parts, PvpOpponentProfile profile, Inventory inventory, Item item)
        {
            if (parts == null || profile == null || item == null) return;
            Transform root = GameData.PlayerModularParent == null ? null : GameData.PlayerModularParent.transform;
            string node = item.EquipmentToActivate ?? string.Empty;
            bool male = HasCandidate(FindExactNamedDescendant(root, "Male_Parts"), node);
            bool female = HasCandidate(FindExactNamedDescendant(root, "Female_Parts"), node);
            bool all = HasCandidate(FindExactNamedDescendant(root, "All_Gender_Parts"), node);
            PvpDiagnostics.Log("pvp_visual_gender_state profile=" + profile.Name + "; profileGender=" + profile.Gender + "; modularGender=" + parts.Gender + "; presentationInventoryIsMale=" + (inventory != null && inventory.isMale) + "; expectedBranch=" + (inventory != null && inventory.isMale ? "Male" : "Female") + "; expectedNode=" + node + "; maleCandidateExists=" + male + "; femaleCandidateExists=" + female + "; allGenderCandidateExists=" + all);
        }

        internal static void LogShellAfterMaterialization(ModularParts parts, PvpOpponentProfile profile, Inventory inventory, Item item)
        {
            if (parts == null || profile == null || item == null) return;
            Transform node = parts.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x != null && x.name == item.EquipmentToActivate);
            Renderer renderer = node == null ? null : node.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x != null);
            SkinnedMeshRenderer skin = node == null ? null : node.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
            MeshFilter filter = node == null ? null : node.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
            string mesh = skin != null ? skin.sharedMesh.name : (filter != null ? filter.sharedMesh.name : "(none)");
            bool active = node != null && node.gameObject.activeInHierarchy;
            PvpDiagnostics.Log("pvp_leg_native_result profile=" + profile.Name + "; itemId=" + item.Id + "; expectedNode=" + item.EquipmentToActivate + "; expectedBranch=" + (inventory != null && inventory.isMale ? "Male" : "Female") + "; candidateExists=" + (node != null) + "; candidateActive=" + active + "; rendererEnabled=" + (renderer != null && renderer.enabled) + "; mesh=" + mesh + "; fullPath=" + FullPath(node));
        }

        private static List<string> ParseIds(string command)
        {
            string tail = (command ?? string.Empty).Substring(Math.Min((command ?? string.Empty).Length, "itemdiag".Length)).Trim();
            if (tail.Equals("legs", StringComparison.OrdinalIgnoreCase)) return new List<string> { "2622680", "8454409" };
            if (string.IsNullOrEmpty(tail) || tail.IndexOf(' ') >= 0) return new List<string>();
            return new List<string> { tail };
        }

        private static void ReportBranches(StringBuilder output, string itemId, string activation)
        {
            if (GameData.PlayerModularParent == null)
            {
                Append(output, "modular=unavailable");
                return;
            }
            Transform parent = GameData.PlayerModularParent.transform;
            ReportBranch(output, itemId, "Male", FindExactNamedDescendant(parent, "Male_Parts"), activation);
            ReportBranch(output, itemId, "Female", FindExactNamedDescendant(parent, "Female_Parts"), activation);
            ReportBranch(output, itemId, "AllGender", FindExactNamedDescendant(parent, "All_Gender_Parts"), activation);
        }

        private static void ReportBranch(StringBuilder output, string itemId, string branch, Transform root, string activation)
        {
            if (root == null) { Append(output, branch + " candidates=unavailable"); return; }
            Transform[] matches = string.IsNullOrEmpty(activation) ? new Transform[0] : root.GetComponentsInChildren<Transform>(true)
                .Where(x => x != null && x.name == activation).Take(MaxCandidatesPerBranch).ToArray();
            if (matches.Length == 0) { Append(output, branch + " candidates=none"); return; }
            for (int i = 0; i < matches.Length; i++)
            {
                Renderer renderer = matches[i].GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x != null);
                SkinnedMeshRenderer skinned = matches[i].GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
                MeshFilter filter = matches[i].GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
                string mesh = skinned != null ? skinned.sharedMesh.name : (filter != null ? filter.sharedMesh.name : "(none)");
                Append(output, "item_visual_candidate itemId=" + itemId + "; requestedBranch=" + branch + "; actualBranch=" + branch + "; fullPath=" + FullPath(matches[i]) + "; node=" + matches[i].name + "; renderer=" + (renderer != null) + "; mesh=" + mesh);
            }
        }

        private static void ReportNormalSimOracle(StringBuilder output, Item item)
        {
            SimPlayer[] sims = UnityEngine.Object.FindObjectsOfType<SimPlayer>();
            bool female = false;
            bool male = false;
            for (int i = 0; sims != null && i < sims.Length && i < 10; i++)
            {
                SimPlayer sim = sims[i];
                if (sim == null) continue;
                Inventory inventory = sim.GetComponentInParent<Inventory>();
                ModularParts parts = sim.GetComponentInChildren<ModularParts>(true);
                if (inventory == null || parts == null) continue;
                bool activeNode = parts.GetComponentsInChildren<Transform>(true).Any(x => x != null && x.name == item.EquipmentToActivate && x.gameObject.activeInHierarchy);
                if (!activeNode) continue;
                string prefix = inventory.isMale ? "normalMaleOracle=" : "normalFemaleOracle=";
                if (inventory.isMale && male) continue;
                if (!inventory.isMale && female) continue;
                Transform active = parts.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x != null && x.name == item.EquipmentToActivate && x.gameObject.activeInHierarchy);
                SkinnedMeshRenderer skinned = active == null ? null : active.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
                MeshFilter filter = active == null ? null : active.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(x => x != null && x.sharedMesh != null);
                string mesh = skinned != null ? skinned.sharedMesh.name : (filter != null ? filter.sharedMesh.name : "(none)");
                Append(output, prefix + "available; sim=" + sim.name + "; isMale=" + inventory.isMale + "; itemId=" + item.Id + "; itemName=" + Safe(item.ItemName, "(unnamed)") + "; EquipmentToActivate=" + item.EquipmentToActivate + "; activeNode=" + item.EquipmentToActivate + "; fullPath=" + FullPath(active) + "; mesh=" + mesh);
                if (inventory.isMale) male = true; else female = true;
            }
            if (!female) Append(output, "normalFemaleOracle=unavailable");
            if (!male) Append(output, "normalMaleOracle=unavailable");
        }

        private static void Append(StringBuilder output, string line)
        {
            if (output == null || output.Length >= MaxOutputChars) return;
            if (output.Length > 0) output.Append("\n");
            output.Append(line.Length > 700 ? line.Substring(0, 700) : line);
        }

        private static Transform FindExactNamedDescendant(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            return all.FirstOrDefault(x => x != null && x.name == name);
        }

        private static bool HasCandidate(Transform root, string activation)
        {
            return root != null && !string.IsNullOrEmpty(activation) && root.GetComponentsInChildren<Transform>(true).Any(x => x != null && x.name == activation);
        }

        private static string FullPath(Transform value)
        {
            if (value == null) return "(none)";
            List<string> parts = new List<string>();
            Transform current = value;
            while (current != null && parts.Count < 16) { parts.Add(current.name); current = current.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string Safe(string value, string fallback) { return string.IsNullOrEmpty(value) ? fallback : value.Replace("\n", " ").Replace("\r", " "); }
    }
}
