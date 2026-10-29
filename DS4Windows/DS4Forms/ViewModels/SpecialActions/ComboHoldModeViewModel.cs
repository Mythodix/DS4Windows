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
using System.ComponentModel;
using System.Linq;
using System.Windows;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.Util;

namespace DS4WinWPF.DS4Forms.ViewModels.SpecialActions
{
    /// <summary>Editor state for the accessibility "Combo Hold Mode" special action.</summary>
    public class ComboHoldModeViewModel : NotifyDataErrorBase
    {
        public sealed class ButtonChoice
        {
            public ButtonChoice(DS4Controls control, string label)
            {
                Control = control;
                Label = label;
            }

            public DS4Controls Control { get; }
            public string Label { get; }
        }

        public sealed class OutputChoice : INotifyPropertyChanged
        {
            private bool isChecked;
            private readonly Action changed;

            public OutputChoice(DS4Controls control, string label, Action changed)
            {
                Control = control;
                Label = label;
                this.changed = changed;
            }

            public DS4Controls Control { get; }
            public string Label { get; }

            public bool IsChecked
            {
                get => isChecked;
                set
                {
                    if (isChecked == value) return;
                    isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                    changed?.Invoke();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public static string LabelFor(DS4Controls c) => c switch
        {
            DS4Controls.Cross => "Cross (X)",
            DS4Controls.Circle => "Circle",
            DS4Controls.Square => "Square",
            DS4Controls.Triangle => "Triangle",
            DS4Controls.L1 => "L1",
            DS4Controls.R1 => "R1",
            DS4Controls.L2 => "L2 (full press)",
            DS4Controls.R2 => "R2 (full press)",
            DS4Controls.L3 => "L3 (left stick click)",
            DS4Controls.R3 => "R3 (right stick click)",
            DS4Controls.DpadUp => "D-pad up",
            DS4Controls.DpadDown => "D-pad down",
            DS4Controls.DpadLeft => "D-pad left",
            DS4Controls.DpadRight => "D-pad right",
            DS4Controls.Options => "Options",
            DS4Controls.Share => "Share / Create",
            DS4Controls.PS => "PS",
            DS4Controls.Mute => "Mute",
            DS4Controls.BLP => "Left back paddle (Edge)",
            DS4Controls.BRP => "Right back paddle (Edge)",
            DS4Controls.FnL => "Fn left (Edge)",
            DS4Controls.FnR => "Fn right (Edge)",
            _ => c.ToString(),
        };

        public static IReadOnlyList<ButtonChoice> HoldChoices { get; } =
            ComboHoldModeSettings.SupportedHoldButtons.Select(c => new ButtonChoice(c, LabelFor(c))).ToArray();

        private readonly List<OutputChoice> outputs;
        private int modeIndex;
        private int holdIndex;
        private int styleIndex;
        private int pressMs = ComboHoldModeSettings.DEFAULT_PULSE_MS;
        private int releaseMs = ComboHoldModeSettings.DEFAULT_PULSE_MS;
        private bool passHold;
        private bool rumble = true;
        private bool lightbar = true;
        private string validationMessage = string.Empty;
        private bool loading;

        public ComboHoldModeViewModel()
        {
            outputs = ComboHoldModeSettings.SupportedOutputs
                .Select(c => new OutputChoice(c, LabelFor(c), RaiseSummary)).ToList();
            LoadSettings(ComboHoldModeSettings.CreateDefault());
        }

        public IReadOnlyList<ButtonChoice> HoldButtons => HoldChoices;
        public IReadOnlyList<OutputChoice> Outputs => outputs;
        public int MinPulseMs => ComboHoldModeSettings.MIN_PULSE_MS;
        public int MaxPulseMs => ComboHoldModeSettings.MAX_PULSE_MS;

        /// <summary>0 = toggle with each press, 1 = only while the mode button is held.</summary>
        public int ModeIndex
        {
            get => modeIndex;
            set
            {
                if (modeIndex == value || value < 0 || value > 1) return;
                modeIndex = value;
                ModeIndexChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler ModeIndexChanged;

        public int HoldIndex
        {
            get => holdIndex;
            set
            {
                if (holdIndex == value) return;
                holdIndex = value;
                HoldIndexChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler HoldIndexChanged;

        /// <summary>0 = rapid repeat (press and release), 1 = hold steady.</summary>
        public int StyleIndex
        {
            get => styleIndex;
            set
            {
                if (styleIndex == value || value < 0 || value > 1) return;
                styleIndex = value;
                StyleIndexChanged?.Invoke(this, EventArgs.Empty);
                PulsePanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler StyleIndexChanged;

        public Visibility PulsePanelVisibility => styleIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler PulsePanelVisibilityChanged;

        public int PressMs
        {
            get => pressMs;
            set
            {
                int v = ComboHoldModeSettings.ClampPulseMs(value);
                if (v == pressMs) return;
                pressMs = v;
                PressMsChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler PressMsChanged;

        public int ReleaseMs
        {
            get => releaseMs;
            set
            {
                int v = ComboHoldModeSettings.ClampPulseMs(value);
                if (v == releaseMs) return;
                releaseMs = v;
                ReleaseMsChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler ReleaseMsChanged;

        public bool PassHold
        {
            get => passHold;
            set { passHold = value; PassHoldChanged?.Invoke(this, EventArgs.Empty); RaiseSummary(); }
        }
        public event EventHandler PassHoldChanged;

        public bool Rumble
        {
            get => rumble;
            set { rumble = value; RumbleChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler RumbleChanged;

        public bool Lightbar
        {
            get => lightbar;
            set { lightbar = value; LightbarChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler LightbarChanged;

        public string ValidationMessage
        {
            get => validationMessage;
            private set
            {
                validationMessage = value ?? string.Empty;
                ValidationMessageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ValidationMessageChanged;

        private string HoldLabel => holdIndex >= 0 && holdIndex < HoldChoices.Count ? HoldChoices[holdIndex].Label : "the hold button";

        public string Summary
        {
            get
            {
                List<string> picked = outputs.Where(o => o.IsChecked).Select(o => o.Label).ToList();
                string what = picked.Count > 0 ? string.Join(", ", picked) : "(no buttons ticked yet)";
                double hz = 1000.0 / (pressMs + releaseMs);
                string how = styleIndex == 0
                    ? $"presses and releases {what} together over and over ({pressMs} ms down, {releaseMs} ms up, about {hz.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)} times a second)"
                    : $"holds {what} down together";
                string mode = modeIndex == 0
                    ? "Press the mode button once to turn combo mode on, press it again to turn it off."
                    : "Combo mode is on only while you hold the mode button.";
                string pass = passHold ? $" {HoldLabel} is also sent." : $" {HoldLabel} itself is not sent while combo mode is on.";
                return $"{mode} While it is on, holding {HoldLabel} {how}; letting go releases them all.{pass} When combo mode is off, {HoldLabel} works normally.";
            }
        }
        public event EventHandler SummaryChanged;

        private void RaiseSummary()
        {
            if (!loading) SummaryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LoadAction(SpecialAction action)
        {
            ComboHoldModeSettings settings = action.comboSettings ?? ComboHoldModeSettings.Parse(action.details);
            LoadSettings(settings);
        }

        public void LoadSettings(ComboHoldModeSettings settings)
        {
            loading = true;
            try
            {
                modeIndex = settings.ModeBehavior == ComboModeBehavior.WhileHeld ? 1 : 0;
                holdIndex = -1;
                for (int i = 0; i < HoldChoices.Count; i++)
                {
                    if (HoldChoices[i].Control == settings.HoldButton) { holdIndex = i; break; }
                }
                styleIndex = settings.Style == ComboOutputStyle.Steady ? 1 : 0;
                pressMs = settings.PressMs;
                releaseMs = settings.ReleaseMs;
                passHold = settings.PassHoldButton;
                rumble = settings.Rumble;
                lightbar = settings.Lightbar;
                foreach (OutputChoice o in outputs)
                    o.IsChecked = settings.Outputs.Contains(o.Control);
            }
            finally
            {
                loading = false;
            }
            RaiseSummary();
        }

        public ComboHoldModeSettings BuildSettings()
        {
            var settings = new ComboHoldModeSettings
            {
                ModeBehavior = modeIndex == 1 ? ComboModeBehavior.WhileHeld : ComboModeBehavior.Toggle,
                HoldButton = holdIndex >= 0 && holdIndex < HoldChoices.Count ? HoldChoices[holdIndex].Control : DS4Controls.None,
                Style = styleIndex == 1 ? ComboOutputStyle.Steady : ComboOutputStyle.Pulse,
                PressMs = pressMs,
                ReleaseMs = releaseMs,
                PassHoldButton = passHold,
                Rumble = rumble,
                Lightbar = lightbar,
            };
            settings.Outputs.AddRange(outputs.Where(o => o.IsChecked).Select(o => o.Control));
            return settings;
        }

        /// <summary>Returns null when the action can be saved, otherwise a plain error for the user.</summary>
        public string Validate(SpecialAction action)
        {
            string[] triggers = string.IsNullOrEmpty(action?.controls)
                ? Array.Empty<string>()
                : action.controls.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string error = null;
            ComboHoldModeSettings settings = BuildSettings();
            if (triggers.Length != 1)
            {
                error = "Combo Hold Mode needs exactly one mode button. Check one button in the list on the left, for example a back paddle.";
            }
            else
            {
                DS4Controls control = SpecialActionTriggerCatalog.Entries
                    .FirstOrDefault(entry => entry.Tag == triggers[0])?.Control ?? DS4Controls.None;
                if (!Mapping.IsHoldToActivateTriggerSupported(control))
                    error = "That mode button can't be pressed. Pick a button, paddle or trigger instead.";
                else if (control == settings.HoldButton)
                    error = "The mode button and the hold button must be different buttons.";
            }

            error ??= settings.Validate();
            ValidationMessage = error;
            return error;
        }

        public void SaveAction(SpecialAction action, bool edit = false)
        {
            Global.SaveAction(action.name, action.controls, 11, BuildSettings().Serialize(), edit);
        }

        public override bool IsValid(SpecialAction action)
        {
            ClearOldErrors();
            string error = Validate(action);
            if (error != null)
            {
                errors["ValidationMessage"] = new List<string> { error };
                RaiseErrorsChanged("ValidationMessage");
                return false;
            }

            return true;
        }

        public override void ClearOldErrors()
        {
            if (errors.Count > 0)
            {
                errors.Clear();
                RaiseErrorsChanged("ValidationMessage");
            }
        }
    }
}
