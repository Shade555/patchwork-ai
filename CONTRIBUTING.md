# Contributing to Patchwork AI

Thank you for your interest in contributing to **Patchwork AI**! This project was built for the **Hacktoberfest 2026 Open-Source AI Challenge (Week 2: Side Hustle)** to empower freelancers, accountants, and small businesses to repair messy spreadsheets using genuine local-first Windows engineering and open-weight AI.

---

## Development Environment Requirements

- **Operating System**: Windows 10 (version 19041+) or Windows 11
- **.NET SDK**: .NET 8.0 SDK (version 8.0.400 or later)
- **Local AI Runtime (Optional for development, required for AI features)**:
  - [Ollama for Windows](https://ollama.com/download/windows)
  - Recommended open-weight models: `gemma2:2b`, `llama3.1:8b`, or `llama3.1:latest`
- **IDE**: Visual Studio 2022 (Community or higher) with *.NET desktop development* workload, or VS Code with C# Dev Kit.

---

## Architecture & Code Standards

Patchwork AI adheres to a strict layered architecture:

1. **`PatchworkAI.Core`**:
   - Zero UI dependencies.
   - Houses domain models (`ImportedDataset`, `DataIssue`, `ProposedTransformation`).
   - Pure deterministic profiling engine (`DataProfiler`) covering all 12 issue categories.
   - Local AI client (`LocalAiService`) with strict JSON schema validation and zero raw data leakage.
   - Deterministic transformation planner & rollback executor (`TransformationExecutor`).
   - File I/O for CSV (`CsvHelper`) and XLSX (`ClosedXML`).

2. **`PatchworkAI.Desktop`**:
   - Native Windows desktop application built with WPF and `WPF-UI` (Fluent Design System).
   - MVVM architecture with `CommunityToolkit.Mvvm`.
   - Never performs blocking work on the UI thread (`async/await` throughout).

3. **`PatchworkAI.Tests`**:
   - Unit and integration test suite using `xUnit` and `FluentAssertions`.
   - Tests all 10 core safety and data integrity criteria.

---

## Core Contribution Principles

1. **Local-First & Absolute Privacy**:
   - Never add cloud telemetry, external cloud LLM dependencies, or remote tracking.
   - The application must function deterministically even when no AI model or network is available.
2. **Data Safety**:
   - Never modify or overwrite the user's original input file. All exports must default to non-destructive paths (e.g. `_repaired`).
   - Never hallucinate financial data, prices, or inventory counts.
3. **Structured Open-Weight AI**:
   - LLM responses must strictly adhere to constrained JSON schemas.
   - LLMs only propose transformations; deterministic C# code executes approved edits.

---

## Running the Automated Test Suite

Before submitting a pull request, run the test suite:

```powershell
dotnet test
```

Ensure all tests pass with zero failures.

---

## Code of Conduct

Please maintain a welcoming, respectful, and constructive environment for all contributors.
