# CaptureGraphicsWin2D
`CaptureGraphicsWin2D` is a simple screenshot tool for Windows 11 that uses the `Microsoft.Graphics.Win2D` API to capture windows with alpha channel and save them as PNG.

This allows you to correctly **capture windows with transparency**:
* windows with rounded borders
* windows with shadows (e.g. context menu)
* windows with transparent surfaces (e.g. taskbar)

<img src="Assets/CaptureGraphicsWin2D.png" width="430">



# Motivation
I was not able to find a screenshot tool that was able to capture the Windows 11 Files context menu / Windows 11 Taskbar / etc with alpha channel for transparency.

<img src="Assets/ContextMenu-1.png" width="287"><img src="Assets/ContextMenu-2.png" width="300">

<img src="Assets/ShellTray.png" width="1920">
