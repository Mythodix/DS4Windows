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
using System.Threading;

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
        // A looping macro whose action has not been processed for this long
        // (profile switched, action disabled/removed) is stopped.
        private const long HOLD_MACRO_STALE_MS = 250;
        private const int HOLD_MACRO_STOP_WAIT_MS = 1000;

        private sealed class HoldMacroEntry
        {
            public readonly HoldMacroRepeatState State = new HoldMacroRepeatState();
            public HoldMacroRunner Runner;
            public volatile int[] Playback = Array.Empty<int>();
            public volatile bool ScanCode;
        }

        // Per controller: macro runner + repeat state for each Macro hold action.
        private static readonly Dictionary<SpecialAction, HoldMacroEntry>[] holdMacros = CreateHoldMacros();

        private static Dictionary<SpecialAction, HoldMacroEntry>[] CreateHoldMacros()
        {
            var result = new Dictionary<SpecialAction, HoldMacroEntry>[Global.MAX_DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Dictionary<SpecialAction, HoldMacroEntry>(ReferenceEqualityComparer.Instance);
            return result;
        }

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
            UpdateComboRumble(device, ctrl);

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

                if (settings.Kind == HoldOutputKind.Macro)
                {
                    // Handled below by the macro runner.
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

            if (settings.Kind == HoldOutputKind.Macro)
            {
                UpdateHoldMacro(device, action, settings, frame);
                return;
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

        private static void UpdateHoldMacro(int device, SpecialAction action, HoldToActivateSettings settings,
            HoldToActivateFrame frame)
        {
            Dictionary<SpecialAction, HoldMacroEntry> macros = holdMacros[device];
            HoldMacroEntry entry;
            lock (macros)
            {
                if (!macros.TryGetValue(action, out entry))
                {
                    if (settings.Macro.Count == 0 && !frame.HoldFired)
                        return;
                    entry = new HoldMacroEntry();
                    HoldMacroEntry captured = entry;
                    entry.Runner = new HoldMacroRunner(token =>
                        PlayHoldMacroPass(device, captured.Playback, captured.ScanCode, token));
                    macros[action] = entry;
                }
                entry.Runner.LastSeenMs = HoldNowMs();
            }

            HoldMacroCommand command = entry.State.Update(settings.Repeat, frame);
            if (command == HoldMacroCommand.PlayOnce || command == HoldMacroCommand.StartLoop)
            {
                if (settings.Macro.Count == 0)
                {
                    entry.State.Reset();
                    return;
                }
                entry.Playback = HoldToActivateSettings.BuildPlaybackMacro(settings.Macro);
                entry.ScanCode = settings.ScanCode;
            }
            entry.Runner.Apply(command);
        }

        /// <summary>
        /// One pass of a hold macro. Waits are cancellable, and every key,
        /// mouse button and controller button the pass pressed is released
        /// before returning, also when it is cut short.
        /// </summary>
        private static void PlayHoldMacroPass(int device, int[] macro, bool scanCode, CancellationToken token)
        {
            if (macro == null || macro.Length == 0) return;
            DS4KeyType keyType = scanCode ? DS4KeyType.ScanCode : DS4KeyType.None;
            bool[] keydown = new bool[512];
            RegisterMacroMouseOwnerInEpoch(device, keydown, CaptureMacroMouseEpoch(device));
            try
            {
                for (int i = 0; i < macro.Length; i++)
                {
                    if (token.IsCancellationRequested) break;
                    int code = macro[i];
                    if (PlayMacroCodeValue(device, macroControl, keyType, code, keydown))
                    {
                        if (token.WaitHandle.WaitOne(Math.Max(0, code - 300))) break;
                    }
                }
            }
            finally
            {
                for (int i = 0; i < keydown.Length; i++)
                {
                    if (keydown[i])
                        PlayMacroCodeValue(device, macroControl, keyType, i, keydown);
                }
                DS4LightBar.forcedFlash[device] = 0;
                DS4LightBar.forcelight[device] = false;
            }
        }

        /// <summary>Stop looping hold macros whose action is no longer being processed (profile change etc.).</summary>
        private static void StopStaleHoldMacros(int device)
        {
            Dictionary<SpecialAction, HoldMacroEntry> macros = holdMacros[device];
            lock (macros)
            {
                if (macros.Count == 0) return;
                long now = HoldNowMs();
                List<SpecialAction> staleActions = null;
                foreach (KeyValuePair<SpecialAction, HoldMacroEntry> pair in macros)
                {
                    if (now - pair.Value.Runner.LastSeenMs > HOLD_MACRO_STALE_MS)
                        (staleActions ??= new List<SpecialAction>()).Add(pair.Key);
                }

                if (staleActions == null) return;
                foreach (SpecialAction stale in staleActions)
                {
                    HoldMacroEntry entry = macros[stale];
                    entry.State.Reset();
                    entry.Runner.StopNow();
                    macros.Remove(stale);
                }
            }
        }

        /// <summary>
        /// Stop every hold macro on this controller and wait (bounded) for
        /// their held outputs to be released. Called on disconnect/stop.
        /// </summary>
        internal static void StopAllHoldMacros(int device)
        {
            if (device < 0 || device >= holdMacros.Length) return;
            ResetComboHoldModes(device);
            Dictionary<SpecialAction, HoldMacroEntry> macros = holdMacros[device];
            List<HoldMacroEntry> entries;
            lock (macros)
            {
                if (macros.Count == 0) return;
                entries = new List<HoldMacroEntry>(macros.Values);
                macros.Clear();
            }

            foreach (HoldMacroEntry entry in entries)
            {
                entry.State.Reset();
                entry.Runner.StopNow();
            }
            foreach (HoldMacroEntry entry in entries)
                entry.Runner.StopAndWait(HOLD_MACRO_STOP_WAIT_MS);

        }

        /// <summary>Press any controller buttons requested by holds, after all normal mappings ran.</summary>
        private static void ApplyHoldToActivateOutputs(int device, DS4StateFieldMapping outputfieldMapping)
        {
            StopStaleHoldMacros(device);
            StopStaleComboModes(device);
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
