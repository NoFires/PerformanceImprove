using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PerformanceImprove.Merchant
{
    internal static class SaleRewriter
    {
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, MethodInfo transfer, MethodInfo redirect, out bool applied)
        {
            var code = input.Select(c => new CodeInstruction(c)).ToList();
            applied = false;
            var calls = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(transfer)).ToArray();
            if (calls.Length != 2 || !redirect.IsStatic || redirect.ReturnType != transfer.ReturnType) return code;
            var expected = new[] { transfer.DeclaringType }.Concat(transfer.GetParameters().Select(p => p.ParameterType));
            if (!redirect.GetParameters().Select(p => p.ParameterType).SequenceEqual(expected)) return code;
            int first = calls[0], after = first + 1;
            while (after < code.Count && code[after].opcode == OpCodes.Nop) after++;
            if (after >= code.Count || code[after].opcode != OpCodes.Pop) return code;
            if (!code.Take(first).Any(c => c.operand is MethodInfo m && m.Name == "GainMoney") ||
                !code.Take(first).Any(c => c.operand is MethodInfo m && m.Name == "set_SellTime")) return code;
            code[first].opcode = OpCodes.Call;
            code[first].operand = redirect;
            applied = true;
            return code;
        }
    }
}
