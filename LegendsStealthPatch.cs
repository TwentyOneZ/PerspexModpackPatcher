using HarmonyLib;

namespace PerspexModpackPatcher;

internal static class LegendsStealthPatch
{
    internal static void Install(Harmony harmony)
    {
        var overload = AccessTools.Method(typeof(BaseAI), "CanSenseTarget",
            new[] { typeof(Character), typeof(bool) });
        if (overload != null) harmony.Patch(overload,
            prefix: new HarmonyMethod(typeof(LegendsStealthPatch), nameof(Sense)));
    }

    private static bool Sense(Character target, ref bool __result)
    {
        if (target is Player player && player.IsCrouching() &&
            player.GetSEMan().HaveStatusEffect("SE_VL_ShadowStalk".GetStableHashCode()))
        {
            __result = false;
            return false;
        }
        return true;
    }
}
