/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DS4Windows
{
    /// <summary>
    /// What a Hold to Activate special action does once its trigger button
    /// has been held for the configured time.
    /// </summary>
    public enum HoldOutputKind
    {
        /// <summary>Press a keyboard key.</summary>
        Key,
        /// <summary>Press a virtual controller button (A, B, LB, RT, ...).</summary>
        Button,
        /// <summary>Play a recorded macro once.</summary>
        Macro,
        /// <summary>Switch to another profile (a second button layout).</summary>
        Profile,
    }

    /// <summary>
    /// How a Key or Button hold output behaves after the hold time is reached.
    /// </summary>
    public enum HoldOutputMode
    {
        /// <summary>Output stays pressed for as long as the trigger stays held.</summary>
        WhileHeld,
        /// <summary>Each completed hold latches the output on, the next one turns it off.</summary>
        Toggle,
    }

    /// <summary>
    /// Settings for the accessibility "Hold to Activate" special action. One
    /// physical button gets two jobs: a quick press keeps its normal mapping
    /// and holding it past <see cref="HoldSeconds"/> runs the hold output.
    /// Stored in the special action's Details element as a small
    /// key=value;key=value string so older parsers simply skip the action.
    /// </summary>
    public sealed class HoldToActivateSettings
    {
        public const string TYPE_NAME = "HoldToActivate";
        public const double MIN_HOLD_SECONDS = 0.5;
        public const double MAX_HOLD_SECONDS = 10.0;
        public const double DEFAULT_HOLD_SECONDS = 3.0;

        private double holdSeconds = DEFAULT_HOLD_SECONDS;

        public double HoldSeconds
        {
            get => holdSeconds;
            set => holdSeconds = ClampHoldSeconds(value);
        }

        public HoldOutputKind Kind { get; set; } = HoldOutputKind.Key;
        public HoldOutputMode Mode { get; set; } = HoldOutputMode.WhileHeld;
        public int KeyCode { get; set; }
        public bool ScanCode { get; set; }
        public X360Controls Button { get; set; } = X360Controls.None;
        public List<int> Macro { get; } = new List<int>();
        public string Profile { get; set; } = string.Empty;
        /// <summary>Short controller rumble when the hold time is reached.</summary>
        public bool Rumble { get; set; } = true;
        /// <summary>
        /// When false (default) the normal button press is held back until
        /// release, then sent as a short tap, so a hold never also triggers
        /// the normal action. When true the normal press goes out immediately
        /// and is released the moment the hold time is reached.
        /// </summary>
        public bool InstantTap { get; set; }

        public long HoldMilliseconds => (long)Math.Round(holdSeconds * 1000.0);

        public bool UsesMode => Kind == HoldOutputKind.Key || Kind == HoldOutputKind.Button;

        public static double ClampHoldSeconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return DEFAULT_HOLD_SECONDS;
            return Math.Min(MAX_HOLD_SECONDS, Math.Max(MIN_HOLD_SECONDS, value));
        }

        /// <summary>
        /// Virtual controller buttons that can be pressed by a hold. Sticks
        /// are left out on purpose; a hold is a button press.
        /// </summary>
        public static bool IsSupportedButton(X360Controls control)
        {
            return control is X360Controls.A or X360Controls.B or X360Controls.X or X360Controls.Y or
                X360Controls.LB or X360Controls.RB or X360Controls.LT or X360Controls.RT or
                X360Controls.LS or X360Controls.RS or X360Controls.Back or X360Controls.Start or
                X360Controls.Guide or X360Controls.DpadUp or X360Controls.DpadDown or
                X360Controls.DpadLeft or X360Controls.DpadRight;
        }

        /// <summary>Returns null when the settings describe a usable hold, otherwise a plain error.</summary>
        public string Validate()
        {
            switch (Kind)
            {
                case HoldOutputKind.Key:
                    if (KeyCode <= 0 || KeyCode > 255) return "Pick a key for the hold to press.";
                    break;
                case HoldOutputKind.Button:
                    if (!IsSupportedButton(Button)) return "Pick a controller button for the hold to press.";
                    break;
                case HoldOutputKind.Macro:
                    if (Macro.Count == 0) return "Record a macro for the hold to play.";
                    break;
                case HoldOutputKind.Profile:
                    if (string.IsNullOrWhiteSpace(Profile)) return "Pick a profile for the hold to switch to.";
                    break;
            }

            return null;
        }

        public string Serialize()
        {
            var parts = new List<string>
            {
                "hold=" + holdSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "out=" + Kind,
            };
            switch (Kind)
            {
                case HoldOutputKind.Key:
                    parts.Add("key=" + KeyCode.ToString(CultureInfo.InvariantCulture));
                    if (ScanCode) parts.Add("sc=1");
                    parts.Add("mode=" + Mode);
                    break;
                case HoldOutputKind.Button:
                    parts.Add("button=" + Button);
                    parts.Add("mode=" + Mode);
                    break;
                case HoldOutputKind.Macro:
                    parts.Add("macro=" + string.Join("/", Macro));
                    break;
                case HoldOutputKind.Profile:
                    parts.Add("profile=" + Uri.EscapeDataString(Profile ?? string.Empty));
                    break;
            }

            parts.Add("rumble=" + (Rumble ? "1" : "0"));
            parts.Add("tap=" + (InstantTap ? "Instant" : "Delayed"));
            return string.Join(";", parts);
        }

        /// <summary>Lenient parser: unknown keys are ignored and bad values fall back to defaults.</summary>
        public static HoldToActivateSettings Parse(string details)
        {
            var result = new HoldToActivateSettings();
            if (string.IsNullOrWhiteSpace(details))
                return result;

            foreach (string rawPart in details.Split(';'))
            {
                int eq = rawPart.IndexOf('=');
                if (eq <= 0) continue;
                string key = rawPart.Substring(0, eq).Trim().ToLowerInvariant();
                string value = rawPart.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "hold":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
                            result.HoldSeconds = secs;
                        break;
                    case "out":
                        if (Enum.TryParse(value, true, out HoldOutputKind kind) && Enum.IsDefined(typeof(HoldOutputKind), kind))
                            result.Kind = kind;
                        break;
                    case "mode":
                        if (Enum.TryParse(value, true, out HoldOutputMode mode) && Enum.IsDefined(typeof(HoldOutputMode), mode))
                            result.Mode = mode;
                        break;
                    case "key":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int keyCode))
                            result.KeyCode = keyCode;
                        break;
                    case "sc":
                        result.ScanCode = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "button":
                        if (Enum.TryParse(value, true, out X360Controls button) && IsSupportedButton(button))
                            result.Button = button;
                        break;
                    case "macro":
                        result.Macro.Clear();
                        foreach (string s in value.Split('/'))
                        {
                            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                                result.Macro.Add(v);
                        }
                        break;
                    case "profile":
                        try { result.Profile = Uri.UnescapeDataString(value); }
                        catch (UriFormatException) { result.Profile = value; }
                        break;
                    case "rumble":
                        result.Rumble = !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase));
                        break;
                    case "tap":
                        result.InstantTap = value.Equals("Instant", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return result;
        }

        public string Describe()
        {
            string what = Kind switch
            {
                HoldOutputKind.Key => "key " + KeyCode.ToString(CultureInfo.InvariantCulture),
                HoldOutputKind.Button => "button " + Button,
                HoldOutputKind.Macro => "macro",
                HoldOutputKind.Profile => "profile " + Profile,
                _ => string.Empty,
            };
            if (UsesMode && Mode == HoldOutputMode.Toggle) what += " (toggle)";
            return $"Hold {holdSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s: {what}";
        }
    }

    /// <summary>One frame of output from <see cref="HoldToActivateTracker"/>.</summary>
    public readonly struct HoldToActivateFrame
    {
        public HoldToActivateFrame(bool sourcePressed, bool holdFired, bool holdHeld)
        {
            SourcePressed = sourcePressed;
            HoldFired = holdFired;
            HoldHeld = holdHeld;
        }

        /// <summary>Whether the button's normal mapping should see the button as pressed this frame.</summary>
        public bool SourcePressed { get; }
        /// <summary>True only on the frame the hold time was reached.</summary>
        public bool HoldFired { get; }
        /// <summary>True while the trigger is still held after the hold time was reached.</summary>
        public bool HoldHeld { get; }
    }

    /// <summary>
    /// Pure, clock-injected state machine for one Hold to Activate action on
    /// one controller. Fixed timing only; there is deliberately no jitter.
    /// </summary>
    public sealed class HoldToActivateTracker
    {
        public enum Phase { Idle, Pressing, TapPulse, HoldFired }

        /// <summary>How long a delayed tap is sent to the normal mapping.</summary>
        public const long TAP_PULSE_MS = 100;

        private long pressStartMs;
        private long pulseEndMs;

        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        /// <summary>Latched state for Toggle mode outputs.</summary>
        public bool Latched { get; private set; }

        public void Reset()
        {
            CurrentPhase = Phase.Idle;
            Latched = false;
        }

        public HoldToActivateFrame Update(bool pressed, long nowMs, long holdMs, bool instantTap, bool toggleOnFire = false)
        {
            if (holdMs < 1) holdMs = 1;
            switch (CurrentPhase)
            {
                case Phase.Pressing:
                    if (!pressed)
                    {
                        if (instantTap)
                        {
                            CurrentPhase = Phase.Idle;
                            return new HoldToActivateFrame(false, false, false);
                        }

                        CurrentPhase = Phase.TapPulse;
                        pulseEndMs = nowMs + TAP_PULSE_MS;
                        return new HoldToActivateFrame(true, false, false);
                    }

                    if (nowMs - pressStartMs >= holdMs)
                    {
                        CurrentPhase = Phase.HoldFired;
                        if (toggleOnFire) Latched = !Latched;
                        return new HoldToActivateFrame(false, true, true);
                    }

                    return new HoldToActivateFrame(instantTap, false, false);

                case Phase.TapPulse:
                    if (nowMs < pulseEndMs)
                        return new HoldToActivateFrame(true, false, false);
                    CurrentPhase = Phase.Idle;
                    return UpdateIdle(pressed, nowMs, instantTap);

                case Phase.HoldFired:
                    if (pressed)
                        return new HoldToActivateFrame(false, false, true);
                    CurrentPhase = Phase.Idle;
                    return new HoldToActivateFrame(false, false, false);

                default:
                    return UpdateIdle(pressed, nowMs, instantTap);
            }
        }

        private HoldToActivateFrame UpdateIdle(bool pressed, long nowMs, bool instantTap)
        {
            if (!pressed)
                return new HoldToActivateFrame(false, false, false);

            CurrentPhase = Phase.Pressing;
            pressStartMs = nowMs;
            return new HoldToActivateFrame(instantTap, false, false);
        }
    }
}
