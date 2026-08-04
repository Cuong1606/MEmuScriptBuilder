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
        bool canCopy,
        bool canPaste,
        bool canUndo,
        bool canDelete,
        Key key,
        ModifierKeys modifiers)
    {
        if (isTextInput) return StepGridShortcut.None;
        if (canCopy && modifiers == ModifierKeys.Control && key == Key.C) return StepGridShortcut.Copy;
        if (canPaste && modifiers == ModifierKeys.Control && key == Key.V) return StepGridShortcut.Paste;
        if (canUndo && modifiers == ModifierKeys.Control && key == Key.Z) return StepGridShortcut.Undo;
        if (canDelete && modifiers == ModifierKeys.None && key == Key.Delete) return StepGridShortcut.Delete;
        if (isGridFocusWithin && canCopy && modifiers == ModifierKeys.None && key == Key.Escape)
            return StepGridShortcut.ClearSelection;
        return StepGridShortcut.None;
    }

    public static bool ShouldPreserveSelectionForDrag(
        int selectedCount,
        bool clickedSelectedRow,
        bool clickedInteractiveControl,
        ModifierKeys modifiers) =>
        selectedCount > 1 && clickedSelectedRow && !clickedInteractiveControl && modifiers == ModifierKeys.None;
}
