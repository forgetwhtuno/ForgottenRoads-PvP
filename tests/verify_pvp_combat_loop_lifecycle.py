#!/usr/bin/env python3
"""Prove the current Assembly-CSharp NPC combat-loop lifecycle used by PvP 0.5.15.

This validator intentionally inspects CLR metadata + method bodies directly with only the Python
standard library. It verifies the exact installed assembly contract behind the combat-loop recovery:

* NPC.Start gates BOTH NavUpdate(float) and BehaviorUpdate(float) launches on NeverAggro.
* NPC.Update does not substitute for either coroutine or directly invoke Combat.
* BehaviorUpdate's generated iterator reaches DoNonRaidBehavior / DoRaidBehavior.
* DoNonRaidBehavior reaches CheckHeals and Combat.
* Combat reaches native skill/spell/melee decision methods.
* NavUpdate's generated iterator reaches UpdateNav.

It does not claim those methods execute successfully in a live match; that remains an in-game gate.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def locate_assembly(explicit: str | None) -> Path:
    candidates = []
    if explicit:
        candidates.append(Path(explicit))
    candidates.extend([
        ROOT / "refs" / "Assembly-CSharp.dll",
        ROOT.parent.parent / "ErenshorSuiteHub" / "refs" / "Assembly-CSharp.dll",
    ])
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    raise SystemExit("Assembly-CSharp.dll not found. Pass --assembly PATH.")


def parse_surface(path: Path):
    data = path.read_bytes()
    u16 = lambda o: struct.unpack_from("<H", data, o)[0]
    u32 = lambda o: struct.unpack_from("<I", data, o)[0]
    u64 = lambda o: struct.unpack_from("<Q", data, o)[0]

    pe = u32(0x3C)
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("not a PE image")
    coff = pe + 4
    section_count = u16(coff + 2)
    optional_size = u16(coff + 16)
    optional = coff + 20
    magic = u16(optional)
    data_dirs = optional + (96 if magic == 0x10B else 112)
    cli_rva = u32(data_dirs + 14 * 8)
    if not cli_rva:
        raise ValueError("CLI directory missing")

    section_table = optional + optional_size
    sections = []
    for index in range(section_count):
        row = section_table + index * 40
        virtual_size = u32(row + 8)
        virtual_address = u32(row + 12)
        raw_size = u32(row + 16)
        raw_pointer = u32(row + 20)
        sections.append((virtual_address, max(virtual_size, raw_size), raw_pointer))

    def rva_offset(rva: int) -> int:
        for virtual_address, size, raw_pointer in sections:
            if virtual_address <= rva < virtual_address + size:
                return raw_pointer + (rva - virtual_address)
        raise ValueError(f"RVA 0x{rva:x} does not map to a section")

    cli = rva_offset(cli_rva)
    metadata = rva_offset(u32(cli + 8))
    if data[metadata:metadata + 4] != b"BSJB":
        raise ValueError("CLR metadata signature missing")

    version_length = u32(metadata + 12)
    cursor = metadata + 16 + ((version_length + 3) // 4) * 4
    stream_count = u16(cursor + 2)
    cursor += 4
    streams = {}
    for _ in range(stream_count):
        offset = u32(cursor)
        size = u32(cursor + 4)
        name_start = cursor + 8
        name_end = data.index(b"\0", name_start)
        name = data[name_start:name_end].decode("ascii", errors="replace")
        name_bytes = ((name_end - name_start + 1 + 3) // 4) * 4
        cursor = name_start + name_bytes
        streams[name] = (metadata + offset, size)

    strings_offset, _ = streams["#Strings"]
    tables_offset, _ = streams.get("#~", streams.get("#-"))
    blob_offset, _ = streams["#Blob"]

    def string_at(index: int) -> str:
        if index == 0:
            return ""
        end = data.index(b"\0", strings_offset + index)
        return data[strings_offset + index:end].decode("utf-8", errors="replace")

    table = tables_offset
    heap_sizes = data[table + 6]
    valid_mask = u64(table + 8)
    cursor = table + 24
    row_counts = [0] * 64
    for index in range(64):
        if (valid_mask >> index) & 1:
            row_counts[index] = u32(cursor)
            cursor += 4

    string_size = 4 if heap_sizes & 0x01 else 2
    guid_size = 4 if heap_sizes & 0x02 else 2
    blob_size = 4 if heap_sizes & 0x04 else 2

    def table_index_size(index: int) -> int:
        return 2 if row_counts[index] < 65536 else 4

    def coded_index_size(tables, tag_bits: int) -> int:
        maximum = max(row_counts[index] for index in tables)
        return 2 if maximum < (1 << (16 - tag_bits)) else 4

    def read_index(offset: int, size: int) -> int:
        return struct.unpack_from("<H" if size == 2 else "<I", data, offset)[0]

    module_size = 2 + string_size + guid_size * 3
    type_ref_size = coded_index_size([0, 26, 35, 1], 2) + string_size * 2
    type_def_size = (
        4 + string_size * 2 + coded_index_size([2, 1, 27], 2)
        + table_index_size(4) + table_index_size(6)
    )
    field_ptr_size = table_index_size(4)
    field_size = 2 + string_size + blob_size
    method_ptr_size = table_index_size(6)
    method_def_size = 4 + 2 + 2 + string_size + blob_size + table_index_size(8)

    type_def_start = cursor + row_counts[0] * module_size + row_counts[1] * type_ref_size
    field_def_start = type_def_start + row_counts[2] * type_def_size + row_counts[3] * field_ptr_size
    method_def_start = field_def_start + row_counts[4] * field_size + row_counts[5] * method_ptr_size

    type_defs = []
    for index in range(row_counts[2]):
        row = type_def_start + index * type_def_size + 4
        name_index = read_index(row, string_size); row += string_size
        namespace_index = read_index(row, string_size); row += string_size
        row += coded_index_size([2, 1, 27], 2)
        field_list = read_index(row, table_index_size(4)); row += table_index_size(4)
        method_list = read_index(row, table_index_size(6))
        full_type = f"{string_at(namespace_index)}.{string_at(name_index)}" if string_at(namespace_index) else string_at(name_index)
        type_defs.append((full_type, field_list, method_list))

    raw_fields = []
    for index in range(row_counts[4]):
        row = field_def_start + index * field_size + 2
        name_index = read_index(row, string_size); row += string_size
        signature_index = read_index(row, blob_size)
        raw_fields.append((string_at(name_index), signature_index))

    raw_methods = []
    for index in range(row_counts[6]):
        row = method_def_start + index * method_def_size
        rva = u32(row); row += 4 + 2 + 2
        name_index = read_index(row, string_size); row += string_size
        signature_index = read_index(row, blob_size)
        raw_methods.append((string_at(name_index), signature_index, rva))

    def compressed_uint(buffer: bytes, offset: int):
        first = buffer[offset]
        if first & 0x80 == 0:
            return first, 1
        if first & 0xC0 == 0x80:
            return ((first & 0x3F) << 8) | buffer[offset + 1], 2
        return (((first & 0x1F) << 24) | (buffer[offset + 1] << 16)
                | (buffer[offset + 2] << 8) | buffer[offset + 3], 4)

    def blob_at(index: int) -> bytes:
        if index == 0:
            return b""
        length, encoded = compressed_uint(data, blob_offset + index)
        start = blob_offset + index + encoded
        return data[start:start + length]

    def parameter_count(signature: bytes) -> int:
        offset = 1
        if signature[0] & 0x10:
            _, size = compressed_uint(signature, offset); offset += size
        count, _ = compressed_uint(signature, offset)
        return count

    method_map = {}
    field_map = {}
    for type_index, (full_type, first_field, first_method) in enumerate(type_defs):
        next_field = type_defs[type_index + 1][1] if type_index + 1 < len(type_defs) else len(raw_fields) + 1
        next_method = type_defs[type_index + 1][2] if type_index + 1 < len(type_defs) else len(raw_methods) + 1
        for field_index in range(max(1, first_field), max(1, next_field)):
            if field_index - 1 < len(raw_fields):
                field_name, _ = raw_fields[field_index - 1]
                field_map[(full_type, field_name)] = 0x04000000 | field_index
        for method_index in range(max(1, first_method), max(1, next_method)):
            if method_index - 1 < len(raw_methods):
                method_name, signature_index, rva = raw_methods[method_index - 1]
                count = parameter_count(blob_at(signature_index))
                method_map[(full_type, method_name, count)] = (0x06000000 | method_index, rva)

    def method_body(rva: int) -> bytes:
        if not rva:
            return b""
        offset = rva_offset(rva)
        first = data[offset]
        if first & 0x03 == 0x02:  # tiny header
            size = first >> 2
            return data[offset + 1:offset + 1 + size]
        if first & 0x03 != 0x03:
            raise ValueError(f"unsupported method header 0x{first:02x} at RVA 0x{rva:x}")
        flags_and_size = u16(offset)
        header_bytes = ((flags_and_size >> 12) & 0x0F) * 4
        code_size = u32(offset + 4)
        return data[offset + header_bytes:offset + header_bytes + code_size]

    return method_map, field_map, method_body


def token_bytes(token: int) -> bytes:
    return struct.pack("<I", token)


def contains_call(body: bytes, token: int) -> bool:
    encoded = token_bytes(token)
    return (b"\x28" + encoded) in body or (b"\x6f" + encoded) in body


def contains_field(body: bytes, opcode: int, token: int) -> bool:
    return bytes([opcode]) + token_bytes(token) in body


def verify_source_ownership() -> int:
    factory_path = ROOT / "src" / "PvpTemporaryCloneFactory.cs"
    policy_path = ROOT / "src" / "PvpNativeCombatLoopPolicy.cs"
    controller_path = ROOT / "src" / "PvpController.cs"
    plugin_path = ROOT / "src" / "ErenshorPvPPlugin.cs"
    for path in (factory_path, policy_path, controller_path, plugin_path):
        if not path.is_file():
            raise SystemExit(f"missing source for ownership verification: {path}")
    factory = factory_path.read_text(encoding="utf-8")
    policy = policy_path.read_text(encoding="utf-8")
    controller = controller_path.read_text(encoding="utf-8")
    plugin = plugin_path.read_text(encoding="utf-8")
    checks = []

    def check(condition: bool, label: str) -> None:
        if not condition:
            raise AssertionError(label)
        checks.append(label)

    check('Dictionary<int, System.Collections.IEnumerator> ModOwnedNavLoops' in factory, 'nav ownership is per proxy')
    check('Dictionary<int, System.Collections.IEnumerator> ModOwnedBehaviorLoops' in factory, 'behavior ownership is per proxy')
    check(re.search(r'private\s+static(?:\s+readonly)?\s+System\.Collections\.IEnumerator\s+\w+\s*;', factory) is None, 'no shared static loop enumerator field')
    check('ownedStartCount == 0' in policy, 'policy forbids second owned startup')
    check('existingHandlePresent && !alreadyModOwned' in policy or '!existingHandlePresent && !alreadyModOwned' in policy, 'resident/native handle blocks startup')
    check('CounterValue(ModOwnedNavLoopStarts, id)' in factory and 'CounterValue(ModOwnedBehaviorLoopStarts, id)' in factory, 'per-proxy start counts consulted')
    check(factory.count('StartCoroutine(loop)') == 1, 'owned startup centralized')
    check('TryReadField(npc, fieldName, out existing) && existing != null' in factory, 'helper rechecks existing handle before startup')
    check('object.ReferenceEquals(current, loop)' in factory, 'failed startup clears only exact candidate handle')
    check('ownedLoops.Remove(id)' in factory, 'failed startup never retains ownership record')
    check(factory.count('StopCoroutine(owned)') == 2, 'cleanup stops exactly nav/behavior owned iterators')
    check('object.ReferenceEquals(current, owned)' in factory, 'cleanup clears native field only for exact owned iterator')
    check('StopAllCoroutines(' not in factory, 'cleanup never stops unrelated native coroutines')
    retire = re.search(r'private static void RetireProxy\(NPC npc\)([\s\S]*?)\n        internal static bool ValidateProxyRewardBoundary', factory)
    check(retire is not None and 'StopOwnedNativeCombatLoops(npc)' in retire.group(1), 'proxy retirement stops owned loops')
    check(retire is not None and 'ModOwnedNavLoopStarts.Remove(id)' in retire.group(1) and 'ModOwnedBehaviorLoopStarts.Remove(id)' in retire.group(1), 'proxy retirement clears start counts')
    despawn = re.search(r'internal static string Despawn\(string reason\)([\s\S]*?)\n        internal static void Shutdown', factory)
    check(despawn is not None and 'StopOwnedNativeCombatLoops(member.GetComponent<NPC>())' in despawn.group(1), 'match/zone/shutdown despawn stops owned loops')
    check('ModOwnedNavLoops.Clear(); ModOwnedBehaviorLoops.Clear(); ModOwnedNavLoopStarts.Clear(); ModOwnedBehaviorLoopStarts.Clear();' in factory, 'match cleanup resets ownership for second match')
    check('PvpTemporaryCloneFactory.Despawn("scene_transition")' in controller, 'zone transition routes through owned-loop cleanup')
    check('PvpTemporaryCloneFactory.Shutdown()' in controller, 'unload routes through owned-loop cleanup')
    check(factory.count('if (PvpCombatContainment.LethalFightActive) return true;') >= 2, 'behavior/nav admission is released only for active fight')
    for surface in ('HighPriorityNavUpdate', 'UpdateNav', 'DoStances', 'DoNonRaidBehavior', 'DoRaidBehavior'):
        check(f'[HarmonyPatch(typeof(NPC), "{surface}")]' in factory, f'pre-GO gate covers {surface}')
    check('npc.NeverAggro = true' in factory and 'npc.CurrentAggroTarget = null' in factory, 'pre-GO aggression and target remain held')
    check('PvpPreparationNavRuntime.TryNormalizeHeldAgent' in factory, 'pre-GO nav remains structurally held')
    check('state.CombatLoopInfrastructureReady' in factory and 'state.PreGoLoopBoundaryReady' in factory, 'countdown readiness requires loop infrastructure and inert boundary')
    check('AreAllProxiesReadyForGo' in factory and 'EvaluateAllProxiesReady(false, out reason)' in factory, 'GO readiness is read-only and cannot recover loops')
    check('PvpTemporaryCloneFactory.AreAllProxiesReadyForGo(out readiness)' in controller, 'controller GO path uses read-only readiness')
    check('npc.NeverAggro = false' not in factory, 'factory does not release NeverAggro early')
    check('AccessTools.Method(typeof(NPC), "Start"' not in factory and '.Start()' not in re.sub(r'//.*', '', factory), 'NPC.Start is never invoked manually')
    check('revision=pvp-0.5.34-preop-world-context-r1' in plugin, 'exact 0.5.34 revision marker present')
    check('PvpNativeCombatLoopPolicy.RunSelfTests()' in controller, 'runtime selftest includes loop policy')
    return len(checks)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", help="Path to current Assembly-CSharp.dll")
    args = parser.parse_args()
    assembly = locate_assembly(args.assembly)
    ownership_checks = verify_source_ownership()
    methods, fields, body_for = parse_surface(assembly)
    failures = []

    def method(type_name: str, name: str, params: int):
        value = methods.get((type_name, name, params))
        if value is None:
            failures.append(f"missing method {type_name}.{name}({params})")
            return 0, b""
        token, rva = value
        return token, body_for(rva)

    def field(type_name: str, name: str):
        value = fields.get((type_name, name))
        if value is None:
            failures.append(f"missing field {type_name}.{name}")
            return 0
        return value

    start_token, start_body = method("NPC", "Start", 0)
    nav_factory_token, _ = method("NPC", "NavUpdate", 1)
    behavior_factory_token, _ = method("NPC", "BehaviorUpdate", 1)
    update_token, update_body = method("NPC", "Update", 0)
    update_nav_token, _ = method("NPC", "UpdateNav", 0)
    stance_token, _ = method("NPC", "DoStances", 0)
    nonraid_token, nonraid_body = method("NPC", "DoNonRaidBehavior", 0)
    raid_token, _ = method("NPC", "DoRaidBehavior", 0)
    high_nav_token, _ = method("NPC", "HighPriorityNavUpdate", 1)
    combat_token, combat_body = method("NPC", "Combat", 0)
    heal_token, _ = method("NPC", "CheckHeals", 0)
    attack_skill_token, _ = method("NPC", "DoAttackSkill", 0)
    attack_spell_token, _ = method("NPC", "DoAttackSpell", 0)
    melee_token, _ = method("NPC", "PerformMeleeHit", 2)
    never_aggro = field("NPC", "NeverAggro")
    nav_do = field("NPC", "navDo")
    beh_do = field("NPC", "behDo")

    behavior_iter = next((t for t, _, _ in methods if "<BehaviorUpdate>d__" in t), None)
    nav_iter = next((t for t, _, _ in methods if "<NavUpdate>d__" in t), None)
    if behavior_iter is None:
        failures.append("missing NPC <BehaviorUpdate>d__ iterator type")
        behavior_move = b""
    else:
        _, behavior_move = method(behavior_iter, "MoveNext", 0)
    if nav_iter is None:
        failures.append("missing NPC <NavUpdate>d__ iterator type")
        nav_move = b""
    else:
        _, nav_move = method(nav_iter, "MoveNext", 0)

    # Current Start must read NeverAggro twice and conditionally skip both loop factories. We assert
    # the exact field/method tokens rather than searching strings in the assembly.
    never_pattern = b"\x7b" + token_bytes(never_aggro)
    never_offsets = []
    pos = 0
    while never_aggro and True:
        found = start_body.find(never_pattern, pos)
        if found < 0:
            break
        never_offsets.append(found)
        pos = found + 1
    if len(never_offsets) != 2:
        failures.append(f"NPC.Start expected exactly 2 NeverAggro reads, found {len(never_offsets)}")
    for offset in never_offsets:
        window = start_body[offset + len(never_pattern):offset + len(never_pattern) + 8]
        if b"\x2d" not in window and b"\x3a" not in window:  # brtrue.s / brtrue
            failures.append("NPC.Start NeverAggro read is not followed by a brtrue gate")
    if nav_factory_token and not contains_call(start_body, nav_factory_token):
        failures.append("NPC.Start no longer calls NavUpdate(float)")
    if behavior_factory_token and not contains_call(start_body, behavior_factory_token):
        failures.append("NPC.Start no longer calls BehaviorUpdate(float)")
    if nav_do and not contains_field(start_body, 0x7D, nav_do):  # stfld
        failures.append("NPC.Start no longer stores navDo")
    if beh_do and not contains_field(start_body, 0x7D, beh_do):
        failures.append("NPC.Start no longer stores behDo")

    for called_token, label in ((nav_factory_token, "NavUpdate(float)"), (behavior_factory_token, "BehaviorUpdate(float)"), (combat_token, "Combat")):
        if called_token and contains_call(update_body, called_token):
            failures.append(f"NPC.Update unexpectedly invokes {label}; lifecycle assumption changed")

    if stance_token and not contains_call(behavior_move, stance_token):
        failures.append("BehaviorUpdate iterator no longer reaches DoStances")
    if nonraid_token and not contains_call(behavior_move, nonraid_token):
        failures.append("BehaviorUpdate iterator no longer reaches DoNonRaidBehavior")
    if raid_token and not contains_call(behavior_move, raid_token):
        failures.append("BehaviorUpdate iterator no longer reaches DoRaidBehavior")
    if heal_token and not contains_call(nonraid_body, heal_token):
        failures.append("DoNonRaidBehavior no longer reaches CheckHeals")
    if combat_token and not contains_call(nonraid_body, combat_token):
        failures.append("DoNonRaidBehavior no longer reaches Combat")
    if high_nav_token and not contains_call(nav_move, high_nav_token):
        failures.append("NavUpdate iterator no longer reaches HighPriorityNavUpdate")
    if update_nav_token and not contains_call(nav_move, update_nav_token):
        failures.append("NavUpdate iterator no longer reaches UpdateNav")
    if attack_skill_token and not contains_call(combat_body, attack_skill_token):
        failures.append("Combat no longer reaches DoAttackSkill")
    if attack_spell_token and not contains_call(combat_body, attack_spell_token):
        failures.append("Combat no longer reaches DoAttackSpell")
    if melee_token and not contains_call(combat_body, melee_token):
        failures.append("Combat no longer reaches PerformMeleeHit")

    digest = hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
    print(f"assembly={assembly}")
    print(f"sha256={digest}")
    print(f"npc_start_neveraggro_gates={len(never_offsets)}")
    print(f"behavior_iterator={behavior_iter or 'MISSING'}")
    print(f"nav_iterator={nav_iter or 'MISSING'}")
    if failures:
        for failure in failures:
            print("FAIL", failure)
        raise SystemExit(1)
    print("verified_chain=NPC.Start -> NavUpdate/BehaviorUpdate; BehaviorUpdate -> DoStances/DoNonRaidBehavior/DoRaidBehavior -> CheckHeals/Combat -> skill/spell/melee; NavUpdate -> HighPriorityNavUpdate/UpdateNav")
    print("verified_negative=NPC.Update does not substitute for NavUpdate/BehaviorUpdate/Combat")
    print(f"ownership_source_checks={ownership_checks}")
    print(f"verify_pvp_combat_loop_lifecycle: PASS ({ownership_checks} ownership/source checks + current-assembly IL chain)")


if __name__ == "__main__":
    main()
