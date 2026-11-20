using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Haul priority + a flat list of allowed items + a search box that adds items. Edits the owner's vanilla
    /// StorageSettings (filter and priority), so vanilla hauling keeps working. The IMGUI code in Draw only wires
    /// widgets to the public operations below (also used by the debug actions).
    /// </summary>
    public class Section_InputFilter : PortTabSection
    {
        public const int MaxSearchResults = 30;
        const float RowH = 26f;
        const float IconSize = 24f;

        readonly IStoreSettingsParent owner;
        string search = "";
        List<ThingDef> candidates;   // every def the fixed settings allow, sorted by label (defs never change in play)

        public Section_InputFilter(IStoreSettingsParent owner) { this.owner = owner; }

        public StorageSettings Settings => owner.GetStoreSettings();

        /// <summary>The text in the search box (kept per port while the game runs).</summary>
        public string SearchText { get => search; set => search = value ?? ""; }
        ThingFilter FixedFilter => owner.GetParentStoreSettings()?.filter;

        // ---- operations ----

        public StoragePriority Priority => Settings.Priority;

        public void SetPriority(StoragePriority p)
        {
            if (p == StoragePriority.Unstored) return;
            Settings.Priority = p;
        }

        /// <summary>Allowed defs, sorted by label.</summary>
        public List<ThingDef> AllowedDefs()
            => Settings.filter.AllowedThingDefs.Where(d => FixedFilter?.Allows(d) ?? true)
                                               .OrderBy(d => d.label, StringComparer.OrdinalIgnoreCase).ToList();

        public bool CanAllow(ThingDef d) => d != null && (FixedFilter?.Allows(d) ?? true);

        /// <summary>Allows d. Returns false if the fixed settings never allow it.</summary>
        public bool Allow(ThingDef d)
        {
            if (!CanAllow(d)) return false;
            Settings.filter.SetAllow(d, true);
            return true;
        }

        public void Disallow(ThingDef d)
        {
            if (d != null) Settings.filter.SetAllow(d, false);
        }

        /// <summary>
        /// Defs allowed by the fixed settings and not yet allowed, whose label contains query (case-insensitive).
        /// Returns at most max; total is the number of all matches. An empty query matches nothing.
        /// </summary>
        public List<ThingDef> Search(string query, int max, out int total)
        {
            total = 0;
            var result = new List<ThingDef>();
            query = query?.Trim();
            if (query.NullOrEmpty()) return result;
            candidates ??= (FixedFilter?.AllowedThingDefs ?? DefDatabase<ThingDef>.AllDefs)
                .Where(d => !d.label.NullOrEmpty())
                .OrderBy(d => d.label, StringComparer.OrdinalIgnoreCase).ToList();
            var filter = Settings.filter;
            foreach (var d in candidates)
            {
                if (filter.Allows(d) || d.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                total++;
                if (result.Count < max) result.Add(d);
            }
            return result;
        }

        // ---- drawing ----

        public override float Draw(Rect rect)
        {
            float x = rect.x, w = rect.width, y = rect.y;
            Text.Font = GameFont.Small;

            // Haul priority
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, 130f, 30f), "CB_HaulPriority".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(x + 135f, y, 160f, 30f), Priority.Label().CapitalizeFirst()))
            {
                var opts = new List<FloatMenuOption>();
                foreach (StoragePriority p in Enum.GetValues(typeof(StoragePriority)))
                {
                    if (p == StoragePriority.Unstored) continue;
                    var local = p;
                    opts.Add(new FloatMenuOption(local.Label().CapitalizeFirst(), () => SetPriority(local)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 38f;

            // Allowed items
            var allowed = AllowedDefs();
            Widgets.Label(new Rect(x, y, w, 24f), "CB_AllowedItems".Translate(allowed.Count));
            y += 26f;
            ThingDef toRemove = null;
            if (allowed.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(x + 10f, y, w - 10f, RowH), "CB_NoItemsAllowed".Translate());
                GUI.color = Color.white;
                y += RowH;
            }
            foreach (var d in allowed)
            {
                var row = new Rect(x, y, w, RowH);
                Widgets.DrawHighlightIfMouseover(row);
                DrawDefRow(row, d);
                if (Widgets.ButtonImage(new Rect(row.xMax - 22f, row.y + 5f, 16f, 16f), TexButton.CloseXSmall,
                                        tooltip: "CB_RemoveItem".Translate(d.label)))
                    toRemove = d;
                y += RowH;
            }
            if (toRemove != null) Disallow(toRemove);
            y += 10f;

            // Search to add
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, 40f, 28f), "CB_AddLabel".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            search = Widgets.TextField(new Rect(x + 45f, y, w - 45f, 28f), search ?? "", 40);
            y += 32f;

            if (search.NullOrEmpty() || search.Trim().Length == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(x + 10f, y, w - 10f, RowH), "CB_SearchHint".Translate());
                GUI.color = Color.white;
                y += RowH;
                return y - rect.y;
            }

            var matches = Search(search, MaxSearchResults, out int total);
            ThingDef toAdd = null;
            foreach (var d in matches)
            {
                var row = new Rect(x, y, w, RowH);
                Widgets.DrawHighlightIfMouseover(row);
                DrawDefRow(row, d);
                TooltipHandler.TipRegion(row, "CB_AddItem".Translate(d.label));
                if (Widgets.ButtonInvisible(row)) toAdd = d;
                y += RowH;
            }
            if (toAdd != null) Allow(toAdd);
            if (total == 0 || total > matches.Count)
            {
                GUI.color = Color.gray;
                string msg = total == 0 ? "CB_SearchNoMatch".Translate() : "CB_SearchMore".Translate(total - matches.Count);
                Widgets.Label(new Rect(x + 10f, y, w - 10f, RowH), msg);
                GUI.color = Color.white;
                y += RowH;
            }
            return y - rect.y;
        }

        static void DrawDefRow(Rect row, ThingDef d)
        {
            Widgets.DefIcon(new Rect(row.x + 2f, row.y + 1f, IconSize, IconSize), d);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + IconSize + 8f, row.y, row.width - IconSize - 34f, row.height), d.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
