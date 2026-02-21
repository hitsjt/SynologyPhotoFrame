using System.Diagnostics;
using System.IO;

namespace SynologyPhotoFrame.Helpers;

public static class OnScreenKeyboardHelper
{
    private const string TabTipPath = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";

    public static void Show()
    {
        try
        {
            if (File.Exists(TabTipPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = TabTipPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    public static void Hide()
    {
        // The touch keyboard auto-hides when focus leaves input fields
    }
}
