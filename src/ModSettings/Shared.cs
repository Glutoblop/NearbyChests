// SHARED FILE: the canonical copy lives in D:\Work\Valheim\Shared\ModSettings. Edit it there and run
// Shared\sync.ps1 to copy it into every plugin's src\ModSettings folder. Bump UiVersion on every change.
//
// In-game "Mod Settings" window, with no dependency between plugins:
//   - Every plugin compiles its own copy of these files. Their [HarmonyPatch] classes are picked up by the
//     plugin's normal PatchAll, so including the folder is all a plugin has to do.
//   - At runtime all copies find each other (same type name in each plugin's assembly) and agree on one owner:
//     the highest UiVersion, ties broken by assembly name. Only the owner adds the pause-menu button and window,
//     so the newest UI code installed always wins, and one plugin on its own works just the same.
//   - The window has one tab per plugin carrying a copy, listing that plugin's BepInEx config.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace GlutoModSettings
{
    internal static class Shared
    {
        public const int UiVersion = 1;

        private static ManualLogSource _log;
        private static bool? _isOwner;

        private static ManualLogSource Logger => _log ?? (_log = BepInEx.Logging.Logger.CreateLogSource("ModSettings"));
        internal static void Log(string msg) => Logger.LogInfo(msg);
        internal static void Warn(string msg) => Logger.LogWarning(msg);

        private static Assembly Self => typeof(Shared).Assembly;

        // Plugins that carry a copy of this code: their assembly has a GlutoModSettings.Shared type.
        internal static List<(PluginInfo info, int version)> Participants()
        {
            var list = new List<(PluginInfo, int)>();
            string typeName = typeof(Shared).FullName;
            foreach (PluginInfo p in Chainloader.PluginInfos.Values)
            {
                if (!p.Instance) continue;
                Type t = p.Instance.GetType().Assembly.GetType(typeName, false);
                if (t == null) continue;
                int v = 0;
                try { v = Convert.ToInt32(t.GetField(nameof(UiVersion), BindingFlags.Public | BindingFlags.Static)?.GetValue(null)); }
                catch { /* unreadable: treat as oldest */ }
                list.Add((p, v));
            }
            return list;
        }

        // Decided once, after every plugin has loaded (first called from Menu.Start).
        internal static bool IsOwner
        {
            get
            {
                if (_isOwner.HasValue) return _isOwner.Value;
                var owner = Participants()
                    .OrderByDescending(p => p.version)
                    .ThenBy(p => p.info.Instance.GetType().Assembly.GetName().Name, StringComparer.Ordinal)
                    .Select(p => p.info.Instance.GetType().Assembly)
                    .FirstOrDefault();
                _isOwner = owner == null || owner == Self;
                if (_isOwner.Value) Log($"Mod Settings window provided by {Self.GetName().Name} (UI v{UiVersion}).");
                return _isOwner.Value;
            }
        }
    }
}
