using System;
using System.Collections.Generic;

namespace BossGatedPortals
{
    /// <summary>
    /// Keeps a game update from turning a mod error into a broken game feature. Every hook catches its
    /// own errors, reports them here once, and falls back to vanilla behaviour for that call.
    ///
    /// Pattern for every Harmony hook: the Prefix/Postfix itself holds only try/catch and one call to a
    /// [MethodImpl(NoInlining)] method with the real work. A game member that was renamed or removed
    /// fails when the method mentioning it is first run, so that mention must sit inside the try.
    /// </summary>
    internal static class Failsafe
    {
        private static readonly HashSet<string> reported = new HashSet<string>();

        public static void Report(string where, Exception e)
        {
            if (!reported.Add(where)) return; // once per hook, not every frame
            Jotunn.Logger.LogError($"{where} failed, using vanilla behaviour there instead " +
                $"(game update?): {e}");
        }

        /// <summary>
        /// The "teleport all" world modifier. Checked by name, not by the GlobalKeys enum: the enum's
        /// number is baked into this DLL and would point at the wrong key if an update re-numbered it.
        /// </summary>
        public static bool TeleportAllSet() => ZoneSystem.instance.GetGlobalKey("teleportall");
    }
}
