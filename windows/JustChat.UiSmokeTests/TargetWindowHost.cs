using System.Windows.Forms;
using WinFormsApplication = System.Windows.Forms.Application;

namespace JustChat.UiSmokeTests;

internal sealed class TargetWindowHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private Form? _form;
    private TextBox? _firstEditor;
    private TextBox? _secondEditor;

    public TargetWindowHost()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "JustChat acceptance target"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("The acceptance target window did not start.");
        }
    }

    public nint Handle => RequiredForm().Handle;

    private void Run()
    {
        WinFormsApplication.SetHighDpiMode(HighDpiMode.SystemAware);
        WinFormsApplication.EnableVisualStyles();
        WinFormsApplication.SetCompatibleTextRenderingDefault(false);

        _firstEditor = CreateEditor("TargetA", "Target A");
        _secondEditor = CreateEditor("TargetB", "Target B");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.Controls.Add(_firstEditor, 0, 0);
        layout.Controls.Add(_secondEditor, 1, 0);

        _form = new Form
        {
            Text = "JustChat Acceptance Target",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(30, 30),
            Width = 850,
            Height = 480
        };
        _form.Controls.Add(layout);
        _form.Shown += (_, _) => _ready.Set();
        WinFormsApplication.Run(_form);
    }

    public void FocusEditor(int index)
    {
        var form = RequiredForm();
        var editor = index == 0 ? _firstEditor! : _secondEditor!;
        using var completed = new ManualResetEventSlim();
        form.BeginInvoke(new Action(() =>
        {
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Show();
            form.Activate();
            editor.Focus();
            completed.Set();
        }));
        if (!completed.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Could not focus the acceptance target editor.");
        }
    }

    private Form RequiredForm() => _form ?? throw new InvalidOperationException("Target form is unavailable.");

    private static TextBox CreateEditor(string name, string accessibleName) => new()
    {
        Name = name,
        AccessibleName = accessibleName,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 12),
        Margin = new Padding(6)
    };

    public void Dispose()
    {
        if (_form is { IsDisposed: false } form)
        {
            form.Invoke(new Action(() => form.Close()));
        }

        _thread.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
    }
}
