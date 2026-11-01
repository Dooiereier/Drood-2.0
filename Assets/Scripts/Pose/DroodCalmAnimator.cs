using Assets.Scripts.Animation;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// While hanging, in the free-fall arch, anchored to what it is attached to (Attached
    /// Parts, <see cref="DroodLimbFollower.Anchored"/>) or just out of a seat
    /// (<see cref="Drood2Script.SettlingFromSeat"/>), feeds the drood's animator "standing
    /// still on the ground" so it plays the calm idle instead of the falling animation, and
    /// doesn't walk its legs from the glide/sink speed, turning, or the movement keys.
    ///
    /// EvaScript.UpdateAnimationController writes these values every frame from EvaScript's
    /// game-loop Update. <see cref="Step"/> runs from Drood 2.0's game-loop Update, after
    /// EvaScript's, and overwrites them before the animator evaluates. The setters are public;
    /// only EvaScript's reference to the controller is private (<see cref="DroodReflection"/>).
    /// </summary>
    public class DroodCalmAnimator : MonoBehaviour
    {
        private EvaScript _eva;
        private DroodParachute _parachute;
        private DroodFreeFall _freeFall;
        private DroodLimbFollower _limbs;
        private Drood2Script _drood;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _parachute = GetComponent<DroodParachute>();
        }

        /// <summary>Hanging under the canopy, in the free-fall arch, or anchored to what it is attached to.</summary>
        internal bool Calm
        {
            get
            {
                _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
                _limbs = _limbs != null ? _limbs : GetComponent<DroodLimbFollower>();
                _drood = _drood != null ? _drood : GetComponent<Drood2Script>();
                return (_parachute != null && _parachute.IsHanging) || (_freeFall != null && _freeFall.Weight > 0f)
                    || (_limbs != null && _limbs.Anchored) || (_drood != null && _drood.SettlingFromSeat);
            }
        }

        internal void Step()
        {
            if (!Calm)
            {
                return;
            }

            var controller = DroodReflection.CrewAnimController(_eva);
            if (controller == null)
            {
                return;
            }

            if (controller.InAir)
            {
                controller.InAir = false;
            }

            if (controller.Speed != 0f)
            {
                controller.Speed = 0f;
            }

            if (controller.ForwardSpeed != 0f)
            {
                controller.ForwardSpeed = 0f;
            }

            if (controller.SideSpeed != 0f)
            {
                controller.SideSpeed = 0f;
            }

            if (controller.VerticalSpeed != 0f)
            {
                controller.VerticalSpeed = 0f;
            }

            // Turning (and movement keys) while hanging would make the animator step in place, as
            // if walking: the turn-in-place and locomotion blends read these inputs.
            if (controller.TurnInput != 0f)
            {
                controller.TurnInput = 0f;
            }

            if (controller.ForwardInput != 0f)
            {
                controller.ForwardInput = 0f;
            }

            if (controller.SideInput != 0f)
            {
                controller.SideInput = 0f;
            }
        }
    }
}
