using System.Windows.Documents;

namespace EncodeNavi.OutputDialog
{
    internal sealed class AdornerHintPresenter(bool isMonochrome) : IHintPresenter
    {
        private readonly List<(AdornerLayer Layer, Adorner Adorner)> adorners = [];

        public void Show(IReadOnlyList<PlacedHint> hints)
        {
            Clear();
            foreach (var (element, hint) in hints)
            {
                if (AdornerLayer.GetAdornerLayer(element) is not { } layer)
                    continue;
                var adorner = new HintAdorner(element, hint, isMonochrome);
                layer.Add(adorner);
                adorners.Add((layer, adorner));
            }
        }

        public void Dispose() => Clear();

        private void Clear()
        {
            foreach (var (layer, adorner) in adorners)
                layer.Remove(adorner);
            adorners.Clear();
        }
    }
}
