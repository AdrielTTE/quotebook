namespace Quotebook.Web.Infrastructure;

/// <summary>Switches read from configuration (environment variables on the host).</summary>
public sealed class AppFeatures(IConfiguration config)
{
    /// <summary>Demo__Enabled=true: seeds a demo business and shows a one-click "Try the demo" button.</summary>
    public bool DemoEnabled => config.GetValue("Demo:Enabled", false);

    /// <summary>Registration__Enabled=false closes open sign-up, so strangers cannot store data on a public demo.</summary>
    public bool RegistrationOpen => config.GetValue("Registration:Enabled", true);
}
