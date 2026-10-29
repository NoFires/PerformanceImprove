using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WrathDeferredNpcViews
{
    internal static class ResourceRewriter
    {
        // Preserve the original selection, local, branches and exception blocks; adapt the first request only.
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, MethodInfo request,
            MethodInfo selector, bool spawning, out bool applied)
        {
            var code = input.Select(c => new CodeInstruction(c)).ToList();
            var matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(request)).ToArray();
            applied = false;
            // Both analysed methods have normal + legacy-override requests. Unexpected modifications fail closed.
            if (matches.Length != 2) return code;
            int index = matches[0] - 1;
            // Request(string, bool): insert before the original hold=false producer, not above that bool.
            if (index < 0 || code[index].opcode != OpCodes.Ldc_I4_0) return code;
            var additions = new List<CodeInstruction>();
            if (spawning)
            {
                additions.Add(new CodeInstruction(OpCodes.Ldarg_1));
                additions.Add(new CodeInstruction(OpCodes.Ldarg_2));
                additions.Add(new CodeInstruction(OpCodes.Ldarg_S, (byte)4));
            }
            else additions.Add(new CodeInstruction(OpCodes.Ldarg_0));
            additions.Add(new CodeInstruction(OpCodes.Call, selector));
            // A branch or EH boundary on the original request must enter the adaptation with its string stack.
            additions[0].labels.AddRange(code[index].labels); code[index].labels.Clear();
            // An EH boundary here is not part of the supported source layout.
            if (code[index].blocks.Count != 0 || code[matches[0]].blocks.Count != 0 || code[matches[0]].labels.Count != 0)
                return input.Select(c => new CodeInstruction(c)).ToList();
            code.InsertRange(index, additions);
            applied = true;
            return code;
        }
    }
}
