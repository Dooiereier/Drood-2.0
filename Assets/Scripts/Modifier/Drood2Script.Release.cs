using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Releases: the drood lets go of what it is connected to, or leaves its seat, when its
    /// Attached Parts "Release on" group is switched on by the craft it is on, or by Vizzy's
    /// "activate part". The player keeps control of the craft they fly. The game's own
    /// activation at launch comes before the first flight update and is ignored.
    /// </summary>
    public partial class Drood2Script
    {
        private bool _structureRecorded;

        /// <summary>Guarded step, first in the frame's update: the structure kept, releases by group.</summary>
        internal void ReleaseBeforeUpdate()
        {
            if (!_structureRecorded)
            {
                _structureRecorded = true;
                RecordStructure();
                if (PartScript?.Data != null && PartScript.Data.Activated)
                {
                    _deactivateAt = Time.time;
                }
            }

            DeactivateAfterActivation();
            EndKeepingCarried();
            ReleaseOnGroup();
            KeepOnStructure();
        }

        /// <summary>Guarded step, last in the frame's update: the upkeep of a seat exit and a release.</summary>
        internal void ReleaseAfterUpdate()
        {
            if (Time.time <= _settleUntil)
            {
                StandOffSeat(GetComponent<EvaScript>());
            }

            RestoreCulling();
            ReRootAfterRelease();
        }

        /// <summary>Guarded step, before the pose chain, not while paused: the pose at a seat exit, and back on the body before the frame is drawn.</summary>
        internal void ReleaseBeforePoses()
        {
            if (_hidden != null)
            {
                UnhideWhenPosed(GetComponent<EvaScript>());
            }

            if (_fallingFrames > 0)
            {
                _fallingFrames--;
                FallingAtOnce(GetComponent<EvaScript>());
            }

            ReRootAfterRelease();
        }

        /// <summary>Guarded step, before each physics step: back on the body.</summary>
        internal void ReleaseBeforePhysics()
        {
            EndKeepingCarried();
            ReRootAfterRelease();
        }

        // Released, the drood becomes its own craft over a few frames (a craft of its own, then one
        // named after its crew member). In that step the game puts its part back at its offset in
        // the craft it left: the drood moves by the way from it to that craft's centre, into it,
        // and at flight speed the next physics step destroys it. So for ReRootWaitSeconds after a
        // release, whenever the drood is alone in its craft and off its body, it is put back on it
        // (ReRootAfterRelease): in the frame's update and late update and before each physics step.
        private const float ReRootWaitSeconds = 2f;
        private const float ReRootFrom = 0.05f;
        private float _reRootUntil = float.NegativeInfinity;
        private Quaternion _releaseRotation;

        /// <summary>
        /// Within ReRootWaitSeconds of a release, while the game splits the drood off: free fall
        /// waits meanwhile, or it would switch on and off with the steps of the split.
        /// </summary>
        internal bool Releasing => Time.time <= _reRootUntil;

        /// <summary>
        /// After a release, once the drood is the only part of its craft: its part at the origin of
        /// its body (the transforms between them, and its saved place in the craft), the body turned
        /// as the drood was, so the drood stays where and how it was.
        /// </summary>
        private void ReRootAfterRelease()
        {
            if (Time.time > _reRootUntil)
            {
                return;
            }

            var craft = PartScript?.CraftScript;
            var body = PartScript?.BodyScript?.RigidBody;
            if (craft?.Data?.Assembly?.Parts == null || craft.Data.Assembly.Parts.Count != 1 + _keptParts || body == null)
            {
                return;
            }

            var offset = Vector3.Distance(transform.position, body.transform.position);
            if (offset < ReRootFrom)
            {
                return;
            }

            var place = body.position;
            body.rotation = _releaseRotation;
            body.transform.SetPositionAndRotation(place, _releaseRotation);
            for (var level = transform; level != null && level != body.transform; level = level.parent)
            {
                level.localPosition = Vector3.zero;
                level.localRotation = Quaternion.identity;
            }

            DroodReflection.ZeroPartPlace(PartScript.Data);
            PartScript.BodyScript.OnPartMassChanged();
            DroodLog.Debug($"Release: the drood put back on its body ({offset:0.00} m away), at {transform.position}.");
        }

        // The flight's game loop has run (activations before it are the game's own, at launch).
        private bool _flightRunning;

        // An activated part ignores further activations (Vizzy's "activate part" among them), and
        // the state is saved: so the drood is switched back off DeactivateSeconds later, ready for
        // the next one. A saved activated drood goes back at the flight's start.
        private const float DeactivateSeconds = 2f;
        private float _deactivateAt = float.PositiveInfinity;

        private void DeactivateAfterActivation()
        {
            if (Time.time >= _deactivateAt)
            {
                _deactivateAt = float.PositiveInfinity;
                PartScript?.Deactivate();
            }
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (!Assets.Scripts.Game.InFlightScene)
            {
                return;
            }

            _deactivateAt = Time.time + DeactivateSeconds;

            if (!_flightRunning)
            {
                DroodLog.Debug("Activated at flight start (the game's own activation): nothing released.");
                return;
            }

            ReleaseConnections();
        }

        private void LeaveSeat(EvaScript eva)
        {
            if (!DroodReflection.LeaveSeat(eva))
            {
                DroodLog.Debug("Release from the seat unavailable (game members missing).");
                return;
            }

            // The exit moves the body's Transform only; its physics pose onto the exit too, or the
            // drood falls from its seat through the cabin.
            var body = PartScript?.BodyScript?.RigidBody;
            if (body != null)
            {
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }

            // The pose at the exit: the seat's animation and hand IK let go until the game has the
            // drood on EVA (StandOffSeat); on the ground then calm for a moment (SettlingFromSeat),
            // in the air the falling pose at once (FallingAtOnce); always animated meanwhile
            // (AnimateAlways), and not drawn until off the seated state (Hide).
            var inAir = (PartScript?.CraftScript?.FlightData?.AltitudeAboveGroundLevel ?? 0.0) > AirborneExitHeight;
            AnimateAlways(eva);
            var seatAnimator = DroodReflection.CrewAnimController(eva)?.Animator;
            _seatedState = seatAnimator != null ? seatAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;
            _settleUntil = Time.time + SeatSettleSeconds;
            _settleCalm = !inAir;
            StandOffSeat(eva);
            if (inAir)
            {
                _fallingFrames = FallingFrames;
                FallingAtOnce(eva);
            }

            Hide(true);
            _releaseRotation = transform.rotation;
            _reRootUntil = Time.time + ReRootWaitSeconds;
            DroodLog.Debug("Released from its seat.");
        }

        // After a seat exit on the ground the drood is held calm this long (DroodCalmAnimator):
        // standing, not the game's fall and landing crouch from the exit point a little above
        // the ground.
        private const float SeatSettleSeconds = 0.6f;
        private float _settleUntil = float.NegativeInfinity;

        // Above this height (m, the craft's above the ground) a seat exit is in the air.
        private const float AirborneExitHeight = 3f;

        // The animator run ahead this far (s) for a seat exit in the air: past the blend into the
        // falling animation, so the first frame shows its steady pose.
        private const float FallingRunAhead = 1f;

        private bool _settleCalm;

        // The game restarts the blend into falling after the exit: the falling pose is set again
        // this many frames, late, after the game's update and before the pose is solved.
        private const int FallingFrames = 4;
        private int _fallingFrames;

        // Not drawn until the animator has left the seated state it had at the exit and
        // HiddenAfterSeat more frames have passed, at most HiddenMaxFrames frames. The animator
        // always animates meanwhile (AnimateAlways): hidden, it would otherwise stop moving the
        // bones.
        private const int HiddenAfterSeat = 2;
        private const int HiddenMaxFrames = 10;
        private int _seatedState;
        private int _hiddenFrames;
        private int _hiddenAfter;
        private Renderer[] _hidden;

        private void Hide(bool hidden)
        {
            if (hidden)
            {
                _hidden = GetComponentsInChildren<Renderer>(true);
                _hiddenFrames = HiddenMaxFrames;
                _hiddenAfter = HiddenAfterSeat;
            }

            if (_hidden == null)
            {
                return;
            }

            foreach (var renderer in _hidden)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = hidden;
                }
            }

            if (!hidden)
            {
                _hidden = null;
            }
        }

        // Late each frame while hidden: shown once off the seated state for long enough.
        private void UnhideWhenPosed(EvaScript eva)
        {
            if (_hidden == null)
            {
                return;
            }

            var animator = DroodReflection.CrewAnimController(eva)?.Animator;
            var seated = animator != null && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _seatedState;
            if (!seated)
            {
                _hiddenAfter--;
            }

            if (--_hiddenFrames <= 0 || _hiddenAfter < 0)
            {
                Hide(false);
            }
        }

        /// <summary>Just out of a seat on the ground (see LeaveSeat): calm.</summary>
        internal bool SettlingFromSeat => _settleCalm && Time.time <= _settleUntil;

        // A drood the player doesn't control has its animator culled (CullUpdateTransforms): its
        // bones move only while Unity counts it visible, and just out of the seat its bounds are
        // still in the cabin. Always animated for ReRootWaitSeconds, then the game's setting back.
        private Animator _alwaysAnimated;
        private AnimatorCullingMode _gameCulling;

        private void AnimateAlways(EvaScript eva)
        {
            var animator = DroodReflection.CrewAnimController(eva)?.Animator;
            if (animator == null)
            {
                return;
            }

            if (_alwaysAnimated == null)
            {
                _gameCulling = animator.cullingMode;
            }

            _alwaysAnimated = animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void RestoreCulling()
        {
            if (_alwaysAnimated == null || Time.time <= _reRootUntil)
            {
                return;
            }

            // Only ours undone: a drood the player has taken control of meanwhile keeps the game's.
            if (_alwaysAnimated.cullingMode == AnimatorCullingMode.AlwaysAnimate && !(GetComponent<EvaScript>()?.IsPlayerCraft ?? false))
            {
                _alwaysAnimated.cullingMode = _gameCulling;
            }

            _alwaysAnimated = null;
        }

        private void FallingAtOnce(EvaScript eva)
        {
            var controller = DroodReflection.CrewAnimController(eva);
            if (controller?.Animator == null)
            {
                return;
            }

            var ik = GetComponentInChildren<RootMotion.FinalIK.FullBodyBipedIK>();
            if (ik != null)
            {
                foreach (var effector in ik.solver.effectors)
                {
                    effector.positionWeight = 0f;
                    effector.rotationWeight = 0f;
                }
            }

            AnimateAlways(eva);
            controller.CrewCompartment = null;
            controller.SelectAnimationController();
            controller.InAir = true;
            controller.Speed = 0f;
            controller.ForwardSpeed = 0f;
            controller.SideSpeed = 0f;
            controller.Update();
            controller.Animator.Update(FallingRunAhead);
        }

        // Until the game has the drood on EVA (a few frames): its animation off the seat and the
        // seat's IK let go, each frame (the game may set them again meanwhile).
        private void StandOffSeat(EvaScript eva)
        {
            if (Time.time > _settleUntil || eva == null || eva.EvaActive)
            {
                return;
            }

            var controller = DroodReflection.CrewAnimController(eva);
            if (controller != null && controller.CrewCompartment != null)
            {
                controller.CrewCompartment = null;
                controller.SelectAnimationController();
            }

            var ik = GetComponentInChildren<RootMotion.FinalIK.FullBodyBipedIK>();
            if (ik != null)
            {
                foreach (var effector in ik.solver.effectors)
                {
                    effector.positionWeight = 0f;
                    effector.rotationWeight = 0f;
                }
            }
        }

        // The Attached Parts "Release on" group: when the craft the drood is on (its controlling
        // command pod) switches it on, the drood lets go, or leaves its seat. Only a switch from
        // off to on counts, not a group already on at the flight's start.
        private bool _releaseGroupSeen;
        private bool _releaseGroupWasOn;

        private void ReleaseOnGroup()
        {
            var group = PartScript?.Data?.GetModifier<Drood2LimbsData>()?.ReleaseGroup ?? 0;
            var pod = PartScript?.CraftScript?.ActiveCommandPod;
            var seated = GetComponent<EvaScript>()?.CrewCompartment != null;
            if (group == 0 || pod == null || (PartScript.Data.PartConnections.Count == 0 && !seated))
            {
                _releaseGroupSeen = false;
                return;
            }

            var on = pod.GetActivationGroupState(group);
            var switchedOn = _releaseGroupSeen && on && !_releaseGroupWasOn;
            _releaseGroupSeen = true;
            _releaseGroupWasOn = on;
            if (switchedOn)
            {
                DroodLog.Debug($"AG{group} of the craft switched on: releasing the drood.");
                ReleaseConnections();
            }
        }

        /// <summary>
        /// The drood broken off whatever it is connected to the way the game breaks off a part
        /// (a wing over its break force, a crash): its body's collision handler disconnects the
        /// part, the game splits it off into its own body and craft (a drood broken off becomes
        /// an active drood again). Parts it carries stay with it (KeepCarried).
        /// </summary>
        private void ReleaseConnections()
        {
            // Seated (a seat or crew compartment): out at the seat's exit, the player keeping control
            // of the craft they fly (LeaveSeat).
            var eva = GetComponent<EvaScript>();
            if (eva?.CrewCompartment != null)
            {
                LeaveSeat(eva);
                return;
            }

            var data = PartScript?.Data;
            var handler = PartScript?.BodyScript?.BodyCollisionHandler;
            if (data == null || data.PartConnections.Count == 0)
            {
                DroodLog.Debug("Activated: no connections to release.");
                return;
            }

            if (handler == null)
            {
                DroodLog.Debug("Activated: the drood's body has no collision handler; nothing released.");
                return;
            }

            var held = DroodHeldParts.Find(data, out var anchored);
            if (!anchored)
            {
                DroodLog.Debug("Activated: only carried parts on the drood; nothing to let go of.");
                return;
            }

            var count = data.PartConnections.Count;
            _releaseRotation = transform.rotation;
            _reRootUntil = Time.time + ReRootWaitSeconds;
            KeepCarried(held);
            handler.DisconnectPart(PartScript);
            DroodLog.Debug($"Activated: the drood broken off ({count} connection(s), {_keptParts} carried part(s) kept).");
        }

        // A release keeps the parts the drood carries. The game splits a disconnected part off with
        // its part group (BodyCollisionHandler.DisconnectParts, in the handler's next Update): every
        // connection out of the group is destroyed unless the other part prevents it, and the
        // group's parts form the new body, which takes all those parts' groups along. A drood is
        // alone in its group, so until that split: the carried parts are listed in its group and
        // prevent disconnections. Only when they fill their own groups (else those groups' other
        // parts would be pulled along).
        private readonly System.Collections.Generic.List<(PartData Part, bool Prevented)> _carriedInGroup = new System.Collections.Generic.List<(PartData, bool)>();
        private System.Collections.Generic.List<PartData> _groupParts;
        private ModApi.Craft.IBodyScript _bodyBeforeSplit;
        private float _keepUntil;
        private int _keptParts;

        private void KeepCarried(System.Collections.Generic.List<DroodHeldParts.Held> held)
        {
            _keptParts = 0;
            var parts = new System.Collections.Generic.HashSet<PartData>();
            foreach (var limb in held)
            {
                parts.UnionWith(limb.Parts);
            }

            var groupParts = PartScript.PartGroup?.Data?.Parts;
            if (parts.Count == 0 || groupParts == null || !DroodReflection.CanSetPreventDisconnect)
            {
                return;
            }

            foreach (var part in parts)
            {
                var own = part.PartScript?.PartGroup?.Data?.Parts;
                if (own == null || own.Exists(other => !parts.Contains(other)))
                {
                    DroodLog.Debug($"Release: '{part.Name}' shares its part group with parts the drood doesn't carry; the carried parts drop off.");
                    return;
                }
            }

            foreach (var part in parts)
            {
                _carriedInGroup.Add((part, DroodReflection.SetPreventDisconnect(part, true)));
                groupParts.Add(part);
            }

            _groupParts = groupParts;
            _bodyBeforeSplit = PartScript.BodyScript;
            _keepUntil = Time.time + ReRootWaitSeconds;
            _keptParts = parts.Count;
        }

        // Once the split is done (the drood on a new body), or it didn't come: back to the game's.
        private void EndKeepingCarried()
        {
            if (_groupParts == null || (PartScript.BodyScript == _bodyBeforeSplit && Time.time <= _keepUntil))
            {
                return;
            }

            foreach (var (part, prevented) in _carriedInGroup)
            {
                _groupParts.Remove(part);
                DroodReflection.SetPreventDisconnect(part, prevented);
            }

            _carriedInGroup.Clear();
            _groupParts = null;
            _bodyBeforeSplit = null;
        }
    }
}
