using Windows.Storage.Streams;
using WinRT;

namespace DesktopLyric;

/// <summary>
/// CsWinRT SMTC wrappers finalize on a non-STA thread and AV
/// (WinRT.IObjectReference.Finalize → c0000005). Closing a session /
/// PlaybackInfo / Timeline NativeObject on the UI thread also drops the
/// live SMTC COM ref, so the clock then reads 0 or throws and lyrics
/// stop following. Only IRandomAccessStream is IClosable and must be
/// closed. Other wrappers: suppress the finalizer and leave the ref.
/// </summary>
internal static class WinRtLifetime
{
    public static void Release(object? obj)
    {
        if (obj == null) return;
        try
        {
            if (obj is IRandomAccessStream stream)
            {
                stream.Dispose();
                return;
            }
            if (obj is IWinRTObject { HasUnwrappableNativeObject: true } w)
                GC.SuppressFinalize(w.NativeObject);
        }
        catch { }
    }
}
