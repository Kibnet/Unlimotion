[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$English,

    [Parameter(Mandatory)]
    [string]$Russian,

    [Parameter(Mandatory)]
    [string]$SupportMatrix,

    [string]$Manifest,

    [switch]$RunNegativeFixtures
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Manifest)) {
    $Manifest = Join-Path $repoRoot 'distribution/release-assets.json'
}

function Resolve-File {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$Label)
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved -or -not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw "$Label does not exist: $Path"
    }
    return $resolved.Path
}

function Read-Json {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$Label)
    try { return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 }
    catch { throw "$Label is not valid JSON: $($_.Exception.Message)" }
}

function Get-ContractRows {
    param([Parameter(Mandatory)] [string]$Content)
    return @($Content -split "`n" | ForEach-Object { $_.TrimEnd("`r") } | Where-Object { $_ -match '^\|.*\|$' })
}

function Get-ClaimMarker {
    param([Parameter(Mandatory)] [object]$Claim)
    return "<!-- distribution-claim:$($Claim.id);platform=$($Claim.platform);architecture=$($Claim.architecture);distribution=$($Claim.distribution) -->"
}

function Assert-Rejected {
    param([Parameter(Mandatory)] [scriptblock]$Action, [Parameter(Mandatory)] [string]$Label)
    try {
        & $Action
    }
    catch {
        return
    }
    throw "Negative README fixture '$Label' was accepted."
}

function Remove-FirstLiteralToken {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [Parameter(Mandatory)] [string]$Token
    )

    $index = $Content.IndexOf($Token, [StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Cannot build negative README fixture because token '$Token' is absent."
    }
    return $Content.Remove($index, $Token.Length)
}

function ConvertTo-PackageArray {
    param([Parameter(Mandatory)] [string]$Value)
    return @($Value.Split([char[]]@(' ', "`t"), [StringSplitOptions]::RemoveEmptyEntries))
}

function Assert-ExactPackageSequence {
    param(
        [Parameter(Mandatory)] [string[]]$Actual,
        [Parameter(Mandatory)] [object[]]$Expected,
        [Parameter(Mandatory)] [string]$Label
    )

    $expectedStrings = @($Expected | ForEach-Object { [string]$_ })
    if ($Actual.Count -ne $expectedStrings.Count) {
        throw "$Label package count differs from the distribution manifest: README=$($Actual.Count), manifest=$($expectedStrings.Count)."
    }
    for ($index = 0; $index -lt $expectedStrings.Count; $index++) {
        if ($Actual[$index] -cne $expectedStrings[$index]) {
            throw "$Label package sequence differs from the distribution manifest at index ${index}: README='$($Actual[$index])', manifest='$($expectedStrings[$index])'."
        }
    }
}

function Get-ExtractAndRunPackages {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [Parameter(Mandatory)] [ValidateSet('12', '13')] [string]$DebianVersion,
        [Parameter(Mandatory)] [string]$Language
    )

    $pattern = "(?m)^# Debian $DebianVersion\r?\n" +
        'sudo apt install (?<packages>[^\r\n]+)\r?$'
    $matches = [regex]::Matches($Content, $pattern)
    if ($matches.Count -ne 1) {
        throw "$Language README must contain exactly one '# Debian $DebianVersion' extract-and-run apt command; found $($matches.Count)."
    }
    return @(ConvertTo-PackageArray -Value $matches[0].Groups['packages'].Value)
}

function Get-DirectFusePackages {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [Parameter(Mandatory)] [ValidateSet('English', 'Russian')] [string]$Language
    )

    $pattern = if ($Language -ceq 'English') {
        '(?m)^- AppImage direct launch additionally needs[^\r\n]*On Debian 12 install `(?<debian12>[^`\r\n]+)`; on Debian 13 install `(?<debian13>[^`\r\n]+)`, then run:$'
    }
    else {
        '(?m)^- Для прямого запуска AppImage[^\r\n]*на Debian 12 установите `(?<debian12>[^`\r\n]+)`, а на Debian 13 — `(?<debian13>[^`\r\n]+)`, затем выполните:$'
    }
    $matches = [regex]::Matches($Content, $pattern)
    if ($matches.Count -ne 1) {
        throw "$Language README must contain exactly one Debian 12/13 direct-FUSE prerequisite statement; found $($matches.Count)."
    }
    return [pscustomobject]@{
        Debian12 = @(ConvertTo-PackageArray -Value $matches[0].Groups['debian12'].Value)
        Debian13 = @(ConvertTo-PackageArray -Value $matches[0].Groups['debian13'].Value)
    }
}

function Assert-RelativeLinks {
    param([Parameter(Mandatory)] [string]$Content, [Parameter(Mandatory)] [string]$ReadmePath)
    $directory = Split-Path -Parent $ReadmePath
    $matches = [regex]::Matches($Content, '!?(?:\[[^\]]*\])\(([^)]+)\)')
    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()
        if ($target.StartsWith('<') -and $target.EndsWith('>')) { $target = $target.Substring(1, $target.Length - 2) }
        if ($target -match '^(?:https?://|mailto:|#)') { continue }
        $pathPart = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        $decoded = [Uri]::UnescapeDataString($pathPart)
        $candidate = Join-Path $directory $decoded
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "README link target does not exist: '$target' in $ReadmePath"
        }
    }
}

function Assert-Readme {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$Matrix,
        [Parameter(Mandatory)] [object]$ManifestObject,
        [Parameter(Mandatory)] [ValidateSet('English', 'Russian')] [string]$Language
    )

    $rows = Get-ContractRows -Content $Content
    $releaseTag = [string]$Matrix.release.rawTag
    $sourceSha = [string]$Matrix.release.sourceSha
    if (-not $Content.Contains("``$releaseTag``", [StringComparison]::Ordinal)) {
        throw "$Language README does not name support snapshot release '$releaseTag'."
    }
    if (-not $Content.Contains("``$sourceSha``", [StringComparison]::Ordinal)) {
        throw "$Language README does not bind the support snapshot to source SHA '$sourceSha'."
    }
    if (-not $Content.Contains('[distribution/support-matrix.json](distribution/support-matrix.json)', [StringComparison]::Ordinal)) {
        throw "$Language README does not link to the durable support matrix."
    }
    if (-not $Content.Contains('`candidateEvidenceAccepted: false`', [StringComparison]::Ordinal)) {
        throw "$Language README does not state that candidate evidence cannot promote published support."
    }
    foreach ($level in @('present', 'metadataVerified', 'launchVerified', 'productionReady')) {
        if (-not $Content.Contains("``$level``", [StringComparison]::Ordinal)) {
            throw "$Language README does not define evidence level '$level'."
        }
    }

    $overclaimPattern = if ($Language -ceq 'English') {
        '(?im)\b(?:fully supports?|supports? all|works? on all|all (?:Windows|macOS|Android) versions?)\b'
    }
    else {
        '(?im)(?:полностью поддерж(?:ивает|ивается)|поддерж(?:ивает|ивается) все версии|работает на всех устройствах|совместим[а-яё]* со всеми версиями)'
    }
    if ($Content -match $overclaimPattern) {
        throw "$Language README contains a broad compatibility overclaim: '$($Matches[0])'."
    }

    $claimRows = @($rows | Where-Object { $_.Contains('<!-- distribution-claim:', [StringComparison]::Ordinal) })
    if ($claimRows.Count -ne @($Matrix.claims).Count) {
        throw "$Language README must contain exactly one marked row for every support claim; found $($claimRows.Count)."
    }
    foreach ($claimRow in $claimRows) {
        if ([regex]::Matches($claimRow, '<!-- distribution-claim:').Count -ne 1) {
            throw "$Language README support row contains more than one claim marker."
        }
    }

    foreach ($claim in $Matrix.claims) {
        $claimMarker = Get-ClaimMarker -Claim $claim
        $matchingRows = @($claimRows | Where-Object { $_.Contains($claimMarker, [StringComparison]::Ordinal) })
        if ($matchingRows.Count -ne 1) {
            throw "$Language README must contain exactly one table row for support claim '$($claim.id)'; found $($matchingRows.Count)."
        }
        $row = $matchingRows[0]
        if (-not $row.Contains("``$($claim.evidenceLevel)``", [StringComparison]::Ordinal) -or
            -not $row.Contains("``$($claim.publicStatus)``", [StringComparison]::Ordinal)) {
            throw "$Language README row '$($claim.id)' does not carry exact evidence/public status tokens."
        }
        foreach ($caveatId in @($claim.readmeCaveatIds)) {
            $caveatMarker = "<!-- distribution-caveat:$caveatId -->"
            if (-not $row.Contains($caveatMarker, [StringComparison]::Ordinal)) {
                throw "$Language README row '$($claim.id)' is missing caveat marker '$caveatId'."
            }
        }
        foreach ($asset in $claim.assets) {
            if (-not $row.Contains("``$($asset.name)``", [StringComparison]::Ordinal)) {
                throw "$Language README row '$($claim.id)' does not name exact asset '$($asset.name)'."
            }
            if ([string]$asset.sha256 -cnotmatch '^[0-9a-f]{64}$') {
                throw "Support claim '$($claim.id)' has an invalid SHA-256 for '$($asset.name)'."
            }
            $manifestMatches = @($ManifestObject.assets | Where-Object id -CEQ ([string]$asset.assetId))
            if ($manifestMatches.Count -ne 1) {
                throw "Support claim '$($claim.id)' asset '$($asset.assetId)' is not unique in the manifest."
            }
            $expectedName = ([string]$manifestMatches[0].filenameTemplate).Replace('{normalizedVersion}', [string]$Matrix.release.normalizedVersion)
            if ($expectedName -cne [string]$asset.name) {
                throw "Support claim '$($claim.id)' name '$($asset.name)' does not match manifest '$expectedName'."
            }
        }
    }

    $runtimePrerequisites = $ManifestObject.linuxRuntimePrerequisites
    foreach ($debianVersion in @('12', '13')) {
        $propertyName = "debian$debianVersion"
        $actualPackages = @(Get-ExtractAndRunPackages -Content $Content -DebianVersion $debianVersion -Language $Language)
        $expectedPackages = @($runtimePrerequisites.appImageExtractAndRun.$propertyName)
        Assert-ExactPackageSequence -Actual $actualPackages -Expected $expectedPackages -Label "$Language README Debian $debianVersion AppImage extract-and-run"
    }

    $directFusePackages = Get-DirectFusePackages -Content $Content -Language $Language
    Assert-ExactPackageSequence -Actual @($directFusePackages.Debian12) -Expected @($runtimePrerequisites.directFuseAdditional.debian12) -Label "$Language README Debian 12 direct-FUSE"
    Assert-ExactPackageSequence -Actual @($directFusePackages.Debian13) -Expected @($runtimePrerequisites.directFuseAdditional.debian13) -Label "$Language README Debian 13 direct-FUSE"

    foreach ($token in @('APPIMAGE_EXTRACT_AND_RUN=1', '`directFUSE: notVerified`')) {
        if (-not $Content.Contains($token, [StringComparison]::Ordinal)) {
            throw "$Language README is missing AppImage caveat token '$token'."
        }
    }
    if ($Content.Contains('cd Unlimotion', [StringComparison]::Ordinal) -or
        $Content.Contains('Set-Location Unlimotion', [StringComparison]::Ordinal)) {
        throw "$Language README still requires changing into the repository before using root entry points."
    }
    foreach ($script in @('run.windows.cmd', 'run.linux.sh', 'run.macos.sh')) {
        if (-not $Content.Contains($script, [StringComparison]::Ordinal)) {
            throw "$Language README does not name source entry point '$script'."
        }
    }
    Assert-RelativeLinks -Content $Content -ReadmePath $Path
}

$englishPath = Resolve-File -Path $English -Label 'English README'
$russianPath = Resolve-File -Path $Russian -Label 'Russian README'
$supportPath = Resolve-File -Path $SupportMatrix -Label 'Support matrix'
$manifestPath = Resolve-File -Path $Manifest -Label 'Distribution manifest'
$englishContent = Get-Content -LiteralPath $englishPath -Raw -Encoding utf8
$russianContent = Get-Content -LiteralPath $russianPath -Raw -Encoding utf8
$matrixObject = Read-Json -Path $supportPath -Label 'Support matrix'
$manifestObject = Read-Json -Path $manifestPath -Label 'Distribution manifest'

if ($matrixObject.candidateEvidenceAccepted -ne $false) {
    throw 'Support matrix must reject candidate evidence for public support.'
}
if ($matrixObject.release.rawTag -cnotmatch '^v?[0-9]+\.[0-9]+\.[0-9]+$' -or
    $matrixObject.release.sourceSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Support matrix release identity is invalid.'
}
if (@($matrixObject.claims).Count -eq 0) { throw 'Support matrix contains no public claims.' }

Assert-Readme -Content $englishContent -Path $englishPath -Matrix $matrixObject -ManifestObject $manifestObject -Language English
Assert-Readme -Content $russianContent -Path $russianPath -Matrix $matrixObject -ManifestObject $manifestObject -Language Russian

$englishRows = Get-ContractRows -Content $englishContent
$russianRows = Get-ContractRows -Content $russianContent
foreach ($claim in $matrixObject.claims) {
    $claimMarker = Get-ClaimMarker -Claim $claim
    $englishRow = @($englishRows | Where-Object { $_.Contains($claimMarker, [StringComparison]::Ordinal) })[0]
    $russianRow = @($russianRows | Where-Object { $_.Contains($claimMarker, [StringComparison]::Ordinal) })[0]
    foreach ($token in @([string]$claim.evidenceLevel, [string]$claim.publicStatus) + @($claim.assets.name) + @($claim.readmeCaveatIds)) {
        if (-not $englishRow.Contains("``$token``", [StringComparison]::Ordinal) -or
            -not $russianRow.Contains("``$token``", [StringComparison]::Ordinal)) {
            if ($token -in @($claim.readmeCaveatIds) -and
                $englishRow.Contains("<!-- distribution-caveat:$token -->", [StringComparison]::Ordinal) -and
                $russianRow.Contains("<!-- distribution-caveat:$token -->", [StringComparison]::Ordinal)) {
                continue
            }
            throw "README parity failed for claim '$($claim.id)' token '$token'."
        }
    }
}

if ($RunNegativeFixtures) {
    $firstClaim = @($matrixObject.claims)[0]
    $secondClaim = @($matrixObject.claims)[1]
    $firstMarker = Get-ClaimMarker -Claim $firstClaim
    $secondMarker = Get-ClaimMarker -Claim $secondClaim
    $duplicateClaim = $englishContent.Replace($firstMarker, $secondMarker)
    Assert-Rejected -Label 'duplicate-claim-marker' -Action {
        Assert-Readme -Content $duplicateClaim -Path $englishPath -Matrix $matrixObject -ManifestObject $manifestObject -Language English
    }

    $firstCaveat = [string]@($firstClaim.readmeCaveatIds)[0]
    $missingCaveat = $englishContent.Replace("<!-- distribution-caveat:$firstCaveat -->", '')
    Assert-Rejected -Label 'missing-caveat-marker' -Action {
        Assert-Readme -Content $missingCaveat -Path $englishPath -Matrix $matrixObject -ManifestObject $manifestObject -Language English
    }

    $overclaim = "$englishContent`nFully supports all Windows versions."
    Assert-Rejected -Label 'broad-overclaim' -Action {
        Assert-Readme -Content $overclaim -Path $englishPath -Matrix $matrixObject -ManifestObject $manifestObject -Language English
    }

    $debian12Packages = @($manifestObject.linuxRuntimePrerequisites.appImageExtractAndRun.debian12 | ForEach-Object { [string]$_ })
    $debian13Packages = @($manifestObject.linuxRuntimePrerequisites.appImageExtractAndRun.debian13 | ForEach-Object { [string]$_ })
    $extractToken = @($debian12Packages | Where-Object { $_ -cnotin $debian13Packages } | Select-Object -First 1)
    if ($extractToken.Count -eq 0) { $extractToken = @($debian12Packages | Select-Object -First 1) }
    $missingExtractToken = Remove-FirstLiteralToken -Content $englishContent -Token $extractToken[0]
    Assert-Rejected -Label 'missing-appimage-extract-runtime-token' -Action {
        Assert-Readme -Content $missingExtractToken -Path $englishPath -Matrix $matrixObject -ManifestObject $manifestObject -Language English
    }

    $directFuseToken = [string]@($manifestObject.linuxRuntimePrerequisites.directFuseAdditional.debian12)[0]
    $missingDirectFuseToken = Remove-FirstLiteralToken -Content $russianContent -Token $directFuseToken
    Assert-Rejected -Label 'missing-appimage-direct-fuse-token' -Action {
        Assert-Readme -Content $missingDirectFuseToken -Path $russianPath -Matrix $matrixObject -ManifestObject $manifestObject -Language Russian
    }
}

Write-Output "README distribution contract passed for $(@($matrixObject.claims).Count) paired support claims."
