using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>Input Port: one row with the vanilla haul priority dropdown (edits the owner's StorageSettings).</summary>
    public class Section_HaulPriority : PortTabSection
    {
        readonly IStoreSettingsParent owner;

        public Section_HaulPriority(IStoreSettingsParent owner) { this.owner = owner; }

        public StoragePriority Priority => owner.GetStoreSettings().Priority;

        public void SetPriority(StoragePriority p)
        {
            if (p == StoragePriority.Unstored) return;
            owner.GetStoreSettings().Priority = p;
        }

        public override float Draw(Rect rect)
        {
            float x = rect.x, y = rect.y;
            Text.Font = GameFont.Small;
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
            return 30f;
        }
    }
}
