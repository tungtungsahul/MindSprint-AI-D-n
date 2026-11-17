param(
    [string]$SourcePath = (Join-Path $PSScriptRoot 'erd.mmd'),
    [string]$PngPath = (Join-Path $PSScriptRoot 'erd.png'),
    [string]$SvgPath = (Join-Path $PSScriptRoot 'erd.svg')
)

$source = Get-Content $SourcePath -Raw
$entityMatches = [regex]::Matches($source, '(?ms)^    (?<name>\w+) \{\r?\n(?<body>.*?)^    \}')
$entities = foreach ($match in $entityMatches) {
    [pscustomobject]@{
        Name = $match.Groups['name'].Value
        Fields = @([regex]::Matches($match.Groups['body'].Value, '(?m)^        (?<field>.+)$') | ForEach-Object { $_.Groups['field'].Value.Trim() })
    }
}
$relations = foreach ($line in ($source -split "`r?`n")) {
    if ($line -match '^    (?<from>\w+)\s+.*?\s+(?<to>\w+)\s*:\s*"(?<label>[^"]+)"') {
        [pscustomobject]@{
            From = $Matches.from
            To = $Matches.to
            Label = $Matches.label
            IsForeignKey = $Matches.label.Contains('FK CASCADE')
        }
    }
}

$width = 1650
$height = 1680
$cardWidth = 470
$rowTops = @(150, 530, 910, 1290)
$columns = @(70, 590, 1110)
$positions = @{}
for ($index = 0; $index -lt $entities.Count; $index++) {
    $positions[$entities[$index].Name] = @{
        X = $columns[$index % 3]
        Y = $rowTops[[math]::Floor($index / 3)]
        Height = 48 + ($entities[$index].Fields.Count * 23)
    }
}
function Format-Field([string]$value) {
    $formatted = $value -replace '\s+"[^"]*"', ''
    if ($value -match '"NULL"') { $formatted += ' ?' }
    return $formatted
}

$svg = [System.Collections.Generic.List[string]]::new()
$svg.Add("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$width`" height=`"$height`" viewBox=`"0 0 $width $height`">")
$svg.Add('<rect width="100%" height="100%" fill="#f3f7f6"/>')
$svg.Add('<text x="70" y="58" font-family="Segoe UI, sans-serif" font-size="30" font-weight="700" fill="#173b4d">MindSprintAI | PostgreSQL ERD</text>')
$svg.Add('<text x="70" y="94" font-family="Segoe UI, sans-serif" font-size="17" fill="#526b75">12 tables | 91 columns | main b890dcc | 14/10/2026</text>')
$svg.Add('<line x1="1040" y1="82" x2="1090" y2="82" stroke="#16846c" stroke-width="3"/><text x="1100" y="88" font-family="Segoe UI, sans-serif" font-size="15" fill="#526b75">Foreign key</text>')
$svg.Add('<line x1="1265" y1="82" x2="1315" y2="82" stroke="#a76a28" stroke-width="3" stroke-dasharray="7 6"/><text x="1325" y="88" font-family="Segoe UI, sans-serif" font-size="15" fill="#526b75">Logical reference</text>')

foreach ($relation in $relations) {
    $from = $positions[$relation.From]
    $to = $positions[$relation.To]
    if ($null -eq $from -or $null -eq $to) { continue }
    $fromCenterX = $from.X + ($cardWidth / 2)
    $toCenterX = $to.X + ($cardWidth / 2)
    if ([math]::Abs($fromCenterX - $toCenterX) -gt 20) {
        $x1 = if ($fromCenterX -lt $toCenterX) { $from.X + $cardWidth } else { $from.X }
        $x2 = if ($fromCenterX -lt $toCenterX) { $to.X } else { $to.X + $cardWidth }
        $y1 = $from.Y + 24
        $y2 = $to.Y + 24
    } else {
        $x1 = $fromCenterX
        $x2 = $toCenterX
        $y1 = if ($from.Y -lt $to.Y) { $from.Y + $from.Height } else { $from.Y }
        $y2 = if ($from.Y -lt $to.Y) { $to.Y } else { $to.Y + $to.Height }
    }
    $stroke = if ($relation.IsForeignKey) { '#16846c' } else { '#a76a28' }
    $dash = if ($relation.IsForeignKey) { '' } else { ' stroke-dasharray="7 6"' }
    $svg.Add("<line x1=`"$x1`" y1=`"$y1`" x2=`"$x2`" y2=`"$y2`" stroke=`"$stroke`" stroke-width=`"3`"$dash/>")
}

foreach ($entity in $entities) {
    $position = $positions[$entity.Name]
    $cardHeight = $position.Height
    $svg.Add("<rect x=`"$($position.X)`" y=`"$($position.Y)`" width=`"$cardWidth`" height=`"$cardHeight`" rx=`"5`" fill=`"#ffffff`" stroke=`"#b9c9c9`" stroke-width=`"1.5`"/>")
    $svg.Add("<path d=`"M $($position.X + 5) $($position.Y) H $($position.X + $cardWidth - 5) Q $($position.X + $cardWidth) $($position.Y) $($position.X + $cardWidth) $($position.Y + 5) V $($position.Y + 42) H $($position.X) V $($position.Y + 5) Q $($position.X) $($position.Y) $($position.X + 5) $($position.Y)`" fill=`"#173b4d`"/>")
    $svg.Add("<text x=`"$($position.X + 15)`" y=`"$($position.Y + 28)`" font-family=`"Segoe UI, sans-serif`" font-size=`"18`" font-weight=`"700`" fill=`"#ffffff`">$([System.Security.SecurityElement]::Escape($entity.Name))</text>")
    for ($fieldIndex = 0; $fieldIndex -lt $entity.Fields.Count; $fieldIndex++) {
        $fieldY = $position.Y + 66 + ($fieldIndex * 23)
        $field = [System.Security.SecurityElement]::Escape((Format-Field $entity.Fields[$fieldIndex]))
        $svg.Add("<text x=`"$($position.X + 16)`" y=`"$fieldY`" font-family=`"Consolas, monospace`" font-size=`"14`" fill=`"#334f59`">$field</text>")
    }
}
$svg.Add('</svg>')
[System.IO.File]::WriteAllText($SvgPath, ($svg -join "`n"), [System.Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(243, 247, 246))
$titleFont = [System.Drawing.Font]::new('Segoe UI', 30, [System.Drawing.FontStyle]::Bold)
$metaFont = [System.Drawing.Font]::new('Segoe UI', 17)
$headerFont = [System.Drawing.Font]::new('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
$fieldFont = [System.Drawing.Font]::new('Consolas', 14)
$titleBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(23, 59, 77))
$metaBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(82, 107, 117))
$fieldBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(51, 79, 89))
$headerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(23, 59, 77))
$whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$cardBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$borderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(185, 201, 201), 1.5)
$fkPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(22, 132, 108), 3)
$logicalPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(167, 106, 40), 3)
$logicalPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
$graphics.DrawString('MindSprintAI | PostgreSQL ERD', $titleFont, $titleBrush, 70, 22)
$graphics.DrawString('12 tables | 91 columns | main b890dcc | 14/10/2026', $metaFont, $metaBrush, 70, 67)
$graphics.DrawLine($fkPen, 1050, 100, 1095, 100)
$graphics.DrawString('Foreign key', $metaFont, $metaBrush, 1105, 88)
$graphics.DrawLine($logicalPen, 1280, 100, 1325, 100)
$graphics.DrawString('Logical reference', $metaFont, $metaBrush, 1335, 88)

foreach ($relation in $relations) {
    $from = $positions[$relation.From]
    $to = $positions[$relation.To]
    if ($null -eq $from -or $null -eq $to) { continue }
    $fromCenterX = $from.X + ($cardWidth / 2)
    $toCenterX = $to.X + ($cardWidth / 2)
    if ([math]::Abs($fromCenterX - $toCenterX) -gt 20) {
        $x1 = if ($fromCenterX -lt $toCenterX) { $from.X + $cardWidth } else { $from.X }
        $x2 = if ($fromCenterX -lt $toCenterX) { $to.X } else { $to.X + $cardWidth }
        $y1 = $from.Y + 24
        $y2 = $to.Y + 24
    } else {
        $x1 = $fromCenterX
        $x2 = $toCenterX
        $y1 = if ($from.Y -lt $to.Y) { $from.Y + $from.Height } else { $from.Y }
        $y2 = if ($from.Y -lt $to.Y) { $to.Y } else { $to.Y + $to.Height }
    }
    $pen = if ($relation.IsForeignKey) { $fkPen } else { $logicalPen }
    $graphics.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
}

foreach ($entity in $entities) {
    $position = $positions[$entity.Name]
    $cardHeight = $position.Height
    $headerRect = [System.Drawing.RectangleF]::new($position.X, $position.Y, $cardWidth, 44)
    $cardRect = [System.Drawing.RectangleF]::new($position.X, $position.Y, $cardWidth, $cardHeight)
    $graphics.FillRectangle($cardBrush, $cardRect)
    $graphics.DrawRectangle($borderPen, $position.X, $position.Y, $cardWidth, $cardHeight)
    $graphics.FillRectangle($headerBrush, $headerRect)
    $graphics.DrawString($entity.Name, $headerFont, $whiteBrush, $position.X + 15, $position.Y + 8)
    for ($fieldIndex = 0; $fieldIndex -lt $entity.Fields.Count; $fieldIndex++) {
        $fieldY = $position.Y + 51 + ($fieldIndex * 23)
        $graphics.DrawString((Format-Field $entity.Fields[$fieldIndex]), $fieldFont, $fieldBrush, $position.X + 16, $fieldY)
    }
}

$bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
foreach ($resource in @($graphics, $bitmap, $titleFont, $metaFont, $headerFont, $fieldFont, $titleBrush, $metaBrush, $fieldBrush, $headerBrush, $whiteBrush, $cardBrush, $borderPen, $fkPen, $logicalPen)) {
    if ($resource -is [System.IDisposable]) { $resource.Dispose() }
}
Write-Output "Rendered $($entities.Count) tables to $PngPath and $SvgPath"