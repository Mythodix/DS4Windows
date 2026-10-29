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

namespace DS4Windows
{
    /// <summary>How the mode button of a Combo Hold Mode action turns the mode on.</summary>
    public enum ComboModeBehavior
    {
        /// <summary>Each press of the mode button turns the mode on, the next press turns it off.</summary>
        Toggle,
        /// <summary>The mode is on only while the mode button is held.</summary>
        WhileHeld,
    }

    /// <summary>How the output buttons are sent while the hold button is held.</summary>
    public enum ComboOutputStyle
    {
        /// <summary>Press and release all outputs together, over and over (default).</summary>
        Pulse,
        /// <summary>Keep all outputs held down steadily.</summary>
        Steady,
    }

    /// <summary>
    /// Settings for the accessibility "Combo Hold Mode" special action. The
    /// action's single trigger is the mode button (for example a DualSense
    /// Edge back paddle). While the mode is on, holding the hold button
    /// presses every output button together for as long as it is held.
    /// Stored in Details as key=value;key=value so older builds skip it.
    /// </summary>
    public sealed class ComboHoldModeSettings
    {
        public const string TYPE_NAME = "ComboHoldMode";
        public const int MIN_PULSE_MS = 30;
        public const int MAX_PULSE_MS = 500;
        public const int DEFAULT_PULSE_MS = 80;

        /// <summary>Output buttons offered in the editor, in display order.</summary>
        public static IReadOnlyList<DS4Controls> SupportedOutputs { get; } = new[]
        {
            DS4Controls.Cross, DS4Controls.Circle, DS4Controls.Square, DS4Controls.Triangle,
            DS4Controls.L1, DS4Controls.R1, DS4Controls.L2, DS4Controls.R2,
            DS4Controls.L3, DS4Controls.R3,
            DS4Controls.DpadUp, DS4Controls.DpadDown, DS4Controls.DpadLeft, DS4Controls.DpadRight,
            DS4Controls.Options, DS4Controls.Share, DS4Controls.PS,
        };

        /// <summary>Physical buttons that can be the hold button.</summary>
        public static IReadOnlyList<DS4Controls> SupportedHoldButtons { get; } = new[]
        {
            DS4Controls.Square, DS4Controls.Cross, DS4Controls.Circle, DS4Controls.Triangle,
            DS4Controls.L1, DS4Controls.R1, DS4Controls.L2, DS4Controls.R2,
            DS4Controls.L3, DS4Controls.R3,
            DS4Controls.DpadUp, DS4Controls.DpadDown, DS4Controls.DpadLeft, DS4Controls.DpadRight,
            DS4Controls.Options, DS4Controls.Share, DS4Controls.PS, DS4Controls.Mute,
            DS4Controls.BLP, DS4Controls.BRP, DS4Controls.FnL, DS4Controls.FnR,
        };

        private int pressMs = DEFAULT_PULSE_MS;
        private int releaseMs = DEFAULT_PULSE_MS;

        public ComboModeBehavior ModeBehavior { get; set; } = ComboModeBehavior.Toggle;
        public DS4Controls HoldButton { get; set; } = DS4Controls.Square;
        public List<DS4Controls> Outputs { get; } = new List<DS4Controls>();
        /// <summary>Also send the hold button itself while the mode is on. Default off (it is replaced).</summary>
        public bool PassHoldButton { get; set; }
        public ComboOutputStyle Style { get; set; } = ComboOutputStyle.Pulse;
        public int PressMs { get => pressMs; set => pressMs = ClampPulseMs(value); }
        public int ReleaseMs { get => releaseMs; set => releaseMs = ClampPulseMs(value); }
        public bool Rumble { get; set; } = true;
        public bool Lightbar { get; set; } = true;

        public static int ClampPulseMs(int value) => Math.Min(MAX_PULSE_MS, Math.Max(MIN_PULSE_MS, value));

        public static bool IsSupportedOutput(DS4Controls c)
        {
            foreach (DS4Controls o in SupportedOutputs) if (o == c) return true;
            return false;
        }

        public static bool IsSupportedHoldButton(DS4Controls c)
        {
            foreach (DS4Controls o in SupportedHoldButtons) if (o == c) return true;
            return false;
        }

        /// <summary>The user's requested combo: R1, R2, L2, Triangle and Cross.</summary>
        public static ComboHoldModeSettings CreateDefault()
        {
            var s = new ComboHoldModeSettings();
            s.Outputs.AddRange(new[] { DS4Controls.R1, DS4Controls.R2, DS4Controls.L2, DS4Controls.Triangle, DS4Controls.Cross });
            return s;
        }

        public string Validate()
        {
            if (!IsSupportedHoldButton(HoldButton)) return "Pick the button you will hold.";
            if (Outputs.Count == 0) return "Tick at least one button to press while you hold.";
            return null;
        }

        public string Serialize()
        {
            var outs = new List<string>(Outputs.Count);
            foreach (DS4Controls c in Outputs) outs.Add(c.ToString());
            var parts = new List<string>
            {
                "mode=" + ModeBehavior,
                "hold=" + HoldButton,
                "out=" + string.Join("/", outs),
                "passhold=" + (PassHoldButton ? "1" : "0"),
                "style=" + Style,
                "down=" + pressMs.ToString(CultureInfo.InvariantCulture),
                "up=" + releaseMs.ToString(CultureInfo.InvariantCulture),
                "rumble=" + (Rumble ? "1" : "0"),
                "light=" + (Lightbar ? "1" : "0"),
            };
            return string.Join(";", parts);
        }

        /// <summary>Lenient parser: unknown keys are ignored and bad values fall back to defaults.</summary>
        public static ComboHoldModeSettings Parse(string details)
        {
            var result = new ComboHoldModeSettings();
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
                    case "mode":
                        if (Enum.TryParse(value, true, out ComboModeBehavior mode) && Enum.IsDefined(typeof(ComboModeBehavior), mode))
                            result.ModeBehavior = mode;
                        break;
                    case "hold":
                        if (Enum.TryParse(value, true, out DS4Controls hold) && IsSupportedHoldButton(hold))
                            result.HoldButton = hold;
                        break;
                    case "out":
                        result.Outputs.Clear();
                        foreach (string s in value.Split('/'))
                        {
                            if (Enum.TryParse(s.Trim(), true, out DS4Controls c) && IsSupportedOutput(c) &&
                                !result.Outputs.Contains(c))
                                result.Outputs.Add(c);
                        }
                        break;
                    case "passhold":
                        result.PassHoldButton = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "style":
                        if (Enum.TryParse(value, true, out ComboOutputStyle style) && Enum.IsDefined(typeof(ComboOutputStyle), style))
                            result.Style = style;
                        break;
                    case "down":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int down))
                            result.PressMs = down;
                        break;
                    case "up":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int up))
                            result.ReleaseMs = up;
                        break;
                    case "rumble":
                        result.Rumble = !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase));
                        break;
                    case "light":
                        result.Lightbar = !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }

            return result;
        }

        public static string ButtonName(DS4Controls c) => c switch
        {
            DS4Controls.Cross => "Cross",
            DS4Controls.Circle => "Circle",
            DS4Controls.Square => "Square",
            DS4Controls.Triangle => "Triangle",
            DS4Controls.L2 => "L2 (full)",
            DS4Controls.R2 => "R2 (full)",
            DS4Controls.DpadUp => "D-pad up",
            DS4Controls.DpadDown => "D-pad down",
            DS4Controls.DpadLeft => "D-pad left",
            DS4Controls.DpadRight => "D-pad right",
            DS4Controls.BLP => "Left back paddle",
            DS4Controls.BRP => "Right back paddle",
            DS4Controls.FnL => "Fn left",
            DS4Controls.FnR => "Fn right",
            _ => c.ToString(),
        };

        public string Describe()
        {
            var names = new List<string>(Outputs.Count);
            foreach (DS4Controls c in Outputs) names.Add(ButtonName(c));
            string style = Style == ComboOutputStyle.Pulse ? "rapid" : "steady";
            return $"Combo mode: hold {ButtonName(HoldButton)} = {string.Join(" + ", names)} ({style})";
        }
    }

    /// <summary>One frame of output from <see cref="ComboHoldModeTracker"/>.</summary>
    public readonly struct ComboHoldModeFrame
    {
        public ComboHoldModeFrame(bool modeOn, bool turnedOn, bool turnedOff, bool suppressHold, bool outputsPressed)
        {
            ModeOn = modeOn;
            TurnedOn = turnedOn;
            TurnedOff = turnedOff;
            SuppressHoldButton = suppressHold;
            OutputsPressed = outputsPressed;
        }

        public bool ModeOn { get; }
        /// <summary>True only on the frame the mode turned on.</summary>
        public bool TurnedOn { get; }
        /// <summary>True only on the frame the mode turned off.</summary>
        public bool TurnedOff { get; }
        /// <summary>The hold button's normal mapping must not see it this frame.</summary>
        public bool SuppressHoldButton { get; }
        /// <summary>All output buttons are pressed this frame.</summary>
        public bool OutputsPressed { get; }
    }

    /// <summary>
    /// Pure, clock-injected state for one Combo Hold Mode action on one
    /// controller. Rapid repeat uses a fixed press/release cycle that starts
    /// pressed the moment the hold button goes down; there is no jitter.
    /// </summary>
    public sealed class ComboHoldModeTracker
    {
        private bool lastModePressed;
        private bool holdActive;
        private long holdStartMs;

        public bool ModeOn { get; private set; }

        public void Reset()
        {
            ModeOn = false;
            lastModePressed = false;
            holdActive = false;
        }

        /// <summary>Whether a pulse cycle that started at <paramref name="startMs"/> is in its pressed part.</summary>
        public static bool PulsePressed(long startMs, long nowMs, int pressMs, int releaseMs)
        {
            pressMs = ComboHoldModeSettings.ClampPulseMs(pressMs);
            releaseMs = ComboHoldModeSettings.ClampPulseMs(releaseMs);
            long elapsed = nowMs - startMs;
            if (elapsed < 0) elapsed = 0;
            return elapsed % (pressMs + releaseMs) < pressMs;
        }

        public ComboHoldModeFrame Update(bool modePressed, bool holdPressed, long nowMs, ComboHoldModeSettings settings)
        {
            bool wasOn = ModeOn;
            if (settings.ModeBehavior == ComboModeBehavior.WhileHeld)
                ModeOn = modePressed;
            else if (modePressed && !lastModePressed)
                ModeOn = !ModeOn;
            lastModePressed = modePressed;

            bool turnedOn = ModeOn && !wasOn;
            bool turnedOff = !ModeOn && wasOn;

            if (!ModeOn)
            {
                holdActive = false;
                return new ComboHoldModeFrame(false, turnedOn, turnedOff, false, false);
            }

            if (!holdPressed)
            {
                holdActive = false;
                return new ComboHoldModeFrame(true, turnedOn, turnedOff, false, false);
            }

            if (!holdActive)
            {
                holdActive = true;
                holdStartMs = nowMs;
            }

            bool outputs = settings.Style == ComboOutputStyle.Steady ||
                PulsePressed(holdStartMs, nowMs, settings.PressMs, settings.ReleaseMs);
            return new ComboHoldModeFrame(true, turnedOn, turnedOff, !settings.PassHoldButton, outputs);
        }
    }

    /// <summary>Rumble pattern player: one buzz for mode on, two short buzzes for mode off.</summary>
    public sealed class ComboRumblePattern
    {
        public const byte STRENGTH = 180;
        private static readonly long[] OnPattern = { 0, 180 };
        private static readonly long[] OffPattern = { 0, 110, 210, 320 };

        private long[] steps;
        private int index;
        private long startMs;

        public bool Active => steps != null;

        public void Start(bool modeOn, long nowMs)
        {
            steps = modeOn ? OnPattern : OffPattern;
            index = 0;
            startMs = nowMs;
        }

        public void Stop() => steps = null;

        /// <summary>Returns null when nothing changes, otherwise whether the motor should be on.</summary>
        public bool? Update(long nowMs)
        {
            if (steps == null) return null;
            bool? result = null;
            while (index < steps.Length && nowMs - startMs >= steps[index])
            {
                result = index % 2 == 0;
                index++;
            }
            if (index >= steps.Length) steps = null;
            return result;
        }
    }
}
