# Made by Claude Ai & Relic Hunter
param([string]$Exe, [string]$View, [string]$Out, [int]$Wait = 5, [string]$Home2 = "", [string]$Game = "", [string[]]$Install = @(), [switch]$Keep)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
Get-Process RelicMods -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$a = @('--view', $View)
if ($Home2) { $a += @('--home', ('"' + $Home2 + '"')) }
if ($Game) { $a += @('--game', ('"' + $Game + '"')) }
foreach ($i in $Install) { $a += ('"' + $i + '"') }
$p = Start-Process -FilePath $Exe -ArgumentList $a -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); $h = $p.MainWindowHandle }
Start-Sleep -Seconds $Wait
$r = New-Object Win+RECT
[void][Win]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[void][Win]::PrintWindow($h, $dc, 2)
$g.ReleaseHdc($dc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if (-not $Keep) { Stop-Process -Id $p.Id -Force }
"captured $Out ($w x $ht)"
