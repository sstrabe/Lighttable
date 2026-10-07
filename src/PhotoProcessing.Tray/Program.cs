using Microsoft.Extensions.Configuration;
using PhotoProcessing.Core;
using PhotoProcessing.Tray;

// One icon per user session.
using var single = new Mutex(initiallyOwned: true, @"Local\PhotoProcessing.Tray", out var isFirst);
if (!isFirst) return;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("PHOTOPROC_")
    .Build();
var settings = configuration.GetSection("PhotoProcessing").Get<PhotoProcessingSettings>() ?? new PhotoProcessingSettings();
var folderUrl = configuration["PhotoProcessing:Tray:FolderUrl"] ?? settings.Nextcloud.BaseUrl;

ApplicationConfiguration.Initialize();
Application.Run(new TrayContext(settings, folderUrl));
