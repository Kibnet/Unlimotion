[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Identity,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory)]
    [ValidateSet('windows-2022')]
    [string]$ExpectedRunnerImage,

    [string]$OutputEvidence,
    [ValidateRange(5, 120)]
    [int]$LaunchTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Test-WindowsDistribution.ps1 must run on Windows.'
}

$startedAtUtc = [DateTime]::UtcNow.ToString('O')
$operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem
$actualOsVersion = [string]$operatingSystem.Version
$actualOsCaption = [string]$operatingSystem.Caption
$actualImageOs = [string]$env:ImageOS
$actualImageVersion = [string]$env:ImageVersion
if ($ExpectedRunnerImage -ceq 'windows-2022') {
    if (-not $actualOsCaption.Contains('Windows Server 2022', [StringComparison]::OrdinalIgnoreCase) -or
        -not $actualOsVersion.StartsWith('10.0.20348', [StringComparison]::Ordinal) -or
        $actualImageOs -cne 'win22' -or
        [string]::IsNullOrWhiteSpace($actualImageVersion)) {
        throw "Expected GitHub windows-2022 (Server 2022 build 20348, ImageOS=win22); observed caption='$actualOsCaption', version='$actualOsVersion', ImageOS='$actualImageOs', ImageVersion='$actualImageVersion'."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$identityPath = (Resolve-Path -LiteralPath $Identity -ErrorAction Stop).Path
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory -ErrorAction Stop).Path
if (-not (Get-Item -LiteralPath $artifactRoot).PSIsContainer) {
    throw "ArtifactDirectory must be a directory: $ArtifactDirectory"
}
if ([string]::IsNullOrWhiteSpace($OutputEvidence)) {
    $OutputEvidence = Join-Path (Split-Path -Parent $artifactRoot) 'evidence/windows-native.json'
}

function Get-RequiredString {
    param([Parameter(Mandatory)] [object]$Object, [Parameter(Mandatory)] [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($property.Value)) {
        throw "Release identity is missing '$Name'."
    }
    return [string]$property.Value
}

function Get-RequiredArtifact {
    param([Parameter(Mandatory)] [string]$Name)
    if ([IO.Path]::GetFileName($Name) -cne $Name) { throw "Artifact name must not contain a path: $Name" }
    $path = Join-Path $artifactRoot $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected artifact is missing: $Name" }
    if ((Get-Item -LiteralPath $path).Length -le 0) { throw "Expected artifact is empty: $Name" }
    return $path
}

function Get-PeMachine {
    param([Parameter(Mandatory)] [string]$Path)
    Add-Type -AssemblyName System.Reflection.Metadata
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try { return [string]$reader.PEHeaders.CoffHeader.Machine }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-PeArchitectureAndVersion {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Version,
        [string[]]$AllowedMachine = @('Amd64'),
        [switch]$RequireProductVersion
    )
    $machine = Get-PeMachine -Path $Path
    if ($AllowedMachine -cnotcontains $machine) { throw "PE '$Path' has machine '$machine', expected one of: $($AllowedMachine -join ', ')." }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($RequireProductVersion) {
        $productVersion = [string]$versionInfo.ProductVersion
        $fileVersion = [string]$versionInfo.FileVersion
        if (-not $productVersion.StartsWith($Version, [StringComparison]::Ordinal) -or
            -not $fileVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
            throw "PE '$Path' does not carry expected product/file version '$Version' (product '$productVersion', file '$fileVersion')."
        }
        if ($productVersion.Contains("v$Version", [StringComparison]::Ordinal) -or
            $fileVersion.Contains("v$Version", [StringComparison]::Ordinal)) {
            throw "PE '$Path' contains a raw v-prefixed version."
        }
    }
    return [ordered]@{
        machine = $machine
        productVersion = [string]$versionInfo.ProductVersion
        fileVersion = [string]$versionInfo.FileVersion
    }
}

function Expand-PortableArchive {
    param(
        [Parameter(Mandatory)] [string]$Archive,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string]$Label
    )
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination
    $pdbs = @(Get-ChildItem -LiteralPath $Destination -Recurse -File | Where-Object Extension -CEQ '.pdb')
    if ($pdbs.Count -gt 0) { throw "$Label contains PDB files: $($pdbs.Name -join ', ')." }
    $executables = @(Get-ChildItem -LiteralPath $Destination -Recurse -File -Filter 'Unlimotion.Desktop.exe')
    if ($executables.Count -ne 1) { throw "$Label must contain exactly one Unlimotion.Desktop.exe; found $($executables.Count)." }
    return $executables[0].FullName
}

function Invoke-WindowSmoke {
    param(
        [Parameter(Mandatory)] [string]$Executable,
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [string]$ExpectedTitle,
        [Parameter(Mandatory)] [string]$WorkDirectory
    )

    $runDirectory = Join-Path $WorkDirectory ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
    $config = Join-Path $runDirectory 'settings.json'
    $stdout = Join-Path $runDirectory 'stdout.log'
    $stderr = Join-Path $runDirectory 'stderr.log'
    $process = Start-Process -FilePath $Executable -ArgumentList "--config=$config" -WorkingDirectory $runDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($LaunchTimeoutSeconds)
        $windowTitle = ''
        do {
            Start-Sleep -Milliseconds 500
            $process.Refresh()
            if ($process.HasExited) {
                $errorText = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
                throw "$Label exited before a window appeared (exit $($process.ExitCode)). $errorText"
            }
            $windowTitle = [string]$process.MainWindowTitle
            if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $windowTitle -ceq $ExpectedTitle) { break }
        } while ([DateTime]::UtcNow -lt $deadline)

        if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $windowTitle -cne $ExpectedTitle) {
            throw "$Label did not show exact window title '$ExpectedTitle' within $LaunchTimeoutSeconds seconds; observed '$windowTitle'."
        }
        return [ordered]@{
            processId = $process.Id
            windowTitle = $windowTitle
            configPath = $config
            stdout = $stdout
            stderr = $stderr
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(10000) | Out-Null
        }
        $process.Dispose()
    }
}

function Write-Evidence {
    param([Parameter(Mandatory)] [object]$Value)
    $parent = Split-Path -Parent $OutputEvidence
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputEvidence -Encoding utf8NoBOM
}

$identityObject = Get-Content -LiteralPath $identityPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
$rawTag = Get-RequiredString -Object $identityObject -Name 'rawTag'
$version = Get-RequiredString -Object $identityObject -Name 'normalizedVersion'
$sourceSha = Get-RequiredString -Object $identityObject -Name 'sourceSha'
$workflowSha = Get-RequiredString -Object $identityObject -Name 'workflowSha'
$tagBinding = Get-RequiredString -Object $identityObject -Name 'tagBinding'
$manifestSha256 = Get-RequiredString -Object $identityObject -Name 'manifestSha256'
$supportMatrixSha256 = Get-RequiredString -Object $identityObject -Name 'supportMatrixSha256'
if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $version -ceq '0.0.0' -or
    $rawTag -cnotmatch '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $rawTag.TrimStart('v') -cne $version -or
    $sourceSha -cnotmatch '^[0-9a-f]{40}$' -or $workflowSha -cnotmatch '^[0-9a-f]{40}$' -or
    $tagBinding -notin @('notApplicable', 'required') -or
    $manifestSha256 -cnotmatch '^[0-9a-f]{64}$' -or $supportMatrixSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Release identity contains invalid version, source/workflow SHA, tag binding or contract SHA fields.'
}
if ($null -eq $identityObject.filenamePlan.windows) { throw 'Release identity does not contain filenamePlan.windows.' }

$portableName = Get-RequiredString -Object $identityObject.filenamePlan.windows -Name 'portableX64'
$legacyPortableName = Get-RequiredString -Object $identityObject.filenamePlan.windows -Name 'legacyPortableX64'
$setupName = Get-RequiredString -Object $identityObject.filenamePlan.windows -Name 'setupX64'
$portablePath = Get-RequiredArtifact -Name $portableName
$legacyPortablePath = Get-RequiredArtifact -Name $legacyPortableName
$setupPath = Get-RequiredArtifact -Name $setupName

$workRoot = Join-Path ([IO.Path]::GetTempPath()) "unlimotion-distribution-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $workRoot | Out-Null

try {
    $portableExecutable = Expand-PortableArchive -Archive $portablePath -Destination (Join-Path $workRoot 'portable') -Label 'Canonical portable archive'
    $legacyExecutable = Expand-PortableArchive -Archive $legacyPortablePath -Destination (Join-Path $workRoot 'legacy-portable') -Label 'Legacy portable archive'
    $portablePe = Assert-PeArchitectureAndVersion -Path $portableExecutable -Version $version -RequireProductVersion
    $legacyPe = Assert-PeArchitectureAndVersion -Path $legacyExecutable -Version $version -RequireProductVersion
    $portablePayloadHash = (Get-FileHash -LiteralPath $portableExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    $legacyPayloadHash = (Get-FileHash -LiteralPath $legacyExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($portablePayloadHash -cne $legacyPayloadHash) {
        throw 'Canonical and legacy portable archives do not contain the same application executable bytes.'
    }
    # Velopack's Setup.exe is an architecture-neutral/x86 bootstrapper; the
    # installed application payload below must still be an exact AMD64 PE.
    $setupPe = Assert-PeArchitectureAndVersion -Path $setupPath -Version $version -AllowedMachine @('I386', 'Amd64') -RequireProductVersion

    $portableSmoke = Invoke-WindowSmoke -Executable $portableExecutable -Label 'Portable application' -ExpectedTitle "Unlimotion $version" -WorkDirectory $workRoot

    $installRoot = Join-Path $workRoot 'installed'
    $installLog = Join-Path $workRoot 'setup.log'
    $setupProcess = Start-Process -FilePath $setupPath -ArgumentList @('--silent', '--installto', $installRoot, '--log', $installLog) -Wait -PassThru
    if ($setupProcess.ExitCode -ne 0) { throw "Setup.exe failed with exit code $($setupProcess.ExitCode)." }
    $installedExecutables = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Filter 'Unlimotion.Desktop.exe')
    if ($installedExecutables.Count -ne 1) { throw "Installed layout must contain exactly one Unlimotion.Desktop.exe; found $($installedExecutables.Count)." }
    $installedPdbs = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object Extension -CEQ '.pdb')
    if ($installedPdbs.Count -gt 0) { throw "Installed layout contains PDB files: $($installedPdbs.Name -join ', ')." }
    $installedExecutable = $installedExecutables[0].FullName
    $installedPe = Assert-PeArchitectureAndVersion -Path $installedExecutable -Version $version -RequireProductVersion
    $installedPayloadHash = (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($installedPayloadHash -cne $portablePayloadHash) {
        throw 'Installed application executable bytes differ from the canonical portable payload.'
    }
    $setupSmoke = Invoke-WindowSmoke -Executable $installedExecutable -Label 'Installed application' -ExpectedTitle "Unlimotion $version" -WorkDirectory $workRoot

    $updater = Join-Path $installRoot 'Update.exe'
    if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) { throw "Installed layout does not contain Update.exe: $updater" }
    $uninstallProcess = Start-Process -FilePath $updater -ArgumentList @('uninstall', '--silent', '--rootDir', $installRoot) -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) { throw "Velopack uninstall failed with exit code $($uninstallProcess.ExitCode)." }
    Start-Sleep -Seconds 2
    if (Test-Path -LiteralPath (Join-Path $installRoot 'current/Unlimotion.Desktop.exe') -PathType Leaf) {
        throw 'Velopack uninstall left the installed application payload behind.'
    }

    $setupSignature = Get-AuthenticodeSignature -LiteralPath $setupPath
    $portableSignature = Get-AuthenticodeSignature -LiteralPath $portableExecutable
    $allowedSignatureStates = @('NotSigned', 'Valid')
    if ([string]$setupSignature.Status -cnotin $allowedSignatureStates -or
        [string]$portableSignature.Status -cnotin $allowedSignatureStates) {
        throw "Invalid Authenticode state: setup=$($setupSignature.Status), portable=$($portableSignature.Status)."
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        kind = 'windows-native-validation-evidence'
        status = 'pass'
        platform = 'windows'
        architecture = 'x64'
        runner = [ordered]@{
            expectedImage = $ExpectedRunnerImage
            osCaption = $actualOsCaption
            osVersion = $actualOsVersion
            imageOs = $actualImageOs
            imageVersion = $actualImageVersion
            processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        }
        startedAtUtc = $startedAtUtc
        finishedAtUtc = [DateTime]::UtcNow.ToString('O')
        rawTag = $rawTag
        normalizedVersion = $version
        sourceSha = $sourceSha
        workflowSha = $workflowSha
        tagBinding = $tagBinding
        manifestSha256 = $manifestSha256
        supportMatrixSha256 = $supportMatrixSha256
        portable = [ordered]@{
            fileName = $portableName
            sha256 = (Get-FileHash -LiteralPath $portablePath -Algorithm SHA256).Hash.ToLowerInvariant()
            executableSha256 = $portablePayloadHash
            pe = $portablePe
            authenticode = [string]$portableSignature.Status
            smoke = $portableSmoke
        }
        legacyPortable = [ordered]@{
            fileName = $legacyPortableName
            sha256 = (Get-FileHash -LiteralPath $legacyPortablePath -Algorithm SHA256).Hash.ToLowerInvariant()
            executableSha256 = $legacyPayloadHash
            pe = $legacyPe
            supportClaim = 'excluded'
        }
        setup = [ordered]@{
            fileName = $setupName
            sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
            pe = $setupPe
            authenticode = [string]$setupSignature.Status
            installedPe = $installedPe
            installedExecutableSha256 = $installedPayloadHash
            installedLayout = [ordered]@{
                root = $installRoot
                executableRelativePath = [IO.Path]::GetRelativePath($installRoot, $installedExecutable)
                pdbCount = 0
            }
            smoke = $setupSmoke
            uninstallVerified = $true
        }
        retry = [ordered]@{ classification = 'deterministic'; attempt = 1; maxAttempts = 1; cleanup = 'unique-temporary-directory' }
        validators = [ordered]@{ powershell = $PSVersionTable.PSVersion.ToString(); authenticode = 'Get-AuthenticodeSignature' }
        productionReady = $false
    }
    Write-Evidence -Value $evidence
    Write-Output "Windows native validation passed; evidence: $OutputEvidence"
}
catch {
    $failure = [ordered]@{
        schemaVersion = 1
        kind = 'windows-native-validation-evidence'
        status = 'fail'
        platform = 'windows'
        architecture = 'x64'
        rawTag = $rawTag
        normalizedVersion = $version
        sourceSha = $sourceSha
        workflowSha = $workflowSha
        tagBinding = $tagBinding
        manifestSha256 = $manifestSha256
        supportMatrixSha256 = $supportMatrixSha256
        error = $_.Exception.Message
        productionReady = $false
    }
    Write-Evidence -Value $failure
    throw
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
