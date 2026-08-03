using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public sealed class TapPointSelection
{
    public ScreenPoint? Point { get; private set; }
    public bool CanConfirm => Point.HasValue;

    public void Select(ScreenPoint point) => Point = point;

    public CapturedTap Confirm()
    {
        if (!CanConfirm)
            throw new InvalidOperationException("Hãy chọn một tọa độ trước khi xác nhận.");

        return new CapturedTap(Point!.Value.X, Point.Value.Y);
    }
}
