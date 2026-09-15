namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Supplies the non-secret <see cref="EnvironmentFacts"/> a support package records (F30). Implemented Web-side
/// (it reads the running build, the configured database provider, and the host OS/runtime); abstracted here so the
/// <see cref="ISupportPackageService"/> stays testable with a fake and the pipeline core keeps no host dependency.
/// </summary>
public interface IEnvironmentFactsProvider
{
    /// <summary>The current environment facts. Cheap and side-effect-free.</summary>
    EnvironmentFacts Capture();
}
