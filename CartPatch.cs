using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PerspexModpackPatcher;

[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
internal static class CartPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var calls = code.Where(instruction => instruction.operand is MethodInfo method &&
            method.IsGenericMethod && method.Name == "GetComponent" &&
            method.DeclaringType == typeof(Component) &&
            method.GetGenericArguments().SequenceEqual(new[] { typeof(ZNetView) })).ToList();
        if (calls.Count != 1) return code;
        calls[0].operand = AccessTools.Method(typeof(CartPatch), nameof(FindView));
        return code;
    }

    private static ZNetView FindView(Component component)
    {
        var direct = component.GetComponent<ZNetView>();
        if (direct != null) return direct;
        return component is CraftingStation && component.name.EndsWith("_cart_craftingstation", StringComparison.Ordinal)
            ? component.GetComponentInParent<ZNetView>() : null;
    }
}
