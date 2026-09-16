param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', ('/win32manifest:' + "$PSScriptRoot\app.manifest"), ('/win32icon:' + "$PSScriptRoot\assets\app.ico"), ('/out:' + "$OutputDirectory\CodexUsage.exe"), ('/resource:' + "$PSScriptRoot\Main.xaml,Main.xaml"), ('/resource:' + "$PSScriptRoot\assets\app.ico,app.ico"), ('/resource:' + "$PSScriptRoot\assets\app.png,app.png")) + $refs + @("$PSScriptRoot\App.cs", "$PSScriptRoot\Theme.cs", "$PSScriptRoot\ResetCoordinator.cs", "$PSScriptRoot\FeatureTests.cs")
& "$framework/csc.exe" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath "$PSScriptRoot/README.md" -Destination "$OutputDirectory/README.md" -Force
Copy-Item -LiteralPath "$PSScriptRoot/LICENSE" -Destination "$OutputDirectory/LICENSE" -Force
Write-Output "Built: $OutputDirectory/CodexUsage.exe"
