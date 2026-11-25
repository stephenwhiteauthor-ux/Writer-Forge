using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WriterForge.Editor.Controls;
using WriterForge.Editor.Services;
using WriterForge.Editor.Windows;

namespace WriterForge.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NewDocumentCommand = new(nameof(NewDocumentCommand), nameof(NewDocumentCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenDocumentCommand = new(nameof(OpenDocumentCommand), nameof(OpenDocumentCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveDocumentCommand = new(nameof(SaveDocumentCommand), nameof(SaveDocumentCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveDocumentAsCommand = new(nameof(SaveDocumentAsCommand), nameof(SaveDocumentAsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand FindCommand = new(nameof(FindCommand), nameof(FindCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ReplaceCommand = new(nameof(ReplaceCommand), nameof(ReplaceCommand), typeof(MainWindow));

    private string? _filePath;
    private AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _autosaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        Editor.DirtyChanged += (_, _) => UpdateTitle();
        UpdateTitle();
        ApplySettings();

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += Autosave_Tick;
        _autosaveTimer.Start();
    }

    // ---------- Title / dirty state ----------

    private void UpdateTitle()
    {
        var name = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
        Title = $"{name}{(Editor.IsDirty ? "*" : "")} — WriterForge";
    }

    private bool ConfirmDiscard()
    {
        if (!Editor.IsDirty) return true;
        var result = MessageBox.Show(
            "Save changes to the current document?",
            "WriterForge",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveWithDialog(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    // ---------- File commands ----------

    private void NewDocument_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        Editor.NewDocument();
        _filePath = null;
        UpdateTitle();
    }

    private void OpenDocument_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Open Document",
            Filter = "WriterForge document (*.wforge)|*.wforge|Word document (*.docx)|*.docx|Rich Text (*.rtf)|*.rtf|Plain Text (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            Editor.LoadDocument(dialog.FileName);
            _filePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the file:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        UpdateTitle();
    }

    private void SaveDocument_Executed(object sender, ExecutedRoutedEventArgs e) => SaveDocument();

    private void SaveDocumentAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveWithDialog();

    private void SaveDocument()
    {
        if (_filePath is null)
        {
            SaveWithDialog();
            return;
        }
        try
        {
            BackupExisting(_filePath);
            Editor.SaveDocument(_filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the file:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateTitle();
    }

    private static void BackupExisting(string path)
    {
        if (!File.Exists(path)) return;
        File.Copy(path, path + ".bak", overwrite: true);
    }

    private void Autosave_Tick(object? sender, EventArgs e)
    {
        if (_filePath is null || !Editor.IsDirty) return;
        try
        {
            Editor.SaveDocument(_filePath);
            UpdateTitle();
        }
        catch
        {
            // Autosave failures are silent — the next tick will retry.
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export as PDF",
            Filter = "PDF document (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = _filePath is null ? "Untitled.pdf" : Path.GetFileNameWithoutExtension(_filePath) + ".pdf",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            Editor.ExportPdf(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export the PDF:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveWithDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Document",
            Filter = "WriterForge document (*.wforge)|*.wforge|Word document (*.docx)|*.docx|Rich Text (*.rtf)|*.rtf|Plain Text (*.txt)|*.txt",
            DefaultExt = ".wforge",
            AddExtension = true,
            FileName = _filePath is null ? "Untitled.wforge" : Path.GetFileName(_filePath),
        };
        if (dialog.ShowDialog(this) != true) return false;

        try
        {
            Editor.SaveDocument(dialog.FileName);
            _filePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the file:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        UpdateTitle();
        return true;
    }

    // ---------- Find / Replace ----------

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e) => Editor.ShowFindReplace();

    private void Replace_Executed(object sender, ExecutedRoutedEventArgs e) => Editor.ShowFindReplace();

    // ---------- Tools ----------

    private void Statistics_Click(object sender, RoutedEventArgs e) => Editor.ShowStatistics();

    private void MacroRecorder_Click(object sender, RoutedEventArgs e)
    {
        new MacroRecorderWindow(Editor) { Owner = this }.ShowDialog();
    }

    private void ReportTypographyResult(string name, int changes)
    {
        MessageBox.Show(
            changes == 0 ? $"{name}: no changes needed." : $"{name}: {changes} change{(changes == 1 ? "" : "s")} made.",
            "WriterForge",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ---------- Settings ----------

    private void ApplySettings()
    {
        GrammarCheckService.BaseAddress = string.IsNullOrWhiteSpace(_settings.LanguageToolUrl)
            ? GrammarCheckService.PublicApi
            : _settings.LanguageToolUrl.Trim().TrimEnd('/');
        Editor.ApplyProofingSettings(_settings.SpellCheckEnabled, _settings.GrammarCheckEnabled);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            _settings.Save();
            ApplySettings();
        }
    }

    private void SmartQuotes_Click(object sender, RoutedEventArgs e) => RecordTool("SmartQuotes", "Smart quotes", () => Editor.SmartQuotes());
    private void Ellipsis_Click(object sender, RoutedEventArgs e) => RecordTool("Ellipsis", "Ellipsis normalisation", () => Editor.NormalizeEllipsis());
    private void Whitespace_Click(object sender, RoutedEventArgs e) => RecordTool("Whitespace", "Whitespace cleanup", () => Editor.CleanWhitespace());
    private void EmptyParagraphs_Click(object sender, RoutedEventArgs e) => RecordTool("EmptyParagraphs", "Empty paragraph removal", () => Editor.RemoveEmptyParagraphs());
    private void AllTypography_Click(object sender, RoutedEventArgs e) => RecordTool("TypographyAll", "Typography cleanup", () => Editor.RunAllTypography());

    private void RecordTool(string name, string label, Func<int> tool)
    {
        var changes = tool();
        if (MacroRecorder.IsRecording) MacroRecorder.Record(name);
        ReportTypographyResult(label, changes);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Edit commands ----------

    private void EditCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        if (Editor.DocumentSurface.CanUndo) Editor.DocumentSurface.Undo();
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        if (Editor.DocumentSurface.CanRedo) Editor.DocumentSurface.Redo();
    }

    private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        Editor.DocumentSurface.Cut();
    }

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        Editor.DocumentSurface.Copy();
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        Editor.DocumentSurface.Paste();
    }

    private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.DocumentSurface.Focus();
        Editor.DocumentSurface.SelectAll();
    }
}
