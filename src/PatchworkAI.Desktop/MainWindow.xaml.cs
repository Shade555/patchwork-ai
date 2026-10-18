using System.Windows;
using PatchworkAI.Core.Services;
using PatchworkAI.Desktop.ViewModels;

namespace PatchworkAI.Desktop
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            // Construct Core Services
            var fileImportService = new FileImportService();
            var dataProfiler = new DataProfiler();
            var localAiService = new LocalAiService();
            var transformationPlanner = new TransformationPlanner();
            var transformationExecutor = new TransformationExecutor();
            var exportService = new ExportService();
            var settingsService = new SettingsService();
            var sampleDataService = new SampleDataService();
            var benchmarkService = new BenchmarkService(sampleDataService, dataProfiler, transformationPlanner, localAiService);

            ViewModel = new MainViewModel(
                fileImportService,
                dataProfiler,
                localAiService,
                transformationPlanner,
                transformationExecutor,
                exportService,
                settingsService,
                sampleDataService,
                benchmarkService);

            DataContext = ViewModel;

            Loaded += async (s, e) =>
            {
                await ViewModel.InitializeAsync();
            };
        }
    }
}