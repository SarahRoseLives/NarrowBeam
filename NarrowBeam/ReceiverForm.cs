using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NarrowBeam;

internal sealed class ReceiverForm : Form
{
    private readonly ComboBox _deviceComboBox;
    private readonly Button _refreshDevicesButton;
    private readonly ComboBox _presetComboBox;
    private readonly NumericUpDown _frequencyUpDown;
    private readonly NumericUpDown _gainUpDown;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;
    private readonly PictureBox _videoBox;
    private readonly System.Windows.Forms.Timer _videoTimer;

    private volatile bool _running;
    private Thread? _rxThread;
    private AmVideoDemodulator? _demodulator;
    private IntPtr _dev;
    private RtlSdr.ReadAsyncCallback? _asyncCallback; // Keep reference to prevent GC

    public ReceiverForm()
    {
        Text = "NarrowBeam - Receiver";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 480);

        var settingsGroup = new GroupBox
        {
            Text = "Receiver Settings",
            Location = new Point(16, 16),
            Size = new Size(540, 160),
        };

        var deviceLabel = new Label { Text = "SDR Device:", AutoSize = true, Location = new Point(20, 34) };
        _deviceComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 30),
            Size = new Size(330, 23),
        };

        _refreshDevicesButton = new Button
        {
            Text = "Refresh",
            Location = new Point(440, 28),
            Size = new Size(90, 27),
        };
        _refreshDevicesButton.Click += (_, _) => RefreshDevices();

        var presetLabel = new Label { Text = "Preset:", AutoSize = true, Location = new Point(20, 76) };
        _presetComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 72),
            Size = new Size(160, 23),
        };
        _presetComboBox.Items.Add(new AtvChannel("Custom", 0));
        foreach (AtvChannel channel in AtvChannels.All)
            _presetComboBox.Items.Add(channel);
        _presetComboBox.DisplayMember = nameof(AtvChannel.Name);
        _presetComboBox.SelectedIndexChanged += PresetChanged;

        var frequencyLabel = new Label { Text = "Frequency:", AutoSize = true, Location = new Point(280, 76) };
        _frequencyUpDown = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 0.250M,
            Minimum = 1,
            Maximum = 6000,
            Value = 427.250M,
            Location = new Point(350, 72),
            Size = new Size(100, 23),
        };

        var gainLabel = new Label { Text = "RX Gain:", AutoSize = true, Location = new Point(20, 118) };
        _gainUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 50,
            Value = 30,
            Location = new Point(100, 114),
            Size = new Size(60, 23),
        };

        _startButton = new Button
        {
            Text = "Start",
            Location = new Point(280, 112),
            Size = new Size(100, 28),
        };
        _startButton.Click += StartButton_Click;

        _stopButton = new Button
        {
            Text = "Stop",
            Location = new Point(390, 112),
            Size = new Size(100, 28),
            Enabled = false,
        };
        _stopButton.Click += StopButton_Click;

        _statusLabel = new Label
        {
            Text = "Status: Idle",
            AutoSize = true,
            Location = new Point(20, 144),
        };

        settingsGroup.Controls.Add(deviceLabel);
        settingsGroup.Controls.Add(_deviceComboBox);
        settingsGroup.Controls.Add(_refreshDevicesButton);
        settingsGroup.Controls.Add(presetLabel);
        settingsGroup.Controls.Add(_presetComboBox);
        settingsGroup.Controls.Add(frequencyLabel);
        settingsGroup.Controls.Add(_frequencyUpDown);
        settingsGroup.Controls.Add(gainLabel);
        settingsGroup.Controls.Add(_gainUpDown);
        settingsGroup.Controls.Add(_startButton);
        settingsGroup.Controls.Add(_stopButton);
        settingsGroup.Controls.Add(_statusLabel);

        var videoGroup = new GroupBox
        {
            Text = "Live Video",
            Location = new Point(570, 16),
            Size = new Size(410, 360),
        };

        _videoBox = new PictureBox
        {
            Location = new Point(10, 22),
            Size = new Size(390, 330),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        videoGroup.Controls.Add(_videoBox);

        Controls.Add(settingsGroup);
        Controls.Add(videoGroup);

        _videoTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _videoTimer.Tick += VideoTimer_Tick;

        _presetComboBox.SelectedIndex = 0;
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        _deviceComboBox.Items.Clear();
        try
        {
            uint count = RtlSdr.rtlsdr_get_device_count();
            for (uint i = 0; i < count; i++)
            {
                string name = Marshal.PtrToStringAnsi(RtlSdr.rtlsdr_get_device_name(i)) ?? "Unknown RTL-SDR";
                _deviceComboBox.Items.Add($"RTL-SDR #{i}: {name}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error listing devices: {ex.Message}");
        }

        if (_deviceComboBox.Items.Count > 0)
            _deviceComboBox.SelectedIndex = 0;
    }

    private void PresetChanged(object? sender, EventArgs e)
    {
        if (_presetComboBox.SelectedItem is AtvChannel channel && channel.FrequencyMhz > 0)
        {
            _frequencyUpDown.Value = channel.FrequencyMhz;
        }
    }

    // Offset tuning to avoid DC spike (LO leakage)
    private const int TunerOffsetHz = 500_000; 

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_deviceComboBox.SelectedIndex < 0) return;

        try
        {
            if (_running) return;

            uint index = (uint)_deviceComboBox.SelectedIndex;
            int openRes = RtlSdr.rtlsdr_open(out _dev, index);
            if (openRes != 0) throw new InvalidOperationException($"Failed to open device: {openRes}");

            uint targetFreq = (uint)(_frequencyUpDown.Value * 1_000_000);
            
            // Set frequency (offset by 1 MHz to maximize bandwidth)
            // Tuning Center = Target + 1.0 MHz.
            // Target is at -1.0 MHz.
            // Passband (2.4 MSPS) = -1.2 to +1.2 MHz.
            // Video (USB) fits from -1.0 to +1.2 MHz (2.2 MHz BW).
            uint tuneFreq = targetFreq + 1_000_000;
            
            int r;
            r = RtlSdr.rtlsdr_set_center_freq(_dev, tuneFreq);
            if (r < 0) throw new Exception("Failed to set frequency");

            // Set Sample Rate (2.4 MSPS for max stable BW)
            r = RtlSdr.rtlsdr_set_sample_rate(_dev, 2_400_000);
            if (r < 0) throw new Exception("Failed to set sample rate");

            // Set Gain
            r = RtlSdr.rtlsdr_set_tuner_gain_mode(_dev, 1); // Manual
            if (r < 0) throw new Exception("Failed to set manual gain mode");

            // Convert UI gain (dB) to tenths of dB for librtlsdr
            int gainTenthsDb = (int)(_gainUpDown.Value * 10);
            r = RtlSdr.rtlsdr_set_tuner_gain(_dev, gainTenthsDb);
            
            r = RtlSdr.rtlsdr_reset_buffer(_dev);
            if (r < 0) throw new Exception("Failed to reset buffer");

            _demodulator = new AmVideoDemodulator(2_400_000);
            _running = true;

            // Important: rtlsdr_read_async blocks, so run in thread
            _rxThread = new Thread(RxLoop) { IsBackground = true };
            _rxThread.Start();
            
            _videoTimer.Start();

            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _statusLabel.Text = $"RX: {targetFreq/1e6:F3} MHz";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Start failed: {ex.Message}");
            StopRx();
        }
    }

    private void RxLoop()
    {
        // Create the delegate and store it to prevent GC
        _asyncCallback = new RtlSdr.ReadAsyncCallback(RxCallback);
        
        // This blocks until cancel_async is called or an error occurs
        int result = RtlSdr.rtlsdr_read_async(_dev, _asyncCallback, IntPtr.Zero, 0, 0);
        
        if (result != 0)
        {
            // Handle error (invoked on RX thread)
            this.Invoke((MethodInvoker)delegate {
                 MessageBox.Show($"Async read failed: {result}");
                 StopRx();
            });
        }
    }

    private void RxCallback(IntPtr buf, uint len, IntPtr ctx)
    {
        if (!_running)
        {
            RtlSdr.rtlsdr_cancel_async(_dev);
            return;
        }
        
        unsafe
        {
            // Process the raw IQ data
            if (_demodulator != null)
            {
                _demodulator.ProcessIq((byte*)buf, (int)len);
            }
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        StopRx();
    }

    private void StopRx()
    {
        if (!_running) return;
        _running = false;
        
        if (_dev != IntPtr.Zero)
        {
            RtlSdr.rtlsdr_cancel_async(_dev);
        }

        if (_rxThread != null && _rxThread.IsAlive)
        {
            // Give async read chance to cancel
            _rxThread.Join(500);
        }

        if (_dev != IntPtr.Zero)
        {
            RtlSdr.rtlsdr_close(_dev);
            _dev = IntPtr.Zero;
        }

        _videoTimer.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        _statusLabel.Text = "Status: Idle";
    }

    private void VideoTimer_Tick(object? sender, EventArgs e)
    {
        if (_demodulator != null && _running)
        {
            var frame = _demodulator.GetFrame();
            if (frame != null)
            {
                var old = _videoBox.Image;
                _videoBox.Image = frame;
                old?.Dispose();
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopRx();
        base.OnFormClosing(e);
    }
}
