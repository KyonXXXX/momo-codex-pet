param([switch]$DesktopShortcut)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $workspace
try {
    dotnet build -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $test = Start-Process -FilePath (Join-Path $workspace 'bin\Release\net8.0-windows\Momo.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'Self-tests failed. See artifacts/test-results.txt.' }
    dotnet publish -c Release --no-restore -o dist/Momo --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $exePath = Join-Path $workspace 'dist\Momo\Momo.exe'
    $wsh = New-Object -ComObject WScript.Shell
    $paths = @((Join-Path $workspace '启动 Momo 桌宠.lnk'))
    if ($DesktopShortcut) { $paths += Join-Path ([Environment]::GetFolderPath('Desktop')) 'Momo Codex 桌宠.lnk' }
    foreach ($path in $paths) {
        $shortcut = $wsh.CreateShortcut($path)
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = Split-Path -Parent $exePath
        $shortcut.IconLocation = $exePath + ',0'
        $shortcut.Description = 'Momo - 常驻显示 Codex 每周额度与 credits 余额'
        $shortcut.Save()
    }
    Write-Output "Ready: $exePath"
} finally { Pop-Location }
