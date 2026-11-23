using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ConveyorBelts
{
    /// <summary>One row of the search results: an item (Def set) or a category (Category set), never both.</summary>
    public readonly struct FilterSearchResult
    {
        public readonly ThingDef Def;
        public readonly ThingCategoryDef Category;
        public readonly string Label;
        /// <summary>The item's first category label, or the keyed "category" word for a category.</summary>
        public readonly string Tag;

        public FilterSearchResult(ThingDef def, ThingCategoryDef category, string label, string tag)
        {
            Def = def;
            Category = category;
            Label = label;
            Tag = tag;
        }
    }

    /// <summary>
    /// Item filter shared by the ports: "Items" + All / None, a search box, and two panes. Left: the vanilla thing-category
    /// tree with tri-state boxes, or while searching a flat list of matching items and categories with "Allow all" /
    /// "Disallow all". Right: every allowed item with ✕. Edits the ThingFilter that <c>filter</c> returns; the items it
    /// lists are those <c>fixedFilter</c> allows (null = every storable def, corpses included). The IMGUI code only wires
    /// widgets to the public operations (also used by the debug actions). Derived lists are cached and rebuilt only when
    /// the filter's contents (allowed defs + special filters, as a cheap signature), the search text, the filter instance
    /// or one of our own toggles changes.
    /// Vanilla special thing filters (e.g. colonist corpses) get no rows of their own: allowing a category, an item or
    /// everything also allows the special filters that can apply to it, and a category box is On only when they are
    /// allowed too, so the tab never shows something as allowed that vanilla would still refuse.
    /// </summary>
    public class Section_ItemFilter : PortTabSection
    {
        const float RowH = 24f;
        const float PaneH = 330f;
        const float PaneHeaderH = 28f;
        const float Gap = 8f;
        const float Indent = 14f;
        const float ArrowW = 18f;
        const float Box = 22f;
        const float IconSize = 20f;

        readonly Func<ThingFilter> filterGetter;
        readonly Func<ThingFilter> fixedGetter;
        string search = "";

        // Every def the filter can list (fixed filter applied), sorted by label. Defs never change in play.
        bool candidatesBuilt;
        ThingFilter candidatesFor;
        List<ThingDef> candidates;
        HashSet<ThingDef> candidateSet;
        readonly Dictionary<ThingCategoryDef, List<ThingDef>> catDefs = new Dictionary<ThingCategoryDef, List<ThingDef>>();
        // Configurable special filters the fixed filter allows, and which of them can apply to a category / an item.
        List<SpecialThingFilterDef> specialCandidates;
        readonly Dictionary<ThingCategoryDef, List<SpecialThingFilterDef>> catSpecials = new Dictionary<ThingCategoryDef, List<SpecialThingFilterDef>>();
        readonly Dictionary<ThingCategoryDef, List<SpecialThingFilterDef>> catOwnSpecials = new Dictionary<ThingCategoryDef, List<SpecialThingFilterDef>>();
        readonly Dictionary<ThingDef, List<SpecialThingFilterDef>> defSpecials = new Dictionary<ThingDef, List<SpecialThingFilterDef>>();

        // Allowed-state caches, dropped by Sync().
        ThingFilter syncFilter;
        int syncCount = -1, syncEdits = -1, edits, syncSig;
        bool inDraw, sigChecked;   // during Draw the content signature is computed once, not on every Filter access
        List<ThingDef> allowedCache;
        readonly Dictionary<ThingCategoryDef, MultiCheckboxState> stateCache = new Dictionary<ThingCategoryDef, MultiCheckboxState>();
        bool? allResultsAllowedCache;

        // Search results, keyed by the trimmed text.
        string resultsFor;
        List<FilterSearchResult> resultsCache;

        // Tree UI state (kept per section, i.e. per port while the game runs).
        readonly HashSet<ThingCategoryDef> expanded = new HashSet<ThingCategoryDef>();
        List<TreeRow> treeRows;
        Vector2 treeScroll, resultsScroll, allowedScroll;
        readonly Dictionary<string, string> truncTree = new Dictionary<string, string>();
        // Result row text truncated to fit label + tag, per result (rebuilt with the results or a new pane width).
        List<FilterSearchResult> layoutFor;
        float layoutWidth;
        string[] shownLabels, shownTags;
        float[] tagWidths;
        readonly Dictionary<string, string> truncAllowed = new Dictionary<string, string>();
        Action pending;   // a click's effect, applied after the panes are drawn

        readonly struct TreeRow
        {
            public readonly ThingCategoryDef Cat;
            public readonly ThingDef Def;
            public readonly int Depth;
            public TreeRow(ThingCategoryDef cat, ThingDef def, int depth) { Cat = cat; Def = def; Depth = depth; }
        }

        public Section_ItemFilter(Func<ThingFilter> filter, Func<ThingFilter> fixedFilter)
        {
            filterGetter = filter;
            fixedGetter = fixedFilter;
        }

        /// <summary>The text in the search box. Non-blank switches the left pane to the result list.</summary>
        public string SearchText { get => search; set => search = value ?? ""; }

        bool Searching => search.Trim().Length > 0;

        // ---- caches ----

        /// <summary>The edited filter; drops the allowed-state caches when it changed since the last call.</summary>
        ThingFilter Filter
        {
            get
            {
                var f = filterGetter();
                bool changed = f != syncFilter || f.AllowedDefCount != syncCount || edits != syncEdits;
                if (!changed && !(inDraw && sigChecked))
                {
                    changed = Signature(f) != syncSig;
                    sigChecked = true;
                }
                if (changed)
                {
                    sigChecked = true;
                    syncFilter = f;
                    syncCount = f.AllowedDefCount;
                    syncEdits = edits;
                    syncSig = Signature(f);
                    allowedCache = null;
                    stateCache.Clear();
                    allResultsAllowedCache = null;
                }
                return f;
            }
        }

        /// <summary>Cheap content hash: allowed defs (order-independent) and which special filters are disallowed.</summary>
        static int Signature(ThingFilter f)
        {
            unchecked
            {
                uint h = (uint)f.AllowedDefCount;
                if (f.AllowedThingDefs is HashSet<ThingDef> set)
                    foreach (var d in set) h += (d.shortHash + 1u) * 2654435761u;
                else
                    foreach (var d in f.AllowedThingDefs) h += (d.shortHash + 1u) * 2654435761u;
                var specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;
                for (int i = 0; i < specials.Count; i++)
                    if (!f.Allows(specials[i])) h = h * 31u + (uint)(i + 1);
                return (int)h;
            }
        }

        List<ThingDef> Candidates
        {
            get
            {
                var fixedF = fixedGetter?.Invoke();
                if (candidatesBuilt && fixedF == candidatesFor) return candidates;
                candidatesBuilt = true;
                candidatesFor = fixedF;
                var set = new HashSet<ThingDef>();
                if (fixedF != null)
                {
                    foreach (var d in fixedF.AllowedThingDefs)
                        if (Listable(d)) set.Add(d);
                }
                else
                {
                    foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
                        if (d.EverStorable(willMinifyIfPossible: true) && Listable(d)) set.Add(d);
                }
                candidateSet = set;
                candidates = new List<ThingDef>(set);
                candidates.Sort(ByLabel);
                catDefs.Clear();
                specialCandidates = new List<SpecialThingFilterDef>();
                foreach (var sf in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
                    if (sf.configurable && sf.parentCategory != null && (fixedF == null || fixedF.Allows(sf))) specialCandidates.Add(sf);
                catSpecials.Clear();
                catOwnSpecials.Clear();
                defSpecials.Clear();
                treeRows = null;
                resultsFor = null;
                resultsCache = null;
                syncFilter = null;
                return candidates;
            }
        }

        static bool Listable(ThingDef d)
            => d != null && !d.label.NullOrEmpty() && !d.thingCategories.NullOrEmpty() && d.virtualDefParent == null && d.PlayerAcquirable;

        static bool Hidden(ThingDef d) => Find.HiddenItemsManager?.Hidden(d) ?? false;

        static int ByLabel(ThingDef a, ThingDef b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase);

        static bool Matches(string label, string q) => label != null && label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The listable defs in cat and its subcategories (each once).</summary>
        List<ThingDef> CatDefs(ThingCategoryDef cat)
        {
            _ = Candidates;
            if (catDefs.TryGetValue(cat, out var list)) return list;
            list = new List<ThingDef>();
            var seen = new HashSet<ThingDef>();
            foreach (var d in cat.DescendantThingDefs)
                if (candidateSet.Contains(d) && seen.Add(d)) list.Add(d);
            catDefs[cat] = list;
            return list;
        }

        /// <summary>
        /// Special filters that can apply to a listable def in cat: their category is cat, inside it or above it, and
        /// their worker can match such a def. own = only those whose category is cat or inside it (what vanilla's
        /// SetAllow(category, false) also clears).
        /// </summary>
        List<SpecialThingFilterDef> CatSpecials(ThingCategoryDef cat, bool own)
        {
            _ = Candidates;
            var dict = own ? catOwnSpecials : catSpecials;
            if (dict.TryGetValue(cat, out var list)) return list;
            var all = new List<SpecialThingFilterDef>();
            var inside = new List<SpecialThingFilterDef>();
            var defs = CatDefs(cat);
            foreach (var sf in specialCandidates)
            {
                bool below = false, above = false;
                foreach (var c in cat.ThisAndChildCategoryDefs) if (c == sf.parentCategory) { below = true; break; }
                if (!below) foreach (var c in cat.Parents) if (c == sf.parentCategory) { above = true; break; }
                if (!below && !above) continue;
                bool applies = false;
                foreach (var d in defs)
                    if (d.IsWithinCategory(sf.parentCategory) && sf.Worker.CanEverMatch(d)) { applies = true; break; }
                if (!applies) continue;
                all.Add(sf);
                if (below) inside.Add(sf);
            }
            catSpecials[cat] = all;
            catOwnSpecials[cat] = inside;
            return own ? inside : all;
        }

        /// <summary>Special filters that can apply to things of def.</summary>
        List<SpecialThingFilterDef> DefSpecials(ThingDef def)
        {
            _ = Candidates;
            if (defSpecials.TryGetValue(def, out var list)) return list;
            list = new List<SpecialThingFilterDef>();
            foreach (var sf in specialCandidates)
                if (def.IsWithinCategory(sf.parentCategory) && sf.Worker.CanEverMatch(def)) list.Add(sf);
            defSpecials[def] = list;
            return list;
        }

        static void SetSpecials(ThingFilter f, List<SpecialThingFilterDef> list, bool allow)
        {
            foreach (var sf in list) f.SetAllow(sf, allow);
        }

        /// <summary>Allows / disallows every listable def in cat with its special filters (all that apply on allow, cat's own on disallow).</summary>
        void SetCategoryIn(ThingFilter f, ThingCategoryDef cat, bool allow)
        {
            foreach (var d in CatDefs(cat)) f.SetAllow(d, allow);
            SetSpecials(f, CatSpecials(cat, own: !allow), allow);
        }

        /// <summary>The special filters the tab manages (configurable, allowed by the fixed filter).</summary>
        public List<SpecialThingFilterDef> ManagedSpecialFilters() { _ = Candidates; return specialCandidates; }

        // ---- operations (the widgets and the debug actions call these) ----

        /// <summary>Every def this filter can list (the fixed filter applied), sorted by label.</summary>
        public List<ThingDef> AllDefs() => Candidates;

        /// <summary>Allows every listable def.</summary>
        public void AllowAll()
        {
            var f = Filter;
            foreach (var d in Candidates) f.SetAllow(d, true);
            SetSpecials(f, specialCandidates, true);
            edits++;
        }

        /// <summary>Disallows everything the filter allows.</summary>
        public void ClearAll()
        {
            var f = Filter;
            var all = new List<ThingDef>(f.AllowedThingDefs);
            foreach (var d in all) f.SetAllow(d, false);
            SetSpecials(f, ManagedSpecialFilters(), false);
            edits++;
        }

        /// <summary>Allows or disallows every listable def in cat and its subcategories.</summary>
        public void SetCategory(ThingCategoryDef cat, bool allow)
        {
            if (cat == null) return;
            SetCategoryIn(Filter, cat, allow);
            edits++;
        }

        /// <summary>
        /// On / Partial / Off over the listable defs in cat and its subcategories (vanilla rule: Off when no def is
        /// allowed; On only when every def and every special filter that can apply to them is allowed).
        /// </summary>
        public MultiCheckboxState CategoryState(ThingCategoryDef cat)
        {
            var f = Filter;
            if (cat == null) return MultiCheckboxState.Off;
            if (stateCache.TryGetValue(cat, out var s)) return s;
            var list = CatDefs(cat);
            int n = 0;
            foreach (var d in list) if (f.Allows(d)) n++;
            bool specialsOn = true;
            foreach (var sf in CatSpecials(cat, own: false)) if (!f.Allows(sf)) { specialsOn = false; break; }
            s = n == 0 ? MultiCheckboxState.Off : n == list.Count && specialsOn ? MultiCheckboxState.On : MultiCheckboxState.Partial;
            stateCache[cat] = s;
            return s;
        }

        /// <summary>Allows (only if listable, with the special filters that can apply to it) or disallows one def.</summary>
        public void SetItem(ThingDef def, bool allow)
        {
            if (def == null) return;
            var f = Filter;
            _ = Candidates;
            if (allow && !candidateSet.Contains(def)) return;
            f.SetAllow(def, allow);
            if (allow) SetSpecials(f, DefSpecials(def), true);
            edits++;
        }

        /// <summary>
        /// Categories (with listable defs) then items whose label contains the search text, case-insensitive, each group
        /// sorted by label. Empty while the text is blank. The returned list is cached; don't modify it.
        /// </summary>
        public List<FilterSearchResult> Results()
        {
            _ = Candidates;
            string q = search.Trim();
            if (resultsCache != null && q == resultsFor) return resultsCache;
            resultsFor = q;
            allResultsAllowedCache = null;
            resultsScroll = Vector2.zero;
            var list = new List<FilterSearchResult>();
            if (q.Length > 0)
            {
                var cats = new List<ThingCategoryDef>();
                foreach (var c in DefDatabase<ThingCategoryDef>.AllDefsListForReading)
                    if (c != ThingCategoryDefOf.Root && Matches(c.label, q) && CatDefs(c).Count > 0) cats.Add(c);
                cats.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
                string catTag = "CB_FilterCategoryTag".Translate();
                foreach (var c in cats) list.Add(new FilterSearchResult(null, c, c.LabelCap, catTag));
                foreach (var d in candidates)
                    if (Matches(d.label, q) && !Hidden(d))
                        list.Add(new FilterSearchResult(d, null, d.LabelCap, d.FirstThingCategory?.LabelCap.ToString() ?? ""));
            }
            resultsCache = list;
            return list;
        }

        /// <summary>True when there are results and every one is allowed (items allowed, categories fully On).</summary>
        public bool AllResultsAllowed()
        {
            var f = Filter;
            var res = Results();
            if (allResultsAllowedCache.HasValue) return allResultsAllowedCache.Value;
            bool all = res.Count > 0;
            foreach (var r in res)
            {
                bool on = r.Def != null ? f.Allows(r.Def) : CategoryState(r.Category) == MultiCheckboxState.On;
                if (!on) { all = false; break; }
            }
            allResultsAllowedCache = all;
            return all;
        }

        /// <summary>"Allow all" / "Disallow all": applies to every current result.</summary>
        public void SetAllResults(bool allow)
        {
            var f = Filter;
            foreach (var r in Results())
            {
                if (r.Def != null)
                {
                    f.SetAllow(r.Def, allow);
                    if (allow) SetSpecials(f, DefSpecials(r.Def), true);
                }
                else SetCategoryIn(f, r.Category, allow);
            }
            edits++;
        }

        /// <summary>Every allowed listable item, sorted by label. Cached; don't modify it.</summary>
        public List<ThingDef> AllowedDefs()
        {
            var f = Filter;
            if (allowedCache != null) return allowedCache;
            var list = new List<ThingDef>();
            foreach (var d in Candidates)
                if (f.Allows(d) && !Hidden(d)) list.Add(d);
            allowedCache = list;
            return list;
        }

        public bool IsExpanded(ThingCategoryDef cat) => cat != null && expanded.Contains(cat);

        /// <summary>Opens or closes a category in the tree (the arrow's call).</summary>
        public void SetExpanded(ThingCategoryDef cat, bool open)
        {
            if (cat == null) return;
            if (open ? expanded.Add(cat) : expanded.Remove(cat)) treeRows = null;
        }

        // ---- tree rows ----

        List<TreeRow> TreeRows()
        {
            _ = Candidates;
            if (treeRows != null) return treeRows;
            var rows = new List<TreeRow>();
            var root = ThingCategoryNodeDatabase.RootNode?.catDef;
            if (root != null) AddChildren(rows, root, 0);
            treeRows = rows;
            return rows;
        }

        void AddChildren(List<TreeRow> rows, ThingCategoryDef cat, int depth)
        {
            foreach (var c in cat.childCategories)
            {
                if (CatDefs(c).Count == 0) continue;
                rows.Add(new TreeRow(c, null, depth));
                if (expanded.Contains(c)) AddChildren(rows, c, depth + 1);
            }
            var children = cat.SortedChildThingDefs ?? cat.childThingDefs;
            foreach (var d in children)
                if (candidateSet.Contains(d) && !Hidden(d)) rows.Add(new TreeRow(null, d, depth));
        }

        // ---- drawing ----

        /// <summary>Header row (32) + search box (34) + the two panes.</summary>
        public override float Height(float width) => 32f + 34f + PaneHeaderH + PaneH;

        public override float Draw(Rect rect)
        {
            inDraw = true;
            sigChecked = false;
            try { return DrawInner(rect); }
            finally { inDraw = false; }
        }

        float DrawInner(Rect rect)
        {
            _ = Filter;
            float x = rect.x, w = rect.width, y = rect.y;
            Text.Font = GameFont.Small;
            pending = null;

            const float BtnW = 70f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, w - 2f * BtnW - 8f, 28f), "CB_FilterItems".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(x + w - 2f * BtnW - 4f, y, BtnW, 28f), "CB_FilterAll".Translate())) pending = AllowAll;
            if (Widgets.ButtonText(new Rect(x + w - BtnW, y, BtnW, 28f), "CB_FilterNone".Translate())) pending = ClearAll;
            y += 32f;

            DrawSearchBox(new Rect(x, y, w, 28f));
            y += 34f;

            float paneW = (w - Gap) / 2f;
            var left = new Rect(x, y, paneW, PaneHeaderH + PaneH);
            var right = new Rect(x + paneW + Gap, y, paneW, PaneHeaderH + PaneH);
            if (Searching) DrawResults(left);
            else DrawTree(left);
            DrawAllowed(right);
            y += PaneHeaderH + PaneH;

            var act = pending;
            pending = null;
            act?.Invoke();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y - rect.y;
        }

        void DrawSearchBox(Rect r)
        {
            GUI.DrawTexture(new Rect(r.x + 4f, r.y + 5f, 18f, 18f), TexButton.Search);
            var field = new Rect(r.x + 26f, r.y, r.width - 26f - 24f, r.height);
            search = Widgets.TextField(field, search, 40) ?? "";
            if (search.Length > 0 && Widgets.ButtonImage(new Rect(field.xMax + 5f, r.y + 6f, 16f, 16f), TexButton.CloseXSmall))
                search = "";
        }

        static void PaneHeader(Rect pane, string label)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(pane.x + 2f, pane.y, pane.width, PaneHeaderH - 2f), label);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        static Rect PaneBody(Rect pane)
        {
            var body = new Rect(pane.x, pane.y + PaneHeaderH, pane.width, PaneH);
            Widgets.DrawMenuSection(body);
            return body.ContractedBy(2f);
        }

        static void VisibleRange(int count, float scrollY, float height, out int from, out int to)
        {
            from = Mathf.Max(0, (int)(scrollY / RowH));
            to = Mathf.Min(count, (int)((scrollY + height) / RowH) + 2);
        }

        static void RowLabel(Rect r, string label, Dictionary<string, string> cache)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(r, label.Truncate(r.width, cache));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        void DrawTree(Rect pane)
        {
            PaneHeader(pane, "CB_FilterCategories".Translate());
            var outR = PaneBody(pane);
            var rows = TreeRows();
            var f = Filter;
            var view = new Rect(0f, 0f, outR.width - 16f, rows.Count * RowH);
            Widgets.BeginScrollView(outR, ref treeScroll, view);
            VisibleRange(rows.Count, treeScroll.y, outR.height, out int from, out int to);
            for (int i = from; i < to; i++)
            {
                var row = rows[i];
                var r = new Rect(0f, i * RowH, view.width, RowH);
                Widgets.DrawHighlightIfMouseover(r);
                float rx = row.Depth * Indent;
                if (row.Cat != null)
                {
                    var cat = row.Cat;
                    bool open = expanded.Contains(cat);
                    var labelR = new Rect(rx + ArrowW + 2f + Box + 4f, r.y, 0f, RowH);
                    labelR.xMax = r.xMax;
                    if (Widgets.ButtonImage(new Rect(rx, r.y + 3f, ArrowW, ArrowW), open ? TexButton.Collapse : TexButton.Reveal)
                        || Widgets.ButtonInvisible(labelR, doMouseoverSound: false))
                    {
                        (open ? SoundDefOf.TabClose : SoundDefOf.TabOpen).PlayOneShotOnCamera();
                        pending = () => SetExpanded(cat, !open);
                    }
                    rx += ArrowW + 2f;
                    var st = CategoryState(cat);
                    var nst = Widgets.CheckboxMulti(new Rect(rx, r.y + 1f, Box, Box), st, paintable: true);
                    if (nst != st) pending = () => SetCategory(cat, nst == MultiCheckboxState.On);
                    RowLabel(labelR, cat.LabelCap, truncTree);
                }
                else
                {
                    var d = row.Def;
                    rx += ArrowW + 2f;
                    bool on = f.Allows(d), was = on;
                    Widgets.Checkbox(new Vector2(rx, r.y + 1f), ref on, Box, paintable: true);
                    if (on != was) pending = () => SetItem(d, on);
                    rx += Box + 4f;
                    Widgets.DefIcon(new Rect(rx, r.y + 2f, IconSize, IconSize), d, drawPlaceholder: true);
                    rx += IconSize + 4f;
                    RowLabel(new Rect(rx, r.y, r.xMax - rx, RowH), d.LabelCap, truncTree);
                }
            }
            Widgets.EndScrollView();
        }

        void DrawResults(Rect pane)
        {
            var res = Results();
            var f = Filter;
            const float BtnW = 100f;
            PaneHeader(new Rect(pane.x, pane.y, pane.width - BtnW, pane.height), "CB_FilterResults".Translate(res.Count));
            if (res.Count > 0)
            {
                bool all = AllResultsAllowed();
                if (Widgets.ButtonText(new Rect(pane.xMax - BtnW, pane.y, BtnW, PaneHeaderH - 4f),
                                       (all ? "CB_FilterDisallowAll" : "CB_FilterAllowAll").Translate()))
                    pending = () => SetAllResults(!all);
            }
            var outR = PaneBody(pane);
            var view = new Rect(0f, 0f, outR.width - 16f, res.Count * RowH);
            if (layoutFor != res || layoutWidth != view.width)
            {
                layoutFor = res;
                layoutWidth = view.width;
                shownLabels = new string[res.Count];
                shownTags = new string[res.Count];
                tagWidths = new float[res.Count];
            }
            Widgets.BeginScrollView(outR, ref resultsScroll, view);
            VisibleRange(res.Count, resultsScroll.y, outR.height, out int from, out int to);
            for (int i = from; i < to; i++)
            {
                var item = res[i];
                var r = new Rect(0f, i * RowH, view.width, RowH);
                Widgets.DrawHighlightIfMouseover(r);
                float rx = 2f;
                if (item.Def != null)
                {
                    var d = item.Def;
                    bool on = f.Allows(d), was = on;
                    Widgets.Checkbox(new Vector2(rx, r.y + 1f), ref on, Box, paintable: true);
                    if (on != was) pending = () => SetItem(d, on);
                    rx += Box + 4f;
                    Widgets.DefIcon(new Rect(rx, r.y + 2f, IconSize, IconSize), d, drawPlaceholder: true);
                    rx += IconSize + 4f;
                }
                else
                {
                    var cat = item.Category;
                    var st = CategoryState(cat);
                    var nst = Widgets.CheckboxMulti(new Rect(rx, r.y + 1f, Box, Box), st, paintable: true);
                    if (nst != st) pending = () => SetCategory(cat, nst == MultiCheckboxState.On);
                    rx += Box + 4f + IconSize + 4f;
                }
                float avail = r.xMax - rx - 4f;
                if (shownLabels[i] == null) LayoutResult(i, item, avail);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rx, r.y, avail - tagWidths[i] - 8f, RowH), shownLabels[i]);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(r.xMax - 4f - tagWidths[i], r.y, tagWidths[i], RowH), shownTags[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            Widgets.EndScrollView();
        }

        /// <summary>Label first: the tag gets what is left, but at least 35% of the row when the tag needs it.</summary>
        void LayoutResult(int i, FilterSearchResult item, float avail)
        {
            Text.Font = GameFont.Small;
            float lw = Text.CalcSize(item.Label).x;
            Text.Font = GameFont.Tiny;
            float tw = Text.CalcSize(item.Tag).x;
            float tagW = lw + tw + 8f <= avail ? tw : Mathf.Max(Mathf.Min(tw, avail * 0.35f), avail - lw - 8f);
            shownTags[i] = item.Tag.Truncate(tagW);
            Text.Font = GameFont.Small;
            shownLabels[i] = item.Label.Truncate(avail - tagW - 8f);
            tagWidths[i] = tagW;
        }

        void DrawAllowed(Rect pane)
        {
            var list = AllowedDefs();
            PaneHeader(pane, "CB_FilterAllowed".Translate(list.Count));
            var outR = PaneBody(pane);
            if (list.Count == 0)
            {
                GUI.color = Color.gray;
                RowLabel(new Rect(outR.x + 6f, outR.y, outR.width - 6f, RowH), "CB_FilterEmpty".Translate(), truncAllowed);
                GUI.color = Color.white;
                return;
            }
            var view = new Rect(0f, 0f, outR.width - 16f, list.Count * RowH);
            Widgets.BeginScrollView(outR, ref allowedScroll, view);
            VisibleRange(list.Count, allowedScroll.y, outR.height, out int from, out int to);
            for (int i = from; i < to; i++)
            {
                var d = list[i];
                var r = new Rect(0f, i * RowH, view.width, RowH);
                Widgets.DrawHighlightIfMouseover(r);
                Widgets.DefIcon(new Rect(r.x + 4f, r.y + 2f, IconSize, IconSize), d, drawPlaceholder: true);
                RowLabel(new Rect(r.x + IconSize + 10f, r.y, r.width - IconSize - 34f, RowH), d.LabelCap, truncAllowed);
                if (Widgets.ButtonImage(new Rect(r.xMax - 22f, r.y + 4f, 16f, 16f), TexButton.CloseXSmall))
                    pending = () => SetItem(d, false);
            }
            Widgets.EndScrollView();
        }
    }
}
