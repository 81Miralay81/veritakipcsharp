param(
  [Parameter(Mandatory=$true)][string]$ConfigPath,
  [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = "Stop"

function Test-IFSIdentifier([string]$name) {
  if ([string]::IsNullOrEmpty($name)) { return $false }
  for ($i = 0; $i -lt $name.Length; $i++) {
    $ch = [int][char]$name[$i]
    $letter = (($ch -ge 65 -and $ch -le 90) -or ($ch -ge 97 -and $ch -le 122))
    $digit = ($ch -ge 48 -and $ch -le 57)
    if ($i -eq 0) { if (-not $letter) { return $false } }
    elseif (-not ($letter -or $digit -or $ch -eq 95)) { return $false }
  }
  return $true
}

try {
  $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json

  # VIEW adini temizle ve ASCII karakterleriyle dogrula.
  $view = ([string]$cfg.view).Trim().Trim([char]0xFEFF).Trim()
  if (-not (Test-IFSIdentifier $view)) {
    throw "Gecersiz VIEW degeri: [$view]"
  }
  if (!$cfg.columns -or $cfg.columns.Count -lt 1) {
    throw "Sorgulanacak gercek alan yok."
  }
  foreach ($fieldName in $cfg.columns) {
    if (-not (Test-IFSIdentifier ([string]$fieldName))) {
      throw "Gecersiz alan adi: $fieldName"
    }
  }

  $cache = Join-Path $env:LOCALAPPDATA "Apps\2.0"
  $names = @(
    "Ifs.Fnd.Core.dll",
    "Ifs.Fnd.Buffer.dll",
    "Ifs.Fnd.Data.dll",
    "Ifs.Fnd.AccessProvider.dll",
    "Ifs.Fnd.AccessProvider.Interactive.dll"
  )
  $paths = @{}
  foreach ($dllName in $names) {
    $dll = Get-ChildItem -Path $cache -Filter $dllName -Recurse -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$dll) {
      throw "IFS istemci DLL bulunamadi: $dllName. IFS uygulamasini ayni Windows hesabiyla ac."
    }
    $paths[$dllName] = $dll.FullName
  }
  foreach ($dllName in $names) {
    Add-Type -Path $paths[$dllName]
  }

  $conn = New-Object Ifs.Fnd.AccessProvider.FndConnection
  $conn.ConnectionString = [string]$cfg.ifs_url
  $conn.InteractiveMode = $true
  $conn.AutoLogon = $false
  $conn.IgnoreCertificateValidation = $false

  # IFS AP keeps remembered credentials in its own user-scoped persistence store.
  # The C# host never receives or writes the password itself.
  $cred = [Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog]::GetStoredCredentials()
  if (!$cred) {
    $cred = New-Object Ifs.Fnd.AccessProvider.Interactive.FndLoginCredentials
  }
  $cred.ConnectTo = [string]$cfg.ifs_url
  $cred.SystemName = [string]$cfg.system_name
  $cred.Language = "tr-TR"
  $cred.ForcedShow = $false
  $remember = [bool]$cfg.remember_credentials
  $cred.ShowLoginDialog = -not $remember
  $cred.RememberPassword = $remember

  $dialog = New-Object Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog($conn)
  # When the user opted in and credentials are stored, let the IFS AP log on
  # without opening the dialog on each manual or scheduled fetch.
  $login = $dialog.ShowDialog($cred, $true, (-not $remember))
  if ([string]$login -ne "OK" -or !$conn.IsSessionInitialized) {
    throw "IFS oturumu baslatilamadi veya giris iptal edildi."
  }
  if ($remember) {
    [Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog]::StoreCredentials($cred)
  }

  $cols = ($cfg.columns | ForEach-Object { '"' + [string]$_ + '"' }) -join ", "
  $where = @()
  $binds = New-Object Ifs.Fnd.AccessProvider.PLSQL.FndBindVariableCollection
  $index = 0

  foreach ($filter in $cfg.filters) {
    $field = [string]$filter.name
    $value = [string]$filter.value
    if (-not (Test-IFSIdentifier $field)) {
      throw "Gecersiz filtre alani: $field"
    }
    if (!$value -or $value -eq "%") { continue }

    if ($value -match '^(\d{2}\.\d{2}\.\d{4})\.\.(\d{2}\.\d{2}\.\d{4})$') {
      $fromName = "P_FROM_$index"
      $toName = "P_TO_$index"
      $where += ('"' + $field + '" >= TO_DATE(:' + $fromName + ",'DD.MM.YYYY') AND " + '"' + $field + '" < TO_DATE(:' + $toName + ",'DD.MM.YYYY') + 1")
      $fromAttr = New-Object Ifs.Fnd.Data.FndAlphaAttribute([string]$Matches[1])
      $toAttr = New-Object Ifs.Fnd.Data.FndAlphaAttribute([string]$Matches[2])
      $binds.Add((New-Object Ifs.Fnd.AccessProvider.PLSQL.FndBindVariable($fromName, $fromAttr)))
      $binds.Add((New-Object Ifs.Fnd.AccessProvider.PLSQL.FndBindVariable($toName, $toAttr)))
    }
    else {
      $bindName = "P_VAL_$index"
      if ($value.Contains("%") -or $value.Contains("_")) {
        $where += ('UPPER("' + $field + '") LIKE UPPER(:' + $bindName + ')')
      }
      else {
        $where += ('"' + $field + '" = :' + $bindName)
      }
      $attr = New-Object Ifs.Fnd.Data.FndAlphaAttribute($value)
      $binds.Add((New-Object Ifs.Fnd.AccessProvider.PLSQL.FndBindVariable($bindName, $attr)))
    }
    $index++
  }

  $whereClause = ""
  if ($where.Count -gt 0) {
    $whereClause = " WHERE " + ($where -join " AND ")
  }

  # Count matching rows first, then let the desktop UI approve or cancel.
  $countCmd = New-Object Ifs.Fnd.AccessProvider.PLSQL.FndPLSQLSelectCommand($conn)
  $countCmd.CommandText = "SELECT COUNT(*) FROM $view$whereClause"
  $countCmd.MaxRows = 1
  foreach ($bind in $binds) {
    $countCmd.BindVariables.Add($bind)
  }
  $countTable = $countCmd.ExecuteReader()
  $rawCount = $countTable.Rows[0][0]
  # IFS COUNT(*) sonucunu Int64 yerine FndNumberAttribute olarak dondurebilir.
  # Attribute nesnesini invariant sayisal metne cevirip guvenli sekilde parse et.
  $countText = [Convert]::ToString($rawCount, [Globalization.CultureInfo]::InvariantCulture)
  $parsedCount = [decimal]0
  $validCount = [decimal]::TryParse(
    $countText,
    [Globalization.NumberStyles]::Number,
    [Globalization.CultureInfo]::InvariantCulture,
    [ref]$parsedCount
  )
  if (-not $validCount -or $parsedCount -lt 0 -or
      $parsedCount -gt [long]::MaxValue -or
      [decimal]::Truncate($parsedCount) -ne $parsedCount) {
    throw "IFS kayit sayisi sayisal deger olarak okunamadi: [$countText]"
  }
  $matchingCount = [long]$parsedCount
  [Console]::Out.WriteLine("MIRALAY_COUNT:$matchingCount")
  [Console]::Out.Flush()
  $approval = [Console]::In.ReadLine()
  if ($approval -ne "Y") {
    [Console]::Out.WriteLine("MIRALAY_CANCELLED")
    [Console]::Out.Flush()
    exit 0
  }

  if ($matchingCount -gt [int]::MaxValue) {
    throw "Eşleşen kayıt sayısı PowerShell/IFS satır sınırını aşıyor: $matchingCount"
  }
  # Buyuk sonuc setlerini tek seferde istemciye yuklemek yerine 1.000'er satirlik
  # sayfalar halinde al. Parcalar kullaniciya gosterilmez; tek sonuc olarak birlestirilir.
  $chunkSize = 1000
  $rows = [System.Collections.Generic.List[object]]::new()
  for ($offset = 0; $offset -lt $matchingCount; $offset += $chunkSize) {
    $upper = [Math]::Min([long]($offset + $chunkSize), $matchingCount)
    $sql = "SELECT * FROM (SELECT Q.*, ROWNUM AS MIRALAY_RN FROM (SELECT $cols FROM $view$whereClause) Q WHERE ROWNUM <= $upper) WHERE MIRALAY_RN > $offset"
    $countCmd.CommandText = $sql
    $countCmd.MaxRows = $chunkSize
    $table = $countCmd.ExecuteReader()
    foreach ($row in $table.Rows) {
      $item = [ordered]@{}
      for ($i = 0; $i -lt $table.Columns.Count; $i++) {
        $columnName = [string]$table.Columns[$i].Name
        if ($columnName -ieq "MIRALAY_RN") { continue }
        $value = $row[$i]
        $item[$columnName] = if ($null -eq $value) { $null } else { $value.ToString() }
      }
      $rows.Add([pscustomobject]$item)
    }
    [Console]::Out.WriteLine("MIRALAY_PROGRESS:$($rows.Count):$matchingCount")
    [Console]::Out.Flush()
    if ($table.Rows.Count -eq 0) { break }
  }

  [pscustomobject]@{
    rows = $rows
    view = $view
    ifs_user = [string]$dialog.UserId
    fetched_at = (Get-Date).ToString("o")
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
  exit 0
}
catch {
  [Console]::Error.WriteLine(($_ | Out-String))
  exit 1
}
