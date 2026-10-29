using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ComboHoldModeTests
{
    public TestContext TestContext { get; set; }

    private const string UserCombo =
        "mode=Toggle;hold=Square;out=R1/R2/L2/Triangle/Cross;passhold=0;style=Pulse;down=80;up=80;rumble=1;light=1";

    private static ComboHoldModeSettings Settings(string details = UserCombo) => ComboHoldModeSettings.Parse(details);

    [TestMethod]
    public void SettingsRoundTripDefaultsAndClamp()
    {
        var s = Settings();
        Assert.AreEqual(ComboModeBehavior.Toggle, s.ModeBehavior);
        Assert.AreEqual(DS4Controls.Square, s.HoldButton);
        CollectionAssert.AreEqual(new[] { DS4Controls.R1, DS4Controls.R2, DS4Controls.L2, DS4Controls.Triangle, DS4Controls.Cross }, s.Outputs);
        Assert.AreEqual(ComboOutputStyle.Pulse, s.Style);
        Assert.AreEqual(80, s.PressMs);
        Assert.AreEqual(80, s.ReleaseMs);
        Assert.IsFalse(s.PassHoldButton);
        Assert.IsTrue(s.Rumble);
        Assert.IsTrue(s.Lightbar);
        Assert.AreEqual(UserCombo, s.Serialize());
        Assert.AreEqual(s.Serialize(), ComboHoldModeSettings.Parse(s.Serialize()).Serialize());
        Assert.AreEqual(UserCombo, ComboHoldModeSettings.CreateDefault().Serialize(), "default is the requested combo");

        var junk = ComboHoldModeSettings.Parse("mode=Bogus;hold=LXNeg;out=R1/LXNeg/R1/nope;style=x;down=5;up=9999;rumble=0;light=false;zzz=1");
        Assert.AreEqual(ComboModeBehavior.Toggle, junk.ModeBehavior);
        Assert.AreEqual(DS4Controls.Square, junk.HoldButton, "sticks are not hold buttons");
        CollectionAssert.AreEqual(new[] { DS4Controls.R1 }, junk.Outputs, "unsupported and duplicate outputs dropped");
        Assert.AreEqual(ComboOutputStyle.Pulse, junk.Style);
        Assert.AreEqual(ComboHoldModeSettings.MIN_PULSE_MS, junk.PressMs);
        Assert.AreEqual(ComboHoldModeSettings.MAX_PULSE_MS, junk.ReleaseMs);
        Assert.IsFalse(junk.Rumble);
        Assert.IsFalse(junk.Lightbar);

        var steady = ComboHoldModeSettings.Parse("mode=WhileHeld;hold=Circle;out=L1;passhold=1;style=Steady");
        Assert.AreEqual(ComboModeBehavior.WhileHeld, steady.ModeBehavior);
        Assert.AreEqual(ComboOutputStyle.Steady, steady.Style);
        Assert.IsTrue(steady.PassHoldButton);
        Assert.IsNull(steady.Validate());
        Assert.IsNotNull(ComboHoldModeSettings.Parse("hold=Square").Validate(), "needs outputs");
        Assert.IsNotNull(ComboHoldModeSettings.Parse("").Validate());
    }

    [TestMethod]
    public void SpecialActionWithPaddleTriggerParsesAndActionsXmlRoundTrips()
    {
        var action = new SpecialAction("Barb combo", "Bottom Left Paddle", ComboHoldModeSettings.TYPE_NAME, UserCombo, extras: "Square");
        Assert.AreEqual(SpecialAction.ActionTypeId.ComboHoldMode, action.typeID);
        CollectionAssert.AreEqual(new[] { DS4Controls.BLP }, action.trigger);
        Assert.AreEqual(0, action.uTrigger.Count, "extras are never an unload chord");
        Assert.AreEqual(DS4Controls.Square, action.comboSettings.HoldButton);

        var store = new BackingStore();
        store.actions.Add(action);
        store.actions.Add(new SpecialAction("Hold Y", "Square", HoldToActivateSettings.TYPE_NAME, "hold=3;out=Button;button=Y"));
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
        StringAssert.Contains(xml, "<Type>ComboHoldMode</Type>");
        StringAssert.Contains(xml, "Bottom Left Paddle");

        ActionsDTO read;
        using (var reader = new StringReader(xml))
            read = (ActionsDTO)serializer.Deserialize(reader);
        var store2 = new BackingStore();
        read.MapTo(store2);
        Assert.AreEqual(3, store2.actions.Count, "existing actions keep loading next to the new type");
        SpecialAction loaded = store2.actions.Single(a => a.name == "Barb combo");
        Assert.AreEqual(SpecialAction.ActionTypeId.ComboHoldMode, loaded.typeID);
        CollectionAssert.AreEqual(new[] { DS4Controls.BLP }, loaded.trigger);
        Assert.AreEqual(UserCombo, loaded.comboSettings.Serialize());
        Assert.AreEqual(SpecialAction.ActionTypeId.HoldToActivate, store2.actions.Single(a => a.name == "Hold Y").typeID);
    }

    [TestMethod]
    public void EdgePaddlesAndFnButtonsAreOfferedAsModeButtons()
    {
        Assert.IsTrue(SpecialActionTriggerCatalog.Entries.Any(e => e.Control == DS4Controls.BLP && e.Tag == "Bottom Left Paddle"));
        Assert.IsTrue(SpecialActionTriggerCatalog.Entries.Any(e => e.Control == DS4Controls.BRP && e.Tag == "Bottom Right Paddle"));
        Assert.IsTrue(SpecialActionTriggerCatalog.Entries.Any(e => e.Control == DS4Controls.FnL));
        Assert.IsTrue(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.BLP));
        Assert.IsTrue(Mapping.IsHoldToActivateTriggerSupported(DS4Controls.BRP));
        // Edge report: byte 10 bits 0x40 / 0x80 are the left / right back paddles.
        var state = new DS4State();
        DualSenseDevice.DecodeAuxiliaryButtons(0x40, state);
        Assert.IsTrue(state.BLP);
        Assert.IsFalse(state.BRP);
        DualSenseDevice.DecodeAuxiliaryButtons(0x80, state);
        Assert.IsFalse(state.BLP);
        Assert.IsTrue(state.BRP);
    }

    [TestMethod]
    public void ToggleTurnsOnAndOffOncePerPress()
    {
        var s = Settings();
        var t = new ComboHoldModeTracker();
        Assert.IsFalse(t.Update(false, false, 0, s).ModeOn);
        var f = t.Update(true, false, 10, s);
        Assert.IsTrue(f.ModeOn);
        Assert.IsTrue(f.TurnedOn);
        f = t.Update(true, false, 500, s);
        Assert.IsTrue(f.ModeOn, "holding the paddle does not flip it again");
        Assert.IsFalse(f.TurnedOn);
        Assert.IsTrue(t.Update(false, false, 600, s).ModeOn, "stays on after release");
        f = t.Update(true, false, 700, s);
        Assert.IsFalse(f.ModeOn);
        Assert.IsTrue(f.TurnedOff);
        Assert.IsFalse(t.Update(false, false, 800, s).ModeOn);
        t.Update(true, false, 900, s);
        Assert.IsTrue(t.ModeOn);
        t.Reset();
        Assert.IsFalse(t.ModeOn, "reset (profile change / disconnect) turns it off");
    }

    [TestMethod]
    public void WhileHeldModeIsOnOnlyWhileModeButtonIsDown()
    {
        var s = Settings(UserCombo.Replace("mode=Toggle", "mode=WhileHeld"));
        var t = new ComboHoldModeTracker();
        Assert.IsTrue(t.Update(true, false, 0, s).TurnedOn);
        Assert.IsTrue(t.Update(true, true, 10, s).OutputsPressed);
        var f = t.Update(false, true, 20, s);
        Assert.IsTrue(f.TurnedOff);
        Assert.IsFalse(f.OutputsPressed);
        Assert.IsFalse(f.SuppressHoldButton, "mode off: hold button works normally");
    }

    [TestMethod]
    public void OutputsOnlyWhileModeOnAndHoldButtonHeldAndHoldButtonIsReplaced()
    {
        var s = Settings(UserCombo.Replace("style=Pulse", "style=Steady"));
        var t = new ComboHoldModeTracker();
        var f = t.Update(false, true, 0, s);
        Assert.IsFalse(f.OutputsPressed, "mode off: nothing extra");
        Assert.IsFalse(f.SuppressHoldButton, "mode off: Square is normal");
        t.Update(true, false, 10, s);
        t.Update(false, false, 20, s);
        f = t.Update(false, false, 30, s);
        Assert.IsTrue(f.ModeOn);
        Assert.IsFalse(f.OutputsPressed, "mode on but Square not held");
        for (long now = 40; now < 2000; now += 4)
        {
            f = t.Update(false, true, now, s);
            Assert.IsTrue(f.OutputsPressed, "steady: held every frame");
            Assert.IsTrue(f.SuppressHoldButton, "Square itself is replaced");
        }
        f = t.Update(false, false, 2004, s);
        Assert.IsFalse(f.OutputsPressed, "released with Square");
        Assert.IsFalse(f.SuppressHoldButton);

        var pass = Settings(UserCombo.Replace("passhold=0", "passhold=1"));
        var t2 = new ComboHoldModeTracker();
        t2.Update(true, false, 0, pass);
        f = t2.Update(false, true, 10, pass);
        Assert.IsTrue(f.OutputsPressed);
        Assert.IsFalse(f.SuppressHoldButton, "also send the hold button");
    }

    [TestMethod]
    public void RapidRepeatPressesAndReleasesOnAFixedCycle()
    {
        var s = Settings();
        var t = new ComboHoldModeTracker();
        t.Update(true, false, 0, s);
        t.Update(false, false, 1, s);
        long start = 1000;
        int presses = 0;
        bool last = false;
        for (long now = start; now < start + 1600; now++)
        {
            bool down = t.Update(false, true, now, s).OutputsPressed;
            long phase = (now - start) % 160;
            Assert.AreEqual(phase < 80, down, $"at +{now - start} ms");
            if (down && !last) presses++;
            last = down;
        }
        Assert.AreEqual(10, presses, "80 ms down + 80 ms up = 10 presses in 1.6 s");

        // Letting go releases at once, and the next hold starts pressed again.
        Assert.IsFalse(t.Update(false, false, start + 1650, s).OutputsPressed);
        Assert.IsTrue(t.Update(false, true, start + 1705, s).OutputsPressed, "new hold starts with a press");
        Assert.IsTrue(t.Update(false, true, start + 1784, s).OutputsPressed);
        Assert.IsFalse(t.Update(false, true, start + 1785, s).OutputsPressed);
        Assert.IsTrue(t.Update(false, true, start + 1865, s).OutputsPressed);
    }

    [TestMethod]
    public void PulseTimingUsesConfiguredAndClampedTimes()
    {
        Assert.IsTrue(ComboHoldModeTracker.PulsePressed(0, 49, 50, 100));
        Assert.IsFalse(ComboHoldModeTracker.PulsePressed(0, 50, 50, 100));
        Assert.IsFalse(ComboHoldModeTracker.PulsePressed(0, 149, 50, 100));
        Assert.IsTrue(ComboHoldModeTracker.PulsePressed(0, 150, 50, 100));
        // 10 ms is clamped up to 30 ms.
        Assert.IsTrue(ComboHoldModeTracker.PulsePressed(0, 29, 10, 10));
        Assert.IsFalse(ComboHoldModeTracker.PulsePressed(0, 30, 10, 10));
        Assert.IsTrue(ComboHoldModeTracker.PulsePressed(0, 60, 10, 10));
        Assert.IsTrue(ComboHoldModeTracker.PulsePressed(100, 50, 80, 80), "clock going backwards stays pressed");
        var s = Settings(UserCombo.Replace("down=80", "down=40").Replace("up=80", "up=120"));
        var t = new ComboHoldModeTracker();
        t.Update(true, false, 0, s);
        Assert.IsTrue(t.Update(false, true, 100, s).OutputsPressed);
        Assert.IsTrue(t.Update(false, true, 139, s).OutputsPressed);
        Assert.IsFalse(t.Update(false, true, 140, s).OutputsPressed);
        Assert.IsFalse(t.Update(false, true, 259, s).OutputsPressed);
        Assert.IsTrue(t.Update(false, true, 260, s).OutputsPressed);
    }

    [TestMethod]
    public void RumbleIsOneBuzzForOnAndTwoForOff()
    {
        var r = new ComboRumblePattern();
        r.Start(true, 1000);
        Assert.AreEqual(true, r.Update(1000));
        Assert.IsNull(r.Update(1100));
        Assert.AreEqual(false, r.Update(1180));
        Assert.IsFalse(r.Active);

        r.Start(false, 0);
        var changes = new List<(long, bool)>();
        for (long now = 0; now < 500; now += 2)
        {
            bool? m = r.Update(now);
            if (m.HasValue) changes.Add((now, m.Value));
        }
        CollectionAssert.AreEqual(new[] { (0L, true), (110L, false), (210L, true), (320L, false) }, changes);
    }

    [TestMethod]
    public void EditorViewModelDefaultsToRequestedComboAndValidates()
    {
        var vm = new ComboHoldModeViewModel();
        AssertSameCombo(UserCombo, vm.BuildSettings());
        Assert.AreEqual(Visibility.Visible, vm.PulsePanelVisibility);
        Assert.IsNull(vm.Validate(new SpecialAction("x", "Bottom Left Paddle", "null", "null")));
        Assert.IsNull(vm.Validate(new SpecialAction("x", "Bottom Right Paddle", "null", "null")));
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Bottom Left Paddle/Bottom Right Paddle", "null", "null")), "one mode button");
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Square", "null", "null")), "mode button must differ from hold button");
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Left Stick Up", "null", "null")));
        StringAssert.Contains(vm.Summary, "over and over");
        StringAssert.Contains(vm.Summary, "works normally");

        vm.StyleIndex = 1;
        Assert.AreEqual(Visibility.Collapsed, vm.PulsePanelVisibility);
        StringAssert.Contains(vm.BuildSettings().Serialize(), "style=Steady");
        vm.PressMs = 5;
        Assert.AreEqual(ComboHoldModeSettings.MIN_PULSE_MS, vm.PressMs);
        foreach (var o in vm.Outputs) o.IsChecked = false;
        Assert.IsNotNull(vm.Validate(new SpecialAction("x", "Bottom Left Paddle", "null", "null")), "needs an output");

        vm.LoadSettings(Settings());
        AssertSameCombo(UserCombo, vm.BuildSettings());
    }

    // The editor lists outputs in its own display order; order never matters at runtime.
    private static void AssertSameCombo(string expected, ComboHoldModeSettings actual)
    {
        var e = Settings(expected);
        CollectionAssert.AreEquivalent(e.Outputs, actual.Outputs);
        e.Outputs.Clear();
        var a = ComboHoldModeSettings.Parse(actual.Serialize());
        a.Outputs.Clear();
        Assert.AreEqual(e.Serialize(), a.Serialize());
    }

    [TestMethod]
    public void HoldToActivateSummaryExplainsHeldBackPress()
    {
        var vm = new HoldToActivateViewModel(new ProfileList());
        vm.LoadSettings(HoldToActivateSettings.Parse("hold=3;out=Macro;macro=273/380/273;rumble=1;tap=Delayed"));
        StringAssert.Contains(vm.Summary, "will not show the button as pressed");
    }

    // Renders the real Special Action editor with Combo Hold Mode set up
    // the way the user asked (left paddle toggle, hold Square).
    [DataTestMethod]
    [TestCategory("UiScreenshots")]
    [DataRow("combo-hold-mode", "Bottom Left Paddle", UserCombo, "DarkTheme")]
    [DataRow("combo-hold-mode-steady", "Bottom Right Paddle",
        "mode=WhileHeld;hold=Square;out=R1/R2/L2/Triangle/Cross;passhold=0;style=Steady;rumble=1;light=1", "DefaultTheme")]
    public void RenderComboHoldModeEditor(string name, string trigger, string details, string theme)
    {
        string dir = Environment.GetEnvironmentVariable("HOLD_TO_ACTIVATE_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = TestContext.TestResultsDirectory;
        Directory.CreateDirectory(dir);
        string imagePath = Path.Combine(dir, $"{name}.png");
        OnSta(() =>
        {
            var action = new SpecialAction("Barb combo (paddle)", trigger, ComboHoldModeSettings.TYPE_NAME, details);
            var editor = new SpecialActionEditor(Global.TEST_PROFILE_INDEX, new ProfileList(), action);
            editor.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative),
            });
            editor.Background = (Brush)editor.FindResource("BackgroundColor");
            const int width = 900, height = 1000;
            editor.Measure(new Size(width, height));
            editor.Arrange(new Rect(0, 0, width, height));
            editor.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            editor.UpdateLayout();

            var tab = (TabItem)editor.FindName("comboHoldModeTab");
            Assert.IsTrue(tab.IsSelected, "Loading a combo action opens its options");
            var combo = (ComboBox)editor.FindName("actionTypeCombo");
            Assert.AreEqual(11, combo.SelectedIndex);
            Assert.AreEqual(details.Contains("style=Pulse") ? Visibility.Visible : Visibility.Collapsed,
                ((FrameworkElement)editor.FindName("comboPulsePanel")).Visibility);

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
