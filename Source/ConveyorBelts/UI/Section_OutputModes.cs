using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>What an output port does when its front cell can't take the item. The buttons call SetFullMode on the port.</summary>
    public class Section_OutputModes : PortTabSection
    {
        readonly Building_OutputPort owner;

        public Section_OutputModes(Building_OutputPort owner) { this.owner = owner; }

        public OutputFullMode Mode => owner.FullMode;
        public void SetMode(OutputFullMode m) => owner.SetFullMode(m);

        public override float Height(float width) => 3f * 28f;

        public override float Draw(Rect rect)
        {
            float x = rect.x, w = rect.width, y = rect.y;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, w, 24f), "CB_WhenFull".Translate());
            y += 28f;
            if (Widgets.RadioButtonLabeled(new Rect(x, y, w, 26f), "CB_OutputHold".Translate(), Mode == OutputFullMode.Hold))
                SetMode(OutputFullMode.Hold);
            y += 28f;
            if (Widgets.RadioButtonLabeled(new Rect(x, y, w, 26f), "CB_OutputSpill".Translate(), Mode == OutputFullMode.Spill))
                SetMode(OutputFullMode.Spill);
            y += 28f;
            return y - rect.y;
        }
    }
}
