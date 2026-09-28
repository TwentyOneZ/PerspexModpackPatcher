using Mono.Cecil;

if (args.Length != 2) throw new ArgumentException("Usage: MergedPluginCheck <PerspexModpackPatcher.dll> <BepInEx core directory>");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(args[1]);
using var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
var types = assembly.MainModule.GetTypes().ToArray();
var plugins = types.Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "BepInEx.BepInPlugin"))
    .ToDictionary(t => (string)t.CustomAttributes.First(a => a.AttributeType.FullName == "BepInEx.BepInPlugin").ConstructorArguments[0].Value);
if (plugins.Count != 2 || !plugins.ContainsKey("twentyonez.perspex.patcher") ||
    !plugins.ContainsKey("TwentyOneZ.PerspexCharacterAuthority"))
    throw new Exception("Merged DLL must expose both BepInEx plugins.");
if (!plugins["TwentyOneZ.PerspexCharacterAuthority"].CustomAttributes.Any(a =>
        a.AttributeType.FullName == "BepInEx.BepInDependency" &&
        (string)a.ConstructorArguments[0].Value == "twentyonez.perspex.patcher"))
    throw new Exception("Authority plugin must load after the patcher.");
var patchTypes = types.Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    .GroupBy(EffectiveNamespace).ToDictionary(g => g.Key, g => g.Count());
if (!patchTypes.TryGetValue("PerspexModpackPatcher", out var gameplay) || gameplay == 0 ||
    !patchTypes.TryGetValue("PerspexCharacterAuthority", out var authority) || authority == 0 ||
    patchTypes.Count != 2)
    throw new Exception("Harmony patch classes must be isolated in the two plugin namespaces.");
if (assembly.MainModule.AssemblyReferences.Any(r => r.Name == "PerspexCharacterAuthority"))
    throw new Exception("Merged DLL still depends on the old authority assembly.");
Console.WriteLine($"Merged plugin verified: 2 BepInEx plugins, {gameplay} gameplay patches, {authority} authority patches.");

static string EffectiveNamespace(TypeDefinition type) =>
    string.IsNullOrEmpty(type.Namespace) && type.DeclaringType != null
        ? EffectiveNamespace(type.DeclaringType) : type.Namespace;
