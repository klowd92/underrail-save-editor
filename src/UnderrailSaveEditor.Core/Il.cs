using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// Minimal IL reader. The game is obfuscated and its member names change with every build, but the
    /// serialization keys inside GetObjectData ("PC1:E", "S4:V", ...) and calls to framework methods do not.
    /// Reading IL lets us find "the field written under key X" or "the method that calls List.Add".
    /// </summary>
    internal static class Il
    {
        internal struct Instruction
        {
            public OpCode OpCode;
            public int Token;
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(o => o.Value);

        public static List<Instruction> Read(MethodBase method)
        {
            var result = new List<Instruction>();
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null)
                return result;

            int pos = 0;
            while (pos < il.Length)
            {
                short value = il[pos++];
                if (value == 0xFE)
                    value = (short)(0xFE00 | il[pos++]);
                var op = OpCodesByValue[value];
                var ins = new Instruction { OpCode = op };
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        pos += 1;
                        break;
                    case OperandType.InlineVar:
                        pos += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        pos += 8;
                        break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(il, pos);
                        pos += 4 + 4 * count;
                        break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.InlineSig:
                        ins.Token = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        break;
                    default: // InlineBrTarget, InlineI, ShortInlineR
                        pos += 4;
                        break;
                }
                result.Add(ins);
            }
            return result;
        }

        /// <summary>Finds the field that <paramref name="type"/>'s own GetObjectData writes under <paramref name="key"/>.</summary>
        public static FieldInfo FieldForKey(Type type, string key)
        {
            var method = type.GetMethod("GetObjectData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null)
                throw new MissingMemberException($"{type.Name} declares no GetObjectData");

            var module = method.Module;
            var typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
            var code = Read(method);
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].OpCode != OpCodes.Ldstr || module.ResolveString(code[i].Token) != key)
                    continue;
                for (int j = i + 1; j < code.Count && j <= i + 6; j++)
                {
                    if (code[j].OpCode == OpCodes.Ldfld)
                        return module.ResolveField(code[j].Token, typeArgs, null);
                }
            }
            throw new MissingMemberException($"{type.Name}: no field is serialized under '{key}'");
        }

        /// <summary>Like <see cref="FieldForKey"/>, but searches <paramref name="type"/> and its base types.</summary>
        public static FieldInfo FieldForKeyInHierarchy(Type type, string key)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                try { return FieldForKey(t, key); }
                catch (MissingMemberException) { }
            }
            throw new MissingMemberException($"{type.Name}: no field is serialized under '{key}'");
        }

        /// <summary>
        /// Maps a constructor's parameters (0-based) to the fields it stores them in directly
        /// (the <c>ldarg.0; ldarg.N; stfld F</c> pattern).
        /// </summary>
        public static Dictionary<int, FieldInfo> ParameterFields(ConstructorInfo ctor)
        {
            var module = ctor.Module;
            var typeArgs = ctor.DeclaringType.IsGenericType ? ctor.DeclaringType.GetGenericArguments() : null;
            var il = ctor.GetMethodBody()?.GetILAsByteArray() ?? new byte[0];
            var result = new Dictionary<int, FieldInfo>();
            var code = Read(ctor);
            // Read() drops short operands, so re-decode ldarg forms from the raw opcode stream.
            var args = LoadedArguments(il);
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].OpCode != OpCodes.Stfld || !args.TryGetValue(i - 1, out int arg) || arg == 0)
                    continue;
                var field = module.ResolveField(code[i].Token, typeArgs, null);
                if (!result.ContainsKey(arg - 1))
                    result[arg - 1] = field;
            }
            return result;
        }

        /// <summary>Instruction index -> argument number, for every ldarg instruction.</summary>
        private static Dictionary<int, int> LoadedArguments(byte[] il)
        {
            var result = new Dictionary<int, int>();
            int pos = 0, index = 0;
            while (pos < il.Length)
            {
                short value = il[pos++];
                if (value == 0xFE)
                    value = (short)(0xFE00 | il[pos++]);
                var op = OpCodesByValue[value];
                if (op == OpCodes.Ldarg_0) result[index] = 0;
                else if (op == OpCodes.Ldarg_1) result[index] = 1;
                else if (op == OpCodes.Ldarg_2) result[index] = 2;
                else if (op == OpCodes.Ldarg_3) result[index] = 3;
                else if (op == OpCodes.Ldarg_S) result[index] = il[pos];
                else if (op == OpCodes.Ldarg) result[index] = BitConverter.ToInt16(il, pos);

                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: pos += 1; break;
                    case OperandType.InlineVar: pos += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: pos += 8; break;
                    case OperandType.InlineSwitch: pos += 4 + 4 * BitConverter.ToInt32(il, pos); break;
                    default: pos += 4; break;
                }
                index++;
            }
            return result;
        }

        /// <summary>True if <paramref name="method"/> loads the string literal <paramref name="literal"/>.</summary>
        public static bool LoadsString(MethodBase method, string literal)
        {
            var module = method.Module;
            return Read(method).Any(i => i.OpCode == OpCodes.Ldstr && module.ResolveString(i.Token) == literal);
        }

        /// <summary>True if <paramref name="method"/> calls a method matching <paramref name="predicate"/>.</summary>
        public static bool Calls(MethodBase method, Func<MethodBase, bool> predicate)
        {
            var module = method.Module;
            var typeArgs = method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            foreach (var ins in Read(method))
            {
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                    continue;
                MethodBase target;
                try { target = module.ResolveMethod(ins.Token, typeArgs, null); }
                catch (ArgumentException) { continue; }
                if (predicate(target))
                    return true;
            }
            return false;
        }
    }
}
