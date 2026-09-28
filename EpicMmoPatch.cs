using System;
using System.Reflection;
using EpicMMOSystem;
using HarmonyLib;

namespace PerspexModpackPatcher;

internal static class EpicMmoPatch
{
    private static readonly FieldInfo Levels = AccessTools.Field(typeof(LevelSystem), "levelsExp");

    internal static void Install(Harmony harmony)
    {
        if (Levels == null) throw new MissingFieldException("EpicMMO level experience table missing");
        harmony.Patch(AccessTools.Method(typeof(LevelSystem), "getNeedExp"),
            prefix: new HarmonyMethod(typeof(EpicMmoPatch), nameof(EnsureLevels)));
    }

    private static void EnsureLevels(LevelSystem __instance)
    {
        if (Levels.GetValue(null) == null) __instance.FillLevelsExp();
    }
}
