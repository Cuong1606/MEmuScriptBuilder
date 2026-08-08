using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MEmuScriptStudio.App.Controls;

public partial class DurationInputControl : UserControl, INotifyPropertyChanged
{
    private const long MillisecondsPerHour = 3_600_000;
    private const long MillisecondsPerMinute = 60_000;
    private const long MillisecondsPerSecond = 1_000;
    private bool isUpdatingParts;
    private bool hoursHasError;
    private bool minutesHasError;
    private bool secondsHasError;
    private bool millisecondsHasError;
    private string validationMessage = string.Empty;

    public DurationInputControl()
    {
        InitializeComponent();
        foreach (var textBox in PartTextBoxes)
        {
            textBox.TextChanged += PartTextBox_TextChanged;
            textBox.PreviewTextInput += PartTextBox_PreviewTextInput;
            DataObject.AddPastingHandler(textBox, PartTextBox_Pasting);
        }
        ApplyTotalMilliseconds(TotalMilliseconds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly DependencyProperty TotalMillisecondsProperty = DependencyProperty.Register(
        nameof(TotalMilliseconds),
        typeof(int),
        typeof(DurationInputControl),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTotalMillisecondsChanged));

    public static readonly DependencyProperty IsInputValidProperty = DependencyProperty.Register(
        nameof(IsInputValid),
        typeof(bool),
        typeof(DurationInputControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty RefreshTokenProperty = DependencyProperty.Register(
        nameof(RefreshToken),
        typeof(long),
        typeof(DurationInputControl),
        new PropertyMetadata(0L, OnRefreshTokenChanged));

    public int TotalMilliseconds
    {
        get => (int)GetValue(TotalMillisecondsProperty);
        set => SetValue(TotalMillisecondsProperty, value);
    }

    public bool IsInputValid
    {
        get => (bool)GetValue(IsInputValidProperty);
        set => SetValue(IsInputValidProperty, value);
    }

    public long RefreshToken
    {
        get => (long)GetValue(RefreshTokenProperty);
        set => SetValue(RefreshTokenProperty, value);
    }

    public bool HoursHasError
    {
        get => hoursHasError;
        private set => SetField(ref hoursHasError, value, nameof(HoursHasError));
    }

    public bool MinutesHasError
    {
        get => minutesHasError;
        private set => SetField(ref minutesHasError, value, nameof(MinutesHasError));
    }

    public bool SecondsHasError
    {
        get => secondsHasError;
        private set => SetField(ref secondsHasError, value, nameof(SecondsHasError));
    }

    public bool MillisecondsHasError
    {
        get => millisecondsHasError;
        private set => SetField(ref millisecondsHasError, value, nameof(MillisecondsHasError));
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetField(ref validationMessage, value, nameof(ValidationMessage));
    }

    internal static bool IsDigitsOnly(string text) => text.All(character => character is >= '0' and <= '9');

    private IEnumerable<TextBox> PartTextBoxes =>
        [HoursTextBox, MinutesTextBox, SecondsTextBox, MillisecondsTextBox];

    private static void OnTotalMillisecondsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (DurationInputControl)sender;
        if (!control.isUpdatingParts) control.ApplyTotalMilliseconds((int)args.NewValue);
    }

    private static void OnRefreshTokenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (DurationInputControl)sender;
        if (!control.isUpdatingParts) control.ApplyTotalMilliseconds(control.TotalMilliseconds);
    }

    private void ApplyTotalMilliseconds(int value)
    {
        isUpdatingParts = true;
        try
        {
            if (value < 0)
            {
                HoursTextBox.Text = "0";
                MinutesTextBox.Text = "0";
                SecondsTextBox.Text = "0";
                MillisecondsTextBox.Text = "0";
                SetValidationState(true, false, false, false, "Thời lượng không được âm.");
                return;
            }

            var remaining = value;
            HoursTextBox.Text = (remaining / MillisecondsPerHour).ToString(CultureInfo.InvariantCulture);
            remaining %= (int)MillisecondsPerHour;
            MinutesTextBox.Text = (remaining / MillisecondsPerMinute).ToString(CultureInfo.InvariantCulture);
            remaining %= (int)MillisecondsPerMinute;
            SecondsTextBox.Text = (remaining / MillisecondsPerSecond).ToString(CultureInfo.InvariantCulture);
            MillisecondsTextBox.Text = (remaining % MillisecondsPerSecond).ToString(CultureInfo.InvariantCulture);
            SetValidationState(false, false, false, false, string.Empty);
        }
        finally
        {
            isUpdatingParts = false;
        }
    }

    private void PartTextBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (isUpdatingParts) return;
        RecalculateTotalMilliseconds();
    }

    private void RecalculateTotalMilliseconds()
    {
        var hoursValid = TryParsePart(HoursTextBox.Text, long.MaxValue, out var hours);
        var minutesValid = TryParsePart(MinutesTextBox.Text, 59, out var minutes);
        var secondsValid = TryParsePart(SecondsTextBox.Text, 59, out var seconds);
        var millisecondsValid = TryParsePart(MillisecondsTextBox.Text, 999, out var milliseconds);

        if (!hoursValid || !minutesValid || !secondsValid || !millisecondsValid)
        {
            var message = !hoursValid
                ? "Giờ phải là số không âm."
                : !minutesValid ? "Phút phải nằm trong khoảng 0–59."
                : !secondsValid ? "Giây phải nằm trong khoảng 0–59."
                : "Mili giây phải nằm trong khoảng 0–999.";
            SetValidationState(!hoursValid, !minutesValid, !secondsValid, !millisecondsValid, message);
            return;
        }

        try
        {
            var total = checked(
                checked(hours * MillisecondsPerHour) +
                checked(minutes * MillisecondsPerMinute) +
                checked(seconds * MillisecondsPerSecond) +
                milliseconds);
            if (total > int.MaxValue)
            {
                SetValidationState(true, false, false, false, $"Tổng thời lượng không được vượt quá {int.MaxValue} ms.");
                return;
            }

            SetValidationState(false, false, false, false, string.Empty);
            isUpdatingParts = true;
            try
            {
                SetCurrentValue(TotalMillisecondsProperty, (int)total);
            }
            finally
            {
                isUpdatingParts = false;
            }
        }
        catch (OverflowException)
        {
            SetValidationState(true, false, false, false, $"Tổng thời lượng không được vượt quá {int.MaxValue} ms.");
        }
    }

    private static bool TryParsePart(string text, long maximum, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
            return true;
        return IsDigitsOnly(text) &&
               long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
               value <= maximum;
    }

    private void SetValidationState(
        bool hoursError,
        bool minutesError,
        bool secondsError,
        bool millisecondsError,
        string message)
    {
        HoursHasError = hoursError;
        MinutesHasError = minutesError;
        SecondsHasError = secondsError;
        MillisecondsHasError = millisecondsError;
        ValidationMessage = message;
        SetCurrentValue(IsInputValidProperty, string.IsNullOrEmpty(message));
    }

    private static void PartTextBox_PreviewTextInput(object sender, TextCompositionEventArgs args) =>
        args.Handled = !IsDigitsOnly(args.Text);

    private static void PartTextBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) ||
            args.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text ||
            !IsDigitsOnly(text))
            args.CancelCommand();
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
