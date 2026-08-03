namespace MEmuScriptStudio.Core.MEmu;

public enum InputCaptureKey
{
    Other,
    Escape,
    Enter
}

public enum InputCaptureKeyAction
{
    PassThrough,
    Suppress,
    Cancel,
    Confirm
}

public static class InputCaptureKeyPolicy
{
    public static InputCaptureKeyAction Resolve(
        bool requiresConfirmation,
        InputCaptureKey key,
        bool isKeyDown,
        bool canConfirm)
    {
        if (key == InputCaptureKey.Escape)
            return isKeyDown ? InputCaptureKeyAction.Cancel : InputCaptureKeyAction.Suppress;

        if (requiresConfirmation && key == InputCaptureKey.Enter)
            return isKeyDown && canConfirm ? InputCaptureKeyAction.Confirm : InputCaptureKeyAction.Suppress;

        return InputCaptureKeyAction.PassThrough;
    }
}

public sealed class InputCaptureKeyLatch
{
    public InputCaptureKey? PendingKey { get; private set; }

    public void Begin(InputCaptureKey key)
    {
        if (key is InputCaptureKey.Other) throw new ArgumentOutOfRangeException(nameof(key));
        PendingKey = key;
    }

    public bool Release(InputCaptureKey key)
    {
        if (PendingKey != key) return false;
        PendingKey = null;
        return true;
    }
}
