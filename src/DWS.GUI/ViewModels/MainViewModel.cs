using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DWS.Core.Logging;
using DWS.Core.Rules;
using DWS.GUI.Services;
using Microsoft.Extensions.Logging;

namespace DWS.GUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SecureRuleEngine _ruleEngine;
    private readonly TransactionJournal _journal;
    private readonly DashboardClient _dashboardClient;
    private readonly ILogger<MainViewModel>? _logger;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _statusText = "Listo";

    [ObservableProperty]
    private string _logOutput = "";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _dashboardUrl = @"\\.\pipe\DWS11_Enterprise_IPC_v2";

    [ObservableProperty]
    private string _workerStatus = "Desconocido";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private int _selectedTabIndex;

    public ObservableCollection<Rule> AvailableRules { get; } = new();
    public ObservableCollection<Rule> FilteredRules { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<TransactionHistoryItem> TransactionHistory { get; } = new();

    [ObservableProperty]
    private TransactionHistoryItem? _selectedTransaction;

    public bool HasSelectedTransaction => SelectedTransaction != null;

    public MainViewModel(
        SecureRuleEngine ruleEngine,
        TransactionJournal journal,
        DashboardClient dashboardClient,
        ILogger<MainViewModel>? logger = null)
    {
        _ruleEngine = ruleEngine;
        _journal = journal;
        _dashboardClient = dashboardClient;
        _logger = logger;

        LoadRules();
        LoadTransactionHistory();

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(SearchText) or nameof(SelectedCategory))
            {
                ApplyFilters();
            }
        };
    }

    private void LoadRules()
    {
        try
        {
            var ruleSet = _ruleEngine.LoadAndVerifyRulesFromFile("rules/rules.json");
            AvailableRules.Clear();
            FilteredRules.Clear();
            Categories.Clear();
            Categories.Add("Todas las categorías");

            foreach (var rule in ruleSet.Rules)
            {
                AvailableRules.Add(rule);
                FilteredRules.Add(rule);

                if (!Categories.Contains(rule.Category))
                {
                    Categories.Add(rule.Category);
                }
            }

            SelectedCategory = "Todas las categorías";
            StatusText = $"Cargadas {AvailableRules.Count} opciones de optimización";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al cargar reglas: {ex.Message}";
            _logger?.LogError(ex, "Failed to load rules");
        }
    }

    private void LoadTransactionHistory()
    {
        TransactionHistory.Clear();
        var history = _journal.GetTransactionHistory();
        foreach (var item in history)
        {
            TransactionHistory.Add(item);
        }
    }

    private void ApplyFilters()
    {
        FilteredRules.Clear();

        var filtered = AvailableRules.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(r =>
                r.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "Todas las categorías")
        {
            filtered = filtered.Where(r => r.Category == SelectedCategory);
        }

        foreach (var rule in filtered)
        {
            FilteredRules.Add(rule);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var rule in FilteredRules)
        {
            rule.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var rule in FilteredRules)
        {
            rule.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task DryRunAsync()
    {
        var selectedRules = AvailableRules.Where(r => r.IsSelected).ToList();

        if (selectedRules.Count == 0)
        {
            StatusText = "No hay reglas seleccionadas";
            return;
        }

        StatusText = "Ejecutando simulación Dry-Run...";
        IsRunning = true;
        ProgressPercentage = 0;
        LogOutput = "=== INICIANDO SIMULACIÓN DRY-RUN ===\n";

        try
        {
            var progress = new Progress<string>(msg =>
            {
                LogOutput += msg + "\n";
            });

            var result = await _dashboardClient.ApplyRulesAsync(
                selectedRules.Select(r => r.Id).ToList(),
                dryRun: true,
                progress: progress);

            if (result?.Success == true)
            {
                StatusText = $"Simulación completada: {result.AuditReport?.TotalChanges ?? 0} cambios evaluados";
                LogOutput += $"✅ Simulación exitosa\n";
                LogOutput += $"📊 Total de cambios a aplicar: {result.AuditReport?.TotalChanges}\n";
                LogOutput += $"🛡️ Claves ya conformes: {result.AuditReport?.CompliantRulesCount}\n";
                LogOutput += $"⚠️ Nivel de riesgo estimado: {result.AuditReport?.RiskLevel}\n";
            }
            else
            {
                StatusText = "Error durante la simulación";
                LogOutput += $"❌ Error: {result?.ErrorMessage}\n";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            LogOutput += $"❌ Excepción: {ex.Message}\n";
            _logger?.LogError(ex, "Dry run failed");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var selectedRules = AvailableRules.Where(r => r.IsSelected).ToList();

        if (selectedRules.Count == 0)
        {
            StatusText = "No hay reglas seleccionadas";
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        ProgressPercentage = 0;
        StatusText = "Aplicando optimizaciones transaccionales (KTM + LGPO)...";
        LogOutput = "=== APLICANDO OPTIMIZACIONES EMPRESARIALES ===\n";

        try
        {
            var progress = new Progress<string>(message =>
            {
                LogOutput += message + "\n";
            });

            var result = await _dashboardClient.ApplyRulesAsync(
                selectedRules.Select(r => r.Id).ToList(),
                dryRun: false,
                progress: progress,
                cancellationToken: _cancellationTokenSource.Token);

            if (result?.Success == true)
            {
                StatusText = "Optimizaciones aplicadas exitosamente con 100% de integridad";
                LogOutput += "✅ Todas las optimizaciones aplicadas correctamente\n";
                LogOutput += "🔒 Transacciones KTM confirmadas\n";
                LogOutput += "📋 Directivas LGPO registradas en registry.pol\n";

                LoadTransactionHistory();
            }
            else
            {
                StatusText = "Error durante la aplicación";
                LogOutput += $"❌ Error: {result?.ErrorMessage}\n";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operación cancelada - Cambios revertidos (KTM Rollback)";
            LogOutput += "⛔ Operación cancelada por el usuario\n";
            LogOutput += "🔄 Cambios revertidos automáticamente a nivel de Kernel\n";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            LogOutput += $"❌ Excepción: {ex.Message}\n";
            _logger?.LogError(ex, "Apply failed");
        }
        finally
        {
            IsRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Abort()
    {
        _cancellationTokenSource?.Cancel();
        StatusText = "Abortando... Revirtiendo cambios transaccionales (KTM Rollback)...";
        LogOutput += "⛔ Abortando operación y ejecutando KTM Rollback...\n";
    }

    [RelayCommand]
    private async Task RevertFullAsync()
    {
        if (SelectedTransaction == null) return;

        StatusText = $"Revirtiendo transacción completa {SelectedTransaction.TransactionId}...";
        bool success = await _dashboardClient.RevertTransactionAsync(SelectedTransaction.TransactionId);

        StatusText = success
            ? "Transacción revertida exitosamente"
            : "Error al revertir transacción";

        if (success)
        {
            LoadTransactionHistory();
        }
    }

    [RelayCommand]
    private async Task RevertByCategoryAsync(string category)
    {
        if (SelectedTransaction == null) return;

        StatusText = $"Revirtiendo categoría '{category}' en {SelectedTransaction.TransactionId}...";
        bool success = await _dashboardClient.RevertByCategoryAsync(SelectedTransaction.TransactionId, category);

        StatusText = success
            ? $"Categoría '{category}' revertida exitosamente"
            : "Error al revertir categoría";

        if (success)
        {
            LoadTransactionHistory();
        }
    }

    [RelayCommand]
    private async Task OpenDashboardAsync()
    {
        try
        {
            if (DashboardUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DashboardUrl,
                    UseShellExecute = true
                });
            }
            else
            {
                await CopyDashboardUrlAsync();
                StatusText = $"Canal seguro IPC activo: {DashboardUrl}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger?.LogError(ex, "Failed to interact with dashboard");
        }
    }

    [RelayCommand]
    private async Task CopyDashboardUrlAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(DashboardUrl);
            StatusText = "URL copiada al portapapeles";
        }
    }

    [RelayCommand]
    private async Task RefreshWorkerStatusAsync()
    {
        try
        {
            var status = await _dashboardClient.GetStatusAsync();
            WorkerStatus = status != null
                ? $"Activo - {status.AppliedRules}/{status.TotalRules} reglas aplicadas"
                : "Inactivo";
        }
        catch (Exception ex)
        {
            WorkerStatus = $"Error: {ex.Message}";
        }
    }

    partial void OnSelectedTransactionChanged(TransactionHistoryItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedTransaction));
    }
}
