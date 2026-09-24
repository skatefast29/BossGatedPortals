using System.Collections.Generic;

namespace BossGatedPortals
{
    /// <summary>One boss tier, parsed from a [Tn - ...] config section.</summary>
    internal class Tier
    {
        public int Index;
        public string Name;              // e.g. "Swamp" (never shown to players as a boss name)
        public string GlobalKey;         // world kill key, lower-case
        public string BossItem;          // prefab name of the boss drop
        public string BossItemToken;     // e.g. "$item_wishbone"; filled in once ObjectDB is loaded
        public HashSet<string> Items = new HashSet<string>();
        public string Hint;
        public string UnlockMessage;
        public bool AllowEverything;
    }
}
