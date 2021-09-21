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
  $x = Get-Content $custom_ref_path | Select-Xml -XPath '//CustomReferenceDir'

  if (![string]::IsNullOrWhiteSpace($x)) {
    $custom_ref_path_enabled = $true
    $reference_dir = [System.IO.Path]::GetFullPath($custom_ref_path, $solution_dir)
  }
}

if (!$custom_ref_path_enabled) {
  $reference_dir = Find-DefaultReferencePath
}

. $upd_refs_script -Path $reference_dir -ReferenceType Oxide -Clean
