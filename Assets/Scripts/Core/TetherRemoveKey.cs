using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Choices for the "Remove Tethers Key" setting. A short list: the settings screen has no
    /// key-capture control. Only keys the game doesn't use in flight (P is pause).
    /// </summary>
    public enum TetherRemoveKey
    {
        None,
        O,
        K,
        L,
        J,
    }

    internal static class TetherRemoveKeyExtensions
    {
        public static KeyCode ToKeyCode(this TetherRemoveKey key)
        {
            switch (key)
            {
                case TetherRemoveKey.O: return KeyCode.O;
                case TetherRemoveKey.K: return KeyCode.K;
                case TetherRemoveKey.L: return KeyCode.L;
                case TetherRemoveKey.J: return KeyCode.J;
                default: return KeyCode.None;
            }
        }
    }
}
