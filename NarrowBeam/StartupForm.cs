using System;
using System.Drawing;
using System.Windows.Forms;

namespace NarrowBeam;

internal sealed class StartupForm : Form
{
    public AppMode SelectedMode { get; private set; } = AppMode.None;

    public StartupForm()
    {
        Text = "NarrowBeam";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 180);

        var titleLabel = new Label
        {
            Text = "Choose what to open",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(110, 24),
        };

        var transmitterButton = new Button
        {
            Text = "Transmitter",
            Size = new Size(120, 36),
            Location = new Point(48, 88),
        };
        transmitterButton.Click += (_, _) =>
        {
            SelectedMode = AppMode.Transmitter;
            DialogResult = DialogResult.OK;
            Close();
        };

        var receiverButton = new Button
        {
            Text = "Receiver",
            Size = new Size(120, 36),
            Location = new Point(192, 88),
        };
        receiverButton.Click += (_, _) =>
        {
            SelectedMode = AppMode.Receiver;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(titleLabel);
        Controls.Add(transmitterButton);
        Controls.Add(receiverButton);
    }
}
