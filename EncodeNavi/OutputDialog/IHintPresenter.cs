using System.Windows;

namespace EncodeNavi.OutputDialog
{
    internal record PlacedHint(FrameworkElement Element, OutputHint Hint);

    internal interface IHintPresenter : IDisposable
    {
        void Show(IReadOnlyList<PlacedHint> hints);
    }
}
