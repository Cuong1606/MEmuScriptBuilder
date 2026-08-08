using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MEmuScriptStudio.App.Controls;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class DurationInputControlTests
{
    [STATestMethod]
    public void Components_ComposeExpectedTotalMilliseconds()
    {
        var control = CreateControl();

        Part(control, "HoursTextBox").Text = "1";
        Part(control, "MinutesTextBox").Text = "2";
        Part(control, "SecondsTextBox").Text = "3";
        Part(control, "MillisecondsTextBox").Text = "400";

        Assert.IsTrue(control.IsInputValid);
        Assert.AreEqual(3_723_400, control.TotalMilliseconds);
    }

    [STATestMethod]
    public void TotalMilliseconds_RoundTripsLegacyValueThroughComponents()
    {
        var control = CreateControl();
        control.TotalMilliseconds = 100_000;

        Assert.AreEqual("0", Part(control, "HoursTextBox").Text);
        Assert.AreEqual("1", Part(control, "MinutesTextBox").Text);
        Assert.AreEqual("40", Part(control, "SecondsTextBox").Text);
        Assert.AreEqual("0", Part(control, "MillisecondsTextBox").Text);

        Part(control, "MillisecondsTextBox").Text = string.Empty;
        Assert.AreEqual(100_000, control.TotalMilliseconds);
        Part(control, "MillisecondsTextBox").Text = "0";
        Assert.AreEqual(100_000, control.TotalMilliseconds);
    }

    [STATestMethod]
    public void TwoWayBinding_UpdatesBothComponentsAndSource()
    {
        var source = new BindingSource { Milliseconds = 100_000 };
        var control = CreateControl();
        BindingOperations.SetBinding(control, DurationInputControl.TotalMillisecondsProperty, new Binding(nameof(BindingSource.Milliseconds))
        {
            Source = source,
            Mode = BindingMode.TwoWay
        });
        BindingOperations.SetBinding(control, DurationInputControl.IsInputValidProperty, new Binding(nameof(BindingSource.IsValid))
        {
            Source = source,
            Mode = BindingMode.OneWayToSource
        });
        DrainBindings();

        Assert.AreEqual("1", Part(control, "MinutesTextBox").Text);
        Assert.AreEqual("40", Part(control, "SecondsTextBox").Text);
        Part(control, "SecondsTextBox").Text = "41";
        DrainBindings();
        Assert.AreEqual(101_000, source.Milliseconds);

        source.Milliseconds = 3_723_400;
        DrainBindings();
        Assert.AreEqual("1", Part(control, "HoursTextBox").Text);
        Assert.AreEqual("2", Part(control, "MinutesTextBox").Text);
        Assert.AreEqual("3", Part(control, "SecondsTextBox").Text);
        Assert.AreEqual("400", Part(control, "MillisecondsTextBox").Text);

        Part(control, "MinutesTextBox").Text = "60";
        DrainBindings();
        Assert.IsFalse(source.IsValid);
        Assert.AreEqual(3_723_400, source.Milliseconds, "Invalid component text must not overwrite the last valid total.");
    }

    [STATestMethod]
    public void Validation_RejectsRangesNonDigitsAndTotalsBeyondIntMaxValue()
    {
        var control = CreateControl();
        control.TotalMilliseconds = int.MaxValue;

        Assert.AreEqual("596", Part(control, "HoursTextBox").Text);
        Assert.AreEqual("31", Part(control, "MinutesTextBox").Text);
        Assert.AreEqual("23", Part(control, "SecondsTextBox").Text);
        Assert.AreEqual("647", Part(control, "MillisecondsTextBox").Text);
        Assert.IsTrue(control.IsInputValid);

        Part(control, "MillisecondsTextBox").Text = "648";
        Assert.IsFalse(control.IsInputValid);
        Assert.AreEqual(int.MaxValue, control.TotalMilliseconds);
        StringAssert.Contains(control.ValidationMessage, int.MaxValue.ToString());

        Part(control, "MillisecondsTextBox").Text = "647";
        Part(control, "MinutesTextBox").Text = "abc";
        Assert.IsFalse(control.IsInputValid);
        Assert.IsTrue(control.MinutesHasError);
        Assert.IsFalse(DurationInputControl.IsDigitsOnly("12a"));
        Assert.IsTrue(DurationInputControl.IsDigitsOnly("012"));

        Part(control, "MinutesTextBox").Text = string.Empty;
        Assert.IsTrue(control.IsInputValid, "An empty component is treated as zero.");
    }

    private static DurationInputControl CreateControl()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        return new DurationInputControl();
    }

    private static TextBox Part(DurationInputControl control, string name) =>
        (TextBox)control.FindName(name);

    private static void DrainBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private sealed class BindingSource : INotifyPropertyChanged
    {
        private int milliseconds;
        private bool isValid = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Milliseconds
        {
            get => milliseconds;
            set
            {
                if (milliseconds == value) return;
                milliseconds = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Milliseconds)));
            }
        }

        public bool IsValid
        {
            get => isValid;
            set
            {
                if (isValid == value) return;
                isValid = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValid)));
            }
        }
    }
}
