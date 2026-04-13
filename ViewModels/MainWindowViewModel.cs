using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sber2Excel.Models;
using Sber2Excel.Services;

namespace Sber2Excel.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly TopLevel _topLevel;
    private readonly PdfParserService _parser = new();
    private readonly ExportService _exporter = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private StatementInfo? _statement;

    [ObservableProperty]
    private string _statusMessage = "Откройте PDF-файл выписки Сбербанка";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool HasData => Statement != null;
    public bool IsNotBusy => !IsBusy;

    public ObservableCollection<Transaction> Transactions { get; } = new();

    public MainWindowViewModel(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    [RelayCommand]
    private async Task OpenPdf()
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть выписку Сбербанка",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF-файлы") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        IsBusy = true;
        StatusMessage = "Разбор выписки…";

        try
        {
            var info = await Task.Run(() => _parser.ParseStatement(path));

            Statement = info;
            Transactions.Clear();
            foreach (var tx in info.Transactions)
                Transactions.Add(tx);

            StatusMessage = $"Загружено {info.Transactions.Count} операций  |  " +
                            $"{info.PeriodFrom:dd.MM.yyyy} – {info.PeriodTo:dd.MM.yyyy}  |  " +
                            $"Владелец: {info.AccountHolder}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    private async Task ExportCsv()
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить как CSV",
            SuggestedFileName = BuildFileName("csv"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV файл") { Patterns = new[] { "*.csv" } }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;

        IsBusy = true;
        StatusMessage = "Экспорт в CSV…";
        try
        {
            await Task.Run(() => _exporter.ExportCsv(path, Statement!));
            StatusMessage = $"CSV сохранён: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    private async Task ExportXlsx()
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить как Excel",
            SuggestedFileName = BuildFileName("xlsx"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel файл") { Patterns = new[] { "*.xlsx" } }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;

        IsBusy = true;
        StatusMessage = "Экспорт в Excel…";
        try
        {
            await Task.Run(() => _exporter.ExportXlsx(path, Statement!));
            StatusMessage = $"Excel сохранён: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnStatementChanged(StatementInfo? value)
    {
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportXlsxCommand.NotifyCanExecuteChanged();
    }

    private string BuildFileName(string ext)
    {
        if (Statement is null) return $"Выписка.{ext}";
        var card = Statement.CardNumber.Replace("•", "").Replace(" ", "").Replace("Visa Classic", "").Trim();
        return $"Выписка_{card}_{Statement.PeriodFrom:yyyy-MM-dd}_{Statement.PeriodTo:yyyy-MM-dd}.{ext}";
    }
}
