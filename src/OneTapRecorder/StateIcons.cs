using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using DrawingIcon = System.Drawing.Icon;

namespace OneTapRecorder;

public partial class MainWindow
{
    private DrawingIcon? recordingIcon;
    private DrawingIcon? savingIcon;
    private ImageSource? idleWindowIcon;
    private ImageSource? recordingWindowIcon;
    private ImageSource? savingWindowIcon;
    private CaptureState? displayedIconState;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private void InitializeStateIcons()
    {
        recordingIcon = CreateStateIcon(false);
        savingIcon = CreateStateIcon(true);
        idleWindowIcon = ToWindowIcon(trayImage!);
        recordingWindowIcon = ToWindowIcon(recordingIcon);
        savingWindowIcon = ToWindowIcon(savingIcon);
        TaskbarItemInfo = new TaskbarItemInfo();
    }

    private static ImageSource ToWindowIcon(DrawingIcon icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    private static DrawingIcon CreateStateIcon(bool saving)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var fill = new SolidBrush(saving ? System.Drawing.Color.FromArgb(230, 161, 35) : System.Drawing.Color.FromArgb(235, 55, 67));
            using var outline = new System.Drawing.Pen(System.Drawing.Color.White, 2);
            graphics.FillEllipse(fill, 2, 2, 28, 28);
            graphics.DrawEllipse(outline, 2, 2, 28, 28);
            if (saving)
                for (int x = 7; x <= 23; x += 8) graphics.FillEllipse(System.Drawing.Brushes.White, x - 2, 14, 4, 4);
            else
                graphics.FillRectangle(System.Drawing.Brushes.White, 10, 10, 12, 12);
        }
        var nativeIcon = bitmap.GetHicon();
        try
        {
            using var borrowedIcon = DrawingIcon.FromHandle(nativeIcon);
            return (DrawingIcon)borrowedIcon.Clone();
        }
        finally { DestroyIcon(nativeIcon); }
    }

    private void UpdateStateIcon()
    {
        if (trayIcon == null || recordingIcon == null || savingIcon == null) return;
        // Cache the images and only touch the shell on transitions; no animation/timer.
        var iconState = state is CaptureState.Recording or CaptureState.Stopping ? state : CaptureState.Idle;
        if (displayedIconState == iconState) return;
        displayedIconState = iconState;
        trayIcon.Icon = iconState switch
        {
            CaptureState.Recording => recordingIcon,
            CaptureState.Stopping => savingIcon,
            _ => trayImage
        };
        Icon = iconState switch
        {
            CaptureState.Recording => recordingWindowIcon,
            CaptureState.Stopping => savingWindowIcon,
            _ => idleWindowIcon
        };
        // An overlay also marks pinned/grouped taskbar buttons that keep their base icon.
        TaskbarItemInfo.Overlay = iconState == CaptureState.Idle ? null : Icon;
    }
}
