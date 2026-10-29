using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WrathLifecycleFix
{
    internal static class CaptureRewriter
    {
        internal static bool IsExpectedSubscribe(MethodInfo method)
        {
            if (method == null || !method.IsStatic || !method.IsGenericMethod || method.ReturnType != typeof(IDisposable) ||
                method.Name != "Subscribe" || method.DeclaringType.FullName != "UniRx.ObservableExtensions") return false;
            ParameterInfo[] parameters = method.GetParameters();
            Type[] arguments = method.GetGenericArguments();
            return arguments.Length == 1 && arguments[0].FullName == "UniRx.Unit" && parameters.Length == 2 &&
                   parameters[1].ParameterType.IsGenericType && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<>) &&
                   parameters[1].ParameterType.GetGenericArguments()[0] == arguments[0] &&
                   parameters[0].ParameterType.IsGenericType && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(IObservable<>) &&
                   parameters[0].ParameterType.GetGenericArguments()[0] == arguments[0];
        }

        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, MethodInfo capture, out bool applied)
        {
            // Do not mutate an existing CodeInstruction's labels/exception blocks or operands.
            var code = input.ToList();
            var matches = new List<int>();
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Call && IsExpectedSubscribe(code[i].operand as MethodInfo)) matches.Add(i);
            applied = false;
            if (matches.Count != 1 || capture == null) return code;
            int index = matches[0];
            int next = index + 1;
            while (next < code.Count && code[next].opcode == OpCodes.Nop) next++;
            if (next >= code.Count || code[next].opcode != OpCodes.Pop) return code;
            var signature = capture.GetParameters();
            if (!capture.IsStatic || capture.ReturnType != typeof(IDisposable) || signature.Length != 2 ||
                signature[0].ParameterType != typeof(IDisposable) || signature[1].ParameterType != typeof(object)) return code;
            code.InsertRange(index + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, capture) });
            applied = true;
            return code;
        }
    }
}
