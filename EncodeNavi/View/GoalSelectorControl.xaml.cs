using System.Windows;
using System.Windows.Controls;
using EncodeNavi.ViewModel;

namespace EncodeNavi.View
{
    public partial class GoalSelectorControl : UserControl
    {
        private readonly Action showDetails;

        internal GoalSelectorControl(Action showDetails)
        {
            this.showDetails = showDetails;
            InitializeComponent();

            Loaded += (_, _) => DataContext = new GoalSelectorViewModel();
            Unloaded += (_, _) =>
            {
                (DataContext as IDisposable)?.Dispose();
                DataContext = null;
            };
        }

        private void DetailsButton_Click(object sender, RoutedEventArgs e) => showDetails();
    }
}
