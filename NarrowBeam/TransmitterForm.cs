using System;
using System.Drawing;
using System.Windows.Forms;

namespace NarrowBeam;

internal sealed class TransmitterForm : Form
{
    private readonly ComboBox _deviceComboBox;
    private readonly Button _refreshDevicesButton;
    private readonly NumericUpDown _frequencyUpDown;
    private readonly TrackBar _bandwidthTrackBar;
    private readonly Label _bandwidthValueLabel;
    private readonly NumericUpDown _gainUpDown;
    private readonly CheckBox _ampCheckBox;
    private readonly CheckBox _testPatternCheckBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;
    private readonly TextBox _logTextBox;

    private TransmitterSession? _session;

    public TransmitterForm()
    {
        Text = "NarrowBeam - Transmitter";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 520);

        var settingsGroup = new GroupBox
        {
            Text = "Transmitter Settings",
            Location = new Point(16, 16),
            Size = new Size(728, 220),
        };

        var deviceLabel = new Label { Text = "Camera:", AutoSize = true, Location = new Point(20, 34) };
        _deviceComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 30),
            Size = new Size(440, 23),
        };

        _refreshDevicesButton = new Button
        {
            Text = "Refresh",
            Location = new Point(560, 28),
            Size = new Size(90, 27),
        };
        _refreshDevicesButton.Click += (_, _) => RefreshDevices();

        var frequencyLabel = new Label { Text = "Frequency:", AutoSize = true, Location = new Point(20, 76) };
        _frequencyUpDown = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 0.250M,
            Minimum = 1,
            Maximum = 6000,
            Value = 427.250M,
            Location = new Point(100, 72),
            Size = new Size(120, 23),
        };

        var mhzLabel1 = new Label { Text = "MHz", AutoSize = true, Location = new Point(228, 76) };

        var bandwidthLabel = new Label { Text = "Bandwidth:", AutoSize = true, Location = new Point(300, 76) };
        _bandwidthTrackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 16,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            Value = 0,
            Location = new Point(384, 64),
            Size = new Size(170, 45),
        };
        _bandwidthTrackBar.ValueChanged += (_, _) => UpdateBandwidthLabel();

        _bandwidthValueLabel = new Label
        {
            Text = "Unlimited",
            AutoSize = true,
            Location = new Point(562, 76),
        };

        var gainLabel = new Label { Text = "TX Gain:", AutoSize = true, Location = new Point(20, 118) };
        _gainUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 47,
            Value = 30,
            Location = new Point(100, 114),
            Size = new Size(80, 23),
        };

        _ampCheckBox = new CheckBox
        {
            Text = "Enable HackRF amp",
            AutoSize = true,
            Location = new Point(220, 116),
        };

        _testPatternCheckBox = new CheckBox
        {
            Text = "Use test pattern instead of webcam",
            AutoSize = true,
            Location = new Point(20, 156),
        };
        _testPatternCheckBox.CheckedChanged += (_, _) => UpdateDeviceControls();

        _startButton = new Button
        {
            Text = "Start",
            Location = new Point(20, 184),
            Size = new Size(100, 28),
        };
        _startButton.Click += StartButton_Click;

        _stopButton = new Button
        {
            Text = "Stop",
            Location = new Point(132, 184),
            Size = new Size(100, 28),
            Enabled = false,
        };
        _stopButton.Click += StopButton_Click;

        _statusLabel = new Label
        {
            Text = "Status: Idle",
            AutoSize = true,
            Location = new Point(260, 190),
        };

        settingsGroup.Controls.Add(deviceLabel);
        settingsGroup.Controls.Add(_deviceComboBox);
        settingsGroup.Controls.Add(_refreshDevicesButton);
        settingsGroup.Controls.Add(frequencyLabel);
        settingsGroup.Controls.Add(_frequencyUpDown);
        settingsGroup.Controls.Add(mhzLabel1);
        settingsGroup.Controls.Add(bandwidthLabel);
        settingsGroup.Controls.Add(_bandwidthTrackBar);
        settingsGroup.Controls.Add(_bandwidthValueLabel);
        settingsGroup.Controls.Add(gainLabel);
        settingsGroup.Controls.Add(_gainUpDown);
        settingsGroup.Controls.Add(_ampCheckBox);
        settingsGroup.Controls.Add(_testPatternCheckBox);
        settingsGroup.Controls.Add(_startButton);
        settingsGroup.Controls.Add(_stopButton);
        settingsGroup.Controls.Add(_statusLabel);

        _logTextBox = new TextBox
        {
            Location = new Point(16, 252),
            Size = new Size(728, 248),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
        };

        Controls.Add(settingsGroup);
        Controls.Add(_logTextBox);

        UpdateBandwidthLabel();
        Load += (_, _) => RefreshDevices();
        FormClosing += (_, _) =>
        {
            if (_session is not null)
            {
                _session.Dispose();
                _session = null;
            }
        };
    }

    private void RefreshDevices()
    {
        try
        {
            _deviceComboBox.Items.Clear();
            foreach (string device in VideoDeviceDiscovery.GetVideoDevices())
                _deviceComboBox.Items.Add(device);

            if (_deviceComboBox.Items.Count > 0)
            {
                _deviceComboBox.SelectedIndex = 0;
                AppendLog($"Loaded {_deviceComboBox.Items.Count} video device(s).");
            }
            else
            {
                AppendLog($"No video devices found. FFmpeg path: {VideoDeviceDiscovery.ResolveFfmpegExecutable()}");
            }

            UpdateDeviceControls();
        }
        catch (Exception ex)
        {
            AppendLog($"Device discovery failed: {ex.Message}");
        }
    }

    private void UpdateDeviceControls()
    {
        bool webcamEnabled = !_testPatternCheckBox.Checked;
        _deviceComboBox.Enabled = webcamEnabled;
        _refreshDevicesButton.Enabled = webcamEnabled;
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_session is not null)
                throw new InvalidOperationException("The transmitter is already running.");

            if (!_testPatternCheckBox.Checked && _deviceComboBox.SelectedItem is null)
                throw new InvalidOperationException("Select a camera or enable test pattern mode.");

            var settings = new TransmitterSettings
            {
                FrequencyMhz = (double)_frequencyUpDown.Value,
                BandwidthMhz = GetBandwidthMHz(),
                GainDb = (int)_gainUpDown.Value,
                AmpEnabled = _ampCheckBox.Checked,
                UseTestPattern = _testPatternCheckBox.Checked,
                DeviceName = _testPatternCheckBox.Checked ? null : _deviceComboBox.SelectedItem?.ToString(),
            };

            _session = new TransmitterSession(AppendLog);
            _session.Start(settings);

            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _statusLabel.Text = "Status: Transmitting";
        }
        catch (Exception ex)
        {
            AppendLog($"Start failed: {ex.Message}");
            _session?.Dispose();
            _session = null;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _statusLabel.Text = "Status: Error";
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _session?.Dispose();
            _session = null;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _statusLabel.Text = "Status: Idle";
        }
        catch (Exception ex)
        {
            AppendLog($"Stop failed: {ex.Message}");
            _statusLabel.Text = "Status: Error";
        }
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private double GetBandwidthMHz()
    {
        return _bandwidthTrackBar.Value / 2.0;
    }

    private void UpdateBandwidthLabel()
    {
        double bw = GetBandwidthMHz();
        _bandwidthValueLabel.Text = bw > 0 ? $"{bw:F1} MHz" : "Unlimited";
    }
}
