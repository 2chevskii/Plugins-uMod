var tempDir = DirectoryPath.FromString(System.IO.Path.GetTempPath()).Combine("2chevskii_plugins_umod");
var tempDownloadsDir = tempDir.Combine("downloads");
var depotdownloaderDir = tempDir.Combine("tools/DepotDownloader");
var referencesDir = Context.Environment.WorkingDirectory.Combine("include/references");

var target = Argument("target", Argument("t", "UpdateReferences"));

Setup(ctx => {
    EnsureDirectoryExists(tempDir);
    EnsureDirectoryExists(tempDownloadsDir);
    EnsureDirectoryExists(depotdownloaderDir);
    EnsureDirectoryExists(referencesDir);
});

Task("Install:DepotDownloader")
.Does(() => {
    Information("Installing DepotDownloader v2.4.7");

    if(FileExists(depotdownloaderDir.CombineWithFilePath("DepotDownloader.exe"))) {
        Verbose("DepotDownloader already installed");
        return;
    }

    const string DOWNLOAD_URL = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.4.7/depotdownloader-2.4.7.zip";
    var downloadPath = tempDownloadsDir.CombineWithFilePath("depotdownloader.zip");

    Verbose("Downloading release from {0} to {1}", DOWNLOAD_URL, downloadPath);

    DownloadFile(DOWNLOAD_URL, downloadPath);

    Verbose("Unpacking archive into {0}", depotdownloaderDir);

    Unzip(downloadPath, depotdownloaderDir);

    var filelistPath = depotdownloaderDir.CombineWithFilePath("filelist_258550.txt");

    Verbose("Creating filelist for app 258550: {0}", filelistPath);

    System.IO.File.WriteAllText(filelistPath.ToString(), "regex:Managed\\/.*\\.dll");

    Information("DepotDownloader installed");
});

Task("UpdateReferences:OriginalLibraries")
.IsDependentOn("Install:DepotDownloader")
.Does(() => {
    Information("Installing Rust DS managed libraries into {0}", referencesDir);

    StartProcess(depotdownloaderDir.CombineWithFilePath("DepotDownloader.exe"), new ProcessSettings {
        WorkingDirectory = depotdownloaderDir,
        Arguments = "-app 258550 -dir " + referencesDir.ToString() + " -filelist " + depotdownloaderDir.CombineWithFilePath("filelist_258550.txt").ToString(),
        RedirectStandardOutput = true,
        RedirectedStandardOutputHandler = log => {
            if(log != null)
                Information("DepotDownloader: {0}", log);
            return log;
        },
        RedirectStandardError = true,
        RedirectedStandardErrorHandler = log => {
            if(log != null)
                Error("DepotDownloader: {0}", log);
            return log;
        }
    });

    Verbose("Copying files to target location");

    GetFiles(referencesDir.Combine("RustDedicated_Data/Managed/*.dll").ToString()).ToList().ForEach(file => {
        MoveFileToDirectory(file, referencesDir);
    });

    DeleteDirectory(referencesDir.Combine("RustDedicated_Data"), new DeleteDirectorySettings {Recursive = true});
    DeleteDirectory(referencesDir.Combine(".DepotDownloader"), new DeleteDirectorySettings {Recursive = true});
});

Task("UpdateReferences:OxideLibraries")
.Does(() => {
    const string DOWNLOAD_URL = "https://umod.org/games/rust/download?tag=public";

    var downloadPath = tempDir.CombineWithFilePath("Oxide.Rust.zip");

    DownloadFile(DOWNLOAD_URL, tempDir.CombineWithFilePath("Oxide.Rust.zip"));

    Unzip(downloadPath, referencesDir);

    GetFiles(referencesDir.Combine("RustDedicated_Data/Managed/*.dll").ToString()).ToList().ForEach(file => {
        var targetFilePath = referencesDir.CombineWithFilePath(file.GetFilename());

        System.IO.File.Move(file.ToString(), targetFilePath.ToString(), true);
    });

    DeleteDirectory(referencesDir.Combine("RustDedicated_Data"), new DeleteDirectorySettings {Recursive = true});
});

Task("UpdateReferences")
.IsDependentOn("UpdateReferences:OriginalLibraries")
.IsDependentOn("UpdateReferences:OxideLibraries");

RunTarget(target);
