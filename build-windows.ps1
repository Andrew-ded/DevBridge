$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK first' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'Install Node.js 22+ first' }
Push-Location frontend
npm install
if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
Pop-Location
New-Item -ItemType Directory -Force backend/wwwroot | Out-Null
Copy-Item frontend/dist/* backend/wwwroot/ -Recurse -Force
dotnet publish backend/DevBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed' }
Write-Host 'Done: dist/DevBridge.exe' -ForegroundColor Green
