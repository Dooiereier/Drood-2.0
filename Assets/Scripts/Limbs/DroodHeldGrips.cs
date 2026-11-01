using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Where each held part sits on its bone (the grip), kept in <see cref="Drood2Data"/> so
    /// it's saved with the craft and in quicksaves.
    ///
    /// Why stored: a part's saved position is wherever the limb follower last put it (the
    /// animated hand), so measuring the grip again after a quickload against the designer pose
    /// would shift the part a little every time. Part ids can't be the key: the game renumbers
    /// parts when a drood becomes its own craft (EVA exit) and on quickload (in-game probe).
    /// The key is the attach point plus the part's place in that held group (first, second,
    /// ... part found from the attach point), and the part type is checked on use.
    ///
    /// SAVE FORMAT: "AttachPoint/Index=PartTypeId:px,py,pz,qx,qy,qz,qw;" per grip.
    /// </summary>
    internal sealed class DroodHeldGrips
    {
        internal struct Grip
        {
            public string PartTypeId;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private readonly Drood2Data _data;
        private readonly Dictionary<string, Grip> _grips = new Dictionary<string, Grip>();
        private string _parsed;

        public DroodHeldGrips(Drood2Data data)
        {
            _data = data;
        }

        public static string Key(string attachPoint, int index) => attachPoint + "/" + index;

        public bool TryGet(string key, string partTypeId, out Grip grip)
        {
            Load();
            return _grips.TryGetValue(key, out grip) && grip.PartTypeId == partTypeId;
        }

        public void Set(string key, Grip grip)
        {
            Load();
            _grips[key] = grip;
            Save();
        }

        /// <summary>Drops grips whose key isn't in <paramref name="keep"/> (parts removed).</summary>
        public void Retain(ICollection<string> keep)
        {
            Load();
            var removed = false;
            foreach (var key in new List<string>(_grips.Keys))
            {
                if (!keep.Contains(key))
                {
                    _grips.Remove(key);
                    removed = true;
                }
            }

            if (removed)
            {
                Save();
            }
        }

        private void Load()
        {
            var text = _data.HeldGrips ?? string.Empty;
            if (ReferenceEquals(text, _parsed))
            {
                return;
            }

            _parsed = text;
            _grips.Clear();
            foreach (var entry in text.Split(';'))
            {
                var equals = entry.IndexOf('=');
                var colon = entry.LastIndexOf(':');
                if (equals <= 0 || colon <= equals)
                {
                    continue;
                }

                var numbers = entry.Substring(colon + 1).Split(',');
                if (numbers.Length != 7)
                {
                    continue;
                }

                var values = new float[7];
                var ok = true;
                for (var i = 0; i < 7 && ok; i++)
                {
                    ok = float.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                }

                if (ok)
                {
                    _grips[entry.Substring(0, equals)] = new Grip
                    {
                        PartTypeId = entry.Substring(equals + 1, colon - equals - 1),
                        Position = new Vector3(values[0], values[1], values[2]),
                        Rotation = new Quaternion(values[3], values[4], values[5], values[6]),
                    };
                }
            }
        }

        private void Save()
        {
            var text = new StringBuilder();
            foreach (var pair in _grips)
            {
                var p = pair.Value.Position;
                var r = pair.Value.Rotation;
                text.Append(pair.Key).Append('=').Append(pair.Value.PartTypeId).Append(':')
                    .Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',')
                    .Append(F(r.x)).Append(',').Append(F(r.y)).Append(',').Append(F(r.z)).Append(',').Append(F(r.w))
                    .Append(';');
            }

            _parsed = text.ToString();
            _data.HeldGrips = _parsed;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
