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
    [string]$DotNetExecutable = 'dotnet',
    [switch]$FullChild
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

function Get-EvidenceExecutionContext {
    if ($FullChild) {
        return 'full-child'
    }

    if ([Environment]::GetEnvironmentVariable('GITHUB_ACTIONS', 'Process') -ceq 'true') {
        return 'github-actions'
    }

    return 'local'
}

function Get-SanitizedRuntime([string]$EvidenceContext, [bool]$SignatureAuthoritative) {
    Assert-True ($EvidenceContext -cin @('github-actions', 'local', 'full-child')) 'Evidence execution context is invalid.'
    $os = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } else { throw 'Evidence runtime operating system is unsupported.' }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    Assert-True ($architecture -cin @('x64', 'arm64')) 'Evidence runtime architecture is unsupported.'
    $sdkVersion = (& (Get-AbsoluteDotNetExecutable) --version).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $sdkVersion -cmatch '^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') 'Could not determine an approved .NET SDK version.'
    Assert-True ((-not $SignatureAuthoritative) -or $EvidenceContext -ceq 'github-actions') 'Signature authority is invalid outside GitHub Actions.'
    return [ordered]@{
        os = $os
        architecture = $architecture
        dotnetSdkVersion = $sdkVersion
        executionContext = $EvidenceContext
        signatureVerification = $true
        revocationMode = $null
        signatureAuthoritative = $SignatureAuthoritative
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Initialize-WindowsNativeFileIdentity {
    Assert-True $IsWindows 'Native Full file identity is available only on Windows.'
    if ($null -ne ('Unlimotion.NuGetEvidence.NativeFileIdentity' -as [type])) { return }

    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Unlimotion.NuGetEvidence
{
    public static class NativeFileIdentity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        public sealed class Result
        {
            public uint VolumeSerialNumber { get; private set; }
            public uint NumberOfLinks { get; private set; }
            public ulong FileIndex { get; private set; }

            public Result(uint volumeSerialNumber, uint numberOfLinks, ulong fileIndex)
            {
                VolumeSerialNumber = volumeSerialNumber;
                NumberOfLinks = numberOfLinks;
                FileIndex = fileIndex;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation fileInformation);

        public static Result Read(SafeFileHandle file)
        {
            ByHandleFileInformation information;
            if (!GetFileInformationByHandle(file, out information))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            ulong fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            return new Result(information.VolumeSerialNumber, information.NumberOfLinks, fileIndex);
        }
    }
}
'@ -ErrorAction Stop
}

function Get-WindowsNativeFileIdentity([string]$Path) {
    Initialize-WindowsNativeFileIdentity
    $item = Get-Item -LiteralPath $Path -Force
    Assert-True (-not $item.PSIsContainer -and -not $item.LinkType) 'Full native file identity requires a regular non-link file.'
    $stream = $null
    try {
        $stream = [IO.FileStream]::new($item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        return [Unlimotion.NuGetEvidence.NativeFileIdentity]::Read($stream.SafeFileHandle)
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Test-PathIsSameOrDescendant([string]$Root, [string]$Candidate) {
    $canonicalRoot = [IO.Path]::GetFullPath($Root)
    $canonicalCandidate = [IO.Path]::GetFullPath($Candidate)
    $relative = [IO.Path]::GetRelativePath($canonicalRoot, $canonicalCandidate)
    return $relative -ceq '.' -or (-not [IO.Path]::IsPathFullyQualified($relative) -and $relative -cne '..' -and -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal))
}

function Assert-FullRootsDoNotOverlap([hashtable]$Roots) {
    $names = @($Roots.Keys | Sort-Object)
    Assert-True ($names.Count -ge 2) 'Full root layout requires at least two roots.'
    foreach ($name in $names) {
        Assert-True ($Roots[$name] -is [string] -and [IO.Path]::IsPathFullyQualified($Roots[$name])) "Full $name root is not absolute."
    }
    for ($leftIndex = 0; $leftIndex -lt $names.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $names.Count; $rightIndex++) {
            $leftName = $names[$leftIndex]
            $rightName = $names[$rightIndex]
            Assert-True (-not (Test-PathIsSameOrDescendant -Root $Roots[$leftName] -Candidate $Roots[$rightName]) -and -not (Test-PathIsSameOrDescendant -Root $Roots[$rightName] -Candidate $Roots[$leftName])) "Full roots $leftName and $rightName overlap."
        }
    }
}

function Get-FullTreeNativeFileIdentityMap([string]$TreeRoot) {
    $root = [IO.Path]::GetFullPath($TreeRoot)
    $rootItem = Get-Item -LiteralPath $root -Force
    Assert-True ($rootItem.PSIsContainer -and -not $rootItem.LinkType) 'Full tree root is invalid.'
    foreach ($directory in @(Get-ChildItem -LiteralPath $root -Recurse -Directory -Force)) {
        Assert-True (-not $directory.LinkType) 'Full tree directory cannot be a link.'
    }
    $identities = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
        Assert-True (-not $file.LinkType) 'Full tree file cannot be a link.'
        if (-not $IsWindows) { continue }
        $identity = Get-WindowsNativeFileIdentity -Path $file.FullName
        Assert-True ($identity.NumberOfLinks -eq 1) 'Full tree file link count must be exactly one.'
        $key = ('{0:x8}:{1:x16}' -f $identity.VolumeSerialNumber, $identity.FileIndex)
        Assert-True (-not $identities.ContainsKey($key)) 'Full tree contains duplicate native file identity.'
        $identities.Add($key, $file.FullName)
    }
    return $identities
}

function Assert-FullTreesHaveDistinctFileIdentity([string]$LeftRoot, [string]$RightRoot) {
    Assert-FullRootsDoNotOverlap -Roots @{ left = [IO.Path]::GetFullPath($LeftRoot); right = [IO.Path]::GetFullPath($RightRoot) }
    $left = Get-FullTreeNativeFileIdentityMap -TreeRoot $LeftRoot
    $right = Get-FullTreeNativeFileIdentityMap -TreeRoot $RightRoot
    foreach ($identity in $left.Keys) {
        Assert-True (-not $right.ContainsKey($identity)) 'Full trees share a native file identity.'
    }
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

function ConvertTo-SecretSeedElements([object[]]$SecretSeeds) {
    $elements = [System.Collections.Generic.List[Text.Json.JsonElement]]::new()
    foreach ($seed in $SecretSeeds) {
        if ($seed -is [Text.Json.JsonElement]) {
            $elements.Add($seed.Clone())
            continue
        }
        $json = ConvertTo-Json -InputObject $seed -Depth 8 -Compress
        $document = [Text.Json.JsonDocument]::Parse($json)
        try {
            $elements.Add($document.RootElement.Clone())
        } finally {
            $document.Dispose()
        }
    }
    return ,$elements.ToArray()
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

function Invoke-SignatureFailureSanitizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'sourceSha', 'runAttempt', 'executionContext', 'failurePhase', 'completedProjects', 'attemptedPackages', 'diagnostics', 'phaseResults') -Name 'SignatureSanitize failure payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    Assert-True (-not (Test-Path -LiteralPath $candidateRoot)) 'SignatureSanitize candidate root must be absent.'
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    $evidenceContext = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'executionContext'
    $failurePhase = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'failurePhase'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$' -and $evidenceContext -cin @('github-actions', 'local', 'full-child') -and $failurePhase -cmatch '^signature:') 'SignatureSanitize failure identity is invalid.'
    $completedProjects = $Payload.GetProperty('completedProjects')
    $attemptedPackages = $Payload.GetProperty('attemptedPackages')
    $diagnostics = $Payload.GetProperty('diagnostics')
    $phases = $Payload.GetProperty('phaseResults')
    Assert-True ($completedProjects.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $completedProjects.GetArrayLength() -le 3) 'SignatureSanitize completed projects are invalid.'
    Assert-True ($attemptedPackages.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $attemptedPackages.GetArrayLength() -le 6) 'SignatureSanitize attempted packages are invalid.'
    Assert-True ($diagnostics.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $diagnostics.GetArrayLength() -ge 1) 'SignatureSanitize diagnostics are invalid.'
    Assert-True ($phases.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $phases.GetArrayLength() -ge 1) 'SignatureSanitize phase results are invalid.'
    New-Item -ItemType Directory -Path $candidateRoot -ErrorAction Stop | Out-Null
    $normalizedPackages = [System.Collections.Generic.List[object]]::new()
    try {
        $expectedPackages = Get-ExpectedPackages
        $attemptedIndex = 0
        foreach ($package in $attemptedPackages.EnumerateArray()) {
            Assert-ExactJsonObjectProperties -Object $package -Expected @('id', 'version', 'nupkgSha512', 'verifyExitCode') -Name 'SignatureSanitize attempted package'
            Assert-True ($attemptedIndex -lt $expectedPackages.Count) 'SignatureSanitize attempted package count is invalid.'
            $id = $package.GetProperty('id').GetString()
            $version = $package.GetProperty('version').GetString()
            $hash = $package.GetProperty('nupkgSha512').GetString()
            $verifyExitCode = $package.GetProperty('verifyExitCode')
            Assert-True ($id -ceq $expectedPackages[$attemptedIndex].Id -and $version -ceq $expectedPackages[$attemptedIndex].Version -and $hash -cmatch '^[0-9a-f]{128}$' -and $verifyExitCode.ValueKind -eq [Text.Json.JsonValueKind]::Number) 'SignatureSanitize attempted package fields are invalid.'
            $logPath = "signature/verify/$id.log"
            $logBytes = [Text.UTF8Encoding]::new($false).GetBytes("package=$id`nversion=$version`nverifyExitCode=$($verifyExitCode.GetInt32())`n")
            [void](Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath $logPath -Bytes $logBytes -SecretSeeds $SecretSeeds)
            $normalizedPackages.Add([ordered]@{ id = $id; version = $version; nupkgSha512 = $hash; verifyExitCode = $verifyExitCode.GetInt32(); verifyLog = $logPath })
            $attemptedIndex++
        }
        $evidence = [ordered]@{
            schemaVersion = 1
            evidenceKind = 'signature-failure'
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = 'Signature'
            runtime = Get-SanitizedRuntime -EvidenceContext $evidenceContext -SignatureAuthoritative ($evidenceContext -ceq 'github-actions')
            failurePhase = $failurePhase
            completedProjects = @($completedProjects.EnumerateArray() | ForEach-Object { $_.Clone() })
            attemptedPackages = $normalizedPackages.ToArray()
            diagnostics = @($diagnostics.EnumerateArray() | ForEach-Object { $_.Clone() })
        }
        Assert-True ($LASTEXITCODE -eq 0) 'SignatureSanitize could not determine the .NET SDK version.'
        $evidenceBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $evidence -Depth 16 -Compress))
        try {
            [void](Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath 'signature/evidence.json' -Bytes $evidenceBytes -SecretSeeds $SecretSeeds)
        } finally {
            [Array]::Clear($evidenceBytes, 0, $evidenceBytes.Length)
        }
        return [ordered]@{ success = $true; failureCode = $null; packages = @() }
    } catch {
        if (Test-Path -LiteralPath $candidateRoot) { Remove-Item -LiteralPath $candidateRoot -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Invoke-SignatureSanitizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    $payloadPropertyNames = @($Payload.EnumerateObject() | ForEach-Object { $_.Name })
    if ($payloadPropertyNames -cnotcontains 'projects') {
        return Invoke-SignatureFailureSanitizeWorker -Payload $Payload -SecretSeeds $SecretSeeds
    }
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'sourceSha', 'runAttempt', 'executionContext', 'projects', 'packages', 'phaseResults') -Name 'SignatureSanitize payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    Assert-True (-not (Test-Path -LiteralPath $candidateRoot)) 'SignatureSanitize candidate root must be absent.'
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    $evidenceContext = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'executionContext'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$' -and $evidenceContext -cin @('github-actions', 'local', 'full-child')) 'SignatureSanitize identity is invalid.'
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
            runtime = Get-SanitizedRuntime -EvidenceContext $evidenceContext -SignatureAuthoritative ($evidenceContext -ceq 'github-actions')
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

function Get-RelativeForwardSlashPath([string]$Root, [string]$Path) {
    return [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Get-ValidatedRawTUnitReports([string]$RawRunRoot) {
    $root = [IO.Path]::GetFullPath($RawRunRoot)
    Assert-True (Test-Path -LiteralPath $root -PathType Container) 'Raw TUnit report root is missing.'
    Assert-True (-not (Get-Item -LiteralPath $root -Force).LinkType) 'Raw TUnit report root cannot be a link.'
    $topFiles = @(Get-ChildItem -LiteralPath $root -Force -File | Sort-Object -Property Name)
    Assert-True ($topFiles.Count -eq 2 -and $topFiles[0].Name -ceq 'results.html' -and $topFiles[1].Name -ceq 'results.trx') 'Raw TUnit report root must contain exactly results.html and results.trx.'
    foreach ($file in $topFiles) {
        Assert-True (-not $file.LinkType) 'Raw TUnit report file cannot be a link.'
    }
    $htmlPath = Join-Path $root 'results.html'
    $trxPath = Join-Path $root 'results.trx'
    Assert-True ((Get-Item -LiteralPath $htmlPath).Length -gt 0 -and (Get-Item -LiteralPath $htmlPath).Length -le 16MB) 'Raw TUnit HTML report size is invalid.'
    Assert-True ((Get-Item -LiteralPath $trxPath).Length -gt 0 -and (Get-Item -LiteralPath $trxPath).Length -le 32MB) 'Raw TUnit TRX report size is invalid.'

    $directories = @(Get-ChildItem -LiteralPath $root -Recurse -Directory -Force)
    foreach ($directory in $directories) {
        Assert-True (-not $directory.LinkType) 'Raw TUnit report directory cannot be a link.'
    }
    $sidecarFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Where-Object { $_.FullName -cne $htmlPath -and $_.FullName -cne $trxPath })
    Assert-True ($directories.Count -eq 3 -and $sidecarFiles.Count -eq 1) 'Raw TUnit report root has an unexpected sidecar shape.'
    $sidecar = $sidecarFiles[0]
    Assert-True (-not $sidecar.LinkType -and $sidecar.Length -eq (Get-Item -LiteralPath $htmlPath).Length) 'Raw TUnit HTML shadow is invalid.'
    $relativeDirectories = @($directories | ForEach-Object { Get-RelativeForwardSlashPath -Root $root -Path $_.FullName } | Sort-Object)
    $runnerId = $relativeDirectories[0]
    Assert-True ($runnerId -cmatch '^[A-Za-z0-9._-]+$' -and $relativeDirectories[1] -ceq "$runnerId/In" -and $relativeDirectories[2] -cmatch ('^{0}/In/[A-Za-z0-9._-]+$' -f [Regex]::Escape($runnerId))) 'Raw TUnit shadow directories are invalid.'
    $sidecarRelativePath = Get-RelativeForwardSlashPath -Root $root -Path $sidecar.FullName
    Assert-True ($sidecarRelativePath -cmatch ('^{0}/In/[A-Za-z0-9._-]+/results\.html$' -f [Regex]::Escape($runnerId))) 'Raw TUnit HTML shadow path is invalid.'
    Assert-True ((Get-FileHash -LiteralPath $sidecar.FullName -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $htmlPath -Algorithm SHA256).Hash) 'Raw TUnit HTML shadow bytes differ from the primary report.'
    return [ordered]@{ trxPath = $trxPath; htmlPath = $htmlPath }
}

function ConvertTo-NonNegativeInt32([string]$Value, [string]$Name) {
    $parsed = 0
    Assert-True ($Value -cmatch '^(0|[1-9][0-9]*)$' -and [int]::TryParse($Value, [ref]$parsed) -and $parsed -ge 0) "$Name is invalid."
    return $parsed
}

function Read-RawTUnitTrxSummary([string]$TrxPath) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($TrxPath, $settings)
    try {
        $document.Load($reader)
    } finally {
        $reader.Dispose()
    }
    $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $namespace)
    Assert-True ($null -ne $counters) 'Raw TUnit TRX counters are missing.'
    $total = ConvertTo-NonNegativeInt32 -Value $counters.GetAttribute('total') -Name 'Raw TUnit total count'
    $passed = ConvertTo-NonNegativeInt32 -Value $counters.GetAttribute('passed') -Name 'Raw TUnit passed count'
    $failed = ConvertTo-NonNegativeInt32 -Value $counters.GetAttribute('failed') -Name 'Raw TUnit failed count'
    $skipped = ConvertTo-NonNegativeInt32 -Value $counters.GetAttribute('notExecuted') -Name 'Raw TUnit skipped count'
    Assert-True ($total -eq $passed + $failed + $skipped) 'Raw TUnit TRX counters do not add up.'
    $results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespace))
    Assert-True ($results.Count -eq $total) 'Raw TUnit TRX result cardinality is invalid.'
    $duration = [TimeSpan]::Zero
    foreach ($result in $results) {
        $resultDuration = [TimeSpan]::Zero
        Assert-True ([TimeSpan]::TryParse($result.GetAttribute('duration'), [ref]$resultDuration) -and $resultDuration -ge [TimeSpan]::Zero) 'Raw TUnit result duration is invalid.'
        $duration += $resultDuration
    }
    return [ordered]@{ discovered = $total; passed = $passed; failed = $failed; skipped = $skipped; durationMs = [long][Math]::Round($duration.TotalMilliseconds, 0, [MidpointRounding]::AwayFromZero) }
}

function Invoke-RegressionSanitizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'sourceSha', 'runAttempt', 'executionContext', 'phaseResults', 'runs') -Name 'RegressionSanitize payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    Assert-True (-not (Test-Path -LiteralPath $candidateRoot)) 'RegressionSanitize candidate root must be absent.'
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    $evidenceContext = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'executionContext'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$' -and $evidenceContext -cin @('github-actions', 'local', 'full-child')) 'RegressionSanitize identity is invalid.'
    $phaseResults = $Payload.GetProperty('phaseResults')
    $runs = $Payload.GetProperty('runs')
    Assert-True ($phaseResults.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $phaseResults.GetArrayLength() -ge 1) 'RegressionSanitize phase results are invalid.'
    Assert-True ($runs.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $runs.GetArrayLength() -eq 3) 'RegressionSanitize run set is invalid.'
    foreach ($phase in $phaseResults.EnumerateArray()) {
        Assert-ExactJsonObjectProperties -Object $phase -Expected @('name', 'status', 'exitCode', 'failureCode') -Name 'RegressionSanitize phase'
        $phaseName = $phase.GetProperty('name')
        $phaseStatus = $phase.GetProperty('status')
        $phaseExitCode = $phase.GetProperty('exitCode')
        $phaseFailureCode = $phase.GetProperty('failureCode')
        Assert-True ($phaseName.ValueKind -eq [Text.Json.JsonValueKind]::String -and $phaseName.GetString() -cmatch '^regression:(restore|build|test):') 'RegressionSanitize phase name is invalid.'
        Assert-True ($phaseStatus.ValueKind -eq [Text.Json.JsonValueKind]::String -and $phaseStatus.GetString() -cin @('success', 'failure')) 'RegressionSanitize phase status is invalid.'
        Assert-True ($phaseExitCode.ValueKind -eq [Text.Json.JsonValueKind]::Number) 'RegressionSanitize phase exit code is invalid.'
        $phaseExit = $phaseExitCode.GetInt32()
        Assert-True (($phaseStatus.GetString() -ceq 'success' -and $phaseExit -eq 0 -and $phaseFailureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) -or ($phaseStatus.GetString() -ceq 'failure' -and $phaseExit -ne 0 -and $phaseFailureCode.ValueKind -eq [Text.Json.JsonValueKind]::String -and -not [string]::IsNullOrWhiteSpace($phaseFailureCode.GetString()))) 'RegressionSanitize phase tuple is invalid.'
    }
    $expectedRuns = @(
        [ordered]@{ runId = 'unit'; projectPath = 'src/Unlimotion.Test/Unlimotion.Test.csproj'; minimumDiscovered = 830 },
        [ordered]@{ runId = 'headless-1'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; minimumDiscovered = 36 },
        [ordered]@{ runId = 'headless-2'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; minimumDiscovered = 36 }
    )
    $normalizedRuns = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $expectedRuns.Count; $index++) {
        $run = $runs[$index]
        Assert-ExactJsonObjectProperties -Object $run -Expected @('runId', 'state', 'projectPath', 'configuration', 'nativeExitCode', 'failureCode', 'discovered', 'passed', 'failed', 'skipped', 'durationMs', 'skipReason') -Name 'RegressionSanitize run'
        $runId = $run.GetProperty('runId')
        $state = $run.GetProperty('state')
        $projectPath = $run.GetProperty('projectPath')
        $configuration = $run.GetProperty('configuration')
        $nativeExitCode = $run.GetProperty('nativeExitCode')
        $failureCode = $run.GetProperty('failureCode')
        $discovered = $run.GetProperty('discovered')
        $passed = $run.GetProperty('passed')
        $failed = $run.GetProperty('failed')
        $skipped = $run.GetProperty('skipped')
        $durationMs = $run.GetProperty('durationMs')
        $skipReason = $run.GetProperty('skipReason')
        $expected = $expectedRuns[$index]
        Assert-True ($runId.ValueKind -eq [Text.Json.JsonValueKind]::String -and $runId.GetString() -ceq $expected.runId) 'RegressionSanitize run id is invalid.'
        Assert-True ($state.ValueKind -eq [Text.Json.JsonValueKind]::String -and $state.GetString() -cin @('success', 'failure', 'not-attempted')) 'RegressionSanitize run state is invalid.'
        Assert-True ($projectPath.ValueKind -eq [Text.Json.JsonValueKind]::String -and $projectPath.GetString() -ceq $expected.projectPath -and $configuration.ValueKind -eq [Text.Json.JsonValueKind]::String -and $configuration.GetString() -ceq 'Debug') 'RegressionSanitize run identity is invalid.'
        Assert-True ($nativeExitCode.ValueKind -in @([Text.Json.JsonValueKind]::Number, [Text.Json.JsonValueKind]::Null)) 'RegressionSanitize native exit code is invalid.'
        if ($nativeExitCode.ValueKind -eq [Text.Json.JsonValueKind]::Number) { [void]$nativeExitCode.GetInt32() }
        $nullableNumbers = @($discovered, $passed, $failed, $skipped, $durationMs)
        foreach ($number in $nullableNumbers) {
            Assert-True ($number.ValueKind -in @([Text.Json.JsonValueKind]::Number, [Text.Json.JsonValueKind]::Null)) 'RegressionSanitize nullable number is invalid.'
            if ($number.ValueKind -eq [Text.Json.JsonValueKind]::Number) { Assert-True ($number.GetInt64() -ge 0) 'RegressionSanitize number cannot be negative.' }
        }
        Assert-True ($failureCode.ValueKind -in @([Text.Json.JsonValueKind]::String, [Text.Json.JsonValueKind]::Null) -and $skipReason.ValueKind -in @([Text.Json.JsonValueKind]::String, [Text.Json.JsonValueKind]::Null)) 'RegressionSanitize nullable string is invalid.'
        $normalized = [ordered]@{
            runId = $runId.GetString()
            state = $state.GetString()
            projectPath = $projectPath.GetString()
            configuration = 'Debug'
            nativeExitCode = if ($nativeExitCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $nativeExitCode.GetInt32() }
            failureCode = if ($failureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $failureCode.GetString() }
            discovered = if ($discovered.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $discovered.GetInt32() }
            passed = if ($passed.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $passed.GetInt32() }
            failed = if ($failed.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $failed.GetInt32() }
            skipped = if ($skipped.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $skipped.GetInt32() }
            durationMs = if ($durationMs.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $durationMs.GetInt64() }
            trx = $null
            html = $null
            skipReason = if ($skipReason.ValueKind -eq [Text.Json.JsonValueKind]::Null) { $null } else { $skipReason.GetString() }
        }
        if ($normalized.state -ceq 'success') {
            Assert-True ($normalized.nativeExitCode -eq 0 -and $null -eq $normalized.failureCode -and $null -eq $normalized.skipReason) 'RegressionSanitize successful run tuple is invalid.'
            Assert-True ($null -ne $normalized.discovered -and $normalized.discovered -ge $expected.minimumDiscovered -and $normalized.passed -eq $normalized.discovered -and $normalized.failed -eq 0 -and $normalized.skipped -eq 0 -and $null -ne $normalized.durationMs) 'RegressionSanitize successful run counts are invalid.'
        } elseif ($normalized.state -ceq 'failure') {
            Assert-True ($null -ne $normalized.nativeExitCode -and $normalized.failureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($normalized.failureCode) -and $null -eq $normalized.skipReason) 'RegressionSanitize failed run tuple is invalid.'
        } else {
            Assert-True ($null -eq $normalized.nativeExitCode -and $null -eq $normalized.failureCode -and $null -eq $normalized.discovered -and $null -eq $normalized.passed -and $null -eq $normalized.failed -and $null -eq $normalized.skipped -and $null -eq $normalized.durationMs -and $normalized.skipReason -ceq 'prerequisite-failed') 'RegressionSanitize skipped run tuple is invalid.'
        }
        $normalizedRuns.Add($normalized)
    }
    if ($normalizedRuns[1].state -ceq 'success' -and $normalizedRuns[2].state -ceq 'success') {
        Assert-True ($normalizedRuns[1].discovered -eq $normalizedRuns[2].discovered -and $normalizedRuns[1].passed -eq $normalizedRuns[2].passed -and $normalizedRuns[1].failed -eq $normalizedRuns[2].failed -and $normalizedRuns[1].skipped -eq $normalizedRuns[2].skipped) 'RegressionSanitize headless runs have inconsistent counts.'
    }
    New-Item -ItemType Directory -Path $candidateRoot -ErrorAction Stop | Out-Null
    try {
        $hasFailedPhase = @($phaseResults.EnumerateArray() | Where-Object { $_.GetProperty('status').ValueKind -eq [Text.Json.JsonValueKind]::String -and $_.GetProperty('status').GetString() -ceq 'failure' }).Count -gt 0
        Assert-True ($hasFailedPhase -or @($normalizedRuns | Where-Object { $_.state -cne 'success' }).Count -eq 0) 'RegressionSanitize non-success run requires a failed phase.'
        foreach ($run in $normalizedRuns) {
            if ($run.state -ceq 'success') {
                $trx = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><ResultSummary outcome=`"Completed`"><Counters total=`"$($run.discovered)`" executed=`"$($run.discovered)`" passed=`"$($run.passed)`" failed=`"0`" notExecuted=`"0`" /></ResultSummary></TestRun>"
                $html = "<!doctype html><html><head><meta charset=`"utf-8`"><title>Unlimotion regression $($run.runId)</title></head><body><h1>Unlimotion regression $($run.runId)</h1><dl><dt>discovered</dt><dd>$($run.discovered)</dd><dt>passed</dt><dd>$($run.passed)</dd><dt>failed</dt><dd>0</dd><dt>skipped</dt><dd>0</dd><dt>durationMs</dt><dd>$($run.durationMs)</dd></dl></body></html>"
                $trxBytes = [Text.UTF8Encoding]::new($false).GetBytes($trx)
                $htmlBytes = [Text.UTF8Encoding]::new($false).GetBytes($html)
                try {
                    $run.trx = Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath "regression/$($run.runId).trx" -Bytes $trxBytes -SecretSeeds $SecretSeeds
                    $run.html = Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath "regression/$($run.runId).html" -Bytes $htmlBytes -SecretSeeds $SecretSeeds
                } finally {
                    [Array]::Clear($trxBytes, 0, $trxBytes.Length)
                    [Array]::Clear($htmlBytes, 0, $htmlBytes.Length)
                }
            }
        }
        $evidence = [ordered]@{
            schemaVersion = 1
            evidenceKind = if ($hasFailedPhase) { 'regression-failure' } else { 'regression-success' }
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = 'Regression'
            runtime = Get-SanitizedRuntime -EvidenceContext $evidenceContext -SignatureAuthoritative $false
            runs = $normalizedRuns.ToArray()
        }
        Assert-True ($LASTEXITCODE -eq 0) 'RegressionSanitize could not determine the .NET SDK version.'
        $evidenceBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $evidence -Depth 16 -Compress))
        try {
            [void](Write-SanitizedCandidateFile -Root $candidateRoot -RelativePath 'regression/evidence.json' -Bytes $evidenceBytes -SecretSeeds $SecretSeeds)
        } finally {
            [Array]::Clear($evidenceBytes, 0, $evidenceBytes.Length)
        }
        return [ordered]@{ success = $true; failureCode = $null; packages = @() }
    } catch {
        if (Test-Path -LiteralPath $candidateRoot) { Remove-Item -LiteralPath $candidateRoot -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Get-CandidateEvidenceManifest([string]$CandidateRoot, [Text.Json.JsonElement[]]$SecretSeeds) {
    $root = [IO.Path]::GetFullPath($CandidateRoot)
    Assert-True ((Test-Path -LiteralPath $root -PathType Container) -and -not (Get-Item -LiteralPath $root -Force).LinkType) 'Candidate evidence root is invalid.'
    foreach ($directory in @(Get-ChildItem -LiteralPath $root -Recurse -Directory -Force)) {
        Assert-True (-not $directory.LinkType) 'Candidate evidence directory cannot be a link.'
    }
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

function Assert-EquivalentEvidenceManifest([object[]]$Expected, [object[]]$Actual, [string]$Name) {
    Assert-True ($Expected.Count -eq $Actual.Count) "$Name has an unexpected entry count."
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $expected = $Expected[$index]
        $actual = $Actual[$index]
        Assert-True ($actual.path -ceq $expected.path -and $actual.sha256 -ceq $expected.sha256 -and [long]$actual.byteLength -eq [long]$expected.byteLength) "$Name does not match its expected manifest entry."
    }
}

function Get-CandidateEvidenceKind([string]$CandidateRoot, [string]$Lane) {
    Assert-True ($Lane -cin @('Signature', 'Regression')) 'PublicationFinalize lane is invalid.'
    $prefix = $Lane.ToLowerInvariant()
    $evidencePath = Join-Path $CandidateRoot "$prefix\evidence.json"
    Assert-True (Test-Path -LiteralPath $evidencePath -PathType Leaf) 'PublicationFinalize lane evidence is missing.'
    $bytes = [IO.File]::ReadAllBytes($evidencePath)
    try {
        $document = [Text.Json.JsonDocument]::Parse([System.ReadOnlyMemory[byte]]::new($bytes))
        try {
            $root = $document.RootElement
            Assert-True ($root.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'PublicationFinalize lane evidence root is invalid.'
            $kind = $root.GetProperty('evidenceKind')
            Assert-True ($kind.ValueKind -eq [Text.Json.JsonValueKind]::String -and $kind.GetString() -cin @("$prefix-success", "$prefix-failure")) 'PublicationFinalize lane evidence kind is invalid.'
            return $kind.GetString()
        } finally {
            $document.Dispose()
        }
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Invoke-PublicationFinalizeWorker([Text.Json.JsonElement]$Payload, [Text.Json.JsonElement[]]$SecretSeeds) {
    Assert-ExactJsonObjectProperties -Object $Payload -Expected @('candidateEvidenceRoot', 'finalEvidenceRoot', 'sourceSha', 'runAttempt', 'lane', 'phaseResults') -Name 'PublicationFinalize payload'
    $candidateRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'candidateEvidenceRoot'))
    $finalRoot = [IO.Path]::GetFullPath((Get-RequiredWorkerPayloadString -Payload $Payload -Name 'finalEvidenceRoot'))
    $sourceSha = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'sourceSha'
    $runAttempt = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'runAttempt'
    $lane = Get-RequiredWorkerPayloadString -Payload $Payload -Name 'lane'
    Assert-True ($sourceSha -cmatch '^[0-9a-f]{40}$' -and $runAttempt -cmatch '^[1-9][0-9]{0,9}$' -and $lane -cin @('Signature', 'Regression')) 'PublicationFinalize identity is invalid.'
    $phaseResults = $Payload.GetProperty('phaseResults')
    Assert-True ($phaseResults.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $phaseResults.GetArrayLength() -ge 1) 'PublicationFinalize phase results are invalid.'
    foreach ($phase in $phaseResults.EnumerateArray()) {
        Assert-ExactJsonObjectProperties -Object $phase -Expected @('name', 'status', 'exitCode', 'failureCode') -Name 'PublicationFinalize phase'
        $phaseName = $phase.GetProperty('name')
        Assert-True ($phaseName.ValueKind -eq [Text.Json.JsonValueKind]::String -and $phaseName.GetString() -cmatch ("^{0}:" -f $lane.ToLowerInvariant())) 'PublicationFinalize phase name is invalid.'
        Assert-True ($phase.GetProperty('status').ValueKind -eq [Text.Json.JsonValueKind]::String -and $phase.GetProperty('status').GetString() -cin @('success', 'failure')) 'PublicationFinalize phase status is invalid.'
        Assert-True ($phase.GetProperty('exitCode').ValueKind -eq [Text.Json.JsonValueKind]::Number) 'PublicationFinalize phase exit code is invalid.'
        $phaseExitCode = $phase.GetProperty('exitCode').GetInt32()
        $phaseFailureCode = $phase.GetProperty('failureCode')
        Assert-True (($phase.GetProperty('status').GetString() -ceq 'success' -and $phaseExitCode -eq 0 -and $phaseFailureCode.ValueKind -eq [Text.Json.JsonValueKind]::Null) -or ($phase.GetProperty('status').GetString() -ceq 'failure' -and $phaseExitCode -ne 0 -and $phaseFailureCode.ValueKind -eq [Text.Json.JsonValueKind]::String -and -not [string]::IsNullOrWhiteSpace($phaseFailureCode.GetString()))) 'PublicationFinalize phase tuple is invalid.'
    }
    Assert-True (-not (Test-Path -LiteralPath $finalRoot) -and (Split-Path -Parent $candidateRoot) -ceq (Split-Path -Parent $finalRoot)) 'PublicationFinalize final root is invalid.'
    $scratchRoot = $finalRoot + '.scratch-' + [Guid]::NewGuid().ToString('N')
    $publicationFailureCode = $null
    try {
        $manifest = Get-CandidateEvidenceManifest -CandidateRoot $candidateRoot -SecretSeeds $SecretSeeds
        $failedPhases = @($phaseResults.EnumerateArray() | Where-Object { $_.GetProperty('status').GetString() -ceq 'failure' })
        $evidenceKind = Get-CandidateEvidenceKind -CandidateRoot $candidateRoot -Lane $lane
        $isSuccess = $failedPhases.Count -eq 0
        $evidencePrefix = $lane.ToLowerInvariant()
        Assert-True (($isSuccess -and $evidenceKind -ceq "$evidencePrefix-success") -or ((-not $isSuccess) -and $evidenceKind -ceq "$evidencePrefix-failure")) 'PublicationFinalize evidence kind does not match phase outcome.'
        Copy-Item -LiteralPath $candidateRoot -Destination $scratchRoot -Recurse -ErrorAction Stop
        $receipt = [ordered]@{
            schemaVersion = 1
            receiptKind = 'primary'
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = $lane
            outcome = if ($isSuccess) { 'success' } else { 'failure' }
            failurePhase = if ($isSuccess) { $null } else { $failedPhases[0].GetProperty('name').GetString() }
            failureCode = if ($isSuccess) { $null } else { $failedPhases[0].GetProperty('failureCode').GetString() }
            phases = @($phaseResults.EnumerateArray() | ForEach-Object { $_.GetRawText() | ConvertFrom-Json -AsHashtable -Depth 16 })
            evidenceManifest = $manifest
        }
        $receiptBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress))
        try {
            Assert-SanitizedBytes -Bytes $receiptBytes -SecretSeeds $SecretSeeds
            [IO.File]::WriteAllBytes((Join-Path $scratchRoot 'attempt-receipt.json'), $receiptBytes)
            $receiptManifestEntry = [ordered]@{ path = 'attempt-receipt.json'; sha256 = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($receiptBytes))).ToLowerInvariant(); byteLength = [long]$receiptBytes.Length }
        } finally {
            [Array]::Clear($receiptBytes, 0, $receiptBytes.Length)
        }
        $finalManifest = Get-CandidateEvidenceManifest -CandidateRoot $scratchRoot -SecretSeeds $SecretSeeds
        $expectedManifest = @(@($manifest) + @($receiptManifestEntry) | Sort-Object @{ Expression = { $_.path }; Ascending = $true })
        Assert-EquivalentEvidenceManifest -Expected $expectedManifest -Actual $finalManifest -Name 'PublicationFinalize scratch tree'
        Move-Item -LiteralPath $scratchRoot -Destination $finalRoot -ErrorAction Stop
        return [ordered]@{ success = $true; failureCode = $null; packages = @() }
    } catch {
        $publicationFailureCode = 'publication-integrity-failed'
    }
    if (Test-Path -LiteralPath $scratchRoot) { Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue }
    try {
        Assert-True ($publicationFailureCode -ceq 'publication-integrity-failed') 'PublicationFinalize fallback state is invalid.'
        New-Item -ItemType Directory -Path $scratchRoot -ErrorAction Stop | Out-Null
        $fallbackReceipt = [ordered]@{
            schemaVersion = 1
            receiptKind = 'safe-fallback'
            sourceSha = $sourceSha
            runAttempt = [int]$runAttempt
            lane = $lane
            outcome = 'failure'
            failureCode = $publicationFailureCode
            evidenceManifest = @()
        }
        $fallbackBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $fallbackReceipt -Depth 16 -Compress))
        try {
            Assert-SanitizedBytes -Bytes $fallbackBytes -SecretSeeds $SecretSeeds
            [IO.File]::WriteAllBytes((Join-Path $scratchRoot 'attempt-receipt.json'), $fallbackBytes)
        } finally {
            [Array]::Clear($fallbackBytes, 0, $fallbackBytes.Length)
        }
        $fallbackManifest = Get-CandidateEvidenceManifest -CandidateRoot $scratchRoot -SecretSeeds $SecretSeeds
        Assert-True ($fallbackManifest.Count -eq 1 -and $fallbackManifest[0].path -ceq 'attempt-receipt.json') 'PublicationFinalize fallback tree has an unexpected file set.'
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
            'RegressionSanitize' { Invoke-RegressionSanitizeWorker -Payload $request.Payload -SecretSeeds $request.SecretSeeds; break }
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
    $command = Get-Command pwsh -ErrorAction Stop | Select-Object -First 1
    $source = [string]$command.Source
    Assert-True (
        [IO.Path]::IsPathFullyQualified($source) -and
        ($null -eq $windowsAppsRoot -or -not $source.StartsWith($windowsAppsRoot, [StringComparison]::OrdinalIgnoreCase)) -and
        (Test-Path -LiteralPath $source -PathType Leaf)
    ) 'Closed worker requires an absolute pwsh executable.'
    return [IO.Path]::GetFullPath($source)
}

function Get-AbsoluteDotNetExecutable {
    $command = Get-Command dotnet -ErrorAction Stop | Select-Object -First 1
    $source = [string]$command.Source
    Assert-True ([IO.Path]::IsPathFullyQualified($source) -and (Test-Path -LiteralPath $source -PathType Leaf)) 'Closed worker requires an absolute dotnet executable.'
    return [IO.Path]::GetFullPath($source)
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

function New-RegressionRunRecord(
    [string]$RunId,
    [string]$ProjectPath,
    [string]$State,
    [object]$NativeExitCode,
    [string]$FailureCode = $null,
    [object]$Discovered = $null,
    [object]$Passed = $null,
    [object]$Failed = $null,
    [object]$Skipped = $null,
    [object]$DurationMs = $null,
    [string]$SkipReason = $null
) {
    Assert-True ($RunId -cin @('unit', 'headless-1', 'headless-2') -and $ProjectPath -cin @('src/Unlimotion.Test/Unlimotion.Test.csproj', 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj') -and $State -cin @('success', 'failure', 'not-attempted')) 'Regression run record identity is invalid.'
    $normalizedFailureCode = if ([string]::IsNullOrEmpty($FailureCode)) { $null } else { $FailureCode }
    $normalizedSkipReason = if ([string]::IsNullOrEmpty($SkipReason)) { $null } else { $SkipReason }
    return [ordered]@{
        runId = $RunId
        state = $State
        projectPath = $ProjectPath
        configuration = 'Debug'
        nativeExitCode = if ($null -ne $NativeExitCode) { [int]$NativeExitCode } else { $null }
        failureCode = $normalizedFailureCode
        discovered = if ($null -ne $Discovered) { [int]$Discovered } else { $null }
        passed = if ($null -ne $Passed) { [int]$Passed } else { $null }
        failed = if ($null -ne $Failed) { [int]$Failed } else { $null }
        skipped = if ($null -ne $Skipped) { [int]$Skipped } else { $null }
        durationMs = if ($null -ne $DurationMs) { [long]$DurationMs } else { $null }
        skipReason = $normalizedSkipReason
    }
}

function Invoke-TestCommandAdapter(
    [string]$RunId,
    [string]$ProjectPath,
    [string]$ProjectRelativePath,
    [string]$RawRunRoot,
    [int]$MinimumDiscovered,
    [object[]]$SecretSeeds = @(),
    [int]$TimeoutSeconds = 1200
) {
    Assert-True ($TimeoutSeconds -ge 1 -and $TimeoutSeconds -le 1200 -and $MinimumDiscovered -ge 1) 'Regression test adapter limits are invalid.'
    $rawRoot = [IO.Path]::GetFullPath($RawRunRoot)
    Assert-True ([IO.Path]::IsPathFullyQualified($ProjectPath) -and (Test-Path -LiteralPath $ProjectPath -PathType Leaf) -and -not (Test-Path -LiteralPath $rawRoot)) 'Regression test adapter paths are invalid.'
    $process = $null
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    try {
        New-Item -ItemType Directory -Path $rawRoot -ErrorAction Stop | Out-Null
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = Get-AbsoluteDotNetExecutable
        $psi.WorkingDirectory = Get-CanonicalRepositoryRoot
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        foreach ($argument in @(
                'run', '--project', $ProjectPath, '-c', 'Debug', '--no-restore', '--no-build', '--',
                '--maximum-parallel-tests', '1', '--report-trx', '--report-trx-filename', 'results.trx',
                '--report-html-filename', (Join-Path $rawRoot 'results.html'), '--results-directory', $rawRoot
            )) {
            [void]$psi.ArgumentList.Add($argument)
        }
        $psi.Environment['TUNIT_DISABLE_GITHUB_REPORTER'] = 'true'
        foreach ($name in @('GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE', 'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL', 'ACTIONS_ID_TOKEN_REQUEST_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_URL')) {
            [void]$psi.Environment.Remove($name)
        }
        foreach ($seed in $SecretSeeds) {
            Assert-True ($seed -is [System.Collections.IDictionary] -and $seed['name'] -is [string]) 'Regression test adapter secret seed is invalid.'
            [void]$psi.Environment.Remove($seed['name'])
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $psi
        Assert-True $process.Start() 'Regression test process did not start.'
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
            foreach ($streamState in @(@{ task = $stdoutTask; buffer = $stdoutBuffer; target = $stdout; maximum = 4MB; kind = 'stdout' }, @{ task = $stderrTask; buffer = $stderrBuffer; target = $stderr; maximum = 4MB; kind = 'stderr' })) {
                if ($null -eq $streamState.task -or -not $streamState.task.IsCompleted) { continue }
                $read = $streamState.task.GetAwaiter().GetResult()
                if ($streamState.kind -ceq 'stdout') { $stdoutTask = $null } else { $stderrTask = $null }
                if ($read -le 0) { continue }
                if ($streamState.target.Length + $read -gt $streamState.maximum -or $stdout.Length + $stderr.Length + $read -gt 8MB) {
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
                if (-not (Stop-ClosedWorkerProcess -Process $process)) {
                    return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-1) -FailureCode 'native-command-termination-unproven'
                }
            }
        }
        if (-not $process.HasExited -and -not $process.WaitForExit(10000)) {
            $terminationProven = Stop-ClosedWorkerProcess -Process $process
            if ($terminationProven) {
                return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-1) -FailureCode 'native-command-timeout'
            }
            return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-1) -FailureCode 'native-command-termination-unproven'
        }
        if ($overflowed) { return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-3) -FailureCode 'native-output-limit-exceeded' }
        if ($timedOut) { return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-1) -FailureCode 'native-command-timeout' }
        $nativeExitCode = [int]$process.ExitCode
        if ($nativeExitCode -ne 0) { return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode $nativeExitCode -FailureCode 'test-command-failed' }
        try {
            $reports = Get-ValidatedRawTUnitReports -RawRunRoot $rawRoot
            $summary = Read-RawTUnitTrxSummary -TrxPath $reports.trxPath
            Assert-True ($summary.discovered -ge $MinimumDiscovered -and $summary.passed -eq $summary.discovered -and $summary.failed -eq 0 -and $summary.skipped -eq 0) 'Raw TUnit report counts do not satisfy the regression contract.'
            return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'success' -NativeExitCode 0 -Discovered $summary.discovered -Passed $summary.passed -Failed $summary.failed -Skipped $summary.skipped -DurationMs $summary.durationMs
        } catch {
            return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode 0 -FailureCode 'test-evidence-failed'
        }
    } catch {
        $terminationProven = $true
        if ($null -ne $process) { $terminationProven = Stop-ClosedWorkerProcess -Process $process }
        if ($terminationProven) {
            return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-2) -FailureCode 'native-command-threw'
        }
        return New-RegressionRunRecord -RunId $RunId -ProjectPath $ProjectRelativePath -State 'failure' -NativeExitCode (-2) -FailureCode 'native-command-termination-unproven'
    } finally {
        if (Test-Path -LiteralPath $rawRoot) { Remove-Item -LiteralPath $rawRoot -Recurse -Force -ErrorAction SilentlyContinue }
        $stdout.Dispose()
        $stderr.Dispose()
        if ($null -ne $process) { $process.Dispose() }
    }
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

function Get-SignatureSanitizerProjects([string]$Root, [string]$PackagesPath, [hashtable[]]$Projects) {
    $baselinePath = Join-Path $Root 'distribution\fixtures\reactiveui-signature-chain-baseline.json'
    $fixture = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json -AsHashtable -Depth 64
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($project in $Projects) {
        $baselineProject = @($fixture.projects | Where-Object { $_.projectPath -ceq $project.path })
        Assert-True ($baselineProject.Count -eq 1) "Signature sanitizer baseline project is invalid: $($project.path)."
        $candidateProject = Get-BaselineProjectPackageSet -Root $Root -ProjectPath $project.path -PackagesPath $PackagesPath -AssetsPath $project.assetsPath
        $result.Add([ordered]@{
                id = $project.id
                projectPath = $project.path
                assetsCopyId = $project.id
                assetsSha256 = Get-Sha256 -Path $project.assetsPath
                baselineGraphSha256 = $baselineProject[0].graphSha256
                targetGraphSha256 = $candidateProject.graphSha256
            })
    }
    return ,$result.ToArray()
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

function ConvertTo-CanonicalPublicationPhases([object[]]$Phases, [string]$DefaultFailureCode) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($DefaultFailureCode)) 'Default publication failure code is invalid.'
    $canonical = [System.Collections.Generic.List[object]]::new()
    foreach ($phase in $Phases) {
        Assert-True ($phase -is [System.Collections.IDictionary]) 'Signature phase cannot be converted to a publication tuple.'
        $name = [string]$phase['name']
        $status = [string]$phase['status']
        $exitCode = [int]$phase['exitCode']
        Assert-True ($name -cmatch '^signature:' -and $status -cin @('success', 'failure')) 'Signature phase cannot be converted to a publication tuple.'
        $failureCode = $null
        if ($status -ceq 'success') {
            Assert-True ($exitCode -eq 0) 'Successful signature phase has a nonzero exit code.'
        } else {
            Assert-True ($exitCode -ne 0) 'Failed signature phase has a zero exit code.'
            if ($phase.Contains('failureCode') -and $phase['failureCode'] -is [string] -and -not [string]::IsNullOrWhiteSpace($phase['failureCode'])) {
                $failureCode = [string]$phase['failureCode']
            } else {
                $failureCode = $DefaultFailureCode
            }
        }
        $canonical.Add([ordered]@{ name = $name; status = $status; exitCode = $exitCode; failureCode = $failureCode })
    }
    Assert-True ($canonical.Count -ge 1) 'Signature publication phases cannot be empty.'
    return ,$canonical.ToArray()
}

function Get-CompletedSignatureProjects([hashtable[]]$Projects, [object[]]$Phases) {
    $completed = [System.Collections.Generic.List[object]]::new()
    foreach ($project in $Projects) {
        $assetsPhase = "signature:assets:$($project.id)"
        $succeeded = @($Phases | Where-Object { $_ -is [System.Collections.IDictionary] -and $_['name'] -ceq $assetsPhase -and $_['status'] -ceq 'success' })
        if ($succeeded.Count -eq 1) {
            $completed.Add([ordered]@{ id = $project.id })
        }
    }
    return ,$completed.ToArray()
}

function Get-AttemptedSignaturePackages([object[]]$Packages) {
    $attempted = [System.Collections.Generic.List[object]]::new()
    foreach ($package in $Packages) {
        Assert-True ($package -is [System.Collections.IDictionary]) 'Signature package cannot be converted to a failure projection.'
        $attempted.Add([ordered]@{
                id = [string]$package['id']
                version = [string]$package['version']
                nupkgSha512 = [string]$package['nupkgSha512']
                verifyExitCode = 0
            })
    }
    return ,$attempted.ToArray()
}

function Publish-SignatureFailureEvidence(
    [string]$CandidateEvidencePath,
    [string]$EvidencePath,
    [string]$SourceSha,
    [int]$Attempt,
    [object[]]$Phases,
    [object[]]$CompletedProjects,
    [object[]]$AttemptedPackages,
    [object[]]$SecretSeeds,
    [string]$DefaultFailureCode
) {
    $publicationPhases = [System.Collections.Generic.List[object]]::new()
    foreach ($phase in (ConvertTo-CanonicalPublicationPhases -Phases $Phases -DefaultFailureCode $DefaultFailureCode)) {
        $publicationPhases.Add($phase)
    }
    $failedPhases = @($publicationPhases | Where-Object { $_.status -ceq 'failure' })
    Assert-True ($failedPhases.Count -ge 1) 'Signature failure publication requires a failed phase.'
    if (Test-Path -LiteralPath $CandidateEvidencePath) {
        Remove-Item -LiteralPath $CandidateEvidencePath -Recurse -Force -ErrorAction Stop
    }

    $sanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureSanitize' -Payload ([ordered]@{
            candidateEvidenceRoot = $CandidateEvidencePath
            sourceSha = $SourceSha
            runAttempt = [string]$Attempt
            executionContext = Get-EvidenceExecutionContext
            failurePhase = $failedPhases[0].name
            completedProjects = $CompletedProjects
            attemptedPackages = $AttemptedPackages
            diagnostics = @([ordered]@{ phase = $failedPhases[0].name; code = $failedPhases[0].failureCode })
            phaseResults = $publicationPhases.ToArray()
        }) -SecretSeeds $SecretSeeds -TimeoutSeconds 300
    if ($sanitizerResult.Success -eq $true -and $sanitizerResult.ExitCode -eq 0 -and $sanitizerResult.TerminationProven -eq $true) {
        $publicationPhases.Add([ordered]@{ name = 'signature:sanitize'; status = 'success'; exitCode = 0; failureCode = $null })
    } else {
        if (Test-Path -LiteralPath $CandidateEvidencePath) {
            Remove-Item -LiteralPath $CandidateEvidencePath -Recurse -Force -ErrorAction Stop
        }
        $sanitizerExitCode = if ($sanitizerResult.ExitCode -eq 0) { 2 } else { [int]$sanitizerResult.ExitCode }
        $sanitizerFailureCode = if ($sanitizerResult.FailureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($sanitizerResult.FailureCode)) { $sanitizerResult.FailureCode } else { 'signature-sanitizer-failed' }
        $publicationPhases.Add([ordered]@{ name = 'signature:sanitize'; status = 'failure'; exitCode = $sanitizerExitCode; failureCode = $sanitizerFailureCode })
    }

    $finalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
            candidateEvidenceRoot = $CandidateEvidencePath
            finalEvidenceRoot = $EvidencePath
            sourceSha = $SourceSha
            runAttempt = [string]$Attempt
            lane = 'Signature'
            phaseResults = $publicationPhases.ToArray()
        }) -SecretSeeds $SecretSeeds -TimeoutSeconds 300
    Assert-True ($finalizerResult.Success -eq $true -and $finalizerResult.ExitCode -eq 0 -and $finalizerResult.TerminationProven -eq $true -and (Test-Path -LiteralPath $EvidencePath -PathType Container)) 'Closed signature failure finalizer failed.'
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
        $sanitizerProjects = Get-SignatureSanitizerProjects -Root $root -PackagesPath $packagesPath -Projects $projects
        $candidateEvidencePath = $evidencePath + '.candidate'
        $sanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureSanitize' -Payload ([ordered]@{
                candidateEvidenceRoot = $candidateEvidencePath
                sourceSha = $sourceSha
                runAttempt = [string]$attempt
                executionContext = Get-EvidenceExecutionContext
                projects = $sanitizerProjects
                packages = $packages.ToArray()
                phaseResults = @($phases)
            }) -SecretSeeds $secretSeeds -TimeoutSeconds 300
        Assert-True ($sanitizerResult.Success -eq $true -and $sanitizerResult.ExitCode -eq 0 -and $sanitizerResult.TerminationProven -eq $true) 'Closed signature sanitizer failed.'
        $phases.Add([ordered]@{ name = 'signature:sanitize'; status = 'success'; exitCode = 0 })
        $finalizerPhases = @($phases | ForEach-Object { [ordered]@{ name = $_.name; status = $_.status; exitCode = [int]$_.exitCode; failureCode = $null } })
        $finalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                candidateEvidenceRoot = $candidateEvidencePath
                finalEvidenceRoot = $evidencePath
                sourceSha = $sourceSha
                runAttempt = [string]$attempt
                lane = 'Signature'
                phaseResults = $finalizerPhases
            }) -SecretSeeds $secretSeeds -TimeoutSeconds 300
        Assert-True ($finalizerResult.Success -eq $true -and $finalizerResult.ExitCode -eq 0 -and $finalizerResult.TerminationProven -eq $true -and (Test-Path -LiteralPath $evidencePath -PathType Container)) 'Closed publication finalizer failed.'
        Write-Output "NuGet signature evidence: $evidencePath"
    } catch {
        $attemptFailure = $_
        $failedPhases = @($phases | Where-Object { $_ -is [System.Collections.IDictionary] -and $_['status'] -ceq 'failure' })
        if ($failedPhases.Count -eq 0) {
            $phases.Add([ordered]@{ name = 'signature:orchestration'; status = 'failure'; exitCode = 2; failureCode = 'attempt-failed' })
        }
        try {
            Publish-SignatureFailureEvidence -CandidateEvidencePath ($evidencePath + '.candidate') -EvidencePath $evidencePath -SourceSha $sourceSha -Attempt $attempt -Phases $phases.ToArray() -CompletedProjects (Get-CompletedSignatureProjects -Projects $projects -Phases $phases.ToArray()) -AttemptedPackages (Get-AttemptedSignaturePackages -Packages $packages.ToArray()) -SecretSeeds $secretSeeds -DefaultFailureCode 'attempt-failed'
        } catch {
            # A finalizer failure is deliberately not replaced by a mutable legacy receipt.
        }
        throw $attemptFailure
    } finally {
        if (Test-Path -LiteralPath $stagedAssetsRoot) {
            Remove-Item -LiteralPath $stagedAssetsRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath ($evidencePath + '.candidate')) {
            Remove-Item -LiteralPath ($evidencePath + '.candidate') -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-RegressionDotNetPhase([string]$Phase, [string[]]$Arguments, [System.Collections.Generic.List[object]]$Phases) {
    try {
        & (Get-AbsoluteDotNetExecutable) @Arguments
        $exitCode = [int]$LASTEXITCODE
        if ($exitCode -eq 0) {
            $Phases.Add([ordered]@{ name = $Phase; status = 'success'; exitCode = 0; failureCode = $null })
            return $true
        }
        $Phases.Add([ordered]@{ name = $Phase; status = 'failure'; exitCode = $exitCode; failureCode = 'regression-command-failed' })
        return $false
    } catch {
        $Phases.Add([ordered]@{ name = $Phase; status = 'failure'; exitCode = -2; failureCode = 'native-command-threw' })
        return $false
    }
}

function Add-RegressionTestPhase([System.Collections.Generic.List[object]]$Phases, [hashtable]$Run) {
    Assert-True ($Run.state -cin @('success', 'failure')) 'Regression test phase requires an attempted run.'
    $phase = "regression:test:$($Run.runId)"
    if ($Run.state -ceq 'success') {
        Assert-True ($Run.nativeExitCode -eq 0 -and $Run.failureCode -eq $null) 'Successful regression run cannot produce a failed phase.'
        $Phases.Add([ordered]@{ name = $phase; status = 'success'; exitCode = 0; failureCode = $null })
        return
    }
    $exitCode = if ($Run.nativeExitCode -eq 0) { 2 } else { [int]$Run.nativeExitCode }
    Assert-True ($exitCode -ne 0 -and $Run.failureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($Run.failureCode)) 'Failed regression run cannot produce a canonical phase.'
    $Phases.Add([ordered]@{ name = $phase; status = 'failure'; exitCode = $exitCode; failureCode = $Run.failureCode })
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
    $secretSeeds = Get-ClosedSecretSeedSnapshot -Environment ([Environment]::GetEnvironmentVariables('Process'))
    $env:NUGET_PACKAGES = $packagesPath

    $unitProject = Join-Path $root 'src\Unlimotion.Test\Unlimotion.Test.csproj'
    $headlessProject = Join-Path $root 'tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj'
    $rawReportsRoot = $evidencePath + '.raw'
    $candidateEvidencePath = $evidencePath + '.candidate'
    Assert-True (-not (Test-Path -LiteralPath $evidencePath) -and -not (Test-Path -LiteralPath $rawReportsRoot) -and -not (Test-Path -LiteralPath $candidateEvidencePath)) 'Regression evidence paths must be absent before the attempt.'
    $unitRestored = Invoke-RegressionDotNetPhase -Phase 'regression:restore:unit' -Arguments @(
        'restore', $unitProject, '--force', '--no-http-cache',
        '--configfile', (Join-Path $root 'src\nuget.config'),
        '-p:Configuration=Debug',
        '-p:DisableImplicitLibraryPacksFolder=true',
        '-p:DisableImplicitNuGetFallbackFolder=true',
        '-p:RestoreFallbackFolders='
    ) -Phases $phases
    $headlessRestored = Invoke-RegressionDotNetPhase -Phase 'regression:restore:headless' -Arguments @(
        'restore', $headlessProject, '--force', '--no-http-cache',
        '--configfile', (Join-Path $root 'src\nuget.config'),
        '-p:Configuration=Debug',
        '-p:DisableImplicitLibraryPacksFolder=true',
        '-p:DisableImplicitNuGetFallbackFolder=true',
        '-p:RestoreFallbackFolders='
    ) -Phases $phases
    $unitBuilt = $false
    if ($unitRestored) {
        $unitBuilt = Invoke-RegressionDotNetPhase -Phase 'regression:build:unit' -Arguments @(
            'build', $unitProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
    }
    $headlessBuilt = $false
    if ($headlessRestored) {
        $headlessBuilt = Invoke-RegressionDotNetPhase -Phase 'regression:build:headless' -Arguments @(
            'build', $headlessProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
    }
    $runs = [System.Collections.Generic.List[object]]::new()
    try {
        if ($unitBuilt) {
            $unitRun = Invoke-TestCommandAdapter -RunId 'unit' -ProjectPath $unitProject -ProjectRelativePath 'src/Unlimotion.Test/Unlimotion.Test.csproj' -RawRunRoot (Join-Path $rawReportsRoot 'unit') -MinimumDiscovered 830 -SecretSeeds $secretSeeds -TimeoutSeconds 1200
            $runs.Add($unitRun)
            Add-RegressionTestPhase -Phases $phases -Run $unitRun
        } else {
            $runs.Add((New-RegressionRunRecord -RunId 'unit' -ProjectPath 'src/Unlimotion.Test/Unlimotion.Test.csproj' -State 'not-attempted' -NativeExitCode $null -SkipReason 'prerequisite-failed'))
        }
        foreach ($headlessRunId in @('headless-1', 'headless-2')) {
            if ($headlessBuilt) {
                $headlessRun = Invoke-TestCommandAdapter -RunId $headlessRunId -ProjectPath $headlessProject -ProjectRelativePath 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj' -RawRunRoot (Join-Path $rawReportsRoot $headlessRunId) -MinimumDiscovered 36 -SecretSeeds $secretSeeds -TimeoutSeconds 600
                $runs.Add($headlessRun)
                Add-RegressionTestPhase -Phases $phases -Run $headlessRun
            } else {
                $runs.Add((New-RegressionRunRecord -RunId $headlessRunId -ProjectPath 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj' -State 'not-attempted' -NativeExitCode $null -SkipReason 'prerequisite-failed'))
            }
        }
    } finally {
        if (Test-Path -LiteralPath $rawReportsRoot) { Remove-Item -LiteralPath $rawReportsRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $sanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'RegressionSanitize' -Payload ([ordered]@{
            candidateEvidenceRoot = $candidateEvidencePath
            sourceSha = $sourceSha
            runAttempt = [string]$attempt
            executionContext = Get-EvidenceExecutionContext
            phaseResults = $phases.ToArray()
            runs = $runs.ToArray()
        }) -SecretSeeds $secretSeeds -TimeoutSeconds 300
    $publicationPhases = [System.Collections.Generic.List[object]]::new()
    foreach ($phase in $phases) { $publicationPhases.Add($phase) }
    if ($sanitizerResult.Success -and $sanitizerResult.ExitCode -eq 0 -and $sanitizerResult.TerminationProven) {
        $publicationPhases.Add([ordered]@{ name = 'regression:sanitize'; status = 'success'; exitCode = 0; failureCode = $null })
    } else {
        $sanitizerExitCode = if ($sanitizerResult.ExitCode -eq 0) { 2 } else { [int]$sanitizerResult.ExitCode }
        $sanitizerFailureCode = if ($sanitizerResult.FailureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($sanitizerResult.FailureCode)) { $sanitizerResult.FailureCode } else { 'regression-sanitizer-failed' }
        $publicationPhases.Add([ordered]@{ name = 'regression:sanitize'; status = 'failure'; exitCode = $sanitizerExitCode; failureCode = $sanitizerFailureCode })
    }
    $finalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
            candidateEvidenceRoot = $candidateEvidencePath
            finalEvidenceRoot = $evidencePath
            sourceSha = $sourceSha
            runAttempt = [string]$attempt
            lane = 'Regression'
            phaseResults = $publicationPhases.ToArray()
        }) -SecretSeeds $secretSeeds -TimeoutSeconds 300
    Assert-True ($finalizerResult.Success -and $finalizerResult.ExitCode -eq 0 -and $finalizerResult.TerminationProven -and (Test-Path -LiteralPath $evidencePath -PathType Container)) 'Regression publication finalizer failed.'
    Write-Output "NuGet regression evidence: $evidencePath"
    if (@($publicationPhases | Where-Object { $_.status -ceq 'failure' }).Count -gt 0) {
        throw 'NuGet regression attempt recorded failed phases.'
    }
}

function Read-ValidatedFullChildReceipt([string]$ChildRoot, [string]$ChildLane, [string]$SourceSha, [int]$Attempt) {
    $root = [IO.Path]::GetFullPath($ChildRoot)
    Assert-True ((Test-Path -LiteralPath $root -PathType Container) -and -not (Get-Item -LiteralPath $root -Force).LinkType) "Full $ChildLane child evidence root is missing."
    Write-Verbose "Full $ChildLane child receipt: native identity."
    [void](Get-FullTreeNativeFileIdentityMap -TreeRoot $root)
    Write-Verbose "Full $ChildLane child receipt: independent validation."
    $validatorPath = Join-Path $PSScriptRoot 'Test-NuGetEvidencePublication.ps1'
    & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File $validatorPath -EvidenceRoot $root -ExpectedLane $ChildLane -ExpectedSourceSha $SourceSha -ExpectedRunAttempt ([string]$Attempt) -ExpectedExecutionContext 'full-child' 2>$null | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) "Full $ChildLane child evidence failed independent validation."
    Write-Verbose "Full $ChildLane child receipt: parse and bind."
    $receiptPath = Join-Path $root 'attempt-receipt.json'
    $receipt = [IO.File]::ReadAllText($receiptPath, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 32
    Assert-True ($receipt.receiptKind -ceq 'primary' -and $receipt.lane -ceq $ChildLane -and $receipt.sourceSha -ceq $SourceSha -and $receipt.runAttempt -eq $Attempt -and $receipt.outcome -cin @('success', 'failure')) "Full $ChildLane child receipt is not a validated primary receipt."
    return $receipt
}

function Publish-FullSafeFallback([string]$EvidencePath, [string]$SourceSha, [int]$Attempt, [object[]]$SecretSeeds) {
    Assert-True (-not (Test-Path -LiteralPath $EvidencePath)) 'Full fallback root must be absent.'
    $seedElements = ConvertTo-SecretSeedElements -SecretSeeds $SecretSeeds
    $scratchRoot = $EvidencePath + '.scratch-' + [Guid]::NewGuid().ToString('N')
    try {
        New-Item -ItemType Directory -Path $scratchRoot -ErrorAction Stop | Out-Null
        $receipt = [ordered]@{
            schemaVersion = 1
            receiptKind = 'safe-fallback'
            sourceSha = $SourceSha
            runAttempt = $Attempt
            lane = 'Full'
            outcome = 'failure'
            failureCode = 'publication-integrity-failed'
            evidenceManifest = @()
        }
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress))
        try {
            Assert-SanitizedBytes -Bytes $bytes -SecretSeeds $seedElements
            [IO.File]::WriteAllBytes((Join-Path $scratchRoot 'attempt-receipt.json'), $bytes)
        } finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        Move-Item -LiteralPath $scratchRoot -Destination $EvidencePath -ErrorAction Stop
    } catch {
        if (Test-Path -LiteralPath $scratchRoot) { Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Assert-FullPrimaryChildReceipts([hashtable]$SignatureReceipt, [hashtable]$RegressionReceipt) {
    Assert-True ($SignatureReceipt.receiptKind -ceq 'primary' -and $SignatureReceipt.lane -ceq 'Signature' -and $SignatureReceipt.outcome -cin @('success', 'failure')) 'Full Signature child receipt cannot be a fallback.'
    Assert-True ($RegressionReceipt.receiptKind -ceq 'primary' -and $RegressionReceipt.lane -ceq 'Regression' -and $RegressionReceipt.outcome -cin @('success', 'failure')) 'Full Regression child receipt cannot be a fallback.'
}

function Publish-FullPrimaryEvidence(
    [string]$CandidateRoot,
    [string]$EvidencePath,
    [string]$SourceSha,
    [int]$Attempt,
    [hashtable]$SignatureReceipt,
    [hashtable]$RegressionReceipt,
    [object[]]$SecretSeeds
) {
    Assert-FullRootsDoNotOverlap -Roots @{ candidate = [IO.Path]::GetFullPath($CandidateRoot); final = [IO.Path]::GetFullPath($EvidencePath) }
    Assert-True ((Test-Path -LiteralPath $CandidateRoot -PathType Container) -and -not (Test-Path -LiteralPath $EvidencePath)) 'Full publication roots are invalid.'
    Assert-FullPrimaryChildReceipts -SignatureReceipt $SignatureReceipt -RegressionReceipt $RegressionReceipt
    $seedElements = ConvertTo-SecretSeedElements -SecretSeeds $SecretSeeds
    try {
        $manifest = Get-CandidateEvidenceManifest -CandidateRoot $CandidateRoot -SecretSeeds $seedElements
        [void](Get-FullTreeNativeFileIdentityMap -TreeRoot $CandidateRoot)
        foreach ($entry in $manifest) {
            Assert-True ($entry.path -cmatch '^(signature|regression)/') 'Full candidate has an unexpected evidence path.'
        }
        $children = @(
            [ordered]@{
                lane = 'Signature'
                relativeRoot = 'signature'
                receiptSha256 = Get-Sha256 -Path (Join-Path $CandidateRoot 'signature\attempt-receipt.json')
                outcome = $SignatureReceipt.outcome
                failureCode = $SignatureReceipt.failureCode
            },
            [ordered]@{
                lane = 'Regression'
                relativeRoot = 'regression'
                receiptSha256 = Get-Sha256 -Path (Join-Path $CandidateRoot 'regression\attempt-receipt.json')
                outcome = $RegressionReceipt.outcome
                failureCode = $RegressionReceipt.failureCode
            }
        )
        $firstFailedChild = @($children | Where-Object { $_.outcome -ceq 'failure' } | Select-Object -First 1)
        $receipt = [ordered]@{
            schemaVersion = 1
            receiptKind = 'full-primary'
            sourceSha = $SourceSha
            runAttempt = $Attempt
            lane = 'Full'
            runtime = Get-SanitizedRuntime -EvidenceContext 'local' -SignatureAuthoritative $false
            outcome = if ($firstFailedChild.Count -eq 0) { 'success' } else { 'failure' }
            failureCode = if ($firstFailedChild.Count -eq 0) { $null } else { $firstFailedChild[0].failureCode }
            childAttempts = $children
            evidenceManifest = $manifest
        }
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress))
        try {
            Assert-SanitizedBytes -Bytes $bytes -SecretSeeds $seedElements
            [IO.File]::WriteAllBytes((Join-Path $CandidateRoot 'attempt-receipt.json'), $bytes)
            $receiptEntry = [ordered]@{ path = 'attempt-receipt.json'; sha256 = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant(); byteLength = [long]$bytes.Length }
        } finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        $finalManifest = Get-CandidateEvidenceManifest -CandidateRoot $CandidateRoot -SecretSeeds $seedElements
        $expectedManifest = @(@($manifest) + @($receiptEntry) | Sort-Object @{ Expression = { $_.path }; Ascending = $true })
        Assert-EquivalentEvidenceManifest -Expected $expectedManifest -Actual $finalManifest -Name 'Full publication tree'
        Move-Item -LiteralPath $CandidateRoot -Destination $EvidencePath -ErrorAction Stop
    } catch {
        if (Test-Path -LiteralPath $CandidateRoot) { Remove-Item -LiteralPath $CandidateRoot -Recurse -Force -ErrorAction SilentlyContinue }
        Publish-FullSafeFallback -EvidencePath $EvidencePath -SourceSha $SourceSha -Attempt $Attempt -SecretSeeds $SecretSeeds
    }
}

function Copy-FullChildEvidenceToCandidate([string]$ChildRoot, [string]$CandidateChildRoot) {
    Assert-FullRootsDoNotOverlap -Roots @{ child = [IO.Path]::GetFullPath($ChildRoot); candidate = [IO.Path]::GetFullPath($CandidateChildRoot) }
    Assert-True ((Test-Path -LiteralPath $ChildRoot -PathType Container) -and -not (Test-Path -LiteralPath $CandidateChildRoot)) 'Full child copy roots are invalid.'
    [void](Get-FullTreeNativeFileIdentityMap -TreeRoot $ChildRoot)
    Copy-Item -LiteralPath $ChildRoot -Destination $CandidateChildRoot -Recurse -ErrorAction Stop
    Assert-True (Test-Path -LiteralPath $CandidateChildRoot -PathType Container) 'Full child evidence copy was not created.'
    Assert-FullTreesHaveDistinctFileIdentity -LeftRoot $ChildRoot -RightRoot $CandidateChildRoot
}

function Assert-FullDeadlineBudget([DateTimeOffset]$DeadlineUtc, [int]$RequiredMinutes, [string]$Description) {
    Assert-True ($RequiredMinutes -ge 1) 'Full deadline reserve must be positive.'
    Assert-True (($DeadlineUtc - [DateTimeOffset]::UtcNow).TotalMinutes -ge $RequiredMinutes) "Full does not have enough time remaining for $Description."
}

function Invoke-FullChildProcess(
    [string]$ChildLane,
    [string]$Root,
    [string]$SourceSha,
    [int]$Attempt,
    [string]$ChildEvidenceRoot,
    [string]$ChildPackagesRoot,
    [DateTimeOffset]$OuterDeadlineUtc,
    [int]$ChildDeadlineMinutes,
    [int]$ReserveMinutes
) {
    Assert-True ($ChildDeadlineMinutes -ge 1 -and $ReserveMinutes -ge 1) 'Full child deadline inputs are invalid.'
    Assert-FullDeadlineBudget -DeadlineUtc $OuterDeadlineUtc -RequiredMinutes ($ChildDeadlineMinutes + $ReserveMinutes) -Description "$ChildLane child envelope"
    $timeoutMilliseconds = [int][Math]::Min(
        [Int32]::MaxValue,
        [Math]::Floor(([TimeSpan]::FromMinutes($ChildDeadlineMinutes)).TotalMilliseconds))
    $process = $null
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = Get-AbsolutePowerShellExecutable
        $startInfo.WorkingDirectory = $Root
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $script:WorkerScriptPath, '-Mode', 'RunAttempt', '-Lane', $ChildLane, '-RepositoryRoot', $Root, '-PackagesRoot', $ChildPackagesRoot, '-ExpectedSourceSha', $SourceSha, '-RunAttempt', ([string]$Attempt), '-EvidenceRoot', $ChildEvidenceRoot, '-FullChild')) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        Assert-True $process.Start() "Full $ChildLane child process did not start."
        $stdoutDrain = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
        $stderrDrain = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        if (-not $process.WaitForExit($timeoutMilliseconds)) {
            $process.Kill($true)
            Assert-True $process.WaitForExit(10000) "Full $ChildLane child process termination was not proven."
            Assert-True ($stdoutDrain.Wait(10000) -and $stderrDrain.Wait(10000)) "Full $ChildLane child stream drain was not proven."
            $stdoutDrain.GetAwaiter().GetResult()
            $stderrDrain.GetAwaiter().GetResult()
            throw "Full $ChildLane child exceeded its declared deadline."
        }
        [void]$process.WaitForExit()
        Assert-True ($stdoutDrain.Wait(10000) -and $stderrDrain.Wait(10000)) "Full $ChildLane child stream drain was not proven."
        $stdoutDrain.GetAwaiter().GetResult()
        $stderrDrain.GetAwaiter().GetResult()
        return [int]$process.ExitCode
    } finally {
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Invoke-FullChildAttempt([string]$ChildLane, [string]$Root, [string]$SourceSha, [int]$Attempt, [string]$ChildWorkRoot, [DateTimeOffset]$OuterDeadlineUtc, [int]$ChildDeadlineMinutes, [int]$ReserveMinutes) {
    $workRoot = [IO.Path]::GetFullPath($ChildWorkRoot)
    $sourceParentRoot = Join-Path ([IO.Path]::GetPathRoot($workRoot)) 'uf'
    $sourceRoot = Join-Path $sourceParentRoot ([Guid]::NewGuid().ToString('N'))
    $childEvidenceRoot = Join-Path $workRoot 'final'
    $childPackagesRoot = Join-Path $workRoot 'packages'
    Assert-True ($ChildLane -cin @('Signature', 'Regression') -and [IO.Path]::GetFileName($workRoot) -ceq $ChildLane.ToLowerInvariant()) 'Full child work root grammar is invalid.'
    Assert-FullRootsDoNotOverlap -Roots @{ repository = [IO.Path]::GetFullPath($Root); source = [IO.Path]::GetFullPath($sourceRoot); evidence = [IO.Path]::GetFullPath($childEvidenceRoot); packages = [IO.Path]::GetFullPath($childPackagesRoot) }
    Assert-True (-not (Test-Path -LiteralPath $workRoot)) "Full $ChildLane child work root must be absent."
    New-Item -ItemType Directory -Path $workRoot, $childPackagesRoot, $sourceParentRoot -Force -ErrorAction Stop | Out-Null
    Assert-True (-not (Get-Item -LiteralPath $workRoot -Force).LinkType -and -not (Get-Item -LiteralPath $childPackagesRoot -Force).LinkType -and -not (Get-Item -LiteralPath $sourceParentRoot -Force).LinkType -and -not (Test-Path -LiteralPath $childEvidenceRoot) -and -not (Test-Path -LiteralPath $sourceRoot)) "Full $ChildLane source worktree root is invalid."
    try {
        Write-Verbose "Full $ChildLane child: create source worktree."
        & git -C $Root worktree add --detach --force $sourceRoot $SourceSha *> $null
        Assert-True ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sourceRoot -PathType Container) -and -not (Get-Item -LiteralPath $sourceRoot -Force).LinkType) "Full $ChildLane child source worktree could not be created."
        Write-Verbose "Full $ChildLane child: execute isolated lane."
        $childExitCode = Invoke-FullChildProcess -ChildLane $ChildLane -Root $sourceRoot -SourceSha $SourceSha -Attempt $Attempt -ChildEvidenceRoot $childEvidenceRoot -ChildPackagesRoot $childPackagesRoot -OuterDeadlineUtc $OuterDeadlineUtc -ChildDeadlineMinutes $ChildDeadlineMinutes -ReserveMinutes $ReserveMinutes
        Write-Verbose "Full $ChildLane child: read receipt."
        $receipt = Read-ValidatedFullChildReceipt -ChildRoot $ChildEvidenceRoot -ChildLane $ChildLane -SourceSha $SourceSha -Attempt $Attempt
        Assert-True (($childExitCode -eq 0) -eq ($receipt.outcome -ceq 'success')) "Full $ChildLane child exit code does not match its receipt outcome."
        return [ordered]@{ exitCode = $childExitCode; evidenceRoot = $childEvidenceRoot; receipt = $receipt }
    } finally {
        if (Test-Path -LiteralPath $sourceRoot -PathType Container) {
            Write-Verbose "Full $ChildLane child: remove source worktree."
            & git -C $Root worktree remove --force $sourceRoot *> $null
            Assert-True ($LASTEXITCODE -eq 0 -and -not (Test-Path -LiteralPath $sourceRoot)) "Full $ChildLane child source worktree could not be removed."
        }
    }
}

function Invoke-FullAttempt {
    Assert-True ($Lane -ceq 'Full' -and -not $FullChild) 'Full must be an outer local wrapper.'
    Assert-True ($IsWindows -and $null -eq [Environment]::GetEnvironmentVariable('GITHUB_ACTIONS', 'Process')) 'Full is allowed only as a local Windows diagnostic wrapper.'
    Assert-True ($env:DOTNET_NUGET_SIGNATURE_VERIFICATION -ceq 'true') 'DOTNET_NUGET_SIGNATURE_VERIFICATION must be exactly true.'
    Assert-True ([string]::IsNullOrEmpty($env:NUGET_CERT_REVOCATION_MODE) -or $env:NUGET_CERT_REVOCATION_MODE -ceq 'online') 'NUGET_CERT_REVOCATION_MODE must be absent or exactly online.'

    $root = Get-CanonicalRepositoryRoot
    $sourceSha = Resolve-SourceSha -Root $root
    $attempt = Resolve-RunAttempt
    $head = (& git -C $root rev-parse HEAD).Trim()
    $sourceStatus = & git -C $root status --porcelain
    Assert-True ($LASTEXITCODE -eq 0 -and $head -ceq $sourceSha -and [string]::IsNullOrWhiteSpace(($sourceStatus -join [Environment]::NewLine))) 'Full requires a clean checkout at the exact expected source SHA.'
    $evidencePath = Get-EvidenceRoot -Root $root -SourceSha $sourceSha -Attempt $attempt
    $candidateRoot = $evidencePath + '.candidate'
    $workRoot = $evidencePath + '.work'
    Assert-FullRootsDoNotOverlap -Roots @{ repository = $root; final = [IO.Path]::GetFullPath($evidencePath); candidate = [IO.Path]::GetFullPath($candidateRoot); work = [IO.Path]::GetFullPath($workRoot) }
    Assert-True (-not (Test-Path -LiteralPath $evidencePath) -and -not (Test-Path -LiteralPath $candidateRoot) -and -not (Test-Path -LiteralPath $workRoot)) 'Full evidence roots must be absent before the attempt.'
    $secretSeeds = Get-ClosedSecretSeedSnapshot -Environment ([Environment]::GetEnvironmentVariables('Process'))
    New-Item -ItemType Directory -Path $candidateRoot, $workRoot -ErrorAction Stop | Out-Null
    Assert-True (-not (Get-Item -LiteralPath $candidateRoot -Force).LinkType -and -not (Get-Item -LiteralPath $workRoot -Force).LinkType) 'Full outer work roots cannot be links.'
    $fullDeadlineUtc = [DateTimeOffset]::UtcNow.AddMinutes(175)

    $signatureReceipt = $null
    $regressionReceipt = $null
    $fullStage = 'signature-child'
    try {
        $signature = Invoke-FullChildAttempt -ChildLane 'Signature' -Root $root -SourceSha $sourceSha -Attempt $attempt -ChildWorkRoot (Join-Path $workRoot 'signature') -OuterDeadlineUtc $fullDeadlineUtc -ChildDeadlineMinutes 65 -ReserveMinutes 105
        $signatureReceipt = $signature.receipt
        $fullStage = 'signature-copy'
        Copy-FullChildEvidenceToCandidate -ChildRoot $signature.evidenceRoot -CandidateChildRoot (Join-Path $candidateRoot 'signature')
        $fullStage = 'regression-child'
        $regression = Invoke-FullChildAttempt -ChildLane 'Regression' -Root $root -SourceSha $sourceSha -Attempt $attempt -ChildWorkRoot (Join-Path $workRoot 'regression') -OuterDeadlineUtc $fullDeadlineUtc -ChildDeadlineMinutes 95 -ReserveMinutes 10
        $regressionReceipt = $regression.receipt
        $fullStage = 'regression-copy'
        Copy-FullChildEvidenceToCandidate -ChildRoot $regression.evidenceRoot -CandidateChildRoot (Join-Path $candidateRoot 'regression')
        $fullStage = 'aggregation-reserve'
        Assert-FullDeadlineBudget -DeadlineUtc $fullDeadlineUtc -RequiredMinutes 10 -Description 'outer aggregation and final validation'
        $fullStage = 'publication'
        Publish-FullPrimaryEvidence -CandidateRoot $candidateRoot -EvidencePath $evidencePath -SourceSha $sourceSha -Attempt $attempt -SignatureReceipt $signatureReceipt -RegressionReceipt $regressionReceipt -SecretSeeds $secretSeeds
    } catch {
        Write-Verbose "Full outer fallback stage: $fullStage."
        if (Test-Path -LiteralPath $candidateRoot) { Remove-Item -LiteralPath $candidateRoot -Recurse -Force -ErrorAction SilentlyContinue }
        if (-not (Test-Path -LiteralPath $evidencePath)) {
            Publish-FullSafeFallback -EvidencePath $evidencePath -SourceSha $sourceSha -Attempt $attempt -SecretSeeds $secretSeeds
        }
    } finally {
        if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-NuGetEvidencePublication.ps1') -EvidenceRoot $evidencePath -ExpectedLane Full -ExpectedSourceSha $sourceSha -ExpectedRunAttempt ([string]$attempt) 2>$null | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'Full outer evidence failed independent validation.'
    $receipt = [IO.File]::ReadAllText((Join-Path $evidencePath 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 32
    Write-Output "NuGet Full evidence: $evidencePath"
    if ($receipt.outcome -cne 'success') { throw 'NuGet Full attempt recorded failed child evidence.' }
}

function Invoke-SelfTest {
    Assert-True ((Get-ExpectedPackages).Count -eq 6) 'Expected signed subset changed.'
    Assert-True ('Signature' -ceq 'Signature') 'Case-sensitive lane comparison changed.'
    $zeroExitRun = New-RegressionRunRecord -RunId 'unit' -ProjectPath 'src/Unlimotion.Test/Unlimotion.Test.csproj' -State 'success' -NativeExitCode 0 -Discovered 830 -Passed 830 -Failed 0 -Skipped 0 -DurationMs 1
    $notAttemptedRun = New-RegressionRunRecord -RunId 'headless-1' -ProjectPath 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj' -State 'not-attempted' -NativeExitCode $null -SkipReason 'prerequisite-failed'
    $zeroExitPhases = [System.Collections.Generic.List[object]]::new()
    Add-RegressionTestPhase -Phases $zeroExitPhases -Run $zeroExitRun
    Assert-True ($zeroExitRun.nativeExitCode -eq 0 -and $zeroExitRun.failureCode -eq $null -and $zeroExitRun.skipReason -eq $null -and $zeroExitRun.discovered -eq 830 -and $zeroExitPhases.Count -eq 1 -and $zeroExitPhases[0].status -ceq 'success' -and $notAttemptedRun.nativeExitCode -eq $null -and $notAttemptedRun.skipReason -ceq 'prerequisite-failed') 'Regression run record did not retain zero and null native exit values.'

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

    $manifestMutationRejected = $false
    try {
        Assert-EquivalentEvidenceManifest -Expected @([ordered]@{ path = 'signature/evidence.json'; sha256 = ('a' * 64); byteLength = 1 }) -Actual @([ordered]@{ path = 'signature/evidence.json'; sha256 = ('b' * 64); byteLength = 1 }) -Name 'Synthetic manifest'
    } catch {
        $manifestMutationRejected = $true
    }
    Assert-True $manifestMutationRejected 'Publication manifest comparison accepted a byte mutation.'

    $rawReportRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-raw-tunit-report-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $rawReportRoot -ErrorAction Stop | Out-Null
        $rawHtml = '<!doctype html><html><body>sanitized later</body></html>'
        $rawTrx = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results><UnitTestResult executionId="00000000-0000-0000-0000-000000000001" testId="00000000-0000-0000-0000-000000000002" testName="Synthetic" computerName="synthetic" duration="00:00:00.1250000" startTime="2026-01-01T00:00:00.0000000+00:00" endTime="2026-01-01T00:00:00.1250000+00:00" testType="00000000-0000-0000-0000-000000000003" outcome="Passed" testListId="00000000-0000-0000-0000-000000000004" relativeResultsDirectory="one" /></Results>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="1" inProgress="0" pending="0" /></ResultSummary>
</TestRun>
'@
        [IO.File]::WriteAllText((Join-Path $rawReportRoot 'results.html'), $rawHtml, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $rawReportRoot 'results.trx'), $rawTrx, [Text.UTF8Encoding]::new($false))
        $shadowRoot = Join-Path $rawReportRoot 'runner-1\In\machine-1'
        New-Item -ItemType Directory -Path $shadowRoot -ErrorAction Stop | Out-Null
        [IO.File]::WriteAllText((Join-Path $shadowRoot 'results.html'), $rawHtml, [Text.UTF8Encoding]::new($false))
        $validatedRawReports = Get-ValidatedRawTUnitReports -RawRunRoot $rawReportRoot
        $rawSummary = Read-RawTUnitTrxSummary -TrxPath $validatedRawReports.trxPath
        Assert-True ($validatedRawReports.htmlPath -ceq (Join-Path $rawReportRoot 'results.html') -and $rawSummary.discovered -eq 1 -and $rawSummary.passed -eq 1 -and $rawSummary.failed -eq 0 -and $rawSummary.skipped -eq 0 -and $rawSummary.durationMs -eq 125) 'Raw TUnit report adapter did not preserve a valid report tuple.'
        [IO.File]::WriteAllText((Join-Path $shadowRoot 'results.html'), $rawHtml + 'changed', [Text.UTF8Encoding]::new($false))
        $mutatedShadowRejected = $false
        try {
            [void](Get-ValidatedRawTUnitReports -RawRunRoot $rawReportRoot)
        } catch {
            $mutatedShadowRejected = $true
        }
        Assert-True $mutatedShadowRejected 'Raw TUnit report adapter accepted a mutated shadow report.'
    } finally {
        if (Test-Path -LiteralPath $rawReportRoot) { Remove-Item -LiteralPath $rawReportRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

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
        Assert-FullDeadlineBudget -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(2)) -RequiredMinutes 1 -Description 'self-test child reserve'
        $deadlineRejected = $false
        try {
            Assert-FullDeadlineBudget -DeadlineUtc ([DateTimeOffset]::UtcNow) -RequiredMinutes 1 -Description 'self-test expired reserve'
        } catch {
            $deadlineRejected = $true
        }
        Assert-True $deadlineRejected 'Full deadline budget accepted an expired reserve.'
        $overlappingRootsRejected = $false
        $rootLayoutFixture = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-full-layout-' + [Guid]::NewGuid().ToString('N'))
        try {
            Assert-FullRootsDoNotOverlap -Roots @{ outer = $rootLayoutFixture; nested = (Join-Path $rootLayoutFixture 'nested') }
        } catch {
            $overlappingRootsRejected = $true
        }
        Assert-True $overlappingRootsRejected 'Full root layout accepted an overlapping child root.'
        if ($IsWindows) {
            $nativeIdentityFixture = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-full-identity-' + [Guid]::NewGuid().ToString('N'))
            try {
                $leftRoot = Join-Path $nativeIdentityFixture 'left'
                $rightRoot = Join-Path $nativeIdentityFixture 'right'
                New-Item -ItemType Directory -Path $leftRoot, $rightRoot -ErrorAction Stop | Out-Null
                [IO.File]::WriteAllText((Join-Path $leftRoot 'one.txt'), 'one', [Text.UTF8Encoding]::new($false))
                [IO.File]::WriteAllText((Join-Path $rightRoot 'two.txt'), 'two', [Text.UTF8Encoding]::new($false))
                Assert-FullTreesHaveDistinctFileIdentity -LeftRoot $leftRoot -RightRoot $rightRoot
                New-Item -ItemType HardLink -Path (Join-Path $leftRoot 'two.txt') -Target (Join-Path $leftRoot 'one.txt') -ErrorAction Stop | Out-Null
                $hardLinkRejected = $false
                try {
                    [void](Get-FullTreeNativeFileIdentityMap -TreeRoot $leftRoot)
                } catch {
                    $hardLinkRejected = $true
                }
                Assert-True $hardLinkRejected 'Full native identity accepted a hard-linked file.'
            } finally {
                if (Test-Path -LiteralPath $nativeIdentityFixture) { Remove-Item -LiteralPath $nativeIdentityFixture -Recurse -Force -ErrorAction SilentlyContinue }
            }
        }
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
                    executionContext = 'full-child'
                    projects = $sanitizerProjects
                    packages = $sanitizerPackages
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0; failureCode = $null })
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
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'success'; exitCode = 0; failureCode = $null })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($finalizerResult.Success -eq $true -and $finalizerResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $finalizerRoot)) 'Publication finalizer worker did not publish the primary tree.'
            $finalFiles = @(Get-ChildItem -LiteralPath $finalizerRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($finalizerRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($finalFiles.Count -eq 8 -and $finalFiles[0] -ceq 'attempt-receipt.json') 'Publication finalizer tree has an invalid file set.'
            $finalReceipt = [IO.File]::ReadAllText((Join-Path $finalizerRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($finalReceipt.failurePhase -eq $null -and $finalReceipt.failureCode -eq $null -and (@($finalReceipt.evidenceManifest)).Count -eq 7 -and (@($finalReceipt.evidenceManifest | Where-Object { $_.path -ceq 'attempt-receipt.json' })).Count -eq 0) 'Publication finalizer receipt self-hashed or lost candidate files.'
            $phaseFallbackRoot = $sanitizerRoot + '-phase-fallback'
            $phaseFallbackResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $sanitizerRoot
                    finalEvidenceRoot = $phaseFallbackRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Signature'
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'failure'; exitCode = 1; failureCode = 'signature-verification-failed' })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($phaseFallbackResult.Success -eq $true -and $phaseFallbackResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $phaseFallbackRoot)) 'Publication finalizer worker did not convert a failed phase into fallback evidence.'
            $phaseFallbackFiles = @(Get-ChildItem -LiteralPath $phaseFallbackRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($phaseFallbackRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($phaseFallbackFiles.Count -eq 1 -and $phaseFallbackFiles[0] -ceq 'attempt-receipt.json') 'Publication finalizer phase fallback tree has an invalid file set.'
            Remove-Item -LiteralPath $phaseFallbackRoot -Recurse -Force -ErrorAction Stop
            $fallbackRoot = $sanitizerRoot + '-fallback'
            $fallbackResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $sanitizerRoot + '-missing'
                    finalEvidenceRoot = $fallbackRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Signature'
                    phaseResults = @([ordered]@{ name = 'signature:verify:graph'; status = 'failure'; exitCode = 1; failureCode = 'signature-verification-failed' })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($fallbackResult.Success -eq $true -and $fallbackResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $fallbackRoot)) 'Publication finalizer worker did not publish the fallback tree.'
            $fallbackFiles = @(Get-ChildItem -LiteralPath $fallbackRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($fallbackRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($fallbackFiles.Count -eq 1 -and $fallbackFiles[0] -ceq 'attempt-receipt.json') 'Publication finalizer fallback tree has an invalid file set.'
            $fallbackReceipt = [IO.File]::ReadAllText((Join-Path $fallbackRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($fallbackReceipt.receiptKind -ceq 'safe-fallback' -and $fallbackReceipt.failureCode -ceq 'publication-integrity-failed' -and (@($fallbackReceipt.evidenceManifest)).Count -eq 0) 'Publication finalizer fallback receipt is invalid.'
            Remove-Item -LiteralPath $fallbackRoot -Recurse -Force -ErrorAction Stop
            $failureSanitizerRoot = $sanitizerRoot + '-failure-candidate'
            $failureSanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'SignatureSanitize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $failureSanitizerRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    executionContext = 'full-child'
                    failurePhase = 'signature:verify:ReactiveUI.Avalonia'
                    completedProjects = @()
                    attemptedPackages = @([ordered]@{ id = 'ReactiveUI.Avalonia'; version = '12.0.2'; nupkgSha512 = ('a' * 128); verifyExitCode = 1 })
                    diagnostics = @([ordered]@{ phase = 'signature:verify:ReactiveUI.Avalonia'; code = 'signature-verification-failed' })
                    phaseResults = @([ordered]@{ name = 'signature:verify:ReactiveUI.Avalonia'; status = 'failure'; exitCode = 1 })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($failureSanitizerResult.Success -eq $true -and $failureSanitizerResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $failureSanitizerRoot)) 'Signature failure sanitizer did not return a closed success tuple.'
            $failureCandidateFiles = @(Get-ChildItem -LiteralPath $failureSanitizerRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($failureSanitizerRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($failureCandidateFiles.Count -eq 2 -and $failureCandidateFiles[0] -ceq 'signature/evidence.json' -and $failureCandidateFiles[1] -ceq 'signature/verify/ReactiveUI.Avalonia.log') 'Signature failure sanitizer candidate file set is invalid.'
            $failureEvidence = [IO.File]::ReadAllText((Join-Path $failureSanitizerRoot 'signature\evidence.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($failureEvidence.evidenceKind -ceq 'signature-failure' -and $failureEvidence.failurePhase -ceq 'signature:verify:ReactiveUI.Avalonia' -and (@($failureEvidence.attemptedPackages)).Count -eq 1 -and -not $failureEvidence.ContainsKey('projects') -and -not $failureEvidence.ContainsKey('packages')) 'Signature failure sanitizer evidence shape is invalid.'
            $failurePrimaryRoot = $failureSanitizerRoot + '-final'
            $failurePrimaryResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $failureSanitizerRoot
                    finalEvidenceRoot = $failurePrimaryRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Signature'
                    phaseResults = @([ordered]@{ name = 'signature:verify:ReactiveUI.Avalonia'; status = 'failure'; exitCode = 1; failureCode = 'signature-verification-failed' })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($failurePrimaryResult.Success -eq $true -and $failurePrimaryResult.ExitCode -eq 0 -and (Test-Path -LiteralPath $failurePrimaryRoot)) 'Publication finalizer did not publish signature failure evidence.'
            $failurePrimaryFiles = @(Get-ChildItem -LiteralPath $failurePrimaryRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($failurePrimaryRoot, $_.FullName) -replace '\\', '/' } | Sort-Object)
            Assert-True ($failurePrimaryFiles.Count -eq 3 -and $failurePrimaryFiles[0] -ceq 'attempt-receipt.json') 'Publication finalizer signature failure tree has an invalid file set.'
            $failurePrimaryReceipt = [IO.File]::ReadAllText((Join-Path $failurePrimaryRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($failurePrimaryReceipt.outcome -ceq 'failure' -and $failurePrimaryReceipt.failurePhase -ceq 'signature:verify:ReactiveUI.Avalonia' -and $failurePrimaryReceipt.failureCode -ceq 'signature-verification-failed') 'Publication finalizer signature failure receipt is invalid.'
            $integratedFailureRoot = $sanitizerRoot + '-integrated-failure-final'
            $integratedFailureCandidate = $integratedFailureRoot + '.candidate'
            Publish-SignatureFailureEvidence -CandidateEvidencePath $integratedFailureCandidate -EvidencePath $integratedFailureRoot -SourceSha ('a' * 40) -Attempt 1 -Phases @([ordered]@{ name = 'signature:verify:worker'; status = 'failure'; exitCode = -2; failureCode = 'native-command-threw' }) -CompletedProjects @() -AttemptedPackages @() -SecretSeeds $syntheticSeeds -DefaultFailureCode 'attempt-failed'
            $integratedFailureReceipt = [IO.File]::ReadAllText((Join-Path $integratedFailureRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($integratedFailureReceipt.receiptKind -ceq 'primary' -and $integratedFailureReceipt.outcome -ceq 'failure' -and $integratedFailureReceipt.failurePhase -ceq 'signature:verify:worker' -and $integratedFailureReceipt.failureCode -ceq 'native-command-threw') 'Signature attempt failure publication did not preserve the failed worker phase.'
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $integratedFailureRoot -ExpectedLane Signature -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1
            Assert-True ($LASTEXITCODE -eq 0) 'Independent validator rejected the published Signature failure evidence.'
            Remove-Item -LiteralPath $integratedFailureRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $integratedFailureCandidate -Recurse -Force -ErrorAction Stop
            $regressionCandidateRoot = $sanitizerRoot + '-regression-candidate'
            $regressionSanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'RegressionSanitize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $regressionCandidateRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    executionContext = 'full-child'
                    phaseResults = @([ordered]@{ name = 'regression:test:unit'; status = 'success'; exitCode = 0; failureCode = $null })
                    runs = @(
                        [ordered]@{ runId = 'unit'; state = 'success'; projectPath = 'src/Unlimotion.Test/Unlimotion.Test.csproj'; configuration = 'Debug'; nativeExitCode = 0; failureCode = $null; discovered = 830; passed = 830; failed = 0; skipped = 0; durationMs = 1; skipReason = $null },
                        [ordered]@{ runId = 'headless-1'; state = 'success'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; configuration = 'Debug'; nativeExitCode = 0; failureCode = $null; discovered = 36; passed = 36; failed = 0; skipped = 0; durationMs = 1; skipReason = $null },
                        [ordered]@{ runId = 'headless-2'; state = 'success'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; configuration = 'Debug'; nativeExitCode = 0; failureCode = $null; discovered = 36; passed = 36; failed = 0; skipped = 0; durationMs = 1; skipReason = $null }
                    )
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($regressionSanitizerResult.Success -eq $true -and $regressionSanitizerResult.ExitCode -eq 0) 'Regression sanitizer worker did not return a closed success tuple.'
            $regressionFinalRoot = $regressionCandidateRoot + '-final'
            $regressionFinalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $regressionCandidateRoot
                    finalEvidenceRoot = $regressionFinalRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Regression'
                    phaseResults = @([ordered]@{ name = 'regression:test:unit'; status = 'success'; exitCode = 0; failureCode = $null })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($regressionFinalizerResult.Success -eq $true -and $regressionFinalizerResult.ExitCode -eq 0) 'Publication finalizer did not publish Regression candidate evidence.'
            $regressionEvidence = [IO.File]::ReadAllText((Join-Path $regressionFinalRoot 'regression\evidence.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($regressionEvidence.evidenceKind -ceq 'regression-success' -and (@($regressionEvidence.runs)).Count -eq 3 -and $regressionEvidence.runs[0].trx.path -ceq 'regression/unit.trx' -and $regressionEvidence.runs[2].html.path -ceq 'regression/headless-2.html') 'Regression sanitizer evidence shape is invalid.'
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $regressionFinalRoot -ExpectedLane Regression -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1 -ExpectedExecutionContext full-child
            Assert-True ($LASTEXITCODE -eq 0) 'Independent validator rejected published Regression candidate evidence.'
            $fullCandidateRoot = $sanitizerRoot + '-full-candidate'
            $fullEvidenceRoot = $sanitizerRoot + '-full-final'
            New-Item -ItemType Directory -Path $fullCandidateRoot -ErrorAction Stop | Out-Null
            Copy-Item -LiteralPath $failurePrimaryRoot -Destination (Join-Path $fullCandidateRoot 'signature') -Recurse -ErrorAction Stop
            Copy-Item -LiteralPath $regressionFinalRoot -Destination (Join-Path $fullCandidateRoot 'regression') -Recurse -ErrorAction Stop
            $fullRegressionReceipt = [IO.File]::ReadAllText((Join-Path $regressionFinalRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Publish-FullPrimaryEvidence -CandidateRoot $fullCandidateRoot -EvidencePath $fullEvidenceRoot -SourceSha ('a' * 40) -Attempt 1 -SignatureReceipt $failurePrimaryReceipt -RegressionReceipt $fullRegressionReceipt -SecretSeeds $syntheticSeeds
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $fullEvidenceRoot -ExpectedLane Full -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1
            Assert-True ($LASTEXITCODE -eq 0) 'Independent validator rejected the recursive Full primary evidence.'
            $fullReceipt = [IO.File]::ReadAllText((Join-Path $fullEvidenceRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($fullReceipt.receiptKind -ceq 'full-primary' -and $fullReceipt.outcome -ceq 'failure' -and $fullReceipt.failureCode -ceq 'signature-verification-failed' -and (@($fullReceipt.childAttempts)).Count -eq 2 -and $fullReceipt.childAttempts[0].relativeRoot -ceq 'signature' -and $fullReceipt.childAttempts[1].relativeRoot -ceq 'regression') 'Full primary publication did not preserve the ordered child receipts.'
            $fallbackChildRejected = $false
            try {
                Assert-FullPrimaryChildReceipts -SignatureReceipt ([ordered]@{ receiptKind = 'safe-fallback'; lane = 'Signature'; outcome = 'failure' }) -RegressionReceipt $fullRegressionReceipt
            } catch {
                $fallbackChildRejected = $true
            }
            Assert-True $fallbackChildRejected 'Full primary publication accepted a fallback child receipt.'
            $fullOrderTamperRoot = $fullEvidenceRoot + '-order-tamper'
            Copy-Item -LiteralPath $fullEvidenceRoot -Destination $fullOrderTamperRoot -Recurse -ErrorAction Stop
            $fullOrderTamperReceiptPath = Join-Path $fullOrderTamperRoot 'attempt-receipt.json'
            $fullOrderTamperReceipt = [IO.File]::ReadAllText($fullOrderTamperReceiptPath, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            $fullOrderTamperReceipt.childAttempts = @($fullOrderTamperReceipt.childAttempts[1], $fullOrderTamperReceipt.childAttempts[0])
            [IO.File]::WriteAllText($fullOrderTamperReceiptPath, (ConvertTo-Json -InputObject $fullOrderTamperReceipt -Depth 16 -Compress), [Text.UTF8Encoding]::new($false))
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $fullOrderTamperRoot -ExpectedLane Full -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1 2>$null | Out-Null
            Assert-True ($LASTEXITCODE -ne 0) 'Independent validator accepted a reordered Full child receipt list.'
            Remove-Item -LiteralPath $fullOrderTamperRoot -Recurse -Force -ErrorAction Stop
            $fullRootTamperRoot = $fullEvidenceRoot + '-root-tamper'
            Copy-Item -LiteralPath $fullEvidenceRoot -Destination $fullRootTamperRoot -Recurse -ErrorAction Stop
            $fullRootTamperReceiptPath = Join-Path $fullRootTamperRoot 'attempt-receipt.json'
            $fullRootTamperReceipt = [IO.File]::ReadAllText($fullRootTamperReceiptPath, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            $fullRootTamperReceipt.childAttempts[0].relativeRoot = 'regression'
            [IO.File]::WriteAllText($fullRootTamperReceiptPath, (ConvertTo-Json -InputObject $fullRootTamperReceipt -Depth 16 -Compress), [Text.UTF8Encoding]::new($false))
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $fullRootTamperRoot -ExpectedLane Full -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1 2>$null | Out-Null
            Assert-True ($LASTEXITCODE -ne 0) 'Independent validator accepted an invalid Full child root.'
            Remove-Item -LiteralPath $fullRootTamperRoot -Recurse -Force -ErrorAction Stop
            $fullManifestTamperRoot = $fullEvidenceRoot + '-manifest-tamper'
            Copy-Item -LiteralPath $fullEvidenceRoot -Destination $fullManifestTamperRoot -Recurse -ErrorAction Stop
            [IO.File]::AppendAllText((Join-Path $fullManifestTamperRoot 'signature\attempt-receipt.json'), ' ', [Text.UTF8Encoding]::new($false))
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $fullManifestTamperRoot -ExpectedLane Full -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1 2>$null | Out-Null
            Assert-True ($LASTEXITCODE -ne 0) 'Independent validator accepted a Full child receipt manifest mismatch.'
            Remove-Item -LiteralPath $fullManifestTamperRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $fullEvidenceRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $regressionFinalRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $regressionCandidateRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $failurePrimaryRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $failureSanitizerRoot -Recurse -Force -ErrorAction Stop
            $regressionFailureCandidateRoot = $sanitizerRoot + '-regression-failure-candidate'
            $regressionFailureSanitizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'RegressionSanitize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $regressionFailureCandidateRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    executionContext = 'full-child'
                    phaseResults = @([ordered]@{ name = 'regression:test:unit'; status = 'failure'; exitCode = 2; failureCode = 'test-evidence-failed' })
                    runs = @(
                        [ordered]@{ runId = 'unit'; state = 'failure'; projectPath = 'src/Unlimotion.Test/Unlimotion.Test.csproj'; configuration = 'Debug'; nativeExitCode = 0; failureCode = 'test-evidence-failed'; discovered = $null; passed = $null; failed = $null; skipped = $null; durationMs = $null; skipReason = $null },
                        [ordered]@{ runId = 'headless-1'; state = 'not-attempted'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; configuration = 'Debug'; nativeExitCode = $null; failureCode = $null; discovered = $null; passed = $null; failed = $null; skipped = $null; durationMs = $null; skipReason = 'prerequisite-failed' },
                        [ordered]@{ runId = 'headless-2'; state = 'not-attempted'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; configuration = 'Debug'; nativeExitCode = $null; failureCode = $null; discovered = $null; passed = $null; failed = $null; skipped = $null; durationMs = $null; skipReason = 'prerequisite-failed' }
                    )
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($regressionFailureSanitizerResult.Success -eq $true -and $regressionFailureSanitizerResult.ExitCode -eq 0) 'Regression failure sanitizer worker did not return a closed success tuple.'
            $regressionFailureFinalRoot = $regressionFailureCandidateRoot + '-final'
            $regressionFailureFinalizerResult = Invoke-ClosedWorkerProcessAdapter -WorkerKind 'PublicationFinalize' -Payload ([ordered]@{
                    candidateEvidenceRoot = $regressionFailureCandidateRoot
                    finalEvidenceRoot = $regressionFailureFinalRoot
                    sourceSha = ('a' * 40)
                    runAttempt = '1'
                    lane = 'Regression'
                    phaseResults = @([ordered]@{ name = 'regression:test:unit'; status = 'failure'; exitCode = 2; failureCode = 'test-evidence-failed' })
                }) -SecretSeeds $syntheticSeeds -TimeoutSeconds 10
            Assert-True ($regressionFailureFinalizerResult.Success -eq $true -and $regressionFailureFinalizerResult.ExitCode -eq 0) 'Publication finalizer did not publish Regression failure evidence.'
            $regressionFailureEvidence = [IO.File]::ReadAllText((Join-Path $regressionFailureFinalRoot 'regression\evidence.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            $regressionFailureReceipt = [IO.File]::ReadAllText((Join-Path $regressionFailureFinalRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($regressionFailureEvidence.evidenceKind -ceq 'regression-failure' -and $regressionFailureEvidence.runs[0].nativeExitCode -eq 0 -and $regressionFailureEvidence.runs[0].failureCode -ceq 'test-evidence-failed' -and $regressionFailureEvidence.runs[0].trx -eq $null -and $regressionFailureReceipt.outcome -ceq 'failure' -and $regressionFailureReceipt.failurePhase -ceq 'regression:test:unit' -and $regressionFailureReceipt.failureCode -ceq 'test-evidence-failed') 'Regression failure publication did not preserve the synthetic test-evidence failure tuple.'
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $regressionFailureFinalRoot -ExpectedLane Regression -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1 -ExpectedExecutionContext full-child
            Assert-True ($LASTEXITCODE -eq 0) 'Independent validator rejected published Regression failure evidence.'
            $regressionFailureFullCandidateRoot = $sanitizerRoot + '-full-regression-failure-candidate'
            $regressionFailureFullEvidenceRoot = $sanitizerRoot + '-full-regression-failure-final'
            New-Item -ItemType Directory -Path $regressionFailureFullCandidateRoot -ErrorAction Stop | Out-Null
            Copy-Item -LiteralPath $finalizerRoot -Destination (Join-Path $regressionFailureFullCandidateRoot 'signature') -Recurse -ErrorAction Stop
            Copy-Item -LiteralPath $regressionFailureFinalRoot -Destination (Join-Path $regressionFailureFullCandidateRoot 'regression') -Recurse -ErrorAction Stop
            Publish-FullPrimaryEvidence -CandidateRoot $regressionFailureFullCandidateRoot -EvidencePath $regressionFailureFullEvidenceRoot -SourceSha ('a' * 40) -Attempt 1 -SignatureReceipt $finalReceipt -RegressionReceipt $regressionFailureReceipt -SecretSeeds $syntheticSeeds
            & (Get-AbsolutePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File (Join-Path (Get-CanonicalRepositoryRoot) 'scripts\Test-NuGetEvidencePublication.ps1') -EvidenceRoot $regressionFailureFullEvidenceRoot -ExpectedLane Full -ExpectedSourceSha ('a' * 40) -ExpectedRunAttempt 1
            Assert-True ($LASTEXITCODE -eq 0) 'Independent validator rejected Full evidence with a Regression child failure.'
            $regressionFailureFullReceipt = [IO.File]::ReadAllText((Join-Path $regressionFailureFullEvidenceRoot 'attempt-receipt.json'), [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
            Assert-True ($regressionFailureFullReceipt.receiptKind -ceq 'full-primary' -and $regressionFailureFullReceipt.outcome -ceq 'failure' -and $regressionFailureFullReceipt.failureCode -ceq 'test-evidence-failed' -and $regressionFailureFullReceipt.childAttempts[0].outcome -ceq 'success' -and $regressionFailureFullReceipt.childAttempts[1].outcome -ceq 'failure') 'Full primary evidence did not preserve a Regression child failure.'
            Remove-Item -LiteralPath $regressionFailureFullEvidenceRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $regressionFailureFinalRoot -Recurse -Force -ErrorAction Stop
            Remove-Item -LiteralPath $regressionFailureCandidateRoot -Recurse -Force -ErrorAction Stop
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
    'Full' { Invoke-FullAttempt; break }
    default { throw 'RunAttempt lane must be exactly Signature, Regression or Full.' }
}
