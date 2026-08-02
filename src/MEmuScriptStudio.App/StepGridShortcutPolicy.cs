using System.Windows.Input;

namespace MEmuScriptStudio.App;

public enum StepGridShortcut
{
    None,
    Copy,
    Paste,
    Delete
}

public static class StepGridShortcutPolicy
{
    public static StepGridShortcut Resolve(
        bool isGridFocusWithin,
        bool isTextInput,
        bool canMutate,
        Key key,
        ModifierKeys modifiers)
    {
        if (!isGridFocusWithin || isTextInput || !canMutate) return StepGridShortcut.None;
        if (modifiers == ModifierKeys.Control && key == Key.C) return StepGridShortcut.Copy;
        if (modifiers == ModifierKeys.Control && key == Key.V) return StepGridShortcut.Paste;
        if (modifiers == ModifierKeys.None && key == Key.Delete) return StepGridShortcut.Delete;
        return StepGridShortcut.None;
    }
}
