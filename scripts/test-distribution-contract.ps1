param(
    [ValidateSet('All', 'BlobParity', 'BlobParityAggregate', 'BuildIsolation', 'InventorySupport', 'IdentityTriggers', 'VelopackFeeds', 'Retry', 'Evidence', 'WorkflowSecurity')]
    [string]$Area = 'All',

    [string]$Manifest = (Join-Path $PSScriptRoot '..\distribution\release-assets.json'),

    [string]$ManifestSchema = (Join-Path $PSScriptRoot '..\distribution\release-assets.schema.json'),

    [string]$Fixture = (Join-Path $PSScriptRoot '..\distribution\fixtures\release-1.27.0.json'),

    [string]$SupportMatrix = (Join-Path $PSScriptRoot '..\distribution\support-matrix.json'),

    [string]$SupportMatrixSchema = (Join-Path $PSScriptRoot '..\distribution\support-matrix.schema.json'),

    [string]$Resolver = (Join-Path $PSScriptRoot 'Resolve-ReleaseIdentity.ps1'),

    [string]$EvidenceSchema = (Join-Path $PSScriptRoot '..\distribution\evidence.schema.json'),

    [string]$ArtifactValidator = (Join-Path $PSScriptRoot 'Test-DistributionArtifact.ps1'),

    [string]$Workflow = (Join-Path $PSScriptRoot '..\.github\workflows\distribution-validation.yml'),

    [string]$BlobParityEvidencePath,

    [string[]]$BlobParityInputPath,

    [string]$SourceSha,

    [string]$WorkflowSha,

    [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:checks = [System.Collections.Generic.List[string]]::new()
$script:negativeFixtureCount = 0
$script:pythonExecutable = $null
$BlobParityInputPath = @(
    $BlobParityInputPath |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if ($resolved.Provider.Name -ne 'FileSystem' -or -not [System.IO.File]::Exists($resolved.Path)) {
        throw "$DisplayName must be an existing file: $Path"
    }
    return $resolved.Path
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 -ErrorAction Stop
    }
    catch {
        throw "$DisplayName is not valid JSON: $($_.Exception.Message)"
    }
}

function Copy-JsonObject {
    param([Parameter(Mandatory = $true)][object]$InputObject)

    return $InputObject | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json -Depth 100
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Add-Check {
    param([Parameter(Mandatory = $true)][string]$Name)

    $script:checks.Add($Name)
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$MessagePattern
    )

    $failedAsExpected = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $failedAsExpected = $true
        if (-not [string]::IsNullOrWhiteSpace($MessagePattern) -and $_.Exception.Message -cnotmatch $MessagePattern) {
            throw "Negative fixture '$Name' failed for an unexpected reason: $($_.Exception.Message)"
        }
    }

    if (-not $failedAsExpected) {
        throw "Negative fixture '$Name' unexpectedly passed."
    }

    $script:negativeFixtureCount++
    Add-Check -Name "negative:$Name"
}

function Assert-JsonFileSchema {
    param(
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$SchemaPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $isValid = Test-Json -LiteralPath $JsonPath -SchemaFile $SchemaPath -ErrorAction Stop
    if (-not $isValid) {
        throw "$Name does not satisfy schema '$SchemaPath'."
    }
    Add-Check -Name "schema:$Name"
}

function Assert-JsonObjectSchema {
    param(
        [Parameter(Mandatory = $true)][object]$Document,
        [Parameter(Mandatory = $true)][string]$SchemaPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $json = $Document | ConvertTo-Json -Depth 100 -Compress
    try {
        $isValid = Test-Json -Json $json -SchemaFile $SchemaPath -ErrorAction Stop
    }
    catch {
        throw "$Name does not satisfy schema '$SchemaPath': $($_.Exception.Message)"
    }
    if (-not $isValid) {
        throw "$Name does not satisfy schema '$SchemaPath'."
    }
}

function Get-LowerFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Utf8Bytes {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Text.UTF8Encoding]::new($false).GetBytes($Value)
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Invoke-GitBytes {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $output = [System.IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw "Unable to start Git for: $($Arguments -join ' ')"
        }
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($output)
        $process.WaitForExit()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Git command failed with exit code $($process.ExitCode): git $($Arguments -join ' ')`n$standardError"
        }
        return ,$output.ToArray()
    }
    finally {
        $output.Dispose()
        $process.Dispose()
    }
}

function ConvertFrom-NulSeparatedUtf8 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    if ($Bytes.Count -eq 0) {
        return @()
    }
    $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    Assert-Condition ($text[$text.Length - 1] -eq [char]0) 'NUL-delimited Git output is not terminated.'
    return @($text.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries))
}

function Get-TrackedDistributionJsonPaths {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $pathspecs = @(
        ':(glob)distribution/*.json',
        ':(glob)distribution/fixtures/*.json'
    )
    [byte[]]$rawPaths = Invoke-GitBytes -RepositoryRoot $RepositoryRoot -Arguments (@('ls-files', '-z', '--') + $pathspecs)
    [string[]]$paths = @(ConvertFrom-NulSeparatedUtf8 -Bytes $rawPaths)
    Assert-Condition ($paths.Count -gt 0) 'No tracked distribution JSON files match the approved patterns.'

    $caseInsensitivePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) {
        Assert-Condition (
            $path -cmatch '^distribution/(?:[^/]+|fixtures/[^/]+)\.json$') `
            "Tracked path '$path' escaped the approved distribution JSON patterns."
        Assert-Condition ($caseInsensitivePaths.Add($path)) "Tracked distribution JSON path collision: '$path'."
    }
    [Array]::Sort($paths, [System.StringComparer]::Ordinal)
    return $paths
}

function Get-GitPathAttributes {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$GitPath,
        [string]$Source
    )

    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add('check-attr')
    if (-not [string]::IsNullOrWhiteSpace($Source)) {
        $arguments.Add("--source=$Source")
    }
    foreach ($argument in @('-z', 'text', 'eol', '--', $GitPath)) {
        $arguments.Add($argument)
    }
    [byte[]]$rawAttributes = Invoke-GitBytes -RepositoryRoot $RepositoryRoot `
        -Arguments @($arguments)
    $tokens = @(ConvertFrom-NulSeparatedUtf8 -Bytes $rawAttributes)
    Assert-Condition ($tokens.Count -eq 6) "Git returned an unexpected attribute record for '$GitPath'."

    $attributes = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    for ($index = 0; $index -lt $tokens.Count; $index += 3) {
        Assert-Condition ($tokens[$index] -ceq $GitPath) "Git attribute record path differs from '$GitPath'."
        Assert-Condition (-not $attributes.ContainsKey($tokens[$index + 1])) "Duplicate Git attribute '$($tokens[$index + 1])' for '$GitPath'."
        $attributes.Add($tokens[$index + 1], $tokens[$index + 2])
    }
    Assert-Condition ($attributes.ContainsKey('text') -and $attributes.ContainsKey('eol')) "Git attribute record for '$GitPath' is incomplete."
    return [pscustomobject][ordered]@{
        text = $attributes['text']
        eol = $attributes['eol']
    }
}

function Get-SourceBoundGitPathAttributes {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$GitPath
    )

    $headAttributes = Get-GitPathAttributes -RepositoryRoot $RepositoryRoot -GitPath $GitPath -Source 'HEAD'
    $effectiveAttributes = Get-GitPathAttributes -RepositoryRoot $RepositoryRoot -GitPath $GitPath
    Assert-Condition (
        $headAttributes.text -ceq 'set' -and $headAttributes.eol -ceq 'lf') `
        "Tracked distribution JSON '$GitPath' committed HEAD attributes must be text=set/eol=lf; actual text=$($headAttributes.text), eol=$($headAttributes.eol)."
    Assert-Condition (
        $effectiveAttributes.text -ceq 'set' -and $effectiveAttributes.eol -ceq 'lf') `
        "Tracked distribution JSON '$GitPath' effective worktree attributes must be text=set/eol=lf; actual text=$($effectiveAttributes.text), eol=$($effectiveAttributes.eol)."
    Assert-Condition (
        $effectiveAttributes.text -ceq $headAttributes.text -and
        $effectiveAttributes.eol -ceq $headAttributes.eol) `
        "Tracked distribution JSON '$GitPath' effective worktree attributes differ from committed HEAD attributes."
    return $headAttributes
}

function Resolve-RepositoryWorktreePath {
    param(
        [Parameter(Mandatory = $true)][string]$WorktreeRoot,
        [Parameter(Mandatory = $true)][string]$GitPath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($WorktreeRoot).TrimEnd([char[]]'\/')
    $relativePath = $GitPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($resolvedRoot, $relativePath))
    $comparison = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    $rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition ($resolvedPath.StartsWith($rootPrefix, $comparison)) "Tracked path '$GitPath' escaped the worktree root."
    return $resolvedPath
}

function Test-ContainsCarriageReturn {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    foreach ($value in $Bytes) {
        if ($value -eq 0x0D) {
            return $true
        }
    }
    return $false
}

function Get-CurrentRunnerOs {
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return 'windows'
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Linux)) {
        return 'linux'
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return 'macos'
    }
    throw 'Blob parity checker does not recognize the current operating system.'
}

function Resolve-BlobParityIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$ExpectedSourceSha,
        [string]$ExpectedWorkflowSha
    )

    [byte[]]$rawHead = Invoke-GitBytes -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse', 'HEAD')
    $headSha = [System.Text.UTF8Encoding]::new($false, $true).GetString($rawHead).Trim()
    Assert-Condition ($headSha -cmatch '^[0-9a-f]{40}$') "Current HEAD '$headSha' is not a lowercase SHA-1 value."

    $source = if ([string]::IsNullOrWhiteSpace($ExpectedSourceSha)) { $headSha } else { $ExpectedSourceSha }
    $workflow = if ([string]::IsNullOrWhiteSpace($ExpectedWorkflowSha)) { $headSha } else { $ExpectedWorkflowSha }
    Assert-Condition ($source -cmatch '^[0-9a-f]{40}$') "Blob parity source SHA '$source' is not a lowercase SHA-1 value."
    Assert-Condition ($workflow -cmatch '^[0-9a-f]{40}$') "Blob parity workflow SHA '$workflow' is not a lowercase SHA-1 value."
    Assert-Condition ($source -ceq $headSha) "Blob parity source SHA '$source' does not match current HEAD '$headSha'."
    return [pscustomobject][ordered]@{
        sourceSha = $source
        workflowSha = $workflow
    }
}

function New-DistributionBlobParityEntry {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$WorktreeRoot,
        [Parameter(Mandatory = $true)][string]$GitPath
    )

    $attributes = Get-SourceBoundGitPathAttributes -RepositoryRoot $RepositoryRoot -GitPath $GitPath

    $worktreePath = Resolve-RepositoryWorktreePath -WorktreeRoot $WorktreeRoot -GitPath $GitPath
    Assert-Condition ([System.IO.File]::Exists($worktreePath)) "Tracked distribution JSON '$GitPath' is missing from the physical worktree."
    [byte[]]$worktreeBytes = [System.IO.File]::ReadAllBytes($worktreePath)
    [byte[]]$blobBytes = Invoke-GitBytes -RepositoryRoot $RepositoryRoot -Arguments @('cat-file', 'blob', "HEAD:$GitPath")

    $worktreeHasCarriageReturn = Test-ContainsCarriageReturn -Bytes $worktreeBytes
    $blobHasCarriageReturn = Test-ContainsCarriageReturn -Bytes $blobBytes
    Assert-Condition (-not $worktreeHasCarriageReturn) "Tracked distribution JSON '$GitPath' worktree contains raw 0x0D; LF-only bytes are required."
    Assert-Condition (-not $blobHasCarriageReturn) "Tracked distribution JSON '$GitPath' committed blob contains raw 0x0D; LF-only bytes are required."

    $worktreeSha256 = Get-BytesSha256 -Bytes $worktreeBytes
    $blobSha256 = Get-BytesSha256 -Bytes $blobBytes
    Assert-Condition ($worktreeBytes.Count -eq $blobBytes.Count) "Tracked distribution JSON '$GitPath' worktree/blob raw byte sizes differ."
    Assert-Condition ($worktreeSha256 -ceq $blobSha256) "Tracked distribution JSON '$GitPath' worktree SHA-256 differs from the raw HEAD blob."

    return [pscustomobject][ordered]@{
        path = $GitPath
        attributes = [pscustomobject][ordered]@{
            text = [string]$attributes.text
            eol = [string]$attributes.eol
        }
        worktreeBytes = [long]$worktreeBytes.Count
        blobBytes = [long]$blobBytes.Count
        worktreeSha256 = $worktreeSha256
        blobSha256 = $blobSha256
        worktreeHasCarriageReturn = $worktreeHasCarriageReturn
        blobHasCarriageReturn = $blobHasCarriageReturn
        sha256Match = $true
        lfVerdict = 'pass'
    }
}

function New-DistributionBlobParityReport {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkflowSha
    )

    $paths = @(Get-TrackedDistributionJsonPaths -RepositoryRoot $RepositoryRoot)
    $files = @(
        foreach ($path in $paths) {
            New-DistributionBlobParityEntry -RepositoryRoot $RepositoryRoot -WorktreeRoot $RepositoryRoot -GitPath $path
        }
    )
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        kind = 'distribution-blob-parity'
        checkerVersion = 1
        status = 'pass'
        os = Get-CurrentRunnerOs
        sourceSha = $ExpectedSourceSha
        workflowSha = $ExpectedWorkflowSha
        patterns = @('distribution/*.json', 'distribution/fixtures/*.json')
        fileCount = $files.Count
        files = $files
        productionReady = $false
    }
}

function Test-IsIntegerValue {
    param([AllowNull()][object]$Value)

    return (
        $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [short] -or $Value -is [ushort] -or
        $Value -is [int] -or $Value -is [uint] -or
        $Value -is [long] -or $Value -is [ulong])
}

function Assert-ExactPropertySet {
    param(
        [Parameter(Mandatory = $true)][object]$Document,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    [string[]]$actualNames = @($Document.PSObject.Properties.Name)
    [string[]]$expectedNames = @($Expected)
    [Array]::Sort($actualNames, [System.StringComparer]::Ordinal)
    [Array]::Sort($expectedNames, [System.StringComparer]::Ordinal)
    Assert-Condition (
        (($actualNames | ConvertTo-Json -Compress) -ceq ($expectedNames | ConvertTo-Json -Compress))) `
        "$Label property set differs; actual=$($actualNames -join ',')."
}

function Assert-DirectBlobParityReport {
    param(
        [Parameter(Mandatory = $true)][object]$Document,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$ExpectedPaths,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkflowSha
    )

    Assert-ExactPropertySet -Document $Document `
        -Expected @('schemaVersion', 'kind', 'checkerVersion', 'status', 'os', 'sourceSha', 'workflowSha', 'patterns', 'fileCount', 'files', 'productionReady') `
        -Label $Label
    Assert-Condition ((Test-IsIntegerValue $Document.schemaVersion) -and [long]$Document.schemaVersion -eq 1) "$Label schemaVersion must equal integer 1."
    Assert-Condition ([string]$Document.kind -ceq 'distribution-blob-parity') "$Label kind is invalid."
    Assert-Condition ((Test-IsIntegerValue $Document.checkerVersion) -and [long]$Document.checkerVersion -eq 1) "$Label checkerVersion must equal integer 1."
    Assert-Condition ([string]$Document.status -ceq 'pass') "$Label status must equal pass."
    Assert-Condition ([string]$Document.os -cin @('windows', 'linux', 'macos')) "$Label runner OS is invalid."
    Assert-Condition ([string]$Document.sourceSha -ceq $ExpectedSourceSha) "$Label source SHA differs from current HEAD."
    Assert-Condition ([string]$Document.workflowSha -ceq $ExpectedWorkflowSha) "$Label workflow SHA differs from the expected workflow SHA."
    Assert-Condition (
        ((@($Document.patterns) | ConvertTo-Json -Compress) -ceq (@('distribution/*.json', 'distribution/fixtures/*.json') | ConvertTo-Json -Compress))) `
        "$Label approved pattern set or order differs."
    Assert-Condition ($Document.productionReady -is [bool] -and -not $Document.productionReady) "$Label must remain non-productionReady."

    $files = @($Document.files)
    Assert-Condition ((Test-IsIntegerValue $Document.fileCount) -and [long]$Document.fileCount -eq $files.Count) "$Label fileCount does not match files."
    Assert-Condition ($files.Count -eq $ExpectedPaths.Count) "$Label tracked path count differs from the current repository."
    $reportedPaths = @($files | ForEach-Object { [string]$_.path })
    Assert-Condition (
        (($reportedPaths | ConvertTo-Json -Compress) -ceq ($ExpectedPaths | ConvertTo-Json -Compress))) `
        "$Label tracked paths must use the exact ordinal repository order."
    $filesByPath = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($file in $files) {
        Assert-ExactPropertySet -Document $file `
            -Expected @('path', 'attributes', 'worktreeBytes', 'blobBytes', 'worktreeSha256', 'blobSha256', 'worktreeHasCarriageReturn', 'blobHasCarriageReturn', 'sha256Match', 'lfVerdict') `
            -Label "$Label file entry"
        $path = [string]$file.path
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($path)) "$Label has an empty tracked path."
        Assert-Condition (-not $filesByPath.ContainsKey($path)) "$Label has duplicate tracked path '$path'."
        $filesByPath.Add($path, $file)

        Assert-ExactPropertySet -Document $file.attributes -Expected @('text', 'eol') -Label "$Label attributes for '$path'"
        Assert-Condition ([string]$file.attributes.text -ceq 'set' -and [string]$file.attributes.eol -ceq 'lf') "$Label attributes for '$path' are not text=set/eol=lf."
        Assert-Condition ((Test-IsIntegerValue $file.worktreeBytes) -and [long]$file.worktreeBytes -gt 0) "$Label worktree byte size for '$path' is invalid."
        Assert-Condition ((Test-IsIntegerValue $file.blobBytes) -and [long]$file.blobBytes -eq [long]$file.worktreeBytes) "$Label worktree/blob byte sizes for '$path' differ."
        Assert-Condition ([string]$file.worktreeSha256 -cmatch '^[0-9a-f]{64}$') "$Label worktree SHA-256 for '$path' is invalid."
        Assert-Condition ([string]$file.blobSha256 -cmatch '^[0-9a-f]{64}$') "$Label blob SHA-256 for '$path' is invalid."
        Assert-Condition ([string]$file.worktreeSha256 -ceq [string]$file.blobSha256) "$Label worktree/blob SHA-256 values for '$path' differ."
        Assert-Condition ($file.worktreeHasCarriageReturn -is [bool] -and -not $file.worktreeHasCarriageReturn) "$Label worktree CR verdict for '$path' is invalid."
        Assert-Condition ($file.blobHasCarriageReturn -is [bool] -and -not $file.blobHasCarriageReturn) "$Label blob CR verdict for '$path' is invalid."
        Assert-Condition ($file.sha256Match -is [bool] -and $file.sha256Match) "$Label SHA parity verdict for '$path' is invalid."
        Assert-Condition ([string]$file.lfVerdict -ceq 'pass') "$Label LF verdict for '$path' is invalid."
    }

    foreach ($path in $ExpectedPaths) {
        Assert-Condition ($filesByPath.ContainsKey($path)) "$Label tracked path set is missing '$path'."
        $reported = $filesByPath[$path]
        $attributes = Get-SourceBoundGitPathAttributes -RepositoryRoot $RepositoryRoot -GitPath $path
        Assert-Condition (
            [string]$reported.attributes.text -ceq [string]$attributes.text -and
            [string]$reported.attributes.eol -ceq [string]$attributes.eol) `
            "$Label reported attributes for '$path' differ from committed HEAD attributes."
        [byte[]]$blobBytes = Invoke-GitBytes -RepositoryRoot $RepositoryRoot -Arguments @('cat-file', 'blob', "HEAD:$path")
        $blobSha256 = Get-BytesSha256 -Bytes $blobBytes
        Assert-Condition (
            [long]$reported.blobBytes -eq $blobBytes.Count -and [string]$reported.blobSha256 -ceq $blobSha256) `
            "$Label committed blob fields for '$path' do not match the current HEAD blob."
    }
    return $filesByPath
}

function New-DistributionBlobParityAggregateReport {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$InputPaths,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkflowSha
    )

    Assert-Condition ($InputPaths.Count -eq 3) 'Blob parity aggregate requires exactly three direct reports.'
    [string[]]$expectedPaths = @(Get-TrackedDistributionJsonPaths -RepositoryRoot $RepositoryRoot)
    $reportsByOs = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $reportReferences = [System.Collections.Generic.List[object]]::new()
    foreach ($inputPath in $InputPaths) {
        $resolvedPath = Resolve-ExistingFile -Path $inputPath -DisplayName 'Blob parity input report'
        $document = Read-JsonFile -Path $resolvedPath -DisplayName "Blob parity input report '$resolvedPath'"
        $label = "Blob parity input report '$resolvedPath'"
        $filesByPath = Assert-DirectBlobParityReport -Document $document -Label $label `
            -RepositoryRoot $RepositoryRoot -ExpectedPaths $expectedPaths `
            -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        $os = [string]$document.os
        Assert-Condition (-not $reportsByOs.ContainsKey($os)) "Blob parity aggregate has duplicate runner OS '$os'."
        $reportsByOs.Add($os, [pscustomobject][ordered]@{
            document = $document
            filesByPath = $filesByPath
        })
        $reportReferences.Add([pscustomobject][ordered]@{
            os = $os
            fileName = [System.IO.Path]::GetFileName($resolvedPath)
            sha256 = Get-LowerFileSha256 -Path $resolvedPath
        })
    }

    Assert-Condition (
        (($reportsByOs.Keys | Sort-Object) -join ',') -ceq 'linux,macos,windows') `
        'Blob parity aggregate runner OS set must be exactly windows, linux and macos.'

    $canonicalFiles = @(
        foreach ($path in $expectedPaths) {
            $reference = $reportsByOs['windows'].filesByPath[$path]
            foreach ($os in @('linux', 'macos')) {
                $candidate = $reportsByOs[$os].filesByPath[$path]
                Assert-Condition (
                    [long]$candidate.blobBytes -eq [long]$reference.blobBytes -and
                    [string]$candidate.blobSha256 -ceq [string]$reference.blobSha256) `
                    "Blob parity aggregate committed blob fields drifted for '$path' on '$os'."
            }
            [pscustomobject][ordered]@{
                path = $path
                blobBytes = [long]$reference.blobBytes
                blobSha256 = [string]$reference.blobSha256
            }
        }
    )
    $sortedReferences = @($reportReferences | Sort-Object -Property os)
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        kind = 'distribution-blob-parity-aggregate'
        checkerVersion = 1
        status = 'pass'
        sourceSha = $ExpectedSourceSha
        workflowSha = $ExpectedWorkflowSha
        patterns = @('distribution/*.json', 'distribution/fixtures/*.json')
        reportRefs = $sortedReferences
        fileCount = $canonicalFiles.Count
        files = $canonicalFiles
        productionReady = $false
    }
}

function New-CrlfBlobParityFixtureBytes {
    param([Parameter(Mandatory = $true)][byte[]]$BlobBytes)

    $output = [System.IO.MemoryStream]::new()
    try {
        foreach ($value in $BlobBytes) {
            if ($value -eq 0x0A) {
                $output.WriteByte(0x0D)
            }
            $output.WriteByte($value)
        }
        return ,$output.ToArray()
    }
    finally {
        $output.Dispose()
    }
}

function Get-BlobParityMutationFixture {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$GitPaths
    )

    Assert-Condition ($GitPaths.Count -gt 0) 'Blob parity mutation fixture setup requires at least one tracked JSON path.'
    foreach ($gitPath in $GitPaths) {
        [byte[]]$blobBytes = Invoke-GitBytes -RepositoryRoot $RepositoryRoot -Arguments @('cat-file', 'blob', "HEAD:$gitPath")
        if (Test-ContainsCarriageReturn -Bytes $blobBytes) {
            continue
        }
        for ($index = 1; $index -lt $blobBytes.Count; $index++) {
            if ($blobBytes[$index - 1] -eq 0x0A -and $blobBytes[$index] -eq 0x20) {
                return [pscustomobject][ordered]@{
                    gitPath = $gitPath
                    blobBytes = $blobBytes
                    indentationIndex = $index
                }
            }
        }
    }

    throw (
        'Blob parity mutation fixture setup requires at least one LF-only tracked JSON blob with an indentation space immediately after LF; ' +
        "inspected paths: $($GitPaths -join ', ').")
}

function Test-BlobParityByteMutationNegativeFixtures {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$GitPaths
    )

    $mutationFixture = Get-BlobParityMutationFixture -RepositoryRoot $RepositoryRoot -GitPaths $GitPaths
    $gitPath = [string]$mutationFixture.gitPath
    [byte[]]$blobBytes = $mutationFixture.blobBytes
    [byte[]]$crlfBytes = New-CrlfBlobParityFixtureBytes -BlobBytes $blobBytes
    Assert-Condition (Test-ContainsCarriageReturn -Bytes $crlfBytes) 'CRLF negative fixture does not contain raw 0x0D.'
    $fixtureText = [System.Text.UTF8Encoding]::new($false, $true).GetString($crlfBytes)
    try {
        $fixtureText | ConvertFrom-Json -Depth 100 -ErrorAction Stop | Out-Null
    }
    catch {
        throw "CRLF negative fixture must remain valid JSON: $($_.Exception.Message)"
    }

    [byte[]]$validLfMutation = [byte[]]$blobBytes.Clone()
    $validLfMutation[[int]$mutationFixture.indentationIndex] = 0x09
    Assert-Condition ($validLfMutation.Count -eq $blobBytes.Count) `
        'Valid-LF byte mutation fixture must preserve the raw blob byte size.'
    Assert-Condition (-not (Test-ContainsCarriageReturn -Bytes $validLfMutation)) `
        'Valid-LF byte mutation fixture unexpectedly contains raw 0x0D.'
    Assert-Condition (
        (Get-BytesSha256 -Bytes $validLfMutation) -cne (Get-BytesSha256 -Bytes $blobBytes)) `
        'Valid-LF byte mutation fixture must change the raw SHA-256 value.'
    $validLfMutationText = [System.Text.UTF8Encoding]::new($false, $true).GetString($validLfMutation)
    try {
        $validLfMutationText | ConvertFrom-Json -Depth 100 -ErrorAction Stop | Out-Null
    }
    catch {
        throw "Valid-LF byte mutation fixture must remain valid JSON: $($_.Exception.Message)"
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-blob-parity-crlf-' + [Guid]::NewGuid().ToString('N'))
    try {
        $fixturePath = Resolve-RepositoryWorktreePath -WorktreeRoot $temporaryRoot -GitPath $gitPath
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fixturePath)) | Out-Null
        [System.IO.File]::WriteAllBytes($fixturePath, $crlfBytes)
        Assert-Throws -Name 'blob-parity-valid-json-crlf' -MessagePattern 'worktree contains raw 0x0D' -Action {
            New-DistributionBlobParityEntry -RepositoryRoot $RepositoryRoot -WorktreeRoot $temporaryRoot -GitPath $gitPath
        }
        [System.IO.File]::WriteAllBytes($fixturePath, $validLfMutation)
        Assert-Throws -Name 'blob-parity-valid-json-lf-byte-mutation' -MessagePattern 'worktree SHA-256 differs from the raw HEAD blob' -Action {
            New-DistributionBlobParityEntry -RepositoryRoot $RepositoryRoot -WorktreeRoot $temporaryRoot -GitPath $gitPath
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($temporaryRoot)) {
            [System.IO.Directory]::Delete($temporaryRoot, $true)
        }
    }
}

function Test-BlobParityAttributeSourceFixtures {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-blob-parity-attributes-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments @('init', '--quiet'))
        $emptyHooks = Join-Path $temporaryRoot 'empty-hooks'
        [System.IO.Directory]::CreateDirectory($emptyHooks) | Out-Null
        foreach ($configuration in @(
                @('user.name', 'Unlimotion Contract Tests'),
                @('user.email', 'tests@unlimotion.invalid'),
                @('commit.gpgSign', 'false'),
                @('core.hooksPath', $emptyHooks),
                @('core.autocrlf', 'false'))) {
            [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments (@('config') + $configuration))
        }

        $gitPath = 'distribution/source-binding-fixture.json'
        $jsonPath = Resolve-RepositoryWorktreePath -WorktreeRoot $temporaryRoot -GitPath $gitPath
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($jsonPath)) | Out-Null
        [System.IO.File]::WriteAllText(
            $jsonPath,
            "{`n  `"value`": true`n}`n",
            [System.Text.UTF8Encoding]::new($false))
        [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments @('add', '--', $gitPath))
        [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments @('commit', '--quiet', '-m', 'Add JSON fixture without attributes'))

        $attributesPath = Join-Path $temporaryRoot '.gitattributes'
        [System.IO.File]::WriteAllText(
            $attributesPath,
            "distribution/*.json text eol=lf`n",
            [System.Text.UTF8Encoding]::new($false))
        [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments @('add', '--', '.gitattributes'))

        $effectiveAttributes = Get-GitPathAttributes -RepositoryRoot $temporaryRoot -GitPath $gitPath
        $headAttributes = Get-GitPathAttributes -RepositoryRoot $temporaryRoot -GitPath $gitPath -Source 'HEAD'
        Assert-Condition (
            $effectiveAttributes.text -ceq 'set' -and $effectiveAttributes.eol -ceq 'lf') `
            'Source-binding negative fixture must expose staged/worktree text=set/eol=lf attributes.'
        Assert-Condition (
            $headAttributes.text -ceq 'unspecified' -and $headAttributes.eol -ceq 'unspecified') `
            'Source-binding negative fixture HEAD must not contain the staged/worktree attributes.'
        Assert-Throws -Name 'blob-parity-attributes-not-committed-in-head' `
            -MessagePattern 'committed HEAD attributes must be text=set/eol=lf' -Action {
            New-DistributionBlobParityEntry -RepositoryRoot $temporaryRoot -WorktreeRoot $temporaryRoot -GitPath $gitPath
        }

        [void](Invoke-GitBytes -RepositoryRoot $temporaryRoot -Arguments @('commit', '--quiet', '-m', 'Commit LF attributes'))
        $positive = New-DistributionBlobParityEntry `
            -RepositoryRoot $temporaryRoot -WorktreeRoot $temporaryRoot -GitPath $gitPath
        Assert-Condition (
            $positive.attributes.text -ceq 'set' -and
            $positive.attributes.eol -ceq 'lf' -and
            $positive.sha256Match -and
            $positive.lfVerdict -ceq 'pass') `
            'Source-binding positive fixture must pass after LF attributes are committed in HEAD.'
        Add-Check -Name 'blob-parity:committed-head-and-effective-attributes-match'
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        Assert-Condition (-not [System.IO.Directory]::Exists($temporaryRoot)) `
            "Blob parity attribute-source fixture failed to remove temporary Git repository '$temporaryRoot'."
    }
}

function Test-BlobParityAggregateFixtures {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][object]$DirectReport,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkflowSha
    )

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-blob-parity-aggregate-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $paths = [System.Collections.Generic.List[string]]::new()
        foreach ($os in @('windows', 'linux', 'macos')) {
            $document = Copy-JsonObject $DirectReport
            $document.os = $os
            $path = Join-Path $temporaryRoot "$os.json"
            Write-JsonUtf8NoBom -Path $path -Document $document
            $paths.Add($path)
        }

        $positive = New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
            -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        Assert-Condition ($positive.status -ceq 'pass' -and $positive.fileCount -eq @($DirectReport.files).Count) `
            'Blob parity aggregate positive fixture did not preserve the complete canonical path set.'
        Add-Check -Name 'blob-parity-aggregate:three-os-canonical-closure'

        $missingOs = Copy-JsonObject $DirectReport
        $missingOs.os = 'linux'
        Write-JsonUtf8NoBom -Path $paths[2] -Document $missingOs
        Assert-Throws -Name 'blob-parity-aggregate-missing-os' -MessagePattern 'runner OS' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }

        $macos = Copy-JsonObject $DirectReport
        $macos.os = 'macos'
        $macos.files = @($macos.files | Select-Object -Skip 1)
        $macos.fileCount = @($macos.files).Count
        Write-JsonUtf8NoBom -Path $paths[2] -Document $macos
        Assert-Throws -Name 'blob-parity-aggregate-missing-path' -MessagePattern 'tracked path' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }

        $macos = Copy-JsonObject $DirectReport
        $macos.os = 'macos'
        $macos.files = @($macos.files | Sort-Object -Property path -Descending)
        Write-JsonUtf8NoBom -Path $paths[2] -Document $macos
        Assert-Throws -Name 'blob-parity-aggregate-path-order' -MessagePattern 'exact ordinal repository order' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }

        $macos = Copy-JsonObject $DirectReport
        $macos.os = 'macos'
        $macos.sourceSha = '0' * 40
        Write-JsonUtf8NoBom -Path $paths[2] -Document $macos
        Assert-Throws -Name 'blob-parity-aggregate-source-sha-mismatch' -MessagePattern 'source SHA differs from current HEAD' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }

        $macos = Copy-JsonObject $DirectReport
        $macos.os = 'macos'
        $macos.workflowSha = '0' * 40
        Write-JsonUtf8NoBom -Path $paths[2] -Document $macos
        Assert-Throws -Name 'blob-parity-aggregate-workflow-sha-mismatch' -MessagePattern 'workflow SHA differs from the expected workflow SHA' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }

        $macos = Copy-JsonObject $DirectReport
        $macos.os = 'macos'
        $macos.files[0].worktreeSha256 = '0' * 64
        $macos.files[0].blobSha256 = '0' * 64
        Write-JsonUtf8NoBom -Path $paths[2] -Document $macos
        Assert-Throws -Name 'blob-parity-aggregate-hash-drift' -MessagePattern 'current HEAD blob' -Action {
            New-DistributionBlobParityAggregateReport -RepositoryRoot $RepositoryRoot -InputPaths @($paths) `
                -ExpectedSourceSha $ExpectedSourceSha -ExpectedWorkflowSha $ExpectedWorkflowSha
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($temporaryRoot)) {
            [System.IO.Directory]::Delete($temporaryRoot, $true)
        }
    }
}

function New-OrdinalIgnoreCaseMap {
    return [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
}

function Get-YamlTopLevelBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = @($normalized -split "`n")
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -ceq "${Name}:") {
            $start = $index
            break
        }
    }
    Assert-Condition ($start -ge 0) "Workflow is missing top-level '${Name}:' block."

    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if (-not [string]::IsNullOrWhiteSpace($lines[$index]) -and $lines[$index] -cmatch '^[A-Za-z0-9_.-]+:\s*') {
            $end = $index
            break
        }
    }
    return ($lines[$start..($end - 1)] -join "`n")
}

function Get-WorkflowJobBlocks {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $jobsBlock = Get-YamlTopLevelBlock -Text $WorkflowText -Name 'jobs'
    $jobMatches = [regex]::Matches($jobsBlock, '(?m)^  ([A-Za-z0-9_-]+):\s*$')
    Assert-Condition ($jobMatches.Count -gt 0) 'Workflow jobs block is empty.'

    $result = [ordered]@{}
    for ($index = 0; $index -lt $jobMatches.Count; $index++) {
        $jobId = $jobMatches[$index].Groups[1].Value
        $start = $jobMatches[$index].Index
        $end = if ($index + 1 -lt $jobMatches.Count) { $jobMatches[$index + 1].Index } else { $jobsBlock.Length }
        Assert-Condition (-not $result.Contains($jobId)) "Workflow has duplicate job id '$jobId'."
        $result[$jobId] = $jobsBlock.Substring($start, $end - $start)
    }
    return $result
}

function Test-DistributionProducerResults {
    param(
        [Parameter(Mandatory = $true)][bool]$Relevant,
        [Parameter(Mandatory = $true)][hashtable]$Results
    )

    Assert-Condition ($Results.ContainsKey('changes')) "Producer fixture is missing 'changes'."
    if ([string]$Results.changes -cne 'success') {
        return [ordered]@{ status = 'failure'; applicable = $true; producersOk = $false }
    }

    $mandatory = @($Results.Keys | Where-Object { $_ -cne 'changes' })
    Assert-Condition ($mandatory.Count -gt 0) 'Producer fixture has no mandatory producers.'
    if ($Relevant) {
        $allSucceeded = @($mandatory | Where-Object { [string]$Results[$_] -cne 'success' }).Count -eq 0
        return [ordered]@{
            status = if ($allSucceeded) { 'pendingAggregate' } else { 'failure' }
            applicable = $true
            producersOk = $allSucceeded
        }
    }

    $allSkipped = @($mandatory | Where-Object { [string]$Results[$_] -cne 'skipped' }).Count -eq 0
    return [ordered]@{
        status = if ($allSkipped) { 'notApplicable' } else { 'failure' }
        applicable = -not $allSkipped
        producersOk = $allSkipped
    }
}

function Assert-ProducerFixtureAccepted {
    param([Parameter(Mandatory = $true)][object]$Outcome)

    Assert-Condition ([string]$Outcome.status -cne 'failure') 'Producer fixture must be rejected fail-closed.'
}

function Replace-WorkflowFixtureOnce {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $expression = [regex]::new($Pattern)
    Assert-Condition ($expression.IsMatch($Text)) "Workflow fixture '$Name' could not find its mutation target."
    return $expression.Replace($Text, $Replacement, 1)
}

function Test-MacosCandidateSigningContract {
    param([Parameter(Mandatory = $true)][string]$BuilderText)

    $normalized = $BuilderText.Replace("`r`n", "`n").Replace("`r", "`n")
    $preflightMatches = [regex]::Matches($normalized, '(?m)^for command_name in ([^;\r\n]+); do$')
    Assert-Condition ($preflightMatches.Count -eq 1) 'macOS builder must have exactly one command preflight loop.'
    $preflightCommands = @($preflightMatches[0].Groups[1].Value.Trim() -split '\s+')
    Assert-Condition (@($preflightCommands | Where-Object { $_ -ceq 'codesign' }).Count -eq 1) `
        'macOS builder command preflight must require codesign exactly once.'

    $vpkPackLine = '"$vpk_path" pack \'
    $vpkAdHocLine = '  --signAppIdentity - \'
    $vpkEndLine = '  --skip-updates'
    $legacySignLine = 'codesign --force --deep --sign - "$app_path"'
    $legacyVerifyLine = 'codesign --verify --deep --strict --verbose=2 "$app_path"'
    $legacyPackageLine = 'productbuild --component "$app_path" /Applications "$asset_directory/$legacy_pkg_name"'

    foreach ($requiredLine in @($vpkPackLine, $vpkAdHocLine, $vpkEndLine, $legacySignLine, $legacyVerifyLine, $legacyPackageLine)) {
        Assert-Condition ([regex]::Matches($normalized, "(?m)^$([regex]::Escape($requiredLine))$").Count -eq 1) `
            "macOS builder must contain exactly one required signing-contract line: $requiredLine"
    }
    Assert-Condition ([regex]::Matches($normalized, '(?m)^\s*--signAppIdentity\b[^\r\n]*$').Count -eq 1) `
        'macOS builder must use only the one literal Velopack ad-hoc signing identity.'
    Assert-Condition ($normalized -cnotmatch '(?im)--signInstallIdentity|--notaryProfile|--keychain|security\s+import') `
        'macOS candidate builder must not import or reference production signing credentials.'
    Assert-Condition ([regex]::Matches($normalized, '\bcodesign\b').Count -eq 3) `
        'macOS candidate builder must mention codesign only in preflight and the two exact legacy bundle commands.'

    $builderLines = @($normalized -split "`n")
    $vpkStartIndexes = @(
        for ($index = 0; $index -lt $builderLines.Count; $index++) {
            if ($builderLines[$index] -ceq $vpkPackLine) { $index }
        }
    )
    $vpkEndIndexes = @(
        for ($index = 0; $index -lt $builderLines.Count; $index++) {
            if ($builderLines[$index] -ceq $vpkEndLine) { $index }
        }
    )
    Assert-Condition ($vpkStartIndexes.Count -eq 1 -and $vpkEndIndexes.Count -eq 1) `
        'macOS builder must have one unambiguous Velopack command block.'
    $vpkStartIndex = $vpkStartIndexes[0]
    $vpkEndIndex = $vpkEndIndexes[0]
    Assert-Condition ($vpkEndIndex -gt $vpkStartIndex) 'macOS Velopack command terminator must follow its start.'
    for ($index = $vpkStartIndex; $index -lt $vpkEndIndex; $index++) {
        Assert-Condition ($builderLines[$index].EndsWith('\', [StringComparison]::Ordinal)) `
            'Every macOS Velopack command line before --skip-updates must continue with a backslash.'
    }
    $vpkSigningIndex = -1
    for ($index = $vpkStartIndex + 1; $index -lt $vpkEndIndex; $index++) {
        if ($builderLines[$index] -ceq $vpkAdHocLine) { $vpkSigningIndex = $index }
    }
    Assert-Condition ($vpkSigningIndex -gt $vpkStartIndex -and $vpkSigningIndex -lt $vpkEndIndex) `
        'Velopack ad-hoc signing must be part of the one continuous vpk pack command block.'

    $orderedLines = @($vpkPackLine, $vpkAdHocLine, $vpkEndLine, $legacySignLine, $legacyVerifyLine, $legacyPackageLine)
    $previousIndex = -1
    foreach ($line in $orderedLines) {
        $currentIndex = $normalized.IndexOf($line, [StringComparison]::Ordinal)
        Assert-Condition ($currentIndex -gt $previousIndex) `
            'macOS signing order must be vpk pack < Velopack ad-hoc seal < pack completion < legacy sign < strict verify < productbuild.'
        $previousIndex = $currentIndex
    }
}

function Test-MacosLaunchIsolationContract {
    param([Parameter(Mandatory = $true)][string]$ValidatorText)

    $normalized = $ValidatorText.Replace("`r`n", "`n").Replace("`r", "`n")
    $launchMatches = [regex]::Matches($normalized, '(?ms)^launch_app\(\) \{\n(?<body>.*?)^\}\n')
    Assert-Condition ($launchMatches.Count -eq 1) 'macOS validator must contain exactly one launch_app function.'
    $launchBody = $launchMatches[0].Groups['body'].Value

    $requiredLines = @(
        '  local config="$run_directory/settings.json"',
        '  local task_storage="$run_directory/Tasks"',
        '  mkdir -p -- "$task_storage"',
        '  jq -cn --arg path "$task_storage" ''{TaskStorage:{Path:$path,IsServerMode:"False"}}'' >"$config"',
        '  "$binary" "--config=$config" >"$stdout" 2>"$stderr" &'
    )
    foreach ($requiredLine in $requiredLines) {
        Assert-Condition ([regex]::Matches($launchBody, "(?m)^$([regex]::Escape($requiredLine))$").Count -eq 1) `
            "macOS launch isolation must contain exactly one required line: $requiredLine"
    }

    $previousIndex = -1
    foreach ($requiredLine in $requiredLines) {
        $currentIndex = $launchBody.IndexOf($requiredLine, [StringComparison]::Ordinal)
        Assert-Condition ($currentIndex -gt $previousIndex) `
            'macOS launch isolation order must be config path < task storage path < storage creation < config write < process launch.'
        $previousIndex = $currentIndex
    }
    Assert-Condition ([regex]::Matches($launchBody, '(?m)^\s*(?:local\s+)?task_storage=').Count -eq 1) `
        'macOS native smoke must assign task_storage exactly once.'
    Assert-Condition ([regex]::Matches($launchBody, '(?m)>\s*"\$config"\s*$').Count -eq 1) `
        'macOS native smoke must write the seeded config exactly once.'
    Assert-Condition ($launchBody -cnotmatch '(?m)(?:task_storage=|--arg\s+path\s+)["'']/Tasks["'']') `
        'macOS native smoke must not assign or write root-level /Tasks storage.'
    Assert-Condition ([regex]::Matches($launchBody, '--arg configPath "\$config"').Count -eq 1 -and
        [regex]::Matches($launchBody, '--arg taskStoragePath "\$task_storage"').Count -eq 1 -and
        [regex]::Matches($launchBody, 'launchConfiguration:"seeded-isolated-task-storage"').Count -eq 1 -and
        [regex]::Matches($launchBody, 'unconfiguredFirstRunVerified:false').Count -eq 1) `
        'macOS smoke evidence must disclose seeded isolated storage and deny unconfigured first-run coverage.'
}

function Test-WindowsLaunchIsolationContract {
    param([Parameter(Mandatory = $true)][string]$ValidatorText)

    $normalized = $ValidatorText.Replace("`r`n", "`n").Replace("`r", "`n")
    $launchMatches = [regex]::Matches($normalized, '(?ms)^function Invoke-WindowSmoke \{\n(?<body>.*?)^\}\n')
    Assert-Condition ($launchMatches.Count -eq 1) 'Windows validator must contain exactly one Invoke-WindowSmoke function.'
    $launchBody = $launchMatches[0].Groups['body'].Value

    $requiredLines = @(
        "    `$runDirectory = Join-Path `$WorkDirectory ('window smoke ' + [Guid]::NewGuid().ToString('N'))",
        "    `$config = Join-Path `$runDirectory 'settings.json'",
        "    `$taskStorage = Join-Path `$runDirectory 'Tasks'",
        '    New-Item -ItemType Directory -Force -Path $taskStorage | Out-Null',
        "    @{ TaskStorage = @{ Path = `$taskStorage; IsServerMode = 'False' } } | ConvertTo-Json -Compress | Set-Content -LiteralPath `$config -Encoding utf8NoBOM",
        '    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()',
        '    $startInfo.UseShellExecute = $false',
        '    $startInfo.ArgumentList.Add("--config=$config")',
        '    $process = [System.Diagnostics.Process]::new()',
        '    $process.StartInfo = $startInfo',
        '        $started = $process.Start()'
    )
    $previousIndex = -1
    foreach ($requiredLine in $requiredLines) {
        Assert-Condition ([regex]::Matches($launchBody, "(?m)^$([regex]::Escape($requiredLine))$").Count -eq 1) `
            "Windows launch isolation must contain exactly one required line: $requiredLine"
        $currentIndex = $launchBody.IndexOf($requiredLine, [StringComparison]::Ordinal)
        Assert-Condition ($currentIndex -gt $previousIndex) `
            'Windows launch isolation order must be spaced run directory < config path < task storage path < storage creation < config write < ProcessStartInfo < ArgumentList binding < process launch.'
        $previousIndex = $currentIndex
    }
    Assert-Condition ([regex]::Matches($launchBody, '(?m)^\s*\$taskStorage\s*=').Count -eq 1) `
        'Windows native smoke must assign taskStorage exactly once.'
    Assert-Condition ([regex]::Matches($launchBody, '(?m)Set-Content -LiteralPath \$config\b').Count -eq 1) `
        'Windows native smoke must write the seeded config exactly once.'
    Assert-Condition ([regex]::Matches($launchBody, '(?m)^    \$startInfo\.ArgumentList\.Add\("--config=\$config"\)$').Count -eq 1 -and
        [regex]::Matches($launchBody, '(?i)(?<!-)--?config=').Count -eq 1 -and
        $launchBody -cnotmatch '(?m)Start-Process\b|\.Arguments\s*=') `
        'Windows native smoke must preserve the spaced config path through ProcessStartInfo.ArgumentList.'
    Assert-Condition ($launchBody -cnotmatch '(?im)(?:=|Path\s*=)\s*[''"](?:[A-Z]:\\Tasks|/Tasks)[''"]') `
        'Windows native smoke must not assign or write root-level Tasks storage.'
    foreach ($evidenceLine in @(
            '            taskStoragePath = $taskStorage',
            "            launchConfiguration = 'seeded-isolated-task-storage'",
            '            unconfiguredFirstRunVerified = $false')) {
        Assert-Condition ([regex]::Matches($launchBody, "(?m)^$([regex]::Escape($evidenceLine))$").Count -eq 1) `
            "Windows smoke evidence must contain exactly one disclosure line: $evidenceLine"
    }
}

function Test-ProcessArgumentListPreservesSpacedConfigPath {
    $expected = '--config=C:\temporary path\settings.json'
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Resolve-PythonExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-c',
            'import sys; assert len(sys.argv) == 2; sys.stdout.write(sys.argv[1])',
            $expected)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'ProcessStartInfo spaced-path probe did not start.' }
        $actual = $process.StandardOutput.ReadToEnd()
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or $actual -cne $expected) {
            throw "ProcessStartInfo.ArgumentList did not preserve the exact spaced config argument. Exit=$($process.ExitCode); Actual='$actual'; Error='$errorText'."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Test-LinuxLaunchIsolationContract {
    param([Parameter(Mandatory = $true)][string]$ValidatorText)

    $normalized = $ValidatorText.Replace("`r`n", "`n").Replace("`r", "`n")
    $userMatches = [regex]::Matches($normalized, '(?ms)^ensure_test_user\(\) \{\n(?<body>.*?)^\}\n')
    Assert-Condition ($userMatches.Count -eq 1) 'Linux validator must contain exactly one ensure_test_user function.'
    $userBody = $userMatches[0].Groups['body'].Value
    $requiredLines = @(
        '    install -d -o 10001 -g 10001 /home/unlimotion-test/unlimotion-data/Tasks',
        '    printf ''%s\n'' ''{"TaskStorage":{"Path":"/home/unlimotion-test/unlimotion-data/Tasks","IsServerMode":"False"}}'' > /home/unlimotion-test/unlimotion-data/config.json',
        '    chown 10001:10001 /home/unlimotion-test/unlimotion-data/config.json',
        '  CONFIG_PATH=''/home/unlimotion-test/unlimotion-data/config.json''',
        '  TASK_STORAGE_PATH=''/home/unlimotion-test/unlimotion-data/Tasks''',
        '  LAUNCH_CONFIGURATION=''seeded-isolated-task-storage'''
    )
    $previousIndex = -1
    foreach ($requiredLine in $requiredLines) {
        Assert-Condition ([regex]::Matches($userBody, "(?m)^$([regex]::Escape($requiredLine))$").Count -eq 1) `
            "Linux launch isolation must contain exactly one required line: $requiredLine"
        $currentIndex = $userBody.IndexOf($requiredLine, [StringComparison]::Ordinal)
        Assert-Condition ($currentIndex -gt $previousIndex) `
            'Linux launch isolation order must be task storage creation < config write < config ownership < evidence state transition.'
        $previousIndex = $currentIndex
    }
    Assert-Condition ([regex]::Matches($userBody, '(?m)>\s*/home/unlimotion-test/unlimotion-data/config\.json\s*$').Count -eq 1) `
        'Linux native smoke must write the seeded config exactly once.'
    Assert-Condition ($userBody -cnotmatch '"Path":"/Tasks"') `
        'Linux native smoke must not overwrite the seeded config with root-level /Tasks storage.'
    Assert-Condition ([regex]::Matches($normalized, '--config=/home/unlimotion-test/unlimotion-data/config\.json').Count -eq 1) `
        'Linux missing-runtime launch must use the seeded isolated config exactly once.'
    Assert-Condition ([regex]::Matches($normalized, 'local config_path=''/home/unlimotion-test/unlimotion-data/config\.json''').Count -eq 1) `
        'Linux positive launch must bind the seeded isolated config exactly once.'
    Assert-Condition ([regex]::Matches($normalized, '(?m)^    "\$executable" "--config=\$config_path" > "\$app_log" 2>&1 &$').Count -eq 1 -and
        [regex]::Matches($normalized, '(?i)(?<!-)--?config=').Count -eq 2) `
        'Linux positive and negative launches must each pass exactly one seeded config argument.'
    foreach ($evidencePattern in @(
            'CONFIG_PATH=""',
            'TASK_STORAGE_PATH=""',
            'LAUNCH_CONFIGURATION="notApplicable"',
            'UNCONFIGURED_FIRST_RUN_VERIFIED=false',
            '"launchConfiguration": %s,\n'' "$(json_string "$LAUNCH_CONFIGURATION")"',
            '"unconfiguredFirstRunVerified": %s,\n'' "$UNCONFIGURED_FIRST_RUN_VERIFIED"',
            'appimage-extract-and-run-with-seeded-isolated-task-storage',
            'debian-package-external-x11-with-seeded-isolated-task-storage',
            'negative-missing-runtime-external-x11-with-seeded-isolated-task-storage')) {
        Assert-Condition ([regex]::Matches($normalized, [regex]::Escape($evidencePattern)).Count -eq 1) `
            "Linux smoke evidence must contain exactly one configured-launch disclosure: $evidencePattern"
    }
    $executionCaseMatches = [regex]::Matches(
        $normalized,
        '(?ms)^CURRENT_STEP="artifact-metadata"\ncase "\$MODE" in\n(?<body>.*?)^esac$')
    Assert-Condition ($executionCaseMatches.Count -eq 1) 'Linux validator must contain exactly one final mode execution case.'
    $metadataMatches = [regex]::Matches($executionCaseMatches[0].Groups['body'].Value, '(?ms)^  metadata\)\n(?<body>.*?)^    ;;$')
    Assert-Condition ($metadataMatches.Count -eq 1 -and $metadataMatches[0].Groups['body'].Value -cnotmatch 'ensure_test_user|LAUNCH_CONFIGURATION=') `
        'Linux metadata mode must not seed or claim launch configuration.'
}

function Get-WorkflowNamedStepBlock {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowText,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $normalized = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $pattern = "(?m)^      - name:\s*$([regex]::Escape($StepName))\s*$"
    $matches = [regex]::Matches($normalized, $pattern)
    Assert-Condition ($matches.Count -eq 1) "Workflow must contain exactly one step named '$StepName'."

    $start = $matches[0].Index
    $nextStep = $normalized.IndexOf("`n      - name:", $start + $matches[0].Length, [StringComparison]::Ordinal)
    if ($nextStep -lt 0) { $nextStep = $normalized.Length }
    return $normalized.Substring($start, $nextStep - $start)
}

function Replace-WorkflowNamedStepFixtureOnce {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowText,
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $normalized = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $stepBlock = Get-WorkflowNamedStepBlock -WorkflowText $normalized -StepName $StepName
    $mutatedStep = Replace-WorkflowFixtureOnce -Text $stepBlock -Pattern $Pattern -Replacement $Replacement -Name $Name
    $stepStart = $normalized.IndexOf($stepBlock, [StringComparison]::Ordinal)
    Assert-Condition ($stepStart -ge 0) "Workflow fixture '$Name' could not locate the exact named step block."
    return $normalized.Substring(0, $stepStart) + $mutatedStep + $normalized.Substring($stepStart + $stepBlock.Length)
}

function Assert-WorkflowStepShaBindings {
    param(
        [Parameter(Mandatory = $true)][string]$StepBlock,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $normalized = $StepBlock.Replace("`r`n", "`n").Replace("`r", "`n")
    $envMatches = [regex]::Matches($normalized, '(?m)^        env:\s*$')
    $runMatches = [regex]::Matches($normalized, '(?m)^        run:\s*\|\s*$')
    Assert-Condition ($envMatches.Count -eq 1 -and $runMatches.Count -eq 1 -and $runMatches[0].Index -gt $envMatches[0].Index) `
        "$Label must have one step-local env block before its run block."
    $envBlock = $normalized.Substring(
        $envMatches[0].Index,
        $runMatches[0].Index - $envMatches[0].Index)
    Assert-Condition (
        [regex]::Matches($envBlock, '(?m)^          SOURCE_SHA: \$\{\{ github\.sha \}\}\s*$').Count -eq 1) `
        "$Label must bind exact step-local SOURCE_SHA: `${{ github.sha }}."
    Assert-Condition (
        [regex]::Matches($envBlock, '(?m)^          WORKFLOW_SHA: \$\{\{ job\.workflow_sha \}\}\s*$').Count -eq 1) `
        "$Label must bind exact step-local WORKFLOW_SHA: `${{ job.workflow_sha }}."
}

function Get-EmbeddedWorkflowPythonBlock {
    param(
        [Parameter(Mandatory = $true)][string]$StepBlock,
        [Parameter(Mandatory = $true)][string[]]$RequiredSnippets,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $lines = @($StepBlock.Replace("`r`n", "`n").Replace("`r", "`n") -split "`n")
    $matchingBlocks = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cnotmatch "^ *[^`n]*<<'PY'\s*$") { continue }

        $rawCodeLines = [System.Collections.Generic.List[string]]::new()
        $indent = $null
        $terminated = $false
        for ($codeIndex = $index + 1; $codeIndex -lt $lines.Count; $codeIndex++) {
            $line = [string]$lines[$codeIndex]
            if ($line -cmatch '^(?<indent> *)PY\s*$') {
                $indent = [string]$Matches.indent
                $index = $codeIndex
                $terminated = $true
                break
            }
            $rawCodeLines.Add($line)
        }
        Assert-Condition $terminated "Embedded Python block '$DisplayName' has no closing PY marker."

        $codeLines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in $rawCodeLines) {
            Assert-Condition (
                [string]::IsNullOrEmpty($line) -or $line.StartsWith($indent, [StringComparison]::Ordinal)) `
                "Embedded Python block '$DisplayName' escapes its YAML run indentation."
            $codeLines.Add($(if ([string]::IsNullOrEmpty($line)) { '' } else { $line.Substring($indent.Length) }))
        }
        $code = $codeLines -join "`n"
        $containsEverySnippet = $true
        foreach ($snippet in $RequiredSnippets) {
            if (-not $code.Contains($snippet, [StringComparison]::Ordinal)) {
                $containsEverySnippet = $false
                break
            }
        }
        if ($containsEverySnippet) { $matchingBlocks.Add($code) }
    }

    Assert-Condition ($matchingBlocks.Count -eq 1) "Workflow must expose exactly one embedded Python block for '$DisplayName'."
    return $matchingBlocks[0]
}

function Resolve-PythonExecutable {
    if (-not [string]::IsNullOrWhiteSpace([string]$script:pythonExecutable)) {
        return $script:pythonExecutable
    }

    $runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
    $candidateNames = if ($runningOnWindows) { @('python', 'python3') } else { @('python3', 'python') }
    foreach ($candidateName in $candidateNames) {
        foreach ($command in @(Get-Command $candidateName -CommandType Application -All -ErrorAction SilentlyContinue)) {
            $source = [string]$command.Source
            if ($runningOnWindows -and $source -match '(?i)[\\/]WindowsApps[\\/]') { continue }
            $script:pythonExecutable = $source
            return $script:pythonExecutable
        }
    }
    throw 'Python 3 is required to execute the embedded workflow contract fixtures.'
}

function Invoke-EmbeddedWorkflowPython {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [string[]]$ScriptArguments = @(),
        [hashtable]$Environment = @{}
    )

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-workflow-python-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $process = $null
    try {
        $scriptPath = Join-Path $temporaryRoot 'embedded-workflow.py'
        [System.IO.File]::WriteAllText($scriptPath, $Script + "`n", [System.Text.UTF8Encoding]::new($false))

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = Resolve-PythonExecutable
        $startInfo.WorkingDirectory = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add($scriptPath)
        foreach ($argument in $ScriptArguments) { $startInfo.ArgumentList.Add([string]$argument) }
        foreach ($key in $Environment.Keys) { $startInfo.Environment[[string]$key] = [string]$Environment[$key] }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        Assert-Condition $process.Start() 'Failed to start Python for an embedded workflow fixture.'
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return [pscustomobject][ordered]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput
            StandardError = $standardError
        }
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-EmbeddedPythonSucceeded {
    param(
        [Parameter(Mandatory = $true)][object]$Result,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Condition (
        [int]$Result.ExitCode -eq 0) `
        "Embedded workflow fixture '$Name' failed: $([string]$Result.StandardError)"
}

function Assert-EmbeddedPythonRejected {
    param(
        [Parameter(Mandatory = $true)][object]$Result,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    if ([int]$Result.ExitCode -eq 0) {
        throw "Negative fixture '$Name' unexpectedly passed."
    }
    $combinedOutput = ([string]$Result.StandardOutput) + "`n" + ([string]$Result.StandardError)
    Assert-Condition (
        $combinedOutput.Contains($ExpectedMessage, [StringComparison]::Ordinal)) `
        "Negative fixture '$Name' failed for an unexpected reason: $combinedOutput"
    $script:negativeFixtureCount++
    Add-Check -Name "negative:$Name"
}

function Write-JsonUtf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Document
    )

    $json = $Document | ConvertTo-Json -Depth 100 -Compress
    [System.IO.File]::WriteAllText($Path, $json + "`n", [System.Text.UTF8Encoding]::new($false))
}

function Write-BlobParityEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Document
    )

    $fullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($directory)) "Blob parity evidence path must have a parent directory: $Path"
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    Write-JsonUtf8NoBom -Path $fullPath -Document $Document
}

function Read-GitHubOutputMap {
    param([Parameter(Mandatory = $true)][string]$Path)

    $outputs = @{}
    foreach ($line in @(Get-Content -LiteralPath $Path -Encoding utf8)) {
        $separator = $line.IndexOf('=', [StringComparison]::Ordinal)
        Assert-Condition ($separator -gt 0) "Invalid GITHUB_OUTPUT fixture line: '$line'."
        $outputs[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
    }
    return $outputs
}

function New-EmbeddedProducerEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$GitHubOutputPath,
        [switch]$DuplicateArtifactId
    )

    $environment = @{
        RELEVANT = 'true'
        CHANGES_RESULT = 'success'
        CONTRACT_RESULT = 'success'
        WINDOWS_RESULT = 'success'
        LINUX_RESULT = 'success'
        MACOS_X64_RESULT = 'success'
        MACOS_ARM64_RESULT = 'success'
        ANDROID_BUILD_RESULT = 'success'
        ANDROID_API23_RESULT = 'success'
        ANDROID_API36_RESULT = 'success'
        GITHUB_SHA = ('1' * 40)
        WORKFLOW_SHA = ('2' * 40)
        EVENT_NAME = 'pull_request'
        PR_HEAD_SHA = ('1' * 40)
        PR_BASE_SHA = ('3' * 40)
        GITHUB_RUN_ID = '424242'
        GITHUB_RUN_ATTEMPT = '7'
        GITHUB_OUTPUT = $GitHubOutputPath
    }
    $producerPlan = @(
        [pscustomobject]@{ Name = 'contract'; Prefix = 'CONTRACT'; Attempt = 3 },
        [pscustomobject]@{ Name = 'windows-x64'; Prefix = 'WINDOWS'; Attempt = 1 },
        [pscustomobject]@{ Name = 'linux-x64'; Prefix = 'LINUX'; Attempt = 4 },
        [pscustomobject]@{ Name = 'macos-x64'; Prefix = 'MACOS_X64'; Attempt = 2 },
        [pscustomobject]@{ Name = 'macos-arm64'; Prefix = 'MACOS_ARM64'; Attempt = 5 },
        [pscustomobject]@{ Name = 'android-multi'; Prefix = 'ANDROID'; Attempt = 2 },
        [pscustomobject]@{ Name = 'android-api23'; Prefix = 'ANDROID_API23'; Attempt = 6 },
        [pscustomobject]@{ Name = 'android-api36'; Prefix = 'ANDROID_API36'; Attempt = 1 }
    )

    $artifactId = 1001
    foreach ($producer in $producerPlan) {
        $baseName = "distribution-$($producer.Name)-fixture-attempt-$($producer.Attempt)"
        $environment["$($producer.Prefix)_NAME"] = $baseName
        $environment["$($producer.Prefix)_ID"] = [string]$artifactId
        $environment["$($producer.Prefix)_DIGEST"] = ('{0:x64}' -f $artifactId)
        $artifactId++
        $environment["$($producer.Prefix)_RECEIPT_NAME"] = "$baseName-receipt"
        $environment["$($producer.Prefix)_RECEIPT_ID"] = [string]$artifactId
        $environment["$($producer.Prefix)_RECEIPT_DIGEST"] = ('{0:x64}' -f $artifactId)
        $artifactId++
    }
    if ($DuplicateArtifactId) {
        $environment.ANDROID_API36_RECEIPT_ID = $environment.CONTRACT_ID
    }
    return $environment
}

function Invoke-EmbeddedProducerInspectionFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$DuplicateArtifactId
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $producerPath = Join-Path $Root 'producer-results.json'
    $githubOutputPath = Join-Path $Root 'github-output.txt'
    $environment = New-EmbeddedProducerEnvironment -GitHubOutputPath $githubOutputPath -DuplicateArtifactId:$DuplicateArtifactId
    $result = Invoke-EmbeddedWorkflowPython -Script $Script -ScriptArguments @($producerPath) -Environment $environment
    Assert-EmbeddedPythonSucceeded -Result $result -Name 'producer-inspection'
    return [pscustomobject][ordered]@{
        Result = $result
        Document = Read-JsonFile -Path $producerPath -DisplayName 'Embedded producer inspection output'
        Outputs = Read-GitHubOutputMap -Path $githubOutputPath
        Environment = $environment
        ProducerPath = $producerPath
    }
}

function Get-EmbeddedProducerTransports {
    param([Parameter(Mandatory = $true)][object]$ProducerDocument)

    $transports = [System.Collections.Generic.List[object]]::new()
    foreach ($producerProperty in $ProducerDocument.artifacts.psobject.Properties) {
        foreach ($role in @('main', 'receipt')) {
            $transports.Add($producerProperty.Value.$role)
        }
    }
    return @($transports)
}

function New-EmbeddedAggregateDirectoryFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$ProducerDocument
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    foreach ($transport in @(Get-EmbeddedProducerTransports -ProducerDocument $ProducerDocument)) {
        [System.IO.Directory]::CreateDirectory((Join-Path $Root ([string]$transport.name))) | Out-Null
    }
}

function New-EmbeddedReceiptValidationFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$ProducerDocument,
        [Parameter(Mandatory = $true)][object]$IdentityDocument
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $identityPath = Join-Path $Root 'identity.json'
    $producerPath = Join-Path $Root 'producer-results.json'
    Write-JsonUtf8NoBom -Path $identityPath -Document $IdentityDocument
    Write-JsonUtf8NoBom -Path $producerPath -Document $ProducerDocument

    $identityBinding = [ordered]@{}
    foreach ($key in @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256', 'signatureProfile')) {
        $identityBinding[$key] = $IdentityDocument.$key
    }
    $receiptPaths = @{}
    $payloadRoots = @{}
    foreach ($producerName in @('contract', 'android_api23', 'android_api36')) {
        $producerTransport = $ProducerDocument.artifacts.$producerName
        $payloadRoot = Join-Path $Root ([string]$producerTransport.main.name)
        $receiptRoot = Join-Path $Root ([string]$producerTransport.receipt.name)
        [System.IO.Directory]::CreateDirectory($payloadRoot) | Out-Null
        [System.IO.Directory]::CreateDirectory($receiptRoot) | Out-Null

        if ($producerName -ceq 'contract') {
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'identity.json') -Document $IdentityDocument
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'contract-evidence.json') -Document ([ordered]@{ status = 'pass' })
            $blobParityReport = New-DistributionBlobParityReport -RepositoryRoot $script:repositoryRoot `
                -ExpectedSourceSha ([string]$IdentityDocument.sourceSha) `
                -ExpectedWorkflowSha ([string]$IdentityDocument.workflowSha)
            $blobParityReport.os = 'windows'
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'blob-parity.json') -Document $blobParityReport
        }
        else {
            $api = if ($producerName -ceq 'android_api23') { '23' } else { '36' }
            $logcatName = "android-api$api-logcat.txt"
            $emulatorLogName = "android-api$api-emulator.log"
            $logcatPath = Join-Path $payloadRoot $logcatName
            $emulatorLogPath = Join-Path $payloadRoot $emulatorLogName
            [System.IO.File]::WriteAllText($logcatPath, "api$api-logcat-fixture`n", [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText($emulatorLogPath, "api$api-emulator-fixture`n", [System.Text.UTF8Encoding]::new($false))
            $evidence = [ordered]@{
                runtime = [ordered]@{
                    logcat = [ordered]@{
                        fileName = $logcatName
                        sha256 = Get-LowerFileSha256 -Path $logcatPath
                        bytes = (Get-Item -LiteralPath $logcatPath).Length
                    }
                    emulatorLog = [ordered]@{
                        fileName = $emulatorLogName
                        sha256 = Get-LowerFileSha256 -Path $emulatorLogPath
                        bytes = (Get-Item -LiteralPath $emulatorLogPath).Length
                    }
                }
            }
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'evidence.json') -Document $evidence
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'download-transport.json') -Document ([ordered]@{ status = 'pass' })
        }

        $payloads = @(
            Get-ChildItem -LiteralPath $payloadRoot -File | Sort-Object Name | ForEach-Object {
                [ordered]@{
                    fileName = $_.Name
                    sha256 = Get-LowerFileSha256 -Path $_.FullName
                }
            }
        )
        $receipt = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-evidence-transport-receipt'
            identity = $identityBinding
            artifact = [ordered]@{
                name = [string]$producerTransport.main.name
                id = [string]$producerTransport.main.id
                digest = ([string]$producerTransport.main.digest).ToLowerInvariant()
                retentionDays = 7
            }
            payloads = $payloads
            productionReady = $false
        }
        $receiptPath = Join-Path $receiptRoot 'evidence-transport-receipt.json'
        Write-JsonUtf8NoBom -Path $receiptPath -Document $receipt
        $receiptPaths[$producerName] = $receiptPath
        $payloadRoots[$producerName] = $payloadRoot
    }

    return [pscustomobject][ordered]@{
        Root = $Root
        IdentityPath = $identityPath
        ProducerPath = $producerPath
        ReceiptPaths = $receiptPaths
        PayloadRoots = $payloadRoots
    }
}

function Invoke-EmbeddedReceiptValidator {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [Parameter(Mandatory = $true)][object]$Fixture
    )

    return Invoke-EmbeddedWorkflowPython -Script $Script -ScriptArguments @(
        [string]$Fixture.Root,
        [string]$Fixture.IdentityPath,
        [string]$Fixture.ProducerPath)
}

function Test-EmbeddedWorkflowBehaviorFixtures {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $inspectStep = Get-WorkflowNamedStepBlock -WorkflowText $WorkflowText -StepName 'Inspect every producer result'
    $aggregateStep = Get-WorkflowNamedStepBlock -WorkflowText $WorkflowText -StepName 'Require exact artifact set after bounded transport'
    $inspectScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $inspectStep `
        -RequiredSnippets @('artifact_prefixes = (', 'artifact_ids = []', 'stream.write(f"artifact_ids=') `
        -DisplayName 'producer inspection'
    $exactDirectoryScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $aggregateStep `
        -RequiredSnippets @('Expected 16 unique producer artifact names', 'Downloaded artifact set mismatch') `
        -DisplayName 'exact aggregate directory set'
    $receiptScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $aggregateStep `
        -RequiredSnippets @('receipt_plan = {', 'Receipt payload hash mismatch:', 'does not bind the exact sidecar bytes') `
        -DisplayName 'receipt and Android runtime sidecar validation'
    Add-Check -Name 'workflow-behavior:embedded-python-extracted-by-step'

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-workflow-behavior-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $positiveInspection = Invoke-EmbeddedProducerInspectionFixture `
            -Script $inspectScript -Root (Join-Path $temporaryRoot 'inspect-positive')
        Assert-Condition ($positiveInspection.Document.status -ceq 'pendingAggregate') 'Embedded inspection must keep the mixed-attempt producer set pendingAggregate.'
        Assert-Condition ($positiveInspection.Outputs.producers_ok -ceq 'true') 'Embedded inspection must accept 16 valid unique producer artifact ids.'
        $positiveTransports = @(Get-EmbeddedProducerTransports -ProducerDocument $positiveInspection.Document)
        $positiveIds = @($positiveTransports | ForEach-Object { [string]$_.id })
        $positiveNames = @($positiveTransports | ForEach-Object { [string]$_.name })
        Assert-Condition ($positiveIds.Count -eq 16 -and @($positiveIds | Sort-Object -Unique).Count -eq 16) 'Positive producer fixture must expose 16 unique main/receipt artifact ids.'
        Assert-Condition ($positiveNames.Count -eq 16 -and @($positiveNames | Sort-Object -Unique).Count -eq 16) 'Positive producer fixture must expose 16 unique main/receipt artifact names.'
        $reportedIds = @([string]$positiveInspection.Outputs.artifact_ids -split ',')
        Assert-Condition ($reportedIds.Count -eq 16 -and @($reportedIds | Sort-Object -Unique).Count -eq 16) 'Embedded inspection must emit exactly 16 unique artifact ids.'
        Assert-Condition (
            ((@($reportedIds | Sort-Object) -join ',') -ceq (@($positiveIds | Sort-Object) -join ','))) `
            'Embedded inspection artifact_ids output must bind the exact main/receipt id set.'
        $attempts = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($name in $positiveNames) {
            Assert-Condition ($name -cmatch '-attempt-([0-9]+)(?:-receipt)?$') "Mixed-attempt fixture artifact name '$name' is not attempt-scoped."
            $attempts.Add($Matches[1]) | Out-Null
        }
        Assert-Condition ($attempts.Count -gt 1) 'Positive producer fixture must exercise mixed producer run attempts.'
        Add-Check -Name 'workflow-behavior:embedded-inspect-mixed-attempts-16-unique-ids'

        $duplicateInspection = Invoke-EmbeddedProducerInspectionFixture `
            -Script $inspectScript -Root (Join-Path $temporaryRoot 'inspect-duplicate') -DuplicateArtifactId
        $duplicateIds = @(
            Get-EmbeddedProducerTransports -ProducerDocument $duplicateInspection.Document |
                ForEach-Object { [string]$_.id })
        Assert-Condition (@($duplicateIds | Sort-Object -Unique).Count -eq 15) 'Duplicate-id fixture must expose exactly 15 unique ids across 16 transports.'
        Assert-Condition ($duplicateInspection.Document.status -ceq 'failure') 'Embedded inspection must fail closed when 16 transports do not have 16 unique ids.'
        Assert-Condition ($duplicateInspection.Outputs.producers_ok -ceq 'false') 'Duplicate producer artifact id must clear producers_ok.'
        Assert-Condition ([string]::IsNullOrEmpty([string]$duplicateInspection.Outputs.artifact_ids)) 'Duplicate producer artifact id must suppress the aggregate artifact id list.'
        Assert-Condition (
            ([string]$duplicateInspection.Document.reason).Contains('duplicate id=', [StringComparison]::Ordinal)) `
            'Duplicate producer artifact id must be reported as the fail-closed reason.'
        $script:negativeFixtureCount++
        Add-Check -Name 'negative:workflow-embedded-inspect-not-16-unique-ids'

        $producerPath = Join-Path $temporaryRoot 'aggregate-producer-results.json'
        Write-JsonUtf8NoBom -Path $producerPath -Document $positiveInspection.Document
        $downloadRoot = Join-Path $temporaryRoot 'aggregate-download'
        New-EmbeddedAggregateDirectoryFixture -Root $downloadRoot -ProducerDocument $positiveInspection.Document
        $exactResult = Invoke-EmbeddedWorkflowPython -Script $exactDirectoryScript -ScriptArguments @($downloadRoot, $producerPath)
        Assert-EmbeddedPythonSucceeded -Result $exactResult -Name 'exact aggregate directory set'
        Add-Check -Name 'workflow-behavior:embedded-aggregate-exact-16-directories'
        [System.IO.Directory]::CreateDirectory((Join-Path $downloadRoot 'distribution-android-api23-fixture-failure-attempt-1')) | Out-Null
        $staleResult = Invoke-EmbeddedWorkflowPython -Script $exactDirectoryScript -ScriptArguments @($downloadRoot, $producerPath)
        Assert-EmbeddedPythonRejected -Result $staleResult `
            -Name 'workflow-embedded-aggregate-stale-failure-directory' `
            -ExpectedMessage 'Downloaded artifact set mismatch'

        $identityDocument = [pscustomobject][ordered]@{
            rawTag = 'v1.2.3'
            normalizedVersion = '1.2.3'
            sourceSha = ('1' * 40)
            workflowSha = ('2' * 40)
            tagBinding = 'notApplicable'
            manifestSha256 = ('4' * 64)
            supportMatrixSha256 = ('5' * 64)
            signatureProfile = 'validation'
        }
        $receiptPositive = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-positive') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $receiptPositiveResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $receiptPositive
        Assert-EmbeddedPythonSucceeded -Result $receiptPositiveResult -Name 'receipt and Android runtime sidecar validation'
        Add-Check -Name 'workflow-behavior:embedded-receipt-blob-parity-runtime-sidecar-positive'

        $receiptMismatch = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-payload-mismatch') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $contractReceipt = Read-JsonFile -Path $receiptMismatch.ReceiptPaths.contract -DisplayName 'Contract receipt mismatch fixture'
        @($contractReceipt.payloads | Where-Object fileName -ceq 'identity.json')[0].sha256 = ('0' * 64)
        Write-JsonUtf8NoBom -Path $receiptMismatch.ReceiptPaths.contract -Document $contractReceipt
        $receiptMismatchResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $receiptMismatch
        Assert-EmbeddedPythonRejected -Result $receiptMismatchResult `
            -Name 'workflow-embedded-receipt-payload-hash-mismatch' `
            -ExpectedMessage 'Receipt payload hash mismatch: contract/identity.json'

        $blobParityMismatch = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-blob-parity-mismatch') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $blobParityPath = Join-Path $blobParityMismatch.PayloadRoots.contract 'blob-parity.json'
        [System.IO.File]::AppendAllText($blobParityPath, "mutated-blob-parity`n", [System.Text.UTF8Encoding]::new($false))
        $blobParityMismatchResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $blobParityMismatch
        Assert-EmbeddedPythonRejected -Result $blobParityMismatchResult `
            -Name 'workflow-embedded-contract-blob-parity-hash-mismatch' `
            -ExpectedMessage 'Receipt payload hash mismatch: contract/blob-parity.json'

        $missingBlobParity = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-blob-parity-missing') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        [System.IO.File]::Delete((Join-Path $missingBlobParity.PayloadRoots.contract 'blob-parity.json'))
        $missingBlobParityResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $missingBlobParity
        Assert-EmbeddedPythonRejected -Result $missingBlobParityResult `
            -Name 'workflow-embedded-contract-blob-parity-missing' `
            -ExpectedMessage 'Unexpected contract downloaded payload closure'

        $runtimeMismatch = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'runtime-sidecar-mismatch') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $api23LogcatPath = Join-Path $runtimeMismatch.PayloadRoots.android_api23 'android-api23-logcat.txt'
        [System.IO.File]::AppendAllText($api23LogcatPath, "mutated-sidecar`n", [System.Text.UTF8Encoding]::new($false))
        $api23Receipt = Read-JsonFile -Path $runtimeMismatch.ReceiptPaths.android_api23 -DisplayName 'Android API 23 runtime mismatch receipt'
        @($api23Receipt.payloads | Where-Object fileName -ceq 'android-api23-logcat.txt')[0].sha256 = Get-LowerFileSha256 -Path $api23LogcatPath
        Write-JsonUtf8NoBom -Path $runtimeMismatch.ReceiptPaths.android_api23 -Document $api23Receipt
        $runtimeMismatchResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $runtimeMismatch
        Assert-EmbeddedPythonRejected -Result $runtimeMismatchResult `
            -Name 'workflow-embedded-android-runtime-sidecar-mismatch' `
            -ExpectedMessage 'android_api23 runtime.logcat does not bind the exact sidecar bytes'
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-WorkflowBlobParityWiring {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowText,
        [Parameter(Mandatory = $true)][object]$JobBlocks
    )

    foreach ($jobId in @('contract', 'linux_x64', 'macos_x64', 'distribution-verdict')) {
        Assert-Condition ($JobBlocks.Contains($jobId)) "Blob parity workflow is missing required job '$jobId'."
    }

    $contractBlock = [string]$JobBlocks['contract']
    $contractValidation = Get-WorkflowNamedStepBlock -WorkflowText $contractBlock `
        -StepName 'Validate schemas, inventory, identity, feeds and retry policy'
    Assert-WorkflowStepShaBindings -StepBlock $contractValidation -Label 'Contract blob parity validation'
    Assert-Condition (
        $contractValidation.Contains('-Area All `', [StringComparison]::Ordinal) -and
        $contractValidation.Contains('-SourceSha $env:SOURCE_SHA `', [StringComparison]::Ordinal) -and
        $contractValidation.Contains('-WorkflowSha $env:WORKFLOW_SHA `', [StringComparison]::Ordinal) -and
        $contractValidation.Contains('-BlobParityEvidencePath artifacts/contract/blob-parity.json `', [StringComparison]::Ordinal)) `
        'Contract validation must run Area All and emit the exact source-bound artifacts/contract/blob-parity.json report.'
    $contractStage = Get-WorkflowNamedStepBlock -WorkflowText $contractBlock -StepName 'Stage contract evidence'
    Assert-Condition (
        $contractStage.Contains(
            'Copy-Item artifacts/contract/identity.json,artifacts/contract/contract-evidence.json,artifacts/contract/blob-parity.json -Destination artifacts/upload/contract',
            [StringComparison]::Ordinal)) `
        'Contract staging must copy identity.json, contract-evidence.json and blob-parity.json as one exact closure.'
    $contractReceipt = Get-WorkflowNamedStepBlock -WorkflowText $contractBlock -StepName 'Record contract evidence transport receipt'
    Assert-Condition (
        $contractReceipt.Contains(
            "`$payloads = @('identity.json', 'contract-evidence.json', 'blob-parity.json') | ForEach-Object {",
            [StringComparison]::Ordinal)) `
        'Contract receipt must bind identity.json, contract-evidence.json and blob-parity.json.'

    $producerPlans = @(
        [pscustomobject][ordered]@{
            JobId = 'linux_x64'
            OsLabel = 'Linux'
            CheckerStep = 'Validate canonical JSON byte identity on Linux'
            BuildStep = 'Build Linux candidates from one publish'
            RetainStep = 'Retain Linux blob parity evidence'
            StageStep = 'Stage Linux tar transport'
            UploadStep = 'Upload exact Linux candidate as tar'
            ScratchPath = 'artifacts/distribution-validation/blob-parity-linux-x64.json'
            BuilderArgument = '--output-root'
            BuilderRoot = 'artifacts/distribution-validation/linux-x64'
            RetainedPath = 'artifacts/distribution-validation/linux-x64/evidence/blob-parity.json'
        },
        [pscustomobject][ordered]@{
            JobId = 'macos_x64'
            OsLabel = 'macOS x64'
            CheckerStep = 'Validate canonical JSON byte identity on macOS'
            BuildStep = 'Build macOS x64 candidates'
            RetainStep = 'Retain macOS blob parity evidence'
            StageStep = 'Stage macOS x64 evidence'
            UploadStep = 'Upload exact macOS x64 candidate'
            ScratchPath = 'artifacts/distribution-validation/blob-parity-macos-x64.json'
            BuilderArgument = '--output-dir'
            BuilderRoot = 'artifacts/distribution-validation/macos-x64'
            RetainedPath = 'artifacts/distribution-validation/macos-x64/evidence/blob-parity.json'
        }
    )
    foreach ($plan in $producerPlans) {
        $jobBlock = [string]$JobBlocks[$plan.JobId]
        $checkoutMarker = '      - name: Checkout exact source'
        $checkerMarker = "      - name: $($plan.CheckerStep)"
        $buildMarker = "      - name: $($plan.BuildStep)"
        $retainMarker = "      - name: $($plan.RetainStep)"
        $stageMarker = "      - name: $($plan.StageStep)"
        $uploadMarker = "      - name: $($plan.UploadStep)"
        Assert-Condition ([regex]::Matches($jobBlock, "(?m)^$([regex]::Escape($checkerMarker))`$").Count -eq 1) `
            "$($plan.OsLabel) blob parity checker step must exist exactly once."
        Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName 'Checkout exact source' | Out-Null
        Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName $plan.StageStep | Out-Null
        Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName $plan.UploadStep | Out-Null
        $checkoutIndex = $jobBlock.IndexOf($checkoutMarker, [StringComparison]::Ordinal)
        $checkerIndex = $jobBlock.IndexOf($checkerMarker, [StringComparison]::Ordinal)
        $buildIndex = $jobBlock.IndexOf($buildMarker, [StringComparison]::Ordinal)
        $retainIndex = $jobBlock.IndexOf($retainMarker, [StringComparison]::Ordinal)
        $stageIndex = $jobBlock.IndexOf($stageMarker, [StringComparison]::Ordinal)
        $uploadIndex = $jobBlock.IndexOf($uploadMarker, [StringComparison]::Ordinal)
        Assert-Condition (
            $checkoutIndex -ge 0 -and
            $checkerIndex -gt $checkoutIndex -and
            $buildIndex -gt $checkerIndex -and
            $retainIndex -gt $buildIndex -and
            $stageIndex -gt $retainIndex -and
            $uploadIndex -gt $stageIndex) `
            "$($plan.OsLabel) blob parity order must be checkout < checker < build < retain < stage < upload."

        $checkerStep = Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName $plan.CheckerStep
        Assert-WorkflowStepShaBindings -StepBlock $checkerStep -Label "$($plan.OsLabel) blob parity validation"
        Assert-Condition (
            $checkerStep.Contains('-Area BlobParity `', [StringComparison]::Ordinal) -and
            $checkerStep.Contains('-SourceSha $env:SOURCE_SHA `', [StringComparison]::Ordinal) -and
            $checkerStep.Contains('-WorkflowSha $env:WORKFLOW_SHA `', [StringComparison]::Ordinal) -and
            $checkerStep.Contains("-BlobParityEvidencePath $($plan.ScratchPath)", [StringComparison]::Ordinal)) `
            "$($plan.OsLabel) blob parity checker must use the exact Area/source/workflow/scratch wiring."

        $buildStep = Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName $plan.BuildStep
        $builderArgumentPattern = "(?m)^\s*$([regex]::Escape($plan.BuilderArgument))\s+$([regex]::Escape($plan.BuilderRoot))\s+\\\s*`$"
        Assert-Condition ([regex]::Matches($buildStep, $builderArgumentPattern).Count -eq 1) `
            "$($plan.OsLabel) build step must use exact $($plan.BuilderArgument) $($plan.BuilderRoot)."
        $builderRootPrefix = $plan.BuilderRoot.TrimEnd('/') + '/'
        Assert-Condition (
            $plan.ScratchPath -cne $plan.BuilderRoot -and
            -not $plan.ScratchPath.StartsWith($builderRootPrefix, [StringComparison]::Ordinal)) `
            "$($plan.OsLabel) blob parity scratch report must remain outside the actual builder-owned output root."

        $retainStep = Get-WorkflowNamedStepBlock -WorkflowText $jobBlock -StepName $plan.RetainStep
        $retainSource = "Copy-Item -LiteralPath $($plan.ScratchPath) " + [char]0x60
        Assert-Condition (
            $retainStep.Contains($retainSource, [StringComparison]::Ordinal) -and
            $retainStep.Contains("-Destination $($plan.RetainedPath)", [StringComparison]::Ordinal)) `
            "$($plan.OsLabel) blob parity retain step must copy the scratch report into evidence/blob-parity.json."
    }

    $finalBlock = [string]$JobBlocks['distribution-verdict']
    Assert-Condition (
        $finalBlock.Contains('"contract": {"identity.json", "contract-evidence.json", "blob-parity.json"},', [StringComparison]::Ordinal)) `
        'Aggregate receipt validation must require blob-parity.json in the exact contract payload closure.'
    $aggregateStep = Get-WorkflowNamedStepBlock -WorkflowText $finalBlock `
        -StepName 'Merge platform evidence and aggregate 22 exact assets'
    Assert-WorkflowStepShaBindings -StepBlock $aggregateStep -Label 'Final blob parity aggregate'
    Assert-Condition ([regex]::Matches($aggregateStep, '(?m)^        id:\s*aggregate\s*$').Count -eq 1) `
        'Blob parity aggregate must execute inside the exact step id aggregate.'
    Assert-Condition ($aggregateStep -cnotmatch '(?m)^        continue-on-error:') `
        'Blob parity aggregate step id aggregate must remain fail-closed without continue-on-error.'
    foreach ($requiredInput in @(
            '(Join-Path (Join-Path $download ''${{ needs.contract.outputs.artifact_name }}'') ''blob-parity.json''),',
            '(Join-Path $linuxRoot ''evidence/blob-parity.json''),',
            '(Join-Path (Join-Path $download ''${{ needs.macos_x64.outputs.artifact_name }}'') ''evidence/blob-parity.json'')')) {
        Assert-Condition ($aggregateStep.Contains($requiredInput, [StringComparison]::Ordinal)) `
            "Blob parity aggregate is missing exact input '$requiredInput'."
    }
    $aggregateInputWiring = '-BlobParityInputPath ($blobParityInputs -join '','') ' + [char]0x60
    Assert-Condition (
        $aggregateStep.Contains('-Area BlobParityAggregate `', [StringComparison]::Ordinal) -and
        $aggregateStep.Contains($aggregateInputWiring, [StringComparison]::Ordinal) -and
        $aggregateStep.Contains('-SourceSha $env:SOURCE_SHA `', [StringComparison]::Ordinal) -and
        $aggregateStep.Contains('-WorkflowSha $env:WORKFLOW_SHA `', [StringComparison]::Ordinal) -and
        $aggregateStep.Contains('-BlobParityEvidencePath artifacts/verdict/blob-parity.json', [StringComparison]::Ordinal)) `
        'Step id aggregate must validate the exact three reports and emit artifacts/verdict/blob-parity.json.'

    $machineVerdictStep = Get-WorkflowNamedStepBlock -WorkflowText $finalBlock -StepName 'Write final machine-readable verdict'
    Assert-Condition (
        $machineVerdictStep.Contains('AGGREGATE_OUTCOME: ${{ steps.aggregate.outcome }}', [StringComparison]::Ordinal) -and
        $machineVerdictStep.Contains('and os.environ["AGGREGATE_OUTCOME"] == "success"', [StringComparison]::Ordinal)) `
        'Final machine verdict must require successful step id aggregate outcome.'
    $enforceVerdictStep = Get-WorkflowNamedStepBlock -WorkflowText $finalBlock -StepName 'Enforce stable fail-closed verdict'
    Assert-Condition (
        $enforceVerdictStep.Contains('AGGREGATE_OUTCOME: ${{ steps.aggregate.outcome }}', [StringComparison]::Ordinal) -and
        $enforceVerdictStep.Contains('if [[ "$AGGREGATE_OUTCOME" != "success" ]]; then', [StringComparison]::Ordinal) -and
        $enforceVerdictStep.Contains('exit 1', [StringComparison]::Ordinal)) `
        'Stable final enforcement must fail when step id aggregate does not succeed.'
}

function Test-WorkflowSecurityContract {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $normalized = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $onBlock = Get-YamlTopLevelBlock -Text $normalized -Name 'on'
    $eventNames = @([regex]::Matches($onBlock, '(?m)^  ([A-Za-z0-9_-]+):\s*$') | ForEach-Object { $_.Groups[1].Value })
    Assert-Condition (
        (($eventNames | Sort-Object) -join ',') -ceq 'pull_request,workflow_dispatch') `
        'Standalone distribution workflow must expose only pull_request and workflow_dispatch triggers.'
    Assert-Condition ($onBlock -cnotmatch '(?m)^    paths(?:-ignore)?:') 'pull_request must not use a path filter because the stable verdict is required for every PR.'
    Assert-Condition ($onBlock -cmatch '(?m)^      raw_tag:\s*$') 'workflow_dispatch must expose only the synthetic raw_tag input.'
    Assert-Condition ($onBlock -cnotmatch '(?m)^      (?:source|source_sha|ref|release|publish):\s*$') 'workflow_dispatch must not accept a source or publication input.'

    Assert-Condition ($normalized -cmatch '(?m)^permissions:\s*\n  contents:\s*read\s*$') 'Workflow-level permissions must be contents: read.'
    Assert-Condition ($normalized -cnotmatch '(?im)^\s*(?:permissions:\s*)?write-all\s*$') 'Workflow must not grant write-all.'
    Assert-Condition ($normalized -cnotmatch '(?im)^\s*[A-Za-z][A-Za-z0-9_-]*:\s*write\s*$') 'Standalone validation must not grant any write permission.'
    Assert-Condition ($normalized -cnotmatch '(?i)\$\{\{\s*secrets\s*[.\[]') 'Standalone validation must not reference repository or environment secrets.'
    Assert-Condition ($normalized -cnotmatch '(?i)\bGITHUB_TOKEN\b') 'Standalone validation must not expose GITHUB_TOKEN.'

    $mutationPatterns = @(
        '(?im)\bgh\s+release\s+(?:create|upload|edit|delete)\b',
        '(?im)\bgh\s+api\b[^\n]*(?:--method|-X)\s*(?:POST|PUT|PATCH|DELETE)\b',
        '(?im)\bgit\s+(?:push|tag\s+(?!-l\b|--list\b))',
        '(?im)\bcurl\b[^\n]*(?:-X|--request)\s*(?:POST|PUT|PATCH|DELETE)\b[^\n]*(?:api|uploads)\.github\.com',
        '(?im)\b(?:softprops/action-gh-release|ncipollo/release-action|actions/create-release|marvinpinto/action-automatic-releases)@'
    )
    foreach ($pattern in $mutationPatterns) {
        Assert-Condition ($normalized -cnotmatch $pattern) "Standalone validation contains a forbidden release/repository mutation matching '$pattern'."
    }

    $usesMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*([^\s#]+)')
    Assert-Condition ($usesMatches.Count -gt 0) 'Workflow has no action references to validate.'
    foreach ($usesMatch in $usesMatches) {
        $reference = $usesMatch.Groups[1].Value
        if ($reference.StartsWith('./', [StringComparison]::Ordinal)) { continue }
        Assert-Condition (
            $reference -cmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$') `
            "External action '$reference' must use a full lowercase commit SHA."
    }

    $jobBlocks = Get-WorkflowJobBlocks -WorkflowText $normalized
    foreach ($jobId in $jobBlocks.Keys) {
        Assert-Condition (
            [string]$jobBlocks[$jobId] -cmatch '(?m)^    permissions:\s*\n      contents:\s*read\s*$') `
            "Job '$jobId' must explicitly use contents: read."
    }
    Assert-Condition ($jobBlocks.Contains('distribution-verdict')) "Workflow is missing stable job 'distribution-verdict'."
    Test-WorkflowBlobParityWiring -WorkflowText $normalized -JobBlocks $jobBlocks
    $finalBlock = [string]$jobBlocks['distribution-verdict']
    Assert-Condition ($finalBlock -cmatch '(?m)^    name:\s*distribution-verdict\s*$') 'Stable final job name must remain distribution-verdict.'
    Assert-Condition ($finalBlock -cmatch '(?m)^    if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$') 'Stable final job must run under always().'
    foreach ($jobId in @($jobBlocks.Keys | Where-Object { $_ -cne 'distribution-verdict' })) {
        Assert-Condition (
            $finalBlock -cmatch "(?m)^      - $([regex]::Escape($jobId))\s*$") `
            "Stable final job must declare '$jobId' in needs."
    }

    Assert-Condition ($finalBlock -cmatch 'if results\["changes"\] != "success":') 'Final evaluator must fail when the scope producer does not succeed.'
    Assert-Condition ($finalBlock -cmatch 'elif relevant == "false":') 'Final evaluator must handle irrelevant changes explicitly.'
    Assert-Condition ($finalBlock -cmatch 'if value != "skipped"') 'Irrelevant changes require every mandatory producer to be skipped.'
    Assert-Condition ($finalBlock -cmatch 'elif relevant == "true":') 'Final evaluator must handle relevant changes explicitly.'
    Assert-Condition ($finalBlock -cmatch 'all\(value == "success" for value in mandatory\.values\(\)\)') 'Relevant changes require every mandatory producer to succeed.'

    Assert-Condition ($finalBlock -cmatch '(?m)^.*source_short=.*GITHUB_SHA.*$') 'Final inspection must derive source_short directly from GITHUB_SHA.'
    Assert-Condition (
        $finalBlock -cmatch '(?ms)^      - name:\s*Upload final verdict evidence\s*$.*?^          name:\s*distribution-verdict-\$\{\{\s*steps\.inspect\.outputs\.source_short\s*\}\}-attempt-\$\{\{\s*github\.run_attempt\s*\}\}\s*$') `
        'Final verdict artifact name must use the direct inspect source_short output and workflow run attempt.'
    Assert-Condition ($finalBlock -cmatch '\$\{\{\s*job\.workflow_sha\s*\}\}') 'Final evidence must read the immutable workflow SHA from job.workflow_sha.'
    Assert-Condition ($finalBlock -cmatch '["'']workflowSha["'']') 'Final machine evidence must record workflowSha.'

    Assert-Condition ($normalized -cmatch 'global\\\.json\$') 'Distribution scope must include root global.json.'
    Assert-Condition ($normalized -cmatch '\\\.gitattributes\$') 'Distribution scope must include root .gitattributes.'
    Assert-Condition ($normalized -cmatch 'Directory\\\.\(Build\|Packages\)\\\.\(props\|targets\)\$') 'Distribution scope must include root Directory.Build/Packages props/targets.'
    Assert-Condition ($normalized -cmatch '\[Nn\]u\[Gg\]et\\\.config\$') 'Distribution scope must include root NuGet.config case variants.'

    $uploadMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*actions/upload-artifact@[0-9a-f]{40}\s*(?:#.*)?$')
    Assert-Condition ($uploadMatches.Count -gt 0) 'Workflow has no upload-artifact invocation.'
    foreach ($uploadMatch in $uploadMatches) {
        $stepStart = $normalized.LastIndexOf("`n      - name:", $uploadMatch.Index, [StringComparison]::Ordinal)
        Assert-Condition ($stepStart -ge 0) 'upload-artifact invocation must belong to a named step.'
        $nextStep = $normalized.IndexOf("`n      - name:", $uploadMatch.Index + $uploadMatch.Length, [StringComparison]::Ordinal)
        if ($nextStep -lt 0) { $nextStep = $normalized.Length }
        $stepBlock = $normalized.Substring($stepStart, $nextStep - $stepStart)
        Assert-Condition ($stepBlock -cmatch '(?m)^          if-no-files-found:\s*error\s*$') 'Every artifact upload must fail when files are missing.'
        Assert-Condition ($stepBlock -cmatch '(?m)^          overwrite:\s*false\s*$') 'Every artifact upload must forbid overwrite.'
        Assert-Condition ($stepBlock -cmatch '(?m)^          retention-days:\s*7\s*$') 'Every artifact upload must retain evidence for seven days.'
        Assert-Condition ($stepBlock -cnotmatch '(?m)^        continue-on-error:\s*true\s*$') 'Artifact upload must be one fail-closed atomic invocation.'
        Assert-Condition ([regex]::Matches($stepBlock, '(?m)^        uses:\s*actions/upload-artifact@').Count -eq 1) 'Each artifact upload step must invoke upload-artifact exactly once.'
        Assert-Condition (
            $stepBlock -cmatch '(?m)^          name:\s*.+\$\{\{\s*github\.run_attempt\s*\}\}.*$') `
            'Every artifact upload name must include github.run_attempt.'
    }

    $downloadMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*actions/download-artifact@[0-9a-f]{40}\s*(?:#.*)?$')
    Assert-Condition ($downloadMatches.Count -eq 6) 'Workflow must contain exactly four Android API and two aggregate artifact downloads.'
    $androidDownloadCount = 0
    $aggregateDownloadCount = 0
    foreach ($downloadMatch in $downloadMatches) {
        $stepStart = $normalized.LastIndexOf("`n      - name:", $downloadMatch.Index, [StringComparison]::Ordinal)
        Assert-Condition ($stepStart -ge 0) 'download-artifact invocation must belong to a named step.'
        $nextStep = $normalized.IndexOf("`n      - name:", $downloadMatch.Index + $downloadMatch.Length, [StringComparison]::Ordinal)
        if ($nextStep -lt 0) { $nextStep = $normalized.Length }
        $stepBlock = $normalized.Substring($stepStart, $nextStep - $stepStart)
        Assert-Condition ($stepBlock -cnotmatch '(?m)^          pattern:') 'Distribution evidence must never be downloaded through a broad artifact pattern.'

        if ($stepBlock -cmatch '(?m)^          artifact-ids:\s*\$\{\{\s*needs\.android_build\.outputs\.artifact_id\s*\}\}\s*$') {
            $androidDownloadCount++
            Assert-Condition ($stepBlock -cmatch '(?m)^          merge-multiple:\s*true\s*$') 'Each Android API download must flatten the exact producer artifact.'
            continue
        }
        if ($stepBlock -cmatch '(?m)^          artifact-ids:\s*\$\{\{\s*steps\.inspect\.outputs\.artifact_ids\s*\}\}\s*$') {
            $aggregateDownloadCount++
            Assert-Condition ($stepBlock -cmatch '(?m)^          merge-multiple:\s*false\s*$') 'Each aggregate download must preserve the exact producer artifact directories.'
            continue
        }
        throw 'Every distribution download must use an approved exact artifact-ids output.'
    }
    Assert-Condition ($androidDownloadCount -eq 4) 'Workflow must download the exact Android build artifact twice for each API cell.'
    Assert-Condition ($aggregateDownloadCount -eq 2) 'Workflow must download the exact inspected producer artifact set on both bounded attempts.'
}

function Test-InventoryContract {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument
    )

    Assert-Condition ($ManifestDocument.schemaVersion -eq 1) 'Manifest schemaVersion must equal 1.'
    Assert-Condition ($ManifestDocument.product -ceq 'Unlimotion') 'Manifest product must equal Unlimotion.'
    Assert-Condition ($FixtureDocument.schemaVersion -eq 1) 'Fixture schemaVersion must equal 1.'
    Assert-Condition ($FixtureDocument.product -ceq 'Unlimotion') 'Fixture product must equal Unlimotion.'
    Assert-Condition ($FixtureDocument.release.rawTag -ceq '1.27.0') 'Fixture rawTag must remain 1.27.0.'
    Assert-Condition ($FixtureDocument.release.normalizedVersion -ceq '1.27.0') 'Fixture normalizedVersion must remain 1.27.0.'
    Assert-Condition ($FixtureDocument.release.sourceSha -ceq '5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd') 'Fixture sourceSha drifted.'
    Assert-Condition ($FixtureDocument.release.assetCount -eq 22) 'Fixture assetCount must equal 22.'
    Assert-Condition (@($ManifestDocument.assets).Count -eq 22) 'Manifest must classify exactly 22 release assets.'
    Assert-Condition (@($FixtureDocument.assets).Count -eq 22) 'Fixture must contain exactly 22 release assets.'

    $requiredSupportLevels = @('present', 'metadataVerified', 'launchVerified', 'productionReady')
    Assert-Condition (
        (($ManifestDocument.supportLevels | ConvertTo-Json -Compress) -ceq ($requiredSupportLevels | ConvertTo-Json -Compress))) `
        'Manifest supportLevels must remain the ordered four-level contract.'
    Assert-Condition (
        $ManifestDocument.androidProductionCertificateSha256 -cmatch '^[0-9a-f]{64}$') `
        'Manifest Android production certificate fingerprint must be lowercase SHA-256.'
    $expectedLinuxPrerequisites = [ordered]@{
        debian12 = @('ca-certificates', 'libc6', 'libgcc-s1', 'libgssapi-krb5-2', 'libstdc++6', 'tzdata', 'zlib1g', 'libx11-6', 'libice6', 'libsm6', 'libfontconfig1', 'libicu72', 'libssl3')
        debian13 = @('ca-certificates', 'libc6', 'libgcc-s1', 'libgssapi-krb5-2', 'libstdc++6', 'tzdata', 'zlib1g', 'libx11-6', 'libice6', 'libsm6', 'libfontconfig1', 'libicu76', 'libssl3t64')
    }
    foreach ($debian in $expectedLinuxPrerequisites.Keys) {
        Assert-Condition (
            ((@($ManifestDocument.linuxRuntimePrerequisites.appImageExtractAndRun.$debian) | ConvertTo-Json -Compress) -ceq
                (@($expectedLinuxPrerequisites[$debian]) | ConvertTo-Json -Compress))) `
            "Manifest AppImage runtime prerequisite contract drifted for '$debian'."
    }
    Assert-Condition ((@($ManifestDocument.linuxRuntimePrerequisites.directFuseAdditional.debian12) -join ' ') -ceq 'libfuse2') 'Debian 12 direct-FUSE prerequisite drifted.'
    Assert-Condition ((@($ManifestDocument.linuxRuntimePrerequisites.directFuseAdditional.debian13) -join ' ') -ceq 'libfuse2t64') 'Debian 13 direct-FUSE prerequisite drifted.'

    $manifestById = New-OrdinalIgnoreCaseMap
    $generatedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $normalizedVersion = [string]$FixtureDocument.release.normalizedVersion
    foreach ($asset in @($ManifestDocument.assets)) {
        $assetId = [string]$asset.id
        Assert-Condition (-not $manifestById.ContainsKey($assetId)) "Duplicate manifest asset id '$assetId'."
        $manifestById.Add($assetId, $asset)

        $template = [string]$asset.filenameTemplate
        Assert-Condition ($template -notmatch '\{rawTag\}') "Asset '$assetId' uses forbidden rawTag placeholder."
        $generatedName = $template.Replace('{normalizedVersion}', $normalizedVersion)
        Assert-Condition ($generatedName -notmatch '[{}]') "Asset '$assetId' has an unknown filename placeholder."
        Assert-Condition ([System.IO.Path]::GetFileName($generatedName) -ceq $generatedName) "Asset '$assetId' generated a path instead of a filename."
        Assert-Condition ($generatedNames.Add($generatedName)) "Case-insensitive generated filename collision: '$generatedName'."

        if ([bool]$asset.legacy) {
            Assert-Condition ($asset.role -ceq 'legacyDuplicate') "Legacy asset '$assetId' must use role legacyDuplicate."
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$asset.legacyOwner)) "Legacy asset '$assetId' needs an owner."
            Assert-Condition ($asset.migrationStage -ge 4) "Legacy asset '$assetId' needs migrationStage >= 4."
        }
        else {
            Assert-Condition ($asset.role -cne 'legacyDuplicate') "Non-legacy asset '$assetId' cannot use role legacyDuplicate."
        }
    }

    $fixtureById = New-OrdinalIgnoreCaseMap
    $fixtureNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($fixtureAsset in @($FixtureDocument.assets)) {
        $assetId = [string]$fixtureAsset.assetId
        Assert-Condition (-not $fixtureById.ContainsKey($assetId)) "Duplicate fixture asset id '$assetId'."
        Assert-Condition ($manifestById.ContainsKey($assetId)) "Unexpected fixture asset id '$assetId'."
        Assert-Condition ($fixtureNames.Add([string]$fixtureAsset.name)) "Duplicate fixture asset name '$($fixtureAsset.name)'."
        Assert-Condition ($fixtureAsset.size -is [long] -or $fixtureAsset.size -is [int]) "Fixture asset '$assetId' size must be an integer."
        Assert-Condition ([long]$fixtureAsset.size -gt 0) "Fixture asset '$assetId' must not be zero-byte."
        Assert-Condition ([string]$fixtureAsset.sha256 -cmatch '^[0-9a-f]{64}$') "Fixture asset '$assetId' has invalid SHA-256."
        $createdAtIsValid = $fixtureAsset.createdAt -is [DateTime] -or $fixtureAsset.createdAt -is [DateTimeOffset]
        $updatedAtIsValid = $fixtureAsset.updatedAt -is [DateTime] -or $fixtureAsset.updatedAt -is [DateTimeOffset]
        Assert-Condition $createdAtIsValid "Fixture asset '$assetId' createdAt is invalid."
        Assert-Condition $updatedAtIsValid "Fixture asset '$assetId' updatedAt is invalid."

        $manifestAsset = $manifestById[$assetId]
        $expectedName = ([string]$manifestAsset.filenameTemplate).Replace('{normalizedVersion}', $normalizedVersion)
        Assert-Condition ([string]$fixtureAsset.name -ceq $expectedName) "Fixture asset '$assetId' name '$($fixtureAsset.name)' does not match '$expectedName'."
        Assert-Condition ([string]$fixtureAsset.role -ceq [string]$manifestAsset.role) "Fixture asset '$assetId' role does not match manifest."
        $expectedDownloadUrl = "https://github.com/Kibnet/Unlimotion/releases/download/1.27.0/$expectedName"
        Assert-Condition ([string]$fixtureAsset.downloadUrl -ceq $expectedDownloadUrl) "Fixture asset '$assetId' download URL drifted."
        if ($manifestAsset.role -ceq 'updaterPackage') {
            Assert-Condition ($fixtureAsset.PSObject.Properties.Name -contains 'sha1') "Updater package '$assetId' must record SHA-1 for legacy feed relation validation."
            Assert-Condition ([string]$fixtureAsset.sha1 -cmatch '^[0-9a-f]{40}$') "Updater package '$assetId' has invalid SHA-1."
        }
        $fixtureById.Add($assetId, $fixtureAsset)
    }

    foreach ($assetId in $manifestById.Keys) {
        Assert-Condition ($fixtureById.ContainsKey($assetId)) "Fixture is missing manifest asset '$assetId'."
    }

    $relationIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relation in @($ManifestDocument.relations)) {
        Assert-Condition ($relationIds.Add([string]$relation.id)) "Duplicate relation id '$($relation.id)'."
        Assert-Condition ($manifestById.ContainsKey([string]$relation.feedAssetId)) "Relation '$($relation.id)' references unknown feed asset."
        Assert-Condition ($manifestById.ContainsKey([string]$relation.packageAssetId)) "Relation '$($relation.id)' references unknown package asset."
        Assert-Condition ($manifestById[[string]$relation.feedAssetId].role -ceq 'updaterFeed') "Relation '$($relation.id)' feed role is not updaterFeed."
        Assert-Condition ($manifestById[[string]$relation.packageAssetId].role -ceq 'updaterPackage') "Relation '$($relation.id)' package role is not updaterPackage."
    }

    Assert-Condition (@($FixtureDocument.feeds).Count -eq @($ManifestDocument.relations).Count) 'Fixture feed payload count must match manifest relation count.'
}

function Test-SupportContract {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument,
        [Parameter(Mandatory = $true)][object]$SupportDocument
    )

    Assert-Condition ($SupportDocument.schemaVersion -eq 1) 'Support snapshot schemaVersion must equal 1.'
    Assert-Condition ($SupportDocument.product -ceq 'Unlimotion') 'Support snapshot product must equal Unlimotion.'
    Assert-Condition (-not [bool]$SupportDocument.candidateEvidenceAccepted) 'Support snapshot must reject candidate evidence promotion.'
    Assert-Condition ([bool]$SupportDocument.productionReadyDerived) 'productionReady must remain derived.'
    Assert-Condition ($SupportDocument.release.rawTag -ceq $FixtureDocument.release.rawTag) 'Support release tag differs from fixture.'
    Assert-Condition ($SupportDocument.release.normalizedVersion -ceq $FixtureDocument.release.normalizedVersion) 'Support normalized version differs from fixture.'
    Assert-Condition ($SupportDocument.release.sourceSha -ceq $FixtureDocument.release.sourceSha) 'Support source SHA differs from fixture.'
    Assert-Condition ($SupportDocument.release.publishedAt -ceq $FixtureDocument.release.publishedAt) 'Support publishedAt differs from fixture.'
    Assert-Condition ($SupportDocument.release.releaseUrl -ceq $FixtureDocument.release.releaseUrl) 'Support release URL differs from fixture.'
    Assert-Condition ($SupportDocument.lastPublishedAndroidVersionCode -eq 353) 'Current support snapshot must retain Android versionCode 353.'
    Assert-Condition (@($SupportDocument.claims).Count -eq 7) 'Current README-facing support snapshot must contain seven claims.'

    $manifestById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ManifestDocument.assets)) {
        $manifestById.Add([string]$asset.id, $asset)
    }
    $fixtureById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($FixtureDocument.assets)) {
        $fixtureById.Add([string]$asset.assetId, $asset)
    }

    $canonicalPublicAssets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @($ManifestDocument.assets)) {
        if (-not [bool]$asset.legacy -and $asset.role -in @('userInstaller', 'userPortable')) {
            $null = $canonicalPublicAssets.Add([string]$asset.id)
        }
    }

    $claimIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $claimedAssets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($claim in @($SupportDocument.claims)) {
        Assert-Condition ($claimIds.Add([string]$claim.id)) "Duplicate support claim id '$($claim.id)'."
        Assert-Condition ($claim.releaseTag -ceq $FixtureDocument.release.rawTag) "Support claim '$($claim.id)' tag differs from release snapshot."
        Assert-Condition ($claim.sourceSha -ceq $FixtureDocument.release.sourceSha) "Support claim '$($claim.id)' source SHA differs from release snapshot."
        Assert-Condition ($ManifestDocument.supportLevels -ccontains [string]$claim.evidenceLevel) "Support claim '$($claim.id)' has unknown evidence level."
        Assert-Condition ($claim.evidenceLevel -cne 'productionReady') "Release 1.27.0 cannot be marked productionReady by Stage-3 candidate evidence."
        if ($claim.evidenceLevel -ceq 'present') {
            Assert-Condition (@($claim.verifiedCells).Count -eq 0) "Present-only claim '$($claim.id)' cannot carry a verified OS cell."
        }
        else {
            Assert-Condition (@($claim.verifiedCells).Count -gt 0) "Verified claim '$($claim.id)' must name at least one exact OS cell."
        }
        Assert-Condition (@($claim.caveats).Count -gt 0) "Support claim '$($claim.id)' must retain a caveat."
        Assert-Condition (@($claim.durableEvidenceUrls) -ccontains [string]$FixtureDocument.release.releaseUrl) "Support claim '$($claim.id)' must link the exact release."

        foreach ($assetReference in @($claim.assets)) {
            $assetId = [string]$assetReference.assetId
            Assert-Condition ($canonicalPublicAssets.Contains($assetId)) "Support claim '$($claim.id)' references non-canonical public asset '$assetId'."
            Assert-Condition ($claimedAssets.Add($assetId)) "Support asset '$assetId' is claimed more than once."
            Assert-Condition ($fixtureById.ContainsKey($assetId)) "Support asset '$assetId' is missing from fixture."
            $fixtureAsset = $fixtureById[$assetId]
            Assert-Condition ([string]$assetReference.name -ceq [string]$fixtureAsset.name) "Support asset '$assetId' name differs from fixture."
            Assert-Condition ([string]$assetReference.sha256 -ceq [string]$fixtureAsset.sha256) "Support asset '$assetId' digest differs from exact fixture bytes."
            Assert-Condition (@($claim.durableEvidenceUrls) -ccontains [string]$fixtureAsset.downloadUrl) "Support claim '$($claim.id)' lacks exact asset URL for '$assetId'."
        }
    }

    foreach ($assetId in $canonicalPublicAssets) {
        Assert-Condition ($claimedAssets.Contains($assetId)) "Canonical public asset '$assetId' is absent from support claims."
    }
    Assert-Condition ($claimedAssets.Count -eq $canonicalPublicAssets.Count) 'Support claims do not cover canonical public assets exactly once.'
}

function Test-ObservedInventory {
    param(
        [Parameter(Mandatory = $true)][object]$ExpectedFixture,
        [Parameter(Mandatory = $true)][object]$ObservedFixture
    )

    Assert-Condition ($ObservedFixture.release.rawTag -ceq $ExpectedFixture.release.rawTag) 'Observed release tag differs from frozen fixture.'
    Assert-Condition ($ObservedFixture.release.sourceSha -ceq $ExpectedFixture.release.sourceSha) 'Observed release source SHA differs from frozen fixture.'
    Assert-Condition (@($ObservedFixture.assets).Count -eq @($ExpectedFixture.assets).Count) 'Observed asset count differs from frozen fixture.'

    $expectedByName = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ExpectedFixture.assets)) {
        Assert-Condition (-not $expectedByName.ContainsKey([string]$asset.name)) "Frozen fixture has duplicate observed name '$($asset.name)'."
        $expectedByName.Add([string]$asset.name, $asset)
    }

    $observedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @($ObservedFixture.assets)) {
        $name = [string]$asset.name
        Assert-Condition ($observedNames.Add($name)) "Observed inventory contains duplicate name '$name'."
        Assert-Condition ($expectedByName.ContainsKey($name)) "Observed inventory contains unexpected name '$name'."
        $expected = $expectedByName[$name]
        Assert-Condition ($name -ceq [string]$expected.name) "Observed asset name casing differs for '$name'."
        Assert-Condition ([long]$asset.size -eq [long]$expected.size) "Observed asset '$name' size differs from frozen fixture."
        Assert-Condition ([string]$asset.sha256 -ceq [string]$expected.sha256) "Observed asset '$name' SHA-256 differs from frozen fixture."
    }

    foreach ($name in $expectedByName.Keys) {
        Assert-Condition ($observedNames.Contains($name)) "Observed inventory is missing '$name'."
    }
}

function Get-ExpectedChannel {
    param([Parameter(Mandatory = $true)][object]$FeedAsset)

    if ($FeedAsset.platform -ceq 'windows') {
        return 'windows'
    }
    if ($FeedAsset.platform -ceq 'linux') {
        return 'linux'
    }
    if ($FeedAsset.platform -ceq 'macos' -and $FeedAsset.architecture -ceq 'x64') {
        return 'macos-x64'
    }
    if ($FeedAsset.platform -ceq 'macos' -and $FeedAsset.architecture -ceq 'arm64') {
        return 'macos-arm64'
    }
    throw "Cannot derive feed channel for '$($FeedAsset.id)'."
}

function Get-FeedRecord {
    param(
        [Parameter(Mandatory = $true)][object]$Feed,
        [Parameter(Mandatory = $true)][object]$PackageAsset
    )

    if ($Feed.format -ceq 'velopack-json-v1') {
        $document = [string]$Feed.content | ConvertFrom-Json -Depth 20 -ErrorAction Stop
        $records = @($document.Assets | Where-Object { $_.FileName -ceq $PackageAsset.name })
        Assert-Condition ($records.Count -eq 1) "Feed '$($Feed.assetId)' must contain package '$($PackageAsset.name)' exactly once."
        $record = $records[0]
        return [pscustomobject]@{
            packageId = [string]$record.PackageId
            version = [string]$record.Version
            type = [string]$record.Type
            filename = [string]$record.FileName
            sha1 = ([string]$record.SHA1).ToLowerInvariant()
            sha256 = ([string]$record.SHA256).ToLowerInvariant()
            size = [long]$record.Size
        }
    }

    if ($Feed.format -ceq 'squirrel-releases-v1') {
        $content = ([string]$Feed.content).TrimStart([char[]]@([char]0xfeff)).Trim()
        $records = @()
        foreach ($line in @($content -split '\r?\n')) {
            $match = [regex]::Match($line, '^([0-9A-Fa-f]{40})\s+(\S+)\s+([0-9]+)$')
            Assert-Condition ($match.Success) "Legacy feed '$($Feed.assetId)' has invalid line '$line'."
            if ($match.Groups[2].Value -ceq $PackageAsset.name) {
                $records += [pscustomobject]@{
                    packageId = 'Unlimotion'
                    version = $null
                    type = 'Full'
                    filename = $match.Groups[2].Value
                    sha1 = $match.Groups[1].Value.ToLowerInvariant()
                    sha256 = $null
                    size = [long]$match.Groups[3].Value
                }
            }
        }
        Assert-Condition ($records.Count -eq 1) "Legacy feed '$($Feed.assetId)' must contain package '$($PackageAsset.name)' exactly once."
        return $records[0]
    }

    throw "Unknown feed format '$($Feed.format)'."
}

function Test-FeedRelations {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument
    )

    $manifestById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ManifestDocument.assets)) {
        $manifestById.Add([string]$asset.id, $asset)
    }
    $fixtureById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($FixtureDocument.assets)) {
        $fixtureById.Add([string]$asset.assetId, $asset)
    }
    $feedById = New-OrdinalIgnoreCaseMap
    foreach ($feed in @($FixtureDocument.feeds)) {
        Assert-Condition (-not $feedById.ContainsKey([string]$feed.assetId)) "Duplicate feed payload '$($feed.assetId)'."
        $feedById.Add([string]$feed.assetId, $feed)

        Assert-Condition ($fixtureById.ContainsKey([string]$feed.assetId)) "Feed payload '$($feed.assetId)' has no fixture asset."
        $feedAsset = $fixtureById[[string]$feed.assetId]
        $bytes = Get-Utf8Bytes -Value ([string]$feed.content)
        Assert-Condition ($bytes.Length -eq [long]$feedAsset.size) "Feed '$($feed.assetId)' byte length differs from fixture."
        Assert-Condition ((Get-BytesSha256 -Bytes $bytes) -ceq [string]$feedAsset.sha256) "Feed '$($feed.assetId)' content digest differs from fixture."
    }

    foreach ($relation in @($ManifestDocument.relations)) {
        $feedId = [string]$relation.feedAssetId
        $packageId = [string]$relation.packageAssetId
        Assert-Condition ($feedById.ContainsKey($feedId)) "Relation '$($relation.id)' has no frozen feed payload."
        Assert-Condition ($fixtureById.ContainsKey($packageId)) "Relation '$($relation.id)' has no frozen package asset."
        $feedManifestAsset = $manifestById[$feedId]
        $packageManifestAsset = $manifestById[$packageId]
        $packageFixtureAsset = $fixtureById[$packageId]
        $feed = $feedById[$feedId]

        Assert-Condition ($feed.format -ceq $relation.format) "Relation '$($relation.id)' format differs from feed payload."
        Assert-Condition ($relation.channel -ceq (Get-ExpectedChannel -FeedAsset $feedManifestAsset)) "Relation '$($relation.id)' channel does not match platform/architecture."
        Assert-Condition ($feedManifestAsset.platform -ceq $packageManifestAsset.platform) "Relation '$($relation.id)' crosses platforms."
        Assert-Condition ($feedManifestAsset.architecture -ceq $packageManifestAsset.architecture) "Relation '$($relation.id)' crosses architectures."

        $record = Get-FeedRecord -Feed $feed -PackageAsset $packageFixtureAsset
        Assert-Condition ($record.packageId -ceq $relation.packageId) "Relation '$($relation.id)' package id mismatch."
        Assert-Condition ($record.type -ceq 'Full') "Relation '$($relation.id)' must point to a Full package."
        Assert-Condition ($record.filename -ceq $packageFixtureAsset.name) "Relation '$($relation.id)' filename mismatch."
        Assert-Condition ($record.size -eq [long]$packageFixtureAsset.size) "Relation '$($relation.id)' size mismatch."
        Assert-Condition ($record.sha1 -ceq [string]$packageFixtureAsset.sha1) "Relation '$($relation.id)' SHA-1 mismatch."
        if ($relation.format -ceq 'velopack-json-v1') {
            Assert-Condition ($record.version -ceq $FixtureDocument.release.normalizedVersion) "Relation '$($relation.id)' version mismatch."
            Assert-Condition ($record.sha256 -ceq [string]$packageFixtureAsset.sha256) "Relation '$($relation.id)' SHA-256 mismatch."
        }
        if ($relation.hashAlgorithm -ceq 'sha256') {
            Assert-Condition ($record.sha256 -ceq [string]$packageFixtureAsset.sha256) "Relation '$($relation.id)' configured SHA-256 mismatch."
        }
        elseif ($relation.hashAlgorithm -ceq 'sha1') {
            Assert-Condition ($record.sha1 -ceq [string]$packageFixtureAsset.sha1) "Relation '$($relation.id)' configured SHA-1 mismatch."
        }
        else {
            throw "Relation '$($relation.id)' has unsupported hash algorithm."
        }
    }
}

function Set-FeedContentAndRefreshDigest {
    param(
        [Parameter(Mandatory = $true)][object]$FixtureDocument,
        [Parameter(Mandatory = $true)][string]$FeedAssetId,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $feed = @($FixtureDocument.feeds | Where-Object { $_.assetId -ceq $FeedAssetId })
    Assert-Condition ($feed.Count -eq 1) "Cannot mutate unknown feed '$FeedAssetId'."
    $fixtureAsset = @($FixtureDocument.assets | Where-Object { $_.assetId -ceq $FeedAssetId })
    Assert-Condition ($fixtureAsset.Count -eq 1) "Cannot refresh unknown feed asset '$FeedAssetId'."
    $feed[0].content = $Content
    $bytes = Get-Utf8Bytes -Value $Content
    $fixtureAsset[0].size = [long]$bytes.Length
    $fixtureAsset[0].sha256 = Get-BytesSha256 -Bytes $bytes
}

function Invoke-ResolverDocument {
    param(
        [Parameter(Mandatory = $true)][string]$RawTag,
        [string]$SourceSha = '0123456789abcdef0123456789abcdef01234567',
        [string]$WorkflowSha = '89abcdef0123456789abcdef0123456789abcdef',
        [ValidateSet('notApplicable', 'required')][string]$TagBinding = 'notApplicable',
        [long]$AndroidVersionCode = 1,
        [ValidateSet('ci-test', 'production-monotonic')][string]$AndroidVersionCodePolicy = 'ci-test',
        [switch]$IncludeSupportMatrix
    )

    $arguments = @(
        '-NoProfile',
        '-File', $script:resolverPath,
        '-RawTag', $RawTag,
        '-SourceSha', $SourceSha,
        '-WorkflowSha', $WorkflowSha,
        '-TagBinding', $TagBinding,
        '-AndroidVersionCode', [string]$AndroidVersionCode,
        '-AndroidVersionCodePolicy', $AndroidVersionCodePolicy,
        '-Manifest', $script:manifestPath)
    if ($IncludeSupportMatrix) {
        $arguments += @('-SupportMatrix', $script:supportMatrixPath)
    }

    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Resolver failed: $($output -join [Environment]::NewLine)"
    }
    try {
        return ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100 -ErrorAction Stop
    }
    catch {
        throw "Resolver returned invalid JSON: $($_.Exception.Message)"
    }
}

function Test-IdentityContract {
    $numeric = Invoke-ResolverDocument -RawTag '1.2.3' -IncludeSupportMatrix
    $prefixed = Invoke-ResolverDocument -RawTag 'v1.2.3' -IncludeSupportMatrix
    Assert-Condition ($numeric.rawTag -ceq '1.2.3') 'Resolver must preserve numeric raw tag.'
    Assert-Condition ($prefixed.rawTag -ceq 'v1.2.3') 'Resolver must preserve v-prefixed raw tag.'
    Assert-Condition ($numeric.normalizedVersion -ceq '1.2.3') 'Numeric tag normalized version mismatch.'
    Assert-Condition ($prefixed.normalizedVersion -ceq '1.2.3') 'Prefixed tag normalized version mismatch.'
    Assert-Condition (
        (($numeric.filenamePlan | ConvertTo-Json -Depth 30 -Compress) -ceq ($prefixed.filenamePlan | ConvertTo-Json -Depth 30 -Compress))) `
        'Dual raw tag forms must generate identical filename plans.'
    Assert-Condition (@($numeric.filenamePlan.byAssetId.PSObject.Properties).Count -eq 22) 'Resolver filename plan must cover 22 assets.'
    foreach ($property in @($numeric.filenamePlan.byAssetId.PSObject.Properties)) {
        Assert-Condition ([string]$property.Value -cnotmatch 'v1\.2\.3') "Raw v tag leaked into filename '$($property.Value)'."
    }
    Assert-Condition ($numeric.sourceSha -ceq '0123456789abcdef0123456789abcdef01234567') 'Resolver source SHA mismatch.'
    Assert-Condition ($numeric.workflowSha -ceq '89abcdef0123456789abcdef0123456789abcdef') 'Resolver workflow SHA mismatch.'
    Assert-Condition ($numeric.androidVersionCode -eq 1) 'Resolver ci-test Android versionCode mismatch.'
    Assert-Condition ($numeric.lastPublishedAndroidVersionCode -eq 353) 'Resolver must report last published Android versionCode.'
    Assert-Condition ($numeric.signatureProfile -ceq 'test') 'ci-test resolver output must use test signature profile.'
    Assert-Condition (-not [bool]$numeric.productionVersionCodeMonotonic) 'ci-test version must not claim production monotonicity.'

    foreach ($invalidTag in @('V1.2.3', '01.2.3', '1.02.3', '1.2.03', '1.2', '1.2.3-rc.1', '1.2.3+build', '0.0.0')) {
        $capturedTag = $invalidTag
        Assert-Throws -Name "invalid-tag-$capturedTag" -Action {
            Invoke-ResolverDocument -RawTag $capturedTag -IncludeSupportMatrix
        }
    }
    Assert-Throws -Name 'uppercase-source-sha' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -SourceSha ('A' * 40) -IncludeSupportMatrix
    }
    Assert-Throws -Name 'android-version-code-zero' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -AndroidVersionCode 0 -IncludeSupportMatrix
    }
    Assert-Throws -Name 'android-version-code-overflow' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -AndroidVersionCode 2100000001 -IncludeSupportMatrix
    }
    Assert-Throws -Name 'ci-test-tag-binding-required' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -IncludeSupportMatrix
    }
    Assert-Throws -Name 'production-tag-binding-not-applicable' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding notApplicable -AndroidVersionCode 354 -AndroidVersionCodePolicy production-monotonic -IncludeSupportMatrix
    }
    Assert-Throws -Name 'production-support-matrix-missing' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -AndroidVersionCode 354 -AndroidVersionCodePolicy production-monotonic
    }
    Assert-Throws -Name 'production-version-code-not-monotonic' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -AndroidVersionCode 353 -AndroidVersionCodePolicy production-monotonic -IncludeSupportMatrix
    }

    $production = Invoke-ResolverDocument `
        -RawTag 'v1.2.3' `
        -TagBinding required `
        -AndroidVersionCode 354 `
        -AndroidVersionCodePolicy production-monotonic `
        -IncludeSupportMatrix
    Assert-Condition ($production.signatureProfile -ceq 'production') 'Monotonic production identity must use production signature profile.'
    Assert-Condition ([bool]$production.productionVersionCodeMonotonic) 'Monotonic production identity must report monotonicity.'
    Assert-Condition ($production.androidVersionCodeSource -ceq 'stage4-production-allocator') 'Production version code source must be Stage-4 allocator.'
}

function Test-RetryContract {
    param([Parameter(Mandatory = $true)][object]$ManifestDocument)

    $expected = [ordered]@{
        deterministic = [ordered]@{ maxAttempts = 1; cleanup = 'none'; retryableClassification = 'never' }
        aptNetwork = [ordered]@{ maxAttempts = 3; cleanup = 'new-container'; retryableClassification = 'infrastructure-only' }
        emulatorBoot = [ordered]@{ maxAttempts = 2; cleanup = 'kill-emulator-delete-avd-data-use-new-port'; retryableClassification = 'infrastructure-only' }
        artifactTransport = [ordered]@{ maxAttempts = 2; cleanup = 'clean-extraction-directory'; retryableClassification = 'infrastructure-only' }
    }
    foreach ($property in $expected.GetEnumerator()) {
        $actualRule = $ManifestDocument.retryPolicy.($property.Key)
        Assert-Condition ($actualRule.maxAttempts -eq $property.Value.maxAttempts) "Retry maxAttempts drift for '$($property.Key)'."
        Assert-Condition ($actualRule.cleanup -ceq $property.Value.cleanup) "Retry cleanup drift for '$($property.Key)'."
        Assert-Condition ($actualRule.retryableClassification -ceq $property.Value.retryableClassification) "Retry classification drift for '$($property.Key)'."
    }

    $classificationFixtures = @(
        @{ name = 'metadata-failure'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'package-install-after-network'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'apt-mirror-network'; rule = 'aptNetwork'; attempts = 3 },
        @{ name = 'emulator-boot-timeout'; rule = 'emulatorBoot'; attempts = 2 },
        @{ name = 'emulator-install-failure'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'artifact-download-service-outage'; rule = 'artifactTransport'; attempts = 2 },
        @{ name = 'artifact-upload-action'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'artifact-hash-mismatch'; rule = 'deterministic'; attempts = 1 }
    )
    foreach ($fixture in $classificationFixtures) {
        $rule = $ManifestDocument.retryPolicy.($fixture.rule)
        Assert-Condition ($rule.maxAttempts -eq $fixture.attempts) "Retry fixture '$($fixture.name)' has wrong budget."
    }
}

function Get-IdentityProjectionForEvidence {
    param([Parameter(Mandatory = $true)][object]$Identity)
    return [ordered]@{
        rawTag = [string]$Identity.rawTag
        normalizedVersion = [string]$Identity.normalizedVersion
        sourceSha = [string]$Identity.sourceSha
        workflowSha = [string]$Identity.workflowSha
        tagBinding = [string]$Identity.tagBinding
        manifestSha256 = [string]$Identity.manifestSha256
        supportMatrixSha256 = [string]$Identity.supportMatrixSha256
        signatureProfile = [string]$Identity.signatureProfile
    }
}

function New-NotApplicableUnixEvidence {
    return [ordered]@{
        applicability = 'notApplicable'
        assetId = $null
        archiveFileName = $null
        archiveSha256 = $null
        archiveEntry = $null
        originalMode = $null
        tarStoredMode = $null
        restoredMode = $null
        originalSha256 = $null
        restoredSha256 = $null
    }
}

function New-NativeCellFixture {
    param([Parameter(Mandatory = $true)][string]$Id)
    $common = [ordered]@{
        id = $Id
        status = 'pass'
        platform = ''
        architecture = ''
        osName = ''
        osVersion = ''
        mode = ''
        metadata = 'pass'
        install = 'pass'
        launch = 'pass'
        signature = 'notApplicable'
        negativeControl = 'notApplicable'
        directFuse = 'notApplicable'
        evidenceFile = "$Id.json"
        evidenceSha256 = 'b' * 64
        assetIds = @('fixture-asset')
    }
    if ($Id -ceq 'windows-server-2022-x64') {
        $common.platform = 'windows'; $common.architecture = 'x64'; $common.osName = 'Windows Server'; $common.osVersion = '2022'
        $common.mode = 'setup-and-portable-install-launch-with-seeded-isolated-task-storage'; $common.signature = 'stateRecorded'; $common.assetIds = @('windows-setup-x64', 'windows-portable-x64')
    }
    elseif ($Id -match '^debian-(12|13)-x64-(clean|upgrade|appimage|missing-runtime-negative)$') {
        $linuxMode = $Matches[2]
        $common.platform = 'linux'; $common.architecture = 'x64'; $common.osName = 'debian'; $common.osVersion = $Matches[1]; $common.mode = "$linuxMode-with-seeded-isolated-task-storage"
        if ($linuxMode -ceq 'appimage') {
            $common.assetIds = [string[]]@('linux-deb-x64', 'linux-appimage-x64')
            $common.install = 'extractPassed'; $common.directFuse = 'notVerified'
        }
        elseif ($linuxMode -ceq 'missing-runtime-negative') {
            $common.assetIds = [string[]]@('linux-deb-x64')
            $common.launch = 'expectedFailureObserved'; $common.negativeControl = 'pass'
        }
        else { $common.assetIds = [string[]]@('linux-deb-x64') }
    }
    elseif ($Id -match '^macos-15-(x64|arm64)$') {
        $common.platform = 'macos'; $common.architecture = $Matches[1]; $common.osName = 'macOS'; $common.osVersion = '15'
        $common.mode = 'package-and-portable-native-launch-with-seeded-isolated-task-storage'; $common.signature = 'stateRecorded'; $common.assetIds = @("macos-$($Matches[1])-setup", "macos-$($Matches[1])-portable")
    }
    elseif ($Id -match '^android-(arm64|x64)-apk-metadata$') {
        $common.platform = 'android'; $common.architecture = $Matches[1]; $common.osName = 'android'; $common.osVersion = 'notApplicable'
        $common.mode = 'apk-metadata'; $common.install = 'notApplicable'; $common.launch = 'notApplicable'; $common.signature = 'stateRecorded'
        $common.assetIds = @("android-$($Matches[1])-apk")
        $common.evidenceFile = 'android-artifact.json'
    }
    elseif ($Id -match '^android-api-(23|36)-x64-emulator$') {
        $common.platform = 'android'; $common.architecture = 'x64'; $common.osName = 'android'; $common.osVersion = "API $($Matches[1])"
        $common.mode = 'emulator'; $common.signature = 'coveredByArtifactCell'; $common.assetIds = @('android-x64-apk')
        $common.evidenceFile = "android-api$($Matches[1])-emulator.json"
    }
    else { throw "Unknown native cell fixture id '$Id'." }
    return $common
}

function New-PlatformEvidenceFixtures {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$IdentityDocument
    )
    $identity = Get-IdentityProjectionForEvidence -Identity $IdentityDocument
    $cellGroups = [ordered]@{
        'windows/x64' = @('windows-server-2022-x64')
        'linux/x64' = @(
            'debian-12-x64-clean', 'debian-12-x64-upgrade', 'debian-12-x64-appimage', 'debian-12-x64-missing-runtime-negative',
            'debian-13-x64-clean', 'debian-13-x64-upgrade', 'debian-13-x64-appimage', 'debian-13-x64-missing-runtime-negative')
        'macos/x64' = @('macos-15-x64')
        'macos/arm64' = @('macos-15-arm64')
        'android/multi' = @('android-arm64-apk-metadata', 'android-x64-apk-metadata', 'android-api-23-x64-emulator', 'android-api-36-x64-emulator')
    }
    $fixtures = [System.Collections.Generic.List[object]]::new()
    $artifactId = 100
    foreach ($entry in $cellGroups.GetEnumerator()) {
        $platform, $architecture = $entry.Key -split '/'
        $assets = @($ManifestDocument.assets | Where-Object {
            $_.platform -ceq $platform -and ($architecture -ceq 'multi' -or $_.architecture -ceq $architecture)
        } | ForEach-Object {
            [ordered]@{
                assetId = [string]$_.id
                fileName = [string]$IdentityDocument.filenamePlan.byAssetId.($_.id)
                size = 1
                sha256 = 'a' * 64
            }
        })
        $relations = @($ManifestDocument.relations | Where-Object {
            $feed = @($ManifestDocument.assets | Where-Object id -CEQ $_.feedAssetId)[0]
            $feed.platform -ceq $platform -and ($architecture -ceq 'multi' -or $feed.architecture -ceq $architecture)
        } | ForEach-Object {
            [ordered]@{
                relationId = [string]$_.id
                feedAssetId = [string]$_.feedAssetId
                packageAssetId = [string]$_.packageAssetId
                channel = [string]$_.channel
                format = [string]$_.format
                packageSha1 = 'c' * 40
                packageSha256 = 'a' * 64
                packageSize = 1
                status = 'pass'
            }
        })
        $architectures = if ($architecture -ceq 'multi') { @('arm64', 'x64') } else { @($architecture) }
        $preEvidence = @($architectures | ForEach-Object {
            [ordered]@{
                evidenceId = "$platform-$_"
                fileName = "artifact-evidence-$platform-$_.json"
                sha256 = 'd' * 64
                platform = $platform
                architecture = $_
            }
        })
        $unixMode = if ($platform -ceq 'linux') {
            [ordered]@{
                applicability = 'required'
                assetId = 'linux-appimage-x64'
                archiveFileName = 'distribution-linux-x64.tar'
                archiveSha256 = 'e' * 64
                archiveEntry = 'assets/Unlimotion.AppImage'
                originalMode = '0755'
                tarStoredMode = '0755'
                restoredMode = '0755'
                originalSha256 = 'a' * 64
                restoredSha256 = 'a' * 64
            }
        } else { New-NotApplicableUnixEvidence }
        $transport = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-transport-receipt'
            status = 'pass'
            platform = $platform
            architecture = $architecture
            identity = $identity
            artifactName = "distribution-$platform-$architecture-fixture"
            artifactId = [string]$artifactId
            artifactDigest = 'f' * 64
            retentionDays = 7
            ifNoFilesFound = 'error'
            overwrite = $false
            preEvidence = $preEvidence
            unixMode = $unixMode
            productionReady = $false
        }
        $nativeCells = @($entry.Value | ForEach-Object { New-NativeCellFixture -Id $_ })
        $nativeEvidence = if ($entry.Key -ceq 'android/multi') {
            @(
                [ordered]@{ fileName = 'android-artifact.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'artifact' },
                [ordered]@{ fileName = 'native-cache-evidence.json'; sha256 = '2' * 64; kind = 'distribution-android-native-evidence'; mode = 'provenance' },
                [ordered]@{ fileName = 'native-inputs.json'; sha256 = '5' * 64; kind = 'distribution-android-native-inputs'; mode = 'native-inputs' },
                [ordered]@{ fileName = 'native-provenance.json'; sha256 = '6' * 64; kind = 'distribution-android-native-provenance'; mode = 'native-provenance' },
                [ordered]@{ fileName = 'android-api23-emulator.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'emulator' },
                [ordered]@{ fileName = 'android-api36-emulator.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'emulator' },
                [ordered]@{ fileName = 'android-api23-download-transport.json'; sha256 = '3' * 64; kind = 'distribution-download-transport'; mode = 'distribution-download-transport' },
                [ordered]@{ fileName = 'android-api36-download-transport.json'; sha256 = '4' * 64; kind = 'distribution-download-transport'; mode = 'distribution-download-transport' }
            )
        }
        else {
            @($nativeCells | ForEach-Object {
                [ordered]@{
                    fileName = [string]$_.evidenceFile
                    sha256 = [string]$_.evidenceSha256
                    kind = 'fixture-native-evidence'
                    mode = [string]$_.mode
                }
            })
        }
        $fixtures.Add([ordered]@{
            schemaVersion = 1
            kind = 'distribution-platform-evidence'
            status = 'pass'
            platform = $platform
            architecture = $architecture
            identity = $identity
            artifactEvidence = $preEvidence
            nativeEvidence = @($nativeEvidence)
            transportReceiptFile = "transport-receipt-$platform-$architecture.json"
            transportReceiptSha256 = '1' * 64
            transport = $transport
            artifacts = $assets
            relations = $relations
            nativeCells = @($nativeCells)
            releasePromotion = 'notApplicable'
            productionSignatureEligibility = 'notApplicable'
            productionReady = $false
        })
        $artifactId++
    }
    return @($fixtures)
}

function Invoke-AggregateFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$IdentityDocument,
        [Parameter(Mandatory = $true)][object[]]$Platforms,
        [string[]]$RequestedAssetIds
    )
    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $identityPath = Join-Path $Root 'identity.json'
    [System.IO.File]::WriteAllText($identityPath, ($IdentityDocument | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
    $platformPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($platform in $Platforms) {
        $path = Join-Path $Root "platform-$($platform.platform)-$($platform.architecture).json"
        [System.IO.File]::WriteAllText($path, ($platform | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
        $platformPaths.Add($path)
    }
    $arguments = @(
        '-NoProfile', '-File', $script:artifactValidatorPath,
        '-Mode', 'Aggregate',
        '-Manifest', $script:manifestPath,
        '-SupportMatrix', $script:supportMatrixPath,
        '-EvidenceSchema', $script:evidenceSchemaPath,
        '-Identity', $identityPath,
        '-EvidencePath', ($platformPaths -join ','),
        '-OutputChecksums', (Join-Path $Root 'SHA256SUMS.txt'),
        '-Evidence', (Join-Path $Root 'distribution-evidence.json'))
    if ($null -ne $RequestedAssetIds) { $arguments += @('-AssetId', ($RequestedAssetIds -join ',')) }
    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Aggregate fixture failed: $($output -join [Environment]::NewLine)" }
    return Read-JsonFile -Path (Join-Path $Root 'distribution-evidence.json') -DisplayName 'Aggregate fixture output'
}

function Test-AndroidNativeEvidenceConverters {
    param([Parameter(Mandatory = $true)][object]$IdentityDocument)

    try {
        . $script:artifactValidatorPath `
            -Mode Validate `
            -Manifest $script:manifestPath `
            -SupportMatrix $script:supportMatrixPath `
            -EvidenceSchema $script:evidenceSchemaPath
    }
    catch {
        if ($_.Exception.Message -notlike 'Validate mode requires*') { throw }
    }

    function New-AndroidRawEvidenceBase {
        param([Parameter(Mandatory = $true)][string]$NativeMode)
        return [pscustomobject][ordered]@{
            schemaVersion = 1
            kind = 'distribution-android-native-evidence'
            outcome = 'passed'
            productionReady = $false
            mode = $NativeMode
            rawTag = [string]$IdentityDocument.rawTag
            normalizedVersion = [string]$IdentityDocument.normalizedVersion
            sourceSha = [string]$IdentityDocument.sourceSha
            workflowSha = [string]$IdentityDocument.workflowSha
            tagBinding = [string]$IdentityDocument.tagBinding
            manifestSha256 = [string]$IdentityDocument.manifestSha256
            supportMatrixSha256 = [string]$IdentityDocument.supportMatrixSha256
            signatureProfile = [string]$IdentityDocument.signatureProfile
            androidVersionCode = [int]$IdentityDocument.androidVersionCode
            androidVersionCodePolicy = [string]$IdentityDocument.androidVersionCodePolicy
        }
    }

    $identityProjection = Copy-JsonObject (Get-IdentityProjectionForEvidence -Identity $IdentityDocument)
    $apkName = [string]$IdentityDocument.filenamePlan.byAssetId.'android-x64-apk'
    $apkSha256 = '4' * 64
    $artifactsByName = @{
        $apkName = [pscustomobject]@{ assetId = 'android-x64-apk'; sha256 = $apkSha256 }
    }
    $expectedTransportName = 'distribution-android-multi-fixture'
    $expectedTransportId = '123'
    $expectedTransportDigest = 'f' * 64

    $provenance = New-AndroidRawEvidenceBase -NativeMode provenance
    $provenance | Add-Member -NotePropertyName nativeInputDigest -NotePropertyValue ('5' * 64)
    $provenance | Add-Member -NotePropertyName nativeInputsSha256 -NotePropertyValue ('5' * 64)
    $provenance | Add-Member -NotePropertyName nativeProvenanceSha256 -NotePropertyValue ('6' * 64)
    $provenance | Add-Member -NotePropertyName outputClosureSha256 -NotePropertyValue ('7' * 64)
    $provenance | Add-Member -NotePropertyName requestedCacheKey -NotePropertyValue ('android-native-v2-linux-x64-' + ('5' * 64))
    $provenance | Add-Member -NotePropertyName matchedCacheKey -NotePropertyValue ([string]$provenance.requestedCacheKey)
    $provenance | Add-Member -NotePropertyName androidApiLevel -NotePropertyValue 23
    $provenance | Add-Member -NotePropertyName outputCount -NotePropertyValue 4
    $provenance | Add-Member -NotePropertyName cacheHit -NotePropertyValue $true
    $provenance | Add-Member -NotePropertyName cacheSave -NotePropertyValue $false

    $inputFileSha256 = [ordered]@{}
    foreach ($relative in @(
        'scripts/android-native-common.sh',
        'scripts/build-openssl-android.sh',
        'scripts/build-libssh2-android.sh',
        'scripts/build-libgit2-android.sh',
        'scripts/pack-libgit2sharp-nativebinaries-android.sh',
        'scripts/build-android-distribution.sh',
        'src/Unlimotion.Android/Unlimotion.Android.csproj',
        'src/Directory.Packages.props',
        'src/nuget.config'
    )) {
        $inputFileSha256[$relative] = 'd' * 64
    }
    $nativeInputs = [pscustomobject][ordered]@{
        schemaVersion = 1
        androidApiLevel = 23
        ndkRevision = '27.2.12479018'
        host = [pscustomobject][ordered]@{
            os = 'Linux'; arch = 'X64'; toolchainTriples = @('aarch64-linux-android', 'x86_64-linux-android')
        }
        abis = @('arm64-v8a', 'x86_64')
        sources = [pscustomobject][ordered]@{
            openssl = [pscustomobject][ordered]@{ version = '3.0.14'; url = 'https://example.invalid/openssl.tar.gz'; sha256 = '1' * 64 }
            libssh2 = [pscustomobject][ordered]@{ version = '1.11.1'; url = 'https://example.invalid/libssh2.tar.gz'; sha256 = '2' * 64 }
            libgit2Commit = '3' * 40
            upstreamNativePackage = [pscustomobject][ordered]@{ version = '2.0.324'; url = 'https://example.invalid/native.nupkg'; sha256 = '3' * 64 }
        }
        nativePackageVersion = '2.0.324-android.7'
        inputFileSha256 = [pscustomobject]$inputFileSha256
    }
    $rawProvenance = [pscustomobject][ordered]@{
        schemaVersion = 1
        nativeInputDigest = '5' * 64
        requestedCacheKey = 'android-native-v2-linux-x64-' + ('5' * 64)
        matchedCacheKey = 'android-native-v2-linux-x64-' + ('5' * 64)
        inputs = $nativeInputs
        outputs = @(
            [pscustomobject][ordered]@{ path = 'nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg'; size = 128; sha256 = 'e' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/Z.so'; size = 2; sha256 = 'a' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/a.so'; size = 3; sha256 = 'b' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/_fixture.so'; size = 4; sha256 = 'c' * 64 }
        )
    }
    $fixtureOutputClosure = Assert-AndroidNativeProvenanceDocument -Report $rawProvenance
    Assert-Condition ($fixtureOutputClosure -ceq '6a007c20e330e21e70e73a0cd1f26126307a8efe33fa3480d18ca0cd82507910') `
        'Android raw provenance output closure must use Python-compatible ordinal path/key canonicalization.'
    $provenance.outputClosureSha256 = $fixtureOutputClosure
    $provenanceCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $provenance; sha256 = '8' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($provenanceCells.Count -eq 0) 'Android provenance evidence must validate without producing a native cell.'
    Add-Check -Name 'evidence:android-provenance-raw-converter'

    $stringOutputCount = Copy-JsonObject $provenance
    $stringOutputCount.outputCount = '4'
    Assert-Throws -Name 'android-provenance-string-output-count' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringOutputCount; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $numericCacheHit = Copy-JsonObject $provenance
    $numericCacheHit.cacheHit = 1
    Assert-Throws -Name 'android-provenance-numeric-cache-boolean' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericCacheHit; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $nativeInputsEvidence = [pscustomobject]@{ report = $nativeInputs; sha256 = '5' * 64 }
    $nativeProvenanceEvidence = [pscustomobject]@{ report = $rawProvenance; sha256 = '6' * 64 }
    $nativeInputCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence $nativeInputsEvidence `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-inputs.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    $nativeProvenanceCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence $nativeProvenanceEvidence `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-provenance.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($nativeInputCells.Count -eq 0 -and $nativeProvenanceCells.Count -eq 0) `
        'Android raw native input/provenance documents must validate without producing native cells.'

    $stringNativeInputsSchema = Copy-JsonObject $nativeInputs
    $stringNativeInputsSchema.schemaVersion = '1'
    Assert-Throws -Name 'android-native-inputs-string-schema-version' -Action {
        Assert-AndroidNativeInputsDocument -Report $stringNativeInputsSchema
    }

    $scalarAbis = Copy-JsonObject $nativeInputs
    $scalarAbis.abis = 'arm64-v8a|x86_64'
    Assert-Throws -Name 'android-native-inputs-scalar-abis' -Action {
        Assert-AndroidNativeInputsDocument -Report $scalarAbis
    }

    $scalarToolchainTriples = Copy-JsonObject $nativeInputs
    $scalarToolchainTriples.host.toolchainTriples = 'aarch64-linux-android|x86_64-linux-android'
    Assert-Throws -Name 'android-native-inputs-scalar-toolchain-triples' -Action {
        Assert-AndroidNativeInputsDocument -Report $scalarToolchainTriples
    }

    $scalarProvenanceOutputs = Copy-JsonObject $rawProvenance
    $scalarProvenanceOutputs.outputs = 'android-native/a.so'
    Assert-Throws -Name 'android-native-provenance-scalar-outputs' -Action {
        Assert-AndroidNativeProvenanceDocument -Report $scalarProvenanceOutputs
    }
    $nativeInputsReference = New-NativeEvidenceReference -InputEvidence ([pscustomobject]@{
        report = $nativeInputs; path = 'native-inputs.json'; sha256 = '5' * 64
    })
    $nativeProvenanceReference = New-NativeEvidenceReference -InputEvidence ([pscustomobject]@{
        report = $rawProvenance; path = 'native-provenance.json'; sha256 = '6' * 64
    })
    Assert-Condition ($nativeInputsReference.kind -ceq 'distribution-android-native-inputs' -and
        $nativeInputsReference.mode -ceq 'native-inputs' -and
        $nativeProvenanceReference.kind -ceq 'distribution-android-native-provenance' -and
        $nativeProvenanceReference.mode -ceq 'native-provenance') `
        'Android raw native documents must produce strict inferred platform-evidence references.'

    $artifactCacheEvidence = [pscustomobject]@{
        report = [pscustomobject]@{
            nativeCache = [pscustomobject][ordered]@{
                nativeInputDigest = '5' * 64
                requestedKey = 'android-native-v2-linux-x64-' + ('5' * 64)
                matchedKey = 'android-native-v2-linux-x64-' + ('5' * 64)
                hit = $true
                saveRequired = $false
            }
        }
    }
    $summaryEvidence = [pscustomobject]@{ report = $provenance }
    Assert-AndroidNativeProvenanceClosure `
        -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $summaryEvidence `
        -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    Add-Check -Name 'evidence:android-native-provenance-byte-closure'

    $badOutputCountSummary = Copy-JsonObject $summaryEvidence
    $badOutputCountSummary.report.outputCount = 5
    Assert-Throws -Name 'android-provenance-output-count-closure-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $badOutputCountSummary `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badArtifactCache = Copy-JsonObject $artifactCacheEvidence
    $badArtifactCache.report.nativeCache.nativeInputDigest = '0' * 64
    Assert-Throws -Name 'android-artifact-native-cache-digest-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $badArtifactCache -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $mutatedNativeInputs = Copy-JsonObject $nativeInputsEvidence
    $mutatedNativeInputs.report.sources.openssl.sha256 = '0' * 64
    $mutatedNativeInputs.sha256 = '0' * 64
    Assert-Throws -Name 'android-downloaded-native-inputs-mutation' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $mutatedNativeInputs -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badCacheSemantics = Copy-JsonObject $artifactCacheEvidence
    $badCacheSemantics.report.nativeCache.hit = $false
    $badCacheSemantics.report.nativeCache.saveRequired = $true
    Assert-Throws -Name 'android-artifact-native-cache-outcome-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $badCacheSemantics -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badCacheOutcome = Copy-JsonObject $provenance
    $badCacheOutcome.cacheHit = $false
    $badCacheOutcome.cacheSave = $false
    Assert-Throws -Name 'android-provenance-invalid-cache-outcome' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badCacheOutcome; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $downloadTransport = [pscustomobject][ordered]@{
        schemaVersion = 1
        kind = 'distribution-download-transport'
        status = 'pass'
        scope = 'android-api23'
        identity = $identityProjection
        sourceArtifact = [pscustomobject][ordered]@{
            name = $expectedTransportName
            id = $expectedTransportId
            digest = $expectedTransportDigest
        }
        retry = [pscustomobject][ordered]@{
            rule = 'bounded-clean-retry'
            classification = 'transient-transport'
            maxAttempts = 2
            firstOutcome = 'failure'
            secondOutcome = 'success'
            selectedAttempt = 2
            cleanupBeforeAttempt2 = 'completed'
            exhausted = $false
        }
        productionReady = $false
    }
    $transportCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $downloadTransport; sha256 = '9' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)

    $firstAttemptTransport = Copy-JsonObject $downloadTransport
    $firstAttemptTransport.retry.classification = 'none'
    $firstAttemptTransport.retry.firstOutcome = 'success'
    $firstAttemptTransport.retry.secondOutcome = 'skipped'
    $firstAttemptTransport.retry.selectedAttempt = 1
    $firstAttemptTransport.retry.cleanupBeforeAttempt2 = 'notRequired'
    $firstAttemptTransportCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $firstAttemptTransport; sha256 = '9' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($transportCells.Count -eq 0 -and $firstAttemptTransportCells.Count -eq 0) `
        'Both valid Android download retry outcomes must validate without producing a native cell.'
    Add-Check -Name 'evidence:android-download-transport-raw-converter'

    $numericTransportProductionReady = Copy-JsonObject $downloadTransport
    $numericTransportProductionReady.productionReady = 0
    Assert-Throws -Name 'android-transport-numeric-production-ready' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericTransportProductionReady; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $stringTransportAttempt = Copy-JsonObject $downloadTransport
    $stringTransportAttempt.retry.selectedAttempt = '2'
    Assert-Throws -Name 'android-transport-string-selected-attempt' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringTransportAttempt; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badTransportCleanup = Copy-JsonObject $downloadTransport
    $badTransportCleanup.retry.cleanupBeforeAttempt2 = 'notRequired'
    Assert-Throws -Name 'android-transport-retry-cleanup-missing' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badTransportCleanup; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badTransportSource = Copy-JsonObject $downloadTransport
    $badTransportSource.sourceArtifact.digest = 'e' * 64
    Assert-Throws -Name 'android-transport-source-artifact-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badTransportSource; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    Assert-Throws -Name 'android-transport-scope-filename-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $downloadTransport; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api36-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $emulator = New-AndroidRawEvidenceBase -NativeMode emulator
    $emulator | Add-Member -NotePropertyName supportLevel -NotePropertyValue launchVerified
    $emulator | Add-Member -NotePropertyName recordedAtUtc -NotePropertyValue '2026-07-19T12:00:00Z'
    $emulator | Add-Member -NotePropertyName asset -NotePropertyValue ([pscustomobject]@{
        name = $apkName; architecture = 'x86_64'; sha256Before = $apkSha256; sha256After = $apkSha256
    })
    $emulator | Add-Member -NotePropertyName runtime -NotePropertyValue ([pscustomobject][ordered]@{
        apiLevel = 23
        bootAttempts = 2
        maxBootAttempts = 2
        deviceFingerprint = 'google/sdk/test:userdebug/test-keys'
        deviceSdk = 23
        systemImagePackage = 'system-images;android-23;google_apis;x86_64'
        systemImageRevision = '10'
        serial = 'emulator-5554'
        applicationId = 'com.Kibnet.Unlimotion'
        activity = 'MainActivity'
        processId = '123'
        fatalLogcatEntries = 0
        logcat = [pscustomobject][ordered]@{
            fileName = 'android-api23-logcat.txt'; sha256 = 'b' * 64; bytes = 128
        }
        emulatorLog = [pscustomobject][ordered]@{
            fileName = 'android-api23-emulator.log'; sha256 = 'c' * 64; bytes = 256
        }
    })
    $emulator | Add-Member -NotePropertyName tools -NotePropertyValue ([pscustomobject]@{
        emulatorVersion = 'Android emulator version 36.4.9.0'
        adbVersion = 'Android Debug Bridge version 1.0.41'
        aaptVersion = 'Android Asset Packaging Tool, v0.2'
    })
    $emulator | Add-Member -NotePropertyName runner -NotePropertyValue ([pscustomobject]@{
        imageOs = 'ubuntu24'; imageVersion = '20260719.1.0'; uname = 'Linux runner'
    })
    $emulator | Add-Member -NotePropertyName bootRetry -NotePropertyValue ([pscustomobject][ordered]@{
        rule = 'bounded-clean-retry'
        classification = 'transient-emulator-boot'
        attempts = 2
        maxAttempts = 2
        cleanupBeforeAttempt2 = 'kill-delete-avd-remove-files-and-wipe-data'
        outcomes = @('failure', 'success')
        exhausted = $false
    })
    $emulatorCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $emulator; sha256 = 'a' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)

    $firstBootEmulator = Copy-JsonObject $emulator
    $firstBootEmulator.recordedAtUtc = '2026-07-19T12:00:00Z'
    $firstBootEmulator.runtime.bootAttempts = 1
    $firstBootEmulator.bootRetry.classification = 'none'
    $firstBootEmulator.bootRetry.attempts = 1
    $firstBootEmulator.bootRetry.cleanupBeforeAttempt2 = 'notRequired'
    $firstBootEmulator.bootRetry.outcomes = @('success')
    $firstBootEmulatorCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $firstBootEmulator; sha256 = 'a' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($emulatorCells.Count -eq 1 -and $firstBootEmulatorCells.Count -eq 1 -and
        [string]$emulatorCells[0].id -ceq 'android-api-23-x64-emulator' -and
        [string]$firstBootEmulatorCells[0].id -ceq 'android-api-23-x64-emulator') `
        'Both valid Android emulator boot outcomes must produce the exact API-23 native cell.'
    Add-Check -Name 'evidence:android-emulator-raw-converter'

    $stringEmulatorApi = Copy-JsonObject $emulator
    $stringEmulatorApi.recordedAtUtc = '2026-07-19T12:00:00Z'
    $stringEmulatorApi.runtime.apiLevel = '23'
    Assert-Throws -Name 'android-emulator-string-api-level' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringEmulatorApi; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $numericRetryExhausted = Copy-JsonObject $emulator
    $numericRetryExhausted.recordedAtUtc = '2026-07-19T12:00:00Z'
    $numericRetryExhausted.bootRetry.exhausted = 0
    Assert-Throws -Name 'android-emulator-numeric-retry-exhausted' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericRetryExhausted; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $scalarRetryOutcomes = Copy-JsonObject $emulator
    $scalarRetryOutcomes.recordedAtUtc = '2026-07-19T12:00:00Z'
    $scalarRetryOutcomes.bootRetry.outcomes = 'failure|success'
    Assert-Throws -Name 'android-emulator-scalar-retry-outcomes' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $scalarRetryOutcomes; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badDeviceSdk = Copy-JsonObject $emulator
    $badDeviceSdk.recordedAtUtc = '2026-07-19T12:00:00Z'
    $badDeviceSdk.runtime.deviceSdk = 24
    Assert-Throws -Name 'android-emulator-device-sdk-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badDeviceSdk; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    Assert-Throws -Name 'android-emulator-api-filename-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $emulator; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api36-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $mergeRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-android-merge-contract-' + [Guid]::NewGuid().ToString('N'))
    try {
        $producerRoot = Join-Path $mergeRoot 'producer'
        $assetsRoot = Join-Path $producerRoot 'assets'
        $artifactEvidenceRoot = Join-Path $producerRoot 'evidence'
        $nativeRoot = Join-Path $mergeRoot 'native'
        [IO.Directory]::CreateDirectory($assetsRoot) | Out-Null
        [IO.Directory]::CreateDirectory($artifactEvidenceRoot) | Out-Null
        [IO.Directory]::CreateDirectory($nativeRoot) | Out-Null
        $writeJson = {
            param([string]$Path, [object]$Value)
            [IO.File]::WriteAllText(
                $Path,
                (($Value | ConvertTo-Json -Depth 100 -Compress) + "`n"),
                [Text.UTF8Encoding]::new($false)
            )
        }

        $identityPath = Join-Path $mergeRoot 'identity.json'
        & $writeJson $identityPath $IdentityDocument
        $arm64Name = [string]$IdentityDocument.filenamePlan.byAssetId.'android-arm64-apk'
        $x64Name = [string]$IdentityDocument.filenamePlan.byAssetId.'android-x64-apk'
        $arm64Path = Join-Path $assetsRoot $arm64Name
        $x64Path = Join-Path $assetsRoot $x64Name
        [IO.File]::WriteAllText($arm64Path, 'arm64-apk-fixture', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($x64Path, 'x64-apk-fixture', [Text.UTF8Encoding]::new($false))
        $arm64Sha = Get-LowerFileSha256 -Path $arm64Path
        $x64Sha = Get-LowerFileSha256 -Path $x64Path

        $artifactEvidencePaths = [Collections.Generic.List[string]]::new()
        foreach ($artifactFixture in @(
            [pscustomobject]@{ Architecture = 'arm64'; AssetId = 'android-arm64-apk'; FileName = $arm64Name; Path = $arm64Path; Sha256 = $arm64Sha },
            [pscustomobject]@{ Architecture = 'x64'; AssetId = 'android-x64-apk'; FileName = $x64Name; Path = $x64Path; Sha256 = $x64Sha }
        )) {
            $artifactEvidence = [ordered]@{
                schemaVersion = 1
                kind = 'distribution-artifact-evidence'
                status = 'pass'
                platform = 'android'
                architecture = $artifactFixture.Architecture
                rawTag = [string]$IdentityDocument.rawTag
                normalizedVersion = [string]$IdentityDocument.normalizedVersion
                sourceSha = [string]$IdentityDocument.sourceSha
                workflowSha = [string]$IdentityDocument.workflowSha
                tagBinding = [string]$IdentityDocument.tagBinding
                manifestSha256 = [string]$IdentityDocument.manifestSha256
                supportMatrixSha256 = [string]$IdentityDocument.supportMatrixSha256
                identitySignatureProfile = [string]$IdentityDocument.signatureProfile
                signatureProfile = 'test'
                productionReady = $false
                artifacts = @([ordered]@{
                    assetId = $artifactFixture.AssetId
                    fileName = $artifactFixture.FileName
                    size = (Get-Item -LiteralPath $artifactFixture.Path).Length
                    sha256 = $artifactFixture.Sha256
                })
                relations = @()
            }
            $artifactEvidencePath = Join-Path $artifactEvidenceRoot "artifact-evidence-$($artifactFixture.Architecture).json"
            & $writeJson $artifactEvidencePath $artifactEvidence
            $artifactEvidencePaths.Add($artifactEvidencePath)
        }

        $integrationNativeInputs = Copy-JsonObject $nativeInputs
        $nativeInputsPath = Join-Path $nativeRoot 'native-inputs.json'
        & $writeJson $nativeInputsPath $integrationNativeInputs
        $nativeInputsSha = Get-LowerFileSha256 -Path $nativeInputsPath
        $integrationCacheKey = "android-native-v2-linux-x64-$nativeInputsSha"
        $integrationRawProvenance = Copy-JsonObject $rawProvenance
        $integrationRawProvenance.nativeInputDigest = $nativeInputsSha
        $integrationRawProvenance.requestedCacheKey = $integrationCacheKey
        $integrationRawProvenance.matchedCacheKey = $integrationCacheKey
        $integrationRawProvenance.inputs = $integrationNativeInputs
        $nativeProvenancePath = Join-Path $nativeRoot 'native-provenance.json'
        & $writeJson $nativeProvenancePath $integrationRawProvenance
        $nativeProvenanceSha = Get-LowerFileSha256 -Path $nativeProvenancePath

        $integrationSummary = Copy-JsonObject $provenance
        $integrationSummary.nativeInputDigest = $nativeInputsSha
        $integrationSummary.nativeInputsSha256 = $nativeInputsSha
        $integrationSummary.nativeProvenanceSha256 = $nativeProvenanceSha
        $integrationSummary.requestedCacheKey = $integrationCacheKey
        $integrationSummary.matchedCacheKey = $integrationCacheKey
        $summaryPath = Join-Path $nativeRoot 'native-cache-evidence.json'
        & $writeJson $summaryPath $integrationSummary

        $integrationArtifact = New-AndroidRawEvidenceBase -NativeMode artifact
        $integrationArtifact | Add-Member -NotePropertyName supportLevel -NotePropertyValue metadataVerified
        $integrationArtifact | Add-Member -NotePropertyName runner -NotePropertyValue ([pscustomobject]@{ os = 'Linux'; architecture = 'X64' })
        $integrationArtifact | Add-Member -NotePropertyName assets -NotePropertyValue @(
            [pscustomobject][ordered]@{
                assetId = 'android-arm64-apk'; name = $arm64Name; rid = 'android-arm64'; architecture = 'arm64-v8a'
                size = (Get-Item -LiteralPath $arm64Path).Length; sha256Before = $arm64Sha; sha256After = $arm64Sha
                applicationId = 'com.Kibnet.Unlimotion'; versionName = [string]$IdentityDocument.normalizedVersion
                versionCode = [int]$IdentityDocument.androidVersionCode; minSdk = 23; targetSdk = 36
                signatureProfile = [string]$IdentityDocument.signatureProfile; signatureFingerprintSha256 = '9' * 64
                signerCount = 1; zipAligned = $true; nativeSymbolsVerified = $true
            },
            [pscustomobject][ordered]@{
                assetId = 'android-x64-apk'; name = $x64Name; rid = 'android-x64'; architecture = 'x86_64'
                size = (Get-Item -LiteralPath $x64Path).Length; sha256Before = $x64Sha; sha256After = $x64Sha
                applicationId = 'com.Kibnet.Unlimotion'; versionName = [string]$IdentityDocument.normalizedVersion
                versionCode = [int]$IdentityDocument.androidVersionCode; minSdk = 23; targetSdk = 36
                signatureProfile = [string]$IdentityDocument.signatureProfile; signatureFingerprintSha256 = '8' * 64
                signerCount = 1; zipAligned = $true; nativeSymbolsVerified = $true
            }
        )
        $integrationArtifact | Add-Member -NotePropertyName arm64LaunchVerified -NotePropertyValue $false
        $integrationArtifact | Add-Member -NotePropertyName arm64LaunchReason -NotePropertyValue 'No native arm64 device in Stage-3 fixture'
        $integrationArtifact | Add-Member -NotePropertyName nativeCache -NotePropertyValue ([pscustomobject][ordered]@{
            nativeInputDigest = $nativeInputsSha
            requestedKey = $integrationCacheKey
            matchedKey = $integrationCacheKey
            hit = $true
            saveRequired = $false
        })
        $artifactReportPath = Join-Path $nativeRoot 'android-artifact.json'
        & $writeJson $artifactReportPath $integrationArtifact

        $integrationEmulator23 = Copy-JsonObject $firstBootEmulator
        $integrationEmulator23.recordedAtUtc = '2026-07-19T12:00:00Z'
        $integrationEmulator23.asset.name = $x64Name
        $integrationEmulator23.asset.sha256Before = $x64Sha
        $integrationEmulator23.asset.sha256After = $x64Sha
        $emulator23Path = Join-Path $nativeRoot 'android-api23-emulator.json'
        & $writeJson $emulator23Path $integrationEmulator23

        $integrationEmulator36 = Copy-JsonObject $firstBootEmulator
        $integrationEmulator36.recordedAtUtc = '2026-07-19T12:00:00Z'
        $integrationEmulator36.asset.name = $x64Name
        $integrationEmulator36.asset.sha256Before = $x64Sha
        $integrationEmulator36.asset.sha256After = $x64Sha
        $integrationEmulator36.runtime.apiLevel = 36
        $integrationEmulator36.runtime.deviceSdk = 36
        $integrationEmulator36.runtime.systemImagePackage = 'system-images;android-36;google_apis;x86_64'
        $integrationEmulator36.runtime.logcat.fileName = 'android-api36-logcat.txt'
        $integrationEmulator36.runtime.emulatorLog.fileName = 'android-api36-emulator.log'
        $emulator36Path = Join-Path $nativeRoot 'android-api36-emulator.json'
        & $writeJson $emulator36Path $integrationEmulator36

        $transport23Path = Join-Path $nativeRoot 'android-api23-download-transport.json'
        & $writeJson $transport23Path $firstAttemptTransport
        $integrationTransport36 = Copy-JsonObject $downloadTransport
        $integrationTransport36.scope = 'android-api36'
        $transport36Path = Join-Path $nativeRoot 'android-api36-download-transport.json'
        & $writeJson $transport36Path $integrationTransport36

        $preEvidence = @(
            foreach ($artifactEvidencePath in $artifactEvidencePaths) {
                $artifactEvidence = Get-Content -Raw -LiteralPath $artifactEvidencePath | ConvertFrom-Json -Depth 100
                [ordered]@{
                    evidenceId = "android-$($artifactEvidence.architecture)"
                    fileName = [IO.Path]::GetFileName($artifactEvidencePath)
                    sha256 = Get-LowerFileSha256 -Path $artifactEvidencePath
                    platform = 'android'
                    architecture = [string]$artifactEvidence.architecture
                }
            }
        )
        $transportReceipt = [ordered]@{
            schemaVersion = 1; kind = 'distribution-transport-receipt'; status = 'pass'
            platform = 'android'; architecture = 'multi'; identity = $identityProjection
            artifactName = $expectedTransportName; artifactId = $expectedTransportId; artifactDigest = $expectedTransportDigest
            retentionDays = 7; ifNoFilesFound = 'error'; overwrite = $false
            preEvidence = $preEvidence; unixMode = New-NotApplicableUnixEvidence
            productionReady = $false
        }
        $transportReceiptPath = Join-Path $mergeRoot 'transport-receipt.json'
        & $writeJson $transportReceiptPath $transportReceipt
        $platformEvidencePath = Join-Path $mergeRoot 'android-multi.json'
        $nativePaths = @(
            $artifactReportPath, $summaryPath, $nativeInputsPath, $nativeProvenancePath,
            $emulator23Path, $transport23Path, $emulator36Path, $transport36Path
        )
        $mergeArguments = @(
            '-NoProfile', '-File', $script:artifactValidatorPath,
            '-Mode', 'MergePlatform',
            '-Manifest', $script:manifestPath,
            '-SupportMatrix', $script:supportMatrixPath,
            '-EvidenceSchema', $script:evidenceSchemaPath,
            '-Identity', $identityPath,
            '-EvidencePath', ($artifactEvidencePaths -join ','),
            '-NativeEvidencePath', ($nativePaths -join ','),
            '-TransportReceiptPath', $transportReceiptPath,
            '-ArtifactTransportName', $expectedTransportName,
            '-ArtifactTransportId', $expectedTransportId,
            '-ArtifactTransportDigest', $expectedTransportDigest,
            '-Evidence', $platformEvidencePath,
            '-Platform', 'android',
            '-Architecture', 'multi'
        )
        $mergeOutput = & pwsh @mergeArguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Android MergePlatform behavioral fixture failed: $($mergeOutput -join [Environment]::NewLine)"
        }
        $platformEvidence = Get-Content -Raw -LiteralPath $platformEvidencePath | ConvertFrom-Json -Depth 100
        Assert-Condition (@($platformEvidence.nativeEvidence).Count -eq 8 -and @($platformEvidence.nativeCells).Count -eq 4) `
            'Android MergePlatform must emit eight exact native references and four mandatory native cells.'
        Add-Check -Name 'evidence:android-merge-platform-eight-sidecar-closure'
    }
    finally {
        if (Test-Path -LiteralPath $mergeRoot) { Remove-Item -LiteralPath $mergeRoot -Recurse -Force }
    }
}

function Test-TaskStorageEvidenceContracts {
    try {
        . $script:artifactValidatorPath `
            -Mode Validate `
            -Manifest $script:manifestPath `
            -SupportMatrix $script:supportMatrixPath `
            -EvidenceSchema $script:evidenceSchemaPath
    }
    catch {
        if ($_.Exception.Message -notlike 'Validate mode requires*') { throw }
    }

    $seeded = [pscustomobject][ordered]@{
        launchConfiguration = 'seeded-isolated-task-storage'
        unconfiguredFirstRunVerified = $false
        configPath = '/tmp/window smoke/settings.json'
        taskStoragePath = '/tmp/window smoke/Tasks'
    }
    Assert-SeededTaskStorageEvidence -Evidence $seeded -Label 'positive seeded task-storage fixture'
    Assert-SeededTaskStorageEvidence -Evidence ([pscustomobject][ordered]@{
            launchConfiguration = 'seeded-isolated-task-storage'
            unconfiguredFirstRunVerified = $false
            configPath = 'D:\runner work\window smoke\settings.json'
            taskStoragePath = 'D:\runner work\window smoke\Tasks'
        }) -Label 'positive Windows seeded task-storage fixture'

    foreach ($variant in @(
            [pscustomobject]@{ Name = 'seeded-first-run-string-false'; Value = 'false'; Pattern = 'must be a JSON boolean' },
            [pscustomobject]@{ Name = 'seeded-first-run-number-zero'; Value = 0; Pattern = 'must be a JSON boolean' },
            [pscustomobject]@{ Name = 'seeded-first-run-true'; Value = $true; Pattern = 'must identify seeded isolated task storage' })) {
        $badBoolean = Copy-JsonObject $seeded
        $badBoolean.unconfiguredFirstRunVerified = $variant.Value
        Assert-Throws -Name $variant.Name -MessagePattern $variant.Pattern -Action {
            Assert-SeededTaskStorageEvidence -Evidence $badBoolean -Label $variant.Name
        }
    }

    $missingBoolean = Copy-JsonObject $seeded
    $missingBoolean.PSObject.Properties.Remove('unconfiguredFirstRunVerified')
    Assert-Throws -Name 'seeded-first-run-boolean-missing' -MessagePattern 'Required property.*is missing' -Action {
        Assert-SeededTaskStorageEvidence -Evidence $missingBoolean -Label 'missing seeded boolean'
    }

    $metadata = [pscustomobject][ordered]@{
        launchConfiguration = 'notApplicable'
        unconfiguredFirstRunVerified = $false
        configPath = ''
        taskStoragePath = ''
    }
    Assert-NotApplicableTaskStorageEvidence -Evidence $metadata -Label 'positive Linux metadata fixture'

    $metadataOverclaim = Copy-JsonObject $seeded
    Assert-Throws -Name 'linux-metadata-configured-storage-overclaim' -MessagePattern 'notApplicable' -Action {
        Assert-NotApplicableTaskStorageEvidence -Evidence $metadataOverclaim -Label 'Linux metadata overclaim fixture'
    }

    $metadataNullPaths = Copy-JsonObject $metadata
    $metadataNullPaths.configPath = $null
    $metadataNullPaths.taskStoragePath = $null
    Assert-Throws -Name 'linux-metadata-null-storage-paths' -MessagePattern 'must be a JSON string' -Action {
        Assert-NotApplicableTaskStorageEvidence -Evidence $metadataNullPaths -Label 'Linux metadata null-path fixture'
    }

    foreach ($fixture in @(
            [pscustomobject]@{
                Name = 'seeded-relative-storage-paths'
                ConfigPath = 'relative/settings.json'
                TaskStoragePath = 'relative/Tasks'
                Pattern = 'must be an absolute'
            },
            [pscustomobject]@{
                Name = 'seeded-traversal-storage-path'
                ConfigPath = '/tmp/window/settings.json'
                TaskStoragePath = '/tmp/window/../../Tasks'
                Pattern = 'no empty/dot segments'
            },
            [pscustomobject]@{
                Name = 'seeded-mismatched-storage-parent'
                ConfigPath = '/tmp/window-one/settings.json'
                TaskStoragePath = '/tmp/window-two/Tasks'
                Pattern = 'share one canonical isolated parent'
            },
            [pscustomobject]@{
                Name = 'seeded-root-storage-path'
                ConfigPath = '/settings.json'
                TaskStoragePath = '/Tasks'
                Pattern = 'below a non-root directory'
            })) {
        $badPaths = Copy-JsonObject $seeded
        $badPaths.configPath = $fixture.ConfigPath
        $badPaths.taskStoragePath = $fixture.TaskStoragePath
        Assert-Throws -Name $fixture.Name -MessagePattern $fixture.Pattern -Action {
            Assert-SeededTaskStorageEvidence -Evidence $badPaths -Label $fixture.Name
        }
    }
}

function ConvertTo-NormalizedMsBuildPath {
    param([AllowEmptyString()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }
    return ($Value.Trim() -replace '\\', '/').TrimEnd('/')
}

function Test-NormalizedMsBuildPathEqual {
    param(
        [AllowEmptyString()][string]$Actual,
        [AllowEmptyString()][string]$Expected
    )

    $comparison = if ($IsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }
    return (ConvertTo-NormalizedMsBuildPath $Actual).Equals(
        (ConvertTo-NormalizedMsBuildPath $Expected),
        $comparison)
}

function Invoke-MsBuildEvaluation {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [string]$RuntimeIdentifier,
        [switch]$BuildingSolution,
        [switch]$IncludeCompile
    )

    $properties = @(
        'MSBuildProjectName',
        'BuildingSolutionFile',
        'BaseIntermediateOutputPath',
        'MSBuildProjectExtensionsPath',
        'ProjectAssetsFile',
        'DefaultItemExcludes',
        'BaseOutputPath',
        'OutputPath',
        'PublishDir'
    )
    $items = @('PackageReference')
    if ($IncludeCompile) {
        $items += 'Compile'
    }

    $arguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in @(
            'msbuild',
            $ProjectPath,
            '-nologo',
            '-verbosity:quiet',
            "-p:Configuration=$Configuration")) {
        $arguments.Add($argument)
    }
    if ($BuildingSolution) {
        $arguments.Add('-p:BuildingSolutionFile=true')
    }
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $arguments.Add("-p:RuntimeIdentifier=$RuntimeIdentifier")
    }
    $arguments.Add("-getProperty:$($properties -join ',')")
    $arguments.Add("-getItem:$($items -join ',')")

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $script:repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start dotnet msbuild for '$ProjectPath'."
        }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "dotnet msbuild evaluation failed with exit code $($process.ExitCode) for '$ProjectPath':`n$standardError`n$standardOutput"
        }
        try {
            $document = $standardOutput | ConvertFrom-Json -Depth 100 -ErrorAction Stop
        }
        catch {
            throw "dotnet msbuild evaluation did not return JSON for '$ProjectPath': $($_.Exception.Message)`n$standardError`n$standardOutput"
        }

        $packageReferences = @()
        if ($null -ne $document.Items -and
            $null -ne $document.Items.PSObject.Properties['PackageReference']) {
            $packageReferences = @($document.Items.PackageReference | ForEach-Object { [string]$_.Identity })
        }
        $compilePaths = @()
        if ($null -ne $document.Items -and
            $null -ne $document.Items.PSObject.Properties['Compile']) {
            $compilePaths = @($document.Items.Compile | ForEach-Object { [string]$_.FullPath })
        }

        return [pscustomobject][ordered]@{
            projectPath = [System.IO.Path]::GetFullPath($ProjectPath)
            projectName = [string]$document.Properties.MSBuildProjectName
            configuration = $Configuration
            runtimeIdentifier = $RuntimeIdentifier
            buildingSolutionFile = [string]$document.Properties.BuildingSolutionFile
            baseIntermediateOutputPath = [string]$document.Properties.BaseIntermediateOutputPath
            msBuildProjectExtensionsPath = [string]$document.Properties.MSBuildProjectExtensionsPath
            projectAssetsFile = [string]$document.Properties.ProjectAssetsFile
            defaultItemExcludes = [string]$document.Properties.DefaultItemExcludes
            baseOutputPath = [string]$document.Properties.BaseOutputPath
            outputPath = [string]$document.Properties.OutputPath
            publishDir = [string]$document.Properties.PublishDir
            packageReferences = $packageReferences
            compilePaths = $compilePaths
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-DesktopBuildIsolationObservation {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $desktopRoot = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'src/Unlimotion.Desktop'))
    $projects = @(
        [pscustomobject]@{
            name = 'Unlimotion.Desktop'
            path = Join-Path $desktopRoot 'Unlimotion.Desktop.csproj'
        },
        [pscustomobject]@{
            name = 'Unlimotion.Desktop.ForDebianBuild'
            path = Join-Path $desktopRoot 'Unlimotion.Desktop.ForDebianBuild.csproj'
        },
        [pscustomobject]@{
            name = 'Unlimotion.Desktop.ForMacBuild'
            path = Join-Path $desktopRoot 'Unlimotion.Desktop.ForMacBuild.csproj'
        }
    )
    $directPlans = @(
        [pscustomobject]@{ name = 'Unlimotion.Desktop'; rid = 'win-x64' },
        [pscustomobject]@{ name = 'Unlimotion.Desktop.ForDebianBuild'; rid = 'linux-x64' },
        [pscustomobject]@{ name = 'Unlimotion.Desktop.ForMacBuild'; rid = 'osx-x64' },
        [pscustomobject]@{ name = 'Unlimotion.Desktop.ForMacBuild'; rid = 'osx-arm64' }
    )

    $sentinelId = [Guid]::NewGuid().ToString('N')
    $sentinelDirectories = @(
        (Join-Path $desktopRoot "obj/build-isolation-sentinel-$sentinelId"),
        (Join-Path $desktopRoot "bin/build-isolation-sentinel-$sentinelId")
    )
    $sentinelPaths = @(
        (Join-Path $sentinelDirectories[0] 'ForeignObj.g.cs'),
        (Join-Path $sentinelDirectories[1] 'ForeignBin.g.cs')
    )
    $solution = @()
    $solutionRelease = @()
    $directDebug = @()
    $direct = @()
    try {
        foreach ($directory in $sentinelDirectories) {
            [System.IO.Directory]::CreateDirectory($directory) | Out-Null
        }
        foreach ($path in $sentinelPaths) {
            [System.IO.File]::WriteAllText(
                $path,
                '#error Foreign generated source sentinel must not enter Compile.' + [Environment]::NewLine,
                [System.Text.UTF8Encoding]::new($false))
        }

        $solution = @(
            foreach ($project in $projects) {
                Invoke-MsBuildEvaluation -ProjectPath $project.path -Configuration Debug -BuildingSolution -IncludeCompile
            }
        )
        $solutionRelease = @(
            foreach ($project in $projects) {
                Invoke-MsBuildEvaluation -ProjectPath $project.path -Configuration Release -BuildingSolution -IncludeCompile
            }
        )
        $directDebug = @(
            foreach ($project in $projects) {
                Invoke-MsBuildEvaluation -ProjectPath $project.path -Configuration Debug -IncludeCompile
            }
        )
        $direct = @(
            foreach ($plan in $directPlans) {
                $project = @($projects | Where-Object name -CEQ $plan.name)[0]
                Invoke-MsBuildEvaluation -ProjectPath $project.path -Configuration Release -RuntimeIdentifier $plan.rid -IncludeCompile
            }
        )
    }
    finally {
        foreach ($path in $sentinelPaths) {
            if ([System.IO.File]::Exists($path)) {
                [System.IO.File]::Delete($path)
            }
        }
        foreach ($directory in $sentinelDirectories) {
            if ([System.IO.Directory]::Exists($directory) -and
                [System.IO.Directory]::GetFileSystemEntries($directory).Count -eq 0) {
                [System.IO.Directory]::Delete($directory, $false)
            }
        }
    }

    return [pscustomobject][ordered]@{
        desktopRoot = $desktopRoot
        sentinelPaths = @($sentinelPaths | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
        solution = $solution
        solutionRelease = $solutionRelease
        directDebug = $directDebug
        direct = $direct
    }
}

function Assert-DesktopBuildIsolationContract {
    param([Parameter(Mandatory = $true)][object]$Observation)

    $solution = @($Observation.solution)
    $solutionRelease = @($Observation.solutionRelease)
    $directDebug = @($Observation.directDebug)
    $direct = @($Observation.direct)
    $expectedProjectNames = @(
        'Unlimotion.Desktop',
        'Unlimotion.Desktop.ForDebianBuild',
        'Unlimotion.Desktop.ForMacBuild'
    )
    Assert-Condition ($solution.Count -eq 3) 'Build isolation must evaluate exactly three solution Desktop projects.'
    Assert-Condition (
        (@($solution.projectName | Sort-Object) -join '|') -ceq (@($expectedProjectNames | Sort-Object) -join '|')) `
        'Build isolation must evaluate the exact three Desktop project names.'

    $pathComparer = if ($IsWindows) {
        [StringComparer]::OrdinalIgnoreCase
    }
    else {
        [StringComparer]::Ordinal
    }
    $assetsPaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
    $outputRoots = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
    $desktopRoot = ConvertTo-NormalizedMsBuildPath ([string]$Observation.desktopRoot)
    $expectedWholeObjExclusion = "$desktopRoot/obj/**"
    $expectedWholeBinExclusion = "$desktopRoot/bin/**"
    $sentinelPaths = @($Observation.sentinelPaths | ForEach-Object { ConvertTo-NormalizedMsBuildPath ([string]$_) })

    foreach ($row in $solution) {
        $name = [string]$row.projectName
        Assert-Condition ([string]$row.buildingSolutionFile -ceq 'true') `
            "Project '$name' must evaluate BuildingSolutionFile=true for solution output isolation."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseIntermediateOutputPath "obj/$name") `
            "Project '$name' must use project-bound BaseIntermediateOutputPath 'obj/$name/'."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.msBuildProjectExtensionsPath "$desktopRoot/obj/$name") `
            "Project '$name' must use project-bound MSBuildProjectExtensionsPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.projectAssetsFile "$desktopRoot/obj/$name/project.assets.json") `
            "Project '$name' must use project-bound ProjectAssetsFile."
        $assetsPaths.Add((ConvertTo-NormalizedMsBuildPath ([string]$row.projectAssetsFile))) | Out-Null

        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseOutputPath "bin/$name") `
            "Project '$name' must use solution-only BaseOutputPath 'bin/$name/'."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.outputPath "bin/$name/Debug/net10.0") `
            "Project '$name' must use solution-only Debug OutputPath."
        $outputRoots.Add((ConvertTo-NormalizedMsBuildPath ([string]$row.baseOutputPath))) | Out-Null

        $excludeEntries = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        ([string]$row.defaultItemExcludes -split ';') |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $excludeEntries.Add($_) | Out-Null }
        Assert-Condition (
            $excludeEntries.Contains($expectedWholeObjExclusion) -and
            $excludeEntries.Contains($expectedWholeBinExclusion)) `
            "Project '$name' must preserve whole Desktop obj/bin exclusions."

        $compilePaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        $row.compilePaths |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath ([string]$_) } |
            ForEach-Object { $compilePaths.Add($_) | Out-Null }
        foreach ($sentinelPath in $sentinelPaths) {
            Assert-Condition (-not $compilePaths.Contains($sentinelPath)) `
                "Project '$name' must exclude every foreign generated-source sentinel from Compile."
        }
    }
    Assert-Condition ($assetsPaths.Count -eq 3) 'Build isolation must produce three unique project-bound assets paths.'
    Assert-Condition ($outputRoots.Count -eq 3) 'Build isolation must produce three unique solution-only output roots.'

    Assert-Condition ($solutionRelease.Count -eq 3) `
        'Build isolation must evaluate exactly three Release solution Desktop projects.'
    Assert-Condition (
        (@($solutionRelease.projectName | Sort-Object) -join '|') -ceq (@($expectedProjectNames | Sort-Object) -join '|')) `
        'Build isolation must evaluate the exact three Release solution project names.'
    foreach ($row in $solutionRelease) {
        $name = [string]$row.projectName
        Assert-Condition ([string]$row.buildingSolutionFile -ceq 'true') `
            "Release solution project '$name' must evaluate BuildingSolutionFile=true."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseIntermediateOutputPath "obj/$name") `
            "Release solution project '$name' must retain project-bound BaseIntermediateOutputPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.msBuildProjectExtensionsPath "$desktopRoot/obj/$name") `
            "Release solution project '$name' must retain project-bound MSBuildProjectExtensionsPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.projectAssetsFile "$desktopRoot/obj/$name/project.assets.json") `
            "Release solution project '$name' must retain project-bound ProjectAssetsFile."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseOutputPath "bin/$name") `
            "Release solution project '$name' must use solution-only Release BaseOutputPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.outputPath "bin/$name/Release/net10.0") `
            "Release solution project '$name' must use solution-only Release OutputPath."

        $releaseSolutionExcludes = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        ([string]$row.defaultItemExcludes -split ';') |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $releaseSolutionExcludes.Add($_) | Out-Null }
        Assert-Condition (
            $releaseSolutionExcludes.Contains($expectedWholeObjExclusion) -and
            $releaseSolutionExcludes.Contains($expectedWholeBinExclusion)) `
            "Release solution project '$name' must preserve whole Desktop obj/bin exclusions."
        $releaseSolutionCompile = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        $row.compilePaths |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath ([string]$_) } |
            ForEach-Object { $releaseSolutionCompile.Add($_) | Out-Null }
        foreach ($sentinelPath in $sentinelPaths) {
            Assert-Condition (-not $releaseSolutionCompile.Contains($sentinelPath)) `
                "Release solution project '$name' must exclude every foreign generated-source sentinel from Compile."
        }
    }

    Assert-Condition ($directDebug.Count -eq 3) `
        'Build isolation must evaluate exactly three direct Debug Desktop projects.'
    Assert-Condition (
        (@($directDebug.projectName | Sort-Object) -join '|') -ceq (@($expectedProjectNames | Sort-Object) -join '|')) `
        'Build isolation must evaluate the exact three direct Debug project names.'
    foreach ($row in $directDebug) {
        $name = [string]$row.projectName
        Assert-Condition ([string]$row.buildingSolutionFile -cne 'true') `
            "Direct Debug project '$name' must not set BuildingSolutionFile=true."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseIntermediateOutputPath "obj/$name") `
            "Direct Debug project '$name' must retain project-bound BaseIntermediateOutputPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.msBuildProjectExtensionsPath "$desktopRoot/obj/$name") `
            "Direct Debug project '$name' must retain project-bound MSBuildProjectExtensionsPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.projectAssetsFile "$desktopRoot/obj/$name/project.assets.json") `
            "Direct Debug project '$name' must retain project-bound ProjectAssetsFile."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseOutputPath 'bin') `
            "Direct Debug project '$name' must preserve legacy direct BaseOutputPath 'bin/'."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.outputPath 'bin/Debug/net10.0') `
            "Direct Debug project '$name' must preserve legacy direct Debug OutputPath."

        $directDebugExcludes = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        ([string]$row.defaultItemExcludes -split ';') |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $directDebugExcludes.Add($_) | Out-Null }
        Assert-Condition (
            $directDebugExcludes.Contains($expectedWholeObjExclusion) -and
            $directDebugExcludes.Contains($expectedWholeBinExclusion)) `
            "Direct Debug project '$name' must preserve whole Desktop obj/bin exclusions."
        $directDebugCompile = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        $row.compilePaths |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath ([string]$_) } |
            ForEach-Object { $directDebugCompile.Add($_) | Out-Null }
        foreach ($sentinelPath in $sentinelPaths) {
            Assert-Condition (-not $directDebugCompile.Contains($sentinelPath)) `
                "Direct Debug project '$name' must exclude every foreign generated-source sentinel from Compile."
        }
    }

    Assert-Condition ($direct.Count -eq 4) 'Build isolation must evaluate exactly four direct Release RID paths.'
    $expectedDirect = @{
        'Unlimotion.Desktop|win-x64' = 'bin/Release/net10.0/win-x64'
        'Unlimotion.Desktop.ForDebianBuild|linux-x64' = 'bin/Release/net10.0/linux-x64'
        'Unlimotion.Desktop.ForMacBuild|osx-x64' = 'bin/Release/net10.0/osx-x64'
        'Unlimotion.Desktop.ForMacBuild|osx-arm64' = 'bin/Release/net10.0/osx-arm64'
    }
    $directKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $direct) {
        $name = [string]$row.projectName
        $rid = [string]$row.runtimeIdentifier
        $key = "$name|$rid"
        Assert-Condition $expectedDirect.ContainsKey($key) "Unexpected direct Release evaluation '$key'."
        Assert-Condition ($directKeys.Add($key)) `
            "Build isolation must cover each exact direct Release project/RID pair exactly once; duplicate '$key'."
        Assert-Condition ([string]$row.buildingSolutionFile -cne 'true') `
            "Direct Release evaluation '$key' must not set BuildingSolutionFile=true."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseIntermediateOutputPath "obj/$name") `
            "Direct Release evaluation '$key' must retain project-bound BaseIntermediateOutputPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.msBuildProjectExtensionsPath "$desktopRoot/obj/$name") `
            "Direct Release evaluation '$key' must retain project-bound MSBuildProjectExtensionsPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.projectAssetsFile "$desktopRoot/obj/$name/project.assets.json") `
            "Direct Release evaluation '$key' must retain project-bound ProjectAssetsFile."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.baseOutputPath 'bin') `
            "Direct Release evaluation '$key' must preserve legacy direct BaseOutputPath 'bin/'."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.outputPath $expectedDirect[$key]) `
            "Direct Release evaluation '$key' must preserve legacy direct OutputPath."
        Assert-Condition (Test-NormalizedMsBuildPathEqual $row.publishDir "$($expectedDirect[$key])/publish") `
            "Direct Release evaluation '$key' must preserve legacy direct PublishDir."

        $directExcludeEntries = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        ([string]$row.defaultItemExcludes -split ';') |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $directExcludeEntries.Add($_) | Out-Null }
        Assert-Condition (
            $directExcludeEntries.Contains($expectedWholeObjExclusion) -and
            $directExcludeEntries.Contains($expectedWholeBinExclusion)) `
            "Direct Release evaluation '$key' must preserve whole Desktop obj/bin exclusions."

        $directCompilePaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
        $row.compilePaths |
            ForEach-Object { ConvertTo-NormalizedMsBuildPath ([string]$_) } |
            ForEach-Object { $directCompilePaths.Add($_) | Out-Null }
        foreach ($sentinelPath in $sentinelPaths) {
            Assert-Condition (-not $directCompilePaths.Contains($sentinelPath)) `
                "Direct Release evaluation '$key' must exclude every foreign generated-source sentinel from Compile."
        }
    }
    Assert-Condition (
        $directKeys.Count -eq $expectedDirect.Count -and
        @($expectedDirect.Keys | Where-Object { -not $directKeys.Contains($_) }).Count -eq 0) `
        'Build isolation must cover the exact four direct Release project/RID pairs.'

    foreach ($debugRow in @(
            @($solution | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0],
            @($directDebug | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0])) {
        $debugDiagnostics = @($debugRow.packageReferences | Where-Object { $_ -ceq 'AvaloniaUI.DiagnosticsSupport' })
        Assert-Condition ($debugDiagnostics.Count -eq 1) `
            'Every Debian Debug package graph must contain exactly one AvaloniaUI.DiagnosticsSupport reference.'
    }
    foreach ($releaseRow in @(
            @($solutionRelease | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0],
            @($direct | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0])) {
        $releaseDiagnostics = @($releaseRow.packageReferences | Where-Object { $_ -ceq 'AvaloniaUI.DiagnosticsSupport' })
        Assert-Condition ($releaseDiagnostics.Count -eq 0) `
            'Every Debian Release package graph must not contain AvaloniaUI.DiagnosticsSupport.'
    }
}

function Test-DesktopBuildIsolationNegativeFixtures {
    param([Parameter(Mandatory = $true)][object]$Observation)

    $sharedObj = Copy-JsonObject $Observation
    $sharedObj.solution[0].baseIntermediateOutputPath = 'obj/'
    $sharedObj.solution[0].msBuildProjectExtensionsPath = Join-Path $Observation.desktopRoot 'obj'
    $sharedObj.solution[0].projectAssetsFile = Join-Path $Observation.desktopRoot 'obj/project.assets.json'
    Assert-Throws -Name 'build-isolation-shared-obj' -MessagePattern 'project-bound BaseIntermediateOutputPath' -Action {
        Assert-DesktopBuildIsolationContract $sharedObj
    }

    $missingWholeExclusions = Copy-JsonObject $Observation
    $name = [string]$missingWholeExclusions.solution[0].projectName
    $missingWholeExclusions.solution[0].defaultItemExcludes = "obj/$name/**;bin/$name/**"
    Assert-Throws -Name 'build-isolation-missing-whole-obj-bin-exclusion' -MessagePattern 'whole Desktop obj/bin exclusions' -Action {
        Assert-DesktopBuildIsolationContract $missingWholeExclusions
    }

    $unconditionalOutput = Copy-JsonObject $Observation
    $directName = [string]$unconditionalOutput.direct[0].projectName
    $directRid = [string]$unconditionalOutput.direct[0].runtimeIdentifier
    $unconditionalOutput.direct[0].baseOutputPath = "bin/$directName/"
    $unconditionalOutput.direct[0].outputPath = "bin/$directName/Release/net10.0/$directRid/"
    $unconditionalOutput.direct[0].publishDir = "bin/$directName/Release/net10.0/$directRid/publish/"
    Assert-Throws -Name 'build-isolation-unconditional-output-relocation' -MessagePattern 'legacy direct BaseOutputPath' -Action {
        Assert-DesktopBuildIsolationContract $unconditionalOutput
    }

    $duplicateMissingDirectRid = Copy-JsonObject $Observation
    $duplicateMissingDirectRid.direct[3].projectName = $duplicateMissingDirectRid.direct[2].projectName
    $duplicateMissingDirectRid.direct[3].runtimeIdentifier = $duplicateMissingDirectRid.direct[2].runtimeIdentifier
    Assert-Throws -Name 'build-isolation-duplicate-missing-direct-rid' -MessagePattern 'exact direct Release project/RID pair' -Action {
        Assert-DesktopBuildIsolationContract $duplicateMissingDirectRid
    }

    $directMissingWholeExclusions = Copy-JsonObject $Observation
    $directName = [string]$directMissingWholeExclusions.direct[0].projectName
    $directMissingWholeExclusions.direct[0].defaultItemExcludes = "obj/$directName/**;bin/$directName/**"
    Assert-Throws -Name 'build-isolation-direct-missing-whole-obj-bin-exclusion' -MessagePattern 'Direct Release.*whole Desktop obj/bin exclusions' -Action {
        Assert-DesktopBuildIsolationContract $directMissingWholeExclusions
    }

    $directCompileSentinelLeak = Copy-JsonObject $Observation
    $directCompileSentinelLeak.direct[0].compilePaths = @($directCompileSentinelLeak.direct[0].compilePaths) + @($Observation.sentinelPaths[0])
    Assert-Throws -Name 'build-isolation-direct-compile-sentinel-leak' -MessagePattern 'Direct Release.*foreign generated-source sentinel' -Action {
        Assert-DesktopBuildIsolationContract $directCompileSentinelLeak
    }

    $outputBoundToConfiguration = Copy-JsonObject $Observation
    $releaseSolutionName = [string]$outputBoundToConfiguration.solutionRelease[0].projectName
    $outputBoundToConfiguration.solutionRelease[0].baseOutputPath = 'bin/'
    $outputBoundToConfiguration.solutionRelease[0].outputPath = 'bin/Release/net10.0/'
    $outputBoundToConfiguration.directDebug[0].baseOutputPath = "bin/$releaseSolutionName/"
    $outputBoundToConfiguration.directDebug[0].outputPath = "bin/$releaseSolutionName/Debug/net10.0/"
    Assert-Throws -Name 'build-isolation-output-bound-to-configuration' -MessagePattern 'solution-only Release BaseOutputPath' -Action {
        Assert-DesktopBuildIsolationContract $outputBoundToConfiguration
    }

    $diagnosticsBoundToSolution = Copy-JsonObject $Observation
    $debugDirectDebian = @($diagnosticsBoundToSolution.directDebug | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0]
    $debugDirectDebian.packageReferences = @($debugDirectDebian.packageReferences | Where-Object { $_ -cne 'AvaloniaUI.DiagnosticsSupport' })
    $releaseSolutionDebian = @($diagnosticsBoundToSolution.solutionRelease | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0]
    $releaseSolutionDebian.packageReferences = @($releaseSolutionDebian.packageReferences) + @('AvaloniaUI.DiagnosticsSupport')
    Assert-Throws -Name 'build-isolation-diagnostics-bound-to-solution' -MessagePattern 'Debian Debug.*AvaloniaUI.DiagnosticsSupport' -Action {
        Assert-DesktopBuildIsolationContract $diagnosticsBoundToSolution
    }

    $missingDebugDiagnostics = Copy-JsonObject $Observation
    $debianDebug = @($missingDebugDiagnostics.solution | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0]
    $debianDebug.packageReferences = @($debianDebug.packageReferences | Where-Object { $_ -cne 'AvaloniaUI.DiagnosticsSupport' })
    Assert-Throws -Name 'build-isolation-missing-debug-diagnostics' -MessagePattern 'Debian Debug.*AvaloniaUI.DiagnosticsSupport' -Action {
        Assert-DesktopBuildIsolationContract $missingDebugDiagnostics
    }

    $releaseDiagnosticsLeak = Copy-JsonObject $Observation
    $debianRelease = @($releaseDiagnosticsLeak.direct | Where-Object projectName -CEQ 'Unlimotion.Desktop.ForDebianBuild')[0]
    $debianRelease.packageReferences = @($debianRelease.packageReferences) + @('AvaloniaUI.DiagnosticsSupport')
    Assert-Throws -Name 'build-isolation-diagnostics-release-leak' -MessagePattern 'Debian Release.*AvaloniaUI.DiagnosticsSupport' -Action {
        Assert-DesktopBuildIsolationContract $releaseDiagnosticsLeak
    }

    $compileSentinelLeak = Copy-JsonObject $Observation
    $compileSentinelLeak.solution[0].compilePaths = @($compileSentinelLeak.solution[0].compilePaths) + @($Observation.sentinelPaths[0])
    Assert-Throws -Name 'build-isolation-compile-sentinel-leak' -MessagePattern 'foreign generated-source sentinel' -Action {
        Assert-DesktopBuildIsolationContract $compileSentinelLeak
    }
}

$script:repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$script:manifestPath = Resolve-ExistingFile -Path $Manifest -DisplayName 'Manifest'
$script:manifestSchemaPath = Resolve-ExistingFile -Path $ManifestSchema -DisplayName 'ManifestSchema'
$script:fixturePath = Resolve-ExistingFile -Path $Fixture -DisplayName 'Fixture'
$script:supportMatrixPath = Resolve-ExistingFile -Path $SupportMatrix -DisplayName 'SupportMatrix'
$script:supportMatrixSchemaPath = Resolve-ExistingFile -Path $SupportMatrixSchema -DisplayName 'SupportMatrixSchema'
$script:resolverPath = Resolve-ExistingFile -Path $Resolver -DisplayName 'Resolver'
$script:evidenceSchemaPath = Resolve-ExistingFile -Path $EvidenceSchema -DisplayName 'EvidenceSchema'
$script:artifactValidatorPath = Resolve-ExistingFile -Path $ArtifactValidator -DisplayName 'ArtifactValidator'
$script:workflowPath = Resolve-ExistingFile -Path $Workflow -DisplayName 'Workflow'

$manifestDocument = Read-JsonFile -Path $script:manifestPath -DisplayName 'Manifest'
$fixtureDocument = Read-JsonFile -Path $script:fixturePath -DisplayName 'Fixture'
$supportDocument = Read-JsonFile -Path $script:supportMatrixPath -DisplayName 'SupportMatrix'

Assert-JsonFileSchema -JsonPath $script:manifestPath -SchemaPath $script:manifestSchemaPath -Name 'release-assets.json'
Assert-JsonFileSchema -JsonPath $script:supportMatrixPath -SchemaPath $script:supportMatrixSchemaPath -Name 'support-matrix.json'

$areasRun = [System.Collections.Generic.List[string]]::new()
$blobParityIdentity = $null
$blobParityDirectReport = $null
if ($Area -in @('All', 'BlobParity', 'BlobParityAggregate')) {
    $blobParityIdentity = Resolve-BlobParityIdentity -RepositoryRoot $script:repositoryRoot `
        -ExpectedSourceSha $SourceSha -ExpectedWorkflowSha $WorkflowSha
}

if ($Area -in @('All', 'BlobParity')) {
    $blobParityDirectReport = New-DistributionBlobParityReport -RepositoryRoot $script:repositoryRoot `
        -ExpectedSourceSha $blobParityIdentity.sourceSha -ExpectedWorkflowSha $blobParityIdentity.workflowSha
    Assert-DirectBlobParityReport -Document $blobParityDirectReport -Label 'Generated blob parity report' `
        -RepositoryRoot $script:repositoryRoot `
        -ExpectedPaths @(Get-TrackedDistributionJsonPaths -RepositoryRoot $script:repositoryRoot) `
        -ExpectedSourceSha $blobParityIdentity.sourceSha -ExpectedWorkflowSha $blobParityIdentity.workflowSha | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($BlobParityEvidencePath)) {
        Write-BlobParityEvidence -Path $BlobParityEvidencePath -Document $blobParityDirectReport
    }
    Test-BlobParityByteMutationNegativeFixtures -RepositoryRoot $script:repositoryRoot `
        -GitPaths @($blobParityDirectReport.files | ForEach-Object { [string]$_.path })
    Test-BlobParityAttributeSourceFixtures
    Add-Check -Name 'blob-parity:tracked-json-physical-bytes-match-raw-head-blobs'
    $areasRun.Add('BlobParity')
}

if ($Area -ceq 'BlobParityAggregate') {
    Assert-Condition ($BlobParityInputPath.Count -eq 3) 'BlobParityAggregate area requires exactly three -BlobParityInputPath values.'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($BlobParityEvidencePath)) 'BlobParityAggregate area requires -BlobParityEvidencePath.'
    $blobParityAggregateReport = New-DistributionBlobParityAggregateReport -RepositoryRoot $script:repositoryRoot `
        -InputPaths $BlobParityInputPath `
        -ExpectedSourceSha $blobParityIdentity.sourceSha -ExpectedWorkflowSha $blobParityIdentity.workflowSha
    $aggregateFixtureSource = Read-JsonFile -Path $BlobParityInputPath[0] -DisplayName 'Blob parity aggregate fixture source'
    Test-BlobParityAggregateFixtures -RepositoryRoot $script:repositoryRoot -DirectReport $aggregateFixtureSource `
        -ExpectedSourceSha $blobParityIdentity.sourceSha -ExpectedWorkflowSha $blobParityIdentity.workflowSha
    Write-BlobParityEvidence -Path $BlobParityEvidencePath -Document $blobParityAggregateReport
    Add-Check -Name 'blob-parity-aggregate:input-reports-match-current-head'
    $areasRun.Add('BlobParityAggregate')
}
elseif ($Area -ceq 'All') {
    Test-BlobParityAggregateFixtures -RepositoryRoot $script:repositoryRoot -DirectReport $blobParityDirectReport `
        -ExpectedSourceSha $blobParityIdentity.sourceSha -ExpectedWorkflowSha $blobParityIdentity.workflowSha
    $areasRun.Add('BlobParityAggregate')
}

if ($Area -in @('All', 'BuildIsolation')) {
    $buildIsolationObservation = New-DesktopBuildIsolationObservation -RepositoryRoot $script:repositoryRoot
    Assert-DesktopBuildIsolationContract -Observation $buildIsolationObservation
    foreach ($checkName in @(
            'build-isolation:three-project-assets-paths',
            'build-isolation:three-solution-output-roots',
            'build-isolation:whole-obj-bin-exclusions',
            'build-isolation:foreign-compile-sentinels-excluded',
            'build-isolation:four-legacy-direct-publish-paths',
            'build-isolation:debian-debug-only-diagnostics')) {
        Add-Check -Name $checkName
    }
    Test-DesktopBuildIsolationNegativeFixtures -Observation $buildIsolationObservation
    $areasRun.Add('BuildIsolation')
}

if ($Area -in @('All', 'InventorySupport')) {
    Test-InventoryContract -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument
    Test-SupportContract -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument -SupportDocument $supportDocument
    Test-ObservedInventory -ExpectedFixture $fixtureDocument -ObservedFixture (Copy-JsonObject $fixtureDocument)
    Add-Check -Name 'inventory:22-exact-assets'
    Add-Check -Name 'inventory:frozen-observation-exact-match'
    Add-Check -Name 'support:seven-exact-digest-claims'
    $areasRun.Add('InventorySupport')

    $missing = Copy-JsonObject $fixtureDocument
    $missing.assets = @($missing.assets | Select-Object -Skip 1)
    Assert-Throws -Name 'missing-asset' -Action { Test-InventoryContract $manifestDocument $missing }

    $duplicate = Copy-JsonObject $fixtureDocument
    $duplicate.assets = @($duplicate.assets) + @(Copy-JsonObject $duplicate.assets[0])
    Assert-Throws -Name 'duplicate-asset' -Action { Test-InventoryContract $manifestDocument $duplicate }

    $unexpected = Copy-JsonObject $fixtureDocument
    $unexpected.assets[0].assetId = 'unexpected-release-asset'
    Assert-Throws -Name 'unexpected-asset' -Action { Test-InventoryContract $manifestDocument $unexpected }

    $zeroByte = Copy-JsonObject $fixtureDocument
    $zeroByte.assets[0].size = 0
    Assert-Throws -Name 'zero-byte-asset' -Action { Test-InventoryContract $manifestDocument $zeroByte }

    $staleVersion = Copy-JsonObject $fixtureDocument
    @($staleVersion.assets | Where-Object assetId -ceq 'linux-deb-x64')[0].name = 'Unlimotion-1.26.0.deb'
    Assert-Throws -Name 'stale-version-name' -Action { Test-InventoryContract $manifestDocument $staleVersion }

    $hashMismatch = Copy-JsonObject $fixtureDocument
    $hashMismatch.assets[0].sha256 = '0' * 64
    Assert-Throws -Name 'fixture-hash-mismatch' -Action {
        Test-ObservedInventory $fixtureDocument $hashMismatch
    }

    $sameNameDifferentDigest = Copy-JsonObject $supportDocument
    $sameNameDifferentDigest.claims[0].assets[0].sha256 = '0' * 64
    Assert-Throws -Name 'same-name-different-digest' -Action {
        Test-SupportContract $manifestDocument $fixtureDocument $sameNameDifferentDigest
    }

    $illegalPromotion = Copy-JsonObject $supportDocument
    $illegalPromotion.claims[0].evidenceLevel = 'productionReady'
    Assert-Throws -Name 'candidate-support-promotion' -Action {
        Test-SupportContract $manifestDocument $fixtureDocument $illegalPromotion
    }

    $caseCollision = Copy-JsonObject $manifestDocument
    @($caseCollision.assets | Where-Object id -ceq 'windows-portable-x64-legacy')[0].filenameTemplate = 'unlimotion-win-portable.zip'
    Assert-Throws -Name 'case-insensitive-filename-collision' -Action {
        Test-InventoryContract $caseCollision $fixtureDocument
    }

    $unknownRole = Copy-JsonObject $manifestDocument
    $unknownRole.assets[0].role = 'installer'
    Assert-Throws -Name 'unknown-role-enum' -Action {
        Assert-JsonObjectSchema -Document $unknownRole -SchemaPath $script:manifestSchemaPath -Name 'unknown-role-manifest'
    }

    $unknownManifestProperty = Copy-JsonObject $manifestDocument
    $unknownManifestProperty | Add-Member -NotePropertyName unexpectedProperty -NotePropertyValue true
    Assert-Throws -Name 'manifest-additional-property' -Action {
        Assert-JsonObjectSchema -Document $unknownManifestProperty -SchemaPath $script:manifestSchemaPath -Name 'manifest-with-extra-property'
    }

    $unknownSupportProperty = Copy-JsonObject $supportDocument
    $unknownSupportProperty.claims[0] | Add-Member -NotePropertyName unexpectedProperty -NotePropertyValue true
    Assert-Throws -Name 'support-additional-property' -Action {
        Assert-JsonObjectSchema -Document $unknownSupportProperty -SchemaPath $script:supportMatrixSchemaPath -Name 'support-with-extra-property'
    }

    $hiddenAppImagePrerequisite = Copy-JsonObject $manifestDocument
    $hiddenAppImagePrerequisite.linuxRuntimePrerequisites.appImageExtractAndRun.debian12 = @(
        $hiddenAppImagePrerequisite.linuxRuntimePrerequisites.appImageExtractAndRun.debian12 | Where-Object { $_ -cne 'libssl3' })
    Assert-Throws -Name 'appimage-prerequisite-drift' -Action {
        Test-InventoryContract $hiddenAppImagePrerequisite $fixtureDocument
    }
}

if ($Area -in @('All', 'VelopackFeeds')) {
    Test-FeedRelations -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument
    Add-Check -Name 'feeds:five-exact-relations'
    $areasRun.Add('VelopackFeeds')

    $staleFeed = Copy-JsonObject $fixtureDocument
    $feed = @($staleFeed.feeds | Where-Object assetId -ceq 'linux-feed-json')[0]
    $content = ([string]$feed.content).Replace('"Version":"1.27.0"', '"Version":"1.26.0"')
    Set-FeedContentAndRefreshDigest -FixtureDocument $staleFeed -FeedAssetId 'linux-feed-json' -Content $content
    Assert-Throws -Name 'feed-stale-version' -Action { Test-FeedRelations $manifestDocument $staleFeed }

    $wrongHash = Copy-JsonObject $fixtureDocument
    $feed = @($wrongHash.feeds | Where-Object assetId -ceq 'macos-x64-feed-json')[0]
    $content = ([string]$feed.content).Replace('25401EA2F62DC893BA3FC5393AA216CAF193783EE78B9D3838EB13559E69D77E', ('0' * 64))
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongHash -FeedAssetId 'macos-x64-feed-json' -Content $content
    Assert-Throws -Name 'feed-sha256-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongHash }

    $wrongSize = Copy-JsonObject $fixtureDocument
    $feed = @($wrongSize.feeds | Where-Object assetId -ceq 'macos-arm64-feed-json')[0]
    $content = ([string]$feed.content).Replace('"Size":64103986', '"Size":64103985')
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongSize -FeedAssetId 'macos-arm64-feed-json' -Content $content
    Assert-Throws -Name 'feed-size-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongSize }

    $wrongPackageName = Copy-JsonObject $fixtureDocument
    $feed = @($wrongPackageName.feeds | Where-Object assetId -ceq 'windows-feed-json')[0]
    $content = ([string]$feed.content).Replace('Unlimotion-1.27.0-full.nupkg', 'Unlimotion-1.26.0-full.nupkg')
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongPackageName -FeedAssetId 'windows-feed-json' -Content $content
    Assert-Throws -Name 'feed-filename-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongPackageName }

    $wrongLegacyHash = Copy-JsonObject $fixtureDocument
    $feed = @($wrongLegacyHash.feeds | Where-Object assetId -ceq 'windows-feed-legacy')[0]
    $content = ([string]$feed.content).Replace('3D218564C242EBC2237738B6C9D8445DF16B5402', ('0' * 40))
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongLegacyHash -FeedAssetId 'windows-feed-legacy' -Content $content
    Assert-Throws -Name 'legacy-feed-sha1-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongLegacyHash }

    $wrongChannel = Copy-JsonObject $manifestDocument
    @($wrongChannel.relations | Where-Object id -ceq 'windows-json-feed-to-package')[0].channel = 'linux'
    Assert-Throws -Name 'feed-wrong-channel' -Action { Test-FeedRelations $wrongChannel $fixtureDocument }
}

if ($Area -in @('All', 'IdentityTriggers')) {
    Test-IdentityContract
    Add-Check -Name 'identity:dual-tag-trigger-version-code'
    $areasRun.Add('IdentityTriggers')
}

if ($Area -in @('All', 'Retry')) {
    Test-RetryContract -ManifestDocument $manifestDocument
    Add-Check -Name 'retry:exact-budgets-and-classification'
    $areasRun.Add('Retry')

    $badRetry = Copy-JsonObject $manifestDocument
    $badRetry.retryPolicy.aptNetwork.maxAttempts = 4
    Assert-Throws -Name 'retry-apt-budget' -Action { Test-RetryContract $badRetry }

    $badCleanup = Copy-JsonObject $manifestDocument
    $badCleanup.retryPolicy.emulatorBoot.cleanup = 'reuse-avd'
    Assert-Throws -Name 'retry-emulator-cleanup' -Action { Test-RetryContract $badCleanup }

    $retryDeterministic = Copy-JsonObject $manifestDocument
    $retryDeterministic.retryPolicy.deterministic.maxAttempts = 2
    Assert-Throws -Name 'retry-deterministic-failure' -Action { Test-RetryContract $retryDeterministic }
}

if ($Area -in @('All', 'WorkflowSecurity')) {
    $workflowText = Get-Content -LiteralPath $script:workflowPath -Raw -Encoding utf8
    $macosBuilderPath = Resolve-ExistingFile -Path (Join-Path $script:repositoryRoot 'scripts\build-macos-distribution.sh') `
        -DisplayName 'macOS distribution builder'
    $macosBuilderText = Get-Content -LiteralPath $macosBuilderPath -Raw -Encoding utf8
    Test-MacosCandidateSigningContract -BuilderText $macosBuilderText
    Add-Check -Name 'macos-signing:velopack-and-legacy-bundles-adhoc-sealed'

    $macosValidatorPath = Resolve-ExistingFile -Path (Join-Path $script:repositoryRoot 'scripts\test-macos-distribution.sh') `
        -DisplayName 'macOS distribution validator'
    $macosValidatorText = Get-Content -LiteralPath $macosValidatorPath -Raw -Encoding utf8
    Test-MacosLaunchIsolationContract -ValidatorText $macosValidatorText
    Add-Check -Name 'macos-launch:isolated-writable-task-storage'

    $windowsValidatorPath = Resolve-ExistingFile -Path (Join-Path $script:repositoryRoot 'scripts\Test-WindowsDistribution.ps1') `
        -DisplayName 'Windows distribution validator'
    $windowsValidatorText = Get-Content -LiteralPath $windowsValidatorPath -Raw -Encoding utf8
    Test-WindowsLaunchIsolationContract -ValidatorText $windowsValidatorText
    Add-Check -Name 'windows-launch:isolated-writable-task-storage'
    Test-ProcessArgumentListPreservesSpacedConfigPath
    Add-Check -Name 'windows-launch:argument-list-preserves-spaced-config-path'

    $linuxValidatorPath = Resolve-ExistingFile -Path (Join-Path $script:repositoryRoot 'scripts\smoke-linux-artifacts.sh') `
        -DisplayName 'Linux distribution validator'
    $linuxValidatorText = Get-Content -LiteralPath $linuxValidatorPath -Raw -Encoding utf8
    Test-LinuxLaunchIsolationContract -ValidatorText $linuxValidatorText
    Add-Check -Name 'linux-launch:isolated-writable-task-storage'

    $macosLaunchIsolationFixtures = @(
        [pscustomobject]@{
            Name = 'macos-launch-task-storage-config-missing'
            Text = Replace-WorkflowFixtureOnce -Text $macosValidatorText `
                -Pattern '(?m)^  jq -cn --arg path "\$task_storage" .+$' `
                -Replacement '  # isolated TaskStorage config removed by fixture' `
                -Name 'macos-launch-task-storage-config-missing'
        },
        [pscustomobject]@{
            Name = 'macos-launch-root-task-storage-forbidden'
            Text = Replace-WorkflowFixtureOnce -Text $macosValidatorText `
                -Pattern '(?m)^  local task_storage="\$run_directory/Tasks"$' `
                -Replacement '  local task_storage="/Tasks"' `
                -Name 'macos-launch-root-task-storage-forbidden'
        },
        [pscustomobject]@{
            Name = 'macos-launch-late-root-task-storage-reassignment'
            Text = Replace-WorkflowFixtureOnce -Text $macosValidatorText `
                -Pattern '(?m)^  local task_storage="\$run_directory/Tasks"$' `
                -Replacement "  local task_storage=`"`$run_directory/Tasks`"`n  task_storage=`"/Tasks`"" `
                -Name 'macos-launch-late-root-task-storage-reassignment'
        }
    )
    foreach ($macosLaunchIsolationFixture in $macosLaunchIsolationFixtures) {
        Assert-Throws -Name $macosLaunchIsolationFixture.Name -Action {
            Test-MacosLaunchIsolationContract -ValidatorText $macosLaunchIsolationFixture.Text
        }
    }

    $windowsLaunchIsolationFixtures = @(
        [pscustomobject]@{
            Name = 'windows-launch-task-storage-config-missing'
            Text = Replace-WorkflowFixtureOnce -Text $windowsValidatorText `
                -Pattern '(?m)^    @\{ TaskStorage = .+$' `
                -Replacement '    # isolated TaskStorage config removed by fixture' `
                -Name 'windows-launch-task-storage-config-missing'
        },
        [pscustomobject]@{
            Name = 'windows-launch-root-task-storage-forbidden'
            Text = Replace-WorkflowFixtureOnce -Text $windowsValidatorText `
                -Pattern '(?m)^    \$taskStorage = Join-Path \$runDirectory ''Tasks''$' `
                -Replacement "    `$taskStorage = 'C:\Tasks'" `
                -Name 'windows-launch-root-task-storage-forbidden'
        },
        [pscustomobject]@{
            Name = 'windows-launch-late-root-task-storage-reassignment'
            Text = Replace-WorkflowFixtureOnce -Text $windowsValidatorText `
                -Pattern '(?m)^    \$taskStorage = Join-Path \$runDirectory ''Tasks''$' `
                -Replacement "    `$taskStorage = Join-Path `$runDirectory 'Tasks'`n    `$taskStorage = 'C:\Tasks'" `
                -Name 'windows-launch-late-root-task-storage-reassignment'
        },
        [pscustomobject]@{
            Name = 'windows-launch-flattened-config-argument'
            Text = Replace-WorkflowFixtureOnce -Text $windowsValidatorText `
                -Pattern '(?m)^    \$startInfo\.ArgumentList\.Add\("--config=\$config"\)$' `
                -Replacement '    $process = Start-Process -FilePath $Executable -ArgumentList "--config=$config" -PassThru' `
                -Name 'windows-launch-flattened-config-argument'
        },
        [pscustomobject]@{
            Name = 'windows-launch-competing-single-dash-config-argument'
            Text = Replace-WorkflowFixtureOnce -Text $windowsValidatorText `
                -Pattern '(?m)^    \$startInfo\.ArgumentList\.Add\("--config=\$config"\)$' `
                -Replacement "    `$startInfo.ArgumentList.Add(`"-config=C:\Tasks\settings.json`")`n    `$startInfo.ArgumentList.Add(`"--config=`$config`")" `
                -Name 'windows-launch-competing-single-dash-config-argument'
        }
    )
    foreach ($windowsLaunchIsolationFixture in $windowsLaunchIsolationFixtures) {
        Assert-Throws -Name $windowsLaunchIsolationFixture.Name -Action {
            Test-WindowsLaunchIsolationContract -ValidatorText $windowsLaunchIsolationFixture.Text
        }
    }

    $linuxLateRootRewrite = @'
    printf '%s\n' '{"TaskStorage":{"Path":"/home/unlimotion-test/unlimotion-data/Tasks","IsServerMode":"False"}}' > /home/unlimotion-test/unlimotion-data/config.json
    printf '%s\n' '{"TaskStorage":{"Path":"/Tasks","IsServerMode":"False"}}' > /home/unlimotion-test/unlimotion-data/config.json
'@.TrimEnd()
    $linuxLaunchIsolationFixtures = @(
        [pscustomobject]@{
            Name = 'linux-launch-task-storage-config-missing'
            Text = Replace-WorkflowFixtureOnce -Text $linuxValidatorText `
                -Pattern '(?m)^    printf ''%s\\n'' ''\{"TaskStorage".+$' `
                -Replacement '    # isolated TaskStorage config removed by fixture' `
                -Name 'linux-launch-task-storage-config-missing'
        },
        [pscustomobject]@{
            Name = 'linux-launch-root-task-storage-forbidden'
            Text = Replace-WorkflowFixtureOnce -Text $linuxValidatorText `
                -Pattern '"Path":"/home/unlimotion-test/unlimotion-data/Tasks"' `
                -Replacement '"Path":"/Tasks"' `
                -Name 'linux-launch-root-task-storage-forbidden'
        },
        [pscustomobject]@{
            Name = 'linux-launch-late-root-task-storage-rewrite'
            Text = Replace-WorkflowFixtureOnce -Text $linuxValidatorText `
                -Pattern '(?m)^    printf ''%s\\n'' ''\{"TaskStorage".+$' `
                -Replacement $linuxLateRootRewrite `
                -Name 'linux-launch-late-root-task-storage-rewrite'
        },
        [pscustomobject]@{
            Name = 'linux-metadata-seeded-storage-overclaim'
            Text = Replace-WorkflowFixtureOnce -Text $linuxValidatorText `
                -Pattern '(?m)^LAUNCH_CONFIGURATION="notApplicable"$' `
                -Replacement 'LAUNCH_CONFIGURATION="seeded-isolated-task-storage"' `
                -Name 'linux-metadata-seeded-storage-overclaim'
        },
        [pscustomobject]@{
            Name = 'linux-launch-competing-single-dash-config-argument'
            Text = Replace-WorkflowFixtureOnce -Text $linuxValidatorText `
                -Pattern '(?m)^    "\$executable" "--config=\$config_path" > "\$app_log" 2>&1 &$' `
                -Replacement '    "$executable" "-config=/Tasks/settings.json" "--config=$config_path" > "$app_log" 2>&1 &' `
                -Name 'linux-launch-competing-single-dash-config-argument'
        }
    )
    foreach ($linuxLaunchIsolationFixture in $linuxLaunchIsolationFixtures) {
        Assert-Throws -Name $linuxLaunchIsolationFixture.Name -Action {
            Test-LinuxLaunchIsolationContract -ValidatorText $linuxLaunchIsolationFixture.Text
        }
    }

    $macosSigningFixtures = @(
        [pscustomobject]@{
            Name = 'macos-vpk-adhoc-signing-missing'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^  --signAppIdentity - \\$' -Replacement '  # Velopack ad-hoc signing removed by fixture' `
                -Name 'macos-vpk-adhoc-signing-missing'
        },
        [pscustomobject]@{
            Name = 'macos-legacy-adhoc-signing-missing'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^codesign --force --deep --sign - "\$app_path"$' `
                -Replacement '# Legacy bundle signing removed by fixture' -Name 'macos-legacy-adhoc-signing-missing'
        },
        [pscustomobject]@{
            Name = 'macos-legacy-signature-verification-missing'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^codesign --verify --deep --strict --verbose=2 "\$app_path"$' `
                -Replacement '# Legacy bundle verification removed by fixture' -Name 'macos-legacy-signature-verification-missing'
        },
        [pscustomobject]@{
            Name = 'macos-dynamic-signing-identity-forbidden'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^  --signAppIdentity - \\$' -Replacement '  --signAppIdentity "$MACOS_SIGNING_IDENTITY" \' `
                -Name 'macos-dynamic-signing-identity-forbidden'
        },
        [pscustomobject]@{
            Name = 'macos-signing-credentials-forbidden'
            Text = $macosBuilderText + "`nsecurity import fixture.p12 -P secret -A`n"
        },
        [pscustomobject]@{
            Name = 'macos-vpk-signing-outside-command-block'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^  --packAuthors Kibnet \\$' -Replacement '  --packAuthors Kibnet' `
                -Name 'macos-vpk-signing-outside-command-block'
        },
        [pscustomobject]@{
            Name = 'macos-extra-dynamic-codesign-forbidden'
            Text = $macosBuilderText + "`ncodesign --sign `"`$MACOS_SIGNING_IDENTITY`" `"`$app_path`"`n"
        },
        [pscustomobject]@{
            Name = 'macos-codesign-preflight-missing'
            Text = Replace-WorkflowFixtureOnce -Text $macosBuilderText `
                -Pattern '(?m)^(for command_name in [^;\r\n]*) codesign(; do)$' -Replacement '$1$2' `
                -Name 'macos-codesign-preflight-missing'
        }
    )
    $legacySignLine = 'codesign --force --deep --sign - "$app_path"'
    $legacyPackageLine = 'productbuild --component "$app_path" /Applications "$asset_directory/$legacy_pkg_name"'
    $misorderedMacosSigning = $macosBuilderText.Replace($legacySignLine, '__legacy_sign_order_fixture__')
    $misorderedMacosSigning = $misorderedMacosSigning.Replace(
        $legacyPackageLine,
        $legacyPackageLine + "`n" + $legacySignLine)
    $misorderedMacosSigning = $misorderedMacosSigning.Replace('__legacy_sign_order_fixture__', '# Legacy signing moved by fixture')
    $macosSigningFixtures += [pscustomobject]@{
        Name = 'macos-legacy-signing-after-productbuild'
        Text = $misorderedMacosSigning
    }
    foreach ($macosSigningFixture in $macosSigningFixtures) {
        Assert-Throws -Name $macosSigningFixture.Name -Action {
            Test-MacosCandidateSigningContract -BuilderText $macosSigningFixture.Text
        }
    }

    Test-WorkflowSecurityContract -WorkflowText $workflowText
    Test-EmbeddedWorkflowBehaviorFixtures -WorkflowText $workflowText
    Add-Check -Name 'workflow-security:pr-manual-read-only-pinned-no-mutation'
    Add-Check -Name 'workflow-security:stable-final-all-needs-fail-closed'
    Add-Check -Name 'workflow-security:root-build-scope-direct-identity'
    Add-Check -Name 'workflow-security:atomic-upload-settings'

    $producerNames = @('contract', 'windows_x64', 'linux_x64', 'macos_x64', 'macos_arm64', 'android_build', 'android_api23', 'android_api36')
    $relevantSuccess = @{ changes = 'success' }
    foreach ($producerName in $producerNames) { $relevantSuccess[$producerName] = 'success' }
    $successOutcome = Test-DistributionProducerResults -Relevant $true -Results $relevantSuccess
    Assert-Condition ($successOutcome.status -ceq 'pendingAggregate' -and [bool]$successOutcome.producersOk) 'Relevant all-success producer fixture must proceed to aggregate.'

    $irrelevantSkipped = @{ changes = 'success' }
    foreach ($producerName in $producerNames) { $irrelevantSkipped[$producerName] = 'skipped' }
    $notApplicableOutcome = Test-DistributionProducerResults -Relevant $false -Results $irrelevantSkipped
    Assert-Condition ($notApplicableOutcome.status -ceq 'notApplicable' -and -not [bool]$notApplicableOutcome.applicable) 'Irrelevant all-skipped producer fixture must be notApplicable.'
    Add-Check -Name 'workflow-producers:relevant-success-and-irrelevant-not-applicable'

    $changesFailed = $relevantSuccess.Clone()
    $changesFailed.changes = 'failure'
    Assert-Throws -Name 'producer-changes-failure' -Action {
        Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $true -Results $changesFailed)
    }
    foreach ($producerResult in @('failure', 'skipped', 'cancelled')) {
        $relevantFailure = $relevantSuccess.Clone()
        $relevantFailure.windows_x64 = $producerResult
        Assert-Throws -Name "producer-relevant-$producerResult" -Action {
            Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $true -Results $relevantFailure)
        }
    }
    $irrelevantUnexpectedSuccess = $irrelevantSkipped.Clone()
    $irrelevantUnexpectedSuccess.contract = 'success'
    Assert-Throws -Name 'producer-irrelevant-unexpected-success' -Action {
        Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $false -Results $irrelevantUnexpectedSuccess)
    }

    $triggerMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^on:\s*$' -Replacement ("on:`n  push:") -Name 'disallowed-trigger'
    Assert-Throws -Name 'workflow-disallowed-push-trigger' -Action { Test-WorkflowSecurityContract $triggerMutation }

    $writeMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^  contents:\s*read\s*$' -Replacement '  contents: write' -Name 'write-permission'
    Assert-Throws -Name 'workflow-write-permission' -Action { Test-WorkflowSecurityContract $writeMutation }

    $secretMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^env:\s*$' -Replacement ("env:`n" + '  FORBIDDEN_TOKEN: ${{ secrets.RELEASE_TOKEN }}') -Name 'secret-reference'
    Assert-Throws -Name 'workflow-secret-reference' -Action { Test-WorkflowSecurityContract $secretMutation }

    $floatingActionMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'actions/checkout@[0-9a-f]{40}' -Replacement 'actions/checkout@v4' -Name 'floating-action'
    Assert-Throws -Name 'workflow-floating-action' -Action { Test-WorkflowSecurityContract $floatingActionMutation }

    $mutationCommand = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          set -euo pipefail\s*$' -Replacement ("          set -euo pipefail`n          gh release create v0.0.0-forbidden") -Name 'release-mutation'
    Assert-Throws -Name 'workflow-release-mutation-command' -Action { Test-WorkflowSecurityContract $mutationCommand }

    $withoutAlways = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^    if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$' -Replacement '    if: ${{ success() }}' -Name 'stable-final-always'
    Assert-Throws -Name 'workflow-final-without-always' -Action { Test-WorkflowSecurityContract $withoutAlways }

    $withoutNeed = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^      - android_api36\s*$' -Replacement '      # android_api36 removed by negative fixture' -Name 'stable-final-needs'
    Assert-Throws -Name 'workflow-final-missing-producer-need' -Action { Test-WorkflowSecurityContract $withoutNeed }

    $wrongRelevantRule = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'all\(value == "success" for value in mandatory\.values\(\)\)' -Replacement 'all(value == "skipped" for value in mandatory.values())' -Name 'relevant-all-success'
    Assert-Throws -Name 'workflow-relevant-not-all-success' -Action { Test-WorkflowSecurityContract $wrongRelevantRule }

    $wrongIrrelevantRule = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'if value != "skipped"' -Replacement 'if value != "success"' -Name 'irrelevant-all-skipped'
    Assert-Throws -Name 'workflow-irrelevant-not-all-skipped' -Action { Test-WorkflowSecurityContract $wrongIrrelevantRule }

    Assert-Condition ($workflowText.Contains('steps.inspect.outputs.source_short', [StringComparison]::Ordinal)) 'Workflow fixture is missing direct source_short references.'
    $wrongSourceShort = $workflowText.Replace('steps.inspect.outputs.source_short', 'needs.changes.outputs.source_short')
    Assert-Throws -Name 'workflow-final-source-short-depends-on-changes' -Action { Test-WorkflowSecurityContract $wrongSourceShort }

    $withoutBuildScope = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'Directory\\\.\(Build\|Packages\)\\\.\(props\|targets\)\$' -Replacement 'Directory\.Packages\.props$' -Name 'root-build-scope'
    Assert-Throws -Name 'workflow-root-build-scope-missing' -Action { Test-WorkflowSecurityContract $withoutBuildScope }

    $withoutAttributesScope = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '\\\.gitattributes\$' -Replacement '\.editorconfig$' -Name 'gitattributes-scope'
    Assert-Throws -Name 'workflow-gitattributes-scope-missing' -Action { Test-WorkflowSecurityContract $withoutAttributesScope }

    $linuxCheckerLine = '      - name: Validate canonical JSON byte identity on Linux'
    $withoutLinuxChecker = Replace-WorkflowFixtureOnce -Text $workflowText `
        -Pattern ([regex]::Escape($linuxCheckerLine)) `
        -Replacement '      - name: Linux blob parity checker removed by negative fixture' `
        -Name 'linux-blob-parity-checker-missing'
    Assert-Throws -Name 'workflow-linux-blob-parity-checker-missing' `
        -MessagePattern 'Linux blob parity checker step must exist exactly once' -Action {
        Test-WorkflowSecurityContract $withoutLinuxChecker
    }

    $linuxBuildLine = '      - name: Build Linux candidates from one publish'
    $linuxCheckerTemporaryLine = '      - name: __blob_parity_checker_order_fixture__'
    Assert-Condition (
        $workflowText.Contains($linuxCheckerLine, [StringComparison]::Ordinal) -and
        $workflowText.Contains($linuxBuildLine, [StringComparison]::Ordinal)) `
        'Workflow fixture is missing Linux checker/build step names.'
    $misorderedLinuxChecker = $workflowText.Replace($linuxCheckerLine, $linuxCheckerTemporaryLine)
    $misorderedLinuxChecker = $misorderedLinuxChecker.Replace($linuxBuildLine, $linuxCheckerLine)
    $misorderedLinuxChecker = $misorderedLinuxChecker.Replace($linuxCheckerTemporaryLine, $linuxBuildLine)
    Assert-Throws -Name 'workflow-linux-blob-parity-checker-after-build' `
        -MessagePattern 'Linux blob parity order must be checkout < checker < build < retain < stage < upload' -Action {
        Test-WorkflowSecurityContract $misorderedLinuxChecker
    }

    $linuxBuilderRootLine = '            --output-root artifacts/distribution-validation/linux-x64 \'
    $linuxBuilderRootCollision = Replace-WorkflowFixtureOnce -Text $workflowText `
        -Pattern ([regex]::Escape($linuxBuilderRootLine)) `
        -Replacement '            --output-root artifacts/distribution-validation \' `
        -Name 'linux-blob-parity-builder-root-collision'
    Assert-Throws -Name 'workflow-linux-blob-parity-builder-root-collision' `
        -MessagePattern 'Linux build step must use exact --output-root artifacts/distribution-validation/linux-x64' -Action {
        Test-WorkflowSecurityContract $linuxBuilderRootCollision
    }

    $macosBuilderRootLine = '            --output-dir artifacts/distribution-validation/macos-x64 \'
    $macosBuilderRootCollision = Replace-WorkflowFixtureOnce -Text $workflowText `
        -Pattern ([regex]::Escape($macosBuilderRootLine)) `
        -Replacement '            --output-dir artifacts/distribution-validation \' `
        -Name 'macos-blob-parity-builder-root-collision'
    Assert-Throws -Name 'workflow-macos-blob-parity-builder-root-collision' `
        -MessagePattern 'macOS x64 build step must use exact --output-dir artifacts/distribution-validation/macos-x64' -Action {
        Test-WorkflowSecurityContract $macosBuilderRootCollision
    }

    foreach ($orderPlan in @(
            [pscustomobject]@{
                Slug = 'linux'
                Label = 'Linux'
                Retain = '      - name: Retain Linux blob parity evidence'
                Stage = '      - name: Stage Linux tar transport'
            },
            [pscustomobject]@{
                Slug = 'macos'
                Label = 'macOS x64'
                Retain = '      - name: Retain macOS blob parity evidence'
                Stage = '      - name: Stage macOS x64 evidence'
            })) {
        $temporaryLine = "      - name: __blob_parity_$($orderPlan.Slug)_retain_order_fixture__"
        Assert-Condition (
            $workflowText.Contains($orderPlan.Retain, [StringComparison]::Ordinal) -and
            $workflowText.Contains($orderPlan.Stage, [StringComparison]::Ordinal)) `
            "Workflow fixture is missing $($orderPlan.Label) retain/stage step names."
        $misorderedRetain = $workflowText.Replace($orderPlan.Retain, $temporaryLine)
        $misorderedRetain = $misorderedRetain.Replace($orderPlan.Stage, $orderPlan.Retain)
        $misorderedRetain = $misorderedRetain.Replace($temporaryLine, $orderPlan.Stage)
        Assert-Throws -Name "workflow-$($orderPlan.Slug)-blob-parity-retain-after-stage" `
            -MessagePattern "$($orderPlan.Label) blob parity order must be checkout < checker < build < retain < stage < upload" -Action {
            Test-WorkflowSecurityContract $misorderedRetain
        }
    }

    $shaBindingPlans = @(
        [pscustomobject]@{ Slug = 'contract'; Step = 'Validate schemas, inventory, identity, feeds and retry policy' },
        [pscustomobject]@{ Slug = 'linux'; Step = 'Validate canonical JSON byte identity on Linux' },
        [pscustomobject]@{ Slug = 'macos'; Step = 'Validate canonical JSON byte identity on macOS' },
        [pscustomobject]@{ Slug = 'aggregate'; Step = 'Merge platform evidence and aggregate 22 exact assets' }
    )
    foreach ($bindingPlan in $shaBindingPlans) {
        foreach ($binding in @(
                [pscustomobject]@{
                    Variable = 'SOURCE_SHA'
                    Pattern = '(?m)^          SOURCE_SHA: \$\{\{ github\.sha \}\}\s*$'
                },
                [pscustomobject]@{
                    Variable = 'WORKFLOW_SHA'
                    Pattern = '(?m)^          WORKFLOW_SHA: \$\{\{ job\.workflow_sha \}\}\s*$'
                })) {
            $withoutBinding = Replace-WorkflowNamedStepFixtureOnce -WorkflowText $workflowText `
                -StepName $bindingPlan.Step -Pattern $binding.Pattern `
                -Replacement "          # $($binding.Variable) removed by negative fixture" `
                -Name "$($bindingPlan.Slug)-$($binding.Variable.ToLowerInvariant())-binding"
            Assert-Throws -Name "workflow-$($bindingPlan.Slug)-$($binding.Variable.ToLowerInvariant())-binding-missing" `
                -MessagePattern "must bind exact step-local $($binding.Variable)" -Action {
                Test-WorkflowSecurityContract $withoutBinding
            }
        }
    }

    $aggregateAreaLine = '            -Area BlobParityAggregate `'
    $withoutBlobParityAggregate = Replace-WorkflowFixtureOnce -Text $workflowText `
        -Pattern ([regex]::Escape($aggregateAreaLine)) -Replacement '            -Area Evidence `' `
        -Name 'blob-parity-aggregate-call-missing'
    Assert-Throws -Name 'workflow-blob-parity-aggregate-call-missing' `
        -MessagePattern 'Step id aggregate must validate the exact three reports' -Action {
        Test-WorkflowSecurityContract $withoutBlobParityAggregate
    }

    $aggregateContinueOnError = Replace-WorkflowNamedStepFixtureOnce -WorkflowText $workflowText `
        -StepName 'Merge platform evidence and aggregate 22 exact assets' `
        -Pattern '(?m)^        id:\s*aggregate\s*$' `
        -Replacement "        id: aggregate`n        continue-on-error: true" `
        -Name 'blob-parity-aggregate-continue-on-error'
    Assert-Throws -Name 'workflow-blob-parity-aggregate-continue-on-error' `
        -MessagePattern 'step id aggregate must remain fail-closed without continue-on-error' -Action {
        Test-WorkflowSecurityContract $aggregateContinueOnError
    }

    $finalOutcomeDrift = Replace-WorkflowNamedStepFixtureOnce -WorkflowText $workflowText `
        -StepName 'Enforce stable fail-closed verdict' `
        -Pattern 'AGGREGATE_OUTCOME: \$\{\{ steps\.aggregate\.outcome \}\}' `
        -Replacement 'AGGREGATE_OUTCOME: ${{ steps.inspect.outcome }}' `
        -Name 'blob-parity-final-outcome-binding'
    Assert-Throws -Name 'workflow-blob-parity-final-outcome-binding-missing' `
        -MessagePattern 'Stable final enforcement must fail when step id aggregate does not succeed' -Action {
        Test-WorkflowSecurityContract $finalOutcomeDrift
    }

    $contractPayloadLine = "          `$payloads = @('identity.json', 'contract-evidence.json', 'blob-parity.json') | ForEach-Object {"
    $withoutContractBlobParityReceipt = Replace-WorkflowFixtureOnce -Text $workflowText `
        -Pattern ([regex]::Escape($contractPayloadLine)) `
        -Replacement "          `$payloads = @('identity.json', 'contract-evidence.json') | ForEach-Object {" `
        -Name 'contract-blob-parity-receipt-payload-missing'
    Assert-Throws -Name 'workflow-contract-blob-parity-receipt-payload-missing' `
        -MessagePattern 'Contract receipt must bind identity.json, contract-evidence.json and blob-parity.json' -Action {
        Test-WorkflowSecurityContract $withoutContractBlobParityReceipt
    }

    $withoutWorkflowSha = $workflowText.Replace('${{ job.workflow_sha }}', '${{ github.sha }}')
    Assert-Throws -Name 'workflow-job-workflow-sha-missing' -Action { Test-WorkflowSecurityContract $withoutWorkflowSha }

    $withoutOverwriteGuard = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          overwrite:\s*false\s*$' -Replacement '          overwrite: true' -Name 'upload-overwrite'
    Assert-Throws -Name 'workflow-upload-overwrite-enabled' -Action { Test-WorkflowSecurityContract $withoutOverwriteGuard }

    $withoutUploadAttempt = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          name:\s*distribution-contract-\$\{\{\s*needs\.changes\.outputs\.source_short\s*\}\}-attempt-\$\{\{\s*github\.run_attempt\s*\}\}\s*$' -Replacement '          name: distribution-contract-${{ needs.changes.outputs.source_short }}' -Name 'upload-attempt-scope'
    Assert-Throws -Name 'workflow-upload-without-run-attempt' -Action { Test-WorkflowSecurityContract $withoutUploadAttempt }

    $broadAndroidDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          artifact-ids:\s*\$\{\{\s*needs\.android_build\.outputs\.artifact_id\s*\}\}\s*$' -Replacement '          pattern: distribution-android-*' -Name 'android-broad-download'
    Assert-Throws -Name 'workflow-android-broad-download' -Action { Test-WorkflowSecurityContract $broadAndroidDownload }

    $nestedAndroidDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          merge-multiple:\s*true\s*$' -Replacement '          merge-multiple: false' -Name 'android-download-nested'
    Assert-Throws -Name 'workflow-android-download-not-flat' -Action { Test-WorkflowSecurityContract $nestedAndroidDownload }

    $flatAggregateDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          merge-multiple:\s*false\s*$' -Replacement '          merge-multiple: true' -Name 'aggregate-download-flat'
    Assert-Throws -Name 'workflow-aggregate-download-not-isolated' -Action { Test-WorkflowSecurityContract $flatAggregateDownload }

    $areasRun.Add('WorkflowSecurity')
}

if ($Area -in @('All', 'Evidence')) {
    $identity = Invoke-ResolverDocument -RawTag 'v1.2.3' -IncludeSupportMatrix
    Test-AndroidNativeEvidenceConverters -IdentityDocument $identity
    Test-TaskStorageEvidenceContracts
    $platformFixtures = @(New-PlatformEvidenceFixtures -ManifestDocument $manifestDocument -IdentityDocument $identity)
    foreach ($platformFixture in $platformFixtures) {
        Assert-JsonObjectSchema -Document $platformFixture -SchemaPath $script:evidenceSchemaPath -Name "platform-$($platformFixture.platform)-$($platformFixture.architecture)"
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-evidence-contract-' + [Guid]::NewGuid().ToString('N'))
    try {
        $positive = Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'positive-default-full-set') -IdentityDocument $identity -Platforms $platformFixtures
        Assert-Condition ($positive.assetCount -eq 22) 'Aggregate default must cover all 22 manifest assets.'
        Assert-Condition (@($positive.mandatoryCellIds).Count -eq 15) 'Aggregate must cover all 15 mandatory native cells.'
        Assert-Condition ($positive.productionReady -eq $false) 'Stage-3 aggregate must remain non-production-ready.'
        Assert-Condition ($positive.productionSignatureEligibility -ceq 'notApplicable') 'Stage-3 aggregate signature eligibility must be explicit N/A.'
        Add-Check -Name 'evidence:full-schema-aggregate-22-assets-15-cells'

        Assert-Throws -Name 'aggregate-explicit-partial-asset-set' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'partial-assets') -IdentityDocument $identity -Platforms $platformFixtures -RequestedAssetIds @('windows-setup-x64')
        }

        $missingCell = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($missingCell | Where-Object platform -CEQ 'android')[0]
        $androidEnvelope.nativeCells = @($androidEnvelope.nativeCells | Where-Object id -CNE 'android-api-36-x64-emulator')
        Assert-Throws -Name 'aggregate-missing-native-cell' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-cell') -IdentityDocument $identity -Platforms $missingCell
        }

        $missingNativeReference = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($missingNativeReference | Where-Object platform -CEQ 'android')[0]
        $androidEnvelope.nativeEvidence = @($androidEnvelope.nativeEvidence | Where-Object fileName -CNE 'native-cache-evidence.json')
        Assert-Throws -Name 'aggregate-missing-android-native-reference' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-android-native-reference') -IdentityDocument $identity -Platforms $missingNativeReference
        }

        $wrongNativeReferenceMode = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($wrongNativeReferenceMode | Where-Object platform -CEQ 'android')[0]
        @($androidEnvelope.nativeEvidence | Where-Object fileName -CEQ 'native-cache-evidence.json')[0].mode = 'artifact'
        Assert-Throws -Name 'aggregate-wrong-android-native-reference-mode' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-android-native-reference-mode') -IdentityDocument $identity -Platforms $wrongNativeReferenceMode
        }

        $wrongNativeCellReference = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($wrongNativeCellReference | Where-Object platform -CEQ 'android')[0]
        @($androidEnvelope.nativeCells | Where-Object id -CEQ 'android-api-23-x64-emulator')[0].evidenceSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-native-cell-reference-sha-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'native-cell-reference-sha-mismatch') -IdentityDocument $identity -Platforms $wrongNativeCellReference
        }

        $wrongOs = Copy-JsonObject $platformFixtures
        @($wrongOs | Where-Object platform -CEQ 'windows')[0].nativeCells[0].osVersion = '2025'
        Assert-Throws -Name 'aggregate-wrong-native-os' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-os') -IdentityDocument $identity -Platforms $wrongOs
        }

        $wrongArchitecture = Copy-JsonObject $platformFixtures
        @($wrongArchitecture | Where-Object { $_.platform -ceq 'macos' -and $_.architecture -ceq 'arm64' })[0].nativeCells[0].architecture = 'x64'
        Assert-Throws -Name 'aggregate-wrong-native-architecture' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-architecture') -IdentityDocument $identity -Platforms $wrongArchitecture
        }

        $failedCell = Copy-JsonObject $platformFixtures
        @($failedCell | Where-Object platform -CEQ 'linux')[0].nativeCells[0].status = 'fail'
        Assert-Throws -Name 'aggregate-failed-native-cell' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'failed-cell') -IdentityDocument $identity -Platforms $failedCell
        }

        $unconfiguredLaunchClaim = Copy-JsonObject $platformFixtures
        @($unconfiguredLaunchClaim | Where-Object platform -CEQ 'windows')[0].nativeCells[0].mode = 'setup-and-portable-install-launch'
        Assert-Throws -Name 'aggregate-unconfigured-first-run-overclaim' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'unconfigured-first-run-overclaim') -IdentityDocument $identity -Platforms $unconfiguredLaunchClaim
        }

        $wrongMode = Copy-JsonObject $platformFixtures
        @($wrongMode | Where-Object platform -CEQ 'linux')[0].transport.unixMode.tarStoredMode = '0644'
        Assert-Throws -Name 'transport-wrong-tar-mode' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-mode') -IdentityDocument $identity -Platforms $wrongMode
        }

        $missingPlatform = @((Copy-JsonObject $platformFixtures) | Where-Object { $_.platform -cne 'windows' })
        Assert-Throws -Name 'aggregate-missing-platform-envelope' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-platform') -IdentityDocument $identity -Platforms $missingPlatform
        }

        $wrongIdentity = Copy-JsonObject $identity
        $wrongIdentity.supportMatrixSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-support-matrix-hash-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-support-hash') -IdentityDocument $wrongIdentity -Platforms $platformFixtures
        }

        $wrongManifestIdentity = Copy-JsonObject $identity
        $wrongManifestIdentity.manifestSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-manifest-hash-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-manifest-hash') -IdentityDocument $wrongManifestIdentity -Platforms $platformFixtures
        }

        $freeFormEligibility = Copy-JsonObject $platformFixtures
        @($freeFormEligibility | Where-Object platform -CEQ 'windows')[0] | Add-Member -NotePropertyName productionSignatureEligible -NotePropertyValue $true
        Assert-Throws -Name 'evidence-free-form-production-signature-eligibility' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'free-form-eligibility') -IdentityDocument $identity -Platforms $freeFormEligibility
        }

        $illegalProductionReady = Copy-JsonObject $platformFixtures
        @($illegalProductionReady | Where-Object platform -CEQ 'android')[0].productionReady = $true
        Assert-Throws -Name 'evidence-stage3-production-ready' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'production-ready') -IdentityDocument $identity -Platforms $illegalProductionReady
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
    Add-Check -Name 'evidence:transport-receipt-explicit-na-and-unix-mode'
    Add-Check -Name 'evidence:missing-failed-wrong-os-arch-fail-closed'
    Add-Check -Name 'evidence:task-storage-launch-state-strictly-typed'
    $areasRun.Add('Evidence')
}

$evidence = [ordered]@{
    schemaVersion = 1
    outcome = 'pass'
    areaRequested = $Area
    areasRun = @($areasRun)
    checks = @($script:checks)
    negativeFixtures = $script:negativeFixtureCount
    manifestSha256 = Get-LowerFileSha256 -Path $script:manifestPath
    fixtureSha256 = Get-LowerFileSha256 -Path $script:fixturePath
    supportMatrixSha256 = Get-LowerFileSha256 -Path $script:supportMatrixPath
    evidenceSchemaSha256 = Get-LowerFileSha256 -Path $script:evidenceSchemaPath
}
$evidenceJson = $evidence | ConvertTo-Json -Depth 20

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $fullEvidencePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidencePath)
    $evidenceDirectory = [System.IO.Path]::GetDirectoryName($fullEvidencePath)
    if ([string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        throw "EvidencePath must have a parent directory: $EvidencePath"
    }
    [System.IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        $fullEvidencePath,
        $evidenceJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

Write-Output $evidenceJson
