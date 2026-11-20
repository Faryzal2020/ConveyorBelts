using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>Gizmos and buffer handling shared by the ports: "open Port tab", copy/paste of a port's settings, buffer drop.</summary>
    public static class PortGizmos
    {
        static Texture2D tabIcon;

        /// <summary>Opens the Port tab of the selected building.</summary>
        public static void OpenPortTab() => InspectPaneUtility.OpenTab(typeof(ITab_Port));

        public static Command_Action OpenTab(string labelKey, string descKey)
        {
            tabIcon ??= ContentFinder<Texture2D>.Get("ConveyorBelts/UI/TabPort");
            return new Command_Action
            {
                defaultLabel = labelKey.Translate(),
                defaultDesc = descKey.Translate(),
                icon = tabIcon,
                action = OpenPortTab,
            };
        }

        /// <summary>One clipboard per settings type, so a port only pastes settings copied from its own kind.</summary>
        static class Clipboard<T> where T : struct { public static T? Value; }

        /// <summary>Copy gizmo, plus a paste gizmo once something of type T was copied.</summary>
        public static IEnumerable<Gizmo> CopyPaste<T>(Func<T> get, Action<T> set, string copyDescKey, string pasteDescKey)
            where T : struct
        {
            yield return new Command_Action
            {
                defaultLabel = "CB_CopyMode".Translate(),
                defaultDesc = copyDescKey.Translate(),
                icon = TexButton.Copy,
                action = () => Clipboard<T>.Value = get(),
            };
            if (Clipboard<T>.Value.HasValue)
                yield return new Command_Action
                {
                    defaultLabel = "CB_PasteMode".Translate(),
                    defaultDesc = pasteDescKey.Translate(),
                    icon = TexButton.Paste,
                    action = () => { if (Clipboard<T>.Value.HasValue) set(Clipboard<T>.Value.Value); },
                };
        }

        /// <summary>
        /// Puts a port's held stack back on the map at (or near) cell, e.g. after the port was removed.
        /// Never destroys it: with no room nearby it is spawned on the cell itself.
        /// </summary>
        public static void DropBuffer(Thing t, IntVec3 cell, Map map)
        {
            if (t == null || t.Destroyed || t.stackCount <= 0 || t.Spawned || map == null) return;
            if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near) && !t.Destroyed && !t.Spawned)
            {
                Log.Warning($"[CB] no room near {cell} for {t}; spawning on the cell");
                GenSpawn.Spawn(t, cell, map);
            }
        }
    }
}
