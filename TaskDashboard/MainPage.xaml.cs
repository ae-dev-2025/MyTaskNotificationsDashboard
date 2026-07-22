namespace TaskDashboard;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if WINDOWS
		// The UI test suites attach over CDP, enabled by the
		// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS environment variable. The
		// WebView2 loader honors that variable by itself on a normal desktop
		// but documents dropping it for elevated processes, so forward it
		// explicitly through the environment options too — the supported API
		// path. Note this was NOT sufficient to open the debug port on an
		// elevated GitHub runner; the port stayed closed there regardless
		// (see docs/README.md), which is why demo capture is local-only.
		// Harmless when the variable is unset.
		blazorWebView.BlazorWebViewInitializing += (_, e) =>
		{
			var extraArgs = Environment.GetEnvironmentVariable(
				"WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
			if (!string.IsNullOrWhiteSpace(extraArgs))
			{
				e.EnvironmentOptions ??=
					new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
				e.EnvironmentOptions.AdditionalBrowserArguments = extraArgs;
			}
		};
#endif
	}
}
