$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Start-Process powershell -ArgumentList '-NoExit','-Command',"cd '$PSScriptRoot\backend'; dotnet run"
Set-Location frontend
npm run dev
