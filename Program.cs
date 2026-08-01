using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace NtfsDeletedFilesViewer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            if (!IsAdministrator())
            {
                try
                {
                    ProcessStartInfo elevation = new ProcessStartInfo(Application.ExecutablePath);
                    elevation.UseShellExecute = true;
                    elevation.Verb = "runas";
                    Process.Start(elevation);
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode != 1223)
                    {
                        MessageBox.Show(
                            "The application could not request administrator access.\r\n\r\n" + ex.Message,
                            "NTFS Deleted Files Viewer",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }

                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal enum ScannerBackend
    {
        NativeApi,
        Fsutil
    }

    internal sealed class ScanOptions
    {
        public string Volume;
        public bool VerifyCoverage;
        public bool ResolvePaths;
        public ScannerBackend Backend;
    }

    internal sealed class ScanProgress
    {
        public int Percent;
        public string Message;

        public ScanProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }
    }

    internal sealed class ScanResult
    {
        public readonly List<DeletionRecord> Deletions = new List<DeletionRecord>();
        public string Volume;
        public string BackendUsed;
        public DateTime? OldestRecordTime;
        public DateTime? NewestRecordTime;
        public bool CoverageVerified;
        public long RecordsScanned;
        public long UnknownVersionRecords;
        public long MalformedRecords;
        public long FirstUsn;
        public long NextUsn;
        public ulong JournalId;
        public string Warning;
    }

    internal sealed class DeletionRecord
    {
        public DateTime Timestamp;
        public string FileName;
        public bool IsDirectory;
        public string BestKnownPath;
        public string ParentPath;
        public string PathStatus;
        public uint ReasonCode;
        public string ReasonText;
        public string FileId;
        public string ParentFileId;
        public long Usn;
        public ushort MajorVersion;
        public uint FileAttributes;
        public byte[] ParentIdBytes;
        public bool ParentIdIsExtended;
    }

    internal sealed class DriveChoice
    {
        public string Volume;
        public string Display;

        public override string ToString()
        {
            return Display;
        }
    }

    internal sealed class BackendChoice
    {
        public ScannerBackend Backend;
        public string Display;

        public override string ToString()
        {
            return Display;
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppTitle = "NTFS Deleted Files Viewer";
        private const string Version = "0.1.1";

        private ComboBox driveCombo;
        private ComboBox backendCombo;
        private Button refreshDrivesButton;
        private Button scanButton;
        private Button cancelButton;
        private Button exportButton;
        private Button copyButton;
        private Button aboutButton;
        private CheckBox verifyCoverageCheck;
        private CheckBox resolvePathsCheck;
        private TextBox searchBox;
        private DateTimePicker fromPicker;
        private DateTimePicker toPicker;
        private Button applyFilterButton;
        private Button clearFilterButton;
        private DataGridView grid;
        private DataTable resultsTable;
        private Label resultCountLabel;
        private ProgressBar progressBar;
        private Label statusLabel;
        private Label coverageLabel;
        private DateTimePicker incidentPicker;
        private NumericUpDown incidentWindow;
        private Button incidentCheckButton;
        private Label incidentResultLabel;
        private CancellationTokenSource scanCancellation;
        private ScanResult currentResult;
        private ToolTip toolTip;

        public MainForm()
        {
            Text = AppTitle;
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1420;
            Height = 860;
            MinimumSize = new Size(1040, 680);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            InitializeInterface();
            LoadDrives();
        }

        private void InitializeInterface()
        {
            toolTip = new ToolTip();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Padding = new Padding(12, 7, 12, 3);
            Label title = new Label();
            title.Text = "Exactly what NTFS says was deleted";
            title.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = true;
            title.Location = new Point(10, 5);
            Label subtitle = new Label();
            subtitle.Text = "Read-only USN journal viewer — every retained record with the FILE_DELETE reason bit";
            subtitle.AutoSize = true;
            subtitle.ForeColor = SystemColors.GrayText;
            subtitle.Location = new Point(13, 39);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            root.Controls.Add(header, 0, 0);

            FlowLayoutPanel scanPanel = CreateFlowPanel();
            scanPanel.Padding = new Padding(10, 6, 8, 2);
            scanPanel.Controls.Add(CreateLabel("NTFS drive:"));
            driveCombo = new ComboBox();
            driveCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            driveCombo.Width = 265;
            scanPanel.Controls.Add(driveCombo);

            refreshDrivesButton = new Button();
            refreshDrivesButton.Text = "Refresh";
            refreshDrivesButton.AutoSize = true;
            refreshDrivesButton.Click += RefreshDrivesButton_Click;
            scanPanel.Controls.Add(refreshDrivesButton);

            scanPanel.Controls.Add(CreateSpacer(8));
            scanPanel.Controls.Add(CreateLabel("Engine:"));
            backendCombo = new ComboBox();
            backendCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            backendCombo.Width = 235;
            backendCombo.Items.Add(new BackendChoice { Backend = ScannerBackend.NativeApi, Display = "Native Windows API (recommended)" });
            backendCombo.Items.Add(new BackendChoice { Backend = ScannerBackend.Fsutil, Display = "fsutil CSV compatibility mode" });
            backendCombo.SelectedIndex = 0;
            scanPanel.Controls.Add(backendCombo);

            scanButton = new Button();
            scanButton.Text = "Scan journal";
            scanButton.AutoSize = true;
            scanButton.Font = new Font(scanButton.Font, FontStyle.Bold);
            scanButton.Click += ScanButton_Click;
            scanPanel.Controls.Add(scanButton);

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.AutoSize = true;
            cancelButton.Enabled = false;
            cancelButton.Click += CancelButton_Click;
            scanPanel.Controls.Add(cancelButton);

            exportButton = new Button();
            exportButton.Text = "Export visible CSV";
            exportButton.AutoSize = true;
            exportButton.Enabled = false;
            exportButton.Click += ExportButton_Click;
            scanPanel.Controls.Add(exportButton);

            copyButton = new Button();
            copyButton.Text = "Copy selection";
            copyButton.AutoSize = true;
            copyButton.Enabled = false;
            copyButton.Click += CopyButton_Click;
            scanPanel.Controls.Add(copyButton);

            aboutButton = new Button();
            aboutButton.Text = "About";
            aboutButton.AutoSize = true;
            aboutButton.Click += AboutButton_Click;
            scanPanel.Controls.Add(aboutButton);
            root.Controls.Add(scanPanel, 0, 1);

            FlowLayoutPanel optionsPanel = CreateFlowPanel();
            optionsPanel.Padding = new Padding(10, 8, 8, 2);
            verifyCoverageCheck = new CheckBox();
            verifyCoverageCheck.Text = "Read all record types to verify timeline coverage";
            verifyCoverageCheck.Checked = true;
            verifyCoverageCheck.AutoSize = true;
            optionsPanel.Controls.Add(verifyCoverageCheck);
            toolTip.SetToolTip(verifyCoverageCheck, "When enabled, the scanner reads all retained USN records so it can prove whether an incident time lies inside the observed journal timeline. This is slower than deletion-only scanning.");

            resolvePathsCheck = new CheckBox();
            resolvePathsCheck.Text = "Resolve surviving parent folders";
            resolvePathsCheck.Checked = true;
            resolvePathsCheck.AutoSize = true;
            optionsPanel.Controls.Add(resolvePathsCheck);
            toolTip.SetToolTip(resolvePathsCheck, "Uses each parent file ID to obtain the parent's current path when that directory still exists. A directory renamed after deletion may have a different current path.");
            root.Controls.Add(optionsPanel, 0, 2);

            FlowLayoutPanel filterPanel = CreateFlowPanel();
            filterPanel.Padding = new Padding(10, 3, 8, 1);
            filterPanel.Controls.Add(CreateLabel("Search:"));
            searchBox = new TextBox();
            searchBox.Width = 260;
            searchBox.KeyDown += SearchBox_KeyDown;
            filterPanel.Controls.Add(searchBox);

            filterPanel.Controls.Add(CreateSpacer(8));
            filterPanel.Controls.Add(CreateLabel("From:"));
            fromPicker = CreateDateTimePicker(true);
            filterPanel.Controls.Add(fromPicker);
            filterPanel.Controls.Add(CreateLabel("To:"));
            toPicker = CreateDateTimePicker(true);
            filterPanel.Controls.Add(toPicker);

            applyFilterButton = new Button();
            applyFilterButton.Text = "Apply filter";
            applyFilterButton.AutoSize = true;
            applyFilterButton.Click += ApplyFilterButton_Click;
            filterPanel.Controls.Add(applyFilterButton);

            clearFilterButton = new Button();
            clearFilterButton.Text = "Clear";
            clearFilterButton.AutoSize = true;
            clearFilterButton.Click += ClearFilterButton_Click;
            filterPanel.Controls.Add(clearFilterButton);

            resultCountLabel = new Label();
            resultCountLabel.Text = "No scan loaded";
            resultCountLabel.AutoSize = true;
            resultCountLabel.Margin = new Padding(18, 7, 3, 0);
            filterPanel.Controls.Add(resultCountLabel);
            root.Controls.Add(filterPanel, 0, 3);

            resultsTable = CreateResultsTable();
            grid = CreateGrid();
            grid.DataSource = resultsTable.DefaultView;
            root.Controls.Add(grid, 0, 4);

            GroupBox incidentGroup = new GroupBox();
            incidentGroup.Text = "Incident check";
            incidentGroup.Dock = DockStyle.Fill;
            incidentGroup.Padding = new Padding(10, 8, 10, 8);
            TableLayoutPanel incidentLayout = new TableLayoutPanel();
            incidentLayout.Dock = DockStyle.Fill;
            incidentLayout.ColumnCount = 2;
            incidentLayout.RowCount = 1;
            incidentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560F));
            incidentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            FlowLayoutPanel incidentControls = CreateFlowPanel();
            incidentControls.WrapContents = true;
            incidentControls.Controls.Add(CreateLabel("Approximate event time:"));
            incidentPicker = CreateDateTimePicker(false);
            incidentPicker.Width = 185;
            incidentPicker.Value = DateTime.Now;
            incidentControls.Controls.Add(incidentPicker);
            incidentControls.Controls.Add(CreateLabel("± minutes:"));
            incidentWindow = new NumericUpDown();
            incidentWindow.Minimum = 1;
            incidentWindow.Maximum = 1440;
            incidentWindow.Value = 5;
            incidentWindow.Width = 65;
            incidentControls.Controls.Add(incidentWindow);
            incidentCheckButton = new Button();
            incidentCheckButton.Text = "Check this window";
            incidentCheckButton.AutoSize = true;
            incidentCheckButton.Enabled = false;
            incidentCheckButton.Click += IncidentCheckButton_Click;
            incidentControls.Controls.Add(incidentCheckButton);

            incidentResultLabel = new Label();
            incidentResultLabel.Dock = DockStyle.Fill;
            incidentResultLabel.TextAlign = ContentAlignment.MiddleLeft;
            incidentResultLabel.Padding = new Padding(10, 4, 10, 4);
            incidentResultLabel.Text = "Scan a journal, then choose the time of the suspected deletion.";
            incidentResultLabel.BorderStyle = BorderStyle.FixedSingle;

            incidentLayout.Controls.Add(incidentControls, 0, 0);
            incidentLayout.Controls.Add(incidentResultLabel, 1, 0);
            incidentGroup.Controls.Add(incidentLayout);
            root.Controls.Add(incidentGroup, 0, 5);

            TableLayoutPanel statusPanel = new TableLayoutPanel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.ColumnCount = 2;
            statusPanel.RowCount = 2;
            statusPanel.Padding = new Padding(10, 3, 10, 4);
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));

            statusLabel = new Label();
            statusLabel.Text = "Ready.";
            statusLabel.Dock = DockStyle.Fill;
            coverageLabel = new Label();
            coverageLabel.Text = "Timeline coverage: not scanned";
            coverageLabel.TextAlign = ContentAlignment.TopRight;
            coverageLabel.Dock = DockStyle.Fill;
            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;

            statusPanel.Controls.Add(statusLabel, 0, 0);
            statusPanel.Controls.Add(coverageLabel, 1, 0);
            statusPanel.Controls.Add(progressBar, 0, 1);
            statusPanel.SetColumnSpan(progressBar, 2);
            root.Controls.Add(statusPanel, 0, 6);
        }

        private static FlowLayoutPanel CreateFlowPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            return panel;
        }

        private static Label CreateLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(3, 7, 3, 0);
            return label;
        }

        private static Control CreateSpacer(int width)
        {
            Panel spacer = new Panel();
            spacer.Width = width;
            spacer.Height = 1;
            spacer.Margin = new Padding(0);
            return spacer;
        }

        private static DateTimePicker CreateDateTimePicker(bool showCheckBox)
        {
            DateTimePicker picker = new DateTimePicker();
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "yyyy-MM-dd HH:mm:ss";
            picker.ShowCheckBox = showCheckBox;
            picker.Checked = false;
            picker.Width = 185;
            return picker;
        }

        private static DataTable CreateResultsTable()
        {
            DataTable table = new DataTable("DeletedFiles");
            table.Locale = CultureInfo.InvariantCulture;
            table.Columns.Add("DeletedAt", typeof(DateTime));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("BestKnownPath", typeof(string));
            table.Columns.Add("Type", typeof(string));
            table.Columns.Add("PathStatus", typeof(string));
            table.Columns.Add("Reason", typeof(string));
            table.Columns.Add("ReasonCode", typeof(string));
            table.Columns.Add("FileId", typeof(string));
            table.Columns.Add("ParentFileId", typeof(string));
            table.Columns.Add("USN", typeof(long));
            table.Columns.Add("RecordVersion", typeof(string));
            table.Columns.Add("Attributes", typeof(string));
            return table;
        }

        private static DataGridView CreateGrid()
        {
            DataGridView view = new DataGridView();
            view.Dock = DockStyle.Fill;
            view.ReadOnly = true;
            view.AllowUserToAddRows = false;
            view.AllowUserToDeleteRows = false;
            view.AllowUserToOrderColumns = true;
            view.AutoGenerateColumns = true;
            view.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            view.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            view.MultiSelect = true;
            view.RowHeadersVisible = false;
            view.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            view.DataBindingComplete += delegate(object sender, DataGridViewBindingCompleteEventArgs args)
            {
                ConfigureGridColumns(view);
            };
            return view;
        }

        private static void ConfigureGridColumns(DataGridView view)
        {
            if (view.Columns.Count == 0)
            {
                return;
            }

            SetColumn(view, "DeletedAt", "Deleted at", 165, "yyyy-MM-dd HH:mm:ss.fff");
            SetColumn(view, "Name", "File or directory name", 230, null);
            SetColumn(view, "BestKnownPath", "Best-known path", 420, null);
            SetColumn(view, "Type", "Type", 75, null);
            SetColumn(view, "PathStatus", "Path confidence", 155, null);
            SetColumn(view, "Reason", "NTFS reason", 210, null);
            SetColumn(view, "ReasonCode", "Reason bits", 105, null);
            SetColumn(view, "FileId", "File ID", 250, null);
            SetColumn(view, "ParentFileId", "Parent file ID", 250, null);
            SetColumn(view, "USN", "USN", 115, null);
            SetColumn(view, "RecordVersion", "USN record", 90, null);
            SetColumn(view, "Attributes", "Attributes", 110, null);
        }

        private static void SetColumn(DataGridView view, string name, string header, int width, string format)
        {
            DataGridViewColumn column = view.Columns[name];
            if (column == null)
            {
                return;
            }

            column.HeaderText = header;
            column.Width = width;
            column.MinimumWidth = 55;
            if (!String.IsNullOrEmpty(format))
            {
                column.DefaultCellStyle.Format = format;
            }
        }

        private void RefreshDrivesButton_Click(object sender, EventArgs e)
        {
            LoadDrives();
        }

        private void LoadDrives()
        {
            string previous = null;
            DriveChoice previousChoice = driveCombo.SelectedItem as DriveChoice;
            if (previousChoice != null)
            {
                previous = previousChoice.Volume;
            }

            driveCombo.Items.Clear();
            DriveInfo[] drives = DriveInfo.GetDrives();
            Array.Sort(drives, delegate(DriveInfo left, DriveInfo right)
            {
                return String.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (DriveInfo drive in drives)
            {
                try
                {
                    if (!drive.IsReady || !String.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string volume = drive.Name.Substring(0, 2).ToUpperInvariant();
                    string label = String.IsNullOrWhiteSpace(drive.VolumeLabel) ? "(no label)" : drive.VolumeLabel;
                    string display = volume + "  " + label + " — " + FormatBytes(drive.AvailableFreeSpace) + " free";
                    driveCombo.Items.Add(new DriveChoice { Volume = volume, Display = display });
                }
                catch
                {
                    // A removable or locked drive can disappear while it is being enumerated.
                }
            }

            int selectedIndex = -1;
            for (int i = 0; i < driveCombo.Items.Count; i++)
            {
                DriveChoice item = driveCombo.Items[i] as DriveChoice;
                if (item != null && String.Equals(item.Volume, previous, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                for (int i = 0; i < driveCombo.Items.Count; i++)
                {
                    DriveChoice item = driveCombo.Items[i] as DriveChoice;
                    if (item != null && String.Equals(item.Volume, "D:", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            if (selectedIndex < 0 && driveCombo.Items.Count > 0)
            {
                selectedIndex = 0;
            }

            driveCombo.SelectedIndex = selectedIndex;
            scanButton.Enabled = driveCombo.Items.Count > 0;
            if (driveCombo.Items.Count == 0)
            {
                statusLabel.Text = "No ready NTFS volumes were found.";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int suffix = 0;
            while (value >= 1024D && suffix < suffixes.Length - 1)
            {
                value /= 1024D;
                suffix++;
            }

            return value.ToString(value >= 100D ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + suffixes[suffix];
        }

        private async void ScanButton_Click(object sender, EventArgs e)
        {
            DriveChoice drive = driveCombo.SelectedItem as DriveChoice;
            BackendChoice backend = backendCombo.SelectedItem as BackendChoice;
            if (drive == null || backend == null)
            {
                return;
            }

            await RunScanAsync(drive.Volume, backend.Backend, true);
        }

        private async Task RunScanAsync(string volume, ScannerBackend backend, bool allowFallback)
        {
            bool retryWithFsutil = false;
            SetBusy(true);
            currentResult = null;
            incidentResultLabel.Text = "Scanning…";
            incidentResultLabel.BackColor = SystemColors.Control;
            resultsTable.Clear();
            resultCountLabel.Text = "Scanning…";
            coverageLabel.Text = "Timeline coverage: scanning";
            CancellationTokenSource localCancellation = new CancellationTokenSource();
            scanCancellation = localCancellation;
            CancellationToken token = localCancellation.Token;
            Progress<ScanProgress> progress = new Progress<ScanProgress>(UpdateProgress);

            ScanOptions options = new ScanOptions();
            options.Volume = volume;
            options.Backend = backend;
            options.VerifyCoverage = verifyCoverageCheck.Checked;
            options.ResolvePaths = resolvePathsCheck.Checked;

            try
            {
                ScanResult result = await Task.Factory.StartNew(
                    delegate { return JournalScanner.Scan(options, token, progress); },
                    token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                currentResult = result;
                PopulateResults(result);
                statusLabel.Text = BuildCompletionStatus(result);
                coverageLabel.Text = BuildCoverageStatus(result);
                incidentPicker.Value = result.NewestRecordTime.HasValue ? result.NewestRecordTime.Value : DateTime.Now;
                incidentResultLabel.Text = "Choose an incident time and window, then click “Check this window”.";
                incidentResultLabel.BackColor = SystemColors.Control;

                if (!String.IsNullOrEmpty(result.Warning))
                {
                    MessageBox.Show(result.Warning, AppTitle + " — scan warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "Scan cancelled.";
                coverageLabel.Text = "Timeline coverage: not established";
                resultCountLabel.Text = "Scan cancelled";
                incidentResultLabel.Text = "The scan was cancelled; no conclusion should be drawn from partial data.";
            }
            catch (Exception ex)
            {
                Exception baseException = ex.GetBaseException();
                if (backend == ScannerBackend.NativeApi && allowFallback)
                {
                    DialogResult retry = MessageBox.Show(
                        "The native Windows API scan failed:\r\n\r\n" + baseException.Message +
                        "\r\n\r\nTry the fsutil compatibility engine instead?",
                        AppTitle,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (retry == DialogResult.Yes)
                    {
                        retryWithFsutil = true;
                    }
                }

                if (!retryWithFsutil)
                {
                    statusLabel.Text = "Scan failed.";
                    coverageLabel.Text = "Timeline coverage: unknown";
                    resultCountLabel.Text = "No results";
                    incidentResultLabel.Text = "The scan failed; no conclusion can be drawn.";
                    MessageBox.Show(
                        "The journal could not be scanned.\r\n\r\n" + baseException.Message,
                        AppTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                SetBusy(false);
                localCancellation.Dispose();
                if (Object.ReferenceEquals(scanCancellation, localCancellation))
                {
                    scanCancellation = null;
                }
            }

            // C# 5 (the compiler bundled with .NET Framework) does not allow
            // await inside a catch block. Perform the optional retry only after
            // the catch/finally sequence has completed and resources are cleaned up.
            if (retryWithFsutil)
            {
                backendCombo.SelectedIndex = 1;
                await RunScanAsync(volume, ScannerBackend.Fsutil, false);
            }
        }

        private void SetBusy(bool busy)
        {
            driveCombo.Enabled = !busy;
            backendCombo.Enabled = !busy;
            refreshDrivesButton.Enabled = !busy;
            scanButton.Enabled = !busy && driveCombo.Items.Count > 0;
            cancelButton.Enabled = busy;
            verifyCoverageCheck.Enabled = !busy;
            resolvePathsCheck.Enabled = !busy;
            exportButton.Enabled = !busy && resultsTable.Rows.Count > 0;
            copyButton.Enabled = !busy && resultsTable.Rows.Count > 0;
            incidentCheckButton.Enabled = !busy && currentResult != null;
            applyFilterButton.Enabled = !busy;
            clearFilterButton.Enabled = !busy;
            progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!busy)
            {
                progressBar.Value = 0;
            }
        }

        private void UpdateProgress(ScanProgress progress)
        {
            statusLabel.Text = progress.Message;
            if (progress.Percent < 0)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(0, Math.Min(100, progress.Percent));
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            if (scanCancellation != null)
            {
                statusLabel.Text = "Cancelling…";
                scanCancellation.Cancel();
            }
        }

        private void PopulateResults(ScanResult result)
        {
            resultsTable.BeginLoadData();
            try
            {
                resultsTable.Clear();
                foreach (DeletionRecord record in result.Deletions)
                {
                    DataRow row = resultsTable.NewRow();
                    row["DeletedAt"] = record.Timestamp;
                    row["Name"] = record.FileName ?? String.Empty;
                    row["BestKnownPath"] = record.BestKnownPath ?? String.Empty;
                    row["Type"] = record.IsDirectory ? "Directory" : "File";
                    row["PathStatus"] = record.PathStatus ?? String.Empty;
                    row["Reason"] = record.ReasonText ?? String.Empty;
                    row["ReasonCode"] = "0x" + record.ReasonCode.ToString("X8", CultureInfo.InvariantCulture);
                    row["FileId"] = record.FileId ?? String.Empty;
                    row["ParentFileId"] = record.ParentFileId ?? String.Empty;
                    row["USN"] = record.Usn;
                    row["RecordVersion"] = "v" + record.MajorVersion.ToString(CultureInfo.InvariantCulture);
                    row["Attributes"] = "0x" + record.FileAttributes.ToString("X8", CultureInfo.InvariantCulture);
                    resultsTable.Rows.Add(row);
                }
            }
            finally
            {
                resultsTable.EndLoadData();
            }

            resultsTable.DefaultView.Sort = "DeletedAt DESC, USN DESC";
            resultsTable.DefaultView.RowFilter = String.Empty;
            resultCountLabel.Text = result.Deletions.Count.ToString("N0", CultureInfo.CurrentCulture) + " deletion records";
            exportButton.Enabled = result.Deletions.Count > 0;
            copyButton.Enabled = result.Deletions.Count > 0;
            incidentCheckButton.Enabled = true;
            ConfigureGridColumns(grid);
        }

        private static string BuildCompletionStatus(ScanResult result)
        {
            string status = result.Deletions.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " FILE_DELETE records found; " +
                result.RecordsScanned.ToString("N0", CultureInfo.CurrentCulture) +
                " journal records examined via " + result.BackendUsed + ".";

            if (result.UnknownVersionRecords > 0)
            {
                status += " " + result.UnknownVersionRecords.ToString("N0", CultureInfo.CurrentCulture) + " unsupported-version records were skipped.";
            }

            return status;
        }

        private static string BuildCoverageStatus(ScanResult result)
        {
            if (!result.CoverageVerified || !result.OldestRecordTime.HasValue || !result.NewestRecordTime.HasValue)
            {
                return "Timeline coverage: not verified";
            }

            return "Timeline coverage: " +
                result.OldestRecordTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) +
                " — " +
                result.NewestRecordTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }

        private void ApplyFilterButton_Click(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyFilter();
                e.SuppressKeyPress = true;
            }
        }

        private void ApplyFilter()
        {
            List<string> expressions = new List<string>();
            string search = searchBox.Text.Trim();
            if (search.Length > 0)
            {
                string escaped = EscapeLikeValue(search);
                expressions.Add("([Name] LIKE '%" + escaped + "%' OR [BestKnownPath] LIKE '%" + escaped + "%' OR [FileId] LIKE '%" + escaped + "%' OR [ParentFileId] LIKE '%" + escaped + "%')");
            }

            if (fromPicker.Checked)
            {
                expressions.Add("[DeletedAt] >= #" + fromPicker.Value.ToString("MM/dd/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture) + "#");
            }

            if (toPicker.Checked)
            {
                expressions.Add("[DeletedAt] <= #" + toPicker.Value.ToString("MM/dd/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture) + "#");
            }

            try
            {
                resultsTable.DefaultView.RowFilter = String.Join(" AND ", expressions.ToArray());
                resultCountLabel.Text = resultsTable.DefaultView.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " visible of " + resultsTable.Rows.Count.ToString("N0", CultureInfo.CurrentCulture);
            }
            catch (EvaluateException ex)
            {
                MessageBox.Show("The filter could not be applied.\r\n\r\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string EscapeLikeValue(string value)
        {
            return value.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        }

        private void ClearFilterButton_Click(object sender, EventArgs e)
        {
            searchBox.Clear();
            fromPicker.Checked = false;
            toPicker.Checked = false;
            resultsTable.DefaultView.RowFilter = String.Empty;
            resultCountLabel.Text = resultsTable.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " deletion records";
        }

        private void IncidentCheckButton_Click(object sender, EventArgs e)
        {
            if (currentResult == null)
            {
                return;
            }

            DateTime center = incidentPicker.Value;
            double minutes = Decimal.ToDouble(incidentWindow.Value);
            DateTime start = center.AddMinutes(-minutes);
            DateTime end = center.AddMinutes(minutes);
            int count = 0;
            foreach (DeletionRecord record in currentResult.Deletions)
            {
                if (record.Timestamp >= start && record.Timestamp <= end)
                {
                    count++;
                }
            }

            fromPicker.Value = start;
            fromPicker.Checked = true;
            toPicker.Value = end;
            toPicker.Checked = true;
            ApplyFilter();

            bool covered = currentResult.CoverageVerified &&
                currentResult.OldestRecordTime.HasValue &&
                currentResult.NewestRecordTime.HasValue &&
                currentResult.OldestRecordTime.Value <= start &&
                currentResult.NewestRecordTime.Value >= end;

            if (!covered)
            {
                incidentResultLabel.BackColor = Color.LemonChiffon;
                incidentResultLabel.Text =
                    "Inconclusive: the selected window is not fully inside a verified journal timeline. " +
                    "Do not interpret zero visible deletion records as proof that nothing was deleted.";
            }
            else if (count == 0)
            {
                incidentResultLabel.BackColor = Color.Honeydew;
                incidentResultLabel.Text =
                    "No FILE_DELETE records were found from " + start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) +
                    " through " + end.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) +
                    ". The observed journal contains records both before and after this window.";
            }
            else
            {
                incidentResultLabel.BackColor = Color.MistyRose;
                incidentResultLabel.Text =
                    count.ToString("N0", CultureInfo.CurrentCulture) + " FILE_DELETE record(s) were found in the selected window. " +
                    "The table is now filtered to those times.";
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (resultsTable.DefaultView.Count == 0)
            {
                return;
            }

            DriveChoice drive = driveCombo.SelectedItem as DriveChoice;
            string drivePart = drive == null ? "volume" : drive.Volume.TrimEnd(':');
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
            dialog.DefaultExt = "csv";
            dialog.AddExtension = true;
            dialog.FileName = "ntfs-deletions-" + drivePart + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (drive != null && IsPathOnVolume(dialog.FileName, drive.Volume))
            {
                DialogResult sameVolume = MessageBox.Show(
                    "The export destination is on " + drive.Volume + ", the volume being examined.\r\n\r\n" +
                    "Writing a CSV there can overwrite space belonging to deleted files. Save to another drive whenever recovery may still be needed.\r\n\r\n" +
                    "Save there anyway?",
                    AppTitle + " — write-safety warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (sameVolume != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                ExportViewToCsv(resultsTable.DefaultView, dialog.FileName);
                statusLabel.Text = "Exported " + resultsTable.DefaultView.Count.ToString("N0", CultureInfo.CurrentCulture) + " visible records to " + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show("The CSV could not be saved.\r\n\r\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsPathOnVolume(string path, string volume)
        {
            try
            {
                string pathRoot = Path.GetPathRoot(Path.GetFullPath(path));
                string volumeRoot = Path.GetPathRoot(volume + "\\");
                return String.Equals(pathRoot, volumeRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ExportViewToCsv(DataView view, string fileName)
        {
            using (StreamWriter writer = new StreamWriter(fileName, false, new UTF8Encoding(true)))
            {
                DataTable table = view.Table;
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    if (c > 0)
                    {
                        writer.Write(',');
                    }
                    writer.Write(CsvEscape(table.Columns[c].ColumnName));
                }
                writer.WriteLine();

                foreach (DataRowView rowView in view)
                {
                    for (int c = 0; c < table.Columns.Count; c++)
                    {
                        if (c > 0)
                        {
                            writer.Write(',');
                        }

                        object value = rowView.Row[c];
                        string text;
                        if (value is DateTime)
                        {
                            text = ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
                        }

                        writer.Write(CsvEscape(text));
                    }
                    writer.WriteLine();
                }
            }
        }

        private static string CsvEscape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (grid.GetCellCount(DataGridViewElementStates.Selected) == 0)
            {
                return;
            }

            try
            {
                DataObject data = grid.GetClipboardContent();
                if (data != null)
                {
                    Clipboard.SetDataObject(data, true);
                    statusLabel.Text = "Copied the selected table cells to the clipboard.";
                }
            }
            catch (ExternalException ex)
            {
                MessageBox.Show("The clipboard was busy. Please try again.\r\n\r\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AboutButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                AppTitle + " " + Version + "\r\n\r\n" +
                "Reads the existing NTFS USN change journal and displays records whose reason flags contain FILE_DELETE (0x00000200).\r\n\r\n" +
                "The application never creates, deletes, resets, or resizes a journal. It does not recover file contents. Best-known paths are based on parent IDs that still resolve now and may differ if a parent directory was renamed later.\r\n\r\n" +
                "Primary engine: FSCTL_QUERY_USN_JOURNAL + FSCTL_READ_USN_JOURNAL.\r\n" +
                "Compatibility engine: fsutil usn readjournal ... csv.",
                AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    internal static class JournalScanner
    {
        public static ScanResult Scan(ScanOptions options, CancellationToken token, IProgress<ScanProgress> progress)
        {
            if (options.Backend == ScannerBackend.Fsutil)
            {
                return FsutilJournalReader.Read(options, token, progress);
            }

            return NativeJournalReader.Read(options, token, progress);
        }
    }

    internal sealed class JournalInformation
    {
        public ulong JournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    internal sealed class ParsedUsnRecord
    {
        public int RecordLength;
        public ushort MajorVersion;
        public long Usn;
        public DateTime? Timestamp;
        public uint Reason;
        public uint FileAttributes;
        public string FileName;
        public string FileId;
        public string ParentFileId;
        public byte[] ParentIdBytes;
        public bool ParentIdIsExtended;
    }

    internal static class NativeJournalReader
    {
        private const uint AllReasonBits = 0xFFFFFFFFU;
        private const uint FileDeleteReason = 0x00000200U;
        private const int OutputBufferSize = 4 * 1024 * 1024;

        public static ScanResult Read(ScanOptions options, CancellationToken token, IProgress<ScanProgress> progress)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return ReadSnapshot(options, token, progress);
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode == NativeMethods.ErrorJournalEntryDeleted && attempt == 0)
                    {
                        Report(progress, -1, "The journal wrapped during the scan; restarting from its new oldest record…");
                        continue;
                    }

                    throw;
                }
            }

            throw new InvalidOperationException("The USN journal changed too quickly to obtain a stable snapshot.");
        }

        private static ScanResult ReadSnapshot(ScanOptions options, CancellationToken token, IProgress<ScanProgress> progress)
        {
            Report(progress, -1, "Opening " + options.Volume + " and querying its USN journal…");
            using (SafeFileHandle volumeHandle = NativeMethods.OpenVolume(options.Volume))
            {
                JournalInformation journal = NativeMethods.QueryJournal(volumeHandle);
                ScanResult result = new ScanResult();
                result.Volume = options.Volume;
                result.BackendUsed = "native Windows API";
                result.JournalId = journal.JournalId;
                result.FirstUsn = journal.FirstUsn;
                result.NextUsn = journal.NextUsn;

                uint reasonMask = options.VerifyCoverage ? AllReasonBits : FileDeleteReason;
                long startUsn = journal.FirstUsn;
                long snapshotNextUsn = journal.NextUsn;
                byte[] output = new byte[OutputBufferSize];
                DateTime? oldest = null;
                DateTime? newest = null;

                while (startUsn < snapshotNextUsn)
                {
                    token.ThrowIfCancellationRequested();

                    NativeMethods.READ_USN_JOURNAL_DATA_V1 request = new NativeMethods.READ_USN_JOURNAL_DATA_V1();
                    request.StartUsn = startUsn;
                    request.ReasonMask = reasonMask;
                    request.ReturnOnlyOnClose = 0;
                    request.Timeout = 0;
                    request.BytesToWaitFor = 0;
                    request.UsnJournalID = journal.JournalId;
                    request.MinMajorVersion = 2;
                    request.MaxMajorVersion = 3;

                    int bytesReturned;
                    bool success = NativeMethods.ReadJournal(volumeHandle, ref request, output, out bytesReturned);
                    if (!success)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == NativeMethods.ErrorHandleEof)
                        {
                            break;
                        }

                        throw NativeMethods.CreateFriendlyException(error, "Reading the USN journal");
                    }

                    if (bytesReturned < 8)
                    {
                        break;
                    }

                    long returnedNextUsn = BitConverter.ToInt64(output, 0);
                    int offset = 8;
                    while (offset + 8 <= bytesReturned)
                    {
                        int recordLength = BitConverter.ToInt32(output, offset);
                        if (recordLength < 8 || offset + recordLength > bytesReturned)
                        {
                            result.MalformedRecords++;
                            break;
                        }

                        ushort majorVersion = BitConverter.ToUInt16(output, offset + 4);
                        ParsedUsnRecord parsed = null;
                        if (majorVersion == 2 || majorVersion == 3)
                        {
                            parsed = ParseRecord(output, offset, recordLength, majorVersion);
                            if (parsed == null)
                            {
                                result.MalformedRecords++;
                            }
                        }
                        else
                        {
                            result.UnknownVersionRecords++;
                        }

                        if (parsed != null && parsed.Usn < snapshotNextUsn)
                        {
                            result.RecordsScanned++;
                            if (parsed.Timestamp.HasValue)
                            {
                                DateTime timestamp = parsed.Timestamp.Value;
                                if (!oldest.HasValue || timestamp < oldest.Value)
                                {
                                    oldest = timestamp;
                                }
                                if (!newest.HasValue || timestamp > newest.Value)
                                {
                                    newest = timestamp;
                                }
                            }

                            if ((parsed.Reason & FileDeleteReason) != 0 && parsed.Timestamp.HasValue)
                            {
                                DeletionRecord deletion = new DeletionRecord();
                                deletion.Timestamp = parsed.Timestamp.Value;
                                deletion.FileName = parsed.FileName;
                                deletion.IsDirectory = (parsed.FileAttributes & NativeMethods.FileAttributeDirectory) != 0;
                                deletion.BestKnownPath = parsed.FileName;
                                deletion.ParentPath = String.Empty;
                                deletion.PathStatus = options.ResolvePaths ? "Unresolved" : "Not requested";
                                deletion.ReasonCode = parsed.Reason;
                                deletion.ReasonText = ReasonFormatter.Format(parsed.Reason);
                                deletion.FileId = parsed.FileId;
                                deletion.ParentFileId = parsed.ParentFileId;
                                deletion.Usn = parsed.Usn;
                                deletion.MajorVersion = parsed.MajorVersion;
                                deletion.FileAttributes = parsed.FileAttributes;
                                deletion.ParentIdBytes = parsed.ParentIdBytes;
                                deletion.ParentIdIsExtended = parsed.ParentIdIsExtended;
                                result.Deletions.Add(deletion);
                            }
                        }

                        offset += recordLength;
                    }

                    if (returnedNextUsn <= startUsn)
                    {
                        break;
                    }

                    startUsn = returnedNextUsn;
                    if (startUsn > snapshotNextUsn)
                    {
                        startUsn = snapshotNextUsn;
                    }

                    int scanPercent = CalculateScanPercent(journal.FirstUsn, snapshotNextUsn, startUsn, options.ResolvePaths ? 80 : 100);
                    Report(progress, scanPercent,
                        "Reading journal records… " + result.RecordsScanned.ToString("N0", CultureInfo.CurrentCulture) +
                        " examined; " + result.Deletions.Count.ToString("N0", CultureInfo.CurrentCulture) + " deletions found");
                }

                result.OldestRecordTime = oldest;
                result.NewestRecordTime = newest;
                result.CoverageVerified = options.VerifyCoverage && oldest.HasValue && newest.HasValue && result.UnknownVersionRecords == 0;

                if (options.ResolvePaths && result.Deletions.Count > 0)
                {
                    PathResolver.Resolve(volumeHandle, result.Deletions, token, progress, 80, 100);
                }

                if (result.UnknownVersionRecords > 0 || result.MalformedRecords > 0)
                {
                    StringBuilder warning = new StringBuilder();
                    if (result.UnknownVersionRecords > 0)
                    {
                        warning.Append(result.UnknownVersionRecords.ToString("N0", CultureInfo.CurrentCulture));
                        warning.Append(" journal record(s) used an unsupported USN record version and were skipped. ");
                    }
                    if (result.MalformedRecords > 0)
                    {
                        warning.Append(result.MalformedRecords.ToString("N0", CultureInfo.CurrentCulture));
                        warning.Append(" malformed record(s) or buffer tails were skipped. ");
                    }
                    warning.Append("Timeline coverage is therefore marked as unverified.");
                    result.Warning = warning.ToString();
                    result.CoverageVerified = false;
                }

                Report(progress, 100, "Scan complete.");
                return result;
            }
        }

        private static int CalculateScanPercent(long first, long next, long current, int maximum)
        {
            if (next <= first)
            {
                return maximum;
            }

            double ratio = (double)(current - first) / (double)(next - first);
            int value = (int)Math.Round(ratio * maximum, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(maximum, value));
        }

        private static ParsedUsnRecord ParseRecord(byte[] buffer, int offset, int recordLength, ushort majorVersion)
        {
            try
            {
                int minimum = majorVersion == 2 ? 60 : 76;
                if (recordLength < minimum)
                {
                    return null;
                }

                ParsedUsnRecord record = new ParsedUsnRecord();
                record.RecordLength = recordLength;
                record.MajorVersion = majorVersion;

                int fileNameLengthOffset;
                int fileNameOffsetOffset;
                int fileIdOffset;
                int parentIdOffset;
                int idLength;
                int usnOffset;
                int timestampOffset;
                int reasonOffset;
                int attributesOffset;

                if (majorVersion == 2)
                {
                    fileIdOffset = 8;
                    parentIdOffset = 16;
                    idLength = 8;
                    usnOffset = 24;
                    timestampOffset = 32;
                    reasonOffset = 40;
                    attributesOffset = 52;
                    fileNameLengthOffset = 56;
                    fileNameOffsetOffset = 58;
                    record.ParentIdIsExtended = false;
                }
                else
                {
                    fileIdOffset = 8;
                    parentIdOffset = 24;
                    idLength = 16;
                    usnOffset = 40;
                    timestampOffset = 48;
                    reasonOffset = 56;
                    attributesOffset = 68;
                    fileNameLengthOffset = 72;
                    fileNameOffsetOffset = 74;
                    record.ParentIdIsExtended = true;
                }

                ushort fileNameLength = BitConverter.ToUInt16(buffer, offset + fileNameLengthOffset);
                ushort fileNameOffset = BitConverter.ToUInt16(buffer, offset + fileNameOffsetOffset);
                if (fileNameOffset + fileNameLength > recordLength || offset + fileNameOffset + fileNameLength > buffer.Length)
                {
                    return null;
                }

                byte[] fileIdBytes = new byte[idLength];
                byte[] parentIdBytes = new byte[idLength];
                Buffer.BlockCopy(buffer, offset + fileIdOffset, fileIdBytes, 0, idLength);
                Buffer.BlockCopy(buffer, offset + parentIdOffset, parentIdBytes, 0, idLength);

                record.Usn = BitConverter.ToInt64(buffer, offset + usnOffset);
                long fileTime = BitConverter.ToInt64(buffer, offset + timestampOffset);
                if (fileTime > 0)
                {
                    record.Timestamp = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                }
                record.Reason = BitConverter.ToUInt32(buffer, offset + reasonOffset);
                record.FileAttributes = BitConverter.ToUInt32(buffer, offset + attributesOffset);
                record.FileName = Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);
                record.FileId = IdFormatter.ToDisplay(fileIdBytes);
                record.ParentFileId = IdFormatter.ToDisplay(parentIdBytes);
                record.ParentIdBytes = parentIdBytes;
                return record;
            }
            catch
            {
                return null;
            }
        }

        private static void Report(IProgress<ScanProgress> progress, int percent, string message)
        {
            if (progress != null)
            {
                progress.Report(new ScanProgress(percent, message));
            }
        }
    }

    internal static class FsutilJournalReader
    {
        private const uint FileDeleteReason = 0x00000200U;

        public static ScanResult Read(ScanOptions options, CancellationToken token, IProgress<ScanProgress> progress)
        {
            Report(progress, -1, "Starting fsutil compatibility scan…");
            ScanResult result = new ScanResult();
            result.Volume = options.Volume;
            result.BackendUsed = "fsutil CSV compatibility mode";

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.SystemDirectory, "fsutil.exe");
            info.Arguments = "usn readjournal " + options.Volume + " startusn=0 csv";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            try
            {
                info.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                info.StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                // The default process encoding remains usable as a compatibility fallback.
            }

            StringBuilder errorText = new StringBuilder();
            Dictionary<string, int> header = null;
            DateTime? oldest = null;
            DateTime? newest = null;
            long parsedRows = 0;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (!String.IsNullOrEmpty(args.Data))
                    {
                        lock (errorText)
                        {
                            errorText.AppendLine(args.Data);
                        }
                    }
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException("fsutil.exe could not be started.");
                }
                process.BeginErrorReadLine();

                try
                {
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (token.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            token.ThrowIfCancellationRequested();
                        }

                        if (line.Length == 0)
                        {
                            continue;
                        }

                        List<string> fields = CsvParser.ParseLine(line);
                        if (fields.Count < 4)
                        {
                            continue;
                        }

                        if (header == null && LooksLikeHeader(fields))
                        {
                            header = BuildHeaderMap(fields);
                            continue;
                        }

                        FsutilRow row;
                        if (!TryParseRow(fields, header, out row))
                        {
                            result.MalformedRecords++;
                            continue;
                        }

                        parsedRows++;
                        result.RecordsScanned++;
                        if (!oldest.HasValue || row.Timestamp < oldest.Value)
                        {
                            oldest = row.Timestamp;
                        }
                        if (!newest.HasValue || row.Timestamp > newest.Value)
                        {
                            newest = row.Timestamp;
                        }

                        if ((row.Reason & FileDeleteReason) != 0)
                        {
                            DeletionRecord deletion = new DeletionRecord();
                            deletion.Timestamp = row.Timestamp;
                            deletion.FileName = row.FileName;
                            deletion.IsDirectory = (row.FileAttributes & NativeMethods.FileAttributeDirectory) != 0 ||
                                row.FileAttributesText.IndexOf("Directory", StringComparison.OrdinalIgnoreCase) >= 0;
                            deletion.BestKnownPath = row.FileName;
                            deletion.ParentPath = String.Empty;
                            deletion.PathStatus = options.ResolvePaths ? "Unresolved" : "Not requested";
                            deletion.ReasonCode = row.Reason;
                            deletion.ReasonText = String.IsNullOrWhiteSpace(row.ReasonText) ? ReasonFormatter.Format(row.Reason) : row.ReasonText;
                            deletion.FileId = row.FileId;
                            deletion.ParentFileId = row.ParentFileId;
                            deletion.Usn = row.Usn;
                            deletion.MajorVersion = row.MajorVersion;
                            deletion.FileAttributes = row.FileAttributes;
                            deletion.ParentIdBytes = IdFormatter.ParseFsutilId(row.ParentFileId, out deletion.ParentIdIsExtended);
                            result.Deletions.Add(deletion);
                        }

                        if ((parsedRows % 25000) == 0)
                        {
                            Report(progress, -1,
                                "fsutil: " + parsedRows.ToString("N0", CultureInfo.CurrentCulture) +
                                " records examined; " + result.Deletions.Count.ToString("N0", CultureInfo.CurrentCulture) + " deletions found");
                        }
                    }

                    process.WaitForExit();
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch { }
                    }
                }

                if (process.ExitCode != 0)
                {
                    string message;
                    lock (errorText)
                    {
                        message = errorText.ToString().Trim();
                    }
                    if (message.Length == 0)
                    {
                        message = "fsutil exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".";
                    }
                    throw new InvalidOperationException(message);
                }
            }

            result.OldestRecordTime = oldest;
            result.NewestRecordTime = newest;
            result.CoverageVerified = options.VerifyCoverage && oldest.HasValue && newest.HasValue && result.MalformedRecords == 0;

            if (options.ResolvePaths && result.Deletions.Count > 0)
            {
                using (SafeFileHandle volumeHandle = NativeMethods.OpenVolume(options.Volume))
                {
                    PathResolver.Resolve(volumeHandle, result.Deletions, token, progress, 80, 100);
                }
            }

            if (result.MalformedRecords > 0)
            {
                result.Warning = result.MalformedRecords.ToString("N0", CultureInfo.CurrentCulture) +
                    " fsutil output line(s) could not be parsed. Timeline coverage is therefore marked as unverified. " +
                    "Use the native Windows API engine when possible.";
                result.CoverageVerified = false;
            }

            Report(progress, 100, "Scan complete.");
            return result;
        }

        private static bool LooksLikeHeader(List<string> fields)
        {
            int matches = 0;
            foreach (string field in fields)
            {
                string normalized = NormalizeHeader(field);
                if (normalized == "usn" || normalized == "filename" || normalized == "reasonnr" || normalized == "timestamp" || normalized == "fileid" || normalized == "parentfileid")
                {
                    matches++;
                }
            }
            return matches >= 3;
        }

        private static Dictionary<string, int> BuildHeaderMap(List<string> fields)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fields.Count; i++)
            {
                string raw = fields[i] ?? String.Empty;
                string key = NormalizeHeader(raw);
                bool numericLabel = raw.IndexOf('#') >= 0 ||
                    raw.IndexOf(" nr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf(" number", StringComparison.OrdinalIgnoreCase) >= 0;

                if (numericLabel && key == "reason")
                {
                    key = "reasonnumber";
                }
                else if (numericLabel && key == "fileattributes")
                {
                    key = "fileattributesnumber";
                }
                else if (numericLabel && key == "sourceinfo")
                {
                    key = "sourceinfonumber";
                }

                if (key.Length > 0 && !map.ContainsKey(key))
                {
                    map.Add(key, i);
                }
            }
            return map;
        }

        private static string NormalizeHeader(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char ch in value)
            {
                if (Char.IsLetterOrDigit(ch))
                {
                    builder.Append(Char.ToLowerInvariant(ch));
                }
            }
            return builder.ToString();
        }

        private static bool TryParseRow(List<string> fields, Dictionary<string, int> header, out FsutilRow row)
        {
            row = null;
            int usnIndex = GetIndex(header, 0, "usn");
            int fileNameIndex = GetIndex(header, 1, "filename");
            int reasonNumberIndex = GetIndex(header, 3, "reasonnr", "reasonnumber");
            int reasonTextIndex = GetIndex(header, 4, "reason");
            int timestampIndex = GetIndex(header, 5, "timestamp");
            int attributesNumberIndex = GetIndex(header, 6, "fileattributesnumber", "fileattributesnr", "fileattributes");
            int attributesTextIndex = GetIndex(header, 7, "fileattributes");
            int fileIdIndex = GetIndex(header, 8, "fileid");
            int parentIdIndex = GetIndex(header, 9, "parentfileid");
            int majorIndex = GetIndex(header, 13, "majorversion");

            int maximum = Math.Max(Math.Max(Math.Max(fileNameIndex, reasonNumberIndex), timestampIndex), Math.Max(fileIdIndex, parentIdIndex));
            if (maximum < 0 || fields.Count <= maximum)
            {
                return false;
            }

            uint reason;
            if (!TryParseUInt(GetField(fields, reasonNumberIndex), out reason))
            {
                string reasonTextValue = GetField(fields, reasonTextIndex);
                int colon = reasonTextValue.IndexOf(':');
                string prefix = colon >= 0 ? reasonTextValue.Substring(0, colon) : reasonTextValue;
                if (!TryParseUInt(prefix, out reason))
                {
                    return false;
                }
            }

            DateTime timestamp;
            if (!TryParseDateTime(GetField(fields, timestampIndex), out timestamp))
            {
                return false;
            }

            uint attributes = 0;
            TryParseUInt(GetField(fields, attributesNumberIndex), out attributes);
            long usn = 0;
            TryParseLong(GetField(fields, usnIndex), out usn);
            ushort major = 0;
            UInt16.TryParse(GetField(fields, majorIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out major);

            row = new FsutilRow();
            row.Usn = usn;
            row.FileName = GetField(fields, fileNameIndex);
            row.Reason = reason;
            row.ReasonText = GetField(fields, reasonTextIndex);
            row.Timestamp = timestamp;
            row.FileAttributes = attributes;
            row.FileAttributesText = GetField(fields, attributesTextIndex);
            row.FileId = GetField(fields, fileIdIndex);
            row.ParentFileId = GetField(fields, parentIdIndex);
            row.MajorVersion = major;
            return row.FileName != null;
        }

        private static int GetIndex(Dictionary<string, int> header, int defaultIndex, params string[] names)
        {
            if (header != null)
            {
                foreach (string name in names)
                {
                    int value;
                    if (header.TryGetValue(name, out value))
                    {
                        return value;
                    }
                }
            }
            return defaultIndex;
        }

        private static string GetField(List<string> fields, int index)
        {
            if (index < 0 || index >= fields.Count)
            {
                return String.Empty;
            }
            return fields[index].Trim();
        }

        private static bool TryParseUInt(string text, out uint value)
        {
            value = 0;
            if (String.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return UInt32.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            return UInt32.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseLong(string text, out long value)
        {
            value = 0;
            if (String.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong unsigned;
                if (UInt64.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unsigned))
                {
                    value = unchecked((long)unsigned);
                    return true;
                }
                return false;
            }
            return Int64.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseDateTime(string text, out DateTime value)
        {
            DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, styles, out value))
            {
                return true;
            }
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, styles, out value);
        }

        private static void Report(IProgress<ScanProgress> progress, int percent, string message)
        {
            if (progress != null)
            {
                progress.Report(new ScanProgress(percent, message));
            }
        }

        private sealed class FsutilRow
        {
            public long Usn;
            public string FileName;
            public uint Reason;
            public string ReasonText;
            public DateTime Timestamp;
            public uint FileAttributes;
            public string FileAttributesText;
            public string FileId;
            public string ParentFileId;
            public ushort MajorVersion;
        }
    }

    internal static class CsvParser
    {
        public static List<string> ParseLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }

    internal static class ReasonFormatter
    {
        private sealed class ReasonName
        {
            public uint Flag;
            public string Name;

            public ReasonName(uint flag, string name)
            {
                Flag = flag;
                Name = name;
            }
        }

        private static readonly ReasonName[] Names =
        {
            new ReasonName(0x00000001U, "Data overwrite"),
            new ReasonName(0x00000002U, "Data extend"),
            new ReasonName(0x00000004U, "Data truncation"),
            new ReasonName(0x00000010U, "Named data overwrite"),
            new ReasonName(0x00000020U, "Named data extend"),
            new ReasonName(0x00000040U, "Named data truncation"),
            new ReasonName(0x00000100U, "File create"),
            new ReasonName(0x00000200U, "File delete"),
            new ReasonName(0x00000400U, "EA change"),
            new ReasonName(0x00000800U, "Security change"),
            new ReasonName(0x00001000U, "Rename old name"),
            new ReasonName(0x00002000U, "Rename new name"),
            new ReasonName(0x00004000U, "Indexable change"),
            new ReasonName(0x00008000U, "Basic info change"),
            new ReasonName(0x00010000U, "Hard-link change"),
            new ReasonName(0x00020000U, "Compression change"),
            new ReasonName(0x00040000U, "Encryption change"),
            new ReasonName(0x00080000U, "Object-ID change"),
            new ReasonName(0x00100000U, "Reparse-point change"),
            new ReasonName(0x00200000U, "Stream change"),
            new ReasonName(0x00400000U, "Transacted change"),
            new ReasonName(0x00800000U, "Integrity change"),
            new ReasonName(0x80000000U, "Close")
        };

        public static string Format(uint reason)
        {
            List<string> values = new List<string>();
            foreach (ReasonName item in Names)
            {
                if ((reason & item.Flag) != 0)
                {
                    values.Add(item.Name);
                }
            }

            return values.Count == 0 ? "Unknown reason bits" : String.Join(" | ", values.ToArray());
        }
    }

    internal static class IdFormatter
    {
        public static string ToDisplay(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(rawBytes.Length * 2);
            for (int i = rawBytes.Length - 1; i >= 0; i--)
            {
                builder.Append(rawBytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        public static byte[] ParseFsutilId(string value, out bool extended)
        {
            extended = false;
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            StringBuilder hexBuilder = new StringBuilder();
            foreach (char ch in value)
            {
                if (Uri.IsHexDigit(ch))
                {
                    hexBuilder.Append(ch);
                }
            }

            string hex = hexBuilder.ToString();
            if (hex.Length == 0)
            {
                return null;
            }

            int bytes = hex.Length > 16 ? 16 : 8;
            extended = bytes == 16;
            hex = hex.PadLeft(bytes * 2, '0');
            if (hex.Length > bytes * 2)
            {
                hex = hex.Substring(hex.Length - (bytes * 2));
            }

            byte[] raw = new byte[bytes];
            try
            {
                for (int i = 0; i < bytes; i++)
                {
                    int source = hex.Length - 2 - (i * 2);
                    raw[i] = Byte.Parse(hex.Substring(source, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                return raw;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class PathLookupResult
    {
        public bool Success;
        public string Path;
    }

    internal static class PathResolver
    {
        public static void Resolve(
            SafeFileHandle volumeHandle,
            List<DeletionRecord> records,
            CancellationToken token,
            IProgress<ScanProgress> progress,
            int startPercent,
            int endPercent)
        {
            Dictionary<string, PathLookupResult> cache = new Dictionary<string, PathLookupResult>(StringComparer.Ordinal);
            int total = records.Count;
            for (int i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                DeletionRecord record = records[i];
                if (record.ParentIdBytes == null || record.ParentIdBytes.Length == 0)
                {
                    record.PathStatus = "Parent ID unavailable";
                    continue;
                }

                string key = (record.ParentIdIsExtended ? "E:" : "F:") + Convert.ToBase64String(record.ParentIdBytes);
                PathLookupResult lookup;
                if (!cache.TryGetValue(key, out lookup))
                {
                    lookup = new PathLookupResult();
                    string path;
                    lookup.Success = NativeMethods.TryResolveFileIdPath(volumeHandle, record.ParentIdBytes, record.ParentIdIsExtended, out path);
                    lookup.Path = path;
                    cache.Add(key, lookup);
                }

                if (lookup.Success && !String.IsNullOrEmpty(lookup.Path))
                {
                    record.ParentPath = lookup.Path;
                    record.BestKnownPath = CombinePath(lookup.Path, record.FileName);
                    record.PathStatus = "Parent resolves now";
                }
                else
                {
                    record.BestKnownPath = record.FileName;
                    record.PathStatus = "Parent unavailable";
                }

                if ((i % 25) == 0 || i == total - 1)
                {
                    double ratio = total == 0 ? 1D : (double)(i + 1) / (double)total;
                    int percent = startPercent + (int)Math.Round((endPercent - startPercent) * ratio, MidpointRounding.AwayFromZero);
                    if (progress != null)
                    {
                        progress.Report(new ScanProgress(percent,
                            "Resolving surviving parent folders… " + (i + 1).ToString("N0", CultureInfo.CurrentCulture) +
                            " of " + total.ToString("N0", CultureInfo.CurrentCulture)));
                    }
                }
            }
        }

        private static string CombinePath(string parent, string name)
        {
            if (String.IsNullOrEmpty(parent))
            {
                return name ?? String.Empty;
            }
            if (String.IsNullOrEmpty(name))
            {
                return parent;
            }
            return parent.EndsWith("\\", StringComparison.Ordinal) ? parent + name : parent + "\\" + name;
        }
    }

    internal static class NativeMethods
    {
        internal const uint FileAttributeDirectory = 0x00000010U;
        internal const int ErrorHandleEof = 38;
        internal const int ErrorJournalEntryDeleted = 1178;
        private const int ErrorJournalNotActive = 1179;
        private const int ErrorAccessDenied = 5;
        private const int ErrorInvalidFunction = 1;
        private const int ErrorInvalidParameter = 87;

        private const uint GenericRead = 0x80000000U;
        private const uint FileShareRead = 0x00000001U;
        private const uint FileShareWrite = 0x00000002U;
        private const uint FileShareDelete = 0x00000004U;
        private const uint OpenExisting = 3U;
        private const uint FileFlagBackupSemantics = 0x02000000U;
        private const uint FsctlReadUsnJournal = 0x000900BBU;
        private const uint FsctlQueryUsnJournal = 0x000900F4U;
        private const int FileIdType = 0;
        private const int ExtendedFileIdType = 2;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct READ_USN_JOURNAL_DATA_V1
        {
            public long StartUsn;
            public uint ReasonMask;
            public uint ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public ulong UsnJournalID;
            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            [Out] byte[] lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref READ_USN_JOURNAL_DATA_V1 lpInBuffer,
            int nInBufferSize,
            [Out] byte[] lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle OpenFileById(
            SafeFileHandle hVolumeHint,
            IntPtr lpFileId,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwFlagsAndAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags);

        internal static SafeFileHandle OpenVolume(string volume)
        {
            string normalized = volume.TrimEnd('\\');
            SafeFileHandle handle = CreateFile(
                "\\\\.\\" + normalized,
                GenericRead,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw CreateFriendlyException(error, "Opening volume " + normalized);
            }

            return handle;
        }

        internal static JournalInformation QueryJournal(SafeFileHandle volumeHandle)
        {
            byte[] output = new byte[128];
            int bytesReturned;
            bool success = DeviceIoControl(
                volumeHandle,
                FsctlQueryUsnJournal,
                IntPtr.Zero,
                0,
                output,
                output.Length,
                out bytesReturned,
                IntPtr.Zero);

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                throw CreateFriendlyException(error, "Querying the USN journal");
            }

            if (bytesReturned < 56)
            {
                throw new InvalidDataException("Windows returned an unexpectedly short USN journal information structure.");
            }

            JournalInformation info = new JournalInformation();
            info.JournalId = BitConverter.ToUInt64(output, 0);
            info.FirstUsn = BitConverter.ToInt64(output, 8);
            info.NextUsn = BitConverter.ToInt64(output, 16);
            info.LowestValidUsn = BitConverter.ToInt64(output, 24);
            info.MaxUsn = BitConverter.ToInt64(output, 32);
            info.MaximumSize = BitConverter.ToUInt64(output, 40);
            info.AllocationDelta = BitConverter.ToUInt64(output, 48);
            return info;
        }

        internal static bool ReadJournal(
            SafeFileHandle volumeHandle,
            ref READ_USN_JOURNAL_DATA_V1 request,
            byte[] output,
            out int bytesReturned)
        {
            return DeviceIoControl(
                volumeHandle,
                FsctlReadUsnJournal,
                ref request,
                Marshal.SizeOf(typeof(READ_USN_JOURNAL_DATA_V1)),
                output,
                output.Length,
                out bytesReturned,
                IntPtr.Zero);
        }

        internal static bool TryResolveFileIdPath(SafeFileHandle volumeHandle, byte[] idBytes, bool extended, out string path)
        {
            path = null;
            if (idBytes == null || (idBytes.Length != 8 && idBytes.Length != 16))
            {
                return false;
            }

            IntPtr descriptor = Marshal.AllocHGlobal(24);
            try
            {
                byte[] zero = new byte[24];
                Marshal.Copy(zero, 0, descriptor, zero.Length);
                Marshal.WriteInt32(descriptor, 0, 24);
                Marshal.WriteInt32(descriptor, 4, extended ? ExtendedFileIdType : FileIdType);
                Marshal.Copy(idBytes, 0, IntPtr.Add(descriptor, 8), idBytes.Length);

                using (SafeFileHandle fileHandle = OpenFileById(
                    volumeHandle,
                    descriptor,
                    0,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    FileFlagBackupSemantics))
                {
                    if (fileHandle.IsInvalid)
                    {
                        return false;
                    }

                    StringBuilder builder = new StringBuilder(1024);
                    uint length = GetFinalPathNameByHandle(fileHandle, builder, (uint)builder.Capacity, 0);
                    if (length == 0)
                    {
                        return false;
                    }

                    if (length >= builder.Capacity)
                    {
                        builder = new StringBuilder((int)length + 1);
                        length = GetFinalPathNameByHandle(fileHandle, builder, (uint)builder.Capacity, 0);
                        if (length == 0)
                        {
                            return false;
                        }
                    }

                    path = NormalizeFinalPath(builder.ToString());
                    return path.Length > 0;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(descriptor);
            }
        }

        private static string NormalizeFinalPath(string path)
        {
            if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            {
                return "\\\\" + path.Substring(8);
            }
            if (path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(4);
            }
            return path;
        }

        internal static Win32Exception CreateFriendlyException(int error, string operation)
        {
            string hint = String.Empty;
            if (error == ErrorAccessDenied)
            {
                hint = " Administrator access is required, and the volume must allow direct journal reads.";
            }
            else if (error == ErrorJournalNotActive)
            {
                hint = " This volume has no active USN journal. The application will not create one because doing so cannot restore older history.";
            }
            else if (error == ErrorJournalEntryDeleted)
            {
                hint = " The journal wrapped and removed the requested older records while the scan was running.";
            }
            else if (error == ErrorInvalidFunction)
            {
                hint = " The selected volume may not be NTFS or may not support the USN change journal.";
            }
            else if (error == ErrorInvalidParameter)
            {
                hint = " Windows rejected the journal request or record-version range.";
            }

            Win32Exception system = new Win32Exception(error);
            return new Win32Exception(error, operation + " failed: " + system.Message + hint);
        }
    }
}
