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


using System.Collections.Generic;
using static DS4Windows.Global;

namespace DS4Windows
{
    // Runtime for the accessibility "Combo Hold Mode" special action. The
    // action's trigger (for example a DualSense Edge back paddle) turns a
    // mode on/off; while it is on, holding the hold button presses all output
    // buttons together (rapid repeat or steady) until the hold button is
    // released. Outputs are added after normal mappings, like Hold to Activate.
    public partial class Mapping
    {
        private const long COMBO_MODE_STALE_MS = 250;
        private static readonly DS4Color COMBO_MODE_LIGHT = new DS4Color(255, 0, 0);

        private sealed class ComboModeEntry
        {
            public readonly ComboHoldModeTracker Tracker = new ComboHoldModeTracker();
            public long LastSeenMs;
            public bool LightOn;
        }

        private static readonly Dictionary<SpecialAction, ComboModeEntry>[] comboModes = CreateComboModes();
        private static readonly ComboRumblePattern[] comboRumble = CreateComboRumble();

        private static Dictionary<SpecialAction, ComboModeEntry>[] CreateComboModes()
        {
            var result = new Dictionary<SpecialAction, ComboModeEntry>[Global.MAX_DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Dictionary<SpecialAction, ComboModeEntry>(ReferenceEqualityComparer.Instance);
            return result;
        }

        private static ComboRumblePattern[] CreateComboRumble()
        {
            var result = new ComboRumblePattern[Global.MAX_DS4_CONTROLLER_COUNT];
            for (int i = 0; i < result.Length; i++)
                result[i] = new ComboRumblePattern();
            return result;
        }

        private static void UpdateComboRumble(int device, ControlService ctrl)
        {
            bool? motor = comboRumble[device].Update(HoldNowMs());
            if (motor.HasValue)
                ctrl?.setRumble(0, motor.Value ? ComboRumblePattern.STRENGTH : (byte)0, device);
        }

        /// <summary>
        /// Runs every Combo Hold Mode action of the profile before any other
        /// special action, so it sees the real hold button and, while the
        /// mode is on, other actions on that button (such as Hold to
        /// Activate) do not.
        /// </summary>
        private static void ProcessComboHoldModeActions(int device, DS4State cState, DS4State mappedState,
            DS4StateExposed eState, Mouse tp, ControlService ctrl, DS4StateFieldMapping fieldMapping,
            DS4StateFieldMapping outputfieldMapping)
        {
            List<string> profileActions = getProfileActions(device);
            if (profileActions == null) return;
            for (int i = 0, len = profileActions.Count; i < len; i++)
            {
                string actionName = profileActions[i];
                SpecialAction action = GetProfileAction(device, actionName);
                if (action == null || action.typeID != SpecialAction.ActionTypeId.ComboHoldMode)
                    continue;
                if (GetProfileActionIndexOf(device, actionName) < 0)
                    continue;
                ProcessComboHoldModeAction(device, action, cState, mappedState, eState, tp, ctrl,
                    fieldMapping, outputfieldMapping);
            }
        }

        private static void ProcessComboHoldModeAction(int device, SpecialAction action, DS4State cState,
            DS4State mappedState, DS4StateExposed eState, Mouse tp, ControlService ctrl,
            DS4StateFieldMapping fieldMapping, DS4StateFieldMapping outputfieldMapping)
        {
            ComboHoldModeSettings settings = action.comboSettings;
            if (settings == null || action.trigger.Count != 1)
                return;

            DS4Controls modeButton = action.trigger[0];
            DS4Controls holdButton = settings.HoldButton;
            if (!IsHoldToActivateTriggerSupported(modeButton) || !IsHoldToActivateTriggerSupported(holdButton) ||
                modeButton == holdButton)
                return;

            Dictionary<SpecialAction, ComboModeEntry> modes = comboModes[device];
            ComboModeEntry entry;
            long now = HoldNowMs();
            lock (modes)
            {
                if (!modes.TryGetValue(action, out entry))
                {
                    if (modes.Count > 32) modes.Clear();
                    modes[action] = entry = new ComboModeEntry();
                }
                entry.LastSeenMs = now;
            }

            bool modePressed = getBoolSpecialActionMapping(device, modeButton, cState, eState, tp, fieldMapping);
            bool holdPressed = getBoolSpecialActionMapping(device, holdButton, cState, eState, tp, fieldMapping);

            // The mode button only switches the mode; its own mapping never fires.
            if (modePressed)
                SetHoldSource(modeButton, false, cState, fieldMapping, mappedState, outputfieldMapping);

            ComboHoldModeFrame frame = entry.Tracker.Update(modePressed, holdPressed, now, settings);

            if ((frame.TurnedOn || frame.TurnedOff) && settings.Rumble && ctrl != null)
            {
                comboRumble[device].Start(frame.TurnedOn, now);
                UpdateComboRumble(device, ctrl);
            }

            if (frame.SuppressHoldButton && holdPressed)
                SetHoldSource(holdButton, false, cState, fieldMapping, mappedState, outputfieldMapping);

            if (frame.OutputsPressed)
            {
                List<DS4Controls> forced = holdForcedOutputs[device];
                foreach (DS4Controls output in settings.Outputs)
                    forced.Add(output);
            }

            if (frame.ModeOn && settings.Lightbar)
            {
                DS4LightBar.forcedColor[device] = COMBO_MODE_LIGHT;
                DS4LightBar.forcelight[device] = true;
                entry.LightOn = true;
            }
            else if (entry.LightOn)
            {
                DS4LightBar.forcelight[device] = false;
                entry.LightOn = false;
            }
        }

        /// <summary>Drop combo modes whose action is no longer processed (profile change, action disabled).</summary>
        private static void StopStaleComboModes(int device)
        {
            Dictionary<SpecialAction, ComboModeEntry> modes = comboModes[device];
            lock (modes)
            {
                if (modes.Count == 0) return;
                long now = HoldNowMs();
                List<SpecialAction> stale = null;
                foreach (KeyValuePair<SpecialAction, ComboModeEntry> pair in modes)
                {
                    if (now - pair.Value.LastSeenMs > COMBO_MODE_STALE_MS)
                        (stale ??= new List<SpecialAction>()).Add(pair.Key);
                }
                if (stale == null) return;
                foreach (SpecialAction action in stale)
                {
                    ComboModeEntry entry = modes[action];
                    if (entry.LightOn) DS4LightBar.forcelight[device] = false;
                    entry.Tracker.Reset();
                    modes.Remove(action);
                }
            }
        }

        /// <summary>Turn every combo mode on this controller off (disconnect / stop).</summary>
        internal static void ResetComboHoldModes(int device)
        {
            if (device < 0 || device >= comboModes.Length) return;
            Dictionary<SpecialAction, ComboModeEntry> modes = comboModes[device];
            lock (modes)
            {
                foreach (ComboModeEntry entry in modes.Values)
                {
                    if (entry.LightOn) DS4LightBar.forcelight[device] = false;
                    entry.Tracker.Reset();
                }
                modes.Clear();
            }
            comboRumble[device].Stop();
        }
    }
}
