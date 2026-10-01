using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace PerspexModpackPatcher;

internal static class ResurrectionDialogPatch
{
    internal static void Install(Harmony harmony)
    {
        var resurrection = AccessTools.TypeByName("Resurrection.Resurrection");
        var activePopup = AccessTools.Field(resurrection, "activePopup");
        var popupType = AccessTools.Inner(resurrection, "ResurrectionPopup");
        var popupField = AccessTools.Field(popupType, "Popup");
        var target = AccessTools.Method(typeof(UnifiedPopup), nameof(UnifiedPopup.WasVisibleThisFrame));
        if (activePopup == null || popupField == null || target == null)
            throw new InvalidOperationException("Resurrection popup fields were not found.");

        var original = Harmony.GetPatchInfo(target)?.Postfixes
            .FirstOrDefault(patch => patch.owner == "org.bepinex.plugins.resurrection" &&
                           patch.PatchMethod.DeclaringType?.Name == "AllowChatInteractionInResurrectionDialog");
        if (original == null)
            throw new InvalidOperationException("Resurrection dialog postfix was not found.");

        harmony.Unpatch(target, original.PatchMethod);
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(ResurrectionDialogPatch), nameof(WasVisible)));
        activePopupField = activePopup;
        popupValueField = popupField;
    }

    private static System.Reflection.FieldInfo activePopupField;
    private static System.Reflection.FieldInfo popupValueField;
    private static readonly System.Reflection.FieldInfo InstanceField = AccessTools.Field(typeof(UnifiedPopup), "instance");
    private static readonly System.Reflection.FieldInfo StackField = AccessTools.Field(typeof(UnifiedPopup), "popupStack");

    private static void WasVisible(ref bool __result)
    {
        var active = activePopupField.GetValue(null);
        var dialog = active == null ? null : popupValueField.GetValue(active) as PopupBase;
        var popup = InstanceField.GetValue(null) as UnifiedPopup;
        var stack = popup == null ? null : StackField.GetValue(popup) as Stack<PopupBase>;
        if (dialog == null || stack == null) return;

        if (stack.Count == 0)
        {
            var player = Player.m_localPlayer;
            if (player != null && player.IsDead() && player.GetRagdoll() != null)
                UnifiedPopup.Push(dialog);
        }

        if (stack.Count > 0 && ReferenceEquals(stack.Peek(), dialog))
            __result = false;
    }
}
