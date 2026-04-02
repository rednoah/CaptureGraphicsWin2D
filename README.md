# CaptureGraphicsWin2D
`CaptureGraphicsWin2D` is a simple screenshot tool for Windows 11 that uses the `Microsoft.Graphics.Win2D` API to capture windows with alpha channel and save them as PNG.


This allows you to correctly **capture windows with transparency**:
* windows with rounded borders
* windows with shadows (e.g. context menu)
* windows with transparent surfaces (e.g. taskbar)

<img src="Assets/CaptureGraphicsWin2D.png" width="430">

Video:
* [Screenshot Tool for Windows 11 - CaptureGraphicsWin2D - YouTube](https://www.youtube.com/watch?v=aWMvMo0-vAs)

Links:
* [Product Page](https://rednoah.github.io/CaptureGraphicsWin2D/)
* [Microsoft Store](https://apps.microsoft.com/detail/9nf3pwrd277v)




# Command-Line Usage
e.g. capture all windows
```bash
CaptureGraphicsWin2D "C:\Captures"
```
e.g. capture a given window
```bash
CaptureGraphicsWin2D "C:\Captures" "Shell_TrayWnd"
```




# Motivation
I was not able to find a screenshot tool that was able to capture the Windows 11 Files context menu / Windows 11 Taskbar / etc with alpha channel for transparency.

<img src="Assets/ContextMenu-1.png" width="287"><img src="Assets/ContextMenu-2.png" width="300">
<img src="Assets/ShellTray.png" width="1920">

<img src="Assets/ContextMenu-1.Dark.png" width="287"><img src="Assets/ContextMenu-2.Dark.png" width="300">
<img src="Assets/ShellTray.Dark.png" width="1920">




# Privacy Policy
`CaptureGraphicsWin2D` is a simple screenshot tool. It does not collect personal information. It does not even connect to the internet in the first place. The Microsoft Store requires a privacy policy for all applications that run with `<rescap:Capability Name="runFullTrust" />`.
