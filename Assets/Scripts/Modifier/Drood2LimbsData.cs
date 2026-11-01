using ModApi.Craft.Parts;
using ModApi.Craft.Parts.Attributes;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Data half of Drood 2.0's second part modifier: its limbs' settings, in their own section
    /// of the designer's part properties ("Attached Parts", below the Drood 2.0 settings). Declared
    /// after the others in the part XML; a craft saved without it gets it with its defaults (the
    /// game creates every modifier of the part type, taking the craft's values where it has them).
    ///
    /// SAVE FORMAT: the type id is an XML element in every craft and save with a Drood 2.0.
    /// Never rename it.
    /// </summary>
    [System.Serializable]
    [PartModifierTypeId(ModifierTypeId)]
    [DesignerPartModifier(SectionLabel, PanelOrder = Drood2Data.PanelOrder + 1)]
    public class Drood2LimbsData : PartModifierData<Drood2LimbsScript>
    {
        public const string ModifierTypeId = "Drood2Limbs";

        // The section's header (DesignerPartModifier: the game builds it for the modifier's panel;
        // OnHeaderLabelRequested only renames an existing one, and a panel without one throws).
        private const string SectionLabel = "Attached Parts";

        // What the drood does with parts attached to its limbs: Automatic (light ones carried,
        // anything bigger the drood is attached to, DroodHeldParts), Carried (all carried: they
        // follow its limbs, it walks with them) or Anchored (it is attached to them: it doesn't
        // walk or fall, it rests in a calm pose).
        // SAVE FORMAT: the values are saved as text; an XML attribute.
        public const string AttachedAutomatic = "Automatic";
        public const string AttachedCarried = "Carried";
        public const string AttachedAnchored = "Anchored";

        [SerializeField]
        [DesignerPropertySpinner(AttachedAutomatic, AttachedCarried, AttachedAnchored, Label = "Mode", Tooltip = "<align=left><color=#E6E6E6><color=#5AB4FF><b>Automatic:</b></color> light parts (up to 100 kg and 25 parts on a limb, no command pod) are carried; anything bigger holds the drood.\n\n<color=#5AB4FF><b>Carried:</b></color> the drood carries the part(s).\n\n<color=#5AB4FF><b>Anchored:</b></color> the drood is attached to a part.</color>")]
        private string _attachedParts = AttachedAutomatic;

        internal string AttachedParts => _attachedParts;

        // When the drood lets go of what it is connected to (besides Vizzy's "activate part"):
        // never, or when the craft it is on switches on one of these activation groups (that
        // craft's controlling command pod; AG1 to AG4 are the drood's own controls).
        // SAVE FORMAT: the values are saved as text; an XML attribute.
        public const string ReleaseNever = "None";
        private const string ReleasePrefix = "AG ";

        [SerializeField]
        [DesignerPropertySpinner(ReleaseNever, "AG 5", "AG 6", "AG 7", "AG 8", "AG 9", "AG 10", Label = "Release on", Tooltip = "<align=left><color=#E6E6E6>Lets the drood go from what it is connected to when the craft it is on switches this activation group on (e.g. the plane's AG 5), so it drops. Vizzy's activate part releases it too.</color>")]
        private string _releaseOn = ReleaseNever;

        /// <summary>The activation group of the craft it is on that releases the drood; 0 for none.</summary>
        internal int ReleaseGroup
        {
            get
            {
                if (!ReferenceEquals(_releaseGroupOf, _releaseOn))
                {
                    _releaseGroupOf = _releaseOn;
                    _releaseGroup = _releaseOn != null && _releaseOn.StartsWith(ReleasePrefix, System.StringComparison.Ordinal)
                        && int.TryParse(_releaseOn.Substring(ReleasePrefix.Length), out var group) ? group : 0;
                }

                return _releaseGroup;
            }
        }

        // The group parsed once per setting.
        [System.NonSerialized]
        private string _releaseGroupOf;
        [System.NonSerialized]
        private int _releaseGroup;

        /// <summary>A craft saved with the setting on the Drood 2.0 modifier keeps its choice.</summary>
        public override void OnPartLoaded()
        {
            base.OnPartLoaded();
            var earlier = Part.GetModifier<Drood2Data>()?.TakeEarlierAttachedParts();
            if (!string.IsNullOrEmpty(earlier))
            {
                _attachedParts = earlier;
            }
        }
    }
}
