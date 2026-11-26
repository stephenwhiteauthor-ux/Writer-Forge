using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Documents;
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
public partial class MainWindow : RibbonWindow
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
        Editor.ToolbarStateChanged += (_, s) => ApplyEditorState(s);
        Editor.ParagraphStylesChanged += (_, _) => PopulateStyleBox();
        PopulateStyleBox();
        UpdateTitle();
        ApplySettings();

        // The ribbon renders a default (empty) application-menu button; File is
        // a real tab now, so suppress it.
        MainRibbon.ApplicationMenu = null;

        // Start on the Home tab; the File tab opens the backstage overlay.
        Loaded += (_, _) =>
        {
            MainRibbon.SelectedItem = HomeTab;
            _backstageArmed = true;
        };

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += Autosave_Tick;
        _autosaveTimer.Start();
    }

    // ---------- Backstage (File tab) ----------

    private bool _backstageArmed;

    private void MainRibbon_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_backstageArmed || FileTab is null) return;
        if (FileTab.IsSelected) OpenBackstage();
    }

    private void OpenBackstage()
    {
        RefreshBackstageInfo();
        BackstagePanel.Visibility = Visibility.Visible;
    }

    private void CloseBackstage()
    {
        if (BackstagePanel.Visibility != Visibility.Visible) return;
        BackstagePanel.Visibility = Visibility.Collapsed;
        MainRibbon.SelectedItem = HomeTab;
    }

    private void BackstageHome_Click(object sender, RoutedEventArgs e) => CloseBackstage();

    private void FileTab_Click(object sender, RoutedEventArgs e) => OpenBackstage();

    private void RefreshBackstageInfo()
    {
        BsFileName.Text = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
        BsFilePath.Text = _filePath ?? "Not saved yet";
        BsStats.Text = $"Words: {Editor.WordCount}    {(Editor.IsDirty ? "Unsaved changes" : "All changes saved")}";
    }

    // ---------- Ribbon: state sync ----------

    private bool _syncingRibbon;

    private void PopulateStyleBox()
    {
        _syncingRibbon = true;
        try
        {
            StyleBox.Items.Clear();
            foreach (var (name, key) in Editor.GetParagraphStyles())
                StyleBox.Items.Add(new ComboBoxItem { Content = name, Tag = key });
        }
        finally
        {
            _syncingRibbon = false;
        }
    }

    private void ApplyEditorState(EditorToolbarState s)
    {
        _syncingRibbon = true;
        try
        {
            UndoBtn.IsEnabled = s.CanUndo;
            RedoBtn.IsEnabled = s.CanRedo;
            BoldToggle.IsChecked = s.Bold;
            ItalicToggle.IsChecked = s.Italic;
            UnderlineToggle.IsChecked = s.Underline;
            StrikeToggle.IsChecked = s.Strikethrough;
            SubToggle.IsChecked = s.Subscript;
            SupToggle.IsChecked = s.Superscript;
            AlignLeftToggle.IsChecked = s.Alignment == TextAlignment.Left;
            AlignCenterToggle.IsChecked = s.Alignment == TextAlignment.Center;
            AlignRightToggle.IsChecked = s.Alignment == TextAlignment.Right;
            AlignJustifyToggle.IsChecked = s.Alignment == TextAlignment.Justify;
            BulletsToggle.IsChecked = s.Bullets;
            NumbersToggle.IsChecked = s.Numbers;
            SelectComboItem(FontBox, s.FontFamily);
            SelectComboItem(SizeBox, s.FontSize?.ToString("0.##", CultureInfo.InvariantCulture));
            SelectComboItem(StyleBox, s.StyleKey ?? "StyleNormal");
        }
        finally
        {
            _syncingRibbon = false;
        }
    }

    private static void SelectComboItem(ComboBox box, string? value)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem ci)
            {
                ci.IsSelected = string.Equals(
                    ci.Tag as string ?? ci.Content as string, value,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------- Ribbon: Home tab commands ----------

    private void RibbonUndo_Click(object sender, RoutedEventArgs e) => Editor.Undo();
    private void RibbonRedo_Click(object sender, RoutedEventArgs e) => Editor.Redo();
    private void Bold_Click(object sender, RoutedEventArgs e) => Editor.ToggleBold();
    private void Italic_Click(object sender, RoutedEventArgs e) => Editor.ToggleItalic();
    private void Underline_Click(object sender, RoutedEventArgs e) => Editor.ToggleUnderline();
    private void Strike_Click(object sender, RoutedEventArgs e) => Editor.ToggleStrikethrough();
    private void Sub_Click(object sender, RoutedEventArgs e) => Editor.ToggleSubscript();
    private void Sup_Click(object sender, RoutedEventArgs e) => Editor.ToggleSuperscript();
    private void AlignLeft_Click(object sender, RoutedEventArgs e) => Editor.SetAlignment(TextAlignment.Left);
    private void AlignCenter_Click(object sender, RoutedEventArgs e) => Editor.SetAlignment(TextAlignment.Center);
    private void AlignRight_Click(object sender, RoutedEventArgs e) => Editor.SetAlignment(TextAlignment.Right);
    private void AlignJustify_Click(object sender, RoutedEventArgs e) => Editor.SetAlignment(TextAlignment.Justify);
    private void Bullets_Click(object sender, RoutedEventArgs e) => Editor.ToggleBullets();
    private void Numbers_Click(object sender, RoutedEventArgs e) => Editor.ToggleNumbered();
    private void IndentRight_Click(object sender, RoutedEventArgs e) => Editor.Indent(24);
    private void IndentLeft_Click(object sender, RoutedEventArgs e) => Editor.Indent(-24);
    private void ClearFormatting_Click(object sender, RoutedEventArgs e) => Editor.ClearFormatting();
    private void FontColor_Click(object sender, RoutedEventArgs e) => Editor.ShowFontColorMenu(FontColorBtn);
    private void Highlight_Click(object sender, RoutedEventArgs e) => Editor.ShowHighlightMenu(HighlightBtn);

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (FontBox.SelectedItem is ComboBoxItem { Tag: string name })
            Editor.SetFontFamily(name);
    }

    private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (SizeBox.SelectedItem is ComboBoxItem { Tag: string text } &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
            Editor.SetFontSize(size);
    }

    private void StyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (StyleBox.SelectedItem is ComboBoxItem { Tag: string key })
            Editor.SetStyle(key);
    }

    private void SpacingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (SpacingBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Editor.SetLineSpacing(tag);
    }

    private void BeforeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (BeforeBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Editor.SetSpaceBefore(tag);
    }

    private void AfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRibbon || Editor is null) return;
        if (AfterBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Editor.SetSpaceAfter(tag);
    }

    // ---------- Title / dirty state ----------

    private void UpdateTitle()
    {
        if (_filePath is null)
        {
            Title = Editor.IsDirty ? "WriterForge *" : "WriterForge";
        }
        else
        {
            Title = $"{Path.GetFileName(_filePath)}{(Editor.IsDirty ? "*" : "")} — WriterForge";
        }
        RefreshBackstageInfo();
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
        CloseBackstage();
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
        CloseBackstage();
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
            WritingStatsService.RecordSave(_filePath, Editor.WordCount);
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
            WritingStatsService.RecordSave(_filePath, Editor.WordCount);
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
            WritingStatsService.RecordSave(_filePath, Editor.WordCount);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the file:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        UpdateTitle();
        return true;
    }

    // ---------- Insert ----------

    private void InsertTable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertTableWindow { Owner = this };
        if (dialog.ShowDialog() == true)
            Editor.InsertTable(dialog.Rows, dialog.Columns);
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e) => Editor.PromptInsertImage();

    private void TableOp(bool ok)
    {
        if (!ok)
            MessageBox.Show("Place the caret inside a table first.", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void InsertRowBelow_Click(object sender, RoutedEventArgs e) => TableOp(Editor.InsertTableRow(below: true));
    private void InsertRowAbove_Click(object sender, RoutedEventArgs e) => TableOp(Editor.InsertTableRow(below: false));
    private void InsertColRight_Click(object sender, RoutedEventArgs e) => TableOp(Editor.InsertTableColumn(right: true));
    private void InsertColLeft_Click(object sender, RoutedEventArgs e) => TableOp(Editor.InsertTableColumn(right: false));
    private void DeleteRow_Click(object sender, RoutedEventArgs e) => TableOp(Editor.DeleteTableRow());
    private void DeleteColumn_Click(object sender, RoutedEventArgs e) => TableOp(Editor.DeleteTableColumn());

    private void InsertToc_Click(object sender, RoutedEventArgs e)
    {
        var count = Editor.InsertToc();
        if (count == 0)
            MessageBox.Show("No headings found. Apply Heading 1–3 styles first.", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void Analysis_Click(object sender, RoutedEventArgs e)
    {
        new WritingAnalysisWindow(Editor) { Owner = this }.ShowDialog();
    }

    private void Progress_Click(object sender, RoutedEventArgs e)
    {
        new ProjectStatsWindow { Owner = this }.ShowDialog();
    }

    private void PageCount_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(Editor.GetPaginationDebug(), "Pagination", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PageViewToggle_Click(object sender, RoutedEventArgs e)
    {
        var on = PageViewToggle.IsChecked == true;
        Editor.SetPageView(on);
        if (!on && ReadModeToggle.IsChecked == true)
        {
            ReadModeToggle.IsChecked = false;
            MainRibbon.Visibility = Visibility.Visible;
        }
    }

    private void ReadModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var on = ReadModeToggle.IsChecked == true;
        MainRibbon.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Editor.SetPageView(on);
        PageViewToggle.IsChecked = on;
    }

    private void NavPaneToggle_Click(object sender, RoutedEventArgs e) =>
        Editor.SetNavigationPane(NavPaneToggle.IsChecked == true);

    private void RulerToggle_Click(object sender, RoutedEventArgs e) =>
        Editor.SetRuler(RulerToggle.IsChecked == true);

    private void GridlinesToggle_Click(object sender, RoutedEventArgs e) =>
        Editor.SetGridlines(GridlinesToggle.IsChecked == true);

    private void InsertFootnote_Click(object sender, RoutedEventArgs e) => Editor.InsertFootnote();
    private void InsertEndnote_Click(object sender, RoutedEventArgs e) => Editor.InsertFootnote(endnote: true);

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (!Editor.DeleteFootnoteAtCaret())
            MessageBox.Show("Place the caret on a footnote marker or inside a note.", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ConvertNotes_Click(object sender, RoutedEventArgs e) => Editor.ConvertNotes();
    private void RenumberNotes_Click(object sender, RoutedEventArgs e) => Editor.RenumberFootnotes();

    private void SaveStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!Editor.SaveSelectionAsStyle())
            MessageBox.Show("Select some formatted text first.", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void Print_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var flowDocument = Editor.DocumentSurface.Document;
        if (flowDocument is null) return;

        var printable = PrintableDocument.Compose(
            Editor.DocumentSurface.Document, Editor.Metadata.Header, Editor.Metadata.Footer);
        var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)printable).DocumentPaginator;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            try
            {
                dialog.PrintDocument(paginator, _filePath ?? "Untitled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not print:\n{ex.Message}", "WriterForge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void PrintPreview_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var flowDocument = Editor.DocumentSurface.Document;
        if (flowDocument is null) return;
        var printable = PrintableDocument.Compose(
            Editor.DocumentSurface.Document, Editor.Metadata.Header, Editor.Metadata.Footer);
        new PrintPreviewWindow(printable) { Owner = this }.ShowDialog();
    }

    private void PageSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PageSetupWindow(Editor.Metadata.Header, Editor.Metadata.Footer) { Owner = this };
        if (dialog.ShowDialog() == true)
            Editor.ApplyPageSetup(dialog.PageSize, dialog.Margins, dialog.Header, dialog.Footer);
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
