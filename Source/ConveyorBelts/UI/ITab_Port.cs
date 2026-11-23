using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// The single inspect tab of every port: a header plus the selected owner's sections. The window is as tall as its
    /// content (each section declares its height), so there is no outer scrollbar to fight the filter panes for the
    /// mouse wheel; only on a screen too short for it does the content scroll.
    /// </summary>
    public class ITab_Port : ITab
    {
        const float Width = 560f;
        const float Margin = 10f;
        const float HeaderHeight = 32f;
        const float SectionGap = 12f;
        const float ScrollBarW = 16f;

        Vector2 scroll;

        public ITab_Port()
        {
            size = new Vector2(Width, 560f);
            labelKey = "CB_TabPort";
        }

        /// <summary>Sum of the owner's section heights plus the gaps between them, at the given content width.</summary>
        static float ContentHeight(IPortTabOwner owner, float width)
        {
            float h = 0f;
            bool first = true;
            foreach (var section in owner.TabSections)
            {
                if (!first) h += SectionGap;
                first = false;
                h += section.Height(width);
            }
            return h;
        }

        float ContentWidth => Width - 2f * Margin;

        protected override void UpdateSize()
        {
            base.UpdateSize();
            var owner = Owner;
            if (owner == null) return;
            float want = ContentHeight(owner, ContentWidth) + HeaderHeight + 2f * Margin;
            float max = Mathf.Max(200f, PaneTopY - 30f - 10f);   // keep the window on screen
            size = new Vector2(Width, Mathf.Min(want, max));
        }

        IPortTabOwner Owner => SelThing as IPortTabOwner;

        public override bool IsVisible
        {
            get
            {
                var t = SelThing;
                return t is IPortTabOwner && (t.Faction == null || t.Faction == Faction.OfPlayer);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            scroll = Vector2.zero;
        }

        protected override void FillTab()
        {
            var owner = Owner;
            if (owner == null) return;
            var outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(Margin);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(outer.x, outer.y, outer.width - 30f, HeaderHeight), SelThing.LabelCap);
            Text.Font = GameFont.Small;

            var area = new Rect(outer.x, outer.y + HeaderHeight, outer.width, outer.height - HeaderHeight);
            float content = ContentHeight(owner, area.width);
            bool scrolls = content > area.height + 1f;   // only on a screen too short for the window
            float w = scrolls ? area.width - ScrollBarW : area.width;
            if (scrolls)
            {
                Widgets.BeginScrollView(area, ref scroll, new Rect(0f, 0f, w, ContentHeight(owner, w)));
            }
            else Widgets.BeginGroup(area);
            float y = 0f;
            bool first = true;
            foreach (var section in owner.TabSections)
            {
                if (!first)
                {
                    Widgets.DrawLineHorizontal(0f, y + SectionGap / 2f, w);
                    y += SectionGap;
                }
                first = false;
                y += section.Draw(new Rect(0f, y, w, 100000f));
            }
            if (scrolls) Widgets.EndScrollView();
            else Widgets.EndGroup();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
