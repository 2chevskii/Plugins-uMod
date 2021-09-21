#!/usr/bin/env pwsh

using namespace System.IO

[CmdletBinding(PositionalBinding = $true)]
param (
  [Parameter(Mandatory = $false)]
  [ValidateNotNullOrEmpty()]
  [string] $Path,
  [Parameter(Mandatory = $false)]
  [ValidateNotNullOrEmpty()]
  [string] $DepotDownloaderPath,
  [Parameter(Mandatory = $false)]
  [ValidateSet('Original', 'Oxide', 'uMod')]
  [string] $ReferenceType = 'Oxide',
  [Parameter(Mandatory = $false)]
  [ValidateSet('windows', 'linux')]
  [string] $Os,
  [Parameter(Mandatory = $false)]
  [switch] $Clean
)

begin {

  # setup global constants

  $temp_directory = Join-Path ([Path]::GetTempPath()) 'automation' 'update-references'
  $downloads_dir = Join-Path $temp_directory 'downloads'
  $depot_downloader_dir = ''

  if (![string]::IsNullOrWhiteSpace($DepotDownloaderPath)) {
    $depot_downloader_dir = [Path]::GetFullPath($DepotDownloaderPath)
  } else {
    $depot_downloader_dir = Join-Path $temp_directory 'depot-downloader'
  }

  if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $PSScriptRoot 'References'
  }

  $Path = [Path]::GetFullPath($Path)

  if (!$Os) {
    if ($IsWindows) {
      $Os = 'windows'
    } else {
      $Os = 'linux'
    }
  }

  $depot_downloader_exec = Join-Path $depot_downloader_dir 'DepotDownloader.dll'
  $depot_downloader_archive = Join-Path $downloads_dir 'depot-downloader.zip'
  $depot_downloader_version = '2.4.4'
  $depot_downloader_download_url = 'https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_{version}/depotdownloader-{version}.zip'.Replace('{version}', $depot_downloader_version)

  $dotnet_install_path = Join-Path $downloads_dir 'dotnet-install.ps1'
  $dotnet_install_download_url = 'https://gist.githubusercontent.com/2chevskii/69d93f3a753ca7e695e276d7f8b9c6ed/raw/4cfdb0f481f2d7bddb45afec8c9a940270e71463/dotnet-install.ps1'

  $filelist_path = Join-Path $temp_directory 'references.list'
  $filelist_regex = 'regex:RustDedicated_Data\/Managed\/.*\.dll'

  $oxide_dir = Join-Path $temp_directory 'oxide'
  $oxide_archive = Join-Path $downloads_dir 'Oxide.Rust.zip'

  $umod_dir = $oxide_dir.Replace('oxide', 'umod')
  $umod_archive = $oxide_archive.Replace('Oxide', 'uMod')

  $oxide_releases_url = 'https://api.github.com/repos/OxideMod/Oxide.Rust/releases'
  $umod_manifest_url = 'https://assets.umod.org/uMod.Manifest.json'

  $depot_id = $Os -eq 'windows' ? 258551 : 258552

  # setup functions

  function Get-OxideRust-DownloadLink {
    $assets_url = (Invoke-WebRequest $oxide_releases_url | Select-Object -ExpandProperty Content | ConvertFrom-Json)[0].assets_url
    $assets = Invoke-WebRequest $assets_url | Select-Object -ExpandProperty Content | ConvertFrom-Json
    return @{
      linux   = $assets[0].browser_download_url
      windows = $assets[1].browser_download_url
    }
  }

  function Get-uModRust-DownloadLink {
    $packages = (Invoke-WebRequest $umod_manifest_url | Select-Object -ExpandProperty Content | ConvertFrom-Json).Packages
    $umod_rust = $packages | Where-Object -Property 'FileName' -EQ 'uMod.Rust.dll'
    $artifacts = $umod_rust.Resources | Where-Object -Property 'Type' -EQ 4 | Where-Object -Property 'Version' -EQ 'develop'
    return @{
      linux   = ($artifacts.Artifacts | Where-Object -Property 'Platform' -EQ 'linux').Url
      windows = ($artifacts.Artifacts | Where-Object -Property 'Platform' -EQ 'windows').Url
    }
  }

  function Log {
    param (
      $format,
      $arguments
    )

    $message = $format -f $arguments

    Write-Colorized "<cyan>##</cyan> $message"
  }
}

process {
  if ($VerbosePreference -ne 'SilentlyContinue') {
    Log 'Script variables:'
    Log 'References update path: {0}' $Path
    Log 'Installation OS: {0}' $Os.ToUpper()
    Log 'Temp directory: {0}' $temp_directory
    Log 'Downloads directory: {0}' $downloads_dir
    Log 'DepotDownloader directory: {0}' $depot_downloader_dir
    Log 'DepotDownloader download url: {0}' $depot_downloader_download_url
    Log 'dotnet-install path: {0}' $dotnet_install_path
    Log 'Filelist path: {0}' $filelist_path
    Log 'Reference type: {0}' $ReferenceType
    Log 'Clean preference: {0}' $Clean
  }

  Log 'Updating references ({0}) at {1}' ($ReferenceType, $Path)

  if (!(Test-Path $temp_directory)) {
    New-Item -Path $temp_directory -ItemType Directory | Out-Null
  }

  if (!(Test-Path $downloads_dir)) {
    New-Item $downloads_dir -ItemType Directory | Out-Null
  }

  $dotnet_update_needed = $false

  if (!(Get-Command 'dotnet')) {
    $dotnet_update_needed = $true
  }

  $dotnet_version_current = [semver](dotnet --version)

  if (!$dotnet_version_current -or ($dotnet_version_current.Major -lt 5)) {
    $dotnet_update_needed = $true
  }

  if ($dotnet_update_needed) {
    Log 'Issuing .NET update to v5.* | Current version: {0}' ($dotnet_version_current ? $dotnet_version_current : 'unknown')

    if (!(Test-Path $dotnet_install_path)) {
      Log 'Not found dotnet-install.ps1, downloading it from {0}...' $dotnet_install_download_url
      try {
        Invoke-WebRequest $dotnet_install_download_url -OutFile $dotnet_install_path
      } catch {
        Log 'Failed to download dotnet-install script'
        exit 1
      }
    }

    try {
      Log 'Running .NET update script, accept UAC prompt/sudo password request'

      if ($IsWindows) {
        Start-Process -FilePath 'pwsh' -ArgumentList "$dotnet_install_path -Channel 5.0 -Verbose" -Verb RunAs -Wait
      } else {
        . sudo pwsh $dotnet_install_path -Channel 5.0 -Verbose
      }

      if ($LASTEXITCODE -ne 0) {
        throw 'Script fail'
      }
    } catch {
      Log 'Failed to update .NET: {0}' $_.Exception.Message
      exit 1
    }
  }

  if (!(Test-Path $depot_downloader_exec)) {
    Log 'DepotDownloader executable not found, installing version {0} from {1}' ($depot_downloader_version, $depot_downloader_download_url)
    Log 'Installation directory: {0}' $depot_downloader_dir

    if (!(Test-Path $depot_downloader_dir)) {
      New-Item $depot_downloader_dir -ItemType Directory | Out-Null
    }

    if (!(Test-Path $depot_downloader_archive)) {
      try {
        Invoke-WebRequest $depot_downloader_download_url -OutFile $depot_downloader_archive
      } catch {
        Log 'Failed to download DepotDownloader'
        exit 1
      }
    }

    try {
      Expand-Archive -Path $depot_downloader_archive -DestinationPath $depot_downloader_dir
    } catch {
      Log 'Failed to extract DepotDownloader'
      Remove-Item $depot_downloader_archive
      exit 1
    }
  }

  if (!(Test-Path $Path)) {
    New-Item $Path -ItemType Directory | Out-Null
  } elseif ($Clean) {
    $old_files = Get-ChildItem $Path -Recurse -Filter '*.dll'
    Log 'Cleaning {0} old files from {1}' ($files.Count, $Path)
    $old_files | Remove-Item -Recurse
  }

  $filelist_regex > $filelist_path

  try {
    Log 'Using DepotDownloader to download RustDedicated_Data/Managed files...'
    . dotnet $depot_downloader_exec -app 258550 -dir $Path -filelist $filelist_path -depot $depot_id

    if ($LASTEXITCODE -ne 0) {
      throw "DepotDownloader exited with code $LASTEXITCODE"
    }
  } catch {
    Log 'Failed to download RustDedicated files: {0}' $_.Exception.Message
    exit 1
  }

  $files = Get-ChildItem -Path (Join-Path $Path 'RustDedicated_Data' 'Managed') -Filter '*.dll'
  Log 'Moving {0} files to destination folder...' $files.Count
  $files | Move-Item -Destination $Path -Force
  Log 'Cleaning up...'
  Remove-Item (Join-Path $Path 'RustDedicated_Data') -Recurse
  Remove-Item (Join-Path $Path '.DepotDownloader') -Recurse -Force

  if ($ReferenceType -eq 'Original') {
    Log 'Original references updated, exiting'
    exit 0
  }

  Log 'Downloading latest {0} files...' ($ReferenceType -eq 'Oxide' ? 'Oxide' : 'uMod')
  $download_link
  $archive_path
  $files_dir
  try {
    if ($ReferenceType -eq 'Oxide') {
      $archive_path = $oxide_archive
      $files_dir = $oxide_dir
      $download_link = Get-OxideRust-DownloadLink
    } else {
      $archive_path = $umod_archive
      $files_dir = $umod_dir
      $download_link = Get-uModRust-DownloadLink
    }

    if (!$download_link) {
      throw 'Failed to fetch download links from API'
    }
  } catch {
    Log 'Failed to resolve download link: {0}' $_.Exception.Message
    exit 1
  }

  if ($Os -eq 'windows') {
    $download_link = $download_link.windows
  } else {
    $download_link = $download_link.linux
  }

  Log 'Selected {0} download link: {1}' ($Os, $download_link)

  try {
    Invoke-WebRequest $download_link -OutFile $archive_path
  } catch {
    Log 'Failed to download files'
    exit 1
  }

  if (Test-Path $files_dir) {
    Get-ChildItem $files_dir | Remove-Item $files_dir -Recurse
  } else {
    New-Item $files_dir -ItemType Directory | Out-Null
  }

  try {
    Expand-Archive -Path $archive_path -DestinationPath $files_dir -Force

    $files = Get-ChildItem $files_dir -Filter '*.dll' -Recurse
    Log 'Moving {0} files to the destination folder...' $files.Count
    $files | Move-Item -Destination $Path -Force
  } catch {
    Log 'Failed to extract and move files to the destination folder'
    exit 1
  }
  Log 'Cleaning up...'
  Remove-Item $files_dir -Recurse
  Remove-Item $archive_path

  Log 'Update successfull'
  exit 0
}
