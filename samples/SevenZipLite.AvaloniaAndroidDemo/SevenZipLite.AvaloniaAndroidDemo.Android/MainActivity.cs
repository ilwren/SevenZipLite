using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace SevenZipLite.AvaloniaAndroidDemo.Android;

[Activity(
    Label = "SevenZipLite.AvaloniaAndroidDemo.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
