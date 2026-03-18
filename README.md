# NarrowBeam

`NarrowBeam` is a Windows desktop application for amateur television work with HackRF. It currently includes a transmitter mode for generating NTSC video and a receiver mode in the application shell.

## Features

- DirectShow webcam enumeration through `ffmpeg`
- NTSC composite generation for HackRF transmit
- Test pattern transmit mode
- Callsign overlay in the lower-left corner of transmitted video
- Amateur TV preset channels for US 70cm, 33cm, and 23cm allocations
- Live transmitter monitor preview
- Persistent transmitter settings stored in `narrowbeam.ini`

## Requirements

- Windows
- .NET 8 SDK or .NET 8 Desktop Runtime
- HackRF with the required native DLLs
- `ffmpeg.exe` available beside the built executable, or in the repository root during development

The repository already includes the native files expected by the project:

- `ffmpeg.exe`
- `hackrf.dll`
- `libusb-1.0.dll`
- `pthreadVC2.dll`

## Build

From the repository root:

```powershell
dotnet build .\NarrowBeam\NarrowBeam.csproj -c Release
```

Release output:

```text
NarrowBeam\bin\Release\net8.0-windows\
```

## Run

After building, launch:

```powershell
.\NarrowBeam\bin\Release\net8.0-windows\NarrowBeam.exe
```

On startup, choose either transmitter or receiver mode.

## Transmitter usage

1. Select a webcam, or enable the test pattern option.
2. Pick an ATV preset or enter a custom frequency.
3. Set transmit bandwidth, gain, amp state, and optional callsign.
4. Press `Start`.
5. Watch the `Live Monitor` pane to confirm what is being transmitted.

Saved transmitter values are restored from `narrowbeam.ini` in the application directory.

## Notes

- Webcam capture uses `ffmpeg` with DirectShow.
- The transmitted preview reflects the frame being encoded for NTSC output.
- If no camera appears, verify the device is visible to:

```powershell
ffmpeg.exe -hide_banner -list_devices true -f dshow -i dummy
```
