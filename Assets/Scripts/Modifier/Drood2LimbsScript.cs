using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using ModApi.GameLoop;
using ModApi.GameLoop.Interfaces;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Behaviour half of Drood 2.0's limbs modifier: the limbs read its data
    /// (<see cref="Drood2LimbsData"/>); here, the body not drawn while invisible
    /// (<see cref="Renderer.forceRenderingOff"/> on the game's own list of the character's renderers;
    /// no material changes, and a carried part's renderers aren't in that list). Set late each frame,
    /// after a seat exit's own hiding (this modifier comes after <see cref="Drood2Script"/>). The
    /// animator always animates meanwhile: a culled one stops moving the bones of a drood that isn't
    /// drawn, and its carried parts with them.
    /// </summary>
    public class Drood2LimbsScript : PartModifierScript<Drood2LimbsData>, IFlightLateUpdate, IFlightLateUpdatePaused, IDesignerLateUpdate
    {
        private bool _hidden;
        private Animator _animator;
        private AnimatorCullingMode _gameCulling;

        void IFlightLateUpdate.FlightLateUpdate(in FlightFrameData frame) => Apply();

        void IFlightLateUpdatePaused.FlightLateUpdatePaused(in FlightFrameData frame) => Apply();

        void IDesignerLateUpdate.DesignerLateUpdate(in DesignerFrameData frame) => Apply();

        private void Apply()
        {
            var invisible = Data.Invisible;
            if (!invisible && !_hidden)
            {
                return;
            }

            var eva = GetComponent<EvaScript>();
            var renderers = DroodReflection.EvaRenderers(eva);
            if (renderers == null)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = invisible;
                }
            }

            _hidden = invisible;
            _animator = _animator != null ? _animator : GetComponentInChildren<Animator>(true);
            if (_animator == null)
            {
                return;
            }

            if (invisible && _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                _gameCulling = _animator.cullingMode;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            else if (!invisible)
            {
                _animator.cullingMode = _gameCulling;
            }
        }
    }
}
