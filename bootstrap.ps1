using namespace System
using namespace System.IO

[CmdletBinding()]
param(
    [Parameter()]
    [string] $ReferencesDirectory,
    [Parameter()]
    [ValidateSet('vanilla', 'oxide', 'umod')]
    [string] $LibrariesType,
    [Parameter()]
    [string] $DepotDownloaderPath,
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $DepotDownloaderVersion = '2.4.7'
)

begin {
    $script_data_dir = [Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData), 'umod-ref-bootstrapper')


}

process {

}

end {

}
