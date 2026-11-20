using UnityEngine;

namespace ConveyorBelts
{
    /// <summary>One block of the Port tab. Sections keep their own UI state (search text etc.) and are owned by the port.</summary>
    public abstract class PortTabSection
    {
        /// <summary>Draws the section starting at rect.y (rect.height is only an upper bound). Returns the height used.</summary>
        public abstract float Draw(Rect rect);
    }
}
