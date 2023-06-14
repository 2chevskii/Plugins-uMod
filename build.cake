#addin nuget:?package=Cake.Git&version=3.0.0
#addin nuget:?package=Cake.FileHelpers&version=6.1.3

var rootDir = Context.Environment.WorkingDirectory;
var toolsDir = rootDir.Combine("tools");
var downloadsDir = toolsDir.Combine("downloads");
var depotDownloaderDir = toolsDir.Combine("DepotDownloader");
var depotdownloaderDllPath = depotDownloaderDir.CombineWithFilePath("DepotDownloader.dll");

var referencesDir = rootDir.Combine("include/references");

var target = Argument("target", Argument("t", "UpdateReferences"));
var ddReinstall = HasArgument("dd-reinstall");

bool depotdownloaderInstalled = FileExists(depotdownloaderDllPath);

bool shouldInstallDepotDownloader = ddReinstall || !depotdownloaderInstalled;

Setup(ctx => {
    /* EnsureDirectoryExists(tempDir);
    EnsureDirectoryExists(tempDownloadsDir);
    EnsureDirectoryExists(depotdownloaderDir);
    EnsureDirectoryExists(referencesDir); */

    EnsureDirectoryExists(referencesDir);
});

Task("Install:DepotDownloader")
.Does(() => {
    Information("Installing latest DepotDownloader...");

    if(!shouldInstallDepotDownloader) {
        Information("DepotDownloader is already installed");
        return;
    }

    const string CLONE_URL = "https://github.com/SteamRE/DepotDownloader.git";

    var ddRepoPath = downloadsDir.Combine("DepotDownloader");

    EnsureDirectoryDoesNotExist(ddRepoPath);
    // CleanDirectory(ddRepoPath);

    Verbose("Cloning DepotDownloader repository ({0}) to {1}", CLONE_URL, ddRepoPath);

    GitClone(CLONE_URL, ddRepoPath);

    Verbose("Building DepotDownloader with Release configuration...");

    DotNetBuild(ddRepoPath.CombineWithFilePath("DepotDownloader.sln").ToString(), new DotNetBuildSettings {
        Configuration = "Release",
        WorkingDirectory = ddRepoPath,
        OutputDirectory = ddRepoPath.Combine("out")
    });

    Verbose("Creating and cleaning DepotDownloader installation directory: {0}", depotDownloaderDir);

    EnsureDirectoryExists(depotDownloaderDir);
    CleanDirectory(depotDownloaderDir);

    Verbose("Copying build output");

    CopyFiles(ddRepoPath.Combine("out/*").ToString(), depotDownloaderDir);

    Verbose("Creating filelist for app 258550");

    FileWriteText(depotDownloaderDir.CombineWithFilePath("filelist_258550.txt"), "regex:Managed\\/.*\\.dll");

    Information("DepotDownloader installation finished");
});

Task("UpdateReferences:OriginalLibraries")
.IsDependentOn("Install:DepotDownloader")
.Does(() => {

    var tempDir = downloadsDir.Combine("rust_ds_libs");
    EnsureDirectoryExists(tempDir);
    CleanDirectory(tempDir);

    DotNetExecute(depotdownloaderDllPath,
    new ProcessArgumentBuilder()
    .AppendSwitch("-app", "258550")
    .AppendSwitch("-filelist", depotDownloaderDir.CombineWithFilePath("filelist_258550.txt").ToString())
    .AppendSwitch("-dir", tempDir.ToString()),
    new DotNetExecuteSettings{
        WorkingDirectory = tempDir,
        Verbosity = DotNetVerbosity.Normal
    });

    EnsureDirectoryExists(referencesDir);
    CleanDirectory(referencesDir);

    MoveFiles(tempDir.CombineWithFilePath("**/*.dll").ToString(), referencesDir);
});

Task("UpdateReferences:OxideLibraries")
.Does(() => {
    const string DOWNLOAD_URL = "https://umod.org/games/rust/download?tag=public";

    var downloadPath = downloadsDir.CombineWithFilePath("Oxide.Rust.zip");

    if(FileExists(downloadPath))
        DeleteFile(downloadPath);

    DownloadFile(DOWNLOAD_URL, downloadPath);

    var oxideDir = downloadsDir.Combine("rust_ds_oxide");

    EnsureDirectoryExists(oxideDir);
    CleanDirectory(oxideDir);

    Unzip(downloadPath, oxideDir);

    GetFiles(oxideDir.CombineWithFilePath("RustDedicated_Data/Managed/*.dll").ToString()).ToList()
    .ForEach(file => {
        var targetPath = referencesDir.CombineWithFilePath(file.GetFilename());
        if(FileExists(targetPath))
            DeleteFile(targetPath);
        MoveFile(file, targetPath);
    });
});

Task("UpdateReferences")
.IsDependentOn("UpdateReferences:OriginalLibraries")
.IsDependentOn("UpdateReferences:OxideLibraries");

RunTarget(target);
