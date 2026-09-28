using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PerspexModpackPatcher;

internal static class HotbarDiagnostics
{
    private static ConfigEntry<bool> enabled;
    private static ManualLogSource logger;
    private static readonly System.Reflection.MethodInfo takeInput = AccessTools.Method(typeof(Character), "TakeInput");
    private static readonly int[] usedFrame = new int[8];
    private static readonly bool[] physicalHeld = new bool[8];
    private static readonly bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static int reported;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    internal static void Configure(ConfigFile config, ManualLogSource log)
    {
        enabled = config.Bind("Diagnostics", "HotbarInput", false,
            "Log up to 64 physical presses of the 1-8 keys to diagnose hotbar input that stops working after load; disable after testing.");
        logger = log;
    }

    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    private static class UseHotbarItem
    {
        private static void Prefix(Player __instance, int index)
        {
            if (enabled.Value && __instance == Player.m_localPlayer && index >= 1 && index <= 8)
                usedFrame[index - 1] = Time.frameCount;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class PlayerUpdate
    {
        private static void Postfix(Player __instance)
        {
            if (!enabled.Value || __instance != Player.m_localPlayer || reported >= 64) return;
            for (var i = 0; i < 8; i++)
            {
                var key = (KeyCode)((int)KeyCode.Alpha1 + i);
                var physical = windows ? (GetAsyncKeyState(0x31 + i) & 0x8000) != 0 : Input.GetKey(key);
                var pressed = physical && !physicalHeld[i];
                physicalHeld[i] = physical;
                if (!pressed) continue;
                reported++;
                try
                {
                    var button = "Hotbar" + (i + 1);
                    var item = __instance.GetInventory()?.GetItemAt(i, 0);
                    logger.LogInfo($"Hotbar input #{reported} slot {i + 1} frame={Time.frameCount}: bound={ZInput.instance?.GetBoundKeyString(button)}, " +
                        $"focused={Application.isFocused}, unityDown={Input.GetKeyDown(key)}, unityHeld={Input.GetKey(key)}, " +
                        $"zinput={ZInput.GetButtonDown(button)}, alt={ZInput.GetButtonDown(button + "Alt")}, " +
                        $"takeInput={takeInput?.Invoke(__instance, null)}, itemPresent={item != null}, " +
                        $"useCalled={usedFrame[i] == Time.frameCount}.");
                }
                catch (System.Exception error) { logger.LogWarning("Hotbar diagnostic failed: " + error.Message); }
            }
        }
    }
}
