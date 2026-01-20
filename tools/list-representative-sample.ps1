param(
  [string]$Dir = 'Sample\Compair\CSV\N'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Ss = @(10,12,100,200)
$Ps = @('0.05','0.5','1.0')
$Ns = @(1,10,100)

$pairs = @()

foreach ($S in $Ss) {
  foreach ($P in $Ps) {
    $pNorm = ([double]::Parse($P, [Globalization.CultureInfo]::InvariantCulture)).ToString('0.####', [Globalization.CultureInfo]::InvariantCulture)
    foreach ($N in $Ns) {
      $png = Join-Path $Dir ("dot512-material-S$S-P$pNorm-N$N.png")
      $csv = Join-Path $Dir ("radial-falloff-S$S-P$pNorm-N$N.csv")

      if ((Test-Path -LiteralPath $png) -and (Test-Path -LiteralPath $csv)) {
        $pairs += [PSCustomObject]@{ S=$S; P=$pNorm; N=$N; Png=$png; Csv=$csv }
      }
    }
  }
}

$pairs | Sort-Object S,P,N | Format-Table -AutoSize

"Total: $($pairs.Count)"