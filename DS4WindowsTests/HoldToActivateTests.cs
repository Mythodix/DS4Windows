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

    // Renders the real Special Action editor with the new options, the same
    // way the existing editor render tests do. Saved as PNGs for review.
    [DataTestMethod]
    [TestCategory("UiScreenshots")]
    [DataRow("key-while-held", "hold=3;out=Key;key=81;mode=WhileHeld;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("button-toggle", "hold=3;out=Button;button=Y;mode=Toggle;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("profile-switch", "hold=2;out=Profile;profile=Diablo%204%20layout%202;rumble=1;tap=Delayed", "DarkTheme")]
    [DataRow("macro-instant", "hold=1.5;out=Macro;macro=81/81;rumble=0;tap=Instant", "DefaultTheme")]
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
