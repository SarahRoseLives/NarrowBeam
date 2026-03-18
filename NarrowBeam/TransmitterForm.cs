using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NarrowBeam;

internal sealed class TransmitterForm : Form
{
    private readonly ComboBox _deviceComboBox;
    private readonly Button _refreshDevicesButton;
    private readonly ComboBox _presetComboBox;
    private readonly NumericUpDown _frequencyUpDown;
    private readonly TrackBar _bandwidthTrackBar;
    private readonly Label _bandwidthValueLabel;
    private readonly NumericUpDown _gainUpDown;
    private readonly CheckBox _ampCheckBox;
    private readonly TextBox _callsignTextBox;
    private readonly CheckBox _testPatternCheckBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;
    private readonly TextBox _logTextBox;
    private readonly PictureBox _previewBox;
    private readonly System.Windows.Forms.Timer _previewTimer;

    private readonly AppSettings _appSettings;
    private bool _updatingPresetFromCode;
    private TransmitterSession? _session;

    public TransmitterForm()
    {
        _appSettings = LoadSettings();

        Text = "NarrowBeam - Transmitter";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 560);

        var settingsGroup = new GroupBox
        {
            Text = "Transmitter Settings",
            Location = new Point(16, 16),
            Size = new Size(540, 260),
        };

        var previewGroup = new GroupBox
        {
            Text = "Live Monitor",
            Location = new Point(570, 16),
            Size = new Size(410, 360),
        };

        var deviceLabel = new Label { Text = "Camera:", AutoSize = true, Location = new Point(20, 34) };
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
            Value = _appSettings.FrequencyMhz,
            Location = new Point(350, 72),
            Size = new Size(100, 23),
        };
        _frequencyUpDown.ValueChanged += FrequencyChanged;

        var mhzLabel = new Label { Text = "MHz", AutoSize = true, Location = new Point(455, 76) };

        var bandwidthLabel = new Label { Text = "Bandwidth:", AutoSize = true, Location = new Point(20, 118) };
        _bandwidthTrackBar = new TrackBar
        {
            Minimum = 2,
            Maximum = 16,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            Value = Math.Clamp((int)Math.Round(_appSettings.BandwidthMhz * 2.0), 2, 16),
            Location = new Point(100, 106),
            Size = new Size(170, 45),
        };
        _bandwidthTrackBar.ValueChanged += (_, _) => UpdateBandwidthLabel();

        _bandwidthValueLabel = new Label
        {
            Text = $"{GetBandwidthMHz():F1} MHz",
            AutoSize = true,
            Location = new Point(280, 118),
        };

        var gainLabel = new Label { Text = "TX Gain:", AutoSize = true, Location = new Point(350, 118) };
        _gainUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 47,
            Value = Math.Clamp(_appSettings.GainDb, 0, 47),
            Location = new Point(410, 114),
            Size = new Size(60, 23),
        };

        _ampCheckBox = new CheckBox
        {
            Text = "Enable HackRF amp",
            Checked = _appSettings.AmpEnabled,
            AutoSize = true,
            Location = new Point(20, 158),
        };

        var callsignLabel = new Label { Text = "Callsign:", AutoSize = true, Location = new Point(20, 196) };
        _callsignTextBox = new TextBox
        {
            Text = _appSettings.Callsign ?? string.Empty,
            Location = new Point(100, 192),
            Size = new Size(160, 23),
            MaxLength = 10,
        };

        _testPatternCheckBox = new CheckBox
        {
            Text = "Use test pattern instead of webcam",
            Checked = _appSettings.UseTestPattern,
            AutoSize = true,
            Location = new Point(280, 194),
        };
        _testPatternCheckBox.CheckedChanged += (_, _) => UpdateDeviceControls();

        _startButton = new Button
        {
            Text = "Start",
            Location = new Point(20, 224),
            Size = new Size(100, 28),
        };
        _startButton.Click += StartButton_Click;

        _stopButton = new Button
        {
            Text = "Stop",
            Location = new Point(132, 224),
            Size = new Size(100, 28),
            Enabled = false,
        };
        _stopButton.Click += StopButton_Click;

        _statusLabel = new Label
        {
            Text = "Status: Idle",
            AutoSize = true,
            Location = new Point(260, 230),
        };

        _previewBox = new PictureBox
        {
            Location = new Point(10, 22),
            Size = new Size(390, 330),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _logTextBox = new TextBox
        {
            Location = new Point(16, 292),
            Size = new Size(540, 248),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
        };

        _previewTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _previewTimer.Tick += PreviewTimer_Tick;

        settingsGroup.Controls.Add(deviceLabel);
        settingsGroup.Controls.Add(_deviceComboBox);
        settingsGroup.Controls.Add(_refreshDevicesButton);
        settingsGroup.Controls.Add(presetLabel);
        settingsGroup.Controls.Add(_presetComboBox);
        settingsGroup.Controls.Add(frequencyLabel);
        settingsGroup.Controls.Add(_frequencyUpDown);
        settingsGroup.Controls.Add(mhzLabel);
        settingsGroup.Controls.Add(bandwidthLabel);
        settingsGroup.Controls.Add(_bandwidthTrackBar);
        settingsGroup.Controls.Add(_bandwidthValueLabel);
        settingsGroup.Controls.Add(gainLabel);
        settingsGroup.Controls.Add(_gainUpDown);
        settingsGroup.Controls.Add(_ampCheckBox);
        settingsGroup.Controls.Add(callsignLabel);
        settingsGroup.Controls.Add(_callsignTextBox);
        settingsGroup.Controls.Add(_testPatternCheckBox);
        settingsGroup.Controls.Add(_startButton);
        settingsGroup.Controls.Add(_stopButton);
        settingsGroup.Controls.Add(_statusLabel);

        previewGroup.Controls.Add(_previewBox);

        Controls.Add(settingsGroup);
        Controls.Add(previewGroup);
        Controls.Add(_logTextBox);

        ApplySavedPreset();
        UpdateBandwidthLabel();
        UpdateDeviceControls();

        Load += (_, _) => RefreshDevices();
        FormClosing += TransmitterForm_FormClosing;
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return AppSettings.Load();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Failed to load narrowbeam.ini: {ex.Message}", "Settings Load Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"Failed to load narrowbeam.ini: {ex.Message}", "Settings Load Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new AppSettings();
        }
    }

    private void SaveSettings()
    {
        _appSettings.FrequencyMhz = _frequencyUpDown.Value;
        _appSettings.BandwidthMhz = GetBandwidthMHz();
        _appSettings.GainDb = (int)_gainUpDown.Value;
        _appSettings.AmpEnabled = _ampCheckBox.Checked;
        _appSettings.Callsign = string.IsNullOrWhiteSpace(_callsignTextBox.Text) ? null : _callsignTextBox.Text.Trim();
        _appSettings.UseTestPattern = _testPatternCheckBox.Checked;
        _appSettings.LastDevice = _testPatternCheckBox.Checked ? null : _deviceComboBox.SelectedItem?.ToString();
        _appSettings.LastPreset = _presetComboBox.SelectedItem is AtvChannel channel && channel.FrequencyMhz > 0
            ? channel.Name
            : "Custom";

        _appSettings.Save();
    }

    private void ApplySavedPreset()
    {
        if (!string.IsNullOrWhiteSpace(_appSettings.LastPreset))
        {
            foreach (object item in _presetComboBox.Items)
            {
                if (item is AtvChannel channel && string.Equals(channel.Name, _appSettings.LastPreset, StringComparison.Ordinal))
                {
                    _presetComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        foreach (object item in _presetComboBox.Items)
        {
            if (item is AtvChannel channel && channel.FrequencyMhz == _frequencyUpDown.Value)
            {
                _presetComboBox.SelectedItem = item;
                return;
            }
        }

        _presetComboBox.SelectedIndex = 0;
    }

    private void PresetChanged(object? sender, EventArgs e)
    {
        if (_updatingPresetFromCode)
            return;

        if (_presetComboBox.SelectedItem is AtvChannel channel && channel.FrequencyMhz > 0)
        {
            _updatingPresetFromCode = true;
            try
            {
                _frequencyUpDown.Value = channel.FrequencyMhz;
            }
            finally
            {
                _updatingPresetFromCode = false;
            }
        }
    }

    private void FrequencyChanged(object? sender, EventArgs e)
    {
        if (_updatingPresetFromCode)
            return;

        if (_presetComboBox.SelectedItem is AtvChannel channel &&
            channel.FrequencyMhz > 0 &&
            channel.FrequencyMhz != _frequencyUpDown.Value)
        {
            _updatingPresetFromCode = true;
            try
            {
                _presetComboBox.SelectedIndex = 0;
            }
            finally
            {
                _updatingPresetFromCode = false;
            }
        }
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
                int selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(_appSettings.LastDevice))
                {
                    int foundIndex = _deviceComboBox.Items.IndexOf(_appSettings.LastDevice);
                    if (foundIndex >= 0)
                        selectedIndex = foundIndex;
                }

                _deviceComboBox.SelectedIndex = selectedIndex;
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
                Callsign = _callsignTextBox.Text.Trim(),
                UseTestPattern = _testPatternCheckBox.Checked,
                DeviceName = _testPatternCheckBox.Checked ? null : _deviceComboBox.SelectedItem?.ToString(),
            };

            _session = new TransmitterSession(AppendLog);
            _session.Start(settings);
            SaveSettings();
            _previewTimer.Start();

            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _statusLabel.Text = "Status: Transmitting";
        }
        catch (Exception ex)
        {
            AppendLog($"Start failed: {ex.Message}");
            _previewTimer.Stop();
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
            _previewTimer.Stop();
            _session?.Dispose();
            _session = null;
            SaveSettings();
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

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_session?.NtscSignal is null)
            return;

        Image? previousImage = _previewBox.Image;
        _previewBox.Image = _session.NtscSignal.GetPreviewBitmap();
        previousImage?.Dispose();
    }

    private void TransmitterForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _previewTimer.Stop();

        try
        {
            SaveSettings();
        }
        catch (IOException ex)
        {
            AppendLog($"Settings save failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"Settings save failed: {ex.Message}");
        }

        _previewBox.Image?.Dispose();

        if (_session is not null)
        {
            _session.Dispose();
            _session = null;
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

    private double GetBandwidthMHz() => _bandwidthTrackBar.Value / 2.0;

    private void UpdateBandwidthLabel()
    {
        _bandwidthValueLabel.Text = $"{GetBandwidthMHz():F1} MHz";
    }
}
