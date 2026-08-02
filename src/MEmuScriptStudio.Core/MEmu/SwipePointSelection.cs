using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public sealed class SwipePointSelection
{
    public ScreenPoint? StartPoint { get; private set; }
    public ScreenPoint? EndPoint { get; private set; }
    public bool CanConfirm => StartPoint.HasValue && EndPoint.HasValue;

    public void SelectStart(ScreenPoint point) => StartPoint = point;

    public void SelectEnd(ScreenPoint point) => EndPoint = point;

    public CapturedSwipe Confirm()
    {
        if (!CanConfirm)
            throw new InvalidOperationException("Hãy chọn cả điểm bắt đầu và điểm kết thúc trước khi xác nhận.");

        return new CapturedSwipe(StartPoint!.Value.X, StartPoint.Value.Y, EndPoint!.Value.X, EndPoint.Value.Y);
    }
}
