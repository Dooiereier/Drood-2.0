using System.Xml.Linq;
using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Flight.Sim;
using ModApi.Craft;
using ModApi.Flight.Sim;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// A real camera beside the course, for mods that collect the stock camera parts (HorseEye
    /// lists parts whose type id starts with "Camera" and films from the part's own transform):
    /// a one-part craft with a stock Camera1, spawned with the game's own SpawnCraft (as career
    /// contracts spawn crafts in flight) at the Course Camera's spot, player control off, removed
    /// with DestroyCraft when the course goes and by the game at the flight's end
    /// (DestroyOnExitFlightScene). A save keeps the craft but not that flag, so a loaded flight's
    /// camera crafts are cleared up (<see cref="Sweep"/>): all of them without a course, all but
    /// one with it, that one marked again. Its body is kinematic and placed each frame at the spot, aimed
    /// at the drood (<see cref="DroodCourseCamera"/>).
    /// After a quickload the craft of the same name is taken again. The game's own camera list
    /// keeps the Course Camera on the drood (this one isn't the active craft's, so the game
    /// doesn't list it).
    /// </summary>
    internal sealed class DroodCourseCameraCraft
    {
        internal const string CraftName = "Drood 2.0 Course Camera";

        private const string CraftXml =
@"<Craft name=""Drood 2.0 Course Camera"" parent="""" initialBoundsMin=""-0.1,-0.1,-0.1"" initialBoundsMax=""0.1,0.1,0.1"" price=""0"" suppressCraftConfigWarnings=""true"" xmlVersion=""14"" localCenterOfMass=""0,0,0"">
  <Assembly>
    <Parts>
      <Part id=""0"" partType=""Camera1"" position=""0,0,0"" rotation=""0,0,0"" rootPart=""true"" materials=""24,24,24,24,24"">
        <Drag drag=""0,0,0,0,0,0"" area=""0,0,0,0,0,0"" />
        <Config heatShieldScale=""0"" includeInDrag=""false"" />
        <CameraVantage cameraOffset=""0,0.065,0"" fieldOfView=""60"" fieldOfViewMax=""90"" fieldOfViewMin=""1"" padPosition=""false"" version=""2"" />
        <InputController input=""Slider3"" inputId=""Zoom"" />
      </Part>
    </Parts>
    <Connections />
    <Collisions />
    <Bodies>
      <Body id=""1"" partIds=""0"" mass=""0.001"" position=""0,0,0"" rotation=""0,0,0"" centerOfMass=""0,0,0"" />
    </Bodies>
  </Assembly>
  <DesignerSettings />
  <Themes />
  <Symmetry />
</Craft>";

        // At most MaxSpawns spawns a course, at least RespawnSeconds apart (a camera craft lost,
        // e.g. on impact, is spawned again, but never in a loop).
        private const int MaxSpawns = 3;
        private const float RespawnSeconds = 5f;

        private ICraftNode _node;
        private Vector3 _spot;
        private Quaternion _view = Quaternion.identity;
        private bool _hasPlace;
        private bool _failed;
        private int _spawns;
        private float _nextSpawn;

        /// <summary>
        /// The camera at <paramref name="spot"/> (world), turned to <paramref name="view"/>;
        /// spawned first if there is none yet.
        /// </summary>
        internal void Place(ICraftScript drood, Vector3 spot, Quaternion view, float height)
        {
            if (_failed || drood == null)
            {
                return;
            }

            if (_node == null || _node.IsDestroyed)
            {
                _node = Find();
                if (_node == null)
                {
                    if (_spawns >= MaxSpawns || Time.time < _nextSpawn)
                    {
                        return;
                    }

                    _spawns++;
                    _nextSpawn = Time.time + RespawnSeconds;
                    _node = Spawn(drood, spot, view, height);
                    if (_node == null)
                    {
                        if (_spawns >= MaxSpawns)
                        {
                            Debug.LogWarning($"[Drood 2.0] The course camera craft was lost {MaxSpawns} times; no more spawns for this course.");
                        }

                        return;
                    }
                }
            }

            // Loaded only near the player's craft (the game's physics range).
            var craft = _node.CraftScript;
            var part = craft?.RootPart;
            var body = part?.BodyScript?.RigidBody;
            if (body == null)
            {
                return;
            }

            Freeze(body);
            _spot = spot;
            _view = view;
            _hasPlace = true;
            body.position = spot;
            body.rotation = view;
            body.transform.SetPositionAndRotation(spot, view);
        }

        /// <summary>
        /// Physics step: the body held at its place (the game moved it up between frames, and the
        /// frame's placement snapped it back).
        /// </summary>
        internal void Hold()
        {
            if (!_hasPlace || _node == null || _node.IsDestroyed)
            {
                return;
            }

            var body = _node.CraftScript?.RootPart?.BodyScript?.RigidBody;
            if (body == null)
            {
                return;
            }

            Freeze(body);
            body.position = _spot;
            body.rotation = _view;
        }

        /// <summary>
        /// Early in the frame (before other mods read the part, HorseEye in its Update): the
        /// camera turned toward <paramref name="target"/> (world) from its held place.
        /// </summary>
        internal void AimEarly(Vector3 target, Vector3 up)
        {
            if (!_hasPlace || _node == null || _node.IsDestroyed)
            {
                return;
            }

            var body = _node.CraftScript?.RootPart?.BodyScript?.RigidBody;
            var look = target - _spot;
            if (body == null || look.sqrMagnitude < 1f)
            {
                return;
            }

            _view = Quaternion.LookRotation(look.normalized, up);
            body.rotation = _view;
            body.transform.SetPositionAndRotation(_spot, _view);
        }

        /// <summary>The game moved the world (recentring): the held place with it.</summary>
        internal void OnFrameShift(Vector3 positionDelta)
        {
            _spot += positionDelta;
        }

        /// <summary>Not moved by anything: kinematic, no gravity, no collisions, every axis locked.</summary>
        private static void Freeze(Rigidbody body)
        {
            if (!body.isKinematic)
            {
                body.isKinematic = true;
            }

            if (body.useGravity)
            {
                body.useGravity = false;
            }

            if (body.detectCollisions)
            {
                body.detectCollisions = false;
            }

            if (body.constraints != RigidbodyConstraints.FreezeAll)
            {
                body.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

        /// <summary>The camera craft removed (no course any more).</summary>
        internal void Remove()
        {
            var node = _node ?? Find();
            _node = null;
            _hasPlace = false;
            _spawns = 0;
            _nextSpawn = 0f;
            if (node is CraftNode craftNode && !craftNode.IsDestroyed)
            {
                craftNode.DestroyCraft();
                DroodLog.Debug("Course camera craft removed.");
            }
        }

        /// <summary>The flight scene ends: its craft goes with it.</summary>
        internal void Forget()
        {
            _node = null;
        }

        private static readonly System.Collections.Generic.List<ICraftNode> Found = new System.Collections.Generic.List<ICraftNode>();

        /// <summary>
        /// Every camera craft in the flight scene (from a save) removed: the player flies something
        /// else than a Drood 2.0, so no drood's camera would clear them.
        /// </summary>
        internal static void RemoveAll()
        {
            var nodes = Assets.Scripts.Game.Instance?.FlightScene?.FlightState?.CraftNodes;
            if (nodes == null)
            {
                return;
            }

            Found.Clear();
            foreach (var node in nodes)
            {
                if (node != null && !node.IsDestroyed && node.Name == CraftName)
                {
                    Found.Add(node);
                }
            }

            foreach (var node in Found)
            {
                if (node is CraftNode craftNode)
                {
                    craftNode.DestroyCraft();
                }
            }

            if (Found.Count > 0)
            {
                DroodLog.Debug($"Course camera crafts from a save: {Found.Count} removed.");
            }

            Found.Clear();
        }

        /// <summary>
        /// Once per flight scene: the camera crafts there are (from a save) removed, but one kept
        /// when <paramref name="keepOne"/>, marked to go at the flight's end again.
        /// </summary>
        internal void Sweep(bool keepOne)
        {
            var nodes = Assets.Scripts.Game.Instance?.FlightScene?.FlightState?.CraftNodes;
            if (nodes == null)
            {
                return;
            }

            Found.Clear();
            foreach (var node in nodes)
            {
                if (node != null && !node.IsDestroyed && node.Name == CraftName)
                {
                    Found.Add(node);
                }
            }

            for (var i = 0; i < Found.Count; i++)
            {
                if (keepOne && i == 0)
                {
                    Found[i].DestroyOnExitFlightScene = true;
                    _node = Found[i];
                }
                else if (Found[i] is CraftNode craftNode)
                {
                    if (craftNode == _node)
                    {
                        _node = null;
                    }

                    craftNode.DestroyCraft();
                }
            }

            if (Found.Count > (keepOne ? 1 : 0))
            {
                DroodLog.Debug($"Course camera crafts from a save: {Found.Count} found, {(keepOne ? Found.Count - 1 : Found.Count)} removed.");
            }

            Found.Clear();
        }

        private static ICraftNode Find()
        {
            var nodes = Assets.Scripts.Game.Instance?.FlightScene?.FlightState?.CraftNodes;
            if (nodes == null)
            {
                return null;
            }

            foreach (var node in nodes)
            {
                if (node != null && !node.IsDestroyed && node.Name == CraftName)
                {
                    return node;
                }
            }

            return null;
        }

        private ICraftNode Spawn(ICraftScript drood, Vector3 spot, Quaternion view, float height)
        {
            try
            {
                if (!(drood.CraftNode?.Parent is IPlanetNode planet))
                {
                    return null;
                }

                var scene = Assets.Scripts.Flight.FlightSceneScript.Instance;
                var loader = Assets.Scripts.Game.Instance?.CraftLoader;
                if (scene == null || loader == null)
                {
                    return null;
                }

                var data = loader.LoadCraftImmediate(XElement.Parse(CraftXml));
                foreach (var part in data.Assembly.Parts)
                {
                    part.IsSpawned = true;
                }

                // The spot as latitude and longitude, its height above the ground as placed.
                var surface = planet.PlanetVectorToSurfaceVector(drood.ReferenceFrame.FrameToPlanetPosition(spot));
                planet.GetSurfaceCoordinates(surface, out var latitude, out var longitude);
                var heading = (double)view.eulerAngles.y;
                // In the air at its height (on the ground it was destroyed on impact before it was
                // made kinematic).
                var location = new ModApi.State.LaunchLocation(CraftName, ModApi.State.LaunchLocationType.SurfaceLockedAir, planet.Name,
                    latitude, longitude, Vector3d.zero, heading, height);
                var node = scene.SpawnCraft(CraftName, data, location);
                if (node == null)
                {
                    return null;
                }

                node.AllowPlayerControl = false;
                node.DestroyOnExitFlightScene = true;

                // Held on the surface while not loaded (as the career contracts' spawn does for a
                // surface place): otherwise the game moves it along an orbit, straight into the
                // ground, and destroys it as an impact before it is ever loaded.
                node.InContactWithPlanet = true;
                node.FlightStart();

                // Exactly at the spot (as a teleport does; the place from latitude and longitude
                // is only where it starts), moving with the ground.
                node.SetStateVectors(planet.SurfaceVectorToPlanetVector(surface),
                    planet.SurfaceVectorToPlanetVector(planet.CalculateSurfaceVelocity(surface)), node.Orbit.Time);
                var body = node.CraftScript?.RootPart?.BodyScript?.RigidBody;
                if (body != null)
                {
                    Freeze(body);
                }

                DroodLog.Debug($"Course camera craft spawned at {latitude:0.00000}, {longitude:0.00000}.");
                return node;
            }
            catch (System.Exception e)
            {
                _failed = true;
                Debug.LogWarning($"[Drood 2.0] Couldn't spawn the course camera craft: {e.GetBaseException().Message}");
                return null;
            }
        }
    }
}
