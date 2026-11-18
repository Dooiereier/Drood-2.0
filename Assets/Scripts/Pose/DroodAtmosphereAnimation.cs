using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Whether Drood 2.0 is in air thick enough for skydiving (MinAirDensity): there it keeps the
    /// normal animations though the game would pick the space ones (<see cref="AtmosphereAnimationPatch"/>).
    /// The game picks only when its own switches change, so when this changes the pick is made
    /// again (<see cref="Step"/>). Without Juno Harmony nothing changes.
    /// </summary>
    public class DroodAtmosphereAnimation : MonoBehaviour
    {
        // Air at least this dense (kg/m³; about 17 km up on Earth): thinner air, up to the
        // atmosphere's edge, keeps the game's zero-g look, as for the stock drood.
        private const float MinAirDensity = 0.1f;

        /// <summary>Set at mod init once the patch is applied.</summary>
        internal static bool PatchApplied;

        private EvaScript _eva;
        private bool _picked;

        /// <summary>The normal animations, not the space ones.</summary>
        internal bool Normal { get; private set; }

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
        }

        /// <summary>Game-loop Update: the animation set picked again when Normal changes.</summary>
        internal void Step()
        {
            var craft = _eva != null ? _eva.PartScript?.CraftScript : null;
            var normal = craft != null && craft.AtmosphereSample.AirDensity >= MinAirDensity;
            if (normal == Normal && _picked)
            {
                return;
            }

            Normal = normal;
            _picked = true;
            if (PatchApplied)
            {
                DroodReflection.SelectAnimationController(DroodReflection.CrewAnimController(_eva));
            }
        }
    }
}
