using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App.Services;

internal sealed class MainWindowCloseCoordinator
{
    public bool IsResolutionInProgress { get; private set; }
    public bool IsCloseApproved { get; private set; }

    public bool RequiresDeferral(MainViewModel viewModel, bool hasControlCenter) =>
        !IsCloseApproved &&
        (IsResolutionInProgress || viewModel.HasPendingNavigationDraft || viewModel.IsExecuting || hasControlCenter);

    public async Task<bool> TryResolveAsync(MainViewModel viewModel, Func<Task> closeControlCenterAsync)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(closeControlCenterAsync);
        if (IsResolutionInProgress || IsCloseApproved) return false;

        IsResolutionInProgress = true;
        try
        {
            if (viewModel.HasPendingNavigationDraft && !await viewModel.TryPrepareForCloseAsync())
                return false;

            await viewModel.StopAllForSafeShutdownAsync();
            await closeControlCenterAsync();
            if (viewModel.HasPendingNavigationDraft && !await viewModel.TryPrepareForCloseAsync())
            {
                viewModel.ResumeAfterCancelledSafeShutdown();
                return false;
            }
            IsCloseApproved = true;
            return true;
        }
        finally
        {
            IsResolutionInProgress = false;
        }
    }
}
