namespace TaskDashboard;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if WINDOWS
		// The UI test suites attach over CDP, enabled by the
		// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS environment variable. The
		// WebView2 loader honors that variable by itself on a normal desktop,
		// but silently drops it for elevated processes — which is how CI
		// runs — so forward it explicitly through the environment options.
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
