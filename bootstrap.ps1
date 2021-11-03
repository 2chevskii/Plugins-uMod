$solution_dir = $PSScriptRoot
$project_dir = Join-Path $solution_dir 'src'
$custom_ref_path = Join-Path $solution_dir 'ReferenceDir.props'
$reference_dir
$custom_ref_path_enabled = $false
$upd_refs_script = Join-Path $solution_dir 'Update-References.ps1'

function Find-DefaultReferencePath {
    if (Test-Path "$project_dir/References") {
        return "$project_dir/References"
    } elseif (Test-Path "$solution_dir/References") {
        return "$solution_dir/References"
    } else {
        return [System.IO.Path]::GetFullPath('../References', $solution_dir)
    }
}

if (Test-Path $custom_ref_path) {
    # $x = Get-Content $custom_ref_path | Select-Xml -XPath 'Project/PropertyGroup/CustomReferenceDir'
    $xml = [xml](Get-Content $custom_ref_path);
    $namespace = @{ msb = 'http://schemas.microsoft.com/developer/msbuild/2003' }
    $node = Select-Xml $xml -Namespace $namespace -XPath '//msb:CustomReferenceDir' | ForEach-Object { $_.Node.InnerText.Replace('$(SolutionDir)', $PSScriptRoot) };

    if (![string]::IsNullOrWhiteSpace($node)) {
        $custom_ref_path_enabled = $true
        $reference_dir = [System.IO.Path]::GetFullPath([string]($node))
    }
}

if (!$custom_ref_path_enabled) {
    $reference_dir = Find-DefaultReferencePath
}

Write-Host ('Bootstrapping reference directory: {0}' -f $reference_dir)

. $upd_refs_script -Path $reference_dir -ReferenceType uMod -Clean

if ($LASTEXITCODE -eq 0) {
    Write-Host 'Bootstrap successful'
    exit 0
} else {
    Write-Error ('Bootstrap failed: {0}' -f $LASTEXITCODE)
    exit 1
}
