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
    /// How a Macro hold output repeats once the hold time is reached.
    /// </summary>
    public enum HoldMacroRepeat
    {
        /// <summary>Play the macro once per completed hold (default, older configs).</summary>
        Once,
        /// <summary>Keep replaying the macro while the trigger stays held.</summary>
        WhileHeld,
        /// <summary>A completed hold starts looping; the next completed hold stops it.</summary>
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
        /// <summary>Repeat behaviour for Macro output. Missing in older configs = Once.</summary>
        public HoldMacroRepeat Repeat { get; set; } = HoldMacroRepeat.Once;
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
                    if (Repeat != HoldMacroRepeat.Once) parts.Add("repeat=" + Repeat);
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
                    case "repeat":
                        if (Enum.TryParse(value, true, out HoldMacroRepeat repeat) && Enum.IsDefined(typeof(HoldMacroRepeat), repeat))
                            result.Repeat = repeat;
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

        /// <summary>Macro codes 300..999999 are waits of (code - 300) ms.</summary>
        public static bool IsMacroWait(int code) => code >= 300 && code < 1000000;

        /// <summary>Key, mouse button and controller button events (codes below 300).</summary>
        public static bool IsMacroInputEvent(int code) => code >= 0 && code < 300;

        /// <summary>Gap inserted between events of a macro that has no waits at all.</summary>
        public const int DEFAULT_EVENT_GAP_MS = 60;

        /// <summary>
        /// Playback-time copy of a hold macro. When the macro has input events
        /// but no wait codes at all, every press/release would happen within
        /// the same instant and games never see it, so a short wait is put
        /// between consecutive input events. Macros with any wait are returned
        /// unchanged. Saved data is never modified.
        /// </summary>
        public static int[] BuildPlaybackMacro(IList<int> macro, int gapMs = DEFAULT_EVENT_GAP_MS)
        {
            if (macro == null || macro.Count == 0) return Array.Empty<int>();
            bool hasWait = false;
            int inputEvents = 0;
            foreach (int code in macro)
            {
                if (IsMacroWait(code)) { hasWait = true; break; }
                if (IsMacroInputEvent(code)) inputEvents++;
            }

            if (hasWait || inputEvents < 2 || gapMs <= 0)
                return new List<int>(macro).ToArray();

            var result = new List<int>(macro.Count + inputEvents);
            bool seenInput = false;
            foreach (int code in macro)
            {
                if (IsMacroInputEvent(code))
                {
                    if (seenInput) result.Add(300 + gapMs);
                    seenInput = true;
                }
                result.Add(code);
            }
            return result.ToArray();
        }

        public string Describe()
        {
            string what = Kind switch
            {
                HoldOutputKind.Key => "key " + KeyCode.ToString(CultureInfo.InvariantCulture),
                HoldOutputKind.Button => "button " + Button,
                HoldOutputKind.Macro => Repeat switch
                {
                    HoldMacroRepeat.WhileHeld => "macro (repeat while held)",
                    HoldMacroRepeat.Toggle => "macro (toggle repeat)",
                    _ => "macro",
                },
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

    /// <summary>What the macro runner should do after one tracker frame.</summary>
    public enum HoldMacroCommand
    {
        None,
        /// <summary>Play one pass (Once mode). Ignored if a pass is already playing.</summary>
        PlayOnce,
        /// <summary>Start (or keep) looping passes.</summary>
        StartLoop,
        /// <summary>Let the current pass finish, then stop (Repeat while held, trigger released).</summary>
        StopAfterPass,
        /// <summary>Stop now, cutting the current pass and releasing everything it holds (toggle off).</summary>
        StopNow,
    }

    /// <summary>
    /// Pure loop-state logic for a Macro hold output. Fed the tracker frame
    /// each controller report; returns the command for the macro runner.
    /// </summary>
    public sealed class HoldMacroRepeatState
    {
        /// <summary>Minimum pause between two passes of a repeating macro.</summary>
        public const int MIN_PASS_GAP_MS = 50;

        public bool Looping { get; private set; }

        public void Reset() => Looping = false;

        public HoldMacroCommand Update(HoldMacroRepeat repeat, HoldToActivateFrame frame)
        {
            switch (repeat)
            {
                case HoldMacroRepeat.WhileHeld:
                    if (frame.HoldFired)
                    {
                        Looping = true;
                        return HoldMacroCommand.StartLoop;
                    }
                    if (Looping && !frame.HoldHeld)
                    {
                        Looping = false;
                        return HoldMacroCommand.StopAfterPass;
                    }
                    return HoldMacroCommand.None;

                case HoldMacroRepeat.Toggle:
                    if (!frame.HoldFired)
                        return HoldMacroCommand.None;
                    Looping = !Looping;
                    return Looping ? HoldMacroCommand.StartLoop : HoldMacroCommand.StopNow;

                default:
                    Looping = false;
                    return frame.HoldFired ? HoldMacroCommand.PlayOnce : HoldMacroCommand.None;
            }
        }
    }

    /// <summary>
    /// Runs a hold macro on a background thread, one pass at a time. Never
    /// plays two passes concurrently: a new start while a pass is stopping is
    /// queued behind it. The pass delegate must release everything it pressed
    /// before returning (also when cancelled).
    /// </summary>
    public sealed class HoldMacroRunner
    {
        private readonly object sync = new object();
        private readonly Action<System.Threading.CancellationToken> playPass;
        private readonly int passGapMs;
        private System.Threading.Tasks.Task task = System.Threading.Tasks.Task.CompletedTask;
        private System.Threading.CancellationTokenSource cts;
        private bool loop;
        private bool accepting;

        public HoldMacroRunner(Action<System.Threading.CancellationToken> playPass,
            int passGapMs = HoldMacroRepeatState.MIN_PASS_GAP_MS)
        {
            this.playPass = playPass ?? throw new ArgumentNullException(nameof(playPass));
            this.passGapMs = Math.Max(HoldMacroRepeatState.MIN_PASS_GAP_MS, passGapMs);
        }

        /// <summary>Last time the mapping thread saw the owning action (for stale cleanup).</summary>
        public long LastSeenMs { get; set; }

        public bool IsBusy
        {
            get { lock (sync) return !task.IsCompleted; }
        }

        public System.Threading.Tasks.Task CurrentTask
        {
            get { lock (sync) return task; }
        }

        public void Apply(HoldMacroCommand command)
        {
            switch (command)
            {
                case HoldMacroCommand.PlayOnce: Start(false); break;
                case HoldMacroCommand.StartLoop: Start(true); break;
                case HoldMacroCommand.StopAfterPass: StopAfterPass(); break;
                case HoldMacroCommand.StopNow: StopNow(); break;
            }
        }

        public void Start(bool repeat)
        {
            lock (sync)
            {
                if (!task.IsCompleted && accepting)
                {
                    // A pass is already playing for this action. Keep it as
                    // the only one; just update whether it should loop.
                    if (repeat) loop = true;
                    return;
                }

                var tokenSource = new System.Threading.CancellationTokenSource();
                cts = tokenSource;
                loop = repeat;
                accepting = true;
                System.Threading.Tasks.Task previous = task;
                task = previous.ContinueWith(_ => Run(tokenSource),
                    System.Threading.CancellationToken.None,
                    System.Threading.Tasks.TaskContinuationOptions.LongRunning,
                    System.Threading.Tasks.TaskScheduler.Default);
            }
        }

        public void StopAfterPass()
        {
            lock (sync) loop = false;
        }

        public void StopNow()
        {
            lock (sync)
            {
                loop = false;
                accepting = false;
                cts?.Cancel();
            }
        }

        /// <summary>Stops now and waits (bounded) for the pass to release its outputs.</summary>
        public bool StopAndWait(int timeoutMs)
        {
            StopNow();
            System.Threading.Tasks.Task t = CurrentTask;
            try { return t.Wait(timeoutMs); }
            catch (AggregateException) { return true; }
        }

        private void Run(System.Threading.CancellationTokenSource tokenSource)
        {
            System.Threading.CancellationToken token = tokenSource.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try { playPass(token); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e) { AppLogger.LogToGui("Hold macro failed: " + e.Message, true); break; }

                    lock (sync)
                    {
                        if (token.IsCancellationRequested || !loop)
                        {
                            accepting = false;
                            break;
                        }
                    }

                    if (token.WaitHandle.WaitOne(passGapMs))
                        break;

                    lock (sync)
                    {
                        if (token.IsCancellationRequested || !loop)
                        {
                            accepting = false;
                            break;
                        }
                    }
                }
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(cts, tokenSource)) accepting = false;
                }
            }
        }
    }
}
