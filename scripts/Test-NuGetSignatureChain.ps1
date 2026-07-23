[CmdletBinding()]
param(
    [ValidateSet('GenerateBaseline', 'RunAttempt', 'SelfTest', 'Worker')]
    [string]$Mode = 'RunAttempt',
    [AllowEmptyString()]
    [string]$Lane = 'Signature',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$PackagesRoot,
    [AllowEmptyString()]
    [string]$ExpectedSourceSha,
    [AllowEmptyString()]
    [string]$RunAttempt = '1',
    [string]$EvidenceRoot,
    [AllowEmptyString()]
    [string]$ExpectedParentSha,
    [string]$OutputPath,
    [string]$BaselineAssetsRoot,
    [string]$DotNetExecutable = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:WorkerScriptPath = $PSCommandPath

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Get-ExpectedPackages {
    return @(
        @{ Id = 'ReactiveUI.Avalonia'; Version = '12.0.2' },
        @{ Id = 'ReactiveUI'; Version = '23.2.28' },
        @{ Id = 'Splat'; Version = '19.4.1' },
        @{ Id = 'Splat.Builder'; Version = '19.4.1' },
        @{ Id = 'Splat.Core'; Version = '19.4.1' },
        @{ Id = 'Splat.Logging'; Version = '19.4.1' }
    )
}

function Get-ExpectedAuthorFingerprint {
    return '4D2DDD563BC0ECF5C9B438E1CE32E3FCC69DAADAFC2D1BD9CF858FD9E755CFB9'
}

function Get-CanonicalRepositoryRoot {
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'src\Directory.Packages.props')) 'RepositoryRoot does not contain src\Directory.Packages.props.'
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'src\nuget.config')) 'RepositoryRoot does not contain src\nuget.config.'
    return $root
}

function Resolve-RunAttempt {
    $parsed = 0
    Assert-True ($RunAttempt -cmatch '^[1-9][0-9]{0,9}$') 'RunAttempt must be a canonical positive decimal integer.'
    Assert-True ([int]::TryParse($RunAttempt, [ref]$parsed)) 'RunAttempt is outside Int32 range.'
    return $parsed
}

function Resolve-SourceSha([string]$Root) {
    if (-not [string]::IsNullOrEmpty($ExpectedSourceSha)) {
        Assert-True ($ExpectedSourceSha -cmatch '^[0-9a-f]{40}$') 'ExpectedSourceSha must be a lowercase 40-hex Git SHA.'
        return $ExpectedSourceSha
    }

    $sha = (& git -C $Root rev-parse HEAD).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $sha -cmatch '^[0-9a-f]{40}$') 'Could not resolve the current Git SHA.'
    return $sha
}

function New-IsolatedPackagesRoot {
    if ([string]::IsNullOrWhiteSpace($PackagesRoot)) {
        $candidate = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-nuget-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $candidate -ErrorAction Stop | Out-Null
        return [IO.Path]::GetFullPath($candidate)
    }

    $candidate = [IO.Path]::GetFullPath($PackagesRoot)
    Assert-True (Test-Path -LiteralPath $candidate -PathType Container) 'PackagesRoot must be an existing directory.'
    Assert-True (-not (Get-ChildItem -LiteralPath $candidate -Force | Select-Object -First 1)) 'PackagesRoot must be empty before a signature attempt.'
    return $candidate
}

function Get-EvidenceRoot([string]$Root, [string]$SourceSha, [int]$Attempt) {
    if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) {
        $candidate = [IO.Path]::GetFullPath($EvidenceRoot)
        Assert-True (-not (Test-Path -LiteralPath $candidate)) 'EvidenceRoot must not already exist.'
        return $candidate
    }

    return (Join-Path ([IO.Path]::GetTempPath()) ("unlimotion-nuget-evidence-{0}-attempt-{1}-{2}" -f $SourceSha, $Attempt, [Guid]::NewGuid().ToString('N')))
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-CanonicalGraphHash([object[]]$Packages) {
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($package in $Packages) {
        $lines.Add(('{0}`t{1}`t{2}`t{3}`n' -f $package.id, $package.version, $package.source, $package.nupkgSha512))
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($lines -join [string]::Empty))
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Get-BaselineProjectPackageSet([string]$Root, [string]$ProjectPath, [string]$PackagesPath, [string]$AssetsPath) {
    Assert-True (Test-Path -LiteralPath $AssetsPath -PathType Leaf) "Missing staged assets file: $ProjectPath."
    $assets = Get-Content -Raw -LiteralPath $AssetsPath | ConvertFrom-Json -AsHashtable -Depth 32
    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($libraryKey in @($assets.libraries.Keys | Sort-Object)) {
        $separator = $libraryKey.LastIndexOf('/')
        Assert-True ($separator -gt 0) "Invalid assets library key: $libraryKey."
        $id = $libraryKey.Substring(0, $separator)
        $version = $libraryKey.Substring($separator + 1)
        $library = $assets.libraries[$libraryKey]
        if ($library.type -cne 'package') { continue }
        $nupkg = Join-Path $PackagesPath (("{0}\\{1}\\{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $version))
        Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "Missing package payload: $id $version."
        $packages.Add([ordered]@{
            id = $id
            version = $version
            source = 'nuget.org'
            nupkgSha512 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash.ToLowerInvariant()
        })
    }
    $ordered = @($packages | Sort-Object @{ Expression = { $_.id }; Ascending = $true }, @{ Expression = { $_.version }; Ascending = $true })
    return [ordered]@{ projectPath = $ProjectPath; packageSet = $ordered; graphSha256 = Get-CanonicalGraphHash -Packages $ordered }
}

function Get-ExpectedGraphTransitions {
    return @(
        @{ id = 'ReactiveUI.Avalonia'; baselineVersion = '12.0.1'; candidateVersion = '12.0.2' },
        @{ id = 'ReactiveUI'; baselineVersion = '23.2.27'; candidateVersion = '23.2.28' },
        @{ id = 'Splat'; baselineVersion = '19.3.1'; candidateVersion = '19.4.1' },
        @{ id = 'Splat.Builder'; baselineVersion = '19.3.1'; candidateVersion = '19.4.1' },
        @{ id = 'Splat.Core'; baselineVersion = '19.3.1'; candidateVersion = '19.4.1' },
        @{ id = 'Splat.Logging'; baselineVersion = '19.3.1'; candidateVersion = '19.4.1' }
    )
}

function New-OrdinalPackageMap([object[]]$Packages, [string]$Description) {
    $map = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($package in $Packages) {
        Assert-True ($package.id -is [string] -and $package.id.Length -gt 0) "$Description contains a package without an id."
        Assert-True ($package.version -is [string] -and $package.version.Length -gt 0) "$Description contains a package without a version."
        Assert-True ($package.source -is [string] -and $package.source -ceq 'nuget.org') "$Description contains a package from an unexpected source."
        Assert-True ($package.nupkgSha512 -is [string] -and $package.nupkgSha512 -cmatch '^[0-9a-f]{128}$') "$Description contains an invalid package SHA-512."
        Assert-True (-not $map.ContainsKey($package.id)) "$Description contains a duplicate package id: $($package.id)."
        $map.Add($package.id, $package)
    }
    return ,$map
}

function Assert-GraphDiffIsApproved(
    [object[]]$BaselinePackages,
    [object[]]$CandidatePackages,
    [string]$ProjectPath
) {
    $baselineById = New-OrdinalPackageMap -Packages $BaselinePackages -Description "Baseline graph $ProjectPath"
    $candidateById = New-OrdinalPackageMap -Packages $CandidatePackages -Description "Candidate graph $ProjectPath"
    Assert-True ($baselineById.Count -eq $candidateById.Count) "Candidate graph $ProjectPath changed its package cardinality."

    $transitions = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($transition in Get-ExpectedGraphTransitions) {
        Assert-True (-not $transitions.ContainsKey($transition.id)) "Approved graph transition is duplicated: $($transition.id)."
        $transitions.Add($transition.id, $transition)
    }

    foreach ($id in $baselineById.Keys) {
        Assert-True ($candidateById.ContainsKey($id)) "Candidate graph $ProjectPath removed package $id."
        $baselinePackage = $baselineById[$id]
        $candidatePackage = $candidateById[$id]
        if ($transitions.ContainsKey($id)) {
            $transition = $transitions[$id]
            Assert-True ($baselinePackage.version -ceq $transition.baselineVersion) "Baseline graph $ProjectPath has an unexpected version for $id."
            Assert-True ($candidatePackage.version -ceq $transition.candidateVersion) "Candidate graph $ProjectPath has an unapproved version for $id."
            continue
        }

        Assert-True ($candidatePackage.version -ceq $baselinePackage.version) "Candidate graph $ProjectPath changed an unapproved package version: $id."
        Assert-True ($candidatePackage.nupkgSha512 -ceq $baselinePackage.nupkgSha512) "Candidate graph $ProjectPath changed an unapproved package payload: $id."
    }

    foreach ($id in $candidateById.Keys) {
        Assert-True ($baselineById.ContainsKey($id)) "Candidate graph $ProjectPath added package $id."
    }
}

function Assert-CandidateGraphsAgainstBaseline(
    [string]$BaselinePath,
    [string]$Root,
    [string]$PackagesPath,
    [hashtable[]]$Projects
) {
    Assert-True (Test-Path -LiteralPath $BaselinePath -PathType Leaf) 'Baseline fixture is missing.'
    $fixture = Get-Content -Raw -LiteralPath $BaselinePath | ConvertFrom-Json -AsHashtable -Depth 64
    Assert-True ($fixture.schemaVersion -is [long] -and $fixture.schemaVersion -eq 1) 'Baseline fixture schemaVersion is invalid.'
    Assert-True ($fixture.sourceSha -is [string] -and $fixture.sourceSha -ceq 'e11cae9a086ddd4fd97105f00b67bedf05f92700') 'Baseline fixture source SHA is invalid.'
    $baselineProjects = @($fixture.projects)
    Assert-True ($baselineProjects.Count -eq 3) 'Baseline fixture must contain exactly three projects.'
    Assert-True ($Projects.Count -eq 3) 'Candidate graph comparison requires exactly three projects.'

    foreach ($project in $Projects) {
        $matches = @($baselineProjects | Where-Object { $_.projectPath -ceq $project.path })
        Assert-True ($matches.Count -eq 1) "Baseline fixture is missing or duplicating project $($project.path)."
        $candidate = Get-BaselineProjectPackageSet -Root $Root -ProjectPath $project.path -PackagesPath $PackagesPath -AssetsPath $project.assetsPath
        Assert-GraphDiffIsApproved -BaselinePackages @($matches[0].packageSet) -CandidatePackages @($candidate.packageSet) -ProjectPath $project.path
    }
}

function Read-ExactBytes([IO.Stream]$Stream, [int]$Count) {
    Assert-True ($Count -ge 0) 'Closed worker requested a negative byte count.'
    $buffer = [byte[]]::new($Count)
    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($buffer, $offset, $Count - $offset)
        Assert-True ($read -gt 0) 'Closed worker input ended before its declared frame length.'
        $offset += $read
    }
    return ,$buffer
}

function Get-ClosedSeedNameHash([Text.Json.JsonElement]$Seeds) {
    Assert-True ($Seeds.ValueKind -eq [Text.Json.JsonValueKind]::Array) 'Closed worker secretSeeds must be an array.'
    Assert-True ($Seeds.GetArrayLength() -le 64) 'Closed worker received too many secret seeds.'
    $names = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($seed in $Seeds.EnumerateArray()) {
        Assert-True ($seed.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'Closed worker secret seed must be an object.'
        $properties = @($seed.EnumerateObject() | ForEach-Object { $_.Name })
        Assert-True ($properties.Count -eq 2 -and $properties -ccontains 'name' -and $properties -ccontains 'value') 'Closed worker secret seed schema is invalid.'
        $name = $seed.GetProperty('name')
        $value = $seed.GetProperty('value')
        Assert-True ($name.ValueKind -eq [Text.Json.JsonValueKind]::String -and $name.GetString().Length -gt 0 -and $name.GetString().Length -le 256) 'Closed worker secret seed name is invalid.'
        Assert-True ($value.ValueKind -eq [Text.Json.JsonValueKind]::String -and $value.GetString().Length -ge 1 -and $value.GetString().Length -le 8192) 'Closed worker secret seed value is invalid.'
        Assert-True ($seen.Add($name.GetString())) 'Closed worker secret seed names must be unique.'
        $names.Add($name.GetString())
    }
    $orderedNames = @($names | Sort-Object -CaseSensitive)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($orderedNames -join [Environment]::NewLine))
    try {
        return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Test-SecretEnvironmentName([string]$Name) {
    Assert-True (-not [string]::IsNullOrEmpty($Name)) 'Secret environment name is invalid.'
    if ($Name -cin @('PATH', 'PATHEXT', 'PSModulePath', 'HOMEPATH', '__COMPAT_LAYER')) {
        return $false
    }
    $segmented = $Name -replace '([a-z0-9])([A-Z])', '$1 $2' -replace '[^A-Za-z0-9]+', ' '
    $segments = @($segmented.Split(' ', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.ToUpperInvariant() })
    $exactSegments = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in @('TOKEN', 'SECRET', 'PASSWORD', 'PASS', 'KEY', 'CREDENTIAL', 'CREDENTIALS', 'AUTH', 'PAT', 'SAS', 'COOKIE', 'CONNECTION')) {
        [void]$exactSegments.Add($value)
    }
    foreach ($segment in $segments) {
        if ($exactSegments.Contains($segment)) { return $true }
    }
    $upper = $Name.ToUpperInvariant()
    foreach ($suffix in @('TOKEN', 'SECRET', 'PASSWORD', 'PASSWD', 'APIKEY', 'PRIVATEKEY', 'CREDENTIAL', 'CREDENTIALS', 'CONNECTIONSTRING', 'CONNECTIONSTRINGS')) {
        if ($upper.EndsWith($suffix, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Get-ClosedSecretSeedSnapshot([System.Collections.IDictionary]$Environment) {
    $seeds = [System.Collections.Generic.List[object]]::new()
    foreach ($nameObject in $Environment.Keys) {
        $name = [string]$nameObject
        $value = [string]$Environment[$nameObject]
        if (-not (Test-SecretEnvironmentName -Name $name) -or [string]::IsNullOrEmpty($value)) { continue }
        $valueLength = [Text.UTF8Encoding]::new($false).GetByteCount($value)
        Assert-True ($valueLength -ge 1 -and $valueLength -le 8192) 'Secret environment value length is invalid.'
        $seeds.Add([ordered]@{ name = $name; value = $value })
    }
    $ordered = @($seeds | Sort-Object @{ Expression = { $_.name }; Ascending = $true })
    Assert-True ($ordered.Count -le 64) 'Secret environment seed count exceeds the closed limit.'
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($seed in $ordered) { Assert-True $seen.Add($seed.name) 'Secret environment snapshot contains a duplicate name.' }
    return ,$ordered
}

function Read-ClosedWorkerFrame([IO.Stream]$StandardInput) {
    $header = Read-ExactBytes -Stream $StandardInput -Count 4
    $length = 0
    $length = $length -bor (([int]$header[0]) -shl 24)
    $length = $length -bor (([int]$header[1]) -shl 16)
    $length = $length -bor (([int]$header[2]) -shl 8)
    $length = $length -bor ([int]$header[3])
    Assert-True ($length -ge 2 -and $length -le 1048576) 'Closed worker frame length is invalid.'
    $body = Read-ExactBytes -Stream $StandardInput -Count $length
    $trailingByte = $StandardInput.ReadByte()
    Assert-True ($trailingByte -eq -1) "Closed worker accepts exactly one input frame (declared length: $length; trailing byte: $trailingByte)."
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 16
    try {
        $document = [Text.Json.JsonDocument]::Parse([System.ReadOnlyMemory[byte]]::new($body), $options)
        try {
            $root = $document.RootElement
            Assert-True ($root.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'Closed worker frame root must be an object.'
            $properties = @($root.EnumerateObject() | ForEach-Object { $_.Name })
            Assert-True ($properties.Count -eq 4 -and $properties -ccontains 'schemaVersion' -and $properties -ccontains 'workerKind' -and $properties -ccontains 'payload' -and $properties -ccontains 'secretSeeds') 'Closed worker frame schema is invalid.'
            $schemaVersion = $root.GetProperty('schemaVersion')
            $workerKind = $root.GetProperty('workerKind')
            $payload = $root.GetProperty('payload')
            Assert-True ($schemaVersion.ValueKind -eq [Text.Json.JsonValueKind]::Number -and $schemaVersion.GetInt32() -eq 1) 'Closed worker schemaVersion is invalid.'
            Assert-True ($workerKind.ValueKind -eq [Text.Json.JsonValueKind]::String -and $workerKind.GetString() -cin @('SignatureVerify', 'SignatureSanitize', 'RegressionSanitize', 'PublicationFinalize')) 'Closed worker kind is invalid.'
            Assert-True ($payload.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'Closed worker payload must be an object.'
            return [pscustomobject]@{
                WorkerKind = $workerKind.GetString()
                Payload = $payload.Clone()
                SeedNameSha256 = Get-ClosedSeedNameHash -Seeds $root.GetProperty('secretSeeds')
                SecretSeeds = @($root.GetProperty('secretSeeds').EnumerateArray() | ForEach-Object { $_.Clone() })
            }
        } finally {
            $document.Dispose()
        }
    } finally {
        [Array]::Clear($header, 0, $header.Length)
        [Array]::Clear($body, 0, $body.Length)
    }
}

function Write-ClosedWorkerFrame([IO.Stream]$StandardOutput, [System.Collections.IDictionary]$Result) {
    $json = ConvertTo-Json -InputObject $Result -Depth 12 -Compress
    $body = [Text.UTF8Encoding]::new($false).GetBytes($json)
    Assert-True ($body.Length -ge 2 -and $body.Length -le 1048576) 'Closed worker result frame length is invalid.'
    $header = [byte[]]@(
        (($body.Length -shr 24) -band 0xff),
        (($body.Length -shr 16) -band 0xff),
        (($body.Length -shr 8) -band 0xff),
        ($body.Length -band 0xff)
    )
    try {
        $StandardOutput.Write($header, 0, $header.Length)
        $StandardOutput.Write($body, 0, $body.Length)
        $StandardOutput.Flush()
    } finally {
        [Array]::Clear($header, 0, $header.Length)
        [Array]::Clear($body, 0, $body.Length)
    }
}

function Assert-ExactJsonObjectProperties([Text.Json.JsonElement]$Object, [string[]]$Expected, [string]$Name) {
    Assert-True ($Object.ValueKind -eq [Text.Json.JsonValueKind]::Object) "$Name must be an object."
    $actual = @($Object.EnumerateObject() | ForEach-Object { $_.Name } | Sort-Object -CaseSensitive)
    $expectedSorted = @($Expected | Sort-Object -CaseSensitive)
    Assert-True ($actual.Count -eq $expectedSorted.Count) "$Name has an unexpected property count."
    for ($index = 0; $index -lt $expectedSorted.Count; $index++) {
        Assert-True ($actual[$index] -ceq $expectedSorted[$index]) "$Name has an unexpected property."
    }
}

function Get-RequiredWorkerPayloadString([Text.Json.JsonElement]$Payload, [string]$Name) {
    $value = $Payload.GetProperty($Name)
    Assert-True ($value.ValueKind -eq [Text.Json.JsonValueKind]::String -and -not [string]::IsNullOrWhiteSpace($value.GetString())) "Closed worker payload $Name is invalid."
    return $value.GetString()
}

function Invoke-SignatureVerifyWorker([Text.Json.JsonElement]$Payload) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('dotnetExecutable', 'repositoryRoot', 'packagesRoot', 'baselineGraphPath', 'assetsPaths') -Name 'SignatureVerify payload'
    $dotnetExecutable = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'dotnetExecutable'
    $repositoryRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'repositoryRoot'))
    $packagesRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'packagesRoot'))
    $baselineGraphPath = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'baselineGraphPath'))
    Assert-True ([IO.Path]::IsPathFullyQualified($dotnetExecutable) -and (Test-Path -LiteralPath $dotnetExecutable -PathType Leaf)) 'SignatureVerify dotnetExecutable must be an absolute existing file.'
    Assert-True (Test-Path -LiteralPath $packagesRoot -PathType Container) 'SignatureVerify packagesRoot is missing.'
    Assert-True ($baselineGraphPath -ceq [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'distribution/fixtures/reactiveui-signature-chain-baseline.json'))) 'SignatureVerify baseline path is invalid.'

    $assetsPaths = $Payload.GetProperty('assetsPaths')
    Assert-True ($assetsPaths.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $assetsPaths.GetArrayLength() -eq 3) 'SignatureVerify requires exactly three assets paths.'
    $assetValues = @($assetsPaths.EnumerateArray() | ForEach-Object {
            Assert-True ($_.ValueKind -eq [Text.Json.JsonValueKind]::String -and [IO.Path]::IsPathFullyQualified($_.GetString())) 'SignatureVerify assets path is invalid.'
            [IO.Path]::GetFullPath($_.GetString())
        })
    Assert-True ((@($assetValues | Sort-Object -Unique)).Count -eq 3) 'SignatureVerify assets paths must be unique.'
    foreach ($assetPath in $assetValues) {
        Assert-True (Test-Path -LiteralPath $assetPath -PathType Leaf) 'SignatureVerify assets path is missing.'
        Assert-True (-not (Get-Item -LiteralPath $assetPath -Force).LinkType) 'SignatureVerify assets path cannot be a link.'
    }

    $projects = @(
        @{ path = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; assetsPath = $assetValues[0] },
        @{ path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'; assetsPath = $assetValues[1] },
        @{ path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj'; assetsPath = $assetValues[2] }
    )
    Assert-CandidateGraphsAgainstBaseline -BaselinePath $baselineGraphPath -Root $repositoryRoot -PackagesPath $packagesRoot -Projects $projects

    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($expectedPackage in Get-ExpectedPackages) {
        $id = [string]$expectedPackage.Id
        $version = [string]$expectedPackage.Version
        $nupkg = Join-Path $packagesRoot ("{0}\{1}\{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $version)
        Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "SignatureVerify package is absent: $id $version."
        & $dotnetExecutable nuget verify $nupkg --all --certificate-fingerprint (Get-ExpectedAuthorFingerprint) *> $null
        Assert-True ($LASTEXITCODE -eq 0) "SignatureVerify failed for $id $version."
        $packages.Add([ordered]@{
                id = $id
                version = $version
                nupkgSha512 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash.ToLowerInvariant()
                authorCertificateSha256 = Get-ExpectedAuthorFingerprint
            })
    }
    return [ordered]@{ success = $true; failureCode = $null; packages = $packages.ToArray() }
}

function Assert-RelativeEvidencePath([string]$Path) {
    Assert-True ($Path -cmatch '^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?:/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)*$') 'Sanitized evidence path is invalid.'
    Assert-True (-not $Path.Contains('..') -and -not $Path.Contains('\') -and -not [IO.Path]::IsPathFullyQualified($Path)) 'Sanitized evidence path is unsafe.'
}

function Assert-SanitizedBytes([byte[]]$Bytes, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-True ($Bytes.Length -le 4194304) 'Sanitized candidate file exceeds the closed size limit.'
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    Assert-True ($text.IndexOf([char]0) -lt 0) 'Sanitized candidate file contains a NUL byte.'
    Assert-True (-not $text.Contains($script:WorkerScriptPath, [StringComparison]::OrdinalIgnoreCase)) 'Sanitized candidate file contains an absolute worker path.'
    foreach ($seed in $SecretSeeds) {
        $value = $seed.GetProperty('value').GetString()
        Assert-True (-not $text.Contains($value, [StringComparison]::Ordinal)) 'Sanitized candidate file contains a secret value.'
    }
}

function Write-SanitizedCandidateFile([string]$Root, [string]$RelativePath, [byte[]]$Bytes, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-RelativeEvidencePath -Path $RelativePath
    Assert-SanitizedBytes -Bytes $Bytes -SecretSeeds $SecretSeeds
    $destination = [IO.Path]::GetFullPath((Join-Path $Root ($RelativePath -replace '/', [IO.Path]::DirectorySeparatorChar)))
    $relative = [IO.Path]::GetRelativePath($Root, $destination) -replace '\\', '/'
    Assert-True ($relative -ceq $RelativePath) 'Sanitized candidate destination escaped its root.'
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force -ErrorAction Stop | Out-Null
    [IO.File]::WriteAllBytes($destination, $Bytes)
    return [ordered]@{ path = $RelativePath; sha256 = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes))).ToLowerInvariant(); byteLength = [long]$Bytes.Length }
}

function Invoke-SignatureSanitizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'sourceSha', 'runAttempt', 'projects', 'packages', 'phaseResults') -Name 'SignatureSanitize payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    Assert-True (-not (Test-Path -LiteralPath $candidateRoot)) 'SignatureSanitize candidate root must be absent.'
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$') 'SignatureSanitize identity is invalid.'
    $projects = $Payload.GetProperty('projects')
    $packages = $Payload.GetProperty('packages')
    $phases = $Payload.GetProperty('phaseResults')
    Assert-True ($projects.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $projects.GetArrayLength() -eq 3) 'SignatureSanitize projects are invalid.'
    Assert-True ($packages.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $packages.GetArrayLength() -eq 6) 'SignatureSanitize packages are invalid.'
    Assert-True ($phases.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $phases.GetArrayLength() -ge 1) 'SignatureSanitize phase results are invalid.'
    New-Item -ItemType Directory -Path $candidateRoot -ErrorAction Stop | Out-Null
    $logs = [System.Collections.Generic.List[object]]::new()
    $normalizedPackages = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($package in $packages.EnumerateArray()) {
            Assert-ExactJsonObjectProperties -Object $package -Expected @('id', 'version', 'nupkgSha512', 'authorCertificateSha256') -Name 'SignatureSanitize package'
            $id = $package.GetProperty('id').GetString()
            $version = $package.GetProperty('version').GetString()
            $hash = $package.GetProperty('nupkgSha512').GetString()
            Assert-True ($id -cmatch '^[A-Za-z0-9.]+$' -and $version -cmatch '^[0-9A-Za-z.-]+$' -and $hash -cmatch '^[0-9a-f]{128}$') 'SignatureSanitize package fields are invalid.'
            $logPath = "signature/verify/$id.log"
            $logBytes = [Text.UTF8Encoding]::new($false).GetBytes("package=$id`nversion=$version`nverifyExitCode=0`n")
            $logReference = Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath $logPath -Bytes $logBytes -SecretSeeds $SecretSeeds
            $logs.Add([ordered]@{ phase = "signature:verify:$id"; path = $logReference.path; sha256 = $logReference.sha256; byteLength = $logReference.byteLength })
            $normalizedPackages.Add([ordered]@{ id = $id; version = $version; nupkgSha512 = $hash; verifyExitCode = 0; verifyLog = $logReference.path })
        }
        $normalizedProjects = @($projects.EnumerateArray() | ForEach-Object { $_.Clone() })
        $evidence = [ordered]@{
            schemaVersion = 1
            evidenceKind = 'signature-success'
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = 'Signature'
            runtime = [ordered]@{ os = [Environment]::OSVersion.Platform.ToString(); architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString(); dotnetSdkVersion = (& (Get-AbsoluteDotNetExecutable) --version).Trim(); executionContext = 'local'; signatureVerification = $true; revocationMode = $null; signatureAuthoritative = $true }
            projects = $normalizedProjects
            packages = $normalizedPackages.ToArray()
            expectedAuthorFingerprint = Get-ExpectedAuthorFingerprint
            sanitizedLogs = $logs.ToArray()
        }
        Assert-True ($LASTEXITCODE -eq 0) 'SignatureSanitize could not determine the .NET SDK version.'
        $evidenceBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $evidence -Depth 16 -Compress))
        [void](Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath 'signature/evidence.json' -Bytes $evidenceBytes -SecretSeeds $SecretSeeds)
        return [ordered]@{ success = $true; failureCode = $null; packages = @() }
    } catch {
        if (Test-Path -LiteralPath $candidateRoot) { Remove-Item -LiteralPath $candidateRoot -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Get-CandidateEvidenceManifest([string]$CandidateRoot, [Text.Json.JsonElement[]]$SecretSeeds) {
    $root = [IO.Path]::GetFullPath($CandidateRoot)
    Assert-True ((Test-Path -LiteralPath $root -PathType Container) -and -not (Get-Item -LiteralPath $root -Force).LinkType) 'Candidate evidence root is invalid.'
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
        Assert-True (-not $file.LinkType) 'Candidate evidence file cannot be a link.'
        $relativePath = [IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
        Assert-RelativeEvidencePath -Path $relativePath
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        try {
            Assert-SanitizedBytes -Bytes $bytes -SecretSeeds $SecretSeeds
            $entries.Add([ordered]@{ path = $relativePath; sha256 = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant(); byteLength = [long]$bytes.Length })
        } finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
    $ordered = @($entries | Sort-Object @{ Expression = { $_.path }; Ascending = $true })
    Assert-True ($ordered.Count -gt 0) 'Candidate evidence manifest cannot be empty.'
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seenInsensitive = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $ordered) {
        Assert-True ($seen.Add($entry.path) -and $seenInsensitive.Add($entry.path)) 'Candidate evidence manifest contains a duplicate path.'
    }
    return ,$ordered
}

function Invoke-PublicationFinalizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'finalEvidenceRoot', 'sourceSha', 'runAttempt', 'lane', 'phaseResults') -Name 'PublicationFinalize payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    $finalRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'finalEvidenceRoot'))
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    $lane = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'lane'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$' -and $lane -ceq 'Signature') 'PublicationFinalize identity is invalid.'
    Assert-True (-not (Test-Path -LiteralPath $finalRoot) -and (Split-Path -Parent $candidateRoot) -ceq (Split-Path -Parent $finalRoot)) 'PublicationFinalize final root is invalid.'
    $manifest = Get-CandidateEvidenceManifest -CandidateRoot $candidateRoot -SecretSeeds $SecretSeeds
    $scratchRoot = $finalRoot + '.scratch-' + [Guid]::NewGuid().ToString('N')
    try {
        Copy-Item -LiteralPath $candidateRoot -Destination $scratchRoot -Recurse -ErrorAction Stop
        $receipt = [ordered]@{
            schemaVersion = 1
            receiptKind = 'primary'
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = $lane
            outcome = 'success'
            failurePhase = $null
            failureCode = $null
            phases = @($Payload.GetProperty('phaseResults').EnumerateArray() | ForEach-Object { $_.Clone() })
            evidenceManifest = $manifest
        }
        $receiptBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress))
        try {
            Assert-SanitizedBytes -Bytes $receiptBytes -SecretSeeds $SecretSeeds
            [IO.File]::WriteAllBytes((Join-Path $scratchRoot 'attempt-receipt.json'), $receiptBytes)
        } finally {
            [Array]::Clear($receiptBytes, 0, $receiptBytes.Length)
        }
        $finalManifest = Get-CandidateEvidenceManifest -CandidateRoot $scratchRoot -SecretSeeds $SecretSeeds
        Assert-True ($finalManifest.Count -eq $manifest.Count + 1) 'PublicationFinalize final tree has an unexpected file count.'
        Move-Item -LiteralPath $scratchRoot -Destination $finalRoot -ErrorAction Stop
        return [ordered]@{ success = $true; failureCode = $null; packages = @() }
    } catch {
        if (Test-Path -LiteralPath $scratchRoot) { Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Invoke-ClosedWorkerCliMode([IO.Stream]$StandardInput, [IO.Stream]$StandardOutput) {
    $request = Read-ClosedWorkerFrame -StandardInput $StandardInput
    try {
        $workerResult = switch -CaseSensitive ($request.WorkerKind) {
            'SignatureVerify' { Invoke-SignatureVerifyWorker -Payload $request.Payload; break }
            'SignatureSanitize' { Invoke-SignatureSanitizeWorker -Payload $request.Payload -SecretSeeds $request.SecretSeeds; break }
            'PublicationFinalize' { Invoke-PublicationFinalizeWorker -Payload $request.Payload -SecretSeeds $request.SecretSeeds; break }
            default { [ordered]@{ success = $false; failureCode = 'worker-kind-not-implemented'; packages = @() } }
        }
    } catch {
        $workerResult = [ordered]@{ success = $false; failureCode = 'worker-failed'; packages = @() }
    }
    $result = [ordered]@{
        schemaVersion = 1
        workerKind = $request.WorkerKind
        success = [bool]$workerResult.success
        failureCode = $workerResult.failureCode
        seedNameSha256 = $request.SeedNameSha256
        packages = @($workerResult.packages)
    }
    Write-ClosedWorkerFrame -StandardOutput $StandardOutput -Result $result
    return [bool]$workerResult.success
}

function Get-AbsolutePowerShellExecutable {
    $windowsAppsRoot = if ([string]::IsNullOrEmpty($env:LOCALAPPDATA)) { $null } else { Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps' }
    $candidates = @(
        Get-Command pwsh -All -ErrorAction Stop |
            ForEach-Object { $_.Source } |
            Where-Object {
                $_ -is [string] -and
                [IO.Path]::IsPathFullyQualified($_) -and
                ($null -eq $windowsAppsRoot -or -not $_.StartsWith($windowsAppsRoot, [StringComparison]::OrdinalIgnoreCase)) -and
                (Test-Path -LiteralPath $_ -PathType Leaf)
            } |
            Sort-Object -Unique
    )
    Assert-True ($candidates.Count -eq 1) 'Closed worker requires exactly one absolute pwsh executable.'
    return [IO.Path]::GetFullPath($candidates[0])
}

function Get-AbsoluteDotNetExecutable {
    $candidates = @(
        Get-Command dotnet -All -ErrorAction Stop |
            ForEach-Object { $_.Source } |
            Where-Object { $_ -is [string] -and [IO.Path]::IsPathFullyQualified($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
            Sort-Object -Unique
    )
    Assert-True ($candidates.Count -eq 1) 'Closed worker requires exactly one absolute dotnet executable.'
    return [IO.Path]::GetFullPath($candidates[0])
}

function New-ClosedWorkerInput(
    [string]$WorkerKind,
    [System.Collections.IDictionary]$Payload,
    [object[]]$SecretSeeds = @()
) {
    Assert-True ($WorkerKind -cin @('SignatureVerify', 'SignatureSanitize', 'RegressionSanitize', 'PublicationFinalize')) 'Closed worker kind is invalid.'
    $stream = [IO.MemoryStream]::new()
    try {
        Write-ClosedWorkerFrame -StandardOutput $stream -Result ([ordered]@{
                schemaVersion = 1
                workerKind = $WorkerKind
                payload = $Payload
                secretSeeds = @($SecretSeeds)
            })
        return ,$stream.ToArray()
    } finally {
        $stream.Dispose()
    }
}

function Read-ClosedWorkerResult([byte[]]$Bytes, [string]$ExpectedWorkerKind) {
    Assert-True ($Bytes.Length -ge 6 -and $Bytes.Length -le 1048580) 'Closed worker output frame is invalid.'
    $length = 0
    $length = $length -bor (([int]$Bytes[0]) -shl 24)
    $length = $length -bor (([int]$Bytes[1]) -shl 16)
    $length = $length -bor (([int]$Bytes[2]) -shl 8)
    $length = $length -bor ([int]$Bytes[3])
    Assert-True ($length -ge 2 -and $length -le 1048576 -and $Bytes.Length -eq $length + 4) 'Closed worker output frame length is invalid.'
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 16
    $document = [Text.Json.JsonDocument]::Parse([System.ReadOnlyMemory[byte]]::new($Bytes, 4, $length), $options)
    try {
        $root = $document.RootElement
        Assert-ExactJsonObjectProperties -Object $root -Expected @('schemaVersion', 'workerKind', 'success', 'failureCode', 'seedNameSha256', 'packages') -Name 'Closed worker result'
        Assert-True ($root.GetProperty('schemaVersion').ValueKind -eq [Text.Json.JsonValueKind]::Number -and $root.GetProperty('schemaVersion').GetInt32() -eq 1) 'Closed worker result schemaVersion is invalid.'
        Assert-True ($root.GetProperty('workerKind').ValueKind -eq [Text.Json.JsonValueKind]::String -and $root.GetProperty('workerKind').GetString() -ceq $ExpectedWorkerKind) 'Closed worker result kind is invalid.'
        Assert-True ($root.GetProperty('success').ValueKind -in @([Text.Json.JsonValueKind]::True, [Text.Json.JsonValueKind]::False)) 'Closed worker result success is invalid.'
        $failureCode = $root.GetProperty('failureCode')
        Assert-True ($failureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null -or ($failureCode.ValueKind -eq [Text.Json.JsonValueKind]::String -and $failureCode.GetString() -cin @('worker-failed', 'worker-kind-not-implemented'))) 'Closed worker result failureCode is invalid.'
        $seedHash = $root.GetProperty('seedNameSha256')
        Assert-True ($seedHash.ValueKind -eq [Text.Json.JsonValueKind]::String -and $seedHash.GetString() -cmatch '^[0-9a-f]{64}$') 'Closed worker result seedNameSha256 is invalid.'
        $packages = $root.GetProperty('packages')
        Assert-True ($packages.ValueKind -eq [Text.Json.JsonValueKind]::Array) 'Closed worker result packages is invalid.'
        $success = $root.GetProperty('success').GetBoolean()
        Assert-True (($success -and $failureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) -or ((-not $success) -and $failureCode.ValueKind -eq [Text.Json.JsonValueKind]::String)) 'Closed worker result success tuple is invalid.'
        return [pscustomobject]@{
            Success = $success
            FailureCode = if ($failureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $failureCode.GetString() }
            SeedNameSha256 = $seedHash.GetString()
            Packages = @($packages.EnumerateArray() | ForEach-Object { $_.Clone() })
        }
    } finally {
        $document.Dispose()
    }
}

function Stop-ClosedWorkerProcess([Diagnostics.Process]$Process) {
    if ($Process.HasExited) { return $true }
    try {
        $Process.Kill($true)
    } catch {
        return $false
    }
    return $Process.WaitForExit(10000)
}

function Invoke-ClosedWorkerProcessAdapter(
    [string]$WorkerKind,
    [System.Collections.IDictionary]$Payload,
    [object[]]$SecretSeeds = @(),
    [int]$TimeoutSeconds = 120
) {
    Assert-True ($TimeoutSeconds -ge 1 -and $TimeoutSeconds -le 1200) 'Closed worker timeout is invalid.'
    $inputBytes = $null
    $process = $null
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    try {
        $inputBytes = New-ClosedWorkerInput -WorkerKind $WorkerKind -Payload $Payload -SecretSeeds $SecretSeeds
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = Get-AbsolutePowerShellExecutable
        $psi.WorkingDirectory = [IO.Path]::GetFullPath($RepositoryRoot)
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
        foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $script:WorkerScriptPath, '-Mode', 'Worker')) {
            [void]$psi.ArgumentList.Add($argument)
        }
        foreach ($name in @('GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE', 'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL', 'ACTIONS_ID_TOKEN_REQUEST_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_URL')) {
            [void]$psi.Environment.Remove($name)
        }
        foreach ($seed in $SecretSeeds) {
            Assert-True ($seed -is [System.Collections.IDictionary] -and $seed['name'] -is [string]) 'Closed worker seed is invalid.'
            [void]$psi.Environment.Remove($seed['name'])
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $psi
        Assert-True $process.Start() 'Closed worker process did not start.'
        $input = $process.StandardInput.BaseStream
        $input.Write($inputBytes, 0, $inputBytes.Length)
        $input.Flush()
        $input.Dispose()

        $stdoutBuffer = [byte[]]::new(8192)
        $stderrBuffer = [byte[]]::new(8192)
        $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
        $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $timedOut = $false
        $overflowed = $false
        while ($null -ne $stdoutTask -or $null -ne $stderrTask) {
            $tasks = [System.Collections.Generic.List[Threading.Tasks.Task]]::new()
            if ($null -ne $stdoutTask) { $tasks.Add($stdoutTask) }
            if ($null -ne $stderrTask) { $tasks.Add($stderrTask) }
            [void][Threading.Tasks.Task]::WaitAny($tasks.ToArray(), 100)
            foreach ($streamState in @(@{ task = $stdoutTask; buffer = $stdoutBuffer; target = $stdout; maximum = 1048576; kind = 'stdout' }, @{ task = $stderrTask; buffer = $stderrBuffer; target = $stderr; maximum = 16384; kind = 'stderr' })) {
                if ($null -eq $streamState.task -or -not $streamState.task.IsCompleted) { continue }
                $read = $streamState.task.GetAwaiter().GetResult()
                if ($streamState.kind -ceq 'stdout') { $stdoutTask = $null } else { $stderrTask = $null }
                if ($read -le 0) { continue }
                if ($streamState.target.Length + $read -gt $streamState.maximum) {
                    $overflowed = $true
                    continue
                }
                $streamState.target.Write($streamState.buffer, 0, $read)
                if ($streamState.kind -ceq 'stdout') {
                    $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
                } else {
                    $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
                }
            }
            if (($overflowed -or [DateTime]::UtcNow -ge $deadline) -and -not $process.HasExited) {
                $timedOut = -not $overflowed
                $terminationProven = Stop-ClosedWorkerProcess -Process $process
                if (-not $terminationProven) {
                    return [pscustomobject]@{ Success = $false; FailureCode = 'worker-termination-unproven'; ExitCode = -1; TerminationProven = $false; Packages = @(); SeedNameSha256 = $null }
                }
            }
        }
        if (-not $process.HasExited -and -not $process.WaitForExit(10000)) {
            $terminationProven = Stop-ClosedWorkerProcess -Process $process
            return [pscustomobject]@{ Success = $false; FailureCode = 'worker-termination-unproven'; ExitCode = -1; TerminationProven = $terminationProven; Packages = @(); SeedNameSha256 = $null }
        }
        if ($overflowed) { return [pscustomobject]@{ Success = $false; FailureCode = 'native-output-limit-exceeded'; ExitCode = -3; TerminationProven = $true; Packages = @(); SeedNameSha256 = $null } }
        if ($timedOut) { return [pscustomobject]@{ Success = $false; FailureCode = 'native-command-timeout'; ExitCode = -1; TerminationProven = $true; Packages = @(); SeedNameSha256 = $null } }
        $nativeExitCode = [int]$process.ExitCode
        if ($stderr.Length -ne 0) { return [pscustomobject]@{ Success = $false; FailureCode = 'worker-failed'; ExitCode = $nativeExitCode; TerminationProven = $true; Packages = @(); SeedNameSha256 = $null } }
        $workerResult = Read-ClosedWorkerResult -Bytes $stdout.ToArray() -ExpectedWorkerKind $WorkerKind
        if ($workerResult.Success) {
            Assert-True ($nativeExitCode -eq 0) 'Closed worker returned success with a non-zero exit code.'
            return [pscustomobject]@{ Success = $true; FailureCode = $null; ExitCode = 0; TerminationProven = $true; Packages = $workerResult.Packages; SeedNameSha256 = $workerResult.SeedNameSha256 }
        }
        Assert-True ($nativeExitCode -ne 0) 'Closed worker returned failure with a zero exit code.'
        return [pscustomobject]@{ Success = $false; FailureCode = $workerResult.FailureCode; ExitCode = $nativeExitCode; TerminationProven = $true; Packages = @(); SeedNameSha256 = $workerResult.SeedNameSha256 }
    } catch {
        $terminationProven = $true
        if ($null -ne $process) { $terminationProven = Stop-ClosedWorkerProcess -Process $process }
        return [pscustomobject]@{ Success = $false; FailureCode = 'native-command-threw'; ExitCode = -2; TerminationProven = $terminationProven; Packages = @(); SeedNameSha256 = $null }
    } finally {
        if ($null -ne $inputBytes) { [Array]::Clear($inputBytes, 0, $inputBytes.Length) }
        $stdout.Dispose()
        $stderr.Dispose()
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Invoke-GenerateBaseline {
    $root = Get-CanonicalRepositoryRoot
    Assert-True ($ExpectedParentSha -cmatch '^[0-9a-f]{40}$') 'ExpectedParentSha must be a lowercase 40-hex Git SHA.'
    $head = (& git -C $root rev-parse HEAD).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $head -ceq $ExpectedParentSha) 'GenerateBaseline requires exact parent HEAD.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($PackagesRoot)) 'GenerateBaseline requires PackagesRoot.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($OutputPath)) 'GenerateBaseline requires OutputPath.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($BaselineAssetsRoot)) 'GenerateBaseline requires BaselineAssetsRoot.'
    $packagesPath = [IO.Path]::GetFullPath($PackagesRoot)
    Assert-True (Test-Path -LiteralPath $packagesPath -PathType Container) 'GenerateBaseline PackagesRoot must exist.'
    $output = [IO.Path]::GetFullPath($OutputPath)
    $assetsRoot = [IO.Path]::GetFullPath($BaselineAssetsRoot)
    Assert-True (-not (Test-Path -LiteralPath $output)) 'GenerateBaseline OutputPath must not exist.'

    $projectsToStage = @(
        @{ path = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; assets = 'headless.project.assets.json' },
        @{ path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'; assets = 'desktop.project.assets.json' },
        @{ path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj'; assets = 'debian.project.assets.json' }
    )
    $projectPaths = @($projectsToStage | ForEach-Object { $_.path })
    $manifestPaths = @('src/Directory.Packages.props', 'src/nuget.config')
    $manifestPaths += $projectPaths
    $inputManifest = [System.Collections.Generic.List[object]]::new()
    foreach ($path in @($manifestPaths | Sort-Object -Unique)) {
        $absolute = Join-Path $root $path
        Assert-True (Test-Path -LiteralPath $absolute -PathType Leaf) "Baseline input is missing: $path."
        $stage = (& git -C $root ls-files -s -- $path).Trim()
        Assert-True ($LASTEXITCODE -eq 0 -and $stage -match '^(100644|100755) ([0-9a-f]{40}) 0\t') "Baseline input is not an exact regular Git blob: $path."
        $inputManifest.Add([ordered]@{ path = $path; mode = $Matches[1]; gitObjectId = $Matches[2]; byteLength = [long](Get-Item -LiteralPath $absolute).Length; sha256 = Get-Sha256 -Path $absolute })
    }
    $projects = [System.Collections.Generic.List[object]]::new()
    foreach ($project in $projectsToStage) {
        $projects.Add((Get-BaselineProjectPackageSet -Root $root -ProjectPath $project.path -PackagesPath $packagesPath -AssetsPath (Join-Path $assetsRoot $project.assets)))
    }
    $sdk = (& $DotNetExecutable --version).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $sdk -cmatch '^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') 'GenerateBaseline requires a .NET 10 SDK.'
    $fixtureProjects = $projects.ToArray()
    Assert-True ($fixtureProjects.Count -eq $projectsToStage.Count) 'Baseline fixture must contain all staged projects.'
    $fixture = [ordered]@{ schemaVersion = 1; sourceSha = $head; gitObjectFormat = 'sha1'; dotnetSdkVersion = $sdk; inputManifest = $inputManifest.ToArray(); projects = $fixtureProjects }
    $json = $fixture | ConvertTo-Json -Depth 16
    $roundTrip = $json | ConvertFrom-Json -AsHashtable -Depth 32
    $roundTripProjects = @($roundTrip.projects)
    Assert-True ($roundTripProjects.Count -eq $projectsToStage.Count) 'Baseline fixture serialization lost a staged project.'
    for ($index = 0; $index -lt $projectsToStage.Count; $index++) {
        Assert-True ($roundTripProjects[$index].projectPath -ceq $projectsToStage[$index].path) 'Baseline fixture project order or path changed during serialization.'
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
    [IO.File]::WriteAllText($output, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "NuGet baseline fixture: $output"
}

function Invoke-DotNet([string]$Phase, [string[]]$Arguments, [System.Collections.Generic.List[object]]$Phases) {
    & dotnet @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        $Phases.Add([ordered]@{ name = $Phase; status = 'success'; exitCode = 0 })
        return
    }

    $Phases.Add([ordered]@{ name = $Phase; status = 'failure'; exitCode = $exitCode })
    throw "dotnet failed in $Phase with exit code $exitCode."
}

function Get-PackageReceipt([string]$PackagesPath, [System.Collections.Generic.List[object]]$Phases) {
    $receipt = [System.Collections.Generic.List[object]]::new()
    foreach ($package in Get-ExpectedPackages) {
        $id = [string]$package.Id
        $version = [string]$package.Version
        $nupkg = Join-Path $PackagesPath ("{0}\{1}\{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $version)
        Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "Expected package is absent: $id $version."

        & dotnet nuget verify $nupkg --all --certificate-fingerprint (Get-ExpectedAuthorFingerprint) | Out-Host
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $Phases.Add([ordered]@{ name = "signature:verify:$id"; status = 'failure'; exitCode = $exitCode })
            throw "Signature verification failed for $id $version."
        }

        $Phases.Add([ordered]@{ name = "signature:verify:$id"; status = 'success'; exitCode = 0 })
        $receipt.Add([ordered]@{
                id = $id
                version = $version
                nupkgSha512 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash.ToLowerInvariant()
                authorCertificateSha256 = Get-ExpectedAuthorFingerprint
            })
    }

    return ,$receipt
}

function Write-AttemptReceipt(
    [string]$Root,
    [string]$AttemptLane,
    [string]$SourceSha,
    [int]$Attempt,
    [string]$Outcome,
    [System.Collections.Generic.List[object]]$Phases,
    [System.Collections.Generic.List[object]]$Packages,
    [string]$FailureCode
) {
    New-Item -ItemType Directory -Path $Root -ErrorAction Stop | Out-Null
    $receipt = [ordered]@{
        schemaVersion = 1
        lane = $AttemptLane
        sourceSha = $SourceSha
        runAttempt = $Attempt
        outcome = $Outcome
        phases = @($Phases)
        packages = @($Packages)
        failureCode = if ($Outcome -ceq 'success') { $null } else { $FailureCode }
    }

    $json = $receipt | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $Root 'attempt.json'), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Invoke-SignatureAttempt {
    Assert-True ($Lane -ceq 'Signature') 'RunAttempt currently accepts only the Signature lane.'
    Assert-True ($env:DOTNET_NUGET_SIGNATURE_VERIFICATION -ceq 'true') 'DOTNET_NUGET_SIGNATURE_VERIFICATION must be exactly true.'
    Assert-True ([string]::IsNullOrEmpty($env:NUGET_CERT_REVOCATION_MODE) -or $env:NUGET_CERT_REVOCATION_MODE -ceq 'online') 'NUGET_CERT_REVOCATION_MODE must be absent or exactly online.'

    $root = Get-CanonicalRepositoryRoot
    $sourceSha = Resolve-SourceSha -Root $root
    $attempt = Resolve-RunAttempt
    $packagesPath = New-IsolatedPackagesRoot
    $evidencePath = Get-EvidenceRoot -Root $root -SourceSha $sourceSha -Attempt $attempt
    $phases = [System.Collections.Generic.List[object]]::new()
    $packages = [System.Collections.Generic.List[object]]::new()
    $stagedAssetsRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-nuget-assets-' + [Guid]::NewGuid().ToString('N'))
    $secretSeeds = Get-ClosedSecretSeedSnapshot -Environment ([Environment]::GetEnvironmentVariables('Process'))
    $env:NUGET_PACKAGES = $packagesPath

    try {
        $projects = @(
            @{ id = 'headless'; path = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; assetsPath = (Join-Path $stagedAssetsRoot 'headless.project.assets.json') },
            @{ id = 'desktop'; path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'; assetsPath = (Join-Path $stagedAssetsRoot 'desktop.project.assets.json') },
            @{ id = 'debian'; path = 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj'; assetsPath = (Join-Path $stagedAssetsRoot 'debian.project.assets.json') }
        )
        New-Item -ItemType Directory -Path $stagedAssetsRoot -ErrorAction Stop | Out-Null

        foreach ($project in $projects) {
            Invoke-DotNet -Phase "signature:restore:$($project.id)" -Arguments @(
                'restore', (Join-Path $root $project.path), '--force', '--no-http-cache',
                '--configfile', (Join-Path $root 'src\nuget.config'),
                '-p:Configuration=Debug',
                '-p:DisableImplicitLibraryPacksFolder=true',
                '-p:DisableImplicitNuGetFallbackFolder=true',
                '-p:RestoreFallbackFolders='
            ) -Phases $phases
            try {
                $projectDirectory = Split-Path -Parent (Join-Path $root $project.path)
                Copy-Item -LiteralPath (Join-Path $projectDirectory 'obj\project.assets.json') -Destination $project.assetsPath -ErrorAction Stop
                $phases.Add([ordered]@{ name = "signature:assets:$($project.id)"; status = 'success'; exitCode = 0 })
            } catch {
                $phases.Add([ordered]@{ name = "signature:assets:$($project.id)"; status = 'failure'; exitCode = 1 })
                throw
            }
        }

        $workerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureVerify' -Payload ([ordered]@{
                dotnetExecutable = Get-AbsoluteDotNetExecutable
                repositoryRoot = $root
                packagesRoot = $packagesPath
                baselineGraphPath = Join-Path $root 'distribution\fixtures\reactiveui-signature-chain-baseline.json'
                assetsPaths = @($projects | ForEach-Object { $_.assetsPath })
            }) -SecretSeeds $secretSeeds -TimeoutSeconds 1200
        Assert-True ($workerResult.TerminationProven -eq $true) 'Closed signature worker termination was not proven.'
        if (-not $workerResult.Success) {
            $phases.Add([ordered]@{ name = 'signature:verify:worker'; status = 'failure'; exitCode = [int]$workerResult.ExitCode })
            throw "Closed signature worker failed: $($workerResult.FailureCode)."
        }
        Assert-True ($workerResult.ExitCode -eq 0 -and $workerResult.Packages.Count -eq 6) 'Closed signature worker result is invalid.'
        $phases.Add([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0 })
        $packages = [System.Collections.Generic.List[object]]::new()
        $expectedPackages = Get-ExpectedPackages
        for ($packageIndex = 0; $packageIndex -lt $expectedPackages.Count; $packageIndex++) {
            $workerPackage = $workerResult.Packages[$packageIndex]
            Assert-ExactJsonObjectProperties -Object $workerPackage -Expected @('id', 'version', 'nupkgSha512', 'authorCertificateSha256') -Name 'Closed signature worker package'
            $expectedPackage = $expectedPackages[$packageIndex]
            Assert-True ($workerPackage.GetProperty('id').GetString() -ceq $expectedPackage.Id -and $workerPackage.GetProperty('version').GetString() -ceq $expectedPackage.Version) 'Closed signature worker package identity is invalid.'
            Assert-True ($workerPackage.GetProperty('nupkgSha512').GetString() -cmatch '^[0-9a-f]{128}$') 'Closed signature worker package hash is invalid.'
            Assert-True ($workerPackage.GetProperty('authorCertificateSha256').GetString() -ceq (Get-ExpectedAuthorFingerprint)) 'Closed signature worker author fingerprint is invalid.'
            $packages.Add([ordered]@{
                    id = $workerPackage.GetProperty('id').GetString()
                    version = $workerPackage.GetProperty('version').GetString()
                    nupkgSha512 = $workerPackage.GetProperty('nupkgSha512').GetString()
                    authorCertificateSha256 = $workerPackage.GetProperty('authorCertificateSha256').GetString()
                })
            $phases.Add([ordered]@{ name = "signature:verify:$($expectedPackage.Id)"; status = 'success'; exitCode = 0 })
        }
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Signature' -SourceSha $sourceSha -Attempt $attempt -Outcome 'success' -Phases $phases -Packages $packages -FailureCode $null
        Write-Output "NuGet signature evidence: $evidencePath"
    } catch {
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Signature' -SourceSha $sourceSha -Attempt $attempt -Outcome 'failure' -Phases $phases -Packages $packages -FailureCode 'attempt-failed'
        throw
    } finally {
        if (Test-Path -LiteralPath $stagedAssetsRoot) {
            Remove-Item -LiteralPath $stagedAssetsRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-RegressionAttempt {
    Assert-True ($Lane -ceq 'Regression') 'RunAttempt currently accepts only the Signature or Regression lane.'
    Assert-True ($env:DOTNET_NUGET_SIGNATURE_VERIFICATION -ceq 'true') 'DOTNET_NUGET_SIGNATURE_VERIFICATION must be exactly true.'
    Assert-True ([string]::IsNullOrEmpty($env:NUGET_CERT_REVOCATION_MODE) -or $env:NUGET_CERT_REVOCATION_MODE -ceq 'online') 'NUGET_CERT_REVOCATION_MODE must be absent or exactly online.'

    $root = Get-CanonicalRepositoryRoot
    $sourceSha = Resolve-SourceSha -Root $root
    $attempt = Resolve-RunAttempt
    $packagesPath = New-IsolatedPackagesRoot
    $evidencePath = Get-EvidenceRoot -Root $root -SourceSha $sourceSha -Attempt $attempt
    $phases = [System.Collections.Generic.List[object]]::new()
    $packages = [System.Collections.Generic.List[object]]::new()
    $env:NUGET_PACKAGES = $packagesPath

    $unitProject = Join-Path $root 'src\Unlimotion.Test\Unlimotion.Test.csproj'
    $headlessProject = Join-Path $root 'tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj'
    try {
        Invoke-DotNet -Phase 'regression:restore:unit' -Arguments @(
            'restore', $unitProject, '--force', '--no-http-cache',
            '--configfile', (Join-Path $root 'src\nuget.config'),
            '-p:Configuration=Debug',
            '-p:DisableImplicitLibraryPacksFolder=true',
            '-p:DisableImplicitNuGetFallbackFolder=true',
            '-p:RestoreFallbackFolders='
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:restore:headless' -Arguments @(
            'restore', $headlessProject, '--force', '--no-http-cache',
            '--configfile', (Join-Path $root 'src\nuget.config'),
            '-p:Configuration=Debug',
            '-p:DisableImplicitLibraryPacksFolder=true',
            '-p:DisableImplicitNuGetFallbackFolder=true',
            '-p:RestoreFallbackFolders='
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:build:unit' -Arguments @(
            'build', $unitProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:build:headless' -Arguments @(
            'build', $headlessProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:unit' -Arguments @(
            'run', '--project', $unitProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:headless-1' -Arguments @(
            'run', '--project', $headlessProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:headless-2' -Arguments @(
            'run', '--project', $headlessProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases

        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Regression' -SourceSha $sourceSha -Attempt $attempt -Outcome 'success' -Phases $phases -Packages $packages -FailureCode $null
        Write-Output "NuGet regression evidence: $evidencePath"
    } catch {
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Regression' -SourceSha $sourceSha -Attempt $attempt -Outcome 'failure' -Phases $phases -Packages $packages -FailureCode 'attempt-failed'
        throw
    }
}

function Invoke-SelfTest {
    Assert-True ((Get-ExpectedPackages).Count -eq 6) 'Expected signed subset changed.'
    Assert-True ('Signature' -ceq 'Signature') 'Case-sensitive lane comparison changed.'

    $invalidAttemptRejected = $false
    $originalRunAttempt = $RunAttempt
    try {
        $script:RunAttempt = '01'
        [void](Resolve-RunAttempt)
    } catch {
        $invalidAttemptRejected = $true
    } finally {
        $script:RunAttempt = $originalRunAttempt
    }
    Assert-True $invalidAttemptRejected 'Non-canonical run attempt was accepted.'

    $fixtureProjects = @(
        [ordered]@{ projectPath = 'one'; packageSet = @(); graphSha256 = '1' },
        [ordered]@{ projectPath = 'two'; packageSet = @(); graphSha256 = '2' },
        [ordered]@{ projectPath = 'three'; packageSet = @(); graphSha256 = '3' }
    )
    $roundTrip = ([ordered]@{ projects = $fixtureProjects } | ConvertTo-Json -Depth 8) | ConvertFrom-Json -AsHashtable -Depth 16
    Assert-True (@($roundTrip.projects).Count -eq 3) 'Baseline project serialization must preserve all projects.'

    $baselinePackages = @(
        foreach ($transition in Get-ExpectedGraphTransitions) {
            [ordered]@{
                id = $transition.id
                version = $transition.baselineVersion
                source = 'nuget.org'
                nupkgSha512 = (('a' * 128) -join '')
            }
        }
    )
    $candidatePackages = @(
        foreach ($transition in Get-ExpectedGraphTransitions) {
            [ordered]@{
                id = $transition.id
                version = $transition.candidateVersion
                source = 'nuget.org'
                nupkgSha512 = (('b' * 128) -join '')
            }
        }
    )
    Assert-GraphDiffIsApproved -BaselinePackages $baselinePackages -CandidatePackages $candidatePackages -ProjectPath 'synthetic'

    $unrelatedBaseline = @($baselinePackages + [ordered]@{
            id = 'Unrelated.Package'
            version = '1.0.0'
            source = 'nuget.org'
            nupkgSha512 = (('c' * 128) -join '')
        })
    $unrelatedCandidate = @($candidatePackages + [ordered]@{
            id = 'Unrelated.Package'
            version = '2.0.0'
            source = 'nuget.org'
            nupkgSha512 = (('c' * 128) -join '')
        })
    $unrelatedDriftRejected = $false
    try {
        Assert-GraphDiffIsApproved -BaselinePackages $unrelatedBaseline -CandidatePackages $unrelatedCandidate -ProjectPath 'synthetic'
    } catch {
        $unrelatedDriftRejected = $true
    }
    Assert-True $unrelatedDriftRejected 'Graph validation accepted unrelated version drift.'

    $workerJson = '{"schemaVersion":1,"workerKind":"SignatureVerify","payload":{},"secretSeeds":[{"name":"API_TOKEN","value":"example"}]}'
    $workerBody = [Text.UTF8Encoding]::new($false).GetBytes($workerJson)
    $workerInput = [IO.MemoryStream]::new()
    $workerHeader = [byte[]]@(
        (($workerBody.Length -shr 24) -band 0xff),
        (($workerBody.Length -shr 16) -band 0xff),
        (($workerBody.Length -shr 8) -band 0xff),
        ($workerBody.Length -band 0xff)
    )
    try {
        $workerInput.Write($workerHeader, 0, $workerHeader.Length)
        $workerInput.Write($workerBody, 0, $workerBody.Length)
        $workerInput.Position = 0
        $workerRequest = Read-ClosedWorkerFrame -StandardInput $workerInput
        Assert-True ($workerRequest.WorkerKind -ceq 'SignatureVerify') 'Closed worker did not retain its canonical kind.'
        Assert-True ($workerRequest.SeedNameSha256 -cmatch '^[0-9a-f]{64}$') 'Closed worker did not derive a seed-name hash.'
        $nestedWorkerInput = [IO.MemoryStream]::new()
        try {
            Write-ClosedWorkerFrame -StandardOutput $nestedWorkerInput -Result ([ordered]@{
                    schemaVersion = 1
                    workerKind = 'SignatureVerify'
                    payload = [ordered]@{ assetsPaths = @('one', 'two', 'three') }
                    secretSeeds = @()
                })
            $nestedWorkerInput.Position = 0
            $nestedWorkerRequest = Read-ClosedWorkerFrame -StandardInput $nestedWorkerInput
            Assert-True ($nestedWorkerRequest.Payload.GetProperty('assetsPaths').GetArrayLength() -eq 3) 'Closed worker frame lost nested payload values.'
        } finally {
            $nestedWorkerInput.Dispose()
        }
        $largeWorkerInput = [IO.MemoryStream]::new()
        try {
            $largePadding = 'x' * 512
            Write-ClosedWorkerFrame -StandardOutput $largeWorkerInput -Result ([ordered]@{
                    schemaVersion = 1
                    workerKind = 'SignatureVerify'
                    payload = [ordered]@{ padding = $largePadding }
                    secretSeeds = @()
                })
            Assert-True ($largeWorkerInput.Length -gt 260) 'Closed worker self-test did not produce a multi-byte frame length.'
            $largeWorkerInput.Position = 0
            $largeWorkerRequest = Read-ClosedWorkerFrame -StandardInput $largeWorkerInput
            Assert-True ($largeWorkerRequest.Payload.GetProperty('padding').GetString().Length -eq $largePadding.Length) 'Closed worker frame lost a multi-byte length prefix.'
        } finally {
            $largeWorkerInput.Dispose()
        }
        $syntheticSeeds = Get-ClosedSecretSeedSnapshot -Environment ([ordered]@{ PATH = 'ordinary-path'; API_TOKEN = 'one'; ConnectionString = 'two' })
        Assert-True ($syntheticSeeds.Count -eq 2 -and $syntheticSeeds[0].name -ceq 'API_TOKEN' -and $syntheticSeeds[1].name -ceq 'ConnectionString') 'Secret environment snapshot did not retain only closed seed names.'
        $adapterResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureVerify' -Payload ([ordered]@{
                dotnetExecutable = (Get-Command dotnet -ErrorAction Stop).Source
                repositoryRoot = Get-CanonicalRepositoryRoot
                packagesRoot = [IO.Path]::GetTempPath()
                baselineGraphPath = Join-Path (Get-CanonicalRepositoryRoot) 'distribution\fixtures\reactiveui-signature-chain-baseline.json'
                assetsPaths = @(
                    (Join-Path (Get-CanonicalRepositoryRoot) 'missing-worker-adapter-a.json'),
                    (Join-Path (Get-CanonicalRepositoryRoot) 'missing-worker-adapter-b.json'),
                    (Join-Path (Get-CanonicalRepositoryRoot) 'missing-worker-adapter-c.json')
                )
            }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
        Assert-True ($adapterResult.Success -eq $false -and $adapterResult.FailureCode -ceq 'worker-failed' -and $adapterResult.ExitCode -eq 1 -and $adapterResult.TerminationProven -eq $true) 'Closed worker adapter did not preserve the expected negative result.'
        $seedNameBytes = [Text.UTF8Encoding]::new($false).GetBytes('API_TOKEN' + [Environment]::NewLine + 'ConnectionString')
        try {
            $expectedSeedNameHash = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($seedNameBytes))).ToLowerInvariant()
        } finally {
            [Array]::Clear($seedNameBytes, 0, $seedNameBytes.Length)
        }
        Assert-True ($adapterResult.SeedNameSha256 -ceq $expectedSeedNameHash) 'Closed worker adapter did not preserve the seed identity without exposing values.'
        $sanitizerRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-signature-sanitize-' + [Guid]::NewGuid().ToString('N'))
        try {
            $sanitizerProjects = @(
                [ordered]@{ id = 'headless'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; assetsCopyId = 'headless'; assetsSha256 = ('a' * 64); baselineGraphSha256 = ('b' * 64); targetGraphSha256 = ('c' * 64) },
                [ordered]@{ id = 'desktop'; projectPath = 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'; assetsCopyId = 'desktop'; assetsSha256 = ('d' * 64); baselineGraphSha256 = ('e' * 64); targetGraphSha256 = ('f' * 64) },
                [ordered]@{ id = 'debian'; projectPath = 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj'; assetsCopyId = 'debian'; assetsSha256 = ('1' * 64); baselineGraphSha256 = ('2' * 64); targetGraphSha256 = ('3' * 64) }
            )
            $sanitizerPackages = @(
                Get-ExpectedPackages | ForEach-Object { [ordered]@{ id = $_.Id; version = $_.Version; nupkgSha512 = ('a' * 128); authorCertificateSha256 = Get-ExpectedAuthorFingerprint } }
            )
            $sanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureSanitize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $sanitizerRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    projects = $sanitizerProjects
                    packages = $sanitizerPackages
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0 })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($sanitizerResult.Success -eq $true -and $sanitizerResult.ExitCode -eq 0 -and $sanitizerResult.TerminationProven -eq $true) 'Signature sanitizer worker did not return a closed success tuple.'
            $candidateFiles = @(Get-ChildItem -LiteralPath $sanitizerRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($sanitizerRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($candidateFiles.Count -eq 7 -and $candidateFiles[0] -ceq 'signature/evidence.json') 'Signature sanitizer candidate file set is invalid.'
            $candidateText = [IO.File]::ReadAllText((Join-Path $sanitizerRoot 'signature\evidence.json'), [Text.UTF8Encoding]::new($false))
            Assert-True (-not $candidateText.Contains('one', [StringComparison]::Ordinal) -and -not $candidateText.Contains('two', [StringComparison]::Ordinal)) 'Signature sanitizer exposed a secret seed in candidate evidence.'
            $finalizerRoot = $sanitizerRoot + '-final'
            $finalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $sanitizerRoot
                    finalEvidenceRoot = $finalizerRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Signature'
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0 })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($finalizerResult.Success -eq $true -and $finalizerResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $finalizerRoot)) 'Publication finalizer worker did not publish the primary tree.'
            $finalFiles = @(Get-ChildItem -LiteralPath $finalizerRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($finalizerRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($finalFiles.Count -eq 8 -and $finalFiles[0] -ceq 'attempt-receipt.json') 'Publication finalizer tree has an invalid file set.'
            $finalReceipt = [IO.File]::ReadAllText((Join-Path $finalizerRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ((@($finalReceipt.evidenceManifest)).Count -eq 7 -and (@($finalReceipt.evidenceManifest | Where-Object { $_.path -ceq 'attempt-receipt.json' })).Count -eq 0) 'Publication finalizer receipt self-hashed or lost candidate files.'
            Remove-Item -LiteralPath $finalizerRoot -Recurse -Force -ErrorAction Stop
        } finally {
            if (Test-Path -LiteralPath $sanitizerRoot) { Remove-Item -LiteralPath $sanitizerRoot -Recurse -Force -ErrorAction SilentlyContinue }
        }
    } finally {
        $workerInput.Dispose()
        [Array]::Clear($workerHeader, 0, $workerHeader.Length)
        [Array]::Clear($workerBody, 0, $workerBody.Length)
    }
}

if ($Mode -eq 'GenerateBaseline') {
    Invoke-GenerateBaseline
    return
}

if ($Mode -eq 'Worker') {
    $workerSucceeded = Invoke-ClosedWorkerCliMode -StandardInput ([Console]::OpenStandardInput()) -StandardOutput ([Console]::OpenStandardOutput())
    if (-not $workerSucceeded) { exit 1 }
    return
}

if ($Mode -eq 'SelfTest') {
    Invoke-SelfTest
    return
}

switch -CaseSensitive ($Lane) {
    'Signature' { Invoke-SignatureAttempt; break }
    'Regression' { Invoke-RegressionAttempt; break }
    default { throw 'RunAttempt lane must be exactly Signature or Regression.' }
}
