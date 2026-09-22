Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile('C:/Users/aaron/source/repos/LRS/app_window.png')
Write-Output ('size ' + $bmp.Width + 'x' + $bmp.Height)
for ($y = 80; $y -lt $bmp.Height - 10; $y += 16) {
  $bright = 0
  for ($x = 300; $x -lt $bmp.Width - 20; $x += 6) {
    $c = $bmp.GetPixel($x, $y)
    if (($c.R -gt 70) -or ($c.G -gt 70) -or ($c.B -gt 70)) { $bright++ }
  }
  if ($bright -gt 5) { Write-Output ('y=' + $y + ' bright=' + $bright) }
}
$bmp.Dispose()
