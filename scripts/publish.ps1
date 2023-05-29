param(
    [string[]]$Packages,
    $GitHubPat = $env:GITHUBPAT,
    $Source = 'https://nuget.pkg.github.com/ClassonConsultingAB/index.json')

$ErrorActionPreference = 'Stop'

Import-Module -Name $PSScriptRoot\modules\BuildTasks.psm1 -Force

$rootPath = Resolve-Path "$PSScriptRoot\.."
$outputDirPath = Join-Path $rootPath output

Task Publish {
    if ($Packages.Length -eq 0) {
        $Packages = Get-ChildItem $outputDirPath -Filter *.nupkg | Select-Object -ExpandProperty FullName
    }
    if ($Packages.Length -eq 0) {
        Fail 'Found no packages to publish'
    }
    foreach ($package in $packages) {
        $packagePath = Resolve-Path $package
        Exec { dotnet nuget push $packagePath --api-key $GitHubPat --source $Source --skip-duplicate }
    }
}
