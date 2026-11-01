namespace Drood2
{
    /// <summary>
    /// Diagnostic logging, off unless "Debug Logging" is switched on in the mod's settings.
    /// Warnings and errors always go straight to Debug.LogWarning/LogError.
    /// </summary>
    public static class DroodLog
    {
        public static bool DebugEnabled
        {
            get
            {
                try
                {
                    return Assets.Scripts.ModSettings.Instance?.DebugLogging?.Value ?? false;
                }
                catch (System.Exception)
                {
                    // Settings not available yet (early startup).
                    return false;
                }
            }
        }

        public static void Debug(string message)
        {
            if (DebugEnabled)
            {
                UnityEngine.Debug.Log("[Drood 2.0] " + message);
            }
        }
    }
}
