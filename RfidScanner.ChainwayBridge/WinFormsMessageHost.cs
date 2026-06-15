using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RfidScanner.ChainwayBridge;

/// <summary>
/// Hidden WinForms STA thread with message pump — required by Chainway BLEDeviceAPI
/// (same as official MainForm.cs). Prevents Windows Bluetooth pair popup on WPF dispatcher.
/// </summary>
public sealed class WinFormsMessageHost : IDisposable
{
    private readonly object _lock = new();
    private Thread? _thread;
    private Form? _form;
    private ManualResetEventSlim? _ready;
    private volatile bool _running;
    private bool _disposed;

    public Form? Form => _form;
    public bool IsRunning => _running;

    public void Start()
    {
        lock (_lock)
        {
            if (_running && _form != null && !_form.IsDisposed)
                return;

            _ready = new ManualResetEventSlim(false);
            _thread = new Thread(ThreadProc)
            {
                IsBackground = true,
                Name = "ChainwayBleMessageHost"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("Chainway BLE message host failed to start.");
        }
    }

    public void Invoke(Action action)
    {
        if (_disposed)
            return;

        if (!_running || _form == null || _form.IsDisposed)
            Start();

        var form = _form;
        if (form == null || form.IsDisposed)
            return;

        try
        {
            if (form.InvokeRequired)
                form.Invoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
            // Host is shutting down.
        }
    }

    private void ThreadProc()
    {
        try
        {
            Application.EnableVisualStyles();
            var form = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                Size = new Size(1, 1),
                Opacity = 0
            };

            form.Load += (_, _) =>
            {
                _running = true;
                _ready?.Set();
            };

            _form = form;
            Application.Run(form);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleMessageHost failed: {ex.Message}");
            _ready?.Set();
        }
        finally
        {
            _running = false;
            _form = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var form = _form;
        if (form == null || form.IsDisposed)
            return;

        try
        {
            if (form.InvokeRequired)
                form.Invoke((Action)(() => Application.ExitThread()));
            else
                Application.ExitThread();
        }
        catch
        {
            // ignore
        }

        try
        {
            _thread?.Join(2500);
        }
        catch
        {
            // ignore
        }

        _ready?.Dispose();
        _thread = null;
    }
}
