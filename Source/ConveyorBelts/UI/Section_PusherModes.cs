using System.Collections.Generic;
using ConveyorBelts.Sim;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Pusher Port tab: the machine it faces and what it takes right now, the reject mode and the eject side.
    /// The radio buttons and the dropdown call the pusher's setters (also used by the debug actions).
    /// </summary>
    public class Section_PusherModes : PortTabSection
    {
        readonly Building_PusherPort owner;

        public Section_PusherModes(Building_PusherPort owner) { this.owner = owner; }

        public static string ModeKey(PusherRejectMode m) => m switch
        {
            PusherRejectMode.EjectIfNotAcceptedNow => "CB_PusherEjectNow",
            PusherRejectMode.AlwaysWait => "CB_PusherAlwaysWait",
            _ => "CB_PusherEjectNever",
        };

        public static string SidesKey(EjectSides s) => s == EjectSides.RightThenLeft ? "CB_EjectRightThenLeft" : "CB_EjectLeftThenRight";

        /// <summary>"Facing: …" line.</summary>
        public string FacingLine()
        {
            var t = owner.Target;
            return t == null ? "CB_PusherFacingNothing".Translate().ToString() : "CB_PusherFacing".Translate(t.LabelCap).ToString();
        }

        /// <summary>"Accepts now: …" line: for the held item if there is one, else the machine's own summary.</summary>
        public string AcceptsLine()
        {
            var t = owner.Target;
            var a = PushAdapters.For(t);
            string what = null;
            if (a != null)
            {
                var held = owner.Buffer;
                if (held != null)
                {
                    int n = a.CanAccept(t, held);
                    if (n > 0) what = PushAdapters.UpTo(held.def, n);
                }
                else what = a.DescribeAcceptsNow(t);
            }
            return "CB_PusherAcceptsNow".Translate(what ?? "CB_PusherNothing".Translate().ToString()).ToString();
        }

        public override float Draw(Rect rect)
        {
            float x = rect.x, w = rect.width, y = rect.y;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, w, 24f), FacingLine());
            y += 24f;
            string accepts = AcceptsLine();
            float h = Mathf.Max(24f, Text.CalcHeight(accepts, w));
            Widgets.Label(new Rect(x, y, w, h), accepts);
            y += h + 12f;

            Widgets.Label(new Rect(x, y, w, 24f), "CB_PusherWhenRejected".Translate());
            y += 28f;
            foreach (var m in new[] { PusherRejectMode.EjectIfNeverAccepted, PusherRejectMode.EjectIfNotAcceptedNow, PusherRejectMode.AlwaysWait })
            {
                var row = new Rect(x, y, w, 26f);
                if (Widgets.RadioButtonLabeled(row, ModeKey(m).Translate(), owner.RejectMode == m))
                    owner.SetRejectMode(m);
                TooltipHandler.TipRegion(row, (ModeKey(m) + "Desc").Translate());
                y += 28f;
            }
            y += 8f;

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, 110f, 30f), "CB_PusherEjectSide".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(x + 115f, y, 180f, 30f), SidesKey(owner.EjectSides).Translate()))
            {
                var opts = new List<FloatMenuOption>();
                foreach (var s in new[] { EjectSides.LeftThenRight, EjectSides.RightThenLeft })
                {
                    var local = s;
                    opts.Add(new FloatMenuOption(SidesKey(local).Translate(), () => owner.SetEjectSides(local)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 34f;
            return y - rect.y;
        }
    }
}
