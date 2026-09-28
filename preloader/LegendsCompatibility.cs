using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PerspexModpackPatcher.Preloader;

// BepInEx discovers this in BepInEx/patchers before Chainloader.Start loads plugins.
public static class LegendsCompatibility
{
    private const string PublishedHash = "6D57A47434FAE8BDA9C71379189E15731C996512A2CCA12F73E747F61A9048F8";
    public static IEnumerable<string> TargetDLLs => Array.Empty<string>();
    public static void Patch(AssemblyDefinition _) { }

    public static void Initialize()
    {
        new Harmony("twentyonez.perspex.legends.preloader").Patch(
            AccessTools.Method(typeof(Chainloader), "Start"),
            transpiler: new HarmonyMethod(typeof(LegendsCompatibility), nameof(RedirectPluginLoad)));
    }

    public static IEnumerable<CodeInstruction> RedirectPluginLoad(IEnumerable<CodeInstruction> source)
    {
        var instructions = source.ToList();
        var original = AccessTools.Method(typeof(Assembly), nameof(Assembly.LoadFile), new[] { typeof(string) });
        var replacement = AccessTools.Method(typeof(LegendsCompatibility), nameof(LoadPlugin));
        var matches = instructions.Where(i => i.Calls(original)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("BepInEx plugin loader changed; cannot intercept Legends safely.");
        matches[0].operand = replacement;
        return instructions;
    }

    public static Assembly LoadPlugin(string path)
    {
        if (!string.Equals(Path.GetFileName(path), "ValheimLegends.dll", StringComparison.OrdinalIgnoreCase))
            return Assembly.LoadFile(path);

        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(actual, PublishedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Perspex expects the published Dekas Valheim Legends 0.7.10 DLL; found SHA256 " + actual);
        }

        using (var legends = AssemblyDefinition.ReadAssembly(path))
        using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(Paths.ManagedPath, "assembly_valheim.dll")))
        {
            var counts = Rewrite(legends, game);
            using (var buffer = new MemoryStream())
            {
                legends.Write(buffer);
                BepInEx.Logging.Logger.CreateLogSource("Perspex Legends compatibility")
                    .LogInfo($"Patched Dekas Legends in memory: {counts.status} status, {counts.message} message, {counts.effect} effect calls.");
                return Assembly.Load(buffer.ToArray());
            }
        }
    }

    public static (int status, int message, int effect) Rewrite(AssemblyDefinition legends, AssemblyDefinition game)
    {
        var module = legends.MainModule;
        var gameTypes = game.MainModule.Types.ToDictionary(t => t.FullName);
        MethodDefinition Current(string type, string name, int count, string first) =>
            gameTypes[type].Methods.Single(m => m.Name == name && m.Parameters.Count == count &&
                m.Parameters[0].ParameterType.FullName == first);

        var statusEffect = module.ImportReference(Current("SEMan", "AddStatusEffect", 5, "StatusEffect"));
        var statusHash = module.ImportReference(Current("SEMan", "AddStatusEffect", 5, "System.Int32"));
        var message = module.ImportReference(Current("Character", "Message", 5, "MessageHud/MessageType"));
        var effect = module.ImportReference(Current("EffectList", "Create", 6, "UnityEngine.Vector3"));
        var zdoId = effect.Parameters[5].ParameterType;
        var counts = (status: 0, message: 0, effect: 0);

        foreach (var method in AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            var processor = method.Body.GetILProcessor();
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                if (!(instruction.Operand is MethodReference old)) continue;
                if (old.DeclaringType.FullName == "SEMan" && old.Name == "AddStatusEffect" && old.Parameters.Count == 4)
                {
                    processor.InsertBefore(instruction, processor.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_M1));
                    instruction.Operand = old.Parameters[0].ParameterType.FullName == "StatusEffect" ? statusEffect : statusHash;
                    counts.status++;
                }
                else if (old.DeclaringType.FullName == "Character" && old.Name == "Message" && old.Parameters.Count == 4)
                {
                    processor.InsertBefore(instruction, processor.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                    instruction.Operand = message;
                    counts.message++;
                }
                else if (old.DeclaringType.FullName == "EffectList" && old.Name == "Create" && old.Parameters.Count == 5)
                {
                    var variant = new VariableDefinition(zdoId);
                    method.Body.Variables.Add(variant);
                    method.Body.InitLocals = true;
                    processor.InsertBefore(instruction, processor.Create(Mono.Cecil.Cil.OpCodes.Ldloca, variant));
                    processor.InsertBefore(instruction, processor.Create(Mono.Cecil.Cil.OpCodes.Initobj, zdoId));
                    processor.InsertBefore(instruction, processor.Create(Mono.Cecil.Cil.OpCodes.Ldloc, variant));
                    instruction.Operand = effect;
                    counts.effect++;
                }
            }
        }
        if (counts.status != 93 || counts.message != 92 || counts.effect != 4)
            throw new InvalidOperationException($"Unexpected Dekas Legends call sites: {counts}.");
        return counts;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }
}
