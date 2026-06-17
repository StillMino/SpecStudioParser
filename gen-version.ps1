param(
    [string]$ProjectDir,
    [string]$IntermediateDir,
    [string]$AssemblyVersion = "1.0.0"
)

$branch = git -C $ProjectDir rev-parse --abbrev-ref HEAD 2>$null
$count  = git -C $ProjectDir rev-list --count HEAD 2>$null
$hash   = git -C $ProjectDir log -1 --format=%h 2>$null
$date   = Get-Date -Format "yyyy-MM-dd HH:mm"

if (-not $branch) { $branch = "unknown" }
if (-not $count)  { $count  = "0" }
if (-not $hash)   { $hash   = "0000000" }

$version = "$AssemblyVersion-$branch-$count-$hash"

$content = @"
// Auto-generated
namespace SpecStudioParser { internal static class VersionInfo { public const string Version = "$version"; public const string BuildDate = "$date"; } }
"@

$outFile = Join-Path $IntermediateDir "VersionInfo.Generated.cs"
$content | Out-File -Encoding UTF8 $outFile
Write-Output "VersionInfo: $version"
