$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$temporaryDirectory = Join-Path $projectDirectory '.integration-temp'
$privateKeyPath = Join-Path $temporaryDirectory 'id_ed25519'

try {
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

    if (-not (Test-Path $privateKeyPath)) {
        ssh-keygen -q -t ed25519 -N '""' -f $privateKeyPath
    }

    $env:SCC_SSH_PUBLIC_KEY = (Get-Content "$privateKeyPath.pub" -Raw).Trim()
    $env:SCC_SSH_KEY_PATH = $privateKeyPath
    $env:SCC_SSH_HOST = '127.0.0.1'
    $env:SCC_SSH_PORT = '2222'
    $env:SCC_SSH_USER = 'codex'
    $env:SCC_SSH_PASSWORD = 'codex-test-password'

    docker info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Engine is not running.'
    }

    docker compose -f (Join-Path $projectDirectory 'docker-compose.yml') up -d --wait
    dotnet test (Join-Path $projectDirectory 'ServerControlCenter.IntegrationTests.csproj') --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Integration tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    docker compose -f (Join-Path $projectDirectory 'docker-compose.yml') down --volumes --remove-orphans

    if (Test-Path $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
