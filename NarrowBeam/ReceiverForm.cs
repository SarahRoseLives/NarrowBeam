using System.Drawing;
using System.Windows.Forms;

namespace NarrowBeam;

internal sealed class ReceiverForm : Form
{
    public ReceiverForm()
    {
        Text = "NarrowBeam - Receiver";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 220);

        Controls.Add(new Label
        {
            Text = "Receiver UI coming next. Transmitter is the active path for now.",
            AutoSize = true,
            Location = new Point(36, 48),
        });
    }
}
