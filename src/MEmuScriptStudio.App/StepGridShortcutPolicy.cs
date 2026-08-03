using System.Windows.Input;

namespace MEmuScriptStudio.App;

public enum StepGridShortcut
{
    None,
    Copy,
    Paste,
    Delete,
    Undo,
    ClearSelection
}

public static class StepGridShortcutPolicy
{
    public static StepGridShortcut Resolve(
        bool isGridFocusWithin,
        bool isTextInput,
        bool canCopyOrDelete,
        bool canPaste,
        bool canUndo,
        Key key,
        ModifierKeys modifiers)
    {
        if (!isGridFocusWithin || isTextInput) return StepGridShortcut.None;
        if (canCopyOrDelete && modifiers == ModifierKeys.Control && key == Key.C) return StepGridShortcut.Copy;
        if (canPaste && modifiers == ModifierKeys.Control && key == Key.V) return StepGridShortcut.Paste;
        if (canUndo && modifiers == ModifierKeys.Control && key == Key.Z) return StepGridShortcut.Undo;
        if (canCopyOrDelete && modifiers == ModifierKeys.None && key == Key.Delete) return StepGridShortcut.Delete;
        if (canCopyOrDelete && modifiers == ModifierKeys.None && key == Key.Escape) return StepGridShortcut.ClearSelection;
        return StepGridShortcut.None;
    }

    public static bool ShouldPreserveSelectionForDrag(
        int selectedCount,
        bool clickedSelectedRow,
        bool clickedInteractiveControl,
        ModifierKeys modifiers) =>
        selectedCount > 1 && clickedSelectedRow && !clickedInteractiveControl && modifiers == ModifierKeys.None;
}
