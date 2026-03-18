using System;
using System.Windows.Forms;

namespace NarrowBeam;

internal enum AppMode
{
    None,
    Transmitter,
    Receiver,
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var startupForm = new StartupForm();
        if (startupForm.ShowDialog() != DialogResult.OK || startupForm.SelectedMode == AppMode.None)
            return;

        Form mainForm = startupForm.SelectedMode switch
        {
            AppMode.Transmitter => new TransmitterForm(),
            AppMode.Receiver => new ReceiverForm(),
            _ => throw new InvalidOperationException("No application mode selected."),
        };

        Application.Run(mainForm);
    }
}
