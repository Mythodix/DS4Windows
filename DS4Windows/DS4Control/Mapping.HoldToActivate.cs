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
using System.Diagnostics;

namespace DS4Windows
{
    // Runtime for the accessibility "Hold to Activate" special action.
    // A quick press keeps the trigger's normal mapping; holding it for the
    // configured time runs the hold output instead. Timing is fixed.
    public partial class Mapping
    {
        private const long HOLD_RUMBLE_MS = 150;
        private const byte HOLD_RUMBLE_LIGHT = 180;

        private static readonly Dictionary<SpecialAction, HoldToActivateTracker>[] holdTrackers =
            CreateHoldTrackers();
        // Virtual controller outputs a hold wants pressed this frame.
        private static readonly List<DS4Controls>[] holdForcedOutputs = CreateHoldForcedOutputs();
        // After a hold switches profiles, keep the trigger quiet until it is
        // released so the new profile's mapping of the same button never fires.
        private static readonly DS4Controls[] holdSuppressUntilRelease =
            new DS4Controls[Global.MAX_DS4_CONTROLLER_COUNT];
        private static readonly long[] holdRumbleOffAtMs = new long[Global.MAX_DS4_CONTROLLER_COUNT];

        private static Dictionary<SpecialAction, HoldToActivateTracker>[] CreateHoldTrackers()
        {
            var result = new Dictionary<SpecialAction, HoldToActivateTracker>[Global.MAX_DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Dictionary<SpecialAction, HoldToActivateTracker>(ReferenceEqualityComparer.Instance);
            return result;
        }

        private static List<DS4Controls>[] CreateHoldForcedOutputs()
        {
            var result = new List<DS4Controls>[Global.MAX_DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++)
                result[i] = new List<DS4Controls>(4);
            return result;
        }

        private static long HoldNowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        /// <summary>A control can be a hold trigger when it is an on/off button or an analog trigger.</summary>
        public static bool IsHoldToActivateTriggerSupported(DS4Controls control)
        {
            int index = (int)control;
            if (control == DS4Controls.None || index <= 0 || index >= DS4StateFieldMapping.mappedType.Length)
                return false;
            return DS4StateFieldMapping.mappedType[index] is DS4StateFieldMapping.ControlType.Button or
                DS4StateFieldMapping.ControlType.Trigger or DS4StateFieldMapping.ControlType.Touch;
        }

        private static void SetHoldSource(DS4Controls control, bool pressed, DS4State cState,
            DS4StateFieldMapping fieldMapping, DS4State mappedState, DS4StateFieldMapping outputfieldMapping)
        {
            if (!pressed)
            {
                // Same neutralization the Mode Shift activation uses: the source
                // so remaps release cleanly, the destination so the default
                // pass-through cannot leak the press to the game.
                SuppressSwitch2ModeShiftActivation(control, cState, fieldMapping, mappedState, outputfieldMapping);
                return;
            }

            int index = (int)control;
            switch (DS4StateFieldMapping.mappedType[index])
            {
                case DS4StateFieldMapping.ControlType.Trigger:
                    fieldMapping.triggers[index] = 255;
                    outputfieldMapping.triggers[index] = 255;
                    break;
                case DS4StateFieldMapping.ControlType.Button:
                case DS4StateFieldMapping.ControlType.Touch:
                    fieldMapping.buttons[index] = true;
                    outputfieldMapping.buttons[index] = true;
                    break;
            }
        }

        /// <summary>Per-frame housekeeping that must run even when the profile has no actions.</summary>
        private static void BeginHoldToActivateFrame(int device, DS4State cState, DS4StateExposed eState,
            Mouse tp, DS4StateFieldMapping fieldMapping, DS4State mappedState,
            DS4StateFieldMapping outputfieldMapping, ControlService ctrl)
        {
            holdForcedOutputs[device].Clear();

            long rumbleOff = holdRumbleOffAtMs[device];
            if (rumbleOff != 0 && HoldNowMs() >= rumbleOff)
            {
                holdRumbleOffAtMs[device] = 0;
                ctrl?.setRumble(0, 0, device);
            }

            DS4Controls quiet = holdSuppressUntilRelease[device];
            if (quiet != DS4Controls.None)
            {
                if (getBoolSpecialActionMapping(device, quiet, cState, eState, tp, fieldMapping))
                    SetHoldSource(quiet, false, cState, fieldMapping, mappedState, outputfieldMapping);
                else
                    holdSuppressUntilRelease[device] = DS4Controls.None;
            }
        }

        private static void ProcessHoldToActivateAction(int device, SpecialAction action, DS4State cState,
            DS4State mappedState, DS4StateExposed eState, Mouse tp, ControlService ctrl,
            DS4StateFieldMapping fieldMapping, DS4StateFieldMapping outputfieldMapping)
        {
            HoldToActivateSettings settings = action.holdSettings;
            if (settings == null || action.trigger.Count != 1)
                return;

            DS4Controls trigger = action.trigger[0];
            if (!IsHoldToActivateTriggerSupported(trigger))
                return;

            Dictionary<SpecialAction, HoldToActivateTracker> trackers = holdTrackers[device];
            if (!trackers.TryGetValue(action, out HoldToActivateTracker tracker))
            {
                // Profile reloads replace action objects; drop stale trackers.
                if (trackers.Count > 32) trackers.Clear();
                trackers[action] = tracker = new HoldToActivateTracker();
            }

            bool pressed = getBoolSpecialActionMapping(device, trigger, cState, eState, tp, fieldMapping);
            bool toggleOutput = settings.UsesMode && settings.Mode == HoldOutputMode.Toggle;
            HoldToActivateFrame frame = tracker.Update(pressed, HoldNowMs(), settings.HoldMilliseconds,
                settings.InstantTap, toggleOutput);

            // Only touch the normal mapping when the tracker disagrees with the
            // physical state (held back, or a delayed tap being replayed).
            if (frame.SourcePressed != pressed)
                SetHoldSource(trigger, frame.SourcePressed, cState, fieldMapping, mappedState, outputfieldMapping);

            if (frame.HoldFired)
            {
                if (settings.Rumble && ctrl != null)
                {
                    ctrl.setRumble(0, HOLD_RUMBLE_LIGHT, device);
                    holdRumbleOffAtMs[device] = HoldNowMs() + HOLD_RUMBLE_MS;
                }

                if (settings.Kind == HoldOutputKind.Macro && settings.Macro.Count > 0)
                {
                    PlayMacro(device, macroControl, string.Join("/", settings.Macro), null, null,
                        DS4Controls.None, settings.ScanCode ? DS4KeyType.ScanCode : DS4KeyType.None);
                }
                else if (settings.Kind == HoldOutputKind.Profile && !string.IsNullOrWhiteSpace(settings.Profile))
                {
                    holdSuppressUntilRelease[device] = trigger;
                    DS4Device d = ctrl?.DS4Controllers[device];
                    string prolog = string.Format(DS4WinWPF.Properties.Resources.UsingProfile,
                        (device + 1).ToString(), settings.Profile, d != null ? $"{d.Battery}" : "");
                    AppLogger.LogToGui(prolog, false);
                    if (Global.ProfileChangedNotification) AppLogger.LogToTray(prolog);
                    RequestProfileSwitch(device, settings.Profile, true, true, ctrl);
                }
            }

            if (!settings.UsesMode)
                return;

            bool outputActive = toggleOutput ? tracker.Latched : frame.HoldHeld;
            if (!outputActive)
                return;

            if (settings.Kind == HoldOutputKind.Key && settings.KeyCode > 0)
            {
                ushort key = (ushort)settings.KeyCode;
                SyntheticState.KeyPresses kp;
                if (!deviceState[device].keyPresses.TryGetValue(key, out kp))
                {
                    deviceState[device].keyPresses[key] = kp = new SyntheticState.KeyPresses();
                    deviceState[device].nativeKeyAlias[key] = (ushort)Global.outputKBMMapping.GetRealEventKey(key);
                }

                if (settings.ScanCode)
                    kp.current.scanCodeCount++;
                else
                    kp.current.vkCount++;
            }
            else if (settings.Kind == HoldOutputKind.Button &&
                HoldToActivateSettings.IsSupportedButton(settings.Button))
            {
                DS4Controls output = Global.reverseX360ButtonMapping[(int)settings.Button];
                if (output != DS4Controls.None)
                    holdForcedOutputs[device].Add(output);
            }
        }

        /// <summary>Press any controller buttons requested by holds, after all normal mappings ran.</summary>
        private static void ApplyHoldToActivateOutputs(int device, DS4StateFieldMapping outputfieldMapping)
        {
            List<DS4Controls> outputs = holdForcedOutputs[device];
            for (int i = 0, len = outputs.Count; i < len; i++)
            {
                int index = (int)outputs[i];
                switch (DS4StateFieldMapping.mappedType[index])
                {
                    case DS4StateFieldMapping.ControlType.Trigger:
                        outputfieldMapping.triggers[index] = 255;
                        break;
                    case DS4StateFieldMapping.ControlType.Button:
                        outputfieldMapping.buttons[index] = true;
                        break;
                }
            }
        }
    }
}
