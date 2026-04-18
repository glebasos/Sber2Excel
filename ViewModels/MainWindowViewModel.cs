using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sber2Excel.Converters;
using Sber2Excel.Models;
using Sber2Excel.Services;
using Sber2Excel.Services.Parsing;

namespace Sber2Excel.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private TopLevel? _topLevel;
    private readonly ExportService _exporter = new();

    public void AttachTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private StatementInfo? _statement;

    [ObservableProperty]
    private string _statusMessage = "Откройте PDF-файл выписки";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool HasData => Statement != null;
    public bool IsNotBusy => !IsBusy;

    private readonly List<Transaction> _allTransactions = new();

    public ObservableCollection<Transaction> Transactions { get; } = new();

    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty]
    private FlatTreeDataGridSource<Transaction> _transactionsSource = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string? _categoryFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _descriptionFilter = "";

    public bool HasActiveFilter =>
        !string.IsNullOrEmpty(CategoryFilter) || !string.IsNullOrWhiteSpace(DescriptionFilter);

    partial void OnCategoryFilterChanged(string? value) => ApplyFilter();
    partial void OnDescriptionFilterChanged(string value) => ApplyFilter();

    public MainWindowViewModel()
    {
        TransactionsSource = BuildTransactionsSource(Transactions);
    }

    [RelayCommand]
    private void ClearSort()
    {
        TransactionsSource = BuildTransactionsSource(Transactions);
    }

    private void ApplyFilter()
    {
        IEnumerable<Transaction> q = _allTransactions;

        if (!string.IsNullOrEmpty(CategoryFilter))
            q = q.Where(t => t.Category == CategoryFilter);

        if (!string.IsNullOrWhiteSpace(DescriptionFilter))
        {
            var needle = DescriptionFilter.Trim();
            q = q.Where(t => t.Description.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        Transactions.Clear();
        foreach (var t in q) Transactions.Add(t);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        CategoryFilter = null;
        DescriptionFilter = "";
    }

    private static FlatTreeDataGridSource<Transaction> BuildTransactionsSource(
        ObservableCollection<Transaction> rows)
    {
        var greenBrush = new SolidColorBrush(Color.Parse("#1A7F37"));
        var redBrush = new SolidColorBrush(Color.Parse("#CF1322"));

        var amountTemplate = new FuncDataTemplate<Transaction>((_, _) =>
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
            };
            void Apply()
            {
                if (tb.DataContext is Transaction tx)
                {
                    tb.Text = tx.AmountStr;
                    tb.Foreground = tx.IsCredit ? greenBrush : redBrush;
                }
                else
                {
                    tb.Text = "";
                    tb.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            tb.DataContextChanged += (_, _) => Apply();
            Apply();
            return tb;
        });

        var balanceTemplate = new FuncDataTemplate<Transaction>((_, _) =>
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
            };
            void Apply()
            {
                tb.Text = tb.DataContext is Transaction tx ? tx.BalanceStr : "";
            }
            tb.DataContextChanged += (_, _) => Apply();
            Apply();
            return tb;
        });

        return new FlatTreeDataGridSource<Transaction>(rows)
        {
            Columns =
            {
                new TextColumn<Transaction, string>(
                    "Дата операции", x => x.OperationDateStr, new GridLength(145)),
                new TextColumn<Transaction, string>(
                    "Дата обработки", x => x.ProcessingDateStr, new GridLength(120)),
                new TextColumn<Transaction, string>(
                    "Код авт.", x => x.AuthCode, new GridLength(75)),
                new TextColumn<Transaction, string>(
                    "Категория", x => x.Category, new GridLength(160)),
                new TextColumn<Transaction, string>(
                    "Описание", x => x.Description, new GridLength(1, GridUnitType.Star)),
                new TemplateColumn<Transaction>(
                    "Сумма",
                    amountTemplate,
                    width: new GridLength(115),
                    options: new TemplateColumnOptions<Transaction>
                    {
                        CompareAscending = (a, b) => a!.Amount.CompareTo(b!.Amount),
                        CompareDescending = (a, b) => b!.Amount.CompareTo(a!.Amount),
                    }),
                new TemplateColumn<Transaction>(
                    "Остаток",
                    balanceTemplate,
                    width: new GridLength(115),
                    options: new TemplateColumnOptions<Transaction>
                    {
                        CompareAscending = (a, b) => a!.Balance.CompareTo(b!.Balance),
                        CompareDescending = (a, b) => b!.Balance.CompareTo(a!.Balance),
                    }),
            },
        };
    }

    [RelayCommand]
    private async Task OpenPdf()
    {
        var files = await _topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть банковскую выписку",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF-файлы") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;
        var file = files[0];

        IsBusy = true;
        StatusMessage = "Определение формата…";

        try
        {
            byte[] bytes;
            await using (var s = await file.OpenReadAsync())
            {
                using var ms = new System.IO.MemoryStream();
                await s.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var info = await Task.Run(() =>
            {
                var parser = PdfParserFactory.Detect(bytes)
                    ?? throw new NotSupportedException("Формат PDF не поддерживается. Убедитесь, что это выписка одного из поддерживаемых банков.");
                return parser.Parse(bytes);
            });

            Statement = info;

            _allTransactions.Clear();
            _allTransactions.AddRange(info.Transactions);

            Categories.Clear();
            foreach (var c in info.Transactions
                         .Select(t => t.Category)
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Distinct()
                         .OrderBy(c => c))
                Categories.Add(c);

            CategoryFilter = null;
            DescriptionFilter = "";
            ApplyFilter();

            StatusMessage = $"{info.BankName}  |  {info.Transactions.Count} операций  |  " +
                            $"{info.PeriodFrom:dd.MM.yyyy} – {info.PeriodTo:dd.MM.yyyy}  |  " +
                            $"{info.AccountHolder}";
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
        var file = await _topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить как CSV",
            SuggestedFileName = BuildFileName("csv"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV файл") { Patterns = new[] { "*.csv" } }
            }
        });

        if (file is null) return;

        IsBusy = true;
        StatusMessage = "Экспорт в CSV…";
        try
        {
            using var ms = new System.IO.MemoryStream();
            _exporter.ExportCsv(ms, Statement!);
            ms.Position = 0;
            await using var stream = await file.OpenWriteAsync();
            await ms.CopyToAsync(stream);
            await stream.FlushAsync();
            StatusMessage = $"CSV сохранён: {file.Name}";
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
        var file = await _topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить как Excel",
            SuggestedFileName = BuildFileName("xlsx"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel файл") { Patterns = new[] { "*.xlsx" } }
            }
        });

        if (file is null) return;

        IsBusy = true;
        StatusMessage = "Экспорт в Excel…";
        try
        {
            using var ms = new System.IO.MemoryStream();
            _exporter.ExportXlsx(ms, Statement!);
            ms.Position = 0;
            await using var stream = await file.OpenWriteAsync();
            await ms.CopyToAsync(stream);
            await stream.FlushAsync();
            StatusMessage = $"Excel сохранён: {file.Name}";
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
