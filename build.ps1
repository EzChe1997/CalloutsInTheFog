$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /out:"$PSScriptRoot\DbdOverlay.exe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:"$fw\System.Web.Extensions.dll" `
  /r:"$fw\System.IO.Compression.dll" /r:"$fw\System.IO.Compression.FileSystem.dll" /r:"$fw\System.Management.dll" `
  /r:"$fw\Microsoft.VisualBasic.dll" /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" /r:System.Xaml.dll `
  /win32icon:"$PSScriptRoot\app.ico" "$PSScriptRoot\DbdOverlay.cs"
