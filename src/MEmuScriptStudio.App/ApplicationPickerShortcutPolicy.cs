using System.Windows.Input;

namespace MEmuScriptStudio.App;

public static class ApplicationPickerShortcutPolicy
{
    public static bool IsSaveShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.S && modifiers == ModifierKeys.Control;
}
