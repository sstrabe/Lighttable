using Microsoft.Extensions.Configuration;
using PhotoProcessing.Core;

namespace PhotoProcessing.Cli;

/// <summary>appsettings.json next to the exe, then user secrets, then PHOTOPROC_ env vars.</summary>
internal static class AppConfig
{
    public static IConfigurationRoot Build() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets(typeof(AppConfig).Assembly, optional: true)
        .AddEnvironmentVariables("PHOTOPROC_")
        .Build();

    public static PhotoProcessingSettings Bind(IConfiguration configuration) =>
        configuration.GetSection("PhotoProcessing").Get<PhotoProcessingSettings>() ?? new PhotoProcessingSettings();
}
