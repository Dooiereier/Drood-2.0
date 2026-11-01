using System.Collections.Generic;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using ModApi.Craft.Parts.Attributes;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Data half of the Drood 2.0 part modifier, declared in Drood 2.0's part XML. Juno creates
    /// it only for Drood 2.0, so the stock drood never gets it.
    ///
    /// SAVE FORMAT: the type id is an XML element in every craft and save with a Drood 2.0.
    /// Never rename it. Serializable like every stock modifier data class: the game
    /// XML-serializes it for designer undo steps, craft files and saves.
    /// </summary>
    [System.Serializable]
    [PartModifierTypeId(ModifierTypeId)]
    [DesignerPartModifier(SectionLabel, PanelOrder = PanelOrder)]
    public class Drood2Data : PartModifierData<Drood2Script>
    {
        public const string ModifierTypeId = "Drood2";

        // Its section in the designer's part properties: first (the game sorts the panels by
        // PanelOrder, the stock ones at 0, and builds them from the last modifier back), then
        // Attached Parts (Drood2LimbsData).
        private const string SectionLabel = "Parachute & Challenge";
        internal const int PanelOrder = -2;

        private static readonly int[] ParachuteAndDropZoneGroups = { DroodParachute.ActivationGroup, DroodDropZone.ActivationGroup, DroodAutoCam.ActivationGroup, DroodSteeringDisplay.ActivationGroup };
        private static readonly int[] ParachuteAndSwoopGroups = { DroodParachute.ActivationGroup, DroodDropZone.ActivationGroup, DroodSteeringDisplay.ActivationGroup };
        private static readonly int[] ParachuteGroup = { DroodParachute.ActivationGroup, DroodSteeringDisplay.ActivationGroup };

        // The tooltips' paragraphs, left-aligned, in two near-whites by turns (a little dimmer, not
        // another hue, so neither stands out), warnings in amber italics (rich text).
        private const string TipLeft = "<align=left>";
        private const string TipText = "<color=#E6E6E6>";
        private const string TipLabel = "<color=#5AB4FF><b>";
        private const string TipLabelEnd = "</b></color>";
        private const string TipWarn = "<i><color=#F0B46A>";
        private const string TipWarnEnd = "</color></i>";
        private const string TipEnd = "</color>";
        private const string TipBreak = "\n\n";

        public const string ChallengeOff = "Off";
        public const string ChallengeDropZone = "Drop Zone";
        public const string ChallengeSwoopStraight = "Swoop Straight";
        public const string ChallengeSwoop = "Swoop 270 turn";

        public const string RoundCanopy = "Round";
        public const string RamAirCanopy = "Ram-air";
        public const string SwoopCanopy = "Swoop";

        // The canopy type, chosen in the designer. SAVE FORMAT: the values are saved as text.
        [SerializeField]
        [DesignerPropertySpinner(RoundCanopy, RamAirCanopy, SwoopCanopy, Label = "Canopy", Tooltip = TipLeft + TipText + TipLabel + "Round:" + TipLabelEnd + " auto deployment and gentle steering; no challenges." + TipEnd + TipBreak + TipText + TipLabel + "Ram-air:" + TipLabelEnd + " a fast, agile canopy with steering lines, brakes and front risers." + TipEnd + TipBreak + TipText + TipLabel + "Swoop:" + TipLabelEnd + " a small, heavily loaded ram-air canopy, meant for the Swoop challenge: fast glide, very fast dives, long swoops. It needs a well-timed flare." + TipEnd + TipBreak + TipWarn + "Careful with deep brakes: a slow canopy can stall." + TipWarnEnd)]
        private string _canopy = RamAirCanopy;

        /// <summary>
        /// A ram-air canopy was chosen (the ram-air, the default also when a craft doesn't store
        /// the setting, or the swoop wing); anything else is round.
        /// </summary>
        internal bool RamAir => _canopy == RamAirCanopy || _canopy == SwoopCanopy;

        /// <summary>The swoop wing (a small, fast ram-air) was chosen.</summary>
        internal bool SwoopWing => _canopy == SwoopCanopy;

        // Parachute state, kept across quicksave and quickload (preserveState). SAVE FORMAT:
        // these names are XML attributes in saves; only ever add.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private int _chuteState;

        [SerializeField]
        [PartModifierProperty(true, false)]
        private float _chuteExtend;

        [SerializeField]
        [PartModifierProperty(true, false)]
        private float _chuteInflate;

        [SerializeField]
        [PartModifierProperty(true, false)]
        private float _chuteCollapse;

        // Held parts' grips on their bones (DroodHeldGrips). Saved with the craft (the designer
        // measures them) and in quicksaves. SAVE FORMAT: see DroodHeldGrips.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private string _heldGrips = string.Empty;

        internal string HeldGrips { get => _heldGrips; set => _heldGrips = value; }

        /// <summary>Parachute state (<see cref="DroodParachute"/>'s State as a number).</summary>
        internal int ChuteState { get => _chuteState; set => _chuteState = value; }

        internal float ChuteExtend { get => _chuteExtend; set => _chuteExtend = value; }

        internal float ChuteInflate { get => _chuteInflate; set => _chuteInflate = value; }

        internal float ChuteCollapse { get => _chuteCollapse; set => _chuteCollapse = value; }

        // The parachute switched the jetpack off while hanging, and switches it back on after
        // landing (DroodParachute.UpdateJetpack). Saved, so a quickload while hanging keeps it.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private bool _jetpackSuspended;

        internal bool JetpackSuspended { get => _jetpackSuspended; set => _jetpackSuspended = value; }

        // The challenge, chosen in the designer: the Drop Zone (DroodDropZone: launched, the drood
        // is teleported up to jump at the launch spot) or the Swoop course (DroodSwoopCourse).
        // SAVE FORMAT: the values are saved as text; these names are XML attributes.
        [SerializeField]
        [DesignerPropertySpinner(ChallengeOff, ChallengeDropZone, ChallengeSwoopStraight, ChallengeSwoop, Label = "Challenge", Tooltip = TipLeft + TipText + TipLabel + "Drop Zone:" + TipLabelEnd + " jump from 3 km above the target. Land close to its centre, quickly and softly, and beat your high score. " + TipEnd + TipWarn + "Don't open too late: the canopy can open with a malfunction." + TipWarnEnd + TipBreak + TipText + TipLabel + "Swoop:" + TipLabelEnd + " under an open ram-air, dive between the gate's poles, below their tops, and fly as far down the lane as you can." + TipEnd + TipBreak + TipText + "<b>Straight</b> starts behind the gate, facing down the lane. <b>270 turn</b> starts beside the course, for a right 270° turn onto it." + TipEnd + TipBreak + TipText + "AG2 starts again." + TipEnd + TipBreak + TipText + TipLabel + "Tip:" + TipLabelEnd + " use mouse control for better accuracy." + TipEnd)]
        private string _challenge = ChallengeOff;

        // Where the Attached Parts setting was saved before it had its own section
        // (Drood2LimbsData), read once at load and handed over (TakeEarlierAttachedParts).
        // SAVE FORMAT: an XML attribute of crafts saved then.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private string _attachedParts = string.Empty;

        /// <summary>The Attached Parts setting saved on this modifier, if any (then cleared).</summary>
        internal string TakeEarlierAttachedParts()
        {
            var earlier = _attachedParts;
            _attachedParts = string.Empty;
            return earlier;
        }

        // The swoop's difficulty (both variants): a set start (Practice, Expert; its height and
        // distance shown greyed out, read only) or the typed one (Custom). Straight's sets:
        // Practice its first start (305 m, 250 m: ~200 m above its glide to the gate), Expert
        // less to spare (200 m, 200 m: ~120 m above it).
        // SAVE FORMAT: the values are saved as text; an XML attribute, appended.
        public const string DifficultyPractice = "Practice";
        public const string DifficultyExpert = "Expert";
        public const string DifficultyCustom = "Custom";
        private const float PracticeStartHeight = 380f;
        private const float PracticeStartDistance = 220f;
        private const float ExpertStartHeight = 280f;
        private const float ExpertStartDistance = 100f;
        private const float StraightPracticeHeight = 305f;
        private const float StraightPracticeDistance = 250f;
        private const float StraightExpertHeight = 200f;
        private const float StraightExpertDistance = 200f;
        private const string PresetColour = "#8A8A8A";

        [SerializeField]
        [DesignerPropertySpinner(DifficultyPractice, DifficultyExpert, DifficultyCustom, Label = "Difficulty", Tooltip = TipLeft + TipText + "The swoop's start: its height, and how far back from the starting line." + TipEnd)]
        private string _swoopDifficulty = DifficultyPractice;

        // The set start's values, shown read only (greyed) with Practice and Expert.
        [DesignerPropertyLabel(NeverSerialize = true)]
        private string _presetHeightLabel = string.Empty;

        [DesignerPropertyLabel(NeverSerialize = true)]
        private string _presetDistanceLabel = string.Empty;

        // The earlier switch for the Drop Zone (crafts saved before the Challenge selector):
        // read once at load into _challenge (OnPartLoaded), then off.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private bool _dropZone;

        // The challenge's spot ("planet;x;y;z", surface coordinates: the drop zone's target, the
        // swoop course's entry gate), and whether this launch's teleport is done (so a quickload
        // doesn't teleport again). Kept in quicksaves.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private string _dropTarget = string.Empty;

        [SerializeField]
        [PartModifierProperty(true, false)]
        private bool _dropStarted;

        // The swoop course's direction down its lane ("x;y;z", a surface vector). Kept in
        // quicksaves.
        [SerializeField]
        [PartModifierProperty(true, false)]
        private string _swoopHeading = string.Empty;

        // The swoop start (m, text typed in the designer, shown with the Swoop chosen): its
        // height above the course's ground, and how far back from the starting line (the gate's)
        // the start and the turn point are.
        // SAVE FORMAT: XML attributes, appended.
        private const float MinSwoopStartHeight = 50f;
        private const float MaxSwoopStartHeight = 3000f;
        private const float MaxSwoopStartDistance = 2000f;
        // Custom starts at Practice's values; a newly placed Drood 2.0 takes the last ones typed
        // (kept in the game's player preferences under CustomKey + the field's name).
        private const float DefaultSwoopStartHeight = PracticeStartHeight;
        private const float DefaultSwoopStartDistance = PracticeStartDistance;
        private const float DefaultStraightStartHeight = StraightPracticeHeight;
        private const float DefaultStraightStartDistance = StraightPracticeDistance;
        private const string CustomKey = "Drood20.Custom";

        private static string DefaultText(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Swoop start height (m)", Tooltip = "The start's height above the course's ground (50 to 3000). The turn comes a little lower, after the glide to it.")]
        private string _swoopStartHeight = DefaultText(DefaultSwoopStartHeight);

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Swoop start distance (m)", Tooltip = "How far back from the starting line the start and the turn point are (0 to 2000).")]
        private string _swoopStartDistance = DefaultText(DefaultSwoopStartDistance);

        // The straight swoop's start: its height above the course's ground, and how far straight
        // behind the starting line on the lane's line (facing down the lane).
        [SerializeField]
        [DesignerPropertyTextInput(Label = "Straight start height (m)", Tooltip = "The straight swoop's start height above the course's ground (50 to 3000).")]
        private string _straightStartHeight = DefaultText(DefaultStraightStartHeight);

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Straight start distance (m)", Tooltip = "How far straight behind the starting line the straight swoop starts, facing down the lane (0 to 2000).")]
        private string _straightStartDistance = DefaultText(DefaultStraightStartDistance);

        // Whether this part's Custom values were set (from the last typed, when it was placed).
        [SerializeField]
        [PartModifierProperty(true, false)]
        private bool _customTaken;

        internal float StraightStartHeight =>
            _swoopDifficulty == DifficultyPractice ? StraightPracticeHeight
            : _swoopDifficulty == DifficultyExpert ? StraightExpertHeight
            : ParseMetres(_straightStartHeight, MinSwoopStartHeight, MaxSwoopStartHeight, DefaultStraightStartHeight);

        internal float StraightStartDistance =>
            _swoopDifficulty == DifficultyPractice ? StraightPracticeDistance
            : _swoopDifficulty == DifficultyExpert ? StraightExpertDistance
            : ParseMetres(_straightStartDistance, 0f, MaxSwoopStartDistance, DefaultStraightStartDistance);

        internal float SwoopStartHeight =>
            _swoopDifficulty == DifficultyPractice ? PracticeStartHeight
            : _swoopDifficulty == DifficultyExpert ? ExpertStartHeight
            : ParseMetres(_swoopStartHeight, MinSwoopStartHeight, MaxSwoopStartHeight, DefaultSwoopStartHeight);

        internal float SwoopStartDistance =>
            _swoopDifficulty == DifficultyPractice ? PracticeStartDistance
            : _swoopDifficulty == DifficultyExpert ? ExpertStartDistance
            : ParseMetres(_swoopStartDistance, 0f, MaxSwoopStartDistance, DefaultSwoopStartDistance);

        internal string SwoopDifficulty => _swoopDifficulty;

        /// <summary>The challenge in effect: none with a round canopy (the choice stays saved).</summary>
        internal string Challenge => RamAir ? _challenge : ChallengeOff;

        internal bool DropZone => Challenge == ChallengeDropZone;

        internal bool Swoop => Challenge == ChallengeSwoop || Challenge == ChallengeSwoopStraight;

        internal bool SwoopStraight => Challenge == ChallengeSwoopStraight;

        internal string DropTarget { get => _dropTarget; set => _dropTarget = value; }

        internal bool DropStarted { get => _dropStarted; set => _dropStarted = value; }

        internal string SwoopHeading { get => _swoopHeading; set => _swoopHeading = value; }

        /// <summary>
        /// In the designer: the activation groups named (and AutoCam on) as soon as the challenge
        /// is chosen, so the craft is saved with them (see <see cref="Drood2ActivationGroups"/>).
        /// </summary>
        protected override void OnDesignerInitialization(ModApi.Design.PartProperties.IDesignerPartPropertiesModifierInterface d)
        {
            base.OnDesignerInitialization(d);
            ShowPresetLabels();
            if (!_customTaken)
            {
                _customTaken = true;
                _swoopStartHeight = LastCustom(nameof(_swoopStartHeight), _swoopStartHeight);
                _swoopStartDistance = LastCustom(nameof(_swoopStartDistance), _swoopStartDistance);
                _straightStartHeight = LastCustom(nameof(_straightStartHeight), _straightStartHeight);
                _straightStartDistance = LastCustom(nameof(_straightStartDistance), _straightStartDistance);
            }

            d.OnPropertyChanged(() => _challenge, (newValue, oldValue) =>
            {
                Drood2ActivationGroups.Ensure(Part?.PartScript);
                ShowPresetLabels();
                d.Manager.RefreshUI();
            });
            d.OnPropertyChanged(() => _canopy, (newValue, oldValue) =>
            {
                Drood2ActivationGroups.Ensure(Part?.PartScript);
                ShowPresetLabels();
                d.Manager.RefreshUI();
            });
            d.OnVisibilityRequested(() => _challenge, visible => RamAir);
            d.OnVisibilityRequested(() => _swoopDifficulty, visible => Swoop);
            d.OnVisibilityRequested(() => _swoopStartHeight, visible => Challenge == ChallengeSwoop && _swoopDifficulty == DifficultyCustom);
            d.OnVisibilityRequested(() => _swoopStartDistance, visible => Challenge == ChallengeSwoop && _swoopDifficulty == DifficultyCustom);
            d.OnVisibilityRequested(() => _presetHeightLabel, visible => Swoop && _swoopDifficulty != DifficultyCustom);
            d.OnVisibilityRequested(() => _presetDistanceLabel, visible => Swoop && _swoopDifficulty != DifficultyCustom);
            d.OnLabelActivated(() => _presetHeightLabel, label =>
            {
                _heightLabel = label;
                ShowPresetLabels();
            });
            d.OnLabelActivated(() => _presetDistanceLabel, label =>
            {
                _distanceLabel = label;
                ShowPresetLabels();
            });
            d.OnPropertyChanged(() => _swoopDifficulty, (newValue, oldValue) =>
            {
                ShowPresetLabels();
                d.Manager.RefreshUI();
            });

            d.OnPropertyChanged(() => _swoopStartHeight, (newValue, oldValue) =>
                _swoopStartHeight = RememberCustom(nameof(_swoopStartHeight), FormatMetres(newValue, MinSwoopStartHeight, MaxSwoopStartHeight, DefaultSwoopStartHeight, d)));
            d.OnPropertyChanged(() => _swoopStartDistance, (newValue, oldValue) =>
                _swoopStartDistance = RememberCustom(nameof(_swoopStartDistance), FormatMetres(newValue, 0f, MaxSwoopStartDistance, DefaultSwoopStartDistance, d)));
            d.OnVisibilityRequested(() => _straightStartHeight, visible => SwoopStraight && _swoopDifficulty == DifficultyCustom);
            d.OnVisibilityRequested(() => _straightStartDistance, visible => SwoopStraight && _swoopDifficulty == DifficultyCustom);
            d.OnPropertyChanged(() => _straightStartHeight, (newValue, oldValue) =>
                _straightStartHeight = RememberCustom(nameof(_straightStartHeight), FormatMetres(newValue, MinSwoopStartHeight, MaxSwoopStartHeight, DefaultStraightStartHeight, d)));
            d.OnPropertyChanged(() => _straightStartDistance, (newValue, oldValue) =>
                _straightStartDistance = RememberCustom(nameof(_straightStartDistance), FormatMetres(newValue, 0f, MaxSwoopStartDistance, DefaultStraightStartDistance, d)));
        }

        [System.NonSerialized]
        private ModApi.Design.PartProperties.ILabelProperty _heightLabel;

        [System.NonSerialized]
        private ModApi.Design.PartProperties.ILabelProperty _distanceLabel;

        /// <summary>The set start's height and distance on the greyed lines (Practice, Expert), for the variant chosen.</summary>
        private void ShowPresetLabels()
        {
            var straight = SwoopStraight;
            var name = straight ? "Straight" : "Swoop";
            var height = (straight ? StraightStartHeight : SwoopStartHeight).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            var distance = (straight ? StraightStartDistance : SwoopStartDistance).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            // A label shows its field's text.
            _presetHeightLabel = $"<color={PresetColour}>{name} start height (m): {height}</color>";
            _presetDistanceLabel = $"<color={PresetColour}>{name} start distance (m): {distance}</color>";
            if (_heightLabel != null)
            {
                _heightLabel.LabelValue = _presetHeightLabel;
            }

            if (_distanceLabel != null)
            {
                _distanceLabel.LabelValue = _presetDistanceLabel;
            }
        }

        /// <summary>The last Custom value typed for <paramref name="field"/>, or <paramref name="fallback"/>.</summary>
        private static string LastCustom(string field, string fallback)
        {
            return PlayerPrefs.GetString(CustomKey + field, fallback);
        }

        /// <summary>Keeps a typed Custom value for the next Drood 2.0 placed; returns it.</summary>
        private static string RememberCustom(string field, string value)
        {
            PlayerPrefs.SetString(CustomKey + field, value);
            PlayerPrefs.Save();
            return value;
        }

        /// <summary>Typed metres, whole and within <paramref name="min"/>..<paramref name="max"/> (anything else the default).</summary>
        private static float ParseMetres(string text, float min, float max, float fallback)
        {
            return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && !float.IsNaN(value)
                ? Mathf.Clamp(Mathf.Round(value), min, max)
                : fallback;
        }

        private static string FormatMetres(string text, float min, float max, float fallback, ModApi.Design.PartProperties.IDesignerPartPropertiesModifierInterface d)
        {
            var formatted = ParseMetres(text, min, max, fallback).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            if (formatted != text)
            {
                d.Manager.RefreshUI();
            }

            return formatted;
        }

        // The difficulty's earlier name for Expert.
        private const string DifficultyExpertEarlier = "Advanced";

        /// <summary>
        /// A craft saved with the earlier Drop Zone switch on loads with the Drop Zone chosen; one
        /// saved with "Advanced" loads with Expert.
        /// </summary>
        public override void OnPartLoaded()
        {
            base.OnPartLoaded();
            if (_swoopDifficulty == DifficultyExpertEarlier)
            {
                _swoopDifficulty = DifficultyExpert;
            }

            if (_dropZone)
            {
                if (_challenge == ChallengeOff)
                {
                    _challenge = ChallengeDropZone;
                }

                _dropZone = false;
            }
        }

        /// <summary>
        /// The parachute's group (AG1) and, with the Drop Zone chosen, the drop zone's (AG2
        /// "Restart Challenge", <see cref="DroodDropZone"/>) and AutoCam (AG3, <see cref="DroodAutoCam"/>),
        /// with the Swoop chosen its AG2 ("Restart Challenge", <see cref="DroodSwoopCourse"/>), so
        /// the flight activation-group panel lists them for a drood on EVA (the panel collects
        /// these from every part of the craft). Nothing while the drood sits in a crew
        /// compartment: then it's part of the other craft, whose groups aren't the drood's.
        /// </summary>
        public override IEnumerable<int> GetAssociatedActivationGroups()
        {
            if (Assets.Scripts.Game.InFlightScene)
            {
                var eva = Part?.PartScript?.GetModifier<EvaScript>();
                var script = Part?.PartScript?.GetModifier<Drood2Script>();
                if (eva == null || !eva.EvaActive || script == null || !script.HasParachute)
                {
                    return System.Array.Empty<int>();
                }
            }

            return DropZone ? ParachuteAndDropZoneGroups : Swoop ? ParachuteAndSwoopGroups : ParachuteGroup;
        }
    }
}
