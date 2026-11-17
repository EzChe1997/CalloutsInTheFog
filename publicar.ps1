# publicar.ps1 - publica una version nueva del overlay para que la app de tus amigos se actualice sola.
# Uso: doble clic en "Publicar actualizacion.bat" (o: powershell -File publicar.ps1 -Notes "que cambio")
# Pasos: sube el numero de version, compila, firma el indice con tu clave privada, rearma el zip y sube todo a GitHub.
param([string]$Notes, [switch]$NoPush)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$keyFile = Join-Path $env:LOCALAPPDATA 'dbd-overlay\update-key.xml'
if (-not (Test-Path $keyFile)) { throw "No encuentro la clave para firmar ($keyFile). Sin ella tus amigos rechazan las actualizaciones." }

if ($PSBoundParameters.Count -eq 0) { $Notes = Read-Host 'Qué cambió en esta versión (una línea, opcional)' }

# 1) Subir la version
$src = Join-Path $here 'DbdOverlay.cs'
$t = [IO.File]::ReadAllText($src)
if ($t -notmatch 'public const int AppVersion = (\d+);') { throw 'No encuentro AppVersion en DbdOverlay.cs' }
$ver = [int]$Matches[1] + 1
$t = [regex]::Replace($t, 'public const int AppVersion = \d+;', "public const int AppVersion = $ver;")
[IO.File]::WriteAllText($src, $t, (New-Object Text.UTF8Encoding($false)))

# 2) Compilar (si el overlay esta abierto, se aparta el exe en uso y al final se reabre con la version nueva)
$exe = Join-Path $here 'DbdOverlay.exe'
$running = @(Get-Process DbdOverlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$here\*" })
if ($running) {
    Remove-Item "$exe.old" -Force -ErrorAction SilentlyContinue
    if (Test-Path "$exe.old") { $running | Stop-Process -Force; Start-Sleep 1; Remove-Item "$exe.old" -Force }
    if (Test-Path $exe) { Move-Item $exe "$exe.old" }
}
$before = if (Test-Path $exe) { (Get-Item $exe).LastWriteTimeUtc } else { [DateTime]::MinValue }
$out = & (Join-Path $here 'build.ps1') 2>&1 | Out-String
if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTimeUtc -le $before -or $out -match 'error CS') {
    Write-Host $out
    throw 'No compiló (si el overlay está abierto, cerralo y probá de nuevo).'
}

# 3) Indice con la huella del exe, firmado
$sha = [Security.Cryptography.SHA256]::Create()
$hash = -join ($sha.ComputeHash([IO.File]::ReadAllBytes($exe)) | ForEach-Object { $_.ToString('x2') })
$man = [ordered]@{ version = $ver; notes = "$Notes"; date = (Get-Date -Format 'yyyy-MM-dd HH:mm'); files = [ordered]@{ 'DbdOverlay.exe' = $hash } }
$manBytes = (New-Object Text.UTF8Encoding($false)).GetBytes(($man | ConvertTo-Json -Depth 3))
[IO.File]::WriteAllBytes((Join-Path $here 'update.json'), $manBytes)
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($keyFile))
[IO.File]::WriteAllText((Join-Path $here 'update.sig'), [Convert]::ToBase64String($rsa.SignData($manBytes, 'SHA256')))

# 4) Zip para amigos nuevos (sin tu configuracion)
$pkg = Join-Path $here 'dist\DBD-Overlay'
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item $exe, (Join-Path $here 'LEEME.txt') $pkg -Force
Compress-Archive -Path $pkg -DestinationPath (Join-Path $here 'dist\DBD-Overlay.zip') -Force

# 5) Subir a GitHub
git add -- DbdOverlay.cs DbdOverlay.exe app.ico build.ps1 LEEME.txt README.md update.json update.sig .gitignore .gitattributes publicar.ps1 'Publicar actualizacion.bat'
git commit -q -m "Version $ver$(if ($Notes) { ": $Notes" })"
if (-not $NoPush) { git push -q origin main; if ($LASTEXITCODE) { throw 'No se pudo subir a GitHub (git push falló).' } }
if ($running) { Get-Process DbdOverlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$here\*" } | Stop-Process -Force; Start-Sleep 1; Start-Process $exe }
Write-Host ''
Write-Host "Listo: versión $ver publicada. Tus amigos la reciben la próxima vez que abran la app." -ForegroundColor Green
