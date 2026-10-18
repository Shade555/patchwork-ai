using System.Windows.Controls;
using PatchworkAI.Desktop.ViewModels;

namespace PatchworkAI.Desktop.Views
{
    public partial class IssuesView : UserControl
    {
        public IssuesView()
        {
            InitializeComponent();
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                vm.SelectedCategoryFilter = item.Content?.ToString() ?? "All Categories";
                vm.ApplyIssueFilters();
            }
        }

        private void SeverityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                vm.SelectedSeverityFilter = item.Content?.ToString() ?? "All Severities";
                vm.ApplyIssueFilters();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ApplyIssueFilters();
            }
        }
    }
}
