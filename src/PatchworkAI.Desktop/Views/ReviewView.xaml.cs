using System.Windows;
using System.Windows.Controls;
using PatchworkAI.Core.Models;
using PatchworkAI.Desktop.ViewModels;

namespace PatchworkAI.Desktop.Views
{
    public partial class ReviewView : UserControl
    {
        public ReviewView()
        {
            InitializeComponent();
        }

        private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                vm.SelectedReviewStatusFilter = item.Content?.ToString() ?? "All Statuses";
                vm.ApplyTransformationFilters();
            }
        }

        private void RowApprove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProposedTransformation trans && DataContext is MainViewModel vm)
            {
                vm.ApproveTransformation(trans);
            }
        }

        private void RowReject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProposedTransformation trans && DataContext is MainViewModel vm)
            {
                vm.RejectTransformation(trans);
            }
        }
    }
}
