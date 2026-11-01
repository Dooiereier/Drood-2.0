using UnityEngine;

namespace Drood2
{
    // Landed canopy: where it lies, moving it with the world, and ground raycasts.
    public partial class DroodParachute
    {
        /// <summary>
        /// The drood was moved <paramref name="distance"/> (m) to where it lies after a collapse
        /// (DroodLandingFall): the landed canopy is dragged only that much later, so it stays put.
        /// </summary>
        internal void DroodMoved(float distance)
        {
            if (_hasLandedAnchor)
            {
                _landedExtra += distance;
            }
        }

        /// <summary>
        /// The game moved this craft by <paramref name="positionDelta"/> (world recentring or a
        /// teleport), from the modifier's RecalculateFrameState. The landed canopy spot is a
        /// world position, so it moves along.
        /// </summary>
        internal void OnFrameShift(Vector3 positionDelta)
        {
            if (_hasLandedAnchor)
            {
                _landedAnchor += positionDelta;
            }
        }

        /// <summary>
        /// Where the landed canopy lies. Chosen once at touchdown: the round canopy downwind,
        /// along the last airborne drift (or behind the drood without drift), the ram-air
        /// behind the drood, LandedDistance of the line
        /// length away. It then stays put on the ground, except that the drood drags it along
        /// when walking beyond DragDistance. The spot is a world position; when the game moves
        /// the craft (world recentring or a teleport), <see cref="OnFrameShift"/> moves it by the
        /// same amount. Ground height and slope come from raycasts that ignore the drood's own
        /// craft.
        /// </summary>
        private void UpdateLandedAnchor()
        {
            var up = transform.up;
            var position = transform.position;
            if (_visualCollapse <= 0f)
            {
                _hasLandedAnchor = false;
                _landedInWater = false;
                return;
            }

            if (!_hasLandedAnchor)
            {
                var drift = Vector3.ProjectOnPlane(_lastAirborneVelocity, up);
                var direction = drift.sqrMagnitude > 0.25f ? drift.normalized : -Vector3.ProjectOnPlane(transform.forward, up).normalized;

                // The ram-air comes down behind the drood: it runs on after touchdown, so ahead of
                // it the wing would lie in its path.
                if (IsRamAir)
                {
                    direction = -Vector3.ProjectOnPlane(transform.forward, up).normalized;
                }
                // A drood lying down (DroodLandingFall) reaches LyingFoldExtra further back with its
                // legs: the wing lies that much further away.
                var lying = GetComponent<DroodLandingFall>() is DroodLandingFall fall && fall.LiesDown;
                _landedExtra = lying ? LyingFoldExtra : 0f;
                _landedAnchor = position + direction * (CurrentLineLength * LandedDistance + _landedExtra);
                _hasLandedAnchor = true;
            }

            // Dragged along when the drood walks (or slides down a slope) beyond the lines' reach,
            // the full distance (on a steep slope most of it is up and down).
            var offset = _landedAnchor - position;
            var maxDistance = CurrentLineLength * DragDistance + _landedExtra;
            if (offset.magnitude > maxDistance)
            {
                _landedAnchor -= offset - offset.normalized * maxDistance;
            }

            if (FindGround(_landedAnchor, up, out var ground, out var normal))
            {
                _landedGround = ground;
                _landedNormal = normal;
            }
            else
            {
                _landedGround = _landedAnchor;
                _landedNormal = up;
            }

            _groundUnderDrood = FindGround(position, up, out var feet, out _)
                ? Vector3.Dot(feet, up)
                : Vector3.Dot(position, up) - 1f;
        }

        /// <summary>
        /// Height (m) above whatever is below the drood: the game's height above terrain, or
        /// less when a building, pad or another craft is below (one raycast that ignores the
        /// drood's own craft). The raycast only runs with the chute deployed and within
        /// <see cref="StructureRayRange"/> of the terrain.
        /// </summary>
        private float HeightAboveGround(ModApi.Craft.ICraftScript craft)
        {
            var terrain = (float)craft.FlightData.AltitudeAboveGroundLevel;
            if (_state != State.Deployed || terrain > StructureRayRange || terrain <= 0f)
            {
                return terrain;
            }

            var down = (Vector3)craft.FlightData.GravityFrameNormalized;
            var count = Physics.RaycastNonAlloc(PhysicsRoot, down, _groundHits, terrain, ~0, QueryTriggerInteraction.Ignore);
            var height = terrain;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.distance < height && !IsOwnCraft(hit.collider, craft))
                {
                    height = hit.distance;
                }
            }

            return height;
        }

        /// <summary>
        /// Height (m) of the drood's lower foot above whatever is below it (one short raycast
        /// that ignores its own craft), for the flare; far up, the height above ground minus
        /// the feet's distance below the drood's root.
        /// </summary>
        private float FeetHeight(ModApi.Craft.ICraftScript craft)
        {
            var down = (Vector3)craft.FlightData.GravityFrameNormalized;
            _leftAnkle = _leftAnkle != null ? _leftAnkle : FindChild(transform, "LeftAnkle");
            _rightAnkle = _rightAnkle != null ? _rightAnkle : FindChild(transform, "RightAnkle");
            // The feet as posed, moved from the drawn body to where physics has it.
            var root = PhysicsRoot;
            var foot = root;
            if (_leftAnkle != null && _rightAnkle != null)
            {
                foot = (Vector3.Dot(_leftAnkle.position - _rightAnkle.position, down) > 0f ? _leftAnkle.position : _rightAnkle.position)
                    - transform.position + root;
            }

            _feetPoint = foot;
            var rootAboveFoot = Vector3.Dot(foot - root, down) + SoleBelowAnkle;
            if (_altitudeAboveGround > FeetRayRange + rootAboveFoot)
            {
                return _altitudeAboveGround - rootAboveFoot;
            }

            var count = Physics.RaycastNonAlloc(foot - down * 0.2f, down, _groundHits, FeetRayRange, ~0, QueryTriggerInteraction.Ignore);
            var height = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (!IsOwnCraft(hit.collider, craft))
                {
                    height = Mathf.Min(height, hit.distance - 0.2f - SoleBelowAnkle);
                }
            }

            return float.IsPositiveInfinity(height) ? _altitudeAboveGround - rootAboveFoot : Mathf.Max(height, 0f);
        }

        // The ground ahead along the flight path (PathToGround): two rays from the lower foot,
        // along the path and PathRayDown (degrees) below it, PathLookSeconds of flight long (at
        // most PathRayMax, m). Approaching a slope the ground straight below stays far until the
        // last moment; the surface ahead tells how soon the drood meets it.
        private const float PathLookSeconds = 4f;
        private const float PathRayMax = 120f;
        private const float PathRayDown = 15f;
        private Vector3 _feetPoint;
        private float _pathTime = float.PositiveInfinity;

        /// <summary>
        /// The time (s) until the feet meet the ground ahead (see PathLookSeconds): for the
        /// nearest surface each ray hits, the gap to it along its normal over the speed into it
        /// (over level ground, the feet height over the sink). Infinite with nothing ahead.
        /// </summary>
        private void PathToGround(ModApi.Craft.ICraftScript craft)
        {
            _pathTime = float.PositiveInfinity;
            var velocity = (Vector3)craft.SurfaceVelocity;
            var speed = velocity.magnitude;
            if (_state != State.Deployed || TouchedDown || speed < 1f)
            {
                return;
            }

            var down = (Vector3)craft.FlightData.GravityFrameNormalized;
            var along = velocity / speed;
            var range = Mathf.Min(speed * PathLookSeconds, PathRayMax);
            for (var ray = 0; ray < 2; ray++)
            {
                var direction = ray == 0 ? along : Vector3.RotateTowards(along, down, PathRayDown * Mathf.Deg2Rad, 0f);
                var count = Physics.RaycastNonAlloc(_feetPoint, direction, _groundHits, range, ~0, QueryTriggerInteraction.Ignore);
                var nearest = -1;
                for (var i = 0; i < count; i++)
                {
                    if ((nearest < 0 || _groundHits[i].distance < _groundHits[nearest].distance) && !IsOwnCraft(_groundHits[i].collider, craft))
                    {
                        nearest = i;
                    }
                }

                if (nearest < 0)
                {
                    continue;
                }

                var hit = _groundHits[nearest];
                var closing = -Vector3.Dot(velocity, hit.normal);
                if (closing <= 0.1f)
                {
                    continue;
                }

                var gap = Mathf.Max(Vector3.Dot(_feetPoint - hit.point, hit.normal) - SoleBelowAnkle, 0f);
                var time = gap / closing;
                if (time < _pathTime)
                {
                    _pathTime = time;
                }
            }
        }

        private bool IsOwnCraft(Collider collider, ModApi.Craft.ICraftScript craft)
        {
            if (collider.transform.IsChildOf(transform))
            {
                return true;
            }

            // The drood's own body (its capsule sits on the body object, above the part) and any
            // body of its craft.
            var ownBody = _part?.BodyScript?.RigidBody;
            if (ownBody != null && collider.attachedRigidbody == ownBody)
            {
                return true;
            }

            var body = collider.GetComponentInParent<Assets.Scripts.Craft.BodyScript>();
            if (body != null && craft != null && ReferenceEquals(body.CraftScript, craft))
            {
                return true;
            }

            var part = collider.GetComponentInParent<Assets.Scripts.Craft.Parts.PartScript>();
            return part != null && craft != null && ReferenceEquals(part.CraftScript, craft);
        }

        private bool FindGround(Vector3 point, Vector3 up, out Vector3 ground, out Vector3 normal)
        {
            ground = default;
            normal = up;
            var count = Physics.RaycastNonAlloc(point + up * 5f, -up, _groundHits, 20f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var craft = _part?.CraftScript;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.distance >= best || IsOwnCraft(hit.collider, craft))
                {
                    continue;
                }

                best = hit.distance;
                ground = hit.point;
                normal = hit.normal;
            }

            // Landed in water: the canopy and lines rest on the surface, not the sea floor.
            if (_landedInWater && craft?.ReferenceFrame != null)
            {
                var water = craft.ReferenceFrame.GetWaterPosBelowPoint(point, false, craft);
                if (best == float.MaxValue || Vector3.Dot(water - ground, up) > 0f)
                {
                    ground = water;
                    normal = up;
                    return true;
                }
            }

            return best < float.MaxValue;
        }
    }
}
