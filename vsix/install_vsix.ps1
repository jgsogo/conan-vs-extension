
$visualStudioInstallation = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath
$vsixInstaller = Join-Path $visualStudioInstallation 'Common7\IDE\VSIXInstaller.exe'

Write-Host "visualStudioInstallation: $visualStudioInstallation"
Write-Host "vsixInstaller: $vsixInstaller"
Write-Host "localArtifactPath: ${env:localArtifactPath}"

. $vsixInstaller -ArgumentList /q /a ${env:localArtifactPath}
"OK" | Write-Host -ForegroundColor Green
