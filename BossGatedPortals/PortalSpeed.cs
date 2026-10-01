using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// [4 - Portal Speed]: how fast a teleport's screen fades, and how long its loading screen lasts at least.
    /// Vanilla: the screen fades to black over 1 s (Hud.GetFadeDuration), the player is moved at 2 s
    /// (Player.UpdateTeleport), and the loading screen stays until 8 s even when the destination is already
    /// loaded. The real wait for the destination to load is never cut.
    /// Steps aside (vanilla timings, the other mod in charge) when a mod that speeds up portals itself is
    /// installed: they change the same fade and timer, and two of them fighting gives unpredictable timings.
    /// </summary>
    internal static class PortalSpeed
    {
        // Portal-speed mods, by plugin ID (checked against their code): OdinPlus and Muindor QuickTeleport
        // (GetFadeDuration + the teleport timer), FastTeleport (skips the wait), Proper Portals (both).
        private static readonly string[] SpeedMods =
            { "elg.QuickTeleport", "muindor.QuickTeleport", "GemHunter1.FastTeleport", "dev.crystal.properportals" };

        private static bool resolved;
        private static string otherMod; // name of an installed portal-speed mod, or null

        /// <summary>A mod that speeds up portals itself is installed: its name, else null.</summary>
        public static string OtherMod()
        {
            if (!resolved)
            {
                resolved = true;
                foreach (string guid in SpeedMods)
                {
                    if (Chainloader.PluginInfos.TryGetValue(guid, out var p) && p.Instance != null)
                    {
                        otherMod = p.Metadata.Name;
                        break;
                    }
                }
            }
            return otherMod;
        }

        /// <summary>Logs once at startup when another portal-speed mod takes over from [4 - Portal Speed].</summary>
        public static void LogStandDown()
        {
            if (OtherMod() == null) return;
            bool changed = Settings.FadeSeconds.Value != VanillaFade || Settings.MinimumLoadingSeconds.Value != VanillaHold;
            string message = $"Portal Speed: {OtherMod()} is installed and speeds up portals itself, so " +
                "[4 - Portal Speed] leaves the timings to it. Use its settings instead.";
            if (changed) Jotunn.Logger.LogWarning(message);
            else Jotunn.Logger.LogInfo(message);
        }
        private const float VanillaFade = 1f;
        private const float VanillaMoveDelay = 2f;
        private const float VanillaHold = 8f;
        // The player is never moved sooner than this: a cart they pull lets go on its next update once they
        // count as teleporting, and has to before they jump across the world (OdinHorse's hitch never breaks).
        private const float MinimumMoveDelay = 0.25f;
        // Hud divides by the fade time every frame: never zero (0.01 s is one frame or less).
        private const float MinimumFade = 0.01f;

        /// <summary>
        /// Seconds before the player is moved to the destination (vanilla 2): the loading time, but never before
        /// the screen is fully black, so the jump never shows.
        /// </summary>
        public static float MoveDelay()
        {
            try
            {
                return Mathf.Clamp(Mathf.Max(Loading(), Fade()), MinimumMoveDelay, VanillaMoveDelay);
            }
            catch (Exception e)
            {
                Failsafe.Report("Teleport loading time", e);
                return VanillaMoveDelay;
            }
        }

        /// <summary>Seconds the loading screen lasts at least (vanilla 8).</summary>
        public static float Hold()
        {
            try
            {
                return Mathf.Clamp(Loading(), 0f, VanillaHold);
            }
            catch (Exception e)
            {
                Failsafe.Report("Teleport loading time", e);
                return VanillaHold;
            }
        }

        /// <summary>This mod sets the timings: switched on, and no other portal-speed mod installed.</summary>
        private static bool Active() => Settings.Enabled.Value && OtherMod() == null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float Loading() => Active() ? Settings.MinimumLoadingSeconds.Value : VanillaHold;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float Fade() => Active() ? Settings.FadeSeconds.Value : VanillaFade;

        /// <summary>
        /// Hud.GetFadeDuration (private): how long the screen takes to fade to black and back. Changed only for
        /// a living, awake player, which is a teleport; death and sleep keep their own fades.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "GetFadeDuration")]
        private static class FadePatch
        {
            private static void Postfix(Player player, ref float __result)
            {
                try
                {
                    __result = TeleportFade(player, __result);
                }
                catch (Exception e)
                {
                    Failsafe.Report("Teleport fade time", e); // __result keeps vanilla's value
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float TeleportFade(Player player, float vanilla)
            {
                // Another portal-speed mod's own fade time (its postfix may run first) is left as it is.
                if (!Active() || player == null || player.IsDead() || player.IsSleeping()) return vanilla;
                return Mathf.Max(Fade(), MinimumFade);
            }
        }

        /// <summary>
        /// In Player.UpdateTeleport, swap the two constants that follow a read of m_teleportTimer:
        ///   if (!(m_teleportTimer > 2f)) return;                                -> PortalSpeed.MoveDelay()
        ///   if ((!(m_teleportTimer > 8f) &amp;&amp; m_distantTeleport) || !IsAreaReady(...)) -> PortalSpeed.Hold()
        /// If a game update (or another mod's patch) changes that code, logs a warning and leaves it as it is.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateTeleport")]
        private static class UpdateTeleportPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                FieldInfo timer = AccessTools.Field(typeof(Player), "m_teleportTimer");

                // Both found before either is changed: never half-patched.
                int move = FindAfterTimer(codes, timer, VanillaMoveDelay);
                int hold = FindAfterTimer(codes, timer, VanillaHold);
                if (move < 0 || hold < 0)
                {
                    Jotunn.Logger.LogWarning("MinimumLoadingSeconds: couldn't find the teleport wait in the game's " +
                        "code (game update, or another mod changed it). Teleports keep their usual wait.");
                    return codes;
                }

                // Same place on the stack (a float), so only the instruction itself changes; its labels stay.
                codes[move].opcode = OpCodes.Call;
                codes[move].operand = AccessTools.Method(typeof(PortalSpeed), nameof(MoveDelay));
                codes[hold].opcode = OpCodes.Call;
                codes[hold].operand = AccessTools.Method(typeof(PortalSpeed), nameof(Hold));
                return codes;
            }

            /// <summary>Index of "ldc.r4 value" right after a read of the timer field, or -1.</summary>
            private static int FindAfterTimer(List<CodeInstruction> codes, FieldInfo timer, float value)
            {
                if (timer == null) return -1;
                for (int i = 1; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && f == value &&
                        codes[i - 1].LoadsField(timer))
                        return i;
                }
                return -1;
            }
        }
    }
}
