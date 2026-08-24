#!/usr/bin/env python3
"""Verify the current Erenshor managed surface used by Forgotten Roads PvP.

This intentionally uses only the Python standard library. It parses enough ECMA-335
metadata to enumerate TypeDef/MethodDef names and method parameter counts, so source
regression checks can be grounded against the actual Assembly-CSharp.dll even on a
machine without dotnet/Mono/PowerShell.
"""
from __future__ import annotations

import argparse
import hashlib
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
    raise SystemExit(
        "Assembly-CSharp.dll not found. Pass --assembly PATH or provide repo refs/Assembly-CSharp.dll."
    )


def read_metadata_methods(path: Path):
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
        raise ValueError("not a managed PE image (CLI directory missing)")

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
    metadata_rva = u32(cli + 8)
    metadata = rva_offset(metadata_rva)
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

    # Table row sizes needed to reach TypeDef (2) and MethodDef (6).
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
    method_def_start = (
        type_def_start + row_counts[2] * type_def_size + row_counts[3] * field_ptr_size
        + row_counts[4] * field_size + row_counts[5] * method_ptr_size
    )

    type_defs = []
    for index in range(row_counts[2]):
        row = type_def_start + index * type_def_size
        row += 4
        name_index = read_index(row, string_size)
        row += string_size
        namespace_index = read_index(row, string_size)
        row += string_size
        row += coded_index_size([2, 1, 27], 2)
        field_list = read_index(row, table_index_size(4))
        row += table_index_size(4)
        method_list = read_index(row, table_index_size(6))
        type_defs.append((string_at(namespace_index), string_at(name_index), field_list, method_list))

    field_def_start = type_def_start + row_counts[2] * type_def_size + row_counts[3] * field_ptr_size
    raw_fields = []
    for index in range(row_counts[4]):
        row = field_def_start + index * field_size
        row += 2
        name_index = read_index(row, string_size)
        row += string_size
        signature_index = read_index(row, blob_size)
        raw_fields.append((string_at(name_index), signature_index))

    raw_methods = []
    for index in range(row_counts[6]):
        row = method_def_start + index * method_def_size
        row += 4 + 2 + 2
        name_index = read_index(row, string_size)
        row += string_size
        signature_index = read_index(row, blob_size)
        raw_methods.append((string_at(name_index), signature_index))

    def compressed_uint(buffer: bytes, offset: int):
        first = buffer[offset]
        if first & 0x80 == 0:
            return first, 1
        if first & 0xC0 == 0x80:
            return ((first & 0x3F) << 8) | buffer[offset + 1], 2
        return (
            ((first & 0x1F) << 24) | (buffer[offset + 1] << 16)
            | (buffer[offset + 2] << 8) | buffer[offset + 3],
            4,
        )

    def blob_at(index: int) -> bytes:
        if index == 0:
            return b""
        length, encoded = compressed_uint(data, blob_offset + index)
        start = blob_offset + index + encoded
        return data[start:start + length]

    def parameter_count(signature: bytes) -> int:
        if not signature:
            raise ValueError("empty method signature")
        offset = 1  # calling convention
        if signature[0] & 0x10:  # GENERIC
            _, size = compressed_uint(signature, offset)
            offset += size
        count, _ = compressed_uint(signature, offset)
        return count

    result = {}
    field_result = {}
    bool_return_result = set()
    for type_index, (namespace, type_name, first_field, first_method) in enumerate(type_defs):
        next_field = type_defs[type_index + 1][2] if type_index + 1 < len(type_defs) else len(raw_fields) + 1
        next_method = type_defs[type_index + 1][3] if type_index + 1 < len(type_defs) else len(raw_methods) + 1
        full_type = f"{namespace}.{type_name}" if namespace else type_name
        for field_index in range(max(1, first_field), max(1, next_field)):
            if field_index - 1 >= len(raw_fields):
                continue
            field_name, signature_index = raw_fields[field_index - 1]
            signature = blob_at(signature_index)
            # FIELD signature 0x06 followed by the element type. The current PvP compatibility
            # fields are simple float32 fields (ELEMENT_TYPE_R4 = 0x0c).
            field_result[(full_type, field_name)] = signature[1] if len(signature) >= 2 and signature[0] == 0x06 else None
        for method_index in range(max(1, first_method), max(1, next_method)):
            if method_index - 1 >= len(raw_methods):
                continue
            method_name, signature_index = raw_methods[method_index - 1]
            signature = blob_at(signature_index)
            count = parameter_count(signature)
            result.setdefault((full_type, method_name), set()).add(count)
            offset = 1
            if signature and signature[0] & 0x10:
                _, size = compressed_uint(signature, offset)
                offset += size
            _, size = compressed_uint(signature, offset)
            offset += size
            if offset < len(signature) and signature[offset] == 0x02:  # ELEMENT_TYPE_BOOLEAN
                bool_return_result.add((full_type, method_name, count))
    return result, field_result, bool_return_result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", help="Path to current Assembly-CSharp.dll")
    args = parser.parse_args()
    assembly = locate_assembly(args.assembly)
    methods, fields, bool_returns = read_metadata_methods(assembly)

    exact = {
        ("TypeText", "CheckCommands"): {0},
        ("SimPlayer", "LoadAllSimData"): {0},
        ("NPC", "Start"): {0},
        ("NPC", "UpdateNav"): {0},
        ("NPC", "HandleMaintenaceAndCounters"): {0},
        ("NPC", "Update"): {0},
        ("NPC", "Combat"): {0},
        ("NPC", "PerformMeleeHit"): {2},
        ("NPC", "DoAttackSkill"): {0},
        ("NPC", "DoAttackSpell"): {0},
        ("NPC", "CheckHeals"): {0},
        ("NPC", "CheckHealsRaid"): {0},
        ("NPC", "CheckForBow"): {0},
        ("NPC", "CheckForWand"): {0},
        ("NPC", "SetAttackRanges"): {0},
        ("NPC", "AggroOn"): {1},
        ("NPC", "ForceAggroOn"): {1},
        ("NPC", "ManageAggro"): {2},
        ("Character", "DamageMe"): {7},
        ("Character", "MagicDamageMe"): {6},
        ("Character", "BleedDamageMe"): {3},
        ("Character", "DoDeath"): {0},
        ("Stats", "ReduceHP"): {4},
        ("Stats", "HealMe"): {1, 5},
        ("CastSpell", "StartSpell"): {2, 3, 4, 5},
        ("CastSpell", "StartSpellFromProc"): {5},
        ("CastSpell", "StartSpellNoAnim"): {3},
        ("CastSpell", "GetCurrentCast"): {0},
        ("CastSpell", "isCasting"): {0},
        ("SpellVessel", "FixedUpdate"): {0},
        ("SpellVessel", "CreateSpellChargeEffect"): {8},
        ("SpellVessel", "ResolveSpell"): {0},
        ("Stats", "AddStatusEffect"): {3, 4, 5},
        ("Stats", "AddStatusEffectNoChecks"): {4},
        ("Stats", "CountStatusEffects"): {0},
        ("Stats", "CheckForStatus"): {1},
    }

    failures = []
    for key, expected in exact.items():
        actual = methods.get(key)
        if actual != expected:
            failures.append(f"{key[0]}.{key[1]} expected params={sorted(expected)} actual={sorted(actual) if actual else 'MISSING'}")


    exact_float_fields = {
        ("NPC", "NPCSpellCooldown"),
        ("NPC", "atkSpellDelay"),
        ("NPC", "healCD"),
        ("NPC", "forceSpellCD"),
    }
    for key in sorted(exact_float_fields):
        actual_type = fields.get(key)
        if actual_type != 0x0C:
            failures.append(f"{key[0]}.{key[1]} expected float32 field type=0x0c actual={actual_type!r}")

    exact_field_types = {
        ("SpellVessel", "interruptable"): 0x02,
        ("SpellVessel", "SpellSource"): 0x12,
        ("SpellVessel", "targ"): 0x12,
        ("SpellVessel", "spell"): 0x12,
        ("Stats", "<StatusEffects>k__BackingField"): 0x1D,
    }
    for key, expected_type in sorted(exact_field_types.items()):
        actual_type = fields.get(key)
        if actual_type != expected_type:
            failures.append(f"{key[0]}.{key[1]} expected element type=0x{expected_type:02x} actual={actual_type!r}")

    expected_bool_casts = {
        ("CastSpell", "StartSpell", 2),
        ("CastSpell", "StartSpell", 3),
        ("CastSpell", "StartSpell", 4),
        ("CastSpell", "StartSpell", 5),
        ("CastSpell", "StartSpellFromProc", 5),
        ("CastSpell", "StartSpellNoAnim", 3),
    }
    for key in sorted(expected_bool_casts):
        if key not in bool_returns:
            failures.append(f"{key[0]}.{key[1]}({key[2]} params) expected bool return")

    if failures:
        print(f"assembly={assembly}")
        for failure in failures:
            print("FAIL", failure)
        raise SystemExit(1)

    digest = hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
    print(f"assembly={assembly}")
    print(f"sha256={digest}")
    print(f"verified_method_targets={len(exact)}")
    print(f"verified_float_fields={len(exact_float_fields)}")
    print(f"verified_bool_cast_overloads={len(expected_bool_casts)}")
    print(f"verified_effect_fields={len(exact_field_types)}")
    print(f"verified_targets={len(exact) + len(exact_float_fields) + len(expected_bool_casts) + len(exact_field_types)}")
    print("verify_current_assembly_surface: PASS")


if __name__ == "__main__":
    main()
