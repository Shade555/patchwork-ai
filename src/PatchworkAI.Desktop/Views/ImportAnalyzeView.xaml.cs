using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PatchworkAI.Desktop.ViewModels;

namespace PatchworkAI.Desktop.Views
{
    public partial class ImportAnalyzeView : UserControl
    {
        public ImportAnalyzeView()
        {
            InitializeComponent();
        }

        private async void FileDropBorder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var file = files[0];
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".csv" || ext == ".xlsx")
                    {
                        if (DataContext is MainViewModel vm)
                        {
                            await vm.ImportFileAsync(file);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Unsupported file format. Please drop a .csv or .xlsx file.", "File Format Not Supported", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void FileDropBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void SheetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedSheet != null)
            {
                await vm.SwitchSheetAsync();
            }
        }
    }
}
