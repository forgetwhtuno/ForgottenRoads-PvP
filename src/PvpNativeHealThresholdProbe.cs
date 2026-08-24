using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ErenshorPvP
{
    // Current NPC.CheckHeals is the authority for the self-heal threshold. The compatibility bridge
    // keeps the established 0.66 value, but selftest reports PASS only when an opcode-aware walk of
    // the currently loaded native CheckHeals body finds that value as an actual ldc.r4 instruction.
    // Boundary arithmetic at exactly 66% is intentionally not used as a proof because float
    // multiplication can round differently.
    internal static class PvpNativeHealThresholdProbe
    {
        private static readonly Dictionary<ushort, OpCode> _opCodes = BuildOpcodeTable();

        private static Dictionary<ushort, OpCode> BuildOpcodeTable()
        {
            Dictionary<ushort, OpCode> result = new Dictionary<ushort, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode)) continue;
                OpCode opCode = (OpCode)fields[i].GetValue(null);
                result[unchecked((ushort)opCode.Value)] = opCode;
            }
            return result;
        }

        private static bool TryReadOpcode(byte[] il, ref int offset, out OpCode opCode)
        {
            opCode = default(OpCode);
            if (il == null || offset < 0 || offset >= il.Length) return false;

            ushort value = il[offset++];
            if (value == 0xFE)
            {
                if (offset >= il.Length) return false;
                value = (ushort)(0xFE00 | il[offset++]);
            }

            return _opCodes.TryGetValue(value, out opCode);
        }

        private static int OperandSize(OperandType operandType, byte[] il, int operandOffset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    if (il == null || operandOffset < 0 || operandOffset + 4 > il.Length) return -1;
                    int count = BitConverter.ToInt32(il, operandOffset);
                    if (count < 0 || count > (il.Length - operandOffset - 4) / 4) return -1;
                    return 4 + (count * 4);
                default:
                    return -1;
            }
        }

        internal static bool TryVerifyCurrentNative(out float threshold, out string reason)
        {
            threshold = 0f;
            reason = "unverified";
            try
            {
                MethodInfo method = typeof(NPC).GetMethod("CheckHeals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (method == null) { reason = "CheckHeals_missing"; return false; }
                MethodBody body = method.GetMethodBody();
                byte[] il = body == null ? null : body.GetILAsByteArray();
                if (il == null || il.Length < 5) { reason = "CheckHeals_il_missing"; return false; }

                int offset = 0;
                while (offset < il.Length)
                {
                    OpCode opCode;
                    if (!TryReadOpcode(il, ref offset, out opCode))
                    {
                        reason = "CheckHeals_il_opcode_invalid";
                        return false;
                    }

                    int operandOffset = offset;
                    int operandSize = OperandSize(opCode.OperandType, il, operandOffset);
                    if (operandSize < 0 || operandOffset + operandSize > il.Length)
                    {
                        reason = "CheckHeals_il_operand_invalid";
                        return false;
                    }

                    if (opCode == OpCodes.Ldc_R4 && operandSize == 4)
                    {
                        float value = BitConverter.ToSingle(il, operandOffset);
                        if (Math.Abs(value - PvpNativeHealThresholdPolicy.ExpectedThreshold) < 0.0001f)
                        {
                            threshold = value;
                            reason = "NPC.CheckHeals ldc.r4=" + value.ToString("0.00");
                            return true;
                        }
                    }

                    offset += operandSize;
                }

                reason = "expected_threshold_not_found";
                return false;
            }
            catch (Exception ex)
            {
                reason = "probe_" + ex.GetType().Name;
                return false;
            }
        }

        internal static string RunSelfTest()
        {
            float threshold; string reason;
            if (!TryVerifyCurrentNative(out threshold, out reason)) return "FAIL native heal threshold: " + reason;
            return PvpNativeHealThresholdPolicy.BoundarySemantics(threshold)
                ? "PASS native heal threshold proven: " + reason
                : "FAIL native heal threshold semantics";
        }
    }
}
