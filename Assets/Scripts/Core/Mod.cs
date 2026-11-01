namespace Assets.Scripts
{
    using System.Linq;
    using System.Runtime.CompilerServices;
    using HarmonyLib;
    using ModApi.Craft;
    using ModApi.Scenes.Events;
    using UnityEngine;

    public class Mod : ModApi.Mods.GameMod
    {
        private static bool _harmonyFeaturesInitialized;
        private static bool _reportedMissingHarmony;
        private static bool _tethersAvailable;

        private Mod() : base()
        {
        }

        public static Mod Instance { get; } = GetModInstance<Mod>();

        protected override void OnModInitialized()
        {
            // Register the settings category (Debug Logging toggle) so it shows in the menu.
            try
            {
                _ = ModSettings.Instance;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't register the mod settings: {e.Message}");
            }

            // The icon comes first: the game loads the part from the mod's part XML, and its
            // designer icon must be in place before the designer loads, even if a patch fails.
            WriteIcon();
            Game.Instance.SceneManager.SceneLoading += OnSceneLoading;
            Game.Instance.SceneManager.SceneLoaded += OnSceneLoaded;
            Drood2.DroodReflection.Validate();

            TryInitializeHarmonyFeatures();
        }

        public override bool IsModRequiredForCraft(CraftData craftData)
        {
            // Drood 2.0 only exists while this mod is loaded.
            return Drood2.Drood2Types.CraftUsesDroodSurface(craftData);
        }

        private static bool IsHarmonyLoaded()
        {
            return System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony");
        }

        /// <summary>
        /// Harmony (0Harmony) is provided by the "Juno Harmony" mod, not shipped with Drood 2.0.
        /// It's looked for at mod init and again at each scene load, in case Juno Harmony
        /// loads after this mod. Everything that touches HarmonyLib lives in
        /// InitializeHarmonyFeatures, which is only JIT-compiled once the assembly is known to
        /// be loaded.
        /// </summary>
        private static void TryInitializeHarmonyFeatures()
        {
            if (_harmonyFeaturesInitialized || !IsHarmonyLoaded())
            {
                return;
            }

            _harmonyFeaturesInitialized = true;
            InitializeHarmonyFeatures();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitializeHarmonyFeatures()
        {
            // Each patch class on its own, so a class whose game target changed or vanished
            // (after a game update) only disables that feature. Its static setup (reflection
            // lookups of game members) is run here as well, inside the same guard, so a
            // missing member is reported now instead of throwing from inside a game method
            // later.
            var harmony = new Harmony("Drood2");
            var applied = 0;
            var failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Mod).Assembly))
            {
                if (!type.GetCustomAttributes(typeof(HarmonyPatch), true).Any())
                {
                    continue;
                }

                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    Debug.LogError($"[Drood 2.0] Couldn't apply {type.Name}, so that feature is disabled: {e.GetBaseException().Message}");
                }
            }

            // Always logged: settings (Debug Logging) aren't loaded yet during mod init, and
            // this line is how a player's log shows whether every feature came up.
            Debug.Log($"[Drood 2.0] Harmony patches: {applied} applied, {failed} failed.");

            // PersistentTethers uses AccessTools, so it's only used when Harmony is present; its
            // reflection is checked here, and it's created in each flight scene (OnSceneLoaded).
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(Drood2.PersistentTethers).TypeHandle);
                _tethersAvailable = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Drood 2.0] Couldn't start persistent tethers, so that feature is disabled: {e.GetBaseException().Message}");
            }
        }

        private void OnSceneLoaded(object sender, SceneEventArgs e)
        {
            // Once, from the first scene: the settings (Check for Updates) aren't loaded during
            // mod init.
            Drood2.UpdateCheck.Start();
            Drood2.DroodPlaneScenery.OnSceneLoaded();
            if (!Game.InFlightScene)
            {
                return;
            }

            if (_tethersAvailable)
            {
                CreateTethers();
            }

            try
            {
                new GameObject("Drood2.CameraViews").AddComponent<Drood2.DroodCameraViews>();
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[Drood 2.0] Couldn't add the camera views in this flight: {exception.GetBaseException().Message}");
            }
        }

        // Separate and not inlined: touching PersistentTethers loads HarmonyLib.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CreateTethers()
        {
            try
            {
                new GameObject("Drood2.PersistentTethers").AddComponent<Drood2.PersistentTethers>();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Drood 2.0] Couldn't create the persistent tethers in this flight: {e.GetBaseException().Message}");
            }
        }

        private void OnSceneLoading(object sender, SceneEventArgs e)
        {
            CheckPartLoaded();
            TryInitializeHarmonyFeatures();

            if (!_harmonyFeaturesInitialized && !_reportedMissingHarmony)
            {
                _reportedMissingHarmony = true;
                Debug.LogError("[Drood 2.0] The 'Juno Harmony' mod is not installed or enabled. Drood 2.0 requires it: without its patches the part can't be added in the designer.");
            }
        }

        private static void WriteIcon()
        {
            try
            {
                Drood2.Drood2Part.EnsureIcon();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void CheckPartLoaded()
        {
            try
            {
                Drood2.Drood2Part.CheckLoaded();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
