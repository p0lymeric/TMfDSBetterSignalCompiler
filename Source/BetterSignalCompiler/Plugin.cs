using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace polymeric.BetterSignalCompiler;

public enum ConfigErrorLogDisplayFormat {
    TraceAndCount,
    TraceAndVerbose,
    Verbose,
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static Plugin Singleton = null!;

    public ManualLogSource logger;
    public Harmony harmony;

    public ConfigEntry<bool> configParserParseBareIds;
    public ConfigEntry<ConfigErrorLogDisplayFormat> configErrorLogDisplayFormat;
    public ConfigEntry<bool> configErrorLogAbbreviateTrace;

    private Plugin() {
        this.logger = base.Logger;
        this.harmony = new(MyPluginInfo.PLUGIN_GUID);

        this.configParserParseBareIds = this.Config.Bind(
            "Parser",
            "ParseBareIds",
            true,
            "Enables parsing expressions of the form '|<integer>' (e.g. '|5', '|-244'), which directly specify frequency offsets.\nfalse: Do not parse '|<integer>' expressions.\ntrue: Parse '|<integer>' expressions."
        );
        this.configErrorLogDisplayFormat = this.Config.Bind(
            "ErrorLog",
            "DisplayFormat",
            ConfigErrorLogDisplayFormat.Verbose,
            "Selects a formatting style for displaying compilation errors.\nTraceAndCount: Shows a compile trace, followed by a count of each error type.\nTraceAndVerbose: Shows a compile trace, followed by every error.\nVerbose: Shows every error, like the base game compiler."
        );
        this.configErrorLogAbbreviateTrace = this.Config.Bind(
            "ErrorLog",
            "AbbreviateTrace",
            false,
            "Controls whether compile traces print every parsed ID, or whether runs of IDs are collapsed with '|...'.\nfalse: Print every parsed signal ID interspersed with unparsed portions of the input.\ntrue: Collapse parsed signal IDs with '|...' and print unparsed portions of the input."
        );
    }

    private void Awake() {
        Singleton = this;

        Singleton.logger.LogInfo($"Hello from {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION}!");

        HarmonyPatches.PatchInClassShallow(Singleton.harmony, typeof(HarmonyPatches));
    }
}
