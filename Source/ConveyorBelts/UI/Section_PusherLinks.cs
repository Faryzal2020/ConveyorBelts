using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Pusher Port tab, top block: what the pusher recognises behind and in front ("None" when nothing) and the
    /// "Pull from forbidden" checkbox (which sets the owner's PullForbidden, as the debug action does).
    /// </summary>
    public class Section_PusherLinks : PortTabSection
    {
        const float RowH = 26f;

        readonly Building_PusherPort owner;

        public Section_PusherLinks(Building_PusherPort owner) { this.owner = owner; }

        static string OrNone(string s) => s ?? "CB_None".Translate().ToString();

        public string PullingLine() => "CB_PusherPullingFrom".Translate(OrNone(owner.PullingFrom)).ToString();
        public string PushingLine() => "CB_PusherPushingTo".Translate(OrNone(owner.PushingTo)).ToString();

        public override float Height(float width) => 3f * RowH;

        public override float Draw(Rect rect)
        {
            float x = rect.x, w = rect.width, y = rect.y;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, w, RowH), PullingLine());
            y += RowH;
            Widgets.Label(new Rect(x, y, w, RowH), PushingLine());
            y += RowH;
            Text.Anchor = TextAnchor.UpperLeft;
            bool v = owner.PullForbidden;
            Widgets.CheckboxLabeled(new Rect(x, y + 1f, Mathf.Min(w, 260f), 24f), "CB_PullForbidden".Translate(), ref v, placeCheckboxNearText: true);
            if (v != owner.PullForbidden) owner.PullForbidden = v;
            y += RowH;
            return y - rect.y;
        }
    }
}
