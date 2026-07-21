param([switch]$SkipTests, [switch]$SkipPack, [switch]$Publish)

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
$organization = 'ClassonConsultingAB'
$packageName = 'Classon.Identity'
$nugetSource = "https://nuget.pkg.github.com/$organization/index.json"

if (Test-Path $outputDirPath) { Remove-Item $outputDirPath -Recurse }
New-Item $outputDirPath -ItemType Directory | Out-Null
Install-GitVersion
Exec "dotnet-gitversion $rootPath /output file /outputfile $versionFilePath"
$version = (Get-Content $versionFilePath | ConvertFrom-Json).NuGetVersionV2

if ($Publish) {
    if ([string]::IsNullOrEmpty($env:GH_TOKEN)) {
        throw 'GH_TOKEN environment variable is not set.'
    }

    Task 'Check already published' {
        $orgPackageNames = @(Exec "gh api orgs/$organization/packages?package_type=nuget --jq '.[].name'" -ReturnOutput)
        $alreadyPublished = $false
        if ($orgPackageNames -contains $packageName) {
            $existingVersions = @(Exec "gh api orgs/$organization/packages/nuget/$packageName/versions --jq '.[].name'" -ReturnOutput)
            $alreadyPublished = $existingVersions -contains $version
        }
        if ($alreadyPublished) {
            Write-Host "Version $version is already published to $nugetSource. Nothing to do."
            exit 0
        }
    }
}

Task Cleanup {
    Exec "dotnet clean $slnPath --verbosity minimal"
}

Task Build {
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

if ($Publish) {
    Task Publish {
        $packages = Get-ChildItem $outputDirPath -Filter *.nupkg | Select-Object -ExpandProperty FullName
        if ($packages.Length -eq 0) {
            Fail 'Found no packages to publish'
        }
        foreach ($package in $packages) {
            $packagePath = Resolve-Path $package
            Exec { dotnet nuget push $packagePath --api-key $env:GH_TOKEN --source $nugetSource --skip-duplicate }
        }
    }
}
