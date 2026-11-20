using System.Collections.Generic;

namespace ConveyorBelts
{
    /// <summary>A building whose settings are shown in the Port tab, as a stack of sections.</summary>
    public interface IPortTabOwner
    {
        IEnumerable<PortTabSection> TabSections { get; }
    }
}
