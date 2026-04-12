
$regions = @("North", "South", "East", "West", "Central")
$products = @("Widget A", "Widget B", "Widget C", "Widget D")
$rowCount = 5000
$outputPath = "TestData/test_sales.csv"

# Ensure TestData exists
if (!(Test-Path "TestData")) { New-Item -ItemType Directory "TestData" | Out-Null }

$csv = New-Object System.Collections.Generic.List[string]
$csv.Add("region,product,units,revenue,month,date")

$rnd = New-Object System.Random
$zeroRevenueMonth = "2023-06" # Force June 2023 to have zero revenue for some rows or all? 
# User said "at least one month with 0 revenue". I'll make one month have 0 for everything to be safe.

for ($i = 0; $i -lt $rowCount; $i++) {
    $region = $regions[$rnd.Next(0, $regions.Count)]
    $product = $products[$rnd.Next(0, $products.Count)]
    $units = $rnd.Next(1, 100)
    
    # 5 years: 2021 to 2025
    $daysOffset = $rnd.Next(0, (365 * 5))
    $date = (Get-Date "2021-01-01").AddDays($daysOffset)
    $dateStr = $date.ToString("yyyy-MM-dd")
    $monthStr = $date.ToString("yyyy-MM")
    
    $revenue = [math]::Round($units * (10.5 + $rnd.NextDouble() * 40.0), 2)
    
    if ($monthStr -eq $zeroRevenueMonth) {
        $revenue = 0.00
    }

    $csv.Add("$region,$product,$units,$revenue,$monthStr,$dateStr")
}

$csv | Out-File -FilePath $outputPath -Encoding utf8
Write-Host "Generated $rowCount rows to $outputPath"
