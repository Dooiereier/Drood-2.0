using System.Linq;
using ModApi.Craft;
using ModApi.Craft.Parts;
using ModApi.GameLoop;
using ModApi.GameLoop.Interfaces;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Behaviour half of the Drood 2.0 part modifier. Juno creates it with the part (designer and
    /// flight) and moves it with the drood when it leaves a craft.
    ///
    /// In flight it creates Drood 2.0's behaviours (<see cref="Drood2FlightBehaviours"/>) and runs
    /// them from Juno's game loop, which follows the game's pause and time warp. Craft events
    /// come through the modifier's own hooks.
    ///
    /// Releases (letting go of a craft, leaving a seat) are in Drood2Script.Release.cs.
    /// </summary>
    public partial class Drood2Script : PartModifierScript<Drood2Data>, IFlightUpdate, IFlightUpdatePaused, IFlightFixedUpdate, IFlightLateUpdate, IFlightLateUpdatePaused, IDesignerLateUpdate
    {
        // Moves larger than this are logged (teleports).
        private const float LogMoveDistance = 10f;

        // Only a move this large (m) is synced (a teleport or a recentring): the interpolated
        // Transform otherwise lags the physics pose by up to a step, and copying it back would
        // pull a moving drood back by that much (~1.6 m at 80 m/s and 50 physics steps a second).
        private const float SyncMoveDistance = 5f;

        private Drood2FlightBehaviours _flight;
        private DroodDesignerHeldParts _designerHeldParts;

        public override void OnModifiersCreated()
        {
            base.OnModifiersCreated();
            Drood2ActivationGroups.Ensure(PartScript);

            // The behaviours don't need Harmony (only the patched features do).
            if (Assets.Scripts.Game.InFlightScene)
            {
                CreateFlightBehaviours();
            }
            else if (Assets.Scripts.Game.InDesignerScene)
            {
                _designerHeldParts = new DroodDesignerHeldParts(PartScript, new DroodHeldGrips(Data));
            }
        }

        public override void OnCraftLoaded(ICraftScript craftScript, bool movedToNewCraft)
        {
            base.OnCraftLoaded(craftScript, movedToNewCraft);
            ExcludeHeldPartsFromMeshCombine();
        }

        public override void OnCraftStructureChanged(ICraftScript craftScript)
        {
            base.OnCraftStructureChanged(craftScript);
            ExcludeHeldPartsFromMeshCombine();
            _flight?.OnCraftStructureChanged();
            _designerHeldParts?.MarkDirty();
        }

        /// <summary>
        /// The game moved this craft: world recentring, or a teleport
        /// (CraftNode.RecalculateFrameState sets the craft's Transform, then calls this). The
        /// drood's rigidbody is interpolated (EvaScript sets that every physics tick), and Unity
        /// rewrites an interpolated body's Transform from its physics pose every rendered frame, so
        /// the move would be undone before physics sees it (teleporting a drood would only work
        /// while paused). Copy the moved Transform into the rigidbody right away (only for a move of
        /// at least SyncMoveDistance). The stock drood
        /// has the same bug; only Drood 2.0 gets this fix.
        /// </summary>
        public override void RecalculateFrameState(Vector3 positionDelta, Vector3 velocityDelta)
        {
            base.RecalculateFrameState(positionDelta, velocityDelta);

            var body = PartScript?.BodyScript?.RigidBody;
            if (body != null && !body.isKinematic && body.interpolation != RigidbodyInterpolation.None)
            {
                var moved = Vector3.Distance(body.position, body.transform.position);
                if (moved >= SyncMoveDistance)
                {
                    body.position = body.transform.position;
                    body.rotation = body.transform.rotation;
                }

                if (moved > LogMoveDistance)
                {
                    DroodLog.Debug($"Synced the interpolated body after a {moved:0} m move (teleport).");
                }
            }

            _flight?.OnFrameShift(positionDelta);
        }

        void IFlightFixedUpdate.FlightFixedUpdate(in FlightFrameData frame)
        {
            _flight?.FixedUpdate();
        }

        void IFlightUpdate.FlightUpdate(in FlightFrameData frame)
        {
            _flightRunning = true;
            _flight?.Update();
        }

        // A Drood 2.0 that is its craft's primary command part with other parts fixed to it: in the
        // first moments of the flight the game rebuilds the craft around it (named after its crew
        // member) and puts the drood at the craft's origin, but leaves the other parts' transforms
        // where they were, so the drood ends up inside them (with a command chip as primary the
        // rebuild is around the chip and nothing moves). So at the
        // flight's first frame the drood's place on a part it is anchored to is recorded, and for
        // StructureWaitSeconds the drood is put back there whenever it is off it. The parts' saved
        // places follow on their own: the game takes them from the transforms when it saves.
        private const float StructureWaitSeconds = 3f;
        private const float StructureFrom = 0.05f;
        private float _structureUntil = float.NegativeInfinity;
        private Transform _structure;
        private Vector3 _onStructurePosition;
        private Quaternion _onStructureRotation;

        private void RecordStructure()
        {
            var data = PartScript?.Data;
            var craft = PartScript?.CraftScript;
            var eva = GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            if (data == null || craft == null || data.PartConnections.Count == 0 || eva == null || eva.CrewCompartment != null
                || craft.ActiveCommandPod == null || craft.ActiveCommandPod != PartScript.CommandPod)
            {
                return;
            }

            // A part it is anchored to, not one it carries (those follow its bones).
            var held = DroodHeldParts.Find(data, out var anchored);
            if (!anchored)
            {
                return;
            }

            foreach (var connection in data.PartConnections)
            {
                var other = connection.GetOtherPart(data);
                if (other?.PartScript?.Transform != null && !held.Any(group => group.Parts.Contains(other)))
                {
                    _structure = other.PartScript.Transform;
                    break;
                }
            }

            if (_structure == null)
            {
                return;
            }

            var inverse = Quaternion.Inverse(_structure.rotation);
            _onStructurePosition = inverse * (transform.position - _structure.position);
            _onStructureRotation = inverse * transform.rotation;
            _structureUntil = Time.time + StructureWaitSeconds;
        }

        private void KeepOnStructure()
        {
            if (Time.time > _structureUntil || _structure == null)
            {
                return;
            }

            var position = _structure.position + _structure.rotation * _onStructurePosition;
            var offset = Vector3.Distance(transform.position, position);
            if (offset < StructureFrom)
            {
                return;
            }

            transform.SetPositionAndRotation(position, _structure.rotation * _onStructureRotation);
            PartScript.BodyScript?.OnPartMassChanged();
            DroodLog.Debug($"Craft rebuilt around the drood: put back on '{_structure.name}' ({offset:0.00} m away).");
        }

        void IFlightUpdatePaused.FlightUpdatePaused(in FlightFrameData frame)
        {
            _flight?.UpdatePaused();
        }

        void IFlightLateUpdate.FlightLateUpdate(in FlightFrameData frame)
        {
            _flight?.ReleaseLate();
            _flight?.LateUpdate();
        }

        // While paused the Animator still rewrites the bones every frame, so the late chain
        // (IK pose, held parts, canopy) runs here too, with time standing still.
        void IFlightLateUpdatePaused.FlightLateUpdatePaused(in FlightFrameData frame)
        {
            _flight?.LateUpdate();
        }

        void IDesignerLateUpdate.DesignerLateUpdate(in DesignerFrameData frame)
        {
            _designerHeldParts?.LateUpdate();
            if (Data.DropZone)
            {
                DroodPlaneScenery.Want();
            }
        }

        private void ExcludeHeldPartsFromMeshCombine()
        {
            var drood = PartScript?.Data;
            if (drood != null && Assets.Scripts.Game.InFlightScene)
            {
                DroodHeldParts.ExcludeFromMeshCombine(drood);
            }
        }

        /// <summary>The flight behaviours exist with their parachute (AG1 does something).</summary>
        internal bool HasParachute => _flight != null && _flight.HasParachute;

        private void CreateFlightBehaviours()
        {
            try
            {
                _flight = new Drood2FlightBehaviours(gameObject, Data);
                DroodLog.Debug("Drood 2.0 parachute ready (AG1).");
            }
            catch (System.Exception e)
            {
                _flight = null;
                Debug.LogError($"[Drood 2.0] Couldn't create the flight behaviours, so this drood has no parachute: {e}");
            }
        }
    }
}
