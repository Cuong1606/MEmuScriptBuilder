using System.Windows;

namespace MEmuScriptStudio.App.Services;

public interface IConfirmationService
{
    bool Confirm(string message, string title);

    EditorDraftDecision DecideEditorDraft(string description, bool canSave)
    {
        return Confirm(
            $"{description} có thay đổi chưa lưu. Bạn có muốn bỏ các thay đổi này?",
            "Thay đổi chưa lưu")
            ? EditorDraftDecision.Discard
            : EditorDraftDecision.Cancel;
    }
}

public enum EditorDraftDecision
{
    Save,
    Discard,
    Cancel
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public EditorDraftDecision DecideEditorDraft(string description, bool canSave)
    {
        if (!canSave)
        {
            return MessageBox.Show(
                       $"{description} đang không hợp lệ và không thể lưu. Bỏ các thay đổi?",
                       "Thay đổi không hợp lệ",
                       MessageBoxButton.OKCancel,
                       MessageBoxImage.Warning) == MessageBoxResult.OK
                ? EditorDraftDecision.Discard
                : EditorDraftDecision.Cancel;
        }

        return MessageBox.Show(
                   $"{description} có thay đổi chưa lưu.",
                   "Thay đổi chưa lưu",
                   MessageBoxButton.YesNoCancel,
                   MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => EditorDraftDecision.Save,
            MessageBoxResult.No => EditorDraftDecision.Discard,
            _ => EditorDraftDecision.Cancel
        };
    }
}
