using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using Assets.Scripts.Flight;
using ModApi;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Mouse steering for Drood 2.0 while skydiving and under the canopy: the game's own mouse
    /// joystick. Its switch is the game's (FlightControls.EnableMouseJoystick: one for the
    /// flight scene, on the player's controls), toggled with the game's mouse joystick key. The
    /// stick is the cursor's place on the screen, as the game computes it for aircraft
    /// (FlightControls.Update): the middle is neutral, with the player's deadzones and pitch
    /// invert, but full input already halfway to the screen edge (<see cref="Gain"/>).
    /// Left/right steers (the turn keys), up/down is forward/back (front risers or a track;
    /// brakes, flare or a backslide). The keys keep working; the larger of the two counts.
    /// While it is on, the first-person camera doesn't look around with the mouse
    /// (<see cref="DroodFirstPersonBody"/>), so the cursor is free to steer.
    /// </summary>
    internal static class DroodMouseSteering
    {
        // The game caps the deadzone at this (FlightControls.Update's ProcessDeadzone).
        private const float MaxDeadzone = 0.9f;

        // Full input halfway between the deadzone and the screen edge: twice the game's
        // aircraft stick.
        private const float Gain = 2f;

        // The player's deadzones, narrowed for a drood: left/right to SideDeadzoneScale of them,
        // up/down to PitchDeadzoneScale.
        private const float SideDeadzoneScale = 0.125f;
        private const float PitchDeadzoneScale = 0.5f;

        // Left/right input's curve: the share past the dead band to this power.
        private const float SideCurve = 1.5f;

        /// <summary>The game's mouse joystick is on and <paramref name="eva"/> is the drood you fly.</summary>
        internal static bool IsOn(EvaScript eva) =>
            eva != null && eva.IsPlayerCraft && Controls() is FlightControls controls && controls.EnableMouseJoystick;

        private static FlightControls Controls() =>
            (Assets.Scripts.Game.Instance?.FlightScene as FlightSceneScript)?.FlightControls;

        /// <summary>
        /// The game's mouse joystick key for a Drood 2.0 in control. Droods use the "FlightEva"
        /// input map and the game disables "FlightCraft", so the player's binding is read here
        /// (<see cref="DroodKeys"/>), and skipped if the game's own toggle fired. Shows the
        /// game's own message.
        /// </summary>
        internal static void HandleToggle(EvaScript eva)
        {
            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            if (!DroodKeys.CanUseKeys(eva)
                || !(Controls() is FlightControls controls)
                || !DroodKeys.BindingDown(inputs?.ToggleMouseJoystick)
                || inputs?.ToggleMouseJoystick?.GetButtonDownIfEnabled() == true)
            {
                return;
            }

            controls.EnableMouseJoystick = !controls.EnableMouseJoystick;
            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.ShowMessage(
                Locale.GetString(controls.EnableMouseJoystick ? "Flight.Controls.MouseJoystickEnabled" : "Flight.Controls.MouseJoystickDisabled"),
                false,
                5f);
        }

        /// <summary>
        /// The stick: x right (-1..1, on a curve: SideCurve), y up (-1..1, up is forward unless the player inverts
        /// pitch); full input at half the game's deflection (<see cref="Gain"/>). The deadzone
        /// for up (forward) is <paramref name="forwardDeadzoneScale"/> times the player's.
        /// </summary>
        internal static Vector2 Stick(float forwardDeadzoneScale = 1f)
        {
            var settings = Assets.Scripts.Game.Instance?.Settings?.Game?.MouseInputFlight;
            var position = (Vector2)Input.mousePosition;
            var stick = new Vector2(
                Mathf.Clamp(position.x / (Screen.width * 0.5f) - 1f, -1f, 1f),
                Mathf.Clamp(position.y / (Screen.height * 0.5f) - 1f, -1f, 1f));
            if (settings != null)
            {
                stick.x = Deadzone(stick.x, settings.MouseJoystickDeadzoneRoll.Value * SideDeadzoneScale);
                if (settings.MouseJoystickInvertPitch.Value)
                {
                    stick.y = -stick.y;
                }

                var pitchDeadzone = settings.MouseJoystickDeadzonePitch.Value * PitchDeadzoneScale;
                stick.y = Deadzone(stick.y, stick.y > 0f ? pitchDeadzone * forwardDeadzoneScale : pitchDeadzone);
            }

            stick.x = Mathf.Clamp(stick.x * Gain, -1f, 1f);

            // Left/right on a curve (to the power SideCurve): small moves give a little, full
            // input still all of it.
            stick.x = Mathf.Sign(stick.x) * Mathf.Pow(Mathf.Abs(stick.x), SideCurve);
            stick.y = Mathf.Clamp(stick.y * Gain, -1f, 1f);
            return stick;
        }

        /// <summary>
        /// Where the stick's zones lie on the screen, as shares of the half-screen from its middle
        /// (as <see cref="Stick"/> reads them): the dead band's edges to the side, up and down, and
        /// where the input becomes full; whether up is back (the player's pitch invert).
        /// </summary>
        internal static void Zones(float forwardDeadzoneScale, out float side, out float up, out float down, out bool upIsBack)
        {
            var settings = Assets.Scripts.Game.Instance?.Settings?.Game?.MouseInputFlight;
            var roll = settings != null ? Mathf.Min(settings.MouseJoystickDeadzoneRoll.Value * SideDeadzoneScale, MaxDeadzone) : 0f;
            var pitch = settings != null ? settings.MouseJoystickDeadzonePitch.Value * PitchDeadzoneScale : 0f;
            upIsBack = settings != null && settings.MouseJoystickInvertPitch.Value;
            var forward = Mathf.Min(pitch * forwardDeadzoneScale, MaxDeadzone);
            var back = Mathf.Min(pitch, MaxDeadzone);
            side = roll;
            up = upIsBack ? back : forward;
            down = upIsBack ? forward : back;
        }

        /// <summary>Where the input is full, past a dead band ending at <paramref name="deadzone"/> (shares as in <see cref="Zones"/>).</summary>
        internal static float FullAt(float deadzone) => deadzone + (1f - deadzone) / Gain;

        /// <summary>The key or the mouse, whichever is further from neutral.</summary>
        internal static float Combine(float key, float mouse) => Mathf.Abs(key) >= Mathf.Abs(mouse) ? key : mouse;

        private static float Deadzone(float value, float deadzone)
        {
            deadzone = Mathf.Min(deadzone, MaxDeadzone);
            if (value > deadzone)
            {
                return (value - deadzone) / (1f - deadzone);
            }

            if (value < -deadzone)
            {
                return (value + deadzone) / (1f - deadzone);
            }

            return 0f;
        }
    }
}
