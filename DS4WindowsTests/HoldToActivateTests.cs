using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class HoldToActivateTests
{
    public TestContext TestContext { get; set; }

    private const long Hold = 3000;

    [TestMethod]
    public void QuickTapIsHeldBackThenSentAsShortTapOnRelease()
    {
        var t = new HoldToActivateTracker();
        Assert.IsFalse(t.Update(true, 0, Hold, instantTap: false).SourcePressed, "press is held back");
        Assert.IsFalse(t.Update(true, 200, Hold, false).SourcePressed);
        var release = t.Update(false, 250, Hold, false);
        Assert.IsTrue(release.SourcePressed, "tap replays on release");
        Assert.IsFalse(release.HoldFired);
        Assert.IsTrue(t.Update(false, 300, Hold, false).SourcePressed, "tap lasts the pulse");
        Assert.IsFalse(t.Update(false, 250 + HoldToActivateTracker.TAP_PULSE_MS, Hold, false).SourcePressed, "tap ends");
        Assert.AreEqual(HoldToActivateTracker.Phase.Idle, t.CurrentPhase);
    }

    [TestMethod]
    public void LongHoldFiresExactlyOnceAndNeverSendsTheNormalPress()
    {
        var t = new HoldToActivateTracker();
        int fired = 0;
        for (long now = 0; now <= 5000; now += 4)
        {
            var f = t.Update(true, now, Hold, false);
            Assert.IsFalse(f.SourcePressed, $"normal press leaked at {now} ms");
            if (f.HoldFired)
            {
                fired++;
                Assert.AreEqual(Hold, now, "fires at the configured time");
            }
            Assert.AreEqual(now >= Hold, f.HoldHeld);
        }
        Assert.AreEqual(1, fired);
        var release = t.Update(false, 5004, Hold, false);
        Assert.IsFalse(release.SourcePressed, "no tap after a completed hold");
        Assert.IsFalse(release.HoldHeld);
        Assert.AreEqual(HoldToActivateTracker.Phase.Idle, t.CurrentPhase);
    }

    [TestMethod]
    public void InstantTapPassesThroughAndIsReleasedWhenHoldFires()
    {
        var t = new HoldToActivateTracker();
        Assert.IsTrue(t.Update(true, 0, Hold, instantTap: true).SourcePressed);
        Assert.IsTrue(t.Update(true, 2999, Hold, true).SourcePressed);
        var fire = t.Update(true, 3000, Hold, true);
        Assert.IsTrue(fire.HoldFired);
        Assert.IsFalse(fire.SourcePressed, "normal action must not stay stuck");
        Assert.IsFalse(t.Update(true, 3500, Hold, true).SourcePressed);
        Assert.IsFalse(t.Update(false, 3600, Hold, true).SourcePressed);

        // A short instant press ends exactly when the button is released.
        Assert.IsTrue(t.Update(true, 4000, Hold, true).SourcePressed);
        Assert.IsFalse(t.Update(false, 4100, Hold, true).SourcePressed);
    }

    [TestMethod]
    public void ToggleModeLatchesOnEachCompletedHoldOnly()
    {
        var t = new HoldToActivateTracker();
        t.Update(true, 0, Hold, false, toggleOnFire: true);
        t.Update(true, 3000, Hold, false, true);
        t.Update(false, 3100, Hold, false, true);
        Assert.IsTrue(t.Latched);
        // A quick tap does not change the latch.
        t.Update(true, 4000, Hold, false, true);
        t.Update(false, 4100, Hold, false, true);
        t.Update(false, 4300, Hold, false, true);
        Assert.IsTrue(t.Latched);
        t.Update(true, 5000, Hold, false, true);
        t.Update(true, 8000, Hold, false, true);
        Assert.IsFalse(t.Latched);
    }

    [TestMethod]
    public void TimingIsDeterministic()
    {
        long FireTime()
        {
            var t = new HoldToActivateTracker();
            for (long now = 0; ; now++)
                if (t.Update(true, now, 1500, false).HoldFired) return now;
        }
        long first = FireTime();
        for (int i = 0; i < 20; i++) Assert.AreEqual(first, FireTime());
        Assert.AreEqual(1500, first);
    }

    [TestMethod]
    public void SettingsRoundTripAndClamp()
    {
        var s = new HoldToActivateSettings
        {
            HoldSeconds = 2.5, Kind = HoldOutputKind.Profile, Profile = "Diablo; layout=2 / ü",
            Rumble = false, InstantTap = true,
        };
        var back = HoldToActivateSettings.Parse(s.Serialize());
        Assert.AreEqual(2.5, back.HoldSeconds);
        Assert.AreEqual(HoldOutputKind.Profile, back.Kind);
        Assert.AreEqual("Diablo; layout=2 / ü", back.Profile);
        Assert.IsFalse(back.Rumble);
        Assert.IsTrue(back.InstantTap);

        var key = HoldToActivateSettings.Parse("hold=3;out=Key;key=81;sc=1;mode=Toggle;rumble=1;tap=Delayed");
        Assert.AreEqual(81, key.KeyCode);
        Assert.IsTrue(key.ScanCode);
        Assert.AreEqual(HoldOutputMode.Toggle, key.Mode);
        Assert.AreEqual(key.Serialize(), HoldToActivateSettings.Parse(key.Serialize()).Serialize());

        var macro = new HoldToActivateSettings { Kind = HoldOutputKind.Macro };
        macro.Macro.AddRange(new[] { 81, 81 });
        CollectionAssert.AreEqual(new[] { 81, 81 }, HoldToActivateSettings.Parse(macro.Serialize()).Macro);

        Assert.AreEqual(0.5, HoldToActivateSettings.Parse("hold=0.01").HoldSeconds);
        Assert.AreEqual(10.0, HoldToActivateSettings.Parse("hold=99").HoldSeconds);
        var junk = HoldToActivateSettings.Parse("garbage;out=Nope;button=LXNeg;hold=abc");
        Assert.AreEqual(HoldToActivateSettings.DEFAULT_HOLD_SECONDS, junk.HoldSeconds);
        Assert.AreEqual(HoldOutputKind.Key, junk.Kind);
        Assert.AreEqual(X360Controls.None, junk.Button, "sticks are not hold outputs");
        Assert.IsTrue(junk.Rumble, "rumble defaults on");
        Assert.IsNotNull(junk.Validate());
    }

    [TestMethod]
    public void SpecialActionParsesHoldSettingsAndActionsXmlRoundTrips()
    {
        string details = "hold=3;out=Button;button=Y;mode=WhileHeld;rumble=1;tap=Delayed";
        var action = new SpecialAction("Hold Y", "Square", HoldToActivateSettings.TYPE_NAME, details, extras: "Square");
        Assert.AreEqual(SpecialAction.ActionTypeId.HoldToActivate, action.typeID);
        CollectionAssert.AreEqual(new[] { DS4Controls.Square }, action.trigger);
        Assert.AreEqual(0, action.uTrigger.Count, "extras are never an unload chord");
        Assert.AreEqual(X360Controls.Y, action.holdSettings.Button);
        Assert.AreEqual(3.0, action.delayTime);

        var store = new BackingStore();
        store.actions.Add(action);
        store.actions.Add(new SpecialAction("Old macro", "Cross", "Macro", "65/65"));
        var dto = new ActionsDTO();
        dto.MapFrom(store);
        var serializer = new XmlSerializer(typeof(ActionsDTO));
        string xml;
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, dto);
            xml = writer.ToString();
        }
        StringAssert.Contains(xml, "<Type>HoldToActivate</Type>");

        ActionsDTO read;
        using (var reader = new StringReader(xml))
            read = (ActionsDTO)serializer.Deserialize(reader);
        var store2 = new BackingStore();
        read.MapTo(store2);
        Assert.AreEqual(2, store2.actions.Count, "existing actions keep loading next to the new type");
        SpecialAction loaded = store2.actions.Single(a => a.name == "Hold Y");
        Assert.AreEqual(SpecialAction.ActionTypeId.HoldToActivate, loaded.typeID);
        Assert.AreEqual(action.holdSettings.Serialize(), loaded.holdSettings.Serialize());
    }

    [TestMethod]
    public void OnlyHoldableControlsCanBeTriggers()
    {
        Assert.IsTrue(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.Square));
        Assert.IsTrue(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.R2));
        Assert.IsTrue(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.BLP));
        Assert.IsFalse(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.LXNeg));
        Assert.IsFalse(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.None));
    }

    [TestMethod]
    public void EditorViewModelRequiresExactlyOneHoldableTrigger()
    {
        var vm = new HoldToActivateViewModel(new ProfileList());
        vm.LoadSettings(HoldToActivateSettings.Parse("hold=3;out=Button;button=A"));
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Square/Cross", "null", "null")));
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Left Stick Up", "null", "null")));
        Assert.IsNull(vm.Validate(new SpecialAction("x", "Square", "null", "null")));
        Assert.AreEqual(HoldToActivateSettings.Parse("hold=3;out=Button;button=A").Serialize(), vm.BuildSettings().Serialize());
    }

    [TestMethod]
    public void MacroRepeatParsesAndSerializesBackwardCompatibly()
    {
        // The user's real saved action from before this option existed.
        var old = HoldToActivateSettings.Parse("hold=3;out=Macro;macro=273/380/273/274;rumble=1;tap=Delayed");
        Assert.AreEqual(HoldMacroRepeat.Once, old.Repeat, "old configs load as Play once");
        Assert.IsFalse(old.Serialize().Contains("repeat="), "Once is not written, so older builds read it unchanged");

        foreach (HoldMacroRepeat mode in new[] { HoldMacroRepeat.WhileHeld, HoldMacroRepeat.Toggle })
        {
            var s = new HoldToActivateSettings { Kind = HoldOutputKind.Macro, Repeat = mode };
            s.Macro.AddRange(new[] { 273, 380, 273 });
            string text = s.Serialize();
            StringAssert.Contains(text, "repeat=" + mode);
            var back = HoldToActivateSettings.Parse(text);
            Assert.AreEqual(mode, back.Repeat);
            CollectionAssert.AreEqual(new[] { 273, 380, 273 }, back.Macro);
            Assert.AreEqual(text, back.Serialize());
        }

        Assert.AreEqual(HoldMacroRepeat.Toggle, HoldToActivateSettings.Parse("out=Macro;macro=81/81;repeat=toggle").Repeat);
        Assert.AreEqual(HoldMacroRepeat.Once, HoldToActivateSettings.Parse("out=Macro;macro=81/81;repeat=Forever").Repeat);
        Assert.AreEqual(HoldMacroRepeat.Once, HoldToActivateSettings.Parse("out=Macro;macro=81/81;repeat=7").Repeat);
        var key = new HoldToActivateSettings { Kind = HoldOutputKind.Key, KeyCode = 81, Repeat = HoldMacroRepeat.Toggle };
        Assert.IsFalse(key.Serialize().Contains("repeat="), "repeat only applies to macro output");
    }

    [TestMethod]
    public void InstantMacrosGetPlaybackGapsButTimedMacrosAreUntouched()
    {
        // R1 down, R1 up, Cross down, Cross up with no waits at all.
        int[] instant = { 273, 273, 261, 261 };
        CollectionAssert.AreEqual(new[] { 273, 360, 273, 360, 261, 360, 261 },
            HoldToActivateSettings.BuildPlaybackMacro(instant));
        CollectionAssert.AreEqual(new[] { 273, 273, 261, 261 }, instant, "saved data is not modified");

        int[] timed = { 273, 380, 273, 400, 261, 380, 261 };
        CollectionAssert.AreEqual(timed, HoldToActivateSettings.BuildPlaybackMacro(timed));

        // Lightbar/rumble codes are not waits and get no gap of their own.
        int[] withLight = { 1000255000, 65, 65 };
        CollectionAssert.AreEqual(new[] { 1000255000, 65, 360, 65 }, HoldToActivateSettings.BuildPlaybackMacro(withLight));
        CollectionAssert.AreEqual(new[] { 65 }, HoldToActivateSettings.BuildPlaybackMacro(new List<int> { 65 }));
        Assert.AreEqual(0, HoldToActivateSettings.BuildPlaybackMacro(new List<int>()).Length);
    }

    private static List<HoldMacroCommand> Drive(HoldMacroRepeat repeat, HoldToActivateTracker t, HoldMacroRepeatState st,
        bool pressed, long from, long to, List<HoldToActivateFrame> frames = null)
    {
        var commands = new List<HoldMacroCommand>();
        for (long now = from; now < to; now += 4)
        {
            var f = t.Update(pressed, now, Hold, instantTap: false);
            frames?.Add(f);
            var c = st.Update(repeat, f);
            if (c != HoldMacroCommand.None) commands.Add(c);
        }
        return commands;
    }

    [TestMethod]
    public void PlayOnceFiresOncePerCompletedHold()
    {
        var t = new HoldToActivateTracker();
        var st = new HoldMacroRepeatState();
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.PlayOnce }, Drive(HoldMacroRepeat.Once, t, st, true, 0, 6000));
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Once, t, st, false, 6000, 6500).Count, "release does nothing");
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Once, t, st, true, 6500, 6700).Count, "short press does not fire");
        Assert.IsFalse(st.Looping);
    }

    [TestMethod]
    public void RepeatWhileHeldLoopsUntilReleaseThenFinishesThePass()
    {
        var t = new HoldToActivateTracker();
        var st = new HoldMacroRepeatState();
        Assert.AreEqual(0, Drive(HoldMacroRepeat.WhileHeld, t, st, true, 0, 2000).Count, "nothing before the hold time");
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.StartLoop }, Drive(HoldMacroRepeat.WhileHeld, t, st, true, 2000, 9000));
        Assert.IsTrue(st.Looping);
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.StopAfterPass }, Drive(HoldMacroRepeat.WhileHeld, t, st, false, 9000, 9500));
        Assert.IsFalse(st.Looping);

        // A quick tap afterwards is a normal tap and never starts the loop.
        var frames = new List<HoldToActivateFrame>();
        Assert.AreEqual(0, Drive(HoldMacroRepeat.WhileHeld, t, st, true, 9500, 9700, frames).Count);
        Assert.AreEqual(0, Drive(HoldMacroRepeat.WhileHeld, t, st, false, 9700, 9900, frames).Count);
        Assert.IsTrue(frames.Any(f => f.SourcePressed), "tap still reaches the normal mapping");
    }

    [TestMethod]
    public void ToggleRepeatStartsOnHoldKeepsGoingAfterReleaseAndStopsOnNextHold()
    {
        var t = new HoldToActivateTracker();
        var st = new HoldMacroRepeatState();
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.StartLoop }, Drive(HoldMacroRepeat.Toggle, t, st, true, 0, 4000));
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Toggle, t, st, false, 4000, 8000).Count, "keeps looping after release");
        Assert.IsTrue(st.Looping);

        var frames = new List<HoldToActivateFrame>();
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Toggle, t, st, true, 8000, 8200, frames).Count, "quick tap does not stop it");
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Toggle, t, st, false, 8200, 8600, frames).Count);
        Assert.IsTrue(frames.Any(f => f.SourcePressed), "quick tap while looping still does the normal tap");
        Assert.IsTrue(st.Looping);

        var stop = Drive(HoldMacroRepeat.Toggle, t, st, true, 9000, 13000, frames);
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.StopNow }, stop, "next completed hold stops it");
        Assert.IsFalse(st.Looping);
        Assert.AreEqual(0, Drive(HoldMacroRepeat.Toggle, t, st, false, 13000, 13500).Count);
        CollectionAssert.AreEqual(new[] { HoldMacroCommand.StartLoop }, Drive(HoldMacroRepeat.Toggle, t, st, true, 14000, 18000), "and can start again");
    }

    private sealed class FakePass
    {
        private int active;
        public int MaxConcurrent;
        public int Passes;
        public int Released;
        public readonly List<long> Starts = new List<long>();
        public readonly List<long> Ends = new List<long>();
        public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public int PassMs = 40;

        public void Play(CancellationToken token)
        {
            int now = Interlocked.Increment(ref active);
            lock (this)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, now);
                Starts.Add(Clock.ElapsedMilliseconds);
            }
            try
            {
                token.WaitHandle.WaitOne(PassMs);
            }
            finally
            {
                lock (this)
                {
                    Passes++;
                    Released++;
                    Ends.Add(Clock.ElapsedMilliseconds);
                }
                Interlocked.Decrement(ref active);
            }
        }
    }

    [TestMethod]
    public void RunnerPlaysOnceAndIgnoresStartsWhileBusy()
    {
        var pass = new FakePass { PassMs = 150 };
        var runner = new HoldMacroRunner(pass.Play);
        runner.Apply(HoldMacroCommand.PlayOnce);
        runner.Apply(HoldMacroCommand.PlayOnce);
        runner.Apply(HoldMacroCommand.PlayOnce);
        Assert.IsTrue(runner.CurrentTask.Wait(5000));
        Thread.Sleep(100);
        Assert.AreEqual(1, pass.Passes);
        Assert.AreEqual(1, pass.MaxConcurrent);
        Assert.IsFalse(runner.IsBusy);
    }

    [TestMethod]
    public void RunnerLoopsWithGapNeverOverlapsAndStopsAfterPass()
    {
        var pass = new FakePass { PassMs = 30 };
        var runner = new HoldMacroRunner(pass.Play);
        runner.Apply(HoldMacroCommand.StartLoop);
        Thread.Sleep(600);
        runner.Apply(HoldMacroCommand.StartLoop); // re-hold while looping: still one loop
        Thread.Sleep(200);
        runner.Apply(HoldMacroCommand.StopAfterPass);
        Assert.IsTrue(runner.CurrentTask.Wait(5000));
        int passes = pass.Passes;
        Assert.IsTrue(passes >= 4, $"looped {passes} passes");
        Assert.AreEqual(1, pass.MaxConcurrent, "never two passes at once");
        Assert.AreEqual(pass.Passes, pass.Released, "every pass released its outputs");
        lock (pass)
        {
            for (int i = 1; i < pass.Starts.Count; i++)
                Assert.IsTrue(pass.Starts[i] - pass.Ends[i - 1] >= HoldMacroRepeatState.MIN_PASS_GAP_MS - 5,
                    $"gap {pass.Starts[i] - pass.Ends[i - 1]} ms");
        }
        Thread.Sleep(200);
        Assert.AreEqual(passes, pass.Passes, "no more passes after stopping");
    }

    [TestMethod]
    public void RunnerStopNowCutsThePassAndRestartQueuesBehindIt()
    {
        var pass = new FakePass { PassMs = 2000 };
        var runner = new HoldMacroRunner(pass.Play);
        runner.Apply(HoldMacroCommand.StartLoop);
        Thread.Sleep(100);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsTrue(runner.StopAndWait(1000), "stop cuts the long pass");
        Assert.IsTrue(sw.ElapsedMilliseconds < 1000);
        Assert.AreEqual(1, pass.Passes);
        Assert.AreEqual(1, pass.Released);

        pass.PassMs = 20;
        runner.Apply(HoldMacroCommand.StartLoop);
        runner.Apply(HoldMacroCommand.StopNow);
        runner.Apply(HoldMacroCommand.StartLoop);
        Thread.Sleep(300);
        runner.Apply(HoldMacroCommand.StopNow);
        Assert.IsTrue(runner.CurrentTask.Wait(5000));
        Assert.AreEqual(1, pass.MaxConcurrent, "restart after stop never overlaps the old pass");
        Assert.AreEqual(pass.Passes, pass.Released);
    }

    [TestMethod]
    public void EditorShowsRepeatChoiceOnlyForMacroAndSavesIt()
    {
        var vm = new HoldToActivateViewModel(new ProfileList());
        vm.LoadSettings(HoldToActivateSettings.Parse("hold=3;out=Macro;macro=273/380/273;rumble=1;tap=Delayed"));
        Assert.AreEqual(0, vm.RepeatIndex, "old macro holds show Play once");
        Assert.AreEqual(Visibility.Visible, vm.RepeatPanelVisibility);
        vm.RepeatIndex = 2;
        Assert.AreEqual(HoldMacroRepeat.Toggle, HoldToActivateSettings.Parse(vm.BuildSettings().Serialize()).Repeat);
        vm.RepeatIndex = 1;
        StringAssert.Contains(vm.BuildSettings().Serialize(), "repeat=WhileHeld");
        StringAssert.Contains(vm.Summary, "keep holding");
        vm.OutputIndex = 0;
        Assert.AreEqual(Visibility.Collapsed, vm.RepeatPanelVisibility);
    }

    // Renders the real Special Action editor with the new options, the same
    // way the existing editor render tests do. Saved as PNGs for review.
    [DataTestMethod]
    [TestCategory("UiScreenshots")]
    [DataRow("key-while-held", "hold=3;out=Key;key=81;mode=WhileHeld;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("button-toggle", "hold=3;out=Button;button=Y;mode=Toggle;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("profile-switch", "hold=2;out=Profile;profile=Diablo%204%20layout%202;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("macro-instant", "hold=1.5;out=Macro;macro=81/81;rumble=0;tap=Instant", "DefaultTheme")]
    [DataRow("repeat", "hold=3;out=Macro;macro=273/380/273/400/261/380/261;repeat=WhileHeld;rumble=1;tap=Delayed", "DarkTheme")]
    public void RenderHoldToActivateEditor(string name, string details, string theme)
    {
        string dir = Environment.GetEnvironmentVariable("HOLD_TO_ACTIVATE_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = TestContext.TestResultsDirectory;
        Directory.CreateDirectory(dir);
        string imagePath = Path.Combine(dir, $"hold-to-activate-{name}.png");
        OnSta(() =>
        {
            var profiles = new ProfileList();
            profiles.ProfileListCol.Add(new ProfileEntity { Name = "Diablo 4 layout 1" });
            profiles.ProfileListCol.Add(new ProfileEntity { Name = "Diablo 4 layout 2" });
            var action = new SpecialAction("Square: hold for " + name, "Square", HoldToActivateSettings.TYPE_NAME, details);
            var editor = new SpecialActionEditor(Global.TEST_PROFILE_INDEX, profiles, action);
            editor.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative),
            });
            editor.Background = (Brush)editor.FindResource("BackgroundColor");
            const int width = 760, height = 760;
            editor.Measure(new Size(width, height));
            editor.Arrange(new Rect(0, 0, width, height));
            editor.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            editor.UpdateLayout();

            var tab = (TabItem)editor.FindName("holdToActivateTab");
            Assert.IsTrue(tab.IsSelected, "Loading a hold action opens its options");
            var combo = (ComboBox)editor.FindName("actionTypeCombo");
            Assert.AreEqual(10, combo.SelectedIndex);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)editor.FindName("holdRumbleCheck")).Visibility);
            var repeatPanel = (FrameworkElement)editor.FindName("holdRepeatPanel");
            Assert.AreEqual(details.Contains("out=Macro") ? Visibility.Visible : Visibility.Collapsed, repeatPanel.Visibility,
                "macro repeat choice shows only for macro output");

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(editor);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(imagePath);
            encoder.Save(stream);
        });
        Assert.IsTrue(File.Exists(imagePath));
        TestContext.AddResultFile(imagePath);
    }

    private static void OnSta(Action action)
    {
        var operation = SpecialActionTriggerCatalogTests.EditorDispatcher.Value.InvokeAsync(action);
        Assert.AreSame(operation.Task, Task.WhenAny(operation.Task, Task.Delay(TimeSpan.FromSeconds(60))).GetAwaiter().GetResult(),
            "Headless WPF render timed out.");
        operation.Task.GetAwaiter().GetResult();
    }
}
