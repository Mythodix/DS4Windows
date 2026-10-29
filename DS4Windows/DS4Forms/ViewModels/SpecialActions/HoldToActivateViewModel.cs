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
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.Util;

namespace DS4WinWPF.DS4Forms.ViewModels.SpecialActions
{
    /// <summary>
    /// Editor state for the accessibility "Hold to Activate" special action.
    /// </summary>
    public class HoldToActivateViewModel : NotifyDataErrorBase
    {
        public sealed class ButtonChoice
        {
            public ButtonChoice(X360Controls control, string label)
            {
                Control = control;
                Label = label;
            }

            public X360Controls Control { get; }
            public string Label { get; }
        }

        public static IReadOnlyList<ButtonChoice> ButtonChoices { get; } = new[]
        {
            new ButtonChoice(X360Controls.A, "A / Cross"),
            new ButtonChoice(X360Controls.B, "B / Circle"),
            new ButtonChoice(X360Controls.X, "X / Square"),
            new ButtonChoice(X360Controls.Y, "Y / Triangle"),
            new ButtonChoice(X360Controls.LB, "LB / L1"),
            new ButtonChoice(X360Controls.RB, "RB / R1"),
            new ButtonChoice(X360Controls.LT, "LT / L2"),
            new ButtonChoice(X360Controls.RT, "RT / R2"),
            new ButtonChoice(X360Controls.LS, "Left stick click (L3)"),
            new ButtonChoice(X360Controls.RS, "Right stick click (R3)"),
            new ButtonChoice(X360Controls.Back, "Back / Share"),
            new ButtonChoice(X360Controls.Start, "Start / Options"),
            new ButtonChoice(X360Controls.Guide, "Guide / PS"),
            new ButtonChoice(X360Controls.DpadUp, "D-pad up"),
            new ButtonChoice(X360Controls.DpadDown, "D-pad down"),
            new ButtonChoice(X360Controls.DpadLeft, "D-pad left"),
            new ButtonChoice(X360Controls.DpadRight, "D-pad right"),
        };

        private readonly ProfileList profileList;
        private double holdSeconds = HoldToActivateSettings.DEFAULT_HOLD_SECONDS;
        private int outputIndex;
        private int modeIndex;
        private int repeatIndex;
        private int buttonIndex = -1;
        private int profileIndex = -1;
        private int keyValue;
        private DS4KeyType keyType;
        private readonly List<int> macro = new List<int>();
        private bool rumble = true;
        private bool instantTap;
        private string validationMessage = string.Empty;

        public HoldToActivateViewModel(ProfileList profileList)
        {
            this.profileList = profileList;
        }

        public ProfileList ProfileList => profileList;
        public IReadOnlyList<ButtonChoice> Buttons => ButtonChoices;
        public double MinHoldSeconds => HoldToActivateSettings.MIN_HOLD_SECONDS;
        public double MaxHoldSeconds => HoldToActivateSettings.MAX_HOLD_SECONDS;

        public decimal HoldSeconds
        {
            get => (decimal)holdSeconds;
            set
            {
                double clamped = HoldToActivateSettings.ClampHoldSeconds((double)value);
                if (clamped == holdSeconds) return;
                holdSeconds = clamped;
                HoldSecondsChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler HoldSecondsChanged;

        /// <summary>0 = key, 1 = controller button, 2 = macro, 3 = profile.</summary>
        public int OutputIndex
        {
            get => outputIndex;
            set
            {
                if (outputIndex == value || value < 0 || value > 3) return;
                outputIndex = value;
                OutputIndexChanged?.Invoke(this, EventArgs.Empty);
                KeyPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                ButtonPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                MacroPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                ProfilePanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                ModePanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                RepeatPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler OutputIndexChanged;

        public HoldOutputKind Kind => (HoldOutputKind)outputIndex;

        public Visibility KeyPanelVisibility => Kind == HoldOutputKind.Key ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler KeyPanelVisibilityChanged;
        public Visibility ButtonPanelVisibility => Kind == HoldOutputKind.Button ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler ButtonPanelVisibilityChanged;
        public Visibility MacroPanelVisibility => Kind == HoldOutputKind.Macro ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler MacroPanelVisibilityChanged;
        public Visibility ProfilePanelVisibility => Kind == HoldOutputKind.Profile ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler ProfilePanelVisibilityChanged;
        public Visibility ModePanelVisibility =>
            Kind == HoldOutputKind.Key || Kind == HoldOutputKind.Button ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler ModePanelVisibilityChanged;

        public Visibility RepeatPanelVisibility => Kind == HoldOutputKind.Macro ? Visibility.Visible : Visibility.Collapsed;
        public event EventHandler RepeatPanelVisibilityChanged;

        /// <summary>Macro output only: 0 = play once, 1 = repeat while held, 2 = toggle repeat on/off.</summary>
        public int RepeatIndex
        {
            get => repeatIndex;
            set
            {
                if (repeatIndex == value || value < 0 || value > 2) return;
                repeatIndex = value;
                RepeatIndexChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler RepeatIndexChanged;

        public HoldMacroRepeat Repeat => (HoldMacroRepeat)repeatIndex;

        /// <summary>0 = keep pressed while held, 1 = toggle on/off.</summary>
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

        public int ButtonIndex
        {
            get => buttonIndex;
            set
            {
                if (buttonIndex == value) return;
                buttonIndex = value;
                ButtonIndexChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler ButtonIndexChanged;

        public int ProfileIndex
        {
            get => profileIndex;
            set
            {
                if (profileIndex == value) return;
                profileIndex = value;
                ProfileIndexChanged?.Invoke(this, EventArgs.Empty);
                RaiseSummary();
            }
        }
        public event EventHandler ProfileIndexChanged;

        public bool Rumble
        {
            get => rumble;
            set { rumble = value; RumbleChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler RumbleChanged;

        public bool InstantTap
        {
            get => instantTap;
            set { instantTap = value; InstantTapChanged?.Invoke(this, EventArgs.Empty); RaiseSummary(); }
        }
        public event EventHandler InstantTapChanged;

        public int KeyValue => keyValue;
        public DS4KeyType KeyType => keyType;
        public List<int> Macro => macro;

        public string KeyText
        {
            get
            {
                if (keyValue <= 0) return "Select a key";
                return KeyInterop.KeyFromVirtualKey(keyValue).ToString() +
                    (keyType.HasFlag(DS4KeyType.ScanCode) ? " (SC)" : "");
            }
        }
        public event EventHandler KeyTextChanged;

        public string MacroText => macro.Count > 0 ? $"Macro recorded ({macro.Count} steps) - click to re-record" : "Record a macro";
        public event EventHandler MacroTextChanged;

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

        /// <summary>Plain-language description of what the configured button will do.</summary>
        public string Summary
        {
            get
            {
                string secs = holdSeconds.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
                string tap = instantTap
                    ? "A press works normally right away and is released when the hold time is reached."
                    : "A quick press does the button's normal job when you let go. While you hold, the game will not show the button as pressed; that is expected, because the press is held back until DS4Windows knows it is a tap.";
                string what = Kind switch
                {
                    HoldOutputKind.Key => keyValue > 0 ? $"presses {KeyText}" : "presses the key you pick",
                    HoldOutputKind.Button => buttonIndex >= 0 && buttonIndex < ButtonChoices.Count
                        ? $"presses {ButtonChoices[buttonIndex].Label}" : "presses the controller button you pick",
                    HoldOutputKind.Macro => Repeat switch
                    {
                        HoldMacroRepeat.WhileHeld => "plays your macro, then plays it again and again for as long as you keep holding (it finishes the current run when you let go)",
                        HoldMacroRepeat.Toggle => "starts playing your macro over and over, even after you let go; the next long hold stops it (quick taps still work normally)",
                        _ => "plays your macro once",
                    },
                    _ => SelectedProfileName() is string name ? $"switches to profile \"{name}\"" : "switches to the profile you pick",
                };
                if (Kind is HoldOutputKind.Key or HoldOutputKind.Button)
                    what += modeIndex == 1 ? " and keeps it on until your next long hold" : " for as long as you keep holding";
                return $"{tap} Holding for {secs} seconds {what} instead.";
            }
        }
        public event EventHandler SummaryChanged;

        private void RaiseSummary() => SummaryChanged?.Invoke(this, EventArgs.Empty);

        private string SelectedProfileName()
        {
            if (profileList == null || profileIndex < 0 || profileIndex >= profileList.ProfileListCol.Count)
                return null;
            return profileList.ProfileListCol[profileIndex].Name;
        }

        public void LoadAction(SpecialAction action)
        {
            HoldToActivateSettings settings = action.holdSettings ?? HoldToActivateSettings.Parse(action.details);
            LoadSettings(settings);
        }

        public void LoadSettings(HoldToActivateSettings settings)
        {
            holdSeconds = settings.HoldSeconds;
            outputIndex = (int)settings.Kind;
            modeIndex = settings.Mode == HoldOutputMode.Toggle ? 1 : 0;
            repeatIndex = (int)settings.Repeat;
            keyValue = settings.KeyCode;
            keyType = settings.ScanCode ? DS4KeyType.ScanCode : DS4KeyType.None;
            buttonIndex = -1;
            for (int i = 0; i < ButtonChoices.Count; i++)
            {
                if (ButtonChoices[i].Control == settings.Button)
                {
                    buttonIndex = i;
                    break;
                }
            }

            macro.Clear();
            macro.AddRange(settings.Macro);
            profileIndex = -1;
            if (profileList != null && !string.IsNullOrEmpty(settings.Profile))
            {
                ProfileEntity item = profileList.ProfileListCol.FirstOrDefault(x => x.Name == settings.Profile);
                if (item != null) profileIndex = profileList.ProfileListCol.IndexOf(item);
            }

            rumble = settings.Rumble;
            instantTap = settings.InstantTap;
        }

        public HoldToActivateSettings BuildSettings()
        {
            var settings = new HoldToActivateSettings
            {
                HoldSeconds = holdSeconds,
                Kind = Kind,
                Mode = modeIndex == 1 ? HoldOutputMode.Toggle : HoldOutputMode.WhileHeld,
                Repeat = Repeat,
                KeyCode = keyValue,
                ScanCode = keyType.HasFlag(DS4KeyType.ScanCode),
                Button = buttonIndex >= 0 && buttonIndex < ButtonChoices.Count
                    ? ButtonChoices[buttonIndex].Control : X360Controls.None,
                Profile = SelectedProfileName() ?? string.Empty,
                Rumble = rumble,
                InstantTap = instantTap,
            };
            settings.Macro.AddRange(macro);
            return settings;
        }

        public DS4ControlSettings PrepareKeySettings()
        {
            DS4ControlSettings settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionKey = keyValue;
            settings.keyType = keyType;
            settings.actionType = DS4ControlSettings.ActionType.Key;
            return settings;
        }

        public void ReadKeySettings(DS4ControlSettings settings)
        {
            if (settings.actionType == DS4ControlSettings.ActionType.Key)
            {
                keyValue = Convert.ToInt32(settings.action.actionKey);
                // A hold presses the key while held or latches it; the
                // binding window's own toggle flag does not apply here.
                keyType = settings.keyType & DS4KeyType.ScanCode;
            }

            KeyTextChanged?.Invoke(this, EventArgs.Empty);
            RaiseSummary();
        }

        public DS4ControlSettings PrepareMacroSettings()
        {
            DS4ControlSettings settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionMacro = macro.ToArray();
            settings.actionType = DS4ControlSettings.ActionType.Macro;
            settings.keyType = DS4KeyType.Macro;
            return settings;
        }

        public void ReadMacroSettings(DS4ControlSettings settings)
        {
            macro.Clear();
            if (settings.action.actionMacro is int[] recorded)
                macro.AddRange(recorded);
            MacroTextChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Returns null when the action can be saved, otherwise a plain error for the user.</summary>
        public string Validate(SpecialAction action)
        {
            string[] triggers = string.IsNullOrEmpty(action?.controls)
                ? Array.Empty<string>()
                : action.controls.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string error = null;
            if (triggers.Length != 1)
            {
                error = "Hold to Activate needs exactly one trigger button. Check one button in the list on the left.";
            }
            else
            {
                DS4Controls control = SpecialActionTriggerCatalog.Entries
                    .FirstOrDefault(entry => entry.Tag == triggers[0])?.Control ?? DS4Controls.None;
                if (!Mapping.IsHoldToActivateTriggerSupported(control))
                    error = "That trigger can't be held. Pick a button, trigger, paddle or touchpad press instead.";
            }

            error ??= BuildSettings().Validate();
            ValidationMessage = error;
            return error;
        }

        public void SaveAction(SpecialAction action, bool edit = false)
        {
            Global.SaveAction(action.name, action.controls, 10, BuildSettings().Serialize(), edit);
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
