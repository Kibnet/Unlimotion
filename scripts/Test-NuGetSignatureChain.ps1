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

function Read-ClosedWorkerFrame([IO.Stream]$StandardInput) {
    $header = Read-ExactBytes -Stream $StandardInput -Count 4
    $length = (($header[0] -shl 24) -bor ($header[1] -shl 16) -bor ($header[2] -shl 8) -bor $header[3])
    Assert-True ($length -ge 2 -and $length -le 1048576) 'Closed worker frame length is invalid.'
    $body = Read-ExactBytes -Stream $StandardInput -Count $length
    Assert-True ($StandardInput.ReadByte() -eq -1) 'Closed worker accepts exactly one input frame.'
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

function Invoke-ClosedWorkerCliMode([IO.Stream]$StandardInput, [IO.Stream]$StandardOutput) {
    $request = Read-ClosedWorkerFrame -StandardInput $StandardInput
    try {
        $workerResult = switch -CaseSensitive ($request.WorkerKind) {
            'SignatureVerify' { Invoke-SignatureVerifyWorker -Payload $request.Payload; break }
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

        try {
            Assert-CandidateGraphsAgainstBaseline -BaselinePath (Join-Path $root 'distribution/fixtures/reactiveui-signature-chain-baseline.json') -Root $root -PackagesPath $packagesPath -Projects $projects
            $phases.Add([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0 })
        } catch {
            $phases.Add([ordered]@{ name = 'signature:verify:graph'; status = 'failure'; exitCode = 1 })
            throw
        }
        $packages = Get-PackageReceipt -PackagesPath $packagesPath -Phases $phases
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
