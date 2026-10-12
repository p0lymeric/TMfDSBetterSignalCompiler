using System.Reflection;
using HarmonyLib;

namespace polymeric.BetterSignalCompiler;

public static class HarmonyPatches {
    [HarmonyPatch(typeof(C_Reformatter), nameof(C_Reformatter.CompileStringToSignal))]
    public static class Patch__C_Reformatter__CompileStringToSignal {
        public static void Prefix(ref bool __runOriginal, ref SignalMessage __result, string input, ref CompilerResult cr) {
            if (!__runOriginal) {
                return;
            }
            __runOriginal = false;
            __result = MwisCompiler.CompileStringToSignal(input, ref cr);
        }
    }

    [HarmonyPatch(typeof(CompilerResult), "GetStringFromErrorType")]
    public static class Patch__CompilerResult__GetStringFromErrorType {
        public enum CompileErrorTypeExtended {
            Ambiguous = 4,
            PartialCompile = 5,
        }
        public static void Postfix(ref string __result, CompileErrorType cet) {
            switch ((CompileErrorTypeExtended)cet) {
                case CompileErrorTypeExtended.Ambiguous:
                    __result = "Ambiguous";
                    return;
                case CompileErrorTypeExtended.PartialCompile:
                    __result = "Partial compile";
                    return;
                default:
                    return;
            }
        }
    }

    // Variant of Harmony.PatchAll that executes a 1-deep child class scan for HarmonyPatch annotations
    public static void PatchInClassShallow(Harmony harmony, Type outerClass) {
        IEnumerable<Type> allHarmonyTypesUnderOuterClassNonRecursive =
            from type in outerClass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            where HarmonyMethodExtensions.GetFromType(type).Count > 0
            select type
        ;
        foreach (Type type in allHarmonyTypesUnderOuterClassNonRecursive) {
            PatchClassProcessor pcp = harmony.CreateClassProcessor(type);
            pcp.Patch();
        }
    }
}
