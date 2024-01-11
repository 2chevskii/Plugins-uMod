using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Versioning;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Octokit;
using Serilog;
using FileMode = System.IO.FileMode;

class Build : NukeBuild
{
    const string DEPOT_DOWNLOADER_REPO_OWNER = "SteamRE";
    const string DEPOT_DOWNLOADER_REPO_NAME = "DepotDownloader";
    const string FILELIST_CONTENT = @"regex:RustDedicated_Data\/Managed\/.+\.dll";
    const string OXIDE_REPO_OWNER = "OxideMod";
    const string OXIDE_REPO_NAME = "Oxide.Rust";

    IGitHubClient GithubClient;

    bool IsDepotDownloaderInstalled;
    NuGetVersion DepotDownloaderLatestVersion,
        DepotDownloaderCurrentVersion;

    Release DepotDownloaderLatestRelease;

    AbsolutePath ReferencesDir => RootDirectory / "include/references";
    AbsolutePath ToolsDir => RootDirectory / "tools";
    AbsolutePath DownloadsDir => ToolsDir / "downloads";
    AbsolutePath DepotDownloaderDir => ToolsDir / "depot_downloader";
    AbsolutePath DepotDownloaderDllPath => DepotDownloaderDir / "DepotDownloader.dll";
    AbsolutePath FilelistPath => ToolsDir / "rust_ds/filelist.txt";

    Target GetDepotDownloaderLatestVersion =>
        _ =>
            _.Executes(async () =>
            {
                DepotDownloaderLatestRelease = await GetDepotDownloaderLatestRelease();
                DepotDownloaderLatestVersion = GetVersionFromDepotDownloaderTag(
                    DepotDownloaderLatestRelease.TagName
                );
                Log.Information(
                    "DepotDownloader latest version is {Version}",
                    DepotDownloaderLatestVersion
                );
            });

    Target GetDepotDownloaderCurrentVersion =>
        _ =>
            _.Executes(() =>
            {
                IsDepotDownloaderInstalled = DepotDownloaderDllPath.FileExists();
                if (!IsDepotDownloaderInstalled)
                {
                    Log.Information("DepotDownloader is not installed");
                    return;
                }

                FileVersionInfo depotDownloaderFileVersionInfo = FileVersionInfo.GetVersionInfo(
                    DepotDownloaderDllPath
                );

                DepotDownloaderCurrentVersion = NuGetVersion.Parse(
                    depotDownloaderFileVersionInfo.FileVersion
                );

                Log.Information(
                    "DepotDownloader v{Version} is installed at {DepotDownloaderPath}",
                    DepotDownloaderCurrentVersion,
                    DepotDownloaderDllPath
                );
            });

    Target UpdateDepotDownloader =>
        _ =>
            _.DependsOn(GetDepotDownloaderLatestVersion, GetDepotDownloaderCurrentVersion)
                .OnlyWhenDynamic(
                    () =>
                        !IsDepotDownloaderInstalled
                        || DepotDownloaderCurrentVersion < DepotDownloaderLatestVersion
                )
                .Executes(async () =>
                {
                    var zipAsset = DepotDownloaderLatestRelease.Assets.FirstOrDefault(
                        asset => asset.Name.EndsWith(".zip")
                    );

                    if (zipAsset == null)
                    {
                        throw new Exception(
                            $"Could not find .zip asset in DepotDownloader release v{DepotDownloaderLatestRelease}"
                        );
                    }

                    Log.Information(
                        "Found DepotDownloader portable distribution: {Asset}",
                        zipAsset.Name
                    );

                    DepotDownloaderDir.CreateOrCleanDirectory();
                    DownloadsDir.CreateDirectory();

                    Log.Information("Downloading asset...");
                    Uri downloadUri = new Uri(zipAsset.BrowserDownloadUrl);
                    using var httpClient = new HttpClient();
                    var responseStream = await httpClient.GetStreamAsync(downloadUri);

                    Log.Information(
                        "Extracting archive to {DepotDownloaderDir}",
                        DepotDownloaderDir
                    );
                    using ZipArchive archive = new ZipArchive(
                        responseStream,
                        ZipArchiveMode.Read,
                        false
                    );
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        var entryOutputPath = DepotDownloaderDir / entry.FullName;
                        Log.Verbose(
                            "Extracting {EntryName} to {OutputPath}",
                            entry.FullName,
                            entryOutputPath
                        );
                        await using var entryOutputFs = new FileStream(
                            entryOutputPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None
                        );
                        await using var entryFs = entry.Open();
                        await entryFs.CopyToAsync(entryOutputFs);
                    }

                    Log.Information("DepotDownloader installed");
                });

    Target UpdateFilelist =>
        _ =>
            _.Executes(() =>
            {
                if (
                    !FilelistPath.FileExists()
                    || !FilelistPath.ReadAllText().Equals(FILELIST_CONTENT)
                )
                {
                    Log.Information("Filelist content is not up-to-date");
                    FilelistPath.WriteAllText(FILELIST_CONTENT);
                }
                else
                {
                    Log.Information("Filelist is up-to-date");
                }
            });

    Target UpdateRustLibs =>
        _ =>
            _.DependsOn(UpdateDepotDownloader, UpdateFilelist)
                .Executes(() =>
                {
                    AbsolutePath rustDsDownloadsDir = DownloadsDir / "rust_ds";

                    DotNetTasks.DotNet(
                        $"{DepotDownloaderDllPath} -app 258550 -dir {rustDsDownloadsDir} -filelist {FilelistPath}",
                        workingDirectory: DepotDownloaderDir,
                        logOutput: true,
                        logInvocation: true
                    );

                    ReferencesDir.CreateOrCleanDirectory();

                    (rustDsDownloadsDir / "RustDedicated_Data/Managed")
                        .GlobFiles("*.dll")
                        .ForEach(file =>
                        {
                            var destinationPath = ReferencesDir / file.Name;

                            Log.Information(
                                "Copying file {Filename} to destination path: {Destination}",
                                file.Name,
                                destinationPath
                            );
                            FileSystemTasks.CopyFile(
                                file,
                                destinationPath,
                                FileExistsPolicy.Overwrite
                            );
                        });
                });

    Target UpdateOxideLibs =>
        _ =>
            _.After(UpdateRustLibs)
                .Executes(async () =>
                {
                    var oxideLatestRelease = await GithubClient.Repository.Release.GetLatest(
                        OXIDE_REPO_OWNER,
                        OXIDE_REPO_NAME
                    );

                    var version = NuGetVersion.Parse(oxideLatestRelease.TagName);

                    Log.Information("Oxide.Rust latest version is {Version}", version);

                    Func<ReleaseAsset, bool> assetSelector;
                    if (EnvironmentInfo.Platform == PlatformFamily.Linux)
                    {
                        assetSelector = x => x.Name is "Oxide.Rust-linux.zip";
                    }
                    else if (EnvironmentInfo.Platform == PlatformFamily.Windows)
                    {
                        assetSelector = x => x.Name is "Oxide.Rust.zip";
                    }
                    else
                    {
                        throw new Exception(
                            "Unknown platform: " + EnvironmentInfo.Platform.ToString("G")
                        );
                    }

                    var asset = oxideLatestRelease.Assets.FirstOrDefault(assetSelector);

                    if (asset == null)
                    {
                        throw new Exception("Required asset was not found in the release");
                    }

                    var downloadUri = new Uri(asset.BrowserDownloadUrl);
                    using var httpClient = new HttpClient();
                    var responseStream = await httpClient.GetStreamAsync(downloadUri);

                    using ZipArchive archive = new ZipArchive(responseStream);
                    foreach (
                        ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.EndsWith(".dll"))
                    )
                    {
                        var destinationPath = ReferencesDir / entry.Name;
                        await using var entryFs = entry.Open();
                        await using var destFs = destinationPath
                            .ToFileInfo()
                            .Open(FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                        destFs.SetLength(0);
                        await entryFs.CopyToAsync(destFs);
                    }
                });

    Target SetupWorkspace => _ => _.DependsOn(UpdateRustLibs, UpdateOxideLibs);

    public static int Main() => Execute<Build>(x => x.SetupWorkspace);

    protected override void OnBuildInitialized()
    {
        GithubClient = new GitHubClient(new ProductHeaderValue("dvchevskii.Plugins.uMod"));
    }

    static NuGetVersion GetVersionFromDepotDownloaderTag(string tagName)
    {
        int underscoreIndex = tagName.IndexOf('_');
        if (underscoreIndex == -1)
        {
            throw new FormatException(
                $"Tag name '{tagName}' cannot be parsed into version: No underscore character found"
            );
        }

        string versionSubstring = tagName.Substring(underscoreIndex + 1);
        NuGetVersion version = NuGetVersion.Parse(versionSubstring);

        return version;
    }

    Task<Release> GetDepotDownloaderLatestRelease() =>
        GithubClient.Repository.Release.GetLatest(
            DEPOT_DOWNLOADER_REPO_OWNER,
            DEPOT_DOWNLOADER_REPO_NAME
        );
}
