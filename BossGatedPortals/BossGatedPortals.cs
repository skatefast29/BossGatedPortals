using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace BossGatedPortals
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Load after XPortal when it's installed, but work without it.
    [BepInDependency(XPortalGUID, BepInDependency.DependencyFlags.SoftDependency)]
    // Every client must have the mod, with the server's major.minor version; patch versions may differ
    // (Jotunn's recommendation). Bump the minor version for anything that changes how client and server
    // must agree (config meaning, synced settings, gate rules); keep bug fixes to patch versions.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class BossGatedPortals : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jtmill01.bossgatedportals";
        public const string PluginName = "BossGatedPortals";
        public const string PluginVersion = "0.1.4";

        public const string XPortalGUID = "yay.spikehimself.xportal";

        // Our own Harmony ID, separate from XPortal's.
        private readonly Harmony harmony = new Harmony(PluginGUID + ".harmony");

        private void Awake()
        {
            Settings.Bind(Config);
            Settings.LogSummary();

            // Keep the parsed tiers current: quietly on every change, with a log after a server
            // sync or after an admin closes the F1 settings window.
            Config.SettingChanged += (_, __) => Settings.Parse();
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ReportSettings();
            SynchronizationManager.OnConfigurationWindowClosed += ReportSettings;
            // Item IDs can only be checked once the game's item list exists.
            ItemManager.OnItemsRegistered += Settings.Validate;
            ItemManager.OnItemsRegistered += Hints.LogUnmappedItems;

            PatchEachHook();
            CommandManager.Instance.AddConsoleCommand(new StatusCommand());

            bool xportal = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(XPortalGUID);
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded. XPortal detected: {xportal}");
        }

        // Start runs after every plugin's Awake, so all installed mods are known by now.
        private void Start()
        {
            Compatibility.LogInstalledMods();
        }

        /// <summary>
        /// Same as harmony.PatchAll(), one hook at a time. If a game update renames a method we hook,
        /// only that hook is skipped (with an error in the log); PatchAll would stop at the first failure
        /// and leave the rest of the mod half-loaded.
        /// </summary>
        private void PatchEachHook()
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(BossGatedPortals).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Jotunn.Logger.LogError($"Couldn't hook the game for {type.Name} (game update?). " +
                        $"That feature stays vanilla; the rest of the mod still works. {e}");
                }
            }
        }

        private static void ReportSettings()
        {
            Settings.Parse();
            Settings.LogSummary();
            Settings.Validate();
            Hints.LogUnmappedItems();
        }

        // No OnDestroy/UnpatchSelf: the Valheim modding wiki advises against unpatching on shutdown.
    }
}
