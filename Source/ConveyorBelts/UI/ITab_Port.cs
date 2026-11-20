using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>The single inspect tab of every port: a header plus the selected owner's sections in a scroll view.</summary>
    public class ITab_Port : ITab
    {
        static readonly Vector2 WinSize = new Vector2(420f, 480f);
        const float HeaderHeight = 32f;
        const float SectionGap = 12f;

        Vector2 scroll;
        float contentHeight = 100f;

        public ITab_Port()
        {
            size = WinSize;
            labelKey = "CB_TabPort";
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
            var outer = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(outer.x, outer.y, outer.width - 30f, HeaderHeight), SelThing.LabelCap);
            Text.Font = GameFont.Small;

            var scrollOut = new Rect(outer.x, outer.y + HeaderHeight, outer.width, outer.height - HeaderHeight);
            var view = new Rect(0f, 0f, scrollOut.width - 16f, Mathf.Max(contentHeight, scrollOut.height - 1f));
            Widgets.BeginScrollView(scrollOut, ref scroll, view);
            float y = 0f;
            bool first = true;
            foreach (var section in owner.TabSections)
            {
                if (!first)
                {
                    Widgets.DrawLineHorizontal(0f, y + SectionGap / 2f, view.width);
                    y += SectionGap;
                }
                first = false;
                y += section.Draw(new Rect(0f, y, view.width, 100000f));
            }
            Widgets.EndScrollView();
            contentHeight = y;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
