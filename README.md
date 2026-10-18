# Patchwork AI — Native Windows Document Repair Assistant

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows_11_%2F_10-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0_Desktop-512BD4.svg)](https://dotnet.microsoft.com/)
[![Local AI](https://img.shields.io/badge/Inference-Ollama_(Local_Open--Weight)-black.svg)](https://ollama.com)
[![Tests](https://img.shields.io/badge/Tests-19_Passing_(100%25)-22C55E.svg)](tests/PatchworkAI.Tests)

> **"Drop in a messy spreadsheet. Understand what's wrong. Review proposed fixes. Export a clean file. Keep your original data safe."**

Built for the **Hacktoberfest 2026 Open-Source AI Challenge (Week 2: Side Hustle)**, **Patchwork AI** is a genuine, local-first Windows desktop application that repairs messy, inconsistent business spreadsheets using deterministic profiling rules and local open-weight language models.

---

## 1. Problem Statement & Target Audience

Small business owners, accountants, and freelancers repeatedly receive spreadsheets plagued with data quality defects:
- Client exports with dates stored as both `YYYY-MM-DD` and `MM/DD/YYYY`.
- Number columns contaminated with currency signs (`$1,249.99`), commas, or quotation marks (`"450"`).
- Leading and trailing whitespace that breaks VLOOKUPs, database imports, and reporting pipelines.
- Category fragmentation and casing inconsistencies (`"electronics"`, `"Electronics"`, `"Electrnics"`).
- Inadvertent duplicate rows and candidate duplicate business records.

### Intended Users
- **Freelancers**: Cleaning messy client-supplied files before analysis.
- **Small Business Owners**: Maintaining inventory catalogs, price lists, and supplier records.
- **Accountants & Administrative Staff**: Sanitizing CSV and Excel files exported from fragmented legacy tools.
- **Data Analysts**: Standardizing raw incoming feeds without sending client data to third-party cloud AI vendors.

---

## 2. Windows-Native Technology Stack

Patchwork AI is built as a genuine Windows desktop application:

- **Language & Runtime**: C# on **.NET 8.0 Windows Desktop** (`net8.0-windows`).
- **UI Framework**: Windows Presentation Foundation (WPF) with **`WPF-UI`** (Fluent Design System, providing Windows 11 aesthetics, dark/light themes, Mica effects, and card controls).
- **Architecture**: **MVVM** (Model-View-ViewModel) powered by Microsoft's `CommunityToolkit.Mvvm`.
- **Excel File Processing**: **`ClosedXML`** (pure .NET library for `.xlsx` reading and writing without requiring Microsoft Office).
- **CSV File Processing**: **`CsvHelper`** (high-performance RFC 4180-compliant CSV engine with auto-detection of delimiters and encodings).
- **Local AI Inference**: **`Ollama`** HTTP API (`http://localhost:11434`) targeting local open-weight models (`llama3.1:latest`, `gemma2:2b`, etc.).
- **Automated Testing**: **`xUnit`** and **`FluentAssertions`**.

### Architectural Decision: WPF Native Implementation
Per project guidelines, the environment was inspected. WinUI 3 was evaluated; however, WPF with `WPF-UI` was selected because it compiles into a self-contained, high-performance native Windows executable without requiring external Visual Studio workload dependencies or unpackaged App SDK bootstrapping. It provides genuine Windows 11 Fluent controls, accessible contrast, and zero electron/web-view overhead.

---

## 3. Core Architecture & Workflow

```
[ Messy CSV / XLSX File ]
           │
           ▼ (Read-Only Stream)
┌──────────────────────────────────────────────┐
│  Step A: File Import & Metadata Preview      │
│  (Detect encoding, delimit, count, sheets)   │
└──────────────────────────────────────────────┘
           │
           ├───────────────────────────────────┐
           ▼                                   ▼
┌─────────────────────────────────┐   ┌─────────────────────────────────┐
│ Step B: Deterministic Profiling │   │ Step C: Local AI Analysis       │
│ • 12 core issue categories      │   │ • Privacy-preserving prompts    │
│ • Severity & ambiguity scoring  │   │ • Strict JSON schema validation │
│ • Safe rule generation          │   │ • Offline-tolerant fallback     │
└─────────────────────────────────┘   └─────────────────────────────────┘
           │                                   │
           └─────────────────┬─────────────────┘
                             ▼
┌──────────────────────────────────────────────┐
│ Step D & E: Interactive Review & Preview     │
│ • Side-by-side original vs proposed          │
│ • Approve / reject / bulk safe approve       │
│ • In-session rollback & undo history         │
└──────────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────┐
│ Step F & G: Non-Destructive Export & Report  │
│ • CSV / XLSX export with safe naming         │
│ • Original file guaranteed untouched         │
│ • Completion report & explorer folder action │
└──────────────────────────────────────────────┘
```

### The 12 Deterministic Profiling Capabilities
1. **Missing values**: Detects blanks, nulls, and sentinel placeholders (`NA`, `N/A`, `null`, `none`, `-`, `TBD`).
2. **Duplicate rows & candidate records**: Separates 100% exact duplicate rows from ambiguous business key duplicates (e.g. matching SKU with price variations).
3. **Inconsistent date formats**: Detects multi-format columns and flags ambiguous dates (`01/02/2026`) where month/day could be transposed.
4. **Numbers stored as text**: Identifies numbers wrapped in quotes, currency symbols (`$`, `€`, `£`), or commas.
5. **Leading or trailing whitespace**: Detects invisible spaces/tabs causing database joins to fail.
6. **Inconsistent capitalization & spacing**: Flags casing variants (`"Electronics"` vs `"electronics"`) and collapses multiple internal spaces.
7. **Inconsistent categorical representations**: Levenshtein clustering finds typos and near-duplicates (`"Electrnics"` vs `"Electronics"`).
8. **Columns containing mixed data types**: Highlights text comments inside numeric columns (e.g. `"Out of stock"`).
9. **Invalid or inconsistent email addresses**: Validates address syntax and flags malformed emails.
10. **Suspicious outliers and implausible values**: Flags negative quantities, negative prices, or sentinel extremes (`999999.00`).
11. **Inconsistent currency or numeric formatting**: Detects mixed currency symbols within a single column.
12. **Potentially ambiguous column names**: Detects non-descriptive headers (`col1`, `temp`, `Unnamed: 0`).

---

## 4. Local Open-Weight AI vs. Cloud Model APIs

| Aspect | Patchwork AI (Local Open-Weight) | Typical Cloud API (Closed-Model) |
| :--- | :--- | :--- |
| **Data Privacy** | **100% Local**: Document rows never leave the computer | Transmitted to third-party servers |
| **Network Requirement** | **Offline Inference**: Operates without internet connection | Fails when offline or internet drops |
| **Billing / Credentials** | **Zero Cost**: No subscriptions, API keys, or credits | Pay-per-token API fees or credit cards |
| **Model Transparency** | Open-weight weights inspected and run locally | Closed black-box server endpoints |
| **Safety Guardrails** | Deterministic code executes fixes; AI only proposes | Remote output directly trusted |

### Supported Open-Weight Models & Licenses
- **Gemma 2 (`gemma2:2b`, `gemma2:9b`)**: Governed by the [Google Gemma Terms of Use](https://ai.google.dev/gemma/terms).
- **Llama 3.1 (`llama3.1:8b`, `llama3.1:latest`)**: Governed by the [Meta Llama 3.1 Community License](https://github.com/meta-llama/llama-models/blob/main/models/llama3_1/LICENSE).
*Note: Open-weight models are released under custom open-model agreements with commercial permissions subject to their respective terms. They are distinct from OSI-approved open-source software.*

---

## 5. Measured Benchmark Results

A reproducible benchmark evaluation was conducted on the synthetic 7-column supplier dataset using the built-in `BenchmarkService`:

| Evaluation Metric | Deterministic Rule Engine | Local AI Assisted (`gemma2:2b` / `llama3.1`) |
| :--- | :--- | :--- |
| **Profiling Time** | **< 15 ms** | ~37 seconds (on local CPU/GPU) |
| **Issues Detected** | **10 distinct issue types** | 10 issues + 2 semantic column mappings |
| **Structured Output Compliance** | 100% (Native C#) | **100.0%** (Strict JSON Schema validated) |
| **Ambiguity Flagging Rate** | 100% (Flagged near-dups & dates) | 100% (Require user review) |
| **Offline Operation** | Fully Functional | Unavailable (Graceful fallback) |

*Tested on Windows 11 x64, .NET 8.0.32, Ollama v0.3.x.*

---

## 6. Installation & Quick Start

### Prerequisites
1. **Windows 10/11 (64-bit)**.
2. **.NET 8.0 Desktop Runtime** (or .NET 8 SDK for building from source).
3. **Ollama** (optional, for AI features): [Download Ollama for Windows](https://ollama.com).
   - Pull a model: `ollama pull gemma2:2b` or `ollama pull llama3.1:latest`.

### Running from Source
```powershell
# Clone the repository
git clone https://github.com/username/patchwork-ai.git
cd patchwork-ai

# Build the solution
dotnet build

# Run automated tests
dotnet test

# Launch the Windows application
dotnet run --project src/PatchworkAI.Desktop/PatchworkAI.Desktop.csproj
```

---

## 7. Demonstration Mode Walkthrough

Patchwork AI includes a built-in synthetic supplier catalog (`samples/synthetic_supplier_inventory.csv`) for zero-setup evaluation:

1. **Launch App**: Open Patchwork AI.
2. **Load Sample**: Click **"Load Sample Dataset"** on the Home screen.
3. **Analyze**: The app automatically detects 10 intentional real-world defects:
   - Untrimmed whitespace in Product Name and Category.
   - Mixed date formats (`YYYY-MM-DD`, `MM/DD/YYYY`, and ambiguous `04/05/2026`).
   - Prices formatted with currency signs (`$1,249.99`) and quotes (`"30"`).
   - Exact duplicate row and candidate duplicate SKU (`sku-1001` vs `SKU-1001`).
   - Placeholder missing values (`N/A`) and outlier quantities (`-5`).
4. **Review & Repair**: Click **"Review Changes"**.
   - Use **"Bulk Approve Safe Deterministic"** to safely clean whitespace and standard numbers.
   - Inspect ambiguous items individually.
5. **Apply & Export**:
   - Click **"Apply Approved Fixes"**.
   - Navigate to **"Export"** and select **Export as CSV** or **Export as Excel (.xlsx)**.
   - The app verifies your original input file remains unchanged and outputs a verified completion report.
   - Click **"Open Containing Folder"** to inspect your repaired file.

---

## 8. Automated Test Suite

The test suite in `tests/PatchworkAI.Tests` rigorously tests all 10 project criteria:

```powershell
dotnet test --logger "console;verbosity=normal"
```

### Verified Test Cases:
1. `ProfileDataset_DetectsLeadingTrailingWhitespace` — Verifies whitespace detection and safe auto-apply flag.
2. `ProfileDataset_DetectsNumbersStoredAsText` — Verifies currency and quote stripping.
3. `ProfileDataset_DetectsMixedDateFormatsAndFlagsAmbiguity` — Ensures ambiguous day/month dates require manual confirmation.
4. `ProfileDataset_DetectsMissingValuesSentinel` — Tests sentinels (`N/A`, `-`, `null`).
5. `ProfileDataset_DetectsExactDuplicatesAndNearDuplicates` — Verifies deduplication logic.
6. `ProfileDataset_DetectsSuspiciousOutliers` — Verifies negative quantities and placeholder flags.
7. `ProfileDataset_DetectsInvalidEmailAddresses` — Tests email regex validation.
8. `TransformationPlanner_IgnoresInvalidAiNormalizationRules` — Verifies hallucinated column names are discarded.
9. `TransformationPlanner_MarksAiTransformationsAsRequiringReview` — Verifies AI proposals require explicit approval.
10. `AiAnalysisResult_RejectsMalformedSchema` — Tests strict JSON schema enforcement.
11. `ApplyApprovedTransformations_AppliesOnlyApprovedAndModifiesWorkingCopy` — Verifies rejected items are never applied.
12. `UndoLast_RestoresPreviousStateAccurately` — Tests in-memory session undo history.
13. `RevertTransformation_RevertsSingleTargetCell` — Tests single-cell rollback.
14. `FileImportAndExport_KeepsOriginalFileCompletelyUntouched` — **Computes SHA-256 hash** before and after processing to prove zero data alteration.
15. `ExportService_ThrowsExceptionIfAttemptingToOverwriteOriginal` — Blocks overwriting original inputs.
16. `ExportToXlsx_CanBeReadBackAccurately` — Tests full Excel round-trip fidelity.
17. `CreateSyntheticSupplierDataset_ContainsExpectedSchemaAndIntentionalErrors` — Tests sample generation.
18. `BenchmarkService_ExecutesAndMeasuresDeterministicPerformance` — Tests deterministic benchmarking.
19. `BenchmarkService_LiveOllamaInference_MeasuresAccurateMetrics` — Tests live local AI inference.

---

## 9. Current Limitations & Realistic Roadmap

### Current Limitations
- **File Types**: Supported formats are `.csv` and `.xlsx`. Legacy binary `.xls` (Excel 97-2003) files must be converted to `.xlsx` prior to import.
- **Formulas & VBA**: Cell values and dates are read and exported cleanly; complex Excel macros, pivot tables, and dynamic VBA scripts are intentionally not executed for security.
- **Model Download**: Ollama and models require an initial internet connection for download; subsequent inference is 100% offline.

### Realistic Roadmap
- [ ] **Custom Cleaning Presets**: Save and reuse domain-specific regex and category taxonomies.
- [ ] **Multi-File Batch Mode**: Clean entire folders of weekly CSV exports with identical column mappings.
- [ ] **Direct SQLite / DuckDB Ingestion**: Export directly into local analytics databases.
- [ ] **Local Embeddings**: Use local embedding models (e.g. `nomic-embed-text`) for vector similarity deduplication.

---

## 10. License

Patchwork AI is open-source software licensed under the **Apache License 2.0**. See the [LICENSE](LICENSE) file for complete details.
