# Security Policy

## Reporting Security Issues

We take data integrity and local security seriously. Because **Patchwork AI** processes sensitive business spreadsheets (inventory records, customer lists, pricing catalogs, financial exports), maintaining strong local-first security guarantees is paramount.

If you discover a vulnerability or security flaw, please report it responsibly by contacting the maintainers directly or opening a confidential security advisory on GitHub.

---

## Security Architecture Principles

### 1. Local Processing Only
- Patchwork AI does not upload spreadsheet rows, customer emails, prices, or document contents to any cloud server or remote API.
- All deterministic analysis and in-memory transformations execute strictly within the local Windows process space.

### 2. Guardrails for Local AI Inference
- Patchwork AI communicates exclusively with local inference endpoints (such as `http://localhost:11434` for Ollama).
- To preserve user confidentiality, prompts sent to the local model contain **only** column headers, data type statistics, and a small set (3 to 5) of anonymized or representative sample values.
- Full spreadsheets or raw customer databases are never sent to the inference runtime.
- Model responses are strictly validated against strongly-typed JSON schemas. Responses attempting to execute arbitrary code, SQL queries, or shell scripts are completely discarded.

### 3. Non-Destructive Data Handling
- Source files are opened in shared read-only mode (`FileAccess.Read`, `FileShare.ReadWrite`).
- The application actively blocks attempts to overwrite original input files upon export.

### 4. Zero Hardcoded Credentials
- No API keys, secrets, or cloud authentication tokens are included in the source code or configurations.
