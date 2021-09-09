[CmdletBinding()]
param (
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ReferencePath = '',

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $DepotDownloaderPath = ''
)

$DefaultReferencePath = "../References"
$RefPathProps = "./ReferenceDir.props"

$reference_path = $null
$customrefdirprops_exist = Test-Path $RefPathProps

$depotdownloader_latest_release_url = 'https://github.com/SteamRE/DepotDownloader/releases/latest'

$app_id = 258550
$depot_id = 258551

function Get-DepotDownloaderUrl {
    $releases = 'https://api.github.com/repos/steamre/depotdownloader/releases'

    $tag = Invoke-WebRequest -Uri $releases -UseBasicParsing | ConvertFrom-Json | Select-Object -ExpandProperty 'tag_name' -First 1

    Write-Information "Latest depotdownloader tag: $tag"

    $download_url = "https://github.com/steamre/depotdownloader/releases/download/$tag/$($tag -replace '_','-').zip"

    Write-Information "Download URL: $download_url"

    return $download_url
}

function Get-CustomRefDir {
    if(!$customrefdirprops_exist) {
        return $null
    }

    return Get-Content $RefPathProps -Raw | Select-Xml -XPath 'Project/PropertyGroup/CustomReferenceDir'
}

function Set-CustomRefDir {
    param (
        [string] $dir
    )

    $xml = [xml](@"
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <PropertyGroup>
    <CustomReferenceDir></CustomReferenceDir>
    </PropertyGroup>
</Project>
"@)

    $xml.Project.PropertyGroup.CustomReferenceDir = $dir

    $xml.Save($RefPathProps)
}

# Resolve reference path

if([string]::IsNullOrWhiteSpace($ReferencePath) -eq $false) {
    $reference_path = $ReferencePath

    if($customrefdirprops_exist) {
        Set-CustomRefDir $reference_path
    }
} elseif($customrefdirprops_exist) {
    $crd = Get-CustomRefDir

    if([string]::IsNullOrWhiteSpace($crd)) {
        $reference_path = $DefaultReferencePath
        Set-CustomRefDir $reference_path
    } else {
        $reference_path = $crd
    }
} else {
    $reference_path = $DefaultReferencePath
}

Write-Information "Reference directory set to: $reference_path"

if([string]::IsNullOrWhiteSpace($DepotDownloaderPath) -eq $true) {
    $DepotDownloaderPath = 'tools/DepotDownloader'
}

$DepotDownloaderPath = Join-Path -Path $PSScriptRoot -ChildPath $DepotDownloaderPath

Write-Information "DepotDownloader directory set to: $DepotDownloaderPath"

if(!(Test-Path $DepotDownloaderPath)) {
    mkdir $DepotDownloaderPath
}

$DepotDownloaderLatestArchive = Join-Path $DepotDownloaderPath 'latest.zip'

$DepotDownloaderExecutable = Join-Path $DepotDownloaderPath 'DepotDownloader.dll'

$depot_download_url = Get-DepotDownloaderUrl

Write-Information 'Downloading latest DepotDownloader release...'

Invoke-WebRequest $depot_download_url -UseBasicParsing -OutFile $DepotDownloaderLatestArchive

Write-Information 'Extracting DD archive...'

Expand-Archive -Path $DepotDownloaderLatestArchive -DestinationPath $DepotDownloaderPath -Force

dotnet $DepotDownloaderExecutable -app $app_id -depot $depot_id -dir $reference_path -filelist .references
