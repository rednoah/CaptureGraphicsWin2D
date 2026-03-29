# CaptureGraphicsWin2D
`CaptureGraphicsWin2D` is a simple screenshot tool for Windows 11 that uses the `Microsoft.Graphics.Win2D` API to capture windows with alpha channel. This allows you to correctly capture shaped windows and transparent windows, e.g. windows with rounded borders, windows with shadows, etc.

<img src="Assets/CaptureGraphicsWin2D.png" width="430">



# Motivation
I was not able to find a screenshot tool that was capable of taking a screenshot of the Windows 11 Files context menu and correctly capturing the alpha channel.

<img src="Assets/ContextMenu-1.png" width="287"><img src="Assets/ContextMenu-2.png" width="300">
