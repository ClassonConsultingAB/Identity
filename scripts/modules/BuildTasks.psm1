function Task($Title, $Command) {
    Write-Host @"
╬════════════
║ $Title
╬═══

"@ -ForegroundColor DarkGray
    Invoke-Command -ScriptBlock $Command
}

function Fail($Message) {
    Write-Error $Message
    exit -1
}

function Exec($Command) {
    if ($Command.GetType().Name -eq 'String') {
        $Command = [scriptblock]::Create($Command)
    }
    Write-Host "[$Command]" -ForegroundColor DarkGray
    Invoke-Command -ScriptBlock $Command
    Write-Host
    if ($LASTEXITCODE -ne 0) {
        Fail 'Something bad happened'
    }
}

function Install-GitVersion {
    if ($null -eq (Get-Command dotnet-gitversion -ErrorAction SilentlyContinue)) {
        dotnet tool install --global GitVersion.Tool --version 5.*
    }
}

function Install-ReportGenerator {
    if ($null -eq (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
        dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.*
    }
}
