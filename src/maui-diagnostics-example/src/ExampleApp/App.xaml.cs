using Microsoft.Extensions.DependencyInjection;
using ExampleApp.Diagnostics;

namespace ExampleApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Turn on the app's diagnostics (custom counters + in-process listener) at startup.
		DiagnosticsBootstrapper.Start();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}