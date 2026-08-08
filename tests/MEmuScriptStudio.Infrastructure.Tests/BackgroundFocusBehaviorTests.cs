using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MEmuScriptStudio.App.Behaviors;
using MEmuScriptStudio.App.Controls;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class BackgroundFocusBehaviorTests
{
    [STATestMethod]
    public void ClickAway_CommitsBindingWithoutConsumingInteractiveTarget()
    {
        EnsureApplication();
        var source = new TextSource { Value = "before" };
        var input = new TextBox();
        var button = new Button();
        var root = new Grid();
        root.Children.Add(input);
        root.Children.Add(button);
        BindingOperations.SetBinding(input, TextBox.TextProperty, new Binding(nameof(TextSource.Value))
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        });
        DrainBindings();

        input.Text = "after";
        var cleared = BackgroundFocusBehavior.ProcessPreviewMouseDown(root, button, input);

        Assert.IsTrue(cleared);
        Assert.AreEqual("after", source.Value);
        Assert.IsFalse(BackgroundFocusBehavior.GetHasValidationErrors(root));
        Assert.IsTrue(button.IsEnabled, "The behavior must not disable or consume the first interactive click.");
    }

    [STATestMethod]
    public void InvalidBinding_RemainsInvalidAndDoesNotRestoreOldSourceValue()
    {
        EnsureApplication();
        var source = new NumberSource { Value = 7 };
        var input = new TextBox();
        var root = new Grid();
        root.Children.Add(input);
        BindingOperations.SetBinding(input, TextBox.TextProperty, new Binding(nameof(NumberSource.Value))
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            ValidatesOnExceptions = true
        });
        DrainBindings();

        input.Text = "not-a-number";
        var cleared = BackgroundFocusBehavior.ProcessPreviewMouseDown(root, root, input);

        Assert.IsFalse(cleared);
        Assert.AreEqual("not-a-number", input.Text);
        Assert.AreEqual(7, source.Value);
        Assert.IsTrue(Validation.GetHasError(input));
        Assert.IsTrue(BackgroundFocusBehavior.GetHasValidationErrors(root));
    }

    [STATestMethod]
    public void ValidationStateRescansWithoutFocusedTextInputAndIgnoresCollapsedBranches()
    {
        EnsureApplication();
        var source = new NumberSource { Value = 7 };
        var input = new TextBox();
        var inputPanel = new StackPanel();
        inputPanel.Children.Add(input);
        var root = new Grid();
        root.Children.Add(inputPanel);
        BindingOperations.SetBinding(input, TextBox.TextProperty, new Binding(nameof(NumberSource.Value))
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            ValidatesOnExceptions = true
        });
        DrainBindings();
        input.Text = "invalid";

        BackgroundFocusBehavior.ProcessPreviewMouseDown(root, root, input);
        Assert.IsTrue(BackgroundFocusBehavior.GetHasValidationErrors(root));

        inputPanel.Visibility = Visibility.Collapsed;
        BackgroundFocusBehavior.ProcessPreviewMouseDown(root, root, root);

        Assert.IsFalse(BackgroundFocusBehavior.GetHasValidationErrors(root));
    }

    [STATestMethod]
    public void TextChangesPublishVisibleValidationStateWithoutWaitingForClickAway()
    {
        EnsureApplication();
        var source = new NumberSource { Value = 7 };
        var input = new TextBox();
        var root = new Grid();
        root.Children.Add(input);
        BackgroundFocusBehavior.SetIsEnabled(root, true);
        BindingOperations.SetBinding(input, TextBox.TextProperty, new Binding(nameof(NumberSource.Value))
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            ValidatesOnExceptions = true
        });
        DrainBindings();

        input.Text = "invalid";
        DrainBindings();

        Assert.IsTrue(Validation.GetHasError(input));
        Assert.IsTrue(BackgroundFocusBehavior.GetHasValidationErrors(root));

        input.Text = "8";
        DrainBindings();

        Assert.IsFalse(Validation.GetHasError(input));
        Assert.IsFalse(BackgroundFocusBehavior.GetHasValidationErrors(root));
        Assert.AreEqual(8, source.Value);
    }

    [STATestMethod]
    public void SwitchingDurationParts_DoesNotClearFocusOrChangeTheComposedValue()
    {
        EnsureApplication();
        var duration = new DurationInputControl { TotalMilliseconds = 100_000 };
        var root = new Grid();
        root.Children.Add(duration);
        var minutes = (TextBox)duration.FindName("MinutesTextBox");
        var seconds = (TextBox)duration.FindName("SecondsTextBox");

        var cleared = BackgroundFocusBehavior.ProcessPreviewMouseDown(root, seconds, minutes);

        Assert.IsFalse(cleared);
        Assert.AreEqual(100_000, duration.TotalMilliseconds);
        Assert.AreEqual("1", minutes.Text);
        Assert.AreEqual("40", seconds.Text);
        Assert.IsTrue(duration.IsInputValid);
    }

    private static void EnsureApplication()
    {
        if (Application.Current is not null) return;
        var application = new MEmuScriptStudio.App.App();
        application.InitializeComponent();
    }

    private static void DrainBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private sealed class TextSource : INotifyPropertyChanged
    {
        private string value = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Value
        {
            get => value;
            set
            {
                if (this.value == value) return;
                this.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    private sealed class NumberSource
    {
        public int Value { get; set; }
    }
}
