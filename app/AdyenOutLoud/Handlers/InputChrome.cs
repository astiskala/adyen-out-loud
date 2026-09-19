using Microsoft.Maui.Handlers;

namespace AdyenOutLoud.Handlers;

/// <summary>
/// Removes the native border, underline and fill from text fields and pickers.
/// </summary>
/// <remarks>
/// Every field on the page sits inside a themed <c>Border</c> (the <c>InputBorder</c> style), so the
/// platform's own chrome would draw a second outline inside it and ignore the light/dark palette.
/// </remarks>
internal static class InputChrome
{
    private const string Key = "AdyenOutLoud.Borderless";

    /// <summary>
    /// Applies the borderless mapping to every <see cref="Entry"/> and <see cref="Picker"/>.
    /// </summary>
    public static void Register()
    {
        EntryHandler.Mapper.AppendToMapping(Key, (handler, _) => RemoveChrome(handler.PlatformView));
        PickerHandler.Mapper.AppendToMapping(Key, (handler, _) => RemoveChrome(handler.PlatformView));
    }

    private static void RemoveChrome(object platformView)
    {
#if ANDROID
        if (platformView is Android.Views.View view)
        {
            view.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        }
#elif IOS || MACCATALYST
        if (platformView is UIKit.UITextField field)
        {
            field.BorderStyle = UIKit.UITextBorderStyle.None;
        }
        if (platformView is UIKit.UIView view)
        {
            view.BackgroundColor = UIKit.UIColor.Clear;
        }
#elif WINDOWS
        if (platformView is Microsoft.UI.Xaml.Controls.Control control)
        {
            control.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            control.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
#else
        _ = platformView;
#endif
    }
}
