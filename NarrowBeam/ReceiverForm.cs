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
    private readonly ComboBox _sampleRateComboBox;
    private readonly ComboBox _deviceTypeComboBox;
    private readonly NumericUpDown _lnaGainUpDown;
    private readonly NumericUpDown _vgaGainUpDown;
    private readonly Panel _rtlGainPanel;
    private readonly Panel _hackrfGainPanel;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;
    private readonly PictureBox _videoBox;
    private readonly System.Windows.Forms.Timer _videoTimer;

    // Tuning sliders
    private readonly TrackBar _sharpnessSlider;
    private readonly TrackBar _syncThreshSlider;
    private readonly TrackBar _hPosSlider;
    private readonly TrackBar _hSizeSlider;
    private readonly TrackBar _brightnessSlider;
    private readonly Label _sharpnessVal;
    private readonly Label _syncThreshVal;
    private readonly Label _hPosVal;
    private readonly Label _hSizeVal;
    private readonly Label _brightnessVal;

    private volatile bool _running;
    private Thread? _rxThread;
    private AmVideoDemodulator? _demodulator;
    private IntPtr _dev;        // RTL-SDR handle
    private IntPtr _hackrfDev;  // HackRF handle
    private RtlSdr.ReadAsyncCallback? _asyncCallback;
    private HackRf.TransferCallback? _hackrfCallback;

    public ReceiverForm()
    {
        Text = "NarrowBeam - Receiver";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 590);

        var settingsGroup = new GroupBox
        {
            Text = "Receiver Settings",
            Location = new Point(16, 16),
            Size = new Size(540, 185),
        };

        // ── Row 1: Device Type + Device ──────────────────────────────────────
        settingsGroup.Controls.Add(new Label { Text = "Device Type:", AutoSize = true, Location = new Point(20, 30) });
        _deviceTypeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 27),
            Size = new Size(100, 23),
        };
        _deviceTypeComboBox.Items.Add("RTL-SDR");
        _deviceTypeComboBox.Items.Add("HackRF");
        _deviceTypeComboBox.SelectedIndex = 0;
        _deviceTypeComboBox.SelectedIndexChanged += DeviceTypeChanged;
        settingsGroup.Controls.Add(_deviceTypeComboBox);

        var deviceLabel = new Label { Text = "Device:", AutoSize = true, Location = new Point(215, 30) };
        _deviceComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(265, 27),
            Size = new Size(185, 23),
        };
        _refreshDevicesButton = new Button { Text = "⟳", Location = new Point(458, 25), Size = new Size(70, 27) };
        _refreshDevicesButton.Click += (_, _) => RefreshDevices();
        settingsGroup.Controls.Add(deviceLabel);
        settingsGroup.Controls.Add(_deviceComboBox);
        settingsGroup.Controls.Add(_refreshDevicesButton);

        // ── Row 2: Preset + Frequency ────────────────────────────────────────
        var presetLabel = new Label { Text = "Preset:", AutoSize = true, Location = new Point(20, 65) };
        _presetComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 61),
            Size = new Size(160, 23),
        };
        _presetComboBox.Items.Add(new AtvChannel("Custom", 0));
        foreach (AtvChannel channel in AtvChannels.All)
            _presetComboBox.Items.Add(channel);
        _presetComboBox.DisplayMember = nameof(AtvChannel.Name);
        _presetComboBox.SelectedIndexChanged += PresetChanged;

        var frequencyLabel = new Label { Text = "Frequency:", AutoSize = true, Location = new Point(278, 65) };
        _frequencyUpDown = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 0.250M,
            Minimum = 1,
            Maximum = 6000,
            Value = 427.250M,
            Location = new Point(350, 61),
            Size = new Size(100, 23),
        };
        settingsGroup.Controls.Add(presetLabel);
        settingsGroup.Controls.Add(_presetComboBox);
        settingsGroup.Controls.Add(frequencyLabel);
        settingsGroup.Controls.Add(_frequencyUpDown);

        // ── Row 3: Sample Rate ───────────────────────────────────────────────
        settingsGroup.Controls.Add(new Label { Text = "Sample Rate:", AutoSize = true, Location = new Point(20, 103) });
        _sampleRateComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 99),
            Size = new Size(420, 23),
        };
        settingsGroup.Controls.Add(_sampleRateComboBox);
        PopulateSampleRates(isHackRf: false);

        // ── Row 4: Gain controls (two panels, one visible at a time) ─────────
        // RTL-SDR panel
        _rtlGainPanel = new Panel { Location = new Point(0, 130), Size = new Size(540, 30), Visible = true };
        var gainLabel = new Label { Text = "RX Gain:", AutoSize = true, Location = new Point(20, 7) };
        _gainUpDown = new NumericUpDown { Minimum = 0, Maximum = 50, Value = 30, Location = new Point(100, 3), Size = new Size(60, 23) };
        _rtlGainPanel.Controls.Add(gainLabel);
        _rtlGainPanel.Controls.Add(_gainUpDown);
        settingsGroup.Controls.Add(_rtlGainPanel);

        // HackRF panel
        _hackrfGainPanel = new Panel { Location = new Point(0, 130), Size = new Size(540, 30), Visible = false };
        _hackrfGainPanel.Controls.Add(new Label { Text = "LNA Gain:", AutoSize = true, Location = new Point(20, 7) });
        _lnaGainUpDown = new NumericUpDown { Minimum = 0, Maximum = 40, Value = 16, Location = new Point(100, 3), Size = new Size(55, 23) };
        _hackrfGainPanel.Controls.Add(_lnaGainUpDown);
        _hackrfGainPanel.Controls.Add(new Label { Text = "dB  VGA Gain:", AutoSize = true, Location = new Point(162, 7) });
        _vgaGainUpDown = new NumericUpDown { Minimum = 0, Maximum = 62, Value = 20, Location = new Point(270, 3), Size = new Size(55, 23) };
        _hackrfGainPanel.Controls.Add(_vgaGainUpDown);
        _hackrfGainPanel.Controls.Add(new Label { Text = "dB", AutoSize = true, Location = new Point(330, 7) });
        settingsGroup.Controls.Add(_hackrfGainPanel);

        // Start/Stop/Status
        _startButton = new Button { Text = "Start", Location = new Point(390, 153), Size = new Size(70, 25) };
        _startButton.Click += StartButton_Click;
        _stopButton = new Button { Text = "Stop", Location = new Point(465, 153), Size = new Size(70, 25), Enabled = false };
        _stopButton.Click += StopButton_Click;
        _statusLabel = new Label { Text = "Status: Idle", AutoSize = true, Location = new Point(20, 158) };
        settingsGroup.Controls.Add(_startButton);
        settingsGroup.Controls.Add(_stopButton);
        settingsGroup.Controls.Add(_statusLabel);

        var videoGroup = new GroupBox
        {
            Text = "Live Video",
            Location = new Point(570, 16),
            Size = new Size(410, 557),
        };

        _videoBox = new PictureBox
        {
            Location = new Point(10, 22),
            Size = new Size(390, 525),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        videoGroup.Controls.Add(_videoBox);

        Controls.Add(settingsGroup);
        Controls.Add(videoGroup);

        // ── Tuning sliders ────────────────────────────────────────────────────
        var tuningGroup = new GroupBox
        {
            Text = "Demodulator Tuning",
            Location = new Point(16, 212),
            Size = new Size(540, 370),
        };

        (_sharpnessSlider, _sharpnessVal) = AddSlider(tuningGroup, "Sharpness",  0,  0, 100, 85);
        (_syncThreshSlider, _syncThreshVal) = AddSlider(tuningGroup, "Sync Thresh", 1,  5,  40, 20);
        (_hPosSlider,      _hPosVal)      = AddSlider(tuningGroup, "H-Position", 2, 15,  40, 26);
        (_hSizeSlider,     _hSizeVal)     = AddSlider(tuningGroup, "H-Size",     3, 50,  90, 74);
        (_brightnessSlider, _brightnessVal) = AddSlider(tuningGroup, "Brightness", 4, 10,  50, 28);

        _sharpnessSlider.ValueChanged  += (_, _) => ApplyTuning();
        _syncThreshSlider.ValueChanged += (_, _) => ApplyTuning();
        _hPosSlider.ValueChanged       += (_, _) => ApplyTuning();
        _hSizeSlider.ValueChanged      += (_, _) => ApplyTuning();
        _brightnessSlider.ValueChanged += (_, _) => ApplyTuning();

        var resetButton = new Button
        {
            Text = "Reset Defaults",
            Location = new Point(200, 350),
            Size = new Size(130, 27),
        };
        resetButton.Click += (_, _) =>
        {
            _sharpnessSlider.Value  = 85;
            _syncThreshSlider.Value = 20;
            _hPosSlider.Value       = 26;
            _hSizeSlider.Value      = 74;
            _brightnessSlider.Value = 28;
        };
        tuningGroup.Controls.Add(resetButton);

        Controls.Add(tuningGroup);

        _videoTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _videoTimer.Tick += VideoTimer_Tick;

        _presetComboBox.SelectedIndex = 0;
        RefreshDevices();
    }

    private static (TrackBar slider, Label valueLabel) AddSlider(
        GroupBox parent, string name, int row, int min, int max, int def)
    {
        int y = 28 + row * 65;

        parent.Controls.Add(new Label
        {
            Text = name,
            AutoSize = true,
            Location = new Point(12, y + 4),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
        });

        var slider = new TrackBar
        {
            Minimum   = min,
            Maximum   = max,
            Value     = def,
            TickStyle = TickStyle.None,
            Location  = new Point(110, y),
            Size      = new Size(340, 35),
        };

        var valLabel = new Label
        {
            Text      = def.ToString(),
            AutoSize  = true,
            Location  = new Point(460, y + 4),
            Width     = 50,
        };

        slider.ValueChanged += (_, _) => valLabel.Text = slider.Value.ToString();

        parent.Controls.Add(slider);
        parent.Controls.Add(valLabel);
        return (slider, valLabel);
    }

    private void ApplyTuning()
    {
        if (_demodulator == null) return;
        _demodulator.LpfAlpha        = _sharpnessSlider.Value  / 100.0;
        _demodulator.SyncThreshold   = _syncThreshSlider.Value  / 100.0;
        _demodulator.ActiveStart     = _hPosSlider.Value        / 100.0;
        _demodulator.ActiveWidth     = _hSizeSlider.Value       / 100.0;
        _demodulator.BlackLevelOffset = _brightnessSlider.Value / 100.0;
    }

    private void DeviceTypeChanged(object? sender, EventArgs e)
    {
        bool isHackRf = _deviceTypeComboBox.SelectedIndex == 1;
        _rtlGainPanel.Visible    = !isHackRf;
        _hackrfGainPanel.Visible =  isHackRf;
        PopulateSampleRates(isHackRf);
        RefreshDevices();
    }

    private void PopulateSampleRates(bool isHackRf)
    {
        _sampleRateComboBox.Items.Clear();
        if (isHackRf)
        {
            _sampleRateComboBox.Items.Add("4 MSPS");
            _sampleRateComboBox.Items.Add("6 MSPS");
            _sampleRateComboBox.Items.Add("8 MSPS");
            _sampleRateComboBox.Items.Add("10 MSPS");
            _sampleRateComboBox.Items.Add("12.5 MSPS");
            _sampleRateComboBox.Items.Add("16 MSPS");
            _sampleRateComboBox.Items.Add("20 MSPS");
            _sampleRateComboBox.SelectedIndex = 2; // default 8 MSPS
        }
        else
        {
            _sampleRateComboBox.Items.Add("2.0 MSPS");
            _sampleRateComboBox.Items.Add("2.4 MSPS");
            _sampleRateComboBox.Items.Add("2.56 MSPS");
            _sampleRateComboBox.Items.Add("3.2 MSPS");
            _sampleRateComboBox.SelectedIndex = 3; // default 3.2 MSPS
        }
    }

    private uint GetSelectedSampleRate()
    {
        bool isHackRf = _deviceTypeComboBox.SelectedIndex == 1;
        if (isHackRf)
        {
            return _sampleRateComboBox.SelectedIndex switch
            {
                0 =>  4_000_000,
                1 =>  6_000_000,
                3 => 10_000_000,
                4 => 12_500_000,
                5 => 16_000_000,
                6 => 20_000_000,
                _ =>  8_000_000,
            };
        }
        return _sampleRateComboBox.SelectedIndex switch
        {
            0 => 2_000_000,
            2 => 2_560_000,
            3 => 3_200_000,
            _ => 2_400_000,
        };
    }

    private void RefreshDevices()
    {
        _deviceComboBox.Items.Clear();
        bool isHackRf = _deviceTypeComboBox.SelectedIndex == 1;

        if (isHackRf)
        {
            // Don't call hackrf_init() or hackrf_device_list() here — USB enumeration
            // can block the UI thread, especially when the TX device is already open.
            // Show two static entries; the user selects whichever HackRF is the RX unit.
            _deviceComboBox.Items.Add("HackRF #0");
            _deviceComboBox.Items.Add("HackRF #1");
        }
        else
        {
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
                MessageBox.Show($"Error listing RTL-SDR devices: {ex.Message}");
            }
        }

        if (_deviceComboBox.Items.Count > 0)
            _deviceComboBox.SelectedIndex = 0;
    }

    private void PresetChanged(object? sender, EventArgs e)
    {
        if (_presetComboBox.SelectedItem is AtvChannel channel && channel.FrequencyMhz > 0)
            _frequencyUpDown.Value = channel.FrequencyMhz;
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_deviceComboBox.SelectedIndex < 0) return;
        if (_running) return;

        bool isHackRf = _deviceTypeComboBox.SelectedIndex == 1;
        try
        {
            if (isHackRf)
                StartHackRf();
            else
                StartRtlSdr();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Start failed: {ex.Message}");
            StopRx();
        }
    }

    private void StartRtlSdr()
    {
        uint index = (uint)_deviceComboBox.SelectedIndex;
        int openRes = RtlSdr.rtlsdr_open(out _dev, index);
        if (openRes != 0) throw new InvalidOperationException($"Failed to open RTL-SDR: {openRes}");

        uint sampleRate = GetSelectedSampleRate();
        uint targetFreq = (uint)(_frequencyUpDown.Value * 1_000_000);
        uint tuneFreq   = targetFreq + 1_000_000; // offset to avoid DC spike

        int r;
        r = RtlSdr.rtlsdr_set_center_freq(_dev, tuneFreq);
        if (r < 0) throw new Exception("Failed to set frequency");
        r = RtlSdr.rtlsdr_set_sample_rate(_dev, sampleRate);
        if (r < 0) throw new Exception("Failed to set sample rate");
        r = RtlSdr.rtlsdr_set_tuner_gain_mode(_dev, 1);
        if (r < 0) throw new Exception("Failed to set manual gain mode");
        r = RtlSdr.rtlsdr_set_tuner_gain(_dev, (int)(_gainUpDown.Value * 10));
        r = RtlSdr.rtlsdr_reset_buffer(_dev);
        if (r < 0) throw new Exception("Failed to reset buffer");

        _demodulator = new AmVideoDemodulator(sampleRate);
        ApplyTuning();
        _running = true;

        _rxThread = new Thread(RtlSdrLoop) { IsBackground = true };
        _rxThread.Start();
        _videoTimer.Start();

        _startButton.Enabled = false;
        _stopButton.Enabled  = true;
        _statusLabel.Text    = $"RX (RTL-SDR): {targetFreq / 1e6:F3} MHz";
    }

    private void StartHackRf()
    {
        HackRf.Check(HackRf.hackrf_init(), "hackrf_init");

        // Open the specific device selected in the combo box
        int devIdx = _deviceComboBox.SelectedIndex;
        IntPtr listPtr = HackRf.hackrf_device_list();
        if (listPtr == IntPtr.Zero) throw new Exception("No HackRF devices found");
        int openResult = HackRf.hackrf_device_list_open(listPtr, devIdx, out _hackrfDev);
        HackRf.hackrf_device_list_free(listPtr);
        HackRf.Check(openResult, $"hackrf_device_list_open (index {devIdx})");

        uint sampleRate = GetSelectedSampleRate();
        ulong targetFreq = (ulong)(_frequencyUpDown.Value * 1_000_000);

        HackRf.Check(HackRf.hackrf_set_freq(_hackrfDev, targetFreq),                     "set_freq");
        HackRf.Check(HackRf.hackrf_set_sample_rate(_hackrfDev, (double)sampleRate),      "set_sample_rate");
        HackRf.Check(HackRf.hackrf_set_lna_gain(_hackrfDev, (uint)_lnaGainUpDown.Value), "set_lna_gain");
        HackRf.Check(HackRf.hackrf_set_vga_gain(_hackrfDev, (uint)_vgaGainUpDown.Value), "set_vga_gain");
        HackRf.Check(HackRf.hackrf_set_amp_enable(_hackrfDev, 0),                        "set_amp_enable");

        _demodulator = new AmVideoDemodulator(sampleRate);
        ApplyTuning();
        _running = true;

        _hackrfCallback = HackRfRxCallback;
        HackRf.Check(HackRf.hackrf_start_rx(_hackrfDev, _hackrfCallback, IntPtr.Zero), "start_rx");

        _videoTimer.Start();
        _startButton.Enabled = false;
        _stopButton.Enabled  = true;
        _statusLabel.Text    = $"RX (HackRF #{devIdx}): {targetFreq / 1e6:F3} MHz";
    }

    private int HackRfRxCallback(ref HackRf.Transfer transfer)
    {
        if (!_running) return -1;
        unsafe
        {
            if (_demodulator != null)
                _demodulator.ProcessIqSigned((sbyte*)transfer.Buffer, transfer.ValidLength);
        }
        return 0;
    }

    private void RtlSdrLoop()
    {
        _asyncCallback = new RtlSdr.ReadAsyncCallback(RtlSdrCallback);
        int result = RtlSdr.rtlsdr_read_async(_dev, _asyncCallback, IntPtr.Zero, 0, 0);
        if (result != 0 && _running)
        {
            this.Invoke((MethodInvoker)delegate {
                MessageBox.Show($"Async read failed: {result}");
                StopRx();
            });
        }
    }

    private void RtlSdrCallback(IntPtr buf, uint len, IntPtr ctx)
    {
        if (!_running) { RtlSdr.rtlsdr_cancel_async(_dev); return; }
        unsafe
        {
            if (_demodulator != null)
                _demodulator.ProcessIq((byte*)buf, (int)len);
        }
    }

    private void StopButton_Click(object? sender, EventArgs e) => StopRx();

    private void StopRx()
    {
        if (!_running) return;
        _running = false;

        // Stop RTL-SDR
        if (_dev != IntPtr.Zero)
        {
            RtlSdr.rtlsdr_cancel_async(_dev);
            _rxThread?.Join(500);
            RtlSdr.rtlsdr_close(_dev);
            _dev = IntPtr.Zero;
        }

        // Stop HackRF
        if (_hackrfDev != IntPtr.Zero)
        {
            HackRf.hackrf_stop_rx(_hackrfDev);
            HackRf.hackrf_close(_hackrfDev);
            HackRf.hackrf_exit();
            _hackrfDev = IntPtr.Zero;
        }

        _videoTimer.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled  = false;
        _statusLabel.Text    = "Status: Idle";
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
