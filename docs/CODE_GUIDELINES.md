# JellyEmu Code Guidelines and Engineering Standards

## 1. Purpose and Philosophy

This document outlines the architectural patterns, styling conventions, and engineering standards for the JellyEmu project. All contributors and maintainers must adhere to these standards to ensure code readability, maintainability, performance, and long-term project health.

### Core Engineering Principles
- Clarity over Cleverness: Write explicit, self-documenting code. Avoid esoteric language features or nested ternaries that hinder immediate comprehension.
- Separation of Concerns: Maintain strict boundaries between server backend services (C# / .NET), client injection logic (Vanilla JavaScript), template presentation (HTML), and UI presentation styling (CSS).
- Single Source of Truth: Do not duplicate data structures, mappings, or hardcoded magic values across components. Centralize configuration and service endpoints.
- Fail Fast and Recover Gracefully: Validate arguments at API and service boundaries, log informative error contexts, and fail cleanly without crashing server processes or breaking client UI execution.

---

## 2. Formatting and Indentation Standards

Consistency across files is mandatory. Automated linters and formatters must align with the following rules:

### Indentation Rules by Language
- C# (.cs): 4 spaces. No hard tabs.
- JavaScript (.js): 4 spaces. No hard tabs.
- HTML (.html): 4 spaces. Nested tags must be indented logically.
- CSS (.css): 4 spaces per rule block.
- JSON (.json): 2 spaces or 4 spaces consistently per file.
- Markdown (.md): Standard markdown indentation; 2 or 4 spaces for nested lists.

### General Formatting Constraints
- Line Length: Maximum 120 characters per line where reasonable. Break long method signatures, LINQ chains, and complex boolean conditions across multiple lines.
- File Endings: Ensure every source file ends with a single trailing newline.
- Whitespace: Strip trailing whitespace on all lines. Avoid multiple consecutive blank lines; use a single blank line to separate logical blocks within functions.
- Braces:
  - C#: Allman style (opening and closing braces on their own lines).
  - JavaScript: 1TBS / K&R style (opening brace on same line, closing brace on a new line).

---

## 3. Naming Conventions

Consistent naming makes navigating a multi-language stack predictable and straightforward.

### 3.1 C# Backend (.NET 9)
- Namespaces: PascalCase matching the folder hierarchy (for example, `JellyEmu.Controllers`, `JellyEmu.Services`).
- Classes, Structs, and Records: PascalCase nouns (for example, `JellyEmuPreferenceService`, `EffectiveUserPrefs`).
- Interfaces: PascalCase prefixed with `I` (for example, `IJellyEmuService`).
- Methods: PascalCase verbs or verb phrases (for example, `GetEffectivePreferencesAsync`, `NormalizeShader`).
- Properties: PascalCase (for example, `UserId`, `CreatedAtUtc`).
- Private Fields: camelCase prefixed with an underscore (for example, `_appPaths`, `_logger`, `_connectionString`).
- Local Variables and Parameters: camelCase (for example, `userId`, `platformTag`, `currentCore`).
- Constants: PascalCase or UPPER_SNAKE_CASE for compile-time constants.

### 3.2 JavaScript Frontend
- File Naming: lowercase with dot separators (for example, `ejs.setting.js`, `ejs.input.js`, `ejs.save.js`, `settings.js`).
- Functions: camelCase verbs (for example, `loadSettingsData`, `populateCoreSelect`, `applyAllLiveSettings`).
- Variables: camelCase (for example, `currentCore`, `sysPrefs`, `targetDisc`).
- Global Constants: UPPER_SNAKE_CASE (for example, `SHADER_OPTIONS`, `SCALE_OPTIONS`).
- Scoped Module Containers: PascalCase namespace objects (for example, `window.JellyEmu`, `JE`).

### 3.3 HTML and CSS
- DOM Element IDs: lowercase kebab-case prefixed with `je-` to prevent collisions with host client styles (for example, `je-set-core`, `je-btn-save-sys`, `je-pop-settings`).
- CSS Classes: lowercase kebab-case prefixed with `je-` (for example, `.je-popup`, `.je-btn-primary`, `.je-setting-label`).
- CSS Variables: lowercase kebab-case prefixed with `--je-` (for example, `--je-bg`, `--je-accent`).

### 3.4 REST API Endpoints
- Base Path: lowercase kebab-case under `/jellyemu/`.
- Resource Routing: `/jellyemu/{resource}` (for example, `/jellyemu/prefs/{userId}`, `/jellyemu/systems`, `/jellyemu/shaders`).
- Query Parameters: camelCase (for example, `?scope=system&targetId=nes`).

---

## 4. Code Reuse and Duplication Prevention

Duplicate code introduces drift, hidden defects, and unnecessary maintenance overhead. Follow these rules to keep the codebase clean:

### 4.1 Eliminate Redundant Logic
- Do not copy and paste helper utilities between JavaScript modules or C# controllers.
- In JavaScript, common helpers (such as fetch wrappers, URL sanitizers, or popup managers) must be shared via the global namespace `window.JellyEmu` or injected helper functions.
- In C#, shared logic belongs in dedicated service classes (such as `JellyEmuPreferenceService`) or extension classes (such as `RomExtensions.cs`), not duplicated inside controller methods.

### 4.2 Avoid Duplicate Event Handlers and Function Declarations
- Ensure that functions populating or synchronizing UI elements are declared once per module.
- Never attach multiple overlapping listeners (such as listening on both an input change event and a save button) that perform duplicate or conflicting network operations.

### 4.3 Clean Up Legacy and Orphaned Blocks
- When refactoring a feature, completely remove replaced code paths instead of leaving unused private methods or commented-out blocks behind.

---

## 5. Commenting and Documentation Standards

Comments must provide context, rationale, and non-obvious details. Do not write comments that merely restate what the code clearly expresses.

### 5.1 C# Documentation Comments
- Use XML documentation comments (`///`) on all public interfaces, public service methods, controller actions, and configuration models.
- Clearly document parameters, return values, and potential exceptions.
- Use standard single-line comments (`//`) inside method bodies to explain non-trivial algorithms or domain-specific logic.

Example:
```csharp
/// <summary>
/// Retrieves the resolved user preferences by merging system-level overrides with global defaults.
/// </summary>
/// <param name="userId">The unique identifier of the user.</param>
/// <param name="systemTag">The console system identifier (e.g., snes, gba).</param>
/// <returns>An EffectiveUserPrefs object containing the resolved values.</returns>
public async Task<EffectiveUserPrefs> GetEffectivePreferencesAsync(string userId, string? systemTag)
{
    // Retrieve global preferences first as baseline
    var globalPrefs = await GetPreferencesAsync(userId, "global", string.Empty);
    
    // Merge system override if present
    if (!string.IsNullOrEmpty(systemTag))
    {
        var sysPrefs = await GetPreferencesAsync(userId, "system", systemTag);
        return MergePreferences(globalPrefs, sysPrefs);
    }

    return globalPrefs;
}
```

### 5.2 JavaScript Comments
- Use clean multi-line section headers to separate functional zones within large modules.
- Use JSDoc notation for public helper utilities and configuration models.
- Avoid cluttered decorative banners. Keep comments concise, direct, and factual.

Example:
```javascript
// Scoped Data Fetch & UI Synchronization

/**
 * Populates the core selection dropdown based on available system cores.
 * @param {Array<Object>} cores List of core descriptor objects.
 */
function renderCoreOptions(cores) {
    var coreSelect = document.getElementById('je-set-core');
    if (!coreSelect) return;
    
    // Populate options without triggering state mutation
    coreSelect.innerHTML = '';
    cores.forEach(function (c, index) {
        var opt = document.createElement('option');
        opt.value = c.id;
        opt.textContent = c.name + (index === 0 ? ' (Default)' : '');
        coreSelect.appendChild(opt);
    });
}
```

### 5.3 CSS Comments
- Organize stylesheet rules by component or layout area using brief section titles.
- Do not use decorative symbol frames.

Example:
```css
/* Popup and Modal Overlays */
.je-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.7);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 100000;
}

/* Form Controls */
.je-select {
    width: 100%;
    padding: 8px 12px;
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid rgba(255, 255, 255, 0.15);
    border-radius: 6px;
    color: #fff;
}
```

### 5.4 HTML Comments
- Use structural comments to mark major container boundaries and tab panels.
- Keep HTML comments minimal and relevant to layout regions.

Example:
```html
<!-- Tab Panel: System Settings -->
<div class="je-tab-panel" id="je-panel-sys-settings">
    <div class="je-section-title">Emulation Core</div>
    <div class="je-setting">
        <span class="je-setting-label">Loaded Core</span>
        <select id="je-set-core"></select>
    </div>
</div>
```

---

## 6. Error Handling and Defensive Programming

### 6.1 Backend Error Handling (C#)
- Nullability: Enable nullable reference types (`<Nullable>enable</Nullable>`). Explicitly mark nullable variables with `?` and guard against null references.
- Logging: Inject `ILogger<T>` into all services and controllers. Log exceptions with structured parameters, avoiding raw string interpolation in log statements.
- Controller Responses: Return appropriate HTTP status codes (for example, `200 OK`, `400 BadRequest`, `404 NotFound`, `500 InternalServerError`) along with structured JSON error details.

### 6.2 Frontend Error Handling (JavaScript)
- Network Calls: Always attach `.catch()` handlers to `fetch()` calls.
- UI Feedback: Inform the user of failed operations via non-blocking toast notifications or accessible alert dialogs. Restore button loading states upon failure.
- Defensive DOM Traversal: Guard against null elements before accessing properties or adding listeners (`var el = document.getElementById(...); if (el) { ... }`).

---

## 7. Testing and Verification Standards

- Unit Tests: All C# business logic, resolution algorithms, database queries, and serializers must have unit tests under the `Tests/` directory using standard .NET testing frameworks (such as xUnit / NUnit).
- Test Execution: Every change must build without warnings (`dotnet build`) and pass the test suite cleanly (`dotnet test`).
- Manifest Synchronization: When updating plugin versions, verify that `manifest.json` and `JellyEmu.csproj` versions match precisely.
