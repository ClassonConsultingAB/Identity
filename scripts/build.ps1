param([switch]$SkipTests, [switch]$SkipPack)

$ErrorActionPreference = 'Stop'

Import-Module -Name $PSScriptRoot\modules\BuildTasks.psm1 -Force

$rootPath = Resolve-Path "$PSScriptRoot\.."
$versionFilePath = Join-Path $rootPath output\version.json
$outputDirPath = Join-Path $rootPath output
$slnPath = Join-Path $rootPath Classon.Identity.sln
$testResultsFilePath = Join-Path $outputDirPath Classon.Identity.trx
$codeCoverageFilePathPrefix = Join-Path $outputDirPath Classon.Identity.coverage
$codeCoverageReportDirPath = Join-Path $outputDirPath Classon.Identity.coverage
$packTargets = @(
    (Join-Path $rootPath src\Classon.Identity\Classon.Identity.csproj)
)
$configuration = 'Release'

Task Cleanup {
    if (Test-Path $outputDirPath) {
        Remove-Item $outputDirPath -Recurse
    }
    New-Item $outputDirPath -ItemType Directory | Out-Null
    Exec "dotnet clean $slnPath --verbosity minimal"
}

Task Build {
    Install-GitVersion
    Exec "dotnet-gitversion $rootPath /output file /outputfile $versionFilePath"
    (Get-Content $versionFilePath | ConvertFrom-Json).NuGetVersionV2 | `
        Set-Variable version -Scope Script
    Exec "dotnet restore $slnPath"
    Exec "dotnet build $slnPath --configuration $configuration /p:Version=$version --no-restore"
}

if (!$SkipTests) {
    Task Test {
        $codeCoverageFilePath = "$codeCoverageFilePathPrefix.xml"
        Exec "dotnet test $slnPath --configuration $configuration --no-build --logger 'trx;LogFileName=$testResultsFilePath' /property:CollectCoverage=True /property:CoverletOutputFormat=opencover /property:CoverletOutput=$codeCoverageFilePath /property:Exclude='[System.*]*'"
        Install-ReportGenerator
        $codeCoverageFilePaths = @(Resolve-Path "$codeCoverageFilePathPrefix*") -join ';'
        Exec "reportgenerator -reports:'$codeCoverageFilePaths' -targetdir:$codeCoverageReportDirPath -reporttypes:HTML"
    }
}

if (!$SkipPack) {
    Task Pack {
        foreach ($target in $packTargets) {
            Exec "dotnet pack $target --configuration $configuration --no-build /p:Version=$version --output $outputDirPath"
        }
    }
}
