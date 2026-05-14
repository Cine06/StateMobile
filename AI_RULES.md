# AI Development Rules - StateMobile

## Tech Stack Overview
*   **Frontend:** .NET MAUI (Multi-platform App UI) using C# and XAML.
*   **Backend:** ASP.NET Core Web API (.NET 8.0+).
*   **Architecture:** MVVM (Model-View-ViewModel) pattern for the mobile client.
*   **Database:** SQL Server (Backend) and SQLite (Mobile Offline Storage).
*   **Real-time:** SignalR for live chat and push-style notifications.
*   **Security:** JWT Authentication, Biometric (FaceID/Fingerprint) integration, and SecureStorage for credentials.
*   **Mobile Capabilities:** ML Kit (Android) and Vision Kit (iOS) for document scanning; PDF generation for reports.
*   **Dependency Injection:** Native .NET DI container used across both API and Mobile projects.

## Library & Implementation Rules

### 1. UI & Styling (Mobile)
*   **XAML First:** Always use XAML for UI layouts unless dynamic generation is strictly required.
*   **Styles:** Use `Resources/Styles/Styles.xaml` and `Colors.xaml` for global styling. Avoid inline styling.
*   **Icons:** Use SVG files in `Resources/Images` or `FontImageSource` where applicable.
*   **Converters:** Place all XAML converters in the `Converters/` folder and register them in `App.xaml`.

### 2. State Management & Logic
*   **MVVM:** All page logic must reside in a ViewModel. Use `CommunityToolkit.Mvvm` for `ObservableProperty` and `RelayCommand`.
*   **Services:** Business logic, API calls, and hardware access must be abstracted into Services (e.g., `IChatService`, `IDatabaseService`).
*   **Offline Sync:** Use `OfflineDatabaseService` for local data persistence. Ensure data is synced with the API via `SyncService`.

### 3. Backend API
*   **Controllers:** Keep controllers thin. Delegate business logic to Service classes.
*   **Models:** Use DTOs (Data Transfer Objects) for API requests/responses to avoid exposing database schemas directly.
*   **SQL:** Use `SQLActions.cs` or dedicated Service classes for database interactions. Ensure parameterized queries to prevent SQL injection.

### 4. Real-time Features
*   **SignalR:** Use `ChatHub` and `NotificationHub` for real-time updates.
*   **Backgrounding:** On Android, use `BackgroundNotificationService` for persistent connectivity.

### 5. Hardware & Platform Specifics
*   **Scanning:** Use `IDocumentScannerService`. Implementations must be platform-specific (ML Kit for Android, Vision Kit for iOS).
*   **Permissions:** Always check and request permissions using `Microsoft.Maui.ApplicationModel.Permissions` before accessing Camera, Microphone, or Storage.

## Coding Standards
*   **Naming:** Use PascalCase for classes and methods, camelCase for private fields (prefixed with `_`).
*   **Async/Await:** Always use asynchronous programming for I/O bound operations (API, Database, File System).
*   **Error Handling:** Use global exception handling in the API and try-catch blocks in ViewModels to show user-friendly alerts via `DisplayAlert`.