
$visualStudioInstallation = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath
$vsixInstaller = Join-Path $visualStudioInstallation 'Common7\IDE\VSIXInstaller.exe'

Write-Host "visualStudioInstallation: $visualStudioInstallation"
Write-Host "vsixInstaller: $vsixInstaller"
Write-Host "localArtifactPath: ${env:localArtifactPath}"

Start-Process -FilePath "$vsixInstaller" -ArgumentList "/q /a /sp /logFile:\"C:\projects\install.log\" ${env:localArtifactPath}" -Wait -PassThru;

Start-Sleep -s 20
"OK" | Write-Host -ForegroundColor Green
