using HarmonyLib;

// Delegates and refs to private members in the base game assembly

namespace polymeric.BetterSignalCompiler;

public static class DelegatesAndRefs {
    // string CompilerResult.errorMsg
    public static readonly AccessTools.FieldRef<CompilerResult, string> CompilerResult__errorMsg =
        AccessTools.FieldRefAccess<CompilerResult, string>("errorMsg");
}
