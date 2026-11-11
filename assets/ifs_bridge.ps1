param(
  [Parameter(Mandatory=$true)][string]$ConfigPath,
  [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = "Stop"

# Read-only diagnostic log shared with the separate Miralay IFS Tanilama app.
$diagDir = Join-Path $env:LOCALAPPDATA "MiralayIFS\\Diagnostics"
try { New-Item -ItemType Directory -Path $diagDir -Force | Out-Null } catch { }
$diagFile = Join-Path $diagDir ("bridge-" + (Get-Date -Format "yyyyMMdd") + ".jsonl")
function Write-IFSLog([string]$eventName, [object]$data) {
  try {
    $entry = [ordered]@{ time = (Get-Date).ToString("o"); event = $eventName; data = $data }
    ($entry | ConvertTo-Json -Depth 6 -Compress) | Add-Content -LiteralPath $diagFile -Encoding UTF8
  } catch { }
}

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
  Write-IFSLog "bridge_started" @{ view = [string]$cfg.view; columns = @($cfg.columns).Count; filter_fields = @($cfg.filters | ForEach-Object { [string]$_.name }); remember_credentials = [bool]$cfg.remember_credentials }

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

  # IFS .NET AP credentials are persisted in Windows IsolatedStorage for
  # the current user. Do not assume a credentials object means a password
  # was actually restored: check its username/password before suppressing UI.
  $cred = [Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog]::GetStoredCredentials()
  if (!$cred) {
    $cred = New-Object Ifs.Fnd.AccessProvider.Interactive.FndLoginCredentials
  }

  $remember = [bool]$cfg.remember_credentials
  $storedUser = [string]$cred.UserName
  $storedPassword = [string]$cred.Password
  $hasStoredCredentials = (-not [string]::IsNullOrWhiteSpace($storedUser)) -and
                          (-not [string]::IsNullOrWhiteSpace($storedPassword))
  Write-IFSLog "credentials_checked" @{ stored_object = [bool]$cred; has_username = (-not [string]::IsNullOrWhiteSpace($storedUser)); has_password = (-not [string]::IsNullOrWhiteSpace($storedPassword)); remember_enabled = $remember }

  $cred.ConnectTo = [string]$cfg.ifs_url
  $cred.SystemName = [string]$cfg.system_name
  $cred.Language = "tr-TR"
  $cred.ForcedShow = $false
  $cred.RememberPassword = $remember

  # Show the IFS login window if there is no complete saved credential pair.
  # This prevents an empty dialog from appearing on every data fetch while
  # still allowing the first login (or a rejected/expired password) to prompt.
  $cred.ShowLoginDialog = (-not $remember) -or (-not $hasStoredCredentials)

  if ($remember -and $cred.RememberPasswordDisabled) {
    [Console]::Error.WriteLine("IFS istemci ayarlarinda parolayi hatirla ozelligi devre disi. IFS yoneticisinin disableRememberPassword ayarini kontrol etmesi gerekiyor.")
  }

  $dialog = New-Object Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog($conn)
  $login = $dialog.ShowDialog($cred, $true)
  Write-IFSLog "login_result" @{ result = [string]$login; session_initialized = [bool]$conn.IsSessionInitialized; user_id_present = (-not [string]::IsNullOrWhiteSpace([string]$dialog.UserId)) }
  if ([string]$login -ne "OK" -or !$conn.IsSessionInitialized) {
    throw "IFS oturumu baslatilamadi veya giris iptal edildi."
  }

  if ($remember) {
    # Persist the credentials returned by the successful login dialog.
    [Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog]::StoreCredentials($cred)
    # Verify persistence immediately without exposing the password.
    $verifyCred = [Ifs.Fnd.AccessProvider.Interactive.FndLoginDialog]::GetStoredCredentials()
    if (!$verifyCred -or [string]::IsNullOrWhiteSpace([string]$verifyCred.UserName) -or
        [string]::IsNullOrWhiteSpace([string]$verifyCred.Password)) {
      [Console]::Error.WriteLine("IFS girisi basarili ancak kayitli kullanici/parola tekrar okunamadi. IFS istemci ayarlari veya Windows IsolatedStorage kaydi engelliyor olabilir.")
    }
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
  Write-IFSLog "count_query_started" @{ view = $view; selected_columns = @($cfg.columns); filter_fields = @($cfg.filters | ForEach-Object { [string]$_.name }); sql = $countCmd.CommandText }
  $queryWatch = [Diagnostics.Stopwatch]::StartNew()
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
  $queryWatch.Stop()
  Write-IFSLog "count_query_completed" @{ matching_rows = $matchingCount; elapsed_ms = $queryWatch.ElapsedMilliseconds }
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
  [Console]::Out.WriteLine("MIRALAY_PROGRESS:0:$matchingCount")
  [Console]::Out.Flush()
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

  Write-IFSLog "data_query_completed" @{ rows = $rows.Count; expected_rows = $matchingCount }
  [pscustomobject]@{
    rows = $rows
    view = $view
    ifs_user = [string]$dialog.UserId
    fetched_at = (Get-Date).ToString("o")
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
  exit 0
}
catch {
  Write-IFSLog "bridge_error" @{ message = $_.Exception.Message; category = [string]$_.CategoryInfo }
  [Console]::Error.WriteLine(($_ | Out-String))
  exit 1
}
