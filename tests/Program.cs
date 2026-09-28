using Mono.Cecil;
using PerspexModpackPatcher.Preloader;

if (args.Length != 3) throw new ArgumentException("Usage: LegendsCompatibilityCheck <Legends.dll> <assembly_valheim.dll> <BepInEx.dll>");
using var legends = AssemblyDefinition.ReadAssembly(args[0]);
using var game = AssemblyDefinition.ReadAssembly(args[1]);
using var bepinex = AssemblyDefinition.ReadAssembly(args[2]);
var loader = bepinex.MainModule.Types.Single(t => t.FullName == "BepInEx.Bootstrap.Chainloader")
    .Methods.Single(m => m.Name == "Start" && m.Parameters.Count == 0);
if (loader.Body.Instructions.Count(i => i.Operand is MethodReference call &&
    call.DeclaringType.FullName == "System.Reflection.Assembly" && call.Name == "LoadFile") != 1)
    throw new InvalidOperationException("BepInEx plugin loading anchor changed.");

var counts = LegendsCompatibility.Rewrite(legends, game);
using var buffer = new MemoryStream();
legends.Write(buffer);
buffer.Position = 0;
using var result = AssemblyDefinition.ReadAssembly(buffer);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots) => roots.SelectMany(t =>
    new[] { t }.Concat(Types(t.NestedTypes)));
var stale = Types(result.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>()
    .Count(call => (call.DeclaringType.FullName == "SEMan" && call.Name == "AddStatusEffect" && call.Parameters.Count == 4) ||
        (call.DeclaringType.FullName == "Character" && call.Name == "Message" && call.Parameters.Count == 4) ||
        (call.DeclaringType.FullName == "EffectList" && call.Name == "Create" && call.Parameters.Count == 5));
if (stale != 0) throw new InvalidOperationException($"{stale} obsolete call sites remain.");
Console.WriteLine($"Legends compatibility verified: {counts.status} status, {counts.message} message, {counts.effect} effect calls.");
