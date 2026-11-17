# hacer-gifs.ps1 - rearma los GIFs de la guia de la primera vez (guia-es-1.gif ... guia-en-3.gif) con la app de verdad.
# Uso: compilar primero (build.ps1) y despues: powershell -File guia\hacer-gifs.ps1 [-Ffmpeg ruta\ffmpeg.exe]
# Hay que volver a compilar despues: los GIFs van adentro del .exe.
param([string]$Ffmpeg = 'ffmpeg')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tmp = Join-Path $env:TEMP 'cotf-gifs'
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force "$tmp\out" | Out-Null
Copy-Item "$root\DbdOverlay.exe" $tmp
foreach ($d in 'datos', 'iconos', 'mapas') { if (Test-Path "$root\$d") { Copy-Item "$root\$d" $tmp -Recurse } }
foreach ($lang in 'es', 'en') {
    # copia aparte: la lista queda fuera de la pantalla (MargenX) y sin sala ni voz
    "IdiomaApp=$lang`r`nPosicion=arriba-derecha`r`nMargenX=-9000`r`nMargenY=0`r`nNombres=si`r`nVisibilidad=siempre`r`nEscala=90`r`nSalaAuto=no`r`nVoz=no`r`nGuiaVista=si`r`n" |
        Set-Content "$tmp\config.ini" -Encoding ascii
    Start-Process "$tmp\DbdOverlay.exe" -ArgumentList '--gifs', "`"$tmp\out`"" -WorkingDirectory $tmp -Wait
}
if (Test-Path "$tmp\out\error.txt") { throw (Get-Content "$tmp\out\error.txt" -Raw) }
foreach ($d in Get-ChildItem "$tmp\out" -Directory) {
    Push-Location $d.FullName
    & $Ffmpeg -v error -y -f concat -i list.txt -vf 'fps=10,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle' -loop 0 "$root\guia\$($d.Name).gif"
    Pop-Location
}
Remove-Item $tmp -Recurse -Force
Get-ChildItem "$root\guia\*.gif" | Select-Object Name, Length
