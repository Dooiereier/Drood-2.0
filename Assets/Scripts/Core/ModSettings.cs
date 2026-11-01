namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using ModApi.Common;
    using ModApi.Settings.Core;

    /// <summary>
    /// The settings for the mod.
    /// </summary>
    /// <seealso cref="ModApi.Settings.Core.SettingsCategory{Assets.Scripts.ModSettings}" />
    public class ModSettings : SettingsCategory<ModSettings>
    {
        /// <summary>
        /// The mod settings instance.
        /// </summary>
        private static ModSettings _instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModSettings"/> class.
        /// </summary>
        public ModSettings() : base("Drood 2.0")
        {
        }

        /// <summary>
        /// Gets the mod settings instance.
        /// </summary>
        /// <value>
        /// The mod settings instance.
        /// </value>
        public static ModSettings Instance => _instance ?? (_instance = Game.Instance.Settings.ModSettings.GetCategory<ModSettings>());

        /// <summary>
        /// Gets the debug logging setting: extra diagnostic lines in Player.log.
        /// </summary>
        public BoolSetting DebugLogging { get; private set; }

        /// <summary>
        /// Gets the performance log setting: Drood 2.0's time per frame in Player.log.
        /// </summary>
        public BoolSetting PerformanceLog { get; private set; }

        /// <summary>
        /// Gets the update check setting: once per game start, look for a newer version on the
        /// mod's SimpleRockets.com page (<see cref="Drood2.UpdateCheck"/>).
        /// </summary>
        public BoolSetting CheckForUpdates { get; private set; }

        /// <summary>
        /// Gets the key that removes all Drood 2.0 tethers (None: no key).
        /// </summary>
        public EnumSetting<Drood2.TetherRemoveKey> RemoveTethersKey { get; private set; }

        /// <summary>
        /// Gets the wind sound's volume (0..1).
        /// </summary>
        public NumericSetting<float> WindVolume { get; private set; }

        /// <summary>
        /// Gets the audible altimeter setting: its free-fall altitude warning and alert
        /// (<see cref="Drood2.DroodAudibleAltimeter"/>).
        /// </summary>
        public BoolSetting AudibleAltimeter { get; private set; }

        /// <summary>
        /// Gets the button that clears the swoop's high scores (asks first).
        /// </summary>
        public ButtonSetting ResetSwoopHighScores { get; private set; }

        /// <summary>
        /// Gets the button that clears the Drop Zone's high scores (asks first).
        /// </summary>
        public ButtonSetting ResetDropZoneHighScores { get; private set; }

        /// <summary>
        /// Gets Drood 2.0's first-person field of view (degrees).
        /// </summary>
        public NumericSetting<float> FirstPersonFieldOfView { get; private set; }

        /// <summary>
        /// Initializes the settings in the category.
        /// </summary>
        protected override void InitializeSettings()
        {
            this.DebugLogging = this.CreateBool("Debug Logging")
                .SetDescription("Writes extra Drood 2.0 diagnostics to Player.log (deployments, landings, camera views, held parts, tethers).")
                .SetDefault(false);

            this.PerformanceLog = this.CreateBool("Performance Log")
                .SetDescription("Writes Drood 2.0's own time per frame to Player.log every 5 seconds (its share of the frame and its costliest steps).")
                .SetDefault(false);

            this.CheckForUpdates = this.CreateBool("Check for Updates")
                .SetDescription("At game start, looks for a newer Drood 2.0 on SimpleRockets.com and offers to open its page. Nothing is downloaded or installed.")
                .SetDefault(true);

            this.RemoveTethersKey = this.CreateEnum<Drood2.TetherRemoveKey>("Remove Tethers Key")
                .SetDescription("Removes all Drood 2.0 tethers, kept and held. Works only while a Drood 2.0 is in control.")
                .SetDefault(Drood2.TetherRemoveKey.O);

            this.WindVolume = this.CreateNumeric<float>("Wind Volume", 0f, 1f, 0.05f)
                .SetDescription("The wind you hear while skydiving and under the canopy, and the jump plane's engine (Drood 2.0 only).")
                .SetDisplayFormatter(x => $"{x:P0}")
                .SetDefault(0.8f);

            this.AudibleAltimeter = this.CreateBool("Audible Altimeter")
                .SetDescription($"The beeps in the helmet in free fall: an altitude warning at {Drood2.DroodAudibleAltimeter.WarningHeight:0} m and a siren below {Drood2.DroodAudibleAltimeter.AlertHeight:0} m (Drood 2.0 only).")
                .SetDefault(true);

            this.ResetSwoopHighScores = this.CreateButton("Swoop High Scores", "Reset")
                .AddClickEvent((sender, e) => Drood2.DroodDropChallengeBests.AskReset(true));

            this.ResetDropZoneHighScores = this.CreateButton("Drop Zone High Scores", "Reset")
                .AddClickEvent((sender, e) => Drood2.DroodDropChallengeBests.AskReset(false));

            this.FirstPersonFieldOfView = this.CreateNumeric<float>("First-Person Field of View", 40f, 150f, 5f)
                .SetDescription("Drood 2.0's field of view in first person, in degrees (the stock drood is unchanged).")
                .SetDisplayFormatter(x => $"{x:0}°")
                .SetDefault(60f);
        }
    }
}