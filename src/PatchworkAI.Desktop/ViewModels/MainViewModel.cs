using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;

namespace PatchworkAI.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IFileImportService _fileImportService;
        private readonly IDataProfiler _dataProfiler;
        private readonly ILocalAiService _localAiService;
        private readonly ITransformationPlanner _transformationPlanner;
        private readonly ITransformationExecutor _transformationExecutor;
        private readonly IExportService _exportService;
        private readonly ISettingsService _settingsService;
        private readonly ISampleDataService _sampleDataService;
        private readonly IBenchmarkService _benchmarkService;

        [ObservableProperty]
        private string _activeView = "Home";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyStatusText = "Loading...";

        [ObservableProperty]
        private string? _globalStatusMessage;

        [ObservableProperty]
        private bool _isStatusError;

        // Current Dataset State
        [ObservableProperty]
        private ImportedDataset? _currentDataset;

        [ObservableProperty]
        private DataTable? _previewDataTable;

        [ObservableProperty]
        private DataTable? _reviewDataTable;

        [ObservableProperty]
        private ObservableCollection<string> _availableSheets = new();

        [ObservableProperty]
        private string? _selectedSheet;

        // Profiler & Transformation State
        [ObservableProperty]
        private ObservableCollection<DataIssue> _detectedIssues = new();

        [ObservableProperty]
        private ObservableCollection<DataIssue> _filteredIssues = new();

        [ObservableProperty]
        private DataIssue? _selectedIssue;

        [ObservableProperty]
        private ObservableCollection<ProposedTransformation> _proposedTransformations = new();

        [ObservableProperty]
        private ObservableCollection<ProposedTransformation> _filteredTransformations = new();

        [ObservableProperty]
        private ProposedTransformation? _selectedTransformation;

        [ObservableProperty]
        private ObservableCollection<ChangeLogEntry> _changeLogEntries = new();

        [ObservableProperty]
        private ExportReport? _lastExportReport;

        // Model & Settings State
        [ObservableProperty]
        private LocalModelStatus _modelStatus = new();

        [ObservableProperty]
        private UserSettings _settings = new();

        [ObservableProperty]
        private BenchmarkResult? _lastBenchmarkResult;

        // Filters
        [ObservableProperty]
        private string _selectedCategoryFilter = "All Categories";

        [ObservableProperty]
        private string _selectedSeverityFilter = "All Severities";

        [ObservableProperty]
        private string _selectedReviewStatusFilter = "All Statuses";

        [ObservableProperty]
        private string _searchIssueText = string.Empty;

        // Statistics
        public int TotalIssuesCount { get => DetectedIssues.Count; set { } }
        public int ErrorIssuesCount { get => DetectedIssues.Count(i => i.Severity == IssueSeverity.Error); set { } }
        public int WarningIssuesCount { get => DetectedIssues.Count(i => i.Severity == IssueSeverity.Warning); set { } }
        public int InfoIssuesCount { get => DetectedIssues.Count(i => i.Severity == IssueSeverity.Info); set { } }
        public int ApprovedTransformationsCount { get => ProposedTransformations.Count(t => t.Status == TransformationStatus.Approved || t.Status == TransformationStatus.Applied); set { } }
        public int PendingTransformationsCount { get => ProposedTransformations.Count(t => t.Status == TransformationStatus.Pending); set { } }
        public int RejectedTransformationsCount { get => ProposedTransformations.Count(t => t.Status == TransformationStatus.Rejected); set { } }
        public bool HasActiveDataset { get => CurrentDataset != null && CurrentDataset.Rows.Count > 0; set { } }
        public bool CanUndo { get => _transformationExecutor.CanUndo; set { } }

        public MainViewModel(
            IFileImportService fileImportService,
            IDataProfiler dataProfiler,
            ILocalAiService localAiService,
            ITransformationPlanner transformationPlanner,
            ITransformationExecutor transformationExecutor,
            IExportService exportService,
            ISettingsService settingsService,
            ISampleDataService sampleDataService,
            IBenchmarkService benchmarkService)
        {
            _fileImportService = fileImportService;
            _dataProfiler = dataProfiler;
            _localAiService = localAiService;
            _transformationPlanner = transformationPlanner;
            _transformationExecutor = transformationExecutor;
            _exportService = exportService;
            _settingsService = settingsService;
            _sampleDataService = sampleDataService;
            _benchmarkService = benchmarkService;
        }

        public async Task InitializeAsync()
        {
            Settings = await _settingsService.LoadSettingsAsync();
            await CheckOllamaStatusAsync();
        }

        [RelayCommand]
        public void Navigate(string viewName)
        {
            ActiveView = viewName;
            ClearStatusMessage();
        }

        [RelayCommand]
        public async Task CheckOllamaStatusAsync()
        {
            IsBusy = true;
            BusyStatusText = "Checking local AI status...";
            try
            {
                ModelStatus = await _localAiService.CheckConnectionAsync(Settings.OllamaEndpoint, Settings.ModelName);
                if (ModelStatus.IsAvailable)
                {
                    SetSuccessStatus($"Local AI is connected ({ModelStatus.ConfiguredModel})");
                }
                else
                {
                    SetInfoStatus($"Local AI runtime not detected at {Settings.OllamaEndpoint}. Deterministic rules remain fully functional.");
                }
            }
            catch (Exception ex)
            {
                SetErrorStatus($"AI status check failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task LoadSampleDatasetAsync()
        {
            IsBusy = true;
            BusyStatusText = "Loading synthetic supplier inventory dataset...";
            ClearStatusMessage();

            try
            {
                var dataset = _sampleDataService.CreateSyntheticSupplierDataset();
                CurrentDataset = dataset;
                PopulatePreviewTable(dataset);

                // Run deterministic profiler immediately
                await RunDeterministicAnalysisAsync();

                Navigate("Import");
                SetSuccessStatus("Loaded synthetic supplier inventory dataset with 10 intentional error scenarios.");
            }
            catch (Exception ex)
            {
                SetErrorStatus($"Failed to load sample dataset: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task BrowseAndImportFileAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Spreadsheet File",
                Filter = "Spreadsheets (*.csv;*.xlsx)|*.csv;*.xlsx|CSV Files (*.csv)|*.csv|Excel Workbooks (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                await ImportFileAsync(dialog.FileName);
            }
        }

        public async Task ImportFileAsync(string filePath, string? sheetName = null)
        {
            IsBusy = true;
            BusyStatusText = $"Reading '{Path.GetFileName(filePath)}'...";
            ClearStatusMessage();

            try
            {
                var dataset = await _fileImportService.ImportFileAsync(filePath, sheetName);
                CurrentDataset = dataset;

                AvailableSheets.Clear();
                foreach (var s in dataset.SheetNames)
                {
                    AvailableSheets.Add(s);
                }
                SelectedSheet = dataset.SelectedSheet;

                PopulatePreviewTable(dataset);

                _transformationExecutor.ResetSession(dataset);
                ChangeLogEntries.Clear();
                LastExportReport = null;

                // Auto run profiler if configured
                if (Settings.AutoDetectOnLoad)
                {
                    await RunDeterministicAnalysisAsync();
                }

                Navigate("Import");
                SetSuccessStatus($"Successfully imported '{dataset.FileName}' ({dataset.TotalRows} rows, {dataset.TotalColumns} columns).");
            }
            catch (Exception ex)
            {
                SetErrorStatus($"Import error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SwitchSheetAsync()
        {
            if (CurrentDataset != null && !string.IsNullOrEmpty(SelectedSheet) && SelectedSheet != CurrentDataset.SelectedSheet)
            {
                await ImportFileAsync(CurrentDataset.FilePath, SelectedSheet);
            }
        }

        [RelayCommand]
        public async Task RunDeterministicAnalysisAsync()
        {
            if (CurrentDataset == null) return;

            IsBusy = true;
            BusyStatusText = "Running deterministic data profiling...";
            ClearStatusMessage();

            try
            {
                var issues = await Task.Run(() => _dataProfiler.ProfileDataset(CurrentDataset));
                DetectedIssues.Clear();
                foreach (var issue in issues)
                {
                    DetectedIssues.Add(issue);
                }

                // Generate proposed transformations
                var plans = await Task.Run(() => _transformationPlanner.PlanTransformations(CurrentDataset, issues));
                ProposedTransformations.Clear();
                foreach (var p in plans)
                {
                    ProposedTransformations.Add(p);
                }

                ApplyIssueFilters();
                ApplyTransformationFilters();
                OnPropertyChanged(nameof(TotalIssuesCount));
                OnPropertyChanged(nameof(ErrorIssuesCount));
                OnPropertyChanged(nameof(WarningIssuesCount));
                OnPropertyChanged(nameof(InfoIssuesCount));
                OnPropertyChanged(nameof(PendingTransformationsCount));

                SetSuccessStatus($"Analysis complete: Found {DetectedIssues.Count} potential issues across {CurrentDataset.TotalColumns} columns.");
            }
            catch (Exception ex)
            {
                SetErrorStatus($"Profiling failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RunAiAssistedAnalysisAsync()
        {
            if (CurrentDataset == null) return;

            IsBusy = true;
            BusyStatusText = $"Consulting local AI ({Settings.ModelName})...";
            ClearStatusMessage();

            try
            {
                // First ensure deterministic profile exists
                var issues = await Task.Run(() => _dataProfiler.ProfileDataset(CurrentDataset));
                DetectedIssues.Clear();
                foreach (var issue in issues)
                {
                    DetectedIssues.Add(issue);
                }

                // Ask Local AI using strict JSON schema
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Settings.TimeoutSeconds));
                var aiResult = await _localAiService.AnalyzeDatasetAsync(CurrentDataset, Settings.OllamaEndpoint, Settings.ModelName, cts.Token);

                if (aiResult != null && aiResult.IsValid)
                {
                    // Add AI column mapping issues to issue list
                    foreach (var m in aiResult.ColumnMappings)
                    {
                        var col = CurrentDataset.Columns.FirstOrDefault(c => c.Name.Equals(m.OriginalName, StringComparison.OrdinalIgnoreCase));
                        DetectedIssues.Add(new DataIssue
                        {
                            Category = IssueCategory.AmbiguousColumnNames,
                            Column = m.OriginalName,
                            ColumnIndex = col?.Index ?? -1,
                            AffectedRowCount = 0,
                            Severity = IssueSeverity.Info,
                            Explanation = $"AI suggests standardizing column to '{m.SuggestedName}': {m.Rationale}",
                            ProposedFixDescription = $"Rename column to '{m.SuggestedName}'",
                            IsAiSuggested = true,
                            Confidence = m.Confidence,
                            CanAutoApply = false
                        });
                    }

                    // Add AI normalization issues
                    foreach (var r in aiResult.NormalizationRules)
                    {
                        var col = CurrentDataset.Columns.FirstOrDefault(c => c.Name.Equals(r.ColumnName, StringComparison.OrdinalIgnoreCase));
                        DetectedIssues.Add(new DataIssue
                        {
                            Category = IssueCategory.CategoricalInconsistencies,
                            Column = r.ColumnName,
                            ColumnIndex = col?.Index ?? -1,
                            AffectedRowCount = r.VariantValues.Count,
                            Severity = IssueSeverity.Warning,
                            Explanation = $"AI proposed category normalization: merge [{string.Join(", ", r.VariantValues)}] into '{r.CanonicalValue}'. Rationale: {r.Rationale}",
                            ProposedFixDescription = $"Normalize variants to '{r.CanonicalValue}'",
                            IsAiSuggested = true,
                            Confidence = r.Confidence,
                            CanAutoApply = false
                        });
                    }

                    var plans = await Task.Run(() => _transformationPlanner.PlanTransformations(CurrentDataset, DetectedIssues, aiResult));
                    ProposedTransformations.Clear();
                    foreach (var p in plans)
                    {
                        ProposedTransformations.Add(p);
                    }

                    SetSuccessStatus($"AI analysis succeeded: Generated {aiResult.NormalizationRules.Count} category rules and {aiResult.ColumnMappings.Count} column proposals.");
                }
                else
                {
                    // Fall back cleanly to deterministic
                    var plans = await Task.Run(() => _transformationPlanner.PlanTransformations(CurrentDataset, DetectedIssues));
                    ProposedTransformations.Clear();
                    foreach (var p in plans)
                    {
                        ProposedTransformations.Add(p);
                    }

                    SetInfoStatus("Local AI model was unreachable or did not return valid JSON. Deterministic rules applied successfully.");
                }

                ApplyIssueFilters();
                ApplyTransformationFilters();
                OnPropertyChanged(nameof(TotalIssuesCount));
                OnPropertyChanged(nameof(ErrorIssuesCount));
                OnPropertyChanged(nameof(WarningIssuesCount));
                OnPropertyChanged(nameof(InfoIssuesCount));
                OnPropertyChanged(nameof(PendingTransformationsCount));
            }
            catch (Exception ex)
            {
                SetErrorStatus($"AI-assisted analysis error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ApproveTransformation(ProposedTransformation? transformation)
        {
            if (transformation == null) return;
            transformation.Status = TransformationStatus.Approved;
            OnPropertyChanged(nameof(ApprovedTransformationsCount));
            OnPropertyChanged(nameof(PendingTransformationsCount));
            ApplyTransformationFilters();
        }

        [RelayCommand]
        public void RejectTransformation(ProposedTransformation? transformation)
        {
            if (transformation == null) return;
            transformation.Status = TransformationStatus.Rejected;
            OnPropertyChanged(nameof(RejectedTransformationsCount));
            OnPropertyChanged(nameof(PendingTransformationsCount));
            ApplyTransformationFilters();
        }

        [RelayCommand]
        public void BulkApproveSafeDeterministic()
        {
            int count = 0;
            foreach (var t in ProposedTransformations.Where(t => t.IsSafeDeterministic && t.Status == TransformationStatus.Pending))
            {
                t.Status = TransformationStatus.Approved;
                count++;
            }

            OnPropertyChanged(nameof(ApprovedTransformationsCount));
            OnPropertyChanged(nameof(PendingTransformationsCount));
            ApplyTransformationFilters();
            SetSuccessStatus($"Bulk approved {count} safe deterministic fix(es).");
        }

        [RelayCommand]
        public void ApproveAllTransformations()
        {
            foreach (var t in ProposedTransformations.Where(t => t.Status == TransformationStatus.Pending))
            {
                t.Status = TransformationStatus.Approved;
            }
            OnPropertyChanged(nameof(ApprovedTransformationsCount));
            OnPropertyChanged(nameof(PendingTransformationsCount));
            ApplyTransformationFilters();
        }

        [RelayCommand]
        public void RejectAllTransformations()
        {
            foreach (var t in ProposedTransformations.Where(t => t.Status == TransformationStatus.Pending))
            {
                t.Status = TransformationStatus.Rejected;
            }
            OnPropertyChanged(nameof(RejectedTransformationsCount));
            OnPropertyChanged(nameof(PendingTransformationsCount));
            ApplyTransformationFilters();
        }

        [RelayCommand]
        public void ApplyApprovedTransformations()
        {
            if (CurrentDataset == null) return;

            var result = _transformationExecutor.ApplyApprovedTransformations(CurrentDataset, ProposedTransformations);
            PopulatePreviewTable(CurrentDataset);
            PopulateReviewTable(CurrentDataset);

            ChangeLogEntries.Clear();
            foreach (var entry in _transformationExecutor.ChangeLog)
            {
                ChangeLogEntries.Add(entry);
            }

            OnPropertyChanged(nameof(CanUndo));
            ApplyTransformationFilters();

            if (result.Success)
            {
                SetSuccessStatus(result.Message);
            }
            else
            {
                SetErrorStatus(result.Message);
            }
        }

        [RelayCommand]
        public void UndoLastAction()
        {
            if (CurrentDataset == null || !CanUndo) return;

            var result = _transformationExecutor.UndoLast(CurrentDataset);
            PopulatePreviewTable(CurrentDataset);
            PopulateReviewTable(CurrentDataset);

            ChangeLogEntries.Clear();
            foreach (var entry in _transformationExecutor.ChangeLog)
            {
                ChangeLogEntries.Add(entry);
            }

            OnPropertyChanged(nameof(CanUndo));
            SetInfoStatus(result.Message);
        }

        [RelayCommand]
        public async Task ExportRepairedDatasetAsync(string formatStr)
        {
            if (CurrentDataset == null) return;

            var format = formatStr.Equals("XLSX", StringComparison.OrdinalIgnoreCase)
                ? DatasetFormat.Xlsx
                : DatasetFormat.Csv;

            var suggestedPath = _exportService.GenerateSuggestedExportPath(CurrentDataset.FilePath, format);

            var dialog = new SaveFileDialog
            {
                Title = "Export Cleaned Spreadsheet",
                FileName = Path.GetFileName(suggestedPath),
                InitialDirectory = Path.GetDirectoryName(suggestedPath),
                Filter = format == DatasetFormat.Xlsx
                    ? "Excel Workbook (*.xlsx)|*.xlsx"
                    : "CSV File (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                BusyStatusText = $"Exporting repaired dataset to '{Path.GetFileName(dialog.FileName)}'...";
                ClearStatusMessage();

                try
                {
                    var report = await _exportService.ExportDatasetAsync(
                        CurrentDataset,
                        dialog.FileName,
                        format,
                        TotalIssuesCount,
                        ApprovedTransformationsCount,
                        RejectedTransformationsCount,
                        WarningIssuesCount,
                        _transformationExecutor.ChangeLog);

                    LastExportReport = report;
                    Navigate("Export");
                    SetSuccessStatus($"Export completed! Repaired file saved to '{dialog.FileName}'.");
                }
                catch (Exception ex)
                {
                    SetErrorStatus($"Export failed: {ex.Message}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        [RelayCommand]
        public void OpenExportFolder()
        {
            if (LastExportReport != null && !string.IsNullOrEmpty(LastExportReport.ExportPath))
            {
                _exportService.OpenFolderInExplorer(LastExportReport.ExportPath);
            }
        }

        [RelayCommand]
        public async Task RunBenchmarkAsync()
        {
            IsBusy = true;
            BusyStatusText = "Running accuracy & performance benchmark...";
            ClearStatusMessage();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                LastBenchmarkResult = await _benchmarkService.RunBenchmarkAsync(Settings.OllamaEndpoint, Settings.ModelName, cts.Token);
                SetSuccessStatus($"Benchmark complete: Processed in {LastBenchmarkResult.DeterministicProfilingTimeMs}ms (Deterministic) / {LastBenchmarkResult.AiInferenceTimeMs}ms (AI).");
            }
            catch (Exception ex)
            {
                SetErrorStatus($"Benchmark error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            await _settingsService.SaveSettingsAsync(Settings);
            SetSuccessStatus("Settings saved successfully.");
        }

        public void ApplyIssueFilters()
        {
            var filtered = DetectedIssues.AsEnumerable();

            if (SelectedCategoryFilter != "All Categories")
            {
                filtered = filtered.Where(i => i.Category.ToString() == SelectedCategoryFilter);
            }

            if (SelectedSeverityFilter != "All Severities")
            {
                filtered = filtered.Where(i => i.Severity.ToString() == SelectedSeverityFilter);
            }

            if (!string.IsNullOrWhiteSpace(SearchIssueText))
            {
                var query = SearchIssueText.Trim().ToLowerInvariant();
                filtered = filtered.Where(i => i.Column.ToLowerInvariant().Contains(query) ||
                                               i.Explanation.ToLowerInvariant().Contains(query) ||
                                               i.Category.ToString().ToLowerInvariant().Contains(query));
            }

            FilteredIssues.Clear();
            foreach (var item in filtered)
            {
                FilteredIssues.Add(item);
            }
        }

        public void ApplyTransformationFilters()
        {
            var filtered = ProposedTransformations.AsEnumerable();

            if (SelectedReviewStatusFilter != "All Statuses")
            {
                filtered = filtered.Where(t => t.Status.ToString() == SelectedReviewStatusFilter);
            }

            FilteredTransformations.Clear();
            foreach (var item in filtered)
            {
                FilteredTransformations.Add(item);
            }
        }

        private void PopulatePreviewTable(ImportedDataset dataset)
        {
            var dt = new DataTable();
            dt.Columns.Add("#", typeof(int));

            foreach (var col in dataset.Columns)
            {
                dt.Columns.Add(col.Name, typeof(string));
            }

            foreach (var row in dataset.Rows.Take(250)) // Smooth preview of top rows
            {
                var rowData = new object[dataset.Columns.Count + 1];
                rowData[0] = row.RowIndex;
                for (int i = 0; i < dataset.Columns.Count; i++)
                {
                    rowData[i + 1] = i < row.Values.Count ? (row.Values[i] ?? "") : "";
                }
                dt.Rows.Add(rowData);
            }

            PreviewDataTable = dt;
        }

        private void PopulateReviewTable(ImportedDataset dataset)
        {
            var dt = new DataTable();
            dt.Columns.Add("Row", typeof(int));
            dt.Columns.Add("Status", typeof(string));

            foreach (var col in dataset.Columns)
            {
                dt.Columns.Add(col.Name, typeof(string));
            }

            foreach (var row in dataset.Rows.Take(250))
            {
                var rowData = new object[dataset.Columns.Count + 2];
                rowData[0] = row.RowIndex;
                rowData[1] = row.IsModified ? "Modified" : "Original";
                for (int i = 0; i < dataset.Columns.Count; i++)
                {
                    rowData[i + 2] = i < row.Values.Count ? (row.Values[i] ?? "") : "";
                }
                dt.Rows.Add(rowData);
            }

            ReviewDataTable = dt;
        }

        private void SetSuccessStatus(string msg)
        {
            GlobalStatusMessage = msg;
            IsStatusError = false;
        }

        private void SetInfoStatus(string msg)
        {
            GlobalStatusMessage = msg;
            IsStatusError = false;
        }

        private void SetErrorStatus(string msg)
        {
            GlobalStatusMessage = msg;
            IsStatusError = true;
        }

        private void ClearStatusMessage()
        {
            GlobalStatusMessage = null;
            IsStatusError = false;
        }
    }
}
