using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MEmuScriptStudio.App.Behaviors;

public static class BackgroundFocusBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(BackgroundFocusBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HasValidationErrorsProperty = DependencyProperty.RegisterAttached(
        "HasValidationErrors",
        typeof(bool),
        typeof(BackgroundFocusBehavior),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetHasValidationErrors(DependencyObject element) => (bool)element.GetValue(HasValidationErrorsProperty);
    public static void SetHasValidationErrors(DependencyObject element, bool value) => element.SetValue(HasValidationErrorsProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not UIElement root) return;
        if ((bool)args.OldValue)
        {
            root.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown));
            root.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnInputTextChanged));
        }
        if ((bool)args.NewValue)
        {
            root.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), handledEventsToo: true);
            root.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnInputTextChanged), handledEventsToo: true);
        }
    }

    private static void OnInputTextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is not UIElement root) return;
        root.Dispatcher.BeginInvoke(
            () => RefreshValidationState(root),
            System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not UIElement root) return;
        ProcessPreviewMouseDown(root, args.OriginalSource as DependencyObject, Keyboard.FocusedElement as DependencyObject);
    }

    internal static bool ProcessPreviewMouseDown(
        UIElement root,
        DependencyObject? source,
        DependencyObject? focusedElement)
    {
        ArgumentNullException.ThrowIfNull(root);
        var focusedInput = FindInputAncestor(focusedElement);

        if (focusedInput is not null) CommitInputBinding(focusedInput);
        var hasValidationErrors = HasValidationError(root);
        root.SetCurrentValue(HasValidationErrorsProperty, hasValidationErrors);

        if (focusedInput is null || hasValidationErrors || FindInputAncestor(source) is not null) return false;
        Keyboard.ClearFocus();
        return true;
    }

    internal static bool CommitInputBinding(DependencyObject input)
    {
        switch (input)
        {
            case TextBox textBox:
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case ComboBox comboBox when comboBox.IsEditable:
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                break;
        }
        return !Validation.GetHasError(input);
    }

    internal static bool CommitFocusedInputAndRefresh(UIElement root, DependencyObject? focusedElement)
    {
        var focusedInput = FindInputAncestor(focusedElement);
        var focusedInputIsValid = focusedInput is null || CommitInputBinding(focusedInput);
        var hasValidationErrors = !focusedInputIsValid || HasValidationError(root);
        root.SetCurrentValue(HasValidationErrorsProperty, hasValidationErrors);
        return !hasValidationErrors;
    }

    internal static bool RefreshValidationState(UIElement root)
    {
        var hasValidationErrors = HasValidationError(root);
        root.SetCurrentValue(HasValidationErrorsProperty, hasValidationErrors);
        return !hasValidationErrors;
    }

    internal static bool RefreshInputBindingsAndValidation(UIElement root)
    {
        RefreshInputBindingTargets(root);
        return RefreshValidationState(root);
    }

    private static void RefreshInputBindingTargets(DependencyObject root)
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) return;
        switch (root)
        {
            case TextBox textBox:
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                break;
            case ComboBox comboBox:
                comboBox.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
                if (comboBox.IsEditable)
                    comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
                break;
        }

        var childCount = root is Visual or Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (var index = 0; index < childCount; index++)
            RefreshInputBindingTargets(VisualTreeHelper.GetChild(root, index));
    }

    private static DependencyObject? FindInputAncestor(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is TextBoxBase or PasswordBox) return current;
            if (current is ComboBox { IsEditable: true } comboBox) return comboBox;
            current = GetParent(current);
        }
        return null;
    }

    private static bool HasValidationError(DependencyObject root)
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) return false;
        if (Validation.GetHasError(root)) return true;
        var childCount = root is Visual or Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (var index = 0; index < childCount; index++)
        {
            if (HasValidationError(VisualTreeHelper.GetChild(root, index))) return true;
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
            return VisualTreeHelper.GetParent(current);
        return LogicalTreeHelper.GetParent(current);
    }
}
