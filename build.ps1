$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
# los GIFs de la guia van adentro del .exe (se rearman con guia\hacer-gifs.ps1)
$res = @(Get-ChildItem "$PSScriptRoot\guia\*.gif" -ErrorAction SilentlyContinue | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" })
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /out:"$PSScriptRoot\DbdOverlay.exe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:"$fw\System.Web.Extensions.dll" `
  /r:"$fw\System.IO.Compression.dll" /r:"$fw\System.IO.Compression.FileSystem.dll" /r:"$fw\System.Management.dll" `
  /r:"$fw\Microsoft.VisualBasic.dll" /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" /r:System.Xaml.dll `
  /win32icon:"$PSScriptRoot\app.ico" @res "$PSScriptRoot\DbdOverlay.cs"
