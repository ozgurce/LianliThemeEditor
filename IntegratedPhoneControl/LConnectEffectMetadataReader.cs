using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PhoneControl;

internal sealed record LConnectEffectMetadata(int ColorCount);

internal static class LConnectEffectMetadataReader
{
    public static IReadOnlyDictionary<int, LConnectEffectMetadata> ReadWirelessHydroMetadata() =>
        ReadDefaultLightingSettings(
            "LConnect3cs.Products.LWireless.HydroShiftIILCD.HydroShiftIISubProfile",
            "DefaultLightingSettings");

    public static IReadOnlyDictionary<int, LConnectEffectMetadata> ReadTlv2Metadata() =>
        ReadDefaultLightingSettings(
            "LConnect3cs.Products.LWireless.TLV2.TLV2SubProfile",
            "DefaultLightingSettings");

    public static IReadOnlyDictionary<int, LConnectEffectMetadata> ReadWirelessMergeMetadata() =>
        ReadDefaultLightingSettings(
            "LConnect3cs.Products.LWireless.LWirelessProfile",
            "DefaultMergeLightingSettings");

    public static IReadOnlyList<int> ReadWirelessMergeEffectOrder() =>
        ReadDefaultLightingSettings(
                "LConnect3cs.Products.LWireless.LWirelessProfile",
                "DefaultMergeLightingSettings")
            .Keys
            .ToArray();

    public static IReadOnlyList<int> ReadTlv2MergeEffectOrder() =>
        ReadStaticIntArray(
            "LConnect3cs.Products.LWireless.TLV2.TLV2SubProfile",
            "TLV2UIEffectsOrder");

    private static IReadOnlyDictionary<int, LConnectEffectMetadata> ReadDefaultLightingSettings(string typeName, string fieldName)
    {
        var exePath = Path.Combine(LConnectPaths.ProgramFilesRoot, "L-Connect 3.exe");
        if (!File.Exists(exePath))
        {
            return new Dictionary<int, LConnectEffectMetadata>();
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(exePath, new ReaderParameters
            {
                ReadSymbols = false,
                AssemblyResolver = new LConnectCecilResolver()
            });

            var type = assembly.MainModule.GetType(typeName);
            var cctor = type?.Methods.FirstOrDefault(method => method.Name == ".cctor" && method.HasBody);
            if (cctor is null)
            {
                return new Dictionary<int, LConnectEffectMetadata>();
            }

            var result = new Dictionary<int, LConnectEffectMetadata>();
            var instructions = cctor.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (!IsDictionaryAdd(instructions[i], fieldName))
                {
                    continue;
                }

                var start = FindPreviousDictionaryEntryStart(instructions, i);
                if (start < 0)
                {
                    continue;
                }

                var key = TryReadFirstInt(instructions, start, i);
                if (!key.HasValue)
                {
                    continue;
                }

                var colorCount = TryReadColorArrayLength(instructions, start, i) ?? 0;
                result[key.Value] = new LConnectEffectMetadata(colorCount);
            }

            return result;
        }
        catch
        {
            return new Dictionary<int, LConnectEffectMetadata>();
        }
    }

    private static bool IsDictionaryAdd(Instruction instruction, string fieldName)
    {
        if (instruction.OpCode.Code != Code.Callvirt || instruction.Operand is not MethodReference method)
        {
            return false;
        }

        return method.Name == "Add" &&
            method.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal) &&
            method.DeclaringType.FullName.Contains(fieldName.Contains("Default") ? "LightingSetting" : "", StringComparison.Ordinal);
    }

    private static int FindPreviousDictionaryEntryStart(IList<Instruction> instructions, int addIndex)
    {
        for (var i = addIndex - 1; i >= 0; i--)
        {
            if (instructions[i].OpCode.Code == Code.Dup &&
                i + 2 < addIndex &&
                TryGetInlineInt(instructions[i + 1]).HasValue &&
                instructions[i + 2].OpCode.Code == Code.Newobj)
            {
                return i + 1;
            }

            if (instructions[i].OpCode.Code == Code.Stsfld)
            {
                break;
            }
        }

        return -1;
    }

    private static int? TryReadFirstInt(IList<Instruction> instructions, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var value = TryGetInlineInt(instructions[i]);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }

    private static int? TryReadColorArrayLength(IList<Instruction> instructions, int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            if (instructions[i].OpCode.Code != Code.Newarr || instructions[i].Operand is not TypeReference type)
            {
                continue;
            }

            if (!type.FullName.Equals("System.Windows.Media.Color", StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = i - 1; j >= start; j--)
            {
                var value = TryGetInlineInt(instructions[j]);
                if (value.HasValue)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<int> ReadStaticIntArray(string typeName, string fieldName)
    {
        var exePath = Path.Combine(LConnectPaths.ProgramFilesRoot, "L-Connect 3.exe");
        if (!File.Exists(exePath))
        {
            return Array.Empty<int>();
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(exePath, new ReaderParameters
            {
                ReadSymbols = false,
                AssemblyResolver = new LConnectCecilResolver()
            });

            var type = assembly.MainModule.GetType(typeName);
            var cctor = type?.Methods.FirstOrDefault(method => method.Name == ".cctor" && method.HasBody);
            if (cctor is null)
            {
                return Array.Empty<int>();
            }

            var instructions = cctor.Body.Instructions;
            var storeIndex = -1;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode.Code == Code.Stsfld &&
                    instructions[i].Operand is FieldReference field &&
                    field.Name.Equals(fieldName, StringComparison.Ordinal))
                {
                    storeIndex = i;
                    break;
                }
            }

            if (storeIndex < 0)
            {
                return Array.Empty<int>();
            }

            var start = -1;
            for (var i = storeIndex - 1; i >= 0; i--)
            {
                if (instructions[i].OpCode.Code == Code.Newarr)
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                return Array.Empty<int>();
            }

            var values = new List<int>();
            for (var i = start + 1; i < storeIndex; i++)
            {
                if (instructions[i].OpCode.Code != Code.Stelem_I4)
                {
                    continue;
                }

                for (var j = i - 1; j > start; j--)
                {
                    var value = TryGetInlineInt(instructions[j]);
                    if (value.HasValue)
                    {
                        values.Add(value.Value);
                        break;
                    }
                }
            }

            return values;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static int? TryGetInlineInt(Instruction instruction) =>
        instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4_S => (sbyte)instruction.Operand,
            Code.Ldc_I4 => (int)instruction.Operand,
            _ => null
        };

    private sealed class LConnectCecilResolver : BaseAssemblyResolver
    {
        public LConnectCecilResolver()
        {
            if (Directory.Exists(LConnectPaths.ProgramFilesRoot))
            {
                AddSearchDirectory(LConnectPaths.ProgramFilesRoot);
            }
        }
    }
}
